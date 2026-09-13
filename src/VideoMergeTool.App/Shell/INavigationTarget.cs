namespace VideoMergeTool.App.Shell;

/// <summary>
/// Implemented by a feature view model that wants to defer work (such as probing external tools)
/// until the user actually opens its page.
/// </summary>
public interface INavigationTarget
{
    /// <summary>Called each time the feature's sidebar entry becomes the selected page.</summary>
    void OnNavigatedTo();
}
