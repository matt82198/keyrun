using System.Collections.Generic;
using System.Linq;
using static Keyrun.Content.ContentValidator;

namespace Keyrun.Content;

/// <summary>Content rule moved unchanged from ContentValidator; registered in <see cref="ContentRules"/>.</summary>
internal sealed class AbilitiesRule : IContentRule
{
    public void Check(ContentRegistry reg)
    {
        foreach (AbilityDef ability in reg.Abilities.Values)
        {
            string where = "ability \"" + ability.Id + "\"";
            CheckOps(ability.Effects, reg, where, requireSetupClass: true);
            foreach (KeyValuePair<string, ValueList<EffectOp>> variant in ability.Variants ?? new ValueMap<ValueList<EffectOp>>())
                CheckOps(variant.Value, reg, where + " variant \"" + variant.Key + "\"", requireSetupClass: true);
        }
        foreach (EffectDef effect in reg.Effects.Values)
            if (effect.Periodic is not null)
                CheckOps(new[] { effect.Periodic.Op }, reg, "effect \"" + effect.Id + "\" periodic", requireSetupClass: true);
    }
}
