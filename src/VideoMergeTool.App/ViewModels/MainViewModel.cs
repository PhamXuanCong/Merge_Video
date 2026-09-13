using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VideoMergeTool.App.Services;
using VideoMergeTool.App.Theming;
using VideoMergeTool.Core.Enums;
using VideoMergeTool.Core.Interfaces;
using VideoMergeTool.Core.Models;

namespace VideoMergeTool.App.ViewModels;

public sealed partial class MainViewModel : ObservableObject, IDisposable
{
    private static readonly int StatusCount = Enum.GetValues<VideoTaskStatus>().Length;

    /// <summary>How many recently used input folders to keep for quick switching.</summary>
    private const int MaxRecentFolders = 8;

    private readonly IInputVideoScanner _inputVideoScanner;
    private readonly IVideoProcessingCoordinator _processingCoordinator;
    private readonly IUserSettingsService _userSettingsService;
    private readonly IUserPrompt _userPrompt;
    private readonly ThemeManager _themeManager;

    private readonly Dictionary<string, VideoTaskItemViewModel> _taskLookup =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Live count per status, so the summary never re-scans the whole task list.</summary>
    private readonly int[] _statusCounts = new int[StatusCount];

    private readonly Stopwatch _stopwatch = new();
    private readonly DispatcherTimer _elapsedTimer;

    private CancellationTokenSource? _scanCancellation;
    private CancellationTokenSource? _processingCancellation;

    /// <summary>Running sum of <see cref="VideoTaskItemViewModel.EffectiveProgress"/>.</summary>
    private double _progressAccumulator;

    private bool _isDisposed;

