using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VideoMergeTool.App.Features.DownloadVideo.Services;
using VideoMergeTool.App.ViewModels;
using VideoMergeTool.Core.Features.DownloadVideo;
using VideoMergeTool.Core.Features.DownloadVideo.Enums;
using VideoMergeTool.Core.Features.DownloadVideo.Interfaces;
using VideoMergeTool.Core.Features.DownloadVideo.Models;

namespace VideoMergeTool.App.Features.DownloadVideo;

/// <summary>
/// One downloader tab ("chủ đề"): a channel URL, its options and filters, the analyzed video list,
/// progress and log.
/// </summary>
public sealed class DownloadTabViewModel : ObservableObject, IDisposable
{
    private const int MaxLogLines = 2000;
    private const string DefaultTitle = "Chủ đề";

    private readonly IChannelAnalyzer _channelAnalyzer;
    private readonly IVideoDownloadService _downloadService;
    private readonly IDownloadDialogService _dialogService;
    private readonly IArchiveService _archiveService;
    private readonly IDownloadLogger _logger;

    /// <summary>Log entries arrive from background process threads; the collection lives on this one.</summary>
    private readonly SynchronizationContext? _uiContext;

    private CancellationTokenSource? _activeOperationCts;
    private TaskCompletionSource _operationStopped = CompletedOperationSource();

    /// <summary>Set while many rows change at once, so statistics are raised once instead of per row.</summary>
    private bool _bulkSelectionChange;

    private string _channelUrl = string.Empty;
    private string _outputDirectory = string.Empty;
    private DownloadQuality _selectedQuality = DownloadQuality.P1080;
    private bool _downloadVideos = true;
    private bool _downloadShorts;
    private bool _downloadStreams;
    private bool _downloadThumbnail;
    private bool _downloadSubtitles;
    private bool _skipPreviouslyDownloaded = true;
    private bool _useBrowserCookies;
    private string _selectedBrowser = "Chrome";
    private bool _useCookieFile;
    private string _cookieFilePath = string.Empty;
    private string _channelName = "—";
    private string _statusText = "Sẵn sàng";
    private string _dependencySummary = "Chưa kiểm tra";
    private bool _dependenciesAvailable;
    private bool _isAnalyzing;
    private bool _isDownloading;
    private bool _isCancelling;
    private string _currentVideoTitle = "—";
    private double _currentProgressPercent;
    private string _currentSpeed = "—";
    private string _currentEta = "—";
    private string _title;
    private string _titleInput;
    private bool _isDurationFilterEnabled;
    private DurationFilterComparison _selectedDurationComparison = DurationFilterComparison.ShorterThan;
    private int _durationLimitValue = 1;
    private DurationFilterUnit _selectedDurationUnit = DurationFilterUnit.Minutes;
    private int _recentVideoLimit;
    private bool _disposed;

    public DownloadTabViewModel(
        IChannelAnalyzer channelAnalyzer,
        IVideoDownloadService downloadService,
        IDownloadDialogService dialogService,
        IArchiveService archiveService,
        IDownloadLogger logger,
        string title = DefaultTitle)
    {
        _channelAnalyzer = channelAnalyzer;
        _downloadService = downloadService;
        _dialogService = dialogService;
        _archiveService = archiveService;
        _logger = logger;
        _title = NormalizeTitle(title);
        _titleInput = _title;
        _uiContext = SynchronizationContext.Current;
        _logger.EntryLogged += OnLogEntryLogged;

        AnalyzeChannelCommand = new AsyncRelayCommand(AnalyzeChannelAsync, CanAnalyzeChannel);
        StartDownloadCommand = new AsyncRelayCommand(StartDownloadAsync, CanStartDownload);
        CancelDownloadCommand = new RelayCommand(CancelDownload, CanCancelDownload);
        BrowseOutputDirectoryCommand = new RelayCommand(BrowseOutputDirectory, () => !IsBusy);
        BrowseCookieFileCommand = new RelayCommand(BrowseCookieFile, () => !IsBusy);
        PasteUrlCommand = new RelayCommand(PasteUrl, () => !IsBusy);
        SelectAllCommand = new RelayCommand(SelectAll, CanChangeSelection);
        UnselectAllCommand = new RelayCommand(UnselectAll, CanChangeSelection);
        OpenOutputDirectoryCommand = new RelayCommand(OpenOutputDirectory, CanOpenOutputDirectory);
        ClearLogCommand = new RelayCommand(ClearLog, () => Logs.Count > 0);
        ConfirmTitleCommand = new RelayCommand(ConfirmTitle);
    }

    public ObservableCollection<DownloadItemViewModel> VideoItems { get; } = [];

    /// <summary>What the list shows: <see cref="VideoItems"/> after the recent-video and duration filters.</summary>
    public ObservableCollection<DownloadItemViewModel> FilteredVideoItems { get; } = [];

