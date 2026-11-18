using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Avalonia.Threading;
using HarmonyLib;
using Keen.Game2.Client.GameSystems.BlockPlacement.BlockPlacer;
using Keen.Game2.Client.GameSystems.Interaction;
using Keen.Game2.Client.GameSystems.UI3D;
using Keen.Game2.Simulation.GameSystems.BlockPlacement;
using Keen.Game2.Simulation.WorldObjects.CubeBlocks;
using Keen.Game2.Simulation.WorldObjects.CubeGrids;
using Keen.VRage.Core;
using Keen.VRage.Core.Game.Data;
using Keen.VRage.Core.Input;
using Keen.VRage.DCS.Components;
using Keen.VRage.Game.Client.Render;
using Keen.VRage.Input;
using Keen.VRage.Library.Mathematics;
using Keen.VRage.Library.Utils;

namespace Farvane.BuildTools;

/// <summary>Selects the exact built block under the block placer's cursor.</summary>
internal sealed class WorldSelection : IDisposable
{
    private const string HarmonyId = Plugin.Name + ".WorldSelection";
    private const long PlacerActiveWindowMs = 250;
    private static readonly ColorSRGB SelectedColor = new(35, 210, 255, 255);
    private static readonly ColorSRGB BehindColor = new(35, 210, 255, 90);
    private static readonly AccessTools.FieldRef<GridPlacementTarget, CubeGridComponent> TargetGrid =
        AccessTools.FieldRefAccess<GridPlacementTarget, CubeGridComponent>("_grid");
    private static readonly AccessTools.FieldRef<BlockPlacerEntityComponent, UI3DSessionComponent> Ui3d =
        AccessTools.FieldRefAccess<BlockPlacerEntityComponent, UI3DSessionComponent>("_ui3D");

    private readonly Harmony _harmony = new(HarmonyId);
    private readonly Action _openScreen;
    private static WorldSelection? _instance;
    private WeakReference<BlockPlacerEntityComponent>? _placer;
    private SelectedBlock[] _selected = Array.Empty<SelectedBlock>();
    private DispatcherTimer? _timer;
    private int _timerRequested;
    private bool _selectWasDown;
    private bool _openWasDown;
    private long _placerLastActiveMs = long.MinValue / 2;
    private int _drawFailed;

    internal bool Enabled { get; private set; }
    internal int Count => Volatile.Read(ref _selected).Length;

    internal WorldSelection(Action openScreen)
    {
        _openScreen = openScreen;
        if (_instance is not null) throw new InvalidOperationException("Another Build Tools naming selector is already running.");
        _instance = this;
        Patch("Init", nameof(OnPlacerInit));
        Patch("Destroy", nameof(OnPlacerDestroy));
        Patch("BlockPlacerActive", nameof(OnPlacerActive));
        Patch("DrawBlockPlacement", nameof(OnDraw));
        var interact = AccessTools.Method(typeof(UseAction), nameof(UseAction.InteractPrimaryStart));
        if (interact is not null)
            _harmony.Patch(interact, prefix: new HarmonyMethod(typeof(WorldSelection), nameof(ConsumeInteraction)));
    }

    private void Patch(string gameMethod, string patchMethod)
    {
        var method = AccessTools.Method(typeof(BlockPlacerEntityComponent), gameMethod, Type.EmptyTypes)
            ?? throw new NotSupportedException("The SE2 block-placer " + gameMethod + " hook changed in this game build.");
        _harmony.Patch(method, postfix: new HarmonyMethod(typeof(WorldSelection), patchMethod));
    }

    internal void SetEnabled(bool enabled)
    {
        Enabled = enabled;
        Diag.Info("First-person selection mode " + (enabled ? "enabled" : "disabled") + "; " + Count + " block(s) selected.");
    }

    internal IReadOnlyList<(string Name, object Target)> Snapshot() => Volatile.Read(ref _selected)
        .Select(block => (CurrentName(block.Component, block.Entity), (object)block.Component)).ToList();

    internal void Clear()
    {
        Volatile.Write(ref _selected, Array.Empty<SelectedBlock>());
        Diag.Info("First-person selection cleared.");
    }

    private static void OnPlacerInit(BlockPlacerEntityComponent __instance)
    {
        var self = _instance;
        if (self is null) return;
        self._placer = new WeakReference<BlockPlacerEntityComponent>(__instance);
        self.StartKeyboardPolling();
    }

    private static void OnPlacerDestroy(BlockPlacerEntityComponent __instance)
    {
        var self = _instance;
        if (self?._placer is { } weak && weak.TryGetTarget(out var current) && ReferenceEquals(current, __instance))
            self._placer = null;
    }

    private static void OnPlacerActive()
    {
        var self = _instance;
        if (self is not null) Interlocked.Exchange(ref self._placerLastActiveMs, Environment.TickCount64);
    }

