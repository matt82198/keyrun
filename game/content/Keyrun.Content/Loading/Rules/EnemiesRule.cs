using System.Collections.Generic;
using System.Linq;
using static Keyrun.Content.ContentValidator;

namespace Keyrun.Content;

/// <summary>Content rule moved unchanged from ContentValidator; registered in <see cref="ContentRules"/>.</summary>
internal sealed class EnemiesRule : IContentRule
{
    public void Check(ContentRegistry reg)
    {
        var bars = new HashSet<string>(reg.Enemies.Values.SelectMany(e => e.BreakBars ?? new ValueList<BreakBarDef>()).Select(b => b.Id), System.StringComparer.Ordinal);
        foreach (EnemyDef enemy in reg.Enemies.Values)
        {
            string where = "enemy \"" + enemy.Id + "\"";
            foreach (string id in enemy.Abilities)
                if (!reg.Abilities.ContainsKey(id)) Fail(where + " references unknown ability \"" + id + "\"");
            foreach (ScriptStep step in enemy.Script)
            {
                if (!reg.Abilities.TryGetValue(step.Cast, out AbilityDef? cast)) Fail(where + " script references unknown ability \"" + step.Cast + "\"");
                else if (step.Telegraph?.SpansCast == true && cast.CastTicks <= 0)
                    Fail(where + " script step \"" + step.Cast + "\" telegraph spansCast needs a cast with castTicks > 0 (has " + cast.CastTicks + ")");
                // TELEGRAPH-ANCHOR (BIBLE s3, never a lie about where): a cone or line keeps the caster's facing, so
                // drawn from the target it would mark ground the cast never reaches; only a circle may anchor there.
                if (step.Telegraph is { Anchor: TelegraphAnchor.Target, Shape: not TelegraphShape.Circle } anchored)
                    Fail(where + " script step \"" + step.Cast + "\" telegraph anchor \"target\" needs a circle (has " + anchored.Shape.ToString().ToLowerInvariant() + ")");
            }
            foreach (BreakBarDef bar in enemy.BreakBars ?? new ValueList<BreakBarDef>())
                CheckOps(bar.OnEmpty, reg, where + " breakBar \"" + bar.Id + "\"", requireSetupClass: true);
            if (enemy.BreakBarId is not null && !bars.Contains(enemy.BreakBarId))
                Fail(where + " references unknown breakBar \"" + enemy.BreakBarId + "\"");
        }
    }
}
