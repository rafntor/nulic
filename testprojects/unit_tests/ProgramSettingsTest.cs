using System.Text.Json;

namespace unit_tests;

[TestClass]
public class ProgramSettingsTest
{
    // ── SerializeDefault ────────────────────────────────────────────────────

    [TestMethod]
    public void SerializeDefault_is_valid_json()
    {
        var json = nulic.ProgramSettings.SerializeDefault();
        // Should not throw
        var doc = JsonDocument.Parse(json);
        Assert.IsNotNull(doc);
    }

    [TestMethod]
    public void SerializeDefault_has_expected_top_level_keys()
    {
        var doc = JsonDocument.Parse(nulic.ProgramSettings.SerializeDefault());
        var root = doc.RootElement;

        Assert.IsTrue(root.TryGetProperty("exclude", out _), "missing 'exclude'");
        Assert.IsTrue(root.TryGetProperty("ignore",  out _), "missing 'ignore'");
        Assert.IsTrue(root.TryGetProperty("allow",   out _), "missing 'allow'");
        Assert.IsTrue(root.TryGetProperty("overrides", out _), "missing 'overrides'");
    }

    [TestMethod]
    public void SerializeDefault_allow_contains_common_licenses()
    {
        var doc  = JsonDocument.Parse(nulic.ProgramSettings.SerializeDefault());
        var allow = doc.RootElement.GetProperty("allow").EnumerateArray()
            .Select(e => e.GetString()!).ToHashSet(StringComparer.OrdinalIgnoreCase);

        Assert.IsTrue(allow.Contains("MIT"),        "MIT missing from allow");
        Assert.IsTrue(allow.Contains("Apache-2.0"), "Apache-2.0 missing from allow");
        Assert.IsTrue(allow.Contains("BSD-3-Clause"), "BSD-3-Clause missing from allow");
    }

    [TestMethod]
    public void SerializeDefault_exclude_contains_test_pattern()
    {
        var doc     = JsonDocument.Parse(nulic.ProgramSettings.SerializeDefault());
        var exclude = doc.RootElement.GetProperty("exclude").EnumerateArray()
            .Select(e => e.GetString()!).ToArray();

        Assert.IsTrue(exclude.Any(e => e.Contains("test", StringComparison.OrdinalIgnoreCase)),
            "no test exclusion pattern found");
    }

    [TestMethod]
    public void SerializeDefault_overrides_contains_example_entry()
    {
        var doc       = JsonDocument.Parse(nulic.ProgramSettings.SerializeDefault());
        var overrides = doc.RootElement.GetProperty("overrides").EnumerateArray().ToArray();

        Assert.IsTrue(overrides.Length >= 1, "expected at least one override example");
        var first = overrides[0];
        Assert.IsTrue(first.TryGetProperty("id", out var idProp));
        Assert.AreEqual("Longship.Cruises", idProp.GetString());
    }

    // ── Load ────────────────────────────────────────────────────────────────

    [TestMethod]
    public void Load_creates_default_file_when_missing()
    {
        var tmp = Directory.CreateTempSubdirectory("nulic_test_");
        try
        {
            nulic.ProgramSettings.Load(tmp);

            var file = new FileInfo(Path.Join(tmp.FullName, "nulic.json"));
            Assert.IsTrue(file.Exists, "nulic.json should have been created");

            // Created content must be valid JSON
            var doc = JsonDocument.Parse(File.ReadAllText(file.FullName));
            Assert.IsNotNull(doc);
        }
        finally { tmp.Delete(recursive: true); }
    }

    [TestMethod]
    public void Load_reads_existing_file()
    {
        var tmp = Directory.CreateTempSubdirectory("nulic_test_");
        try
        {
            var json = """
                {
                  "allow": ["MIT", "Apache-2.0"],
                  "overrides": []
                }
                """;
            File.WriteAllText(Path.Join(tmp.FullName, "nulic.json"), json);

            nulic.ProgramSettings.Load(tmp);

            Assert.IsNotNull(nulic.ProgramSettings.Settings.Allow);
            CollectionAssert.Contains(nulic.ProgramSettings.Settings.Allow, "MIT");
        }
        finally { tmp.Delete(recursive: true); }
    }

    [TestMethod]
    public void Load_accepts_json_with_comments_and_trailing_commas()
    {
        var tmp = Directory.CreateTempSubdirectory("nulic_test_");
        try
        {
            var jsonc = """
                {
                  // allow list
                  "allow": ["MIT", /* standard */ "Apache-2.0",],
                  "overrides": [],
                }
                """;
            File.WriteAllText(Path.Join(tmp.FullName, "nulic.json"), jsonc);

            // Should not throw
            nulic.ProgramSettings.Load(tmp);

            Assert.IsNotNull(nulic.ProgramSettings.Settings.Allow);
        }
        finally { tmp.Delete(recursive: true); }
    }
}
