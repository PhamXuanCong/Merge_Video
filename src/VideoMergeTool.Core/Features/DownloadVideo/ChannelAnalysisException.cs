namespace VideoMergeTool.Core.Features.DownloadVideo;

/// <summary>A channel analysis failure whose message is safe to show the user as is.</summary>
public sealed class ChannelAnalysisException : Exception
{
    public ChannelAnalysisException(string message)
        : base(message)
    {
    }

    public ChannelAnalysisException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
