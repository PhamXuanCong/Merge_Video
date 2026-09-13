using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using VideoMergeTool.Core.Features.DownloadVideo.Enums;
using VideoMergeTool.Core.Features.DownloadVideo.Models;

namespace VideoMergeTool.App.Features.DownloadVideo;

/// <summary>One row of the video list: fixed metadata plus the observable download state.</summary>
public sealed class DownloadItemViewModel : ObservableObject
{
    private bool _isSelected;
    private double _progressPercent;
    private string _speed;
    private string _eta;
    private DownloadStatus _status;
    private string? _outputFilePath;
    private string? _errorMessage;

    public DownloadItemViewModel(DownloadItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        VideoId = item.VideoId;
        Title = item.Title;
        Url = item.Url;
        ChannelName = item.ChannelName;
        ContentType = item.ContentType;
        DurationSeconds = item.DurationSeconds;
        UploadDate = item.UploadDate;
        ThumbnailUrl = item.ThumbnailUrl;
        _isSelected = item.IsSelected;
        _progressPercent = item.ProgressPercent;
        _speed = item.Speed;
        _eta = item.Eta;
        _status = item.Status;
        _outputFilePath = item.OutputFilePath;
        _errorMessage = item.ErrorMessage;
    }

    public string VideoId { get; }

    public string Title { get; }

    public string Url { get; }

    public string ChannelName { get; }

    public ContentType ContentType { get; }

    public int? DurationSeconds { get; }

    public DateTime? UploadDate { get; }

    public string? ThumbnailUrl { get; }

    public string ContentTypeDisplay =>
        ContentType switch
        {
            ContentType.Video => "Video",
            ContentType.Short => "Short",
            ContentType.Stream => "Livestream",
            _ => ContentType.ToString()
        };

    public string DurationDisplay
    {
        get
        {
            if (DurationSeconds is null)
            {
                return "—";
            }

            var duration = TimeSpan.FromSeconds(DurationSeconds.Value);
            return duration.TotalHours >= 1
                ? duration.ToString(@"h\:mm\:ss", CultureInfo.InvariantCulture)
                : duration.ToString(@"m\:ss", CultureInfo.InvariantCulture);
        }
    }

    public string UploadDateDisplay => UploadDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? "—";

    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (value && !CanSelect)
            {
                return;
            }

            SetProperty(ref _isSelected, value);
        }
    }

    public double ProgressPercent
    {
        get => _progressPercent;
        set => SetProperty(ref _progressPercent, Math.Clamp(value, 0, 100));
    }

    public string Speed
    {
        get => _speed;
        set => SetProperty(ref _speed, value);
    }

    public string Eta
    {
        get => _eta;
        set => SetProperty(ref _eta, value);
    }

    public DownloadStatus Status
    {
        get => _status;
        set
        {
            if (!SetProperty(ref _status, value))
            {
                return;
            }

            OnPropertyChanged(nameof(StatusDisplay));
            OnPropertyChanged(nameof(CanSelect));
            if (!CanSelect)
            {
                IsSelected = false;
            }
        }
    }

    public string StatusDisplay =>
        Status switch
        {
            DownloadStatus.Pending => "Đang chờ",
            DownloadStatus.Analyzing => "Đang phân tích",
            DownloadStatus.Ready => "Sẵn sàng",
            DownloadStatus.Downloading => "Đang tải",
            DownloadStatus.Processing => "Đang xử lý",
            DownloadStatus.Completed => "Hoàn thành",
            DownloadStatus.AlreadyDownloaded => "Đã tải trước đó",
            DownloadStatus.RequiresLogin => "Cần đăng nhập",
            DownloadStatus.Private => "Riêng tư",
            DownloadStatus.Unavailable => "Không khả dụng",
            DownloadStatus.Skipped => "Đã bỏ qua",
            DownloadStatus.Failed => "Lỗi",
            DownloadStatus.Cancelled => "Đã hủy",
            _ => Status.ToString()
        };

    /// <summary>Private, login-only, unavailable, in-progress and finished videos cannot be queued.</summary>
    public bool CanSelect =>
        Status is DownloadStatus.Pending
            or DownloadStatus.Ready
            or DownloadStatus.Failed
            or DownloadStatus.Cancelled
            or DownloadStatus.Skipped;

    public string? OutputFilePath
    {
        get => _outputFilePath;
        set => SetProperty(ref _outputFilePath, value);
    }

    public string? ErrorMessage
    {
        get => _errorMessage;
        set => SetProperty(ref _errorMessage, value);
    }

    public DownloadItem ToModel() =>
        new()
        {
            IsSelected = IsSelected,
            VideoId = VideoId,
            Title = Title,
            Url = Url,
            ChannelName = ChannelName,
            ContentType = ContentType,
            DurationSeconds = DurationSeconds,
            UploadDate = UploadDate,
            ThumbnailUrl = ThumbnailUrl,
            ProgressPercent = ProgressPercent,
            Speed = Speed,
            Eta = Eta,
            Status = Status,
            OutputFilePath = OutputFilePath,
            ErrorMessage = ErrorMessage
        };
}
