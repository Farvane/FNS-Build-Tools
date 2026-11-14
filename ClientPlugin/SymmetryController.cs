using System;
using HarmonyLib;
using Keen.Game2.Client.GameSystems.BlockPlacement.BlockPlacer;
using Keen.Game2.Simulation.GameSystems.BlockPlacement;
using Keen.Game2.Simulation.WorldObjects.CubeGrids;
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
        if (_instance is not null) _instance._placer = new WeakReference<BlockPlacerEntityComponent>(__instance);
    }

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

    public string Clear(params int[] axes)
    {
        var target = RequireTarget(out _);
        foreach (var axis in axes) target.RemoveSymmetryPlaneByAxis(Axes[axis]);
        return "Symmetry cleared on " + Names(axes) + ".";
    }

    private static string Names(int[] axes) => string.Join("/", Array.ConvertAll(axes, a => "XYZ"[a].ToString()));

    private GridPlacementTarget RequireTarget(out CubeGridComponent grid)
    {
        if (_unsupported is not null) throw new InvalidOperationException(_unsupported);
        if (!TryGetTarget(out var target, out grid, out var reason)) throw new InvalidOperationException(reason);
        return target;
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
    private static GridSpans Bounds(CubeGridComponent grid)
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

    private readonly record struct GridSpans(CellSpan X, CellSpan Y, CellSpan Z)
    {
        public CellSpan Get(int axis) => axis switch { 0 => X, 1 => Y, _ => Z };
    }
}
