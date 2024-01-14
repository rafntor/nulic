using AngleSharp.Dom;
using AngleSharp.Html.Parser;
using Serilog;
using System.Net;
using System.Reflection.Metadata;
using System.Text;
using System.Web;
using Textify;

namespace nulic;

internal class LicenseDownload
{
    public static async Task DownloadFrom(Uri licenseurl, FileInfo dest)
    {
        var result = await DownloadFrom(licenseurl, dest, null);

        if (result is null) // pass #2 - connect and try again if redirected
        {
            var rsp = await Program.HttpClient.GetAsync(licenseurl);

            if (rsp.RequestMessage?.RequestUri is Uri url && url != licenseurl)
                result = await DownloadFrom(url, dest, rsp);
        }

        string redirect = result != licenseurl ? $" (via {result})" : "";

        if (result != null)
            Log.Information($"Download from {licenseurl} OK!{redirect}");
        else
            Log.Error($"Download from {licenseurl} failed!");
    }
    static async Task<Uri?> DownloadFrom(Uri url, FileInfo dest, HttpResponseMessage? rsp)
    {
        if (LookupFileLinkFrom(url) is Uri file_link)
        {
            await DownloadFileFrom(file_link, dest, rsp);

            return file_link;
        }

        if (LookupHtmlElementFrom(url) is string element)
        {
            await DownloadHtmlElement(url, element, dest, rsp);

            return url;
        }

        if (LookupHtmlFlattenable(url) is Uri html_link)
        {
            await DownloadHtmlFlattened(html_link, dest, rsp);

            return html_link;
        }

        return null;
    }
    static Uri? LookupFileLinkFrom(Uri url)
    {
        var host = url.Host;

        if (host.StartsWith("www."))
            host = host.Substring(4);

        if (host == "github.com" && url.AbsolutePath.Contains("/blob/"))
        {
            var path = url.AbsolutePath.Replace("/blob/", "/");

            return new Uri($"https://raw.githubusercontent.com{path}");
        }
        if (host == "raw.githubusercontent.com")
        {
            return url;
        }

        return null;
    }
    static Uri? LookupHtmlFlattenable(Uri url)
    {
        var host = url.Host;

        if (host.StartsWith("www."))
            host = host.Substring(4);

        if (host == "dotnet.microsoft.com" && url.AbsolutePath == "/en-us/dotnet_library_license.htm")
        {
            return url;
        }

        return null;
    }
    static string? LookupHtmlElementFrom(Uri url)
    {
        var host = url.Host;

        if (host.StartsWith("www."))
            host = host.Substring(4);

        if (host == "opensource.org")
            return "div#LicenseText";

        return null;
    }
    static async Task DownloadFileFrom(Uri url, FileInfo dest, HttpResponseMessage? rsp)
    {
        if (rsp is null)
            rsp = await Program.HttpClient.GetAsync(url);

        var text = rsp.Content.ReadAsStream();

        var outstream = dest.Create();

        await text.CopyToAsync(outstream);

        await outstream.DisposeAsync();
    }
    static async Task DownloadHtmlElement(Uri url, string element, FileInfo dest, HttpResponseMessage? rsp)
    {
        if (rsp is null)
            rsp = await Program.HttpClient.GetAsync(url);

        var html = await rsp.Content.ReadAsStreamAsync();

        HtmlParser parser = new();

        var doc = await parser.ParseDocumentAsync(html, CancellationToken.None);

        var text = doc.QuerySelector(element)?.TextContent;

        if (string.IsNullOrEmpty(text))
            throw new Exception($"Lookup '{element}' from {url} failed.");

        var outstream = dest.CreateText();

        await outstream.WriteAsync(text);

        await outstream.DisposeAsync();
    }
    static async Task DownloadHtmlFlattened(Uri url, FileInfo dest, HttpResponseMessage? rsp)
    {
        if (rsp is null)
            rsp = await Program.HttpClient.GetAsync(url);

        var html = await rsp.Content.ReadAsStreamAsync();

        HtmlParser parser = new();

        var doc = await parser.ParseDocumentAsync(html, CancellationToken.None);

        var textify = new HtmlToTextConverter();

        //var xx = doc.Body?.ChildNodes.Where(c=>c.TextContent!=null);

        var text = textify.Convert(doc.Body);

        if (string.IsNullOrEmpty(text))
            throw new Exception($"Lookup/flatten {url} failed.");

        var outstream = dest.CreateText();

        await outstream.WriteAsync(text);

        await outstream.DisposeAsync();
    }
}
