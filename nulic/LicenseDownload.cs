using AngleSharp.Html.Parser;
using Serilog;
using Textify;

namespace nulic;

internal class LicenseDownload
{
    internal class UnknownUrlException : Exception {}
    class Download(Uri licenseurl, FileInfo destination)
    {
        public Uri Url { get; set; } = licenseurl;
        public FileInfo Dest { get; set; } = destination;
    }
    public static async Task<NulicLicense> DownloadFrom(Uri licenseurl, FileInfo dest)
    {
        var download = new Download(licenseurl, dest);

        var result = await DownloadFrom(download, null);

        if (result is null) // pass #2 - connect and try again if redirected
        {
            var rsp = await Program.HttpClient.GetAsync(licenseurl);

            if (rsp.RequestMessage?.RequestUri is Uri url)
                download.Url = url;

            if (download.Url != licenseurl)
            {
                var redirectFilename = Path.GetFileName(download.Url.AbsolutePath);
                if (!string.IsNullOrEmpty(redirectFilename))
                    download.Dest = new FileInfo(Path.Join(download.Dest.DirectoryName, redirectFilename));

                result = await DownloadFrom(download, rsp);
            }
        }
        if (result is null)
        {
            Func<Task<string>> download_task = () => throw new UnknownUrlException();

            result = await NulicLicense.FindOrCreate(download_task, download.Dest, download.Url);
        }
        else
        {
            string redirect = result.LicenseUrl != licenseurl ? $" (via {result.LicenseUrl})" : "";

            Log.Information("Download from {url} OK!{redirect}", licenseurl, redirect);
        }

        return result;
    }
    static async Task<NulicLicense?> DownloadFrom(Download download, HttpResponseMessage? rsp)
    {
        var host = NormalizeHost(download.Url);

        if (host == "licenses.nuget.org")
        {
            // licenses.nuget.org/{spdx-id} is just a redirect to the canonical SPDX text
            var spdxId = download.Url.AbsolutePath.Trim('/');
            var sharedRoot = download.Dest.Directory?.Parent
                ?? throw new InvalidOperationException($"Cannot determine license root for licenses.nuget.org URL: {download.Url}");
            return await SpdxLookup.DownloadLicense(spdxId, sharedRoot);
        }

        // spdx.org license URLs: delegate to SpdxLookup which handles the JSON API correctly
        if (host == "spdx.org" && download.Url.AbsolutePath.StartsWith("/licenses/"))
        {
            var spdxId = Path.GetFileNameWithoutExtension(download.Url.AbsolutePath);
            var sharedRoot = download.Dest.Directory?.Parent
                ?? throw new InvalidOperationException($"Cannot determine license root for SPDX URL: {download.Url}");
            return await SpdxLookup.DownloadLicense(spdxId, sharedRoot);
        }

        Func<Task<string>>? download_task = null;
        var urlBefore = download.Url;

        if (LookupFileLinkFrom(download))
        {
            if (download.Url != urlBefore) rsp = null; // URL was transformed, discard stale response
            download_task = () => DownloadFileFrom(download, rsp);
        }
        else if (LookupHtmlElementFrom(download) is string element)
            download_task = () => DownloadHtmlElement(download, element, rsp);

        else if (LookupHtmlFlattenable(download))
            download_task = () => DownloadHtmlFlattened(download, rsp);

        if (download_task is null)
            return null;

        return await NulicLicense.FindOrCreate(download_task, download.Dest, download.Url);
    }
    internal static string NormalizeHost(Uri uri)
    {
        var host = uri.Host;
        return host.StartsWith("www.") ? host[4..] : host;
    }
    static bool LookupFileLinkFrom(Download download)
    {
        var host = NormalizeHost(download.Url);

        if (host == "raw.githubusercontent.com")
            return true;

        if (host == "github.com" && download.Url.AbsolutePath.Contains("/blob/"))
        {
            var path = download.Url.AbsolutePath.Replace("/blob/", "/");

            download.Url = new Uri($"https://raw.githubusercontent.com{path}");

            return true;
        }

        var ext = Path.GetExtension(download.Url.AbsolutePath).ToLowerInvariant();
        if (ext is ".rtf" or ".txt" or ".md")
            return true;

        return false;
    }
    static bool LookupHtmlFlattenable(Download download)
    {
        var host = NormalizeHost(download.Url);

        if (host == "dotnet.microsoft.com" && download.Url.AbsolutePath == "/en-us/dotnet_library_license.htm")
        {
            // redirect to storage at license root-folder, where all shared spdx-licenses are
            download.Dest = new FileInfo(Path.Join(download.Dest.Directory?.Parent?.FullName, "DOTNET.txt"));
            return true;
        }

        return false;
    }
    static string? LookupHtmlElementFrom(Download download)
    {
        var host = NormalizeHost(download.Url);

        if (host == "opensource.org")
        {
            // redirect to common storage at root location
            var rootpath = download.Dest.Directory?.Parent;
            var license = Path.GetFileNameWithoutExtension(download.Url.AbsolutePath);
            download.Dest = new FileInfo(Path.Join(rootpath?.FullName, $"opensource.org.{license}.txt"));
            return "div#LicenseText";
        }

        return null;
    }
    static async Task<string> DownloadFileFrom(Download download, HttpResponseMessage? rsp)
    {
        if (rsp is null)
            rsp = await Program.HttpClient.GetAsync(download.Url);

        rsp.EnsureSuccessStatusCode();

        return await rsp.Content.ReadAsStringAsync();
    }
    static async Task<string> DownloadHtmlElement(Download download, string element, HttpResponseMessage? rsp)
    {
        if (rsp is null)
            rsp = await Program.HttpClient.GetAsync(download.Url);

        rsp.EnsureSuccessStatusCode();

        var html = await rsp.Content.ReadAsStreamAsync();

        HtmlParser parser = new();

        var doc = await parser.ParseDocumentAsync(html, CancellationToken.None);

        var text = doc.QuerySelector(element)?.TextContent;

        if (string.IsNullOrEmpty(text))
            throw new Exception($"Lookup '{element}' from {download.Url} failed.");

        return text;
    }
    static async Task<string> DownloadHtmlFlattened(Download download, HttpResponseMessage? rsp)
    {
        if (rsp is null)
            rsp = await Program.HttpClient.GetAsync(download.Url);

        rsp.EnsureSuccessStatusCode();

        var html = await rsp.Content.ReadAsStreamAsync();

        HtmlParser parser = new();

        var doc = await parser.ParseDocumentAsync(html, CancellationToken.None);

        var textify = new HtmlToTextConverter();

        var text = textify.Convert(doc.Body);

        if (string.IsNullOrEmpty(text))
            throw new Exception($"Lookup/flatten {download.Url} failed.");

        return text;
    }
}
