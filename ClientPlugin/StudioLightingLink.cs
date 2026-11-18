using System;
using System.Reflection;
using HarmonyLib;

namespace Farvane.BuildTools;

/// <summary>
/// Optional link to FNS Studio Lighting. Found by type name at runtime, so this plugin
/// has no assembly reference to it and works when it is not installed.
/// </summary>
internal static class StudioLightingLink
{
    private const string TypeName = "Farvane.StudioLighting.StudioLightingBridge";

    private static Type? BridgeType => AccessTools.TypeByName(TypeName);

    internal static bool Installed =>
        BridgeType is { } type && AccessTools.Property(type, "Enabled") is not null && AccessTools.Method(type, "Toggle") is not null;

    internal static bool Enabled =>
        (bool)(AccessTools.Property(BridgeType!, "Enabled")!.GetValue(null) ?? false);

    /// <summary>Toggles studio lighting; returns its status text. Failures become InvalidOperationException for the UI.</summary>
    internal static string Toggle()
    {
        if (!Installed) throw new InvalidOperationException("FNS Studio Lighting is not installed.");
        try
        {
            return (string?)AccessTools.Method(BridgeType!, "Toggle")!.Invoke(null, null) ?? "Studio lighting toggled.";
        }
        catch (TargetInvocationException error) when (error.InnerException is InvalidOperationException inner)
        {
            throw new InvalidOperationException(inner.Message);
        }
        catch (TargetInvocationException error)
        {
            throw new InvalidOperationException("Studio lighting failed (" + (error.InnerException?.GetType().Name ?? "unknown") + ")");
        }
    }
}
