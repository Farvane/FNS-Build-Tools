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
            Assert.Equal("Ctrl+Alt+Shift+G", settings.HotkeyFor(HotkeyActions.ClearSymmetry));
            Assert.True(settings.HotkeysEnabled);
        }
        finally
        {
            File.Delete(file);
        }
    }

    [Fact]
    public void Missing_settings_use_defaults() =>
        Assert.Equal("Ctrl+Alt+G", Settings.Load(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".json")).HotkeyFor(HotkeyActions.SymmetryToCoG));
}
