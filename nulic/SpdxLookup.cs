using System.Text.Json;

namespace nulic;

internal class SpdxLookup
{
    public static Task<NulicLicense> DownloadLicense(string spdx_id, DirectoryInfo destination)
    {
        // LicenseRef-* are user-defined identifiers — no canonical text exists on spdx.org
        if (spdx_id.StartsWith("LicenseRef-", StringComparison.OrdinalIgnoreCase))
            return Task.FromResult(NulicLicense.NotFound);

        var file = new FileInfo(Path.Join(destination.FullName, $"{spdx_id}.txt"));

        var text_getter = () => FindOrDownloadLicense(spdx_id);

        return NulicLicense.FindOrCreate(text_getter, file, spdx_id: spdx_id);
    }
    public static Task<NulicLicense> DownloadException(string exception_id, DirectoryInfo destination)
    {
        var file = new FileInfo(Path.Join(destination.FullName, $"{exception_id}.txt"));

        var text_getter = () => DownloadSpdxException(exception_id);

        return NulicLicense.FindOrCreate(text_getter, file);
    }
    static Task<string> FindOrDownloadLicense(string spdx_id)
    {
        if (CommonLicenses.Licenses.TryGetValue(spdx_id, out var license_text))
            return Task.FromResult(license_text);

        return DownloadSpdxLicense(spdx_id);
    }
    static async Task<string> DownloadSpdxLicense(string spdx_id)
    {
        var url = new Uri($"https://spdx.org/licenses/{spdx_id}.json");

        var json = await Program.HttpClient.GetStringAsync(url);

        using var doc = JsonDocument.Parse(json);

        if (doc.RootElement.TryGetProperty("licenseText", out var tok) &&
            tok.GetString() is { Length: > 0 } text)
            return text;

        throw new JsonException($"SpdxLookup: Failed to extract 'licenseText' for '{spdx_id}'.");
    }
    static async Task<string> DownloadSpdxException(string exception_id)
    {
        var url = new Uri($"https://spdx.org/licenses/exceptions/{exception_id}.json");

        var json = await Program.HttpClient.GetStringAsync(url);

        using var doc = JsonDocument.Parse(json);

        if (doc.RootElement.TryGetProperty("licenseExceptionText", out var tok) &&
            tok.GetString() is { Length: > 0 } text)
            return text;

        throw new JsonException($"SpdxLookup: Failed to extract 'licenseExceptionText' for '{exception_id}'.");
    }
}
