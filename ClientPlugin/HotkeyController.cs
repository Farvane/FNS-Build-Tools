using System;
using System.Collections.Generic;
using System.Reflection;
using Avalonia.Threading;
using Keen.VRage.Core.Input;
using Keen.VRage.Input;
using Keen.VRage.Library.Diagnostics;
using Keen.VRage.Library.Utils;

namespace Farvane.BuildTools;

/// <summary>
/// Polls the keyboard on the UI thread — the same thread that runs the settings
/// screen's buttons — and fires actions on the press edge of a chord. Chords only
/// fire while the block placer has run within the last few frames, so they are
/// ignored in menus, chat and when no block is equipped.
/// </summary>
internal sealed class HotkeyController : IDisposable
{
    private const string Label = "[FNS Build Tools] ";
    private const long PlacerActiveWindowMs = 250;

    private readonly Func<bool> _placerActive;
    private readonly Dictionary<string, Action> _actions;
    private readonly List<Binding> _bindings = new();
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(30) };
    private bool _started;

    public HotkeyController(Settings settings, Func<long> placerLastActiveMs, Dictionary<string, Action> actions)
    {
        _actions = actions;
        _placerActive = () => Environment.TickCount64 - placerLastActiveMs() < PlacerActiveWindowMs;
        var problems = new List<string>();
        if (!settings.HotkeysEnabled)
        {
            Status = "Hotkeys disabled in settings.json";
            return;
        }
        foreach (var action in HotkeyActions.Defaults.Keys)
        {
            var chord = settings.HotkeyFor(action);
            if (chord.Length == 0) continue;
            if (!Hotkey.TryParse(chord, out var hotkey, out var error)) problems.Add(action + ": " + error);
            else if (!TryResolve(hotkey.Key, out var key)) problems.Add(action + ": unknown key '" + hotkey.Key + "'");
            else _bindings.Add(new Binding(action, hotkey, key));
        }
        Status = problems.Count == 0 ? "Hotkeys ready" : "Some hotkeys disabled — " + string.Join("; ", problems);
        foreach (var problem in problems) Log.Default.WriteLine(Label + "Hotkey disabled: " + problem);
        _timer.Tick += (_, _) => Poll();
    }

    public string Status { get; }

    public IEnumerable<string> Describe()
    {
        foreach (var binding in _bindings) yield return binding.Hotkey + "  " + binding.Action;
    }

    /// <summary>Starts polling. Safe to call from any thread, and more than once.</summary>
    public void Start()
    {
        if (_bindings.Count == 0) return;
        Dispatcher.UIThread.Post(() =>
        {
            if (_started) return;
            _started = true;
            _timer.Start();
        });
    }

    private void Poll()
    {
        try
        {
            var keyboard = Singleton<InputDeviceManager>.Instance?.Keyboard;
            if (keyboard is null) return;
            var active = _placerActive();
            bool ctrl = KeyboardInputs.Control.IsActive(keyboard);
            bool alt = KeyboardInputs.Alt.IsActive(keyboard);
            bool shift = KeyboardInputs.Shift.IsActive(keyboard);
            foreach (var binding in _bindings)
            {
                var down = binding.Key.IsActive(keyboard) && ctrl == binding.Hotkey.Ctrl &&
                    alt == binding.Hotkey.Alt && shift == binding.Hotkey.Shift;
                // Fire on the press edge only, and only while building.
                if (down && !binding.WasDown && active) Run(binding.Action);
                binding.WasDown = down;
            }
        }
        catch (Exception error)
        {
            _timer.Stop();
            Log.Default.WriteLine(Label + "Hotkeys stopped: " + error.GetType().Name + " " + error.Message);
        }
    }

    private void Run(string action)
    {
        try
        {
            _actions[action]();
        }
        catch (Exception error)
        {
            Log.Default.WriteLine(Label + action + " failed: " + error.GetType().Name + " " + error.Message);
        }
    }

    // KeyboardInputs exposes each key as a public static DigitalInput field (A, G, F5, ...).
    private static bool TryResolve(string name, out DigitalInput key)
    {
        key = default;
        var field = typeof(KeyboardInputs).GetField(name, BindingFlags.Public | BindingFlags.Static | BindingFlags.IgnoreCase);
        if (field?.FieldType != typeof(DigitalInput) || field.GetValue(null) is not DigitalInput value) return false;
        key = value;
        return true;
    }

    public void Dispose()
    {
        Dispatcher.UIThread.Post(() => _timer.Stop());
    }

    private sealed class Binding
    {
        public Binding(string action, Hotkey hotkey, DigitalInput key)
        {
            Action = action;
            Hotkey = hotkey;
            Key = key;
        }

        public string Action { get; }
        public Hotkey Hotkey { get; }
        public DigitalInput Key { get; }
        public bool WasDown { get; set; }
    }
}
