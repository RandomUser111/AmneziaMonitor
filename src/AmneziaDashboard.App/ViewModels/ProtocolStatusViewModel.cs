using System;
using Avalonia.Media;
using AmneziaDashboard.Core.Models;
using AmneziaDashboard.App.Services;

namespace AmneziaDashboard.App.ViewModels;

public sealed class ProtocolStatusViewModel : ViewModelBase
{
    private static readonly IBrush OnlineBrush = new SolidColorBrush(Color.Parse("#22C55E"));
    private static readonly IBrush OfflineBrush = new SolidColorBrush(Color.Parse("#94A3B8"));

    public ProtocolStatusViewModel(AmneziaContainerInfo container)
    {
        ContainerName = container.ContainerName;
        DisplayName = container.DisplayName;
        ProtocolKey = container.ProtocolKey;
        Image = container.Image;
        State = container.State;
        DockerStatus = container.Status;
        Ports = container.Ports;
        IsRunning = container.IsRunning;
        ClientCount = container.ClientCount;
    }

    public string ContainerName { get; }
    public string DisplayName { get; }
    public string ProtocolKey { get; }
    public string Image { get; }
    public string State { get; }
    public string DockerStatus { get; }
    public string Ports { get; }
    public bool IsRunning { get; }
    public int? ClientCount { get; }
    public bool CanStart => !IsRunning;
    public bool CanStop => IsRunning;
    public bool CanRestart => IsRunning;
    public IBrush StatusBrush => IsRunning ? OnlineBrush : OfflineBrush;
    public string StatusText => IsRunning ? LocalizationService.T("Running", "Работает") : LocalizationService.T("Stopped", "Остановлен");
    public string UsersText => ClientCount.HasValue ? FormatUsers(ClientCount.Value) : LocalizationService.T("— users", "— пользователей");
    public string PortsText => string.IsNullOrWhiteSpace(Ports) ? "—" : Ports;
    public string ImageText => string.IsNullOrWhiteSpace(Image) ? "—" : Image;
    public string DockerStatusText => string.IsNullOrWhiteSpace(DockerStatus) ? State : DockerStatus;

    public void NotifyLocalizationChanged()
    {
        OnPropertyChanged(nameof(StatusText));
        OnPropertyChanged(nameof(UsersText));
    }

    private static string FormatUsers(int count)
    {
        if (!LocalizationService.IsRussian)
            return $"{count} {(count == 1 ? "user" : "users")}";

        var abs = Math.Abs(count) % 100;
        var last = abs % 10;
        var word = abs is >= 11 and <= 14
            ? "пользователей"
            : last switch
            {
                1 => "пользователь",
                2 or 3 or 4 => "пользователя",
                _ => "пользователей"
            };
        return $"{count} {word}";
    }
}
