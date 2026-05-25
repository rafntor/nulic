namespace unit_tests;

/// <summary>
/// Verifies that each bundled license text in CommonLicenses can be identified by
/// LookupSpdxIDByKeywords — protects against keyword regressions.
/// </summary>
[TestClass]
public class CommonLicensesTest
{
    [TestMethod]
    public void Licenses_dictionary_contains_expected_keys()
    {
        var keys = nulic.CommonLicenses.Licenses.Keys.ToHashSet();
        foreach (var expected in new[] { "MIT", "Apache-2.0", "BSD-3-Clause", "GPL-3.0-only", "MS-PL" })
            Assert.IsTrue(keys.Contains(expected), $"CommonLicenses missing '{expected}'");
    }

    [TestMethod]
    public void All_bundled_license_texts_are_non_empty()
    {
        foreach (var kv in nulic.CommonLicenses.Licenses)
            Assert.IsTrue(kv.Value.Length > 100,
                $"Bundled text for '{kv.Key}' is suspiciously short ({kv.Value.Length} chars)");
    }

    // Each bundled license text should be detected as the correct SPDX ID.
    // This is a regression guard: if keyword rules change, these will catch false negatives.

    [TestMethod] public void MIT_text_self_identifies()
        => AssertSelfIdentifies("MIT");

    [TestMethod] public void Apache20_text_self_identifies()
        => AssertSelfIdentifies("Apache-2.0");

    [TestMethod] public void BSD3_text_self_identifies()
        => AssertSelfIdentifies("BSD-3-Clause");

    [TestMethod] public void GPL3_text_self_identifies()
        => AssertSelfIdentifies("GPL-3.0-only");

    [TestMethod] public void MSPL_text_self_identifies()
        => AssertSelfIdentifies("MS-PL");

    static void AssertSelfIdentifies(string spdxId)
    {
        var text = nulic.CommonLicenses.Licenses[spdxId];
        var detected = nulic.LicenseAnalysis.LookupSpdxIDByKeywords(text);
        // Composite expressions are OK as long as they contain the expected ID
        Assert.IsNotNull(detected, $"No SPDX ID detected in bundled '{spdxId}' text");
        StringAssert.Contains(detected, spdxId,
            $"Bundled '{spdxId}' text detected as '{detected}' — expected to contain '{spdxId}'");
    }
}
