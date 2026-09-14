using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VideoMergeTool.App.Features.DownloadVideo;
using VideoMergeTool.App.Features.FolderVideoStats;
using VideoMergeTool.App.Features.MergeVideo;
using VideoMergeTool.App.Features.RenameVideo;
using VideoMergeTool.App.Theming;
using VideoMergeTool.Core.Enums;
using VideoMergeTool.Core.Interfaces;
using VideoMergeTool.Core.Models;

namespace VideoMergeTool.App.Shell;

/// <summary>
/// Owns everything that spans features: the sidebar, the saved theme, and the single
/// <see cref="UserSettings"/> file. Settings has exactly one writer (this class) so a feature
/// toggling the theme and a feature saving its own options can never race on the same JSON file.
/// </summary>
public sealed partial class ShellViewModel : ObservableObject
{
    private readonly IUserSettingsService _userSettingsService;
    private readonly ThemeManager _themeManager;
    private readonly MergeVideoViewModel _mergeVideoViewModel;
    private readonly RenameVideoViewModel _renameVideoViewModel;
    private readonly DownloadVideoViewModel _downloadVideoViewModel;
    private readonly FolderVideoStatsViewModel _folderVideoStatsViewModel;

    public ShellViewModel(
        IUserSettingsService userSettingsService,
        ThemeManager themeManager,
        MergeVideoViewModel mergeVideoViewModel,
        RenameVideoViewModel renameVideoViewModel,
        DownloadVideoViewModel downloadVideoViewModel,
        FolderVideoStatsViewModel folderVideoStatsViewModel)
    {
        _userSettingsService = userSettingsService;
        _themeManager = themeManager;
        _mergeVideoViewModel = mergeVideoViewModel;
        _renameVideoViewModel = renameVideoViewModel;
        _downloadVideoViewModel = downloadVideoViewModel;
        _folderVideoStatsViewModel = folderVideoStatsViewModel;

        NavigationItems =
        [
            new NavigationItem("Merge Video", mergeVideoViewModel),
            new NavigationItem("Đổi tên video", renameVideoViewModel),
            new NavigationItem("Tải video YouTube", downloadVideoViewModel),
            new NavigationItem("Số lượng video theo folder", folderVideoStatsViewModel)
        ];

        var settings = _userSettingsService.Load();

        // Assigned to the backing field above, so the generated change callback never ran.
        _themePreference = settings.ThemePreference;
        _themeManager.Apply(_themePreference);

        mergeVideoViewModel.LoadSettings(settings);
        renameVideoViewModel.LoadSettings(settings);
        downloadVideoViewModel.LoadSettings(settings);
        folderVideoStatsViewModel.LoadSettings(settings);

        _selectedNavigationItem = NavigationItems[0];
        _currentPage = _selectedNavigationItem.ViewModel;
    }

    public IReadOnlyList<NavigationItem> NavigationItems { get; }

    [ObservableProperty]
    private NavigationItem? _selectedNavigationItem;

    [ObservableProperty]
    private object? _currentPage;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ThemeButtonText))]
    private ThemePreference _themePreference;

    public string ThemeButtonText => ThemePreference switch
    {
        ThemePreference.Light => "Theme: Light",
        ThemePreference.Dark => "Theme: Dark",
        _ => "Theme: Auto"
    };

    /// <summary>Feature view models that might still be busy when the shell window closes.</summary>
    public IEnumerable<ICloseGuard> CloseGuards => NavigationItems.Select(item => item.ViewModel).OfType<ICloseGuard>();

    partial void OnSelectedNavigationItemChanged(NavigationItem? value)
    {
        CurrentPage = value?.ViewModel;
        (value?.ViewModel as INavigationTarget)?.OnNavigatedTo();
    }

    partial void OnThemePreferenceChanged(ThemePreference value) => _themeManager.Apply(value);

    [RelayCommand]
    private void CycleTheme() => ThemePreference = ThemePreference switch
    {
        ThemePreference.System => ThemePreference.Light,
        ThemePreference.Light => ThemePreference.Dark,
        _ => ThemePreference.System
    };

    /// <summary>The only place <see cref="UserSettings"/> is written, tying save to shell lifetime.</summary>
    public void SaveSettings()
    {
        var settings = _mergeVideoViewModel.ExportSettings() with { ThemePreference = ThemePreference };
        settings = _renameVideoViewModel.ExportSettings(settings);
        settings = _downloadVideoViewModel.ExportSettings(settings);
        settings = _folderVideoStatsViewModel.ExportSettings(settings);
        _userSettingsService.Save(settings);
    }
}
