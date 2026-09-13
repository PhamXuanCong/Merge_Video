using VideoMergeTool.Core.Features.RenameVideo.Models;

namespace VideoMergeTool.Core.Features.RenameVideo;

public static class RenamePlanner
{
    /// <summary>
    /// Works out every new name without touching the disk, so the preview can be rebuilt on each
    /// keystroke in the hashtag box.
    /// </summary>
    /// <remarks>
    /// A name counts as taken if anything in the folder has it now, even a video that is about to be
    /// renamed away, or if an earlier video in this batch was given it. That can add a suffix that
    /// was not strictly needed, but it can never make one rename land on another.
    /// </remarks>
    public static IReadOnlyList<RenamePlanItem> Plan(RenameFolderSnapshot snapshot, string? hashtagText)
    {
        var hashtags = VideoNameRules.NormalizeHashtags(hashtagText);
        var taken = new HashSet<string>(snapshot.EntryNames, StringComparer.OrdinalIgnoreCase);
        var plan = new List<RenamePlanItem>(snapshot.VideoFileNames.Count);

        foreach (var fileName in snapshot.VideoFileNames)
        {
            var desiredName = VideoNameRules.ComposeName(fileName, hashtags, out var idFound);

            if (string.Equals(desiredName, fileName, StringComparison.Ordinal))
            {
                plan.Add(new RenamePlanItem(fileName, desiredName, desiredName, idFound));
                continue;
            }

            var newName = VideoNameRules.MakeUnique(desiredName, taken.Contains);
            taken.Add(newName);
            plan.Add(new RenamePlanItem(fileName, desiredName, newName, idFound));
        }

        return plan;
    }
}
