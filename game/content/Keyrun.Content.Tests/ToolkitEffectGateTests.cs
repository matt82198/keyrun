using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace Keyrun.Content.Tests;

/// <summary>
/// design/TOOLKIT-CONTRACT.md s2 effect gate (G2.1; Matt 2026-10-22: "one mobility button minimum", "you should die if
/// you're not using defensives, mobility, the whole toolkit"). The three CLASS rules of <see cref="ToolkitRule"/> become
/// asserts here: (1) a movement button moves the caster, by a displace of the caster or a speed effect of at least
/// +5000 bp; (2) a healer's defensive can target an ally (the report's defensive-external code, reused); (3) a movement
/// button with a speed effect also removes a root or a slow. Encounter threats stay in report mode
/// (<see cref="ToolkitRuleTests"/>). Not registered in ContentRules.All: a failing rule there would fail every content
/// load, and the content on main is known to fail these rules.
/// </summary>
public sealed class ToolkitEffectGateTests
{
    private const int MinSpeedBp = ToolkitRule.MobilityMinMovementBp;

    [Fact]
    public void Every_class_movement_button_moves_the_caster()
    {
        ContentRegistry reg = ContentLoader.Load();
        AssertVacuityGuard(reg);

        var failures = new List<string>();
        foreach (ClassDef cls in Classes(reg))
        {
            if (!reg.Abilities.TryGetValue(cls.Abilities.Movement, out AbilityDef? move))
            {
                failures.Add(cls.Abilities.Movement + " (" + cls.Id + "): mobility-moves-caster: movement ability is missing");
                continue;
            }

            List<EffectOp> ops = AllOps(move).ToList();
            bool displaces = ops.OfType<DisplaceOp>().Any(d => d.DistanceMm > 0);
            bool speeds = ops.OfType<ApplyEffectOp>().Any(a => Modifiers(reg, a.EffectId)?.MovementBp >= MinSpeedBp);
            if (!displaces && !speeds)
                failures.Add(move.Id + " (" + cls.Id + "): mobility-moves-caster: no displace of the caster and no speed effect of +" + MinSpeedBp + " bp");
        }

        Assert.True(failures.Count == 0, Report("movement buttons that do not move the caster", failures));
    }

    [Fact]
    public void Every_healer_defensive_can_target_an_ally()
    {
        ContentRegistry reg = ContentLoader.Load();
        AssertVacuityGuard(reg);

        var failures = ToolkitRule.ClassGaps(reg)
            .Where(g => g.Code == "defensive-external")
            .Select(g => reg.Classes[g.Id].Abilities.Defensive + " (" + g.Id + "): defensive-external: " + g.Detail)
            .ToList();

        Assert.True(failures.Count == 0, Report("healer defensives that cannot target an ally", failures));
    }

    [Fact]
    public void A_movement_button_with_a_speed_effect_also_frees_root_and_slow()
    {
        ContentRegistry reg = ContentLoader.Load();
        AssertVacuityGuard(reg);

        var failures = new List<string>();
        foreach (ClassDef cls in Classes(reg))
        {
            if (!reg.Abilities.TryGetValue(cls.Abilities.Movement, out AbilityDef? move)) continue;

            List<EffectOp> ops = AllOps(move).ToList();
            bool speeds = ops.OfType<ApplyEffectOp>().Any(a => Modifiers(reg, a.EffectId)?.MovementBp >= MinSpeedBp);
            if (!speeds) continue;

            bool frees = ops.OfType<RemoveEffectOp>().Any(r => r.EffectId == "*" || Modifiers(reg, r.EffectId)?.MovementBp < 0)
                || ops.OfType<DisplaceOp>().Any(d => d.FreesBinds);
            if (!frees)
                failures.Add(move.Id + " (" + cls.Id + "): mobility-frees-binds: speed effect but no root or slow removed");
        }

        Assert.True(failures.Count == 0, Report("movement buttons with a speed effect that free no root or slow", failures));
    }

    private static void AssertVacuityGuard(ContentRegistry reg)
    {
        Assert.True(reg.Classes.Count >= 3, "vacuity guard: classes examined " + reg.Classes.Count);
    }

    private static IEnumerable<ClassDef> Classes(ContentRegistry reg) => reg.Classes.Values.OrderBy(c => c.Id, StringComparer.Ordinal);

    private static IEnumerable<EffectOp> AllOps(AbilityDef a) =>
        a.Effects.Concat(a.Variants is null ? Enumerable.Empty<EffectOp>() : a.Variants.Values.SelectMany(v => v));

    private static EffectModifiers? Modifiers(ContentRegistry reg, string effectId) =>
        reg.Effects.TryGetValue(effectId, out EffectDef? e) ? e.Modifiers : null;

    private static string Report(string title, IReadOnlyList<string> failures) =>
        title + " (" + failures.Count + "):\n  " + string.Join("\n  ", failures);
}
