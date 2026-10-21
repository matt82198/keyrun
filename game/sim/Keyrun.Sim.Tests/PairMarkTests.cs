using System.Collections.Generic;
using System.Linq;
using Keyrun.Sim.Combat;
using Keyrun.Sim.Core;
using Keyrun.Sim.Runs;
using Xunit;
using static Keyrun.Sim.Tests.CombatCastTestKit;

namespace Keyrun.Sim.Tests;

/// <summary>
/// The Pair's marks have consequences (design/BIBLE.md section 9.6 item 2: "every 20 s one player is marked to
/// <i>spread</i> (a ring: stand apart), ten seconds later the pack calls a <i>stack</i> (a circle: stand together,
/// damage split). Teaches reading shape."; finding f-87f2a9a5). Before this, content/data/effects/enemy.json gave
/// spreadMarked and stackMarked empty modifiers and nothing in the rules read them, so standing anywhere was safe.
/// <para>
/// Every case drives the real rules step (<see cref="Rules.Make"/>) with the Pair's abilities as content/data ships
/// them (content/data/abilities/enemies.json pair.spreadMark: target, cast 40, 200-tick mark; pair.stackCall: circle
/// 1500, cast 40, 100-tick mark) and the mark rules game/content/data/run.json pairMarks ships (spread 3000 mm, 80;
/// stack 1500 mm, 150).
/// </para>
/// </summary>
public sealed class PairMarkTests
{
    private const int P1 = 1;
    private const int P2 = 2;
    private const int P3 = 3;
    private const int WarriorA = 10;
    private const int WarriorB = 11;
    private const long PlayerHealth = 250;

    /// <summary>Cast 40 then a 200-tick mark: a spread cast started on tick 0 lands on 40 and its mark ends on 240.</summary>
    private const int SpreadEnds = 240;

    /// <summary>Cast 40 then a 100-tick mark: a stack call started on tick 0 lands on 40 and its mark ends on 140.</summary>
    private const int StackEnds = 140;

    /// <summary>The values game/content/data/run.json pairMarks ships.</summary>
    public static readonly MarkRules Shipped = new("spreadMarked", 3000, 80, "stackMarked", 1500, 150);

    [Fact]
    public void A_spread_marked_player_standing_with_an_ally_hurts_the_ally_when_the_mark_runs_out()
    {
        var state = Arena(Player(P1, 0, 0), Player(P2, 1000, 0), Warrior(WarriorA, 0, 5000));
        var events = Run(state, Shipped, SpreadEnds, Cast(WarriorA, "pair.spreadMark", P1));

        Assert.Equal(PlayerHealth - 80, state.Actors[P2].Health);
        Assert.Equal(PlayerHealth, state.Actors[P1].Health);
        var hit = Assert.Single(events, e => e.Type == "damage");
        Assert.Equal(SpreadEnds, hit.Tick);
        Assert.Equal(WarriorA, Int(hit, "sourceId"));
        Assert.Equal(P2, Int(hit, "targetId"));
    }

    [Fact]
    public void Nothing_lands_before_the_spread_mark_runs_out()
    {
        var state = Arena(Player(P1, 0, 0), Player(P2, 1000, 0), Warrior(WarriorA, 0, 5000));
        var events = Run(state, Shipped, SpreadEnds - 1, Cast(WarriorA, "pair.spreadMark", P1));

        Assert.Contains(state.Actors[P1].Effects, e => e.EffectId == "spreadMarked");
        Assert.DoesNotContain(events, e => e.Type == "damage");
        Assert.Equal(PlayerHealth, state.Actors[P2].Health);
    }

    [Fact]
    public void A_spread_marked_player_who_stands_apart_hurts_nobody()
    {
        var state = Arena(Player(P1, 0, 0), Player(P2, 3001, 0), Warrior(WarriorA, 0, 5000));
        var events = Run(state, Shipped, SpreadEnds, Cast(WarriorA, "pair.spreadMark", P1));

        Assert.DoesNotContain(events, e => e.Type == "damage");
        Assert.Equal(PlayerHealth, state.Actors[P2].Health);
        Assert.DoesNotContain(state.Actors[P1].Effects, e => e.EffectId == "spreadMarked");
    }

    [Fact]
    public void Two_spread_marked_players_standing_together_hit_each_other()
    {
        var state = Arena(Player(P1, 0, 0), Player(P2, 1000, 0), Warrior(WarriorA, 0, 5000), Warrior(WarriorB, 1000, 5000));
        Run(state, Shipped, SpreadEnds, Cast(WarriorA, "pair.spreadMark", P1), Cast(WarriorB, "pair.spreadMark", P2));

        Assert.Equal(PlayerHealth - 80, state.Actors[P1].Health);
        Assert.Equal(PlayerHealth - 80, state.Actors[P2].Health);
    }

