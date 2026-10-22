using System;
using System.Collections.Generic;
using System.Linq;
using Keyrun.Bots.Shared;
using Keyrun.Content;
using Keyrun.Host;
using Keyrun.Sim.Core;
using Keyrun.Sim.Encounter;
using Keyrun.Sim.Runs;
using Xunit;

namespace Keyrun.Playtest.Tests;

/// <summary>
/// Finding f-3dcc702c (WoW Mythic+: plan the count, pull to 100, then boss). The autorun leader keeps pulling the layout's
/// packs, in route order and backtracking for any left behind, while the Enemy Forces bar is below 100%, and only walks
/// into the boss arena once it is full (Forces.Read(...).Full). Without it the autorun reaches the boss with the optional
/// packs skipped and, once a key completes only at 100% forces and boss dead, never finishes. Driven through
/// <see cref="PlaytestRunner"/> at key 2, seed 1, the headless replica of the autorun.
/// </summary>
public sealed class PullToHundredForcesTests
{
    private static readonly ContentRegistry Registry = ContentLoader.Load();

    /// <summary>The run, and the bar the tick the boss first engaged (null when it never did).</summary>
    private sealed record Autorun(PlaytestResult Result, ForcesReading? AtBossEngage, int BossEngageTick);

    private static readonly Lazy<Autorun> Default = new(() => Drive(pullToForces: true));

    private static Autorun Drive(bool pullToForces)
    {
        var plan = ForcesPlan.Of(Registry)!;
        var table = plan.PointsByEnemy;
        var required = plan.Required;
        ForcesReading? atBoss = null;
        var bossTick = -1;
        var result = PlaytestRunner.Play(new PlaytestOptions(2, 1, null)
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
                    atBoss = Forces.Read(run.State, table, required);
                    bossTick = tick;
                }
            },
        }, Registry);
        return new Autorun(result, atBoss, bossTick);
    }

    [Fact(DisplayName = "Pull to 100: the autorun fills the forces bar before the boss engages (key 2, seed 1)")]
    public void The_bar_is_full_before_the_boss_engages()
    {
        var r = Default.Value;
        Assert.NotNull(r.AtBossEngage);
        Assert.True(r.AtBossEngage!.Full, $"forces at boss engage (tick {r.BossEngageTick}): {r.AtBossEngage.LogLine()}");
    }

    [Fact(DisplayName = "Pull to 100: the run ends with the boss dead, not abandoned and not unfinished")]
    public void The_run_ends_with_the_boss_dead()
    {
        var r = Default.Value.Result.Run;
        Assert.NotNull(r.RunEnd.ResultTick);
        Assert.Contains(r.RunEnd.Result?.ToJs(), new[] { "timed", "depleted" });
        var boss = r.Dungeon.Pulls[^1].Spawns.Select(s => s.ActorId).ToList();
        Assert.All(boss, id => Assert.False(r.State.Actors[id].Alive));
    }

    [Fact(DisplayName = "Pull to 100: the run ends within twice par (the pulls cost time, not the run)")]
    public void The_run_ends_within_twice_par()
    {
        var r = Default.Value.Result;
        var par = r.Setup.Content.ParTicks ?? 24000;
        Assert.True(r.Run.RunEnd.ResultTick <= 2 * par, $"run_end at tick {r.Run.RunEnd.ResultTick}, par {par}");
    }

    [Fact(DisplayName = "Pull to 100: with the planner off the autorun reaches the boss short of the bar (control)")]
    public void Without_the_planner_the_bar_is_short()
    {
        var r = Drive(pullToForces: false);
        Assert.NotNull(r.AtBossEngage);
        Assert.False(r.AtBossEngage!.Full, $"forces at boss engage without the planner: {r.AtBossEngage.LogLine()}");
    }
}
