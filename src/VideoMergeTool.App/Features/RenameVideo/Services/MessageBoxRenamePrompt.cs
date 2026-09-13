using System.Globalization;
using System.Windows;

namespace VideoMergeTool.App.Features.RenameVideo.Services;

public sealed class MessageBoxRenamePrompt : IRenamePrompt
{
    private const string Caption = "Đổi tên video";

    public bool ConfirmUndo(int fileCount, DateTime renamedAt, string folder)
    {
        var message =
            $"Khôi phục tên gốc cho {fileCount} file đã đổi tên lúc " +
            $"{renamedAt.ToString("HH:mm:ss dd/MM/yyyy", CultureInfo.InvariantCulture)} trong thư mục:{Environment.NewLine}" +
            $"{folder}{Environment.NewLine}{Environment.NewLine}" +
            "File nào mà tên gốc đã bị file khác dùng sẽ được giữ nguyên. Tiếp tục?";

        return Show(message, MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No) == MessageBoxResult.Yes;
    }

    public void ShowError(string message) =>
        Show(message, MessageBoxButton.OK, MessageBoxImage.Error, MessageBoxResult.OK);

    private static MessageBoxResult Show(
        string message,
        MessageBoxButton buttons,
        MessageBoxImage image,
        MessageBoxResult defaultResult)
    {
        var owner = Application.Current?.MainWindow;
        return owner is null
            ? MessageBox.Show(message, Caption, buttons, image, defaultResult)
            : MessageBox.Show(owner, message, Caption, buttons, image, defaultResult);
    }
}
