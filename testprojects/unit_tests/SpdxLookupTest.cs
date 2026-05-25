namespace unit_tests;

[TestClass]
public class SpdxLookupTest
{
    // ── LicenseRef-* short-circuit ─────────────────────────────────────────
    // Bug fixed: before the fix, SpdxLookup.DownloadLicense would create a NulicLicense
    // object and attempt an HTTP call to spdx.org for LicenseRef-* identifiers (which don't
    // exist there), resulting in phantom file references in licenses.json.

    [TestMethod]
    public async Task LicenseRef_returns_NotFound_without_network()
    {
        var tmp = Directory.CreateTempSubdirectory("nulic_spdx_");
        try
        {
            var result = await nulic.SpdxLookup.DownloadLicense("LicenseRef-Softing-U-V2", new DirectoryInfo(tmp.FullName));
            Assert.IsTrue(result.IsNotFound,
                "LicenseRef-* should return the NotFound sentinel — no HTTP call, no file created");
        }
        finally { tmp.Delete(recursive: true); }
    }

    [TestMethod]
    public async Task LicenseRef_prefix_is_case_insensitive()
    {
        var tmp = Directory.CreateTempSubdirectory("nulic_spdx_");
        try
        {
            var result = await nulic.SpdxLookup.DownloadLicense("licenseref-custom", new DirectoryInfo(tmp.FullName));
            Assert.IsTrue(result.IsNotFound, "LicenseRef-* check should be case-insensitive");
        }
        finally { tmp.Delete(recursive: true); }
    }

    [TestMethod]
    public async Task LicenseRef_no_file_created_in_destination()
    {
        var tmp = Directory.CreateTempSubdirectory("nulic_spdx_");
        try
        {
            await nulic.SpdxLookup.DownloadLicense("LicenseRef-ProprietaryAcme", new DirectoryInfo(tmp.FullName));
            var files = tmp.GetFiles();
            Assert.AreEqual(0, files.Length, "No files should be created for LicenseRef-* identifiers");
        }
        finally { tmp.Delete(recursive: true); }
    }
}
