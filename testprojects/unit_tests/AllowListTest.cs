namespace unit_tests;

[TestClass]
public class AllowListTest
{
    static HashSet<string> Ids(params string[] ids)
        => new(ids, StringComparer.OrdinalIgnoreCase);

    static HashSet<string> Exceptions(params string[] exs)
        => new(exs, StringComparer.OrdinalIgnoreCase);

    // ── IsAllowed ──────────────────────────────────────────────────────────

    [TestMethod] public void Single_allowed_id()
        => Assert.IsTrue(nulic.PackageFilter.IsAllowed("MIT", Ids("MIT"), Exceptions()));

    [TestMethod] public void Single_disallowed_id()
        => Assert.IsFalse(nulic.PackageFilter.IsAllowed("GPL-2.0-only", Ids("MIT"), Exceptions()));

    [TestMethod] public void NOASSERTION_is_never_allowed()
        => Assert.IsFalse(nulic.PackageFilter.IsAllowed(
            nulic.NulicLicense.NOASSERTION, Ids("NOASSERTION"), Exceptions()));

    [TestMethod] public void AND_both_allowed()
        => Assert.IsTrue(nulic.PackageFilter.IsAllowed(
            "MIT AND Apache-2.0", Ids("MIT", "Apache-2.0"), Exceptions()));

    [TestMethod] public void AND_one_not_allowed()
        => Assert.IsFalse(nulic.PackageFilter.IsAllowed(
            "MIT AND GPL-2.0-only", Ids("MIT"), Exceptions()));

    [TestMethod] public void OR_any_one_allowed_is_sufficient()
        // MIT OR Apache-2.0 with only MIT on allowlist → true (can choose MIT)
        => Assert.IsTrue(nulic.PackageFilter.IsAllowed(
            "MIT OR Apache-2.0", Ids("MIT"), Exceptions()));

    [TestMethod] public void OR_none_allowed()
        => Assert.IsFalse(nulic.PackageFilter.IsAllowed(
            "GPL-2.0-only OR LGPL-2.1-only", Ids("MIT"), Exceptions()));

    [TestMethod] public void WITH_exception_allowed_without_base_id()
        // Allow list only contains the exception, not the base GPL id
        => Assert.IsTrue(nulic.PackageFilter.IsAllowed(
            "GPL-2.0-only WITH Classpath-exception-2.0",
            Ids(),
            Exceptions("WITH Classpath-exception-2.0")));

    [TestMethod] public void WITH_base_id_allowed()
        // If GPL itself is on the allow list, WITH exception is redundant but should still pass
        => Assert.IsTrue(nulic.PackageFilter.IsAllowed(
            "GPL-2.0-only WITH Classpath-exception-2.0",
            Ids("GPL-2.0-only"),
            Exceptions()));

    [TestMethod] public void WITH_neither_allowed()
        => Assert.IsFalse(nulic.PackageFilter.IsAllowed(
            "GPL-2.0-only WITH eCos-exception-2.0",
            Ids("MIT"),
            Exceptions("WITH Classpath-exception-2.0")));

    [TestMethod] public void Case_insensitive_id_match()
        => Assert.IsTrue(nulic.PackageFilter.IsAllowed("mit", Ids("MIT"), Exceptions()));

    // ── IdPatterns ─────────────────────────────────────────────────────────

    [TestMethod] public void IdPatterns_bare_patterns_passed_through()
    {
        var result = nulic.PackageFilter.IdPatterns(["foo*", "bar"]);
        CollectionAssert.AreEquivalent(new[] { "foo*", "bar" }, result);
    }

    [TestMethod] public void IdPatterns_id_prefix_stripped()
    {
        var result = nulic.PackageFilter.IdPatterns(["id:Foo.*", "id:Bar"]);
        CollectionAssert.AreEquivalent(new[] { "Foo.*", "Bar" }, result);
    }

    [TestMethod] public void IdPatterns_author_patterns_excluded()
    {
        var result = nulic.PackageFilter.IdPatterns(["id:Foo.*", "author:Alice*", "Bar"]);
        CollectionAssert.AreEquivalent(new[] { "Foo.*", "Bar" }, result);
    }

    [TestMethod] public void IdPatterns_empty_input_returns_empty()
    {
        var result = nulic.PackageFilter.IdPatterns([]);
        Assert.AreEqual(0, result.Length);
    }

    [TestMethod] public void IdPatterns_only_author_patterns_returns_empty()
    {
        var result = nulic.PackageFilter.IdPatterns(["author:*Leif*", "author:Alice"]);
        Assert.AreEqual(0, result.Length);
    }

    // ── IsAllowed extra edges ──────────────────────────────────────────────

    [TestMethod] public void Empty_allow_list_disallows_everything()
        => Assert.IsFalse(nulic.PackageFilter.IsAllowed("MIT", Ids(), Exceptions()));

