using System;
using System.Collections.Generic;
using System.Linq;
using Keyrun.Bots;
using Keyrun.Bots.Shared;
using Keyrun.Content;
using Keyrun.Host;
using Keyrun.Sim.Core;
using Keyrun.Sim.Encounter;
using Keyrun.Sim.Runs;
using Xunit;
using Xunit.Abstractions;

namespace Keyrun.Playtest.Tests;

/// <summary>
/// Godot-free replicas of game/src/Host/HudSceneTest.ps1 runs A, E, N and I under R-BACKFILL-2 (BIBLE s9.1, keyrun #446,
/// f-47a7a6db; the #446 lane brief calls it R-WIPE-RESET): on a layout run a wipe is a release and a run-back, not a run
/// end. Every host run is a layout run (HostRunSetup.Build), so the scene test's wiping runs no longer end on the wipe;
/// they end only at the timer cap (<see cref="Timer.CapTicks"/>, twice par on the run clock), depleted and uncleared,
/// titled <see cref="RunEndTitle.OutOfTime"/>. Each run is built the way SimDriver.StartRun builds it
/// (HostRunSetup.Build, an optional <c>--pull N</c> slice and RunRoot.EngageStartPull, StartFor, WithBotLoadouts,
/// RunRoot.CreateRun, BossRules.Arm) and stepped in SimDriver.StepOnce's order through <see cref="Backfill.Step"/>.
/// Measured against the #446 game-e2e log: run A's Warden dies at 362 there and here, and run A printed "AUTORUN done
/// ticks=900 ... run=running" with no "HUD runend" line (the CI failures "run A: expected 3 'HUD runend choice' lines,
/// got 0" and the rest of that shape): the solo wipe released him at 463 and the run went on.
/// <para>
/// The constants below mirror HudSceneTest.ps1's parameters. Each test pins that its run reaches its real end inside its
/// scene-test budget with the headroom the budget kept past the end before the rule changed, so a budget that no longer
/// reaches the end fails here, headless, before it fails in game-e2e.
/// </para>
/// </summary>
public sealed class HudSceneWipeReleaseRunsTests
{
    private const long Seed = 1;   // ArenaOptions.DefaultSeed
    private const int Key = 2;     // ArenaOptions.DefaultKeyLevel
    private const int WardenId = 1;

    /// <summary>HudSceneTest.ps1 $Ticks: run A's <c>--autorun --ticks</c>.</summary>
    internal const int RunATicks = 10000;

    /// <summary>HudSceneTest.ps1 $QueueTicks: run E's <c>--autorun --ticks</c> (run A's run, then the queued run).</summary>
    internal const int RunETicks = 10000;

    /// <summary>HudSceneTest.ps1 $TintTicks: run N's <c>--autorun --ticks</c>.</summary>
    internal const int RunNTicks = 8500;

    /// <summary>
    /// HudSceneTest.ps1 $TintAbandon: run N's <c>--abandon-seat</c>. Re-derived for the #449 bots (pull_clear 1552): at
    /// e87cade0 the old <c>2@200</c> lets the party clear all four pulls (last clear at 8231, the cap end at 8232, so
    /// PullIndex reaches Count and the cap end is not uncleared), while <c>2@300</c> wipes from tick 596 on and ends at the
    /// timer cap uncleared at 7643. Measured: 2@200 end 8232 PullIndex 4/4; 2@300 end 7643 PullIndex 0/4 (also 2@100 end
    /// 7600 and 2@400 end 7812 wipe; 3@* never wipes and runs to the 28800 cap).
    /// </summary>
    internal const string RunNAbandon = "2@300";

    /// <summary>HudSceneTest.ps1 $WipeFrames: run I's <c>--quit-after</c> frames (interactive, three frames per tick).</summary>
    internal const int RunIFrames = 41200;

    /// <summary>
    /// Ticks an autorun keeps past the run end: run A's budget kept 294 before the rule changed (run_end 606 of 900, the
    /// header's PR #362 measurement), and run E's queued run needs about 80 of them to reach its "PULL spawn index=0
    /// id=choir" (header, run E: about 75 ticks to engage the Choir plus the screen's key script).
    /// </summary>
    private const int EndHeadroomTicks = 294;

    /// <summary>Run I's frames around its run before the rule changed: 6600 less three per tick of its run_end at 602.</summary>
    private const int RunIHeadroomFrames = 6600 - (3 * 602);