    [Fact]
    public void A_player_marked_by_several_warriors_detonates_once()
    {
        var state = Arena(Player(P1, 0, 0), Player(P2, 1000, 0), Warrior(WarriorA, 0, 5000), Warrior(WarriorB, 1000, 5000));
        Run(state, Shipped, SpreadEnds, Cast(WarriorA, "pair.spreadMark", P1), Cast(WarriorB, "pair.spreadMark", P1));

        // Two instances (one per caster), one detonation: 80, not 160.
        Assert.Equal(PlayerHealth - 80, state.Actors[P2].Health);
        Assert.Equal(PlayerHealth, state.Actors[P1].Health);
    }

    [Fact]
    public void A_stack_marked_player_standing_alone_takes_the_whole_hit()
    {
        // Only P1 is inside the stack call's 1500 mm circle when it lands; P2 stands well clear.
        var state = Arena(Player(P1, 1000, 0), Player(P2, -6000, 0), Warrior(WarriorA, 0, 0));
        var events = Run(state, Shipped, StackEnds, Cast(WarriorA, "pair.stackCall"));

        Assert.Equal(PlayerHealth - 150, state.Actors[P1].Health);
        Assert.Equal(PlayerHealth, state.Actors[P2].Health);
        var hit = Assert.Single(events, e => e.Type == "damage");
        Assert.Equal(StackEnds, hit.Tick);
        Assert.Equal(WarriorA, Int(hit, "sourceId"));
    }

    [Fact]
    public void Three_players_stacked_together_split_the_hit()
    {
        var state = Arena(Player(P1, 1000, 0), Player(P2, 1200, 400), Player(P3, 800, -400), Warrior(WarriorA, 0, 0));
        Run(state, Shipped, StackEnds, Cast(WarriorA, "pair.stackCall"));

        Assert.Equal(PlayerHealth - 50, state.Actors[P1].Health);
        Assert.Equal(PlayerHealth - 50, state.Actors[P2].Health);
        Assert.Equal(PlayerHealth - 50, state.Actors[P3].Health);
    }

    [Fact]
    public void Stack_marked_players_who_scatter_before_the_mark_runs_out_each_take_the_whole_hit()
    {
        var state = Arena(Player(P1, 1000, 0), Player(P2, -1000, 0), Warrior(WarriorA, 0, 0));
        // Both are inside the circle when the call lands (tick 40), 2000 mm apart: not together.
        Run(state, Shipped, StackEnds, Cast(WarriorA, "pair.stackCall"));

        Assert.Equal(PlayerHealth - 150, state.Actors[P1].Health);
        Assert.Equal(PlayerHealth - 150, state.Actors[P2].Health);
    }

    // BIBLE section 3: "Every dangerous enemy action is a cast with a visible bar and a floor telegraph (circle, cone,
    // line) drawn ahead of resolution. Shape plus colour, never colour alone." The marks resolve when they run out, so
    // each marked player carries a floor ring of the mark's damage radius from landing to resolution.

    [Fact]
    public void A_spread_mark_rings_the_marked_player_with_the_spread_radius_until_it_resolves()
    {
        var state = Arena(Player(P1, 0, 0), Player(P2, 1000, 0), Warrior(WarriorA, 0, 5000));
        var rings = RingsAfterEachStep(state, Shipped, SpreadEnds + 1, Cast(WarriorA, "pair.spreadMark", P1));

        Assert.Empty(rings[40]); // the cast is still on its bar: no mark yet
        var ring = Assert.Single(rings[41]); // landed on tick 40
        Assert.Equal(P1, ring.ActorId);
        Assert.Equal(MarkKind.Spread, ring.Kind);
        Assert.Equal(3000, ring.RadiusMm);
        Assert.Equal(SpreadEnds, ring.EndsTick);
        Assert.Single(rings[SpreadEnds]); // still down on the tick it resolves
        Assert.Empty(rings[SpreadEnds + 1]); // gone once it has
    }

    [Fact]
    public void A_stack_mark_rings_the_marked_player_with_the_stack_radius_until_it_resolves()
    {
        var state = Arena(Player(P1, 1000, 0), Player(P2, -6000, 0), Warrior(WarriorA, 0, 0));
        var rings = RingsAfterEachStep(state, Shipped, StackEnds + 1, Cast(WarriorA, "pair.stackCall"));

        Assert.Empty(rings[40]);
        var ring = Assert.Single(rings[41]);
        Assert.Equal(P1, ring.ActorId);
        Assert.Equal(MarkKind.Stack, ring.Kind);
        Assert.Equal(1500, ring.RadiusMm);
        Assert.Equal(StackEnds, ring.EndsTick);
        Assert.Single(rings[StackEnds]);
        Assert.Empty(rings[StackEnds + 1]);
    }

