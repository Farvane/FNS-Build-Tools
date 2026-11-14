using System.Collections.Generic;

namespace Farvane.BuildTools;

public enum AsymmetryKind
{
    /// <summary>No block occupies exactly the mirrored box.</summary>
    Missing,
    /// <summary>A block is there, but of a different type.</summary>
    DifferentBlock,
    /// <summary>Same block type, different paint.</summary>
    DifferentPaint,
}

/// <param name="Block">Index of the block in the input list.</param>
/// <param name="Axis">0 = X, 1 = Y, 2 = Z: the plane it was mirrored across.</param>
/// <param name="Expected">Where its mirror should be.</param>
public readonly record struct AsymmetryIssue(int Block, int Axis, AsymmetryKind Kind, CellBox Expected);

public static class Asymmetry
{
    /// <summary>
    /// Mirrors a box across an axis plane. The plane distance is in the game's doubled
    /// units (BuildingInfoComponentObjectBuilder reflects cell c to distance − c).
    /// </summary>
    public static CellBox Mirror(CellBox box, int axis, int distance) => axis switch
    {
        0 => box with { MinX = distance - box.MaxX, MaxX = distance - box.MinX },
        1 => box with { MinY = distance - box.MaxY, MaxY = distance - box.MinY },
        _ => box with { MinZ = distance - box.MaxZ, MaxZ = distance - box.MinZ },
    };

    /// <summary>
    /// Checks every block against each active plane separately. Vanilla symmetry places the
    /// combinations of single-plane reflections, so per-plane checks cover multi-plane builds.
    /// <paramref name="planes"/> holds the plane distance per axis, or null when off.
    /// Type and paint are compared with Equals; null paint means "unpainted".
    /// </summary>
    public static List<AsymmetryIssue> Find(IReadOnlyList<CellBox> boxes, IReadOnlyList<object?> types,
        IReadOnlyList<object?> paints, int?[] planes)
    {
        var index = new Dictionary<CellBox, int>(boxes.Count);
        for (var i = 0; i < boxes.Count; i++) index[boxes[i]] = i;
        var issues = new List<AsymmetryIssue>();
        for (var axis = 0; axis < 3; axis++)
        {
            if (planes[axis] is not { } distance) continue;
            for (var i = 0; i < boxes.Count; i++)
            {
                var expected = Mirror(boxes[i], axis, distance);
                if (!index.TryGetValue(expected, out var j))
                    issues.Add(new AsymmetryIssue(i, axis, AsymmetryKind.Missing, expected));
                else if (!Equals(types[i], types[j]))
                    issues.Add(new AsymmetryIssue(i, axis, AsymmetryKind.DifferentBlock, expected));
                else if (!Equals(paints[i], paints[j]))
                    issues.Add(new AsymmetryIssue(i, axis, AsymmetryKind.DifferentPaint, expected));
            }
        }
        return issues;
    }
}
