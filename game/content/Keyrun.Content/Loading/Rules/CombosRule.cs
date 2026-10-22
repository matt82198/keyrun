using System.Collections.Generic;
using System.Linq;
using static Keyrun.Content.ContentValidator;

namespace Keyrun.Content;

/// <summary>Content rule moved unchanged from ContentValidator; registered in <see cref="ContentRules"/>.</summary>
internal sealed class CombosRule : IContentRule
{
    public void Check(ContentRegistry reg)
    {
        foreach (ComboEntry combo in reg.Combos.Values)
        {
            string where = "combo \"" + combo.Id + "\"";
            if (combo.SetupClass != BossPseudoClass && !reg.Classes.ContainsKey(combo.SetupClass))
                Fail(where + " references unknown setupClass \"" + combo.SetupClass + "\"");
            if (combo.PayoffClass != AnyOtherClass && !reg.Classes.ContainsKey(combo.PayoffClass))
                Fail(where + " references unknown payoffClass \"" + combo.PayoffClass + "\"");
            if (combo.PayoffClass == combo.SetupClass)
                Fail(where + " has matching setupClass/payoffClass \"" + combo.SetupClass + "\"");
            CheckOps(combo.Effects, reg, where, requireSetupClass: true);
        }
    }
}
