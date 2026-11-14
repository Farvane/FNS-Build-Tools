using Farvane.BuildTools;
using Xunit;

public class GridMathTests
{
    // Reflection used by the game: p' = 2·distance − p (in cell units, distance doubled).
    private static int Reflect(int distance, int cell) => distance - cell;

    [Theory]
    [InlineData(0, 9)]     // even count: center on a boundary
    [InlineData(-5, 5)]    // odd count spanning the origin
    [InlineData(-10, -3)]  // entirely negative
    [InlineData(3, 3)]     // single cell
    public void Plane_mirrors_the_span_onto_itself(int min, int max)
    {
        var span = new CellSpan(min, max);
        Assert.Equal(max, Reflect(span.PlaneDistance, min));
        Assert.Equal(min, Reflect(span.PlaneDistance, max));
    }

    [Theory]
    [InlineData(0, 9, 10, false)]
    [InlineData(-5, 5, 11, true)]
    [InlineData(-10, -3, 8, false)]
    [InlineData(-9, -3, 7, true)]
    public void Reports_count_and_center_kind(int min, int max, int count, bool onCell)
    {
        var span = new CellSpan(min, max);
        Assert.Equal(count, span.Count);
        Assert.Equal(onCell, span.CenterOnCell);
        Assert.Equal(count * 0.25, span.Meters);
    }

    [Fact]
    public void Matches_vanilla_plane_formula()
    {
        // BlockPlacementSymmetrySetup: 2·Min + GetPlaneOffsetFromBlock(size, edge: 0), offset = size − 1.
        var span = new CellSpan(-7, 12);
        Assert.Equal(2 * span.Min + span.Count - 1, span.PlaneDistance);
    }

    [Fact]
    public void Describes_planes()
    {
        Assert.Equal("X: off", GridMath.DescribePlane('X', GridMath.DisabledPlane));
        Assert.Equal("Y: cell -2.5", GridMath.DescribePlane('Y', -5));
        Assert.Equal("Z: 2.50 m (10 cells), center on a cell boundary", GridMath.Describe('Z', new CellSpan(0, 9)));
    }
}
