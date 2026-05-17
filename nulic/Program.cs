using Serilog;
using System.CommandLine;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using NuGet.Versioning;

[assembly: InternalsVisibleTo("unit_tests")]

namespace nulic;

internal class Program
{
    public static readonly HttpClient HttpClient = new();

    static readonly JsonSerializerOptions _jsonOptions = new()
    {
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        Converters = { new NuGetVersionConverter(), new UriConverter() }
    };
    static async Task Main(string[] args)
    {
        Log.Logger = new LoggerConfiguration()
            .WriteTo.Console()
            .CreateLogger();

        var app = CreateApp();

        await app.Parse(args).InvokeAsync();

        Log.Information("Done.");
    }
    static RootCommand CreateApp()
    {
        var path = new Argument<string>("path")
        {
            Description = "Solution-file, project-file or folder",
            DefaultValueFactory = _ => "."
        };
        var settings_folder = new Option<DirectoryInfo>("--settings-folder")
        {
            Description = "Use custom settings from settings-folder. Settings can add missing license-information and decide which packages and licenses are included in the output.",
            DefaultValueFactory = _ => new DirectoryInfo("settings")
        };
        var dump_settings = new Option<bool>("--dump-settings")
        {
            Description = "Dump current settings and exit. Use this to save the built-in settings to use as base for creating customized settings that override the defaults."
        };

        var exclude = new Option<string[]>("--exclude")
        {
            Description = "Exclude projects matching a glob pattern (matched against full path). Repeatable. E.g: --exclude *Test* --exclude tests/*",
            AllowMultipleArgumentsPerToken = false,
        };
        exclude.Aliases.Add("-e");
        exclude.Arity = ArgumentArity.ZeroOrMore;

        settings_folder.Aliases.Add("-s");
        dump_settings.Aliases.Add("-d");

        var rootCommand = new RootCommand("Nuget license collection and reporting tool.");

        rootCommand.Arguments.Add(path);
        rootCommand.Options.Add(settings_folder);
        rootCommand.Options.Add(dump_settings);
        rootCommand.Options.Add(exclude);

        rootCommand.SetAction(async (parseResult, _) =>
        {
            await Process(parseResult.GetValue(path)!, parseResult.GetValue(settings_folder)!, parseResult.GetValue(dump_settings), parseResult.GetValue(exclude));
            return 0;
        });

        return rootCommand;
    }
    static async Task Process(string path, DirectoryInfo settings_folder, bool dump_settings, string[]? exclude = null)
    {
        ProgramSettings.Load(settings_folder, dump_settings);

        var projects = MSBuildProject.LoadFrom(path, exclude);

        Log.Information($"Found {projects.Count()} project(s) in {path}.");

        var nugetArrays = await Task.WhenAll(projects.Select(NugetMetadata.GetFrom));
        var nugets = nugetArrays.SelectMany(x => x).DistinctBy(n => (n.Id, n.Version)).ToArray();

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

        await File.WriteAllTextAsync(outfile, JsonSerializer.Serialize(nugets, _jsonOptions));
        await MarkdownReport.Write(nugets, license_root);

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

    class NuGetVersionConverter : JsonConverter<NuGetVersion>
    {
        public override NuGetVersion Read(ref Utf8JsonReader r, Type t, JsonSerializerOptions o)
            => NuGetVersion.Parse(r.GetString()!);
        public override void Write(Utf8JsonWriter w, NuGetVersion v, JsonSerializerOptions o)
            => w.WriteStringValue(v.ToString());
    }

    class UriConverter : JsonConverter<Uri>
    {
        public override Uri? Read(ref Utf8JsonReader r, Type t, JsonSerializerOptions o)
            => r.GetString() is string s ? new Uri(s) : null;
        public override void Write(Utf8JsonWriter w, Uri v, JsonSerializerOptions o)
            => w.WriteStringValue(v.ToString());
    }
}
