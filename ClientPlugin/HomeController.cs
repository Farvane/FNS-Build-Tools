using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Avalonia.Threading;
using HarmonyLib;
using Keen.Game2.Simulation.GameSystems.AdminTools;
using Keen.Game2.Simulation.GameSystems.GridId;
using Keen.Game2.Simulation.Replication;
using Keen.Game2.Simulation.WorldObjects.CubeGrids;
using Keen.VRage.Core;
using Keen.VRage.Core.Game.Components;
using Keen.VRage.Core.Game.Data;
using Keen.VRage.DCS.Components;
using Keen.VRage.Library.Diagnostics;
using Keen.VRage.Library.Mathematics;
using Keen.VRage.Library.Utils;
using Keen.VRage.Physics.Data;

namespace Farvane.BuildTools;

/// <summary>
/// Home Grid: moves the looked-at grid so its pivot (CoG or CoM) sits at world
/// 0,0,0 with its axes on the world axes. Grids joined by rotors, pistons, hinges
/// and connectors (the Terminal connection group) and the local character keep
/// their pose relative to it. Stages:
///   1. gather the group and check that the destination is clear of other grids;
///   2. if the grid or player is moving, stop the group (IGameServer.StopEntityMovement,
///      server-side and admin-checked, in-process servers only) and wait until still;
///   3. teleport each entity (EntityAdmin.TeleportEntity, server-side, admin-checked);
///   4. stop the group again so it arrives at rest.
/// The previous pose is kept for Return.
/// No C# async here: Game2.Client defines its own AsyncVoidMethodBuilder and
/// NetworkStory.Task is the game's task type, so progress is polled on the UI thread.
/// Call from the UI thread (screen buttons and hotkeys both run there).
/// </summary>
internal sealed class HomeController
{
    private const double MaxLinearSpeed = 0.05;   // m/s
    private const double MaxAngularSpeed = 0.01;  // rad/s
    private const double ConventionTolerance = 0.001; // m
    private const double ClearanceMargin = 0.5; // m
    private static readonly TimeSpan ServerTimeout = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan StopTimeout = TimeSpan.FromSeconds(3);

    private static readonly AccessTools.FieldRef<GridIdSessionComponent, Dictionary<Entity, int>> GridIds =
        AccessTools.FieldRefAccess<GridIdSessionComponent, Dictionary<Entity, int>>("_gridIds");

    private readonly SymmetryController _context;
    private List<Saved>? _undo;
    private bool _busy;

    public HomeController(SymmetryController context) => _context = context;

    /// <summary>Refuse to land on other grids. Settable from settings.json (HomeClearanceCheck).</summary>
    public bool ClearanceCheck { get; set; } = true;

    public bool CanReturn => _undo is not null;

