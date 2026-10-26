using System.Collections.Generic;
using Keyrun.Sim.Combat;
using Keyrun.Sim.Core;
using Xunit;
using static Keyrun.Sim.Tests.CombatCastTestKit;
using static Keyrun.Sim.Tests.CombatTestKit;

namespace Keyrun.Sim.Tests;

/// <summary>
/// f-fe32ef35 (Matt 2026-10-25, ruling seq 46): "defensives are never 100% invulnerabilities, that would need to be a
/// major CD". R-DEFCAP-1 floors a player's total damageTakenBp at -7500 for every effect alike; this suite pins the
/// exception. An effect applied by a MAJOR cooldown may take the hit below the floor; routine defensives (short
/// cooldowns, active mitigation) and any stack of them stay floored at -7500.
/// <para>
/// WoW precedent (R-WOW-OPUS-1): tank majors sit on 2 to 3 minute cooldowns (Ardent Defender 120 s, Shield Wall
/// 180 s, Icebound Fortitude 180 s, Survival Instincts 180 s); routine mitigation (Shield Block, Ironfur, Ignore
/// Pain) is up every few seconds and never makes a one-shot ignorable. Threshold: an ability with
/// <c>CooldownTicks &gt;= 2400</c> (120 s at 20 Hz, the shortest tank major) is major. Every current kit defensive is
/// 120 to 300 ticks (6 to 15 s), so none qualifies.
/// </para>
/// </summary>
public sealed class MajorCooldownFloorTests
{
    /// <summary>120 s at 20 Hz: the shortest WoW tank major (Ardent Defender).</summary>
    private const int MajorThresholdTicks = 2400;

    private const long Hit = 1000;

    /// <summary>Floored outcome of a 1000 hit: 1000 * (10000 - 7500) / 10000.</summary>
    private const long FlooredDamage = 250;

    private static readonly EffectDef Wall = new("testWall", 1, null, new EffectModifiers(DamageTakenBp: -9000));
    private static readonly EffectDef BraceSelf = new("braceSelf", 1, null, new EffectModifiers(DamageTakenBp: -4000));
    private static readonly EffectDef BracedSelf = new("bracedSelf", 1, null, new EffectModifiers(DamageTakenBp: -3000));
    private static readonly EffectDef AegisShield = new("aegisShield", 1, null, new EffectModifiers(DamageTakenBp: -3500));

    private static readonly IReadOnlyDictionary<string, EffectDef> EffectDefs = Defs(Wall, BraceSelf, BracedSelf, AegisShield);

    private static AbilityDef SelfBuff(string id, string effectId, int cooldownTicks) =>
        Def(id, new AbilityShape(ShapeKind.Self), new EffectOp[] { new ApplyEffectOp(effectId, 200, 1) }, cooldownTicks: cooldownTicks);

    /// <summary>Shield Wall class: 180 s = 3600 ticks.</summary>
    private static readonly AbilityDef ShieldWall = SelfBuff("test.shieldWall", "testWall", 3600);

    /// <summary>The same -9000 effect on a cooldown one tick short of major.</summary>
    private static readonly AbilityDef NearMajorWall = SelfBuff("test.nearMajorWall", "testWall", MajorThresholdTicks - 1);

    /// <summary>The same -9000 effect at exactly the threshold.</summary>
    private static readonly AbilityDef ThresholdWall = SelfBuff("test.thresholdWall", "testWall", MajorThresholdTicks);

    /// <summary>The same -9000 effect on a routine 15 s cooldown (the kit's Brace cadence).</summary>
    private static readonly AbilityDef ShortWall = SelfBuff("test.shortWall", "testWall", 300);

    // The live Warden and Chanter defensives at their content cooldowns (content/data/abilities/*.json).
    private static readonly AbilityDef Brace = SelfBuff("test.brace", "braceSelf", 300);
    private static readonly AbilityDef Vigil = SelfBuff("test.vigil", "bracedSelf", 120);
    private static readonly AbilityDef Aegis = SelfBuff("test.aegis", "aegisShield", 300);

    private static readonly CastContext Ctx =
        new(AbilityMap(ShieldWall, NearMajorWall, ThresholdWall, ShortWall, Brace, Vigil, Aegis), EffectDefs, 60);

    private static (SimState State, ActorState Tank, ActorState Boss) Setup()
    {
        var tank = Make(1, ActorKind.Player, "warden", ActorRole.Tank, maxHealth: 1000);
        var boss = Make(2, ActorKind.Enemy, x: 1000, maxHealth: 1000);
        return (At(10, tank, boss), tank, boss);
    }

    /// <summary>Casts each ability on the tank through StartCast and CompleteCast, as a player press does.</summary>
    private static void Cast(SimState state, ActorState tank, params AbilityDef[] abilities)
    {
        var (_, emit) = Collector();
        foreach (var ability in abilities)
        {
            Assert.Null(Casting.StartCast(state, tank, ability, tank.Id, null, state.Tick, ctx: Ctx));
            Casting.CompleteCast(state, tank, Ctx, emit);
            Assert.Contains(tank.Effects, e => e.EffectId == ((ApplyEffectOp)ability.Effects[0]).EffectId);
            tank.Cooldowns = new JsRecord<int>();
            tank.GcdReadyTick = 0;
        }
    }

    private static long OneShot(SimState state, ActorState tank, ActorState boss) =>
        Mitigation.DealDamage(state.Tick, boss, tank, Hit, EffectDefs, _ => { }, boss.Id).ModifiedAmount;

    [Fact]
    public void A_major_cooldown_takes_a_one_shot_below_the_floor()
    {
        var (state, tank, boss) = Setup();
        Cast(state, tank, ShieldWall);

        var landed = OneShot(state, tank, boss);

        // -9000 unfloored: 1000 * (10000 - 9000) / 10000 = 100.
        Assert.True(
            landed < FlooredDamage,
            $"a 3600-tick (180 s, Shield Wall class) major at -9000 must go past the -7500 floor: expected < {FlooredDamage}, got {landed}");
        Assert.Equal(100, landed);
    }

    [Fact]
    public void An_ability_at_exactly_the_major_threshold_is_major()
    {
        var (state, tank, boss) = Setup();
        Cast(state, tank, ThresholdWall);

        Assert.Equal(100, OneShot(state, tank, boss));
    }

    [Fact]
    public void One_tick_under_the_threshold_stays_floored()
    {
        var (state, tank, boss) = Setup();
        Cast(state, tank, NearMajorWall);

        Assert.Equal(FlooredDamage, OneShot(state, tank, boss));
    }

    [Fact]
    public void A_short_cooldown_defensive_stays_floored()
    {
        var (state, tank, boss) = Setup();
        Cast(state, tank, ShortWall);

        Assert.Equal(FlooredDamage, OneShot(state, tank, boss));
    }

    [Fact]
    public void Stacked_routine_defensives_stay_floored()
    {
        var (state, tank, boss) = Setup();
        // Brace -4000, Vigil -3000, Aegis -3500 = -10500, all routine.
        Cast(state, tank, Brace, Vigil, Aegis);

        Assert.Equal(FlooredDamage, OneShot(state, tank, boss));
    }

    [Fact]
    public void A_raw_effect_with_no_major_cast_behind_it_stays_floored()
    {
        // An effect applied directly (no ability cast, e.g. a combo or aura) is never major, whatever its value.
        var (state, tank, boss) = Setup();
        Effects.ApplyEffectInstance(tank, Wall, tank.Id, 200, 1, state.Tick);

        Assert.Equal(FlooredDamage, OneShot(state, tank, boss));
    }
}
