using System.IO;
using System.Windows.Controls;
using System.Windows.Input;
using Microsoft.Win32;

namespace VideoMergeTool.App.Features.FolderVideoStats;

public partial class FolderVideoStatsView : UserControl
{
    public FolderVideoStatsView()
    {
        InitializeComponent();
    }

    private FolderVideoStatsViewModel? ViewModel => DataContext as FolderVideoStatsViewModel;

    private void BrowseFolder_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        if (ViewModel is not { } viewModel)
        {
            return;
        }

        var dialog = new OpenFolderDialog
        {
            Title = "Chọn thư mục để đếm video"
        };

        if (Directory.Exists(viewModel.NewFolderPath))
        {
            dialog.InitialDirectory = viewModel.NewFolderPath;
        }

        if (dialog.ShowDialog() == true)
        {
            viewModel.AddFolder(dialog.FolderName);
        }
    }

    /// <summary>Lets Enter add the typed path without needing to tab to the button.</summary>
    private void NewFolderPath_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || ViewModel is not { } viewModel)
        {
            return;
        }

        if (viewModel.AddTypedFolderCommand.CanExecute(null))
        {
            viewModel.AddTypedFolderCommand.Execute(null);
        }
    }
}
