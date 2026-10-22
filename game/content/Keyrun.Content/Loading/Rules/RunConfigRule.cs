using System.Collections.Generic;
using System.Linq;
using static Keyrun.Content.ContentValidator;

namespace Keyrun.Content;

/// <summary>Content rule moved unchanged from ContentValidator; registered in <see cref="ContentRules"/>.</summary>
internal sealed class RunConfigRule : IContentRule
{
    public void Check(ContentRegistry reg)
    {
        RunConfig run = reg.Run;
        if (run.Movement.SpeedMmPerTick <= 0) Fail("run movement.speedMmPerTick must be positive, got " + run.Movement.SpeedMmPerTick);
        if (run.Movement.RadiusMm <= 0) Fail("run movement.radiusMm must be positive, got " + run.Movement.RadiusMm);
        AffixesConfig a = run.Affixes;
        if (a.OneAffixKey <= 0 || a.BothAffixKey <= a.OneAffixKey)
            Fail("run affixes must have 0 < oneAffixKey < bothAffixKey, got " + a.OneAffixKey + " and " + a.BothAffixKey);
        if (a.ClauseStartKey <= 0) Fail("run affixes.clauseStartKey must be positive, got " + a.ClauseStartKey);
        foreach ((string name, AffixScaleConfig s) in new[] { ("fortified", a.Fortified), ("tyrannical", a.Tyrannical) })
        {
            if (s.HealthBp < 0 || s.DamageBp < 0)
                Fail("run affixes." + name + " healthBp and damageBp must be non-negative, got " + s.HealthBp + " and " + s.DamageBp);
        }
        ArenaBounds b = run.ArenaBounds;
        if (b.MinX >= b.MaxX || b.MinZ >= b.MaxZ)
            Fail("run arenaBounds must have minX < maxX and minZ < maxZ, got x " + b.MinX + ".." + b.MaxX + " z " + b.MinZ + ".." + b.MaxZ);
    }
}
