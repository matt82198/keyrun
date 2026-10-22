using System;
using System.Collections.Generic;
using System.Linq;
using Keyrun.Sim.Combat;
using Keyrun.Sim.Core;
using Keyrun.Sim.Encounter;
using Keyrun.Sim.Runs;
using Keyrun.Tools.Replay;
using Xunit;

namespace Keyrun.Sim.Tests;

/// <summary>
/// Lane TALENT-PICKS (BIBLE s6 "Talents: forks on the five"): the talents the player picks on the between-runs screen
/// reach the run. The screen resolves its picks into the human's patched AbilityDefs and hands them to
/// <see cref="ScreenState.Queue"/>; the <see cref="QueueRequest"/> carries them, and <see cref="QueueRequest.ToRunOptions"/>
/// emits them as <see cref="RunOptions.Loadouts"/>, which is exactly what SimDriver.StartRun passes to
/// <see cref="RunRoot.CreateRun"/>. Before this lane the request had no loadout, so every picked talent was dropped.
/// </summary>
public sealed class QueuedTalentLoadoutTests
{
    private const long Seed = 7;
    private const string Dungeon = "hollow-vault";
    private const string Ability = "warden.guardBreak";

    private static RunSetup Setup() => RunSetup.Load(OracleFixtures.PathOf("hollow-vault-k2.setup.json"), Seed);

    private static int WardenId(RunSetup setup) => setup.Party.Single(m => m.ClassId == "warden").Id;

    private static IReadOnlyList<string> ClauseIds(RunSetup setup) => setup.Content.Clauses!.Keys.ToList();

    // SimDriver.StartRun: CreateRun with the queued request's options.
    private static Run CreateQueued(RunSetup setup, QueueRequest request) =>
        RunRoot.CreateRun(Seed, setup.Party, setup.Dungeon, setup.Content, request.ToRunOptions());

    private static IReadOnlyDictionary<int, IReadOnlyDictionary<string, AbilityDef>> Loadout(int actorId, AbilityDef def) =>
        new Dictionary<int, IReadOnlyDictionary<string, AbilityDef>>
        {
            [actorId] = new Dictionary<string, AbilityDef>(StringComparer.Ordinal) { [def.Id] = def },
        };

    [Fact(DisplayName = "Talent picks: a picked talent's cooldown and cost reach the queued run's ability")]
    public void Picked_talent_reaches_the_run()
    {
        var setup = Setup();
        var baseDef = setup.Content.Abilities[Ability];
        var talented = baseDef with { CooldownTicks = baseDef.CooldownTicks + 120, Cost = baseDef.Cost with { Amount = baseDef.Cost.Amount + 7 } };
        var state = ScreenState.Open(Dungeon, ClauseIds(setup), 3, 0, ScreenState.NoClauseStartKey);

        var (locked, request) = state.Queue(Loadout(WardenId(setup), talented));

        Assert.True(locked.Locked);
        Assert.NotNull(request);
        var run = CreateQueued(setup, request!);
        Assert.Equal(baseDef.CooldownTicks + 120, run.Content.Abilities[Ability].CooldownTicks);
        Assert.Equal(baseDef.Cost.Amount + 7, run.Content.Abilities[Ability].Cost.Amount);
        Assert.Equal(request!.ClauseId, run.Encounter.Rules.ActiveClauseId);
    }

    [Fact(DisplayName = "Talent picks: with no pick the queued run keeps the base ability and the same content")]
    public void No_pick_keeps_the_base_value()
    {
        var setup = Setup();
        var baseDef = setup.Content.Abilities[Ability];
        var state = ScreenState.Open(Dungeon, ClauseIds(setup), 3, 0, ScreenState.NoClauseStartKey);

        var (_, request) = state.Queue();

        Assert.Null(request!.ToRunOptions().Loadouts);
        var run = CreateQueued(setup, request);
        Assert.Same(setup.Content.Abilities, run.Content.Abilities);
        Assert.Equal(baseDef.CooldownTicks, run.Content.Abilities[Ability].CooldownTicks);
        Assert.Equal(baseDef.Cost.Amount, run.Content.Abilities[Ability].Cost.Amount);
    }

    [Fact(DisplayName = "Talent picks: an empty build queues no loadout")]
    public void Empty_loadout_queues_none()
    {
        var setup = Setup();
        var empty = new Dictionary<int, IReadOnlyDictionary<string, AbilityDef>>();
        var (_, request) = ScreenState.Open(Dungeon, ClauseIds(setup), 3, 0, ScreenState.NoClauseStartKey).Queue(empty);

        Assert.Null(request!.ToRunOptions().Loadouts);
        Assert.Same(setup.Content.Abilities, CreateQueued(setup, request).Content.Abilities);
    }

    [Fact(DisplayName = "Talent picks: a second Queue while locked hands out nothing, picks or not")]
    public void Locked_queue_hands_out_nothing()
    {
        var setup = Setup();
        var talented = setup.Content.Abilities[Ability] with { CooldownTicks = 999 };
        var (locked, _) = ScreenState.Open(Dungeon, ClauseIds(setup), 3, 0, ScreenState.NoClauseStartKey).Queue();

        var (again, request) = locked.Queue(Loadout(WardenId(setup), talented));

        Assert.Same(locked, again);
        Assert.Null(request);
    }

    // Behaviour, not just the map: the run's combat refuses a cast at the talented cooldown it would allow at base.
    private static readonly AbilityDef BaseZap = RunTestKit.Ability("zap", new EffectOp[] { new DamageOp(1, "physical") }, cooldownTicks: 100);

    private static int ZapHits(QueueRequest request)
    {
        var party = new[]
        {
            RunTestKit.Member(1, "warrior", ActorRole.Damage, -2000, 0, 200, 100),
            RunTestKit.Member(2, "target-dummy", ActorRole.Damage, 3000, 0, 300, 0),
        };
        var content = RunTestKit.Content() with
        {
            Abilities = new Dictionary<string, AbilityDef>(StringComparer.Ordinal) { ["zap"] = BaseZap },
            Clauses = new Dictionary<string, ClauseDef>(StringComparer.Ordinal),
        };
        var dungeon = RunTestKit.OnePull("empty", "p", RunTestKit.Spawn(11, "boss", 0, 0, 1));
        var options = request.ToRunOptions() with { ClauseId = null };
        var run = RunRoot.CreateRun(42424242, party, dungeon, content, options);
        var commands = new[] { 0, 150, 260 }.Select(t => (Command)new CastCommand(t, 1, "zap", 2)).ToList();
        var (_, events) = RunTestKit.StepUntil(run, 300, commands);
        return events.Count(e => e.Type == "damage");
    }

    [Fact(DisplayName = "Talent picks: the queued talented cooldown refuses the second cast in combat; no pick lands all three")]
    public void Queued_talent_changes_combat()
    {
        var state = ScreenState.Open(Dungeon, new[] { "echo", "tide", "hush", "pact" }, 3, 0, ScreenState.NoClauseStartKey);
        var (_, plain) = state.Queue();
        var (_, talented) = state.Queue(Loadout(1, BaseZap with { CooldownTicks = 250 }));

        Assert.Equal(3, ZapHits(plain!));
        Assert.Equal(2, ZapHits(talented!));
    }
}
