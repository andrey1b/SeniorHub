using System.IO;
using System.Text.Json;
using System.Windows;

namespace OfisPensionera.Launcher;

public partial class App : Application
{
    public static string CurrentLanguage { get; private set; } = "ru";

    // JSON-файл настроек оставляем для обратной совместимости
    private static readonly string SettingsPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "OfisPensionera", "settings.json");

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        SharedDb.Initialize();
        ApplyLanguage(LoadSavedLanguage());
    }

    public static void SetLanguage(string lang)
    {
        ApplyLanguage(lang);
        SharedDb.SetSetting("language", lang);
        SaveLegacyJson(lang);
    }

    public static void ApplyLanguage(string lang)
    {
        CurrentLanguage = lang;
        var dict = new ResourceDictionary
        {
            Source = new Uri($"Resources/Strings.{lang}.xaml", UriKind.Relative)
        };
        var merged = Current.Resources.MergedDictionaries;
        var old = merged.FirstOrDefault(d =>
            d.Source?.OriginalString?.StartsWith("Resources/Strings.") == true);
        if (old is not null) merged.Remove(old);
        merged.Add(dict);
    }

    private static string LoadSavedLanguage()
    {
        // Сначала общая база, потом legacy JSON
        var fromDb = SharedDb.GetSetting("language");
        if (fromDb == "en" || fromDb == "ru") return fromDb;
        return LoadLegacyJson();
    }

    private static string LoadLegacyJson()
    {
        try
        {
            if (File.Exists(SettingsPath))
            {
                var doc = JsonDocument.Parse(File.ReadAllText(SettingsPath));
                if (doc.RootElement.TryGetProperty("language", out var el))
                    return el.GetString() == "en" ? "en" : "ru";
            }
        }
        catch { }
        return "ru";
    }

    private static void SaveLegacyJson(string lang)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
            File.WriteAllText(SettingsPath, $"{{\"language\":\"{lang}\"}}");
        }
        catch { }
    }
}
