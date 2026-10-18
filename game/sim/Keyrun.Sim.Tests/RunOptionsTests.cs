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
/// Port of tests/sim/integration/run-options.test.ts: <see cref="RunOptions.ClauseId"/> threads into
/// <c>encounter.Rules</c> (fail closed on an unknown id or a second Clause) and <see cref="RunOptions.Loadouts"/>
/// (already-resolved, facet-patched AbilityDefs) merge onto the run's abilities so a cast uses the patched kit.
/// </summary>
public sealed class RunOptionsTests
{
    private const long Seed = 42424242;
    private const int PlayerId = 1;
    private const int DummyId = 2;
    private const int BossId = 11;

    private static readonly ClauseDef Echo = new("echo", "Echo", ClauseKind.Echo, P(("addMaxHealth", 600), ("spawnDelayTicks", 40)), "d");
    private static readonly ClauseDef Tide = new("tide", "Tide", ClauseKind.Tide, P(("driftMmPerTick", 50)), "d");
    private static readonly ClauseDef Hush = new("hush", "Hush", ClauseKind.Hush, P(("intervalTicks", 100), ("silenceDurationTicks", 20)), "d");
    private static readonly ClauseDef Pact = new("pact", "Pact", ClauseKind.Pact, P(("comboDamageBp", 2000), ("soloDamageBp", -1000)), "d");

    private static IReadOnlyDictionary<string, long> P(params (string K, long V)[] kv) => kv.ToDictionary(x => x.K, x => x.V, StringComparer.Ordinal);

    private static readonly PartyMemberSpec[] BaseParty =
    {
        RunTestKit.Member(PlayerId, "warrior", ActorRole.Damage, -2000, 0, 200, 100),
        RunTestKit.Member(DummyId, "target-dummy", ActorRole.Damage, 3000, 0, 300, 0),
    };

    private static readonly DungeonDef Dungeon = RunTestKit.OnePull("golden-pull", "boss", RunTestKit.Spawn(BossId, "boss", 0, 0, 5000));

    private static RunContent BaseContent() => RunTestKit.Content();

    private static RunContent ContentWithClauses() => BaseContent() with
    {
        Clauses = new Dictionary<string, ClauseDef>(StringComparer.Ordinal) { ["echo"] = Echo, ["tide"] = Tide, ["hush"] = Hush, ["pact"] = Pact },
    };

    [Fact]
    public void Defaults_to_no_active_clause_when_options_is_omitted()
    {
        var run = RunRoot.CreateRun(Seed, BaseParty, Dungeon, BaseContent());
        Assert.Null(run.Encounter.Rules.ActiveClauseId);
        Assert.Null(run.Encounter.Rules.ActiveClauseKind);
    }

    [Fact]
    public void Threads_the_clause_id_into_encounter_rules_and_changes_its_hash()
    {
        var noClause = RunRoot.CreateRun(Seed, BaseParty, Dungeon, ContentWithClauses());
        var withEcho = RunRoot.CreateRun(Seed, BaseParty, Dungeon, ContentWithClauses(), new RunOptions { ClauseId = "echo" });
        Assert.Equal("echo", withEcho.Encounter.Rules.ActiveClauseId);
        Assert.Equal(ClauseKind.Echo, withEcho.Encounter.Rules.ActiveClauseKind);
        Assert.NotEqual(Clauses.HashEncounterRules(noClause.Encounter.Rules), Clauses.HashEncounterRules(withEcho.Encounter.Rules));
    }

    [Fact]
    public void Throws_a_named_error_for_an_unknown_clause_id()
    {
        var ex = Assert.Throws<UnknownClauseException>(() =>
            RunRoot.CreateRun(Seed, BaseParty, Dungeon, ContentWithClauses(), new RunOptions { ClauseId = "nope" }));
        Assert.Equal("createRun: options.clauseId \"nope\" is not in content.clauses", ex.Message);
    }

