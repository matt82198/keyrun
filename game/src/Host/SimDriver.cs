using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Godot;
using Keyrun.Bots;
using Keyrun.Bots.Shared;
using Keyrun.Content;
using Keyrun.Hud;
using Keyrun.Sim.Core;
using Keyrun.Sim.Encounter;
using Keyrun.Sim.Runs;
using Keyrun.View;

namespace Keyrun.Host;

/// <summary>
/// Fixed-step accumulator for the 20 Hz sim (STATE.md D9). Godot-free: frame deltas come in as seconds,
/// are rounded to whole nanoseconds, and are spent in whole ticks. Rounding each delta to the nearest
/// nanosecond keeps 1/60 s frames exact enough that three of them always add up to one 50 ms tick
/// (3 x 16666667 ns = 50000001 ns), so 120 frames at 60 fps are exactly 40 ticks.
/// </summary>
public sealed class TickAccumulator
{
    /// <summary>Nanoseconds per second.</summary>
    public const long NanosPerSecond = 1_000_000_000;

    private readonly int _maxStepsPerFrame;
    private long _pendingNanos;

    /// <summary>Builds an accumulator for <paramref name="hz"/> ticks per second.</summary>
    /// <param name="hz">Tick rate (Keyrun.Sim Tick.TickHz).</param>
    /// <param name="maxStepsPerFrame">Cap on ticks run in one frame; a longer stall drops the excess time
    /// instead of spiralling (counted in <see cref="DroppedTicks"/>).</param>
    public TickAccumulator(int hz, int maxStepsPerFrame)
    {
        if (hz <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(hz));
        }
        if (maxStepsPerFrame <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxStepsPerFrame));
        }
        TickNanos = NanosPerSecond / hz;
        _maxStepsPerFrame = maxStepsPerFrame;
    }

    /// <summary>Length of one tick, ns.</summary>
    public long TickNanos { get; }

    /// <summary>Ticks discarded because a single frame owed more than the per-frame cap.</summary>
    public long DroppedTicks { get; private set; }

    /// <summary>Total frame time fed in, ns.</summary>
    public long ElapsedNanos { get; private set; }

    /// <summary>Fraction of the next tick already accumulated, in [0, 1): the render interpolation factor.</summary>
    public double Alpha => (double)_pendingNanos / TickNanos;

    /// <summary>Adds one frame's delta and returns how many ticks are now due (0..maxStepsPerFrame).</summary>
    /// <param name="deltaSeconds">Frame delta, seconds; negative or NaN counts as 0.</param>
    public int Advance(double deltaSeconds)
    {
        var nanos = double.IsNaN(deltaSeconds) || deltaSeconds <= 0 ? 0L : (long)Math.Round(deltaSeconds * NanosPerSecond);
        ElapsedNanos += nanos;
        _pendingNanos += nanos;
        var steps = 0;
        while (_pendingNanos >= TickNanos && steps < _maxStepsPerFrame)
        {
            _pendingNanos -= TickNanos;
            steps++;
        }
        if (_pendingNanos >= TickNanos)
        {
            DroppedTicks += _pendingNanos / TickNanos;
            _pendingNanos %= TickNanos;
        }
        return steps;
    }
}

/// <summary>
/// The arena's user arguments (after <c>--</c> on the Godot command line), in the style of Main.cs's
/// <c>--smoke</c>: <c>--autorun</c>, <c>--ticks N</c>, <c>--log FILE</c>, <c>--write-movie DIR</c>, <c>--key N</c>,
/// <c>--seed N</c>, <c>--pull N</c>, <c>--solo</c>, <c>--requeue</c>, <c>--abandon-seat A@T</c>, <c>--joiner-key K</c>,
/// <c>--press-enter N</c>, <c>--press-ping T</c>, <c>--press-yaw T:Y</c>, <c>--record-input PATH</c>,
/// <c>--replay-input PATH</c>, <c>--bots-hold</c>, <c>--careless-seat S@T:GROUP</c>, <c>--rotation N</c>.
/// </summary>
/// <param name="Autorun">Scripted input instead of the keyboard, and quit after <see cref="Ticks"/> ticks.</param>
/// <param name="Ticks">Ticks to run when autorunning.</param>
/// <param name="LogPath">Optional run log file (one TICK line per tick plus EVENT and host lines).</param>
/// <param name="MovieDir">Optional directory for the three proof PNG frames (needs a rendering window).</param>
/// <param name="KeyLevel">Key level (Tempo scaling and the Slack bank).</param>
/// <param name="Seed">Run seed.</param>
/// <param name="StartPull">1-based pull the run starts at (default 1). A later start drops the earlier pulls from the
/// run's dungeon, keeping actor ids and scripts (lane 3C: the HUD scene test reaches the Pair's SPREAD/STACK calls).</param>
/// <param name="Solo">The human's seat alone, no bot seats (<c>--solo</c>; the phase 2 run). Default: the three-seat
/// bot party (lane 3H).</param>
/// <param name="Requeue">Under <c>--autorun</c>, start the run the between-runs screen queues (<c>--requeue</c>, lane
/// 3I). Off by default so an autorun stays one run (the scene tests and CI read one run's lines, and the HUD test's
/// run B reads the Pair's marks after the solo wipe). An interactive game always starts the queued run.</param>
/// <param name="AbandonSeat">Scripted backfill proof (<c>--abandon-seat A@T</c>, lane 3J): the player in actor A's seat
/// leaves at run tick T (<see cref="SimDriver.AbandonSeat"/>). Null = nobody leaves.</param>
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
/// release the game reads is stamped with the host tick and written to PATH as <c>keyrun.input-log/1</c> (see
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
/// one keeps the legacy heading. Ignored under <c>--autorun</c>. Null = no replay.</param>
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
/// recorded index wins. Null = the clock.</param>
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
    int? Rotation = null)
{
    /// <summary>
    /// Default autorun length: 20 s, long enough for the lieutenant's first heal (tick 196 at key 2), the kick, and
    /// the Warden's opening rotation.
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
                case "--seed":
                    seed = PositiveInt(args, ref i);
                    break;
                case "--abandon-seat":
                    abandon = SeatAbandonPlan.Parse(Value(args, ref i));
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
        return new ArenaOptions(autorun, ticks ?? DefaultTicks, log, movie, key, seed, pull, solo, requeue, abandon, joinerKey, pressEnter, pressPing, recordInput, replayInput, pressYaw, botsHold, careless, rotation);
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

/// <summary>A scripted seat abandon (<c>--abandon-seat A@T</c>, lane 3J): actor <paramref name="ActorId"/> leaves at run
/// tick <paramref name="Tick"/>.</summary>
/// <param name="ActorId">The seat's player actor (1 the human, 2 and 3 the bot seats).</param>
/// <param name="Tick">The run tick (pre-step) at which the seat empties.</param>
public sealed record SeatAbandonPlan(int ActorId, int Tick)
{
    /// <summary>Parses <c>A@T</c> (A a positive actor id, T a tick of 0 or more).</summary>
    /// <param name="raw">The flag value.</param>
    /// <returns>The plan.</returns>
    /// <exception cref="ArgumentException">Not of the form A@T.</exception>
    public static SeatAbandonPlan Parse(string raw)
    {
        ArgumentNullException.ThrowIfNull(raw);
        var parts = raw.Split('@');
        if (parts.Length != 2 ||
            !int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out var actor) || actor <= 0 ||
            !int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var tick))
        {
            throw new ArgumentException($"--abandon-seat needs actorId@tick, got '{raw}'");
        }
        return new SeatAbandonPlan(actor, tick);
    }
}

