using System;
using Keyrun.Host;
using Keyrun.Sim.Core;
using Xunit;

namespace Keyrun.Playtest.Tests;

/// <summary>
/// <see cref="SeatAbandonPlan"/>, the <c>--abandon-seat</c> value (RULING R-415-D: a leaver mid-pull is a scenario, not
/// a number). The fixed form <c>A@T</c> leaves at run tick T, as since lane 3J. The pull-relative form
/// <c>A@pullN+O</c> leaves O ticks after pull N's <c>pull_start</c>, so run F's "a seat abandons during the first pull"
/// holds whatever pacing the run has: the plan resolves to no tick until that pull has started.
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

    [Theory(DisplayName = "malformed values are refused")]
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