    [Fact]
    public void Propagates_clause_already_active_when_content_rules_already_carry_a_clause()
    {
        var already = Clauses.DefaultEncounterRules(5) with { ActiveClauseId = "tide", ActiveClauseKind = ClauseKind.Tide };
        var content = ContentWithClauses() with { EncounterRules = already };
        Assert.Throws<ClauseAlreadyActiveException>(() =>
            RunRoot.CreateRun(Seed, BaseParty, Dungeon, content, new RunOptions { ClauseId = "pact" }));
    }

    [Fact]
    public void Each_clause_computes_different_output_once_threaded()
    {
        var baseRules = RunRoot.CreateRun(Seed, BaseParty, Dungeon, ContentWithClauses()).Encounter.Rules;
        EncounterRules RulesOf(string id) => RunRoot.CreateRun(Seed, BaseParty, Dungeon, ContentWithClauses(), new RunOptions { ClauseId = id }).Encounter.Rules;

        var tg = new Telegraph(1, 11, "cleave", "circle", Vec3.Zero, 0, 10) { Radius = 1000 };
        Assert.NotEqual(Clauses.DriftTelegraphOrigin(tg, baseRules).Origin.X, Clauses.DriftTelegraphOrigin(tg, RulesOf("tide")).Origin.X);

        var hush = RulesOf("hush");
        Assert.Contains(Enumerable.Range(0, 200), t => Clauses.ShouldSilence(t, hush));
        Assert.DoesNotContain(Enumerable.Range(0, 200), t => Clauses.ShouldSilence(t, baseRules));

        var pact = RulesOf("pact");
        Assert.True(Clauses.ApplyPactDamageBp(1000, true, pact) > 1000);
        Assert.True(Clauses.ApplyPactDamageBp(1000, false, pact) < 1000);
        Assert.Equal(1000, Clauses.ApplyPactDamageBp(1000, true, baseRules));

        var interrupt = new SimEvent(5, "interrupt", ("actorId", JsValue.Int(PlayerId)), ("targetId", JsValue.Int(DummyId)));
        Assert.Single(Clauses.PendingEchoSpawns(new[] { interrupt }, RulesOf("echo")));
        Assert.Empty(Clauses.PendingEchoSpawns(new[] { interrupt }, baseRules));
    }

    private static readonly AbilityDef BaseZap = RunTestKit.Ability("zap", new EffectOp[] { new DamageOp(1, "physical") }, cooldownTicks: 100);
    private static readonly AbilityDef TalentedZap = BaseZap with { CooldownTicks = 250 };

    private static RunContent ZapContent() => BaseContent() with
    {
        Abilities = new Dictionary<string, AbilityDef>(StringComparer.Ordinal) { ["zap"] = BaseZap },
    };

    private static readonly DungeonDef ZapDungeon = RunTestKit.OnePull("empty", "p", RunTestKit.Spawn(BossId, "boss", 0, 0, 1));

    private static int RunZapSchedule(RunOptions? options)
    {
        var run = RunRoot.CreateRun(Seed, BaseParty, ZapDungeon, ZapContent(), options);
        var commands = new[] { 0, 150, 260 }.Select(t => (Command)new CastCommand(t, PlayerId, "zap", DummyId)).ToList();
        var (_, events) = RunTestKit.StepUntil(run, 300, commands);
        return events.Count(e => e.Type == "damage");
    }

    [Fact]
    public void With_no_loadout_the_base_cooldown_lets_all_three_casts_land() => Assert.Equal(3, RunZapSchedule(null));

    [Fact]
    public void With_a_loadout_the_second_cast_is_refused_at_the_talented_cooldown()
    {
        var options = new RunOptions
        {
            Loadouts = new Dictionary<int, IReadOnlyDictionary<string, AbilityDef>> { [PlayerId] = new Dictionary<string, AbilityDef>(StringComparer.Ordinal) { ["zap"] = TalentedZap } },
        };
        Assert.Equal(2, RunZapSchedule(options));
    }

