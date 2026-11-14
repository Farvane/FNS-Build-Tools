using Farvane.BuildTools;
using Xunit;

public class MeasureTests
{
    // A 0.5 m block is 2×2×2 cells; a 2.5 m block is 10×10×10.
    private static CellBox Small(int x, int y, int z) => new(x, y, z, x + 1, y + 1, z + 1);

    [Fact]
    public void Tape_between_two_small_blocks_on_one_axis()
    {
        var tape = Measure.Tape(Small(0, 0, 0), Small(10, 0, 0));
        Assert.Equal(new CellSpan(0, 11), tape.Outer[0]);
        Assert.Equal(3.0, tape.Outer[0].Meters);   // six 0.5 m blocks end to end
        Assert.Equal(0.5, tape.Outer[1].Meters);
        Assert.Equal(2.5, tape.CenterDeltaMeters[0]);
        Assert.Equal(2.5, tape.CenterDistanceMeters);
        Assert.Equal(new[] { 0 }, tape.SeparatedAxes);
    }

    [Fact]
    public void Tape_is_symmetric_in_outer_span_and_signed_in_delta()
    {
        var ab = Measure.Tape(Small(0, 0, 0), Small(4, 6, 0));
        var ba = Measure.Tape(Small(4, 6, 0), Small(0, 0, 0));
        Assert.Equal(ab.Outer[0], ba.Outer[0]);
        Assert.Equal(-ab.CenterDeltaMeters[1], ba.CenterDeltaMeters[1]);
        Assert.Equal(new[] { 0, 1 }, ab.SeparatedAxes);
        Assert.Equal(System.Math.Sqrt(1.0 * 1.0 + 1.5 * 1.5), ab.CenterDistanceMeters, 6);
    }

    [Fact]
    public void Midpoint_plane_mirrors_one_block_onto_the_other()
    {
        var a = Small(-7, 0, 0);
        var b = Small(12, 0, 0);
        var tape = Measure.Tape(a, b);
        var d = tape.Midpoint(0);
        // Game reflection: p' = 2·distance − p in doubled units, i.e. cell' = d − cell.
        Assert.Equal(b.MaxX, d - a.MinX);
        Assert.Equal(b.MinX, d - a.MaxX);
    }

    [Fact]
    public void Clear_span_finds_both_walls()
    {
        // Walls at x = -3 and x = 6 (corridor 8 cells = 2 m wide).
        var span = Measure.ClearSpan((0, 0, 0), 0, (x, _, _) => x > -3 && x < 6, 1000);
        Assert.NotNull(span);
        Assert.Equal(new CellSpan(-2, 5), span!.Value.Span);
        Assert.Equal(2.0, span.Value.Span.Meters);
        Assert.False(span.Value.OpenLow);
        Assert.False(span.Value.OpenHigh);
    }

    [Fact]
    public void Clear_span_reports_open_sides_and_blocked_start()
    {
        var open = Measure.ClearSpan((0, 0, 0), 2, (_, _, z) => z < 4, 50);
        Assert.True(open!.Value.OpenLow);
        Assert.False(open.Value.OpenHigh);
        Assert.Null(Measure.ClearSpan((0, 0, 0), 1, (_, _, _) => false, 50));
    }

    [Fact]
    public void Offset_from_plane_in_meters()
    {
        var block = Small(4, 0, 0);                    // center 4.5 cells → doubled 9
        Assert.Equal(1.0, Measure.OffsetFromPlane(block.Center2(0), 1)); // plane at cell 0.5
        Assert.Equal(0.0, Measure.OffsetFromPlane(9, 9));
    }
}
