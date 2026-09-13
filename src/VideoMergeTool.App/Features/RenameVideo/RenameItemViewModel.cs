using CommunityToolkit.Mvvm.ComponentModel;
using VideoMergeTool.Core.Features.RenameVideo.Enums;
using VideoMergeTool.Core.Features.RenameVideo.Models;

namespace VideoMergeTool.App.Features.RenameVideo;

public sealed partial class RenameItemViewModel : ObservableObject
{
    public RenameItemViewModel(RenamePlanItem item)
        : this(item.OriginalName, item.NewName, item.HasChange ? RenameItemStatus.Pending : RenameItemStatus.Unchanged, item.Note)
    {
    }

    public RenameItemViewModel(string originalName, string newName, RenameItemStatus status, string note)
    {
        OriginalName = originalName;
        _newName = newName;
        _status = status;
        _note = note;
    }

    public string OriginalName { get; }

    [ObservableProperty]
    private string _newName;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusText))]
    private RenameItemStatus _status;

    [ObservableProperty]
    private string _note;

    public string StatusText => Status switch
    {
        RenameItemStatus.Pending => "Sẽ đổi",
        RenameItemStatus.Unchanged => "Giữ nguyên",
        RenameItemStatus.Renamed => "Đã đổi",
        RenameItemStatus.Failed => "Lỗi",
        _ => string.Empty
    };

    public void Apply(RenameItemResult result)
    {
        NewName = result.NewName;
        Status = result.Status;
        Note = result.Note;
    }
}
