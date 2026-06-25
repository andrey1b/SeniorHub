using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Net.Http.Json;
using System.Reflection;
using System.Text.Json.Serialization;
using System.Windows;

namespace OfisPensionera.Launcher;

static class Updater
{
    private static readonly HttpClient Http = new()
    {
        Timeout = TimeSpan.FromSeconds(15),
        DefaultRequestHeaders = { { "User-Agent", "SeniorHub-Launcher" } }
    };

    // ── Проверка обновлений самого лаунчера ─────────────────────────────────

    /// <returns>true если обновление найдено и диалог показан, false во всех остальных случаях</returns>
    public static async Task<bool> CheckSeniorHubUpdateAsync()
    {
        try
        {
            var release = await Http.GetFromJsonAsync<GhRelease>(
                "https://api.github.com/repos/andrey1b/SeniorHub/releases/latest");

            if (release is null) return false;

            var tag = release.TagName.TrimStart('v');
            if (!Version.TryParse(tag, out var latest)) return false;

            var cur = Assembly.GetExecutingAssembly().GetName().Version!;
            var current = new Version(cur.Major, cur.Minor, cur.Build);
            if (latest <= current) return false;

            string msg = App.CurrentLanguage == "en"
                ? $"SeniorHub {latest} is available.\nOpen the download page?"
                : $"Доступна новая версия SeniorHub {latest}.\nОткрыть страницу загрузки?";

            var res = MessageBox.Show(msg, "SeniorHub",
                MessageBoxButton.YesNo, MessageBoxImage.Information);

            if (res == MessageBoxResult.Yes)
                Process.Start(new ProcessStartInfo(release.HtmlUrl) { UseShellExecute = true });

            return true;
        }
        catch { return false; /* нет интернета или rate-limit — тихо игнорируем */ }
    }

    // ── Скачивание и запуск setup для модуля ────────────────────────────────

    public static async Task<bool> DownloadAndInstallAsync(string repo, string appName)
    {
        try
        {
            var release = await Http.GetFromJsonAsync<GhRelease>(
                $"https://api.github.com/repos/{repo}/releases/latest");

            if (release is null) return false;

            var asset = release.Assets.FirstOrDefault(a =>
                a.Name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) &&
                a.Name.Contains("Setup", StringComparison.OrdinalIgnoreCase));

            if (asset is null) return false;

            string dest = Path.Combine(Path.GetTempPath(), asset.Name);

            using (var response = await Http.GetAsync(asset.BrowserDownloadUrl,
                       HttpCompletionOption.ResponseHeadersRead))
            {
                response.EnsureSuccessStatusCode();
                // Файл закрываем сразу после записи — иначе installer не запустится
                // («файл занят другим процессом»), т.к. лаунчер держит его открытым.
                await using var fs = File.Create(dest);
                await response.Content.CopyToAsync(fs);
            }

            Process.Start(new ProcessStartInfo(dest) { UseShellExecute = true });
            return true;
        }
        catch (Exception ex)
        {
            string err = App.CurrentLanguage == "en"
                ? $"Download failed:\n{ex.Message}\n\nOpening releases page..."
                : $"Ошибка загрузки:\n{ex.Message}\n\nОткрываю страницу релизов...";

            MessageBox.Show(err, appName, MessageBoxButton.OK, MessageBoxImage.Warning);

            // fallback — браузер на страницу релизов
            Process.Start(new ProcessStartInfo(
                $"https://github.com/{repo}/releases/latest") { UseShellExecute = true });
            return false;
        }
    }

    // ── GitHub JSON-модели ───────────────────────────────────────────────────

    private record GhRelease(
        [property: JsonPropertyName("tag_name")] string TagName,
        [property: JsonPropertyName("html_url")]  string HtmlUrl,
        [property: JsonPropertyName("assets")]    List<GhAsset> Assets);

    private record GhAsset(
        [property: JsonPropertyName("name")]                   string Name,
        [property: JsonPropertyName("browser_download_url")]   string BrowserDownloadUrl);
}
