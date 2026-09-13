using System.Text.Json;
using AmneziaDashboard.Core.Interfaces;
using AmneziaDashboard.Core.Models;

namespace AmneziaDashboard.Infrastructure.Storage;

public class JsonServerProfileStore : IServerProfileStore
{
    private readonly string _filePath;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public JsonServerProfileStore()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var directory = Path.Combine(appData, "AmneziaMonitor");
        _filePath = Path.Combine(directory, "servers.json");
    }

    public async Task<IReadOnlyList<ServerProfile>> LoadAsync(
        CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            return await ReadProfilesAsync(cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task SaveAsync(
        ServerProfile profile,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(profile);

        await _gate.WaitAsync(cancellationToken);
        try
        {
            var profiles = (await ReadProfilesAsync(cancellationToken)).ToList();

            var existing = profiles.FirstOrDefault(x =>
                x.Id == profile.Id ||
                (x.Host.Equals(profile.Host, StringComparison.OrdinalIgnoreCase) &&
                 x.Port == profile.Port &&
                 x.Username.Equals(profile.Username, StringComparison.OrdinalIgnoreCase)));

            if (existing is null)
            {
                profiles.Add(Clone(profile));
            }
            else
            {
                existing.Name = profile.Name;
                existing.Host = profile.Host;
                existing.Port = profile.Port;
                existing.Username = profile.Username;
                existing.RememberPassword = profile.RememberPassword;
                existing.AutoConnect = profile.AutoConnect;
                existing.LastUsedAt = profile.LastUsedAt;
            }

            await WriteProfilesAsync(profiles, cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task DeleteAsync(
        string profileId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(profileId))
            return;

        await _gate.WaitAsync(cancellationToken);
        try
        {
            var profiles = (await ReadProfilesAsync(cancellationToken)).ToList();
            profiles.RemoveAll(x => x.Id == profileId);
            await WriteProfilesAsync(profiles, cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<List<ServerProfile>> ReadProfilesAsync(
        CancellationToken cancellationToken)
    {
        if (!File.Exists(_filePath))
            return [];

        try
        {
            await using var stream = File.OpenRead(_filePath);
            var profiles = await JsonSerializer.DeserializeAsync<List<ServerProfile>>(
                stream,
                cancellationToken: cancellationToken);

            return profiles ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
        catch (IOException)
        {
            return [];
        }
    }

    private async Task WriteProfilesAsync(
        List<ServerProfile> profiles,
        CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(_filePath)!;
        Directory.CreateDirectory(directory);

        var ordered = profiles
            .OrderByDescending(x => x.LastUsedAt)
            .ThenBy(x => x.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

        var tempPath = _filePath + ".tmp";
        await using (var stream = File.Create(tempPath))
        {
            await JsonSerializer.SerializeAsync(
                stream,
                ordered,
                new JsonSerializerOptions { WriteIndented = true },
                cancellationToken);
        }

        File.Move(tempPath, _filePath, true);
    }

    private static ServerProfile Clone(ServerProfile profile) => new()
    {
        Id = profile.Id,
        Name = profile.Name,
        Host = profile.Host,
        Port = profile.Port,
        Username = profile.Username,
        RememberPassword = profile.RememberPassword,
        AutoConnect = profile.AutoConnect,
        LastUsedAt = profile.LastUsedAt
    };
}
