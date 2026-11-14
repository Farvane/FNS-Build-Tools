using Farvane.BuildTools;
using Xunit;

public class RoomFillTests
{
    /// <summary>A hollow box: walls on the outer cells of [min, max], air inside.</summary>
    private static Func<int, int, int, bool> HollowBox((int X, int Y, int Z) min, (int X, int Y, int Z) max,
        Func<int, int, int, bool>? extraAir = null) =>
        (x, y, z) =>
        {
            if (extraAir?.Invoke(x, y, z) == true) return true;
            var inBox = x >= min.X && x <= max.X && y >= min.Y && y <= max.Y && z >= min.Z && z <= max.Z;
            if (!inBox) return true; // outside the ship
            var wall = x == min.X || x == max.X || y == min.Y || y == max.Y || z == min.Z || z == max.Z;
            return !wall;
        };

    [Fact]
    public void Finds_the_interior_of_a_closed_room()
    {
        var room = RoomFill.Fill((3, 3, 3), (0, 0, 0), (10, 6, 8), HollowBox((0, 0, 0), (10, 6, 8)), 100_000);
        Assert.Equal(RoomStatus.Enclosed, room.Status);
        Assert.Equal(new CellSpan(1, 9), room.X);
        Assert.Equal(new CellSpan(1, 5), room.Y);
        Assert.Equal(new CellSpan(1, 7), room.Z);
        Assert.Equal(9 * 5 * 7, room.Cells);
        // The room's center is the hull's center for a symmetric box.
        Assert.Equal(10, room.X.PlaneDistance);
    }

    [Fact]
    public void Off_center_room_gives_its_own_center()
    {
        // Two rooms split by a wall at x = 6; start in the left one.
        Func<int, int, int, bool> ship = (x, y, z) =>
            HollowBox((0, 0, 0), (12, 6, 6))(x, y, z) && x != 6;
        var room = RoomFill.Fill((2, 3, 3), (0, 0, 0), (12, 6, 6), ship, 100_000);
        Assert.Equal(RoomStatus.Enclosed, room.Status);
        Assert.Equal(new CellSpan(1, 5), room.X);
        Assert.Equal(6, room.X.PlaneDistance); // plane through cell 3
    }

    [Fact]
    public void A_hole_to_space_is_reported_as_open()
    {
        var leaky = HollowBox((0, 0, 0), (10, 6, 8), (x, y, z) => x == 10 && y == 3 && z == 4);
        var room = RoomFill.Fill((3, 3, 3), (0, 0, 0), (10, 6, 8), leaky, 100_000);
        Assert.Equal(RoomStatus.Open, room.Status);
    }

    [Fact]
    public void Standing_outside_the_hull_is_open()
    {
        var room = RoomFill.Fill((20, 3, 3), (0, 0, 0), (10, 6, 8), HollowBox((0, 0, 0), (10, 6, 8)), 100_000);
        Assert.Equal(RoomStatus.Open, room.Status);
    }

    [Fact]
    public void Starting_inside_a_floor_block_uses_the_cell_above()
    {
        var room = RoomFill.Fill((3, 0, 3), (0, 0, 0), (10, 6, 8), HollowBox((0, 0, 0), (10, 6, 8)), 100_000);
        Assert.Equal(RoomStatus.Enclosed, room.Status);
        Assert.Equal(new CellSpan(1, 5), room.Y);
    }

    [Fact]
    public void Solid_start_is_blocked()
    {
        var room = RoomFill.Fill((3, 3, 3), (0, 0, 0), (10, 6, 8), (_, _, _) => false, 100_000);
        Assert.Equal(RoomStatus.StartBlocked, room.Status);
    }

    [Fact]
    public void Budget_stops_huge_fills()
    {
        var room = RoomFill.Fill((10, 10, 10), (0, 0, 0), (40, 40, 40), HollowBox((0, 0, 0), (40, 40, 40)), 1_000);
        Assert.Equal(RoomStatus.TooLarge, room.Status);
    }

    [Fact]
    public void Corridor_axes_exclude_the_longest_span()
    {
        var room = new RoomResult(RoomStatus.Enclosed, new CellSpan(0, 3), new CellSpan(0, 39), new CellSpan(0, 5), 0);
        Assert.Equal(new[] { 0, 2 }, RoomFill.CorridorAxes(room));
    }
}
