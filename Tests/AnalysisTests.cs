using Farvane.BuildTools;
using Xunit;

public class AnalysisTests
{
    private static readonly (double, double, double)[] Axes =
    {
        (0, 0, -1), (0, 0, 1), (-1, 0, 0), (1, 0, 0), (0, 1, 0), (0, -1, 0),
    };
    private static readonly string[] Names = { "Forward", "Backward", "Left", "Right", "Up", "Down" };

    [Theory]
    [InlineData(0.9, 0.1, -0.2, 0, 1)]
    [InlineData(-0.9, 0.1, 0.2, 0, -1)]
    [InlineData(0.1, -0.8, 0.5, 1, -1)]
    [InlineData(0.1, 0.2, -0.95, 2, -1)]
    public void Facing_axis_picks_the_dominant_component(double x, double y, double z, int axis, int sign)
    {
        Assert.Equal((axis, sign), Nudge.FacingAxis(x, y, z));
    }

    [Fact]
    public void Nudge_steps_in_half_cells()
    {
        Assert.Equal(20, Nudge.Step(19, 1));   // ½ cell
        Assert.Equal(17, Nudge.Step(19, -2));  // 1 cell back
    }

    [Fact]
    public void Symmetric_thrusters_are_balanced()
    {
        var thrusters = new[] { (0, 1000.0, -2.0, 0.0, 5.0), (0, 1000.0, 2.0, 0.0, 5.0) };
        var g = Assert.Single(ThrustBalance.Compute(thrusters, (0, 0, 0), Axes, Names));
        Assert.Equal("Forward", g.Direction);
        Assert.Equal(2000, g.ForceNewtons);
        Assert.Equal(0, g.OffsetMeters, 6); // along-axis distance (z = 5) does not count
    }

    [Fact]
    public void Offset_thrust_reports_lever_arm_and_torque()
    {
        var thrusters = new[] { (4, 3000.0, 1.0, 0.0, 0.0), (4, 1000.0, -1.0, 0.0, 0.0) };
        var g = Assert.Single(ThrustBalance.Compute(thrusters, (0, 0, 0), Axes, Names));
        Assert.Equal(0.5, g.OffsetMeters, 6);   // center of thrust at x = +0.5
        Assert.Equal(2000, g.TorqueNewtonMeters, 6);
        Assert.Contains("torque", ThrustBalance.Describe(g));
    }

    [Fact]
    public void View_distance_grows_with_size()
    {
        Assert.True(ViewFraming.Distance(10) > ViewFraming.Distance(2));
        Assert.True(ViewFraming.Distance(10) > 10 / System.Math.Tan(35 * System.Math.PI / 180));
    }
}
