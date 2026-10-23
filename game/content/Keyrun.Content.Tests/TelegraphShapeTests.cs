using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace Keyrun.Content.Tests;

/// <summary>
/// Wrong data shown to a player is P0 (BIBLE s3: the shape plus the colour tell the truth). The rule this file pins down:
/// every enemy script step with a floor telegraph draws a shape the ability can produce (R-TELEGRAPH-1). A target-shaped
/// ability may draw a circle pulse around the point of impact, or a line anchored on the caster (aimed at the target by
/// the caster's yaw, which shows the hit path); it may never draw a cone. A self-shaped ability may draw only a circle. A cone telegraph on a target ability misstates where the hit
/// lands (f-e0650754 lane, drowned.undertow: target ability, 25000 mm cone telegraph).
/// </summary>
public sealed class TelegraphShapeTests
{
    private static readonly ContentRegistry Reg = ContentLoader.Load();

    private static IEnumerable<(EnemyDef Enemy, ScriptStep Step, AbilityDef Ability)> TelegraphedSteps() =>
        from enemy in Reg.Enemies.Values
        from step in enemy.Script
        where step.Telegraph is not null
        let ability = Reg.Abilities[step.Cast]
        select (enemy, step, ability);

    /// <summary>
    /// R-TELEGRAPH-1 allowed-shape table: the telegraph shape a script step may draw, given its ability's shape.
    /// | ability shape | allowed telegraph shapes                                                  |
    /// | target        | circle (any anchor); line only when anchored on the caster (seed.lash)     |
    /// | self          | circle only                                                                |
    /// | cone          | cone (the ability's own shape)                                             |
    /// | line          | line (the ability's own shape)                                             |
    /// | circle        | circle (the ability's own shape)                                           |
    /// Anything else is a telegraph that promises a hit area the ability cannot produce (P0).
    /// </summary>
    private const string AllowedShapeTable =
        "allowed telegraph shapes by ability shape (R-TELEGRAPH-1): "
        + "target -> circle, or line anchored on the caster; "
        + "self -> circle only; "
        + "cone -> cone; line -> line; circle -> circle.";

    private static bool TelegraphMatchesAbility(ScriptStep step, AbilityDef ability)
    {
        string abilityShape = ability.Shape.Kind.ToString().ToLowerInvariant();
        string telegraphShape = step.Telegraph!.Shape.ToString().ToLowerInvariant();
        if (abilityShape == "target")
        {
            return telegraphShape == "circle"
                || (telegraphShape == "line" && (step.Telegraph.Anchor ?? TelegraphAnchor.Caster) == TelegraphAnchor.Caster);
        }
        if (abilityShape == "self")
        {
            return telegraphShape == "circle";
        }
        return telegraphShape == abilityShape;
    }

    [Fact]
    public void Every_enemy_script_telegraph_shape_is_allowed_for_its_ability_shape()
    {
        var offenders = new List<string>();
        int count = 0;
        foreach (var (enemy, step, ability) in TelegraphedSteps())
        {
            count++;
            if (!TelegraphMatchesAbility(step, ability))
            {
                string abilityShape = ability.Shape.Kind.ToString().ToLowerInvariant();
                string telegraphShape = step.Telegraph!.Shape.ToString().ToLowerInvariant();
                string anchor = step.Telegraph.Anchor == TelegraphAnchor.Target ? "target" : "caster";
                offenders.Add(enemy.Id + " -> " + step.Cast + " (ability " + abilityShape + ", telegraph " + telegraphShape
                    + ", anchor " + anchor + ")");
            }
        }

        Assert.True(
            offenders.Count == 0,
            "enemy script telegraphs draw a shape the ability cannot produce (wrong data shown to a player is P0). "
            + AllowedShapeTable + " Offending steps: " + string.Join("; ", offenders));
        // Vacuity guard: the scan must see the shipped telegraphs (the vault, bellfoundry and hall enemies).
        Assert.True(count > 0, "no enemy telegraphs were loaded; the shape rule scanned nothing");
    }
}
