using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using Microsoft.Win32;

namespace OfisPensionera.Launcher;

public partial class MainWindow : Window
{
    private static readonly Dictionary<string, AppConfig> Apps = new()
    {
        ["Food"] = new AppConfig(
            ResKey: "TileFood",
            GitHubRepo: "andrey1b/MenuApp",
            ExeName: "MenuApp.exe",
            DevPaths: [
                @"Food\MenuApp\bin\Release\net9.0-windows\MenuApp.exe",
                @"Food\MenuApp\bin\Debug\net9.0-windows\MenuApp.exe",
                @"Food\bin\Release\net9.0-windows\MenuApp.exe",
            ]),

        ["GardenPlanner"] = new AppConfig(
            ResKey: "TileGarden",
            GitHubRepo: "andrey1b/gardenplanner",
            ExeName: "GardenPlanner.exe",
            DevPaths: [
                @"GardenPlanner\GardenPlanner.Maui\bin\Release\net10.0-windows10.0.19041.0\GardenPlanner.Maui.exe",
                @"GardenPlanner\GardenPlanner.Maui\bin\Debug\net10.0-windows10.0.19041.0\GardenPlanner.Maui.exe",
            ]),

        ["HomeAccounting"] = new AppConfig(
            ResKey: "TileMoney",
            GitHubRepo: "andrey1b/HomeAccounting",
            ExeName: "HomeAccounting.exe",
            DevPaths: [
                @"HomeAccounting\HomeAccounting.exe",
            ]),

        ["TakingMedications"] = new AppConfig(
            ResKey: "TileMeds",
            GitHubRepo: "andrey1b/TakingMedications",
            ExeName: "TakingMedications.exe",
            DevPaths: [
                @"TakingMedications\bin\Release\net9.0-windows\TakingMedications.exe",
                @"TakingMedications\bin\Debug\net9.0-windows\TakingMedications.exe",
            ]),

        ["TextToAudiobook"] = new AppConfig(
            ResKey: "TileAudio",
            GitHubRepo: "andrey1b/Text-to-Audiobook",
            ExeName: "TextToAudiobookCSharp.exe",
            DevPaths: [
                @"TextToAudiobook\bin\Release\net9.0-windows\TextToAudiobookCSharp.exe",
                @"TextToAudiobook\bin\Debug\net9.0-windows\TextToAudiobookCSharp.exe",
            ]),

        ["PdfDrive"] = new AppConfig(
            ResKey: "TilePdf",
            GitHubRepo: "andrey1b/PdfDrive",
            ExeName: "PdfDrive.exe",
            DevPaths: [
                @"PdfDrive\bin\Release\net9.0-windows\win-x64\publish\PdfDrive.exe",
                @"PdfDrive\bin\Release\net9.0-windows\win-x64\PdfDrive.exe",
                @"PdfDrive\bin\Debug\net9.0-windows\win-x64\PdfDrive.exe",
            ]),

        ["CommunalBills"] = new AppConfig(
            ResKey: "TileUtil",
            GitHubRepo: "andrey1b/CommunalBills",
            ExeName: "CommunalBills.exe",
            DevPaths: [
                @"CommunalBills\bin\Release\net9.0-windows\win-x64\publish\CommunalBills.exe",
                @"CommunalBills\bin\Debug\net9.0-windows\CommunalBills.exe",
            ]),

        ["MyBiography"] = new AppConfig(
            ResKey: "TileBio",
            GitHubRepo: "andrey1b/MyBiography",
            ExeName: "MyBiography.exe",
            DevPaths: [
                @"MyBiography\bin\Release\net9.0-windows\win-x64\publish\MyBiography.exe",
                @"MyBiography\bin\Debug\net9.0-windows\MyBiography.exe",
            ]),

        ["Utilities"] = new AppConfig(
            ResKey: "TileTools",
            GitHubRepo: "andrey1b/Utilities",
            ExeName: "Utilities.exe",
            DevPaths: [
                @"Utilities\bin\Release\net9.0-windows10.0.19041.0\win-x64\publish\Utilities.exe",
                @"Utilities\bin\Debug\net9.0-windows10.0.19041.0\win-x64\Utilities.exe",
            ]),
    };

    private readonly string? _appsRoot = ResolveAppsRoot();

    private static string Res(string key) => (string)Application.Current.Resources[key];

    public MainWindow()
    {
        InitializeComponent();
        Loaded += OnLoaded;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        RefreshVersionText();
        RefreshSubtitle();
        RefreshTileVersions();
        RefreshInfoPanel();
        await AutoCheckUpdatesAsync();
    }

