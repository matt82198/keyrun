using System.Collections.Generic;
using Keyrun.Sim.Core;
using Keyrun.Sim.Runs;

namespace Keyrun.Host;

/// <summary>Supplies this tick's commands (keyboard or scripted).</summary>
public interface ICommandSource
{
    /// <summary>Appends the commands for <paramref name="tick"/> (the pre-step run tick) to <paramref name="into"/>.</summary>
    /// <param name="tick">The tick the commands are addressed to.</param>
    /// <param name="run">The run before the step.</param>
    /// <param name="into">The command list.</param>
    void Sample(int tick, Run run, List<Command> into);
}