    /// <summary>Frames per sim tick at <c>--fixed-fps 60</c> on a 20 Hz sim (game-e2e: "AUTORUN done ticks=900 frames=2700").</summary>
    private const int FramesPerTick = 3;

    /// <summary>A hang guard far past any cap end (the cap is 28800 on the run clock, which deaths only shorten).</summary>
    private const int Horizon = 40000;

    private readonly ITestOutputHelper _output;

    /// <summary>Creates the test with xUnit's output sink (each run's measured ticks are printed for the PR).</summary>
    /// <param name="output">The sink.</param>
    public HudSceneWipeReleaseRunsTests(ITestOutputHelper output) => _output = output;

    /// <summary>One driven run: the run as it ended (null if it never did), its events, and the run at the budget.</summary>
    internal sealed record Drive(Run? AtEnd, List<SimEvent> Events, Run AtBudget);

    private static readonly Lazy<Drive> RunA = new(() => DriveRun(solo: true, startPull: 1, request: null, progression: null, abandon: null, idle: false, budget: RunATicks));

    private static readonly Lazy<Drive> RunN = new(() => DriveRun(solo: false, startPull: 1, request: null, progression: null, abandon: RunNAbandon, idle: false, budget: RunNTicks));

    // Run I: the screen's Queue starts key 2 with Clause echo (game-e2e: "RUN start autorun=0 ... key=2 clause=echo"),
    // the human never presses a key and --bots-hold keeps the bot seats from acting.
    private static readonly Lazy<Drive> RunI = new(() => DriveRun(solo: false, startPull: 2, request: new QueueRequest("hollow-vault", Key, "echo"), progression: Progression.Start(Seed), abandon: null, idle: true, budget: 0));

    /// <summary>
    /// Steps one host run as SimDriver does until it has both ended and reached <paramref name="budget"/> (the autorun keeps
    /// stepping after the end), or <see cref="Horizon"/>. Shared with <see cref="ScreenSceneWipeReleaseRunTests"/>, whose
    /// scene runs the same host run as run A.
    /// </summary>
    internal static Drive DriveRun(bool solo, int startPull, QueueRequest? request, Progression? progression, string? abandon, bool idle, int budget)
    {
        var registry = ContentLoader.Load();
        var setup = HostRunSetup.Build(registry, "hollow-vault", Key, "warden", withBots: !solo);
        var dungeon = HostRunSetup.FromPull(setup.Dungeon, startPull);
        var start = setup.StartFor(request, progression, registry, "warden");
        var options = (start.Options ?? new RunOptions()) with { RotationIndex = setup.RotationIndex };
        var party = setup.PartyFor(dungeon);
        options = setup.WithBotLoadouts(options, party) ?? options;
        var created = RunRoot.CreateRun(Seed, party, dungeon, setup.Content, options);
        if (startPull > 1)
        {
            created = RunRoot.EngageStartPull(created);
        }
        var run = created with { Encounter = BossRules.Arm(created.Encounter, setup.Boss) };
        var warden = new ScriptedWarden();
        var bots = solo ? null : new BotCommandSource(setup.BotActorIds, KitRegistry.From(registry), Seed);
        var plan = abandon is null ? null : SeatAbandonPlan.Parse(abandon);
        int? anchor = null;
        var book = BackfillState.Empty;
        var queue = new List<QueueRequest>();
        var events = new List<SimEvent>();
        Run? atEnd = null;
        Run? atBudget = null;
        while (run.State.Tick < Horizon && (atEnd is null || run.State.Tick < budget))
        {
            var tick = run.State.Tick;
            if (tick == budget)
            {
                atBudget = run;
            }
            if (plan is not null && tick == plan.ResolveTick(anchor))
            {
                book = Backfill.Abandon(book, run, plan.ActorId);
            }
            var commands = new List<Command>();
            if (!idle)
            {
                warden.Sample(tick, run, commands, book.Open.Select(s => s.ActorId).ToList());
                bots?.Sample(tick, run, commands);
            }
            var stepped = Backfill.Step(book, run, commands, queue);
            book = stepped.Backfill;
            if (plan is not null)
            {
                anchor ??= plan.AnchorAt(tick, stepped.Step.Events, stepped.Step.Run.Dungeon, stepped.Step.Run.State.Actors);
            }
            run = stepped.Step.Run;
            events.AddRange(stepped.Step.Events);
            if (atEnd is null && run.RunEnd.ResultTick is not null)
            {
                atEnd = run;
            }
        }
        return new Drive(atEnd, events, atBudget ?? run);
    }

