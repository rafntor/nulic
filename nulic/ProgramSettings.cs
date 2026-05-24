using Serilog;
using Serilog.Events;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace nulic;

internal record PackageOverride
{
    public required string Id { get; init; }
    public string? Version { get; init; }
    public string? License { get; init; }
    public string? LicenseUrl { get; init; }
    public string[]? Authors { get; init; }
    public string? ProjectUrl { get; init; }
    public string? Copyright { get; init; }
}

internal class NulicSettings
{
    public LogEventLevel? LogLevel { get; set; }
    public string[]? Exclude { get; set; }
    public string[]? Ignore { get; set; }
    public string[]? Allow { get; set; }
    public List<PackageOverride> Overrides { get; set; } = new();
}

internal class ProgramSettings
{
    static readonly JsonSerializerOptions _jsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        Converters = { new JsonStringEnumConverter() }
    };

    static readonly NulicSettings _default = new()
    {
        LogLevel = LogEventLevel.Information,
        Exclude = [@"**\*test*", "demo-project"],
        Ignore = ["developmentDependency", "PrivateAssets", "id:*Longship*", "author:*Leif*"],
        Allow = ["MIT", "Apache-2.0", "BSD-3-Clause", "MS-PL", "Unlicense", "WITH LicenseRef-linking-exception"],
        Overrides =
        [
            new PackageOverride
            {
                Id = "Longship.Cruises",
                Version = "1002.0.1+vinland",
                License = "LicenseRef-Axe-Enforced",
                LicenseUrl = "licenses/DANEGELD_TERMS.txt",
                Authors = ["Leif Erikson"],
                ProjectUrl = "https://longshipcruises.no",
                Copyright = "Copyright © 982 Erik the Red. All oceans reserved."
            }
        ]
    };

    public static NulicSettings Settings { get; private set; } = new();
    public static DirectoryInfo SettingsDir { get; private set; } = new(".");

    public static void Load(DirectoryInfo solutionDir)
    {
        SettingsDir = solutionDir;
        var file = new FileInfo(Path.Join(solutionDir.FullName, "nulic.json"));

        if (file.Exists)
        {
            Settings = JsonSerializer.Deserialize<NulicSettings>(File.ReadAllText(file.FullName), _jsonOptions) ?? new();
            Log.Information("Loaded {file} ({count} overrides)", file.Name, Settings.Overrides.Count);
        }
        else
        {
            Log.Information("No nulic.json found — creating default");
            try
            {
                File.WriteAllText(file.FullName, JsonSerializer.Serialize(_default, _jsonOptions));
            }
            catch (Exception ex)
            {
                Log.Warning("Could not create nulic.json: {message}", ex.Message);
            }
        }
    }
}
