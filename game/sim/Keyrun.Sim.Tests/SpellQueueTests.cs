using System.Collections.Generic;
using System.Linq;
using Keyrun.Sim.Combat;
using Keyrun.Sim.Core;
using Xunit;

namespace Keyrun.Sim.Tests;

/// <summary>
/// The spell queue window (PR #370; Matt: "it should feel almost exact to wow with less button clutter"). A player's
/// press refused only because a cast or the GCD is still running is queued when it lands inside the last
/// <see cref="SpellQueue.WindowTicks"/> ticks of that lock (WoW's default 400 ms at 20 Hz), and fires on the first
/// legal tick. One queued press per actor, the latest wins; a press earlier than the window is dropped. A queued press
/// fires as a cast command of its actor on that tick, so kicks still resolve by the tick they land with the fixed
/// (actorId, kind, insertion) tiebreak of BIBLE s3, never by when the key went down. Driven through
/// <see cref="Rules.Make"/>; the TS twin is tests/sim/combat/spell-queue.test.ts.
/// </summary>
public sealed class SpellQueueTests
{
    private const int CasterId = 1;
    private const int EnemyId = 2;
    private const int OtherKickerId = 3;
    private const int ChannelTicks = 30;
    private const int GcdTicks = 15;

    private static readonly AbilityDef Channel = Def("q.channel", ChannelTicks, GcdTicks, new EffectOp[] { new DamageOp(5, "fire") });
    private static readonly AbilityDef Bolt = Def("q.bolt", 0, GcdTicks, new EffectOp[] { new DamageOp(5, "fire") });
    private static readonly AbilityDef Zap = Def("q.zap", 0, GcdTicks, new EffectOp[] { new DamageOp(7, "fire") });
    private static readonly AbilityDef Kick = Def("q.kick", 0, 0, new EffectOp[] { new InterruptOp() });
    private static readonly AbilityDef Mending = Def("q.mending", 80, 0, new EffectOp[] { new HealOp(50) }, interruptible: true);

    [Fact]
    public void The_window_is_wows_400_ms_in_ticks()
    {
        Assert.Equal(8, SpellQueue.WindowTicks);
        Assert.Equal(400, SpellQueue.WindowTicks * Tick.TickMs);
    }

    [Fact]
    public void A_press_inside_the_last_window_ticks_of_a_cast_fires_on_the_tick_the_cast_ends()
    {
        var (state, rules) = Arena();
        var events = new List<SimEvent>();
        Step(state, rules, events, Cast(CasterId, Channel));
        StepUntil(state, rules, events, ChannelTicks - SpellQueue.WindowTicks);
        Step(state, rules, events, Cast(state.Tick, CasterId, Bolt, EnemyId));
        Assert.Equal(Channel.Id, state.Actors[CasterId].CastId); // queued, the cast under way continues
        Assert.Equal(Bolt.Id, state.Actors[CasterId].QueuedCast?.AbilityId);
        StepUntil(state, rules, events, ChannelTicks + 1);

        Assert.Equal(ChannelTicks, CompleteTick(events, Channel.Id));
        Assert.Equal(ChannelTicks, CompleteTick(events, Bolt.Id));
        Assert.Null(state.Actors[CasterId].QueuedCast);
    }

    [Fact]
    public void A_press_inside_the_last_window_ticks_of_the_gcd_fires_on_the_tick_the_gcd_ends()
    {
        var (state, rules) = Arena();
        var events = new List<SimEvent>();
        Step(state, rules, events, Cast(0, CasterId, Bolt, EnemyId));
        StepUntil(state, rules, events, GcdTicks - 3);
        Step(state, rules, events, Cast(state.Tick, CasterId, Zap, EnemyId));
        StepUntil(state, rules, events, GcdTicks + 2);

        Assert.Equal(GcdTicks, CompleteTick(events, Zap.Id));
    }

