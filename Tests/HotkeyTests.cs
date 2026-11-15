using Farvane.BuildTools;
using Xunit;

public class HotkeyTests
{
    [Theory]
    [InlineData("Ctrl+Alt+G", true, true, false, "G")]
    [InlineData(" ctrl + alt + shift + g ", true, true, true, "G")]
    [InlineData("Control+F5", true, false, false, "F5")]
    [InlineData("Alt+B", false, true, false, "B")]
    public void Parses_chords(string text, bool ctrl, bool alt, bool shift, string key)
    {
        Assert.True(Hotkey.TryParse(text, out var hotkey, out var error), error);
        Assert.Equal(new Hotkey(ctrl, alt, shift, key), hotkey);
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("G")]            // no Ctrl/Alt: would fire while typing
    [InlineData("Shift+G")]
    [InlineData("Ctrl+Alt")]     // no key
    [InlineData("Ctrl+G+H")]     // two keys
    [InlineData("Ctrl++G")]
    public void Rejects_invalid_chords(string? text)
    {
        Assert.False(Hotkey.TryParse(text, out _, out var error));
        Assert.NotEmpty(error);
    }

    [Fact]
    public void Round_trips_to_canonical_text()
    {
        Assert.True(Hotkey.TryParse("shift+g+alt+ctrl", out var hotkey, out _));
        Assert.Equal("Ctrl+Alt+Shift+G", hotkey.ToString());
    }

    [Fact]
    public void Defaults_are_valid_and_distinct()
    {
        var seen = new HashSet<Hotkey>();
        foreach (var chord in HotkeyActions.Defaults.Values)
        {
            Assert.True(Hotkey.TryParse(chord, out var hotkey, out var error), error);
            Assert.True(seen.Add(hotkey), chord + " is assigned twice");
        }
    }

    [Fact]
    public void Settings_override_and_disable_hotkeys()
    {
        var file = Path.Combine(Path.GetTempPath(), "fns-build-tools-" + Guid.NewGuid() + ".json");
        try
        {
            File.WriteAllText(file, "{\"Hotkeys\":{\"SymmetryToCoG\":\"Ctrl+Alt+F6\",\"OpenBuildTools\":\"\"}}");
            var settings = Settings.Load(file);
            Assert.Equal("Ctrl+Alt+F6", settings.HotkeyFor(HotkeyActions.SymmetryToCoG));
            Assert.Equal("", settings.HotkeyFor(HotkeyActions.OpenBuildTools));
            Assert.Equal("Ctrl+Alt+Shift+N", settings.HotkeyFor(HotkeyActions.ClearSymmetry));
            Assert.True(settings.HotkeysEnabled);
        }
        finally
        {
            File.Delete(file);
        }
    }

    [Fact]
    public void Missing_settings_use_defaults() =>
        Assert.Equal("Ctrl+Alt+N", Settings.Load(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".json")).HotkeyFor(HotkeyActions.SymmetryToCoG));

    // Snapshot of keys bound by vanilla gameplay inputs (Keyboard::<Key> in SE2 4.0.0.3169
    // GameData .def files, excluding debug/editor/gizmo inputs). Vanilla fires these even
    // with Ctrl+Alt held, so plugin defaults must avoid them.
    private static readonly HashSet<string> VanillaBound = new(StringComparer.OrdinalIgnoreCase)
    {
        "A","B","C","D","E","F","G","H","J","K","L","M","P","Q","R","S","T","U","V","W","X","Y","Z",
        "D0","D1","D2","D3","D4","D5","D6","D7","D8","D9","F1","F2","F4","F5","F6","F7","F8","F9","F11","F12",
        "Back","Capital","Delete","Down","End","Enter","Escape","Home","Left","Right","Up","PageDown","PageUp",
        "Pause","Space","Tab","OemBacktick","OemCloseBrackets","OemComma","OemOpenBrackets","OemPeriod",
    };

    [Fact]
    public void Defaults_avoid_vanilla_bound_keys()
    {
        foreach (var chord in HotkeyActions.Defaults.Values)
        {
            Assert.True(Hotkey.TryParse(chord, out var hotkey, out _));
            Assert.DoesNotContain(hotkey.Key, VanillaBound);
        }
    }

    [Fact]
    public void Clearance_check_defaults_on_and_can_be_disabled()
    {
        Assert.True(new Settings().HomeClearanceCheck);
        var file = Path.Combine(Path.GetTempPath(), "fns-build-tools-" + Guid.NewGuid() + ".json");
        try
        {
            File.WriteAllText(file, "{\"HomeClearanceCheck\":false}");
            Assert.False(Settings.Load(file).HomeClearanceCheck);
        }
        finally
        {
            File.Delete(file);
        }
    }

    [Fact]
    public void Plane_presets_round_trip_including_off_axes()
    {
        var file = Path.Combine(Path.GetTempPath(), "fns-build-tools-" + Guid.NewGuid() + ".json");
        try
        {
            var settings = new Settings();
            settings.PlanePresets["Hull"] = new int?[] { 19, null, -7 };
            settings.Save(file);
            var loaded = Settings.Load(file);
            Assert.Equal(new int?[] { 19, null, -7 }, loaded.PlanePresets["Hull"]);
            Assert.Empty(Settings.Load(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".json")).PlanePresets);
        }
        finally
        {
            File.Delete(file);
        }
    }
}
