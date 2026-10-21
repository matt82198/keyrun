using System.Linq;
using Keyrun.Sim.Encounter;
using Keyrun.Sim.Runs;
using Xunit;
using Xunit.Abstractions;

namespace Keyrun.Playtest.Tests;

/// <summary>
/// Godot-free replica of game/src/Hud/ScreenSceneTest.ps1's one run (<c>--autorun --ticks $Ticks --solo</c>) under
/// R-BACKFILL-2 (BIBLE s9.1, keyrun #446; ruling R-446-CI, finding f-ea16265e): on a layout run a wipe is a release, not
/// a run end, so the lone Warden's death no longer ends the run inside the old 600-tick budget (game-e2e on f1ff507e:
/// "AUTORUN done ticks=600 ... run=running" and no SCREEN line at all). The scene runs the same host run as
/// HudSceneTest run A (same seed, key, class, solo, pull 1), so it is driven by
/// <see cref="HudSceneWipeReleaseRunsTests.DriveRun"/>; it ends only at the timer cap (<see cref="Timer.CapTicks"/>),
/// depleted and uncleared.
/// <para>
/// The test pins what ScreenSceneTest.ps1 asserts about that end and that $Ticks reaches it: the result is depleted (its
/// "SCREEN bank result=depleted ash=30 total=30" line), the key holds at +2 (its "SCREEN key level=2 locked=0" line, the
/// screen opening at the run's key), and the run-end title names the true cause, "Out of time", not a wipe. The budget
/// must keep the headroom the scene kept past the end before the rule changed, for the screen's key script.
/// </para>
/// </summary>
public sealed class ScreenSceneWipeReleaseRunTests
{
    private const int Key = 2;     // ArenaOptions.DefaultKeyLevel

    /// <summary>ScreenSceneTest.ps1 $Ticks: the scene's <c>--autorun --ticks</c>.</summary>
    internal const int ScreenTicks = 9800;

    /// <summary>
    /// Ticks the scene keeps past the run end: the header's ROOM-LOCK-IN D27 measurement before the rule changed (depleted
    /// run_end at 491 of 600). The screen's key script needs 2 wait frames plus 14 keys, one per frame, after the bank.
    /// </summary>
    private const int EndHeadroomTicks = 600 - 491;

    private readonly ITestOutputHelper _output;

    /// <summary>Creates the test with xUnit's output sink (the measured end tick is printed for the PR).</summary>
    /// <param name="output">The sink.</param>
    public ScreenSceneWipeReleaseRunTests(ITestOutputHelper output) => _output = output;

    /// <summary>
    /// The solo Warden wipes and releases, the run ends at the timer cap depleted with the key held at +2 and the
    /// "Out of time" title, and ScreenSceneTest's $Ticks reaches that end with the old headroom to spare.
    /// </summary>
    [Fact(DisplayName = "ScreenSceneTest: the solo wipe releases, the run ends at the timer cap, inside $Ticks")]
    public void Screen_scene_run_reaches_its_cap_end_inside_its_budget()
    {
        var r = HudSceneWipeReleaseRunsTests.DriveRun(solo: true, startPull: 1, request: null, progression: null, abandon: null, idle: false, budget: ScreenTicks);
        Assert.True(r.AtEnd is not null, "the ScreenSceneTest run never ended");
        var end = r.AtEnd!;
        var endTick = end.RunEnd.ResultTick!.Value;
        var move = KeyMove.ForRun(end)!;
        _output.WriteLine($"ScreenSceneTest run: end={endTick} result={end.RunEnd.Result} ash={Ash.ForResult(end.RunEnd.Result!.Value)} key from={move.From} to={move.To} title=\"{RunEndTitle.ForRun(end, 0, 0)}\" budget={ScreenTicks}");

        // What the scene asserts about the end: "SCREEN bank result=depleted ash=30" and "SCREEN key level=2".
        Assert.Equal(RunResult.Depleted, end.RunEnd.Result);
        Assert.Equal(30, Ash.ForResult(RunResult.Depleted));
        Assert.Equal(Key, move.From);
        Assert.Equal(Key, move.To);

        // The true cause: the timer cap after a release, never the wipe.
        Assert.True(end.Encounter.PullIndex < end.Dungeon.Pulls.Count, "the run cleared its pulls; the cap end is an uncleared one");
        Assert.True(Timer.ClockTicks(end.Encounter, endTick) >= Timer.CapTicks(end.Encounter),
            $"the run ended at {endTick} with its run clock under the cap: not the timer cap's end");
        Assert.Contains(r.Events, e => e.Type == "respawn" && e.Tick < endTick);
        Assert.Equal(endTick, Assert.Single(r.Events, e => e.Type == "run_end").Tick);
        Assert.Equal(RunEndTitle.OutOfTime(Key), RunEndTitle.ForRun(end, 0, 0));

        Assert.True(endTick + EndHeadroomTicks <= ScreenTicks,
            $"the ScreenSceneTest run ends at the timer cap at {endTick}; $Ticks {ScreenTicks} must reach it with {EndHeadroomTicks} ticks to spare");
    }
}
