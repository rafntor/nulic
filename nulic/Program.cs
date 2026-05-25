using Serilog;
using Serilog.Events;
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

        var exitCode = await app.Parse(args).InvokeAsync();

        Log.Information("Done.");

        Environment.Exit(exitCode);
    }
    static RootCommand CreateApp()
    {
        var path = new Argument<string>("path")
        {
            Description = "Solution-file, project-file or folder",
            DefaultValueFactory = _ => "."
        };
        var logLevel = new Option<LogEventLevel?>("--log-level")
        {
            Description = "Minimum log level: Verbose, Debug, Information, Warning, Error, Fatal. Default: Information.",
            DefaultValueFactory = _ => null
        };
        logLevel.Aliases.Add("-l");

        var showDefaults = new Option<bool>("--show-defaults")
        {
            Description = "Print the default nulic.json to stdout and exit."
        };
        showDefaults.Aliases.Add("-d");

        var merge = new Option<string[]>("--merge")
        {
            Description = "Path to a licenses/ directory from another nulic-processed project to merge into the report. Can be specified multiple times.",
            Arity = ArgumentArity.ZeroOrMore,
            AllowMultipleArgumentsPerToken = false,
        };
        merge.Aliases.Add("-m");

        var rootCommand = new RootCommand("Nuget license collection and reporting tool.");

        rootCommand.Arguments.Add(path);
        rootCommand.Options.Add(logLevel);
        rootCommand.Options.Add(showDefaults);
        rootCommand.Options.Add(merge);

        rootCommand.SetAction(async (parseResult, _) =>
        {
            if (parseResult.GetValue(showDefaults))
            {
                Console.WriteLine(ProgramSettings.SerializeDefault());
                return 0;
            }
            return await Process(parseResult.GetValue(path)!, parseResult.GetValue(logLevel), parseResult.GetValue(merge) ?? []);
        });

        return rootCommand;
    }
    static async Task<int> Process(string path, LogEventLevel? logLevel = null, string[]? merges = null)
    {
        var solutionDir = new DirectoryInfo(File.Exists(path) ? Path.GetDirectoryName(path)! : path);
        ProgramSettings.Load(solutionDir);

        var settings = ProgramSettings.Settings;

        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Is(logLevel ?? LogEventLevel.Information)
            .WriteTo.Console()
            .CreateLogger();

        var exclude = settings.Exclude ?? [];
        var ignore  = settings.Ignore  ?? [];
        var allow   = settings.Allow   ?? [];

        var projects = MSBuildProject.LoadFrom(path, exclude.Length > 0 ? exclude : null);

        Log.Information("Found {count} project(s) in {path}.", projects.Count(), path);

        bool ignoreDevDep = ignore.Contains("developmentDependency", StringComparer.OrdinalIgnoreCase);
        bool ignorePrivate = ignore.Contains("PrivateAssets", StringComparer.OrdinalIgnoreCase);
        var patternIgnore = ignore.Where(i =>
            !i.Equals("developmentDependency", StringComparison.OrdinalIgnoreCase) &&
            !i.Equals("PrivateAssets", StringComparison.OrdinalIgnoreCase)).ToArray();

        var nugetArrays = await Task.WhenAll(projects.Select(NugetMetadata.GetFrom));
        var nugets = nugetArrays.SelectMany(x => x).DistinctBy(n => (n.Id, n.Version)).ToArray();

        var ignoredIds = NugetMetadata.GetIgnoredIds(projects, ignoreDevDep, ignorePrivate);
        if (ignoredIds.Count > 0)
            nugets = nugets.Where(n => !ignoredIds.Contains(n.Id)).ToArray();

        if (patternIgnore.Length > 0)
            nugets = PackageFilter.ApplyIgnore(nugets, patternIgnore);

        // Apply overrides from nulic.json: patch matching packages, inject new entries
        // Overrides whose id is in the ignore list are skipped (natural extension of ignore semantics)
        var idPats = PackageFilter.IdPatterns(patternIgnore);
        bool IsIgnored(string id) => ignoredIds.Contains(id) ||
            idPats.Any(p => System.IO.Enumeration.FileSystemName.MatchesSimpleExpression(p, id, ignoreCase: true));

        var overrides = settings.Overrides.Where(o => !IsIgnored(o.Id));
        var injected = new List<NugetMetadata>();
        foreach (var o in overrides)
        {
            var matches = nugets.Where(n =>
                n.Id.Equals(o.Id, StringComparison.OrdinalIgnoreCase) &&
                (o.Version == null || n.Version.ToString() == o.Version)).ToArray();

            if (matches.Length > 0)
                foreach (var m in matches) m.ApplyOverride(o);
            else
                injected.Add(NugetMetadata.FromOverride(o));
        }
        if (injected.Count > 0)
            nugets = nugets.Concat(injected).ToArray();

        var license_root = new DirectoryInfo(Path.Join(solutionDir.FullName, "licenses"));

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

        license_root.Create();
        await File.WriteAllTextAsync(outfile, JsonSerializer.Serialize(nugets, _jsonOptions));

        LicenseEntry[] allEntries;

        if (merges is { Length: > 0 })
        {
            var (merged, mergeOk) = await LicenseMerge.Apply(license_root, merges);
            if (!mergeOk) return -1;
            allEntries = merged!;
        }
        else
        {
            allEntries = nugets.Select(n => new LicenseEntry(
                n.Id, n.Version.ToString(), n.Authors.ToArray(),
                n.ProjectUrl?.ToString(), n.Copyright, n.License,
                n.LicenseUrl?.ToString(), n.LicenseFiles.ToArray())).ToArray();
        }

        await MarkdownReport.Write(license_root);

        var problems = allEntries.Where(e => e.License == NulicLicense.NOASSERTION).ToArray();

        Log.Information("{valid} / {total} packages: license ok", allEntries.Length - problems.Length, allEntries.Length);

        foreach (var p in problems)
            Log.Warning("NOASSERTION: {id} {version}", p.Id, p.Version);

        if (allow.Length > 0)
            return PackageFilter.ApplyAllow(allEntries, allow);

        return 0;
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
