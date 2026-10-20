using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
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
/// never a number: s0 test 3). <see cref="Pop"/> opens a pure <see cref="ReadyCheck"/>: bot seats answer ready at once,
/// the human answers Enter (ready) or Esc (decline) inside the 10-second window, and the end raises
/// <see cref="ReadyCheckEnded"/> (Enter, or the seats requeued with no penalty).</para>
/// The node holds no rules of its own. Under <c>--autorun</c> it prints "QUEUE add|take|consume|roles|pop|ready ..."
/// proof lines.
/// </summary>
public partial class Queue : Node
{
    /// <summary>The scarcity line's node name (a Label added under the Screen).</summary>
    public const string ReadoutNodeName = "QueueRoles";

    private readonly List<QueueRequest> _requests = new();
    private readonly List<ActorRole> _roles = new();
    private Screen? _screen;
    private SimDriver? _driver;
    private Label? _readout;
    private double _seconds;
    private string? _lastRolesProof;

    /// <summary>The screen whose Queued event feeds this list (default: the parent Screen).</summary>
    [Export]
    public NodePath ScreenPath { get; set; } = new("..");

    /// <summary>The driver, for autorun proof lines.</summary>
    [Export]
    public NodePath DriverPath { get; set; } = new("../../../SimDriver");

    /// <summary>Raised once when a ready check resolves (Enter, or Requeue with the seats put back).</summary>
    public event Action<ReadyCheck>? ReadyCheckEnded;

    /// <summary>The waiting requests, oldest first (the list <see cref="Backfill.Step"/> reads).</summary>
    public IReadOnlyList<QueueRequest> Requests => _requests;

    /// <summary>Each waiting request's seat role, parallel to <see cref="Requests"/>.</summary>
    public IReadOnlyList<ActorRole> Roles => _roles;

    /// <summary>The open or last ready check, or null before the first pop.</summary>
    public ReadyCheck? Check { get; private set; }

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
            _readout = new Label
            {
                Name = ReadoutNodeName,
                AnchorLeft = 0f,
                AnchorRight = 1f,
                AnchorTop = 1f,
                AnchorBottom = 1f,
                OffsetTop = -64f,
                OffsetBottom = -36f,
                HorizontalAlignment = HorizontalAlignment.Center,
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
        if (_readout is null)
        {
            return;
        }
        if (Check is { Phase: ReadyPhase.Pending } check)
        {
            var left = (check.RemainingTicks(ClockTick) + Tick.TickHz - 1) / Tick.TickHz;
            var answered = check.ReadySeats.Contains(SimDriver.WardenId);
            _readout.Text = answered
                ? FormattableString.Invariant($"Group found. Waiting on the others ({left}s)")
                : FormattableString.Invariant($"Group found. Enter: ready   Esc: decline ({left}s)");
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

    /// <inheritdoc />
    public override void _UnhandledInput(InputEvent @event)
    {
        if (Check is not { Phase: ReadyPhase.Pending } || @event is not InputEventKey { Pressed: true, Echo: false } key)
        {
            return;
        }
        switch (key.Keycode)
        {
            case Key.Enter or Key.KpEnter:
                Answer(SimDriver.WardenId, ready: true, ClockTick);
                GetViewport().SetInputAsHandled();
                break;
            case Key.Escape:
                Answer(SimDriver.WardenId, ready: false, ClockTick);
                GetViewport().SetInputAsHandled();
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
    /// The queue pops a group: opens a ready check for <paramref name="seats"/> at the node clock. Every seat but the
    /// human's (<see cref="SimDriver.WardenId"/>) is a bot and answers ready at once.
    /// </summary>
    /// <param name="seats">The popped seats.</param>
    /// <returns>The check after the bots' answers.</returns>
    public ReadyCheck Pop(IReadOnlyList<int> seats)
    {
        ArgumentNullException.ThrowIfNull(seats);
        var tick = ClockTick;
        var check = ReadyCheck.Pop(seats, tick);
        Proof(FormattableString.Invariant(
            $"QUEUE pop seats={string.Join(',', check.Seats)} tick={tick} window={ReadyCheck.TimeoutTicks}"));
        foreach (var seat in check.Seats.Where(s => s != SimDriver.WardenId))
        {
            check = check.Accept(seat, tick);
        }
        Check = null; // a new check: its end is reported even when the last one ended the same way
        return Set(check);
    }

    /// <summary>One seat answers the open ready check.</summary>
    /// <param name="seat">The seat.</param>
    /// <param name="ready">True for ready, false for decline.</param>
    /// <param name="tick">The answer tick (the node clock in play).</param>
    /// <returns>The check after the answer, or null when none is open.</returns>
    public ReadyCheck? Answer(int seat, bool ready, int tick)
    {
        if (Check is not { } check)
        {
            return null;
        }
        Proof(FormattableString.Invariant($"QUEUE ready seat={seat} answer={(ready ? "ready" : "decline")} tick={tick}"));
        return Set(ready ? check.Accept(seat, tick) : check.Decline(seat, tick));
    }

    /// <summary>Advances the open ready check to <paramref name="tick"/> (the timeout).</summary>
    /// <param name="tick">The tick.</param>
    public void Advance(int tick)
    {
        if (Check is { Phase: ReadyPhase.Pending } check)
        {
            Set(check.At(tick));
        }
    }

    private ReadyCheck Set(ReadyCheck next)
    {
        var was = Check;
        Check = next;
        if (next.Phase != ReadyPhase.Pending && (was is null || was.Phase == ReadyPhase.Pending))
        {
            Proof(FormattableString.Invariant(
                $"QUEUE ready end phase={next.Phase.ToString().ToLowerInvariant()} requeued={string.Join(',', next.Requeued)}"));
            ReadyCheckEnded?.Invoke(next);
        }
        return next;
    }

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
}
