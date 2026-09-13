using Avalonia.Media;
using AmneziaDashboard.Core.Models;
using CommunityToolkit.Mvvm.ComponentModel;

namespace AmneziaDashboard.App.ViewModels;

public partial class ServerProfileItemViewModel : ViewModelBase
{
    private static readonly IBrush OnlineBrush =
        new SolidColorBrush(Color.Parse("#22C55E"));

    private static readonly IBrush OfflineBrush =
        new SolidColorBrush(Color.Parse("#94A3B8"));

    public ServerProfileItemViewModel(ServerProfile profile, bool isCurrent)
    {
        Profile = profile;
        _isCurrent = isCurrent;
    }

    public ServerProfile Profile { get; }

    public string Id => Profile.Id;

    public string Name => Profile.Name;

    public string Host => Profile.Host;

    public int Port => Profile.Port;

    public string Username => Profile.Username;

    public bool RememberPassword => Profile.RememberPassword;

    public bool AutoConnect => Profile.AutoConnect;

    public string AddressText => $"{Host}:{Port}";

    public string UserText => $"{Username}@{Host}";

    public string PasswordStatusText =>
        RememberPassword ? "Пароль сохранён безопасно" : "Пароль не сохранён";

    public string AutoConnectText =>
        AutoConnect ? "Автоподключение включено" : "Автоподключение выключено";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusText))]
    [NotifyPropertyChangedFor(nameof(StatusBrush))]
    [NotifyPropertyChangedFor(nameof(CanConnect))]
    private bool _isCurrent;

    public string StatusText =>
        IsCurrent ? "Подключён" : "Не подключён";

    public IBrush StatusBrush =>
        IsCurrent ? OnlineBrush : OfflineBrush;

    public bool CanConnect =>
        !IsCurrent;
}