/// <summary>A careless bot seat (<c>--careless-seat S@T:GROUP</c>, CARELESS-SEAT): bot actor <paramref name="ActorId"/>
/// strays toward the idle group <paramref name="GroupId"/> from run tick <paramref name="Tick"/> (<see cref="Careless"/>).</summary>
/// <param name="ActorId">The bot seat's player actor (2 and 3 in the three-seat party).</param>
/// <param name="Tick">The run tick (pre-step) from which the seat strays, 0 or more.</param>
/// <param name="GroupId">The group's id: a pull id of the run's dungeon (choir, pair, seed, boss).</param>
public sealed record CarelessSeatPlan(int ActorId, int Tick, string GroupId)
{
    /// <summary>Parses <c>S@T:GROUP</c> (S a positive actor id, T a tick of 0 or more, GROUP a non-empty id).</summary>
    /// <param name="raw">The flag value.</param>
    /// <returns>The plan.</returns>
    /// <exception cref="ArgumentException">Not of the form S@T:GROUP.</exception>
    public static CarelessSeatPlan Parse(string raw)
    {
        ArgumentNullException.ThrowIfNull(raw);
        var at = raw.Split('@');
        var rest = at.Length == 2 ? at[1].Split(':') : Array.Empty<string>();
        if (at.Length != 2 || rest.Length != 2 || rest[1].Length == 0 ||
            !int.TryParse(at[0], NumberStyles.None, CultureInfo.InvariantCulture, out var actor) || actor <= 0 ||
            !int.TryParse(rest[0], NumberStyles.None, CultureInfo.InvariantCulture, out var tick))
        {
            throw new ArgumentException($"--careless-seat needs actorId@tick:groupId, got '{raw}'");
        }
        return new CarelessSeatPlan(actor, tick, rest[1]);
    }

    /// <summary>The bots' plan: this seat strays toward the actors of <see cref="GroupId"/>'s pull in <paramref name="dungeon"/>.</summary>
    /// <param name="dungeon">The run's dungeon.</param>
    /// <returns>The plan by seat.</returns>
    /// <exception cref="ArgumentException">The dungeon has no pull with that id (fail closed: a typo never runs a normal party).</exception>
    public IReadOnlyDictionary<int, CarelessPlan> For(Keyrun.Sim.Encounter.DungeonDef dungeon)
    {
        ArgumentNullException.ThrowIfNull(dungeon);
        var pull = dungeon.Pulls.FirstOrDefault(p => string.Equals(p.Id, GroupId, StringComparison.Ordinal))
            ?? throw new ArgumentException($"--careless-seat: dungeon {dungeon.Id} has no group '{GroupId}'");
        return new Dictionary<int, CarelessPlan> { [ActorId] = new CarelessPlan(Tick, pull.Spawns.Select(s => s.ActorId).ToList()) };
    }
}

/// <summary>A scripted camera turn (<c>--press-yaw T:Y</c>, lane 3E-c): at run tick <paramref name="Tick"/> the CameraRig
/// turns to <paramref name="Yaw"/> radians.</summary>
/// <param name="Tick">The run tick (pre-step) at which the camera turns, 0 or more.</param>
/// <param name="Yaw">The heading to turn to, radians (finite; the rig wraps it into [-Pi, Pi]).</param>
public sealed record YawPlan(int Tick, float Yaw)
{
    /// <summary>Parses <c>T:Y</c> (T a tick of 0 or more, Y a finite invariant-culture number of radians).</summary>
    /// <param name="raw">The flag value.</param>
    /// <returns>The plan.</returns>
    /// <exception cref="ArgumentException">Not of the form T:Y.</exception>
    public static YawPlan Parse(string raw)
    {
        ArgumentNullException.ThrowIfNull(raw);
        var parts = raw.Split(':');
        if (parts.Length != 2 ||
            !int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out var tick) ||
            !float.TryParse(parts[1], NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var yaw) ||
            !float.IsFinite(yaw))
        {
            throw new ArgumentException($"--press-yaw needs tick:radians, got '{raw}'");
        }
        return new YawPlan(tick, yaw);
    }
}

/// <summary>Supplies this tick's commands (keyboard or scripted).</summary>
public interface ICommandSource
{
    /// <summary>Appends the commands for <paramref name="tick"/> (the pre-step run tick) to <paramref name="into"/>.</summary>
    /// <param name="tick">The tick the commands are addressed to.</param>
    /// <param name="run">The run before the step.</param>
    /// <param name="into">The command list.</param>
    void Sample(int tick, Run run, List<Command> into);
}

