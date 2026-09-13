using System.Globalization;
using System.Windows.Data;
using VideoMergeTool.Core.Enums;

namespace VideoMergeTool.App.Converters;

/// <summary>
/// Turns the status enum into wording a user reads, instead of "ReadingMetadata".
/// </summary>
public sealed class StatusTextConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => value switch
    {
        VideoTaskStatus.Pending => "Waiting",
        VideoTaskStatus.ReadingMetadata => "Reading",
        VideoTaskStatus.Processing => "Merging",
        VideoTaskStatus.Validating => "Checking",
        VideoTaskStatus.Completed => "Done",
        VideoTaskStatus.Skipped => "Skipped",
        VideoTaskStatus.Failed => "Failed",
        VideoTaskStatus.Cancelled => "Cancelled",
        _ => string.Empty
    };

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
