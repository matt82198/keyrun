using System.Collections.Generic;
using System.Linq;

namespace Keyrun.Sim.Runs;

/// <summary>
/// The ordered phases of one <see cref="RunRoot.StepRun"/>, one file each under <c>Run/Phases/</c>. The array below is
/// the order; it is explicit (no reflection, no discovery) so the sim stays pure and a reader sees the whole step in
/// one place. Adding a rule is a new phase file plus one line here, at the point its ordering comment calls for.
/// Each phase reads and advances one <see cref="RunStepCtx"/>; a phase that sets <see cref="RunStepCtx.Halted"/>
/// (RUN-END-FREEZE) ends the step and no later phase runs.
/// </summary>
public static class RunPipeline
{
    private static readonly (string Name, RunPhase Step)[] Phases =
    {
        (nameof(RunEndFreezePhase), RunEndFreezePhase.Step),
        (nameof(EncounterPrePhase), EncounterPrePhase.Step),
        (nameof(CombatPhase), CombatPhase.Step),
        (nameof(BreakBarPatchesPhase), BreakBarPatchesPhase.Step),
        (nameof(RefuseImmuneStunsPhase), RefuseImmuneStunsPhase.Step),
        (nameof(ControlStopsPhase), ControlStopsPhase.Step),
        (nameof(StaggerStopsPhase), StaggerStopsPhase.Step),
        (nameof(TelegraphCancelPhase), TelegraphCancelPhase.Step),
        (nameof(PullsOrLayoutPhase), PullsOrLayoutPhase.Step),
        (nameof(EchoSpawnsPhase), EchoSpawnsPhase.Step),
        (nameof(BossDeathClearsPhase), BossDeathClearsPhase.Step),
        (nameof(ReleasePhase), ReleasePhase.Step),
        (nameof(TargetingPhase), TargetingPhase.Step),
        (nameof(DeathsAndWipePhase), DeathsAndWipePhase.Step),
        (nameof(RunEndPhase), RunEndPhase.Step),
        (nameof(SeedLootPhase), SeedLootPhase.Step),
    };

    /// <summary>The phase names, in run order.</summary>
    public static IReadOnlyList<string> PhaseNames { get; } = Phases.Select(p => p.Name).ToArray();

    /// <summary>Runs the phases over <paramref name="c"/> in order, stopping at the first that halts the step.</summary>
    /// <param name="c">The step context.</param>
    internal static void Run(RunStepCtx c)
    {
        foreach (var (_, step) in Phases)
        {
            step(c);
            if (c.Halted is not null)
            {
                return;
            }
        }
    }
}
