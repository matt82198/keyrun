using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Keyrun.Bots.Shared;
using Keyrun.Host;
using Keyrun.Sim.Core;
using Keyrun.Sim.Runs;

namespace Keyrun.Hud;

/// <summary>
/// The Godot-side queue list (BIBLE s10 Queue band; ENGINE-PLAN 3G backfill). It holds every <see cref="QueueRequest"/>
/// the screen's Queue button raises (<see cref="Screen.Queued"/>), oldest first, and hands the backfill seam one
/// joiner for an open seat: <see cref="TakeFor"/> picks by the pure key band (<see cref="Backfill.PickJoiner"/>, exact
/// key 2-20), and <see cref="Consume"/> removes the requests a <see cref="Backfill.Step"/> reports as joined.
/// <para>BIBLE s10 "while queued, a role-scarcity readout; ready check on pop": each request keeps its seat's role, and
/// a line on the Screen reads the pure <see cref="RoleScarcity"/> of the requests at the screen's key (role names only,
/// never a number: s0 test 3). <see cref="Pop"/> opens the host's <see cref="QueuedRunGate"/> (a pure
/// <see cref="ReadyCheck"/>): bot seats answer ready at once, the human answers Enter (ready) or Esc (decline) inside the
/// 10-second window while the "Group found" line shows, and the gate's <see cref="QueuedRunGate.Entered"/> (SimDriver
/// starts the queued run) or <see cref="QueuedRunGate.Requeued"/> (SimDriver drops the request and reopens the screen)
/// ends it (finding f-e24b4fa4: SimDriver calls <see cref="Pop"/> on every <see cref="Screen.Queued"/>).</para>
/// The node holds no rules of its own. Under <c>--autorun</c> it prints "QUEUE add|take|consume|roles|pop|ready ..."
/// proof lines.
/// </summary>
public partial class Queue : Node
{
    /// <summary>How long a finished check's outcome ("Run starting." or "Back in the queue") stays on the readout.</summary>
    private const double OutcomeHoldSeconds = 2.0;

    /// <summary>Builds the node and its ready-check gate (proof lines go through the driver).</summary>
    public Queue()
    {
        Gate = new QueuedRunGate(GateLine);
        Gate.Entered += _ => _outcomeLeft = OutcomeHoldSeconds;
        Gate.Requeued += (_, _) => _outcomeLeft = OutcomeHoldSeconds;
    }

    /// <summary>The scarcity line's node name (a Label added under the Screen).</summary>
    public const string ReadoutNodeName = "QueueRoles";

    private readonly List<QueueRequest> _requests = new();
    private readonly List<ActorRole> _roles = new();
    private Screen? _screen;
    private SimDriver? _driver;
    private Label? _readout;
    private double _seconds;
    private double _outcomeLeft;
    private string? _lastRolesProof;

    /// <summary>The screen whose Queued event feeds this list (default: the parent Screen).</summary>
    [Export]
    public NodePath ScreenPath { get; set; } = new("..");

    /// <summary>The driver, for autorun proof lines.</summary>
    [Export]
    public NodePath DriverPath { get; set; } = new("../../../SimDriver");

    /// <summary>The host's ready-check gate (BIBLE s10 ready check on pop): SimDriver listens to its Entered and
    /// Requeued events.</summary>
    public QueuedRunGate Gate { get; }

    /// <summary>Raised when the human answers the open check with a key (Enter or Esc), so an input recording can say
    /// its ready answers are recorded keys.</summary>
    public event Action? KeyAnswered;

    /// <summary>True while a ready check waits on answers (Enter belongs to the dialog, never to the run).</summary>
    public bool Checking => Gate.Checking;

    /// <summary>The waiting requests, oldest first (the list <see cref="Backfill.Step"/> reads).</summary>
    public IReadOnlyList<QueueRequest> Requests => _requests;

    /// <summary>Each waiting request's seat role, parallel to <see cref="Requests"/>.</summary>
    public IReadOnlyList<ActorRole> Roles => _roles;

    /// <summary>The open or last ready check, or null before the first pop.</summary>
    public ReadyCheck? Check => Gate.Check;

    /// <summary>The node's ready-check clock in sim ticks (frame time at <see cref="Tick.TickHz"/>).</summary>
    public int ClockTick => (int)Math.Floor(_seconds * Tick.TickHz);

    /// <inheritdoc />
    public override void _Ready()
    {
        _driver = HudNodes.FindDriver(this, DriverPath);
        _screen = GetNodeOrNull<Screen>(ScreenPath);
        if (_screen is not null)
        {
            _screen.Queued += Add;
            // Placed every frame on ActionBarLayout.QueueReadout (finding f-3c1f4542: never over the ability bar).
            _readout = new Label
            {
                Name = ReadoutNodeName,
                HorizontalAlignment = HorizontalAlignment.Center,
                AutowrapMode = TextServer.AutowrapMode.WordSmart,
                MouseFilter = Control.MouseFilterEnum.Ignore,
                Visible = false,
            };
            _screen.CallDeferred(Node.MethodName.AddChild, _readout);
        }
    }

