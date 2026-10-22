using System;
using System.Collections.Generic;
using System.Linq;
using Keyrun.Sim.Core;

namespace Keyrun.Sim.Runs;

/// <summary>
/// One party seat its player left mid-run (BIBLE s10 backfill). The seat's actor stays in the run, alive and idle:
/// abandoning never kills, removes or resets it, so the run's Slack is not spent by the leave.
/// </summary>
/// <param name="ActorId">The seat's player actor.</param>
/// <param name="Tick">The run tick of the abandon (the tick the leaver's commands stop).</param>
/// <param name="PullIndex">The active pull at the abandon; the seat is offered at the next pull start after it.</param>
/// <param name="SlackTicks">The run's Slack bank at the abandon.</param>
public sealed record SeatAbandon(int ActorId, int Tick, int PullIndex, int SlackTicks);

/// <summary>An abandoned seat refilled from the queue at a pull start.</summary>
/// <param name="ActorId">The seat's player actor, now driven by the joiner.</param>
/// <param name="Joiner">The queue request that took the seat.</param>
/// <param name="AbandonTick">The tick the seat emptied.</param>
/// <param name="FillTick">The pull start tick: the first tick whose commands for the seat are the joiner's.</param>
/// <param name="PullIndex">The pull that started.</param>
/// <param name="SlackTicks">The run's Slack the joiner arrives with (the run's own bank, never reset).</param>
public sealed record SeatFill(int ActorId, QueueRequest Joiner, int AbandonTick, int FillTick, int PullIndex, int SlackTicks);

/// <summary>The run's backfill book: seats waiting for a joiner (in abandon order) and seats already refilled.</summary>
/// <param name="Open">Abandoned seats not yet filled, oldest first.</param>
/// <param name="Filled">Refills, in fill order.</param>
public sealed record BackfillState(IReadOnlyList<SeatAbandon> Open, IReadOnlyList<SeatFill> Filled)
{
    /// <summary>No seat ever left.</summary>
    public static BackfillState Empty { get; } = new(Array.Empty<SeatAbandon>(), Array.Empty<SeatFill>());
}

/// <summary>One <see cref="Backfill.Step"/>: the run step, the commands it actually ran, and any refills it made.</summary>
/// <param name="Step">The <see cref="RunRoot.StepRun"/> result.</param>
/// <param name="Backfill">The book after this step.</param>
/// <param name="Applied">The commands passed to the run (an open seat's commands removed).</param>
/// <param name="Fills">Seats filled at this step's pull start, in abandon order.</param>
/// <param name="ConsumedQueueIndices">Indices into the caller's queue that joined, ascending; the caller removes them.</param>
public sealed record BackfillStep(
    RunStepResult Step,
    BackfillState Backfill,
    IReadOnlyList<Command> Applied,
    IReadOnlyList<SeatFill> Fills,
    IReadOnlyList<int> ConsumedQueueIndices);

/// <summary>
/// ENGINE-PLAN 3G backfill (BIBLE s10: "a slot that empties mid-run is offered to the queue at the same key; a joiner
/// arrives at the next pull start with the run's Slack"; research/09, Fellowship's named defect). Pure and
/// deterministic: the refill depends only on the run, the abandon and the queue order, never on a clock.
/// <list type="bullet">
/// <item><see cref="Abandon"/> records the seat; the run is not touched (no death, so no Slack spend).</item>
/// <item><see cref="Step"/> wraps <see cref="RunRoot.StepRun"/>: an open seat's commands are dropped, and at the
/// first <c>pull_start</c> after the abandon the seat goes to the first admitted queue request, whose commands run
/// from the pull start tick on. A <c>pack_engage</c> is not a pull start (R-BACKFILL-1, BIBLE s10 "a joiner arrives at the
/// next pull start"): a seat stays open through a pack's engage.</item>
/// <item><see cref="Admits"/> is the key band (BIBLE s2 "The queue matches on key level only"): exact key equality
/// inside <see cref="ScreenState.MinKey"/>..<see cref="ScreenState.MaxKey"/>.</item>
/// </list>
/// </summary>
public static class Backfill
{
    /// <summary>
    /// The key band: <paramref name="request"/> may join a run at <paramref name="runKeyLevel"/> only when both keys
    /// are the same key inside the ScreenState band 2-20 ("at the same key"). The dungeon and Clause are not matched.
    /// </summary>
    /// <param name="runKeyLevel">The run's key (<c>Run.Encounter.KeyLevel</c>).</param>
    /// <param name="request">The queued request.</param>
    /// <returns>True when the joiner may take a seat.</returns>
    public static bool Admits(int runKeyLevel, QueueRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        return runKeyLevel >= ScreenState.MinKey && runKeyLevel <= ScreenState.MaxKey && request.KeyLevel == runKeyLevel;
    }

