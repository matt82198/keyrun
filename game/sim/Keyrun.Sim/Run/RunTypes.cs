using System;
using System.Collections.Generic;
using Keyrun.Sim.Combat;
using Keyrun.Sim.Core;
using Keyrun.Sim.Encounter;

namespace Keyrun.Sim.Runs;

/// <summary>
/// One party member to spawn as a player actor at run creation (sim/run-types.ts:22 PartyMemberSpec).
/// </summary>
/// <param name="Id">Actor id.</param>
/// <param name="ClassId">Class id.</param>
/// <param name="Role">Role tag.</param>
/// <param name="Pos">Spawn position, mm.</param>
/// <param name="MaxHealth">Maximum (and starting) health.</param>
/// <param name="MaxResource">Maximum (and starting) resource.</param>
public sealed record PartyMemberSpec(int Id, string ClassId, ActorRole Role, Vec3 Pos, long MaxHealth, long MaxResource)
{
    /// <summary>Spawn facing, 1/1024 turn; null = the createActor default 0 (index.ts:85).</summary>
    public int? Yaw { get; init; }
}

/// <summary>
/// The data a run needs beyond the party and dungeon (sim/run-types.ts:39 RunContent): combat definitions for
/// <see cref="Rules"/>, the enemy scripts for the encounter lane, and the pacing knobs that seed the initial
/// <see cref="EncounterState"/>. Content never comes from a content-package reference: the host passes it in.
/// </summary>
public sealed record RunContent
{
    /// <summary>Abilities by id.</summary>
    public required IReadOnlyDictionary<string, AbilityDef> Abilities { get; init; }

    /// <summary>Effect definitions by id.</summary>
    public required IReadOnlyDictionary<string, EffectDef> Effects { get; init; }

    /// <summary>School lockout after an interrupt, ticks.</summary>
    public required int LockoutTicks { get; init; }

    /// <summary>Walk speed for an actor without its own, mm/tick.</summary>
    public required long BaseSpeedMmPerTick { get; init; }

    /// <summary>Actor push-out radius, mm.</summary>
    public required long ActorRadiusMm { get; init; }

    /// <summary>Arena bounds.</summary>
    public required Bounds Bounds { get; init; }

    /// <summary>Actor id to its enemy script (an actor with no entry never casts).</summary>
    public required IReadOnlyDictionary<int, IReadOnlyList<ScriptStep>> EnemyScripts { get; init; }

    /// <summary>Key level; null = 1 (index.ts:96 <c>content.keyLevel ?? 1</c>).</summary>
    public int? KeyLevel { get; init; }

    /// <summary>Par, ticks; null = 24000 (index.ts:112).</summary>
    public int? ParTicks { get; init; }

    /// <summary>Base encounter rules; null = <see cref="Clauses.DefaultEncounterRules"/> for the key (run-options.ts:93).</summary>
    public EncounterRules? EncounterRules { get; init; }

    /// <summary>Burst ComboTable, or null (window and payoff ops then find no combo and no-op).</summary>
    public ComboTable? Combos { get; init; }

    /// <summary>Field ComboTable for floor-patch Windows, or null.</summary>
    public ComboTable? FieldCombos { get; init; }

    /// <summary>
    /// Class id to its Heat/Overheat rule (BIBLE section 5 Cinder), or null for none. Null on every oracle run; a host
    /// builds it from content. A class with a rule opens the run at 0 Heat (<see cref="Actors.StartingResource"/>).
    /// </summary>
    public IReadOnlyDictionary<string, OverheatRule>? Overheat { get; init; }

    /// <summary>
    /// STUN-STOPS-CAST (BIBLE s9.3 "Stops as well as kicks"): the effect ids that stop a cast when they land on a caster
    /// mid an interruptible cast (a stun, a freeze, a silence), or null for none. Null on every oracle run (the frozen
    /// TS has no stops); a host sets it.
    /// </summary>
    public IReadOnlyList<string>? StopEffectIds { get; init; }

    /// <summary>
    /// The Pair's spread and stack mark rules (<see cref="PairMarks"/>), or null for none. Null on every oracle run; a
    /// host builds it from content (game/content/data/run.json pairMarks).
    /// </summary>
    public MarkRules? Marks { get; init; }

    /// <summary>Break-bar sources in their JS key order, or null for none (stepRun then drains none).</summary>
    public IReadOnlyList<BreakBarSource>? BreakBarSources { get; init; }

    /// <summary>Clause id to its definition, looked up by <see cref="RunOptions.ClauseId"/>; null = no Clauses offered.</summary>
    public IReadOnlyDictionary<string, ClauseDef>? Clauses { get; init; }

    /// <summary>
    /// The rooms-and-corridors layout (STATE.md D18), or null for the flat arena. Null on every oracle run: the layout
    /// rules (room-entry pull spawns, gated walkable boxes, <c>room_enter</c>) are new behaviour with no TS oracle and
    /// run only when a host sets it (<see cref="RunRoot"/>).
    /// </summary>
    public NavLayout? Layout { get; init; }

