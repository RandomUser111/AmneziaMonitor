using System.IO;
using System.Linq;
using System.Text;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Input.Platform;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using QRCoder;

namespace AmneziaDashboard.App.Views;

public partial class ClientConfigWindow : Window
{
    private readonly string _clientName;
    private readonly string _configText;
    private bool _profileHandled;

    public ClientConfigWindow()
        : this("Клиент", string.Empty, string.Empty)
    {
    }

    public ClientConfigWindow(string clientName, string clientAddress, string configText, bool isRestored = false)
    {
        InitializeComponent();

        _clientName = string.IsNullOrWhiteSpace(clientName) ? "client" : clientName.Trim();
        _configText = configText;

        TitleText.Text = isRestored
            ? $"Конфигурация «{_clientName}» восстановлена"
            : $"Клиент «{_clientName}» создан";
        AddressText.Text = string.IsNullOrWhiteSpace(clientAddress)
            ? string.Empty
            : $"VPN IP: {clientAddress}";
        ConfigBox.Text = configText;

        RenderQrCode(configText);

        Closing += (_, e) =>
        {
            if (_profileHandled || string.IsNullOrWhiteSpace(_configText))
                return;

            e.Cancel = true;
            StatusText.Text = "Сначала скопируйте или сохраните профиль — после закрытия приватный ключ будет потерян.";
        };
    }

    private void RenderQrCode(string configText)
    {
        if (string.IsNullOrWhiteSpace(configText))
        {
            QrPanel.IsVisible = false;
            return;
        }

        try
        {
            using var qrGenerator = new QRCodeGenerator();
            using var qrData = qrGenerator.CreateQrCode(configText, QRCodeGenerator.ECCLevel.Q);
            var qrCode = new PngByteQRCode(qrData);
            var pngBytes = qrCode.GetGraphic(20);
            using var stream = new MemoryStream(pngBytes);
            QrImage.Source = new Bitmap(stream);
        }
        catch
        {
            QrPanel.IsVisible = false;
            StatusText.Text = "Не удалось сформировать QR-код. Профиль можно сохранить как .conf.";
        }
    }

    private async void CopyButton_Click(object? sender, RoutedEventArgs e)
    {
        var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
        if (clipboard is null)
        {
            StatusText.Text = "Буфер обмена недоступен.";
            return;
        }

        await clipboard.SetTextAsync(_configText);
        _profileHandled = true;
        StatusText.Text = "Скопировано. Теперь окно можно закрыть.";
    }

    private async void SaveButton_Click(object? sender, RoutedEventArgs e)
    {
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel?.StorageProvider is null)
        {
            StatusText.Text = "Диалог сохранения недоступен.";
            return;
        }

        var safeName = string.Concat(_clientName.Select(ch =>
            Path.GetInvalidFileNameChars().Contains(ch) ? '_' : ch));

        var file = await topLevel.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Сохранить VPN-конфигурацию",
            SuggestedFileName = $"{safeName}.conf",
            DefaultExtension = "conf",
            FileTypeChoices =
            [
                new FilePickerFileType("VPN configuration")
                {
                    Patterns = ["*.conf"]
                }
            ]
        });

        if (file is null)
            return;

        await using var stream = await file.OpenWriteAsync();
        stream.SetLength(0);
        await using var writer = new StreamWriter(stream, new UTF8Encoding(false));
        await writer.WriteAsync(_configText);
        await writer.FlushAsync();

        _profileHandled = true;
        StatusText.Text = "Файл сохранён. Теперь окно можно закрыть.";
    }

    private void CloseButton_Click(object? sender, RoutedEventArgs e)
    {
        if (!_profileHandled && !string.IsNullOrWhiteSpace(_configText))
        {
            StatusText.Text = "Сначала скопируйте или сохраните профиль.";
            return;
        }

        Close();
    }
}
