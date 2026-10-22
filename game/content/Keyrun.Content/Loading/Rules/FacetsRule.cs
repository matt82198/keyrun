using System.Collections.Generic;
using System.Linq;
using static Keyrun.Content.ContentValidator;

namespace Keyrun.Content;

/// <summary>Content rule moved unchanged from ContentValidator; registered in <see cref="ContentRules"/>.</summary>
internal sealed class FacetsRule : IContentRule
{
    public void Check(ContentRegistry reg)
    {
        foreach (FacetDef facet in reg.Facets.Values)
        {
            string where = "facet \"" + facet.Id + "\"";
            if (!reg.Abilities.ContainsKey(facet.AbilityId))
                Fail(where + " references unknown ability \"" + facet.AbilityId + "\"");
            CheckOps(facet.Patch.Effects, reg, where, requireSetupClass: true);
            CheckOps(facet.Patch.EffectsAppend, reg, where + " effectsAppend", requireSetupClass: false);
        }
    }
}
