using System;
using System.Collections.Generic;
using Keyrun.Sim.Core;

namespace Keyrun.Sim.Encounter;

/// <summary>
/// The encounter lane's slice of durable run state (sim/encounter/telegraphs.ts:41 EncounterState). Immutable: every
/// step returns a new value via <c>with</c>. The TS optional fields (firedOnDeath, pendingEchoSpawns,
/// nextEchoActorId, nextTelegraphId) carry their read-time defaults here ({}, [], 1000000, 1), which is what
/// <c>createRun</c> (sim/index.ts:106-117) and every reader (index.ts:119-121, echo-spawns.ts:42-47) use.
/// </summary>
public sealed record EncounterState
{
    /// <summary>Pending telegraphs, in registry order.</summary>
    public IReadOnlyList<Telegraph> Telegraphs { get; init; } = Array.Empty<Telegraph>();

    /// <summary>Active Break bars by id, in JS key order. Never mutated after construction: steps build a new one.</summary>
    public JsRecord<BreakBarState> BreakBars { get; init; } = new();

    /// <summary>Index of the active pull.</summary>
    public int PullIndex { get; init; }

    /// <summary>Slack bank, ticks.</summary>
    public int SlackTicks { get; init; }

    /// <summary>Par time, ticks.</summary>
    public int ParTicks { get; init; }

    /// <summary>Key level.</summary>
    public int KeyLevel { get; init; }

    /// <summary>True once the Slack bank has hit 0 (or the run ended depleted).</summary>
    public bool Depleted { get; init; }

    /// <summary>Encounter tunables and the active Clause.</summary>
    public EncounterRules Rules { get; init; } = Clauses.DefaultEncounterRules(1);

    /// <summary>Actor id to the onDeath casts already fired for it (the scripts' fire-once contract).</summary>
    public IReadOnlyDictionary<int, IReadOnlyList<string>> FiredOnDeath { get; init; } = new SortedDictionary<int, IReadOnlyList<string>>();

    /// <summary>STARTCAST-BUSY: scripted casts held for a busy caster (<see cref="CastDeferral"/>; TS deferredCasts, default []).</summary>
    public IReadOnlyList<DeferredCast> DeferredCasts { get; init; } = Array.Empty<DeferredCast>();

    /// <summary>Echo adds queued but not yet spawned.</summary>
    public IReadOnlyList<EchoSpawnRequest> PendingEchoSpawns { get; init; } = Array.Empty<EchoSpawnRequest>();

    /// <summary>Next id an Echo add gets (starts at <see cref="EchoSpawns.EchoActorIdBase"/>).</summary>
    public int NextEchoActorId { get; init; } = EchoSpawns.EchoActorIdBase;

    /// <summary>Next telegraph id (starts at 1; run state, never a process global).</summary>
    public int NextTelegraphId { get; init; } = 1;

    /// <summary>
    /// The armed boss rules (lane 3F, <see cref="BossRules.Arm"/>), or null. Null on every <see cref="Create"/> and so
    /// on every oracle run: the boss rules are new behaviour with no TS oracle and run only when a host arms them.
    /// </summary>
    public BossState? Boss { get; init; }

    /// <summary>
    /// The encounter createRun builds (sim/index.ts:106-117): empty registries, pull 0, a full Slack bank for the
    /// key (<see cref="Timer.InitialSlackTicks"/>), not depleted, nextTelegraphId 1.
    /// </summary>
    /// <param name="keyLevel">Key level (createRun: <c>content.keyLevel ?? 1</c>).</param>
    /// <param name="parTicks">Par (createRun: <c>content.parTicks ?? 24000</c>).</param>
    /// <param name="rules">The rules with any Clause already applied.</param>
    public static EncounterState Create(int keyLevel, int parTicks, EncounterRules rules) => new()
    {
        PullIndex = 0,
        SlackTicks = Timer.InitialSlackTicks(keyLevel),
        ParTicks = parTicks,
        KeyLevel = keyLevel,
        Depleted = false,
        Rules = rules,
        NextTelegraphId = 1,
    };
}
