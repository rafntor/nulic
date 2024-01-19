using Newtonsoft.Json.Linq;
using NuGet.Protocol;

namespace nulic;

internal class SpdxLookup
{
    public static async Task<string> DownloadLicense(string license, DirectoryInfo destination)
    {
        var url = new Uri($"https://spdx.org/licenses/{license}.json");

        var filename = $"{license}.txt";

        var fpath = Path.Join(destination.FullName, filename);

        if (CreateStream(fpath) is StreamWriter sw)
        {
            try
            {
                await FindOrDownloadLicense(license, url, sw);
            }
            catch
            {
                await sw.DisposeAsync();
                File.Delete(fpath);
                throw;
            }

            await sw.DisposeAsync();
        }

        return filename;
    }
    static StreamWriter? CreateStream(string filepath)
    {
        try
        {
            var stream = new FileStream(filepath, FileMode.CreateNew);

            return new StreamWriter(stream);
        }
        catch (IOException ex)
        {
            if (ex.HResult == unchecked((int)0x80070050))
                return null; // allready exists so all is fine

            throw;
        }
    }
    static async Task FindOrDownloadLicense(string license, Uri url, StreamWriter stream)
    {
        string? license_text;

        if (!CommonLicenses.Licenses.TryGetValue(license, out license_text))
            license_text = await DownloadSpdxLicense(license);

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
