using Serilog;
using System.Text.Json;

namespace nulic;

internal static class LicenseMerge
{
    static readonly JsonSerializerOptions _readOptions = JsonOptions.Read;

    public static async Task<(LicenseEntry[]? Entries, bool Ok)> Apply(DirectoryInfo license_root, string[] merge_paths)
    {
        var jsonPath = Path.Join(license_root.FullName, "nulic-packages.json");
        var entries = JsonSerializer.Deserialize<LicenseEntry[]>(
            await File.ReadAllTextAsync(jsonPath), _readOptions)!.ToList();

        var keys = entries.Select(e => (e.Id, e.Version)).ToHashSet();
        bool ok = true;

        foreach (var mergePath in merge_paths)
        {
            var mergeDir = new DirectoryInfo(mergePath);
            if (!mergeDir.Exists)
            {
                Log.Error("Merge path does not exist: {path}", mergePath);
                ok = false;
                continue;
            }

            var bJsonPath = Path.Join(mergeDir.FullName, "nulic-packages.json");
            if (!File.Exists(bJsonPath))
            {
                Log.Error("No nulic-packages.json found at: {path}", bJsonPath);
                ok = false;
                continue;
            }

            Log.Information("Merging from {path}", mergePath);

            var bEntries = JsonSerializer.Deserialize<LicenseEntry[]>(
                await File.ReadAllTextAsync(bJsonPath), _readOptions)!;

            foreach (var b in bEntries)
            {
                // Copy license files — paths are relative to the licenses/ folder
                foreach (var relFile in b.LicenseFiles)
                {
                    var src  = Path.Join(mergeDir.FullName, relFile);
                    var dest = Path.Join(license_root.FullName, relFile);

                    if (!File.Exists(src))
                    {
                        Log.Warning("Merge: source file not found: {file}", src);
                        continue;
                    }

                    if (File.Exists(dest))
                    {
                        var srcBytes  = await File.ReadAllBytesAsync(src);
                        var destBytes = await File.ReadAllBytesAsync(dest);
                        if (!srcBytes.SequenceEqual(destBytes))
                        {
                            Log.Error("Merge conflict: {file} exists in both projects with different content", relFile);
                            ok = false;
                        }
                        // else: identical, skip copy
                    }
                    else
                    {
                        Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
                        File.Copy(src, dest);
                        Log.Information("Merge: copied {file}", relFile);
                    }
                }

                // Add package if not already present (dedup by Id+Version)
                if (keys.Add((b.Id, b.Version)))
                    entries.Add(b);
            }
        }

        await File.WriteAllTextAsync(jsonPath, JsonSerializer.Serialize(entries, JsonOptions.Write));

        return (entries.ToArray(), ok);
    }
}
