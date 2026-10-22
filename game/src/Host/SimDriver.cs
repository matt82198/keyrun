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
/// <para>Finding f-e24b4fa4 (BIBLE s10 "ready check on pop", RULING R-229): a <see cref="Screen.Queued"/> request no
/// longer starts the run at once. The host pops the Queue node's ready check (<see cref="Keyrun.Hud.Queue.Pop"/>, the
/// pure <see cref="QueuedRunGate"/> over Keyrun.Sim's 10-second <see cref="ReadyCheck"/>) for the human seat plus the bot
/// seats; the bots answer ready at once and the human answers Enter or Esc on the "Group found" line. All ready
/// (<see cref="QueuedRunGate.Entered"/>) starts the queued run through <see cref="StartQueuedRun"/>, as before; a decline
/// or the timeout (<see cref="QueuedRunGate.Requeued"/>) drops the request from the Queue node and reopens the screen
/// (<see cref="Screen.OpenFirst"/>, logged <c>SCREEN requeue requeued=S open=1 key=K</c>). While the check is open Enter
/// belongs to it: the run-end Enter and the scripted <c>--press-enter</c> wait. A scripted launch (<c>--autorun</c>,
/// <c>--press-enter</c>, and a <c>--replay-input</c> of a log without recorded ready answers) answers ready for the human
/// at the pop (<see cref="QueuedRunGate.HumanReadyAtPop"/>), so its run starts on the frame and tick it always did,
/// logged <c>QUEUE ready seat=N answer=ready tick=T</c>; <c>--ready-hold F</c> keeps the dialog up F frames first.</para>
/// </remarks>
public partial class SimDriver : Node
{
    /// <summary>The human Warden's actor id.</summary>
    public const int WardenId = 1;

    /// <summary>The dungeon played.</summary>
    public const string DungeonId = DungeonPick.DefaultId;

