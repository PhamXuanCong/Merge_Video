using System.Windows;

namespace VideoMergeTool.App.Services;

public sealed class MessageBoxUserPrompt : IUserPrompt
{
    public bool ConfirmSourceDeletion(int videoCount, string inputFolder)
    {
        var message =
            $"\"After a file succeeds\" is set to Delete it.{Environment.NewLine}{Environment.NewLine}" +
            $"Up to {videoCount} original video(s) in{Environment.NewLine}{inputFolder}{Environment.NewLine}" +
            $"will be deleted — each one only after its merged output has been written and verified.{Environment.NewLine}{Environment.NewLine}" +
            "Deleted files do not go to the Recycle Bin. Continue?";

        return Ask(message, "Delete original videos?");
    }

    public bool ConfirmExitWhileBusy(int unfinishedCount)
    {
        var message =
            $"Video Merge Tool is still working on {unfinishedCount} video(s).{Environment.NewLine}{Environment.NewLine}" +
            $"Closing now stops the current run. The video being merged right now is left untouched and its " +
            $"temporary .processing.mp4 file is removed; videos already finished keep their output.{Environment.NewLine}{Environment.NewLine}" +
            "Close anyway?";

        return Ask(message, "Stop processing and close?");
    }

    private static bool Ask(string message, string caption)
    {
        var owner = Application.Current?.MainWindow;
        var result = owner is null
            ? MessageBox.Show(message, caption, MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No)
            : MessageBox.Show(owner, message, caption, MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No);

        return result == MessageBoxResult.Yes;
    }
}
