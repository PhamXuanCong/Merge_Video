namespace VideoMergeTool.App.Features.MergeVideo.Services;

/// <summary>
/// Lets the view model ask the user to confirm something without owning a dialog itself.
/// </summary>
public interface IUserPrompt
{
    /// <summary>
    /// Confirms a run that will delete the original input videos once each output is verified.
    /// </summary>
    /// <returns><c>true</c> to go ahead.</returns>
    bool ConfirmSourceDeletion(int videoCount, string inputFolder);

    /// <summary>
    /// Confirms closing the window while a run is still going, which stops that run.
    /// </summary>
    /// <returns><c>true</c> to close.</returns>
    bool ConfirmExitWhileBusy(int unfinishedCount);
}
