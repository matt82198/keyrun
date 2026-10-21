using System.Collections.Generic;
using Keyrun.Sim.Core;
using Keyrun.Sim.Runs;

namespace Keyrun.Host;

/// <summary>
/// The host's command-source seam (game/src/Host/SimDriver.cs ICommandSource, same signature), declared here so the
/// host's own Godot-free BotCommandSource.cs can be compiled into this test assembly by link and drive the reference
/// run in <c>NoPolicyParityTests</c>. SimDriver.cs itself needs Godot and is not linked.
/// </summary>
public interface ICommandSource
{
    /// <summary>Appends the commands for <paramref name="tick"/> to <paramref name="into"/>.</summary>
    /// <param name="tick">The tick the commands are addressed to.</param>
    /// <param name="run">The run before the step.</param>
    /// <param name="into">The command list.</param>
    void Sample(int tick, Run run, List<Command> into);
}
