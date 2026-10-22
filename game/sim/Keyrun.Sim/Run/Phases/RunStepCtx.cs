using System;
using System.Collections.Generic;
using Keyrun.Sim.Core;
using Keyrun.Sim.Encounter;

namespace Keyrun.Sim.Runs;

/// <summary>One stage of <see cref="RunRoot.StepRun"/>: reads and advances the step's context in place.</summary>
/// <param name="c">The step context.</param>
internal delegate void RunPhase(RunStepCtx c);

/// <summary>
/// Everything one <see cref="RunRoot.StepRun"/> carries from phase to phase (one allocation per step). The
/// <see cref="Before"/> run is the step's input and is never replaced; <see cref="State"/> and <see cref="Encounter"/>
/// are the in-progress next state and encounter, and every phase reads the latest of them. <see cref="Events"/> is both
/// the tick's event sink and, for the later phases (echo spawns, boss death, deaths and wipe, seed loot), their input.
/// </summary>
internal sealed class RunStepCtx
{
    /// <summary>Starts a step of <paramref name="before"/>.</summary>
    /// <param name="before">The input run.</param>
    /// <param name="commands">This tick's caller commands.</param>
    /// <param name="opts">Host options, or null.</param>
    /// <param name="tables">The run content's cached tables.</param>
    public RunStepCtx(Run before, IReadOnlyList<Command> commands, StepRunOpts? opts, RunTables tables)
    {
        Before = before;
        Commands = commands;
        Opts = opts;
        Tables = tables;
        Events = new List<SimEvent>();
        Emit = Events.Add;
        State = before.State;
        Encounter = before.Encounter;
        ActiveScripts = before.ActiveScripts;
        RunEnd = before.RunEnd;
        Rewards = before.Rewards;
        Releases = before.Releases;
    }

    /// <summary>The input run.</summary>
    public Run Before { get; }

    /// <summary>This tick's caller commands (players and bots).</summary>
    public IReadOnlyList<Command> Commands { get; }

    /// <summary>Host options (abandon, open seats); null = none.</summary>
    public StepRunOpts? Opts { get; }

    /// <summary>The run content's tables (<see cref="RunTables.Of"/>).</summary>
    public RunTables Tables { get; }

    /// <summary>The tick's events, in emit order.</summary>
    public List<SimEvent> Events { get; }

    /// <summary>Appends to <see cref="Events"/>.</summary>
    public Action<SimEvent> Emit { get; }

    /// <summary>The encounter step's result on the pre-combat state (<see cref="EncounterPrePhase"/>).</summary>
    public StepEncounterResult Pre { get; set; } = null!;

    /// <summary>The next state so far.</summary>
    public SimState State { get; set; }

    /// <summary>The next encounter so far.</summary>
    public EncounterState Encounter { get; set; }

    /// <summary>The next run's engage-clocked scripts.</summary>
    public IReadOnlyDictionary<int, IReadOnlyList<ScriptStep>>? ActiveScripts { get; set; }

    /// <summary>The next run's pending releases (<see cref="Release.Step"/>).</summary>
    public IReadOnlyDictionary<int, int> Releases { get; set; }

    /// <summary>The next run's run-end state.</summary>
    public RunEndState RunEnd { get; set; }

    /// <summary>The next run's rewards.</summary>
    public RunRewards Rewards { get; set; }

    /// <summary>Set by a phase that ends the step early: the step's whole result run; no later phase runs.</summary>
    public Run? Halted { get; set; }

    /// <summary>The advanced run the phases built.</summary>
    /// <returns>The step result.</returns>
    public RunStepResult Result()
    {
        if (Halted is { } halted)
        {
            return new RunStepResult(halted, Events);
        }
        var run = Before;
        var next = new Run(State, Encounter, run.Dungeon, run.Content, RunEnd)
        {
            Rewards = Rewards,
            SeedRule = run.SeedRule,
            ActiveScripts = ActiveScripts,
            Spawns = run.Spawns,
            Releases = Releases,
        };
        return new RunStepResult(next, Events);
    }
}
