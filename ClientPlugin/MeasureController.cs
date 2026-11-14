using System;
using System.Globalization;
using System.Threading;
using HarmonyLib;
using Keen.Game2.Client.GameSystems.BlockPlacement.BlockPlacer;
using Keen.Game2.Client.GameSystems.UI3D;
using Keen.Game2.Simulation.GameSystems.BlockPlacement;
using Keen.Game2.Simulation.WorldObjects.CubeGrids;
using Keen.VRage.Core;
using Keen.VRage.Core.Game.Data;
using Keen.VRage.DCS.Components;
using Keen.VRage.Library.Diagnostics;
using Keen.VRage.Library.Mathematics;
using Keen.VRage.Library.Utils;
using Keen.VRage.Core.Render;

namespace Farvane.BuildTools;

/// <summary>
/// Measuring tools: Tape (mark blocks A and B), Midpoint → symmetry, Clear width at the
/// player's position, and a live readout of the aimed block's offset from the symmetry
/// planes. Actions run on the UI thread; drawing runs in a postfix on the block placer's
/// per-frame DrawBlockPlacement job (where vanilla draws its own UI3D overlays), reading an
/// immutable state snapshot. Uses vanilla UI3D helpers in world space.
/// </summary>
internal sealed class MeasureController : IDisposable
{
    private const string Label = "[FNS Build Tools] ";
    private const string HarmonyId = SymmetryController.OwnerId + ".measure";
    private const int MaxClearCells = 400; // 100 m each way
    private static readonly TimeSpan ClearProbeLifetime = TimeSpan.FromSeconds(10);

    private static readonly AccessTools.FieldRef<BlockPlacerEntityComponent, UI3DSessionComponent> Ui3d =
        AccessTools.FieldRefAccess<BlockPlacerEntityComponent, UI3DSessionComponent>("_ui3D");

    // FNS palette: green A, blue B, OPC orange for clearance, near-white text.
    private static readonly ColorSRGB ColorA = new(0x42, 0xB7, 0x7B, 0xFF);
    private static readonly ColorSRGB ColorB = new(0x35, 0x84, 0xC4, 0xFF);
    private static readonly ColorSRGB ColorClear = new(0xE9, 0x65, 0x12, 0xFF);
    private static readonly ColorSRGB ColorText = new(0xF2, 0xF2, 0xF2, 0xFF);
    private static readonly ColorSRGB ColorBehind = new(0xF2, 0xF2, 0xF2, 0x70);

    private static MeasureController? _instance;
    private readonly SymmetryController _context;
    private readonly Harmony? _harmony;
    private State _state = State.Empty;
    private int _drawFailed;

    public MeasureController(SymmetryController context)
    {
        _context = context;
        if (!context.IsSupported) return;
        try
        {
            var draw = AccessTools.Method(typeof(BlockPlacerEntityComponent), "DrawBlockPlacement", Type.EmptyTypes)
                ?? throw new InvalidOperationException("Block placer draw hook not found");
            _harmony = new Harmony(HarmonyId);
            _harmony.Patch(draw, postfix: new HarmonyMethod(typeof(MeasureController), nameof(OnDraw)));
            _instance = this;
        }
        catch (Exception error)
        {
            // Measurements still work and report in messages; only the overlay is missing.
            Log.Default.WriteLine(Label + "Measure overlay unavailable: " + error.GetType().Name + " " + error.Message);
            _harmony?.UnpatchAll(HarmonyId);
        }
    }

    private State Current => Volatile.Read(ref _state);

    private void Set(State next) => Volatile.Write(ref _state, next);

    /// <summary>Marks the aimed block as A, then B; a third mark starts a new measurement.</summary>
    public string MarkAimed()
    {
        var mark = AimedMark(out _);
        var state = Current;
        if (state.A is null || state.B is not null)
        {
            Set(state with { A = mark, B = null });
            return "A marked (" + Size(mark.Box) + "). Mark B next";
        }
        if (!SameGrid(state.A, mark)) throw new InvalidOperationException("Mark B on the same grid as A");
        Set(state with { B = mark });
        return Measure.Describe(Measure.Tape(state.A.Box, mark.Box));
    }

    public string ClearMarks()
    {
        Set(State.Empty with { Readout = Current.Readout });
        return "Measurements cleared";
    }

    /// <summary>Sets symmetry planes halfway between A and B on every axis where they are apart.</summary>
    public string MidpointToSymmetry()
    {
        var state = Current;
        if (state.A is null || state.B is null) throw new InvalidOperationException("Mark A and B first");
        if (!state.A.Grid.TryGetTarget(out var gridEntity) || !SameGrid(state.A, state.B))
            throw new InvalidOperationException("The marked grid no longer exists");
        var grid = gridEntity.TryGet<CubeGridComponent>(default(StringId))
            ?? throw new InvalidOperationException("The marked grid no longer exists");
        var tape = Measure.Tape(state.A.Box, state.B.Box);
        if (tape.SeparatedAxes.Length == 0) throw new InvalidOperationException("A and B are the same position; nothing to center");
        var target = new GridPlacementTarget(grid);
        var axes = new[] { SymmetryPlaneAxis.X, SymmetryPlaneAxis.Y, SymmetryPlaneAxis.Z };
        foreach (var axis in tape.SeparatedAxes) target.AddSymmetryPlane(axes[axis], tape.Midpoint(axis));
        return "Symmetry set to the A↔B midpoint on " + string.Join("/", Array.ConvertAll(tape.SeparatedAxes, a => "XYZ"[a].ToString()));
    }

