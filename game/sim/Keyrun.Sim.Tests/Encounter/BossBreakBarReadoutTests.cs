using System;
using System.Collections.Generic;
using Keyrun.Sim.Core;
using Keyrun.Sim.Encounter;
using Xunit;

namespace Keyrun.Sim.Tests.Encounter;

/// <summary>
/// BREAK-BAR (BIBLE s9 mechanic 2, "share one Break bar"): <see cref="BossBreakBarReadout.For"/> publishes the Break
/// bar the boss unit frame draws. For the armed boss it is the Twins' one shared bar (<see cref="BossConfig.TwinsBar"/>,
/// looked up in <see cref="EncounterState.BreakBars"/>); for any other enemy it is a bar that enemy itself carries; with
/// no such bar in the encounter there is nothing to draw.
/// </summary>
public sealed class BossBreakBarReadoutTests
{
    private const int BossId = 900;
    private const int TwinA = 901;
    private const int TwinB = 902;
    private const string TwinsBarId = "hollowWarden.twinsBar";

    private static BossConfig Config() =>
        new(BossId,
            new[]
            {
                new BossTwinSpec(TwinA, "hollowWarden.twinA", 1000, Vec3.Zero, Array.Empty<ScriptStep>()),
                new BossTwinSpec(TwinB, "hollowWarden.twinB", 1000, Vec3.Zero, Array.Empty<ScriptStep>()),
            },
            new BreakBarSource(TwinsBarId, new[] { TwinA, TwinB }, 100, 40, 20, Array.Empty<BreakBarOnEmptyEffect>()));

    private static EncounterState Armed(params (string Id, BreakBarState Bar)[] bars)
    {
        var record = new JsRecord<BreakBarState>();
        foreach (var (id, bar) in bars)
        {
            record[id] = bar;
        }
        return BossRules.Arm(EncounterState.Create(2, 6000, Clauses.DefaultEncounterRules(2)), Config()) with { BreakBars = record };
    }

    [Fact]
    public void The_armed_boss_publishes_the_twins_one_shared_bar()
    {
        var bar = new BreakBarState(60, 100, new[] { TwinA, TwinB }, 0);
        BossBreakBar? readout = BossBreakBarReadout.For(Armed((TwinsBarId, bar)), BossId);
        Assert.NotNull(readout);
        Assert.Equal(TwinsBarId, readout!.BarId);
        Assert.Equal(60, readout.Bar.Current);
        Assert.Equal(100, readout.Bar.Max);
        Assert.Equal(new[] { TwinA, TwinB }, readout.Bar.ActorIds);
    }

    [Fact]
    public void The_readout_follows_the_sim_value_as_the_bar_drains_and_resets()
    {
        var full = BreakBar.Create(100, new[] { TwinA, TwinB });
        var events = new List<SimEvent>();
        var drained = BreakBar.Drain(full, TwinsBarId, new[] { new BreakBarInput(10, 1, BreakBarTag.Interrupt, 40) }, 10, events.Add);
        Assert.Equal(60, BossBreakBarReadout.For(Armed((TwinsBarId, drained)), BossId)!.Bar.Current);

        var broken = BreakBar.Drain(drained, TwinsBarId, new[] { new BreakBarInput(11, 1, BreakBarTag.Interrupt, 80) }, 11, events.Add);
        var atBreak = BossBreakBarReadout.For(Armed((TwinsBarId, broken)), BossId)!;
        Assert.Equal(0, atBreak.Bar.Current);
        Assert.Equal(11 + BreakBar.ResetCooldownTicks, atBreak.Bar.CooldownReadyTick);

        var reset = BreakBar.TickReset(broken, 11 + BreakBar.ResetCooldownTicks);
        Assert.Equal(100, BossBreakBarReadout.For(Armed((TwinsBarId, reset)), BossId)!.Bar.Current);
    }

    [Fact]
    public void Before_the_twins_bar_exists_the_boss_has_nothing_to_draw()
    {
        Assert.Null(BossBreakBarReadout.For(Armed(), BossId));
    }

    [Fact]
    public void Another_bar_in_the_encounter_is_not_the_bosses_bar()
    {
        var other = new BreakBarState(50, 100, new[] { 120, 121 }, 0);
        Assert.Null(BossBreakBarReadout.For(Armed(("pack.bar", other)), BossId));
    }

    [Fact]
    public void An_unarmed_enemy_that_carries_a_bar_itself_publishes_that_bar()
    {
        var own = new BreakBarState(30, 90, new[] { 130, 131 }, 0);
        var encounter = EncounterState.Create(2, 6000, Clauses.DefaultEncounterRules(2));
        var record = new JsRecord<BreakBarState>();
        record["pair.bar"] = own;
        encounter = encounter with { BreakBars = record };
        BossBreakBar? readout = BossBreakBarReadout.For(encounter, 131);
        Assert.NotNull(readout);
        Assert.Equal("pair.bar", readout!.BarId);
        Assert.Equal(30, readout.Bar.Current);
        Assert.Null(BossBreakBarReadout.For(encounter, BossId));
    }
}
