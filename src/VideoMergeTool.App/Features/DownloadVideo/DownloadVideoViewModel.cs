using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VideoMergeTool.App.Features.DownloadVideo.Services;
using VideoMergeTool.App.Shell;
using VideoMergeTool.Core.Features.DownloadVideo.Interfaces;
using VideoMergeTool.Core.Features.DownloadVideo.Models;
using VideoMergeTool.Core.Models;

namespace VideoMergeTool.App.Features.DownloadVideo;

/// <summary>
/// The YouTube downloader page: several independent tabs ("chủ đề"), each with its own channel,
/// options, video list and progress.
/// </summary>
public sealed class DownloadVideoViewModel : ObservableObject, IDisposable, ICloseGuard, INavigationTarget
{
    private const string TabTitlePrefix = "Chủ đề";

    private readonly IChannelAnalyzer _channelAnalyzer;
    private readonly IVideoDownloadService _downloadService;
    private readonly IDownloadDependencyService _dependencyService;
    private readonly IDownloadDialogService _dialogService;
    private readonly IArchiveService _archiveService;
    private readonly IDownloadLogger _logger;

    private DownloadTabViewModel _selectedTab;
    private int _nextTabNumber = 1;

    /// <summary>Started on the first visit to the page, so runs that never open it never launch yt-dlp.</summary>
    private Task? _dependencyCheck;

    private (bool Available, string Summary, string Status)? _dependencyState;
    private bool _disposed;

    public DownloadVideoViewModel(
        IChannelAnalyzer channelAnalyzer,
        IVideoDownloadService downloadService,
        IDownloadDependencyService dependencyService,
        IDownloadDialogService dialogService,
        IArchiveService archiveService,
        IDownloadLogger logger)
    {
        _channelAnalyzer = channelAnalyzer;
        _downloadService = downloadService;
        _dependencyService = dependencyService;
        _dialogService = dialogService;
        _archiveService = archiveService;
        _logger = logger;

        AddTabCommand = new RelayCommand(AddTab);
        CloseTabCommand = new RelayCommand<DownloadTabViewModel>(CloseTab, CanCloseTab);
        BrowseCookieFileCommand = new RelayCommand(BrowseCookieFile);

        CookieFileSettings.PropertyChanged += OnCookieFileSettingsChanged;

        _selectedTab = CreateTab();
        Tabs.Add(_selectedTab);
    }

    public ObservableCollection<DownloadTabViewModel> Tabs { get; } = [];

    /// <summary>The cookie file is chosen once here and shared by every tab; each tab reads it directly.</summary>
    public SharedCookieFileSettings CookieFileSettings { get; } = new();

    public DownloadTabViewModel SelectedTab
    {
        get => _selectedTab;
        set
        {
            // The tab strip briefly reports no selection while a tab is being removed.
            if (value is not null)
            {
                SetProperty(ref _selectedTab, value);
            }
        }
    }

    public bool IsBusy => Tabs.Any(static tab => tab.IsBusy);

    public IRelayCommand AddTabCommand { get; }

    public IRelayCommand<DownloadTabViewModel> CloseTabCommand { get; }

    public IRelayCommand BrowseCookieFileCommand { get; }

    public void LoadSettings(UserSettings settings)
    {
        IReadOnlyList<DownloadTabSettings> savedTabs = settings.DownloadTabs.Count > 0
            ? settings.DownloadTabs
            : [new DownloadTabSettings { Title = $"{TabTitlePrefix} 1" }];

        Tabs[0].ApplySettings(savedTabs[0]);
        for (var index = 1; index < savedTabs.Count; index++)
        {
            var tab = CreateTab();
            tab.ApplySettings(savedTabs[index]);
            Tabs.Add(tab);
        }

        SelectedTab = Tabs[Math.Clamp(settings.DownloadSelectedTabIndex, 0, Tabs.Count - 1)];

        // Applied after every tab so a saved cookie file wins over any stale per-tab browser-cookie flag.
        CookieFileSettings.CookieFilePath = settings.DownloadCookieFilePath?.Trim() ?? string.Empty;
        CookieFileSettings.UseCookieFile = settings.DownloadUseCookieFile;

        CloseTabCommand.NotifyCanExecuteChanged();
    }

    /// <summary>Tab options only: the analyzed video lists, progress and logs are never saved.</summary>
    public UserSettings ExportSettings(UserSettings settings) =>
        settings with
        {
            DownloadTabs = Tabs.Select(static tab => tab.CreateSettingsSnapshot()).ToList(),
            DownloadSelectedTabIndex = Math.Max(0, Tabs.IndexOf(SelectedTab)),
            DownloadUseCookieFile = CookieFileSettings.UseCookieFile,
            DownloadCookieFilePath = CookieFileSettings.CookieFilePath.Trim()
        };

    public void OnNavigatedTo() => _dependencyCheck ??= CheckDependenciesAsync();

    public Task<bool> ConfirmCloseAsync() => Task.FromResult(_dialogService.Confirm(
        "Trình tải YouTube đang phân tích hoặc tải video. Bạn có muốn hủy tiến trình và đóng ứng dụng không?",
        "Xác nhận đóng"));

