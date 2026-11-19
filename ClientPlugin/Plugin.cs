using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using HarmonyLib;
using Keen.Game2.Client.UI.Library;
using Keen.Game2.Client.UI.TerminalScreen.ControlPanel;
using Keen.Game2.Simulation.WorldObjects.CubeBlocks;
using Keen.VRage.Core;
using Keen.VRage.Core.Plugins;
using Keen.VRage.DCS.Components;
using Keen.VRage.Library.Diagnostics;
using Keen.VRage.Library.Utils;
using Keen.VRage.UI.Screens;

namespace Farvane.BuildTools;

public sealed class Plugin : IPlugin, IDisposable
{
    public const string Name = "FNSBuildTools";
    internal const string Version = "0.9.0";
    private const string Label = "[FNS Build Tools] ";
    private static readonly int[] All = { 0, 1, 2 };

    // Pulsar injects this delegate before constructing the plugin.
#pragma warning disable CS0649
    private static Func<string, string?, string>? GetConfigPath;
#pragma warning restore CS0649

    internal SymmetryController Symmetry { get; } = new();
    internal HomeController Home { get; }
    internal MeasureController Measure { get; }
    internal AsymmetryController Asymmetry { get; }
    internal ThrustController Thrust { get; }
    internal HotkeyController? Hotkeys { get; }
    internal string? SettingsFile { get; }
    internal string? SettingsError { get; }
    private readonly SelectionBridge? _selection;
    private readonly WorldSelection? _worldSelection;
    private readonly string? _namingError;

    public Plugin()
    {
        Home = new HomeController(Symmetry);
        Measure = new MeasureController(Symmetry);
        Asymmetry = new AsymmetryController(Symmetry);
        Thrust = new ThrustController(Symmetry);
        try
        {
            // Naming was Smart Naming v0.3.0; it now lives here (it does not need the symmetry hooks).
            _selection = new SelectionBridge();
            _worldSelection = new WorldSelection(OpenNamingScreen);
        }
        catch (Exception error)
        {
            _namingError = "Naming unavailable: " + error.Message;
            Log.Default.WriteLine(Label + _namingError);
        }
        var settings = new Settings();
        try
        {
            var directory = GetConfigPath?.Invoke(Name, null)
                ?? throw new InvalidOperationException("Pulsar configuration directory is unavailable");
            SettingsFile = Path.Combine(directory, "settings.json");
            settings = Settings.Load(SettingsFile);
        }
        catch (Exception error)
        {
            // Defaults still work; only custom hotkeys are unavailable.
            SettingsError = "settings.json not loaded (" + error.GetType().Name + "); using default hotkeys";
            Log.Default.WriteLine(Label + SettingsError);
        }
        _settings = settings;
        Home.ClearanceCheck = settings.HomeClearanceCheck;
        if (!Symmetry.IsSupported) return;
        try
        {
            Hotkeys = new HotkeyController(settings, () => SymmetryController.PlacerLastActiveMs,
                new Dictionary<string, Action>(StringComparer.Ordinal)
                {
                    [HotkeyActions.SymmetryToCoG] = () => Toast(() => Symmetry.SetToCenterOfGrid(All)),
                    [HotkeyActions.ClearSymmetry] = () => Toast(() => Symmetry.Clear(All)),
                    [HotkeyActions.SymmetryToCoR] = () => Toast(() => Symmetry.SetToCenterOfRoom(corridor: false)),
                    [HotkeyActions.MeasureMark] = () => Toast(Measure.MarkAimed),
                    [HotkeyActions.MeasureClear] = () => Toast(Measure.ClearMarks),
                    [HotkeyActions.MidpointSymmetry] = () => Toast(Measure.MidpointToSymmetry),
                    [HotkeyActions.ClearWidth] = () => Toast(Measure.ClearWidthHere),
                    [HotkeyActions.CenterlineReadout] = () => Toast(Measure.ToggleReadout),
                    [HotkeyActions.FindAsymmetry] = () => Toast(Asymmetry.Toggle),
                    [HotkeyActions.NudgeAway] = () => Toast(() => Symmetry.NudgeFacing(1)),
                    [HotkeyActions.NudgeToward] = () => Toast(() => Symmetry.NudgeFacing(-1)),
                    [HotkeyActions.NudgeAwayCell] = () => Toast(() => Symmetry.NudgeFacing(2)),
                    [HotkeyActions.NudgeTowardCell] = () => Toast(() => Symmetry.NudgeFacing(-2)),
                    [HotkeyActions.OpenBuildTools] = OpenConfigDialog,
                    [HotkeyActions.HomeGrid] = () => ToastLater(report => Home.Home(useCenterOfMass: false, report)),
                    [HotkeyActions.ReturnGrid] = () => ToastLater(Home.Return),
                    [HotkeyActions.OpenRadial] = OpenRadialScreen,
                });
            // Start only once a world is running; see HotkeyController._timer for why.
            var hotkeys = Hotkeys;
            Symmetry.PlacerInitialized = () => hotkeys.Start();
        }
        catch (Exception error)
        {
            Log.Default.WriteLine(Label + "Hotkeys unavailable: " + error.GetType().Name + " " + error.Message);
        }
    }

