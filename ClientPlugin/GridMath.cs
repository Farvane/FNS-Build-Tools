using System.Globalization;

namespace Farvane.BuildTools;

/// <summary>
/// Inclusive range of grid cells on one axis. SE2 grid cells are 0.25 m and
/// grid bounding boxes are max-inclusive (CubeGridCoords.GridToLocal).
/// </summary>
public readonly record struct CellSpan(int Min, int Max)
{
    /// <summary>Number of cells covered, counting both ends.</summary>
    public int Count => Max - Min + 1;

    public double Meters => Count * GridMath.CellSize;

    /// <summary>
    /// Symmetry plane position for the span's center, in the game's doubled-cell
    /// units: BuildingInfoComponentObjectBuilder reflects p to 2·distance − p,
    /// and vanilla places a plane centered on a box at 2·Min + size − 1 = Min + Max.
    /// </summary>
    public int PlaneDistance => Min + Max;

    /// <summary>True when the center falls on a cell center (odd cell count), false on a cell boundary.</summary>
    public bool CenterOnCell => (PlaneDistance & 1) == 0;
}

public static class GridMath
{
    public const double CellSize = 0.25;

    /// <summary>Stored by the game for a disabled symmetry plane.</summary>
    public const int DisabledPlane = int.MaxValue;

    public static string Describe(char axis, CellSpan span) => string.Format(CultureInfo.InvariantCulture,
        "{0}: {1:0.00} m ({2} cells), center on a cell {3}",
        axis, span.Meters, span.Count, span.CenterOnCell ? "center" : "boundary");

    public static string DescribePlane(char axis, int distance) => distance == DisabledPlane
        ? axis + ": off"
        : string.Format(CultureInfo.InvariantCulture, "{0}: cell {1:0.0}", axis, distance / 2.0);
}
