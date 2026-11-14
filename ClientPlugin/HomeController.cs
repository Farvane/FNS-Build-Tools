using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Avalonia.Threading;
using Keen.Game2.Simulation.GameSystems.AdminTools;
using Keen.Game2.Simulation.WorldObjects.CubeGrids;
using Keen.VRage.Core;
using Keen.VRage.Core.Game.Data;
using Keen.VRage.DCS.Components;
using Keen.VRage.Library.Diagnostics;
using Keen.VRage.Library.Mathematics;
using Keen.VRage.Physics.Data;

namespace Farvane.BuildTools;

/// <summary>
/// Home Grid: moves the looked-at grid so its pivot (CoG or CoM) sits at world
/// 0,0,0 with its axes on the world axes. Grids joined by rotors, pistons, hinges
/// and connectors (the Terminal connection group) and the local character keep
/// their pose relative to it. Moves go through the admin-validated, server-run
/// <see cref="EntityAdmin.TeleportEntity"/>. The previous pose is kept for Return.
/// No C# async here: Game2.Client defines its own AsyncVoidMethodBuilder and
/// NetworkStory.Task is the game's task type, so results are polled on the UI thread.
/// Call from the UI thread (screen buttons and hotkeys both run there).
/// </summary>
internal sealed class HomeController
{
    private const double MaxLinearSpeed = 0.05;   // m/s
    private const double MaxAngularSpeed = 0.01;  // rad/s
    private const double ConventionTolerance = 0.001; // m
    private static readonly TimeSpan ServerTimeout = TimeSpan.FromSeconds(15);

    private readonly SymmetryController _context;
    private List<Saved>? _undo;
    private bool _busy;

    public HomeController(SymmetryController context) => _context = context;

    public bool CanReturn => _undo is not null;

    /// <summary>Validates and sends the move; throws InvalidOperationException if it cannot start. <paramref name="report"/> gets the outcome.</summary>
    public void Home(bool useCenterOfMass, Action<string> report)
    {
        if (_busy) throw new InvalidOperationException("A move is already in progress");
        var target = _context.RequireTarget(out var grid);
        var main = target.RootEntity;
        var mainOld = EntityTransformFunctions.GetWorldTransform(main.Data);

        if (main.Data.TryGet<RigidBodyData>(out var motion) &&
            (motion.LinearVelocity.Length() > MaxLinearSpeed || motion.AngularVelocity.Length() > MaxAngularSpeed))
            throw new InvalidOperationException("The grid is moving. Stop it first (dampeners on), then try again");

        Vector3 pivot;
        if (useCenterOfMass)
        {
            if (!main.Data.TryGet<RigidBodyMassProperties>(out var mass))
                throw new InvalidOperationException("Center of mass is unavailable for this grid (static grids have none). Use CoG");
            pivot = mass.CenterOfMass;
        }
        else
        {
            // Center of the block bounding box: (Min + Max) / 2 cells × 0.25 m = PlaneDistance × 0.125.
            var spans = SymmetryController.Bounds(grid);
            pivot = new Vector3((float)(spans.X.PlaneDistance * 0.125), (float)(spans.Y.PlaneDistance * 0.125),
                (float)(spans.Z.PlaneDistance * 0.125));
        }
        var mainNew = new WorldTransform(-new Vector3D(pivot), Quaternion.Identity);

        // Main grid first, then the rest of its mechanical/connector group, then the player.
        var entities = new List<Entity> { main };
        foreach (var other in grid.GetAllGridsInShip())
            if (other is not null && !entities.Contains(other)) entities.Add(other);
        var gridCount = entities.Count;
        var character = _context.LocalCharacter();
        if (character is not null && !entities.Contains(character)) entities.Add(character);

        var moves = new List<(Entity Entity, WorldTransform Old, WorldTransform New)>(entities.Count);
        foreach (var entity in entities)
        {
            var old = EntityTransformFunctions.GetWorldTransform(entity.Data);
            var relative = WorldTransform.GetRelativeTransform(old, mainOld);
            // Same composition vanilla fast travel uses: relative-to-anchor * new anchor transform.
            if (Distance(relative * mainOld, old) > ConventionTolerance)
                throw new InvalidOperationException("Transform check failed; nothing was moved. Please report this with the SE2 log");
            var moved = relative * mainNew;
            moves.Add((entity, old, new WorldTransform(moved.Position, Quaternion.Normalize(moved.Orientation))));
        }

        // Record before sending: if the server applies only some moves, Return can still restore all.
        _undo = moves.Select(m => new Saved(new WeakReference<Entity>(m.Entity), m.Old)).ToList();
        // How far the pivot travels: from its current world position to the origin.
        var distance = Length(WorldTransform.Transform(new Vector3D(pivot), mainOld));
        Send(moves.Select(m => (m.Entity, m.New)), report, () =>
        {
            return string.Format(CultureInfo.InvariantCulture,
                "Grid homed to 0,0,0 by {0} ({1} grid{2}{3}), moved {4:0.0} m. Ctrl+Alt+Shift+O returns it",
                useCenterOfMass ? "CoM" : "CoG", gridCount, gridCount == 1 ? "" : "s",
                character is null ? "" : " + you", distance);
        });
    }

