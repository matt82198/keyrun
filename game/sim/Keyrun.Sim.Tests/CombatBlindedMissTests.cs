using System.Linq;
using Keyrun.Sim.Combat;
using Keyrun.Sim.Core;
using Xunit;
using static Keyrun.Sim.Tests.CombatCastTestKit;
using static Keyrun.Sim.Tests.CombatTestKit;

namespace Keyrun.Sim.Tests;

/// <summary>
/// design/BIBLE.md section 4, Steam: "a cloud that blinds enemies inside (they miss) for 4 s". An actor carrying the
/// "blinded" status (content/data/effects/player.json, empty modifiers; the sim decides by id) misses every outgoing
/// damaging hit while the status lasts: the target loses no health, no absorb is consumed, and a "miss" event names
/// the attacker and the target so the view can show it.
/// </summary>
public sealed class CombatBlindedMissTests
{
    private static readonly EffectDef Blinded = new("blinded", 1, "magic", new EffectModifiers());

    private static ActorState Blind(ActorState a, int durationTicks = 80, int now = 0)
    {
        Effects.ApplyEffectInstance(a, Blinded, 99, durationTicks, 1, now);
        return a;
    }

    [Fact]
    public void A_blinded_attacker_misses_target_keeps_health_and_a_miss_event_replaces_the_damage_event()
    {
        var src = Blind(Actor(1, ActorKind.Enemy));
        var dst = Actor(2, ActorKind.Player, maxHealth: 100);
        var (events, emit) = Collector();
        var r = Mitigation.DealDamage(5, src, dst, 30, Defs(Blinded), emit, 1);
        Assert.Equal(100, dst.Health);
        Assert.Equal(0, r.HealthDamage);
        Assert.False(r.Died);
        var miss = Assert.Single(events);
        Assert.Equal("miss", miss.Type);
        Assert.Equal(5, miss.Tick);
        Assert.Equal(1, Int(miss, "sourceId"));
        Assert.Equal(2, Int(miss, "targetId"));
        Assert.Equal(30, Int(miss, "rawAmount"));
    }

    [Fact]
    public void A_blinded_attacker_does_not_consume_the_target_absorb()
    {
        var shield = new EffectDef("shield", 1, null, new EffectModifiers(Absorb: 50));
        var src = Blind(Actor(1, ActorKind.Enemy));
        var dst = Actor(2, ActorKind.Player);
        Effects.ApplyEffectInstance(dst, shield, 3, 100, 1, 0);
        Mitigation.DealDamage(5, src, dst, 30, Defs(Blinded, shield), _ => { }, 1);
        Assert.Equal(50, Effects.AbsorbPool(dst, Defs(Blinded, shield)));
    }

    [Fact]
    public void An_unblinded_attacker_still_hits()
    {
        var src = Actor(1, ActorKind.Enemy);
        var dst = Actor(2, ActorKind.Player, maxHealth: 100);
        var (events, emit) = Collector();
        Mitigation.DealDamage(5, src, dst, 30, Defs(Blinded), emit, 1);
        Assert.Equal(70, dst.Health);
        Assert.Equal("damage", Assert.Single(events).Type);
    }

    [Fact]
    public void A_blinded_target_is_still_hit_blind_only_affects_the_attacker()
    {
        var src = Actor(1, ActorKind.Player);
        var dst = Blind(Actor(2, ActorKind.Enemy, maxHealth: 100));
        var (events, emit) = Collector();
        Mitigation.DealDamage(5, src, dst, 30, Defs(Blinded), emit, 1);
        Assert.Equal(70, dst.Health);
        Assert.Equal("damage", Assert.Single(events).Type);
    }

    [Fact]
    public void Blind_lasts_only_while_the_status_lasts_a_hit_at_its_end_tick_lands()
    {
        var src = Blind(Actor(1, ActorKind.Enemy), durationTicks: 80, now: 0);
        var dst = Actor(2, ActorKind.Player, maxHealth: 100);
        Mitigation.DealDamage(79, src, dst, 10, Defs(Blinded), _ => { }, 1);
        Assert.Equal(100, dst.Health);
        Mitigation.DealDamage(80, src, dst, 10, Defs(Blinded), _ => { }, 1);
        Assert.Equal(90, dst.Health);
    }

    [Fact]
    public void A_blinded_enemy_casting_a_damaging_ability_misses_through_the_rules_step()
    {
        var bite = Def("bite", new AbilityShape(ShapeKind.Target), new EffectOp[] { new DamageOp(40, "physical") }, range: 5000);
        var enemy = Blind(Make(1, ActorKind.Enemy, x: 0), durationTicks: 80, now: 0);
        var player = Make(2, ActorKind.Player, x: 100, maxHealth: 100);
        var state = At(1, enemy, player);
        var (events, emit) = Collector();
        Rules.Make(Config(AbilityMap(bite), Defs(Blinded)))(state, Cmds(Cast(1, "bite", 2)), emit);
        Assert.Equal(100, player.Health);
        Assert.DoesNotContain(events, e => e.Type == "damage");
        var miss = Assert.Single(events, e => e.Type == "miss");
        Assert.Equal(1, Int(miss, "sourceId"));
        Assert.Equal(2, Int(miss, "targetId"));
    }
}
