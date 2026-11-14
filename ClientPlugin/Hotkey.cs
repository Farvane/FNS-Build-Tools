using System;
using System.Collections.Generic;

namespace Farvane.BuildTools;

/// <summary>
/// A key chord such as "Ctrl+Alt+G". Modifiers must match exactly, so
/// Ctrl+Alt+G and Ctrl+Alt+Shift+G are different hotkeys.
/// </summary>
public sealed record Hotkey(bool Ctrl, bool Alt, bool Shift, string Key)
{
    public static bool TryParse(string? text, out Hotkey hotkey, out string error)
    {
        hotkey = null!;
        error = string.Empty;
        if (string.IsNullOrWhiteSpace(text))
        {
            error = "Hotkey is empty";
            return false;
        }
        bool ctrl = false, alt = false, shift = false;
        string? key = null;
        foreach (var raw in text.Split('+'))
        {
            var part = raw.Trim();
            switch (part.ToLowerInvariant())
            {
                case "ctrl" or "control": ctrl = true; break;
                case "alt": alt = true; break;
                case "shift": shift = true; break;
                case "":
                    error = "Hotkey '" + text + "' has an empty part";
                    return false;
                default:
                    if (key is not null)
                    {
                        error = "Hotkey '" + text + "' names more than one key";
                        return false;
                    }
                    key = part.ToUpperInvariant();
                    break;
            }
        }
        if (key is null)
        {
            error = "Hotkey '" + text + "' has no key, only modifiers";
            return false;
        }
        if (!ctrl && !alt)
        {
            error = "Hotkey '" + text + "' needs Ctrl or Alt so it does not clash with typing or vanilla binds";
            return false;
        }
        hotkey = new Hotkey(ctrl, alt, shift, key);
        return true;
    }

    public override string ToString()
    {
        var parts = new List<string>(4);
        if (Ctrl) parts.Add("Ctrl");
        if (Alt) parts.Add("Alt");
        if (Shift) parts.Add("Shift");
        parts.Add(Key);
        return string.Join("+", parts);
    }
}

/// <summary>Hotkey actions. Names are the keys used in settings.json.</summary>
public static class HotkeyActions
{
    public const string SymmetryToCoG = "SymmetryToCoG";
    public const string ClearSymmetry = "ClearSymmetry";
    public const string OpenBuildTools = "OpenBuildTools";
    public const string HomeGrid = "HomeGrid";
    public const string ReturnGrid = "ReturnGrid";
    public const string SymmetryToCoR = "SymmetryToCoR";

    public static readonly IReadOnlyDictionary<string, string> Defaults = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        // Vanilla matches its key binds regardless of modifiers, so defaults use keys
        // no vanilla gameplay input binds (SE2 4.0.0.3169: I, N, O are free; G opens
        // the block picker). O = Origin.
        [SymmetryToCoG] = "Ctrl+Alt+N",
        [ClearSymmetry] = "Ctrl+Alt+Shift+N",
        [OpenBuildTools] = "Ctrl+Alt+I",
        [HomeGrid] = "Ctrl+Alt+O",
        [ReturnGrid] = "Ctrl+Alt+Shift+O",
        [SymmetryToCoR] = "Ctrl+Shift+N",
    };
}
