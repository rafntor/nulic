namespace nulic;

internal static class MarkdownReport
{
    public static async Task Write(IEnumerable<NugetMetadata> nugets, DirectoryInfo license_root)
    {
        var outfile = Path.Join(license_root.FullName, "licenses.md");
        var sorted = nugets.OrderBy(n => n.Id, StringComparer.OrdinalIgnoreCase).ToArray();

        await using var sw = new StreamWriter(outfile, append: false, System.Text.Encoding.UTF8);

        await WriteTable(sw, sorted);
        await WriteSections(sw, sorted, license_root);
    }

    static async Task WriteTable(StreamWriter sw, NugetMetadata[] nugets)
    {
        await sw.WriteLineAsync("# Third-Party License Notices");
        await sw.WriteLineAsync();
        await sw.WriteLineAsync("| Package | Version | License | Authors |");
        await sw.WriteLineAsync("|---------|---------|---------|---------|");

        foreach (var n in nugets)
        {
            var authors = string.Join(", ", n.Authors);
            var license = n.LicenseUrl is Uri url
                ? $"[{Escape(n.License)}]({url})"
                : Escape(n.License);

            await sw.WriteLineAsync($"| {Escape(n.Id)} | {n.Version} | {license} | {Escape(authors)} |");
        }

        await sw.WriteLineAsync();
    }

    static async Task WriteSections(StreamWriter sw, NugetMetadata[] nugets, DirectoryInfo license_root)
    {
        // Group by license expression; NOASSERTION goes last
        var groups = nugets
            .GroupBy(n => n.License)
            .OrderBy(g => g.Key == NulicLicense.NOASSERTION ? 1 : 0)
            .ThenBy(g => g.Key, StringComparer.OrdinalIgnoreCase);

        await sw.WriteLineAsync("---");
        await sw.WriteLineAsync();

        foreach (var group in groups)
        {
            await sw.WriteLineAsync($"## {group.Key}");
            await sw.WriteLineAsync();

            var packageList = string.Join(", ", group.Select(n => $"{n.Id} {n.Version}"));
            await sw.WriteLineAsync($"**Packages:** {packageList}");
            await sw.WriteLineAsync();

            // Collect unique license files across all packages in this group
            var allFiles = group
                .SelectMany(n => n.LicenseFiles)
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
}
