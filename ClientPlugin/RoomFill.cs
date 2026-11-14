using System;
using System.Collections;
using System.Collections.Generic;

namespace Farvane.BuildTools;

public enum RoomStatus
{
    Enclosed,
    /// <summary>The fill reached the shell around the grid: the space is open to the outside.</summary>
    Open,
    /// <summary>The fill exceeded the cell budget before closing.</summary>
    TooLarge,
    /// <summary>No empty cell at or next to the start.</summary>
    StartBlocked,
}

public readonly record struct RoomResult(RoomStatus Status, CellSpan X, CellSpan Y, CellSpan Z, int Cells);

/// <summary>
/// Six-connected flood fill of empty grid cells from a start cell, inside the grid's
/// cell bounds grown by one. Touching that one-cell shell means the space connects to
/// the outside of the grid. Game-independent: occupancy comes from a callback.
/// </summary>
public static class RoomFill
{
    public static RoomResult Fill((int X, int Y, int Z) start, (int X, int Y, int Z) gridMin, (int X, int Y, int Z) gridMax,
        Func<int, int, int, bool> isEmpty, int maxCells)
    {
        // Search space: grid bounds plus a one-cell shell on every side.
        int x0 = gridMin.X - 1, y0 = gridMin.Y - 1, z0 = gridMin.Z - 1;
        long sx = gridMax.X - gridMin.X + 3, sy = gridMax.Y - gridMin.Y + 3, sz = gridMax.Z - gridMin.Z + 3;
        if (sx <= 0 || sy <= 0 || sz <= 0) throw new ArgumentException("Grid bounds are inverted");
        var volume = sx * sy * sz;
        if (volume > int.MaxValue) return new RoomResult(RoomStatus.TooLarge, default, default, default, 0);

        bool Inside(int x, int y, int z) => x >= x0 && y >= y0 && z >= z0 && x < x0 + sx && y < y0 + sy && z < z0 + sz;
        bool OnShell(int x, int y, int z) => x == x0 || y == y0 || z == z0 || x == x0 + sx - 1 || y == y0 + sy - 1 || z == z0 + sz - 1;
        int Index(int x, int y, int z) => (int)((x - x0) + sx * ((y - y0) + sy * (z - z0)));

        var seed = FindSeed(start, isEmpty);
        if (seed is null) return new RoomResult(RoomStatus.StartBlocked, default, default, default, 0);
        var (fx, fy, fz) = seed.Value;
        if (!Inside(fx, fy, fz) || OnShell(fx, fy, fz))
            return new RoomResult(RoomStatus.Open, default, default, default, 0);

        var visited = new BitArray((int)volume);
        var queue = new Queue<(int, int, int)>();
        visited[Index(fx, fy, fz)] = true;
        queue.Enqueue((fx, fy, fz));
        int minX = fx, minY = fy, minZ = fz, maxX = fx, maxY = fy, maxZ = fz, count = 0;
        Span<(int, int, int)> steps = stackalloc (int, int, int)[] { (1, 0, 0), (-1, 0, 0), (0, 1, 0), (0, -1, 0), (0, 0, 1), (0, 0, -1) };

        while (queue.Count > 0)
        {
            var (x, y, z) = queue.Dequeue();
            if (++count > maxCells) return new RoomResult(RoomStatus.TooLarge, default, default, default, count);
            if (x < minX) minX = x; if (x > maxX) maxX = x;
            if (y < minY) minY = y; if (y > maxY) maxY = y;
            if (z < minZ) minZ = z; if (z > maxZ) maxZ = z;
            foreach (var (dx, dy, dz) in steps)
            {
                int nx = x + dx, ny = y + dy, nz = z + dz;
                if (!Inside(nx, ny, nz)) continue;
                var index = Index(nx, ny, nz);
                if (visited[index] || !isEmpty(nx, ny, nz)) continue;
                if (OnShell(nx, ny, nz)) return new RoomResult(RoomStatus.Open, default, default, default, count);
                visited[index] = true;
                queue.Enqueue((nx, ny, nz));
            }
        }
        return new RoomResult(RoomStatus.Enclosed, new CellSpan(minX, maxX), new CellSpan(minY, maxY), new CellSpan(minZ, maxZ), count);
    }

    /// <summary>The start cell if empty, else the first empty face neighbour (e.g. standing in a floor block's cell).</summary>
    private static (int, int, int)? FindSeed((int X, int Y, int Z) start, Func<int, int, int, bool> isEmpty)
    {
        if (isEmpty(start.X, start.Y, start.Z)) return start;
        (int, int, int)[] around = { (0, 1, 0), (0, -1, 0), (1, 0, 0), (-1, 0, 0), (0, 0, 1), (0, 0, -1) };
        foreach (var (dx, dy, dz) in around)
            if (isEmpty(start.X + dx, start.Y + dy, start.Z + dz)) return (start.X + dx, start.Y + dy, start.Z + dz);
        return null;
    }

    /// <summary>Axes across a corridor: every axis except the one with the longest span.</summary>
    public static int[] CorridorAxes(RoomResult room)
    {
        var counts = new[] { room.X.Count, room.Y.Count, room.Z.Count };
        var longest = Array.IndexOf(counts, Math.Max(counts[0], Math.Max(counts[1], counts[2])));
        var axes = new List<int>(2);
        for (var axis = 0; axis < 3; axis++)
            if (axis != longest) axes.Add(axis);
        return axes.ToArray();
    }
}
