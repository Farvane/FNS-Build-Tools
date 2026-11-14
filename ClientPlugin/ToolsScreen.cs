using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Keen.VRage.UI.Screens;

namespace Farvane.BuildTools;

internal sealed class ToolsScreenModel : ScreenViewModel
{
    internal Plugin Plugin { get; }
    public ToolsScreenModel(Plugin plugin)
    {
        Plugin = plugin;
        KeepsOtherScreensVisible = false;
        AllowsInputBelowUI = false;
        AllowsInputFromLowerScreens = false;
        InitializeInputContext();
    }
}

// Uses the same ScreenView/CreateScreen extension points as the SE2 plugin template.
internal sealed class ToolsScreen : ScreenView
{
    private static readonly int[] All = { 0, 1, 2 };
    private readonly TextBlock _status = new() { TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock _feedback = new() { TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock _hotkeys = new() { TextWrapping = TextWrapping.Wrap, Opacity = 0.85 };
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(1) };

    public ToolsScreen()
    {
        var cog = new WrapPanel();
        cog.Children.Add(Action("All axes → CoG", s => s.SetToCenterOfGrid(All)));
        cog.Children.Add(Action("X → CoG", s => s.SetToCenterOfGrid(0)));
        cog.Children.Add(Action("Y → CoG", s => s.SetToCenterOfGrid(1)));
        cog.Children.Add(Action("Z → CoG", s => s.SetToCenterOfGrid(2)));
        var clear = new WrapPanel();
        clear.Children.Add(Action("Clear all planes", s => s.Clear(All)));
        var close = new Button { Content = "Close", HorizontalAlignment = HorizontalAlignment.Left };
        close.Click += (_, _) => Dispose();

        var panel = new StackPanel { Margin = new Thickness(30), Spacing = 18 };
        panel.Children.Add(new TextBlock { Text = "[FNS] GRIDWORX - Build Tools", FontSize = 25, TextWrapping = TextWrapping.Wrap });
        panel.Children.Add(new TextBlock { Text = "v0.1.2 preview • SE2 " + SymmetryController.SupportedBuild + " • Symmetry to Center of Grid", TextWrapping = TextWrapping.Wrap });
        panel.Children.Add(new TextBlock
        {
            Text = "With a block equipped, look at a grid, then open this screen. CoG is the center of the grid's block bounding box.",
            TextWrapping = TextWrapping.Wrap
        });
        panel.Children.Add(_hotkeys);
        panel.Children.Add(_status);
        panel.Children.Add(cog);
        panel.Children.Add(clear);
        panel.Children.Add(_feedback);
        panel.Children.Add(close);
        var border = new Border
        {
            Width = 760,
            MaxHeight = 760,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Background = new SolidColorBrush(Color.FromRgb(23, 31, 39)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(66, 82, 96)),
            BorderThickness = new Thickness(1),
            Child = new ScrollViewer { Content = panel }
        };
        var overlay = new Grid { Background = new SolidColorBrush(Color.FromArgb(160, 0, 0, 0)) };
        overlay.Children.Add(border);
        HorizontalAlignment = HorizontalAlignment.Stretch;
        VerticalAlignment = VerticalAlignment.Stretch;
        Background = Brushes.Transparent;
        Content = overlay;
        _timer.Tick += (_, _) => Refresh();
    }

    private Button Action(string label, Func<SymmetryController, string> action)
    {
        var button = new Button { Content = label, Margin = new Thickness(0, 0, 10, 10) };
        button.Click += (_, _) =>
        {
            if (DataContext is not ToolsScreenModel model) return;
            try
            {
                _feedback.Text = action(model.Plugin.Symmetry);
            }
            catch (Exception error)
            {
                _feedback.Text = error is InvalidOperationException
                    ? error.Message
                    : "Failed (" + error.GetType().Name + "). Check the SE2 log.";
            }
            Refresh();
        };
        return button;
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        if (DataContext is ToolsScreenModel model)
        {
            var hotkeys = model.Plugin.Hotkeys;
            var lines = hotkeys is null ? "Hotkeys unavailable" : hotkeys.Status + " (while a block is equipped)\n  " + string.Join("\n  ", hotkeys.Describe());
            if (model.Plugin.SettingsError is { } error) lines += "\n" + error;
            else if (model.Plugin.SettingsFile is { } file) lines += "\nCustomize in " + file;
            _hotkeys.Text = lines;
        }
        Refresh();
        _timer.Start();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        _timer.Stop();
        base.OnDetachedFromVisualTree(e);
    }

    private void Refresh()
    {
        if (DataContext is not ToolsScreenModel model) return;
        try
        {
            _status.Text = model.Plugin.Symmetry.Status;
        }
        catch (Exception error)
        {
            _status.Text = "Status unavailable (" + error.GetType().Name + ")";
        }
    }
}
