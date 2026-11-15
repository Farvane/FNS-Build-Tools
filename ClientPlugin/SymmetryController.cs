using System;
using System.Globalization;
using HarmonyLib;
using Keen.Game2.Client.GameSystems.BlockPlacement.BlockPlacer;
using Keen.Game2.Client.GameSystems.PlayerControl;
using Keen.Game2.Simulation.GameSystems.BlockPlacement;
using Keen.Game2.Simulation.WorldObjects.CubeGrids;
using Keen.VRage.Core;
using Keen.VRage.Core.Game.Data;
using Keen.VRage.DCS.Components;
using Keen.VRage.Library.Diagnostics;
using Keen.VRage.Library.Mathematics;

namespace Farvane.BuildTools;

/// <summary>
/// Finds the grid the local block placer last looked at and sets its symmetry
/// planes through <see cref="GridPlacementTarget.AddSymmetryPlane"/> — the same
/// replicated call vanilla's symmetry setup uses. Nothing is written to disk.
/// </summary>
internal sealed class SymmetryController : IDisposable
{
    private const string Label = "[FNS Build Tools] ";
    internal const string OwnerId = "farvane.fns-build-tools";
    internal const string SupportedBuild = "4.0.0.3169";

    private static readonly AccessTools.FieldRef<BlockPlacerEntityComponent, WeakPlacementTarget> LastLookedAtGrid =
        AccessTools.FieldRefAccess<BlockPlacerEntityComponent, WeakPlacementTarget>("_lastLookedAtGrid");
    private static readonly AccessTools.FieldRef<BlockPlacerEntityComponent, ClientPlayersSessionComponent> Players =
        AccessTools.FieldRefAccess<BlockPlacerEntityComponent, ClientPlayersSessionComponent>("_players");
    private static readonly AccessTools.FieldRef<GridPlacementTarget, CubeGridComponent> TargetGrid =
        AccessTools.FieldRefAccess<GridPlacementTarget, CubeGridComponent>("_grid");

    private static readonly SymmetryPlaneAxis[] Axes = { SymmetryPlaneAxis.X, SymmetryPlaneAxis.Y, SymmetryPlaneAxis.Z };

    private static SymmetryController? _instance;
    private readonly Harmony? _harmony;
    private readonly string? _unsupported;
    private WeakReference<BlockPlacerEntityComponent>? _placer;

    public SymmetryController()
    {
        try
        {
            var version = typeof(GridPlacementTarget).Assembly.GetName().Version?.ToString();
            if (version != SupportedBuild)
                throw new InvalidOperationException("Supported SE2 build: " + SupportedBuild + "; found " + version);
            if (_instance is not null)
                throw new InvalidOperationException("Another Build Tools instance is already running");
            var init = AccessTools.Method(typeof(BlockPlacerEntityComponent), "Init", Type.EmptyTypes)
                ?? throw new InvalidOperationException("Block placer hook not found");
            var destroy = AccessTools.Method(typeof(BlockPlacerEntityComponent), "Destroy", Type.EmptyTypes)
                ?? throw new InvalidOperationException("Block placer hook not found");
            var active = AccessTools.Method(typeof(BlockPlacerEntityComponent), "BlockPlacerActive", Type.EmptyTypes)
                ?? throw new InvalidOperationException("Block placer hook not found");
            _harmony = new Harmony(OwnerId);
            _harmony.Patch(init, postfix: new HarmonyMethod(typeof(SymmetryController), nameof(OnPlacerInit)));
            _harmony.Patch(destroy, postfix: new HarmonyMethod(typeof(SymmetryController), nameof(OnPlacerDestroy)));
            _harmony.Patch(active, postfix: new HarmonyMethod(typeof(SymmetryController), nameof(OnPlacerActive)));
            _instance = this;
        }
        catch (Exception error)
        {
            _unsupported = error is InvalidOperationException ? error.Message : "Unavailable (" + error.GetType().Name + ")";
            Log.Default.WriteLine(Label + _unsupported);
            _harmony?.UnpatchAll(OwnerId);
        }
    }

