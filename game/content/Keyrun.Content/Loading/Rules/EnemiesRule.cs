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
                // SCRIPT-GATE (D66): exactly one condition, each one the sim can judge.
                if (step.When is { } gate)
                {
                    string at = where + " script step \"" + step.Cast + "\" when";
                    if ((gate.HasEffect is null) == (gate.HealthBelowBp is null))
                        Fail(at + " must set exactly one of hasEffect and healthBelowBp");
                    if (gate.HasEffect is { } effectId && !reg.Effects.ContainsKey(effectId))
                        Fail(at + " hasEffect references unknown effect \"" + effectId + "\"");
                    if (gate.HealthBelowBp is { } bp && (bp <= 0 || bp >= 10000))
                        Fail(at + " healthBelowBp must be in 1..9999, got " + bp);
                }
            }
            foreach (BreakBarDef bar in enemy.BreakBars ?? new ValueList<BreakBarDef>())
                CheckOps(bar.OnEmpty, reg, where + " breakBar \"" + bar.Id + "\"", requireSetupClass: true);
            if (enemy.BreakBarId is not null && !bars.Contains(enemy.BreakBarId))
                Fail(where + " references unknown breakBar \"" + enemy.BreakBarId + "\"");
        }
    }
}
