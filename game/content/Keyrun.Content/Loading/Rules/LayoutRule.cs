namespace Keyrun.Content;

/// <summary>Runs <see cref="LayoutValidator.Check"/> at its old place in the validation order.</summary>
internal sealed class LayoutRule : IContentRule
{
    public void Check(ContentRegistry reg) => LayoutValidator.Check(reg);
}