    // Тихая автопроверка всех программ при запуске — не чаще раза в день, в фоне.
    // Если обновлений нет — ничего не показывает; если есть — тот же список с предложением обновить.
    private async Task AutoCheckUpdatesAsync()
    {
        try
        {
            var today = DateTime.Today.ToString("yyyy-MM-dd");
            if (SharedDb.GetSetting("last_update_check") == today) return; // сегодня уже проверяли

            await Task.Delay(2500);                          // не мешать открытию окна
            SharedDb.SetSetting("last_update_check", today); // отметить проверку (даже если офлайн — не дёргаем повторно)
            await CheckAllUpdatesAsync();                    // молча при отсутствии обновлений
        }
        catch { /* нет сети / прочее — тихо игнорируем */ }
    }

    // Проверка обновлений всех модулей + лаунчера, список, и обновление всех (один UAC, тихо).
    // Возвращает true, если найдены доступные обновления (иначе вызывающий покажет «обновлений нет»).
    // Статус обновлений — заполняется после проверки, используется для подписей в инфо-панели
    private bool _updateChecked;
    private readonly HashSet<string> _outdated = new();
    private readonly Dictionary<string, string> _latest = new();

    // Кнопка «Обновить все» в шапке инфо-блока: проверить → подписать статус → предложить обновить
    private async void UpdateAll_Click(object sender, RoutedEventArgs e)
    {
        BtnUpdateAll.IsEnabled = false;
        BtnUpdateAll.Content   = Res("InfoChecking");
        try
        {
            bool found = await CheckAllUpdatesAsync();
            if (!found)
                MessageBox.Show(this, Res("InfoAllUpToDate"), "SeniorHub",
                    MessageBoxButton.OK, MessageBoxImage.Information);
        }
        finally
        {
            BtnUpdateAll.SetResourceReference(System.Windows.Controls.ContentControl.ContentProperty, "InfoUpdateAll");
            BtnUpdateAll.IsEnabled = true;
        }
    }

