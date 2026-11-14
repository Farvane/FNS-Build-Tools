using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace Farvane.BuildTools;

public sealed class Settings
{
    public bool HotkeysEnabled { get; set; } = true;

    /// <summary>Home Grid refuses to land on other grids at the origin.</summary>
    public bool HomeClearanceCheck { get; set; } = true;

    /// <summary>Action name → chord. Missing actions use <see cref="HotkeyActions.Defaults"/>; "" disables one.</summary>
    public Dictionary<string, string> Hotkeys { get; set; } = new(StringComparer.Ordinal);

    public string HotkeyFor(string action) =>
        Hotkeys.TryGetValue(action, out var chord) ? chord : HotkeyActions.Defaults[action];

    public static Settings Load(string file)
    {
        if (!File.Exists(file)) return new Settings();
        var settings = JsonSerializer.Deserialize<Settings>(File.ReadAllText(file))
            ?? throw new JsonException("Expected a settings object");
        settings.Hotkeys = new Dictionary<string, string>(settings.Hotkeys ?? new(), StringComparer.Ordinal);
        return settings;
    }

    public void Save(string file)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        var temporary = file + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        File.Move(temporary, file, overwrite: true);
    }
}
