using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Keen.Game2.Client.UI.TerminalScreen.ControlPanel;

namespace Farvane.BuildTools;

internal sealed class SelectionBridge : IDisposable
{
    private const string ViewModelName = "Keen.Game2.Client.UI.TerminalScreen.ControlPanel.ControlPanelViewModel";
    private readonly Harmony _harmony = new(Plugin.Name + ".Selection");
    private static WeakReference<object>? _current;

    internal SelectionBridge()
    {
        var viewModel = AccessTools.TypeByName(ViewModelName)
            ?? throw new NotSupportedException("The SE2 control panel is unavailable in this game build.");
        var changed = AccessTools.Method(viewModel, "HandleControlPanelSelectionChanged")
            ?? throw new NotSupportedException("The SE2 control-panel selection hook changed in this game build.");
        var capture = AccessTools.Method(typeof(SelectionBridge), nameof(Capture));
        _harmony.Patch(changed, postfix: new HarmonyMethod(capture));
    }

    private static void Capture(object __instance) => _current = new WeakReference<object>(__instance);

    internal IReadOnlyList<IControlPanelItem> Selected()
    {
        if (_current is null || !_current.TryGetTarget(out var root)) return Array.Empty<IControlPanelItem>();
        return Discover(root, 3)
            .Where(item => item.IsSelected && !string.IsNullOrWhiteSpace(item.DisplayName))
            .Distinct(ReferenceComparer<IControlPanelItem>.Instance)
            .ToList();
    }

    internal static void Apply(IEnumerable<RenamePlan> plans)
    {
        foreach (var plan in plans)
        {
            if (plan.Target is not IControlPanelItem item)
                throw new InvalidOperationException("A selected control-panel item is no longer available.");
            item.DisplayName = plan.Proposed;
            item.ValidateDisplayName();
        }
    }

    private static IEnumerable<IControlPanelItem> Discover(object root, int depth)
    {
        var seen = new HashSet<object>(ReferenceComparer<object>.Instance);
        var pending = new Queue<(object Value, int Depth)>();
        pending.Enqueue((root, 0));
        while (pending.Count > 0)
        {
            var (value, level) = pending.Dequeue();
            if (!seen.Add(value)) continue;
            if (value is IControlPanelItem item) yield return item;
            if (level >= depth || value is string) continue;
            if (value is IEnumerable sequence)
            {
                foreach (var child in sequence)
                    if (child is not null) pending.Enqueue((child, level + 1));
                continue;
            }
            var type = value.GetType();
            if (type.Namespace is null || !type.Namespace.StartsWith("Keen.Game2.Client.UI.TerminalScreen.ControlPanel", StringComparison.Ordinal)) continue;
            foreach (var field in type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
            {
                object? child;
                try { child = field.GetValue(value); } catch { continue; }
                if (child is not null) pending.Enqueue((child, level + 1));
            }
            foreach (var property in type.GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                         .Where(property => property.GetIndexParameters().Length == 0 && property.CanRead))
            {
                object? child;
                try { child = property.GetValue(value); } catch { continue; }
                if (child is not null) pending.Enqueue((child, level + 1));
            }
        }
    }

    public void Dispose() => _harmony.UnpatchAll(Plugin.Name + ".Selection");

    private sealed class ReferenceComparer<T> : IEqualityComparer<T> where T : class
    {
        internal static readonly ReferenceComparer<T> Instance = new();
        public bool Equals(T? x, T? y) => ReferenceEquals(x, y);
        public int GetHashCode(T obj) => System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(obj);
    }
}