    private Settings _settings = new();

    internal IReadOnlyDictionary<string, int?[]> PlanePresets => _settings.PlanePresets;

    internal string SavePlanePreset(string name)
    {
        name = name.Trim();
        if (name.Length == 0) throw new InvalidOperationException("Type a name for the preset");
        var planes = Symmetry.CurrentPlanes();
        if (Array.TrueForAll(planes, p => p is null)) throw new InvalidOperationException("The target grid has no symmetry planes to save");
        StoreSettings(s => s.PlanePresets[name] = planes);
        return "Saved planes as \"" + name + "\"";
    }

    internal string ApplyPlanePreset(string name)
    {
        if (!_settings.PlanePresets.TryGetValue(name, out var planes)) throw new InvalidOperationException("Preset " + name + " no longer exists");
        Symmetry.ApplyPlanes(planes);
        return "Applied planes \"" + name + "\" to the target grid";
    }

    internal string DeletePlanePreset(string name)
    {
        StoreSettings(s => s.PlanePresets.Remove(name));
        return "Deleted \"" + name + "\"";
    }

    private void StoreSettings(Action<Settings> change)
    {
        if (SettingsFile is null) throw new InvalidOperationException(SettingsError ?? "Settings are unavailable");
        // Change a fresh copy and save it first; a disk failure leaves the live settings unchanged.
        var next = Settings.Load(SettingsFile);
        next.Hotkeys = _settings.Hotkeys;
        next.HotkeysEnabled = _settings.HotkeysEnabled;
        next.HomeClearanceCheck = _settings.HomeClearanceCheck;
        next.PlanePresets = new Dictionary<string, int?[]>(_settings.PlanePresets, StringComparer.Ordinal);
        change(next);
        next.Save(SettingsFile);
        _settings = next;
    }

    public void OpenConfigDialog() => ShowScreen<ToolsScreen>(new ToolsScreenModel(this), showCursor: true, ScreenLayer.Normal);

    // ---- Smart Naming (merged from FNS Smart Naming v0.3.0) ----

    internal NamingDraft Draft { get; set; } = new("", "HGR", "DOOR", 1, 2, 0);

    internal string NamingStatus => _namingError ?? (_worldSelection?.Enabled == true
        ? "First-person selection active — equip a block, aim at a functional block, and press F to toggle it."
        : "Ready — use first-person selection or the vanilla Control Panel.");

    internal bool WorldSelectionEnabled => _worldSelection?.Enabled == true;

    internal void SetWorldSelection(bool enabled) =>
        (_worldSelection ?? throw new InvalidOperationException(NamingStatus)).SetEnabled(enabled);

    internal void ClearWorldSelection() => _worldSelection?.Clear();

    /// <summary>Starts or stops first-person selection and shows the result as a toast.</summary>
    internal void ToggleNamingSelection() => Toast(() =>
    {
        SetWorldSelection(!WorldSelectionEnabled);
        return NamingStatus;
    });

    internal System.Collections.Generic.IReadOnlyList<(string Name, object Target)> Selected()
    {
        var world = _worldSelection?.Snapshot() ?? Array.Empty<(string, object)>();
        if (world.Count > 0) return world;
        return (_selection?.Selected() ?? Array.Empty<IControlPanelItem>())
            .Select(item => (item.DisplayName, (object)item)).ToList();
    }

