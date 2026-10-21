using System;
using System.Collections.Generic;
using System.Linq;
using Keyrun.Host;
using Keyrun.Sim.Core;
using Keyrun.Sim.Encounter;
using Xunit;

namespace Keyrun.Playtest.Tests;

/// <summary>
/// <see cref="SeatAbandonPlan"/>, the <c>--abandon-seat</c> value (RULING R-415-D: a leaver mid-pull is a scenario, not
/// a number). The fixed form <c>A@T</c> leaves at run tick T, as since lane 3J. The pull-relative form
/// <c>A@pullN+O</c> leaves O ticks after pull N's <c>pull_start</c>, so run F's "a seat abandons during the first pull"
/// holds whatever pacing the run has: the plan resolves to no tick until that pull has started. The health form
/// <c>A@pullN.hpP+O</c> (finding f-0fd3e6fb) leaves O ticks after pull N's standing enemies fall to P percent of their
/// health, so the leave is late in the pull however long its fight runs (RunFAbandonPacingTests).
/// </summary>
public sealed class SeatAbandonPlanTests
{
    private static SimEvent PullStart(int tick, int pullIndex) =>
        new(tick, "pull_start", ("pullIndex", JsValue.Int(pullIndex)));

    [Fact(DisplayName = "the fixed form A@T still parses and resolves to T with no anchor")]
    public void Fixed_form_parses_and_resolves_to_its_tick()
    {
        var plan = SeatAbandonPlan.Parse("2@1700");
        Assert.Equal(2, plan.ActorId);
        Assert.Equal(1700, plan.Tick);
        Assert.Null(plan.AnchorPull);
        Assert.Equal(1700, plan.ResolveTick(null));
        Assert.Equal(1700, plan.ResolveTick(76));
    }

    [Fact(DisplayName = "the pull-relative form A@pullN+O parses the actor, the pull and the offset")]
    public void Relative_form_parses()
    {
        var plan = SeatAbandonPlan.Parse("2@pull0+1350");
        Assert.Equal(2, plan.ActorId);
        Assert.Equal(0, plan.AnchorPull);
        Assert.Equal(1350, plan.Tick);
    }

    [Fact(DisplayName = "the pull-relative form resolves to no tick before its pull starts, then to start + offset")]
    public void Relative_form_resolves_from_its_pull_start()
    {
        var plan = SeatAbandonPlan.Parse("2@pull0+1350");
        Assert.Null(plan.ResolveTick(null));
        Assert.Equal(1426, plan.ResolveTick(76));
        Assert.Equal(1430, plan.ResolveTick(80));
    }

    [Fact(DisplayName = "the anchor is the tick of the named pull's pull_start, never another pull's or another event's")]
    public void Anchor_is_the_named_pulls_start()
    {
        var plan = SeatAbandonPlan.Parse("3@pull1+20");
        Assert.Null(plan.AnchorFrom(new[] { PullStart(76, 0) }));
        Assert.Null(plan.AnchorFrom(new[] { new SimEvent(1645, "pull_clear", ("pullIndex", JsValue.Int(1))) }));
        Assert.Equal(1645, plan.AnchorFrom(new[] { PullStart(76, 0), PullStart(1645, 1) }));
        Assert.Null(SeatAbandonPlan.Parse("3@500").AnchorFrom(new[] { PullStart(1645, 1) }));
    }

    // A two-pull dungeon: pull 0 spawns 100 and 101 (1000 health each), pull 1 spawns 102.
    private static readonly DungeonDef TwoPulls = new("d", new[]
    {
        new PullDef("p0", new[] { 100, 101 }.Select(id => new PullSpawn(id, "e", new Vec3(0, 0, 0), 0, 1000)).ToList()),
        new PullDef("p1", new[] { new PullSpawn(102, "e", new Vec3(0, 0, 0), 0, 1000) }),
    });

    private static Dictionary<int, ActorState> Health(params (int Id, long Hp)[] actors) =>
        actors.ToDictionary(a => a.Id, a => new ActorState { Id = a.Id, Kind = ActorKind.Enemy, ClassId = "e", Health = a.Hp, MaxHealth = 1000, Alive = a.Hp > 0 });

