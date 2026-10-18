using System;
using System.Collections.Generic;
using System.Linq;
using Keyrun.Sim.Combat;
using Keyrun.Sim.Core;
using Keyrun.Sim.Encounter;
using Keyrun.Sim.Runs;
using Xunit;

namespace Keyrun.Sim.Tests;

/// <summary>
/// Port of tests/sim/integration/module-state-per-run.test.ts. The TS kept the affinity effect registry, the active
/// EncounterRules and the combo depth as module state; the C# run root threads all three per run (the run's effect
/// table into <see cref="Affinities.Of"/>, the run's Clause into <see cref="RulesConfig.Clause"/>, a fresh
/// <see cref="ClauseHooks"/> per step), so interleaved runs can never see each other's.
/// </summary>
public sealed class RunClauseIsolationTests
{
    private const long Seed = 4242;
    private const int Player = 1;
    private const int Partner = 2;
    private const int Enemy = 100;

    private static readonly DungeonDef Dungeon = RunTestKit.OnePull("one-pull", "boss", RunTestKit.Spawn(Enemy, "boss", 0, 0, 1000000));

    private static readonly PartyMemberSpec[] Party =
    {
        RunTestKit.Member(Player, "alpha", ActorRole.Damage, 0, 0, 200, 100),
        RunTestKit.Member(Partner, "beta", ActorRole.Damage, 0, 0, 200, 100),
    };

    private static RunContent Base(Dictionary<string, AbilityDef> abilities, Dictionary<string, EffectDef> effects) =>
        RunTestKit.Content(abilities, effects, bounds: RunTestKit.Arena20k);

    private static List<(Run Run, List<SimEvent> Events)> Interleave(IReadOnlyList<Run> runs, IReadOnlyList<IReadOnlyList<Command>> commands, int ticks)
    {
        var outs = runs.Select(r => (Run: r, Events: new List<SimEvent>())).ToList();
        for (var t = 0; t < ticks; t++)
        {
            for (var i = 0; i < outs.Count; i++)
            {
                var run = outs[i].Run;
                var stepped = RunRoot.StepRun(run, commands[i].Where(c => c.Tick == run.State.Tick).ToList());
                outs[i].Events.AddRange(stepped.Events);
                outs[i] = (stepped.Run, outs[i].Events);
            }
        }
        return outs;
    }

    private static readonly AbilityDef Pulse = RunTestKit.Ability("pulse", new EffectOp[] { new ApplyEffectOp("mark", 100, 1) }, new AbilityShape(ShapeKind.Circle, Radius: 5000));

    private static EffectDef MarkDef(long damageTakenBp) => new("mark", 1, null, new EffectModifiers(null, damageTakenBp, null, null, null));

    private static RunContent BuffContent() => Base(new() { ["pulse"] = Pulse }, new() { ["mark"] = MarkDef(-1000) });

    private static RunContent CurseContent() => Base(new() { ["pulse"] = Pulse }, new() { ["mark"] = MarkDef(1000) });

    private static readonly Command[] PulseAt10 = { new CastCommand(10, Player, "pulse") };

    private static List<int> Marked(Run run) =>
        run.State.Actors.Values.Where(a => a.Effects.Any(e => e.EffectId == "mark")).Select(a => a.Id).OrderBy(x => x).ToList();

    private static string Line(Run run) => Projection.Line(run.State, run.Encounter.PullIndex, run.Encounter.SlackTicks);

    [Fact]
    public void Registered_effects_interleaved_runs_with_opposite_polarity_each_match_their_solo_replay()
    {
        var both = Interleave(new[] { RunRoot.CreateRun(Seed, Party, Dungeon, BuffContent()), RunRoot.CreateRun(Seed, Party, Dungeon, CurseContent()) }, new[] { PulseAt10, PulseAt10 }, 30);
        var soloBuff = Interleave(new[] { RunRoot.CreateRun(Seed, Party, Dungeon, BuffContent()) }, new[] { PulseAt10 }, 30)[0].Run;
        var soloCurse = Interleave(new[] { RunRoot.CreateRun(Seed, Party, Dungeon, CurseContent()) }, new[] { PulseAt10 }, 30)[0].Run;

        Assert.Equal(new[] { Player, Partner }, Marked(both[0].Run));
        Assert.Equal(new[] { Enemy }, Marked(both[1].Run));
        Assert.Equal(Line(soloBuff), Line(both[0].Run));
        Assert.Equal(Line(soloCurse), Line(both[1].Run));
    }

    private static readonly ClauseDef PactClause = new("pact", "Pact", ClauseKind.Pact,
        new Dictionary<string, long>(StringComparer.Ordinal) { ["comboDamageBp"] = 2000, ["soloDamageBp"] = -1000 }, "d");

