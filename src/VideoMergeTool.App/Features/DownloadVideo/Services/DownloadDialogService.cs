using System.Diagnostics;
using System.IO;
using System.Windows;
using Microsoft.Win32;

namespace VideoMergeTool.App.Features.DownloadVideo.Services;

public sealed class DownloadDialogService : IDownloadDialogService
{
    public string? SelectFolder(string? initialDirectory)
    {
        var dialog = new OpenFolderDialog
        {
            Title = "Chọn thư mục lưu video",
            Multiselect = false
        };

        if (!string.IsNullOrWhiteSpace(initialDirectory) && Directory.Exists(initialDirectory))
        {
            dialog.InitialDirectory = initialDirectory;
        }

        return dialog.ShowDialog() == true ? dialog.FolderName : null;
    }

    public string? GetClipboardText() =>
        Clipboard.ContainsText(TextDataFormat.UnicodeText)
            ? Clipboard.GetText(TextDataFormat.UnicodeText)
            : null;

    public void OpenFolder(string path)
    {
        if (!Directory.Exists(path))
        {
            throw new DirectoryNotFoundException($"Không tìm thấy thư mục: {path}");
        }

        Process.Start(new ProcessStartInfo(path) { UseShellExecute = true })?.Dispose();
    }

    public void ShowWarning(string message, string title) =>
        Show(message, title, MessageBoxButton.OK, MessageBoxImage.Warning, MessageBoxResult.OK);

    public void ShowError(string message, string title) =>
        Show(message, title, MessageBoxButton.OK, MessageBoxImage.Error, MessageBoxResult.OK);

    public void ShowInfo(string message, string title) =>
        Show(message, title, MessageBoxButton.OK, MessageBoxImage.Information, MessageBoxResult.OK);

    public bool Confirm(string message, string title) =>
        Show(message, title, MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No) == MessageBoxResult.Yes;

    private static MessageBoxResult Show(
        string message,
        string title,
        MessageBoxButton buttons,
        MessageBoxImage image,
        MessageBoxResult defaultResult)
    {
        var owner = Application.Current?.MainWindow;
        return owner is null
            ? MessageBox.Show(message, title, buttons, image, defaultResult)
            : MessageBox.Show(owner, message, title, buttons, image, defaultResult);
    }
}
