using AmneziaDashboard.Core.Interfaces;
using AmneziaDashboard.Core.Models;
using Microsoft.Data.Sqlite;

namespace AmneziaDashboard.Infrastructure.Storage;

public sealed class SqliteMonitoringHistoryStore : IMonitoringHistoryStore
{
    private readonly string _connectionString;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private bool _initialized;

    public SqliteMonitoringHistoryStore()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var directory = Path.Combine(appData, "AmneziaMonitor");
        Directory.CreateDirectory(directory);

        var databasePath = Path.Combine(directory, "monitoring.db");
        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Shared
        }.ToString();
    }

    public async Task AppendAsync(
        MonitoringHistoryRecord record,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(record);

        await _gate.WaitAsync(cancellationToken);
        try
        {
            await EnsureInitializedAsync(cancellationToken);

            await using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync(cancellationToken);

            await using var command = connection.CreateCommand();
            command.CommandText = """
                INSERT INTO monitoring_samples (
                    server_key,
                    server_name,
                    captured_at_utc,
                    cpu_percent,
                    ram_percent,
                    disk_percent,
                    download_bps,
                    upload_bps,
                    clients_online,
                    clients_total,
                    traffic_received_bytes,
                    traffic_sent_bytes)
                VALUES (
                    $serverKey,
                    $serverName,
                    $capturedAt,
                    $cpu,
                    $ram,
                    $disk,
                    $download,
                    $upload,
                    $clientsOnline,
                    $clientsTotal,
                    $trafficReceived,
                    $trafficSent);
                """;

            command.Parameters.AddWithValue("$serverKey", record.ServerKey);
            command.Parameters.AddWithValue("$serverName", record.ServerName);
            command.Parameters.AddWithValue("$capturedAt", record.CapturedAt.UtcDateTime.ToString("O"));
            AddNullableDouble(command, "$cpu", record.CpuPercent);
            AddNullableDouble(command, "$ram", record.RamPercent);
            AddNullableDouble(command, "$disk", record.DiskPercent);
            command.Parameters.AddWithValue("$download", record.DownloadBytesPerSecond);
            command.Parameters.AddWithValue("$upload", record.UploadBytesPerSecond);
            command.Parameters.AddWithValue("$clientsOnline", record.ClientsOnline);
            command.Parameters.AddWithValue("$clientsTotal", record.ClientsTotal);
            command.Parameters.AddWithValue("$trafficReceived", record.TrafficReceivedBytes);
            command.Parameters.AddWithValue("$trafficSent", record.TrafficSentBytes);

            await command.ExecuteNonQueryAsync(cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<IReadOnlyList<MonitoringHistoryRecord>> GetRangeAsync(
        string serverKey,
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(serverKey))
            return [];

        await _gate.WaitAsync(cancellationToken);
        try
        {
            await EnsureInitializedAsync(cancellationToken);

            await using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync(cancellationToken);

            await using var command = connection.CreateCommand();
            command.CommandText = """
                SELECT
                    id,
                    server_key,
                    server_name,
                    captured_at_utc,
                    cpu_percent,
                    ram_percent,
                    disk_percent,
                    download_bps,
                    upload_bps,
                    clients_online,
                    clients_total,
                    traffic_received_bytes,
                    traffic_sent_bytes
                FROM monitoring_samples
                WHERE server_key = $serverKey
                  AND captured_at_utc >= $from
                  AND captured_at_utc <= $to
                ORDER BY captured_at_utc ASC;
                """;

            command.Parameters.AddWithValue("$serverKey", serverKey);
            command.Parameters.AddWithValue("$from", from.UtcDateTime.ToString("O"));
            command.Parameters.AddWithValue("$to", to.UtcDateTime.ToString("O"));

            var result = new List<MonitoringHistoryRecord>();
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);

            while (await reader.ReadAsync(cancellationToken))
            {
                if (!DateTimeOffset.TryParse(reader.GetString(3), out var capturedAt))
                    continue;

                result.Add(new MonitoringHistoryRecord
                {
                    Id = reader.GetInt64(0),
                    ServerKey = reader.GetString(1),
                    ServerName = reader.GetString(2),
                    CapturedAt = capturedAt,
                    CpuPercent = reader.IsDBNull(4) ? null : reader.GetDouble(4),
                    RamPercent = reader.IsDBNull(5) ? null : reader.GetDouble(5),
                    DiskPercent = reader.IsDBNull(6) ? null : reader.GetDouble(6),
                    DownloadBytesPerSecond = reader.GetDouble(7),
                    UploadBytesPerSecond = reader.GetDouble(8),
                    ClientsOnline = reader.GetInt32(9),
                    ClientsTotal = reader.GetInt32(10),
                    TrafficReceivedBytes = reader.GetInt64(11),
                    TrafficSentBytes = reader.GetInt64(12)
                });
            }

            return result;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task CleanupOlderThanAsync(
        DateTimeOffset cutoff,
        CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            await EnsureInitializedAsync(cancellationToken);

            await using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync(cancellationToken);

            await using var command = connection.CreateCommand();
            command.CommandText = "DELETE FROM monitoring_samples WHERE captured_at_utc < $cutoff;";
            command.Parameters.AddWithValue("$cutoff", cutoff.UtcDateTime.ToString("O"));
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task EnsureInitializedAsync(CancellationToken cancellationToken)
    {
        if (_initialized)
            return;

        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS monitoring_samples (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                server_key TEXT NOT NULL,
                server_name TEXT NOT NULL,
                captured_at_utc TEXT NOT NULL,
                cpu_percent REAL NULL,
                ram_percent REAL NULL,
                disk_percent REAL NULL,
                download_bps REAL NOT NULL,
                upload_bps REAL NOT NULL,
                clients_online INTEGER NOT NULL,
                clients_total INTEGER NOT NULL,
                traffic_received_bytes INTEGER NOT NULL,
                traffic_sent_bytes INTEGER NOT NULL
            );

            CREATE INDEX IF NOT EXISTS ix_monitoring_samples_server_time
                ON monitoring_samples(server_key, captured_at_utc);
            """;

        await command.ExecuteNonQueryAsync(cancellationToken);
        _initialized = true;
    }

    private static void AddNullableDouble(
        SqliteCommand command,
        string name,
        double? value)
    {
        var parameter = command.Parameters.Add(name, SqliteType.Real);
        parameter.Value = value.HasValue ? value.Value : DBNull.Value;
    }
}