    // The client block placer component exists only for the locally controlled character.
    private static void OnPlacerInit(BlockPlacerEntityComponent __instance)
    {
        if (_instance is null) return;
        _instance._placer = new WeakReference<BlockPlacerEntityComponent>(__instance);
        try
        {
            _instance.PlacerInitialized?.Invoke();
        }
        catch (Exception error)
        {
            Log.Default.WriteLine(Label + "Placer hook failed: " + error.GetType().Name + " " + error.Message);
        }
    }

    /// <summary>Raised when the local block placer initializes in a loaded world (game UI is running).</summary>
    internal Action? PlacerInitialized { get; set; }

    private static void OnPlacerDestroy(BlockPlacerEntityComponent __instance)
    {
        if (_instance?._placer is { } weak && weak.TryGetTarget(out var current) && ReferenceEquals(current, __instance))
            _instance._placer = null;
    }

    private static long _placerLastActiveMs = long.MinValue / 2;

    // Runs every frame from the job system while a block is equipped (possibly off
    // the UI thread): only record the time, never touch game or UI state here.
    private static void OnPlacerActive() =>
        System.Threading.Interlocked.Exchange(ref _placerLastActiveMs, Environment.TickCount64);

    /// <summary><see cref="Environment.TickCount64"/> when the block placer last ran.</summary>
    public static long PlacerLastActiveMs => System.Threading.Interlocked.Read(ref _placerLastActiveMs);

    public bool IsSupported => _unsupported is null;

    public string Status
    {
        get
        {
            if (_unsupported is not null) return _unsupported;
            if (!TryGetTarget(out var target, out var grid, out var reason)) return reason;
            var spans = Bounds(grid);
            var planes = target.BuildingInfo.SymmetryPlanes;
            return "Target grid\n  " + GridMath.Describe('X', spans.X) + "\n  " + GridMath.Describe('Y', spans.Y) +
                "\n  " + GridMath.Describe('Z', spans.Z) +
                "\nCurrent planes: " + GridMath.DescribePlane('X', planes.X) + " · " +
                GridMath.DescribePlane('Y', planes.Y) + " · " + GridMath.DescribePlane('Z', planes.Z);
        }
    }

    /// <summary>Sets the requested axes (0 = X, 1 = Y, 2 = Z) to the Center of Grid.</summary>
    public string SetToCenterOfGrid(params int[] axes)
    {
        var target = RequireTarget(out var grid);
        var spans = Bounds(grid);
        foreach (var axis in axes) target.AddSymmetryPlane(Axes[axis], spans.Get(axis).PlaneDistance);
        return "Symmetry set to CoG on " + Names(axes) + ". Toggle symmetry mode in the block placer to see the planes.";
    }

    private const int MaxRoomCells = 4_000_000; // 0.25 m cells: ~62,500 m³
    private const double TorsoHeight = 0.9;     // m along the character's up axis

    /// <summary>
    /// Sets symmetry to the Center of Room: the bounding box of the empty space
    /// (6-connected, 0.25 m cells) around the player inside the target grid.
    /// <paramref name="corridor"/> sets only the axes across the room's longest span.
    /// </summary>
    public string SetToCenterOfRoom(bool corridor)
    {
        var target = RequireTarget(out var grid);
        var cell = PlayerCell(target.WorldTransform);
        var bounds = Bounds(grid);
        var storage = grid.CubeBlockStorage;
        var room = RoomFill.Fill((cell.X, cell.Y, cell.Z), (bounds.X.Min, bounds.Y.Min, bounds.Z.Min),
            (bounds.X.Max, bounds.Y.Max, bounds.Z.Max), (x, y, z) => storage.IsPositionEmpty(new Vector3I(x, y, z)), MaxRoomCells);
        switch (room.Status)
        {
            case RoomStatus.Open:
                throw new InvalidOperationException("Not inside an enclosed room: the space around you connects to the outside of the grid");
            case RoomStatus.TooLarge:
                throw new InvalidOperationException("The room is too large to measure (over " + MaxRoomCells.ToString("N0", CultureInfo.InvariantCulture) + " cells)");
            case RoomStatus.StartBlocked:
                throw new InvalidOperationException("You appear to be inside blocks; step into the room and try again");
        }
        var spans = new GridSpans(room.X, room.Y, room.Z);
        var axes = corridor ? RoomFill.CorridorAxes(room) : new[] { 0, 1, 2 };
        foreach (var axis in axes) target.AddSymmetryPlane(Axes[axis], spans.Get(axis).PlaneDistance);
        return string.Format(CultureInfo.InvariantCulture, "Symmetry set to CoR on {0}. Room {1:0.00} × {2:0.00} × {3:0.00} m",
            Names(axes), room.X.Meters, room.Y.Meters, room.Z.Meters);
    }