    private static int Int(SimEvent e, string field) => e.Get(field) is JsInt i ? (int)i.Value : -1;

    /// <summary>The run ended at the timer cap: depleted, uncleared, clock at the cap, the "Out of time" title, one run_end after a release.</summary>
    private static int AssertCapEnd(string name, Drive r)
    {
        Assert.True(r.AtEnd is not null, $"run {name} never ended in {Horizon} ticks");
        var end = r.AtEnd!;
        var endTick = end.RunEnd.ResultTick!.Value;
        Assert.Equal(RunResult.Depleted, end.RunEnd.Result);
        Assert.True(end.Encounter.PullIndex < end.Dungeon.Pulls.Count, $"run {name} cleared its pulls; the cap end is an uncleared one");
        Assert.True(Timer.ClockTicks(end.Encounter, endTick) >= Timer.CapTicks(end.Encounter),
            $"run {name} ended at {endTick} with its run clock under the cap: not the timer cap's end");
        Assert.Equal(RunEndTitle.OutOfTime(Key), RunEndTitle.ForRun(end, 0, 0));
        // A wipe never ended it: one run_end, on the end tick, and a seat had died and respawned before it.
        Assert.Equal(endTick, Assert.Single(r.Events, e => e.Type == "run_end").Tick);
        Assert.Contains(r.Events, e => e.Type == "respawn" && e.Tick < endTick);
        return endTick;
    }

    /// <summary>
    /// Run A (<c>--autorun --solo</c>): the solo Warden dies in the Choir, releases and respawns
    /// <see cref="RunWiring.ReleaseDelayTicks"/> after the death step, so the wipe does not end the run; the run ends at the
    /// timer cap, depleted and uncleared ("Out of time -- key stays at +2"), and $Ticks keeps the old headroom past it so
    /// the run-end panel's lines (HudSceneTest check 6) print.
    /// </summary>
    [Fact(DisplayName = "HudSceneTest run A: the solo wipe releases, the run ends at the timer cap, inside $Ticks")]
    public void Run_a_reaches_its_cap_end_inside_its_budget()
    {
        var r = RunA.Value;
        var death = r.Events.First(e => e.Type == "death" && Int(e, "actorId") == WardenId).Tick;
        var respawn = r.Events.First(e => e.Type == "respawn" && Int(e, "actorId") == WardenId).Tick;
        var end = AssertCapEnd("A", r);
        _output.WriteLine($"run A: first death={death} respawn={respawn} end={end} budget={RunATicks}");
        Assert.Equal(death + 1 + RunWiring.ReleaseDelayTicks, respawn);
        Assert.True(end + EndHeadroomTicks <= RunATicks,
            $"run A ends at the timer cap at {end}; HudSceneTest $Ticks {RunATicks} must reach it with {EndHeadroomTicks} ticks to spare");
    }

