using System;
using System.Collections.Generic;
using System.Linq;

namespace Keyrun.Content;

/// <summary>
/// gear.json invariants (BIBLE section 7 "Five slots", "Tier is power and it is capped at 10"). An unknown slot never
/// reaches here: the strict enum converter fails the parse. Runs inside <see cref="ContentValidator.Validate"/>.
/// </summary>
public static class GearValidator
{
    /// <summary>Largest damageDoneBp any one item may reach at tier 10 (+100%).</summary>
    public const long MaxDamageDoneBp = 10000;

    /// <summary>Most negative damageTakenBp any one item may reach at tier 10 (-50%).</summary>
    public const long MinDamageTakenBp = -5000;

    /// <summary>Checks the gear table.</summary>
    /// <param name="reg">The registry.</param>
    /// <exception cref="ContentLoadException">The first violated invariant.</exception>
    public static void Check(ContentRegistry reg)
    {
        ArgumentNullException.ThrowIfNull(reg);
        var seen = new HashSet<(GearSlot, int)>();
        foreach (GearItemDef item in reg.Gear.Values)
        {
            string at = "content: gear item \"" + item.Id + "\" ";
            if (!Enum.IsDefined(item.Slot)) throw new ContentLoadException(at + "has an unknown slot");
            if (item.Tier < 1 || item.Tier > GearItems.MaxTier)
                throw new ContentLoadException(at + "tier " + item.Tier + " is outside 1.." + GearItems.MaxTier);
            if (!seen.Add((item.Slot, item.Tier)))
                throw new ContentLoadException(at + "is more than one item for slot " + GearFacets.SlotId(item.Slot) + " at tier " + item.Tier);
            CheckStats(at + "stats", item.Stats);
            CheckStats(at + "perTier", item.PerTier);
            long steps = GearItems.MaxTier - item.Tier;
            CheckStats(at + "at tier " + GearItems.MaxTier, new GearStatsDef
            {
                DamageDoneBp = item.Stats.DamageDoneBp + (steps * item.PerTier.DamageDoneBp),
                DamageTakenBp = item.Stats.DamageTakenBp + (steps * item.PerTier.DamageTakenBp),
            });
        }
        foreach (GearSlot slot in GearFacets.SlotOrder)
        {
            if (!reg.Gear.Values.Any(g => g.Slot == slot && g.Tier == 1))
                throw new ContentLoadException("content: gear.json has no tier-1 base item for slot " + GearFacets.SlotId(slot));
        }
    }

    private static void CheckStats(string at, GearStatsDef s)
    {
        if (s.DamageDoneBp < 0 || s.DamageDoneBp > MaxDamageDoneBp)
            throw new ContentLoadException(at + " damageDoneBp " + s.DamageDoneBp + " is outside 0.." + MaxDamageDoneBp);
        if (s.DamageTakenBp > 0 || s.DamageTakenBp < MinDamageTakenBp)
            throw new ContentLoadException(at + " damageTakenBp " + s.DamageTakenBp + " is outside " + MinDamageTakenBp + "..0");
    }
}