    public void Return(Action<string> report)
    {
        if (_busy) throw new InvalidOperationException("A move is already in progress");
        if (_undo is null) throw new InvalidOperationException("Nothing to return: Home a grid first");
        var moves = new List<(Entity, WorldTransform)>();
        foreach (var saved in _undo)
            if (saved.Entity.TryGetTarget(out var entity)) moves.Add((entity, saved.Transform));
        if (moves.Count == 0) throw new InvalidOperationException("The homed grid no longer exists");
        Send(moves, report, () =>
        {
            _undo = null;
            return "Returned " + moves.Count + " entit" + (moves.Count == 1 ? "y" : "ies") + " to where they were";
        });
    }

    private void Send(IEnumerable<(Entity Entity, WorldTransform Transform)> moves, Action<string> report, Func<string> onSuccess)
    {
        // Issue every request in the same frame, in order, so the group stays together;
        // then poll the server's verdicts (for example a missing admin permission).
        var stories = moves.Select(m => EntityAdmin.TeleportEntity(m.Entity, m.Transform)).ToList();
        _busy = true;
        var started = DateTime.UtcNow;
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
        timer.Tick += (_, _) =>
        {
            string message;
            try
            {
                var timedOut = DateTime.UtcNow - started > ServerTimeout;
                if (!timedOut && stories.Any(s => !s.Task.IsCompleted)) return;
                if (timedOut)
                {
                    message = "No reply from the server after " + ServerTimeout.TotalSeconds +
                        " s. Check where the grid is; Return may not apply";
                }
                else
                {
                    var failure = stories.Select(s => s.Task.GetAwaiter().GetExceptionResult()).FirstOrDefault(e => e is not null);
                    if (failure is null) message = onSuccess();
                    else
                    {
                        Log.Default.WriteLine("[FNS Build Tools] Move rejected: " + failure.SourceException);
                        message = "The server rejected the move (" + failure.SourceException.GetType().Name +
                            "). Home Grid needs admin rights; it works in single-player";
                    }
                }
            }
            catch (Exception error)
            {
                Log.Default.WriteLine("[FNS Build Tools] Move status unavailable: " + error);
                message = "Move status unavailable (" + error.GetType().Name + "). Check the SE2 log";
            }
            timer.Stop();
            _busy = false;
            report(message);
        };
        timer.Start();
    }

    private static double Distance(WorldTransform a, WorldTransform b) => Length(a.Position - b.Position);

    private static double Length(Vector3D v) => Math.Sqrt(v.X * v.X + v.Y * v.Y + v.Z * v.Z);

    private sealed record Saved(WeakReference<Entity> Entity, WorldTransform Transform);
}
