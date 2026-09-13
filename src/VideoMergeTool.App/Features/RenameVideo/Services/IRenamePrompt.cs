namespace VideoMergeTool.App.Features.RenameVideo.Services;

/// <summary>
/// Lets the rename view model ask or tell the user something without owning a dialog itself.
/// </summary>
public interface IRenamePrompt
{
    /// <returns><c>true</c> to restore the original names.</returns>
    bool ConfirmUndo(int fileCount, DateTime renamedAt, string folder);

    void ShowError(string message);
}