    public MainViewModel(
        IInputVideoScanner inputVideoScanner,
        IVideoProcessingCoordinator processingCoordinator,
        IUserSettingsService userSettingsService,
        IUserPrompt userPrompt,
        ThemeManager themeManager)
    {
        _inputVideoScanner = inputVideoScanner;
        _processingCoordinator = processingCoordinator;
        _userSettingsService = userSettingsService;
        _userPrompt = userPrompt;
        _themeManager = themeManager;

        var settings = _userSettingsService.Load();
        _inputFolder = settings.InputFolder;
        _recentFolders = new ObservableCollection<string>(settings.RecentFolders);
        _mergeLayout = settings.MergeLayout;
        _x264Preset = settings.X264Preset;
        _videoEncoder = settings.VideoEncoder;
        _pairingMode = settings.PairingMode;
        _sourceFileAction = settings.SourceFileAction;
        _existingOutputAction = settings.ExistingOutputAction;
        _cpuThreadLimit = CpuThreadLimits.Contains(settings.CpuThreadLimit)
            ? settings.CpuThreadLimit
            : _cpuThreadLimit;

        // Assigned to the backing field above, so the generated change callback never ran.
        _themePreference = settings.ThemePreference;
        _themeManager.Apply(_themePreference);

        _elapsedTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromSeconds(1)
        };
        _elapsedTimer.Tick += (_, _) => UpdateTimings();
    }

    /// <summary>
    /// Replaced wholesale on each scan: rebuilding a large batch item by item would raise one
    /// collection-changed notification per file.
    /// </summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(StartCommand))]
    private ObservableCollection<VideoTaskItemViewModel> _tasks = [];

    /// <summary>Recently used input folders, newest first, for quick switching without Browse.</summary>
    [ObservableProperty]
    private ObservableCollection<string> _recentFolders = [];

    public IReadOnlyList<SelectableOption<MergeLayout>> MergeLayouts { get; } =
    [
        new(MergeLayout.Horizontal, "Ngang (cạnh nhau)"),
        new(MergeLayout.Vertical, "Dọc (trên dưới)")
    ];

    public IReadOnlyList<SelectableOption<X264Preset>> X264Presets { get; } =
    [
        new(X264Preset.VeryFast, "Rất nhanh"),
        new(X264Preset.Faster, "Nhanh hơn"),
        new(X264Preset.Fast, "Nhanh"),
        new(X264Preset.Medium, "Trung bình (nhẹ nhất)")
    ];

    public IReadOnlyList<SelectableOption<PairingMode>> PairingModes { get; } =
    [
        new(PairingMode.RandomWithoutImmediateRepeat, "Ngẫu nhiên, không lặp"),
        new(PairingMode.Random, "Ngẫu nhiên"),
        new(PairingMode.Sequential, "Theo thứ tự")
    ];

    public IReadOnlyList<SelectableOption<SourceFileAction>> SourceFileActions { get; } =
    [
        new(SourceFileAction.Keep, "Giữ lại"),
        // "processed" is the real folder name on disk, so it stays untranslated.
        new(SourceFileAction.MoveToProcessed, "Chuyển vào \"processed\""),
        new(SourceFileAction.Delete, "Xoá đi")
    ];

    public IReadOnlyList<SelectableOption<ExistingOutputAction>> ExistingOutputActions { get; } =
    [
        new(ExistingOutputAction.Skip, "Bỏ qua file đó"),
        new(ExistingOutputAction.Overwrite, "Ghi đè"),
        new(ExistingOutputAction.CreateUniqueName, "Giữ cả hai")
    ];

    public IReadOnlyList<SelectableOption<VideoEncoder>> VideoEncoders { get; } =
    [
        new(VideoEncoder.Cpu, "CPU (libx264)"),
        new(VideoEncoder.NvidiaGpu, "GPU NVIDIA (NVENC)")
    ];

    public IReadOnlyList<int> CpuThreadLimits { get; } = GetCpuThreadLimits();

    [ObservableProperty]
    private MergeLayout _mergeLayout = MergeLayout.Horizontal;

    [ObservableProperty]
    private X264Preset _x264Preset = X264Preset.VeryFast;

    [ObservableProperty]
    private VideoEncoder _videoEncoder = VideoEncoder.Cpu;

    [ObservableProperty]
    private PairingMode _pairingMode = PairingMode.RandomWithoutImmediateRepeat;

    [ObservableProperty]
    private SourceFileAction _sourceFileAction = SourceFileAction.Delete;

    [ObservableProperty]
    private ExistingOutputAction _existingOutputAction = ExistingOutputAction.Skip;

    [ObservableProperty]
    private int _cpuThreadLimit = new ProcessingOptions().CpuThreadLimit;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ThemeButtonText))]
    private ThemePreference _themePreference = ThemePreference.System;

    public string ThemeButtonText => ThemePreference switch
    {
        ThemePreference.Light => "Theme: Light",
        ThemePreference.Dark => "Theme: Dark",
        _ => "Theme: Auto"
    };

    partial void OnThemePreferenceChanged(ThemePreference value) => _themeManager.Apply(value);

    [RelayCommand]
    private void CycleTheme() => ThemePreference = ThemePreference switch
    {
        ThemePreference.System => ThemePreference.Light,
        ThemePreference.Light => ThemePreference.Dark,
        _ => ThemePreference.System
    };

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ScanCommand))]
    [NotifyCanExecuteChangedFor(nameof(StartCommand))]
    private string _inputFolder = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ScanCommand))]
    [NotifyCanExecuteChangedFor(nameof(StartCommand))]
    [NotifyCanExecuteChangedFor(nameof(CancelCommand))]
    private bool _isBusy;

    [ObservableProperty]
    private double _overallProgress;

    [ObservableProperty]
    private string _summaryText = "Select an input folder to begin.";

    [ObservableProperty]
    private string _countsText = string.Empty;

    [ObservableProperty]
    private string _elapsedText = string.Empty;

    /// <summary>Drives the indeterminate style of the progress bar while a scan is running.</summary>
    [ObservableProperty]
    private bool _isScanning;

    /// <summary>
    /// A scanned list belongs to the folder it came from. Pointing at a different folder without
    /// rescanning would run the batch against files the list does not describe, so drop it.
    /// </summary>
    partial void OnInputFolderChanged(string value)
    {
        if (IsBusy || Tasks.Count == 0)
        {
            return;
        }

        Tasks = [];
        _taskLookup.Clear();
        ResetProgressState();
        ElapsedText = string.Empty;
        SummaryText = "Input folder changed — press Scan again.";
    }

    private bool CanScan() => !IsBusy && !string.IsNullOrWhiteSpace(InputFolder);

    private bool CanStart() => !IsBusy && !string.IsNullOrWhiteSpace(InputFolder) && Tasks.Count > 0;

    private bool CanCancel() => IsBusy;

    [RelayCommand(CanExecute = nameof(CanScan))]
    private async Task ScanAsync()
    {
        IsBusy = true;
        IsScanning = true;
        SummaryText = "Scanning videos…";
        CountsText = string.Empty;
        ElapsedText = string.Empty;

        _scanCancellation = new CancellationTokenSource();

        try
        {
            var videos = await _inputVideoScanner.ScanAsync(InputFolder, _scanCancellation.Token);

            var items = new ObservableCollection<VideoTaskItemViewModel>();
            _taskLookup.Clear();
            foreach (var video in videos)
            {
                var item = new VideoTaskItemViewModel(video.FullPath);
                items.Add(item);
                _taskLookup[video.FullPath] = item;
            }

            Tasks = items;
            ResetProgressState();
            RememberFolder(InputFolder);
            SummaryText = items.Count == 0
                ? "No MP4 files were found in that folder."
                : $"Found {items.Count} input video(s). Press Start to merge them.";
        }
        catch (OperationCanceledException)
        {
            SummaryText = "Scan cancelled.";
        }
        catch (Exception exception)
        {
            SummaryText = $"Scan failed: {exception.Message}";
        }
        finally
        {
            _scanCancellation?.Dispose();
            _scanCancellation = null;
            IsScanning = false;
            IsBusy = false;
        }
    }

    [RelayCommand(CanExecute = nameof(CanStart))]
    private async Task StartAsync()
    {
        // Deleting the originals is the one irreversible thing this app does, so it gets a gate
        // at the moment it would happen rather than only a combo box chosen minutes earlier.
        if (SourceFileAction == SourceFileAction.Delete &&
            !_userPrompt.ConfirmSourceDeletion(Tasks.Count, InputFolder))
        {
            SummaryText = "Start cancelled — no files were changed.";
            return;
        }

        IsBusy = true;
        SummaryText = "Processing videos…";

        foreach (var item in Tasks)
        {
            item.Reset();
        }

        ResetProgressState();
        _stopwatch.Restart();
        _elapsedTimer.Start();

        _processingCancellation = new CancellationTokenSource();
        var progress = new Progress<VideoMergeTask>(OnTaskProgress);

        try
        {
            var summary = await _processingCoordinator.ProcessAsync(
                InputFolder,
                new ProcessingOptions
                {
                    MergeLayout = MergeLayout,
                    X264Preset = X264Preset,
                    VideoEncoder = VideoEncoder,
                    CpuThreadLimit = CpuThreadLimit,
                    PairingMode = PairingMode,
                    SourceFileAction = SourceFileAction,
                    ExistingOutputAction = ExistingOutputAction
                },
                progress,
                _processingCancellation.Token);

            SummaryText = $"Completed: {summary.CompletedCount}; failed: {summary.FailedCount}; " +
                          $"skipped: {summary.SkippedCount}; cancelled: {summary.CancelledCount}.";
        }
        catch (OperationCanceledException)
        {
            SummaryText = "Processing cancelled.";
        }
        catch (Exception exception)
        {
            SummaryText = $"Processing failed: {exception.Message}";
        }
        finally
        {
            _stopwatch.Stop();
            _elapsedTimer.Stop();
            UpdateTimings();
            _processingCancellation?.Dispose();
            _processingCancellation = null;
            IsBusy = false;
        }
    }

    [RelayCommand(CanExecute = nameof(CanCancel))]
    private void Cancel()
    {
        SummaryText = "Cancelling…";
        TryCancel(_processingCancellation);
        TryCancel(_scanCancellation);
    }

    /// <summary>Videos in the list that have not reached a terminal status yet.</summary>
    public int UnfinishedCount => Math.Max(0, Tasks.Count - FinishedCount);

    private int FinishedCount =>
        _statusCounts[(int)VideoTaskStatus.Completed] +
        _statusCounts[(int)VideoTaskStatus.Failed] +
        _statusCounts[(int)VideoTaskStatus.Skipped] +
        _statusCounts[(int)VideoTaskStatus.Cancelled];

    /// <summary>
    /// Cancels whatever is running and waits for it to unwind, so the caller knows the FFmpeg
    /// process tree is gone and the temporary file has been cleaned up before the app exits.
    /// </summary>
    public async Task CancelAndWaitAsync(TimeSpan timeout)
    {
        Cancel();

        var running = new List<Task>(2);
        if (StartCommand.ExecutionTask is { IsCompleted: false } startTask)
        {
            running.Add(startTask);
        }

        if (ScanCommand.ExecutionTask is { IsCompleted: false } scanTask)
        {
            running.Add(scanTask);
        }

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
            // A stuck child process must not leave the window unclosable: close anyway.
        }
        catch (OperationCanceledException)
        {
        }
    }

    public void SaveSettings()
    {
        _userSettingsService.Save(new UserSettings
        {
            InputFolder = InputFolder,
            MergeLayout = MergeLayout,
            X264Preset = X264Preset,
            VideoEncoder = VideoEncoder,
            CpuThreadLimit = CpuThreadLimit,
            PairingMode = PairingMode,
            SourceFileAction = SourceFileAction,
            ExistingOutputAction = ExistingOutputAction,
            ThemePreference = ThemePreference,
            RecentFolders = RecentFolders.ToList()
        });
    }

    /// <summary>
    /// Moves the folder to the front of the recent list (case-insensitive, no duplicates) and
    /// caps the list so it stays a quick picker rather than an ever-growing history.
    /// </summary>
    private void RememberFolder(string folder)
    {
        var trimmed = folder.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (string.IsNullOrWhiteSpace(trimmed))
        {
            return;
        }

        var updated = new ObservableCollection<string>(
            RecentFolders.Where(existing => !string.Equals(existing, trimmed, StringComparison.OrdinalIgnoreCase)));
        updated.Insert(0, trimmed);
        while (updated.Count > MaxRecentFolders)
        {
            updated.RemoveAt(updated.Count - 1);
        }

        RecentFolders = updated;
    }

    public void Dispose()
    {
        if (_isDisposed)
        {
            return;
        }

        _isDisposed = true;
        _elapsedTimer.Stop();
        _processingCancellation?.Dispose();
        _scanCancellation?.Dispose();
    }

    /// <summary>
    /// Called once per FFmpeg progress tick, so everything here is O(1) in the batch size.
    /// </summary>
    private void OnTaskProgress(VideoMergeTask task)
    {
        if (!_taskLookup.TryGetValue(task.InputFile, out var item))
        {
            return;
        }

        var previousProgress = item.EffectiveProgress;
        var previousStatus = item.Status;

        item.Update(task);

        _progressAccumulator += item.EffectiveProgress - previousProgress;
        OverallProgress = Tasks.Count == 0
            ? 0
            : Math.Clamp(_progressAccumulator / Tasks.Count, 0, 1);

        if (previousStatus != item.Status)
        {
            _statusCounts[(int)previousStatus]--;
            _statusCounts[(int)item.Status]++;
            UpdateCountsText();
        }
    }

    private void ResetProgressState()
    {
        _progressAccumulator = 0;
        OverallProgress = 0;
        Array.Clear(_statusCounts);
        _statusCounts[(int)VideoTaskStatus.Pending] = Tasks.Count;
        UpdateCountsText();
    }

    private void UpdateCountsText()
    {
        var completed = _statusCounts[(int)VideoTaskStatus.Completed];
        var failed = _statusCounts[(int)VideoTaskStatus.Failed];
        var skipped = _statusCounts[(int)VideoTaskStatus.Skipped];

        CountsText = Tasks.Count == 0
            ? string.Empty
            : $"{completed + failed + skipped}/{Tasks.Count} done  ·  {completed} ok  ·  {failed} failed  ·  {skipped} skipped";
    }

    private void UpdateTimings()
    {
        var elapsed = _stopwatch.Elapsed;
        if (elapsed == TimeSpan.Zero)
        {
            ElapsedText = string.Empty;
            return;
        }

        ElapsedText = OverallProgress > 0.01 && _stopwatch.IsRunning
            ? $"Elapsed {Format(elapsed)}  ·  about {Format(elapsed * ((1 - OverallProgress) / OverallProgress))} left"
            : $"Elapsed {Format(elapsed)}";
    }

    private static string Format(TimeSpan value) =>
        value.TotalHours >= 1
            ? $"{(int)value.TotalHours}h {value.Minutes:00}m"
            : $"{value.Minutes:00}:{value.Seconds:00}";

    private static void TryCancel(CancellationTokenSource? source)
    {
        try
        {
            source?.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // The operation finished while the click was being handled.
        }
    }

    private static List<int> GetCpuThreadLimits()
    {
        var processorCount = Environment.ProcessorCount;
        var limits = Enumerable.Range(1, Math.Min(processorCount, 16)).ToList();
        if (processorCount > 16)
        {
            limits.Add(processorCount);
        }

        return limits;
    }
}
