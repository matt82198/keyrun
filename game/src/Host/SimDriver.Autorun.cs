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

public partial class SimDriver
{
    // --press-enter N (lane 3D-c's interactive proof): the scripted player waits EnterWaitFrames frames on each Enter
    // prompt (the screen up, or the run ended), then pushes one Enter through the viewport, so the screen's Queue and
    // this node's run-end Enter see the same event a keyboard sends. Never under --autorun.
    private void MaybeScriptedEnter()
    {
        if (_done || Options.Autorun || _enterPresses >= Options.PressEnter)
        {
            return;
        }
        // f-e24b4fa4: an open ready check is not an Enter prompt (the scripted human answers it, never a counted press).
        if ((_screen?.IsOpen != true && Run?.RunEnd.Result is null) || _queue?.Checking == true)
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
    // request at key K joins the Queue node (the screen's Queue button is not pressed mid-run). R-415-D: A@pullN+O
    // empties the seat O ticks after pull N's pull_start (SeatAbandonPlan.ResolveTick), and its line names the anchor;
    // A@pullN.hpP+O (f-0fd3e6fb) counts from the first step that ends with pull N's standing enemies at or below P
    // percent of their total health, and logs "hp=P at=S" for "start=S".
    private void MaybeScriptedAbandon(Run run)
    {
        if (Options.AbandonSeat is not { } plan || run.State.Tick != plan.ResolveTick(_abandonAnchor) || run.RunEnd.Result is not null)
        {
            return;
        }
        AbandonSeat(plan.ActorId);
        _log?.Line(plan.AnchorPull is { } pull
            ? FormattableString.Invariant($"SEAT abandon actor={plan.ActorId} tick={run.State.Tick} pull={pull} {plan.AnchorFields(_abandonAnchor)} offset={plan.Tick}")
            : FormattableString.Invariant($"SEAT abandon actor={plan.ActorId} tick={run.State.Tick}"));
        if (Options.JoinerKey is { } key && _queue is not null)
        {
            var clause = run.Encounter.Rules.ActiveClauseId
                ?? Registry?.Clauses.Keys.OrderBy(k => k, StringComparer.Ordinal).FirstOrDefault()
                ?? "none";
            _queue.Add(new QueueRequest(DungeonId, key, clause));
        }
    }
}
