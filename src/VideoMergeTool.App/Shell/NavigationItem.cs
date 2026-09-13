namespace VideoMergeTool.App.Shell;

/// <summary>
/// One entry in the sidebar: a display title paired with the feature's view model. The
/// content area's <c>DataTemplate</c> resolves <see cref="ViewModel"/> to its view.
/// </summary>
public sealed record NavigationItem(string Title, object ViewModel);
