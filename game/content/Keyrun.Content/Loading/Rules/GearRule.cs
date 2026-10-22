namespace Keyrun.Content;

/// <summary>Runs <see cref="GearValidator.Check"/> at its old place in the validation order.</summary>
internal sealed class GearRule : IContentRule
{
    public void Check(ContentRegistry reg) => GearValidator.Check(reg);
}
