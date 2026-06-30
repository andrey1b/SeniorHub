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

    // ── Проверка обновлений всех модулей + лаунчера ─────────────────────────

    public record AppRef(string Name, string Repo, string? ExePath, bool IsLauncher = false, string? InstalledVersion = null);
    public record ModuleUpdate(AppRef App, string Installed, string Latest, string AssetName, string AssetUrl);

    /// Опрашивает релизы переданных приложений и возвращает те, где есть новая версия.
    public static async Task<List<ModuleUpdate>> CheckUpdatesAsync(IEnumerable<AppRef> apps)
    {
        var result = new List<ModuleUpdate>();
        foreach (var app in apps)
        {
            try
            {
                if (string.IsNullOrEmpty(app.ExePath) || !File.Exists(app.ExePath)) continue; // не установлено — пропуск
                if (string.IsNullOrEmpty(app.Repo)) continue;

                var release = await Http.GetFromJsonAsync<GhRelease>(
                    $"https://api.github.com/repos/{app.Repo}/releases/latest");
                if (release is null || !TryParseVersion(release.TagName, out var latest)) continue;

                // Версия установленного: сперва переданная из детекта (учитывает спец-случаи
                // вроде GardenPlanner с неверным FileVersion), иначе — из FileVersionInfo.
                Version installed;
                if (!string.IsNullOrEmpty(app.InstalledVersion) && TryParseVersion(app.InstalledVersion, out var iv))
                    installed = iv;
                else
                {
                    var vi = FileVersionInfo.GetVersionInfo(app.ExePath);
                    installed = new Version(vi.FileMajorPart, vi.FileMinorPart, vi.FileBuildPart);
                }
                if (latest <= installed) continue;

                var asset = release.Assets.FirstOrDefault(a =>
                    a.Name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) &&
                    a.Name.Contains("Setup", StringComparison.OrdinalIgnoreCase));
                if (asset is null) continue;

                result.Add(new ModuleUpdate(app, installed.ToString(3), latest.ToString(),
                    asset.Name, asset.BrowserDownloadUrl));
            }
            catch { /* недоступный модуль — пропускаем */ }
        }
        return result;
    }

    /// Скачивает все установщики и запускает их ТИХО одной цепочкой с правами админа (один UAC).
    /// Возвращает true, если в наборе есть сам лаунчер (его нужно закрыть — cmd перезапустит).
    public static async Task<bool> UpdateAllAsync(IEnumerable<ModuleUpdate> updates, string launcherExe)
    {
        var dir = Path.Combine(Path.GetTempPath(), "SeniorHubUpdates");
        Directory.CreateDirectory(dir);

        var modules = new List<string>();
        string? launcherInstaller = null;

        foreach (var u in updates)
        {
            var dest = Path.Combine(dir, u.AssetName);
            using (var resp = await Http.GetAsync(u.AssetUrl, HttpCompletionOption.ResponseHeadersRead))
            {
                resp.EnsureSuccessStatusCode();
                await using var fs = File.Create(dest);
                await resp.Content.CopyToAsync(fs);
            }
            if (u.App.IsLauncher) launcherInstaller = dest; else modules.Add(dest);
        }

        const string flags = "/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /CLOSEAPPLICATIONS";
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("@echo off");
        sb.AppendLine("timeout /t 2 /nobreak >nul");
        foreach (var m in modules) sb.AppendLine($"\"{m}\" {flags}");
        if (launcherInstaller is not null)
        {
            sb.AppendLine($"\"{launcherInstaller}\" {flags}");
            sb.AppendLine($"explorer.exe \"{launcherExe}\"");   // перезапуск лаунчера в обычном режиме
        }
        var cmd = Path.Combine(dir, "run_updates.cmd");
        File.WriteAllText(cmd, sb.ToString());

        // Один запрос UAC на всю цепочку. Win32Exception (1223) — если отклонён.
        Process.Start(new ProcessStartInfo(cmd) { UseShellExecute = true, Verb = "runas" });
        return launcherInstaller is not null;
    }

    private static bool TryParseVersion(string? tag, out Version version)
    {
        version = new Version(0, 0);
        var m = System.Text.RegularExpressions.Regex.Match(tag ?? "", @"\d+(\.\d+){1,3}");
        return m.Success && Version.TryParse(m.Value, out version!);
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
