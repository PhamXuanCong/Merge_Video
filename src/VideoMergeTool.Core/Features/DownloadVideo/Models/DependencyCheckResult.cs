namespace VideoMergeTool.Core.Features.DownloadVideo.Models;

public sealed record DependencyInfo(
    string Name,
    string Path,
    bool IsAvailable,
    string? Version,
    string? Error);

public sealed record DependencyCheckResult(IReadOnlyList<DependencyInfo> Dependencies)
{
    public bool IsSuccess => Dependencies.All(static dependency => dependency.IsAvailable);

    public IReadOnlyList<DependencyInfo> Missing =>
        Dependencies.Where(static dependency => !dependency.IsAvailable).ToArray();
}
