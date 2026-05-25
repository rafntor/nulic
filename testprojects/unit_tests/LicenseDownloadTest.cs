namespace unit_tests;

[TestClass]
public class LicenseDownloadTest
{
    // ── NormalizeHost ──────────────────────────────────────────────────────

    [TestMethod] public void NormalizeHost_strips_www_prefix()
        => Assert.AreEqual("github.com",
            nulic.LicenseDownload.NormalizeHost(new Uri("https://www.github.com/foo")));

    [TestMethod] public void NormalizeHost_bare_host_unchanged()
        => Assert.AreEqual("github.com",
            nulic.LicenseDownload.NormalizeHost(new Uri("https://github.com/foo")));

    [TestMethod] public void NormalizeHost_raw_githubusercontent_unchanged()
        => Assert.AreEqual("raw.githubusercontent.com",
            nulic.LicenseDownload.NormalizeHost(new Uri("https://raw.githubusercontent.com/foo/bar")));

    [TestMethod] public void NormalizeHost_subdomain_other_than_www_preserved()
        => Assert.AreEqual("api.example.com",
            nulic.LicenseDownload.NormalizeHost(new Uri("https://api.example.com/path")));

    [TestMethod] public void NormalizeHost_www_only_prefix_not_double_stripped()
        // "www.www.example.com" → only the leading "www." is stripped
        => Assert.AreEqual("www.example.com",
            nulic.LicenseDownload.NormalizeHost(new Uri("https://www.www.example.com/path")));
}
