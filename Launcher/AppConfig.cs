namespace OfisPensionera.Launcher;

/// <summary>
/// Конфигурация одного модуля лаунчера.
/// </summary>
record AppConfig(
    string ResKey,         // ключ локализованного имени в ресурсах
    string? GitHubRepo,    // "owner/repo" для скачивания setup, null если недоступно
    string ExeName,        // имя exe для поиска в реестре
    string[] DevPaths,     // пути относительно appsRoot (dev-окружение)
    bool IsDisabled = false
);
