using System.IO;

namespace VideoMergeTool.App.Features.DownloadVideo.Services;

/// <summary>
/// Keeps folder pickers, the clipboard, Explorer and message boxes out of the downloader view models.
/// </summary>
public interface IDownloadDialogService
{
    /// <returns>The chosen folder, or <c>null</c> if the user cancelled.</returns>
    string? SelectFolder(string? initialDirectory);

    /// <returns>The chosen file, or <c>null</c> if the user cancelled.</returns>
    string? SelectFile(string? initialFilePath, string filter, string title);

    string? GetClipboardText();

    /// <exception cref="DirectoryNotFoundException">The folder does not exist.</exception>
    void OpenFolder(string path);

    void ShowWarning(string message, string title);

    void ShowError(string message, string title);

    void ShowInfo(string message, string title);

    /// <returns><c>true</c> if the user chose Yes.</returns>
    bool Confirm(string message, string title);
}