    /// <inheritdoc />
    public override void _ExitTree()
    {
        if (_screen is not null)
        {
            _screen.Queued -= Add;
        }
    }

    /// <inheritdoc />
    public override void _Process(double delta)
    {
        _seconds += delta;
        Advance(ClockTick);
        if (_outcomeLeft > 0)
        {
            _outcomeLeft -= delta;
        }
        if (_readout is null)
        {
            return;
        }
        PlaceReadout();
        var wait = QueueWait.Describe(GateState());
        if (wait.Length > 0)
        {
            _readout.Text = wait;
            _readout.Visible = true;
            return;
        }
        var open = _screen is { IsOpen: true };
        _readout.Visible = open;
        if (open && _screen!.State is { } state)
        {
            _readout.Text = Scarcity(state.KeyLevel).Readout();
        }
    }

    /// <summary>
    /// Puts the readout on <see cref="Keyrun.Content.ActionBarLayout.QueueReadout"/>: the party panel's text row in a
    /// live run (left aligned under the frames), else bottom centre on the queue screen.
    /// </summary>
    private void PlaceReadout()
    {
        var view = _screen!.Size;
        var seats = PartySeats();
        var rect = Keyrun.Content.ActionBarLayout.QueueReadout((int)view.X, (int)view.Y, seats);
        _readout!.SetAnchorsPreset(Control.LayoutPreset.TopLeft);
        _readout.Position = new Vector2(rect.X, rect.Y);
        _readout.Size = new Vector2(rect.Width, rect.Height);
        _readout.HorizontalAlignment = seats > 0 ? HorizontalAlignment.Left : HorizontalAlignment.Center;
    }

    // Player seats in the live run (0 with no run: no ability bar, no party frames).
    private int PartySeats() =>
        _driver?.Run is { } run ? run.State.Actors.Values.Count(a => a.Kind == ActorKind.Player) : 0;

    /// <summary>
    /// The gate state the wait readout is a pure function of. An open check reads while it waits; a finished check's
    /// outcome reads only for <see cref="OutcomeHoldSeconds"/> after it ends, so a stale check never sticks on screen.
    /// </summary>
    private QueueGateState GateState()
    {
        var check = Check;
        var partySeats = PartySeats();
        var openSeats = _driver?.OpenSeats.Count ?? 0;
        var humanReady = check is not null && check.ReadySeats.Contains(Gate.HumanSeat);
        // The fill time is not modelled (the backfill joins at the next pull start), so the open-seat line reads "at the next pull".
        return QueueWait.StateFor(
            check?.Phase,
            Gate.Checking,
            check?.Seats.Count ?? 0,
            humanReady,
            Gate.RemainingTicks(ClockTick),
            _outcomeLeft > 0,
            partySeats,
            openSeats,
            0);
    }

    /// <inheritdoc />
    public override void _UnhandledInput(InputEvent @event)
    {
        if (!Gate.Checking || @event is not InputEventKey { Pressed: true, Echo: false } key)
        {
            return;
        }
        switch (key.Keycode)
        {
            case Key.Enter or Key.KpEnter:
                KeyAnswered?.Invoke();
                GetViewport().SetInputAsHandled();
                Answer(Gate.HumanSeat, ready: true, ClockTick);
                break;
            case Key.Escape:
                KeyAnswered?.Invoke();
                GetViewport().SetInputAsHandled();
                Answer(Gate.HumanSeat, ready: false, ClockTick);
                break;
        }
    }

    /// <summary>Adds one request at the back of the queue, with the human seat's role.</summary>
    /// <param name="request">The request.</param>
    public void Add(QueueRequest request) => Add(request, HumanRole());

    /// <summary>Adds one request at the back of the queue.</summary>
    /// <param name="request">The request.</param>
    /// <param name="role">The requesting seat's role (read by the scarcity readout only; matching stays key-only).</param>
    public void Add(QueueRequest request, ActorRole role)
    {
        ArgumentNullException.ThrowIfNull(request);
        _requests.Add(request);
        _roles.Add(role);
        Proof(FormattableString.Invariant(
            $"QUEUE add dungeon={request.DungeonId} key={request.KeyLevel} clause={request.ClauseId} role={role.ToJs()} size={_requests.Count}"));
        ProofRoles(request.KeyLevel);
    }

