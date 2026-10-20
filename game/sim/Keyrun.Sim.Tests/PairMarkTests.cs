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
