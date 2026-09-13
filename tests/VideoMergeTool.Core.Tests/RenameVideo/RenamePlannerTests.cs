using VideoMergeTool.Core.Features.RenameVideo;
using VideoMergeTool.Core.Features.RenameVideo.Models;

namespace VideoMergeTool.Core.Tests.RenameVideo;

public sealed class RenamePlannerTests
{
    [Fact]
    public void PlanSuffixesANameAlreadyUsedByAFileInTheFolder()
    {
        var snapshot = Snapshot(["Clip [1].mp4"], extraEntries: ["Clip #trend.mp4"]);

        var item = Assert.Single(RenamePlanner.Plan(snapshot, "#trend"));

        Assert.Equal("Clip #trend (1).mp4", item.NewName);
        Assert.Equal("Clip #trend.mp4", item.DesiredName);
        Assert.True(item.SuffixAdded);
    }

    [Fact]
    public void PlanNeverGivesTwoVideosInTheSameBatchTheSameName()
    {
        var snapshot = Snapshot(["Clip [1].mp4", "clip [2].mp4", "Clip [3].mp4"]);

        var names = RenamePlanner.Plan(snapshot, "#trend").Select(item => item.NewName).ToList();

        string[] expected = ["Clip #trend.mp4", "clip #trend (1).mp4", "Clip #trend (2).mp4"];
        Assert.Equal(expected, names);
    }

    [Fact]
    public void PlanTreatsASubfolderWithTheTargetNameAsTaken()
    {
        var snapshot = Snapshot(["Clip [1].mp4"], extraEntries: ["Clip.mp4"]);

        Assert.Equal("Clip (1).mp4", Assert.Single(RenamePlanner.Plan(snapshot, string.Empty)).NewName);
    }

    [Fact]
    public void PlanMarksFilesWithNothingToChangeAsUnchanged()
    {
        var snapshot = Snapshot(["No id.mp4", "Has id [9].mp4"]);

        var plan = RenamePlanner.Plan(snapshot, "  ");

        Assert.False(plan[0].HasChange);
        Assert.False(plan[0].IdFound);
        Assert.Contains("Không tìm thấy ID", plan[0].Note);
        Assert.True(plan[1].HasChange);
        Assert.Equal("Has id.mp4", plan[1].NewName);
        Assert.Equal(string.Empty, plan[1].Note);
    }

    private static RenameFolderSnapshot Snapshot(string[] videos, string[]? extraEntries = null) =>
        new(@"C:\videos", videos, [.. videos, .. extraEntries ?? []]);
}
