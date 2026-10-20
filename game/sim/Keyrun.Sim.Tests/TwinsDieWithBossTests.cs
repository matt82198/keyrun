using System.Collections.Generic;
using System.Linq;
using Keyrun.Sim.Core;
using Keyrun.Sim.Encounter;
using Keyrun.Sim.Runs;
using Xunit;

namespace Keyrun.Sim.Tests;

/// <summary>
/// TWINS-DIE-WITH-BOSS (Matt 2026-10-20: "the Twins die with the Hollow Warden", as WoW ends an encounter on the boss's
/// death): the tick the Hollow Warden dies, every Sundered Twin still standing dies with it, each with its own
/// <c>death</c> event on the boss's death tick, so no Twin is alive at <c>run_end</c> and the run clears. Everything
/// runs through <see cref="RunRoot.StepRun"/> on the armed boss run, killing the boss with real Warden strikes.
/// </summary>
public sealed class TwinsDieWithBossTests
{
    /// <summary>The Warden strikes <paramref name="target"/> every tick from <paramref name="from"/> until <paramref name="until"/>.</summary>
    private static List<Command> Strike(int target, int from, int until) =>
        Enumerable.Range(from, until - from)
            .Select(t => (Command)new CastCommand(t, BossTestKit.Warden, "warden.strike", target))
            .ToList();

    private static bool IsDeathOf(SimEvent e, int actorId) =>
        e.Type == "death" && e.Get("actorId") is JsInt id && id.Value == actorId;

    /// <summary>Steps past the Twins' spawn, puts the boss on 1 hp and strikes it until the run ends.</summary>
    private static (Run Run, List<SimEvent> Events) KillBoss(Run run, int from)
    {
        run.State.Actors[BossTestKit.Boss].Health = 1;
        return BossTestKit.StepUntil(run, from + 60, Strike(BossTestKit.Boss, from, from + 60));
    }

    [Fact(DisplayName = "The Hollow Warden's death kills both standing Twins on its death tick, each with a death event")]
    public void Both_twins_die_on_the_boss_death_tick()
    {
        var (live, _) = BossTestKit.StepUntil(BossTestKit.BuildRun(), 3);
        Assert.True(live.State.Actors[BossTestKit.TwinA].Alive);
        Assert.True(live.State.Actors[BossTestKit.TwinB].Alive);

        var (after, events) = KillBoss(live, 3);

        var bossDeath = Assert.Single(events, e => IsDeathOf(e, BossTestKit.Boss));
        foreach (var twin in new[] { BossTestKit.TwinA, BossTestKit.TwinB })
        {
            var twinDeath = Assert.Single(events, e => IsDeathOf(e, twin));
            Assert.Equal(bossDeath.Tick, twinDeath.Tick);
            Assert.False(after.State.Actors[twin].Alive);
            Assert.Equal(0, after.State.Actors[twin].Health);
        }
    }

    [Fact(DisplayName = "No Twin is alive at run_end and the boss pull clears the run timed")]
    public void No_twin_alive_at_run_end_and_the_run_clears()
    {
        var (live, _) = BossTestKit.StepUntil(BossTestKit.BuildRun(), 3);

        var (after, events) = KillBoss(live, 3);

        var runEnd = Assert.Single(events, e => e.Type == "run_end");
        Assert.Equal(RunResult.Timed, after.RunEnd.Result);
        var deaths = events.Where(e => e.Type == "death").ToList();
        Assert.All(new[] { BossTestKit.TwinA, BossTestKit.TwinB }, twin =>
            Assert.Contains(deaths, d => IsDeathOf(d, twin) && d.Tick <= runEnd.Tick));
        Assert.DoesNotContain(after.State.Actors.Values, a => a.ClassId.StartsWith("hollowWarden.twin", System.StringComparison.Ordinal) && a.Alive);
    }

    [Fact(DisplayName = "Twin B killed first dies once; Twin A, left on low health, dies with the boss (seed 1 key 2 shape)")]
    public void A_twin_killed_first_dies_once_and_the_other_dies_with_the_boss()
    {
        var (live, _) = BossTestKit.StepUntil(BossTestKit.BuildRun(), 3);
        live.State.Actors[BossTestKit.TwinB].Health = 1;
        live.State.Actors[BossTestKit.TwinA].Health = 30;
        var (twinBDown, firstEvents) = BossTestKit.StepUntil(live, 40, Strike(BossTestKit.TwinB, 3, 40));
        Assert.Single(firstEvents, e => IsDeathOf(e, BossTestKit.TwinB));
        Assert.True(twinBDown.State.Actors[BossTestKit.TwinA].Alive);
        Assert.Null(twinBDown.RunEnd.Result);

        var (after, events) = KillBoss(twinBDown, 40);

        var bossDeath = Assert.Single(events, e => IsDeathOf(e, BossTestKit.Boss));
        Assert.DoesNotContain(events, e => IsDeathOf(e, BossTestKit.TwinB));
        Assert.Equal(bossDeath.Tick, Assert.Single(events, e => IsDeathOf(e, BossTestKit.TwinA)).Tick);
        Assert.False(after.State.Actors[BossTestKit.TwinA].Alive);
        Assert.Equal(RunResult.Timed, after.RunEnd.Result);
    }

    [Fact(DisplayName = "Control: while the boss lives the Twins do not die on their own")]
    public void Control_twins_live_while_the_boss_lives()
    {
        var (after, events) = BossTestKit.StepUntil(BossTestKit.BuildRun(), 200);

        Assert.True(after.State.Actors[BossTestKit.Boss].Alive);
        Assert.DoesNotContain(events, e => IsDeathOf(e, BossTestKit.TwinA) || IsDeathOf(e, BossTestKit.TwinB));
        Assert.True(after.State.Actors[BossTestKit.TwinA].Alive);
        Assert.True(after.State.Actors[BossTestKit.TwinB].Alive);
    }
}
