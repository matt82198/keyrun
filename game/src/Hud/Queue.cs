using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Keyrun.Content;
using Keyrun.Host;
using Keyrun.Sim.Core;
using Keyrun.Sim.Runs;

namespace Keyrun.Hud;

/// <summary>
/// The Godot-side queue list (BIBLE s10 Queue band; ENGINE-PLAN 3G backfill). It holds every <see cref="QueueRequest"/>
/// the screen's Queue button raises (<see cref="Screen.Queued"/>), oldest first, and hands the backfill seam one
/// joiner for an open seat: <see cref="TakeFor"/> picks by the pure key band (<see cref="Backfill.PickJoiner"/>, exact
/// key 2-20), and <see cref="Consume"/> removes the requests a <see cref="Backfill.Step"/> reports as joined.
/// <para>BIBLE s10 "while queued, a role-scarcity readout; ready check on pop": while the screen is open this node draws
/// the party's roles under the Queue box (<see cref="RoleReadout"/>: which roles a player fills, which the queue still
/// needs). A Queue press pops a ready check (<see cref="ReadyCheck"/>): bots accept at once, the player accepts with
/// Enter, Y or the Accept box (Esc, N or Decline says no) within <see cref="ReadyCheck.DefaultTimeoutMs"/>. All ready
/// raises <see cref="PartyReady"/>, which starts the run (SimDriver); a decline or the timeout drops the request and reopens
/// the screen with no penalty. The scripted players (<c>--autorun</c>, <c>--press-enter</c>, <c>--replay-input</c>)
/// accept on pop, so every scripted run starts exactly as before.</para>
/// The node holds no rules of its own. Under <c>--autorun</c> it prints "QUEUE add|take|consume|roles ..." and
/// "READY pop|accept|decline|done ..." proof lines.
/// </summary>
public partial class Queue : Node
{
    private const float Margin = 32;
    private static readonly Color Gold = new(1f, 0.85f, 0.5f);
    private static readonly Color Calm = new(0.7f, 0.85f, 1f);
    private static readonly Color Good = new(0.5f, 1f, 0.6f);
    private static readonly Color Short = new(1f, 0.62f, 0.4f);

    private readonly List<QueueRequest> _requests = new();
    private Screen? _screen;
    private SimDriver? _driver;
    private Control? _panel;
    private ReadyCheck? _check;
    private QueueRequest? _popped;
    private bool _screenWasOpen;
    private double _carryMs;

    /// <summary>Raised when every seat accepted the ready check, with the request that popped (the run to start).</summary>
    public event Action<QueueRequest>? PartyReady;

    /// <summary>The screen whose Queued event feeds this list (default: the parent Screen).</summary>
    [Export]
    public NodePath ScreenPath { get; set; } = new("..");

    /// <summary>The driver, for the party and autorun proof lines.</summary>
    [Export]
    public NodePath DriverPath { get; set; } = new("../../../SimDriver");

    /// <summary>The waiting requests, oldest first (the list <see cref="Backfill.Step"/> reads).</summary>
    public IReadOnlyList<QueueRequest> Requests => _requests;

    /// <summary>The open ready check, or null when none is up.</summary>
    public ReadyCheck? Check => _check;

    /// <summary>True while a ready check waits on an answer (the host ignores Enter meanwhile).</summary>
    public bool Checking => _check is { Outcome: ReadyOutcome.Waiting };

    /// <inheritdoc />
    public override void _Ready()
    {
        _driver = HudNodes.FindDriver(this, DriverPath);
        _screen = GetNodeOrNull<Screen>(ScreenPath);
        if (_screen is null)
        {
            return;
        }
        _screen.Queued += OnQueued;
        // The drawing surface: a full-rect Control appended to the Screen, so it draws over the screen's bands and
        // over the run when the screen is closed. The screen is still setting up its children here, hence deferred.
        _panel = new Control { Name = "QueuePanel", MouseFilter = Control.MouseFilterEnum.Ignore };
        _panel.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        _panel.Draw += DrawPanel;
        _panel.GuiInput += OnPanelInput;
        _screen.CallDeferred(Node.MethodName.AddChild, _panel);
    }