    /// <summary>Validates and starts the move; throws InvalidOperationException if it cannot start. <paramref name="report"/> gets the outcome.</summary>
    public void Home(bool useCenterOfMass, Action<string> report)
    {
        if (_busy) throw new InvalidOperationException("A move is already in progress");
        var group = Gather();
        // Plan once up front: validates conventions and clearance before anything is touched.
        var plan = Plan(group, useCenterOfMass);
        if (ClearanceCheck) CheckClearance(group, plan);

        var moving = IsMoving(group.Main) || (group.Character is not null && IsMoving(group.Character));
        var server = group.Grid.TryGetSession<IGameServer>();
        if (!moving)
        {
            Execute(group, useCenterOfMass, server, report, stopped: false);
            return;
        }
        if (server is null)
            throw new InvalidOperationException("The grid or you are moving, and stopping needs a single-player or hosted game. Stop first, then try again");

        // Stop everything that will move, wait for the replicas to read still, then plan from the stopped poses.
        _busy = true;
        foreach (var entity in group.All) server.StopEntityMovement(entity);
        var started = DateTime.UtcNow;
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
        timer.Tick += (_, _) =>
        {
            var still = !IsMoving(group.Main) && (group.Character is null || !IsMoving(group.Character));
            if (!still && DateTime.UtcNow - started < StopTimeout) return;
            timer.Stop();
            _busy = false;
            if (!still)
            {
                report("Could not stop the grid within " + StopTimeout.TotalSeconds +
                    " s (thrusters or gravity still acting?). Nothing was moved");
                return;
            }
            try
            {
                Execute(group, useCenterOfMass, server, report, stopped: true);
            }
            catch (InvalidOperationException error)
            {
                report(error.Message);
            }
            catch (Exception error)
            {
                Log.Default.WriteLine("[FNS Build Tools] Home failed after stopping: " + error);
                report("Home failed (" + error.GetType().Name + "). Check the SE2 log");
            }
        };
        timer.Start();
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

    private Group Gather()
    {
        var target = _context.RequireTarget(out var grid);
        var main = target.RootEntity;
        // Main grid first, then the rest of its mechanical/connector group, then the player.
        var grids = new List<Entity> { main };
        foreach (var other in grid.GetAllGridsInShip())
            if (other is not null && !grids.Contains(other)) grids.Add(other);
        var character = _context.LocalCharacter();
        if (character is not null && grids.Contains(character)) character = null;
        return new Group(grid, main, grids, character);
    }

    private static IReadOnlyList<Move> Plan(Group group, bool useCenterOfMass)
    {
        var mainOld = EntityTransformFunctions.GetWorldTransform(group.Main.Data);
        var pivot = Pivot(group, useCenterOfMass);
        var mainNew = new WorldTransform(-new Vector3D(pivot), Quaternion.Identity);
        var moves = new List<Move>();
        foreach (var entity in group.All)
        {
            var old = EntityTransformFunctions.GetWorldTransform(entity.Data);
            var relative = WorldTransform.GetRelativeTransform(old, mainOld);
            // WorldTransform * WorldTransform is parent * child (IL: Ori = a.Ori·b.Ori,
            // Pos = a.Pos + rotate(b.Pos, a.Ori)). GetRelativeTransform returns a WorldTransform,
            // so recompose as anchor * relative. (RelativeTransform * WorldTransform, which fast
            // travel uses, takes the parent on the right; that overload does not apply here.)
            var rebuilt = mainOld * relative;
            if (Distance(rebuilt, old) > ConventionTolerance || !SameOrientation(rebuilt.Orientation, old.Orientation))
            {
                Log.Default.WriteLine("[FNS Build Tools] Transform check failed for " + entity.DebugName +
                    ": old=" + old + " rebuilt=" + rebuilt + " anchor=" + mainOld);
                throw new InvalidOperationException("Transform check failed; nothing was moved. Please report this with the SE2 log");
            }
            var moved = mainNew * relative;
            moves.Add(new Move(entity, old, new WorldTransform(moved.Position, Quaternion.Normalize(moved.Orientation))));
        }
        return moves;
    }

    private static Vector3 Pivot(Group group, bool useCenterOfMass)
    {
        if (useCenterOfMass)
        {
            if (!group.Main.Data.TryGet<RigidBodyMassProperties>(out var mass))
                throw new InvalidOperationException("Center of mass is unavailable for this grid (static grids have none). Use CoG");
            return mass.CenterOfMass;
        }
        // Center of the block bounding box: (Min + Max) / 2 cells × 0.25 m = PlaneDistance × 0.125.
        var spans = SymmetryController.Bounds(group.Grid);
        return new Vector3((float)(spans.X.PlaneDistance * 0.125), (float)(spans.Y.PlaneDistance * 0.125),
            (float)(spans.Z.PlaneDistance * 0.125));
    }

    private void Execute(Group group, bool useCenterOfMass, IGameServer? server, Action<string> report, bool stopped)
    {
        var moves = Plan(group, useCenterOfMass);
        // Record before sending: if the server applies only some moves, Return can still restore all.
        _undo = moves.Select(m => new Saved(new WeakReference<Entity>(m.Entity), m.Old)).ToList();
        var gridCount = group.Grids.Count;
        Send(moves.Select(m => (m.Entity, m.New)), report, () =>
        {
            // Teleport keeps velocity; zero it so the group arrives at rest.
            if (server is not null)
                foreach (var entity in group.All) server.StopEntityMovement(entity);
            return string.Format(CultureInfo.InvariantCulture,
                "{0}Grid homed to 0,0,0 by {1} ({2} grid{3}{4}). Ctrl+Alt+Shift+O returns it",
                stopped ? "Stopped, then homed. " : "", useCenterOfMass ? "CoM" : "CoG", gridCount,
                gridCount == 1 ? "" : "s", group.Character is null ? "" : " + you");
        });
    }

    /// <summary>Refuses if another grid's bounds overlap where the group's grids will land.</summary>
    private static void CheckClearance(Group group, IReadOnlyList<Move> plan)
    {
        var registry = group.Grid.TryGetSession<GridIdSessionComponent>();
        if (registry is null)
        {
            Log.Default.WriteLine("[FNS Build Tools] Clearance check skipped: grid registry unavailable");
            return;
        }
        var destinations = new List<BoundingBoxD>();
        foreach (var move in plan)
            if (CubeGrid(move.Entity) is { } grid) destinations.Add(WorldBox(grid, move.New));
        var blocking = new List<string>();
        foreach (var other in GridIds(registry).Keys.ToList())
        {
            if (other is null || group.Grids.Contains(other) || CubeGrid(other) is not { GridClosing: false } grid) continue;
            if (grid.CubeBlockStorage.CubeBlockCount == 0) continue;
            var box = WorldBox(grid, EntityTransformFunctions.GetWorldTransform(other.Data));
            if (destinations.Any(d => Overlaps(d, box, ClearanceMargin))) blocking.Add(Name(grid));
        }
        if (blocking.Count > 0)
            throw new InvalidOperationException("The origin is not clear: " + string.Join(", ", blocking.Take(3)) +
                (blocking.Count > 3 ? " and " + (blocking.Count - 3) + " more" : "") +
                " would overlap. Move it away first, or set HomeClearanceCheck to false in settings.json");
    }

    private static CubeGridComponent? CubeGrid(Entity entity)
    {
        try
        {
            return entity.TryGet<CubeGridComponent>(default(StringId));
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static BoundingBoxD WorldBox(CubeGridComponent grid, WorldTransform transform)
    {
        var spans = SymmetryController.Bounds(grid);
        var cells = new BoundingBoxI(new Vector3I(spans.X.Min, spans.Y.Min, spans.Z.Min),
            new Vector3I(spans.X.Max, spans.Y.Max, spans.Z.Max));
        return CubeGridCoords.GridToWorld(cells, transform);
    }

    private static bool Overlaps(BoundingBoxD a, BoundingBoxD b, double margin) =>
        a.Min.X - margin < b.Max.X && b.Min.X - margin < a.Max.X &&
        a.Min.Y - margin < b.Max.Y && b.Min.Y - margin < a.Max.Y &&
        a.Min.Z - margin < b.Max.Z && b.Min.Z - margin < a.Max.Z;

    private static string Name(CubeGridComponent grid)
    {
        try
        {
            var text = grid.DisplayName.ToString();
            return string.IsNullOrWhiteSpace(text) ? "a grid" : "'" + text + "'";
        }
        catch (Exception)
        {
            return "a grid";
        }
    }

    private static bool IsMoving(Entity entity) =>
        entity.Data.TryGet<RigidBodyData>(out var motion) &&
        (motion.LinearVelocity.Length() > MaxLinearSpeed || motion.AngularVelocity.Length() > MaxAngularSpeed);

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

    // q and −q are the same rotation, so compare |q1·q2| against 1.
    private static bool SameOrientation(Quaternion a, Quaternion b) =>
        Math.Abs((double)a.X * b.X + (double)a.Y * b.Y + (double)a.Z * b.Z + (double)a.W * b.W) > 0.9999;

    private static double Length(Vector3D v) => Math.Sqrt(v.X * v.X + v.Y * v.Y + v.Z * v.Z);

    private sealed record Saved(WeakReference<Entity> Entity, WorldTransform Transform);

    private sealed record Move(Entity Entity, WorldTransform Old, WorldTransform New);

    private sealed record Group(CubeGridComponent Grid, Entity Main, List<Entity> Grids, Entity? Character)
    {
        public IEnumerable<Entity> All => Character is null ? Grids : Grids.Append(Character);
    }
}