    [Fact]
    public void A_press_earlier_than_the_window_is_dropped()
    {
        var (state, rules) = Arena();
        var events = new List<SimEvent>();
        Step(state, rules, events, Cast(CasterId, Channel));
        StepUntil(state, rules, events, ChannelTicks - SpellQueue.WindowTicks - 1);
        Step(state, rules, events, Cast(state.Tick, CasterId, Bolt, EnemyId));
        Assert.Null(state.Actors[CasterId].QueuedCast);
        StepUntil(state, rules, events, ChannelTicks + 10);

        Assert.Equal(ChannelTicks, CompleteTick(events, Channel.Id));
        Assert.Null(CompleteTick(events, Bolt.Id));
    }

    [Fact]
    public void The_latest_queued_press_wins()
    {
        var (state, rules) = Arena();
        var events = new List<SimEvent>();
        Step(state, rules, events, Cast(CasterId, Channel));
        StepUntil(state, rules, events, ChannelTicks - 6);
        Step(state, rules, events, Cast(state.Tick, CasterId, Bolt, EnemyId));
        Step(state, rules, events, Cast(state.Tick, CasterId, Zap, EnemyId));
        StepUntil(state, rules, events, ChannelTicks + 20);

        Assert.Equal(ChannelTicks, CompleteTick(events, Zap.Id));
        Assert.Null(CompleteTick(events, Bolt.Id));
    }

    [Fact]
    public void A_fresh_press_on_the_firing_tick_supersedes_the_queued_one()
    {
        var (state, rules) = Arena();
        var events = new List<SimEvent>();
        Step(state, rules, events, Cast(CasterId, Channel));
        StepUntil(state, rules, events, ChannelTicks - 2);
        Step(state, rules, events, Cast(state.Tick, CasterId, Bolt, EnemyId));
        StepUntil(state, rules, events, ChannelTicks);
        Step(state, rules, events, Cast(state.Tick, CasterId, Zap, EnemyId));
        StepUntil(state, rules, events, ChannelTicks + 20);

        Assert.Equal(ChannelTicks, CompleteTick(events, Zap.Id));
        Assert.Null(CompleteTick(events, Bolt.Id));
    }

    [Fact]
    public void Queued_and_fresh_kicks_on_one_tick_resolve_by_actor_id_never_by_press_order()
    {
        foreach (var freshFirst in new[] { true, false })
        {
            var (state, rules) = Arena();
            var events = new List<SimEvent>();
            // Actor 1 casts the channel and queues its kick 4 ticks before the channel ends; actor 3 is idle and
            // presses its kick fresh on the very tick actor 1's queued kick fires. Both land on that tick.
            Step(state, rules, events, Cast(CasterId, Channel), new CastCommand(0, EnemyId, Mending.Id, CasterId));
            StepUntil(state, rules, events, ChannelTicks - 4);
            Step(state, rules, events, Cast(state.Tick, CasterId, Kick, EnemyId));
            StepUntil(state, rules, events, ChannelTicks);
            var fresh = Cast(state.Tick, OtherKickerId, Kick, EnemyId);
            var dummyTarget = new TargetCommand(state.Tick, OtherKickerId, EnemyId);
            Step(state, rules, events, freshFirst ? new Command[] { fresh, dummyTarget } : new Command[] { dummyTarget, fresh });

            var kick = Assert.Single(events, e => e.Type == "interrupt");
            Assert.Equal(ChannelTicks, kick.Tick);
            Assert.Equal(CasterId, ((JsInt)kick.Get("sourceId")!).Value);
        }
    }