    private void StartKeyboardPolling()
    {
        if (Interlocked.Exchange(ref _timerRequested, 1) == 1) return;
        Dispatcher.UIThread.Post(() =>
        {
            _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(30) };
            _timer.Tick += (_, _) => PollKeyboard();
            _timer.Start();
            Diag.Info("In-world naming hotkeys started: Ctrl+Alt+Shift+I opens Smart Naming; F toggles the aimed block while selection is active.");
        });
    }

    private void PollKeyboard()
    {
        try
        {
            var keyboard = Singleton<InputDeviceManager>.Instance?.Keyboard;
            if (keyboard is null) return;
            var ctrl = KeyboardInputs.Control.IsActive(keyboard);
            var alt = KeyboardInputs.Alt.IsActive(keyboard);
            var shift = KeyboardInputs.Shift.IsActive(keyboard);
            var openDown = KeyboardInputs.I.IsActive(keyboard) && ctrl && alt && shift;
            if (openDown && !_openWasDown) _openScreen();
            _openWasDown = openDown;

            var selectDown = KeyboardInputs.F.IsActive(keyboard) && !ctrl && !alt && !shift;
            if (selectDown && !_selectWasDown && Enabled)
            {
                if (Environment.TickCount64 - Interlocked.Read(ref _placerLastActiveMs) >= PlacerActiveWindowMs)
                    Diag.Info("Selection ignored: equip any block, then aim at the functional block and press F.");
                else
                {
                    try { ToggleAimed(); }
                    catch (Exception error) { Diag.Info("Selection ignored: " + error.Message); }
                }
            }
            _selectWasDown = selectDown;
        }
        catch (Exception error)
        {
            _timer?.Stop();
            Diag.Failed("Keyboard selection", error);
        }
    }

    private void ToggleAimed()
    {
        var placer = _placer is not null && _placer.TryGetTarget(out var current) ? current : null;
        var aimed = placer?.AlignedBlock?.Target
            ?? throw new InvalidOperationException("No built block is targeted. Keep a block equipped and aim at the block to select.");
        if (aimed.Grid is not GridPlacementTarget target)
            throw new InvalidOperationException("Only built grid blocks can be selected.");
        var grid = TargetGrid(target) ?? throw new InvalidOperationException("The targeted grid is unavailable.");
        var cubeBlock = grid.CubeBlockStorage.TryGetCubeBlock(aimed.AABB.Min)
            ?? throw new InvalidOperationException("The aimed block could not be resolved.");
        var entity = cubeBlock.Entity;
        var functional = entity.TryGet<FunctionalBlockComponent>(default);
        if (functional is null)
        {
            Diag.Info("Target ignored: " + entity.DebugName + " is not a functional block.");
            return;
        }

        var selected = Volatile.Read(ref _selected);
        var existing = Array.FindIndex(selected, block => ReferenceEquals(block.Entity, entity));
        if (existing >= 0)
        {
            var next = selected.Where((_, index) => index != existing).ToArray();
            Volatile.Write(ref _selected, next);
            Diag.Info("Removed " + CurrentName(functional, entity) + "; " + next.Length + " selected.");
            return;
        }
        var added = selected.Append(new SelectedBlock(entity, functional, new WeakReference<Entity>(target.RootEntity), cubeBlock.AABB)).ToArray();
        Volatile.Write(ref _selected, added);
        Diag.Info("Selected " + CurrentName(functional, entity) + "; " + added.Length + " selected.");
    }

    private static bool ConsumeInteraction() => _instance?.Enabled != true;

    private static void OnDraw(BlockPlacerEntityComponent __instance)
    {
        var self = _instance;
        if (self is null || Volatile.Read(ref self._drawFailed) != 0) return;
        try
        {
            var ui3d = Ui3d(__instance);
            if (ui3d is null) return;
            foreach (var block in Volatile.Read(ref self._selected))
            {
                if (!block.Grid.TryGetTarget(out var grid)) continue;
                var transform = EntityTransformFunctions.GetWorldTransform(grid.Data);
                UI3DDefaultRendering.DrawSelectionOutline(ui3d,
                    CubeGridCoords.GridToWorldOriented(block.Cells, transform), SelectedColor, 3f, BehindColor, 0.75f, 0.2f);
            }
        }
        catch (Exception error)
        {
            if (Interlocked.Exchange(ref self._drawFailed, 1) == 0) Diag.Failed("Selection outline", error);
        }
    }

    private static string CurrentName(FunctionalBlockComponent component, Entity entity) =>
        string.IsNullOrWhiteSpace(component.DisplayName.RawText) ? entity.DebugName : component.DisplayName.RawText;

    public void Dispose()
    {
        Enabled = false;
        Clear();
        if (_timerRequested == 1) Dispatcher.UIThread.Post(() => _timer?.Stop());
        _harmony.UnpatchAll(HarmonyId);
        if (ReferenceEquals(_instance, this)) _instance = null;
    }

    private sealed record SelectedBlock(Entity Entity, FunctionalBlockComponent Component, WeakReference<Entity> Grid, BoundingBoxI Cells);
}
