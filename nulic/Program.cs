using Newtonsoft.Json;
using Serilog;
using System.CommandLine;
using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("unit_tests")]

namespace nulic;

internal class Program
{
    public static HttpClient HttpClient => new();
    static async Task Main(string[] args)
    {
        Log.Logger = new LoggerConfiguration()
            .WriteTo.Console()
            .CreateLogger();

        var app = CreateApp();

        await app.InvokeAsync(args);

        Log.Information("Done.");
    }
    static RootCommand CreateApp()
    {
        var path = new Argument<string>("path", () => ".", "Solution-file, project-file or folder");
        var settings_folder = new Option<DirectoryInfo>("--settings-folder", () => new DirectoryInfo("settings"), "Use custom settings from settings-folder. Settings can add missing license-information and decide which packages and licenses are included in the output.");
        var dump_settings = new Option<bool>("--dump-settings", "Dump current settings and exit. Use this to save the built-in settings to use as base for creating customized settings that override the defaults.");

        settings_folder.AddAlias("-s");
        dump_settings.AddAlias("-d");

        var rootCommand = new RootCommand("Nuget license collection and reporting tool.");

        rootCommand.AddArgument(path);
        rootCommand.AddOption(settings_folder);
        rootCommand.AddOption(dump_settings);

        rootCommand.SetHandler(Process, path, settings_folder, dump_settings);

        return rootCommand;
    }
    static async Task Process(string path, DirectoryInfo settings_folder, bool dump_settings)
    {
        ProgramSettings.Load(settings_folder, dump_settings);

        var projects = MSBuildProject.LoadFrom(path);

        Log.Information($"Found {projects.Count()} project(s) in {path}.");

        var nugets = projects.SelectMany(NugetMetadata.GetFrom).Distinct().ToArray();

        string? dir = File.Exists(path) ? Path.GetDirectoryName(path) : path;
        var license_root = new DirectoryInfo(Path.Join(dir, "licenses"));

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

        await File.WriteAllTextAsync(outfile, JsonConvert.SerializeObject(nugets, Formatting.Indented));

        var problems = nugets.Where(n => n.License == NulicLicense.NOASSERTION);

        var nuget_count = nugets.Count();
        var problem_count = problems.Count();

        Console.WriteLine($"{nugets.Count()} packages has valid license");

        if (problem_count > 0)
        {
            Console.WriteLine($"{problem_count} packages has not : ");
            Console.WriteLine(string.Join(Environment.NewLine, problems));
        }
    }
}