/// <summary>
/// Runs the real C# run root (<see cref="RunRoot.CreateRun"/> / <see cref="RunRoot.StepRun"/>, lane 2D) at 20 Hz from
/// Godot's frame loop with a fixed accumulator (D9), on Hollow Vault with the human Warden plus a Chanter and a Cinder
/// bot seat (<see cref="BotCommandSource"/>, lane 3H; <c>--solo</c> drops the bots): pull 1 (the Choir) spawns at
/// creation, the encounter step chases and casts, the combat rules resolve the party's casts. Keeps the
/// previous and current state for render interpolation. Keyrun.Sim stays pure; this node is the only place frame time
/// meets sim ticks.
/// </summary>
/// <remarks>
/// <c>--autorun --ticks N</c> replaces the keyboard with PlayerInput's scripted Warden, prints one
/// <c>AUTORUN done ticks=N frames=F frame_ms=M</c> line, then <c>BOTS done commands=C hash=H</c> (the bot command
/// stream's count and FNV-1a 64 fingerprint), and quits with exit code 0 once N ticks have run.
/// <c>--write-movie DIR</c> saves three proof frames (Choir chase, lieutenant heal cast bar, after the kick), each on
/// the tick its trace event picks (<see cref="MovieDirector"/>), logged <c>FRAME path tick=T event="..." event_tick=E</c>.
/// Interactive: once the run ends, Enter opens the between-runs screen (lane 3D-c, <see cref="Screen.OpenFirst"/>,
/// logged <c>SCREEN reopen result=R open=1 key=K</c>) unless it is already up (a reward take opened it; it owns Enter
/// then), so the next run is always the one its Queue hands to <see cref="StartQueuedRun"/>. Under <c>--solo</c> Enter
/// still starts a fresh run at <c>--key</c> with no Clause; under <c>--autorun</c> Enter does nothing.
/// <para>Lane 3D-b: the interactive launch (<see cref="StartsOnScreen"/>) creates no run; it opens the between-runs
/// screen (<see cref="Screen.OpenFirst"/>, logged <c>SCREEN first open=1 key=K</c>) and the first run is the one Queue
/// hands to <see cref="StartQueuedRun"/>. Until then <see cref="Run"/> is null and nothing steps.</para>
/// <para>Lane 3I: every run is created with the boss armed (<see cref="BossRules.Arm"/> with
/// <see cref="HostRunSetup.Boss"/>, logged as <c>BOSS armed twins=N lockAt=P</c>), and a <see cref="Screen.Queued"/>
/// request starts the next run at the queued key with the queued Clause (<see cref="StartQueuedRun"/>; under
/// <c>--autorun</c> only with <c>--requeue</c>). The <c>RUN start</c> line reports the created run's key (its content KeyLevel) and Clause (the
/// sim's <see cref="Keyrun.Sim.Encounter.EncounterRules.ActiveClauseId"/>, <c>none</c> without one).</para>
/// <para>Lane 3J: every tick steps <see cref="Backfill.Step"/> over the Queue node's requests (Hud/Screen/Queue,
/// <see cref="Keyrun.Hud.Queue"/>) instead of <see cref="RunRoot.StepRun"/>. <see cref="AbandonSeat"/> opens a seat
/// (logged <c>SEAT abandon actor=N tick=T</c>); at the next pull start the first matching-key request takes it (logged
/// <c>SEAT refill actor=N tick=T key=K</c>) and leaves the queue (<see cref="Keyrun.Hud.Queue.Consume"/>). A request
/// that starts a run (<see cref="StartQueuedRun"/>) also leaves the queue, so it never also refills a seat. The joiner's
/// commands are the seat's own command source (PlayerInput for seat 1, BotCommandSource for 2 and 3), whose commands
/// are dropped while the seat is open.</para>
/// </remarks>
public partial class SimDriver : Node
{
    /// <summary>The human Warden's actor id.</summary>
    public const int WardenId = 1;

    /// <summary>The dungeon played.</summary>
    public const string DungeonId = "hollow-vault";

    /// <summary>
    /// The human's class for a run with no queued class (the CLI, <c>--autorun</c> and <c>--solo</c> runs); a queued run
    /// plays the class picked on the screen's Character tab (<see cref="Lobby.ClassFor"/>).
    /// </summary>
    public const string HumanClassId = Lobby.DefaultClassId;

    private const int MaxStepsPerFrame = 5;

    // --press-enter: frames the scripted player waits on each Enter prompt before pressing.
    private const int EnterWaitFrames = 10;

    private readonly TickAccumulator _accumulator = new(Tick.TickHz, MaxStepsPerFrame);
    private readonly List<Command> _commands = new();
    private ICommandSource? _input;
    private PlayerInput? _player;
    private BotCommandSource? _bots;
    private Screen? _screen;
    private Queue? _queue;
    private BackfillState _backfill = BackfillState.Empty;
    private RunLog? _log;
    private int _pendingCaptures;
    // f-50d8099e: --write-movie frames are picked from the trace (MovieDirector), one director per run.
    private MovieDirector? _movie;
    private int _enterPresses;
    private int _enterWait;
    private bool _pingPressed;
    private InputRecorder? _recorder;
    private InputReplayer? _replayer;
    private InputLog? _replayLog;
    private int _replayTotal;
    private int _replayCameraTotal;
    private float? _replayStartHeading;
    private bool _replayDoneLogged;
    private CameraRig? _rig;
    private bool _cameraBaselined;
    private bool _yawPressed;
    private bool _done;

    /// <summary>The PlayerInput node.</summary>
    [Export]
    public NodePath InputPath { get; set; } = new("../PlayerInput");

    /// <summary>The CameraRig whose heading PlayerInput's WASD is relative to (lane 3E-c: recorded and replayed).</summary>
    [Export]
    public NodePath CameraRigPath { get; set; } = new("../CameraRig");

    /// <summary>The Queue node whose requests backfill reads (lane 3J; Arena.tscn mounts it at Hud/Screen/Queue).</summary>
    [Export]
    public NodePath QueuePath { get; set; } = new("../Hud/Screen/Queue");

    /// <summary>Parsed user arguments.</summary>
    public ArenaOptions Options { get; private set; } = new(false, ArenaOptions.DefaultTicks, null, null);

    /// <summary>The validated content (display names for the views).</summary>
    public ContentRegistry? Registry { get; private set; }

    /// <summary>The run's setup (party, dungeon, content, the human's ability slots).</summary>
    public HostRunSetup? Setup { get; private set; }

    /// <summary>
    /// The weekly affix rotation index every run of this launch is built with (<see cref="AffixWeek"/>): the week
    /// since the last US reset (Tuesday 15:00 UTC), taken once at launch from the UTC clock, or <c>--rotation N</c>, or the
    /// replayed input log's recorded index. It goes into the
    /// setup (the screen's affix line reads it there), the run's <see cref="RunOptions.RotationIndex"/> and the
    /// <c>--record-input</c> log header.
    /// </summary>
    public int RotationIndex { get; private set; }

