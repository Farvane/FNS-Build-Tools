using System.Collections.Generic;

namespace Farvane.BuildTools;

public sealed record NamingTemplate(string Name, string System, string Function);

public static class Templates
{
    public static IReadOnlyList<NamingTemplate> BuiltIn { get; } = new[]
    {
        new NamingTemplate("Hangar doors", "HGR", "DOOR"),
        new NamingTemplate("Weapons", "WPN", "WPN"),
        new NamingTemplate("Thrusters", "PROP", "THR"),
        new NamingTemplate("Lights", "LGT", "LGT"),
        new NamingTemplate("Airlocks", "AL", "DOOR"),
        new NamingTemplate("Control stations", "CTRL", "STA")
    };

    public static IReadOnlyDictionary<string, string> Abbreviations { get; } =
        new Dictionary<string, string>
        {
            ["Ship's Security Detachment"] = "SSD", ["Combat Information Center"] = "CIC",
            ["Ship Central Computing"] = "SC2", ["Ship Security Station"] = "S3",
            ["Ship Fabrication Bay"] = "FAB", ["Engineering"] = "ENG",
            ["Captain's Quarters"] = "CQ", ["XO's Quarters"] = "XQ",
            ["Officer's Quarters"] = "OQ", ["Crew Quarters"] = "CRQ",
            ["Bridge Access"] = "BA", ["Primary Oxygen Generation/Storage"] = "02",
            ["Forward Port Escape Pod"] = "EP1", ["Aft Port Escape Pod"] = "EP2",
            ["Forward Starboard Escape Pod"] = "EP3", ["Aft Starboard Escape Pod"] = "EP4",
            ["Aft Port Airlock"] = "AL1", ["Aft Starboard Airlock"] = "AL2",
            ["Aft Port Airlock Passageway"] = "AL1P", ["Aft Starboard Airlock Passageway"] = "AL2P",
            ["Medical Bay"] = "MED", ["Aft Port Hangar Passageway"] = "AH1P",
            ["Aft Starboard Hangar Passageway"] = "AH2P", ["Aft Port Hangar"] = "AH1",
            ["Aft Starboard Hangar"] = "AH2"
        };
}