    /// <summary>The grid cell at the local character's torso (about 0.9 m along its up axis).</summary>
    internal Vector3I PlayerCell(WorldTransform gridTransform)
    {
        var character = LocalCharacter() ?? throw new InvalidOperationException("Your character is unavailable");
        var body = EntityTransformFunctions.GetWorldTransform(character.Data);
        var torso = body.Position + WorldTransform.TransformDirection(new Vector3D(0, 1, 0), body) * TorsoHeight;
        return CubeGridCoords.WorldToGrid(torso, gridTransform);
    }

    /// <summary>The local block placer, while one exists (a world is loaded).</summary>
    internal BlockPlacerEntityComponent? Placer => _placer is not null && _placer.TryGetTarget(out var placer) ? placer : null;

    /// <summary>
    /// Moves the plane of the grid axis you are looking along by <paramref name="halfCells"/>
    /// half-cells (0.125 m each); positive moves it away from you.
    /// </summary>
    public string NudgeFacing(int halfCells)
    {
        var target = RequireTarget(out _);
        var forward = CameraForward() ?? throw new InvalidOperationException("Camera direction is unavailable");
        var local = WorldTransform.TransformDirectionInv(new Vector3D(forward), target.WorldTransform);
        var (axis, sign) = Nudge.FacingAxis(local.X, local.Y, local.Z);
        var planes = target.BuildingInfo.SymmetryPlanes;
        var current = axis == 0 ? planes.X : axis == 1 ? planes.Y : planes.Z;
        if (current == GridMath.DisabledPlane)
            throw new InvalidOperationException("You are facing along " + "XYZ"[axis] + ", which has no symmetry plane to nudge");
        var next = Nudge.Step(current, sign * halfCells);
        target.AddSymmetryPlane(Axes[axis], next);
        return string.Format(CultureInfo.InvariantCulture, "{0} plane moved {1:+0.000;-0.000} m ({2})",
            "XYZ"[axis], halfCells * GridMath.CellSize / 2, GridMath.DescribePlane("XYZ"[axis], next));
    }

    /// <summary>Active plane distances per axis for the target grid (null = off).</summary>
    public int?[] CurrentPlanes()
    {
        var planes = RequireTarget(out _).BuildingInfo.SymmetryPlanes;
        int? On(int d) => d == GridMath.DisabledPlane ? null : d;
        return new[] { On(planes.X), On(planes.Y), On(planes.Z) };
    }

    /// <summary>Sets every axis to the given plane (or removes it when null).</summary>
    public void ApplyPlanes(int?[] planes)
    {
        var target = RequireTarget(out _);
        for (var axis = 0; axis < 3; axis++)
        {
            if (planes[axis] is { } d) target.AddSymmetryPlane(Axes[axis], d);
            else target.RemoveSymmetryPlaneByAxis(Axes[axis]);
        }
    }

    /// <summary>Size, block count, attached grids, mass and the CoM–CoG offset of the target grid.</summary>
    public string Info()
    {
        if (!TryGetTarget(out var target, out var grid, out var reason)) return reason;
        var spans = Bounds(grid);
        var ship = 0;
        foreach (var _ in grid.GetAllGridsInShip()) ship++;
        var text = string.Format(CultureInfo.InvariantCulture, "Grid info: {0:0.00} × {1:0.00} × {2:0.00} m, {3:N0} blocks{4}",
            spans.X.Meters, spans.Y.Meters, spans.Z.Meters, grid.CubeBlockStorage.CubeBlockCount,
            ship > 1 ? ", " + (ship - 1) + " attached grid" + (ship == 2 ? "" : "s") : "");
        if (target.RootEntity.Data.TryGet<Keen.VRage.Physics.Data.RigidBodyMassProperties>(out var mass) && mass.Mass > 0)
        {
            var com = mass.CenterOfMass;
            double dx = com.X - spans.X.PlaneDistance * 0.125, dy = com.Y - spans.Y.PlaneDistance * 0.125, dz = com.Z - spans.Z.PlaneDistance * 0.125;
            text += string.Format(CultureInfo.InvariantCulture,
                "\n  Mass {0:N0} kg · CoM offset from CoG X {1:+0.00;-0.00;0.00} Y {2:+0.00;-0.00;0.00} Z {3:+0.00;-0.00;0.00} m",
                mass.Mass, dx, dy, dz);
        }
        else
        {
            text += "\n  Static grid: no mass data";
        }
        return text;
    }