    public async Task CancelAndWaitAsync(TimeSpan timeout)
    {
        var running = Tabs
            .Where(static tab => tab.IsBusy)
            .Select(static tab => tab.CancelActiveOperationAsync())
            .ToList();

        if (running.Count == 0)
        {
            return;
        }

        try
        {
            await Task.WhenAll(running).WaitAsync(timeout);
        }
        catch (TimeoutException)
        {
            // A stuck yt-dlp must not leave the window unclosable: close anyway.
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        CookieFileSettings.PropertyChanged -= OnCookieFileSettingsChanged;
        foreach (var tab in Tabs)
        {
            tab.PropertyChanged -= OnTabPropertyChanged;
            tab.Dispose();
        }
    }

    private async Task CheckDependenciesAsync()
    {
        ApplyDependencyState(false, "Chưa kiểm tra", "Đang kiểm tra dependency");

        try
        {
            var result = await _dependencyService.CheckAsync(CancellationToken.None);
            var summary = string.Join(
                Environment.NewLine,
                result.Dependencies.Select(static dependency =>
                    dependency.IsAvailable
                        ? $"{dependency.Name}: {dependency.Version ?? "OK"}"
                        : $"{dependency.Name}: THIẾU — {dependency.Path}"));

            foreach (var dependency in result.Dependencies)
            {
                if (dependency.IsAvailable)
                {
                    _logger.Info($"{dependency.Name} available: {dependency.Version ?? dependency.Path}");
                }
                else
                {
                    _logger.Error($"Missing dependency {dependency.Name}. Expected at: {dependency.Path}");
                }
            }

            if (result.IsSuccess)
            {
                ApplyDependencyState(true, summary, "Sẵn sàng");
                _logger.Info("Dependency check completed successfully.");
                return;
            }

            ApplyDependencyState(false, summary, "Có lỗi");
            var missingPaths = string.Join(
                Environment.NewLine,
                result.Missing.Select(static item => $"{item.Name}: {item.Path}"));
            _dialogService.ShowWarning(
                $"Thiếu dependency bắt buộc:{Environment.NewLine}{missingPaths}{Environment.NewLine}{Environment.NewLine}" +
                "Hãy đặt các file .exe vào thư mục Tools rồi khởi động lại ứng dụng.",
                "Thiếu dependency");
        }
        catch (Exception exception)
        {
            ApplyDependencyState(false, "Không kiểm tra được dependency", "Có lỗi");
            _logger.Error("Downloader initialization failed.", exception: exception);
            _dialogService.ShowError("Không thể khởi tạo trình tải YouTube. Xem log để biết chi tiết.", "Lỗi khởi tạo");
        }
    }

    private void ApplyDependencyState(bool available, string summary, string status)
    {
        _dependencyState = (available, summary, status);
        foreach (var tab in Tabs)
        {
            tab.ApplyDependencyState(available, summary, status);
        }
    }

    private void AddTab()
    {
        var tab = CreateTab();
        tab.CopySharedStateFrom(SelectedTab);
        Tabs.Add(tab);
        SelectedTab = tab;
        CloseTabCommand.NotifyCanExecuteChanged();
    }

    private void CloseTab(DownloadTabViewModel? tab)
    {
        if (tab is null || !CanCloseTab(tab))
        {
            return;
        }

        var removedIndex = Tabs.IndexOf(tab);
        var wasSelected = ReferenceEquals(SelectedTab, tab);
        tab.PropertyChanged -= OnTabPropertyChanged;
        Tabs.Remove(tab);
        tab.Dispose();

        if (wasSelected)
        {
            SelectedTab = Tabs[Math.Clamp(removedIndex, 0, Tabs.Count - 1)];
        }

        OnPropertyChanged(nameof(IsBusy));
        CloseTabCommand.NotifyCanExecuteChanged();
    }

    /// <summary>The last tab and any busy tab stay open.</summary>
    private bool CanCloseTab(DownloadTabViewModel? tab) => tab is not null && Tabs.Count > 1 && !tab.IsBusy;

    private DownloadTabViewModel CreateTab()
    {
        var tab = new DownloadTabViewModel(
            _channelAnalyzer,
            _downloadService,
            _dialogService,
            _archiveService,
            _logger,
            CookieFileSettings,
            $"{TabTitlePrefix} {_nextTabNumber++}");

        if (_dependencyState is { } state)
        {
            tab.ApplyDependencyState(state.Available, state.Summary, state.Status);
        }

        tab.PropertyChanged += OnTabPropertyChanged;
        return tab;
    }

    private void OnTabPropertyChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName == nameof(DownloadTabViewModel.IsBusy))
        {
            OnPropertyChanged(nameof(IsBusy));
            CloseTabCommand.NotifyCanExecuteChanged();
        }
    }

    private void BrowseCookieFile()
    {
        try
        {
            var selected = _dialogService.SelectFile(
                CookieFileSettings.CookieFilePath,
                "Cookie files (*.txt)|*.txt|All files (*.*)|*.*",
                "Chọn file cookie (Netscape/cookies.txt)");
            if (!string.IsNullOrWhiteSpace(selected))
            {
                CookieFileSettings.CookieFilePath = selected;
                CookieFileSettings.UseCookieFile = true;
            }
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            _logger.Error("Cookie file picker failed.", exception: exception);
            _dialogService.ShowError("Không thể chọn file cookie. Hãy thử lại hoặc nhập đường dẫn trực tiếp.", "Lỗi file cookie");
        }
    }

    /// <summary>yt-dlp rejects a command line combining --cookies and --cookies-from-browser, so the
    /// shared cookie file turning on must turn every tab's own browser-cookie option off.</summary>
    private void OnCookieFileSettingsChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName != nameof(SharedCookieFileSettings.UseCookieFile) || !CookieFileSettings.UseCookieFile)
        {
            return;
        }

        foreach (var tab in Tabs)
        {
            tab.UseBrowserCookies = false;
        }
    }
}
