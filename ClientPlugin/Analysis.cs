using System;
using System.Collections.Generic;
using System.Globalization;

namespace Farvane.BuildTools;

/// <summary>Plane nudging: pick the grid axis you are facing and step its plane in half-cells.</summary>
public static class Nudge
{
    /// <summary>The grid axis most aligned with a grid-local look direction, and the sign of travel along it.</summary>
    public static (int Axis, int Sign) FacingAxis(double x, double y, double z)
    {
        double ax = Math.Abs(x), ay = Math.Abs(y), az = Math.Abs(z);
        if (ax >= ay && ax >= az) return (0, x >= 0 ? 1 : -1);
        if (ay >= az) return (1, y >= 0 ? 1 : -1);
        return (2, z >= 0 ? 1 : -1);
    }

    /// <summary>New plane distance (doubled cells) after moving <paramref name="halfCells"/> half-cells along +axis.</summary>
    public static int Step(int distance, int halfCells) => distance + halfCells;
}

public readonly record struct ThrustGroup(string Direction, int Count, double ForceNewtons, double OffsetMeters, double TorqueNewtonMeters,
    double CenterX, double CenterY, double CenterZ);

/// <summary>
/// Thrust balance: for each thrust direction, the force-weighted center of thrust versus the
/// center of mass. The offset perpendicular to the thrust axis is the lever arm that turns the
/// ship when that group fires; zero means it pushes straight through the CoM.
/// </summary>
public static class ThrustBalance
{
    public static IReadOnlyList<ThrustGroup> Compute(IEnumerable<(int Direction, double Force, double X, double Y, double Z)> thrusters,
        (double X, double Y, double Z) com, (double X, double Y, double Z)[] axisVectors, string[] names)
    {
        var sums = new (int Count, double F, double X, double Y, double Z)[axisVectors.Length];
        foreach (var t in thrusters)
        {
            if (t.Direction < 0 || t.Direction >= sums.Length || t.Force <= 0) continue;
            ref var s = ref sums[t.Direction];
            s.Count++;
            s.F += t.Force;
            s.X += t.X * t.Force;
            s.Y += t.Y * t.Force;
            s.Z += t.Z * t.Force;
        }
        var groups = new List<ThrustGroup>();
        for (var d = 0; d < sums.Length; d++)
        {
            var s = sums[d];
            if (s.Count == 0) continue;
            double cx = s.X / s.F, cy = s.Y / s.F, cz = s.Z / s.F;
            // Perpendicular component of (center − CoM) relative to the unit thrust axis.
            double rx = cx - com.X, ry = cy - com.Y, rz = cz - com.Z;
            var (ux, uy, uz) = axisVectors[d];
            var along = rx * ux + ry * uy + rz * uz;
            double px = rx - along * ux, py = ry - along * uy, pz = rz - along * uz;
            var offset = Math.Sqrt(px * px + py * py + pz * pz);
            groups.Add(new ThrustGroup(names[d], s.Count, s.F, offset, offset * s.F, cx, cy, cz));
        }
        return groups;
    }

    public static string Describe(ThrustGroup g) => string.Format(CultureInfo.InvariantCulture,
        "{0}: {1} thruster{2}, {3}, center of thrust {4:0.00} m off CoM{5}",
        g.Direction, g.Count, g.Count == 1 ? "" : "s", Force(g.ForceNewtons), g.OffsetMeters,
        g.OffsetMeters < 0.05 ? " (balanced)" : string.Format(CultureInfo.InvariantCulture, " (torque {0})", Torque(g.TorqueNewtonMeters)));

    public static string Force(double newtons) => newtons >= 1e6
        ? (newtons / 1e6).ToString("0.00", CultureInfo.InvariantCulture) + " MN"
        : (newtons / 1e3).ToString("0.0", CultureInfo.InvariantCulture) + " kN";

    private static string Torque(double nm) => nm >= 1e6
        ? (nm / 1e6).ToString("0.00", CultureInfo.InvariantCulture) + " MN·m"
        : (nm / 1e3).ToString("0.0", CultureInfo.InvariantCulture) + " kN·m";
}

/// <summary>Camera distance so a box fits in view.</summary>
public static class ViewFraming
{
    /// <summary>Distance from the pivot that frames a bounding sphere of <paramref name="radius"/> in a 70° view, plus margin.</summary>
    public static double Distance(double radius) => radius / Math.Tan(35 * Math.PI / 180) * 1.15 + 2.0;
}