    public ObservableCollection<string> Logs { get; } = [];

    public IReadOnlyList<SelectableOption<DownloadQuality>> QualityOptions { get; } =
    [
        new(DownloadQuality.Best, "Best"),
        new(DownloadQuality.P2160, "2160p"),
        new(DownloadQuality.P1440, "1440p"),
        new(DownloadQuality.P1080, "1080p"),
        new(DownloadQuality.P720, "720p"),
        new(DownloadQuality.P480, "480p"),
        new(DownloadQuality.AudioMp3, "Audio MP3")
    ];

    public IReadOnlyList<string> BrowserOptions { get; } = ["Chrome", "Edge", "Firefox"];

    public IReadOnlyList<SelectableOption<DurationFilterComparison>> DurationComparisonOptions { get; } =
    [
        new(DurationFilterComparison.ShorterThan, "Nhỏ hơn"),
        new(DurationFilterComparison.LongerThan, "Lớn hơn")
    ];

    public IReadOnlyList<SelectableOption<DurationFilterUnit>> DurationUnitOptions { get; } =
    [
        new(DurationFilterUnit.Seconds, "Giây"),
        new(DurationFilterUnit.Minutes, "Phút"),
        new(DurationFilterUnit.Hours, "Giờ")
    ];

    public IAsyncRelayCommand AnalyzeChannelCommand { get; }

    public IAsyncRelayCommand StartDownloadCommand { get; }

    public IRelayCommand CancelDownloadCommand { get; }

    public IRelayCommand BrowseOutputDirectoryCommand { get; }

    public IRelayCommand BrowseCookieFileCommand { get; }

    public IRelayCommand PasteUrlCommand { get; }

    public IRelayCommand SelectAllCommand { get; }

    public IRelayCommand UnselectAllCommand { get; }

    public IRelayCommand OpenOutputDirectoryCommand { get; }

    public IRelayCommand ClearLogCommand { get; }

    public IRelayCommand ConfirmTitleCommand { get; }

    public string Title
    {
        get => _title;
        set
        {
            var normalizedTitle = NormalizeTitle(value);
            SetProperty(ref _title, normalizedTitle);
            if (!string.Equals(_titleInput, normalizedTitle, StringComparison.Ordinal))
            {
                _titleInput = normalizedTitle;
                OnPropertyChanged(nameof(TitleInput));
            }
        }
    }

    /// <summary>The title being typed; it only becomes <see cref="Title"/> when confirmed, so the tab is not renamed per keystroke.</summary>
    public string TitleInput
    {
        get => _titleInput;
        set => SetProperty(ref _titleInput, value ?? string.Empty);
    }

    public bool IsDurationFilterEnabled
    {
        get => _isDurationFilterEnabled;
        set
        {
            if (SetProperty(ref _isDurationFilterEnabled, value))
            {
                ApplyFilters();
            }
        }
    }

    public DurationFilterComparison SelectedDurationComparison
    {
        get => _selectedDurationComparison;
        set
        {
            if (SetProperty(ref _selectedDurationComparison, value))
            {
                ApplyFilters();
            }
        }
    }

    public int DurationLimitValue
    {
        get => _durationLimitValue;
        set
        {
            if (SetProperty(ref _durationLimitValue, Math.Max(1, value)))
            {
                ApplyFilters();
            }
        }
    }

    public DurationFilterUnit SelectedDurationUnit
    {
        get => _selectedDurationUnit;
        set
        {
            if (SetProperty(ref _selectedDurationUnit, value))
            {
                ApplyFilters();
            }
        }
    }

    /// <summary>0 shows every video.</summary>
    public int RecentVideoLimit
    {
        get => _recentVideoLimit;
        set
        {
            if (SetProperty(ref _recentVideoLimit, Math.Max(0, value)))
            {
                ApplyFilters();
            }
        }
    }

    public string ChannelUrl
    {
        get => _channelUrl;
        set
        {
            if (SetProperty(ref _channelUrl, value))
            {
                NotifyCommandStates();
            }
        }
    }

    public string OutputDirectory
    {
        get => _outputDirectory;
        set
        {
            if (SetProperty(ref _outputDirectory, value))
            {
                NotifyCommandStates();
            }
        }
    }

    public DownloadQuality SelectedQuality
    {
        get => _selectedQuality;
        set => SetProperty(ref _selectedQuality, value);
    }

    public bool DownloadVideos
    {
        get => _downloadVideos;
        set => SetContentOption(ref _downloadVideos, value);
    }

    public bool DownloadShorts
    {
        get => _downloadShorts;
        set => SetContentOption(ref _downloadShorts, value);
    }

    public bool DownloadStreams
    {
        get => _downloadStreams;
        set => SetContentOption(ref _downloadStreams, value);
    }

    public bool DownloadThumbnail
    {
        get => _downloadThumbnail;
        set => SetProperty(ref _downloadThumbnail, value);
    }

