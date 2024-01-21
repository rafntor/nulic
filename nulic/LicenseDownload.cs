using AngleSharp.Html.Parser;
using AngleSharp.Io;
using NuGet.Protocol.Plugins;
using Serilog;
using System.ComponentModel;
using Textify;

namespace nulic;

internal class LicenseDownload
{
    class Download
    {
        public Download(Uri licenseurl, FileInfo destination)
        {
            url = licenseurl;
            dest = destination;
        }
        public Uri url;
        public FileInfo dest;
        public StreamWriter stream = StreamWriter.Null;
    }
    public static async Task<NulicLicense> DownloadFrom(Uri licenseurl, FileInfo dest)
    {
        var download = new Download (licenseurl, dest);

        var result = await DownloadFrom(download, null);

        if (result is null) // pass #2 - connect and try again if redirected
        {
            var rsp = await Program.HttpClient.GetAsync(licenseurl);

            if (rsp.RequestMessage?.RequestUri is Uri url)
                download.url = url;

            if (download.url != licenseurl)
                result = await DownloadFrom(download, rsp);
        }

        if (result is null)
            throw new Exception($"Download from {licenseurl} failed!");

        string redirect = result.LicenseUrl != licenseurl ? $" (via {result.LicenseUrl})" : "";

        Log.Information($"Download from {licenseurl} OK!{redirect}");

        return result;
    }
    static async Task<NulicLicense?> DownloadFrom(Download download, HttpResponseMessage? rsp)
    {
        Func<Task>? download_task = null;

        if (LookupFileLinkFrom(ref download))
            download_task = () => DownloadFileFrom(download, rsp);

        else if (LookupHtmlElementFrom(ref download) is string element)
            download_task = () => DownloadHtmlElement(download, element, rsp);

        else if (LookupHtmlFlattenable(ref download))
            download_task = () => DownloadHtmlFlattened(download, rsp);

        if (download_task is null)
            return null;

        var result = NulicLicense.FindExisting(download.dest);

        return result ?? await CreateFrom(download, download_task);
    }
    static async Task<NulicLicense> CreateFrom(Download download, Func<Task> download_task)
    {
        if (CreateStream(ref download))
        {
            try
            {
                await download_task();
            }
            catch (Exception ex)
            {
                download.stream.Dispose();
                download.dest.Delete();
                throw new Exception($"Download from {download.url} failed", ex);
            }

            var result = await NulicLicense.Create(download.dest, url: download.url, stream: download.stream.BaseStream);

            await download.stream.DisposeAsync();

            return result;
        }

        return await NulicLicense.Create(download.dest, url: download.url);
    }
    static bool CreateStream(ref Download download)
    {
        try
        {
            var stream = new FileStream(download.dest.FullName, FileMode.CreateNew);

            download.stream = new StreamWriter(stream);

            return true;
        }
        catch (IOException ex)
        {
            if (ex.HResult == unchecked((int)0x80070050))
                return false; // allready exists, return and preoceed

            throw;
        }
    }
    static bool LookupFileLinkFrom(ref Download download)
    {
        var host = download.url.Host;

        if (host.StartsWith("www."))
            host = host.Substring(4);

        if (host == "raw.githubusercontent.com")
            return true;

        if (host == "github.com" && download.url.AbsolutePath.Contains("/blob/"))
        {
            var path = download.url.AbsolutePath.Replace("/blob/", "/");

            download.url = new Uri($"https://raw.githubusercontent.com{path}");

            return true;
        }

        if (host == "spdx.org" && download.url.AbsolutePath.Contains("/licenses/"))
        {
            throw new Exception("todo ...");
        }

        return false;
    }
    static bool LookupHtmlFlattenable(ref Download download)
    {
        var host = download.url.Host;

        if (host.StartsWith("www."))
            host = host.Substring(4);

        if (host == "dotnet.microsoft.com" && download.url.AbsolutePath == "/en-us/dotnet_library_license.htm")
        {
            // redirect to storage at license root-folder, where all shared spdx-licenses are
            download.dest = new FileInfo(Path.Join(download.dest.Directory?.Parent?.FullName, "DOTNET.txt"));
            return true;
        }

        return false;
    }
    static string? LookupHtmlElementFrom(ref Download download)
    {
        var host = download.url.Host;

        if (host.StartsWith("www."))
            host = host.Substring(4);

        if (host == "opensource.org")
        {
            // redirect to be storage outside package-specific location
            var rootpath = download.dest.Directory?.Parent?.FullName;
            var license = Path.GetFileNameWithoutExtension(download.url.AbsolutePath);
            download.dest = new FileInfo(Path.Join(rootpath, "opensource.org", $"{license}.txt"));
            return "div#LicenseText";
        }

        return null;
    }
    static async Task DownloadFileFrom(Download download, HttpResponseMessage? rsp)
    {
        if (rsp is null)
            rsp = await Program.HttpClient.GetAsync(download.url);

        rsp.EnsureSuccessStatusCode();

        var text = await rsp.Content.ReadAsStreamAsync();

        await text.CopyToAsync(download.stream.BaseStream);
    }
    static async Task DownloadHtmlElement(Download download, string element, HttpResponseMessage? rsp)
    {
        if (rsp is null)
            rsp = await Program.HttpClient.GetAsync(download.url);

        rsp.EnsureSuccessStatusCode();

        var html = await rsp.Content.ReadAsStreamAsync();

        HtmlParser parser = new();

        var doc = await parser.ParseDocumentAsync(html, CancellationToken.None);

        var text = doc.QuerySelector(element)?.TextContent;

        if (string.IsNullOrEmpty(text))
            throw new Exception($"Lookup '{element}' from {download.url} failed.");

        await download.stream.WriteAsync(text);
    }
    static async Task DownloadHtmlFlattened(Download download, HttpResponseMessage? rsp)
    {
        if (rsp is null)
            rsp = await Program.HttpClient.GetAsync(download.url);

        rsp.EnsureSuccessStatusCode();

        var html = await rsp.Content.ReadAsStreamAsync();

        HtmlParser parser = new();

        var doc = await parser.ParseDocumentAsync(html, CancellationToken.None);

        var textify = new HtmlToTextConverter();

        //var xx = doc.Body?.ChildNodes.Where(c=>c.TextContent!=null);

        var text = textify.Convert(doc.Body);

        if (string.IsNullOrEmpty(text))
            throw new Exception($"Lookup/flatten {download.url} failed.");

        await download.stream.WriteAsync(text);
    }
}
