using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Keen.VRage.UI.Screens;

namespace Farvane.BuildTools;

/// <summary>Click-through message near the top of the screen that closes itself; gameplay input continues.</summary>
internal sealed class ToastScreenModel : ScreenViewModel
{
    internal string Message { get; }
    public ToastScreenModel(string message)
    {
        Message = message;
        KeepsOtherScreensVisible = true;
        AllowsInputBelowUI = true;
        AllowsInputFromLowerScreens = true;
        NeedsGeneralUIInput = false;
        CanHaveFocus = false;
        InitializeInputContext();
    }
}

internal sealed class ToastScreen : ScreenView
{
    private readonly TextBlock _text = new() { TextWrapping = TextWrapping.Wrap, Foreground = Brushes.White };
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(3) };

    public ToastScreen()
    {
        IsHitTestVisible = false;
        Focusable = false;
        HorizontalAlignment = HorizontalAlignment.Stretch;
        VerticalAlignment = VerticalAlignment.Stretch;
        Background = Brushes.Transparent;
        Content = new Border
        {
            MaxWidth = 900,
            Margin = new Thickness(0, 120, 0, 0),
            Padding = new Thickness(18, 10),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Top,
            Background = new SolidColorBrush(Color.FromArgb(215, 23, 31, 39)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(53, 132, 196)),
            BorderThickness = new Thickness(1),
            Child = _text
        };
        _timer.Tick += (_, _) =>
        {
            _timer.Stop();
            Dispose();
        };
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        if (DataContext is ToastScreenModel model) _text.Text = "GRIDWORX  ·  " + model.Message;
        _timer.Start();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        _timer.Stop();
        base.OnDetachedFromVisualTree(e);
    }
}
