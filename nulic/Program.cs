using Newtonsoft.Json;
using NuGet.Protocol;
using Serilog;
using System.CommandLine;
using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("unit_tests")]

namespace nulic;

internal class Program
{
    public static HttpClient HttpClient => new();
    static RootCommand CreateApp()
    {
        var path = new Argument<string>("path", () => ".", "[<Solution|Project|Directory>]");

        var fileOption = new Option<FileInfo?>(
        name: "--file",
        description: "The file to read and display on the console.");

        var rootCommand = new RootCommand("Sample app for System.CommandLine");
        rootCommand.AddArgument(path);
        rootCommand.AddOption(fileOption);

        rootCommand.SetHandler(async (path) =>
        {
            await Process(path);
        },
            path);

        return rootCommand;
    }
    static async Task Process(string path)
    {
        var projects = MSBuildProject.LoadFrom(path);

        Log.Information($"Found {projects.Count()} project(s) in {path}.");

        var nugets = projects.SelectMany(NugetMetadata.GetFrom).Distinct().ToArray();

        var license_root = new DirectoryInfo(Path.Join(path, "licenses"));

        try
        {
            await NugetMetadata.CollectInformation(nugets, license_root);
        }
        catch (Exception ex)
        {
            Log.Fatal(ex, "Exception:");

            Environment.Exit(-1);
        }

        var outfile = Path.Join(license_root.FullName, "licenses.json");

        await File.WriteAllTextAsync(outfile, JsonConvert.SerializeObject(nugets));
    }
    static async Task Main(string[] args)
    {
        Log.Logger = new LoggerConfiguration()
            .WriteTo.Console()
            .CreateLogger();

        var app2 = CreateApp();

        await app2.InvokeAsync(args);

        Log.Information("Done.");
    }
}