    [TestMethod] public void AND_three_parts_all_allowed()
        => Assert.IsTrue(nulic.PackageFilter.IsAllowed(
            "MIT AND Apache-2.0 AND BSD-3-Clause",
            Ids("MIT", "Apache-2.0", "BSD-3-Clause"), Exceptions()));

    [TestMethod] public void AND_three_parts_last_disallowed()
        => Assert.IsFalse(nulic.PackageFilter.IsAllowed(
            "MIT AND Apache-2.0 AND GPL-2.0-only",
            Ids("MIT", "Apache-2.0"), Exceptions()));

    // ── ApplyIgnore ────────────────────────────────────────────────────────

    static nulic.NugetMetadata MakePackage(string id, string? version = null, string[]? authors = null)
        => nulic.NugetMetadata.FromOverride(new nulic.PackageOverride
        {
            Id = id,
            Version = version ?? "1.0.0",
            Authors = authors,
        });

    [TestMethod] public void ApplyIgnore_bare_wildcard_matches_id()
    {
        var pkgs = new[] { MakePackage("Foo.Bar"), MakePackage("Other.Pkg") };
        var result = nulic.PackageFilter.ApplyIgnore(pkgs, ["Foo.*"]);
        Assert.AreEqual(1, result.Length);
        Assert.AreEqual("Other.Pkg", result[0].Id);
    }

    [TestMethod] public void ApplyIgnore_id_prefix_wildcard_matches()
    {
        var pkgs = new[] { MakePackage("My.Library"), MakePackage("Unrelated") };
        var result = nulic.PackageFilter.ApplyIgnore(pkgs, ["id:My.*"]);
        Assert.AreEqual(1, result.Length);
        Assert.AreEqual("Unrelated", result[0].Id);
    }

    [TestMethod] public void ApplyIgnore_author_pattern_matches_when_all_authors_match()
    {
        var pkgs = new[] { MakePackage("Pkg.A", authors: ["Alice"]), MakePackage("Pkg.B", authors: ["Bob"]) };
        var result = nulic.PackageFilter.ApplyIgnore(pkgs, ["author:Alice"]);
        Assert.AreEqual(1, result.Length);
        Assert.AreEqual("Pkg.B", result[0].Id);
    }

    [TestMethod] public void ApplyIgnore_author_pattern_skips_when_not_all_authors_match()
    {
        // Pkg.A has two authors — only one matches "Alice", so it should NOT be ignored
        var pkgs = new[] { MakePackage("Pkg.A", authors: ["Alice", "Bob"]) };
        var result = nulic.PackageFilter.ApplyIgnore(pkgs, ["author:Alice"]);
        Assert.AreEqual(1, result.Length, "Should NOT be ignored when not all authors match");
    }

    [TestMethod] public void ApplyIgnore_no_patterns_returns_all()
    {
        var pkgs = new[] { MakePackage("A"), MakePackage("B") };
        var result = nulic.PackageFilter.ApplyIgnore(pkgs, []);
        Assert.AreEqual(2, result.Length);
    }

    [TestMethod] public void ApplyIgnore_case_insensitive()
    {
        var pkgs = new[] { MakePackage("FOO.Bar") };
        var result = nulic.PackageFilter.ApplyIgnore(pkgs, ["foo.*"]);
        Assert.AreEqual(0, result.Length, "Pattern matching should be case-insensitive");
    }

    // ── ApplyAllow (end-to-end) ────────────────────────────────────────────

    static nulic.LicenseEntry Entry(string id, string license)
        => new(id, "1.0.0", [], null, null, license, null, []);

    [TestMethod] public void ApplyAllow_all_pass_returns_zero()
    {
        var entries = new[] { Entry("A", "MIT"), Entry("B", "Apache-2.0") };
        Assert.AreEqual(0, nulic.PackageFilter.ApplyAllow(entries, ["MIT", "Apache-2.0"]));
    }

    [TestMethod] public void ApplyAllow_violation_returns_one()
    {
        var entries = new[] { Entry("A", "MIT"), Entry("B", "GPL-2.0-only") };
        Assert.AreEqual(1, nulic.PackageFilter.ApplyAllow(entries, ["MIT"]));
    }

    [TestMethod] public void ApplyAllow_noassertion_is_a_violation()
    {
        var entries = new[] { Entry("A", nulic.NulicLicense.NOASSERTION) };
        Assert.AreEqual(1, nulic.PackageFilter.ApplyAllow(entries, ["MIT", "NOASSERTION"]));
    }

    [TestMethod] public void ApplyAllow_empty_entries_returns_zero()
    {
        Assert.AreEqual(0, nulic.PackageFilter.ApplyAllow([], ["MIT"]));
    }

    [TestMethod] public void ApplyAllow_empty_allowed_list_flags_everything()
    {
        var entries = new[] { Entry("A", "MIT") };
        Assert.AreEqual(1, nulic.PackageFilter.ApplyAllow(entries, []));
    }
}
