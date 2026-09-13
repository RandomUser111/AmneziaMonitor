using AmneziaDashboard.Core.Models;

namespace AmneziaDashboard.Core.Interfaces;

public interface IMonitoringHistoryStore
{
    Task AppendAsync(
        MonitoringHistoryRecord record,
        CancellationToken cancellationToken = default);

    Task AppendClientTrafficAsync(
        IReadOnlyList<ClientTrafficHistoryRecord> records,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<MonitoringHistoryRecord>> GetRangeAsync(
        string serverKey,
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ClientTrafficSummary>> GetClientTrafficSummaryAsync(
        string serverKey,
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken = default);

    Task CleanupOlderThanAsync(
        DateTimeOffset cutoff,
        CancellationToken cancellationToken = default);
}