    [Fact]
    public void Spread_and_stack_rings_differ_in_shape_not_just_colour()
    {
        var spreadState = Arena(Player(P1, 0, 0), Warrior(WarriorA, 0, 5000));
        var spread = Assert.Single(RingsAfterEachStep(spreadState, Shipped, 41, Cast(WarriorA, "pair.spreadMark", P1))[41]);
        var stackState = Arena(Player(P1, 1000, 0), Warrior(WarriorA, 0, 0));
        var stack = Assert.Single(RingsAfterEachStep(stackState, Shipped, 41, Cast(WarriorA, "pair.stackCall"))[41]);

        // Spread: one broken ring at the spread radius, dashes with gaps (stand apart).
        Assert.Equal(PairMarks.SpreadDashes, spread.Strokes.Count);
        Assert.All(spread.Strokes, s => Assert.Equal(3000, s.RadiusMm));
        Assert.All(spread.Strokes, s => Assert.True(s.ToPermille > s.FromPermille));
        Assert.True(spread.Strokes.Sum(s => s.ToPermille - s.FromPermille) < 1000);
        Assert.DoesNotContain(spread.Strokes, s => s.ToPermille - s.FromPermille == 1000);

        // Stack: two unbroken concentric circles, the outer at the stack radius (gather in).
        Assert.Equal(2, stack.Strokes.Count);
        Assert.All(stack.Strokes, s => Assert.Equal((0, 1000), (s.FromPermille, s.ToPermille)));
        Assert.Equal(new long[] { 1500, 750 }, stack.Strokes.Select(s => s.RadiusMm).ToArray());
    }

    [Fact]
    public void A_player_marked_by_several_warriors_carries_one_ring()
    {
        var state = Arena(Player(P1, 0, 0), Warrior(WarriorA, 0, 5000), Warrior(WarriorB, 1000, 5000));
        var rings = RingsAfterEachStep(state, Shipped, 41, Cast(WarriorA, "pair.spreadMark", P1), Cast(WarriorB, "pair.spreadMark", P1));

        Assert.Equal(2, state.Actors[P1].Effects.Count(e => e.EffectId == "spreadMarked"));
        Assert.Single(rings[41]);
    }

    [Fact]
    public void Without_mark_rules_no_ring_is_drawn()
    {
        var state = Arena(Player(P1, 0, 0), Warrior(WarriorA, 0, 5000));
        var rings = RingsAfterEachStep(state, null, 41, Cast(WarriorA, "pair.spreadMark", P1));

        Assert.Contains(state.Actors[P1].Effects, e => e.EffectId == "spreadMarked");
        Assert.Empty(rings[41]);
    }

    [Fact]
    public void Without_mark_rules_the_marks_do_nothing_as_on_every_oracle_run()
    {
        var spread = Arena(Player(P1, 0, 0), Player(P2, 1000, 0), Warrior(WarriorA, 0, 5000));
        Assert.DoesNotContain(Run(spread, null, SpreadEnds, Cast(WarriorA, "pair.spreadMark", P1)), e => e.Type == "damage");

        var stack = Arena(Player(P1, 1000, 0), Warrior(WarriorA, 0, 0));
        Assert.DoesNotContain(Run(stack, null, StackEnds, Cast(WarriorA, "pair.stackCall")), e => e.Type == "damage");
        Assert.Equal(PlayerHealth, stack.Actors[P1].Health);
    }

    [Fact]
    public void The_run_root_hands_the_content_mark_rules_to_the_combat_rules()
    {
        Assert.Same(Shipped, RunWiring.RulesConfigOf(RunTestKit.Content() with { Marks = Shipped }).Marks);
        Assert.Null(RunWiring.RulesConfigOf(RunTestKit.Content()).Marks);
    }

    // mark_applied / mark_detonated: the receipt a capture reads from demo.log (BIBLE s9 The Pair). One mark_applied per
    // completed mark cast, on the tick the mark lands; one mark_detonated per resolved mark, on the tick it runs out.

