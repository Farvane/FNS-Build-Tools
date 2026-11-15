using System;
using System.Collections.Generic;
using System.IO;
using HarmonyLib;
using Keen.Game2.Client.UI.Library;
using Keen.VRage.Core;
using Keen.VRage.Core.Plugins;
using Keen.VRage.Library.Diagnostics;
using Keen.VRage.Library.Utils;
using Keen.VRage.UI.Screens;

namespace Farvane.BuildTools;

public sealed class Plugin : IPlugin, IDisposable
{
    public const string Name = "FNSBuildTools";
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

    public Plugin()
    {
        Home = new HomeController(Symmetry);
        Measure = new MeasureController(Symmetry);
        Asymmetry = new AsymmetryController(Symmetry);
        Thrust = new ThrustController(Symmetry);
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
        Measure.Dispose();
        Asymmetry.Dispose();
        Symmetry.Dispose();
    }
}
