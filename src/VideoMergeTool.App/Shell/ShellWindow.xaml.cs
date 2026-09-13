using System.IO;
using System.Windows;
using VideoMergeTool.App.Theming;

namespace VideoMergeTool.App.Shell;

public partial class ShellWindow : Window
{
    /// <summary>How long to wait for a busy feature to unwind before closing regardless.</summary>
    private static readonly TimeSpan ShutdownTimeout = TimeSpan.FromSeconds(10);

    private readonly ThemeManager _themeManager;

    /// <summary>Set once every busy feature has been stopped, so the retried close goes through.</summary>
    private bool _isClosingConfirmed;

    public ShellWindow(ThemeManager themeManager, ShellViewModel viewModel)
    {
        _themeManager = themeManager;
        DataContext = viewModel;
        InitializeComponent();

        SourceInitialized += (_, _) => ApplyTitleBarTheme();
        _themeManager.ThemeChanged += OnThemeChanged;
        Closing += ShellWindow_Closing;
    }

    private ShellViewModel ViewModel => (ShellViewModel)DataContext;

    private void OnThemeChanged(object? sender, EventArgs e) => ApplyTitleBarTheme();

    private void ApplyTitleBarTheme() => WindowChrome.ApplyTitleBarTheme(this, _themeManager.IsDark);

    private void Window_PreviewDragOver(object sender, DragEventArgs e)
    {
        var isFolderDrop = ViewModel.CurrentPage is IFolderDropTarget && TryGetDroppedFolder(e) is not null;

        e.Effects = isFolderDrop ? DragDropEffects.Copy : DragDropEffects.None;
        DropOverlay.Visibility = isFolderDrop ? Visibility.Visible : Visibility.Collapsed;
        e.Handled = true;
    }

    private void Window_PreviewDragLeave(object sender, DragEventArgs e)
    {
        DropOverlay.Visibility = Visibility.Collapsed;
    }

    private void Window_PreviewDrop(object sender, DragEventArgs e)
    {
        DropOverlay.Visibility = Visibility.Collapsed;
        e.Handled = true;

        if (ViewModel.CurrentPage is not IFolderDropTarget target || TryGetDroppedFolder(e) is not { } folder)
        {
            return;
        }

        target.OnFolderDropped(folder);
    }

    /// <summary>
    /// Resolves a drop to the folder it means: a dropped folder is used as is, a dropped file
    /// contributes the folder that contains it.
    /// </summary>
    private static string? TryGetDroppedFolder(DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(DataFormats.FileDrop) ||
            e.Data.GetData(DataFormats.FileDrop) is not string[] { Length: > 0 } paths)
        {
            return null;
        }

        var path = paths[0];

        if (Directory.Exists(path))
        {
            return path;
        }

        return File.Exists(path) ? Path.GetDirectoryName(path) : null;
    }

    /// <summary>
    /// Closing mid-run used to just drop the window: FFmpeg was left running as an orphan and its
    /// half-written .processing.mp4 stayed behind. Now every busy feature is stopped and awaited
    /// first, via whichever navigation items implement <see cref="ICloseGuard"/>.
    /// </summary>
    private async void ShellWindow_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        if (!_isClosingConfirmed)
        {
            var busyGuards = ViewModel.CloseGuards.Where(guard => guard.IsBusy).ToList();
            if (busyGuards.Count > 0)
            {
                // Must be set before the first await, while the event is still being handled.
                e.Cancel = true;

                foreach (var guard in busyGuards)
                {
                    if (!await guard.ConfirmCloseAsync())
                    {
                        return;
                    }
                }

                IsEnabled = false;
                try
                {
                    await Task.WhenAll(busyGuards.Select(guard => guard.CancelAndWaitAsync(ShutdownTimeout)));
                }
                finally
                {
                    IsEnabled = true;
                }

                _isClosingConfirmed = true;
                Close();
                return;
            }
        }

        _themeManager.ThemeChanged -= OnThemeChanged;
        ViewModel.SaveSettings();
    }
}