    /// <summary>
    /// D65 / f-61f01ca3: the dungeon this launch plays, <c>--dungeon ID</c> resolved against the content
    /// (<see cref="DungeonPick.Resolve"/>); <see cref="DungeonId"/>, the default, until content loads.
    /// </summary>
    public string SelectedDungeonId { get; private set; } = DungeonId;

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
    // R-415-D: the run tick of a pull-relative --abandon-seat plan's anchor pull_start, once this run has seen it.
    private int? _abandonAnchor;
    private RunLog? _log;
    private int _pendingCaptures;
    // f-50d8099e: --write-movie frames are picked from the trace (MovieDirector), one director per run.
    private MovieDirector? _movie;
    private int _enterPresses;
    private int _enterWait;
    private bool _pingPressed;
    private InputRecorder? _recorder;
    private InputReplayer? _replayer;
    // f-6a00609e / f-6b922005 (R-CLOCK-1): the one input-log clock a recording stamps and a replay reads (first-Screen
    // frames until the first run exists, then those frames plus TicksRun). Set whenever this launch records or replays.
    private ReplayClock? _inputClock;
    private InputLog? _replayLog;
    private int _replayTotal;
    private int _replayCameraTotal;
    private float? _replayStartHeading;
    private bool _replayDoneLogged;
    private CameraRig? _rig;
    private bool _cameraBaselined;
    private bool _yawPressed;
    private int _readyHoldWait;
    private bool _talentsLogged;
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
            // D65: an unknown --dungeon id stops the launch (ArgumentException naming the id and the known ones).
            SelectedDungeonId = DungeonPick.Resolve(Registry, Options.DungeonId);
            // D45/D46 (f-46043bc6): --dungeon-seed N plays the seeded two-branch dungeon (SeededLayout: graph, grid, fill,
            // layout); the bots walk the branch the seed picks. Without it the run plays the hand-built Hollow Vault.
            // A multi-segment dungeon (the Sunken Bellfoundry, R-MULTIBOSS-1) is always seeded: DungeonPick.BuildSetup
            // takes --dungeon-seed N, else --seed N, so this two-branch Hollow Vault rebuild is for the tutorial only.
            if (Options.DungeonSeed is { } dungeonSeed && Registry.Dungeons[SelectedDungeonId].Segments.Count <= 1)
            {
                var seeded = SeededLayout.Build(Registry, dungeonSeed);
                Registry = seeded.Registry;
                GD.Print(FormattableString.Invariant(
                    $"DUNGEON seeded seed={dungeonSeed} branch={seeded.Branch} walked={string.Join(",", seeded.Fill.Branches[seeded.Branch].Rooms.Select(SeededLayout.RoomBoxId))} side={string.Join(",", seeded.Fill.Branches[1 - seeded.Branch].Rooms.Select(SeededLayout.RoomBoxId))} pulls={string.Join(",", seeded.Registry.Dungeons[SelectedDungeonId].Pulls.Select(p => p.Id))} forces={seeded.Fill.Branches[seeded.Branch].Points}/{seeded.Fill.TotalPoints}"));
            }
            // The weekly affix rotation (AffixWeek, Matt 2026-10-20 "lean into m+ design"): a live run takes the index
            // of the week since the last US reset (Tuesday 15:00 UTC), read from the UTC clock here in the host, never in
            // the sim, unless --rotation N pins it; a --replay-input run takes the index its log recorded, never the
            // clock. Read the log first so the setup is built at the recorded index. A --write-movie launch without
            // --rotation N is pinned to AffixWeek.MovieRotationIndex (f-e1ff13e3): its frames are evidence, never the week's.
            _replayLog = !Options.Autorun && Options.ReplayInput is { } replayPath ? InputLog.Load(replayPath) : null;
            RotationIndex = AffixWeek.ForRun(
                _replayLog is not null, _replayLog?.RotationIndex, Options.Rotation, DateTime.UtcNow, AffixWeek.RotationLength(Registry.Clauses.Count),
                writeMovie: Options.MovieDir is not null);
            Setup = DungeonPick.BuildSetup(Registry, SelectedDungeonId, Options.Seed, Options.DungeonSeed, Options.KeyLevel, HumanClassId, withBots: !Options.Solo, rotationIndex: RotationIndex);
            _log = new RunLog(Options.LogPath, Options.Autorun, Registry);
            _player = GetNode<PlayerInput>(InputPath);
            _rig = GetNodeOrNull<CameraRig>(CameraRigPath);
            // CAMERA-VERTICAL: --camera chase|vertical picks the rig's preset (V flips it in play).
            _rig?.SetMode(Options.Camera);
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
        MaybeReadyHold();
        MaybeReplay();
        if (_done || Run is null)
        {
            // f-6a00609e: a frame the first Screen was up with no run is one tick of the input-log clock, so a replayed
            // log can drive the Screen and its talent panel on spaced frames before Enter starts the run, and (f-6b922005)
            // a recording stamps first-Screen keys on the same frames.
            if (!_done)
            {
                _inputClock?.EndFrame(Run is not null, _screen is not null);
            }
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
            if (_recorder is { } recorder && _inputClock is { } clock)
            {
                // f-6b922005 (R-CLOCK-1): stamped on the input-log clock a replay reads, never bare TicksRun.
                var stamp = clock.Stamp(Run is not null, TicksRun);
                recorder.SyncPolled(stamp, PlayerInput.PolledKeys, Input.IsPhysicalKeyPressed);
                if (_rig is not null)
                {
                    recorder.SyncCamera(stamp, _rig.Yaw);
                }
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
        // f-6b922005 (R-CLOCK-1): the stamp is the input-log clock (first-Screen frames, then those plus TicksRun), the
        // value a replay of the log reads, so a key on the Screen and one in the run's first ticks stay in order.
        if (_recorder is { } recorder && _inputClock is { } clock && @event is InputEventKey { Echo: false } key)
        {
            recorder.OnKey(clock.Stamp(Run is not null, TicksRun), key.PhysicalKeycode != Key.None ? key.PhysicalKeycode : key.Keycode, key.Pressed);
        }
    }

    /// <inheritdoc />
    public override void _UnhandledInput(InputEvent @event)
    {
        // f-e24b4fa4: while the queue pop's ready check is open, Enter is its answer (Queue), never the run's.
        if (Options.Autorun || Run?.RunEnd.Result is not { } result || _screen?.IsOpen == true || _queue?.Checking == true ||
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
            _screen.Queued -= OnQueued;
        }
        if (_queue is not null)
        {
            _queue.Gate.Entered -= StartQueuedRun;
            _queue.Gate.Requeued -= OnPopRequeued;
            _queue.KeyAnswered -= OnReadyKey;
        }
        if (_player is not null)
        {
            _player.Pinged -= RoutePing;
        }
        SaveRecording(final: true);
        _log?.Dispose();
    }

    // request null: the CLI run (--key, no Clause), the --autorun/--solo start and the --solo Enter restart. Otherwise the queued
    // run, which is also the interactive game's first run (lane 3D-b, StartsOnScreen).
    private void StartRun(QueueRequest? request)
    {
        var registry = Registry ?? throw new InvalidOperationException("SimDriver started a run before content loaded");
        var dungeonId = request?.DungeonId ?? SelectedDungeonId;
        var keyLevel = request?.KeyLevel ?? Options.KeyLevel;
        // LOBBY-TABS: the class picked on the screen's Character tab builds the party (the human takes that class's
        // roster seat, bots fill the other two); a CLI run (no request) plays the default Warden. The class is read from
        // the screen, never from the queue payload (Lobby.ClassFor).
        var classId = Lobby.ClassFor(request, _screen?.CurrentLobby);
        if (Setup is null || Setup.Dungeon.Id != dungeonId || Setup.Content.KeyLevel != keyLevel || Setup.HumanClassId != classId)
        {
            Setup = DungeonPick.BuildSetup(registry, dungeonId, Options.Seed, Options.DungeonSeed, keyLevel, classId, withBots: !Options.Solo, rotationIndex: RotationIndex);
        }
        var setup = Setup;
        // R7: the --pull N slice, StartFor, WithBotLoadouts, CreateRun, EngageStartPull and BossRules.Arm, in that order,
        // live in HostRunFactory, the one assembly path PlaytestRunner and the tests share.
        var (run, start, party, dungeon) = HostRunFactory.Create(setup, new RunRequest(Options.Seed, registry, classId)
        {
            Queued = request,
            Progression = _screen?.Progression,
            StartPull = Options.StartPull,
            Talents = Options.Talents,
        });
        Run = run;
        if (!_talentsLogged)
        {
            // f-a3de0a01: each --talent pick, once per launch, after the factory accepted it (a bad pick exits 1 above).
            _talentsLogged = true;
            foreach (var talent in Options.Talents)
            {
                _log?.Line(talent.ProofLine());
            }
        }
        _backfill = BackfillState.Empty;
        // A pull engaged at creation (a --pull N start) drops its pull_start (see below): it started at the run's first tick.
        // A health-relative plan (f-0fd3e6fb) still waits for that pull's health to fall.
        _abandonAnchor = Options.AbandonSeat is { HealthPercent: null, AnchorPull: { } anchorPull } && anchorPull == run.Encounter.PullIndex &&
            Pulls.IsEngaged(run.Encounter, dungeon, run.Encounter.PullIndex) ? run.State.Tick : null;
        State = run.State;
        PreviousState = run.State;
        LastEvents = Array.Empty<SimEvent>();
        _frameSteps.Reset(run.State);
        _movie = Options.MovieDir is null ? null : new MovieDirector(WardenId, run.Content.Abilities);
        _bots?.Reset();
        _log?.Line(FormattableString.Invariant(
            $"RUN start autorun={(Options.Autorun ? 1 : 0)} ticks={Options.Ticks} hz={Tick.TickHz} dungeon={setup.Dungeon.Id} key={setup.Content.KeyLevel} clause={run.Encounter.Rules.ActiveClauseId ?? "none"} seed={Options.Seed} actors={run.State.Actors.Count}"));
        // HUMAN-PLAY (D65/D66): who sits where, so a replay or a capture can tie seat 1 to the class the screen picked.
        _log?.Line(FormattableString.Invariant(
            $"RUN seats human={setup.HumanClassId} bots={(setup.BotActorIds.Count == 0 ? "none" : string.Join(",", setup.BotActorIds.Select(id => FormattableString.Invariant($"{id}:{party.First(p => p.Id == id).ClassId}"))))}"));
        if (run.Content.Layout is { } navLayout)
        {
            // R-PATH-1: one PATH line per path the sim's grid computes (a straight line that was blocked), so the capture
            // log proves actors walk routes; a straight walk in sight prints nothing.
            Keyrun.Sim.Nav.NavGrid.Of(navLayout).Observer = (id, goal, n) =>
                _log?.Line(FormattableString.Invariant($"PATH actor={id} goal={goal.X},{goal.Z} waypoints={n}"));
        }
        // CAMERA-VERTICAL proof line: the rig's mode, pitch (mrad) and zoom (mm) as it holds them at run start.
        var cameraView = CameraMode.For(Options.Camera);
        _log?.Line(_rig is { } rig
            ? CameraMode.ProofLine(rig.Mode, rig.PitchMrad, rig.ZoomMm)
            : CameraMode.ProofLine(Options.Camera, cameraView.PitchMrad, cameraView.BoomMm));
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
        _abandonAnchor ??= Options.AbandonSeat?.AnchorAt(preTick, result.Events, result.Run.Dungeon, result.Run.State.Actors);
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
