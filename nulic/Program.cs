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

        var ignore = new Option<string[]>("--ignore")
        {
            Description = "Ignore packages by ID glob, author glob (prefix 'author:'), or special flags. " +
                          "Flags: 'developmentDependency' (packages.config), 'PrivateAssets' (SDK-style PrivateAssets=all). " +
                          "Repeatable. E.g: --ignore developmentDependency --ignore PrivateAssets --ignore *Longship.Cruises* --ignore author:*Erik the Red*",
            AllowMultipleArgumentsPerToken = false,
        };
        ignore.Aliases.Add("-i");
        ignore.Arity = ArgumentArity.ZeroOrMore;

        var allow = new Option<string[]>("--allow")
        {
            Description = "SPDX license IDs that are permitted. If specified, exits with code 1 if any package license is not in the list. NOASSERTION always fails. Use 'WITH <exception>' to allow any license carrying that exception. Repeatable. E.g: --allow MIT --allow Apache-2.0 --allow \"WITH Classpath-exception-2.0\"",
            AllowMultipleArgumentsPerToken = false,
        };
        allow.Aliases.Add("-a");
        allow.Arity = ArgumentArity.ZeroOrMore;

        settings_folder.Aliases.Add("-s");
        dump_settings.Aliases.Add("-d");

        var rootCommand = new RootCommand("Nuget license collection and reporting tool.");

        rootCommand.Arguments.Add(path);
        rootCommand.Options.Add(settings_folder);
        rootCommand.Options.Add(dump_settings);
        rootCommand.Options.Add(exclude);
        rootCommand.Options.Add(ignore);
        rootCommand.Options.Add(allow);

        rootCommand.SetAction(async (parseResult, _) =>
        {
            return await Process(parseResult.GetValue(path)!, parseResult.GetValue(settings_folder)!, parseResult.GetValue(dump_settings), parseResult.GetValue(exclude), parseResult.GetValue(ignore), parseResult.GetValue(allow));
        });

        return rootCommand;
    }
    static async Task<int> Process(string path, DirectoryInfo settings_folder, bool dump_settings, string[]? exclude = null, string[]? ignore = null, string[]? allow = null)
    {
        ProgramSettings.Load(settings_folder, dump_settings);

        var projects = MSBuildProject.LoadFrom(path, exclude);

        Log.Information($"Found {projects.Count()} project(s) in {path}.");

        bool ignoreDevDep = ignore?.Contains("developmentDependency", StringComparer.OrdinalIgnoreCase) ?? false;
        bool ignorePrivate = ignore?.Contains("PrivateAssets", StringComparer.OrdinalIgnoreCase) ?? false;
        var patternIgnore = ignore?.Where(i =>
            !i.Equals("developmentDependency", StringComparison.OrdinalIgnoreCase) &&
            !i.Equals("PrivateAssets", StringComparison.OrdinalIgnoreCase)).ToArray();

        var nugetArrays = await Task.WhenAll(projects.Select(NugetMetadata.GetFrom));
        var nugets = nugetArrays.SelectMany(x => x).DistinctBy(n => (n.Id, n.Version)).ToArray();

        var ignoredIds = NugetMetadata.GetIgnoredIds(projects, ignoreDevDep, ignorePrivate);
        if (ignoredIds.Count > 0)
            nugets = nugets.Where(n => !ignoredIds.Contains(n.Id)).ToArray();

        if (patternIgnore?.Length > 0)
            nugets = ApplyIgnore(nugets, patternIgnore);

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

        var problem_count = problems.Count();

        Console.WriteLine($"{nugets.Count()} packages has valid license");

        if (problem_count > 0)
        {
            Console.WriteLine($"{problem_count} packages has not : ");
            Console.WriteLine(string.Join(Environment.NewLine, problems));
        }

        if (allow?.Length > 0)
            return ApplyAllow(nugets, allow);

        return 0;
    }

    static int ApplyAllow(NugetMetadata[] nugets, string[] allowed)
    {
        var allowedIds = new HashSet<string>(
            allowed.Where(a => !a.StartsWith("WITH ", StringComparison.OrdinalIgnoreCase)),
            StringComparer.OrdinalIgnoreCase);
        var allowedExceptions = new HashSet<string>(
            allowed.Where(a => a.StartsWith("WITH ", StringComparison.OrdinalIgnoreCase)),
            StringComparer.OrdinalIgnoreCase);

        var violations = nugets.Where(n => !IsAllowed(n.License, allowedIds, allowedExceptions)).ToArray();

        if (violations.Length == 0)
            return 0;

        Console.WriteLine($"{violations.Length} packages not in allowlist:");
        foreach (var v in violations)
            Console.WriteLine($"  {v.Id} {v.Version} [{v.License}]");

        return 1;
    }

    static bool IsAllowed(string license, HashSet<string> allowedIds, HashSet<string> allowedExceptions)
    {
        if (license == NulicLicense.NOASSERTION) return false;

        foreach (var part in license.Split([" AND ", " OR "], StringSplitOptions.RemoveEmptyEntries))
        {
            var component = part.Trim();
            var withIdx = component.IndexOf(" WITH ", StringComparison.OrdinalIgnoreCase);

            string baseId;
            string? exception;

            if (withIdx >= 0)
            {
                baseId = component[..withIdx].Trim();
                exception = "WITH " + component[(withIdx + " WITH ".Length)..].Trim();
            }
            else
            {
                baseId = component;
                exception = null;
            }

            var componentAllowed = allowedIds.Contains(baseId)
                || (exception != null && allowedExceptions.Contains(exception));

            if (!componentAllowed) return false;
        }

        return true;
    }

    static NugetMetadata[] ApplyIgnore(NugetMetadata[] nugets, string[] patterns)
    {
        var idPatterns = patterns.Where(p => !p.StartsWith("author:", StringComparison.OrdinalIgnoreCase)).ToArray();
        var authorPatterns = patterns
            .Where(p => p.StartsWith("author:", StringComparison.OrdinalIgnoreCase))
            .Select(p => p["author:".Length..])
            .ToArray();

        bool Matches(string value, string[] pats) => pats.Any(p =>
            System.IO.Enumeration.FileSystemName.MatchesSimpleExpression(p, value, ignoreCase: true));

        return nugets.Where(n =>
        {
            if (Matches(n.Id, idPatterns))
            {
                Log.Information($"Ignored: {n.Id} {n.Version} (id match)");
                return false;
            }
            if (n.Authors.Any() && n.Authors.All(a => Matches(a, authorPatterns)))
            {
                Log.Information($"Ignored: {n.Id} {n.Version} (author match)");
                return false;
            }
            return true;
        }).ToArray();
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
