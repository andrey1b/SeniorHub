using System.IO;
using System.IO.Compression;
using Microsoft.Data.Sqlite;

namespace OfisPensionera.Launcher;

// Общая SQLite-база %LocalAppData%\SeniorHub\shared.db
// Читается всеми приложениями офиса по договорённости о пути.
static class SharedDb
{
    public static readonly string DbPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "SeniorHub", "shared.db");

    // Папка для автоматических ежедневных копий
    private static readonly string BackupsDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "SeniorHub", "Backups");

    private const int KeepDailyBackups = 7;   // сколько последних копий хранить

    private static string ConnStr => $"Data Source={DbPath}";

    private static string LocalAppData =>
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

    // Источники данных программ SeniorHub для общего бэкапа: папка в %LOCALAPPDATA%,
    // шаблоны файлов данных и окончания имён, которые надо исключить (бинарники приложения).
    private static readonly (string Folder, string[] Patterns, string[] ExcludeSuffix)[] AppDataSources =
    {
        ("HomeAccounting",    new[] { "*.db", "*.json" }, Array.Empty<string>()),
        ("MenuApp",           new[] { "*.json" },         new[] { ".deps.json", ".runtimeconfig.json" }),
        ("CommunalBills",     new[] { "*.json" },         Array.Empty<string>()),
        ("PdfDrive",          new[] { "*.json" },         Array.Empty<string>()),
        ("TakingMedications", new[] { "*.db", "*.json" }, new[] { ".deps.json", ".runtimeconfig.json" }),
        ("MyBiography",       new[] { "*.db", "*.json" }, new[] { ".deps.json", ".runtimeconfig.json" }),
        ("Utilities",         new[] { "*.json" },         new[] { ".deps.json", ".runtimeconfig.json" }),
    };

    // Хранилище «Огорода» (WebView2 localStorage, LevelDB) — участки, заметки, фото
    private static readonly string GardenLevelDbRel = Path.Combine(
        "GardenPlanner", "app", "GardenPlanner.Maui.exe.WebView2",
        "EBWebView", "Default", "Local Storage", "leveldb");

    public static void Initialize()
    {
        try
        {
            SQLitePCL.Batteries_V2.Init();
            Directory.CreateDirectory(Path.GetDirectoryName(DbPath)!);
            using var conn = Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                CREATE TABLE IF NOT EXISTS settings (
                    key   TEXT PRIMARY KEY,
                    value TEXT NOT NULL
                );
                CREATE TABLE IF NOT EXISTS user_profile (
                    id         INTEGER PRIMARY KEY CHECK(id = 1),
                    name       TEXT,
                    birth_date TEXT,
                    updated_at TEXT DEFAULT (datetime('now'))
                );
                INSERT INTO user_profile (id) VALUES (1) ON CONFLICT DO NOTHING;
                CREATE TABLE IF NOT EXISTS pharmacy_expenses (
                    id         INTEGER PRIMARY KEY AUTOINCREMENT,
                    date       TEXT NOT NULL,
                    amount     REAL NOT NULL,
                    note       TEXT,
                    account    TEXT,
                    created_at TEXT DEFAULT (datetime('now'))
                );
                """;
            cmd.ExecuteNonQuery();
        }
        catch { }
    }

    public static string? GetSetting(string key)
    {
        try
        {
            using var conn = Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT value FROM settings WHERE key = $k";
            cmd.Parameters.AddWithValue("$k", key);
            return cmd.ExecuteScalar() as string;
        }
        catch { return null; }
    }

    public static void SetSetting(string key, string value)
    {
        try
        {
            using var conn = Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                INSERT INTO settings(key,value) VALUES($k,$v)
                ON CONFLICT(key) DO UPDATE SET value=$v
                """;
            cmd.Parameters.AddWithValue("$k", key);
            cmd.Parameters.AddWithValue("$v", value);
            cmd.ExecuteNonQuery();
        }
        catch { }
    }

    public static (string? Name, string? BirthDate) GetUserProfile()
    {
        try
        {
            using var conn = Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT name, birth_date FROM user_profile WHERE id = 1";
            using var r = cmd.ExecuteReader();
            if (r.Read())
                return (r.IsDBNull(0) ? null : r.GetString(0),
                        r.IsDBNull(1) ? null : r.GetString(1));
        }
        catch { }
        return (null, null);
    }

    public static void SetUserProfile(string? name, string? birthDate)
    {
        try
        {
            using var conn = Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                UPDATE user_profile
                SET name=$n, birth_date=$b, updated_at=datetime('now')
                WHERE id = 1
                """;
            cmd.Parameters.AddWithValue("$n", name       ?? (object)DBNull.Value);
            cmd.Parameters.AddWithValue("$b", birthDate  ?? (object)DBNull.Value);
            cmd.ExecuteNonQuery();
        }
        catch { }
    }

    private static SqliteConnection Open()
    {
        var conn = new SqliteConnection(ConnStr);
        conn.Open();
        return conn;
    }

    // ── Резервное копирование ────────────────────────────────────────────────

    /// <summary>
    /// Тихая автоматическая копия ВСЕХ данных SeniorHub при запуске: один
    /// ZIP-архив в день, хранится последние <see cref="KeepDailyBackups"/> штук.
    /// Ошибки проглатываются — резервное копирование не должно мешать работе.
    /// </summary>
    public static void AutoBackup()
    {
        try
        {
            Directory.CreateDirectory(BackupsDir);

            string todayZip = Path.Combine(
                BackupsDir, $"SeniorHub_{DateTime.Now:yyyy-MM-dd}.zip");

            // Копию за сегодня уже сделали — выходим
            if (!File.Exists(todayZip))
                CreateFullBackup(todayZip);

            CleanupOldBackups();
        }
        catch { }
    }

    /// <summary>
    /// Ручное сохранение полной копии в выбранный пользователем ZIP-файл
    /// (флешка, «Документы» и т.п.). Бросает исключение при ошибке —
    /// вызывающий показывает сообщение.
    /// </summary>
    public static void ExportTo(string destPath) => CreateFullBackup(destPath);

    /// <summary>
    /// Восстановление из выбранной копии. Понимает новый формат (ZIP со всеми
    /// программами) и старый (одиночный .db только для shared.db).
    /// Бросает исключение при ошибке.
    /// </summary>
    public static void RestoreFrom(string sourcePath)
    {
        if (sourcePath.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
            RestoreFullBackup(sourcePath);
        else
            RestoreSharedDbOnly(sourcePath);
    }

    // ── Полный бэкап (ZIP всех программ) ──────────────────────────────────────

    // Собирает ZIP: shared.db (через безопасный Backup API) + данные всех
    // программ офиса. Пути внутри архива — относительно %LOCALAPPDATA%,
    // поэтому восстановление кладёт файлы туда же, откуда взяло.
    private static void CreateFullBackup(string zipPath)
    {
        string tmpZip = zipPath + ".tmp";
        if (File.Exists(tmpZip)) File.Delete(tmpZip);

        string? sharedSnap = null;
        try
        {
            using (var zip = ZipFile.Open(tmpZip, ZipArchiveMode.Create))
            {
                // 1. shared.db — консистентный снимок через SQLite Backup API
                if (File.Exists(DbPath))
                {
                    sharedSnap = Path.Combine(
                        Path.GetTempPath(), $"shared_snap_{Guid.NewGuid():N}.db");
                    BackupSharedTo(sharedSnap);
                    AddFileToZip(zip, sharedSnap, "SeniorHub/shared.db");
                }

                // 2. Файловые данные WPF-программ
                foreach (var (folder, patterns, exclude) in AppDataSources)
                {
                    string dir = Path.Combine(LocalAppData, folder);
                    if (!Directory.Exists(dir)) continue;

                    foreach (var pattern in patterns)
                        foreach (var file in Directory.EnumerateFiles(dir, pattern))
                        {
                            string name = Path.GetFileName(file);
                            if (exclude.Any(s => name.EndsWith(s, StringComparison.OrdinalIgnoreCase)))
                                continue;
                            AddFileToZip(zip, file, $"{folder}/{name}");
                        }
                }

                // 3. Хранилище «Огорода» (LevelDB) — копируем папку целиком
                string gardenDir = Path.Combine(LocalAppData, GardenLevelDbRel);
                if (Directory.Exists(gardenDir))
                    foreach (var file in Directory.EnumerateFiles(gardenDir))
                    {
                        string rel = GardenLevelDbRel.Replace(Path.DirectorySeparatorChar, '/');
                        AddFileToZip(zip, file, $"{rel}/{Path.GetFileName(file)}");
                    }
            }

            if (File.Exists(zipPath)) File.Delete(zipPath);
            File.Move(tmpZip, zipPath);
        }
        finally
        {
            if (sharedSnap is not null && File.Exists(sharedSnap))
                try { File.Delete(sharedSnap); } catch { }
            if (File.Exists(tmpZip))
                try { File.Delete(tmpZip); } catch { }
        }
    }

    // Распаковывает ZIP-копию обратно в %LOCALAPPDATA%. shared.db
    // восстанавливается через Backup API (безопасно при открытой базе),
    // остальные файлы — перезаписью.
    private static void RestoreFullBackup(string zipPath)
    {
        using var zip = ZipFile.OpenRead(zipPath);
        foreach (var entry in zip.Entries)
        {
            if (string.IsNullOrEmpty(entry.Name)) continue; // папка

            string relWin = entry.FullName.Replace('/', Path.DirectorySeparatorChar);

            // shared.db — через безопасный Backup API
            if (relWin.Equals(Path.Combine("SeniorHub", "shared.db"),
                    StringComparison.OrdinalIgnoreCase))
            {
                string tmp = Path.Combine(
                    Path.GetTempPath(), $"shared_restore_{Guid.NewGuid():N}.db");
                try
                {
                    entry.ExtractToFile(tmp, overwrite: true);
                    RestoreSharedDbOnly(tmp);
                }
                finally { try { File.Delete(tmp); } catch { } }
                continue;
            }

            string dest = Path.Combine(LocalAppData, relWin);
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
                entry.ExtractToFile(dest, overwrite: true);
            }
            catch { /* файл занят запущенной программой — пропускаем */ }
        }
    }

    // Восстановление только shared.db из одиночного .db (старый формат копии).
    private static void RestoreSharedDbOnly(string sourceDbPath)
    {
        using var src = new SqliteConnection($"Data Source={sourceDbPath};Mode=ReadOnly");
        src.Open();
        Directory.CreateDirectory(Path.GetDirectoryName(DbPath)!);
        using var dst = Open();
        src.BackupDatabase(dst);
    }

    // Снимок рабочей базы в указанный файл через SQLite Backup API
    // (работает корректно, даже если база открыта другим соединением).
    private static void BackupSharedTo(string destPath)
    {
        using var src = Open();
        using var dst = new SqliteConnection($"Data Source={destPath}");
        dst.Open();
        src.BackupDatabase(dst);
    }

    // Добавляет файл в архив, открывая его с разделяемым доступом —
    // чтобы не упасть, если файл занят работающей программой.
    private static void AddFileToZip(ZipArchive zip, string sourcePath, string entryName)
    {
        try
        {
            var entry = zip.CreateEntry(entryName, CompressionLevel.Optimal);
            using var fs = new FileStream(
                sourcePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var es = entry.Open();
            fs.CopyTo(es);
        }
        catch { /* файл занят/недоступен — пропускаем, не валим весь бэкап */ }
    }

    // Оставляет только KeepDailyBackups самых свежих копий (новый ZIP и старый .db)
    private static void CleanupOldBackups()
    {
        try
        {
            foreach (var pattern in new[] { "SeniorHub_*.zip", "shared_*.db" })
            {
                var files = Directory.GetFiles(BackupsDir, pattern);
                if (files.Length <= KeepDailyBackups) continue;

                Array.Sort(files, StringComparer.Ordinal); // имя с датой → сортировка = хронология
                for (int i = 0; i < files.Length - KeepDailyBackups; i++)
                    File.Delete(files[i]);
            }
        }
        catch { }
    }
}
