using System;

namespace Farvane.BuildTools;

/// <summary>Places radial menu items on a circle. Item 0 is at the top; the rest go clockwise.</summary>
public static class RadialGeometry
{
    public static (double X, double Y) Position(int index, int count, double radius, double centerX, double centerY)
    {
        if (count < 1) throw new ArgumentOutOfRangeException(nameof(count));
        if (index < 0 || index >= count) throw new ArgumentOutOfRangeException(nameof(index));
        // Screen y grows downward, so sin is used as-is and -90° starts at the top.
        var angle = -Math.PI / 2 + index * 2 * Math.PI / count;
        return (centerX + radius * Math.Cos(angle), centerY + radius * Math.Sin(angle));
    }
}
