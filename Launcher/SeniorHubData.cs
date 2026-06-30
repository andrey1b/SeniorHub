using System.IO;
using Microsoft.Data.Sqlite;

namespace SeniorHub.Data;

// ╔══════════════════════════════════════════════════════════════════════════╗
// ║  Общий слой доступа к данным Senior Hub.                                   ║
// ║                                                                            ║
// ║  Это КАНОНИЧЕСКАЯ версия файла. Чтобы программа офиса работала с общей      ║
// ║  базой, скопируйте этот файл в её проект КАК ЕСТЬ (менять не нужно) и       ║
// ║  вызовите SeniorHubData.Initialize() при старте.                           ║
// ║                                                                            ║
// ║  Единая база: %LOCALAPPDATA%\SeniorHub\shared.db                           ║
// ║  Зависимости: Microsoft.Data.Sqlite.Core + SQLitePCLRaw.bundle_winsqlite3  ║
// ║                                                                            ║
// ║  Режим WAL + busy_timeout позволяют нескольким программам работать с базой ║
// ║  одновременно без блокировок. Все методы глотают ошибки — общий слой не    ║
// ║  должен ронять программу.                                                  ║
// ╚══════════════════════════════════════════════════════════════════════════╝
static class SeniorHubData
{
    // Текущая версия схемы общей базы (PRAGMA user_version). Поднимать при
    // добавлении таблиц/колонок и дописывать миграцию в Migrate().
    private const int SchemaVersion = 1;

    public static readonly string DbPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "SeniorHub", "shared.db");

    private static string ConnStr => $"Data Source={DbPath}";

    // Создаёт базу/схему, включает WAL. Безопасно вызывать многократно.
    public static void Initialize()
    {
        try
        {
            SQLitePCL.Batteries_V2.Init();
            Directory.CreateDirectory(Path.GetDirectoryName(DbPath)!);

            using var conn = Open();

            // WAL — параллельное чтение во время записи; хранится в файле базы.
            using (var pragma = conn.CreateCommand())
            {
                pragma.CommandText = "PRAGMA journal_mode=WAL;";
                pragma.ExecuteNonQuery();
            }

            using (var cmd = conn.CreateCommand())
            {
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

            Migrate(conn);
        }
        catch { }
    }

    // Пошаговые миграции схемы по PRAGMA user_version (на будущее).
    private static void Migrate(SqliteConnection conn)
    {
        int ver;
        using (var get = conn.CreateCommand())
        {
            get.CommandText = "PRAGMA user_version;";
            ver = Convert.ToInt32(get.ExecuteScalar());
        }
        if (ver >= SchemaVersion) return;

        // Будущие миграции: if (ver < 2) { ... }

        using var set = conn.CreateCommand();
        set.CommandText = $"PRAGMA user_version = {SchemaVersion};";
        set.ExecuteNonQuery();
    }

    // Открывает соединение с общей базой. Метод публичен: программы могут
    // выполнять собственные запросы к своим таблицам в общей базе.
    public static SqliteConnection Open()
    {
        var conn = new SqliteConnection(ConnStr);
        conn.Open();
        // busy_timeout задаётся на каждое соединение: ждать до 5 с, если база
        // занята другой программой, вместо мгновенной ошибки «database is locked».
        using var pragma = conn.CreateCommand();
        pragma.CommandText = "PRAGMA busy_timeout=5000;";
        pragma.ExecuteNonQuery();
        return conn;
    }

    // ── Настройки (key/value) ─────────────────────────────────────────────────

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

    // ── Профиль пользователя (общий для всех программ офиса) ───────────────────

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
            cmd.Parameters.AddWithValue("$n", name      ?? (object)DBNull.Value);
            cmd.Parameters.AddWithValue("$b", birthDate ?? (object)DBNull.Value);
            cmd.ExecuteNonQuery();
        }
        catch { }
    }
}
