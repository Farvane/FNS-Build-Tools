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
    internal HotkeyController? Hotkeys { get; }
    internal string? SettingsFile { get; }
    internal string? SettingsError { get; }

    public Plugin()
    {
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
        if (!Symmetry.IsSupported) return;
        try
        {
            Hotkeys = new HotkeyController(settings, () => SymmetryController.PlacerLastActiveMs,
                new Dictionary<string, Action>(StringComparer.Ordinal)
                {
                    [HotkeyActions.SymmetryToCoG] = () => Toast(() => Symmetry.SetToCenterOfGrid(All)),
                    [HotkeyActions.ClearSymmetry] = () => Toast(() => Symmetry.Clear(All)),
                    [HotkeyActions.OpenBuildTools] = OpenConfigDialog,
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
        Symmetry.Dispose();
    }
}
