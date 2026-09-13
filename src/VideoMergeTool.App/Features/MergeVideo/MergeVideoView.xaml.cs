using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;

namespace VideoMergeTool.App.Features.MergeVideo;

public partial class MergeVideoView : UserControl
{
    /// <summary>Combined width of the fixed Status and Progress columns.</summary>
    private const double FixedColumnWidth = 245;

    /// <summary>Room left for the vertical scroll bar and the card's own padding.</summary>
    private const double ScrollBarAllowance = 26;

    private const double MinimumFileColumnWidth = 180;
    private const double MinimumDetailsColumnWidth = 150;

    public MergeVideoView()
    {
        InitializeComponent();
    }

    private MergeVideoViewModel? ViewModel => DataContext as MergeVideoViewModel;

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
                Window.GetWindow(this),
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
}
