using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Windows;
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
        _ = await Updater.CheckSeniorHubUpdateAsync();
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
        };

        foreach (var (key, tb) in tiles)
        {
            var exe = FindExe(Apps[key]);
            if (exe is null) continue;
            var vi = FileVersionInfo.GetVersionInfo(exe);
            if (vi.FileMajorPart == 0 && vi.FileMinorPart == 0) continue;
            tb.Text = $"v{vi.FileMajorPart}.{vi.FileMinorPart}.{vi.FileBuildPart}";
        }
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

    private string? FindExe(AppConfig cfg)
    {
        // 1. dev-пути относительно appsRoot
        if (_appsRoot is not null)
        {
            foreach (string rel in cfg.DevPaths)
            {
                string full = Path.GetFullPath(Path.Combine(_appsRoot, rel));
                if (File.Exists(full)) return full;
            }
        }

        // 2. поиск по реестру (uninstall entries от Inno Setup)
        string? fromRegistry = FindInRegistry(cfg.ExeName);
        if (fromRegistry is not null) return fromRegistry;

        return null;
    }

    private static string? FindInRegistry(string exeName)
    {
        string[] hives =
        [
            @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall",
            @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall",
        ];

        foreach (var hivePath in hives)
        {
            using var key = Registry.LocalMachine.OpenSubKey(hivePath);
            if (key is null) continue;
            foreach (var name in key.GetSubKeyNames())
            {
                using var sub = key.OpenSubKey(name);
                var dir = sub?.GetValue("InstallLocation") as string;
                if (string.IsNullOrEmpty(dir)) continue;
                string candidate = Path.Combine(dir, exeName);
                if (File.Exists(candidate)) return candidate;
            }
        }
        return null;
    }

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
