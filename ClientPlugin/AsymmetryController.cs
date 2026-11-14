using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using Avalonia.Threading;
using HarmonyLib;
using Keen.Game2.Client.GameSystems.BlockPlacement.BlockPlacer;
using Keen.Game2.Client.GameSystems.UI3D;
using Keen.Game2.Simulation.WorldObjects.CubeGrids;
using Keen.VRage.Core.Game.Data;
using Keen.VRage.DCS.Components;
using Keen.VRage.Library.Diagnostics;
using Keen.VRage.Library.Mathematics;

namespace Farvane.BuildTools;

/// <summary>
/// Live asymmetry finder: outlines blocks with no mirror (or a different block / paint
/// in the mirrored spot) across the target grid's active symmetry planes. Scans run on
/// the UI thread and repeat whenever the grid's block storage version changes; drawing
/// runs in the block placer's DrawBlockPlacement job from an immutable snapshot.
/// </summary>
internal sealed class AsymmetryController : IDisposable
{
    private const string Label = "[FNS Build Tools] ";
    private const string HarmonyId = SymmetryController.OwnerId + ".asymmetry";
    private const int MaxDrawn = 300;

    private static readonly AccessTools.FieldRef<BlockPlacerEntityComponent, UI3DSessionComponent> Ui3d =
        AccessTools.FieldRefAccess<BlockPlacerEntityComponent, UI3DSessionComponent>("_ui3D");

    private static readonly ColorSRGB ColorMissing = new(0xE0, 0x3C, 0x31, 0xFF);
    private static readonly ColorSRGB ColorGhost = new(0xE0, 0x3C, 0x31, 0x80);
    private static readonly ColorSRGB ColorBlock = new(0xE9, 0x65, 0x12, 0xFF);
    private static readonly ColorSRGB ColorPaint = new(0xF2, 0xC9, 0x4C, 0xFF);
    private static readonly ColorSRGB ColorBehind = new(0xF2, 0xF2, 0xF2, 0x50);

    private static AsymmetryController? _instance;
    private readonly SymmetryController _context;
    private readonly Harmony? _harmony;
    private DispatcherTimer? _timer;
    private Snapshot? _snapshot;
    private int _drawFailed;

    public AsymmetryController(SymmetryController context)
    {
        _context = context;
        if (!context.IsSupported) return;
        try
        {
            var draw = AccessTools.Method(typeof(BlockPlacerEntityComponent), "DrawBlockPlacement", Type.EmptyTypes)
                ?? throw new InvalidOperationException("Block placer draw hook not found");
            _harmony = new Harmony(HarmonyId);
            _harmony.Patch(draw, postfix: new HarmonyMethod(typeof(AsymmetryController), nameof(OnDraw)));
            _instance = this;
        }
        catch (Exception error)
        {
            Log.Default.WriteLine(Label + "Asymmetry overlay unavailable: " + error.GetType().Name + " " + error.Message);
            _harmony?.UnpatchAll(HarmonyId);
        }
    }

    public bool IsOn => _timer is not null;

    /// <summary>Latest summary for the tools screen, or null when off.</summary>
    public string? Summary => Volatile.Read(ref _snapshot)?.Summary;

    /// <summary>Turns the live finder on for the looked-at grid, or off. Call on the UI thread.</summary>
    public string Toggle()
    {
        if (IsOn)
        {
            Stop();
            return "Asymmetry finder off";
        }
        _context.RequireTarget(out var grid);
        var snapshot = Scan(grid);
        Volatile.Write(ref _snapshot, snapshot);
        var weak = new WeakReference<CubeGridComponent>(grid);
        var version = grid.CubeBlockStorage.Version;
        var lastScan = DateTime.UtcNow;
        // UI thread, created lazily in-world (never from the plugin constructor).
        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        _timer.Tick += (_, _) =>
        {
            try
            {
                if (!weak.TryGetTarget(out var g) || g.GridClosing)
                {
                    Stop();
                    return;
                }
                var planes = g.GetPlanes();
                var current = Volatile.Read(ref _snapshot);
                // Block add/remove bumps the storage version; paint may not, so also rescan every few seconds.
                if (g.CubeBlockStorage.Version == version && current is not null && current.Planes.SequenceEqual(planes) &&
                    DateTime.UtcNow - lastScan < TimeSpan.FromSeconds(3)) return;
                version = g.CubeBlockStorage.Version;
                lastScan = DateTime.UtcNow;
                Volatile.Write(ref _snapshot, Scan(g));
            }
            catch (Exception error)
            {
                Log.Default.WriteLine(Label + "Asymmetry rescan failed: " + error);
                Stop();
            }
        };
        _timer.Start();
        return snapshot.Summary + ". Live until toggled off";
    }

    private void Stop()
    {
        _timer?.Stop();
        _timer = null;
        Volatile.Write(ref _snapshot, null);
    }

