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
            Assert.Equal(190, System.Math.Sqrt((x - 300) * (x - 300) + (y - 300) * (y - 300)), 9);
        }
    }

    [Fact]
    public void Rejects_an_index_outside_the_ring()
    {
        Assert.Throws<System.ArgumentOutOfRangeException>(() => RadialGeometry.Position(3, 3, 10, 0, 0));
    }
}
