using Serilog;
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
    public List<PackageOverride> Overrides { get; set; } = new();
}

internal class ProgramSettings
{
    static readonly JsonSerializerOptions _jsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
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
                File.WriteAllText(file.FullName, JsonSerializer.Serialize(new NulicSettings(), _jsonOptions));
            }
            catch (Exception ex)
            {
                Log.Warning("Could not create nulic.json: {message}", ex.Message);
            }
        }
    }
}