    private static Snapshot Scan(CubeGridComponent grid)
    {
        var planes = grid.GetPlanes();
        if (planes.All(p => p is null))
            throw new InvalidOperationException("No symmetry planes on this grid. Set some first (Ctrl+Alt+N for CoG)");
        var boxes = new List<CellBox>();
        var types = new List<object?>();
        var paints = new List<object?>();
        foreach (var block in grid.CubeBlockStorage.GetAllCubeBlocks())
        {
            if (block is null) continue;
            var box = block.AABB;
            boxes.Add(new CellBox(box.Min.X, box.Min.Y, box.Min.Z, box.Max.X, box.Max.Y, box.Max.Z));
            types.Add(block.Definition);
            paints.Add(block.Color);
        }
        var issues = Asymmetry.Find(boxes, types, paints, planes);
        // Flag each block once (its first issue), keeping every missing-mirror ghost.
        var flagged = new Dictionary<int, AsymmetryKind>();
        var ghosts = new List<CellBox>();
        foreach (var issue in issues)
        {
            if (!flagged.ContainsKey(issue.Block)) flagged[issue.Block] = issue.Kind;
            if (issue.Kind == AsymmetryKind.Missing) ghosts.Add(issue.Expected);
        }
        var marks = flagged.Select(f => (boxes[f.Key], f.Value)).ToArray();
        int Count(AsymmetryKind kind) => flagged.Values.Count(k => k == kind);
        var active = string.Join("/", Enumerable.Range(0, 3).Where(a => planes[a] is not null).Select(a => "XYZ"[a].ToString()));
        var summary = marks.Length == 0
            ? "Symmetric across " + active + ": every block has its mirror"
            : string.Format(CultureInfo.InvariantCulture,
                "Asymmetry across {0}: {1} without a mirror, {2} different block, {3} different paint",
                active, Count(AsymmetryKind.Missing), Count(AsymmetryKind.DifferentBlock), Count(AsymmetryKind.DifferentPaint));
        return new Snapshot(new WeakReference<CubeGridComponent>(grid), planes, marks, ghosts.Distinct().ToArray(), summary);
    }

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
            if (Interlocked.Exchange(ref self._drawFailed, 1) == 0)
                Log.Default.WriteLine(Label + "Asymmetry overlay disabled after an error: " + error);
        }
    }

    private void Draw(BlockPlacerEntityComponent placer)
    {
        var snapshot = Volatile.Read(ref _snapshot);
        if (snapshot is null || !snapshot.Grid.TryGetTarget(out var grid)) return;
        var ui3d = Ui3d(placer);
        if (ui3d is null) return;
        var wt = EntityTransformFunctions.GetWorldTransform(grid.Entity.Data);
        var drawn = 0;
        foreach (var (box, kind) in snapshot.Marks)
        {
            if (drawn++ >= MaxDrawn) break;
            var color = kind switch
            {
                AsymmetryKind.Missing => ColorMissing,
                AsymmetryKind.DifferentBlock => ColorBlock,
                _ => ColorPaint,
            };
            UI3DDefaultRendering.DrawSelectionOutline(ui3d, CubeGridCoords.GridToWorldOriented(Cells(box), wt), color, 3f, ColorBehind, 0.75f, 0.2f);
        }
        foreach (var ghost in snapshot.Ghosts)
        {
            if (drawn++ >= MaxDrawn) break;
            UI3DDefaultRendering.DrawSelectionOutline(ui3d, CubeGridCoords.GridToWorldOriented(Cells(ghost), wt), ColorGhost, 1.5f, ColorBehind, 0.5f, 0.2f);
        }
    }

    private static BoundingBoxI Cells(CellBox box) =>
        new(new Vector3I(box.MinX, box.MinY, box.MinZ), new Vector3I(box.MaxX, box.MaxY, box.MaxZ));

    public void Dispose()
    {
        if (_timer is not null) Dispatcher.UIThread.Post(Stop);
        _harmony?.UnpatchAll(HarmonyId);
        if (ReferenceEquals(_instance, this)) _instance = null;
    }

    private sealed record Snapshot(WeakReference<CubeGridComponent> Grid, int?[] Planes,
        (CellBox Box, AsymmetryKind Kind)[] Marks, CellBox[] Ghosts, string Summary);
}

internal static class GridPlaneExtensions
{
    /// <summary>Active symmetry plane distances per axis (doubled cells), null when off.</summary>
    public static int?[] GetPlanes(this CubeGridComponent grid)
    {
        var info = grid.Entity.TryGet<BuildingInfoComponent>(default(Keen.VRage.Library.Utils.StringId))
            ?? throw new InvalidOperationException("This grid has no symmetry data");
        var planes = info.SymmetryData.SymmetryPlanes;
        int? On(int d) => d == GridMath.DisabledPlane ? null : d;
        return new[] { On(planes.X), On(planes.Y), On(planes.Z) };
    }
}
