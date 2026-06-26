using System.IO;
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
    /// Тихая автоматическая копия базы при запуске: одна копия в день,
    /// хранится последние <see cref="KeepDailyBackups"/> штук. Ошибки
    /// проглатываются — резервное копирование не должно мешать работе.
    /// </summary>
    public static void AutoBackup()
    {
        try
        {
            if (!File.Exists(DbPath)) return;
            Directory.CreateDirectory(BackupsDir);

            string todayFile = Path.Combine(
                BackupsDir, $"shared_{DateTime.Now:yyyy-MM-dd}.db");

            // Копию за сегодня уже сделали — выходим
            if (!File.Exists(todayFile))
                BackupToFile(todayFile);

            CleanupOldBackups();
        }
        catch { }
    }

    /// <summary>
    /// Ручное сохранение копии в выбранный пользователем файл (флешка,
    /// «Документы» и т.п.). Бросает исключение при ошибке — вызывающий
    /// показывает сообщение.
    /// </summary>
    public static void ExportTo(string destPath) => BackupToFile(destPath);

    /// <summary>
    /// Восстановление базы из выбранного файла-копии. Бросает исключение
    /// при ошибке.
    /// </summary>
    public static void RestoreFrom(string sourcePath)
    {
        // Источник — файл-копия, приёмник — рабочая база.
        using var src = new SqliteConnection($"Data Source={sourcePath};Mode=ReadOnly");
        src.Open();
        Directory.CreateDirectory(Path.GetDirectoryName(DbPath)!);
        using var dst = Open();
        src.BackupDatabase(dst);
    }

    // Копирует рабочую базу в указанный файл через безопасный SQLite Backup API
    // (работает корректно, даже если база открыта другим соединением).
    private static void BackupToFile(string destPath)
    {
        using var src = Open();
        using var dst = new SqliteConnection($"Data Source={destPath}");
        dst.Open();
        src.BackupDatabase(dst);
    }

    // Оставляет только KeepDailyBackups самых свежих копий, остальные удаляет
    private static void CleanupOldBackups()
    {
        try
        {
            var files = Directory.GetFiles(BackupsDir, "shared_*.db");
            if (files.Length <= KeepDailyBackups) return;

            Array.Sort(files, StringComparer.Ordinal);  // имя с датой → сортировка = хронология
            for (int i = 0; i < files.Length - KeepDailyBackups; i++)
                File.Delete(files[i]);
        }
        catch { }
    }
}
