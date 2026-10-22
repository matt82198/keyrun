using System.Collections.Generic;
using System.Linq;
using Keyrun.Bots;
using Keyrun.Bots.Shared;
using Keyrun.Content;
using Keyrun.Host;
using Keyrun.Sim.Core;
using Keyrun.Sim.Encounter;
using Keyrun.Sim.Runs;
using Xunit;

namespace Keyrun.Bots.Tests;

/// <summary>
/// G2.4 K3a: the host's bot command source stamps every K2 receipt with the playtester mode. The host has no plan, so
/// it records the default (guided) unless a caller sets <see cref="BotCommandSource.Mode"/>.
/// </summary>
public sealed class PlaytesterModeReceiptTests
{
    private const long Seed = 1;
    private const int Key = 2;
    private const string DungeonId = "hollow-vault";
    private const int Ticks = 1200;

    private static readonly ContentRegistry Registry = ContentLoader.Load();

    private sealed class CollectingSink : IBotReceiptSink
    {
        public List<BotReceipt> Receipts { get; } = new();

        public void Record(BotReceipt receipt) => Receipts.Add(receipt);
    }

    private static List<BotReceipt> Drive(PlaytesterMode? mode)
    {
        var setup = HostRunSetup.Build(Registry, DungeonId, Key, "warden", withBots: true);
        var start = setup.StartFor(null, null, Registry, "warden");
        var options = (start.Options ?? new RunOptions()) with { RotationIndex = setup.RotationIndex };
        var party = setup.PartyFor(setup.Dungeon);
        options = setup.WithBotLoadouts(options, party) ?? options;
        var created = RunRoot.CreateRun(Seed, party, setup.Dungeon, setup.Content, options);
        var run = created with { Encounter = BossRules.Arm(created.Encounter, setup.Boss) };

        var warden = new ScriptedWarden();
        var bots = new BotCommandSource(setup.BotActorIds, KitRegistry.From(Registry), Seed, BotCommandSource.DefaultSkillDial, null);
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
        return sink.Receipts;
    }

    [Fact(DisplayName = "Host receipts: the default mode is guided when no mode is set")]
    public void Host_receipts_default_to_guided()
    {
        var receipts = Drive(mode: null);
        Assert.True(receipts.Count > 50, $"vacuous run: only {receipts.Count} receipts");
        Assert.All(receipts, r => Assert.Equal("guided", r.Mode));
    }

    [Fact(DisplayName = "Host receipts: a blind-mode command source stamps blind on every receipt")]
    public void Host_receipts_follow_the_mode_set_on_the_source()
    {
        var receipts = Drive(PlaytesterMode.Blind);
        Assert.True(receipts.Count > 50, $"vacuous run: only {receipts.Count} receipts");
        Assert.All(receipts, r => Assert.Equal("blind", r.Mode));
        Assert.Equal(receipts.Count, receipts.Count(r => r.Mode == "blind"));
    }
}
