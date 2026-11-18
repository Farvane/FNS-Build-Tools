using System;

namespace Farvane.BuildTools;

/// <summary>
/// Radial menu geometry in screen coordinates (y grows downward). Angles are radians,
/// measured clockwise from the top. Item 0 sits at the top; the rest go clockwise.
/// </summary>
public static class RadialGeometry
{
    /// <summary>Point on a circle of the given radius at an angle clockwise from the top.</summary>
    public static (double X, double Y) Polar(double angle, double radius, double centerX, double centerY) =>
        (centerX + radius * Math.Sin(angle), centerY - radius * Math.Cos(angle));

    /// <summary>Centre of item <paramref name="index"/> on a ring of <paramref name="count"/> items.</summary>
    public static (double X, double Y) Position(int index, int count, double radius, double centerX, double centerY)
    {
        if (count < 1) throw new ArgumentOutOfRangeException(nameof(count));
        if (index < 0 || index >= count) throw new ArgumentOutOfRangeException(nameof(index));
        return Polar(index * 2 * Math.PI / count, radius, centerX, centerY);
    }

    /// <summary>Start and end angle of item <paramref name="index"/>'s wedge, clockwise from the top.</summary>
    public static (double Start, double End) SectorAngles(int index, int count)
    {
        if (count < 1) throw new ArgumentOutOfRangeException(nameof(count));
        if (index < 0 || index >= count) throw new ArgumentOutOfRangeException(nameof(index));
        var span = 2 * Math.PI / count;
        var start = index * span - span / 2;
        return (start, start + span);
    }

    /// <summary>The wedge under a pointer at (<paramref name="dx"/>, <paramref name="dy"/>) from the centre.</summary>
    public static int SectorAt(double dx, double dy, int count)
    {
        if (count < 1) throw new ArgumentOutOfRangeException(nameof(count));
        var span = 2 * Math.PI / count;
        var angle = Math.Atan2(dx, -dy);
        if (angle < 0) angle += 2 * Math.PI;
        // Wedge i covers [i·span − span/2, i·span + span/2); shift by half a span before dividing.
        var index = (int)Math.Floor((angle + span / 2) / span) % count;
        return index;
    }
}
