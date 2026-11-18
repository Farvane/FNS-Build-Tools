using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Keen.VRage.UI.Screens;

namespace Farvane.BuildTools;

/// <summary>One action on the radial menu. Run it on the plugin; it reports its own result as a toast.</summary>
internal sealed record RadialItem(string Label, Action<Plugin> Run);

/// <summary>A top-level radial sector. Items are built per open, so optional items can depend on what is installed.</summary>
internal sealed record RadialSector(string Label, Func<Plugin, IReadOnlyList<RadialItem>> Items);

internal sealed class RadialScreenModel : ScreenViewModel
{
    internal Plugin Plugin { get; }
    public RadialScreenModel(Plugin plugin)
    {
        Plugin = plugin;
        KeepsOtherScreensVisible = false;
        AllowsInputBelowUI = false;
        AllowsInputFromLowerScreens = false;
        InitializeInputContext();
    }
}

/// <summary>
/// Radial menu as a click-driven screen: seven sectors on the outer ring, and the chosen
/// sector's items on an inner ring. Click the centre to go back, or to close from the top ring.
/// The screen closes before an action runs, so the game (aim, F key) is free again.
/// </summary>
internal sealed class RadialScreen : ScreenView
{
    private const double Size = 560;
    private const double Center = Size / 2;
    private const double OuterRadius = 210;
    private const double InnerRadius = 210;
    private const double ButtonWidth = 150;
    private const double ButtonHeight = 40;

    private static readonly RadialSector[] Sectors =
    {
        new("Symmetry", _ => new RadialItem[]
        {
            new("All axes → CoG", p => p.RunSync(x => x.Symmetry.SetToCenterOfGrid(new[] { 0, 1, 2 }))),
            new("All axes → CoR", p => p.RunSync(x => x.Symmetry.SetToCenterOfRoom(corridor: false))),
            new("Corridor → CoR", p => p.RunSync(x => x.Symmetry.SetToCenterOfRoom(corridor: true))),
            new("Clear planes", p => p.RunSync(x => x.Symmetry.Clear(new[] { 0, 1, 2 }))),
        }),
        new("Measure", _ => new RadialItem[]
        {
            new("Mark A / B", p => p.RunSync(x => x.Measure.MarkAimed())),
            new("Midpoint → symmetry", p => p.RunSync(x => x.Measure.MidpointToSymmetry())),
            new("Clear width here", p => p.RunSync(x => x.Measure.ClearWidthHere())),
            new("Centerline readout", p => p.RunSync(x => x.Measure.ToggleReadout())),
            new("Clear marks", p => p.RunSync(x => x.Measure.ClearMarks())),
        }),
        new("Grid", _ => new RadialItem[]
        {
            new("Home grid (CoG)", p => p.RunLater((x, report) => x.Home.Home(useCenterOfMass: false, report))),
            new("Home grid (CoM)", p => p.RunLater((x, report) => x.Home.Home(useCenterOfMass: true, report))),
            new("Return grid", p => p.RunLater((x, report) => x.Home.Return(report))),
            new("Thrust balance", p => p.RunSync(x => x.Thrust.Report())),
        }),
        new("Nudge", _ => new RadialItem[]
        {
            new("Facing −1 cell", p => p.RunSync(x => x.Symmetry.NudgeFacing(-2))),
            new("Facing −½", p => p.RunSync(x => x.Symmetry.NudgeFacing(-1))),
            new("Facing +½", p => p.RunSync(x => x.Symmetry.NudgeFacing(1))),
            new("Facing +1 cell", p => p.RunSync(x => x.Symmetry.NudgeFacing(2))),
        }),
        new("Name", _ => new RadialItem[]
        {
            new("Start / stop selection", p => p.ToggleNamingSelection()),
            new("Open naming window", p => p.OpenNamingScreen()),
        }),
        new("Check", _ => new RadialItem[]
        {
            new("Asymmetry finder (toggle)", p => p.RunSync(x => x.Asymmetry.Toggle())),
        }),
        new("View", _ =>
        {
            var items = new List<RadialItem>();
            for (var side = 0; side < HomeController.ViewNames.Length; side++)
            {
                var captured = side;
                items.Add(new RadialItem(HomeController.ViewNames[side], p => p.RunLater((x, report) => x.Home.View(captured, report))));
            }
            items.Add(new RadialItem("Back from view", p => p.RunLater((x, report) => x.Home.ReturnFromView(report))));
            if (StudioLightingLink.Installed)
                items.Add(new RadialItem("Studio lighting on/off", p => p.RunSync(_ => StudioLightingLink.Toggle())));
            return items;
        }),
    };

