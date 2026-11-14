using System;
using HarmonyLib;
using Keen.Game2.Client.UI.Library;
using Keen.VRage.Core;
using Keen.VRage.Core.Plugins;
using Keen.VRage.Library.Diagnostics;
using Keen.VRage.Library.Utils;

namespace Farvane.BuildTools;

public sealed class Plugin : IPlugin, IDisposable
{
    public const string Name = "FNSBuildTools";

    internal SymmetryController Symmetry { get; } = new();

    public void OpenConfigDialog()
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
            ui.CreateScreen<ToolsScreen>(new ToolsScreenModel(this), showCursor: true);
        }
        catch (Exception error)
        {
            Log.Default.WriteLine("[FNS Build Tools] Screen unavailable: " + error.GetType().Name);
        }
    }

    public void Dispose() => Symmetry.Dispose();
}