    [Fact(DisplayName = "the health form A@pullN.hpP+O parses the actor, the pull, the share and the offset")]
    public void Health_form_parses()
    {
        var plan = SeatAbandonPlan.Parse("2@pull0.hp10+1");
        Assert.Equal(2, plan.ActorId);
        Assert.Equal(0, plan.AnchorPull);
        Assert.Equal(10, plan.HealthPercent);
        Assert.Equal(1, plan.Tick);
        Assert.Null(plan.ResolveTick(null));
        Assert.Equal(1458, plan.ResolveTick(1457));
        Assert.Null(SeatAbandonPlan.Parse("2@pull0+1350").HealthPercent);
    }

    [Fact(DisplayName = "the health form anchors on the first step its pull's standing enemies are at or below P of their health")]
    public void Health_form_anchors_at_its_share()
    {
        var plan = SeatAbandonPlan.Parse("2@pull0.hp10+1");
        var none = Array.Empty<SimEvent>();
        // 1000 + 101 of 2000 is above a tenth; 0 + 200 is exactly a tenth; 0 + 199 below it.
        Assert.Null(plan.AnchorAt(900, none, TwoPulls, Health((100, 1000), (101, 101), (102, 1000))));
        Assert.Null(plan.AnchorAt(900, none, TwoPulls, Health((100, 0), (101, 201), (102, 1000))));
        Assert.Equal(901, plan.AnchorAt(901, none, TwoPulls, Health((100, 0), (101, 200), (102, 1000))));
        Assert.Equal(902, plan.AnchorAt(902, none, TwoPulls, Health((100, 0), (101, 199), (102, 1000))));
    }

    [Fact(DisplayName = "the health form never anchors on another pull's health, a cleared pull or an unspawned one")]
    public void Health_form_ignores_other_pulls_and_cleared_pulls()
    {
        var plan = SeatAbandonPlan.Parse("2@pull0.hp10+1");
        var none = Array.Empty<SimEvent>();
        // Pull 1 is low but pull 0 is full: no anchor. Pull 0 all dead (cleared): no anchor, the leave would be past it.
        Assert.Null(plan.AnchorAt(500, none, TwoPulls, Health((100, 1000), (101, 1000), (102, 1))));
        Assert.Null(plan.AnchorAt(500, none, TwoPulls, Health((100, 0), (101, 0), (102, 1000))));
        // Unspawned spawns count at full health.
        Assert.Null(plan.AnchorAt(500, none, TwoPulls, Health((100, 1))));
        // A pull_start is not the health form's anchor, and the pull_start form ignores health.
        Assert.Null(plan.AnchorFrom(new[] { PullStart(76, 0) }));
        Assert.Equal(76, SeatAbandonPlan.Parse("2@pull0+1350").AnchorAt(76, new[] { PullStart(76, 0) }, TwoPulls, Health((100, 1))));
    }

    [Theory(DisplayName = "malformed values are refused")]
    [InlineData("2@pull0.hp+1")]
    [InlineData("2@pull0.hp10")]
    [InlineData("2@pull0.hp10+0")]
    [InlineData("2@pull0.hp0+1")]
    [InlineData("2@pull0.hp100+1")]
    [InlineData("2@pull.hp10+1")]
    [InlineData("2@pull0.hpx+1")]
    [InlineData("2@pull0.hp10.hp5+1")]
    [InlineData("2")]
    [InlineData("0@100")]
    [InlineData("2@")]
    [InlineData("2@pull")]
    [InlineData("2@pull0")]
    [InlineData("2@pull+5")]
    [InlineData("2@pull0-5")]
    [InlineData("2@pullx+5")]
    [InlineData("2@pull0+")]
    [InlineData("2@pull0+-5")]
    public void Malformed_values_are_refused(string raw)
    {
        Assert.Throws<ArgumentException>(() => SeatAbandonPlan.Parse(raw));
    }
}
