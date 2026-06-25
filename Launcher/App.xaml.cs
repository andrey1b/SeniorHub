using System.IO;
using System.Text.Json;
using System.Windows;

namespace OfisPensionera.Launcher;

public partial class App : Application
{
    public static string CurrentLanguage { get; private set; } = "ru";

    // Путь к legacy-файлу настроек — нужен только для одноразовой миграции
    private static readonly string LegacySettingsPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "OfisPensionera", "settings.json");

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        SharedDb.Initialize();
        MigrateLegacySettings();
        var savedLang = SharedDb.GetSetting("language");
        ApplyLanguage(savedLang == "en" || savedLang == "ru" ? savedLang : "ru");
    }

    public static void SetLanguage(string lang)
    {
        ApplyLanguage(lang);
        SharedDb.SetSetting("language", lang);
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

    // Одноразовая миграция: читает старый JSON, сохраняет в SharedDb, удаляет файл
    private static void MigrateLegacySettings()
    {
        try
        {
            if (!File.Exists(LegacySettingsPath)) return;
            var doc = JsonDocument.Parse(File.ReadAllText(LegacySettingsPath));
            if (doc.RootElement.TryGetProperty("language", out var el))
            {
                var lang = el.GetString();
                if ((lang == "en" || lang == "ru") && SharedDb.GetSetting("language") is null)
                    SharedDb.SetSetting("language", lang);
            }
            File.Delete(LegacySettingsPath);
        }
        catch { }
    }
}
