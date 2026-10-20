using System;
using System.Collections.Generic;
using System.Linq;
using Keyrun.Sim.Core;
using Keyrun.Sim.Encounter;
using Keyrun.Sim.Runs;
using Xunit;
using Xunit.Abstractions;
using C = Keyrun.Content;
using S = Keyrun.Sim.Combat;

namespace Keyrun.Bots.Tests;

/// <summary>
/// High keys stay survivable through mitigation, not one-shots (Matt 2026-10-20: "as damage scales this will turn into
/// a one shot, so abilities and damage reduction stuff needs to be considered"). At the highest key (BIBLE s8: keys
/// 2-20) the hardest single enemy hit in the shipped content, scaled by that key's affixes (game/content/data/run.json),
/// is driven through the real damage pipeline (<see cref="S.Mitigation.DealDamage"/>) against each roster seat wearing
/// the floor of key 20's gear band (<see cref="Rewards.RollRewardChoices(long,int,RunResult,IReadOnlyList{string})"/>).
/// With the class's E defensive up, no seat dies to it; with none, the tank still survives it.
/// </summary>
public sealed class HighKeySurvivabilityTests
{
    private const int MaxKey = 20;

    // game/src/Host/HostRunSetup.cs Roster (TankMaxHealth 400, SupportMaxHealth 250); that Godot-only assembly is not
    // referenced by this test project, so the seat health is restated here.
    private static readonly (string ClassId, ActorRole Role, long MaxHealth)[] Seats =
    {
        ("warden", ActorRole.Tank, 400),
        ("chanter", ActorRole.Healer, 250),
        ("cinder", ActorRole.Damage, 250),
    };

    private static readonly C.ContentRegistry Reg = C.ContentLoader.Load();

    private readonly ITestOutputHelper _out;

    public HighKeySurvivabilityTests(ITestOutputHelper output) => _out = output;

    [Fact]
    public void Hardest_hit_at_the_top_key_never_one_shots_a_defended_seat_and_never_one_shots_the_tank()
    {
        var (hitId, raw) = HardestHit(MaxKey);
        var tier = GearFloorTier(MaxKey);
        var defs = Reg.Effects.Values.ToDictionary(e => e.Id, ToSim, StringComparer.Ordinal);
        var failures = new List<string>();
        foreach (var (classId, role, maxHealth) in Seats)
        {
            var defensiveEffect = DefensiveEffectId(classId);
            var bare = Hit(classId, role, maxHealth, tier, raw, defs, null);
            var defended = Hit(classId, role, maxHealth, tier, raw, defs, defensiveEffect);
            _out.WriteLine(
                $"key {MaxKey} {hitId} raw {raw} vs {classId} ({role}) maxHealth {maxHealth} gear t{tier} " +
                $"bare: dr {bare.DrBp} bp dmg {bare.Damage} died {bare.Died} | {defensiveEffect}: dr {defended.DrBp} bp dmg {defended.Damage} died {defended.Died}");
            if (defended.Died)
            {
                failures.Add($"{classId} dies to {hitId} ({defended.Damage} >= {maxHealth}) with {defensiveEffect} up");
            }
            if (role == ActorRole.Tank && bare.Died)
            {
                failures.Add($"tank {classId} dies to {hitId} ({bare.Damage} >= {maxHealth}) with no defensive");
            }
        }
        Assert.True(failures.Count == 0, string.Join("; ", failures));
    }

    private readonly record struct HitOutcome(long DrBp, long Damage, bool Died);

    private static HitOutcome Hit(string classId, ActorRole role, long maxHealth, int tier, long raw, IReadOnlyDictionary<string, S.EffectDef> defs, string? effectId)
    {
        var target = S.Actors.Create(1, ActorKind.Player, classId, role, Vec3.Zero, maxHealth, 100);
        var gear = C.GearFacets.SlotOrder
            .Select(slot => C.GearItems.StatsAt(Reg.Gear, slot, tier))
            .Aggregate(default(GearStats), (sum, s) => sum + new GearStats(s.DamageDoneBp, s.DamageTakenBp));
        GearPower.Apply(target, gear);
        if (effectId is not null)
        {
            S.Effects.ApplyEffectInstance(target, defs[effectId], target.Id, 100, 1, 0);
        }
        var source = S.Actors.Create(900, ActorKind.Enemy, "enemy", ActorRole.None, Vec3.Zero, 1000, 0);
        var drBp = S.Effects.AggregateModifiers(target, defs).DamageTakenBp + target.GearDamageTakenBp;
        var r = S.Mitigation.DealDamage(1, source, target, raw, defs, _ => { }, source.Id);
        return new HitOutcome(drBp, r.ModifiedAmount, r.Died);
    }

    /// <summary>The largest single damage op any enemy casts, after its side's affix at <paramref name="key"/>.</summary>
    private static (string Id, long Amount) HardestHit(int key)
    {
        var table = new AffixTable(
            new AffixScale(Reg.Run.Affixes.Fortified.HealthBp, Reg.Run.Affixes.Fortified.DamageBp),
            new AffixScale(Reg.Run.Affixes.Tyrannical.HealthBp, Reg.Run.Affixes.Tyrannical.DamageBp),
            Reg.Run.Affixes.OneAffixKey,
            Reg.Run.Affixes.BothAffixKey);
        var best = (Id: "", Amount: 0L);
        foreach (var rotation in new[] { 0, 1 })
        {
            var affixes = Affixes.AffixesFor(table, key, rotation);
            foreach (var enemy in Reg.Enemies.Values)
            {
                var bossSide = enemy.Role == C.EnemyRole.Boss || enemy.BreakBarId is not null;
                var bp = Affixes.DamageBp(table, affixes, bossSide);
                foreach (var id in enemy.Abilities.Concat(enemy.Script.Select(s => s.Cast)).Distinct(StringComparer.Ordinal))
                {
                    var scaled = Affixes.ScaleDamage(ForkParityTests.ToSim(Reg.Abilities[id]), bp);
                    foreach (var d in scaled.Effects.OfType<S.DamageOp>())
                    {
                        if (d.Amount > best.Amount)
                        {
                            best = (id, d.Amount);
                        }
                    }
                }
            }
        }
        Assert.True(best.Amount > 0, "no enemy damage op found");
        return best;
    }

    /// <summary>The lowest tier key <paramref name="key"/>'s end-of-run choices offer (the floor of its gear band).</summary>
    private static int GearFloorTier(int key)
    {
        var slots = Rewards.GearSlots.ToList();
        return Enumerable.Range(1, 200)
            .SelectMany(seed => Rewards.RollRewardChoices(seed, key, RunResult.Timed, slots))
            .Min(i => i.Tier);
    }

    /// <summary>The effect the class's E defensive puts on its caster (content: the defensive ability's applyEffect).</summary>
    private static string DefensiveEffectId(string classId)
    {
        var ability = ForkParityTests.ToSim(Reg.Abilities[Reg.Classes[classId].Abilities.Defensive]);
        var apply = ability.Effects.OfType<S.ApplyEffectOp>().FirstOrDefault();
        Assert.True(apply is not null, $"{classId}'s defensive applies no effect");
        return apply!.EffectId;
    }

    private static S.EffectDef ToSim(C.EffectDef e) => new(
        e.Id,
        e.MaxStacks,
        e.DispellableTag,
        new S.EffectModifiers(e.Modifiers.DamageDoneBp, e.Modifiers.DamageTakenBp, e.Modifiers.HasteBp, e.Modifiers.MovementBp, e.Modifiers.Absorb));
}