    [Fact]
    public void Throws_a_named_error_for_a_loadout_keyed_to_an_actor_not_in_the_party()
    {
        var options = new RunOptions
        {
            Loadouts = new Dictionary<int, IReadOnlyDictionary<string, AbilityDef>> { [999] = new Dictionary<string, AbilityDef>(StringComparer.Ordinal) { ["zap"] = TalentedZap } },
        };
        var ex = Assert.Throws<UnknownLoadoutActorException>(() => RunRoot.CreateRun(Seed, BaseParty, ZapDungeon, ZapContent(), options));
        Assert.Equal("createRun: options.loadouts has an entry for actorId 999, which is not in party", ex.Message);
    }

    [Fact]
    public void Throws_a_named_error_for_a_loadout_patching_an_ability_absent_from_content()
    {
        var options = new RunOptions
        {
            Loadouts = new Dictionary<int, IReadOnlyDictionary<string, AbilityDef>> { [PlayerId] = new Dictionary<string, AbilityDef>(StringComparer.Ordinal) { ["not-a-real-ability"] = TalentedZap } },
        };
        var ex = Assert.Throws<UnknownLoadoutAbilityException>(() => RunRoot.CreateRun(Seed, BaseParty, ZapDungeon, ZapContent(), options));
        Assert.Equal("createRun: options.loadouts[1] patches ability \"not-a-real-ability\", which is not in content.abilities", ex.Message);
    }

    [Fact]
    public void Default_options_keep_the_same_content_reference()
    {
        var content = BaseContent();
        Assert.Same(content, RunRoot.CreateRun(Seed, BaseParty, Dungeon, content, null).Content);
        Assert.Same(content, RunRoot.CreateRun(Seed, BaseParty, Dungeon, content, new RunOptions()).Content);
    }

    [Fact]
    public void The_later_party_member_wins_when_two_loadouts_patch_the_same_ability()
    {
        var first = BaseZap with { CooldownTicks = 111 };
        var second = BaseZap with { CooldownTicks = 222 };
        var options = new RunOptions
        {
            Loadouts = new Dictionary<int, IReadOnlyDictionary<string, AbilityDef>>
            {
                [DummyId] = new Dictionary<string, AbilityDef>(StringComparer.Ordinal) { ["zap"] = second },
                [PlayerId] = new Dictionary<string, AbilityDef>(StringComparer.Ordinal) { ["zap"] = first },
            },
        };
        var run = RunRoot.CreateRun(Seed, BaseParty, ZapDungeon, ZapContent(), options);
        Assert.Equal(222, run.Content.Abilities["zap"].CooldownTicks);
    }

    [Fact]
    public void A_facet_patched_window_with_its_setup_class_reaches_combat_through_the_loadout_layer()
    {
        // Facet "Window effectsAppend with setupClass" (ENGINE-PLAN 2B FACET RUNTIME): the host resolves the facet
        // into the AbilityDef; the run root's loadout layer is what carries it into RulesConfig.
        var open = RunTestKit.Ability("open", new EffectOp[] { new DamageOp(1, "physical") });
        var patched = open with { Effects = open.Effects.Append(new WindowOp("w", 60, true, "warrior")).ToList() };
        var content = BaseContent() with
        {
            Abilities = new Dictionary<string, AbilityDef>(StringComparer.Ordinal) { ["open"] = open },
        };
        var options = new RunOptions
        {
            Loadouts = new Dictionary<int, IReadOnlyDictionary<string, AbilityDef>> { [PlayerId] = new Dictionary<string, AbilityDef>(StringComparer.Ordinal) { ["open"] = patched } },
        };

        var plain = RunTestKit.StepUntil(RunRoot.CreateRun(Seed, BaseParty, Dungeon, content), 3, new Command[] { new CastCommand(0, PlayerId, "open", BossId) });
        var facet = RunTestKit.StepUntil(RunRoot.CreateRun(Seed, BaseParty, Dungeon, content, options), 3, new Command[] { new CastCommand(0, PlayerId, "open", BossId) });

        Assert.DoesNotContain(plain.Events, e => e.Type == "window_opened");
        var opened = Assert.Single(facet.Events, e => e.Type == "window_opened");
        Assert.Contains("\"setupClass\":\"warrior\"", opened.ToCanonicalJson());
    }
}