    /// <summary>Free space along each grid axis through the player's position, shown for a few seconds.</summary>
    public string ClearWidthHere()
    {
        var target = _context.RequireTarget(out var grid);
        var cell = _context.PlayerCell(target.WorldTransform);
        var storage = grid.CubeBlockStorage;
        var spans = new (CellSpan Span, bool OpenLow, bool OpenHigh)?[3];
        for (var axis = 0; axis < 3; axis++)
            spans[axis] = Measure.ClearSpan((cell.X, cell.Y, cell.Z), axis,
                (x, y, z) => storage.IsPositionEmpty(new Vector3I(x, y, z)), MaxClearCells);
        if (spans[0] is null) throw new InvalidOperationException("You appear to be inside blocks; step into open space");
        Set(Current with { Clear = new ClearProbe(new WeakReference<Entity>(target.RootEntity), (cell.X, cell.Y, cell.Z), spans,
            DateTime.UtcNow + ClearProbeLifetime) });
        var parts = new string[3];
        for (var axis = 0; axis < 3; axis++) parts[axis] = "XYZ"[axis] + " " + Width(spans[axis]!.Value);
        return "Clear width here: " + string.Join(" · ", parts);
    }

    public string ToggleReadout()
    {
        var on = !Current.Readout;
        Set(Current with { Readout = on });
        return on ? "Centerline readout on: offsets of the aimed block from the symmetry planes" : "Centerline readout off";
    }

    private Mark AimedMark(out GridPlacementTarget gridTarget)
    {
        var placer = _context.Placer ?? throw new InvalidOperationException("Equip a block and look at a block on a grid");
        var block = placer.AlignedBlock?.Target ?? throw new InvalidOperationException("Look at a block on a grid to mark it");
        gridTarget = block.Grid as GridPlacementTarget ?? throw new InvalidOperationException("Projections cannot be measured; look at a built block");
        var box = block.AABB;
        return new Mark(new WeakReference<Entity>(gridTarget.RootEntity), new CellBox(box.Min.X, box.Min.Y, box.Min.Z, box.Max.X, box.Max.Y, box.Max.Z));
    }

    private static bool SameGrid(Mark a, Mark b) =>
        a.Grid.TryGetTarget(out var ga) && b.Grid.TryGetTarget(out var gb) && ReferenceEquals(ga, gb);

    private static string Size(CellBox box) => string.Format(CultureInfo.InvariantCulture, "{0:0.##} × {1:0.##} × {2:0.##} m",
        box.Axis(0).Meters, box.Axis(1).Meters, box.Axis(2).Meters);

    private static string Width((CellSpan Span, bool OpenLow, bool OpenHigh) clear) =>
        clear.OpenLow || clear.OpenHigh
            ? string.Format(CultureInfo.InvariantCulture, "≥{0:0.00} m (open)", clear.Span.Meters)
            : string.Format(CultureInfo.InvariantCulture, "{0:0.00} m", clear.Span.Meters);

    // ---- Drawing (block placer draw job; never throw, never mutate state) ----

    private static void OnDraw(BlockPlacerEntityComponent __instance)
    {
        var self = _instance;
        if (self is null || Volatile.Read(ref self._drawFailed) != 0) return;
        try
        {
            self.Draw(__instance);
        }
        catch (Exception error)
        {
            // Draw runs every frame; report once and stop drawing rather than spam the log.
            if (Interlocked.Exchange(ref self._drawFailed, 1) == 0)
                Log.Default.WriteLine(Label + "Measure overlay disabled after an error: " + error);
        }
    }

