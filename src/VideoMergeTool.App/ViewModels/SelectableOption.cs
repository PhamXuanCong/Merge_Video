namespace VideoMergeTool.App.ViewModels;

/// <summary>
/// Pairs an option value with the label shown in a combo box, so the UI never has to display a
/// raw enum name such as "RandomWithoutImmediateRepeat".
/// </summary>
public sealed record SelectableOption<T>(T Value, string Label)
{
    public override string ToString() => Label;
}