    internal async Task<bool> CheckAllUpdatesAsync()
    {
        string launcherExe;
        try { launcherExe = Process.GetCurrentProcess().MainModule!.FileName!; }
        catch { launcherExe = Path.Combine(AppContext.BaseDirectory, "SeniorHub.exe"); }

        var refs = new List<Updater.AppRef>();
        foreach (var cfg in Apps.Values)
            if (cfg.GitHubRepo is not null)
            {
                var st = ResolveStatus(cfg);
                refs.Add(new Updater.AppRef(Res(cfg.ResKey), cfg.GitHubRepo, st.ExePath, false, st.Version));
            }
        refs.Add(new Updater.AppRef("Senior Hub", "andrey1b/SeniorHub", launcherExe, IsLauncher: true));

        var ups = await Updater.CheckUpdatesAsync(refs);

        // Запомнить статус и подписать строки инфо-панели («последняя версия» / «доступно X»)
        _updateChecked = true;
        _outdated.Clear(); _latest.Clear();
        foreach (var u in ups) { _outdated.Add(u.App.Name); _latest[u.App.Name] = u.Latest; }
        RefreshInfoPanel();

        if (ups.Count == 0) return false;

        bool ru = App.CurrentLanguage != "en";
        var sb = new StringBuilder();
        sb.AppendLine(ru ? "Доступны обновления:" : "Updates available:");
        sb.AppendLine();
        foreach (var u in ups) sb.AppendLine($"   •  {u.App.Name}:   {u.Installed} → {u.Latest}");
        sb.AppendLine();
        sb.AppendLine(ru
            ? "Обновить все сейчас? Будет один запрос прав администратора, установка пройдёт автоматически."
            : "Update all now? One administrator prompt; everything installs automatically.");

        if (MessageBox.Show(this, sb.ToString(), "SeniorHub",
                MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
            return true; // обновления есть, но пользователь отказался — «обновлений нет» не показываем

        try
        {
            bool launcherWillRestart = await Updater.UpdateAllAsync(ups, launcherExe);
            if (launcherWillRestart)
                Application.Current.Shutdown();   // освободить exe лаунчера; cmd обновит и перезапустит
            else
                MessageBox.Show(this,
                    ru ? "Обновление запущено. Это может занять пару минут."
                       : "Update started. This may take a couple of minutes.",
                    "SeniorHub", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (System.ComponentModel.Win32Exception)
        {
            MessageBox.Show(this, ru ? "Обновление отменено." : "Update cancelled.",
                "SeniorHub", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        return true;
    }

    private void RefreshTileVersions()
    {
        var tiles = new (string Key, System.Windows.Controls.TextBlock Tb)[]
        {
            ("Food",              VerFood),
            ("GardenPlanner",     VerGarden),
            ("HomeAccounting",    VerMoney),
            ("TakingMedications", VerMeds),
            ("TextToAudiobook",   VerAudio),
            ("PdfDrive",          VerPdf),
            ("CommunalBills",     VerUtil),
            ("MyBiography",        VerBio),
            ("Utilities",         VerTools),
        };

        foreach (var (key, tb) in tiles)
        {
            var ver = ResolveStatus(Apps[key]).Version;
            if (!string.IsNullOrEmpty(ver)) tb.Text = $"v{ver}";
        }
    }

    // Инфо-блок: одна строка на программу — «Name» установлена ДД.ММ.ГГГГ. Версия x. Папка(ссылка)
    private void RefreshInfoPanel()
    {
        InfoList.Children.Clear();
        bool ru = App.CurrentLanguage != "en";
        var normal = new SolidColorBrush(Color.FromRgb(0x33, 0x50, 0x3A));
        var dim    = new SolidColorBrush(Color.FromRgb(0x9A, 0xA8, 0x9A));
        normal.Freeze(); dim.Freeze();

        // «Деньги» (HomeAccounting) — общий журнал расходов: остальные программы читают расходы оттуда.
        // Сверху — подсказка, а если «Деньги» не установлены — заметное предупреждение.
        if (Apps.TryGetValue("HomeAccounting", out var moneyCfg))
        {
            var hint = new TextBlock
            {
                FontSize = 11, Margin = new Thickness(0, 1, 0, 5),
                TextWrapping = TextWrapping.Wrap
            };
            if (ResolveStatus(moneyCfg).Installed)
            {
                hint.Foreground = normal;
                hint.Inlines.Add(new Run(ru
                    ? "💰 «Деньги» — общий журнал расходов: «Таблетки», «Огород», «Еда» и «Коммуналка» берут расходы отсюда."
                    : "💰 “Money” is the shared expense log — Meds, Garden, Food and Utilities read expenses from it."));
            }
            else
            {
                var warn = new SolidColorBrush(Color.FromRgb(0xC6, 0x28, 0x28)); warn.Freeze();
                hint.Foreground = warn; hint.FontWeight = FontWeights.Bold;
                hint.Inlines.Add(new Run(ru
                    ? "⚠ Установите «Деньги» — без них «Таблетки», «Огород», «Еда» и «Коммуналка» не покажут расходы."
                    : "⚠ Install “Money” — without it Meds, Garden, Food and Utilities can't show expenses."));
            }
            InfoList.Children.Add(hint);
        }

        foreach (var cfg in Apps.Values)
        {
            var name = Res(cfg.ResKey);
            var st = ResolveStatus(cfg);
            var line = new TextBlock { FontSize = 11, Margin = new Thickness(0, 1, 0, 1),
                                       TextTrimming = TextTrimming.CharacterEllipsis };

            if (st.Installed)
            {
                var date = st.InstallDate?.ToString("dd.MM.yyyy") ?? "—";
                var ver  = st.Version ?? "—";
                line.Foreground = normal;
                line.Inlines.Add(new Run(ru
                    ? $"«{name}» установлена {date}. Версия {ver}. "
                    : $"“{name}” installed {date}. Version {ver}. "));
                var link = new Hyperlink(new Run(ru ? "Папка" : "Folder"));
                var folder = st.Folder;
                link.Click += (_, _) => OpenFolder(folder);
                line.Inlines.Add(link);

                // Статус обновления (после нажатия «Обновить все»)
                if (_updateChecked)
                {
                    if (_outdated.Contains(name) && _latest.TryGetValue(name, out var latest))
                    {
                        var upd = new SolidColorBrush(Color.FromRgb(0xC6, 0x28, 0x28)); upd.Freeze();
                        line.Inlines.Add(new Run(ru ? $"   — доступно {latest}" : $"   — update {latest}")
                            { Foreground = upd, FontWeight = FontWeights.Bold });
                    }
                    else
                    {
                        var ok = new SolidColorBrush(Color.FromRgb(0x2E, 0x7D, 0x32)); ok.Freeze();
                        line.Inlines.Add(new Run(ru ? "   — последняя версия" : "   — up to date")
                            { Foreground = ok });
                    }
                }
            }
            else
            {
                line.Foreground = dim;
                line.Inlines.Add(new Run(ru ? $"«{name}» — не установлена"
                                            : $"“{name}” — not installed"));
            }
            InfoList.Children.Add(line);
        }
    }

    // Кнопка «🔄» — пересканировать установленные программы (без перезапуска лаунчера)
    private void RefreshInstalled_Click(object sender, RoutedEventArgs e)
    {
        RefreshTileVersions();
        RefreshInfoPanel();
    }

    private static void OpenFolder(string? folder)
    {
        if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder)) return;
        try { Process.Start(new ProcessStartInfo(folder) { UseShellExecute = true }); }
        catch { /* папка недоступна */ }
    }

    internal void RefreshVersionText()
    {
        var ver = Assembly.GetExecutingAssembly().GetName().Version!;
        tbVersion.Text = $"{Res("VersionLabel")} {ver.Major}.{ver.Minor}.{ver.Build}";
    }

    internal void RefreshSubtitle()
    {
        var (name, _) = SharedDb.GetUserProfile();
        if (!string.IsNullOrWhiteSpace(name))
        {
            tbSubtitle.Text = App.CurrentLanguage == "en"
                ? $"Welcome, {name}!"
                : $"Добро пожаловать, {name}!";
        }
        else
        {
            tbSubtitle.Text = Res("AppSubtitle");
        }
    }

    private void OpenSettings(object sender, RoutedEventArgs e)
    {
        new SettingsWindow { Owner = this }.ShowDialog();
        RefreshVersionText();
        RefreshSubtitle();
        RefreshInfoPanel();
    }

    // ── обработчики плиток ───────────────────────────────────────────────────

    private async void LaunchFood(object sender, RoutedEventArgs e)             => await Launch("Food");
    private async void LaunchGarden(object sender, RoutedEventArgs e)           => await Launch("GardenPlanner");
    private async void LaunchHomeAccounting(object sender, RoutedEventArgs e)   => await Launch("HomeAccounting");
    private async void LaunchMeds(object sender, RoutedEventArgs e)             => await Launch("TakingMedications");
    private async void LaunchAudio(object sender, RoutedEventArgs e)            => await Launch("TextToAudiobook");
    private async void LaunchPdf(object sender, RoutedEventArgs e)              => await Launch("PdfDrive");
    private async void LaunchUtil(object sender, RoutedEventArgs e)             => await Launch("CommunalBills");
    private async void LaunchBio(object sender, RoutedEventArgs e)              => await Launch("MyBiography");
    private async void LaunchTools(object sender, RoutedEventArgs e)            => await Launch("Utilities");

    // ── основная логика ──────────────────────────────────────────────────────

    private async Task Launch(string key)
    {
        var cfg = Apps[key];
        string displayName = Res(cfg.ResKey);

        // Временно отключённые модули
        if (cfg.IsDisabled)
        {
            string msg = App.CurrentLanguage == "en"
                ? $"\"{displayName}\" will be available soon!"
                : $"Модуль «{displayName}» скоро будет доступен!";
            MessageBox.Show(msg, displayName, MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        // Поиск установленного exe
        string? exePath = FindExe(cfg);

        if (exePath is not null)
        {
            TryStart(exePath, displayName);
            return;
        }

        // Exe не найден — предложить скачать setup
        if (cfg.GitHubRepo is not null)
        {
            string prompt = App.CurrentLanguage == "en"
                ? $"\"{displayName}\" is not installed.\n\nDownload and install it now?"
                : $"Программа «{displayName}» не установлена.\n\nСкачать и установить?";

            if (MessageBox.Show(prompt, displayName,
                    MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
            {
                await Updater.DownloadAndInstallAsync(cfg.GitHubRepo, displayName);
            }
        }
        else
        {
            string hint = App.CurrentLanguage == "en"
                ? $"\"{displayName}\" is not built yet.\n\nOpen OfisPensionera.slnx in Visual Studio,\nright-click the project → Rebuild (Ctrl+Shift+B)."
                : $"Программа «{displayName}» ещё не собрана.\n\nОткройте OfisPensionera.slnx в Visual Studio,\nправая кнопка на проекте → «Пересборка» (Ctrl+Shift+B).";

            MessageBox.Show(hint, displayName, MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }

    // Статус модуля: путь к exe, папка установки, версия, дата установки.
    internal sealed record ModuleStatus(string? ExePath, string? Folder, string? Version, DateTime? InstallDate)
    {
        public bool Installed => ExePath is not null;
    }

    private string? FindExe(AppConfig cfg) => ResolveStatus(cfg).ExePath;

    // Ищет модуль: сначала dev-пути, затем записи Inno Setup в реестре —
    // HKLM (64 и 32-бит) И HKCU (установка «для текущего пользователя», напр. MenuApp).
    internal ModuleStatus ResolveStatus(AppConfig cfg)
    {
        // 1. dev-пути относительно appsRoot
        if (_appsRoot is not null)
            foreach (string rel in cfg.DevPaths)
            {
                string full = Path.GetFullPath(Path.Combine(_appsRoot, rel));
                if (File.Exists(full))
                    return new ModuleStatus(full, Path.GetDirectoryName(full), FileVer(full), SafeWriteTime(full));
            }

        // 2. реестр uninstall: все ветки
        (RegistryKey Root, string Path)[] roots =
        [
            (Registry.LocalMachine, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall"),
            (Registry.LocalMachine, @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall"),
            (Registry.CurrentUser,  @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall"),
        ];

        foreach (var (root, path) in roots)
        {
            using var key = root.OpenSubKey(path);
            if (key is null) continue;
            foreach (var name in key.GetSubKeyNames())
            {
                using var sub = key.OpenSubKey(name);
                if (sub?.GetValue("InstallLocation") is not string dir || string.IsNullOrEmpty(dir)) continue;
                string candidate = Path.Combine(dir, cfg.ExeName);
                if (!File.Exists(candidate)) continue;

                var ver  = sub.GetValue("DisplayVersion") as string ?? FileVer(candidate);
                var date = ParseInstallDate(sub.GetValue("InstallDate") as string) ?? SafeWriteTime(candidate);
                return new ModuleStatus(candidate, dir.TrimEnd('\\'), ver, date);
            }
        }

        // 3. fallback: само-обновляемые приложения (Velopack/Squirrel) в %LOCALAPPDATA%\<Имя>\,
        //    без записи в реестре, с версионными именами exe (напр. GardenPlanner-4.5.0.exe).
        var baseName = Path.GetFileNameWithoutExtension(cfg.ExeName);
        var localDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), baseName);
        if (Directory.Exists(localDir))
        {
            string? exe = File.Exists(Path.Combine(localDir, cfg.ExeName))
                ? Path.Combine(localDir, cfg.ExeName) : null;
            Version? best = null;
            foreach (var f in Directory.EnumerateFiles(localDir, baseName + "*.exe"))
            {
                var fn = Path.GetFileName(f);
                if (fn.Contains("Setup", StringComparison.OrdinalIgnoreCase) ||
                    fn.Contains("Update", StringComparison.OrdinalIgnoreCase) ||
                    fn.Contains("Uninstall", StringComparison.OrdinalIgnoreCase)) continue;
                exe ??= f; // если нет «плоского» exe — берём версионный
                var m = System.Text.RegularExpressions.Regex.Match(fn, @"\d+(\.\d+){1,3}");
                if (m.Success && Version.TryParse(m.Value, out var v) && (best is null || v > best)) best = v;
            }
            if (exe is not null)
                return new ModuleStatus(exe, localDir, best?.ToString() ?? FileVer(exe), SafeWriteTime(exe));
        }

        return new ModuleStatus(null, null, null, null);
    }

    private static string? FileVer(string exe)
    {
        try
        {
            var vi = FileVersionInfo.GetVersionInfo(exe);
            if (vi.FileMajorPart == 0 && vi.FileMinorPart == 0 && vi.FileBuildPart == 0) return null;
            return $"{vi.FileMajorPart}.{vi.FileMinorPart}.{vi.FileBuildPart}";
        }
        catch { return null; }
    }

    private static DateTime? SafeWriteTime(string path)
    {
        try { return File.GetLastWriteTime(path); } catch { return null; }
    }

    private static DateTime? ParseInstallDate(string? s)
        => DateTime.TryParseExact(s, "yyyyMMdd", null,
               System.Globalization.DateTimeStyles.None, out var d) ? d : null;

    private static void TryStart(string path, string displayName)
    {
        try
        {
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            string title = App.CurrentLanguage == "en" ? "Error" : "Ошибка";
            MessageBox.Show($"{displayName}:\n{ex.Message}", title,
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private static string? ResolveAppsRoot()
    {
        string? dir = AppContext.BaseDirectory;
        while (!string.IsNullOrEmpty(dir) && dir.Length > 3)
        {
            if (Directory.Exists(Path.Combine(dir, "apps")) &&
                Directory.Exists(Path.Combine(dir, "Launcher")))
                return Path.Combine(dir, "apps");
            dir = Path.GetDirectoryName(dir);
        }
        return null;
    }
}
