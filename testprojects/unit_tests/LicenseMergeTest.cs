using System.Text.Json;

namespace unit_tests;

[TestClass]
public class LicenseMergeTest
{
    static readonly JsonSerializerOptions _writeOptions = new()
    {
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };
    static readonly JsonSerializerOptions _readOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    static DirectoryInfo MakeLicenseDir(string tmpRoot, string name,
        nulic.LicenseEntry[] entries, Dictionary<string, string>? files = null)
    {
        var dir = new DirectoryInfo(Path.Join(tmpRoot, name));
        dir.Create();
        File.WriteAllText(Path.Join(dir.FullName, "nulic-packages.json"),
            JsonSerializer.Serialize(entries, _writeOptions));
        foreach (var kv in files ?? [])
        {
            var dest = Path.Join(dir.FullName, kv.Key);
            Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
            File.WriteAllText(dest, kv.Value);
        }
        return dir;
    }

    static async Task<nulic.LicenseEntry[]> ReadJson(DirectoryInfo dir)
    {
        var json = await File.ReadAllTextAsync(Path.Join(dir.FullName, "nulic-packages.json"));
        return JsonSerializer.Deserialize<nulic.LicenseEntry[]>(json, _readOptions)!;
    }

    // ── Apply ──────────────────────────────────────────────────────────────

    [TestMethod]
    public async Task Merge_adds_new_package_from_source()
    {
        var tmp = Directory.CreateTempSubdirectory("nulic_merge_");
        try
        {
            var a = MakeLicenseDir(tmp.FullName, "A", [
                new("Pkg.A", "1.0.0", ["Alice"], null, null, "MIT", null, ["MIT.txt"])
            ], new() { ["MIT.txt"] = "MIT text" });

            var b = MakeLicenseDir(tmp.FullName, "B", [
                new("Pkg.B", "2.0.0", ["Bob"], null, null, "Apache-2.0", null, ["Apache-2.0.txt"])
            ], new() { ["Apache-2.0.txt"] = "Apache text" });

            var (entries, ok) = await nulic.LicenseMerge.Apply(a, [b.FullName]);

            Assert.IsTrue(ok);
            Assert.IsNotNull(entries);
            Assert.AreEqual(2, entries!.Length);
            Assert.IsTrue(entries.Any(e => e.Id == "Pkg.B"), "Pkg.B should be in merged entries");
        }
        finally { tmp.Delete(recursive: true); }
    }

    [TestMethod]
    public async Task Merge_copies_new_license_file_to_target()
    {
        var tmp = Directory.CreateTempSubdirectory("nulic_merge_");
        try
        {
            var a = MakeLicenseDir(tmp.FullName, "A", [
                new("Pkg.A", "1.0.0", [], null, null, "MIT", null, [])
            ]);

            var b = MakeLicenseDir(tmp.FullName, "B", [
                new("Pkg.B", "1.0.0", [], null, null, "Apache-2.0", null, ["Apache-2.0.txt"])
            ], new() { ["Apache-2.0.txt"] = "Apache text" });

            var (_, ok) = await nulic.LicenseMerge.Apply(a, [b.FullName]);

            Assert.IsTrue(ok);
            Assert.IsTrue(File.Exists(Path.Join(a.FullName, "Apache-2.0.txt")),
                "Apache-2.0.txt should be copied to target dir");
        }
        finally { tmp.Delete(recursive: true); }
    }

    [TestMethod]
    public async Task Merge_dedup_same_package_id_and_version()
    {
        var tmp = Directory.CreateTempSubdirectory("nulic_merge_");
        try
        {
            var a = MakeLicenseDir(tmp.FullName, "A", [
                new("Shared.Pkg", "1.0.0", [], null, null, "MIT", null, ["MIT.txt"])
            ], new() { ["MIT.txt"] = "MIT text" });

            var b = MakeLicenseDir(tmp.FullName, "B", [
                new("Shared.Pkg", "1.0.0", [], null, null, "MIT", null, ["MIT.txt"])
            ], new() { ["MIT.txt"] = "MIT text" });

            var (entries, ok) = await nulic.LicenseMerge.Apply(a, [b.FullName]);

            Assert.IsTrue(ok);
            Assert.AreEqual(1, entries!.Length, "Duplicate package should be deduplicated");
        }
        finally { tmp.Delete(recursive: true); }
    }

    [TestMethod]
    public async Task Merge_identical_file_content_is_not_an_error()
    {
        var tmp = Directory.CreateTempSubdirectory("nulic_merge_");
        try
        {
            var a = MakeLicenseDir(tmp.FullName, "A", [
                new("Pkg.A", "1.0.0", [], null, null, "MIT", null, ["MIT.txt"])
            ], new() { ["MIT.txt"] = "same content" });

            var b = MakeLicenseDir(tmp.FullName, "B", [
                new("Pkg.B", "1.0.0", [], null, null, "MIT", null, ["MIT.txt"])
            ], new() { ["MIT.txt"] = "same content" });

            var (_, ok) = await nulic.LicenseMerge.Apply(a, [b.FullName]);

            Assert.IsTrue(ok, "Identical file content should not produce an error");
        }
        finally { tmp.Delete(recursive: true); }
    }

