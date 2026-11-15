using System;
using System.Collections.Generic;
using HarmonyLib;
using Keen.Game2.Simulation.WorldObjects.CubeBlocks;
using Keen.Game2.Simulation.WorldObjects.CubeBlocks.Movement;
using Keen.VRage.Library.Mathematics;
using Keen.VRage.Physics.Data;

namespace Farvane.BuildTools;

/// <summary>
/// Thrust balance for the target grid: per thrust direction (as the game assigns it,
/// ThrustData.Direction = block orientation × definition ThrustDirection), the total rated
/// thrust and how far its center of thrust sits off the center of mass. Subgrids are not
/// included. Read-only.
/// </summary>
internal sealed class ThrustController
{
    private static readonly AccessTools.FieldRef<ThrusterComponent, CubeBlockComponent> Block =
        AccessTools.FieldRefAccess<ThrusterComponent, CubeBlockComponent>("_cubeBlock");

    private readonly SymmetryController _context;

    public ThrustController(SymmetryController context) => _context = context;

    public string Report()
    {
        var target = _context.RequireTarget(out var grid);
        if (!target.RootEntity.Data.TryGet<RigidBodyMassProperties>(out var mass) || mass.Mass <= 0)
            throw new InvalidOperationException("Thrust balance needs a ship (static grids have no center of mass)");
        var thrusters = new List<(int, double, double, double, double)>();
        grid.VisitAllBlocksWithComponent<ThrusterComponent>(thruster =>
        {
            var block = Block(thruster);
            if (block is null || thruster.Definition is not { } definition) return;
            var direction = block.BlockOrientation.TransformDirection(definition.ThrustDirection);
            var box = block.AABB;
            thrusters.Add(((int)direction, definition.ThrustPower,
                (box.Min.X + box.Max.X) * 0.125, (box.Min.Y + box.Max.Y) * 0.125, (box.Min.Z + box.Max.Z) * 0.125));
        }, false);
        if (thrusters.Count == 0) throw new InvalidOperationException("No thrusters on this grid");

        var axes = new (double, double, double)[6];
        var names = new string[6];
        for (var d = 0; d < 6; d++)
        {
            var v = Base6Directions.GetVector((Base6Directions.Direction)d);
            axes[d] = (v.X, v.Y, v.Z);
            names[d] = ((Base6Directions.Direction)d).ToString();
        }
        var com = mass.CenterOfMass;
        var groups = ThrustBalance.Compute(thrusters, (com.X, com.Y, com.Z), axes, names);
        var lines = new List<string> { "Thrust balance (rated thrust, this grid only; directions as the game assigns them):" };
        foreach (var group in groups) lines.Add("  " + ThrustBalance.Describe(group));
        return string.Join(Environment.NewLine, lines);
    }
}
