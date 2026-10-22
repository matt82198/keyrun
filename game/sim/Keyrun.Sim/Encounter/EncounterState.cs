using System;
using System.Collections.Generic;
using Keyrun.Sim.Core;

namespace Keyrun.Sim.Encounter;

/// <summary>
/// The encounter lane's slice of durable run state (sim/encounter/telegraphs.ts:40 EncounterState). Immutable: every
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

    /// <summary>
    /// PRESPAWN-AGGRO (C#-only; BIBLE section 9.1): the ids of every group (pull or pack) that has engaged, in engage
    /// order. A group engages once and stays engaged. On a run without a layout a pull engages when it spawns.
    /// </summary>
    public IReadOnlyList<string> Engaged { get; init; } = Array.Empty<string>();

    /// <summary>R-SYNERGY-1 / G2.5 lane L4: the skip record (<see cref="Skips"/>). Empty until the party passes a pack's post.</summary>
    public SkipLedger Skips { get; init; } = SkipLedger.Empty;

    /// <summary>Slack bank, ticks.</summary>
    public int SlackTicks { get; init; }

    /// <summary>Par time, ticks.</summary>
    public int ParTicks { get; init; }

    /// <summary>Key level.</summary>
    public int KeyLevel { get; init; }

    /// <summary>True once an unrecovered full wipe ended the run depleted (STATE.md D46: deaths never set it).</summary>
    public bool Depleted { get; init; }

    /// <summary>
    /// Player deaths this run (MPLUS-DEATH-PENALTY, BIBLE s8): one per player death, same-tick deaths each counted, as
    /// the Mythic+ timer counts them. <see cref="Timer.DeathPenaltyTicks"/> turns it into the time the deaths cost.
    /// </summary>
    public int Deaths { get; init; }

    /// <summary>
    /// Time the run's deaths and wipes have added to the run clock, ticks (STATE.md D46, Mythic+): each death adds
    /// <see cref="Timer.DeathCostTicks"/>, each full wipe <see cref="Timer.WipeCostTicks"/> on top.
    /// <see cref="Timer.ClockTicks"/> is the elapsed run timer plus this.
    /// </summary>
    public int PenaltyTicks { get; init; }

    /// <summary>Encounter tunables and the active Clause.</summary>
    public EncounterRules Rules { get; init; } = Clauses.DefaultEncounterRules(1);

    /// <summary>Actor id to the onDeath casts already fired for it (the scripts' fire-once contract).</summary>
    public IReadOnlyDictionary<int, IReadOnlyList<string>> FiredOnDeath { get; init; } = new SortedDictionary<int, IReadOnlyList<string>>();

    /// <summary>
    /// CAST-FINISH: actor id to the indices of its script steps that came due while it was casting and wait for that
    /// cast to land, in the order they will fire (<see cref="StepEnemyScriptsResult.PendingSteps"/>). Always empty on a
    /// frozen-TS replay (<see cref="ScriptStep.TsCastReplace"/>).
    /// </summary>
    public IReadOnlyDictionary<int, IReadOnlyList<int>> PendingSteps { get; init; } = new SortedDictionary<int, IReadOnlyList<int>>();

    /// <summary>
    /// STARTCAST-BUSY: frozen-TS replay steps (<see cref="ScriptStep.TsCastReplace"/>) that came due while their caster
    /// was busy, held until it is free (<see cref="CastDeferral"/>; TS <c>deferredCasts</c>, default []). Always empty
    /// for the CAST-FINISH steps, which wait in <see cref="PendingSteps"/> instead.
    /// </summary>
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
    /// R-MULTIBOSS-1: the bosses still to fight after <see cref="Boss"/>, in order (<see cref="BossRules.ArmAll"/>); each is
    /// armed in <see cref="Boss"/>'s place when the one before it dies. Empty on every run armed with one boss.
    /// </summary>
    public IReadOnlyList<BossConfig> NextBosses { get; init; } = Array.Empty<BossConfig>();

    /// <summary>
    /// R-MULTIBOSS-1: how many bosses <see cref="BossRules.ArmAll"/> armed when it armed more than one, else 0. Above 1 every
    /// boss death emits <c>boss_death</c>; 0 (every one-boss run, the Hollow Vault) emits none, so its event stream is unchanged.
    /// </summary>
    public int BossesArmed { get; init; }

    /// <summary>R-MULTIBOSS-1: the 0-based fight-order index of <see cref="Boss"/> among the armed bosses.</summary>
    public int BossIndex { get; init; }

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