    /// <summary>World-space forward of the player's render camera.</summary>
    internal Vector3? CameraForward()
    {
        if (Placer is not { } placer) return null;
        var camera = Players(placer)?.LocalPlayerController?.CameraSystem?.RenderCameraEntity;
        if (camera is null) return null;
        return EntityTransformFunctions.GetWorldTransform(camera.Data).Orientation.GetForward();
    }

    public string Clear(params int[] axes)
    {
        var target = RequireTarget(out _);
        foreach (var axis in axes) target.RemoveSymmetryPlaneByAxis(Axes[axis]);
        return "Symmetry cleared on " + Names(axes) + ".";
    }

    private static string Names(int[] axes) => string.Join("/", Array.ConvertAll(axes, a => "XYZ"[a].ToString()));

    internal GridPlacementTarget RequireTarget(out CubeGridComponent grid)
    {
        if (_unsupported is not null) throw new InvalidOperationException(_unsupported);
        if (!TryGetTarget(out var target, out grid, out var reason)) throw new InvalidOperationException(reason);
        return target;
    }

    /// <summary>The locally controlled character (bottom of the control stack), as vanilla's debug teleport uses.</summary>
    internal Entity? LocalCharacter()
    {
        if (_placer is null || !_placer.TryGetTarget(out var placer)) return null;
        return Players(placer)?.LocalPlayerController?.GetBottomControllable()?.Entity;
    }

    private bool TryGetTarget(out GridPlacementTarget target, out CubeGridComponent grid, out string reason)
    {
        target = null!;
        grid = null!;
        if (_placer is null || !_placer.TryGetTarget(out var placer))
        {
            reason = "Waiting — equip a block in a world and look at a grid";
            return false;
        }
        var candidate = placer.AlignedGrid?.Target ?? LastLookedAtGrid(placer)?.Target;
        if (candidate is ProjectionPlacementTarget)
        {
            reason = "Projections are not supported yet — look at a built grid";
            return false;
        }
        if (candidate is not GridPlacementTarget gridTarget || TargetGrid(gridTarget) is not { GridClosing: false } found)
        {
            reason = "Waiting — with a block equipped, look at the grid you want to edit";
            return false;
        }
        if (found.CubeBlockStorage.CubeBlockCount == 0)
        {
            reason = "The target grid has no blocks";
            return false;
        }
        target = gridTarget;
        grid = found;
        reason = string.Empty;
        return true;
    }

    // Matches CubeGridComponent.ComputeBoundingBox: the union of every block's cell AABB.
    internal static GridSpans Bounds(CubeGridComponent grid)
    {
        var box = BoundingBoxI.CreateInvalid();
        foreach (var block in grid.CubeBlockStorage.GetAllCubeBlocks())
        {
            if (block is null) continue;
            var aabb = block.AABB;
            box.Include(in aabb);
        }
        return new GridSpans(new CellSpan(box.Min.X, box.Max.X), new CellSpan(box.Min.Y, box.Max.Y),
            new CellSpan(box.Min.Z, box.Max.Z));
    }

    public void Dispose()
    {
        _harmony?.UnpatchAll(OwnerId);
        if (ReferenceEquals(_instance, this)) _instance = null;
        _placer = null;
    }

    internal readonly record struct GridSpans(CellSpan X, CellSpan Y, CellSpan Z)
    {
        public CellSpan Get(int axis) => axis switch { 0 => X, 1 => Y, _ => Z };
    }
}
