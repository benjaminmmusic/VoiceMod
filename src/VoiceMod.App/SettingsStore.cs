using System.IO;
using System.Text.Json;

namespace VoiceMod.App;

public static class SettingsStore
{
    private static readonly string Path = System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "VoiceMod",
        "settings.json");

    public static AppSettings Load()
    {
        try
        {
            if (!File.Exists(Path)) return new AppSettings();
            var json = File.ReadAllText(Path);
            return JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();
        }
        catch (Exception ex)
        {
            // log to console; carry on with defaults rather than crashing
            System.Diagnostics.Debug.WriteLine($"SettingsStore.Load failed: {ex}");
            return new AppSettings();
        }
    }

    public static void Save(AppSettings settings)
    {
        try
        {
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path)!);
            var json = JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(Path, json);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"SettingsStore.Save failed: {ex}");
            // silent failure — settings just don't persist this session
        }
    }
}