using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Keyrun.Bots.Guides;
using Keyrun.Bots.Roles;
using Keyrun.Bots.Shared;
using Keyrun.Content;
using Keyrun.Host;
using Keyrun.Sim.Core;
using Keyrun.Sim.Encounter;
using Keyrun.Sim.Runs;
using Xunit;
using static Keyrun.Bots.Tests.RolesTestKit;

namespace Keyrun.Bots.Tests;

/// <summary>
/// G2.4 K3b (design/PLAYTESTER-GUIDE.md sections 3 and 4): a Guided Warden plays the authored guide
/// (game/content/data/guides/warden.json) instead of the scripted tank, and a Blind run is unchanged.
/// </summary>
public sealed class GuidedWardenTests
{
    private const int Tick = 10;
    private const long Seed = 1;
    private const int Key = 2;
    private const string DungeonId = "hollow-vault";
    private const int Ticks = 1200;

    private static readonly ContentRegistry Registry = ContentLoader.Load();

    private static string GuideText()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "game", "content", "data", "guides", "warden.json")))
        {
            dir = dir.Parent;
        }
        Assert.True(dir is not null, "game/content/data/guides/warden.json not found above the test binary");
        return File.ReadAllText(Path.Combine(dir!.FullName, "game", "content", "data", "guides", "warden.json"));
    }

    private static IReadOnlySet<string> AbilityIds() => Registry.Abilities.Keys.ToHashSet(StringComparer.Ordinal);

    private static GuideSet Guide() => GuideSet.Parse(GuideText(), AbilityIds());

    private static ActorState Warden(long resource, long health = 400) =>
        Actor(id: 2, classId: "warden", role: ActorRole.Tank, health: health, maxHealth: 400, resource: resource, maxResource: 100, targetId: 100,
            cooldowns: new[] { ("warden.brace", 999) });

    private static ActorState Enemy(int id, int targetId, string? castId = null, int? castStart = null, int? castEnds = null) =>
        Actor(id: id, kind: ActorKind.Enemy, classId: castId is null ? "pair.melee" : "choir.melee", role: ActorRole.None, z: 0, yaw: 512,
            health: 6720, maxHealth: 6720, resource: 0, maxResource: 0, targetId: targetId, threat: new[] { (2, 100L) },
            castId: castId, castStartTick: castStart, castEndsTick: castEnds);

    private static IReadOnlyList<Command> DecideGuided(GuideSet guides, ActorState self, params ActorState[] others) =>
        new Guides.GuidedTankRole(guides.Require("warden")).Decide(ViewOf(Tick, RealKits, null, new[] { self }.Concat(others).ToArray()), self);

    [Fact(DisplayName = "Guided Warden > the first castable guide entry wins: Vigil when its rule holds and Momentum covers it")]
    public void Guided_warden_casts_first_castable_entry()
    {
        var guide = Guide();
        var cmds = DecideGuided(guide, Warden(resource: 100), Enemy(100, 2), Enemy(101, 2), Enemy(102, 2));
        var first = Assert.IsType<CastCommand>(cmds.First());
        Assert.Equal("warden.vigil", first.AbilityId);
        Assert.Equal("guide:warden:vigil-chain", Reasons.Of(first).RuleId);
    }

    [Fact(DisplayName = "Guided Warden > the next castable guide entry when the first is not castable: Bulwark when Momentum cannot pay for Vigil")]
    public void Guided_warden_falls_through_to_next_castable_entry()
    {
        var guide = Guide();
        var cmds = DecideGuided(guide, Warden(resource: 40), Enemy(100, 2), Enemy(101, 2), Enemy(102, 2));
        var first = Assert.IsType<CastCommand>(cmds.First());
        Assert.Equal("warden.bulwark", first.AbilityId);
        Assert.Equal("guide:warden:bulwark-pressure", Reasons.Of(first).RuleId);
    }

    [Fact(DisplayName = "Guided Warden > kicks a must-kick cast per the kick ledger: Shield Bash on an interruptible Choral Mending in its window")]
    public void Guided_warden_kicks_the_ledger_cast()
    {
        var guide = Guide();
        var cmds = DecideGuided(guide, Warden(resource: 0), Enemy(100, 2, castId: "choir.healCast", castStart: Tick - 74, castEnds: Tick + 4), Enemy(101, 2), Enemy(102, 2));
        var first = Assert.IsType<CastCommand>(cmds.First());
        Assert.Equal("warden.shieldBash", first.AbilityId);
        Assert.Equal("tank:ledger-kick", Reasons.Of(first).RuleId);
    }

    [Fact(DisplayName = "Guided Warden > fails closed on a missing guide: Require names the class")]
    public void Missing_guide_throws_naming_the_class()
    {
        var ex = Assert.Throws<InvalidOperationException>(() => GuideSet.Empty.Require("warden"));
        Assert.Contains("warden", ex.Message, StringComparison.Ordinal);
        var roles = Assert.Throws<InvalidOperationException>(() => RoleSet.Guided(GuideSet.Empty));
        Assert.Contains("warden", roles.Message, StringComparison.Ordinal);
    }

    [Fact(DisplayName = "Guided Warden > fails closed on a malformed guide: an unknown press id is rejected with the rule id")]
    public void Unknown_press_id_is_rejected()
    {
        const string json = "{\"classId\":\"warden\",\"version\":1,\"rotation\":[{\"id\":\"x\",\"press\":\"warden.nope\",\"when\":{\"all\":[]},\"reason\":\"r\"}]}";
        var ex = Assert.Throws<InvalidOperationException>(() => GuideSet.Parse(json, AbilityIds()));
        Assert.Contains("warden.nope", ex.Message, StringComparison.Ordinal);
    }

    private sealed class CollectingSink : IBotReceiptSink
    {
        public List<BotReceipt> Receipts { get; } = new();

        public void Record(BotReceipt receipt) => Receipts.Add(receipt);
    }

    private static (string Proof, List<BotReceipt> Receipts) Drive(PlaytesterMode? mode, GuideSet? guides)
    {
        var setup = HostRunSetup.Build(Registry, DungeonId, Key, "warden", withBots: true);
        var start = setup.StartFor(null, null, Registry, "warden");
        var options = (start.Options ?? new RunOptions()) with { RotationIndex = setup.RotationIndex };
        var party = setup.PartyFor(setup.Dungeon);
        options = setup.WithBotLoadouts(options, party) ?? options;
        var created = RunRoot.CreateRun(Seed, party, setup.Dungeon, setup.Content, options);
        var run = created with { Encounter = BossRules.Arm(created.Encounter, setup.Boss) };

        var warden = new ScriptedWarden();
        var bots = new BotCommandSource(setup.BotActorIds, KitRegistry.From(Registry), Seed, BotCommandSource.DefaultSkillDial, null, guides);
        bots.Reset();
        var sink = new CollectingSink();
        bots.Receipts = sink;
        if (mode is { } m)
        {
            bots.Mode = m;
        }
        while (run.State.Tick < Ticks && run.RunEnd.Result is null)
        {
            var commands = new List<Command>();
            warden.Sample(run.State.Tick, run, commands, new List<int>());
            bots.Sample(run.State.Tick, run, commands);
            bots.TakeReactions();
            run = RunRoot.StepRun(run, commands).Run;
        }
        return (bots.ProofLine(), sink.Receipts);
    }

    [Fact(DisplayName = "Blind mode is unchanged > a Blind run with a guide attached is byte-identical to the run with no guide")]
    public void Blind_run_with_guide_matches_unguided_run()
    {
        var plain = Drive(PlaytesterMode.Blind, guides: null);
        var guided = Drive(PlaytesterMode.Blind, Guide());
        Assert.True(plain.Receipts.Count > 50, $"vacuous run: only {plain.Receipts.Count} receipts");
        Assert.Equal(plain.Proof, guided.Proof);
        Assert.Equal(
            plain.Receipts.Select(r => (r.Tick, r.Mode, r.Reason)),
            guided.Receipts.Select(r => (r.Tick, r.Mode, r.Reason)));
    }
}
