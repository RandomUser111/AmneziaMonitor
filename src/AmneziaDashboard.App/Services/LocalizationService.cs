using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Avalonia;

namespace AmneziaDashboard.App.Services;

public static class LocalizationService
{
    public const string English = "English";
    public const string Russian = "Russian";

    private static readonly IReadOnlyList<LocalizedResource> Resources =
    [
        new("Loc.ClientConfig.Created", "Client created", "Клиент создан"),
        new("Loc.ClientConfig.SaveNow", "Save the profile now. It contains the new client private key, which Amnezia Monitor intentionally does not store locally.", "Сохраните профиль сейчас. Он содержит приватный ключ нового клиента, который Amnezia Monitor намеренно не сохраняет локально."),
        new("Loc.ClientConfig.QrTitle", "QR code for phone", "QR-код для телефона"),
        new("Loc.ClientConfig.QrDescription", "Scan the code in WireGuard or a compatible Amnezia VPN client. The QR code contains the complete .conf profile of the new client.", "Сканируйте код в приложении WireGuard или совместимом клиенте Amnezia VPN. В QR-код встроен полный .conf-профиль нового клиента."),
        new("Loc.ClientConfig.QrFallback", "If scanning is unavailable, you can copy or save the configuration to a file below.", "Если сканирование недоступно, ниже можно скопировать или сохранить конфигурацию в файл."),
        new("Loc.Common.Copy", "Copy", "Копировать"),
        new("Loc.Common.SaveConf", "Save .conf", "Сохранить .conf"),
        new("Loc.Common.Done", "Done", "Готово"),
        new("Loc.ClientConfig.WindowTitle", "Client configuration", "Конфигурация клиента"),
        new("Loc.Clients.Title", "Clients", "Клиенты"),
        new("Loc.Clients.OnlineLabel", "Online:", "Онлайн:"),
        new("Loc.Common.Of", "of", "из"),
        new("Loc.Clients.SearchPlaceholder", "Search by name, IP, protocol, endpoint, or key", "Поиск по имени, IP, протоколу, endpoint или ключу"),
        new("Loc.Common.AutoRefresh5", "Auto-refresh: 5 sec", "Автообновление: 5 сек"),
        new("Loc.Clients.NoData", "No client data", "Нет данных о клиентах"),
        new("Loc.Common.NothingFound", "Nothing found", "Ничего не найдено"),
        new("Loc.Clients.AdjustFilter", "Change the search text or status filter.", "Измените строку поиска или фильтр статуса."),
        new("Loc.Common.Client", "Client", "Клиент"),
        new("Loc.Common.Protocol", "Protocol", "Протокол"),
        new("Loc.Clients.DownloadTotal", "↓ total", "↓ всего"),
        new("Loc.Clients.UploadTotal", "↑ total", "↑ всего"),
        new("Loc.Common.Speed", "Speed", "Скорость"),
        new("Loc.Common.Actions", "Actions", "Действия"),
        new("Loc.Clients.NewClient", "New client", "Новый клиент"),
        new("Loc.Clients.All", "All clients", "Все клиенты"),
        new("Loc.Clients.OnlineOnly", "Online only", "Только онлайн"),
        new("Loc.Clients.OfflineOnly", "Offline only", "Только офлайн"),
        new("Loc.Clients.Rename", "Rename client", "Переименовать клиента"),
        new("Loc.Clients.RestoreConfig", "Restore configuration", "Восстановить конфигурацию"),
        new("Loc.Clients.Revoke", "Revoke access", "Отозвать доступ"),
        new("Loc.Connect.SavedServers", "Saved servers", "Сохранённые серверы"),
        new("Loc.Common.Name", "Name", "Название"),
        new("Loc.Connect.Host", "IP address or server name", "IP-адрес или имя сервера"),
        new("Loc.Connect.User", "User", "Пользователь"),
        new("Loc.Connect.Port", "SSH port", "SSH-порт"),
        new("Loc.Connect.Password", "Password", "Пароль"),
        new("Loc.Common.Delete", "Delete", "Удалить"),
        new("Loc.Connect.SaveProfile", "Save server settings", "Сохранить параметры сервера"),
        new("Loc.Connect.SavePassword", "Save SSH password securely", "Сохранить SSH-пароль безопасно"),
        new("Loc.Connect.AutoConnect", "Connect to this server automatically at startup", "Подключаться к этому серверу автоматически при запуске"),
        new("Loc.Common.Cancel", "Cancel", "Отмена"),
        new("Loc.Connect.WindowTitle", "Server connection", "Подключение к серверу"),
        new("Loc.CreateClient.Title", "New VPN client", "Новый VPN-клиент"),
        new("Loc.CreateClient.Name", "Client name", "Имя клиента"),
        new("Loc.CreateClient.NamePlaceholder", "For example: Laptop", "Например: Ноутбук"),
        new("Loc.Common.Create", "Create", "Создать"),
        new("Loc.Nav.Overview", "Overview", "Обзор"),
        new("Loc.Dashboard.Subtitle", "VPN server status", "Состояние VPN-сервера"),
        new("Loc.Dashboard.Server", "SERVER", "СЕРВЕР"),
        new("Loc.Dashboard.Disk", "Disk", "Диск"),
        new("Loc.Dashboard.Network", "Network:", "Сеть:"),
        new("Loc.Dashboard.AmneziaContainers", "Amnezia containers", "Контейнеры Amnezia"),
        new("Loc.Dashboard.Clients", "CLIENTS", "КЛИЕНТЫ"),
        new("Loc.Common.Online", "Online", "Онлайн"),
        new("Loc.Common.Total", "Total", "Всего"),
        new("Loc.Dashboard.Traffic", "TRAFFIC", "ТРАФИК"),
        new("Loc.Dashboard.Received", "Received", "Получено"),
        new("Loc.Dashboard.Sent", "Sent", "Отправлено"),
        new("Loc.DeleteServer.Question", "Delete saved server?", "Удалить сохранённый сервер?"),
        new("Loc.DeleteServer.SafeNote", "The VPN server and its Docker containers will not be affected.", "VPN-сервер и его Docker-контейнеры затронуты не будут."),
        new("Loc.DeleteServer.LocalOnly", "The profile and saved SSH password will be removed only from this computer.", "Профиль и сохранённый SSH-пароль будут удалены только с этого компьютера."),
        new("Loc.DeleteServer.Title", "Delete server", "Удалить сервер"),
        new("Loc.History.Title", "History", "История"),
        new("Loc.History.NoData", "No chart data yet", "Пока нет данных для графиков"),
        new("Loc.History.StorageNote", "Amnezia Monitor stores one monitoring sample approximately every 30 seconds.", "Amnezia Monitor сохраняет одну точку мониторинга примерно раз в 30 секунд."),
        new("Loc.History.ServerLoad", "Server load", "Загрузка сервера"),
        new("Loc.History.Network", "Network", "Сеть"),
        new("Loc.History.NetworkSpeed", "External interface speed", "Скорость внешнего интерфейса"),
        new("Loc.History.OnlineClients", "VPN clients online", "VPN-клиенты онлайн"),
        new("Loc.History.OnlineClientsDescription", "Number of active WireGuard / AmneziaWG clients", "Количество активных WireGuard / AmneziaWG клиентов"),
        new("Loc.History.1h", "1 h", "1 ч"),
        new("Loc.History.6h", "6 h", "6 ч"),
        new("Loc.History.24h", "24 h", "24 ч"),
        new("Loc.History.7d", "7 days", "7 дней"),
        new("Loc.Traffic.Title", "Client traffic", "Трафик клиентов"),
        new("Loc.Traffic.30d", "30 days", "30 дней"),
        new("Loc.Traffic.CollectionNote", "Per-client usage is calculated from traffic counter deltas stored approximately every 30 seconds. Statistics start accumulating after this version is installed.", "Использование по клиентам рассчитывается по изменениям счётчиков трафика, сохраняемым примерно каждые 30 секунд. Статистика начинает накапливаться после установки этой версии."),
        new("Loc.Traffic.Total", "TOTAL TRAFFIC", "ВСЕГО ТРАФИКА"),
        new("Loc.Traffic.Download", "DOWNLOAD", "СКАЧАНО"),
        new("Loc.Traffic.Upload", "UPLOAD", "ОТПРАВЛЕНО"),
        new("Loc.Traffic.Clients", "CLIENTS", "КЛИЕНТОВ"),
        new("Loc.Traffic.Search", "Search by client, VPN IP, protocol, or key", "Поиск по клиенту, VPN IP, протоколу или ключу"),
        new("Loc.Traffic.NoData", "No client traffic data yet", "Пока нет данных о трафике клиентов"),
        new("Loc.Traffic.NoDataDescription", "Keep monitoring running to accumulate per-client usage statistics for the selected server.", "Оставьте мониторинг включённым, чтобы накопить статистику использования по клиентам выбранного сервера."),
        new("Loc.Traffic.Client", "Client", "Клиент"),
        new("Loc.Traffic.Protocol", "Protocol", "Протокол"),
        new("Loc.Traffic.VpnIp", "VPN IP", "VPN IP"),
        new("Loc.Traffic.Share", "Share", "Доля"),
        new("Loc.Common.Refresh", "Refresh", "Обновить"),
        new("Loc.Logs.Title", "Logs", "Журнал"),
        new("Loc.Logs.Search", "Search logs", "Поиск по журналу"),
        new("Loc.Logs.Empty", "The log is empty", "Журнал пока пуст"),
        new("Loc.Logs.EmptyDescription", "Connection, client, and Docker events will appear here.", "События подключения, клиентов и Docker появятся здесь."),
        new("Loc.Logs.Time", "Time", "Время"),
        new("Loc.Logs.Level", "Level", "Уровень"),
        new("Loc.Logs.Category", "Category", "Категория"),
        new("Loc.Logs.Event", "Event", "Событие"),
        new("Loc.Common.Clear", "Clear", "Очистить"),
        new("Loc.Logs.EventsTab", "Events", "События"),
        new("Loc.Nav.Servers", "Servers", "Серверы"),
        new("Loc.Nav.Protocols", "Protocols", "Протоколы"),
        new("Loc.Nav.Settings", "Settings", "Настройки"),
        new("Loc.Protocols.Subtitle", "Installed Amnezia VPN Docker containers", "Установленные Docker-контейнеры Amnezia VPN"),
        new("Loc.Protocols.System", "SYSTEM", "СИСТЕМА"),
        new("Loc.Protocols.Ports", "PORTS", "ПОРТЫ"),
        new("Loc.Protocols.DockerStatus", "DOCKER STATUS", "СТАТУС DOCKER"),
        new("Loc.Rename.NewName", "New name", "Новое имя"),
        new("Loc.Common.Save", "Save", "Сохранить"),
        new("Loc.Restore.Question", "Restore configuration?", "Восстановить конфигурацию?"),
        new("Loc.Restore.PrivateKeyNote", "The original client PrivateKey is not stored on the VPN server, so the old .conf cannot be restored exactly.", "Исходный PrivateKey клиента не хранится на VPN-сервере, поэтому старый .conf невозможно восстановить буквально."),
        new("Loc.Restore.KeyPairOnly", "Only the client key pair will be reissued", "Будет перевыпущена только ключевая пара клиента"),
        new("Loc.Restore.Details", "The name and VPN IP will be preserved. The server entry will remain the same client. The old configuration will stop working, and Amnezia Monitor will show a new .conf and QR code. Backups will be created before the change.", "Имя и VPN IP сохранятся. Серверная запись останется тем же клиентом. Старый конфиг перестанет работать, а Amnezia Monitor покажет новый .conf и QR-код. Перед изменением будут созданы резервные копии."),
        new("Loc.Restore.Button", "Restore config", "Восстановить конфиг"),
        new("Loc.Revoke.Question", "Revoke VPN access?", "Отозвать VPN-доступ?"),
        new("Loc.Revoke.Description", "The peer will be removed from the active interface and server configuration.", "Peer будет удалён из активного интерфейса и серверной конфигурации."),
        new("Loc.Revoke.BackupTitle", "A backup will be created before the change", "Перед изменением будет создана резервная копия"),
        new("Loc.Revoke.BackupDetails", "Amnezia Monitor will save copies of the server .conf and clientsTable inside the VPN container. The client configuration on the device will no longer connect.", "Amnezia Monitor сохранит копии серверного .conf и clientsTable внутри VPN-контейнера. Сам клиентский конфиг на устройстве перестанет подключаться."),
        new("Loc.Servers.Subtitle", "Saved Amnezia VPN servers and quick switching between them", "Сохранённые Amnezia VPN серверы и быстрое переключение между ними"),
        new("Loc.Servers.Current", "Current server:", "Текущий сервер:"),
        new("Loc.Servers.TopSwitchHint", "You can also switch from the top bar", "Также можно переключаться из верхней панели"),
        new("Loc.Servers.None", "No saved servers yet", "Сохранённых серверов пока нет"),
        new("Loc.Servers.NoneDescription", "Add your first VPS to make it available in the quick switch list.", "Добавьте первый VPS, чтобы он появился в списке быстрого переключения."),
        new("Loc.Servers.Credentials", "CREDENTIALS", "УЧЁТНЫЕ ДАННЫЕ"),
        new("Loc.Servers.Startup", "STARTUP", "ЗАПУСК"),
        new("Loc.Servers.AddPlus", "+ Add server", "+ Добавить сервер"),
        new("Loc.Servers.Add", "Add server", "Добавить сервер"),
        new("Loc.Common.Connect", "Connect", "Подключиться"),
        new("Loc.Common.Edit", "Edit", "Изменить"),
        new("Loc.Settings.Subtitle", "Amnezia Monitor settings", "Параметры Amnezia Monitor"),
        new("Loc.Settings.Theme", "Appearance", "Тема оформления"),
        new("Loc.Settings.ThemeDescription", "Changes are applied immediately to the entire application.", "Переключение применяется сразу ко всему приложению."),
        new("Loc.Settings.Storage", "Settings storage", "Хранение настроек"),
        new("Loc.Settings.StorageDescription", "Server profiles are stored locally. SSH passwords are stored only in the system secure storage: Windows Credential Manager or Linux Secret Service.", "Профили серверов сохраняются локально. SSH-пароли хранятся только в системном защищённом хранилище: Windows Credential Manager или Linux Secret Service."),
        new("Loc.Settings.Light", "Light", "Светлая"),
        new("Loc.Settings.Dark", "Dark", "Тёмная"),
        new("Loc.Settings.System", "System", "Как в системе"),
        new("Loc.Settings.Language", "Language", "Язык"),
        new("Loc.Settings.LanguageDescription", "The interface language is applied immediately and saved for the next launch.", "Язык интерфейса применяется сразу и сохраняется для следующего запуска."),
        new("Loc.Settings.DesktopIntegration", "Desktop integration", "Интеграция с рабочим столом"),
        new("Loc.Settings.Notifications", "Show desktop notifications", "Показывать уведомления на рабочем столе"),
        new("Loc.Settings.MinimizeToTray", "Minimize to system tray", "Сворачивать в системный трей"),
        new("Loc.Settings.CloseToTray", "Close button hides to system tray", "Кнопка закрытия скрывает программу в системный трей"),
        new("Loc.Settings.TrayDescription", "The tray keeps monitoring active in the background. Desktop notifications report lost/restored SSH connectivity and VPN container state changes.", "Трей оставляет мониторинг работать в фоне. Уведомления сообщают о потере/восстановлении SSH-связи и изменении состояния VPN-контейнеров."),
        new("Loc.Settings.Updates", "Updates", "Обновления"),
        new("Loc.Settings.UpdatesDescription", "Amnezia Monitor can check GitHub Releases for a newer stable version. No account or telemetry is used.", "Amnezia Monitor может проверять GitHub Releases на наличие новой стабильной версии. Учётная запись и телеметрия не используются."),
        new("Loc.Settings.AutomaticUpdateChecks", "Check for updates automatically", "Автоматически проверять обновления"),
        new("Loc.Settings.CheckForUpdates", "Check now", "Проверить сейчас"),
        new("Loc.Update.ViewRelease", "View release", "Открыть релиз"),
        new("Loc.Nav.Backup", "Backup & restore", "Бэкап и перенос"),
        new("Loc.Backup.Title", "Backup & migration", "Резервная копия и перенос"),
        new("Loc.Backup.Subtitle", "Create a portable Amnezia server backup or restore it to the currently connected server.", "Создание переносимой копии Amnezia-сервера или восстановление на текущем подключённом сервере."),
        new("Loc.Backup.Create", "Create full backup", "Создать полную копию"),
        new("Loc.Backup.Restore", "Restore / migrate", "Восстановить / перенести"),
        new("Loc.Backup.CreateDescription", "Includes /opt/amnezia, Amnezia Docker container metadata, named volumes and Docker images. The backup may be large.", "Включает /opt/amnezia, метаданные Docker-контейнеров Amnezia, именованные тома и Docker-образы. Копия может быть большой."),
        new("Loc.Backup.RestoreDescription", "Restores keys, users, ports, VPN subnets, volumes and containers on the connected target server.", "Восстанавливает ключи, пользователей, порты, VPN-подсети, тома и контейнеры на подключённом целевом сервере."),
        new("Loc.Backup.EndpointWarning", "Important: client configs continue working without changes only if their endpoint still resolves to the new server (for example, the same floating IP is moved, or the same DNS name is updated). A client config that contains the old literal IP cannot discover a new IP automatically.", "Важно: клиентские конфиги продолжат работать без изменений только если их endpoint по-прежнему указывает на новый сервер (например, перенесён тот же floating IP или обновлено то же DNS-имя). Конфиг с буквальным старым IP не может автоматически узнать новый IP."),
        new("Loc.Backup.SecretWarning", "The backup contains private VPN server keys and other sensitive configuration. Store the .ambackup file and its checksum securely. Backup encryption is planned for a future release.", "Резервная копия содержит приватные ключи VPN-сервера и другие чувствительные данные. Храните файл .ambackup и его контрольную сумму в безопасном месте. Шифрование резервных копий планируется в следующей версии."),
        new("Loc.Backup.NoServer", "Connect to a server first.", "Сначала подключитесь к серверу."),
        new("Loc.Backup.ProgressIdle", "Ready", "Готово"),
        new("Loc.Backup.SelectSave", "Save Amnezia backup", "Сохранить резервную копию Amnezia"),
        new("Loc.Backup.SelectOpen", "Select Amnezia backup", "Выберите резервную копию Amnezia"),
        new("Loc.Backup.RestoreConfirm", "Restoring will stop/replace Amnezia containers on the target server. Use a clean target server whenever possible.", "Восстановление остановит/заменит контейнеры Amnezia на целевом сервере. По возможности используйте чистый целевой сервер."),
    ];