    /// <inheritdoc />
    public override void _ExitTree()
    {
        if (_screen is not null)
        {
            _screen.Queued -= OnQueued;
        }
        if (_panel is not null)
        {
            _panel.Draw -= DrawPanel;
            _panel.GuiInput -= OnPanelInput;
        }
    }

    /// <inheritdoc />
    public override void _Process(double delta)
    {
        var open = _screen?.IsOpen == true;
        if (open && !_screenWasOpen)
        {
            var rows = RoleReadout.For(PartySeats());
            var needs = RoleReadout.Scarce(rows);
            Proof("QUEUE roles " + string.Join(" ", rows.Select(r => RoleReadout.Name(r.Role) + "=" + Holder(r))) +
                  " needs=" + (needs.Count > 0 ? string.Join(",", needs.Select(RoleReadout.Name)) : "none"));
        }
        _screenWasOpen = open;
        if (_check is { } check && Checking)
        {
            _carryMs += delta * 1000.0;
            var ms = (int)Math.Min(_carryMs, int.MaxValue);
            _carryMs -= ms;
            _check = check.Advance(ms);
            Resolve();
        }
        _panel?.QueueRedraw();
    }

    /// <inheritdoc />
    public override void _UnhandledInput(InputEvent @event)
    {
        if (!Checking || @event is not InputEventKey { Pressed: true, Echo: false } key)
        {
            return;
        }
        switch (key.Keycode)
        {
            case Key.Enter or Key.KpEnter or Key.Y:
                Answer(accept: true, by: "player");
                break;
            case Key.Escape or Key.N:
                Answer(accept: false, by: "player");
                break;
            default:
                return;
        }
        GetViewport().SetInputAsHandled();
    }

    /// <summary>Adds one request at the back of the queue.</summary>
    /// <param name="request">The request.</param>
    public void Add(QueueRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        _requests.Add(request);
        Proof(FormattableString.Invariant(
            $"QUEUE add dungeon={request.DungeonId} key={request.KeyLevel} clause={request.ClauseId} size={_requests.Count}"));
    }

    /// <summary>
    /// The queue pops for <paramref name="request"/>: a ready check opens over the party's seats (bots accept at once).
    /// When every seat has accepted, <see cref="PartyReady"/> fires with the request.
    /// </summary>
    /// <param name="request">The request that popped (already in <see cref="Requests"/>).</param>
    public void Pop(QueueRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        _check = ReadyCheck.Pop(PartySeats());
        _popped = request;
        _carryMs = 0;
        var bots = _check.Seats.Count(s => s.Fill == SeatHolder.Bot);
        Proof(FormattableString.Invariant(
            $"READY pop seats={_check.Seats.Count} bots={bots} wait={_check.WaitingSeats.Count} timeout={_check.TimeoutMs}"));
        if (ScriptedPlayer)
        {
            while (Checking)
            {
                Answer(accept: true, by: "script");
            }
        }
        Resolve();
    }