    [TestMethod]
    public async Task Merge_content_conflict_returns_ok_false()
    {
        var tmp = Directory.CreateTempSubdirectory("nulic_merge_");
        try
        {
            var a = MakeLicenseDir(tmp.FullName, "A", [
                new("Pkg.A", "1.0.0", [], null, null, "MIT", null, ["MIT.txt"])
            ], new() { ["MIT.txt"] = "version A" });

            var b = MakeLicenseDir(tmp.FullName, "B", [
                new("Pkg.B", "1.0.0", [], null, null, "MIT", null, ["MIT.txt"])
            ], new() { ["MIT.txt"] = "version B — different!" });

            var (_, ok) = await nulic.LicenseMerge.Apply(a, [b.FullName]);

            Assert.IsFalse(ok, "Content mismatch should produce ok=false");
        }
        finally { tmp.Delete(recursive: true); }
    }

    [TestMethod]
    public async Task Merge_missing_source_dir_returns_ok_false()
    {
        var tmp = Directory.CreateTempSubdirectory("nulic_merge_");
        try
        {
            var a = MakeLicenseDir(tmp.FullName, "A", []);

            var (_, ok) = await nulic.LicenseMerge.Apply(a, [@"C:\nonexistent\path\that\does\not\exist"]);

            Assert.IsFalse(ok, "Missing source directory should produce ok=false");
        }
        finally { tmp.Delete(recursive: true); }
    }

    [TestMethod]
    public async Task Merge_missing_licenses_json_returns_ok_false()
    {
        var tmp = Directory.CreateTempSubdirectory("nulic_merge_");
        try
        {
            var a = MakeLicenseDir(tmp.FullName, "A", []);

            // B exists as directory but has no nulic-packages.json
            var b = new DirectoryInfo(Path.Join(tmp.FullName, "B"));
            b.Create();

            var (_, ok) = await nulic.LicenseMerge.Apply(a, [b.FullName]);

            Assert.IsFalse(ok, "Missing nulic-packages.json should produce ok=false");
        }
        finally { tmp.Delete(recursive: true); }
    }

    [TestMethod]
    public async Task Merge_merged_json_is_written_back()
    {
        var tmp = Directory.CreateTempSubdirectory("nulic_merge_");
        try
        {
            var a = MakeLicenseDir(tmp.FullName, "A", [
                new("Pkg.A", "1.0.0", [], null, null, "MIT", null, [])
            ]);

            var b = MakeLicenseDir(tmp.FullName, "B", [
                new("Pkg.B", "2.0.0", [], null, null, "Apache-2.0", null, [])
            ]);

            await nulic.LicenseMerge.Apply(a, [b.FullName]);

            var merged = await ReadJson(a);
            Assert.AreEqual(2, merged.Length, "nulic-packages.json should contain both packages");
        }
        finally { tmp.Delete(recursive: true); }
    }

    [TestMethod]
    public async Task Merge_multiple_sources_combined()
    {
        var tmp = Directory.CreateTempSubdirectory("nulic_merge_");
        try
        {
            var a = MakeLicenseDir(tmp.FullName, "A", [
                new("Pkg.A", "1.0.0", [], null, null, "MIT", null, [])
            ]);
            var b = MakeLicenseDir(tmp.FullName, "B", [
                new("Pkg.B", "1.0.0", [], null, null, "MIT", null, [])
            ]);
            var c = MakeLicenseDir(tmp.FullName, "C", [
                new("Pkg.C", "1.0.0", [], null, null, "Apache-2.0", null, [])
            ]);

            var (entries, ok) = await nulic.LicenseMerge.Apply(a, [b.FullName, c.FullName]);

            Assert.IsTrue(ok);
            Assert.AreEqual(3, entries!.Length, "All 3 packages should be in merged result");
        }
        finally { tmp.Delete(recursive: true); }
    }

    [TestMethod]
    public async Task Merge_missing_source_file_warns_but_ok_stays_true()
    {
        // B's JSON references a file that doesn't exist on disk — should warn but not fail
        var tmp = Directory.CreateTempSubdirectory("nulic_merge_");
        try
        {
            var a = MakeLicenseDir(tmp.FullName, "A", []);

            var b = MakeLicenseDir(tmp.FullName, "B", [
                new("Pkg.B", "1.0.0", [], null, null, "MIT", null, ["MIT.txt"])
                // note: MIT.txt is NOT created
            ]);

            var (entries, ok) = await nulic.LicenseMerge.Apply(a, [b.FullName]);

            Assert.IsTrue(ok, "Missing source file is a warning, not an error");
            Assert.AreEqual(1, entries!.Length, "Package should still be merged even if file is missing");
        }
        finally { tmp.Delete(recursive: true); }
    }

    [TestMethod]
    public async Task Merge_creates_subdirectory_for_nested_license_file()
    {
        // License files can live in subdirectories, e.g. "Pkg.B/1.0/LICENSE"
        var tmp = Directory.CreateTempSubdirectory("nulic_merge_");
        try
        {
            var a = MakeLicenseDir(tmp.FullName, "A", []);

            var nestedRelPath = Path.Join("Pkg.B", "1.0", "LICENSE");
            var b = MakeLicenseDir(tmp.FullName, "B", [
                new("Pkg.B", "1.0.0", [], null, null, "MIT", null, [nestedRelPath])
            ], new() { [nestedRelPath] = "license text" });

            var (_, ok) = await nulic.LicenseMerge.Apply(a, [b.FullName]);

            Assert.IsTrue(ok);
            Assert.IsTrue(File.Exists(Path.Join(a.FullName, nestedRelPath)),
                "Intermediate subdirectory should be created and file copied");
        }
        finally { tmp.Delete(recursive: true); }
    }
}