    private void Draw(BlockPlacerEntityComponent placer)
    {
        var state = Current;
        if (state.A is null && state.Clear is null && !state.Readout) return;
        var ui3d = Ui3d(placer);
        if (ui3d is null) return;
        var font = placer.Definition.BuildDimensionsFont;

        Vector3D? centerA = DrawMark(ui3d, font, state.A, ColorA, "A");
        Vector3D? centerB = DrawMark(ui3d, font, state.B, ColorB, "B");
        if (centerA is { } a && centerB is { } b)
        {
            UI3DDefaultRendering.DrawLine(ui3d, a, ColorA, b, ColorB, 2f, UI3DSessionComponent.DepthBehavior.Both);
            var mid = (a + b) * 0.5;
            var tape = Measure.Tape(state.A!.Box, state.B!.Box);
            ui3d.AddText(in mid, string.Format(CultureInfo.InvariantCulture, "{0:0.00} m  (X {1:0.##} · Y {2:0.##} · Z {3:0.##})",
                tape.CenterDistanceMeters, tape.Outer[0].Meters, tape.Outer[1].Meters, tape.Outer[2].Meters),
                ColorText, font, 1f, IMeshConsumer.TextAlignmentEnum.AlignCenter, false, false, default);
        }

        if (state.Clear is { } clear && DateTime.UtcNow < clear.Until && clear.Grid.TryGetTarget(out var clearGrid))
        {
            var wt = EntityTransformFunctions.GetWorldTransform(clearGrid.Data);
            for (var axis = 0; axis < 3; axis++)
            {
                if (clear.Axes[axis] is not { } span) continue;
                var low = CellPoint(clear.Start, axis, span.Span.Min - 0.5);
                var high = CellPoint(clear.Start, axis, span.Span.Max + 0.5);
                var wLow = CubeGridCoords.LocalToWorld(low, wt);
                var wHigh = CubeGridCoords.LocalToWorld(high, wt);
                UI3DDefaultRendering.DrawLine(ui3d, wLow, ColorClear, wHigh, ColorClear, 2f, UI3DSessionComponent.DepthBehavior.Both);
                var label = wHigh;
                ui3d.AddText(in label, "XYZ"[axis] + " " + Width(span), ColorClear, font, 1f, IMeshConsumer.TextAlignmentEnum.AlignCenter, false, false, default);
            }
        }

        if (state.Readout && placer.AlignedBlock?.Target is { Grid: GridPlacementTarget gridTarget } aimed)
        {
            var planes = gridTarget.BuildingInfo.SymmetryPlanes;
            var box = aimed.AABB;
            var cell = new CellBox(box.Min.X, box.Min.Y, box.Min.Z, box.Max.X, box.Max.Y, box.Max.Z);
            var parts = new string[3];
            for (var axis = 0; axis < 3; axis++)
            {
                var plane = axis == 0 ? planes.X : axis == 1 ? planes.Y : planes.Z;
                parts[axis] = plane == GridMath.DisabledPlane
                    ? "XYZ"[axis] + " —"
                    : string.Format(CultureInfo.InvariantCulture, "{0} {1:+0.00;-0.00;0.00} m", "XYZ"[axis],
                        Measure.OffsetFromPlane(cell.Center2(axis), plane));
            }
            var top = CubeGridCoords.LocalToWorld(CellCenter(cell) + new Vector3(0, (float)(cell.Axis(1).Meters / 2 + 0.3), 0), gridTarget.WorldTransform);
            ui3d.AddText(in top, string.Join(" · ", parts), ColorText, font, 1f, IMeshConsumer.TextAlignmentEnum.AlignCenter, false, false, default);
        }
    }

    private static Vector3D? DrawMark(UI3DSessionComponent ui3d, Keen.VRage.Render.Contracts.Font font, Mark? mark, ColorSRGB color, string name)
    {
        if (mark is null || !mark.Grid.TryGetTarget(out var grid)) return null;
        var wt = EntityTransformFunctions.GetWorldTransform(grid.Data);
        var cells = new BoundingBoxI(new Vector3I(mark.Box.MinX, mark.Box.MinY, mark.Box.MinZ), new Vector3I(mark.Box.MaxX, mark.Box.MaxY, mark.Box.MaxZ));
        UI3DDefaultRendering.DrawSelectionOutline(ui3d, CubeGridCoords.GridToWorldOriented(cells, wt), color, 3f, ColorBehind, 0.75f, 0.2f);
        var center = CubeGridCoords.LocalToWorld(CellCenter(mark.Box), wt);
        var label = center;
        ui3d.AddText(in label, name, color, font, 1.4f, IMeshConsumer.TextAlignmentEnum.AlignCenter, false, false, default);
        return center;
    }

    // Cell i's center is at i × 0.25 m in grid-local space; a box center is (Min + Max) / 2 cells.
    private static Vector3 CellCenter(CellBox box) => new(
        (float)(box.Center2(0) * GridMath.CellSize / 2), (float)(box.Center2(1) * GridMath.CellSize / 2), (float)(box.Center2(2) * GridMath.CellSize / 2));

    private static Vector3 CellPoint((int X, int Y, int Z) start, int axis, double along)
    {
        double x = start.X, y = start.Y, z = start.Z;
        if (axis == 0) x = along; else if (axis == 1) y = along; else z = along;
        return new Vector3((float)(x * GridMath.CellSize), (float)(y * GridMath.CellSize), (float)(z * GridMath.CellSize));
    }

    public void Dispose()
    {
        _harmony?.UnpatchAll(HarmonyId);
        if (ReferenceEquals(_instance, this)) _instance = null;
    }

    private sealed record Mark(WeakReference<Entity> Grid, CellBox Box);

    private sealed record ClearProbe(WeakReference<Entity> Grid, (int X, int Y, int Z) Start,
        (CellSpan Span, bool OpenLow, bool OpenHigh)?[] Axes, DateTime Until);

    private sealed record State(Mark? A, Mark? B, ClearProbe? Clear, bool Readout)
    {
        public static readonly State Empty = new(null, null, null, false);
    }
}
