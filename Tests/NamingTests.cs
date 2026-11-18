using System.Linq;
using Farvane.BuildTools;
using Xunit;

public class NamingTests
{
    [Fact]
    public void Hangar_doors_match_requested_example()
    {
        var blocks = new[] { ("Door D", (object)4), ("Door B", (object)2), ("Door A", (object)1), ("Door C", (object)3) };
        var plans = NamingEngine.Plan(blocks, new NamingRequest("Port", "HGR", "Door", 1, 2));
        Assert.Equal(new[] { "PORT-HGR-DOOR-01", "PORT-HGR-DOOR-02", "PORT-HGR-DOOR-03", "PORT-HGR-DOOR-04" },
            plans.Select(plan => plan.Proposed).ToArray());
    }

    [Fact]
    public void Tokens_are_safe_and_empty_segments_are_skipped()
    {
        var plans = NamingEngine.Plan(new[] { ("Light", (object)1) }, new NamingRequest(" Aft Port ", "", "Work light", 7, 3));
        Assert.Equal("AFT-PORT-WORK-LIGHT-007", plans[0].Proposed);
    }

    [Fact]
    public void Includes_requested_templates_and_abbreviations()
    {
        var names = Templates.BuiltIn.Select(item => item.Name).ToArray();
        foreach (var expected in new[] { "Weapons", "Thrusters", "Lights", "Airlocks", "Control stations" })
            Assert.Contains(expected, names);
        Assert.Equal("CIC", Templates.Abbreviations["Combat Information Center"]);
        Assert.Equal("AH1", Templates.Abbreviations["Aft Port Hangar"]);
    }
}
