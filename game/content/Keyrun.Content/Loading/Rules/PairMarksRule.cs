using System.Collections.Generic;
using System.Linq;
using static Keyrun.Content.ContentValidator;

namespace Keyrun.Content;

/// <summary>Content rule moved unchanged from ContentValidator; registered in <see cref="ContentRules"/>.</summary>
internal sealed class PairMarksRule : IContentRule
{
    /// <summary>run.json pairMarks (BIBLE section 9.6 item 2): both marks name real effects; every number is positive.</summary>
    public void Check(ContentRegistry reg)
    {
        PairMarksConfig m = reg.Run.PairMarks;
        if (!reg.Effects.ContainsKey(m.SpreadEffectId))
            Fail("run pairMarks.spreadEffectId \"" + m.SpreadEffectId + "\" is not an effect");
        if (!reg.Effects.ContainsKey(m.StackEffectId))
            Fail("run pairMarks.stackEffectId \"" + m.StackEffectId + "\" is not an effect");
        if (m.SpreadRadiusMm <= 0) Fail("run pairMarks.spreadRadiusMm must be positive, got " + m.SpreadRadiusMm);
        if (m.SpreadDamage <= 0) Fail("run pairMarks.spreadDamage must be positive, got " + m.SpreadDamage);
        if (m.StackRadiusMm <= 0) Fail("run pairMarks.stackRadiusMm must be positive, got " + m.StackRadiusMm);
        if (m.StackDamage <= 0) Fail("run pairMarks.stackDamage must be positive, got " + m.StackDamage);
    }
}
