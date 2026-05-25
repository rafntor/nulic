using Serilog;
using System.IO.Enumeration;

namespace nulic;

internal static class PackageFilter
{
    // Split a flat SPDX expression into AND clauses, each of which may contain OR alternatives.
    // AND  → all clauses must be allowed (must comply with all)
    // OR   → any one alternative in the clause is sufficient (can choose a compatible one)
    // WITH → binds tighter than AND/OR; handled per-leaf
    internal static bool IsAllowed(string license, HashSet<string> allowedIds, HashSet<string> allowedExceptions)
    {
        if (license == NulicLicense.NOASSERTION) return false;

        foreach (var andClause in license.Split(" AND ", StringSplitOptions.RemoveEmptyEntries))
        {
            // Each AND clause may be "A OR B OR C" — at least one must be allowed
            var orAlternatives = andClause.Split(" OR ", StringSplitOptions.RemoveEmptyEntries);
            var anyAllowed = orAlternatives.Any(alt => IsLeafAllowed(alt.Trim(), allowedIds, allowedExceptions));
            if (!anyAllowed) return false;
        }

        return true;
    }

    static bool IsLeafAllowed(string component, HashSet<string> allowedIds, HashSet<string> allowedExceptions)
    {
        var withIdx = component.IndexOf(" WITH ", StringComparison.OrdinalIgnoreCase);

        string baseId;
        string? exception;

        if (withIdx >= 0)
        {
            baseId = component[..withIdx].Trim();
            exception = "WITH " + component[(withIdx + " WITH ".Length)..].Trim();
        }
        else
        {
            baseId = component;
            exception = null;
        }

        return allowedIds.Contains(baseId)
            || (exception != null && allowedExceptions.Contains(exception));
    }

    internal static string[] IdPatterns(string[] patterns) => patterns
        .Where(p => !p.StartsWith("author:", StringComparison.OrdinalIgnoreCase))
        .Select(p => p.StartsWith("id:", StringComparison.OrdinalIgnoreCase) ? p["id:".Length..] : p)
        .ToArray();

    internal static int ApplyAllow(LicenseEntry[] entries, string[] allowed)
    {
        var allowedIds = new HashSet<string>(
            allowed.Where(a => !a.StartsWith("WITH ", StringComparison.OrdinalIgnoreCase)),
            StringComparer.OrdinalIgnoreCase);
        var allowedExceptions = new HashSet<string>(
            allowed.Where(a => a.StartsWith("WITH ", StringComparison.OrdinalIgnoreCase)),
            StringComparer.OrdinalIgnoreCase);

        var violations = entries.Where(e => !IsAllowed(e.License, allowedIds, allowedExceptions)).ToArray();

        if (violations.Length == 0)
            return 0;

        foreach (var v in violations)
            Log.Warning("Not in allowlist: {id} {version} [{license}]", v.Id, v.Version, v.License);

        return 1;
    }

    internal static NugetMetadata[] ApplyIgnore(NugetMetadata[] nugets, string[] patterns)
    {
        var idPats = IdPatterns(patterns);
        var authorPats = patterns
            .Where(p => p.StartsWith("author:", StringComparison.OrdinalIgnoreCase))
            .Select(p => p["author:".Length..])
            .ToArray();

        bool Matches(string value, string[] pats) => pats.Any(p =>
            FileSystemName.MatchesSimpleExpression(p, value, ignoreCase: true));

        return nugets.Where(n =>
        {
            if (Matches(n.Id, idPats))
            {
                Log.Information("Ignored: {id} {version} (id match)", n.Id, n.Version);
                return false;
            }
            if (n.Authors.Any() && n.Authors.All(a => Matches(a, authorPats)))
            {
                Log.Information("Ignored: {id} {version} (author match)", n.Id, n.Version);
                return false;
            }
            return true;
        }).ToArray();
    }
}