    internal string ApplyNames(System.Collections.Generic.IReadOnlyList<RenamePlan> plans)
    {
        if (_selection is null) throw new InvalidOperationException(NamingStatus);
        // First-person selections are renamed on the in-process server (the client may not set the sync property);
        // Control Panel selections go through the panel's own setter. ServerRename validates every block first.
        var world = plans.Where(plan => plan.Target is Entity).Select(plan => ((Entity)plan.Target, plan.Proposed)).ToList();
        var panel = plans.Where(plan => plan.Target is IControlPanelItem).ToList();
        if (world.Count + panel.Count != plans.Count) throw new InvalidOperationException("A selected block is no longer available.");
        var status = world.Count > 0 ? ServerRename.Apply(world) : "Renamed " + panel.Count + " block(s).";
        SelectionBridge.Apply(panel);
        _worldSelection?.SetEnabled(false);
        _worldSelection?.Clear();
        if (panel.Count > 0) Diag.Info("Renamed " + panel.Count + " Control Panel block(s).");
        return status;
    }

    /// <summary>Runs an action that returns its result text, showing that text as a toast.</summary>
    internal void RunSync(Func<Plugin, string> action) => Toast(() => action(this));

    /// <summary>Runs an action whose result arrives later, showing it as a toast when it does.</summary>
    internal void RunLater(Action<Plugin, Action<string>> start) => ToastLater(report => start(this, report));

    /// <summary>Opens the radial menu (the Radial hotkey, while a block is equipped).</summary>
    public void OpenRadialScreen() => ShowScreen<RadialScreen>(new RadialScreenModel(this), showCursor: true, ScreenLayer.Normal);

    /// <summary>Opens the naming window (Ctrl+Alt+Shift+I in-world, or from the Tools screen).</summary>
    public void OpenNamingScreen() => ShowScreen<NamingScreen>(new NamingScreenModel(this), showCursor: true, ScreenLayer.Normal);

    /// <summary>Runs an action and shows its result, or the reason it could not run.</summary>
    private void Toast(Func<string> action)
    {
        string message;
        try
        {
            message = action();
        }
        catch (InvalidOperationException error)
        {
            message = error.Message;
        }
        ShowScreen<ToastScreen>(new ToastScreenModel(message), showCursor: false, ScreenLayer.Top);
    }

    /// <summary>Starts an action whose outcome arrives later through its report callback; shows it as a toast.</summary>
    private void ToastLater(Action<Action<string>> start)
    {
        Action<string> show = message => ShowScreen<ToastScreen>(new ToastScreenModel(message), showCursor: false, ScreenLayer.Top);
        try
        {
            start(show);
        }
        catch (InvalidOperationException error)
        {
            show(error.Message);
        }
        catch (Exception error)
        {
            Log.Default.WriteLine(Label + "Action failed: " + error);
            show("Failed (" + error.GetType().Name + "). Check the SE2 log");
        }
    }

    private static void ShowScreen<TView>(ScreenViewModel model, bool showCursor, ScreenLayer layer) where TView : ScreenView, new()
    {
        try
        {
            var engine = Singleton<VRageCore>.Instance?.Engine;
            var appType = AccessTools.TypeByName("Keen.Game2.GameAppComponent");
            if (engine is null || appType is null) return;
            var get = AccessTools.FirstMethod(engine.GetType(), method => method.Name == "Get" &&
                method.IsGenericMethodDefinition && method.GetGenericArguments().Length == 1 &&
                method.GetParameters().Length == 1)
                ?? throw new InvalidOperationException("Engine component lookup is unavailable");
            var app = get.MakeGenericMethod(appType).Invoke(engine, new object[] { default(StringId) });
            var ui = AccessTools.Method(appType, "GetSharedUI")?.Invoke(app, null) as SharedUIComponent;
            if (ui is null) return;
            ui.CreateScreen<TView>(model, showCursor, layer);
        }
        catch (Exception error)
        {
            Log.Default.WriteLine(Label + "Screen unavailable: " + error.GetType().Name);
        }
    }

    public void Dispose()
    {
        Hotkeys?.Dispose();
        _worldSelection?.Dispose();
        _selection?.Dispose();
        Measure.Dispose();
        Asymmetry.Dispose();
        Symmetry.Dispose();
    }
}
