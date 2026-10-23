using System;
using System.Collections.Generic;
using System.Globalization;
using Godot;
using Keyrun.Content;
using Keyrun.Hud;
using Keyrun.Sim.Runs;
using Keyrun.View;

namespace Keyrun.Host;

/// <summary>
/// The arena's user arguments (after <c>--</c> on the Godot command line), in the style of Main.cs's
/// <c>--smoke</c>: <c>--autorun</c>, <c>--ticks N</c>, <c>--log FILE</c>, <c>--write-movie DIR</c>, <c>--key N</c>,
/// <c>--seed N</c>, <c>--dungeon-seed N</c>, <c>--pull N</c>, <c>--solo</c>, <c>--requeue</c>, <c>--abandon-seat A@T|A@pullN+O|A@pullN.hpP+O</c>, <c>--joiner-key K</c>,
/// <c>--press-enter N</c>, <c>--press-ping T</c>, <c>--press-yaw T:Y</c>, <c>--record-input PATH</c>,
/// <c>--replay-input PATH</c>, <c>--bots-hold</c>, <c>--careless-seat S@T:GROUP</c>, <c>--rotation N</c>,
/// <c>--ready-hold F</c>, <c>--talent SEAT:TALENT</c>, <c>--dungeon ID</c>.
/// </summary>
/// <param name="Autorun">Scripted input instead of the keyboard, and quit after <see cref="Ticks"/> ticks.</param>
/// <param name="Ticks">Ticks to run when autorunning.</param>
/// <param name="LogPath">Optional run log file (one TICK line per tick plus EVENT and host lines).</param>
/// <param name="MovieDir">Optional directory for the three proof PNG frames (needs a rendering window).</param>
/// <param name="KeyLevel">Key level (Tempo scaling and the Slack bank).</param>
/// <param name="Seed">Run seed.</param>
/// <param name="StartPull">1-based pull the run starts at, in route order including segment boss pulls (default 1; out of
/// 1..pulls fails fast). A later start drops the earlier pulls from the run's dungeon, keeping actor ids and scripts (lane 3C: the HUD scene test reaches the Pair's SPREAD/STACK calls).</param>
/// <param name="Solo">The human's seat alone, no bot seats (<c>--solo</c>; the phase 2 run). Default: the three-seat
/// bot party (lane 3H).</param>
/// <param name="Requeue">Under <c>--autorun</c>, start the run the between-runs screen queues (<c>--requeue</c>, lane
/// 3I). Off by default so an autorun stays one run (the scene tests and CI read one run's lines, and the HUD test's
/// run B reads the Pair's marks after the solo wipe). An interactive game always starts the queued run.</param>
/// <param name="AbandonSeat">Scripted backfill proof (<c>--abandon-seat A@T</c>, lane 3J): the player in actor A's seat
/// leaves at run tick T (<see cref="SimDriver.AbandonSeat"/>); <c>A@pullN+O</c> (R-415-D) leaves O ticks after pull N's
/// pull_start, and <c>A@pullN.hpP+O</c> (f-0fd3e6fb) O ticks after pull N's standing enemies fall to P percent of
/// their total health (<see cref="SeatAbandonPlan"/>). Null = nobody leaves.</param>
/// <param name="JoinerKey">With <see cref="AbandonSeat"/>, <c>--joiner-key K</c> adds one <see cref="QueueRequest"/> at
/// key K straight to the Queue node at the abandon (the scripted joiner; the screen's Queue button is not pressed
/// mid-run). Null = no joiner is queued.</param>
/// <param name="PressEnter">Interactive scene-test seam (<c>--press-enter N</c>, lane 3D-c): a scripted player pushes
/// Enter through the real input path (<see cref="Viewport.PushInput"/>) each time the game waits on Enter (the
/// between-runs screen is up, or the run has ended), up to N presses. Ignored under <c>--autorun</c>. 0 = never.</param>
/// <param name="PressPing">Interactive scene-test seam (<c>--press-ping T</c>, lane 3H-c): once the run reaches tick T a
/// scripted player pushes one G (the ping key) through the real input path (<see cref="Viewport.PushInput"/>), so
/// PlayerInput raises <see cref="PlayerInput.Pinged"/> exactly as a keyboard G does. Ignored under <c>--autorun</c>.
/// 0 = never.</param>
/// <param name="RecordInput">Interactive input recorder (<c>--record-input PATH</c>, lane 3E-b): every key press and
/// release the game reads is stamped with the input-log clock (<see cref="ReplayClock.Stamp"/>: first-Screen frames, then
/// those frames plus <c>TicksRun</c>; f-6b922005, R-CLOCK-1) and written to PATH as <c>keyrun.input-log/1</c> (see
/// <see cref="InputLog"/>) at each run end and at quit, logged <c>INPUT record done events=N path=P</c>. Lane 3E-c: the
/// CameraRig heading WASD is relative to is sampled before each step and recorded whenever it changes; a log holding a
/// heading is written as <c>keyrun.input-log/2</c> and logged <c>INPUT record done events=N cam=M path=P</c>. Lane
/// HOST-HEADING: the rig first turns down the route as on every other launch, and that start heading is written as the
/// log's <c>startHeading</c> (while a replay is also recorded, the replayed log's own value, none when it has none).
/// Ignored under <c>--autorun</c>. Null = no recording.</param>
/// <param name="ReplayInput">Interactive input replayer (<c>--replay-input PATH</c>, lane 3E-b): the log's events go back
/// through <see cref="Input.ParseInputEvent"/> at their host ticks, so PlayerInput's polled keys and every node's
/// <c>_UnhandledInput</c> see them as keyboard input; logged <c>INPUT replay tick=T key=K pressed=B</c> per event and
/// <c>INPUT replay done events=N last=T</c>. Lane 3E-c: a <c>/2</c> log's headings are put back on the CameraRig
/// (<see cref="Keyrun.View.CameraRig.RestoreYaw"/>) before the step at their tick, logged
/// <c>INPUT replay cam tick=T yaw=Y</c>, and the summary ends <c>cam=M</c>. Lane HOST-HEADING: a log's
/// <c>startHeading</c> is put on the rig before the first step, logged <c>INPUT replay heading yaw=Y</c>; a log without
/// one keeps the legacy heading. Finding f-6a00609e: until the launch's first run exists a log's ticks are frames the
/// first Screen has been up (0 = the frame it is hooked), so a log can drive the Screen and its talent panel on spaced
/// frames before its Enter starts the run; from the first run on they are those frames plus <c>TicksRun</c> (ruling
/// R-CLOCK-1, the value a recording stamps; <see cref="ReplayClock"/>). Ignored under <c>--autorun</c>. Null = no
/// replay.</param>
/// <param name="PressYaw">Interactive scene-test seam (<c>--press-yaw T:Y</c>, lane 3E-c): once the run reaches tick T a
/// scripted player turns the CameraRig to Y radians through <see cref="Keyrun.View.CameraRig.Look"/>, the call the mouse
/// handler makes, logged <c>INPUT yaw tick=T yaw=Y</c> (Y as the rig then reports it). Ignored under <c>--autorun</c>.
/// Null = never.</param>
/// <param name="BotsHold">Scene-test seam (<c>--bots-hold</c>, lane PARTY-TOGETHER): the bot seats spawn as usual but the
/// host applies none of their commands, so they stand and take no action (HudSceneTest run I's wipe source: nobody
/// fights). The bots library is untouched; only the host's per-step sample is skipped. Default false.</param>
/// <param name="CarelessSeat">CARELESS-SEAT (<c>--careless-seat S@T:GROUP</c>; Matt 2026-10-20 "a bad player will
/// accidentally aggro more groups, just test it naturally"): bot seat S plays carelessly from run tick T, straying toward
/// the idle group GROUP (a pull id) until it engages (<see cref="Careless"/>). Null = every bot seat plays normally.</param>
/// <param name="Rotation">Weekly affix rotation override (<c>--rotation N</c>, N &gt;= 0): pins a live run's rotation
/// index (<see cref="AffixWeek.ForRun"/>) instead of the week since the last Tuesday 15:00 UTC reset, so an
/// <c>--autorun</c> or <c>--record-input</c> scene test at keys 5-9 never depends on the date. On a replay the log's
/// recorded index wins. Null = the clock, except under <c>--write-movie</c>, which is pinned to
/// <see cref="AffixWeek.MovieRotationIndex"/> (f-e1ff13e3).</param>
/// <param name="ReadyHold">Scene-test seam (<c>--ready-hold F</c>, finding f-e24b4fa4): a scripted launch (<c>--autorun</c>,
/// <c>--press-enter</c>) normally answers the queue pop's ready check for the human at the pop
/// (<see cref="QueuedRunGate.HumanReadyAtPop"/>); with F the "Group found" dialog stays up F frames first and the scripted
/// human then answers ready (no key is pushed, so a recording is unchanged). 0 = answer at the pop.</param>
/// <param name="Camera">CAMERA-VERTICAL (<c>--camera chase|vertical</c>, <see cref="CameraMode"/>): the CameraRig's
/// starting preset, render-only. Chase (the default) is the unchanged follow camera; V flips it in play.</param>
/// <param name="DungeonSeed">D45/D46 (<c>--dungeon-seed N</c>): play the seeded two-branch dungeon generated from N
/// (<see cref="Keyrun.Bots.Shared.SeededLayout"/>) instead of the hand-built Hollow Vault; null (the default) keeps the hand layout.</param>
/// <param name="TalentPicks">Finding f-a3de0a01 (<c>--talent SEAT:TALENT</c>, repeatable, <see cref="SeatTalent"/>): seat SEAT,
/// bot or human, plays every run with that talent picked (<see cref="RunRequest.Talents"/>), logged
/// <c>TALENT seat=S id=T</c> once. A seat not in the party or a talent its class lacks stops the launch. Null = none.</param>
/// <param name="AbandonAllSeats">R-K4-15 (c) (<c>--abandon-seat all</c>): every seat, the human and each bot, is abandoned
/// at tick 0 before the first step (the RunToEnd abandonAllSeats construction, deduplicated). Mutually exclusive with
/// <see cref="AbandonSeat"/>, which stays single-seat. False = no all-seats abandon.</param>
/// <param name="AbandonAllAtTick">R-K4-15 (d) (<c>--abandon-seat all@TICK</c>): with <see cref="AbandonAllSeats"/>, the all-seats
/// abandon fires once, when the run reaches tick TICK, not at creation (the HudRunN live phase). 0 = tick 0 (the (c) path).</param>
/// <param name="DungeonId">D65 / f-61f01ca3 (<c>--dungeon ID</c>, <see cref="DungeonPick"/>): the dungeon the launch's CLI run and
/// the pre-run screen start on. An id the registry lacks stops the launch (<see cref="DungeonPick.Resolve"/>). Null = the
/// default, <see cref="DungeonPick.DefaultId"/>.</param>
public sealed record ArenaOptions(
    bool Autorun,
    int Ticks,
    string? LogPath,
    string? MovieDir,
    int KeyLevel = ArenaOptions.DefaultKeyLevel,
    long Seed = ArenaOptions.DefaultSeed,
    int StartPull = 1,
    bool Solo = false,
    bool Requeue = false,
    SeatAbandonPlan? AbandonSeat = null,
    int? JoinerKey = null,
    int PressEnter = 0,
    int PressPing = 0,
    string? RecordInput = null,
    string? ReplayInput = null,
    YawPlan? PressYaw = null,
    bool BotsHold = false,
    CarelessSeatPlan? CarelessSeat = null,
    int? Rotation = null,
    int ReadyHold = 0,
    CameraModeKind Camera = CameraModeKind.Chase,
    long? DungeonSeed = null,
    IReadOnlyList<SeatTalent>? TalentPicks = null,
    string? DungeonId = null,
    bool AbandonAllSeats = false,
    int AbandonAllAtTick = 0)
{
    /// <summary>The <c>--talent</c> picks in flag order (empty when none).</summary>
    public IReadOnlyList<SeatTalent> Talents => TalentPicks ?? Array.Empty<SeatTalent>();

    /// <summary>
    /// Default autorun length: 20 s, set when the lieutenant's first heal began at tick 196 at key 2, leaving room for
    /// it, the kick, and the Warden's opening rotation. Measured on main 026bca17 (f-e1ff13e3,
    /// MovieFramesDefaultRunTests, every rotation index alike): the first heal now begins at tick 272 and seat 2 kicks it
    /// at tick 274, so heal and kick still fall inside 400 ticks, but the kick lands before
    /// <see cref="MovieDirector.CastBarLeadTicks"/>, so a default-length <c>--write-movie</c> run takes no cast-bar or
    /// after-kick frame (the next heal, 664 to the kick at 685, is past tick 400).
    /// </summary>
    public const int DefaultTicks = 400;

    /// <summary>Default key level: 2, the oracle bot runs' key.</summary>
    public const int DefaultKeyLevel = 2;

    /// <summary>Default seed.</summary>
    public const long DefaultSeed = 1;

    /// <summary>Parses user arguments (Godot passes them through verbatim). An argument this switch does not read goes to
    /// <see cref="Keyrun.Content.ForeignUserArgs.Skip"/>: Main's and Screen's flags are stepped over, anything else throws.</summary>
    /// <exception cref="ArgumentException">A flag is missing its value, a number flag is not a positive integer, or an
    /// argument is unknown.</exception>
    public static ArenaOptions Parse(IReadOnlyList<string> args)
    {
        var autorun = false;
        int? ticks = null;
        string? log = null;
        string? movie = null;
        var key = DefaultKeyLevel;
        long seed = DefaultSeed;
        var pull = 1;
        var solo = false;
        var botsHold = false;
        int? rotation = null;
        var requeue = false;
        SeatAbandonPlan? abandon = null;
        int? joinerKey = null;
        var pressEnter = 0;
        var pressPing = 0;
        string? recordInput = null;
        string? replayInput = null;
        YawPlan? pressYaw = null;
        CarelessSeatPlan? careless = null;
        var readyHold = 0;
        var camera = CameraModeKind.Chase;
        long? dungeonSeed = null;
        var talents = new List<SeatTalent>();
        string? dungeon = null;
        var abandonAllSeats = false;
        var abandonAllAtTick = 0;
        for (var i = 0; i < args.Count; i++)
        {
            switch (args[i])
            {
                case "--autorun":
                    autorun = true;
                    break;
                case "--solo":
                    solo = true;
                    break;
                case "--rotation":
                    rotation = AffixWeek.ParseOverride(Value(args, ref i));
                    break;
                case "--bots-hold":
                    botsHold = true;
                    break;
                case "--requeue":
                    requeue = true;
                    break;
                case "--ticks":
                    ticks = PositiveInt(args, ref i);
                    break;
                case "--key":
                    key = PositiveInt(args, ref i);
                    break;
                case "--pull":
                    pull = PositiveInt(args, ref i);
                    break;
                case "--dungeon-seed":
                    dungeonSeed = PositiveInt(args, ref i);
                    break;
                case "--seed":
                    seed = PositiveInt(args, ref i);
                    break;
                case "--abandon-seat":
                    // R-K4-15 (c): all abandons every seat at tick 0 so the scene twin constructs uncleared-at-cap deterministically.
                    // R-K4-15 (d): all@TICK abandons every seat once the run reaches TICK, opening the HudRunN live phase.
                    var abandonValue = Value(args, ref i);
                    if (abandonValue == "all")
                    {
                        abandonAllSeats = true;
                    }
                    else if (abandonValue.StartsWith("all@", StringComparison.Ordinal))
                    {
                        abandonAllSeats = true;
                        abandonAllAtTick = AbandonAllTick(abandonValue);
                    }
                    else
                    {
                        abandon = SeatAbandonPlan.Parse(args[i]);
                    }
                    break;
                case "--joiner-key":
                    joinerKey = PositiveInt(args, ref i);
                    break;
                case "--press-enter":
                    pressEnter = PositiveInt(args, ref i);
                    break;
                case "--press-ping":
                    pressPing = PositiveInt(args, ref i);
                    break;
                case "--ready-hold":
                    readyHold = PositiveInt(args, ref i);
                    break;
                case "--press-yaw":
                    pressYaw = YawPlan.Parse(Value(args, ref i));
                    break;
                case "--careless-seat":
                    careless = CarelessSeatPlan.Parse(Value(args, ref i));
                    break;
                case "--record-input":
                    recordInput = Value(args, ref i);
                    break;
                case "--replay-input":
                    replayInput = Value(args, ref i);
                    break;
                case "--log":
                    log = Value(args, ref i);
                    break;
                case SeatTalent.Flag:
                    talents.Add(SeatTalent.Parse(Value(args, ref i)));
                    break;
                case DungeonPick.Flag:
                    dungeon = Value(args, ref i);
                    break;
                case CameraMode.Flag:
                    camera = CameraMode.Parse(Value(args, ref i));
                    break;
                case "--write-movie":
                    movie = Value(args, ref i);
                    autorun = true; // proof frames come from the scripted run
                    break;
                default:
                    // CARELESS-SEAT-ARGS (PR #308 run C): an argument nobody reads stops the launch. A build without a
                    // flag used to drop it silently and run as if it were never passed.
                    Keyrun.Content.ForeignUserArgs.Skip(args, ref i);
                    break;
            }
        }
        return new ArenaOptions(autorun, ticks ?? DefaultTicks, log, movie, key, seed, pull, solo, requeue, abandon, joinerKey, pressEnter, pressPing, recordInput, replayInput, pressYaw, botsHold, careless, rotation, readyHold, camera, dungeonSeed, talents, dungeon, abandonAllSeats, abandonAllAtTick);
    }

    private static int PositiveInt(IReadOnlyList<string> args, ref int i)
    {
        var flag = args[i];
        var raw = Value(args, ref i);
        if (!int.TryParse(raw, NumberStyles.None, CultureInfo.InvariantCulture, out var n) || n <= 0)
        {
            throw new ArgumentException($"{flag} needs a positive integer, got '{raw}'");
        }
        return n;
    }

    /// <summary>Parses the tick of <c>--abandon-seat all@TICK</c> (R-K4-15 (d)): a non-negative integer, 0 meaning tick 0.</summary>
    /// <exception cref="ArgumentException">The tick is not a non-negative integer.</exception>
    private static int AbandonAllTick(string value)
    {
        var raw = value["all@".Length..];
        if (!int.TryParse(raw, NumberStyles.None, CultureInfo.InvariantCulture, out var tick))
        {
            throw new ArgumentException($"--abandon-seat all@TICK needs a non-negative integer tick, got '{raw}'");
        }
        return tick;
    }

    private static string Value(IReadOnlyList<string> args, ref int i)
    {
        if (i + 1 >= args.Count)
        {
            throw new ArgumentException($"{args[i]} needs a value");
        }
        i++;
        return args[i];
    }
}
