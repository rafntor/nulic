using System.Text.Json;

namespace nulic;

internal static class MarkdownReport
{
    static readonly JsonSerializerOptions _readOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    public static async Task Write(DirectoryInfo license_root)
    {
        var jsonPath = Path.Join(license_root.FullName, "licenses.json");
        var entries = JsonSerializer.Deserialize<LicenseEntry[]>(
            await File.ReadAllTextAsync(jsonPath), _readOptions) ?? [];

        var sorted = entries.OrderBy(e => e.Id, StringComparer.OrdinalIgnoreCase).ToArray();
        var outfile = Path.Join(license_root.FullName, "licenses.md");

        await using var sw = new StreamWriter(outfile, append: false, System.Text.Encoding.UTF8);

        await WriteTable(sw, sorted, license_root);
        await WriteSections(sw, sorted, license_root);
    }

    static async Task WriteTable(StreamWriter sw, LicenseEntry[] entries, DirectoryInfo license_root)
    {
        await sw.WriteLineAsync("# Third-Party License Notices");
        await sw.WriteLineAsync();
        await sw.WriteLineAsync("| Package | Version | License | Authors |");
        await sw.WriteLineAsync("|---------|---------|---------|---------|");

        foreach (var e in entries)
        {
            var name = e.ProjectUrl is string pu
                ? $"[{Escape(e.Id)}]({pu})"
                : Escape(e.Id);
            var authors = string.Join(", ", e.Authors);
            var license = FormatLicense(e.License, e.LicenseFiles, license_root);

            await sw.WriteLineAsync($"| {name} | {e.Version} | {license} | {Escape(authors)} |");
        }

        await sw.WriteLineAsync();
    }

    static async Task WriteSections(StreamWriter sw, LicenseEntry[] entries, DirectoryInfo license_root)
    {
        // Group by license expression; NOASSERTION goes last
        var groups = entries
            .GroupBy(e => e.License)
            .OrderBy(g => g.Key == NulicLicense.NOASSERTION ? 1 : 0)
            .ThenBy(g => g.Key, StringComparer.OrdinalIgnoreCase);

        await sw.WriteLineAsync("---");
        await sw.WriteLineAsync();

        foreach (var group in groups)
        {
            await sw.WriteLineAsync($"## {group.Key}");
            await sw.WriteLineAsync();

            var packageList = string.Join(", ", group.Select(e => $"{e.Id} {e.Version}"));
            await sw.WriteLineAsync($"**Packages:** {packageList}");
            await sw.WriteLineAsync();

            var copyrights = group.Select(e => e.Copyright).Where(c => !string.IsNullOrWhiteSpace(c)).Distinct().ToArray();
            if (copyrights.Length > 0)
            {
                foreach (var c in copyrights)
                    await sw.WriteLineAsync($"> {Escape(c!)}");
                await sw.WriteLineAsync();
            }

            // Collect unique license files across all packages in this group
            var allFiles = group
                .SelectMany(e => e.LicenseFiles)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(f => f);

            bool hasFiles = false;
            foreach (var file in allFiles)
            {
                var link = file.Replace('\\', '/');
                await sw.WriteAsync($"[{Path.GetFileName(file)}]({link})");
                await sw.WriteAsync("  ");
                hasFiles = true;
            }

            if (!hasFiles)
                await sw.WriteAsync("*(no license file available)*");

            await sw.WriteLineAsync();
            await sw.WriteLineAsync();
            await sw.WriteLineAsync("---");
            await sw.WriteLineAsync();
        }
    }

    static string Escape(string text) =>
        text.Replace("|", "\\|").Replace("\r", "").Replace("\n", " ");

    static bool HasLicenseRef(string license) =>
        license.Contains("LicenseRef-", StringComparison.OrdinalIgnoreCase);

    // Format the license column: link each SPDX component to its local file in licenses/.
    // Falls back to plain text if no matching file is found — never links to external URLs.
    static string FormatLicense(string license, string[] licenseFiles, DirectoryInfo license_root)
    {
        if (HasLicenseRef(license)) return Escape(license);

        if (license.Contains(" AND ") || license.Contains(" OR "))
            return FormatCompound(license, licenseFiles, license_root);

        var url = FindFileUrl(license, licenseFiles, license_root, singleFileFallback: true);
        return url != null ? $"[{Escape(license)}]({url})" : Escape(license);
    }

    static string FormatCompound(string license, string[] licenseFiles, DirectoryInfo license_root)
    {
        var sb = new System.Text.StringBuilder();
        var parts = System.Text.RegularExpressions.Regex.Split(license, @"( AND | OR )");
        foreach (var part in parts)
        {
            if (part is " AND " or " OR ") { sb.Append(part); continue; }
            if (HasLicenseRef(part)) { sb.Append(Escape(part)); continue; }
            var url = FindFileUrl(part, licenseFiles, license_root, singleFileFallback: false);
            sb.Append(url != null ? $"[{Escape(part)}]({url})" : Escape(part));
        }
        return sb.ToString();
    }

    static string? FindFileUrl(string spdxId, string[] licenseFiles, DirectoryInfo license_root, bool singleFileFallback)
    {
        // 1. Package-specific file whose basename matches the SPDX ID
        var byName = licenseFiles.FirstOrDefault(f =>
            string.Equals(Path.GetFileNameWithoutExtension(f), spdxId, StringComparison.OrdinalIgnoreCase));
        if (byName != null) return byName.Replace('\\', '/');

        // 2. Shared file at license root: e.g. licenses/MIT.txt
        var shared = $"{spdxId}.txt";
        if (File.Exists(Path.Join(license_root.FullName, shared)))
            return shared;

        // 3. Single-file fallback for packages with one non-canonically-named file
        if (singleFileFallback && licenseFiles.Length == 1)
            return licenseFiles[0].Replace('\\', '/');

        return null;
    }
}