    [Fact]
    public void A_completed_spread_cast_emits_one_mark_applied_naming_the_marked_player_and_its_detonation_tick()
    {
        var state = Arena(Player(P1, 0, 0), Player(P2, 1000, 0), Warrior(WarriorA, 0, 5000));
        var events = Run(state, Shipped, SpreadEnds, Cast(WarriorA, "pair.spreadMark", P1));

        var applied = Assert.Single(events, e => e.Type == "mark_applied");
        Assert.Equal(40, applied.Tick);
        Assert.Equal("pair.spreadMark", Str(applied, "cast"));
        Assert.Equal("spread", Str(applied, "kind"));
        Assert.Equal(WarriorA, Int(applied, "sourceId"));
        Assert.Equal(new long[] { P1 }, Longs(applied, "targetIds"));
        Assert.Equal(3000, Int(applied, "radius"));
        Assert.Equal(SpreadEnds, Int(applied, "detonatesTick"));
    }

    [Fact]
    public void A_spread_mark_detonates_once_on_its_detonation_tick_listing_who_it_hit_and_for_how_much()
    {
        var state = Arena(Player(P1, 0, 0), Player(P2, 1000, 0), Warrior(WarriorA, 0, 5000));
        var events = Run(state, Shipped, SpreadEnds, Cast(WarriorA, "pair.spreadMark", P1));

        var detonated = Assert.Single(events, e => e.Type == "mark_detonated");
        Assert.Equal(SpreadEnds, detonated.Tick);
        Assert.Equal("spread", Str(detonated, "kind"));
        Assert.Equal(P1, Int(detonated, "markedId"));
        Assert.Equal(new long[] { P2 }, Longs(detonated, "hitIds"));
        Assert.Equal(new (long, long)[] { (P2, 80) }, DamageOf(detonated));
    }

    [Fact]
    public void A_spread_mark_that_hits_nobody_still_detonates_once_with_an_empty_hit_list()
    {
        var state = Arena(Player(P1, 0, 0), Player(P2, 3001, 0), Warrior(WarriorA, 0, 5000));
        var events = Run(state, Shipped, SpreadEnds, Cast(WarriorA, "pair.spreadMark", P1));

        var detonated = Assert.Single(events, e => e.Type == "mark_detonated");
        Assert.Equal(SpreadEnds, detonated.Tick);
        Assert.Empty(Longs(detonated, "hitIds"));
        Assert.Empty(DamageOf(detonated));
    }

    [Fact]
    public void A_completed_stack_call_emits_mark_applied_and_a_detonation_that_hits_the_marked_player_alone()
    {
        var state = Arena(Player(P1, 1000, 0), Player(P2, -6000, 0), Warrior(WarriorA, 0, 0));
        var events = Run(state, Shipped, StackEnds, Cast(WarriorA, "pair.stackCall"));

        var applied = Assert.Single(events, e => e.Type == "mark_applied");
        Assert.Equal(40, applied.Tick);
        Assert.Equal("pair.stackCall", Str(applied, "cast"));
        Assert.Equal("stack", Str(applied, "kind"));
        Assert.Equal(new long[] { P1 }, Longs(applied, "targetIds"));
        Assert.Equal(1500, Int(applied, "radius"));
        Assert.Equal(StackEnds, Int(applied, "detonatesTick"));

        var detonated = Assert.Single(events, e => e.Type == "mark_detonated");
        Assert.Equal(StackEnds, detonated.Tick);
        Assert.Equal("stack", Str(detonated, "kind"));
        Assert.Equal(P1, Int(detonated, "markedId"));
        Assert.Equal(new long[] { P1 }, Longs(detonated, "hitIds"));
        Assert.Equal(new (long, long)[] { (P1, 150) }, DamageOf(detonated));
    }

    [Fact]
    public void A_mark_cast_stopped_before_it_lands_emits_no_mark_event()
    {
        var state = Arena(Player(P1, 0, 0), Player(P2, 1000, 0), Warrior(WarriorA, 0, 5000));
        var rules = Rules.Make(Config(PairAbilities(), PairEffects()) with { Marks = Shipped });
        var events = new List<SimEvent>();
        Tick(state, rules, Cmds(Cast(WarriorA, "pair.spreadMark", P1)), events.Add);
        while (state.Tick < 20)
        {
            Tick(state, rules, Cmds(), events.Add);
        }
        Casting.Stopcast(state.Actors[WarriorA], events.Add, state.Tick);
        while (state.Tick <= SpreadEnds)
        {
            Tick(state, rules, Cmds(), events.Add);
        }

        Assert.Contains(events, e => e.Type == "cast_stopped");
        Assert.DoesNotContain(events, e => e.Type == "mark_applied" || e.Type == "mark_detonated");
        Assert.Equal(PlayerHealth, state.Actors[P2].Health);
    }

