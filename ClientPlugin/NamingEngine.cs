using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Farvane.BuildTools;

public sealed record NamingRequest(string Location, string System, string Function, int Start, int Width);

public sealed record RenamePlan(string Original, string Proposed, object Target);

public static class NamingEngine
{
    public static IReadOnlyList<RenamePlan> Plan(IEnumerable<(string Name, object Target)> blocks, NamingRequest request)
    {
        if (request.Start < 0) throw new ArgumentOutOfRangeException(nameof(request.Start));
        if (request.Width is < 1 or > 6) throw new ArgumentOutOfRangeException(nameof(request.Width));
        var tokens = new[] { request.Location, request.System, request.Function }
            .Select(NormalizeToken).Where(value => value.Length > 0).ToArray();
        if (tokens.Length == 0) throw new ArgumentException("Enter a location, system, or function.");
        var prefix = string.Join("-", tokens);
        return blocks.OrderBy(block => block.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(block => block.Name, StringComparer.Ordinal)
            .Select((block, index) => new RenamePlan(
                block.Name,
                prefix + "-" + (request.Start + index).ToString(new string('0', request.Width)),
                block.Target))
            .ToList();
    }

    public static string NormalizeToken(string? value)
    {
        var result = new StringBuilder();
        var separator = false;
        foreach (var character in (value ?? string.Empty).Trim().ToUpperInvariant())
        {
            if (char.IsLetterOrDigit(character))
            {
                if (separator && result.Length > 0) result.Append('-');
                result.Append(character);
                separator = false;
            }
            else separator = true;
        }
        return result.ToString();
    }
}

