using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Godot;
using Keyrun.Bots;
using Keyrun.Content;
using Keyrun.Hud;
using Keyrun.Sim.Core;
using Keyrun.Sim.Encounter;
using Keyrun.Sim.Runs;

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
/// <c>--seed N</c>, <c>--pull N</c>, <c>--solo</c>, <c>--requeue</c>, <c>--abandon-seat A@T</c>, <c>--joiner-key K</c>.
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
    int? JoinerKey = null)
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

    /// <summary>Parses user arguments; unknown arguments are ignored (Godot passes them through verbatim).</summary>
    /// <exception cref="ArgumentException">A flag is missing its value, or a number flag is not a positive integer.</exception>
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
        var requeue = false;
        SeatAbandonPlan? abandon = null;
        int? joinerKey = null;
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
                case "--log":
                    log = Value(args, ref i);
                    break;
                case "--write-movie":
                    movie = Value(args, ref i);
                    autorun = true; // proof frames come from the scripted run
                    break;
            }
        }
        return new ArenaOptions(autorun, ticks ?? DefaultTicks, log, movie, key, seed, pull, solo, requeue, abandon, joinerKey);
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
/// <c>--write-movie DIR</c> saves three proof frames (Choir chase, lieutenant heal cast bar, after the kick).
/// Interactive: once the run ends, Enter starts a fresh run at <c>--key</c> with no Clause (unless the between-runs
/// screen is up: it owns Enter then).
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

    /// <summary>The human's class.</summary>
    public const string HumanClassId = "warden";

    private const int MaxStepsPerFrame = 5;

    // Proof frames: the post-step tick at which to capture, and file name. Timings follow ScriptedWarden at key 2: the
    // Choir closes in the first second, the lieutenant's 80-tick heal starts at tick 196, and the Warden kicks it once
    // it is half cast (tick 236 or the first tick after that his Shield Bash is in range).
    private static readonly (int Tick, string Name)[] MovieFrames =
    {
        (8, "01-choir-chase.png"),
        (220, "02-lieutenant-cast-bar.png"),
        (246, "03-after-kick.png"),
    };

    private readonly TickAccumulator _accumulator = new(Tick.TickHz, MaxStepsPerFrame);
    private readonly List<Command> _commands = new();
    private ICommandSource? _input;
    private BotCommandSource? _bots;
    private Screen? _screen;
    private Queue? _queue;
    private BackfillState _backfill = BackfillState.Empty;
    private RunLog? _log;
    private int _pendingCaptures;
    private bool _done;

    /// <summary>The PlayerInput node.</summary>
    [Export]
    public NodePath InputPath { get; set; } = new("../PlayerInput");

    /// <summary>The Queue node whose requests backfill reads (lane 3J; Arena.tscn mounts it at Hud/Screen/Queue).</summary>
    [Export]
    public NodePath QueuePath { get; set; } = new("../Hud/Screen/Queue");

    /// <summary>Parsed user arguments.</summary>
    public ArenaOptions Options { get; private set; } = new(false, ArenaOptions.DefaultTicks, null, null);

    /// <summary>The validated content (display names for the views).</summary>
    public ContentRegistry? Registry { get; private set; }

    /// <summary>The run's setup (party, dungeon, content, the human's ability slots).</summary>
    public HostRunSetup? Setup { get; private set; }

    /// <summary>The current run, or null before <see cref="_Ready"/> succeeded.</summary>
    public Run? Run { get; private set; }

    /// <summary>The state after the latest step.</summary>
    public SimState State { get; private set; } = SimState.Create(1);

    /// <summary>The state before the latest step (render interpolation start).</summary>
    public SimState PreviousState { get; private set; } = SimState.Create(1);

    /// <summary>Events of the latest step.</summary>
    public IReadOnlyList<SimEvent> LastEvents { get; private set; } = Array.Empty<SimEvent>();

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
            Setup = HostRunSetup.Build(Registry, DungeonId, Options.KeyLevel, HumanClassId, withBots: !Options.Solo);
            _log = new RunLog(Options.LogPath, Options.Autorun, Registry);
            _input = GetNode<PlayerInput>(InputPath);
            _bots = new BotCommandSource(Setup.BotActorIds, KitRegistry.From(Registry), Options.Seed);
            StartRun(null);
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
        if (_done || Run is null)
        {
            return;
        }
        Frames++;
        var steps = _accumulator.Advance(delta);
        for (var i = 0; i < steps; i++)
        {
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
    public override void _UnhandledInput(InputEvent @event)
    {
        if (!Options.Autorun && Run?.RunEnd.Result is not null && _screen?.IsOpen != true &&
            @event is InputEventKey { Pressed: true, Echo: false, Keycode: Key.Enter or Key.KpEnter })
        {
            StartRun(null);
        }
    }

    /// <inheritdoc />
    public override void _ExitTree()
    {
        if (_screen is not null)
        {
            _screen.Queued -= StartQueuedRun;
        }
        _log?.Dispose();
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

    private void HookScreen()
    {
        _queue ??= GetNodeOrNull<Queue>(QueuePath);
        if (_screen is not null || GetTree().GetFirstNodeInGroup(Screen.GroupName) is not Screen screen)
        {
            return;
        }
        _screen = screen;
        _screen.Queued += StartQueuedRun;
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

    // request null: the CLI run (--key, no Clause), today's start and Enter restart. Otherwise the queued run.
    private void StartRun(QueueRequest? request)
    {
        var registry = Registry ?? throw new InvalidOperationException("SimDriver started a run before content loaded");
        var dungeonId = request?.DungeonId ?? DungeonId;
        var keyLevel = request?.KeyLevel ?? Options.KeyLevel;
        if (Setup is null || Setup.Dungeon.Id != dungeonId || Setup.Content.KeyLevel != keyLevel)
        {
            Setup = HostRunSetup.Build(registry, dungeonId, keyLevel, HumanClassId, withBots: !Options.Solo);
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
        var created = RunRoot.CreateRun(Options.Seed, setup.Party, dungeon, setup.Content, request?.ToRunOptions());
        // 3F's hand-off (its decision 6): arm the boss on the created run's encounter.
        var run = created with { Encounter = BossRules.Arm(created.Encounter, setup.Boss) };
        Run = run;
        _backfill = BackfillState.Empty;
        State = run.State;
        PreviousState = run.State;
        LastEvents = Array.Empty<SimEvent>();
        _bots?.Reset();
        _log?.Line(FormattableString.Invariant(
            $"RUN start autorun={(Options.Autorun ? 1 : 0)} ticks={Options.Ticks} hz={Tick.TickHz} dungeon={setup.Dungeon.Id} key={setup.Content.KeyLevel} clause={run.Encounter.Rules.ActiveClauseId ?? "none"} seed={Options.Seed} actors={run.State.Actors.Count}"));
        var boss = run.Encounter.Boss?.Config ?? throw new InvalidOperationException("SimDriver: the run's boss is not armed");
        _log?.Line(FormattableString.Invariant(
            $"BOSS armed twins={boss.Twins.Count} lockAt={boss.LockThresholdBp / 100} window={boss.LockWindowTicks} bar={boss.TwinsBar.Id} boss={boss.BossActorId}"));
        // createRun spawns pull 0 and drops its pull_start (it has no event sink); the log names it here.
        _log?.PullSpawn(run.Encounter.PullIndex, dungeon);
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
        _bots?.Sample(preTick, run, _commands);
        // Lane 3J (3G's hand-off): RunRoot.StepRun through the backfill book; an open seat's commands are dropped.
        var queue = _queue;
        var step = Backfill.Step(_backfill, run, _commands, queue?.Requests ?? Array.Empty<QueueRequest>());
        var result = step.Step;
        _backfill = step.Backfill;
        Run = result.Run;
        PreviousState = run.State;
        State = result.Run.State;
        LastEvents = result.Events;
        TicksRun++;
        _log?.Tick(preTick, run.State, State, result.Events, result.Run.Dungeon);
        foreach (var fill in step.Fills)
        {
            _log?.Line(FormattableString.Invariant($"SEAT refill actor={fill.ActorId} tick={fill.FillTick} key={fill.Joiner.KeyLevel}"));
        }
        queue?.Consume(step.ConsumedQueueIndices);
        Stepped?.Invoke(preTick, result.Events);
        MaybeCapture(State.Tick);
    }

    private void MaybeCapture(int tickReached)
    {
        if (Options.MovieDir is not { } dir)
        {
            return;
        }
        foreach (var (tick, name) in MovieFrames)
        {
            if (tick == tickReached)
            {
                _pendingCaptures++;
                CaptureAfterDraw(Path.Combine(dir, name));
            }
        }
    }

    private async void CaptureAfterDraw(string path)
    {
        // Wait for the frame that shows this tick to finish drawing, then save it.
        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        var image = GetViewport().GetTexture().GetImage();
        var err = image.SavePng(path);
        _log?.Line($"FRAME {path} tick={State.Tick} {(err == Error.Ok ? "ok" : "error " + err)}");
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
