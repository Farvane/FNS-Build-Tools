using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using HarmonyLib;
using Keen.Game2.Client.UI.TerminalScreen.ControlPanel;
using Keen.VRage.Core.Game.Components;
using Keen.VRage.DCS.Components;

namespace Farvane.BuildTools;

/// <summary>
/// Tracks the blocks selected in the vanilla Control Panel. Each selection change is snapshotted and resolved to
/// server entities straight away, because the panel's ids stop working once it closes; the naming window (which
/// cannot be open at the same time) then renames the snapshot through <see cref="ServerRename"/>.
/// </summary>
internal sealed class SelectionBridge : IDisposable
{
    private const string ViewModelName = "Keen.Game2.Client.UI.TerminalScreen.ControlPanel.ControlPanelViewModel";
    private static readonly Type? ViewModelType = AccessTools.TypeByName(ViewModelName);
    private static readonly FieldInfo? ControlPanelComponent = ViewModelType is null ? null : AccessTools.Field(ViewModelType, "_controlPanel");
    private readonly Harmony _harmony = new(Plugin.Name + ".Selection");
    private static IReadOnlyList<PanelBlock> _selected = Array.Empty<PanelBlock>();
    private static int _captureFailed;

    internal SelectionBridge()
    {
        if (ViewModelType is null) throw new NotSupportedException("The SE2 control panel is unavailable in this game build.");
        if (ControlPanelComponent is null) throw new NotSupportedException("The SE2 control-panel session link changed in this game build.");
        var changed = AccessTools.Method(ViewModelType, "HandleControlPanelSelectionChanged")
            ?? throw new NotSupportedException("The SE2 control-panel selection hook changed in this game build.");
        var capture = AccessTools.Method(typeof(SelectionBridge), nameof(Capture));
        _harmony.Patch(changed, postfix: new HarmonyMethod(capture));
    }

    /// <summary>Postfix on ControlPanelViewModel.HandleControlPanelSelectionChanged: snapshot and resolve the selection.</summary>
    private static void Capture(object __instance)
    {
        try
        {
            var blocks = Discover(__instance, 4)
                .OfType<ControlPanelEntityViewModel>()
                .Where(item => item.IsSelected && !item.IsGroup && !string.IsNullOrWhiteSpace(item.DisplayName))
                .Distinct(ReferenceComparer<ControlPanelEntityViewModel>.Instance)
                .ToList();
            if (blocks.Count == 0)
            {
                Volatile.Write(ref _selected, Array.Empty<PanelBlock>());
                return;
            }
            var session = (ControlPanelComponent!.GetValue(__instance) as Component)?.GetSession();
            if (session is null) return;
            var selected = blocks.Select(item => new PanelBlock(item.DisplayName, item.Model.EntityId, session)).ToList();
            ServerRename.Resolve(session, selected);
            Volatile.Write(ref _selected, selected);
        }
        catch (Exception error)
        {
            if (Interlocked.Exchange(ref _captureFailed, 1) == 0) Diag.Failed("Control Panel selection", error);
        }
    }

    /// <summary>The blocks last selected in the Control Panel (kept after it closes, until renamed or cleared).</summary>
    internal IReadOnlyList<PanelBlock> Selected() => Volatile.Read(ref _selected);

    internal void Clear() => Volatile.Write(ref _selected, Array.Empty<PanelBlock>());

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

    public void Dispose()
    {
        _harmony.UnpatchAll(Plugin.Name + ".Selection");
        Clear();
    }

    private sealed class ReferenceComparer<T> : IEqualityComparer<T> where T : class
    {
        internal static readonly ReferenceComparer<T> Instance = new();
        public bool Equals(T? x, T? y) => ReferenceEquals(x, y);
        public int GetHashCode(T obj) => System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(obj);
    }
}

