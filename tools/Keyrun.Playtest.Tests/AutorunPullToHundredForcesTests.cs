using System;
using System.Linq;
using Keyrun.Bots.Shared;
using Keyrun.Content;
using Keyrun.Sim.Encounter;
using Keyrun.Sim.Runs;
using Xunit;
using Xunit.Abstractions;

namespace Keyrun.Playtest.Tests;

/// <summary>
/// R-FORCES-COMPLETE-1 with f-7aa25aa6 / f-3dcc702c: the scripted autorun (PullToForces on by default) fills the Enemy
/// Forces bar to 100% BEFORE the final boss engages and the run still ends Timed inside the content's par. Hollow Vault is
/// one boss; the Sunken Bellfoundry is three segments, so its check is on the final segment boss. Key 2, seed 1.
/// </summary>
public sealed class AutorunPullToHundredForcesTests
{
    private static readonly ContentRegistry Registry = ContentLoader.Load();

    private readonly ITestOutputHelper _out;

    /// <summary>Creates the test with xUnit's output sink (measured values are printed for the PR).</summary>
    /// <param name="output">The sink.</param>
    public AutorunPullToHundredForcesTests(ITestOutputHelper output) => _out = output;

    [Theory(DisplayName = "Autorun pull to 100: forces >= 100% before the final boss engages and the run ends Timed within par (key 2, seed 1)")]
    [InlineData("hollow-vault")]
    [InlineData("sunken-bellfoundry")]
    public void Bar_is_full_before_the_final_boss_and_the_run_is_timed(string dungeon)
    {
        var plan = ForcesPlan.Of(Registry)!;
        var finals = SeededRoster.SegmentBosses(Registry, dungeon);
        var finalBoss = finals[^1];
        ForcesReading? atFinal = null;
        var finalTick = -1;
        var result = PlaytestRunner.Play(new PlaytestOptions(2, 1, null)
        {
            DungeonId = dungeon,
            Observe = (tick, run, _) =>
            {
                if (atFinal is null && run.State.Actors.Values.Any(a => a.Alive && !a.Idle && a.ClassId == finalBoss))
                {
                    atFinal = Forces.Read(run.State, plan.PointsByEnemy, plan.Required);
                    finalTick = tick;
                }
            },
        }, Registry);
        var par = Registry.Dungeons[dungeon].ParTicks;
        _out.WriteLine($"{dungeon}: final boss engaged tick {finalTick} forces {atFinal?.LogLine()}; run_end {result.Run.RunEnd.Result} tick {result.Run.RunEnd.ResultTick} par {par}");
        Assert.NotNull(atFinal);
        Assert.True(atFinal!.Points >= plan.Required, $"forces at final boss engage (tick {finalTick}): {atFinal.LogLine()}");
        Assert.Equal(RunResult.Timed, result.Run.RunEnd.Result);
        Assert.True(result.Run.RunEnd.ResultTick <= par, $"run_end at tick {result.Run.RunEnd.ResultTick}, par {par}");
    }
}