    public static string CurrentLanguage { get; private set; } = English;

    public static bool IsRussian => CurrentLanguage == Russian;

    public static event EventHandler? LanguageChanged;

    public static void ApplyLanguage(string? language, bool raiseEvent = true)
    {
        CurrentLanguage = Normalize(language);

        var culture = IsRussian ? new CultureInfo("ru-RU") : new CultureInfo("en-US");
        CultureInfo.CurrentCulture = culture;
        CultureInfo.CurrentUICulture = culture;
        CultureInfo.DefaultThreadCurrentCulture = culture;
        CultureInfo.DefaultThreadCurrentUICulture = culture;

        if (Application.Current is not null)
        {
            foreach (var item in Resources)
                Application.Current.Resources[item.Key] = IsRussian ? item.Russian : item.English;
        }

        if (raiseEvent)
            LanguageChanged?.Invoke(null, EventArgs.Empty);
    }

    public static string T(string english, string russian) => IsRussian ? russian : english;

    public static string Get(string key)
    {
        foreach (var item in Resources)
        {
            if (item.Key == key)
                return IsRussian ? item.Russian : item.English;
        }

        return key;
    }

    public static string TranslateExternalMessage(string? message)
    {
        if (string.IsNullOrWhiteSpace(message) || IsRussian)
            return message ?? string.Empty;

        var text = message;
        foreach (var pair in ExternalMessageTranslations.OrderByDescending(x => x.Key.Length))
            text = text.Replace(pair.Key, pair.Value, StringComparison.Ordinal);

        return text;
    }

