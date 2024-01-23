using Newtonsoft.Json.Linq;
using NuGet.Protocol;
using System.ComponentModel.DataAnnotations;
using System.Net.Http.Headers;

namespace nulic;

internal class SpdxLookup
{
    public static async Task<NulicLicense> DownloadLicense(string spdx_id, DirectoryInfo destination)
    {
        var file = new FileInfo(Path.Join(destination.FullName, $"{spdx_id}.txt"));

        var text_getter = () => FindOrDownloadLicense(spdx_id);

        return await NulicLicense.FindOrCreate(text_getter, file, spdx_id: spdx_id);
    }
    static async Task<string> FindOrDownloadLicense(string spdx_id)
    {
        if (CommonLicenses.Licenses.TryGetValue(spdx_id, out var license_text))
            return license_text;

        return await DownloadSpdxLicense(spdx_id);
    }
    static async Task<string> DownloadSpdxLicense(string spdx_id)
    {
        var url = new Uri($"https://spdx.org/licenses/{spdx_id}.json");

        var reply = await Program.HttpClient.GetStringAsync(url);

        var jsonobj = (JObject) reply.FromJson(typeof(object));

        if (jsonobj["licenseText"] is JToken tok)
            if (tok.ToObject(typeof(string)) is string text && text.Length > 0)
                return text;

        throw new Newtonsoft.Json.JsonException($"SpdxLookup: Failed to extract 'licenseText'.");
    }
}
