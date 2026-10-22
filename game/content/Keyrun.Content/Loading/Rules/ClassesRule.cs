using System.Collections.Generic;
using System.Linq;
using static Keyrun.Content.ContentValidator;

namespace Keyrun.Content;

/// <summary>Content rule moved unchanged from ContentValidator; registered in <see cref="ContentRules"/>.</summary>
internal sealed class ClassesRule : IContentRule
{
    public void Check(ContentRegistry reg)
    {
        foreach (ClassDef cls in reg.Classes.Values)
        {
            ClassAbilitySlots s = cls.Abilities;
            var slots = new (string Slot, string Id)[]
            {
                ("rot1", s.Rot1), ("rot2", s.Rot2), ("rot3", s.Rot3), ("rot4", s.Rot4), ("rot5", s.Rot5),
                ("interrupt", s.Interrupt), ("defensive", s.Defensive), ("utility", s.Utility), ("movement", s.Movement),
            };
            foreach ((string slot, string id) in slots)
                if (!reg.Abilities.ContainsKey(id))
                    Fail("class \"" + cls.Id + "\" slot \"" + slot + "\" references unknown ability \"" + id + "\"");
            foreach (TalentFork fork in cls.Talents)
            {
                if (!reg.Abilities.ContainsKey(fork.AbilityId))
                    Fail("class \"" + cls.Id + "\" talent fork references unknown ability \"" + fork.AbilityId + "\"");
                foreach (TalentOption option in fork.Options)
                {
                    string where = "class \"" + cls.Id + "\" talent \"" + option.Id + "\"";
                    CheckOps(option.Patch.Effects, reg, where, requireSetupClass: true);
                    foreach (KeyValuePair<string, ValueList<EffectOp>> variant in option.Patch.Variants ?? new ValueMap<ValueList<EffectOp>>())
                        CheckOps(variant.Value, reg, where + " variant \"" + variant.Key + "\"", requireSetupClass: true);
                }
            }
        }
    }
}
