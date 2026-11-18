using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
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
/// Mouse-direction radial menu. The seven sectors form the outer ring; the chosen sector's
/// items form an inner ring with the same geometry. Move the pointer toward a wedge to
/// highlight it, then click to pick it. The centre hub goes back, or closes on the top ring.
/// The menu closes before an action runs, so the game (aim, F key) is free again.
/// </summary>
internal sealed class RadialScreen : ScreenView
{
    private const double Size = 560;
    private const double Center = Size / 2;
    private const double HubRadius = 62;
    private const double OuterRadius = 250;
    private const double LabelRadius = 165;
    private const double LabelWidth = 120;
    private const int NoWedge = -1;
    private const int HubTarget = -2;

    private static readonly IBrush WedgeFill = new SolidColorBrush(Color.FromRgb(35, 50, 62));
    private static readonly IBrush WedgeHover = new SolidColorBrush(Color.FromRgb(28, 110, 140));
    private static readonly IBrush WedgeStroke = new SolidColorBrush(Color.FromRgb(66, 82, 96));
    private static readonly IBrush HubFill = new SolidColorBrush(Color.FromRgb(23, 31, 39));

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

    private readonly Canvas _canvas = new() { Width = Size, Height = Size, Background = Brushes.Transparent };
    private readonly List<Path> _wedges = new();
    private IReadOnlyList<string> _labels = Array.Empty<string>();
    private Action<int>? _pick;
    private Action? _hub;
    private int _hover = NoWedge;

    public RadialScreen()
    {
        _canvas.PointerMoved += (_, e) => Hover(e.GetPosition(_canvas));
        _canvas.PointerExited += (_, _) => Hover(new Point(-1000, -1000));
        _canvas.PointerPressed += (_, e) =>
        {
            if (!e.GetCurrentPoint(_canvas).Properties.IsLeftButtonPressed) return;
            e.Handled = true;
            var target = TargetAt(e.GetPosition(_canvas));
            if (target == HubTarget) _hub?.Invoke();
            else if (target >= 0) _pick?.Invoke(target);
        };
        var border = new Border
        {
            Width = Size + 40,
            Height = Size + 40,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Background = new SolidColorBrush(Color.FromRgb(23, 31, 39)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(66, 82, 96)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius((Size + 40) / 2),
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
        ShowRing(Sectors.Select(sector => sector.Label).ToArray(), index => ShowItems(Sectors[index]),
            "FNS Build Tools", "Close", () => Dispose());
    }

    private void ShowItems(RadialSector sector)
    {
        if (DataContext is not RadialScreenModel model) return;
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
        ShowRing(items.Select(item => item.Label).ToArray(), index => Run(items[index]),
            sector.Label, "‹ Back", ShowSectors);
    }

    /// <summary>Draws one ring of wedges with a labelled hub in the centre.</summary>
    private void ShowRing(IReadOnlyList<string> labels, Action<int> pick, string title, string hubLabel, Action hub)
    {
        _canvas.Children.Clear();
        _wedges.Clear();
        _labels = labels;
        _pick = pick;
        _hub = hub;
        _hover = NoWedge;
        for (var i = 0; i < labels.Count; i++)
        {
            var wedge = new Path
            {
                Data = Wedge(i, labels.Count),
                Fill = WedgeFill,
                Stroke = WedgeStroke,
                StrokeThickness = 1,
            };
            _wedges.Add(wedge);
            _canvas.Children.Add(wedge);
        }
        for (var i = 0; i < labels.Count; i++)
        {
            var (start, end) = RadialGeometry.SectorAngles(i, labels.Count);
            var (x, y) = RadialGeometry.Polar((start + end) / 2, LabelRadius, Center, Center);
            var text = new TextBlock
            {
                Text = labels[i],
                Width = LabelWidth,
                TextWrapping = TextWrapping.Wrap,
                TextAlignment = TextAlignment.Center,
                IsHitTestVisible = false,
            };
            _canvas.Children.Add(text);
            Place(text, x, y);
        }
        var hubShape = new Ellipse
        {
            Width = HubRadius * 2,
            Height = HubRadius * 2,
            Fill = HubFill,
            Stroke = WedgeStroke,
            IsHitTestVisible = false,
        };
        Canvas.SetLeft(hubShape, Center - HubRadius);
        Canvas.SetTop(hubShape, Center - HubRadius);
        _canvas.Children.Add(hubShape);
        var hubText = new TextBlock
        {
            Text = title + "\n" + hubLabel,
            Width = HubRadius * 2 - 14,
            TextWrapping = TextWrapping.Wrap,
            TextAlignment = TextAlignment.Center,
            IsHitTestVisible = false,
        };
        _canvas.Children.Add(hubText);
        Place(hubText, Center, Center);
    }

    private void Hover(Point point)
    {
        var target = TargetAt(point);
        var wedge = target >= 0 ? target : NoWedge;
        if (wedge == _hover) return;
        if (_hover >= 0 && _hover < _wedges.Count) _wedges[_hover].Fill = WedgeFill;
        _hover = wedge;
        if (_hover >= 0 && _hover < _wedges.Count) _wedges[_hover].Fill = WedgeHover;
    }

    /// <summary>Which wedge is under a point: its index, <see cref="HubTarget"/> for the hub, or <see cref="NoWedge"/>.</summary>
    private int TargetAt(Point point)
    {
        if (_labels.Count == 0) return NoWedge;
        var dx = point.X - Center;
        var dy = point.Y - Center;
        var distance = Math.Sqrt(dx * dx + dy * dy);
        if (distance < HubRadius) return HubTarget;
        if (distance > OuterRadius) return NoWedge;
        return RadialGeometry.SectorAt(dx, dy, _labels.Count);
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

    /// <summary>Annular wedge for item <paramref name="index"/> of <paramref name="count"/>.</summary>
    private static Geometry Wedge(int index, int count)
    {
        var (start, end) = RadialGeometry.SectorAngles(index, count);
        var large = end - start > Math.PI;
        var figure = new PathFigure
        {
            StartPoint = PolarPoint(start, HubRadius),
            IsClosed = true,
            Segments = new PathSegments
            {
                new LineSegment { Point = PolarPoint(start, OuterRadius) },
                new ArcSegment { Point = PolarPoint(end, OuterRadius), Size = new Size(OuterRadius, OuterRadius), IsLargeArc = large, SweepDirection = SweepDirection.Clockwise },
                new LineSegment { Point = PolarPoint(end, HubRadius) },
                new ArcSegment { Point = PolarPoint(start, HubRadius), Size = new Size(HubRadius, HubRadius), IsLargeArc = large, SweepDirection = SweepDirection.CounterClockwise },
            },
        };
        return new PathGeometry { Figures = new PathFigures { figure } };
    }

    private static Point PolarPoint(double angle, double radius)
    {
        var (x, y) = RadialGeometry.Polar(angle, radius, Center, Center);
        return new Point(x, y);
    }

    /// <summary>Centres a label on a point, using its fixed width or its measured size.</summary>
    private static void Place(Control control, double x, double y)
    {
        control.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        var size = control.DesiredSize;
        var width = control.Width > 0 ? control.Width : size.Width;
        Canvas.SetLeft(control, x - width / 2);
        Canvas.SetTop(control, y - size.Height / 2);
    }
}
