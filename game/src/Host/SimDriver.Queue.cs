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

    /// <summary>Writes one host line to the run log (console and <c>--log</c> file): the Queue node's ready-check lines.</summary>
    /// <param name="line">The line.</param>
    public void HostLine(string line) => _log?.Line(line);

    // f-e24b4fa4 (BIBLE s10 "ready check on pop"): Screen.Queued pops the Queue node's ready check for the human seat plus
    // the bot seats instead of starting the run; the gate's Entered starts it (StartQueuedRun), Requeued reopens the
    // screen (OnPopRequeued). A launch that would not start a queued run (plain --autorun) pops nothing, as before. No
    // Queue node in the scene: the request starts the run at once.
    private void OnQueued(QueueRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (_done || Registry is null || (Options.Autorun && !Options.Requeue))
        {
            return;
        }
        if (_queue is not { } queue || Setup is not { } setup)
        {
            StartQueuedRun(request);
            return;
        }
        _readyHoldWait = 0;
        var humanReady = QueuedRunGate.HumanReadyAtPop(
            Options.Autorun, Options.PressEnter, _replayer is not null, _replayLog?.ReadyKeys == true, Options.ReadyHold);
        queue.Pop(request, setup.HumanActorId, setup.BotActorIds, humanReady);
    }

    // A decline or the timeout: the request leaves the Queue node (it can never refill a seat), nobody is penalised, and
    // the between-runs screen comes back so the player can queue again (its Key band unlocks on Open).
    private void OnPopRequeued(QueueRequest request, ReadyCheck check)
    {
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
        if (_done || _screen is not { } screen)
        {
            return;
        }
        screen.OpenFirst();
        _log?.Line(FormattableString.Invariant(
            $"SCREEN requeue requeued={string.Join(',', check.Requeued)} open={(screen.IsOpen ? 1 : 0)} key={screen.State?.KeyLevel ?? 0}"));
    }

    // The human answered the ready check with a key: a recording says its ready answers are keys, so its replay leaves
    // the answer to the replayed keys instead of answering at the pop.
    private void OnReadyKey()
    {
        if (_recorder is not null)
        {
            _recorder.ReadyKeys = true;
        }
    }

    // --ready-hold F: a scripted launch's human answers ready once the dialog has been up F frames (no key is pushed).
    private void MaybeReadyHold()
    {
        if (Options.ReadyHold == 0 || _queue is not { Checking: true } queue ||
            (!Options.Autorun && Options.PressEnter == 0 && _replayer is null))
        {
            return;
        }
        if (++_readyHoldWait < Options.ReadyHold)
        {
            return;
        }
        _readyHoldWait = 0;
        queue.Answer(queue.Gate.HumanSeat, ready: true, queue.ClockTick);
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
        _screen.Queued += OnQueued;
        if (_queue is not null)
        {
            _queue.Gate.Entered += StartQueuedRun;
            _queue.Gate.Requeued += OnPopRequeued;
            _queue.KeyAnswered += OnReadyKey;
        }
        if (StartsOnScreen && Run is null)
        {
            // Lane 3D-b: the first run is the one the player queues on the screen (Queued -> StartQueuedRun).
            screen.OpenFirst();
            _log?.Line(FormattableString.Invariant(
                $"SCREEN first open={(screen.IsOpen ? 1 : 0)} key={screen.State?.KeyLevel ?? 0}"));
        }
    }
}