    /// <summary>
    /// True when the launch opens the between-runs screen instead of starting a run (lane 3D-b): the interactive game,
    /// i.e. neither <c>--autorun</c> (which <c>--write-movie</c> implies) nor <c>--solo</c>. Those two keep starting the
    /// CLI run (<c>--key</c>, no Clause) at once, so CI, E2eAssert and the scene tests read the same lines as before.
    /// </summary>
    public bool StartsOnScreen => !Options.Autorun && !Options.Solo;

    /// <summary>The current run, or null before <see cref="_Ready"/> succeeded and, when <see cref="StartsOnScreen"/>,
    /// until the screen queues the first run.</summary>
    public Run? Run { get; private set; }

    /// <summary>The state after the latest step.</summary>
    public SimState State { get; private set; } = SimState.Create(1);

    /// <summary>The state before the latest step (render interpolation start).</summary>
    public SimState PreviousState { get; private set; } = SimState.Create(1);

    /// <summary>Events of the latest step.</summary>
    public IReadOnlyList<SimEvent> LastEvents { get; private set; } = Array.Empty<SimEvent>();

    // KF1-READABLE: every step of the latest frame that stepped (a slow frame runs up to MaxStepsPerFrame).
    private readonly StepBatch<SimState, SimEvent> _frameSteps = new(SimState.Create(1));

    /// <summary>
    /// The state before the first step of the latest frame that stepped. Views that react to events (the animator, the
    /// hit flash) diff against this rather than <see cref="PreviousState"/>, so a slow frame's earlier steps still show.
    /// </summary>
    public SimState FrameStartState => _frameSteps.Start;

    /// <summary>The events of every step of the latest frame that stepped, in order (KF1-READABLE).</summary>
    public IReadOnlyList<SimEvent> FrameEvents => _frameSteps.Events;

    /// <summary>Render interpolation factor between <see cref="PreviousState"/> and <see cref="State"/>.</summary>
    public float Alpha => (float)_accumulator.Alpha;

    /// <summary>Frames processed so far.</summary>
    public int Frames { get; private set; }

    /// <summary>Ticks stepped so far.</summary>
    public int TicksRun { get; private set; }

    /// <summary>Encounter telegraphs pending after the latest step.</summary>
    public IReadOnlyList<Telegraph> ActiveTelegraphs => Run?.Encounter.Telegraphs ?? Array.Empty<Telegraph>();

    /// <summary>Raised after every step with the pre-step tick and that step's events.</summary>
    public event Action<int, IReadOnlyList<SimEvent>>? Stepped;

    /// <summary>Raised when a fresh run replaces the current one (views drop per-run state).</summary>
    public event Action? RunStarted;

    /// <summary>The display name of an actor: its class or enemy name from content, else its class id.</summary>
    /// <param name="actor">The actor.</param>
    /// <returns>The name.</returns>
    public string DisplayName(ActorState actor)
    {
        ArgumentNullException.ThrowIfNull(actor);
        return Names.Actor(Registry, actor.ClassId);
    }

    /// <summary>The content display name of an ability, else its id.</summary>
    /// <param name="abilityId">The ability id.</param>
    /// <returns>The name.</returns>
    public string AbilityName(string abilityId) => Names.Ability(Registry, abilityId);

    /// <inheritdoc />
    public override void _Ready()
    {
        try
        {
            Options = ArenaOptions.Parse(OS.GetCmdlineUserArgs());
            Registry = ContentLoader.Load();
            // The weekly affix rotation (AffixWeek, Matt 2026-10-20 "lean into m+ design"): a live run takes the index
            // of the week since the last US reset (Tuesday 15:00 UTC), read from the UTC clock here in the host, never in
            // the sim, unless --rotation N pins it; a --replay-input run takes the index its log recorded, never the
            // clock. Read the log first so the setup is built at the recorded index.
            _replayLog = !Options.Autorun && Options.ReplayInput is { } replayPath ? InputLog.Load(replayPath) : null;
            RotationIndex = AffixWeek.ForRun(
                _replayLog is not null, _replayLog?.RotationIndex, Options.Rotation, DateTime.UtcNow, AffixWeek.RotationLength(Registry.Clauses.Count));
            Setup = HostRunSetup.Build(Registry, DungeonId, Options.KeyLevel, HumanClassId, withBots: !Options.Solo, rotationIndex: RotationIndex);
            _log = new RunLog(Options.LogPath, Options.Autorun, Registry);
            _player = GetNode<PlayerInput>(InputPath);
            _rig = GetNodeOrNull<CameraRig>(CameraRigPath);
            _input = _player;
            // CARELESS-SEAT: --careless-seat S@T:GROUP makes bot seat S stray toward GROUP's idle pull from tick T.
            var careless = Options.CarelessSeat?.For(Setup.Dungeon);
            _bots = new BotCommandSource(Setup.BotActorIds, KitRegistry.From(Registry), Options.Seed, BotCommandSource.DefaultSkillDial, careless);
            // Lane 3H-c: the human's G reaches the bot seats (hud-wiring.ts derivePingEvent -> simHost.applyCommand).
            _player.Pinged += RoutePing;
            // Lane 3E-b: --record-input / --replay-input, interactive only (an autorun's input is ScriptedWarden).
            if (!Options.Autorun)
            {
                OpenInputLogs();
            }
            // Lane 3D-b: the interactive launch waits on the between-runs screen (opened in HookScreen); --autorun and
            // --solo start the CLI run at once, as before.
            if (!StartsOnScreen)
            {
                StartRun(null);
            }
            // The screen (Hud/Screen) is readied after this node; find it once the tree is up.
            Callable.From(HookScreen).CallDeferred();

            if (Options.MovieDir is { } dir)
            {
                if (DisplayServer.GetName() == "headless")
                {
                    throw new InvalidOperationException("--write-movie needs a rendering window; drop --headless");
                }
                Directory.CreateDirectory(dir);
            }
        }
        catch (Exception ex)
        {
            GD.PrintErr($"ARENA error: {ex.Message}");
            _done = true;
            GetTree().Quit(1);
        }
    }