    /// <summary>The role-scarcity readout of the requests the key band admits at <paramref name="keyLevel"/>.</summary>
    /// <param name="keyLevel">The key.</param>
    /// <returns>The pure readout.</returns>
    public RoleScarcity Scarcity(int keyLevel) =>
        RoleScarcity.Read(_requests.Select((r, i) => (r, i)).Where(p => Backfill.Admits(keyLevel, p.r)).Select(p => _roles[p.i]));

    /// <summary>Removes and returns the first request the key band admits to a run at <paramref name="runKeyLevel"/>.</summary>
    /// <param name="runKeyLevel">The run's key.</param>
    /// <returns>The joiner, or null when nobody waiting matches the key.</returns>
    public QueueRequest? TakeFor(int runKeyLevel)
    {
        var index = Backfill.PickJoiner(runKeyLevel, _requests);
        if (index < 0)
        {
            Proof(FormattableString.Invariant($"QUEUE take key={runKeyLevel} none size={_requests.Count}"));
            return null;
        }
        var request = _requests[index];
        _requests.RemoveAt(index);
        _roles.RemoveAt(index);
        Proof(FormattableString.Invariant($"QUEUE take key={runKeyLevel} index={index} size={_requests.Count}"));
        return request;
    }

    /// <summary>Removes the requests a <see cref="Backfill.Step"/> placed into seats.</summary>
    /// <param name="consumedIndices">The step's <see cref="BackfillStep.ConsumedQueueIndices"/> (indices into <see cref="Requests"/>).</param>
    public void Consume(IReadOnlyList<int> consumedIndices)
    {
        ArgumentNullException.ThrowIfNull(consumedIndices);
        foreach (var index in consumedIndices.Distinct().OrderByDescending(i => i))
        {
            if (index >= 0 && index < _requests.Count)
            {
                _requests.RemoveAt(index);
                _roles.RemoveAt(index);
            }
        }
        if (consumedIndices.Count > 0)
        {
            Proof(FormattableString.Invariant($"QUEUE consume n={consumedIndices.Count} size={_requests.Count}"));
        }
    }

    /// <summary>
    /// The queue pops a group for <paramref name="request"/> (SimDriver, on every <see cref="Screen.Queued"/>): opens the
    /// gate's ready check for the human seat plus <paramref name="botSeats"/> at the node clock. The bots answer ready at
    /// once; with <paramref name="humanReadyAtPop"/> (a scripted launch, <see cref="QueuedRunGate.HumanReadyAtPop"/>) so
    /// does the human, and the run is entered before this returns.
    /// </summary>
    /// <param name="request">The queued request.</param>
    /// <param name="humanSeat">The human's seat.</param>
    /// <param name="botSeats">The bot seats.</param>
    /// <param name="humanReadyAtPop">Answer ready for the human at the pop.</param>
    /// <returns>The check after the pop's answers.</returns>
    public ReadyCheck Pop(QueueRequest request, int humanSeat, IReadOnlyList<int> botSeats, bool humanReadyAtPop) =>
        Gate.Queued(request, humanSeat, botSeats, ClockTick, humanReadyAtPop);

    /// <summary>One seat answers the open ready check.</summary>
    /// <param name="seat">The seat.</param>
    /// <param name="ready">True for ready, false for decline.</param>
    /// <param name="tick">The answer tick (the node clock in play).</param>
    /// <returns>The check after the answer, or null when none is open.</returns>
    public ReadyCheck? Answer(int seat, bool ready, int tick) => Gate.Answer(seat, ready, tick);

    /// <summary>Advances the open ready check to <paramref name="tick"/> (the timeout).</summary>
    /// <param name="tick">The tick.</param>
    public void Advance(int tick) => Gate.Advance(tick);

    // The human seat's role in the live run (the Warden tanks in HostRunSetup's roster), else tank.
    private ActorRole HumanRole() =>
        _driver?.Run?.State.Actors.TryGetValue(SimDriver.WardenId, out var human) == true && human.Role != ActorRole.None
            ? human.Role
            : ActorRole.Tank;

    private void ProofRoles(int keyLevel)
    {
        var line = FormattableString.Invariant($"QUEUE roles key={keyLevel} readout=\"{Scarcity(keyLevel).Readout()}\"");
        if (line != _lastRolesProof)
        {
            _lastRolesProof = line;
            Proof(line);
        }
    }

    private void Proof(string line)
    {
        if (_driver is not null)
        {
            HudNodes.Proof(_driver, line);
        }
    }

    // The ready check's lines go to the driver's run log in every launch (console and --log file), so an interactive
    // proof (--press-enter, --ready-hold, --replay-input) shows the pop, each answer and the end next to RUN start.
    private void GateLine(string line) => _driver?.HostLine(line);
}