    private static string Normalize(string? language) =>
        language?.Trim().ToLowerInvariant() switch
        {
            "russian" or "ru" or "ru-ru" => Russian,
            _ => English
        };

    private static readonly IReadOnlyDictionary<string, string> ExternalMessageTranslations =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["Не удалось установить SSH-соединение."] = "Could not establish an SSH connection.",
            ["Ошибка авторизации. Проверьте логин и пароль."] = "Authentication failed. Check the username and password.",
            ["Ошибка авторизации. Проверьте SSH-доступ."] = "Authentication failed. Check SSH access.",
            ["Сервер не ответил за отведённое время."] = "The server did not respond in time.",
            ["SSH-соединение отклонено сервером. Проверьте адрес и порт."] = "The SSH connection was refused. Check the address and port.",
            ["SSH-соединение отклонено сервером."] = "The SSH connection was refused by the server.",
            ["Операция отменена."] = "The operation was cancelled.",
            ["Некорректное имя Docker-контейнера."] = "Invalid Docker container name.",
            ["Имя клиента не может быть пустым."] = "The client name cannot be empty.",
            ["Имя клиента слишком длинное (максимум 100 символов)."] = "The client name is too long (maximum 100 characters).",
            ["Контейнер не является активным WireGuard/AmneziaWG или интерфейс не запущен."] = "The container is not an active WireGuard/AmneziaWG container or the interface is not running.",
            ["Не удалось определить серверный .conf-файл."] = "Could not determine the server .conf file.",
            ["Не удалось прочитать серверную конфигурацию."] = "Could not read the server configuration.",
            ["В серверной конфигурации не найден Address интерфейса."] = "The interface Address was not found in the server configuration.",
            ["В VPN-подсети не найден свободный IPv4-адрес."] = "No free IPv4 address was found in the VPN subnet.",
            ["Не удалось сгенерировать приватный ключ клиента."] = "Could not generate the client private key.",
            ["Не удалось получить public key нового клиента."] = "Could not obtain the new client public key.",
            ["Не удалось получить public key сервера."] = "Could not obtain the server public key.",
            ["Не удалось прочитать PresharedKey сервера."] = "Could not read the server PresharedKey.",
            ["Не удалось определить UDP-порт VPN-сервера."] = "Could not determine the VPN server UDP port.",
            ["Клиент с таким public key уже присутствует в clientsTable."] = "A client with this public key already exists in clientsTable.",
            ["Для этого контейнера пока не поддерживается переименование клиентов."] = "Renaming clients is not supported for this container yet.",
            ["Клиент не найден в clientsTable."] = "The client was not found in clientsTable.",
            ["Некорректный WireGuard/AmneziaWG public key."] = "Invalid WireGuard/AmneziaWG public key.",
            ["Не удалось определить активный WireGuard/AmneziaWG интерфейс."] = "Could not determine the active WireGuard/AmneziaWG interface.",
            ["Не найден серверный .conf-файл этого клиента."] = "The server .conf file for this client was not found.",
            ["Peer клиента не найден в серверной конфигурации."] = "The client peer was not found in the server configuration.",
            ["Не удалось определить VPN IP существующего клиента."] = "Could not determine the existing client VPN IP.",
            ["Не удалось сгенерировать новый приватный ключ клиента."] = "Could not generate a new client private key.",
            ["Не удалось получить новый public key клиента."] = "Could not obtain the new client public key.",
            ["Не удалось прочитать PresharedKey клиента."] = "Could not read the client PresharedKey.",
            ["Не удалось подготовить обновлённую серверную конфигурацию."] = "Could not prepare the updated server configuration.",
            ["Не удалось обновить служебную запись клиента."] = "Could not update the client metadata record.",
            ["Некорректная операция Docker."] = "Invalid Docker operation.",
            ["Не удалось проверить состояние контейнера после операции."] = "Could not verify the container state after the operation.",
            ["Docker выполнил команду, но итоговое состояние контейнера отличается от ожидаемого."] = "Docker executed the command, but the final container state is not the expected state.",
            ["Некорректный путь конфигурации."] = "Invalid configuration path.",
            ["Некорректный путь файла в контейнере."] = "Invalid file path in the container.",
            ["Защищённое хранилище паролей недоступно. В Linux установите пакет libsecret/secret-tool."] = "Secure password storage is unavailable. On Linux, install libsecret/secret-tool.",
            ["Пустой пароль не сохраняется."] = "An empty password is not saved.",
            ["secret-tool не найден."] = "secret-tool was not found.",
            ["Для этого контейнера журнал Docker отключён настройкой log-driver=none."] = "Docker logs are disabled for this container by log-driver=none.",
            ["Это штатная конфигурация Amnezia: stdout/stderr контейнера не сохраняются, поэтому получить их задним числом невозможно. Используйте вкладку «События» для журнала операций Amnezia Monitor."] = "This is a normal Amnezia configuration: container stdout/stderr is not stored, so past output cannot be retrieved. Use the Events tab for the Amnezia Monitor operation log.",
            ["не поддерживает чтение через docker logs. Amnezia Monitor не будет считать это ошибкой контейнера. Если журнал не хранится самим драйвером, получить прошлые сообщения невозможно."] = "does not support reading through docker logs. Amnezia Monitor will not treat this as a container error. If the driver does not store the log, past messages cannot be retrieved.",
            ["неизвестный"] = "unknown",
            ["Не удалось прочитать Docker logs."] = "Could not read Docker logs.",
            ["Контейнер запущен."] = "Container started.",
            ["Контейнер остановлен."] = "Container stopped.",
            ["Контейнер перезапущен."] = "Container restarted.",
            ["Не удалось создать резервную копию конфигурации:"] = "Could not create a configuration backup:",
            ["Не удалось создать резервную копию clientsTable:"] = "Could not create a clientsTable backup:",
            ["Не удалось обновить серверную конфигурацию."] = "Could not update the server configuration.",
            ["Не удалось обновить clientsTable."] = "Could not update clientsTable.",
            ["Не удалось применить новый peer."] = "Could not apply the new peer.",
            ["Не удалось применить новый ключ."] = "Could not apply the new key.",
            ["Не удалось удалить peer из активного интерфейса."] = "Could not remove the peer from the active interface.",
            ["Доступ отозван."] = "Access revoked.",
            ["Конфигурация восстановлена."] = "Configuration restored.",
            ["Клиент создан."] = "Client created.",
        };

    private sealed record LocalizedResource(string Key, string English, string Russian);
}
