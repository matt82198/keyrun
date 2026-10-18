using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using Godot;
using Keyrun.Content;
using Keyrun.Sim.Combat;
using Keyrun.Sim.Core;
using Keyrun.Sim.Encounter;

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
/// <c>--smoke</c>: <c>--autorun</c>, <c>--ticks N</c>, <c>--log FILE</c>, <c>--write-movie DIR</c>.
/// </summary>
/// <param name="Autorun">Scripted input instead of the keyboard, and quit after <see cref="Ticks"/> ticks.</param>
/// <param name="Ticks">Ticks to run when autorunning.</param>
/// <param name="LogPath">Optional run log file (one TICK line per tick plus EVENT lines).</param>
/// <param name="MovieDir">Optional directory for the three proof PNG frames (needs a rendering window).</param>
public sealed record ArenaOptions(bool Autorun, int Ticks, string? LogPath, string? MovieDir)
{
    /// <summary>Default autorun length: one full Cleave cycle plus its flash (resolve at tick 160).</summary>
    public const int DefaultTicks = 170;

    /// <summary>Parses user arguments; unknown arguments are ignored (Godot passes them through verbatim).</summary>
    /// <exception cref="ArgumentException">A flag is missing its value, or --ticks is not a positive integer.</exception>
    public static ArenaOptions Parse(IReadOnlyList<string> args)
    {
        var autorun = false;
        int? ticks = null;
        string? log = null;
        string? movie = null;
        for (var i = 0; i < args.Count; i++)
        {
            switch (args[i])
            {
                case "--autorun":
                    autorun = true;
                    break;
                case "--ticks":
                    var raw = Value(args, ref i);
                    if (!int.TryParse(raw, NumberStyles.None, CultureInfo.InvariantCulture, out var n) || n <= 0)
                    {
                        throw new ArgumentException($"--ticks needs a positive integer, got '{raw}'");
                    }
                    ticks = n;
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
        return new ArenaOptions(autorun, ticks ?? DefaultTicks, log, movie);
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

/// <summary>Supplies this tick's commands (keyboard or scripted).</summary>
public interface ICommandSource
{
    /// <summary>Appends the commands for <paramref name="tick"/> (the pre-step state tick) to <paramref name="into"/>.</summary>
    void Sample(int tick, SimState state, List<Command> into);
}

/// <summary>Encounter-side work run inside each step's rules (phase 1: only DebugCleaveEmitter).</summary>
public interface ITickHook
{
    /// <summary>Adds the hook's actors to the fresh state before tick 0.</summary>
    void Install(SimState state);

    /// <summary>Runs on the step's clone after movement (its Tick is still the pre-step tick).</summary>
    void OnStep(SimState state, Action<SimEvent> emit);

    /// <summary>Telegraphs pending after the latest step, for the view.</summary>
    IReadOnlyList<Telegraph> ActiveTelegraphs { get; }
}

/// <summary>
/// Runs the Keyrun.Sim tick at 20 Hz from Godot's frame loop with a fixed accumulator (D9), keeps the
/// previous and current state for render interpolation, and owns the phase 1 arena rules: move/dash
/// through Combat/Movement.cs, actor push-out, bounds clamp, then the encounter hooks. Keyrun.Sim stays
/// pure; this node is the only place frame time meets sim ticks.
/// </summary>
/// <remarks>
/// <c>--autorun --ticks N</c> replaces the keyboard with PlayerInput's scripted walk, prints one
/// <c>AUTORUN done ticks=N frames=F frame_ms=M</c> line and quits with exit code 0 once N ticks have run.
/// <c>--write-movie DIR</c> saves three proof frames (Warden mid-walk, cone mid-fill, cone resolved).
/// </remarks>
public partial class SimDriver : Node
{
    /// <summary>The human Warden's actor id.</summary>
    public const int WardenId = 1;

    /// <summary>
    /// Walk speed, actor radius and arena bounds, read from content: game/content/data/run.json through
    /// <see cref="ContentLoader.Load()"/> (validated, no defaults). The host holds no movement constants.
    /// The values were first taken from the retired TS client (client/src/sim-host/run-setup.ts:53-57); the
    /// content file is now the source of truth. Loading fails closed in <see cref="_Ready"/>.
    /// </summary>
    public MovementConfig? MovementRules { get; private set; }

    /// <summary>Maps the validated content run config onto the sim's movement rules.</summary>
    /// <param name="run">The loaded <see cref="ContentRegistry.Run"/>.</param>
    public static MovementConfig ToMovementConfig(RunConfig run)
    {
        ArgumentNullException.ThrowIfNull(run);
        var b = run.ArenaBounds;
        return new MovementConfig(run.Movement.SpeedMmPerTick, run.Movement.RadiusMm, new Bounds(b.MinX, b.MaxX, b.MinZ, b.MaxZ));
    }

    private const int MaxStepsPerFrame = 5;

    // Proof frames: tick at which to capture, and file name. The Cleave telegraph is registered on
    // tick 120 and resolves on tick 160 (every 160, lead 40); the walk runs on ticks 0..19.
    private static readonly (int Tick, string Name)[] MovieFrames =
    {
        (10, "01-warden-mid-walk.png"),
        (141, "02-cone-mid-fill.png"),
        (161, "03-cone-resolved.png"),
    };

    private readonly TickAccumulator _accumulator = new(Tick.TickHz, MaxStepsPerFrame);
    private readonly List<Command> _commands = new();
    private readonly List<ITickHook> _hooks = new();
    private ICommandSource? _input;
    private RunLog? _log;
    private int _pendingCaptures;
    private bool _done;

    /// <summary>The PlayerInput node.</summary>
    [Export]
    public NodePath InputPath { get; set; } = new("../PlayerInput");

    /// <summary>The DebugCleaveEmitter node (TEMPORARY, 2E replaces it with the encounter step).</summary>
    [Export]
    public NodePath EmitterPath { get; set; } = new("../DebugCleaveEmitter");

    /// <summary>Parsed user arguments.</summary>
    public ArenaOptions Options { get; private set; } = new(false, ArenaOptions.DefaultTicks, null, null);

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

    /// <summary>Telegraphs pending after the latest step, across all hooks.</summary>
    public IReadOnlyList<Telegraph> ActiveTelegraphs
    {
        get
        {
            if (_hooks.Count == 1)
            {
                return _hooks[0].ActiveTelegraphs;
            }
            var all = new List<Telegraph>();
            foreach (var h in _hooks)
            {
                all.AddRange(h.ActiveTelegraphs);
            }
            return all;
        }
    }

    /// <summary>Raised after every step with the pre-step tick and that step's events.</summary>
    public event Action<int, IReadOnlyList<SimEvent>>? Stepped;

    /// <inheritdoc />
    public override void _Ready()
    {
        try
        {
            Options = ArenaOptions.Parse(OS.GetCmdlineUserArgs());
            MovementRules = ToMovementConfig(ContentLoader.Load().Run);
            _log = new RunLog(Options.LogPath, Options.Autorun);
            _input = GetNode<PlayerInput>(InputPath);
            _hooks.Add(GetNode<DebugCleaveEmitter>(EmitterPath));

            var state = SimState.Create(1);
            state.Phase = "combat";
            state.Actors.Add(WardenId, new ActorState
            {
                Id = WardenId,
                Kind = ActorKind.Player,
                ClassId = "warden",
                Role = ActorRole.Tank,
                Pos = Vec3.Zero,
                Yaw = 256, // facing +z, toward the box
                Health = 1000,
                MaxHealth = 1000,
            });
            foreach (var hook in _hooks)
            {
                hook.Install(state);
            }
            State = state;
            PreviousState = state;
            _log.Line($"RUN start autorun={(Options.Autorun ? 1 : 0)} ticks={Options.Ticks} hz={Tick.TickHz} actors={state.Actors.Count}");

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
        if (_done)
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
    public override void _ExitTree()
    {
        _log?.Dispose();
    }

    private void StepOnce()
    {
        var preTick = State.Tick;
        _commands.Clear();
        _input?.Sample(preTick, State, _commands);
        var result = Tick.Step(State, _commands, Rules);
        PreviousState = State;
        State = result.State;
        LastEvents = result.Events;
        TicksRun++;
        _log?.Tick(preTick, State, result.Events);
        Stepped?.Invoke(preTick, result.Events);
        MaybeCapture(State.Tick);
    }

    // Phase 1 arena rules (sim/combat/rules.ts command loop, movement part only; 2B ports the full Rules.cs).
    private void Rules(SimState state, IReadOnlyList<Command> commands, Action<SimEvent> emit)
    {
        var movement = MovementRules ?? throw new InvalidOperationException("SimDriver stepped before content loaded");
        foreach (var cmd in commands)
        {
            if (!state.Actors.TryGetValue(cmd.ActorId, out var actor) || !actor.Alive)
            {
                continue;
            }
            switch (cmd)
            {
                case MoveCommand m:
                    Movement.ApplyMovement(actor, m.Dir, movement, 0, m.Yaw);
                    break;
                case DashCommand d:
                    Movement.ApplyMovement(actor, d.Dir, Movement.DashDistanceMm, 0, d.Yaw);
                    break;
            }
        }
        var actors = new List<ActorState>(state.Actors.Values);
        for (var i = 0; i < actors.Count; i++)
        {
            for (var j = i + 1; j < actors.Count; j++)
            {
                if (actors[i].Alive && actors[j].Alive)
                {
                    Movement.ResolveCollision(actors[i], actors[j], movement);
                }
            }
        }
        foreach (var actor in actors)
        {
            Movement.ClampToBounds(actor, movement);
        }
        foreach (var hook in _hooks)
        {
            hook.OnStep(state, emit);
        }
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
        var warden = State.Actors.TryGetValue(WardenId, out var w) ? w.Pos : Vec3.Zero;
        _log?.Line($"AUTORUN done ticks={TicksRun} frames={Frames} frame_ms={frameMs} dropped={_accumulator.DroppedTicks} warden=({warden.X},{warden.Z})");
        _log?.Dispose();
        GetTree().Quit(0);
    }
}
