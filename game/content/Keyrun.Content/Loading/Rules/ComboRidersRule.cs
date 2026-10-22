using System.Collections.Generic;
using System.Linq;
using static Keyrun.Content.ContentValidator;

namespace Keyrun.Content;

/// <summary>Content rule moved unchanged from ContentValidator; registered in <see cref="ContentRules"/>.</summary>
internal sealed class ComboRidersRule : IContentRule
{
    public void Check(ContentRegistry reg)
    {
        foreach (ComboRider rider in reg.ComboRiders.Values)
        {
            string where = "combo rider \"" + rider.Id + "\"";
            if (!reg.Combos.ContainsKey(rider.Id))
                Fail(where + " references unknown combo \"" + rider.Id + "\"");
            CheckOps(rider.Effects, reg, where, requireSetupClass: true, riderOps: true);
        }
    }
}
