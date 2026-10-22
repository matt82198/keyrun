using System;
using Keyrun.Sim.Core;
using Keyrun.Sim.Runs;
using Xunit;

namespace Keyrun.Sim.Tests;

/// <summary>
/// <see cref="RunRoot.StepRun"/> is the loop over <see cref="RunPipeline"/>: its phases run in the documented order
/// (the order the inline method had, each stage's ordering comment kept in its phase file), and the per-content
/// <see cref="RunTables"/> are built once and reused by every later step instead of rebuilding the rules each tick.
/// </summary>
public sealed class RunPipelineTests
{
    [Fact(DisplayName = "The run pipeline runs its phases in the documented order")]
    public void Pipeline_order_is_the_documented_order()
    {
        var documented = new[]
        {
            "RunEndFreezePhase",
            "EncounterPrePhase",
            "CombatPhase",
            "BreakBarPatchesPhase",
            "RefuseImmuneStunsPhase",
            "ControlStopsPhase",
            "StaggerStopsPhase",
            "TelegraphCancelPhase",
            "PullsOrLayoutPhase",
            "EchoSpawnsPhase",
            "BossDeathClearsPhase",
            "ReleasePhase",
            "TargetingPhase",
            "DeathsAndWipePhase",
            "RunEndPhase",
            "SeedLootPhase",
        };

        Assert.Equal(documented, RunPipeline.PhaseNames);
    }

    [Theory(DisplayName = "Two steps of one run build its tables and rules once")]
    [InlineData(true)]
    [InlineData(false)]
    public void Run_tables_are_built_once_for_two_steps(bool armed)
    {
        var built = BossTestKit.BuildRun(armed: armed);
        // A content instance no other test holds, so the build count is this test's alone.
        var run = built with { Content = built.Content with { } };
        var tables = RunTables.Of(run.Content);
        Assert.Equal(0, tables.RulesBuilds);

        var first = RunRoot.StepRun(run, Array.Empty<Command>());
        var second = RunRoot.StepRun(first.Run, Array.Empty<Command>());

        Assert.Equal(run.State.Tick + 2, second.Run.State.Tick);
        Assert.Same(tables, RunTables.Of(second.Run.Content));
        Assert.Equal(1, tables.RulesBuilds);
    }
}