    /// <summary>The waiting player accepts (true) or declines (false) the open ready check; ignored when none waits.</summary>
    /// <param name="accept">Accept or decline.</param>
    public void Answer(bool accept) => Answer(accept, "player");

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
            }
        }
        if (consumedIndices.Count > 0)
        {
            Proof(FormattableString.Invariant($"QUEUE consume n={consumedIndices.Count} size={_requests.Count}"));
        }
    }

    // The scripted players never see the dialog: they accept on pop, so the scripted runs keep their Enter counts.
    private bool ScriptedPlayer => _driver?.Options is { } o && (o.Autorun || o.PressEnter > 0 || o.ReplayInput is not null);

    private void OnQueued(QueueRequest request)
    {
        Add(request);
        Pop(request);
    }

    private void Answer(bool accept, string by)
    {
        if (_check is not { } check || !Checking)
        {
            return;
        }
        var seat = check.WaitingSeats[0];
        _check = accept ? check.Accept(seat) : check.Decline(seat);
        Proof(FormattableString.Invariant(
            $"READY {(accept ? "accept" : "decline")} seat={seat} role={RoleReadout.Name(check.Seats[seat].Role)} by={by}"));
        if (by == "player")
        {
            Resolve();
        }
    }

    // Ends a finished check: all ready starts the run (PartyReady); a decline or the timeout drops the request and returns
    // the party to the screen's Queue with no penalty (research/08).
    private void Resolve()
    {
        if (_check is not { } check || _popped is not { } request || check.Outcome == ReadyOutcome.Waiting)
        {
            return;
        }
        var outcome = check.Outcome;
        _check = null;
        _popped = null;
        Proof("READY done outcome=" + outcome switch
        {
            ReadyOutcome.AllReady => "all-ready",
            ReadyOutcome.Declined => "declined",
            _ => "expired",
        });
        if (outcome == ReadyOutcome.AllReady)
        {
            PartyReady?.Invoke(request);
            return;
        }
        var index = _requests.FindLastIndex(r => ReferenceEquals(r, request));
        if (index >= 0)
        {
            _requests.RemoveAt(index);
        }
        _screen?.OpenFirst();
    }

    // The party's seats, one per roster role (HostRunSetup.Roster): the human's, the bots', or open (--solo).
    private IReadOnlyList<QueueSeat> PartySeats()
    {
        var setup = _driver?.Setup;
        return HostRunSetup.Roster.Select(slot =>
        {
            var member = setup?.Party.FirstOrDefault(p => p.ClassId == slot.ClassId);
            SeatHolder fill;
            if (setup is null)
            {
                fill = slot.ClassId == SimDriver.HumanClassId ? SeatHolder.Human : SeatHolder.Open;
            }
            else if (member is null)
            {
                fill = SeatHolder.Open;
            }
            else
            {
                fill = member.Id == setup.HumanActorId ? SeatHolder.Human : SeatHolder.Bot;
            }
            return new QueueSeat(ToQueueRole(slot.Role), fill);
        }).ToList();
    }

    private static QueueRole ToQueueRole(ActorRole role) => role switch
    {
        ActorRole.Tank => QueueRole.Tank,
        ActorRole.Healer => QueueRole.Healer,
        _ => QueueRole.Damage,
    };

    private static string Holder(RoleStatus r) => r.Humans > 0 ? "player" : r.Bots > 0 ? "bot" : "open";

    private void DrawPanel()
    {
        if (_panel is null || _screen is null)
        {
            return;
        }
        _panel.MouseFilter = Checking ? Control.MouseFilterEnum.Stop : Control.MouseFilterEnum.Ignore;
        if (_check is { } check && Checking)
        {
            DrawCheck(_panel, check);
        }
        else if (_screen.IsOpen)
        {
            DrawReadout(_panel);
        }
    }

    // The readout box under the Queue box and its caption. The column is Screen.LayoutFor's Queue band (same Margin,
    // 46/32 percent splits and 16 px gutters; Screen.cs is another lane's file, so the arithmetic is mirrored here).
    private void DrawReadout(Control c)
    {
        var size = c.Size;
        var width = size.X - (2 * Margin);
        var x = Margin + (width * 0.46f) + 16 + (width * 0.32f) + 16;
        var w = size.X - Margin - x;
        var queueBottom = Margin + 30 + 120;
        var top = queueBottom + 24 + (3 * 16) + 16;
        var rows = RoleReadout.For(PartySeats());
        var box = new Rect2(x, top, w, 34 + (rows.Count * 20) + 22);
        c.DrawRect(box, HudNodes.Back);
        c.DrawRect(box, HudNodes.Edge, filled: false, width: 1f);
        HudNodes.Text(c, box.Position + new Vector2(12, 22), "Roles", 16, Gold);
        var y = box.Position.Y + 44;
        foreach (var row in rows)
        {
            HudNodes.Text(c, new Vector2(x + 12, y), Fit(RoleReadout.Label(row), w - 24, 14), 14, row.Scarce ? Short : Good);
            y += 20;
        }
        HudNodes.Text(c, new Vector2(x + 12, y + 2), Fit(RoleReadout.Needs(rows), w - 24, 12), 12, Calm);
    }

    // The ready-check dialog, centred over whatever is behind it, with Accept and Decline boxes.
    private void DrawCheck(Control c, ReadyCheck check)
    {
        c.DrawRect(new Rect2(Vector2.Zero, c.Size), new Color(0f, 0f, 0f, 0.55f));
        var box = CheckBox(c.Size, check);
        c.DrawRect(box, new Color(0.08f, 0.09f, 0.12f, 0.98f));
        c.DrawRect(box, Gold, filled: false, width: 2f);
        HudNodes.Text(c, box.Position + new Vector2(18, 32), "Ready check", 22, Gold);
        var seconds = FormattableString.Invariant($"{check.SecondsLeft} s");
        HudNodes.Text(c, new Vector2(box.End.X - 18 - HudNodes.Width(seconds, 16), box.Position.Y + 30), seconds, 16, Colors.White);
        if (_popped is { } request && _screen is { } screen)
        {
            var (name, _) = screen.ClauseText(request.ClauseId);
            HudNodes.Text(c, box.Position + new Vector2(18, 56),
                Fit(FormattableString.Invariant($"Key {request.KeyLevel} with {name}"), box.Size.X - 36, 14), 14, Calm);
        }
        var y = box.Position.Y + 86;
        for (var i = 0; i < check.Seats.Count; i++)
        {
            var seat = check.Seats[i];
            var role = RoleReadout.Name(seat.Role);
            var who = seat.Fill == SeatHolder.Bot ? "bot" : "you";
            var ready = check.Answers[i] == ReadyAnswer.Accepted;
            HudNodes.Text(c, new Vector2(box.Position.X + 18, y), char.ToUpperInvariant(role[0]) + role[1..] + "  (" + who + ")", 14, Colors.White);
            HudNodes.Text(c, new Vector2(box.Position.X + 200, y), ready ? "ready" : "waiting", 14, ready ? Good : Short);
            y += 20;
        }
        var (accept, decline) = Buttons(box);
        c.DrawRect(accept, new Color(0.2f, 0.34f, 0.22f));
        c.DrawRect(decline, new Color(0.34f, 0.2f, 0.18f));
        HudNodes.Text(c, accept.Position + new Vector2(12, 22), "Accept (Enter)", 14, Colors.White);
        HudNodes.Text(c, decline.Position + new Vector2(12, 22), "Decline (Esc)", 14, Colors.White);
    }

    private static Rect2 CheckBox(Vector2 size, ReadyCheck check)
    {
        var w = Mathf.Min(380f, size.X - (2 * Margin));
        var h = 86 + (check.Seats.Count * 20) + 56f;
        return new Rect2((size.X - w) / 2, (size.Y - h) / 2, w, h);
    }

    private static (Rect2 Accept, Rect2 Decline) Buttons(Rect2 box)
    {
        var w = (box.Size.X - 54) / 2;
        var y = box.End.Y - 46;
        return (new Rect2(box.Position.X + 18, y, w, 32), new Rect2(box.Position.X + 36 + w, y, w, 32));
    }

    private void OnPanelInput(InputEvent @event)
    {
        if (_panel is null || _check is not { } check || !Checking ||
            @event is not InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left } click)
        {
            return;
        }
        var (accept, decline) = Buttons(CheckBox(_panel.Size, check));
        if (accept.HasPoint(click.Position))
        {
            Answer(accept: true, by: "player");
        }
        else if (decline.HasPoint(click.Position))
        {
            Answer(accept: false, by: "player");
        }
        _panel.AcceptEvent();
    }

    private static string Fit(string text, float width, int size)
    {
        if (HudNodes.Width(text, size) <= width)
        {
            return text;
        }
        var s = text;
        while (s.Length > 1 && HudNodes.Width(s + "...", size) > width)
        {
            s = s[..^1];
        }
        return s + "...";
    }

    private void Proof(string line)
    {
        if (_driver is not null)
        {
            HudNodes.Proof(_driver, line);
        }
    }
}