    [Fact]
    public void A_queued_kick_that_fires_on_the_enemy_casts_end_tick_is_too_late()
    {
        var (state, rules) = Arena();
        var events = new List<SimEvent>();
        // The enemy's cast ends on the tick the caster's channel ends: completion runs before commands, so the
        // queued kick (whose landing tick is that tick) misses, exactly as a fresh kick pressed then would.
        Step(state, rules, events, Cast(CasterId, Channel));
        Step(state, rules, events); // tick 1
        state.Actors[EnemyId].CastId = Mending.Id;
        state.Actors[EnemyId].CastStartTick = state.Tick - 50;
        state.Actors[EnemyId].CastEndsTick = ChannelTicks;
        StepUntil(state, rules, events, ChannelTicks - 3);
        Step(state, rules, events, Cast(state.Tick, CasterId, Kick, EnemyId));
        StepUntil(state, rules, events, ChannelTicks + 2);

        Assert.DoesNotContain(events, e => e.Type == "interrupt");
        Assert.Equal(ChannelTicks, CompleteTick(events, Mending.Id));
    }

    [Fact]
    public void An_enemy_press_is_never_queued()
    {
        var (state, rules) = Arena();
        var events = new List<SimEvent>();
        Step(state, rules, events, new CastCommand(0, EnemyId, Bolt.Id, CasterId));
        StepUntil(state, rules, events, GcdTicks - 2);
        Step(state, rules, events, new CastCommand(state.Tick, EnemyId, Zap.Id, CasterId));
        StepUntil(state, rules, events, GcdTicks + 5);

        Assert.Null(state.Actors[EnemyId].QueuedCast);
        Assert.Null(CompleteTick(events, Zap.Id));
    }

    private static CastCommand Cast(int actorId, AbilityDef def) => new(0, actorId, def.Id, EnemyId);

    private static CastCommand Cast(int tick, int actorId, AbilityDef def, int targetId) => new(tick, actorId, def.Id, targetId);

    private static int? CompleteTick(List<SimEvent> events, string abilityId) =>
        events.Where(e => e.Type == "cast_complete" && e.Get("abilityId") is JsString s && s.Value == abilityId)
            .Select(e => (int?)e.Tick)
            .FirstOrDefault();

    private static (SimState State, StepRules Rules) Arena()
    {
        var abilities = new Dictionary<string, AbilityDef>
        {
            [Channel.Id] = Channel, [Bolt.Id] = Bolt, [Zap.Id] = Zap, [Kick.Id] = Kick, [Mending.Id] = Mending,
        };
        var rules = Rules.Make(new RulesConfig
        {
            Abilities = abilities,
            Effects = new Dictionary<string, EffectDef>(),
            LockoutTicks = 60,
            BaseSpeedMmPerTick = 300,
            ActorRadiusMm = 50,
            Bounds = new Bounds(-10000, 10000, -10000, 10000),
        });
        var state = SimState.Create(1);
        state.Phase = "combat";
        state.Actors[CasterId] = Actors.Create(CasterId, ActorKind.Player, "cinder", ActorRole.Damage, new Vec3(0, 0, 0), 1000, 100, 0);
        state.Actors[EnemyId] = Actors.Create(EnemyId, ActorKind.Enemy, "choir.cantor", ActorRole.None, new Vec3(0, 0, 1000), 100000, 0, 0);
        state.Actors[OtherKickerId] = Actors.Create(OtherKickerId, ActorKind.Player, "cinder", ActorRole.Damage, new Vec3(1000, 0, 0), 1000, 100, 0);
        return (state, rules);
    }

    private static void Step(SimState state, StepRules rules, List<SimEvent> events, params Command[] cmds)
    {
        rules(state, cmds, events.Add);
        state.Tick += 1;
    }

    private static void StepUntil(SimState state, StepRules rules, List<SimEvent> events, int tick)
    {
        while (state.Tick < tick)
        {
            Step(state, rules, events);
        }
    }

    private static AbilityDef Def(string id, int castTicks, int gcdTicks, IReadOnlyList<EffectOp> effects, bool interruptible = false) => new()
    {
        Id = id,
        CastTicks = castTicks,
        GcdTicks = gcdTicks,
        CooldownTicks = 0,
        Charges = 1,
        Cost = new AbilityCost("none", 0),
        Range = 5000,
        Shape = new AbilityShape(ShapeKind.Target),
        Interruptible = interruptible,
        Effects = effects,
        Tags = null,
    };
}
