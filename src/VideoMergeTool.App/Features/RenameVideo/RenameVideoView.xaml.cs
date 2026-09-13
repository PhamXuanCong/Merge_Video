using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;

namespace VideoMergeTool.App.Features.RenameVideo;

public partial class RenameVideoView : UserControl
{
    private const double StatusColumnWidth = 95;

    /// <summary>Room left for the vertical scroll bar and the card's own padding.</summary>
    private const double ScrollBarAllowance = 26;

    private const double MinimumNameColumnWidth = 160;
    private const double MinimumNoteColumnWidth = 120;

    public RenameVideoView()
    {
        InitializeComponent();
    }

    private RenameVideoViewModel? ViewModel => DataContext as RenameVideoViewModel;

    /// <summary>Picking a folder is a request to see its preview, so scan right away.</summary>
    private void SelectFolder_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel is not { } viewModel)
        {
            return;
        }

        var dialog = new OpenFolderDialog
        {
            Title = "Chọn thư mục chứa video cần đổi tên"
        };

        if (Directory.Exists(viewModel.Folder))
        {
            dialog.InitialDirectory = viewModel.Folder;
        }

        if (dialog.ShowDialog() != true)
        {
            return;
        }

        viewModel.Folder = dialog.FolderName;

        if (viewModel.ScanCommand.CanExecute(null))
        {
            viewModel.ScanCommand.Execute(null);
        }
    }

    private void OpenFolder_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel is not { } viewModel || string.IsNullOrWhiteSpace(viewModel.Folder))
        {
            return;
        }

        var folder = viewModel.Folder.Trim();
        if (!Directory.Exists(folder))
        {
            MessageBox.Show(
                Window.GetWindow(this),
                $"Thư mục không tồn tại:{Environment.NewLine}{folder}",
                "Đổi tên video",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        Process.Start(new ProcessStartInfo(folder) { UseShellExecute = true })?.Dispose();
    }

    /// <summary>GridView has no star sizing, so the text columns share whatever the fixed Status column leaves.</summary>
    private void ItemList_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (!e.WidthChanged)
        {
            return;
        }

        var available = ItemList.ActualWidth - StatusColumnWidth - ScrollBarAllowance;
        if (double.IsNaN(available) || available <= 0)
        {
            return;
        }

        OriginalNameColumn.Width = Math.Max(MinimumNameColumnWidth, available * 0.37);
        NewNameColumn.Width = Math.Max(MinimumNameColumnWidth, available * 0.37);
        NoteColumn.Width = Math.Max(MinimumNoteColumnWidth, available * 0.26);
    }
}
