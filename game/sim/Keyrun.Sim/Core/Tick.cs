using System.Collections.Generic;
using System.Linq;

namespace Keyrun.Sim.Core;

/// <summary>
/// The rules a step runs (sim/core/tick.ts StepRules): mutate <paramref name="state"/> (a fresh clone) in place
/// for this tick's validated, ordered commands, emitting events through <paramref name="emit"/>.
/// </summary>
/// <param name="state">The clone to mutate; its Tick is still the pre-step tick.</param>
/// <param name="commands">This tick's valid commands in deterministic order.</param>
/// <param name="emit">Event sink, in emission order.</param>
public delegate void StepRules(SimState state, IReadOnlyList<Command> commands, System.Action<SimEvent> emit);

/// <summary>The outcome of one <see cref="Tick.Step"/>.</summary>
/// <param name="State">The advanced state (a new object).</param>
/// <param name="Events">Events emitted during the step, in order.</param>
public sealed record StepResult(SimState State, IReadOnlyList<SimEvent> Events);

/// <summary>
/// Port of sim/core/tick.ts: <see cref="Step"/> advances a state by exactly one tick. Validates the commands,
/// keeps only those addressed to the current tick, orders them by (actorId, kind, insertion order), hands them
/// to the rules on a clone, then advances the tick and the run timer. The input state is never modified.
/// </summary>
public static class Tick
{
    /// <summary>Simulation rate: 20 ticks per second.</summary>
    public const int TickHz = 20;

    /// <summary>Milliseconds per tick, derived from <see cref="TickHz"/> as <c>Math.round(1000 / TICK_HZ)</c>.</summary>
    public static readonly int TickMs = (int)IntMath.RoundHalfUp(1000, TickHz);

    /// <summary>Deterministic ordering: actorId ascending, then KIND_ORDER, then original position (stable).</summary>
    public static IReadOnlyList<Command> Sort(IReadOnlyList<Command> commands) =>
        commands
            .Select((c, i) => (c, i))
            .OrderBy(x => x.c.ActorId)
            .ThenBy(x => Commands.KindOrder(x.c))
            .ThenBy(x => x.i)
            .Select(x => x.c)
            .ToList();

    /// <summary>Advances <paramref name="state"/> by one tick. Invalid or mis-addressed commands are dropped, never thrown on.</summary>
    public static StepResult Step(SimState state, IReadOnlyList<Command> commands, StepRules rules)
    {
        var events = new List<SimEvent>();
        var valid = commands
            .Select(Commands.Validate)
            .Where(c => c is not null && c.Tick == state.Tick)
            .Select(c => c!)
            .ToList();
        var sorted = Sort(valid);

        var next = state.Clone();
        rules(next, sorted, events.Add);
        next.Tick += 1;
        next.RunTimerTicks += 1;
        return new StepResult(next, events);
    }
}
