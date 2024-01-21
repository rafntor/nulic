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

        var result = NulicLicense.FindExisting(file);

        return result ?? await DownloadLicense(file, spdx_id);
    }
    static async Task<NulicLicense> DownloadLicense(FileInfo file, string spdx_id)
    {
        var url = new Uri($"https://spdx.org/licenses/{spdx_id}.json");

        if (CreateStream(file) is StreamWriter sw)
        {
            try
            {
                await FindOrDownloadLicense(spdx_id, url, sw);
            }
            catch
            {
                await sw.DisposeAsync();
                file.Delete();
                throw;
            }

            await sw.DisposeAsync();
        }

        return await NulicLicense.Create(file, spdx_id: spdx_id, url: url);
    }
    static StreamWriter? CreateStream(FileInfo filepath)
    {
        try
        {
            var stream = new FileStream(filepath.FullName, FileMode.CreateNew);

            return new StreamWriter(stream);
        }
        catch (IOException ex)
        {
            if (ex.HResult == unchecked((int)0x80070050))
                return null; // allready exists so all is fine

            throw;
        }
    }
    static async Task FindOrDownloadLicense(string spdx_id, Uri url, StreamWriter stream)
    {
        string? license_text;

        // hmm?
        if (!CommonLicenses.Licenses.TryGetValue(spdx_id, out license_text))
            license_text = await DownloadSpdxLicense(spdx_id);

        await stream.WriteAsync(license_text);
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
