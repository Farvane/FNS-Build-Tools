using Farvane.BuildTools;
using Xunit;

public class AsymmetryTests
{
    private static CellBox Small(int x, int y, int z) => new(x, y, z, x + 1, y + 1, z + 1);

    // Plane between cells 9 and 10 on X: doubled distance 19 (cell c ↔ 19 − c).
    private static readonly int?[] PlaneX = { 19, null, null };

    [Fact]
    public void Mirror_matches_game_reflection()
    {
        var mirrored = Asymmetry.Mirror(Small(4, 0, 0), 0, 19);
        Assert.Equal(new CellBox(14, 0, 0, 15, 1, 1), mirrored);
        Assert.Equal(Small(4, 0, 0), Asymmetry.Mirror(mirrored, 0, 19));
    }

    [Fact]
    public void Symmetric_pair_has_no_issues()
    {
        var boxes = new[] { Small(4, 0, 0), Small(14, 0, 0) };
        var issues = Asymmetry.Find(boxes, new object?[] { "armor", "armor" }, new object?[] { "red", "red" }, PlaneX);
        Assert.Empty(issues);
    }

    [Fact]
    public void Block_on_the_plane_mirrors_onto_itself()
    {
        var straddling = new CellBox(9, 0, 0, 10, 1, 1);
        Assert.Empty(Asymmetry.Find(new[] { straddling }, new object?[] { "armor" }, new object?[] { null }, PlaneX));
    }

    [Fact]
    public void Missing_mirror_is_reported_with_expected_box()
    {
        var issues = Asymmetry.Find(new[] { Small(4, 0, 0) }, new object?[] { "armor" }, new object?[] { null }, PlaneX);
        var issue = Assert.Single(issues);
        Assert.Equal(AsymmetryKind.Missing, issue.Kind);
        Assert.Equal(Small(14, 0, 0), issue.Expected);
        Assert.Equal(0, issue.Axis);
    }

    [Fact]
    public void Different_type_and_paint_are_distinguished()
    {
        var boxes = new[] { Small(4, 0, 0), Small(14, 0, 0), Small(4, 4, 0), Small(14, 4, 0) };
        var types = new object?[] { "armor", "window", "armor", "armor" };
        var paints = new object?[] { "red", "red", "red", "blue" };
        var issues = Asymmetry.Find(boxes, types, paints, PlaneX);
        Assert.Equal(4, issues.Count); // both sides of each pair are flagged
        Assert.Equal(2, issues.FindAll(i => i.Kind == AsymmetryKind.DifferentBlock).Count);
        Assert.Equal(2, issues.FindAll(i => i.Kind == AsymmetryKind.DifferentPaint).Count);
    }

    [Fact]
    public void Each_active_plane_is_checked()
    {
        // Symmetric across X, but not across Z (plane between cells 0 and 1 → doubled 1).
        var boxes = new[] { Small(4, 0, 4), Small(14, 0, 4) };
        var issues = Asymmetry.Find(boxes, new object?[] { "a", "a" }, new object?[] { null, null }, new int?[] { 19, null, 1 });
        Assert.Equal(2, issues.Count);
        Assert.All(issues, i => Assert.Equal(2, i.Axis));
    }

    [Fact]
    public void Partial_overlap_is_missing()
    {
        // A 2.5 m block mirrored onto a 0.5 m block's spot does not count as a mirror.
        var big = new CellBox(0, 0, 0, 9, 9, 9);
        var small = Small(10, 0, 0);
        var issues = Asymmetry.Find(new[] { big, small }, new object?[] { "a", "a" }, new object?[] { null, null }, PlaneX);
        Assert.Equal(2, issues.Count);
        Assert.All(issues, i => Assert.Equal(AsymmetryKind.Missing, i.Kind));
    }
}
