using System.Collections.Generic;
using System.Linq;
using static Keyrun.Content.ContentValidator;

namespace Keyrun.Content;

/// <summary>Content rule moved unchanged from ContentValidator; registered in <see cref="ContentRules"/>.</summary>
internal sealed class HeatOverheatRule : IContentRule
{
    public void Check(ContentRegistry reg)
    {
        HeatOverheatConfig h = reg.Run.HeatOverheat;
        if (!reg.Effects.ContainsKey(h.ChillEffectId))
            Fail("run heatOverheat.chillEffectId \"" + h.ChillEffectId + "\" is not an effect");
        if (h.ChillTicks <= 0) Fail("run heatOverheat.chillTicks must be positive, got " + h.ChillTicks);
    }
}
