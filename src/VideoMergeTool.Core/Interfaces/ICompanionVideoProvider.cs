namespace VideoMergeTool.Core.Interfaces;

public interface ICompanionVideoProvider
{
    Task<IReadOnlyList<string>> GetVideosAsync(
        CancellationToken cancellationToken);
}
