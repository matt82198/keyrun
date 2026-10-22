using System;
using System.Linq;
using Keyrun.Bots.Shared;
using Keyrun.Content;
using Keyrun.Sim.Core;
using Keyrun.Sim.Encounter;
using Keyrun.Sim.Runs;
using Xunit;

namespace Keyrun.Playtest.Tests;

/// <summary>
/// Finding f-7aa25aa6 (WoW Mythic+: plan the count, pull to 100, then boss), the policy half of f-3dcc702c. A policy party
/// is the same competent group as the scripted one, so it must also count the Enemy Forces bar and pull to 100% before it
/// engages the boss; otherwise, once a key finishes only at 100% forces (#576), every policy run reaches the boss at
/// about 680 of 1000 and cannot finish timed. Driven through <see cref="PlaytestRunner"/> at key 2, seed 1, for two kick
/// plans (the Choir-lesson plan and a kicks-only plan), each with the planner on (the default) and off (the control).
/// </summary>
public sealed class PolicyPullToHundredForcesTests
{
    private static readonly ContentRegistry Registry = ContentLoader.Load();

    private const string KickPlan = "{\"mitigate\": [\"warden\"], \"kicks\": [{\"cast\": \"choir.healCast\", \"kickers\": [\"cinder\", \"chanter\", \"warden\"]}]}";

    private const string KicksOnly = "{\"kicks\": [{\"cast\": \"choir.healCast\", \"kickers\": [\"cinder\", \"chanter\", \"warden\"]}]}";

    /// <summary>The run, and the bar the tick the boss first engaged (null when it never did).</summary>
    private sealed record Drive(PlaytestResult Result, ForcesReading? AtBossEngage, int BossEngageTick);

    private static Drive Play(string policyJson, bool? pullToForces)
    {
        var plan = ForcesPlan.Of(Registry)!;
        ForcesReading? atBoss = null;
        var bossTick = -1;
        var result = PlaytestRunner.Play(new PlaytestOptions(2, 1, PolicyReader.Parse(policyJson, Registry))
        {
            PullToForces = pullToForces,
            Observe = (tick, run, _) =>
            {
                if (atBoss is not null)
                {
                    return;
                }
                var boss = run.Dungeon.Pulls[^1].Spawns.Select(s => s.ActorId).ToHashSet();
                if (run.State.Actors.Values.Any(a => boss.Contains(a.Id) && a.Alive && !a.Idle))
                {
                    atBoss = Forces.Read(run.State, plan.PointsByEnemy, plan.Required);
                    bossTick = tick;
                }
            },
        }, Registry);
        return new Drive(result, atBoss, bossTick);
    }

    public static readonly TheoryData<string> Plans = new()
    {
        KickPlan,
        KicksOnly,
    };

    [Theory(DisplayName = "Policy pull to 100: a policy party engages the boss with the bar full (bp 10000) by default (key 2, seed 1)")]
    [MemberData(nameof(Plans))]
    public void The_bar_is_full_before_the_boss_engages(string plan)
    {
        var r = Play(plan, null);
        Assert.NotNull(r.AtBossEngage);
        Assert.True(r.AtBossEngage!.Full, $"forces at boss engage (tick {r.BossEngageTick}): {r.AtBossEngage.LogLine()}");
        Assert.True(r.AtBossEngage.Points >= 1000, $"forces at boss engage: {r.AtBossEngage.LogLine()}");
        Assert.True(r.AtBossEngage.PercentBp >= 10000, $"forces bp at boss engage: {r.AtBossEngage.PercentBp}");
    }

    [Theory(DisplayName = "Policy pull to 100: a policy party that kicks ends the run timed, within par (key 2, seed 1)")]
    [MemberData(nameof(Plans))]
    public void The_run_ends_timed_within_par(string plan)
    {
        var r = Play(plan, null).Result;
        var par = r.Setup.Content.ParTicks ?? 24000;
        Assert.Equal(RunResult.Timed, r.Run.RunEnd.Result);
        Assert.True(r.Run.RunEnd.ResultTick <= par, $"run_end at tick {r.Run.RunEnd.ResultTick}, par {par}");
    }

    [Theory(DisplayName = "Policy pull to 100: with the plan off the same policy run reaches the boss short of the bar (control)")]
    [MemberData(nameof(Plans))]
    public void Without_the_plan_the_bar_is_short(string plan)
    {
        var r = Play(plan, false);
        Assert.NotNull(r.AtBossEngage);
        Assert.False(r.AtBossEngage!.Full, $"forces at boss engage without the plan: {r.AtBossEngage.LogLine()}");
    }
}