    /// <inheritdoc />
    public override void _Process(double delta)
    {
        // Lane 3E-c: the heading the rig starts at (every _Ready has run, PlayerInput has turned it to the spawn facing)
        // is the recorder's baseline, never an event. Lane HOST-HEADING: a plain recording faces the route first
        // (CameraRig.EnsureStartHeading) and writes that baseline as the log's startHeading; a replay puts the file's
        // startHeading back on the rig here, before the first step, and keeps the legacy heading when it has none.
        if (!_cameraBaselined && _rig is not null && (_recorder is not null || _replayer is not null))
        {
            _cameraBaselined = true;
            _rig.EnsureStartHeading();
            if (_replayStartHeading is { } heading)
            {
                _rig.RestoreYaw(heading);
                _log?.Line(FormattableString.Invariant($"INPUT replay heading yaw={InputLog.FormatYaw(heading)}"));
            }
            if (_recorder is not null)
            {
                _recorder.BaselineCamera(_rig.Yaw);
                _recorder.StartHeading = _replayer is null ? _rig.Yaw : _replayStartHeading;
            }
        }
        MaybeScriptedEnter();
        MaybeReplay();
        if (_done || Run is null)
        {
            return;
        }
        MaybeScriptedPing(Run);
        MaybeScriptedYaw(Run);
        Frames++;
        var steps = _accumulator.Advance(delta);
        for (var i = 0; i < steps; i++)
        {
            // Lane 3E-b: a replayed event lands before the step after its tick, as a key pressed in a frame does; the
            // recorder's poll diff runs at the same point, so both see the keys PlayerInput is about to sample. Lane
            // 3E-c: so does the camera heading, the value SampleMove reads (CameraRig.Yaw).
            MaybeReplay();
            _recorder?.SyncPolled(TicksRun, PlayerInput.PolledKeys, Input.IsPhysicalKeyPressed);
            if (_rig is not null)
            {
                _recorder?.SyncCamera(TicksRun, _rig.Yaw);
            }
            StepOnce();
            if (Options.Autorun && TicksRun >= Options.Ticks)
            {
                break;
            }
        }
        if (Options.Autorun && TicksRun >= Options.Ticks && _pendingCaptures == 0)
        {
            Finish();
        }
    }

    /// <inheritdoc />
    public override void _Input(InputEvent @event)
    {
        // Lane 3E-b: _Input sees every key before the GUI and the _UnhandledInput readers (PlayerInput, the screen, the
        // run-end panel, this node), so the log holds whatever any of them reads. Echo repeats are not transitions.
        if (_recorder is not null && @event is InputEventKey { Echo: false } key)
        {
            _recorder.OnKey(TicksRun, key.PhysicalKeycode != Key.None ? key.PhysicalKeycode : key.Keycode, key.Pressed);
        }
    }

    /// <inheritdoc />
    public override void _UnhandledInput(InputEvent @event)
    {
        if (Options.Autorun || Run?.RunEnd.Result is not { } result || _screen?.IsOpen == true ||
            @event is not InputEventKey { Pressed: true, Echo: false, Keycode: Key.Enter or Key.KpEnter })
        {
            return;
        }
        if (!StartsOnScreen)
        {
            // --solo: Enter restarts at --key with no Clause, as before.
            StartRun(null);
            return;
        }
        // Lane 3D-c (ENGINE-PLAN "end-of-run choice of three, then Screen"): every interactive run end, wipe included,
        // goes back through the between-runs screen; the next run starts only from its Queue (Queued -> StartQueuedRun).
        // A reward take has already opened it (RunEndPanel -> Screen.OpenFrom); this is the no-take route.
        if (_screen is { } screen)
        {
            screen.OpenFirst();
            _log?.Line(FormattableString.Invariant(
                $"SCREEN reopen result={result.ToJs()} open={(screen.IsOpen ? 1 : 0)} key={screen.State?.KeyLevel ?? 0}"));
        }
        GetViewport().SetInputAsHandled();
    }

    /// <inheritdoc />
    public override void _ExitTree()
    {
        if (_screen is not null)
        {
            _screen.Queued -= StartQueuedRun;
        }
        if (_player is not null)
        {
            _player.Pinged -= RoutePing;
        }
        SaveRecording(final: true);
        _log?.Dispose();
    }

    // --record-input / --replay-input (lane 3E-b). A replay is refused (ARENA error, exit 1) when the log's seed or key
    // differs from this launch's: the same keys would not make the same run.
    private void OpenInputLogs()
    {
        if (Options.RecordInput is not null)
        {
            _recorder = new InputRecorder();
        }
        if (Options.ReplayInput is { } path)
        {
            var log = _replayLog ?? InputLog.Load(path);
            if (log.Seed != Options.Seed || log.KeyLevel != Options.KeyLevel)
            {
                throw new ArgumentException(FormattableString.Invariant(
                    $"--replay-input {path} was recorded at seed={log.Seed} key={log.KeyLevel}, this launch is seed={Options.Seed} key={Options.KeyLevel}"));
            }
            if ((log.Camera.Count > 0 || log.StartHeading is not null) && _rig is null)
            {
                throw new ArgumentException($"--replay-input {path} holds camera headings but the scene has no CameraRig at {CameraRigPath}");
            }
            _replayer = new InputReplayer(log);
            _replayStartHeading = log.StartHeading;
            _replayTotal = log.Events.Count;
            _replayCameraTotal = log.Camera.Count;
        }
    }

    // --replay-input: feeds every due event to the engine's Input singleton and flushes it at once, so the key state
    // PlayerInput polls (Input.IsPhysicalKeyPressed) and the viewport's event delivery (_Input, GUI, _UnhandledInput)
    // both see it before the next step, exactly as a keyboard press between frames. Waits for the screen to be hooked
    // on the first screen (the run-less launch), where no tick advances.
    private void MaybeReplay()
    {
        if (_replayer is null || _done || (_screen is null && Run is null))
        {
            return;
        }
        foreach (var e in _replayer.Due(TicksRun))
        {
            _log?.Line(FormattableString.Invariant($"INPUT replay tick={e.Tick} key={e.Key} pressed={(e.Pressed ? "true" : "false")}"));
            Input.ParseInputEvent(new InputEventKey { Keycode = e.Key, PhysicalKeycode = e.Key, Pressed = e.Pressed });
            Input.FlushBufferedEvents();
        }
        // Lane 3E-c: the recorded heading goes back on the rig PlayerInput reads, before the step at its tick. A /1 log
        // has none, and its summary line is unchanged.
        foreach (var c in _replayer.DueCamera(TicksRun))
        {
            _log?.Line(FormattableString.Invariant($"INPUT replay cam tick={c.Tick} yaw={InputLog.FormatYaw(c.Yaw)}"));
            _rig?.RestoreYaw(c.Yaw);
        }
        if (_replayer.Done && !_replayDoneLogged)
        {
            _replayDoneLogged = true;
            var cam = _replayCameraTotal > 0 ? FormattableString.Invariant($" cam={_replayCameraTotal}") : string.Empty;
            _log?.Line(FormattableString.Invariant($"INPUT replay done events={_replayTotal} last={_replayer.LastTick}{cam}"));
        }
    }

