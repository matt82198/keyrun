using System.Collections.Generic;
using System.Linq;
using static Keyrun.Content.ContentValidator;

namespace Keyrun.Content;

/// <summary>Content rule moved unchanged from ContentValidator; registered in <see cref="ContentRules"/>.</summary>
internal sealed class MeleeAbilitiesRule : IContentRule
{
    /// <summary>melee-abilities.json (KF1 strike cue): every id names a loaded ability, listed once.</summary>
    public void Check(ContentRegistry reg)
    {
        var seen = new System.Collections.Generic.HashSet<string>(System.StringComparer.Ordinal);
        foreach (string id in reg.MeleeAbilities)
        {
            if (!reg.Abilities.ContainsKey(id))
                Fail("melee-abilities \"" + id + "\" is not an ability");
            if (!seen.Add(id))
                Fail("melee-abilities lists \"" + id + "\" twice");
        }
    }
}
