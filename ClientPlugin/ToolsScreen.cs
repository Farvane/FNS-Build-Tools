using System;
using System.Linq;
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
    private readonly TextBlock _info = new() { TextWrapping = TextWrapping.Wrap };
    private readonly StackPanel _presets = new() { Spacing = 6 };
    private readonly TextBox _presetName = new() { Width = 260, Watermark = "Preset name" };
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(1) };

    public ToolsScreen()
    {
        var cog = new WrapPanel();
        cog.Children.Add(Action("All axes → CoG", s => s.SetToCenterOfGrid(All)));
        cog.Children.Add(Action("X → CoG", s => s.SetToCenterOfGrid(0)));
        cog.Children.Add(Action("Y → CoG", s => s.SetToCenterOfGrid(1)));
        cog.Children.Add(Action("Z → CoG", s => s.SetToCenterOfGrid(2)));
        var cor = new WrapPanel();
        cor.Children.Add(Action("All axes → CoR", s => s.SetToCenterOfRoom(corridor: false)));
        cor.Children.Add(Action("Corridor → CoR", s => s.SetToCenterOfRoom(corridor: true)));
        var measure = new WrapPanel();
        measure.Children.Add(PluginAction("Mark A / B", p => p.Measure.MarkAimed()));
        measure.Children.Add(PluginAction("Midpoint → symmetry", p => p.Measure.MidpointToSymmetry()));
        measure.Children.Add(PluginAction("Clear width here", p => p.Measure.ClearWidthHere()));
        measure.Children.Add(PluginAction("Centerline readout", p => p.Measure.ToggleReadout()));
        measure.Children.Add(PluginAction("Clear marks", p => p.Measure.ClearMarks()));
        measure.Children.Add(PluginAction("Asymmetry finder (toggle)", p => p.Asymmetry.Toggle()));
        var clear = new WrapPanel();
        clear.Children.Add(Action("Clear all planes", s => s.Clear(All)));
        var home = new WrapPanel();
        home.Children.Add(LaterAction("Home grid (CoG)", (p, report) => p.Home.Home(useCenterOfMass: false, report)));
        home.Children.Add(LaterAction("Home grid (CoM)", (p, report) => p.Home.Home(useCenterOfMass: true, report)));
        home.Children.Add(LaterAction("Return grid", (p, report) => p.Home.Return(report)));
        var nudge = new WrapPanel();
        nudge.Children.Add(PluginAction("Facing plane −1 cell", p => p.Symmetry.NudgeFacing(-2)));
        nudge.Children.Add(PluginAction("−½", p => p.Symmetry.NudgeFacing(-1)));
        nudge.Children.Add(PluginAction("+½", p => p.Symmetry.NudgeFacing(1)));
        nudge.Children.Add(PluginAction("+1 cell", p => p.Symmetry.NudgeFacing(2)));
        var savePreset = new WrapPanel();
        savePreset.Children.Add(_presetName);
        savePreset.Children.Add(PluginAction("Save current planes", p =>
        {
            var message = p.SavePlanePreset(_presetName.Text ?? "");
            RebuildPresets(p);
            return message;
        }));
        var views = new WrapPanel();
        for (var side = 0; side < HomeController.ViewNames.Length; side++)
        {
            var captured = side;
            views.Children.Add(LaterAction(HomeController.ViewNames[side], (p, report) => p.Home.View(captured, report)));
        }
        views.Children.Add(LaterAction("Back from view", (p, report) => p.Home.ReturnFromView(report)));
        var thrust = new WrapPanel();
        thrust.Children.Add(PluginAction("Thrust balance", p => p.Thrust.Report()));
        var close = new Button { Content = "Close", HorizontalAlignment = HorizontalAlignment.Left };
        close.Click += (_, _) => Dispose();

        var panel = new StackPanel { Margin = new Thickness(30), Spacing = 18 };
        panel.Children.Add(new TextBlock { Text = "[FNS] GRIDWORX - Build Tools", FontSize = 25, TextWrapping = TextWrapping.Wrap });
        panel.Children.Add(new TextBlock { Text = "v0.7.0 preview • SE2 " + SymmetryController.SupportedBuild + " • Symmetry CoG/CoR • Home Grid • Measure • Asymmetry • Nudge/Presets • Views • Thrust", TextWrapping = TextWrapping.Wrap });
        panel.Children.Add(new TextBlock
        {
            Text = "With a block equipped, look at a grid, then open this screen. CoG is the center of the grid's block bounding box.",
            TextWrapping = TextWrapping.Wrap
        });
        panel.Children.Add(_hotkeys);
        panel.Children.Add(_status);
        panel.Children.Add(_info);
        panel.Children.Add(cog);
        panel.Children.Add(new TextBlock
        {
            Text = "Center of Room: stand inside an enclosed room of the target grid. Corridor sets only the axes across the room's longest direction.",
            TextWrapping = TextWrapping.Wrap
        });
        panel.Children.Add(cor);
        panel.Children.Add(clear);
        panel.Children.Add(new TextBlock
        {
            Text = "Measure: look at a block and mark it A, then B (hotkeys work best: the screen hides your aim). Midpoint puts planes halfway between them. Clear width shows free space along each axis where you stand.",
            TextWrapping = TextWrapping.Wrap
        });
        panel.Children.Add(measure);
        panel.Children.Add(new TextBlock
        {
            Text = "Home grid: move the grid (and anything on its rotors, pistons, hinges and connectors) so its center is at world 0,0,0, square to the world axes. You move with it. A moving grid (or you) is stopped first. Refuses if another grid is in the way. Needs admin rights (fine in single-player).",
            TextWrapping = TextWrapping.Wrap
        });
        panel.Children.Add(home);
        panel.Children.Add(new TextBlock
        {
            Text = "Nudge: moves the symmetry plane of the axis you are looking along (+ is away from you). Hotkeys Ctrl+Alt+= / Ctrl+Alt+- (½ cell), add Shift for a whole cell.",
            TextWrapping = TextWrapping.Wrap
        });
        panel.Children.Add(nudge);
        panel.Children.Add(new TextBlock { Text = "Plane presets (saved in settings.json, applied to the target grid):", TextWrapping = TextWrapping.Wrap });
        panel.Children.Add(savePreset);
        panel.Children.Add(_presets);
        panel.Children.Add(new TextBlock
        {
            Text = "Axis views: moves you to look at the target grid's center from one side, square to its axes (admin teleport). Back returns you to where you started.",
            TextWrapping = TextWrapping.Wrap
        });
        panel.Children.Add(views);
        panel.Children.Add(thrust);
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

    private void RebuildPresets(Plugin plugin)
    {
        _presets.Children.Clear();
        foreach (var name in plugin.PlanePresets.Keys.OrderBy(n => n, StringComparer.OrdinalIgnoreCase))
        {
            var planes = plugin.PlanePresets[name];
            var row = new WrapPanel();
            row.Children.Add(new TextBlock
            {
                Text = name + "  (" + string.Join(" · ", Enumerable.Range(0, 3).Select(a => planes.Length > a && planes[a] is { } d
                    ? GridMath.DescribePlane("XYZ"[a], d) : "XYZ"[a] + ": off")) + ")",
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 12, 0)
            });
            var captured = name;
            row.Children.Add(PluginAction("Apply", p => p.ApplyPlanePreset(captured)));
            row.Children.Add(PluginAction("Delete", p =>
            {
                var message = p.DeletePlanePreset(captured);
                RebuildPresets(p);
                return message;
            }));
            _presets.Children.Add(row);
        }
        if (_presets.Children.Count == 0) _presets.Children.Add(new TextBlock { Text = "No presets yet.", Opacity = 0.7 });
    }

    private Button PluginAction(string label, Func<Plugin, string> action)
    {
        var button = new Button { Content = label, Margin = new Thickness(0, 0, 10, 10) };
        button.Click += (_, _) =>
        {
            if (DataContext is not ToolsScreenModel model) return;
            try
            {
                _feedback.Text = action(model.Plugin);
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

    private Button LaterAction(string label, Action<Plugin, Action<string>> start)
    {
        var button = new Button { Content = label, Margin = new Thickness(0, 0, 10, 10) };
        button.Click += (_, _) =>
        {
            if (DataContext is not ToolsScreenModel model) return;
            button.IsEnabled = false;
            _feedback.Text = "Working…";
            Action<string> done = message =>
            {
                button.IsEnabled = true;
                _feedback.Text = message;
                Refresh();
            };
            try
            {
                start(model.Plugin, done);
            }
            catch (Exception error)
            {
                done(error is InvalidOperationException
                    ? error.Message
                    : "Failed (" + error.GetType().Name + "). Check the SE2 log.");
            }
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
            RebuildPresets(model.Plugin);
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
            _info.Text = model.Plugin.Symmetry.Info();
            _status.Text = model.Plugin.Symmetry.Status +
                (model.Plugin.Asymmetry.Summary is { } asym ? Environment.NewLine + "Asymmetry finder (live): " + asym : "");
        }
        catch (Exception error)
        {
            _status.Text = "Status unavailable (" + error.GetType().Name + ")";
        }
    }
}