    [Fact]
    public void A_mark_caster_who_dies_mid_cast_emits_no_mark_event()
    {
        var state = Arena(Player(P1, 0, 0), Player(P2, 1000, 0), Warrior(WarriorA, 0, 5000));
        var rules = Rules.Make(Config(PairAbilities(), PairEffects()) with { Marks = Shipped });
        var events = new List<SimEvent>();
        Tick(state, rules, Cmds(Cast(WarriorA, "pair.spreadMark", P1)), events.Add);
        while (state.Tick < 20)
        {
            Tick(state, rules, Cmds(), events.Add);
        }
        Mitigation.DealDamage(state.Tick, null, state.Actors[WarriorA], 100000, PairEffects(), events.Add, P1);
        while (state.Tick <= SpreadEnds)
        {
            Tick(state, rules, Cmds(), events.Add);
        }

        Assert.Contains(events, e => e.Type == "death" && Int(e, "actorId") == WarriorA);
        Assert.DoesNotContain(events, e => e.Type == "mark_applied" || e.Type == "mark_detonated");
        Assert.Equal(PlayerHealth, state.Actors[P2].Health);
    }

    private static long[] Longs(SimEvent e, string key) =>
        ((JsArray)e.Get(key)!).Items.Select(i => ((JsInt)i).Value).ToArray();

    private static (long, long)[] DamageOf(SimEvent e) =>
        ((JsArray)e.Get("damage")!).Items
            .Select(i => (((JsInt)((JsObject)i).Get("actorId")!).Value, ((JsInt)((JsObject)i).Get("amount")!).Value))
            .ToArray();

    private static ActorState Player(int id, long x, long z) => Make(id, ActorKind.Player, "warden", ActorRole.Tank, x, z, PlayerHealth);

    private static ActorState Warrior(int id, long x, long z) => Make(id, ActorKind.Enemy, "pair.melee", ActorRole.None, x, z, 1200);

    private static SimState Arena(params ActorState[] actors) => At(0, actors);

    /// <summary>Issue <paramref name="first"/> on tick 0, then step with no commands through <paramref name="lastTick"/>.</summary>
    private static List<SimEvent> Run(SimState state, MarkRules? marks, int lastTick, params Command[] first)
    {
        var rules = Rules.Make(Config(PairAbilities(), PairEffects()) with { Marks = marks });
        var events = new List<SimEvent>();
        Tick(state, rules, first, events.Add);
        while (state.Tick <= lastTick)
        {
            Tick(state, rules, Cmds(), events.Add);
        }
        return events;
    }

    /// <summary>
    /// As <see cref="Run"/>, recording <see cref="PairMarks.Rings"/> after every step, keyed by the state's tick then
    /// (what a view draws between steps): entry t is the rings after the step that ran tick t - 1.
    /// </summary>
    private static Dictionary<int, List<MarkRing>> RingsAfterEachStep(SimState state, MarkRules? marks, int lastTick, params Command[] first)
    {
        var rules = Rules.Make(Config(PairAbilities(), PairEffects()) with { Marks = marks });
        var rings = new Dictionary<int, List<MarkRing>>();
        Tick(state, rules, first, _ => { });
        rings[state.Tick] = PairMarks.Rings(state, marks);
        while (state.Tick <= lastTick - 1)
        {
            Tick(state, rules, Cmds(), _ => { });
            rings[state.Tick] = PairMarks.Rings(state, marks);
        }
        return rings;
    }

    /// <summary>content/data/abilities/enemies.json pair.spreadMark and pair.stackCall.</summary>
    private static IReadOnlyDictionary<string, AbilityDef> PairAbilities() => AbilityMap(
        Def(
            "pair.spreadMark",
            new AbilityShape(ShapeKind.Target),
            new EffectOp[] { new ApplyEffectOp("spreadMarked", 200, 1) },
            castTicks: 40,
            cooldownTicks: 400,
            costResource: "none",
            range: 25000,
            tags: new[] { "control" }),
        Def(
            "pair.stackCall",
            new AbilityShape(ShapeKind.Circle, 1500),
            new EffectOp[] { new ApplyEffectOp("stackMarked", 100, 1) },
            castTicks: 40,
            cooldownTicks: 400,
            costResource: "none",
            range: 0,
            tags: new[] { "control" }));

    /// <summary>content/data/effects/enemy.json spreadMarked and stackMarked: max 1 stack, no modifiers.</summary>
    private static IReadOnlyDictionary<string, EffectDef> PairEffects() => CombatTestKit.Defs(
        new EffectDef("spreadMarked", 1, null, new EffectModifiers()),
        new EffectDef("stackMarked", 1, null, new EffectModifiers()));
}
