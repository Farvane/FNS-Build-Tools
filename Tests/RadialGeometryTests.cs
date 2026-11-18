using System;
using Farvane.BuildTools;
using Xunit;

public class RadialGeometryTests
{
    [Fact]
    public void First_item_is_at_the_top_and_items_go_clockwise()
    {
        var (x0, y0) = RadialGeometry.Position(0, 4, 100, 0, 0);
        Assert.Equal(0, x0, 9);
        Assert.Equal(-100, y0, 9);
        var (x1, y1) = RadialGeometry.Position(1, 4, 100, 0, 0);
        Assert.Equal(100, x1, 9);
        Assert.Equal(0, y1, 9);
    }

    [Fact]
    public void Items_sit_on_the_circle_around_the_center()
    {
        for (var i = 0; i < 7; i++)
        {
            var (x, y) = RadialGeometry.Position(i, 7, 190, 300, 300);
            Assert.Equal(190, Math.Sqrt((x - 300) * (x - 300) + (y - 300) * (y - 300)), 9);
        }
    }

    [Fact]
    public void Rejects_an_index_outside_the_ring()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => RadialGeometry.Position(3, 3, 10, 0, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => RadialGeometry.SectorAngles(-1, 3));
    }

    [Theory]
    [InlineData(0, -1, 7, 0)]   // straight up
    [InlineData(1, 0, 4, 1)]    // right
    [InlineData(0, 1, 4, 2)]    // down
    [InlineData(-1, 0, 4, 3)]   // left
    [InlineData(-1, -1, 8, 7)]  // up-left, a 45° wedge
    public void Pointer_direction_picks_the_wedge_it_points_into(double dx, double dy, int count, int expected)
    {
        Assert.Equal(expected, RadialGeometry.SectorAt(dx, dy, count));
    }

    [Fact]
    public void Every_wedge_centre_points_back_to_its_own_wedge()
    {
        for (var count = 1; count <= 9; count++)
        for (var i = 0; i < count; i++)
        {
            var (start, end) = RadialGeometry.SectorAngles(i, count);
            var middle = (start + end) / 2;
            var dx = Math.Sin(middle);
            var dy = -Math.Cos(middle);
            Assert.Equal(i, RadialGeometry.SectorAt(dx, dy, count));
        }
    }

    [Fact]
    public void Wedges_cover_the_whole_circle_without_gaps()
    {
        for (var count = 1; count <= 9; count++)
        {
            var (first, _) = RadialGeometry.SectorAngles(0, count);
            var (_, last) = RadialGeometry.SectorAngles(count - 1, count);
            Assert.Equal(2 * Math.PI, last - first, 9);
        }
    }
}
