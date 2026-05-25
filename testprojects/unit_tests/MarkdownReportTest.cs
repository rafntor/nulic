using System.Text.Json;

namespace unit_tests;

[TestClass]
public class MarkdownReportTest
{
    static readonly JsonSerializerOptions _writeOptions = new()
    {
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    static DirectoryInfo WriteLicensesJson(string tmpDir, nulic.LicenseEntry[] entries)
    {
        var dir = new DirectoryInfo(tmpDir);
        dir.Create();
        File.WriteAllText(Path.Join(tmpDir, "nulic-packages.json"),
            JsonSerializer.Serialize(entries, _writeOptions));
        return dir;
    }

    static async Task<string> RunAndRead(nulic.LicenseEntry[] entries)
    {
        var tmp = Directory.CreateTempSubdirectory("nulic_md_");
        try
        {
            var dir = WriteLicensesJson(tmp.FullName, entries);
            await nulic.MarkdownReport.Write(dir);
            return await File.ReadAllTextAsync(Path.Join(tmp.FullName, "third-party-notices.md"));
        }
        finally { tmp.Delete(recursive: true); }
    }

    // ── Table ──────────────────────────────────────────────────────────────

    [TestMethod]
    public async Task Table_contains_header()
    {
        var md = await RunAndRead([
            new("Foo.Bar", "1.0.0", ["Alice"], null, null, "MIT", null, [])
        ]);
        StringAssert.Contains(md, "# Third-Party License Notices");
        StringAssert.Contains(md, "| Package | Version | License | Authors |");
    }

    [TestMethod]
    public async Task Table_row_plain_name_and_license()
    {
        var md = await RunAndRead([
            new("Foo.Bar", "1.2.3", ["Alice"], null, null, "MIT", null, [])
        ]);
        StringAssert.Contains(md, "| Foo.Bar | 1.2.3 | MIT | Alice |");
    }

    [TestMethod]
    public async Task Table_row_with_project_url_becomes_link()
    {
        var md = await RunAndRead([
            new("Foo.Bar", "1.0.0", ["Alice"], "https://example.com", null, "MIT", null, [])
        ]);
        StringAssert.Contains(md, "[Foo.Bar](https://example.com)");
    }

    [TestMethod]
    public async Task Table_row_with_license_url_ignored_in_table()
    {
        // LicenseUrl is stored in JSON for tooling, but table links only to local files
        var md = await RunAndRead([
            new("Foo.Bar", "1.0.0", ["Alice"], null, null, "MIT", "https://spdx.org/licenses/MIT", [])
        ]);
        // No local file → plain text, no link
        Assert.IsFalse(md.Contains("[MIT]("), "no local file means no link, even if LicenseUrl present");
        StringAssert.Contains(md, "MIT");
    }

    [TestMethod]
    public async Task Table_embedded_file_used_as_link()
    {
        var md = await RunAndRead([
            new("Foo.Bar", "1.0.0", ["Alice"], null, null, "MIT",
                "https://licenses.nuget.org/MIT", ["MIT.txt"])
        ]);
        StringAssert.Contains(md, "[MIT](MIT.txt)");
    }

    [TestMethod]
    public async Task Table_shared_root_file_linked_when_package_has_no_own_file()
    {
        // Package declares MIT via expression — LicenseFiles is empty.
        // But MIT.txt was downloaded to the shared licenses root for another package.
        var tmp = Directory.CreateTempSubdirectory("nulic_md_");
        try
        {
            var dir = WriteLicensesJson(tmp.FullName, [
                new("Foo.Bar", "1.0.0", [], null, null, "MIT", null, [])
            ]);
            // Shared file already present (downloaded for some other MIT package)
            File.WriteAllText(Path.Join(tmp.FullName, "MIT.txt"), "MIT license text");

            await nulic.MarkdownReport.Write(dir);
            var md = await File.ReadAllTextAsync(Path.Join(tmp.FullName, "third-party-notices.md"));

            StringAssert.Contains(md, "[MIT](MIT.txt)",
                "shared MIT.txt at root should be used even when LicenseFiles is empty");
        }
        finally { tmp.Delete(recursive: true); }
    }

    [TestMethod]
    public async Task Table_single_unnamed_file_used_as_fallback()
    {
        // Package has "LICENSE" (no SPDX name) — single file fallback applies
        var md = await RunAndRead([
            new("Pkg.A", "1.0.0", [], null, null, "MIT", null, [@"Pkg.A\1.0\LICENSE"])
        ]);
        StringAssert.Contains(md, "[MIT](Pkg.A/1.0/LICENSE)");
    }

    [TestMethod]
    public async Task Table_multiple_authors_joined_with_comma()
    {
        var md = await RunAndRead([
            new("Foo.Bar", "1.0.0", ["Alice", "Bob"], null, null, "MIT", null, [])
        ]);
        StringAssert.Contains(md, "Alice, Bob");
    }

    [TestMethod]
    public async Task Table_pipe_in_name_is_escaped()
    {
        var md = await RunAndRead([
            new("Foo|Bar", "1.0.0", ["Alice"], null, null, "MIT", null, [])
        ]);
        StringAssert.Contains(md, @"Foo\|Bar");
    }

    [TestMethod]
    public async Task Table_entries_sorted_case_insensitively_by_id()
    {
        var md = await RunAndRead([
            new("zlib", "1.0", [], null, null, "Zlib", null, []),
            new("Newtonsoft.Json", "13.0", [], null, null, "MIT", null, []),
            new("AngleSharp", "1.0", [], null, null, "MIT", null, []),
        ]);
        var nIdx  = md.IndexOf("Newtonsoft.Json", StringComparison.Ordinal);
        var aIdx  = md.IndexOf("AngleSharp",      StringComparison.Ordinal);
        var zIdx  = md.IndexOf("zlib",             StringComparison.Ordinal);
        // AngleSharp < Newtonsoft.Json < zlib
        Assert.IsTrue(aIdx < nIdx, "AngleSharp should come before Newtonsoft.Json");
        Assert.IsTrue(nIdx < zIdx, "Newtonsoft.Json should come before zlib");
    }

    // ── Sections ───────────────────────────────────────────────────────────

    [TestMethod]
    public async Task Sections_grouped_by_license()
    {
        var md = await RunAndRead([
            new("A", "1.0", [], null, null, "MIT",        null, []),
            new("B", "1.0", [], null, null, "Apache-2.0", null, []),
            new("C", "1.0", [], null, null, "MIT",        null, []),
        ]);
        // MIT section should list both A and C
        var mitIdx = md.IndexOf("## MIT (");
        Assert.IsTrue(mitIdx >= 0, "MIT section missing");
        var mitSection = md[mitIdx..];
        StringAssert.Contains(mitSection[..mitSection.IndexOf("---")], "A 1.0");
        StringAssert.Contains(mitSection[..mitSection.IndexOf("---")], "C 1.0");
    }

    [TestMethod]
    public async Task Sections_heading_includes_package_count()
    {
        var md = await RunAndRead([
            new("A", "1.0", [], null, null, "MIT", null, []),
            new("B", "1.0", [], null, null, "MIT", null, []),
        ]);
        StringAssert.Contains(md, "## MIT (2 packages)");
    }

    [TestMethod]
    public async Task Sections_heading_singular_for_one_package()
    {
        var md = await RunAndRead([
            new("A", "1.0", [], null, null, "MIT", null, [])
        ]);
        StringAssert.Contains(md, "## MIT (1 package)");
    }

    [TestMethod]
    public async Task Sections_NOASSERTION_appears_last()
    {
        var md = await RunAndRead([
            new("A", "1.0", [], null, null, nulic.NulicLicense.NOASSERTION, null, []),
            new("B", "1.0", [], null, null, "MIT",                          null, []),
        ]);
        var mitIdx          = md.IndexOf("## MIT (",         StringComparison.Ordinal);
        var noAssertionIdx  = md.IndexOf($"## {nulic.NulicLicense.NOASSERTION} (", StringComparison.Ordinal);

        Assert.IsTrue(mitIdx >= 0,         "MIT section missing");
        Assert.IsTrue(noAssertionIdx >= 0, "NOASSERTION section missing");
        Assert.IsTrue(mitIdx < noAssertionIdx, "NOASSERTION should come after MIT");
    }

    [TestMethod]
    public async Task Sections_no_license_file_shows_placeholder()
    {
        var md = await RunAndRead([
            new("A", "1.0", [], null, null, nulic.NulicLicense.NOASSERTION, null, [])
        ]);
        StringAssert.Contains(md, "*(no license file available)*");
    }

    [TestMethod]
    public async Task Sections_license_file_shown_as_link()
    {
        var tmp = Directory.CreateTempSubdirectory("nulic_md_");
        try
        {
            var dir = WriteLicensesJson(tmp.FullName, [
                new("A", "1.0", [], null, null, "MIT", null, ["MIT.txt"])
            ]);
            // Create the referenced file so MarkdownReport doesn't fail on missing files
            File.WriteAllText(Path.Join(tmp.FullName, "MIT.txt"), "MIT license text");

            await nulic.MarkdownReport.Write(dir);
            var md = await File.ReadAllTextAsync(Path.Join(tmp.FullName, "third-party-notices.md"));

            StringAssert.Contains(md, "[MIT.txt](MIT.txt)");
        }
        finally { tmp.Delete(recursive: true); }
    }

    [TestMethod]
    public async Task Sections_dedup_shared_license_file_across_packages()
    {
        var tmp = Directory.CreateTempSubdirectory("nulic_md_");
        try
        {
            var dir = WriteLicensesJson(tmp.FullName, [
                new("A", "1.0", [], null, null, "MIT", null, ["MIT.txt"]),
                new("B", "2.0", [], null, null, "MIT", null, ["MIT.txt"]),
            ]);
            File.WriteAllText(Path.Join(tmp.FullName, "MIT.txt"), "MIT license text");

            await nulic.MarkdownReport.Write(dir);
            var md = await File.ReadAllTextAsync(Path.Join(tmp.FullName, "third-party-notices.md"));

            // MIT.txt should appear exactly once in the sections (deduplicated)
            var mitSection = md[(md.IndexOf("## MIT") + "## MIT".Length)..];
            var occurrences = 0;
            var search = "[MIT.txt]";
            var idx = 0;
            while ((idx = mitSection.IndexOf(search, idx)) >= 0) { occurrences++; idx++; }
            Assert.AreEqual(1, occurrences, "MIT.txt link should appear exactly once (deduplicated)");
        }
        finally { tmp.Delete(recursive: true); }
    }

    [TestMethod]
    public async Task Sections_copyright_shown_as_blockquote()
    {
        var md = await RunAndRead([
            new("A", "1.0", [], null, "Copyright © 2024 Alice", "MIT", null, [])
        ]);
        StringAssert.Contains(md, "> Copyright © 2024 Alice");
    }

    [TestMethod]
    public async Task Empty_entries_produces_valid_empty_report()
    {
        var md = await RunAndRead([]);
        StringAssert.Contains(md, "# Third-Party License Notices");
        // Should not throw
    }

    [TestMethod]
    public async Task Table_empty_authors_produces_no_extra_commas()
    {
        var md = await RunAndRead([
            new("Foo.Bar", "1.0.0", [], null, null, "MIT", null, [])
        ]);
        // Authors column should be empty (no ", " artifact)
        Assert.IsFalse(md.Contains(", ,"), "Should not have extra commas for empty authors");
    }

    [TestMethod]
    public async Task Sections_backslash_path_converted_to_forward_slash_in_link()
    {
        var tmp = Directory.CreateTempSubdirectory("nulic_md_");
        try
        {
            // Simulate a package-specific license file with backslash separator (Windows path)
            var subDir = Path.Join(tmp.FullName, "Pkg.A.1.0");
            Directory.CreateDirectory(subDir);
            File.WriteAllText(Path.Join(subDir, "LICENSE"), "license text");

            var dir = WriteLicensesJson(tmp.FullName, [
                new("Pkg.A", "1.0.0", [], null, null, "MIT", null, [@"Pkg.A.1.0\LICENSE"])
            ]);

            await nulic.MarkdownReport.Write(dir);
            var md = await File.ReadAllTextAsync(Path.Join(tmp.FullName, "third-party-notices.md"));

            // Link URL must use forward slashes
            StringAssert.Contains(md, "(Pkg.A.1.0/LICENSE)");
        }
        finally { tmp.Delete(recursive: true); }
    }

    // ── License link rules ─────────────────────────────────────────────────

    [TestMethod]
    public async Task Table_LicenseRef_is_not_clickable_even_with_url()
    {
        var md = await RunAndRead([
            new("Acme.Sdk", "1.0.0", [], null, null,
                "LicenseRef-Proprietary", "https://acme.com/license", [])
        ]);
        Assert.IsFalse(md.Contains("[LicenseRef-Proprietary]("), "LicenseRef-* must not be a hyperlink");
        StringAssert.Contains(md, "LicenseRef-Proprietary");
    }

    [TestMethod]
    public async Task Table_WITH_expression_links_to_local_file()
    {
        var md = await RunAndRead([
            new("SomeLib", "1.0.0", [], null, null,
                "GPL-2.0-only WITH Classpath-exception-2.0",
                "https://licenses.nuget.org/GPL-2.0-only%20WITH%20Classpath-exception-2.0",
                ["GPL-2.0-only WITH Classpath-exception-2.0.txt"])
        ]);
        StringAssert.Contains(md, "[GPL-2.0-only WITH Classpath-exception-2.0](GPL-2.0-only WITH Classpath-exception-2.0.txt)");
    }

    [TestMethod]
    public async Task Table_WITH_expression_plain_text_when_no_local_file()
    {
        var md = await RunAndRead([
            new("SomeLib", "1.0.0", [], null, null,
                "GPL-2.0-only WITH Classpath-exception-2.0", null, [])
        ]);
        Assert.IsFalse(md.Contains("[GPL-2.0-only WITH Classpath-exception-2.0]("),
            "WITH expression without local file must be plain text");
    }

    [TestMethod]
    public async Task Table_AND_compound_each_component_links_to_its_file()
    {
        var md = await RunAndRead([
            new("metis.net", "3.0.0", [], null, null,
                "Apache-2.0 AND BSD-3-Clause", null,
                ["Apache-2.0.txt", "BSD-3-Clause.txt"])
        ]);
        StringAssert.Contains(md, "[Apache-2.0](Apache-2.0.txt)");
        StringAssert.Contains(md, "[BSD-3-Clause](BSD-3-Clause.txt)");
        StringAssert.Contains(md, " AND ");
    }

    [TestMethod]
    public async Task Table_AND_compound_plain_text_when_no_files()
    {
        var md = await RunAndRead([
            new("Bundle", "1.0.0", [], null, null, "MIT AND Apache-2.0", null, [])
        ]);
        Assert.IsFalse(md.Contains("[MIT AND Apache-2.0]("), "compound without files must be plain text");
        StringAssert.Contains(md, "MIT AND Apache-2.0");
    }

    // ── Summary / Footer / NOASSERTION highlight ──────────────────────────

    [TestMethod]
    public async Task Summary_shows_package_and_license_counts()
    {
        var md = await RunAndRead([
            new("A", "1.0", [], null, null, "MIT",        null, []),
            new("B", "1.0", [], null, null, "Apache-2.0", null, []),
            new("C", "1.0", [], null, null, "MIT",        null, []),
        ]);
        StringAssert.Contains(md, "3 packages");
        StringAssert.Contains(md, "2 unique licenses");
    }

    [TestMethod]
    public async Task Summary_shows_unresolved_count_when_noassertion_present()
    {
        var na = nulic.NulicLicense.NOASSERTION;
        var md = await RunAndRead([
            new("A", "1.0", [], null, null, "MIT", null, []),
            new("B", "1.0", [], null, null, na,    null, []),
        ]);
        StringAssert.Contains(md, "⚠️ 1 unresolved");
    }

    [TestMethod]
    public async Task Summary_no_unresolved_label_when_all_resolved()
    {
        var md = await RunAndRead([
            new("A", "1.0", [], null, null, "MIT", null, [])
        ]);
        Assert.IsFalse(md.Contains("unresolved"), "should not mention unresolved when none");
    }

    [TestMethod]
    public async Task Table_NOASSERTION_row_has_warning_emoji()
    {
        var na = nulic.NulicLicense.NOASSERTION;
        var md = await RunAndRead([
            new("Bad.Pkg", "1.0", [], null, null, na, null, [])
        ]);
        StringAssert.Contains(md, "| ⚠️ Bad.Pkg |");
    }

    [TestMethod]
    public async Task Footer_contains_nulic_and_date()
    {
        var md = await RunAndRead([
            new("A", "1.0", [], null, null, "MIT", null, [])
        ]);
        StringAssert.Contains(md, "[nulic](https://github.com/rafntor/nulic)");
        StringAssert.Contains(md, DateTime.Now.Year.ToString());
    }
}