    /// <summary>
    /// PATH-PACKS (BIBLE section 9.1; layout runs only): the layout's trash packs, each standing idle from tick 0
    /// (<see cref="LayoutPacks.SpawnAllIdle"/>) and engaging whole by proximity or damage as one <see cref="Aggro.GroupsOf"/>
    /// group with pull index -1. Their members' scripts ride in <see cref="EnemyScripts"/>.
    /// Null or empty = no packs (every oracle run).
    /// </summary>
    public IReadOnlyList<PackGroup>? Packs { get; init; }

    /// <summary>
    /// This content with its movement fields taken from the host's validated <paramref name="movement"/>
    /// (the run-root side of <see cref="RulesConfig.WithMovement"/>: the host never hard-codes speed, radius or bounds).
    /// </summary>
    /// <param name="movement">Movement rules (game/content/data/run.json via ContentRegistry.Run).</param>
    /// <returns>A new content value.</returns>
    public RunContent WithMovement(MovementConfig movement)
    {
        ArgumentNullException.ThrowIfNull(movement);
        return this with { BaseSpeedMmPerTick = movement.BaseSpeedMmPerTick, ActorRadiusMm = movement.ActorRadiusMm, Bounds = movement.Bounds };
    }
}

/// <summary>
/// The run's own terminal state (sim/run-wiring.ts:142 RunEndState), threaded by <see cref="RunRoot.StepRun"/> every
/// tick. <see cref="Result"/> stays null until the run ends; <see cref="WipeStartTick"/> is the first tick of an
/// ongoing full wipe.
/// </summary>
/// <param name="Result">How the run ended, or null while it runs.</param>
/// <param name="ResultTick">The tick it ended, or null.</param>
/// <param name="WipeStartTick">The tick the current full wipe began, or null.</param>
public sealed record RunEndState(RunResult? Result, int? ResultTick, int? WipeStartTick)
{
    /// <summary>run-wiring.ts:148 initialRunEndState: nothing ended, no wipe.</summary>
    public static RunEndState Initial { get; } = new(null, null, null);
}

/// <summary>
/// One run (sim/run-types.ts:72 Run): the sim state, the encounter state, and the dungeon and content
/// <see cref="RunRoot.StepRun"/> reads every tick, plus the run's terminal state.
/// </summary>
/// <param name="State">The sim state; record t of an oracle trace is this after t steps.</param>
/// <param name="Encounter">The encounter state.</param>
/// <param name="Dungeon">The dungeon.</param>
/// <param name="Content">The run's content with any loadouts merged.</param>
/// <param name="RunEnd">The terminal state.</param>
public sealed record Run(SimState State, EncounterState Encounter, DungeonDef Dungeon, RunContent Content, RunEndState RunEnd)
{
    /// <summary>
    /// The run's reward-side state (SEED-LOOT-AWARD, BIBLE s9 item 3): <see cref="RunRoot.StepRun"/> folds
    /// <see cref="SeedLoot.Observe"/> over every step's events, so the Seed bonus is part of the run's own result.
    /// <see cref="RunRewards.Empty"/> until the Seed's Window opens; no oracle field (the frozen TS has no bonus loot).
    /// </summary>
    public RunRewards Rewards { get; init; } = RunRewards.Empty;

    /// <summary>The Seed loot rule resolved from the content at <see cref="RunRoot.CreateRun"/>, or null when no enemy's
    /// onDeath cast opens a Window (then <see cref="Rewards"/> stays empty).</summary>
    public SeedLootRule? SeedRule { get; init; }

    /// <summary>
    /// SEED-PULL-TEACHES (C#-only, layout runs): the enemy scripts the encounter steps, with each room-entry pull's
    /// actors on their pull's own clock (<see cref="Scripts.StartingAt"/>, set by <see cref="RunRoot.StepRun"/> on the
    /// tick the pull spawns). Null = <see cref="RunContent.EnemyScripts"/> unchanged (every run without a layout, and a
    /// layout run before its first room-entry spawn).
    /// </summary>
    public IReadOnlyDictionary<int, IReadOnlyList<ScriptStep>>? ActiveScripts { get; init; }
}

/// <summary>Options a host may pass to one <see cref="RunRoot.StepRun"/> (sim/run-types.ts:84 StepRunOpts).</summary>
/// <param name="AbandonRequested">An explicit human quit: ends the run "abandoned" this tick.</param>
/// <param name="OpenSeats">
/// Actor ids of abandoned, unfilled seats (C#-only, STATE.md D27; <see cref="Backfill.Step"/> passes them): an open seat's
/// idle actor never engages a layout group and is not in the run, so it never holds a dead party's run open
/// (<see cref="RunWiring.IsWipe"/>, STATE.md D44). Null = none.
/// </param>
public sealed record StepRunOpts(bool AbandonRequested = false, IReadOnlyList<int>? OpenSeats = null);

/// <summary>One <see cref="RunRoot.StepRun"/> result: the advanced run and every event of the tick in emission order.</summary>
/// <param name="Run">The advanced run (a new value; the input is not modified).</param>
/// <param name="Events">The tick's events: encounter first, then combat, then pull, Echo, Slack and run-end policy.</param>
public sealed record RunStepResult(Run Run, IReadOnlyList<SimEvent> Events);
