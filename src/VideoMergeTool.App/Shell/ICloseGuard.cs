namespace VideoMergeTool.App.Shell;

/// <summary>
/// Implemented by a feature view model that can still be busy (a background run in progress)
/// when the shell window is closing. The shell checks every navigation item for this contract
/// instead of hardcoding shutdown behaviour to one feature.
/// </summary>
public interface ICloseGuard
{
    bool IsBusy { get; }

    /// <summary>
    /// Lets the feature show its own confirmation dialog (wording specific to what it does).
    /// </summary>
    /// <returns><c>true</c> if the shell should stop the run and proceed with closing.</returns>
    Task<bool> ConfirmCloseAsync();

    /// <summary>Cancels the feature's running work and waits for it to unwind.</summary>
    Task CancelAndWaitAsync(TimeSpan timeout);
}
