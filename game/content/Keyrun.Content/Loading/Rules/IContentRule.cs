namespace Keyrun.Content;

/// <summary>
/// One content invariant over a loaded <see cref="ContentRegistry"/>. A new rule is a new file under Loading/Rules/
/// plus one line in <see cref="ContentRules.All"/>; <see cref="ContentValidator.Validate"/> runs that list in order.
/// </summary>
public interface IContentRule
{
    /// <summary>Checks <paramref name="reg"/>.</summary>
    /// <param name="reg">A loaded registry.</param>
    /// <exception cref="ContentLoadException">The first violated invariant.</exception>
    void Check(ContentRegistry reg);
}