    private static RunContent PactContent()
    {
        var combos = new ComboTable();
        combos.Set("alpha", "beta", new ComboDef("combo1", new EffectOp[] { new DamageOp(1000, "physical") }));
        return Base(
            new()
            {
                ["open"] = RunTestKit.Ability("open", new EffectOp[] { new WindowOp("w", 60, true, "alpha") }),
                ["close"] = RunTestKit.Ability("close", new EffectOp[] { new PayoffOp() }),
                ["solo"] = RunTestKit.Ability("solo", new EffectOp[] { new DamageOp(1000, "physical") }),
            },
            new()) with
        {
            Combos = combos,
            Clauses = new Dictionary<string, ClauseDef>(StringComparer.Ordinal) { ["pact"] = PactClause },
        };
    }

    private static readonly Command[] ComboAt20 = { new CastCommand(10, Player, "open", Enemy), new CastCommand(20, Partner, "close", Enemy) };

    private static readonly Command[] SoloAt20 = { new CastCommand(20, Player, "solo", Enemy) };

    [Fact]
    public void Combo_depth_run_As_combo_tick_never_makes_run_Bs_same_tick_solo_hit_count_as_a_combo()
    {
        var pact = new RunOptions { ClauseId = "pact" };
        var outs = Interleave(
            new[] { RunRoot.CreateRun(Seed, Party, Dungeon, PactContent(), pact), RunRoot.CreateRun(Seed, Party, Dungeon, PactContent(), pact) },
            new[] { ComboAt20, SoloAt20 },
            30);

        Assert.Contains(outs[0].Events, e => e.Type == "combo");
        Assert.Equal(new long[] { 1200 }, RunTestKit.Damages(outs[0].Events));
        Assert.Equal(new long[] { 900 }, RunTestKit.Damages(outs[1].Events));
    }

    [Fact]
    public void Active_rules_a_pact_runs_clause_does_not_leak_into_a_clause_less_rules_built_afterwards()
    {
        var pact = RunRoot.CreateRun(Seed, Party, Dungeon, PactContent(), new RunOptions { ClauseId = "pact" });
        var (_, pactEvents) = RunTestKit.StepUntil(pact, 21, SoloAt20);
        Assert.Equal(new long[] { 900 }, RunTestKit.Damages(pactEvents));

        // A clause-less run driven through the lower-level Rules.Make + Tick.Step path afterwards.
        var plain = RunRoot.CreateRun(Seed, Party, Dungeon, Base(new Dictionary<string, AbilityDef>(PactContent().Abilities), new()));
        var rules = Rules.Make(RunWiring.RulesConfigOf(plain.Content));
        var state = plain.State;
        var plainEvents = new List<SimEvent>();
        while (state.Tick <= 20)
        {
            var s = Keyrun.Sim.Core.Tick.Step(state, SoloAt20.Where(c => c.Tick == state.Tick).ToList(), rules);
            state = s.State;
            plainEvents.AddRange(s.Events);
        }
        Assert.Equal(new long[] { 1000 }, RunTestKit.Damages(plainEvents));
    }

    [Fact]
    public void Hush_silences_a_cast_inside_a_pulse_through_step_run_and_not_outside_it()
    {
        var hush = new ClauseDef("hush", "Hush", ClauseKind.Hush,
            new Dictionary<string, long>(StringComparer.Ordinal) { ["intervalTicks"] = 100, ["silenceDurationTicks"] = 20 }, "d");
        var content = Base(new() { ["solo"] = RunTestKit.Ability("solo", new EffectOp[] { new DamageOp(10, "physical") }) }, new()) with
        {
            Clauses = new Dictionary<string, ClauseDef>(StringComparer.Ordinal) { ["hush"] = hush },
        };
        var commands = new Command[] { new CastCommand(50, Player, "solo", Enemy), new CastCommand(105, Player, "solo", Enemy), new CastCommand(130, Player, "solo", Enemy) };

        var (_, withHush) = RunTestKit.StepUntil(RunRoot.CreateRun(Seed, Party, Dungeon, content, new RunOptions { ClauseId = "hush" }), 140, commands);
        var (_, without) = RunTestKit.StepUntil(RunRoot.CreateRun(Seed, Party, Dungeon, content), 140, commands);

        Assert.Equal(new[] { 50, 130 }, withHush.Where(e => e.Type == "damage").Select(e => e.Tick).ToArray());
        Assert.Equal(new[] { 50, 105, 130 }, without.Where(e => e.Type == "damage").Select(e => e.Tick).ToArray());
    }
}
