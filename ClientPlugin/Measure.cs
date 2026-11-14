using System;
using System.Collections.Generic;
using System.Globalization;

namespace Farvane.BuildTools;

/// <summary>Inclusive cell box (grid coordinates, max-inclusive like the game's block AABBs).</summary>
public readonly record struct CellBox(int MinX, int MinY, int MinZ, int MaxX, int MaxY, int MaxZ)
{
    public CellSpan Axis(int axis) => axis switch
    {
        0 => new CellSpan(MinX, MaxX),
        1 => new CellSpan(MinY, MaxY),
        _ => new CellSpan(MinZ, MaxZ),
    };

    /// <summary>Center in doubled-cell units (Min + Max), exact for odd and even sizes.</summary>
    public int Center2(int axis) => Axis(axis).PlaneDistance;
}

/// <summary>Tape result between two marked blocks on the same grid.</summary>
public readonly record struct TapeResult(CellSpan[] Outer, double[] CenterDeltaMeters, double CenterDistanceMeters, int[] SeparatedAxes)
{
    /// <summary>Symmetry plane distance (doubled cells) halfway between the two blocks on an axis.</summary>
    public int Midpoint(int axis) => Outer[axis].PlaneDistance;
}

public static class Measure
{
    private static readonly string[] AxisNames = { "X", "Y", "Z" };

    /// <summary>
    /// Outer spans cover both blocks face to face (inclusive), the way you would count
    /// blocks along a hull. Center deltas run block center to block center.
    /// </summary>
    public static TapeResult Tape(CellBox a, CellBox b)
    {
        var outer = new CellSpan[3];
        var delta = new double[3];
        var separated = new List<int>(3);
        for (var axis = 0; axis < 3; axis++)
        {
            var sa = a.Axis(axis);
            var sb = b.Axis(axis);
            outer[axis] = new CellSpan(Math.Min(sa.Min, sb.Min), Math.Max(sa.Max, sb.Max));
            delta[axis] = (b.Center2(axis) - a.Center2(axis)) / 2.0 * GridMath.CellSize;
            if (b.Center2(axis) != a.Center2(axis)) separated.Add(axis);
        }
        var distance = Math.Sqrt(delta[0] * delta[0] + delta[1] * delta[1] + delta[2] * delta[2]);
        return new TapeResult(outer, delta, distance, separated.ToArray());
    }

    public static string Describe(TapeResult tape)
    {
        var parts = new List<string>(3);
        for (var axis = 0; axis < 3; axis++)
            parts.Add(string.Format(CultureInfo.InvariantCulture, "{0} {1:0.00} m", AxisNames[axis], tape.Outer[axis].Meters));
        return string.Format(CultureInfo.InvariantCulture, "A↔B outer {0} · center to center {1:0.00} m",
            string.Join(" × ", parts), tape.CenterDistanceMeters);
    }

    /// <summary>
    /// Free cells from <paramref name="start"/> along -axis and +axis until a non-empty cell,
    /// capped at <paramref name="maxCells"/> each way. Returns the inclusive empty span, or null
    /// if the start cell is occupied. A side that hit the cap is open (no wall found).
    /// </summary>
    public static (CellSpan Span, bool OpenLow, bool OpenHigh)? ClearSpan((int X, int Y, int Z) start, int axis,
        Func<int, int, int, bool> isEmpty, int maxCells)
    {
        if (!isEmpty(start.X, start.Y, start.Z)) return null;
        int Step(int sign, out bool open)
        {
            var reach = 0;
            open = true;
            while (reach < maxCells)
            {
                var n = reach + 1;
                var (x, y, z) = axis switch
                {
                    0 => (start.X + sign * n, start.Y, start.Z),
                    1 => (start.X, start.Y + sign * n, start.Z),
                    _ => (start.X, start.Y, start.Z + sign * n),
                };
                if (!isEmpty(x, y, z))
                {
                    open = false;
                    break;
                }
                reach = n;
            }
            return reach;
        }
        var low = Step(-1, out var openLow);
        var high = Step(1, out var openHigh);
        var s = axis switch { 0 => start.X, 1 => start.Y, _ => start.Z };
        return (new CellSpan(s - low, s + high), openLow, openHigh);
    }

    /// <summary>Signed offset in meters of a block's center from a symmetry plane (both in doubled cells).</summary>
    public static double OffsetFromPlane(int blockCenter2, int planeDistance) =>
        (blockCenter2 - planeDistance) / 2.0 * GridMath.CellSize;
}
