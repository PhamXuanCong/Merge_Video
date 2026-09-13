namespace VideoMergeTool.Core.Features.RenameVideo.Models;

/// <param name="RenamedEntries">Only the rows that actually renamed a file, in the order they ran.</param>
public sealed record RenameLogBatch(string BatchId, DateTime StartedAt, IReadOnlyList<RenameLogEntry> RenamedEntries);