    /// <summary>
    /// Run A's late-combo lines (lane COMBO-BLAME): the run-end panel prints one line per distinct (windowId, reason) of
    /// seat 1's combo_denied, while the scene test counts those EVENT lines over the whole autorun, after the end too.
    /// No distinct pair may first appear between the end and $Ticks, or the two counts part for a reason that is not a
    /// defect.
    /// <para>
    /// R-WARDEN-FILLER-1 (f-95d23cfc, keyrun #540: Strike is the filler; Guard Break presses only when it closes a Window):
    /// this test used to require at least one pair by the end, but every pair solo run A ever produced was a Guard Break
    /// pressed into the Warden's own Sundered ("sundered|self_combo"). That was the wasted press the filler fix removes, so
    /// it was a fact of the old rotation, not of the HUD. Re-measured on the merged tree, solo run A has Strike 172, Vigil 74,
    /// Brace 24, Shield Bash 15, no Guard Break or Reckoning, and 0 seat-1 combo_denied by the end at 9600. With no ally
    /// Window to close, a solo Warden that plays right is never denied. The pinned expectation is that measured empty set;
    /// the closure rule (no pair first appears after the end) still holds and still guards the HUD count. The non-vacuous
    /// COMBO-BLAME line now comes from the party runs, where Reckoning into his own Sundered is still denied (the default
    /// three-seat run: one "sundered|self_combo").
    /// </para>
    /// </summary>
    [Fact(DisplayName = "HudSceneTest run A: seat 1's distinct combo_denied at the run end are all the autorun prints")]
    public void Run_a_combo_denied_set_is_closed_at_the_end()
    {
        var r = RunA.Value;
        var end = AssertCapEnd("A", r);
        static string Pair(SimEvent e) => (e.Get("windowId") is JsString w ? w.Value : string.Empty) + "|" + (e.Get("reason") is JsString s ? s.Value : string.Empty);
        var own = r.Events.Where(e => e.Type == "combo_denied" && Int(e, "payoffActorId") == WardenId && e.Tick < RunATicks).ToList();
        var atEnd = own.Where(e => e.Tick <= end).Select(Pair).ToHashSet(StringComparer.Ordinal);
        var later = own.Where(e => e.Tick > end).Select(Pair).Where(p => !atEnd.Contains(p)).Distinct(StringComparer.Ordinal).ToList();
        _output.WriteLine($"run A: seat 1 distinct combo_denied by the end={atEnd.Count}, new between the end {end} and $Ticks {RunATicks}={later.Count}");
        // R-WARDEN-FILLER-1: the solo autorun Warden never presses a Payoff he cannot close, so run A's set is empty.
        Assert.True(atEnd.Count == 0, $"run A: seat 1 was denied {string.Join(",", atEnd)} by the end: the solo filler rotation pressed a Payoff into a Window it may not close");
        Assert.Empty(later);
    }

    /// <summary>
    /// Run E (<c>--autorun --solo --requeue</c>): its first run is run A's, so the screen can queue only once that run has
    /// ended at the timer cap; $QueueTicks keeps the old headroom past that end for the queued run's Choir spawn.
    /// </summary>
    [Fact(DisplayName = "HudSceneTest run E: the first run (run A's) ends at the timer cap inside $QueueTicks with room for the queued run")]
    public void Run_e_first_run_ends_inside_its_budget()
    {
        var end = AssertCapEnd("E", RunA.Value);
        _output.WriteLine($"run E: first run end={end} budget={RunETicks}");
        Assert.True(end + EndHeadroomTicks <= RunETicks,
            $"run E's first run ends at {end}; HudSceneTest $QueueTicks {RunETicks} must leave {EndHeadroomTicks} ticks for the queued run");
    }

    /// <summary>
    /// Run N (<c>--autorun --abandon-seat 2@300</c>, lane TINT-RUNOVER): the party wipes and releases, and the run ends at
    /// the timer cap uncleared (check 41's "HUD runend result=depleted ... cleared=0" and its "over=1" lines need that
    /// end inside $TintTicks).
    /// </summary>
    [Fact(DisplayName = "HudSceneTest run N: the run ends at the timer cap inside $TintTicks")]
    public void Run_n_reaches_its_cap_end_inside_its_budget()
    {
        var end = AssertCapEnd("N", RunN.Value);
        _output.WriteLine($"run N: end={end} budget={RunNTicks}");
        Assert.True(end + EndHeadroomTicks <= RunNTicks,
            $"run N ends at the timer cap at {end}; HudSceneTest $TintTicks {RunNTicks} must reach it with {EndHeadroomTicks} ticks to spare");
    }

    /// <summary>
    /// Run I (<c>--press-enter 3 --pull 2 --bots-hold</c>, lane 3D-c): nobody fights, the seats die and release over and
    /// over, and the run ends at the timer cap with a depleted run_end; $WipeFrames covers that end at three frames per
    /// tick plus the frames the run kept around it before (the screen first, the presses, the queued restart).
    /// </summary>
    [Fact(DisplayName = "HudSceneTest run I: the idle party's run ends at the timer cap inside $WipeFrames")]
    public void Run_i_reaches_its_cap_end_inside_its_budget()
    {
        var end = AssertCapEnd("I", RunI.Value);
        _output.WriteLine($"run I: end={end} frames needed={(FramesPerTick * end) + RunIHeadroomFrames} budget={RunIFrames}");
        Assert.True((FramesPerTick * end) + RunIHeadroomFrames <= RunIFrames,
            $"run I ends at the timer cap at tick {end}; HudSceneTest $WipeFrames {RunIFrames} must cover {FramesPerTick} frames per tick plus {RunIHeadroomFrames}");
    }
}
