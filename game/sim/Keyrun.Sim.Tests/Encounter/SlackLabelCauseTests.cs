using System;
using Keyrun.Hud;
using Keyrun.Sim.Core;
using Keyrun.Sim.Encounter;
using Keyrun.Sim.Runs;
using Xunit;

namespace Keyrun.Sim.Tests.Encounter;

/// <summary>
/// Finding f-44796144 (P0, wrong data on screen): the run clock bar's label (<see cref="SlackReading.Label"/>) said
/// "Wiped -- key stays +K" whenever the encounter's Depleted flag was set, but the sim also sets Depleted for a clear
/// that finished past par (<c>Run.cs</c> run_end: <see cref="RunResult.Depleted"/> with every pull cleared). In WoW
/// Mythic+ that is "key not timed", not a wipe. The label now names the run's actual end cause, derived from the
/// run's recorded end (<see cref="RunEndState.Result"/> and whether every pull was cleared), not the Depleted boolean.
/// Every case drives a real run through <see cref="RunRoot.StepRun"/> to its end and reads <see cref="SlackReading.Of"/>.
/// </summary>
public sealed class SlackLabelCauseTests
{
    private const int EnemyId = 10;
    private const int KeyLevel = 7;

    private enum End
    {
        TimedClear,
        OverParClear,
        Wipe,
        Abandon,
    }

    private static Run Live(int parTicks) =>
        RunRoot.CreateRun(
            424242,
            new[]
            {
                RunTestKit.Member(1, "warden", ActorRole.Tank, 0, 0, 500, 100),
                RunTestKit.Member(2, "cinder", ActorRole.Damage, 1000, 0, 250, 100),
            },
            RunTestKit.OnePull("slack-label-fixture", "the-only-pull", RunTestKit.Spawn(EnemyId, "dummy", 2000, 0, 1000)),
            RunTestKit.Content(lockoutTicks: 0) with { KeyLevel = KeyLevel, ParTicks = parTicks });

    private static Run Ended(End end)
    {
        // Par 10 ticks: a clear on tick 700 is past par (the sim finishes it Depleted with every pull cleared).
        var (live, _) = RunTestKit.StepUntil(Live(end == End.OverParClear ? 10 : 100000), 700);
        Assert.Null(live.RunEnd.Result);
        switch (end)
        {
            case End.Abandon:
                return RunRoot.StepRun(live, Array.Empty<Command>(), new StepRunOpts(AbandonRequested: true)).Run;
            case End.Wipe:
                foreach (var id in new[] { 1, 2 })
                {
                    live.State.Actors[id].Alive = false;
                    live.State.Actors[id].Health = 0;
                }
                return RunTestKit.StepUntil(live, live.State.Tick + RunWiring.WipeGraceTicks + 2).Run;
            default:
                live.State.Actors[EnemyId].Alive = false;
                live.State.Actors[EnemyId].Health = 0;
                return RunRoot.StepRun(live, Array.Empty<Command>()).Run;
        }
    }

    [Fact]
    public void An_over_par_clear_says_over_par_and_never_wiped()
    {
        var run = Ended(End.OverParClear);
        Assert.Equal(RunResult.Depleted, run.RunEnd.Result);
        Assert.True(run.Encounter.Depleted);
        Assert.True(run.Encounter.PullIndex >= run.Dungeon.Pulls.Count);

        var label = SlackReading.Of(run).Label;

        Assert.DoesNotContain("Wiped", label, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("Over par -- key stays +7", label);
    }

    [Fact]
    public void A_lasting_wipe_says_wiped_and_the_key_stays()
    {
        var run = Ended(End.Wipe);
        Assert.Equal(RunResult.Depleted, run.RunEnd.Result);
        Assert.True(run.Encounter.PullIndex < run.Dungeon.Pulls.Count);

        Assert.Equal("Wiped -- key stays +7", SlackReading.Of(run).Label);
    }

    [Fact]
    public void A_timed_clear_keeps_the_deaths_reading()
    {
        var run = Ended(End.TimedClear);
        Assert.Equal(RunResult.Timed, run.RunEnd.Result);

        var label = SlackReading.Of(run).Label;

        Assert.StartsWith("Deaths ", label, StringComparison.Ordinal);
        Assert.DoesNotContain("key stays", label, StringComparison.Ordinal);
    }

    [Fact]
    public void An_abandoned_run_says_abandoned_and_never_wiped()
    {
        var run = Ended(End.Abandon);
        Assert.Equal(RunResult.Abandoned, run.RunEnd.Result);

        var label = SlackReading.Of(run).Label;

        Assert.DoesNotContain("Wiped", label, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("Abandoned -- key stays +7", label);
    }

    [Fact]
    public void A_live_run_reads_deaths()
    {
        var (live, _) = RunTestKit.StepUntil(Live(100000), 50);
        Assert.Null(live.RunEnd.Result);

        Assert.StartsWith("Deaths ", SlackReading.Of(live).Label, StringComparison.Ordinal);
    }
}
