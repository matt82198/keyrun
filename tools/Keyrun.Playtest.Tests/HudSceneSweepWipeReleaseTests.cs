using System.Linq;
using Keyrun.Sim.Core;
using Keyrun.Sim.Encounter;
using Keyrun.Sim.Runs;
using Xunit;
using Xunit.Abstractions;

namespace Keyrun.Playtest.Tests;

/// <summary>
/// Godot-free replicas of the game-e2e scene tests after ScreenSceneTest (ProgressionSaveSceneTest, RunEndBonusSceneTest,
/// NameplateDeclutterTest, CombatTextOverlapTest) under R-BACKFILL-2 (BIBLE s9.1, keyrun #446; ruling R-446-CI, finding
/// f-ea16265e): on a layout run a wipe is a release, not a run end. A scene whose solo Warden run waited for the wipe
/// to end the run must now reach the timer cap end (<see cref="Timer.CapTicks"/>); game-e2e on d870209d (job
/// 119246754459) failed ProgressionSaveSceneTest (10) at <c>--ticks 600</c>: launch 1 never printed "SCREEN bank
/// result=depleted ash=30 total=30". Each solo scene runs the same host run as HudSceneTest run A (seed 1, key 2, solo
/// Warden, pull 1) and is driven by <see cref="HudSceneWipeReleaseRunsTests.DriveRun"/>.
/// <para>
/// The three-seat party run (no <c>--solo</c>, RunEndBonusSceneTest run C, NameplateDeclutterTest and
/// CombatTextOverlapTest at <c>--ticks 9000</c>, R-DOOR-1) is pinned too: the test proves it still ends inside 9000 under
/// the rule. HelpHintSceneTest (a 400-tick party autorun read only for the controls hint's fade
/// by tick ~70, and an idle replay), SimDriverSceneTest (40 ticks) and CombatViewSceneTest (400 ticks) assert no run
/// end and need no guard.
/// </para>
/// </summary>
public sealed class HudSceneSweepWipeReleaseTests
{
    private const int Key = 2;     // ArenaOptions.DefaultKeyLevel

    /// <summary>ProgressionSaveSceneTest.ps1 $Ticks: both launches' <c>--autorun --ticks</c>.</summary>
    internal const int ProgressionTicks = 9800;

    /// <summary>RunEndBonusSceneTest.ps1 $Ticks: run A's <c>--autorun --ticks --solo</c>.</summary>
    internal const int RunEndBonusSoloTicks = 9800;

    /// <summary>
    /// The party autorun's <c>--ticks</c>: RunEndBonusSceneTest.ps1 $PartyTicks, NameplateDeclutterTest.ps1 $Ticks and
    /// CombatTextOverlapTest.ps1 $Ticks (all 9000). R-DOOR-1 (ruling 3): the Hollow Door at 100% Enemy Forces makes the
    /// dungeon longer by design (the party finishes the engaged packs and pulls the optional ones), so the budget moves
    /// from 7000 to 9000 together with the scene harness args. A Timed clear at 7847 under 9000 is the expected result.
    /// </summary>
    internal const int PartyTicks = 9000;

    /// <summary>
    /// Ticks a solo scene keeps past the run end: the solo run's run_end was at 491 of the old 600-tick budget (the
    /// ScreenSceneTest header's ROOM-LOCK-IN D27 measurement; the same run). ProgressionSaveSceneTest's launch 1 runs the
    /// screen's autorun script after the bank in those ticks (take, three rerolls, four talent steps).
    /// </summary>
    private const int SoloEndHeadroomTicks = 600 - 491;

    private readonly ITestOutputHelper _output;

    /// <summary>Creates the test with xUnit's output sink (the measured end ticks are printed for the PR).</summary>
    /// <param name="output">The sink.</param>
    public HudSceneSweepWipeReleaseTests(ITestOutputHelper output) => _output = output;

    /// <summary>The solo run ended at the timer cap after a release: depleted, uncleared, one run_end, "Out of time".</summary>
    private static int AssertSoloCapEnd(string scene, HudSceneWipeReleaseRunsTests.Drive r)
    {
        Assert.True(r.AtEnd is not null, $"the {scene} solo run never ended");
        var end = r.AtEnd!;
        var endTick = end.RunEnd.ResultTick!.Value;
        Assert.Equal(RunResult.Depleted, end.RunEnd.Result);
        Assert.True(end.Encounter.PullIndex < end.Dungeon.Pulls.Count, $"the {scene} run cleared its pulls; the cap end is an uncleared one");
        Assert.True(Timer.ClockTicks(end.Encounter, endTick) >= Timer.CapTicks(end.Encounter),
            $"the {scene} run ended at {endTick} with its run clock under the cap: not the timer cap's end");
        Assert.Contains(r.Events, e => e.Type == "respawn" && e.Tick < endTick);
        Assert.Equal(endTick, Assert.Single(r.Events, e => e.Type == "run_end").Tick);
        Assert.Equal(RunEndTitle.OutOfTime(Key), RunEndTitle.ForRun(end, 0, 0));
        return endTick;
    }