    // --record-input: written at every run end (a crash keeps the finished runs) and at quit, which logs the summary.
    private void SaveRecording(bool final)
    {
        if (_recorder is null || Options.RecordInput is not { } path)
        {
            return;
        }
        try
        {
            _recorder.ToLog(Options.Seed, Options.KeyLevel, RotationIndex).Save(path);
            if (final)
            {
                var cam = _recorder.Camera.Count > 0 ? FormattableString.Invariant($" cam={_recorder.Camera.Count}") : string.Empty;
                _log?.Line(FormattableString.Invariant($"INPUT record done events={_recorder.Events.Count}{cam} path={path}"));
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            GD.PrintErr($"INPUT record error path={path}: {ex.Message}");
        }
    }

    /// <summary>
    /// Starts the run the between-runs screen queued (lane 3I; 3D's <see cref="QueueRequest"/>): the setup is rebuilt at
    /// the request's dungeon and key, and the run carries the request's one Clause (<see cref="QueueRequest.ToRunOptions"/>).
    /// </summary>
    /// <param name="request">The queued key and Clause.</param>
    public void StartQueuedRun(QueueRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (_done || Registry is null || (Options.Autorun && !Options.Requeue))
        {
            return;
        }
        StartRun(request);
        // 3G's Queue node also holds every Screen.Queued request; the one that just started a run leaves it, so it
        // can never also refill a seat (lane 3J).
        if (_queue is { } queue)
        {
            for (var i = queue.Requests.Count - 1; i >= 0; i--)
            {
                if (ReferenceEquals(queue.Requests[i], request))
                {
                    queue.Consume(new[] { i });
                    break;
                }
            }
        }
    }

    /// <summary>
    /// The player in <paramref name="actorId"/>'s seat leaves the current run (lane 3J, BIBLE s10 backfill): the seat's
    /// commands are dropped from the next step on, and the next pull start offers it to the queue at the run's key.
    /// </summary>
    /// <param name="actorId">A player actor of the run.</param>
    /// <exception cref="ArgumentException">Not a player seat of the run, or already open (<see cref="Backfill.Abandon"/>).</exception>
    public void AbandonSeat(int actorId) => _backfill = Backfill.Abandon(_backfill, Run!, actorId);

    /// <summary>The abandoned seats not yet refilled (actor ids): the scripted leader never waits for them.</summary>
    public IReadOnlyList<int> OpenSeats => _backfill.Open.Select(s => s.ActorId).ToList();

    private void HookScreen()
    {
        _queue ??= GetNodeOrNull<Queue>(QueuePath);
        if (_screen is not null || GetTree().GetFirstNodeInGroup(Screen.GroupName) is not Screen screen)
        {
            return;
        }
        _screen = screen;
        _screen.Queued += StartQueuedRun;
        if (StartsOnScreen && Run is null)
        {
            // Lane 3D-b: the first run is the one the player queues on the screen (Queued -> StartQueuedRun).
            screen.OpenFirst();
            _log?.Line(FormattableString.Invariant(
                $"SCREEN first open={(screen.IsOpen ? 1 : 0)} key={screen.State?.KeyLevel ?? 0}"));
        }
    }

    // --press-enter N (lane 3D-c's interactive proof): the scripted player waits EnterWaitFrames frames on each Enter
    // prompt (the screen up, or the run ended), then pushes one Enter through the viewport, so the screen's Queue and
    // this node's run-end Enter see the same event a keyboard sends. Never under --autorun.
    private void MaybeScriptedEnter()
    {
        if (_done || Options.Autorun || _enterPresses >= Options.PressEnter)
        {
            return;
        }
        if (_screen?.IsOpen != true && Run?.RunEnd.Result is null)
        {
            _enterWait = 0;
            return;
        }
        if (++_enterWait < EnterWaitFrames)
        {
            return;
        }
        _enterWait = 0;
        _enterPresses++;
        _log?.Line(FormattableString.Invariant($"INPUT enter n={_enterPresses} screen={(_screen?.IsOpen == true ? 1 : 0)}"));
        GetViewport().PushInput(new InputEventKey { Keycode = Key.Enter, PhysicalKeycode = Key.Enter, Pressed = true });
    }

    // Lane 3H-c: PlayerInput.Pinged (G, raised inside the human's Sample, so Run is still the pre-step run) becomes a
    // bots PingEvent, as hud-wiring.ts derivePingEvent builds it: a target ping on the human's target, else a spot ping.
    // The TS spot point is the crosshair's floor hit, falling back to the pinger; the port uses the pinger's position.
    private void RoutePing(int? targetId)
    {
        if (_bots is null || Run is not { } run || !run.State.Actors.TryGetValue(WardenId, out var me))
        {
            return;
        }
        var tick = run.State.Tick;
        var ping = targetId is { } target
            ? new PingEvent(tick, WardenId, PingKind.Target) { TargetId = target }
            : new PingEvent(tick, WardenId, PingKind.Spot) { Point = new PointMm(me.Pos.X, me.Pos.Z) };
        var kind = ping.Kind == PingKind.Target ? "target" : "spot";
        var at = ping.Point is { } p ? FormattableString.Invariant($" point=({p.X},{p.Z})") : string.Empty;
        if (!_bots.Ping(ping))
        {
            _log?.Line(FormattableString.Invariant($"PING limited kind={kind} tick={tick}"));
            return;
        }
        _log?.Line(FormattableString.Invariant($"PING routed kind={kind} target={(targetId?.ToString(CultureInfo.InvariantCulture) ?? "none")} tick={tick}{at}"));
    }

    // --press-ping T (lane 3H-c's interactive proof): once the live run reaches tick T the scripted player pushes one G
    // through the viewport, so PlayerInput queues the ping key exactly as a keyboard press does. Never under --autorun.
    private void MaybeScriptedPing(Run run)
    {
        if (_pingPressed || Options.Autorun || Options.PressPing == 0 || run.State.Tick < Options.PressPing ||
            run.RunEnd.Result is not null || _screen?.IsOpen == true)
        {
            return;
        }
        _pingPressed = true;
        _log?.Line(FormattableString.Invariant($"INPUT ping tick={run.State.Tick}"));
        GetViewport().PushInput(new InputEventKey { Keycode = Key.G, PhysicalKeycode = Key.G, Pressed = true });
    }

    // --press-yaw T:Y (lane 3E-c's interactive proof): once the live run reaches tick T the scripted player turns the
    // CameraRig to Y through CameraRig.Look, the call PlayerInput's mouse handler makes, so the heading changes exactly as
    // a mouse turn changes it (headless has no captured mouse to move). Never under --autorun.
    private void MaybeScriptedYaw(Run run)
    {
        if (_yawPressed || Options.Autorun || Options.PressYaw is not { } plan || _rig is null || run.State.Tick < plan.Tick ||
            run.RunEnd.Result is not null || _screen?.IsOpen == true)
        {
            return;
        }
        _yawPressed = true;
        _rig.Look(plan.Yaw - _rig.Yaw, 0);
        _log?.Line(FormattableString.Invariant($"INPUT yaw tick={run.State.Tick} yaw={InputLog.FormatYaw(_rig.Yaw)}"));
    }

    // --abandon-seat A@T (lane 3J's scripted proof): at run tick T the seat empties and, with --joiner-key K, one
    // request at key K joins the Queue node (the screen's Queue button is not pressed mid-run).
    private void MaybeScriptedAbandon(Run run)
    {
        if (Options.AbandonSeat is not { } plan || run.State.Tick != plan.Tick || run.RunEnd.Result is not null)
        {
            return;
        }
        AbandonSeat(plan.ActorId);
        _log?.Line(FormattableString.Invariant($"SEAT abandon actor={plan.ActorId} tick={run.State.Tick}"));
        if (Options.JoinerKey is { } key && _queue is not null)
        {
            var clause = run.Encounter.Rules.ActiveClauseId
                ?? Registry?.Clauses.Keys.OrderBy(k => k, StringComparer.Ordinal).FirstOrDefault()
                ?? "none";
            _queue.Add(new QueueRequest(DungeonId, key, clause));
        }
    }

    // request null: the CLI run (--key, no Clause), the --autorun/--solo start and the --solo Enter restart. Otherwise the queued
    // run, which is also the interactive game's first run (lane 3D-b, StartsOnScreen).
    private void StartRun(QueueRequest? request)
    {
        var registry = Registry ?? throw new InvalidOperationException("SimDriver started a run before content loaded");
        var dungeonId = request?.DungeonId ?? DungeonId;
        var keyLevel = request?.KeyLevel ?? Options.KeyLevel;
        // LOBBY-TABS: the class picked on the screen's Character tab builds the party (the human takes that class's
        // roster seat, bots fill the other two); a CLI run (no request) plays the default Warden. The class is read from
        // the screen, never from the queue payload (Lobby.ClassFor).
        var classId = Lobby.ClassFor(request, _screen?.CurrentLobby);
        if (Setup is null || Setup.Dungeon.Id != dungeonId || Setup.Content.KeyLevel != keyLevel || Setup.HumanClassId != classId)
        {
            Setup = HostRunSetup.Build(registry, dungeonId, keyLevel, classId, withBots: !Options.Solo, rotationIndex: RotationIndex);
        }
        var setup = Setup;
        var dungeon = setup.Dungeon;
        if (Options.StartPull > 1)
        {
            if (Options.StartPull > dungeon.Pulls.Count)
            {
                throw new ArgumentException($"--pull {Options.StartPull} is past the dungeon's {dungeon.Pulls.Count} pulls");
            }
            dungeon = dungeon with { Pulls = dungeon.Pulls.Skip(Options.StartPull - 1).ToList() };
        }
        // Lane NAV-CORE: a --pull N run starts its party in pull N's room (HostRunSetup.PartyFor), else in the Entrance.
        // Finding f-f9b80112 (BIBLE s7): the screen's equipped gear, keystone included, reaches the run on top of the
        // request's Clause and talents (RunStart.Of; no screen yet means the request's options alone).
        var start = setup.StartFor(request, _screen?.Progression, registry, classId);
        // The run's options record the weekly rotation index the setup was built with (the sim reads it as an input).
        var options = (start.Options ?? new RunOptions()) with { RotationIndex = setup.RotationIndex };
        // Bot seats wear their default gear (HostRunSetup.BotLoadouts, BIBLE s6 parity) next to the human's loadouts.
        var party = setup.PartyFor(dungeon);
        options = setup.WithBotLoadouts(options, party) ?? options;
        var created = RunRoot.CreateRun(Options.Seed, party, dungeon, setup.Content, options);
        if (Options.StartPull > 1)
        {
            // A --pull N start begins in pull N's fight (NavLayouts.PlaceParty; STATE.md D27 spawned it at creation).
            // PRESPAWN-AGGRO leaves that pull idle with the party at its room start, outside the aggro ring, so an idle
            // human never fights (HudSceneTest runs I and K1 never wiped): engage it here, the host's explicit fight start.
            created = RunRoot.EngageStartPull(created);
        }
        // 3F's hand-off (its decision 6): arm the boss on the created run's encounter.
        var run = created with { Encounter = BossRules.Arm(created.Encounter, setup.Boss) };
        Run = run;
        _backfill = BackfillState.Empty;
        State = run.State;
        PreviousState = run.State;
        LastEvents = Array.Empty<SimEvent>();
        _frameSteps.Reset(run.State);
        _movie = Options.MovieDir is null ? null : new MovieDirector(WardenId, run.Content.Abilities);
        _bots?.Reset();
        _log?.Line(FormattableString.Invariant(
            $"RUN start autorun={(Options.Autorun ? 1 : 0)} ticks={Options.Ticks} hz={Tick.TickHz} dungeon={setup.Dungeon.Id} key={setup.Content.KeyLevel} clause={run.Encounter.Rules.ActiveClauseId ?? "none"} seed={Options.Seed} actors={run.State.Actors.Count}"));
        if (start.Gear is { } gear)
        {
            _log?.Line(FormattableString.Invariant(
                $"GEAR run items={gear.Gear.Count} facets={(gear.Gear.Count == 0 ? "none" : string.Join(",", gear.Facets.Select(f => f.Id)))} patched={gear.Abilities.Count} unapplied={gear.UnappliedFacetIds.Count}"));
        }
        foreach (var (actor, botGear) in setup.BotGear.Where(kv => party.Any(p => p.Id == kv.Key)).OrderBy(kv => kv.Key))
        {
            _log?.Line(FormattableString.Invariant(
                $"GEAR bot actor={actor} applied={botGear.Applied.Count} skipped={botGear.Skipped.Count} facets={(botGear.Applied.Count > 0 ? string.Join(',', botGear.Applied.Select(f => f.FacetId)) : "-")}"));
        }
        var boss = run.Encounter.Boss?.Config ?? throw new InvalidOperationException("SimDriver: the run's boss is not armed");
        _log?.Line(FormattableString.Invariant(
            $"BOSS armed twins={boss.Twins.Count} lockAt={boss.LockThresholdBp / 100} window={boss.LockWindowTicks} bar={boss.TwinsBar.Id} boss={boss.BossActorId}"));
        if (Options.CarelessSeat is { } careless)
        {
            // CARELESS-SEAT: name the careless seat and the group it strays toward, so a scene test can tie a pull to it.
            var group = string.Join(",", careless.For(setup.Dungeon)[careless.ActorId].GroupActorIds.Select(id => id.ToString(CultureInfo.InvariantCulture)));
            _log?.Line(FormattableString.Invariant($"BOTS careless seat={careless.ActorId} from={careless.Tick} group={careless.GroupId} actors={group}"));
        }
        // createRun spawns pull 0 and drops its pull_start (it has no event sink); the log names it here. On a layout
        // run every group stands idle from tick 0 (PRESPAWN-AGGRO) and none is engaged at creation except a --pull N start's
        // (EngageStartPull above): each other pull_start comes on its aggro-radius engage and RunLog names it then.
        if (Pulls.IsEngaged(run.Encounter, dungeon, run.Encounter.PullIndex))
        {
            _log?.PullSpawn(run.Encounter.PullIndex, dungeon);
        }
        // Every other group stands idle from tick 0: name its enemies once, so the log accounts for a skipped pack.
        _log?.IdleGroups(run);
        _log?.Begin(run.State);
        RunStarted?.Invoke();
    }

    private void StepOnce()
    {
        var run = Run!;
        var preTick = run.State.Tick;
        MaybeScriptedAbandon(run);
        _commands.Clear();
        // sim-runtime.ts step(): the human's commands first, then every bot seat's (one runBots call).
        _input?.Sample(preTick, run, _commands);
        if (!Options.BotsHold)
        {
            // --bots-hold: the bot seats stay in the party but act on nothing (HudSceneTest run I).
            _bots?.Sample(preTick, run, _commands);
        }
        foreach (var reaction in _bots?.TakeReactions() ?? Array.Empty<string>())
        {
            _log?.Line(reaction);
        }
        // Lane 3J (3G's hand-off): RunRoot.StepRun through the backfill book; an open seat's commands are dropped.
        var queue = _queue;
        var step = Backfill.Step(_backfill, run, _commands, queue?.Requests ?? Array.Empty<QueueRequest>());
        var result = step.Step;
        _backfill = step.Backfill;
        Run = result.Run;
        PreviousState = run.State;
        State = result.Run.State;
        LastEvents = result.Events;
        _frameSteps.Step(Frames, run.State, result.Events);
        TicksRun++;
        _log?.Tick(preTick, run.State, State, result.Events, result.Run.Dungeon);
        foreach (var fill in step.Fills)
        {
            _log?.Line(FormattableString.Invariant($"SEAT refill actor={fill.ActorId} tick={fill.FillTick} key={fill.Joiner.KeyLevel}"));
        }
        queue?.Consume(step.ConsumedQueueIndices);
        if (run.RunEnd.Result is null && result.Run.RunEnd.Result is not null)
        {
            SaveRecording(final: false);
        }
        Stepped?.Invoke(preTick, result.Events);
        MaybeCapture(run.State, State, result.Events);
    }

    private void MaybeCapture(SimState before, SimState after, IReadOnlyList<SimEvent> events)
    {
        if (Options.MovieDir is not { } dir || _movie is null)
        {
            return;
        }
        foreach (var shot in _movie.Observe(before, after, events))
        {
            _pendingCaptures++;
            CaptureAfterDraw(Path.Combine(dir, shot.Name), after.Tick, shot);
        }
    }

    private async void CaptureAfterDraw(string path, int tick, MovieShot shot)
    {
        // Wait for the frame that shows this tick to finish drawing, then save it. The FRAME line names the trace
        // event the frame was taken for and that event's tick (f-50d8099e).
        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        var image = GetViewport().GetTexture().GetImage();
        var err = image.SavePng(path);
        _log?.Line(FormattableString.Invariant(
            $"FRAME {path} tick={tick} event=\"{shot.Event}\" event_tick={shot.EventTick} {(err == Error.Ok ? "ok" : "error " + err)}"));
        _pendingCaptures--;
    }

    private void Finish()
    {
        _done = true;
        var frameMs = (long)Math.Round(_accumulator.ElapsedNanos / 1_000_000.0);
        var warden = State.Actors.TryGetValue(WardenId, out var w) ? w : null;
        var pos = warden?.Pos ?? Vec3.Zero;
        var result = Run?.RunEnd.Result is { } r ? r.ToJs() : "running";
        _log?.Line(FormattableString.Invariant(
            $"AUTORUN done ticks={TicksRun} frames={Frames} frame_ms={frameMs} dropped={_accumulator.DroppedTicks} warden=({pos.X},{pos.Z}) hp={warden?.Health ?? 0} run={result}"));
        if (_bots is not null)
        {
            _log?.Line(_bots.ProofLine());
        }
        _log?.Dispose();
        GetTree().Quit(0);
    }
}

/// <summary>Content display names for actors and abilities (Godot-free).</summary>
public static class Names
{
    /// <summary>An actor's enemy or class display name, else <paramref name="classId"/>.</summary>
    /// <param name="registry">The content, or null.</param>
    /// <param name="classId">The actor's class id.</param>
    /// <returns>The name.</returns>
    public static string Actor(ContentRegistry? registry, string classId)
    {
        if (registry is null)
        {
            return classId;
        }
        if (registry.Enemies.TryGetValue(classId, out var enemy))
        {
            return enemy.Name;
        }
        return registry.Classes.TryGetValue(classId, out var cls) ? cls.Name : classId;
    }

    /// <summary>An ability's display name, else <paramref name="abilityId"/>.</summary>
    /// <param name="registry">The content, or null.</param>
    /// <param name="abilityId">The ability id.</param>
    /// <returns>The name.</returns>
    public static string Ability(ContentRegistry? registry, string abilityId) =>
        registry is not null && registry.Abilities.TryGetValue(abilityId, out var def) ? def.Name : abilityId;
}
