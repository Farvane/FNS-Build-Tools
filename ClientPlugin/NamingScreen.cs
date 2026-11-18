using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Layout;
using Avalonia.Media;
using Keen.VRage.UI.Screens;

namespace Farvane.BuildTools;

internal sealed record NamingDraft(string Location, string System, string Function, int Start, int Width, int TemplateIndex);

internal sealed class NamingScreenModel : ScreenViewModel
{
    internal Plugin Plugin { get; }
    internal NamingScreenModel(Plugin plugin) { Plugin = plugin; KeepsOtherScreensVisible = true; InitializeInputContext(); }
}

internal sealed class NamingScreen : ScreenView
{
    private readonly ComboBox _template = new() { Width = 220, ItemsSource = Templates.BuiltIn, SelectedIndex = 0 };
    private readonly ComboBox _abbreviation = new() { Width = 330, ItemsSource = Templates.Abbreviations.ToArray() };
    private readonly TextBox _location = new() { Width = 160, Watermark = "PORT" };
    private readonly TextBox _system = new() { Width = 160, Watermark = "HGR" };
    private readonly TextBox _function = new() { Width = 160, Watermark = "DOOR" };
    private readonly NumericUpDown _start = new() { Width = 150, Minimum = 0, Maximum = 999999, Value = 1 };
    private readonly NumericUpDown _width = new() { Width = 150, Minimum = 1, Maximum = 6, Value = 2 };
    private readonly TextBlock _status = new() { TextWrapping = TextWrapping.Wrap };
    private readonly StackPanel _preview = new() { Spacing = 4 };
    private IReadOnlyList<RenamePlan> _plans = Array.Empty<RenamePlan>();
    private Button? _select;

    public NamingScreen()
    {
        _template.ItemTemplate = new FuncDataTemplate<NamingTemplate>((item, _) => new TextBlock { Text = item?.Name });
        _abbreviation.ItemTemplate = new FuncDataTemplate<KeyValuePair<string, string>>((item, _) => new TextBlock { Text = item.Key + " — " + item.Value });
        _template.SelectionChanged += (_, _) => { if (_template.SelectedItem is NamingTemplate item) { _system.Text = item.System; _function.Text = item.Function; } };
        _abbreviation.SelectionChanged += (_, _) => { if (_abbreviation.SelectedItem is KeyValuePair<string, string> item) _location.Text = item.Value; };
        var refresh = new Button { Content = "Refresh preview" };
        var apply = new Button { Content = "Apply names" };
        _select = new Button { Content = "Start first-person selection" };
        var clear = new Button { Content = "Clear first-person selection" };
        var close = new Button { Content = "Close" };
        refresh.Click += (_, _) => RefreshPreview();
        apply.Click += (_, _) => Apply();
        _select.Click += (_, _) => ToggleSelection();
        clear.Click += (_, _) => { Plugin?.ClearWorldSelection(); RefreshPreview(); };
        close.Click += (_, _) => { SaveDraft(); Dispose(); };
        var panel = new StackPanel { Margin = new Thickness(30), Spacing = 14 };
        panel.Children.Add(new TextBlock { Text = "[FNS] GRIDWORX - Smart Naming", FontSize = 25 });
        panel.Children.Add(new TextBlock { Text = "Select blocks in first person or in Terminal → Control Panel, then build names from location, system, function, and sequence.", TextWrapping = TextWrapping.Wrap });
        panel.Children.Add(_status);
        panel.Children.Add(Row(_select, clear));
        panel.Children.Add(new TextBlock { Text = "Selection: equip any block, aim at a built functional block, and press F to add/remove its cyan outline. Ctrl+Alt+Shift+I opens this window in-world.", TextWrapping = TextWrapping.Wrap });
        panel.Children.Add(Labeled("Template", _template));
        panel.Children.Add(Labeled("FNS room/location abbreviation", _abbreviation));
        panel.Children.Add(Row(Labeled("Location / room", _location), Labeled("System", _system), Labeled("Function", _function)));
        panel.Children.Add(Row(Labeled("First number", _start), Labeled("Number width", _width), refresh, apply, close));
        panel.Children.Add(new TextBlock { Text = "Preview (alphabetical by current name)", FontSize = 19 });
        panel.Children.Add(_preview);
        Content = new Grid
        {
            Background = new SolidColorBrush(Color.FromArgb(160, 0, 0, 0)),
            Children = { new Border { MinWidth = 850, MaxWidth = 1450, MaxHeight = 900, Margin = new Thickness(30), Padding = new Thickness(20), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Background = new SolidColorBrush(Color.FromRgb(23, 31, 39)), Child = new ScrollViewer { Content = panel } } }
        };
    }