    /// <summary>
    /// ProgressionSaveSceneTest: both launches run <c>--autorun --solo</c>. Launch 2's run does not wear launch 1's saved
    /// gear or talents: SimDriver._Ready starts the autorun run (<c>StartRun(null)</c>) before the deferred HookScreen
    /// sets <c>_screen</c>, so <c>setup.StartFor(request, _screen?.Progression, ...)</c> gets no progression and both
    /// launches run the same run. That run ends at the timer cap depleted: launch 1 banks 30 ("SCREEN bank
    /// result=depleted ash=30 total=30"), launch 2 adds 30 to the banked 30 ("SCREEN open ash=60 runs=2"), and $Ticks
    /// reaches the end with the ticks the screen's autorun script used after the bank before the rule changed.
    /// </summary>
    [Fact(DisplayName = "ProgressionSaveSceneTest: both launches' solo run ends at the timer cap depleted, inside $Ticks")]
    public void Progression_save_scene_runs_reach_their_cap_end_inside_the_budget()
    {
        var r = HudSceneWipeReleaseRunsTests.DriveRun(solo: true, startPull: 1, request: null, progression: null, abandon: null, idle: false, budget: ProgressionTicks);
        var endTick = AssertSoloCapEnd("ProgressionSaveSceneTest", r);
        var ash = Ash.ForResult(r.AtEnd!.RunEnd.Result!.Value);
        _output.WriteLine($"ProgressionSaveSceneTest run: end={endTick} ash={ash} launch2 total={ash + ash} budget={ProgressionTicks}");
        Assert.Equal(30, ash);
        Assert.Equal(60, ash + ash);
        Assert.True(endTick + SoloEndHeadroomTicks <= ProgressionTicks,
            $"the ProgressionSaveSceneTest run ends at the timer cap at {endTick}; $Ticks {ProgressionTicks} must reach it with {SoloEndHeadroomTicks} ticks to spare");
    }

    /// <summary>
    /// RunEndBonusSceneTest run A (<c>--autorun --solo</c>): the run-end panel prints "HUD runend bonus count=0
    /// seedWindowEnds=none" only once the run ends, now at the timer cap; across that longer run the solo Warden still
    /// never meets the Seed (no seedbloom Window opens, the run carries no bonus loot), so the asserted literal holds.
    /// </summary>
    [Fact(DisplayName = "RunEndBonusSceneTest run A: the solo run ends at the timer cap with no Seed met, inside $Ticks")]
    public void Run_end_bonus_solo_run_reaches_its_cap_end_with_no_seed_inside_the_budget()
    {
        var r = HudSceneWipeReleaseRunsTests.DriveRun(solo: true, startPull: 1, request: null, progression: null, abandon: null, idle: false, budget: RunEndBonusSoloTicks);
        var endTick = AssertSoloCapEnd("RunEndBonusSceneTest A", r);
        var rewards = r.AtEnd!.Rewards;
        _output.WriteLine($"RunEndBonusSceneTest run A: end={endTick} bonus count={rewards.BonusLoot.Count} seedWindowEnds={rewards.SeedWindowEndsTick?.ToString() ?? "none"} budget={RunEndBonusSoloTicks}");
        Assert.Empty(rewards.BonusLoot);
        Assert.Null(rewards.SeedWindowEndsTick);
        Assert.DoesNotContain(r.Events, e => e.Type == "window_opened" && e.Tick <= endTick && e.Get("windowId") is JsString { Value: "seedbloom" });
        Assert.True(endTick + SoloEndHeadroomTicks <= RunEndBonusSoloTicks,
            $"the RunEndBonusSceneTest run A ends at the timer cap at {endTick}; $Ticks {RunEndBonusSoloTicks} must reach it with {SoloEndHeadroomTicks} ticks to spare");
    }

    /// <summary>
    /// The party autorun (RunEndBonusSceneTest run C, NameplateDeclutterTest, CombatTextOverlapTest): the bot party
    /// opens the Seed's seedbloom Window and the run ends before 7000, so the run-end lines those scenes read ("HUD
    /// runend bonus", "HUD runend layout", "HUD numbers cleared reason=runend") still print inside the unchanged budget.
    /// </summary>
    [Fact(DisplayName = "Party scene run (RunEndBonus C, NameplateDeclutter, CombatTextOverlap): ends inside the 9000-tick budget (R-DOOR-1)")]
    public void Party_scene_run_ends_inside_its_unchanged_budget()
    {
        var r = HudSceneWipeReleaseRunsTests.DriveRun(solo: false, startPull: 1, request: null, progression: null, abandon: null, idle: false, budget: PartyTicks);
        Assert.True(r.AtEnd is not null, "the party scene run never ended");
        var end = r.AtEnd!;
        var endTick = end.RunEnd.ResultTick!.Value;
        var seed = r.Events.FirstOrDefault(e => e.Type == "window_opened" && e.Get("windowId") is JsString { Value: "seedbloom" });
        _output.WriteLine($"party scene run: end={endTick} result={end.RunEnd.Result} seedbloom opened={seed?.Tick.ToString() ?? "never"} budget={PartyTicks} spare={PartyTicks - endTick}");
        Assert.Equal(endTick, Assert.Single(r.Events, e => e.Type == "run_end").Tick);
        Assert.True(seed is not null && seed.Tick < endTick, "RunEndBonusSceneTest run C: the Seed's seedbloom Window never opened before run_end");
        Assert.True(endTick < PartyTicks,
            $"the party scene run ends at {endTick}; the unchanged --ticks {PartyTicks} must reach it");
    }
}