    public bool DownloadSubtitles
    {
        get => _downloadSubtitles;
        set => SetProperty(ref _downloadSubtitles, value);
    }

    public bool SkipPreviouslyDownloaded
    {
        get => _skipPreviouslyDownloaded;
        set => SetProperty(ref _skipPreviouslyDownloaded, value);
    }

    /// <summary>Only the browser name is ever passed to yt-dlp; cookies are never read, stored or logged by this app.</summary>
    public bool UseBrowserCookies
    {
        get => _useBrowserCookies;
        set
        {
            if (SetProperty(ref _useBrowserCookies, value) && value)
            {
                // yt-dlp rejects a command line combining --cookies-from-browser and --cookies.
                UseCookieFile = false;
            }
        }
    }

    public string SelectedBrowser
    {
        get => _selectedBrowser;
        set => SetProperty(ref _selectedBrowser, NormalizeBrowserDisplay(value));
    }

    /// <summary>The cookie file path is only ever passed to yt-dlp; it is never read, stored or logged by this app.</summary>
    public bool UseCookieFile
    {
        get => _useCookieFile;
        set
        {
            if (SetProperty(ref _useCookieFile, value) && value)
            {
                UseBrowserCookies = false;
            }
        }
    }

    public string CookieFilePath
    {
        get => _cookieFilePath;
        set => SetProperty(ref _cookieFilePath, value ?? string.Empty);
    }

    public string ChannelName
    {
        get => _channelName;
        set => SetProperty(ref _channelName, value);
    }

    public string StatusText
    {
        get => _statusText;
        set => SetProperty(ref _statusText, value);
    }

    public string DependencySummary
    {
        get => _dependencySummary;
        set => SetProperty(ref _dependencySummary, value);
    }

    public bool DependenciesAvailable
    {
        get => _dependenciesAvailable;
        set
        {
            if (SetProperty(ref _dependenciesAvailable, value))
            {
                NotifyCommandStates();
            }
        }
    }

    public bool IsAnalyzing
    {
        get => _isAnalyzing;
        private set
        {
            if (SetProperty(ref _isAnalyzing, value))
            {
                OnPropertyChanged(nameof(IsBusy));
                NotifyCommandStates();
            }
        }
    }

    public bool IsDownloading
    {
        get => _isDownloading;
        private set
        {
            if (SetProperty(ref _isDownloading, value))
            {
                OnPropertyChanged(nameof(IsBusy));
                NotifyCommandStates();
            }
        }
    }

    public bool IsCancelling
    {
        get => _isCancelling;
        private set
        {
            if (SetProperty(ref _isCancelling, value))
            {
                NotifyCommandStates();
            }
        }
    }

    public bool IsBusy => IsAnalyzing || IsDownloading;

    public string CurrentVideoTitle
    {
        get => _currentVideoTitle;
        private set => SetProperty(ref _currentVideoTitle, value);
    }

    public double CurrentProgressPercent
    {
        get => _currentProgressPercent;
        private set => SetProperty(ref _currentProgressPercent, Math.Clamp(value, 0, 100));
    }

    public string CurrentSpeed
    {
        get => _currentSpeed;
        private set => SetProperty(ref _currentSpeed, value);
    }

    public string CurrentEta
    {
        get => _currentEta;
        private set => SetProperty(ref _currentEta, value);
    }

    public int TotalVideoCount => VideoItems.Count;

    public int VisibleVideoCount => FilteredVideoItems.Count;

    public int FilteredOutCount => TotalVideoCount - VisibleVideoCount;

    public int SelectedVideoCount => FilteredVideoItems.Count(static item => item.IsSelected);

    public int PendingCount => FilteredVideoItems.Count(static item =>
        item.IsSelected &&
        item.Status is DownloadStatus.Pending
            or DownloadStatus.Ready
            or DownloadStatus.Failed
            or DownloadStatus.Cancelled);

    public int CompletedCount => FilteredVideoItems.Count(static item => item.Status == DownloadStatus.Completed);

    public int SkippedCount => FilteredVideoItems.Count(static item =>
        item.Status is DownloadStatus.AlreadyDownloaded or DownloadStatus.Skipped);

    public int FailedCount => FilteredVideoItems.Count(static item =>
        item.Status is DownloadStatus.Failed
            or DownloadStatus.Private
            or DownloadStatus.RequiresLogin
            or DownloadStatus.Unavailable);

    public void ApplyDependencyState(bool available, string summary, string statusText)
    {
        DependenciesAvailable = available;
        DependencySummary = summary;
        if (!IsBusy)
        {
            StatusText = statusText;
        }
    }

    /// <summary>Cancels the running analysis or download and waits until it has fully stopped.</summary>
    public async Task CancelActiveOperationAsync()
    {
        if (!IsBusy)
        {
            return;
        }

        CancelDownload();
        await _operationStopped.Task;
    }