    /// <summary>The first admitted request in queue order (first queued, first offered), or -1.</summary>
    /// <param name="runKeyLevel">The run's key.</param>
    /// <param name="queue">The queue, oldest first.</param>
    /// <returns>The index, or -1 when nobody in the queue is admitted.</returns>
    public static int PickJoiner(int runKeyLevel, IReadOnlyList<QueueRequest> queue)
    {
        ArgumentNullException.ThrowIfNull(queue);
        return PickJoiner(runKeyLevel, queue, Array.Empty<int>());
    }

    /// <summary>Records that the player in <paramref name="actorId"/>'s seat left at <paramref name="run"/>'s tick.</summary>
    /// <param name="state">The book.</param>
    /// <param name="run">The run at the abandon (read only).</param>
    /// <param name="actorId">A player actor of the run.</param>
    /// <returns>The book with the seat open.</returns>
    /// <exception cref="ArgumentException">Not a player actor of the run, or its seat is already open.</exception>
    public static BackfillState Abandon(BackfillState state, Run run, int actorId)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(run);
        if (!run.State.Actors.TryGetValue(actorId, out var actor) || actor.Kind != ActorKind.Player)
        {
            throw new ArgumentException($"backfill: actor {actorId} is not a player seat of this run", nameof(actorId));
        }
        if (state.Open.Any(s => s.ActorId == actorId))
        {
            throw new ArgumentException($"backfill: seat {actorId} is already open", nameof(actorId));
        }
        var abandon = new SeatAbandon(actorId, run.State.Tick, run.Encounter.PullIndex, run.Encounter.SlackTicks);
        return state with { Open = state.Open.Append(abandon).ToList() };
    }

    /// <summary>The commands with every open seat's removed (an abandoned actor stands idle until refilled).</summary>
    /// <param name="state">The book.</param>
    /// <param name="commands">This tick's commands.</param>
    /// <returns>The kept commands; <paramref name="commands"/> itself when no seat is open.</returns>
    public static IReadOnlyList<Command> SeatCommands(BackfillState state, IReadOnlyList<Command> commands)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(commands);
        if (state.Open.Count == 0)
        {
            return commands;
        }
        return commands.Where(c => !state.Open.Any(s => s.ActorId == c.ActorId)).ToList();
    }

    /// <summary>
    /// <see cref="SeatCommands"/>, plus a stopcast for every open seat whose actor still has a cast in flight. An empty
    /// seat never acts (HudSceneTest run F check 23), and a cast begun before the leave would otherwise complete after it:
    /// keyrun #540 CI, "chanter.cadence" (castTicks 20) completed at 1593 one tick after seat 2 left at 1592. WoW drops a
    /// disconnected player's cast the same way.
    /// </summary>
    /// <param name="state">The book.</param>
    /// <param name="run">The run about to step (read only).</param>
    /// <param name="commands">This tick's commands.</param>
    /// <returns>The kept commands with the open seats' stopcasts appended.</returns>
    private static IReadOnlyList<Command> OpenSeatCommands(BackfillState state, Run run, IReadOnlyList<Command> commands)
    {
        var kept = SeatCommands(state, commands);
        var stops = state.Open
            .Where(s => run.State.Actors.TryGetValue(s.ActorId, out var a) && a.CastId is not null)
            .Select(s => (Command)new StopCastCommand(run.State.Tick, s.ActorId))
            .ToList();
        return stops.Count == 0 ? kept : kept.Concat(stops).ToList();
    }

    /// <summary>
    /// One run tick with backfill: steps the run on the seat-filtered commands, then, when the step started a pull
    /// and the run has not ended, gives each open seat abandoned before that pull the next admitted queue request.
    /// </summary>
    /// <param name="state">The book.</param>
    /// <param name="run">The run.</param>
    /// <param name="commands">This tick's commands, addressed to <c>run.State.Tick</c> (a joiner's use the seat's actor id).</param>
    /// <param name="queue">The queue, oldest first (read only; consumed indices are returned).</param>
    /// <param name="opts">Host options; null = none.</param>
    /// <returns>The step.</returns>
    public static BackfillStep Step(BackfillState state, Run run, IReadOnlyList<Command> commands, IReadOnlyList<QueueRequest> queue, StepRunOpts? opts = null)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(run);
        ArgumentNullException.ThrowIfNull(commands);
        ArgumentNullException.ThrowIfNull(queue);

        var applied = OpenSeatCommands(state, run, commands);
        // STATE.md D27: an open seat's idle actor never holds a layout pull back.
        var stepOpts = state.Open.Count == 0
            ? opts
            : (opts ?? new StepRunOpts()) with { OpenSeats = state.Open.Select(s => s.ActorId).ToList() };
        var stepped = RunRoot.StepRun(run, applied, stepOpts);
        var next = stepped.Run;
        // A pull starts on its pull_start event. On the flat arena that is the step the index advances; on a layout run
        // (STATE.md D18) the index advances on the clear and the next pull starts later, on room entry. BIBLE s10: "a joiner
        // arrives at the next pull start". R-BACKFILL-1 (ruling, PR #528): only a pull_start is one; a pack_engage (a patrol
        // walking into the party between pulls, or a path pack) never refills a seat, so the seat stays open through it.
        var started = stepped.Events.FirstOrDefault(e => e.Type == "pull_start");
        if (state.Open.Count == 0 || started is null || next.RunEnd.Result is not null)
        {
            return new BackfillStep(stepped, state, applied, Array.Empty<SeatFill>(), Array.Empty<int>());
        }

        var consumed = new List<int>();
        var fills = new List<SeatFill>();
        var open = new List<SeatAbandon>();
        foreach (var seat in state.Open)
        {
            var pick = seat.Tick < started.Tick ? PickJoiner(next.Encounter.KeyLevel, queue, consumed) : -1;
            if (pick < 0)
            {
                open.Add(seat);
                continue;
            }
            consumed.Add(pick);
            fills.Add(new SeatFill(seat.ActorId, queue[pick], seat.Tick, next.State.Tick, next.Encounter.PullIndex, next.Encounter.SlackTicks));
        }
        if (fills.Count == 0)
        {
            return new BackfillStep(stepped, state, applied, Array.Empty<SeatFill>(), Array.Empty<int>());
        }
        consumed.Sort();
        var book = new BackfillState(open, state.Filled.Concat(fills).ToList());
        var arrived = stepped with { Run = next with { State = Arrive(next.State, state.Open, fills) } };
        return new BackfillStep(arrived, book, applied, fills, consumed);
    }

    /// <summary>
    /// BIBLE s9.1 and s10: "a joiner arrives at the next pull start ... inside the party's taken branch". Each filled
    /// seat's actor is placed on the living party member who stayed (never an open or just-filled seat) nearest to where
    /// the leaver stood (squared XZ distance, lowest id on a tie): the rear of the party on the route it took, so the
    /// joiner starts the pull with the party instead of where the leaver idled. With nobody left standing it stays put.
    /// </summary>
    private static SimState Arrive(SimState state, IReadOnlyList<SeatAbandon> wasOpen, IReadOnlyList<SeatFill> fills)
    {
        var moved = new List<ActorState>();
        foreach (var fill in fills)
        {
            var seat = state.Actors[fill.ActorId];
            var anchor = state.Actors.Values
                .Where(a => a.Kind == ActorKind.Player && a.Alive && a.Id != seat.Id && !wasOpen.Any(s => s.ActorId == a.Id))
                .OrderBy(a => DistSq(a.Pos, seat.Pos))
                .ThenBy(a => a.Id)
                .FirstOrDefault();
            if (anchor is null)
            {
                continue;
            }
            var placed = seat.Clone();
            placed.Pos = anchor.Pos;
            moved.Add(placed);
        }
        return RunWiring.MergeActors(state, moved);
    }

    private static long DistSq(Vec3 a, Vec3 b)
    {
        var dx = a.X - b.X;
        var dz = a.Z - b.Z;
        return (dx * dx) + (dz * dz);
    }

    private static int PickJoiner(int runKeyLevel, IReadOnlyList<QueueRequest> queue, IReadOnlyList<int> taken)
    {
        for (var i = 0; i < queue.Count; i++)
        {
            if (!taken.Contains(i) && Admits(runKeyLevel, queue[i]))
            {
                return i;
            }
        }
        return -1;
    }
}
