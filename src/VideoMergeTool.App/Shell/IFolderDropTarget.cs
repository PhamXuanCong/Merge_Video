namespace VideoMergeTool.App.Shell;

/// <summary>
/// Implemented by a feature view model that wants folders dropped onto the shell window. Only
/// the currently visible feature receives drops.
/// </summary>
public interface IFolderDropTarget
{
    void OnFolderDropped(string folderPath);
}
