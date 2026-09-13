namespace VideoMergeTool.Core.Features.MergeVideo.Interfaces;

public interface ICompanionVideoProvider
{
    Task<IReadOnlyList<string>> GetVideosAsync(
        CancellationToken cancellationToken);
}