    /// <summary>Gives a new tab the same options as the tab it was created from; URL, title and videos stay its own.</summary>
    public void CopySharedStateFrom(DownloadTabViewModel source)
    {
        ArgumentNullException.ThrowIfNull(source);

        OutputDirectory = source.OutputDirectory;
        SelectedQuality = source.SelectedQuality;
        DownloadVideos = source.DownloadVideos;
        DownloadShorts = source.DownloadShorts;
        DownloadStreams = source.DownloadStreams;
        DownloadThumbnail = source.DownloadThumbnail;
        DownloadSubtitles = source.DownloadSubtitles;
        SkipPreviouslyDownloaded = source.SkipPreviouslyDownloaded;
        UseBrowserCookies = source.UseBrowserCookies;
        SelectedBrowser = source.SelectedBrowser;
        UseCookieFile = source.UseCookieFile;
        CookieFilePath = source.CookieFilePath;
        IsDurationFilterEnabled = source.IsDurationFilterEnabled;
        SelectedDurationComparison = source.SelectedDurationComparison;
        DurationLimitValue = source.DurationLimitValue;
        SelectedDurationUnit = source.SelectedDurationUnit;
        RecentVideoLimit = source.RecentVideoLimit;
        DependenciesAvailable = source.DependenciesAvailable;
        DependencySummary = source.DependencySummary;
        StatusText = source.IsBusy ? "Sẵn sàng" : source.StatusText;
    }

    public void ApplySettings(DownloadTabSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        Title = settings.Title;
        ChannelUrl = settings.ChannelUrl?.Trim() ?? string.Empty;
        OutputDirectory = settings.OutputDirectory?.Trim() ?? string.Empty;
        SelectedQuality = settings.Quality;
        DownloadVideos = settings.DownloadVideos;
        DownloadShorts = settings.DownloadShorts;
        DownloadStreams = settings.DownloadStreams;
        DownloadThumbnail = settings.DownloadThumbnail;
        DownloadSubtitles = settings.DownloadSubtitles;
        SkipPreviouslyDownloaded = settings.SkipPreviouslyDownloaded;
        UseBrowserCookies = settings.UseBrowserCookies;
        SelectedBrowser = NormalizeBrowserDisplay(settings.SelectedBrowser);
        UseCookieFile = settings.UseCookieFile;
        CookieFilePath = settings.CookieFilePath?.Trim() ?? string.Empty;
        IsDurationFilterEnabled = settings.IsDurationFilterEnabled;
        SelectedDurationComparison = settings.DurationFilterComparison;
        DurationLimitValue = settings.DurationLimitValue;
        SelectedDurationUnit = settings.DurationFilterUnit;
        RecentVideoLimit = settings.RecentVideoLimit;
    }

    public DownloadTabSettings CreateSettingsSnapshot() =>
        new()
        {
            Title = Title,
            ChannelUrl = ChannelUrl.Trim(),
            OutputDirectory = OutputDirectory.Trim(),
            Quality = SelectedQuality,
            DownloadVideos = DownloadVideos,
            DownloadShorts = DownloadShorts,
            DownloadStreams = DownloadStreams,
            DownloadThumbnail = DownloadThumbnail,
            DownloadSubtitles = DownloadSubtitles,
            SkipPreviouslyDownloaded = SkipPreviouslyDownloaded,
            UseBrowserCookies = UseBrowserCookies,
            SelectedBrowser = SelectedBrowser.ToLowerInvariant(),
            UseCookieFile = UseCookieFile,
            CookieFilePath = CookieFilePath.Trim(),
            IsDurationFilterEnabled = IsDurationFilterEnabled,
            DurationFilterComparison = SelectedDurationComparison,
            DurationLimitValue = DurationLimitValue,
            DurationFilterUnit = SelectedDurationUnit,
            RecentVideoLimit = RecentVideoLimit
        };

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _logger.EntryLogged -= OnLogEntryLogged;
        foreach (var item in VideoItems)
        {
            item.PropertyChanged -= OnVideoItemPropertyChanged;
        }

