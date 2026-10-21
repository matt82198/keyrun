using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace Keyrun.Content.Tests;

/// <summary>
/// design/BIBLE.md section 3: "Every dangerous enemy action is a cast with a visible bar and a floor
/// telegraph (circle, cone, line) drawn ahead of resolution." The rule this file pins down:
/// a scripted step is DANGEROUS when its enemy's role is boss, elite or lieutenant (the named
/// enemies section 9 builds a pull or the boss around, never ordinary melee or caster trash) and the
/// step's ability deals damage (a <see cref="DamageOp"/>). Every dangerous step must have
/// <c>castTicks &gt; 0</c> (the visible bar), a telegraph with <c>leadTicks &gt; 0</c> (drawn ahead),
/// and a telegraph that reaches at least as far as the ability can hit (its range for a
/// target-shaped cast, its shape radius otherwise), so the floor shape never under-reports the danger.
/// </summary>
public sealed class DangerousCastTelegraphTests
{
    private static readonly ContentRegistry Reg = ContentLoader.Load();

    private static readonly EnemyRole[] DangerousRoles = { EnemyRole.Boss, EnemyRole.Elite, EnemyRole.Lieutenant };

    private static IEnumerable<(EnemyDef Enemy, ScriptStep Step, AbilityDef Ability)> DangerousSteps() =>
        from enemy in Reg.Enemies.Values
        where DangerousRoles.Contains(enemy.Role)
        from step in enemy.Script
        let ability = Reg.Abilities[step.Cast]
        where ability.Effects.Any(op => op is DamageOp)
        select (enemy, step, ability);

    private static int Reach(AbilityDef a) =>
        a.Shape.Kind == ShapeKind.Target ? a.Range : a.Shape.Radius ?? a.Shape.Length ?? 0;

    [Fact]
    public void Every_dangerous_boss_elite_or_lieutenant_cast_has_a_bar_and_a_floor_telegraph()
    {
        var failures = new List<string>();
        int count = 0;
        foreach (var (enemy, step, ability) in DangerousSteps())
        {
            count++;
            string where = enemy.Id + " -> " + ability.Id;
            if (ability.CastTicks <= 0) failures.Add(where + ": castTicks " + ability.CastTicks + " (no cast bar)");
            if (step.Telegraph is null)
            {
                failures.Add(where + ": no floor telegraph");
                continue;
            }
            if (step.Telegraph.LeadTicks <= 0) failures.Add(where + ": telegraph leadTicks " + step.Telegraph.LeadTicks);
            int drawn = step.Telegraph.Radius ?? step.Telegraph.Length ?? 0;
            if (drawn < Reach(ability)) failures.Add(where + ": telegraph reach " + drawn + " < ability reach " + Reach(ability));
        }

        Assert.True(failures.Count == 0, string.Join(System.Environment.NewLine, failures));
        // Vacuity guard: seed.slam, seed.lash (KF3-SEED-ENGAGE), hollowWarden.slam, hollowWarden.cleave, twinA and twinB twins.slam.
        Assert.Equal(6, count);
    }

    [Fact]
    public void The_bosss_Lock_is_a_cast_with_a_bar_and_a_floor_circle_that_spans_it()
    {
        // LOCK-TELEGRAPH (f-dfafcec6): the Lock deals no damage, so the rule above never saw it, and it shipped with
        // castTicks 0 and no telegraph: the boss's biggest mechanic began with no bar and nothing on the floor. The one
        // boss script step that casts the setup:lock ability must have a cast bar and a circle that spans the cast.
        var lockStep = Assert.Single(
            from enemy in Reg.Enemies.Values
            where enemy.Role == EnemyRole.Boss
            from step in enemy.Script
            let ability = Reg.Abilities[step.Cast]
            where ability.Tags.Contains("setup:lock")
            select (enemy, step, ability));
        string at = lockStep.enemy.Id + " -> " + lockStep.ability.Id;
        Assert.True(lockStep.ability.CastTicks > 0, at + ": castTicks " + lockStep.ability.CastTicks + " (no cast bar)");
        var telegraph = lockStep.step.Telegraph;
        Assert.True(telegraph is not null, at + ": no floor telegraph");
        Assert.Equal(TelegraphShape.Circle, telegraph!.Shape);
        Assert.True((telegraph.Radius ?? 0) > 0, at + ": telegraph radius " + telegraph.Radius);
        Assert.True(telegraph.SpansCast == true, at + ": telegraph does not span its cast");
        Assert.True(telegraph.LeadTicks >= lockStep.ability.CastTicks, at + ": lead " + telegraph.LeadTicks + " < cast " + lockStep.ability.CastTicks);
    }

    [Fact]
    public void Every_dangerous_cast_telegraph_spans_the_cast()
    {
        // SLAM-TELEGRAPH: the floor shape stays down for the whole cast bar and resolves when the cast lands
        // (seed.slam, seed.lash, hollowWarden.slam, hollowWarden.cleave, twinA and twinB twins.slam).
        var slams = DangerousSteps().ToList();
        Assert.Equal(6, slams.Count);
        Assert.All(slams, s =>
        {
            Assert.True(s.Step.Telegraph?.SpansCast == true, s.Enemy.Id + " -> " + s.Ability.Id + " telegraph does not span its cast");
            Assert.True(s.Step.Telegraph!.LeadTicks >= s.Ability.CastTicks, s.Enemy.Id + ": lead " + s.Step.Telegraph.LeadTicks + " < cast " + s.Ability.CastTicks);
        });
    }
}
