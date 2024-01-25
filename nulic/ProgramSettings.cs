using Newtonsoft.Json;

namespace nulic;

internal class SettingsData
{
    public IEnumerable<string> CommonLicenses => nulic.CommonLicenses.Licenses.Keys;
}
internal class ProgramSettings
{
    public static SettingsData Settings { get; private set; } = new();
    public static void Load(DirectoryInfo settings_folder, bool dump_settings)
    {
        if (settings_folder.Exists)
        {
            //load any 'settings.json' and <license>.txt - files
        }
        else
        {
        }

        if (dump_settings)
        {
            Console.Write(JsonConvert.SerializeObject(Settings, Formatting.Indented));

            Environment.Exit(0);
        }
    }
}
