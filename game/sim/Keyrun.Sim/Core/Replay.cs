using System.Collections.Generic;
using System.Linq;

namespace Keyrun.Sim.Core;

/// <summary>
/// Port of sim/core/replay.ts record/apply: replay a whole command stream deterministically from a seed.
/// The TS goldenHash (hashState of the final state) is not ported: hashState is not part of the oracle
/// (ORACLE.md section 2); the projection hash in <see cref="Projection"/> is the parity hash.
/// </summary>
public static class Replay
{
    /// <summary>A tick-sorted (stable) copy of the stream. Commands are immutable records, so the copy is independent.</summary>
    public static IReadOnlyList<Command> Record(IEnumerable<Command> commands) =>
        commands.OrderBy(c => c.Tick).ToList();

    /// <summary>Replays from <see cref="SimState.Create"/>(seed) through the last command tick (no steps for an empty stream).</summary>
    public static StepResult Apply(long seed, IEnumerable<Command> commands, StepRules rules)
    {
        var ordered = Record(commands);
        var maxTick = ordered.Count == 0 ? -1 : ordered.Max(c => c.Tick);
        var state = SimState.Create(seed);
        var events = new List<SimEvent>();
        for (var t = 0; t <= maxTick; t++)
        {
            var tick = t;
            var result = Tick.Step(state, ordered.Where(c => c.Tick == tick).ToList(), rules);
            state = result.State;
            events.AddRange(result.Events);
        }
        return new StepResult(state, events);
    }
}