        _activeOperationCts?.Dispose();
    }

    private bool CanAnalyzeChannel() =>
        DependenciesAvailable &&
        !IsBusy &&
        !string.IsNullOrWhiteSpace(ChannelUrl) &&
        HasSelectedContentType();

    private async Task AnalyzeChannelAsync()
    {
        if (!YouTubeUrlHelper.TryNormalizeChannelUrl(ChannelUrl, out var normalizedUri, out var validationError) ||
            normalizedUri is null)
        {
            _logger.Warning(validationError);
            _dialogService.ShowWarning(validationError, "URL không hợp lệ");
            return;
        }

        var cancellationToken = BeginOperation(isDownload: false);
        IsAnalyzing = true;
        StatusText = "Đang phân tích kênh";
        ChannelName = "—";
        ClearVideoItems();
        _logger.Info($"Starting channel analysis: {normalizedUri}");

        try
        {
            var result = await _channelAnalyzer.AnalyzeAsync(
                normalizedUri,
                GetSelectedContentTypes(),
                RecentVideoLimit,
                resolveMissingDurations: IsDurationFilterEnabled,
                UseBrowserCookies,
                SelectedBrowser.ToLowerInvariant(),
                UseCookieFile,
                CookieFilePath.Trim(),
                cancellationToken);
            ChannelName = result.ChannelName;

            // A tab still carrying its placeholder name takes the channel's name.
            if (Title.StartsWith(DefaultTitle + " ", StringComparison.Ordinal) ||
                string.Equals(Title, DefaultTitle, StringComparison.Ordinal))
            {
                Title = result.ChannelName;
            }

            var archivedIds = SkipPreviouslyDownloaded && !string.IsNullOrWhiteSpace(OutputDirectory)
                ? await _archiveService.ReadVideoIdsAsync(OutputDirectory, result.ChannelName, cancellationToken)
                : new HashSet<string>();

            foreach (var model in result.Items)
            {
                if (SkipPreviouslyDownloaded && archivedIds.Contains(model.VideoId))
                {
                    model.Status = DownloadStatus.AlreadyDownloaded;
                    model.IsSelected = false;
                }

                AddVideoItem(new DownloadItemViewModel(model));
            }

            // Hiding every video because none has a duration would look like an empty channel.
            if (IsDurationFilterEnabled &&
                result.Items.Count > 0 &&
                result.Items.All(static item => item.DurationSeconds is null))
            {
                IsDurationFilterEnabled = false;
                const string durationWarning =
                    "Không lấy được thời lượng từ YouTube Data API hoặc yt-dlp. " +
                    "Bộ lọc thời lượng đã được tắt để vẫn hiển thị danh sách. " +
                    "Hãy cấu hình YouTube Data API key (ưu tiên) hoặc bật cookie trình duyệt rồi phân tích lại.";
                _logger.Warning(durationWarning);
                _dialogService.ShowWarning(durationWarning, "Không lấy được thời lượng");
            }

            ApplyFilters();
            if (VideoItems.Count == 0)
            {
                StatusText = "Hoàn thành";
                _logger.Warning("Channel analysis completed but no videos were found.");
                _dialogService.ShowInfo("Không tìm thấy video trong các loại nội dung đã chọn.", "Không có video");
            }
            else
            {
                StatusText = "Sẵn sàng";
                _logger.Info($"Found {VideoItems.Count} unique videos.");
            }
        }
        catch (OperationCanceledException)
        {
            StatusText = "Đã hủy";
            _logger.Warning("Channel analysis cancelled by user.");
        }
        catch (ChannelAnalysisException exception)
        {
            StatusText = "Có lỗi";
            _logger.Error("Channel analysis failed.", exception: exception);
            _dialogService.ShowError(exception.Message, "Không thể phân tích kênh");
        }
        catch (Exception exception)
        {
            StatusText = "Có lỗi";
            _logger.Error("Unexpected channel analysis failure.", exception: exception);
            _dialogService.ShowError(
                "Đã xảy ra lỗi ngoài dự kiến khi phân tích kênh. Xem log để biết chi tiết.",
                "Lỗi");
        }
        finally
        {
            IsAnalyzing = false;
            CompleteOperation();
        }
    }

    private bool CanStartDownload() =>
        DependenciesAvailable &&
        !IsBusy &&
        !string.IsNullOrWhiteSpace(OutputDirectory) &&
        FilteredVideoItems.Any(static item => item.IsSelected && item.CanSelect);

    private async Task StartDownloadAsync()
    {
        if (!await TryPrepareOutputDirectoryAsync())
        {
            return;
        }

        // Only rows the filters still show are downloaded.
        var selectedItems = FilteredVideoItems
            .Where(static item => item.IsSelected && item.CanSelect)
            .ToArray();
        if (selectedItems.Length == 0)
        {
            _dialogService.ShowWarning("Vui lòng chọn ít nhất một video có thể tải.", "Chưa chọn video");
            return;
        }

        var cancellationToken = BeginOperation(isDownload: true);
        IsDownloading = true;
        StatusText = "Đang tải";
        var options = CreateDownloadOptions();
        _logger.Info($"Starting sequential download of {selectedItems.Length} selected videos.");

        DownloadItemViewModel? currentItem = null;
        try
        {
            foreach (var item in selectedItems)
            {
                cancellationToken.ThrowIfCancellationRequested();
                currentItem = item;
                PrepareItemForDownload(item);
                var progress = new Progress<DownloadProgressMessage>(message => ApplyProgress(item, message));

                var result = await _downloadService.DownloadAsync(item.ToModel(), options, progress, cancellationToken);
                ApplyDownloadResult(item, result);
                RaiseStatisticsChanged();
            }

            StatusText = FailedCount > 0 ? "Hoàn thành — có lỗi" : "Hoàn thành";
            _logger.Info(
                $"Download queue completed. Completed: {CompletedCount}, skipped: {SkippedCount}, failed: {FailedCount}.");
        }
        catch (OperationCanceledException)
        {
            if (currentItem is not null &&
                currentItem.Status is DownloadStatus.Downloading or DownloadStatus.Processing)
            {
                currentItem.Status = DownloadStatus.Cancelled;
                currentItem.ErrorMessage = "Đã hủy bởi người dùng. File .part được giữ để tiếp tục lần sau.";
            }

            StatusText = "Đã hủy";
            _logger.Warning("Download queue cancelled by user.");
            RaiseStatisticsChanged();
        }
        catch (Exception exception)
        {
            if (currentItem is not null)
            {
                currentItem.Status = DownloadStatus.Failed;
                currentItem.ErrorMessage = "Lỗi ngoài dự kiến. Xem log để biết chi tiết.";
            }

            StatusText = "Có lỗi";
            _logger.Error("Unexpected error while processing the download queue.", currentItem?.VideoId, exception);
            _dialogService.ShowError("Đã xảy ra lỗi ngoài dự kiến khi tải. Xem log để biết chi tiết.", "Lỗi tải video");
        }
        finally
        {
            IsDownloading = false;
            CurrentVideoTitle = "—";
            CurrentSpeed = "—";
            CurrentEta = "—";
            CurrentProgressPercent = 0;
            CompleteOperation();
        }
    }

    private bool CanCancelDownload() => IsBusy && !IsCancelling;

    /// <summary>
    /// The token reaches <c>ProcessHelper.RunAsync</c>, which kills yt-dlp together with any ffmpeg it spawned.
    /// </summary>
    private void CancelDownload()
    {
        if (_activeOperationCts is null || _activeOperationCts.IsCancellationRequested)
        {
            return;
        }

        IsCancelling = true;
        StatusText = "Đang hủy";
        _activeOperationCts.Cancel();
        _logger.Warning("Cancellation requested by user.");
    }

    private void BrowseOutputDirectory()
    {
        try
        {
            var selected = _dialogService.SelectFolder(OutputDirectory);
            if (!string.IsNullOrWhiteSpace(selected))
            {
                OutputDirectory = selected;
            }
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            _logger.Error("Folder picker failed.", exception: exception);
            _dialogService.ShowError("Không thể chọn thư mục. Hãy thử lại hoặc nhập đường dẫn trực tiếp.", "Lỗi thư mục");
        }
    }

    private void BrowseCookieFile()
    {
        try
        {
            var selected = _dialogService.SelectFile(
                CookieFilePath,
                "Cookie files (*.txt)|*.txt|All files (*.*)|*.*",
                "Chọn file cookie (Netscape/cookies.txt)");
            if (!string.IsNullOrWhiteSpace(selected))
            {
                CookieFilePath = selected;
                UseCookieFile = true;
            }
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            _logger.Error("Cookie file picker failed.", exception: exception);
            _dialogService.ShowError("Không thể chọn file cookie. Hãy thử lại hoặc nhập đường dẫn trực tiếp.", "Lỗi file cookie");
        }
    }

    private void PasteUrl()
    {
        try
        {
            var text = _dialogService.GetClipboardText();
            if (!string.IsNullOrWhiteSpace(text))
            {
                ChannelUrl = text.Trim();
            }
        }
        catch (ExternalException exception)
        {
            // Another application is holding the clipboard open.
            _logger.Error("Clipboard is temporarily unavailable.", exception: exception);
            _dialogService.ShowWarning("Clipboard đang được ứng dụng khác sử dụng. Hãy thử lại.", "Không thể dán URL");
        }
    }

    private bool CanChangeSelection() => !IsBusy && VisibleVideoCount > 0;

    private void SelectAll() => SetAllSelections(isSelected: true);

    private void UnselectAll() => SetAllSelections(isSelected: false);

    private void SetAllSelections(bool isSelected)
    {
        _bulkSelectionChange = true;
        try
        {
            foreach (var item in FilteredVideoItems)
            {
                item.IsSelected = isSelected && item.CanSelect;
            }
        }
        finally
        {
            _bulkSelectionChange = false;
            RaiseStatisticsChanged();
            NotifyCommandStates();
        }
    }

    private bool CanOpenOutputDirectory() => !string.IsNullOrWhiteSpace(OutputDirectory);

    private void OpenOutputDirectory()
    {
        try
        {
            _dialogService.OpenFolder(OutputDirectory.Trim());
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or InvalidOperationException or Win32Exception)
        {
            _logger.Error("Could not open output directory.", exception: exception);
            _dialogService.ShowError(exception.Message, "Không thể mở thư mục");
        }
    }

    private void ClearLog()
    {
        Logs.Clear();
        ClearLogCommand.NotifyCanExecuteChanged();
    }

    private async Task<bool> TryPrepareOutputDirectoryAsync()
    {
        if (string.IsNullOrWhiteSpace(OutputDirectory))
        {
            _dialogService.ShowWarning("Vui lòng chọn thư mục lưu.", "Thiếu thư mục");
            return false;
        }

        try
        {
            OutputDirectory = await _downloadService.PrepareOutputDirectoryAsync(OutputDirectory, CancellationToken.None);
            return true;
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            _logger.Error("Output directory is invalid or not writable.", exception: exception);
            _dialogService.ShowError("Thư mục lưu không hợp lệ hoặc ứng dụng không có quyền ghi.", "Lỗi thư mục");
            return false;
        }
    }

    private void PrepareItemForDownload(DownloadItemViewModel item)
    {
        item.Status = DownloadStatus.Downloading;
        item.ProgressPercent = 0;
        item.Speed = string.Empty;
        item.Eta = string.Empty;
        item.ErrorMessage = null;
        item.OutputFilePath = null;
        CurrentVideoTitle = item.Title;
        CurrentProgressPercent = 0;
        CurrentSpeed = "—";
        CurrentEta = "—";
        _logger.Info("Download started.", item.VideoId);
        RaiseStatisticsChanged();
    }

    private void ApplyProgress(DownloadItemViewModel item, DownloadProgressMessage message)
    {
        switch (message.Type)
        {
            case ProgressMessageType.Start:
                item.Status = DownloadStatus.Downloading;
                break;
            case ProgressMessageType.Progress:
                item.Status = DownloadStatus.Downloading;
                item.ProgressPercent = message.Percent ?? item.ProgressPercent;
                item.Speed = message.Speed;
                item.Eta = message.Eta;
                CurrentProgressPercent = item.ProgressPercent;
                CurrentSpeed = string.IsNullOrWhiteSpace(message.Speed) ? "—" : message.Speed;
                CurrentEta = string.IsNullOrWhiteSpace(message.Eta) ? "—" : message.Eta;
                break;
            case ProgressMessageType.Processing:
                item.Status = DownloadStatus.Processing;
                break;
            case ProgressMessageType.Complete:
                item.ProgressPercent = 100;
                item.OutputFilePath = message.OutputFilePath;
                CurrentProgressPercent = 100;
                break;
            case ProgressMessageType.Error:
                item.ErrorMessage = message.Message;
                break;
            case ProgressMessageType.Warning:
            case ProgressMessageType.Debug:
                break;
        }
    }

    private void ApplyDownloadResult(DownloadItemViewModel item, DownloadResult result)
    {
        item.Status = result.Status;
        item.OutputFilePath = result.OutputFilePath;
        item.ErrorMessage = result.ErrorMessage;

        if (result.Status == DownloadStatus.Completed)
        {
            item.ProgressPercent = 100;
            _logger.Info($"Download completed: {result.OutputFilePath}", item.VideoId);
        }
        else if (result.Status == DownloadStatus.AlreadyDownloaded)
        {
            _logger.Info("Skipped because the video is already in the archive.", item.VideoId);
        }
        else
        {
            _logger.Error(result.ErrorMessage ?? "Download failed.", item.VideoId);
        }
    }

    private CancellationToken BeginOperation(bool isDownload)
    {
        _activeOperationCts?.Dispose();
        _activeOperationCts = new CancellationTokenSource();
        _operationStopped = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        IsCancelling = false;

        if (isDownload)
        {
            CurrentProgressPercent = 0;
        }

        return _activeOperationCts.Token;
    }

    private void CompleteOperation()
    {
        _activeOperationCts?.Dispose();
        _activeOperationCts = null;
        IsCancelling = false;
        _operationStopped.TrySetResult();
        NotifyCommandStates();
    }

    private DownloadOptions CreateDownloadOptions() =>
        new()
        {
            OutputDirectory = OutputDirectory,
            ChannelName = ChannelName,
            Quality = SelectedQuality,
            DownloadThumbnail = DownloadThumbnail,
            DownloadSubtitles = DownloadSubtitles,
            SkipPreviouslyDownloaded = SkipPreviouslyDownloaded,
            UseBrowserCookies = UseBrowserCookies,
            BrowserName = SelectedBrowser.ToLowerInvariant(),
            UseCookieFile = UseCookieFile,
            CookieFilePath = CookieFilePath.Trim()
        };

    private List<ContentType> GetSelectedContentTypes()
    {
        var types = new List<ContentType>(3);
        if (DownloadVideos)
        {
            types.Add(ContentType.Video);
        }

        if (DownloadShorts)
        {
            types.Add(ContentType.Short);
        }

        if (DownloadStreams)
        {
            types.Add(ContentType.Stream);
        }

        return types;
    }

    private bool HasSelectedContentType() => DownloadVideos || DownloadShorts || DownloadStreams;

    private void ConfirmTitle() => Title = TitleInput;

    private static string NormalizeTitle(string? value) =>
        string.IsNullOrWhiteSpace(value) ? DefaultTitle : value.Trim();

    private void SetContentOption(ref bool field, bool value)
    {
        if (SetProperty(ref field, value))
        {
            NotifyCommandStates();
        }
    }

    private void AddVideoItem(DownloadItemViewModel item)
    {
        item.PropertyChanged += OnVideoItemPropertyChanged;
        VideoItems.Add(item);
    }

    private bool MatchesDurationFilter(DownloadItemViewModel item) =>
        DurationFilterHelper.Matches(
            item.DurationSeconds,
            IsDurationFilterEnabled,
            SelectedDurationComparison,
            DurationLimitValue,
            SelectedDurationUnit);

    /// <summary>
    /// Filters run live on every option change. Rows a filter hides are also unticked, so a video
    /// the user can no longer see is never queued by accident.
    /// </summary>
    private void ApplyFilters()
    {
        var recentItems = RecentVideoFilterHelper.TakeMostRecent(VideoItems, RecentVideoLimit, static item => item.UploadDate);
        var matchingItems = recentItems.Where(MatchesDurationFilter).ToArray();

        if (IsDurationFilterEnabled || RecentVideoLimit > 0)
        {
            var visibleItems = matchingItems.ToHashSet();
            _bulkSelectionChange = true;
            try
            {
                foreach (var item in VideoItems.Where(item => !visibleItems.Contains(item)))
                {
                    item.IsSelected = false;
                }
            }
            finally
            {
                _bulkSelectionChange = false;
            }
        }

        FilteredVideoItems.Clear();
        foreach (var item in matchingItems)
        {
            FilteredVideoItems.Add(item);
        }

        RaiseStatisticsChanged();
        NotifyCommandStates();
    }

    private void ClearVideoItems()
    {
        foreach (var item in VideoItems)
        {
            item.PropertyChanged -= OnVideoItemPropertyChanged;
        }

        VideoItems.Clear();
        FilteredVideoItems.Clear();
        RaiseStatisticsChanged();
    }

    private void OnVideoItemPropertyChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (_bulkSelectionChange)
        {
            return;
        }

        if (args.PropertyName is nameof(DownloadItemViewModel.IsSelected) or nameof(DownloadItemViewModel.Status))
        {
            RaiseStatisticsChanged();
            NotifyCommandStates();
        }
    }

    /// <summary>The log is app-wide: every tab shows every entry.</summary>
    private void OnLogEntryLogged(object? sender, DownloadLogEntry entry)
    {
        void AddEntry()
        {
            Logs.Add(entry.ToString());
            while (Logs.Count > MaxLogLines)
            {
                Logs.RemoveAt(0);
            }

            ClearLogCommand.NotifyCanExecuteChanged();
        }

        if (_uiContext is null || SynchronizationContext.Current == _uiContext)
        {
            AddEntry();
        }
        else
        {
            _uiContext.Post(_ => AddEntry(), null);
        }
    }

    private void RaiseStatisticsChanged()
    {
        OnPropertyChanged(nameof(TotalVideoCount));
        OnPropertyChanged(nameof(VisibleVideoCount));
        OnPropertyChanged(nameof(FilteredOutCount));
        OnPropertyChanged(nameof(SelectedVideoCount));
        OnPropertyChanged(nameof(PendingCount));
        OnPropertyChanged(nameof(CompletedCount));
        OnPropertyChanged(nameof(SkippedCount));
        OnPropertyChanged(nameof(FailedCount));
    }

    private void NotifyCommandStates()
    {
        AnalyzeChannelCommand.NotifyCanExecuteChanged();
        StartDownloadCommand.NotifyCanExecuteChanged();
        CancelDownloadCommand.NotifyCanExecuteChanged();
        BrowseOutputDirectoryCommand.NotifyCanExecuteChanged();
        BrowseCookieFileCommand.NotifyCanExecuteChanged();
        PasteUrlCommand.NotifyCanExecuteChanged();
        SelectAllCommand.NotifyCanExecuteChanged();
        UnselectAllCommand.NotifyCanExecuteChanged();
        OpenOutputDirectoryCommand.NotifyCanExecuteChanged();
    }

    private static string NormalizeBrowserDisplay(string? value) =>
        value?.Trim().ToLowerInvariant() switch
        {
            "edge" => "Edge",
            "firefox" => "Firefox",
            _ => "Chrome"
        };

    private static TaskCompletionSource CompletedOperationSource()
    {
        var source = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        source.SetResult();
        return source;
    }
}
