using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using VideoMergeTool.App.Services;
using VideoMergeTool.App.Theming;
using VideoMergeTool.App.ViewModels;

namespace VideoMergeTool.App;

public partial class MainWindow : Window
{
    /// <summary>Combined width of the fixed Status and Progress columns.</summary>
    private const double FixedColumnWidth = 245;

    /// <summary>Room left for the vertical scroll bar and the card's own padding.</summary>
    private const double ScrollBarAllowance = 26;

    private const double MinimumFileColumnWidth = 180;
    private const double MinimumDetailsColumnWidth = 150;

    /// <summary>How long to wait for FFmpeg to die before closing regardless.</summary>
    private static readonly TimeSpan ShutdownTimeout = TimeSpan.FromSeconds(10);

    private readonly ThemeManager _themeManager;
    private readonly IUserPrompt _userPrompt;

    /// <summary>Set once the running batch has been stopped, so the retried close goes through.</summary>
    private bool _isClosingConfirmed;

    public MainWindow(ThemeManager themeManager, IUserPrompt userPrompt)
    {
        _themeManager = themeManager;
        _userPrompt = userPrompt;
        InitializeComponent();

        SourceInitialized += (_, _) => ApplyTitleBarTheme();
        _themeManager.ThemeChanged += OnThemeChanged;
        Closing += MainWindow_Closing;
    }

    private MainViewModel? ViewModel => DataContext as MainViewModel;

    private void OnThemeChanged(object? sender, EventArgs e) => ApplyTitleBarTheme();

    private void ApplyTitleBarTheme() => WindowChrome.ApplyTitleBarTheme(this, _themeManager.IsDark);

    private void SelectFolder_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel is not { } viewModel)
        {
            return;
        }

        var dialog = new OpenFolderDialog
        {
            Title = "Select the input folder containing MP4 videos"
        };

        if (Directory.Exists(viewModel.InputFolder))
        {
            dialog.InitialDirectory = viewModel.InputFolder;
        }

        if (dialog.ShowDialog() == true)
        {
            viewModel.InputFolder = dialog.FolderName;
        }
    }

    /// <summary>
    /// Picking a recent folder is a request to switch to it right away, so it mirrors the
    /// drag-and-drop behaviour: set the folder and scan it immediately.
    /// </summary>
    private void RecentFoldersBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ViewModel is not { } viewModel || sender is not ComboBox { SelectedItem: string folder })
        {
            return;
        }

        viewModel.InputFolder = folder;

        if (viewModel.ScanCommand.CanExecute(null))
        {
            viewModel.ScanCommand.Execute(null);
        }
    }

    private void OpenOutputFolder_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel is not { } viewModel || string.IsNullOrWhiteSpace(viewModel.InputFolder))
        {
            return;
        }

        var outputFolder = Path.Combine(viewModel.InputFolder, "output");
        var target = Directory.Exists(outputFolder) ? outputFolder : viewModel.InputFolder;

        if (!Directory.Exists(target))
        {
            MessageBox.Show(
                this,
                $"That folder does not exist yet:{Environment.NewLine}{target}",
                "Video Merge Tool",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        Process.Start(new ProcessStartInfo(target) { UseShellExecute = true })?.Dispose();
    }

    /// <summary>
    /// GridView has no star sizing, so the two text columns are given the width left over once
    /// the fixed Status and Progress columns are accounted for.
    /// </summary>
    private void TaskList_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (!e.WidthChanged)
        {
            return;
        }

        var available = TaskList.ActualWidth - FixedColumnWidth - ScrollBarAllowance;
        if (double.IsNaN(available) || available <= 0)
        {
            return;
        }

        FileColumn.Width = Math.Max(MinimumFileColumnWidth, available * 0.55);
        DetailsColumn.Width = Math.Max(MinimumDetailsColumnWidth, available * 0.45);
    }

    private void Window_PreviewDragOver(object sender, DragEventArgs e)
    {
        var isFolderDrop = TryGetDroppedFolder(e) is not null;

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

        if (ViewModel is not { } viewModel || TryGetDroppedFolder(e) is not { } folder)
        {
            return;
        }

        viewModel.InputFolder = folder;

        // Dropping a folder is a request to look inside it, so scan straight away.
        if (viewModel.ScanCommand.CanExecute(null))
        {
            viewModel.ScanCommand.Execute(null);
        }
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
    /// half-written .processing.mp4 stayed behind. Now the run is stopped and awaited first.
    /// </summary>
    private async void MainWindow_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        if (!_isClosingConfirmed && ViewModel is { IsBusy: true } busyViewModel)
        {
            // Must be set before the first await, while the event is still being handled.
            e.Cancel = true;

            if (!_userPrompt.ConfirmExitWhileBusy(busyViewModel.UnfinishedCount))
            {
                return;
            }

            IsEnabled = false;
            try
            {
                await busyViewModel.CancelAndWaitAsync(ShutdownTimeout);
            }
            finally
            {
                IsEnabled = true;
            }

            _isClosingConfirmed = true;
            Close();
            return;
        }

        _themeManager.ThemeChanged -= OnThemeChanged;
        ViewModel?.SaveSettings();
    }
}