    private Plugin? Plugin => (DataContext as NamingScreenModel)?.Plugin;
    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        if (Plugin is { } plugin)
        {
            var draft = plugin.Draft;
            _template.SelectedIndex = Math.Clamp(draft.TemplateIndex, 0, Templates.BuiltIn.Count - 1);
            _location.Text = draft.Location;
            _system.Text = draft.System;
            _function.Text = draft.Function;
            _start.Value = draft.Start;
            _width.Value = draft.Width;
        }
        UpdateSelectionButton();
        RefreshPreview();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        SaveDraft();
        base.OnDetachedFromVisualTree(e);
    }

    private void RefreshPreview()
    {
        try
        {
            SaveDraft();
            var selected = Plugin?.Selected() ?? Array.Empty<(string Name, object Target)>();
            _plans = NamingEngine.Plan(selected, new NamingRequest(_location.Text ?? "", _system.Text ?? "", _function.Text ?? "", (int)(_start.Value ?? 1), (int)(_width.Value ?? 2)));
            _status.Text = (Plugin?.NamingStatus ?? "Unavailable") + " Selected: " + selected.Count + ".";
            _preview.Children.Clear();
            foreach (var plan in _plans.Take(100)) _preview.Children.Add(new TextBlock { Text = plan.Original + "  →  " + plan.Proposed });
            if (_plans.Count == 0) _preview.Children.Add(new TextBlock { Text = "No selected functional blocks found. Select with F in-world, or keep Terminal Control Panel open underneath this window with blocks selected." });
            if (_plans.Count > 100) _preview.Children.Add(new TextBlock { Text = "…and " + (_plans.Count - 100) + " more." });
        }
        catch (Exception error) { _plans = Array.Empty<RenamePlan>(); _status.Text = error.Message; }
    }

    private void Apply()
    {
        try { if (_plans.Count == 0) throw new InvalidOperationException("Refresh a non-empty preview first."); _status.Text = Plugin!.ApplyNames(_plans); RefreshPreview(); }
        catch (Exception error) { Diag.Failed("Batch rename", error); _status.Text = error.Message; }
    }

    private void ToggleSelection()
    {
        try
        {
            SaveDraft();
            var enabled = !Plugin!.WorldSelectionEnabled;
            Plugin.SetWorldSelection(enabled);
            UpdateSelectionButton();
            if (enabled) Dispose();
        }
        catch (Exception error) { _status.Text = error.Message; }
    }

    private void UpdateSelectionButton()
    {
        if (_select is not null) _select.Content = Plugin?.WorldSelectionEnabled == true
            ? "Stop first-person selection"
            : "Start first-person selection";
    }

    private void SaveDraft()
    {
        if (Plugin is not { } plugin) return;
        plugin.Draft = new NamingDraft(_location.Text ?? "", _system.Text ?? "", _function.Text ?? "",
            (int)(_start.Value ?? 1), (int)(_width.Value ?? 2), Math.Max(0, _template.SelectedIndex));
    }

    private static StackPanel Row(params Control[] controls) { var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Bottom }; foreach (var control in controls) row.Children.Add(control); return row; }
    private static StackPanel Labeled(string label, Control control) { var panel = new StackPanel { Spacing = 4 }; panel.Children.Add(new TextBlock { Text = label }); panel.Children.Add(control); return panel; }
}