    private readonly Canvas _canvas = new() { Width = Size, Height = Size };
    private readonly TextBlock _title = new() { TextWrapping = TextWrapping.Wrap, FontSize = 16, TextAlignment = TextAlignment.Center, Width = 150 };

    public RadialScreen()
    {
        var border = new Border
        {
            Width = Size + 40,
            Height = Size + 40,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Background = new SolidColorBrush(Color.FromRgb(23, 31, 39)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(66, 82, 96)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(Size / 2),
            Child = _canvas,
        };
        var overlay = new Grid { Background = new SolidColorBrush(Color.FromArgb(160, 0, 0, 0)) };
        overlay.Children.Add(border);
        HorizontalAlignment = HorizontalAlignment.Stretch;
        VerticalAlignment = VerticalAlignment.Stretch;
        Background = Brushes.Transparent;
        Content = overlay;
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        ShowSectors();
    }

    private void ShowSectors()
    {
        _canvas.Children.Clear();
        _title.Text = "FNS Build Tools";
        Place(_title, Center, Center);
        var close = Button("Close", () => Dispose());
        Place(close, Center, Center + 36);
        for (var i = 0; i < Sectors.Length; i++)
        {
            var sector = Sectors[i];
            var (x, y) = RadialGeometry.Position(i, Sectors.Length, OuterRadius, Center, Center);
            _canvas.Children.Add(Button(sector.Label, () => ShowItems(sector)));
            Place(_canvas.Children[^1], x, y);
        }
    }

    private void ShowItems(RadialSector sector)
    {
        if (DataContext is not RadialScreenModel model) return;
        _canvas.Children.Clear();
        _title.Text = sector.Label;
        Place(_title, Center, Center);
        var back = Button("‹ Back", ShowSectors);
        Place(back, Center, Center + 36);
        IReadOnlyList<RadialItem> items;
        try
        {
            items = sector.Items(model.Plugin);
        }
        catch (Exception error)
        {
            Diag.Failed("Radial " + sector.Label, error);
            items = Array.Empty<RadialItem>();
        }
        for (var i = 0; i < items.Count; i++)
        {
            var item = items[i];
            var (x, y) = RadialGeometry.Position(i, items.Count, InnerRadius, Center, Center);
            _canvas.Children.Add(Button(item.Label, () => Run(item)));
            Place(_canvas.Children[^1], x, y);
        }
    }

    private void Run(RadialItem item)
    {
        if (DataContext is not RadialScreenModel model) return;
        var plugin = model.Plugin;
        // Close first so the game (aim, F key) is free; the item reports its result as a toast.
        Dispose();
        try { item.Run(plugin); }
        catch (Exception error) { Diag.Failed("Radial item " + item.Label, error); }
    }

    private static Button Button(string label, Action onClick)
    {
        var button = new Button
        {
            Content = label,
            Width = ButtonWidth,
            Height = ButtonHeight,
            HorizontalContentAlignment = HorizontalAlignment.Center,
        };
        button.Click += (_, _) => onClick();
        return button;
    }

    private static void Place(Control control, double x, double y)
    {
        Canvas.SetLeft(control, x - (double.IsNaN(control.Width) || control.Width == 0 ? ButtonWidth : control.Width) / 2);
        Canvas.SetTop(control, y - (double.IsNaN(control.Height) || control.Height == 0 ? ButtonHeight : control.Height) / 2);
    }
}
