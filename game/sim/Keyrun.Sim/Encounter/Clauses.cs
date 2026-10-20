using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Keyrun.Sim.Core;

namespace Keyrun.Sim.Encounter;

/// <summary>The four Clauses (sim/encounter/clauses.ts:39 ClauseKind; BIBLE s8).</summary>
public enum ClauseKind
{
    /// <summary>"echo": every interrupted cast spawns a weak add.</summary>
    Echo,

    /// <summary>"tide": floor telegraphs drift.</summary>
    Tide,

    /// <summary>"hush": silence pulses on a fixed interval.</summary>
    Hush,

    /// <summary>"pact": Combos deal more, uncombo'd hits deal less.</summary>
    Pact,
}

/// <summary>A Clause as the encounter sees it (clauses.ts:41, a structural mirror of content's ClauseDef).</summary>
/// <param name="Id">Clause id.</param>
/// <param name="Name">Display name.</param>
/// <param name="Kind">Which of the four.</param>
/// <param name="Params">Integer params by name (an absent param reads as 0, clauses.ts <c>?? 0</c>).</param>
/// <param name="Description">Display text.</param>
public sealed record ClauseDef(string Id, string Name, ClauseKind Kind, IReadOnlyDictionary<string, long> Params, string Description);

/// <summary>
/// Encounter-lane tunables plus the one active Clause's knobs (clauses.ts:49 EncounterRules). Immutable; a Clause is
/// folded in with <see cref="Clauses.ApplyClause"/>.
/// </summary>
public sealed record EncounterRules
{
    /// <summary>Key level these rules were built for.</summary>
    public int KeyLevel { get; init; }

    /// <summary>Tempo cast-frequency bp per key (<see cref="Tempo.TempoBpPerKey"/>).</summary>
    public int TempoBpPerKey { get; init; }

    /// <summary>Lead shrink bp per key (<see cref="Tempo.LeadShrinkBpPerKey"/>).</summary>
    public int LeadShrinkBpPerKey { get; init; }

    /// <summary>Lead floor (<see cref="Tempo.LeadFloorTicks"/>).</summary>
    public int LeadFloorTicks { get; init; }

    /// <summary>Every floor (<see cref="Tempo.EveryFloorTicks"/>).</summary>
    public int EveryFloorTicks { get; init; }

    /// <summary>Slack base (<see cref="Timer.SlackBaseTicks"/>).</summary>
    public int SlackBaseTicks { get; init; }

    /// <summary>Slack per key (<see cref="Timer.SlackPerKeyTicks"/>).</summary>
    public int SlackPerKeyTicks { get; init; }

    /// <summary>Death cost (<see cref="Timer.DeathSlackCostTicks"/>).</summary>
    public int DeathSlackCostTicks { get; init; }

    /// <summary>Wipe cost (<see cref="Timer.WipeSlackCostTicks"/>).</summary>
    public int WipeSlackCostTicks { get; init; }

    /// <summary>Tide: mm per tick every telegraph origin drifts along +x.</summary>
    public long DriftMmPerTick { get; init; }

    /// <summary>Hush: ticks between silence pulses.</summary>
    public int SilenceIntervalTicks { get; init; }

    /// <summary>Hush: ticks each pulse lasts.</summary>
    public int SilenceDurationTicks { get; init; }

    /// <summary>Pact: bp added to a Combo hit.</summary>
    public long ComboDamageBp { get; init; }

    /// <summary>Pact: bp added to a solo hit (negative = less).</summary>
    public long SoloDamageBp { get; init; }

    /// <summary>Echo: max health of a spawned add.</summary>
    public long EchoAddMaxHealth { get; init; }

    /// <summary>Echo: ticks from the interrupt to the spawn.</summary>
    public int EchoSpawnDelayTicks { get; init; }

    /// <summary>The active Clause id, or null.</summary>
    public string? ActiveClauseId { get; init; }

    /// <summary>The active Clause kind, or null.</summary>
    public ClauseKind? ActiveClauseKind { get; init; }
}

/// <summary>clauses.ts:93 ClauseAlreadyActiveError: a second Clause reached the sim (exactly one per key, fail closed).</summary>
public sealed class ClauseAlreadyActiveException : InvalidOperationException
{
    /// <summary>Builds the TS message: <c>clause "a" already active; cannot also apply "b" (exactly one Clause per key)</c>.</summary>
    /// <param name="activeId">The Clause already active.</param>
    /// <param name="attemptedId">The Clause refused.</param>
    public ClauseAlreadyActiveException(string activeId, string attemptedId)
        : base($"clause \"{activeId}\" already active; cannot also apply \"{attemptedId}\" (exactly one Clause per key)")
    {
    }
}

/// <summary>An Echo add not yet spawned (clauses.ts:148 EchoSpawnRequest).</summary>
/// <param name="SpawnAtTick">The tick it spawns.</param>
/// <param name="SourceActorId">The interrupted caster (where it spawns).</param>
/// <param name="MaxHealth">Its max health.</param>
public sealed record EchoSpawnRequest(int SpawnAtTick, int SourceActorId, long MaxHealth);

/// <summary>
/// The four Clauses as run modifiers (sim/encounter/clauses.ts). Pure: Tide drifts telegraph origins, Hush is a
/// silence-window predicate, Pact is a bp damage weighting, Echo reduces interrupt events to spawn requests
/// (realised by <see cref="EchoSpawns"/>). Hush and Pact are read by the combat lanes (casting, damage).
/// </summary>
public static class Clauses
{
    /// <summary>clauses.ts:70 defaultEncounterRules: tempo/timer constants, every Clause knob 0, no Clause active.</summary>
    public static EncounterRules DefaultEncounterRules(int keyLevel) => new()
    {
        KeyLevel = keyLevel,
        TempoBpPerKey = Tempo.TempoBpPerKey,
        LeadShrinkBpPerKey = Tempo.LeadShrinkBpPerKey,
        LeadFloorTicks = Tempo.LeadFloorTicks,
        EveryFloorTicks = Tempo.EveryFloorTicks,
        SlackBaseTicks = Timer.SlackBaseTicks,
        SlackPerKeyTicks = Timer.SlackPerKeyTicks,
        DeathSlackCostTicks = Timer.DeathSlackCostTicks,
        WipeSlackCostTicks = Timer.WipeSlackCostTicks,
    };

    private static long Param(ClauseDef clause, string name) => clause.Params.TryGetValue(name, out var v) ? v : 0;

    private static int IntParam(ClauseDef clause, string name) => checked((int)Param(clause, name));

    /// <summary>
    /// clauses.ts:101 applyClause: a NEW rules value with the Clause's params folded in and it marked active.
    /// </summary>
    /// <exception cref="ClauseAlreadyActiveException">A Clause is already active on <paramref name="rules"/>.</exception>
    public static EncounterRules ApplyClause(EncounterRules rules, ClauseDef clause)
    {
        if (rules.ActiveClauseId is not null)
        {
            throw new ClauseAlreadyActiveException(rules.ActiveClauseId, clause.Id);
        }
        var next = rules with { ActiveClauseId = clause.Id, ActiveClauseKind = clause.Kind };
        return clause.Kind switch
        {
            ClauseKind.Echo => next with
            {
                EchoAddMaxHealth = Param(clause, "addMaxHealth"),
                EchoSpawnDelayTicks = IntParam(clause, "spawnDelayTicks"),
            },
            ClauseKind.Tide => next with { DriftMmPerTick = Param(clause, "driftMmPerTick") },
            ClauseKind.Hush => next with
            {
                SilenceIntervalTicks = IntParam(clause, "intervalTicks"),
                SilenceDurationTicks = IntParam(clause, "silenceDurationTicks"),
            },
            ClauseKind.Pact => next with
            {
                ComboDamageBp = Param(clause, "comboDamageBp"),
                SoloDamageBp = Param(clause, "soloDamageBp"),
            },
            _ => throw new ArgumentException($"unknown clause kind: {clause.Kind}", nameof(clause)),
        };
    }

    /// <summary>clauses.ts:124 driftTelegraphOrigin: Tide shifts the origin +x by driftMmPerTick; otherwise identity.</summary>
    public static Telegraph DriftTelegraphOrigin(Telegraph telegraph, EncounterRules rules)
    {
        if (rules.ActiveClauseKind != ClauseKind.Tide || rules.DriftMmPerTick == 0)
        {
            return telegraph;
        }
        return telegraph with { Origin = telegraph.Origin with { X = telegraph.Origin.X + rules.DriftMmPerTick } };
    }

    /// <summary>clauses.ts:128 driftTelegraphs: <see cref="DriftTelegraphOrigin"/> over the registry, in order.</summary>
    public static IReadOnlyList<Telegraph> DriftTelegraphs(IReadOnlyList<Telegraph> telegraphs, EncounterRules rules) =>
        telegraphs.Select(t => DriftTelegraphOrigin(t, rules)).ToList();

    /// <summary>
    /// BIBLE.md:156 "*Tide* (floor patches drift)" (no TS counterpart): under Tide every floor Window in
    /// <see cref="SimState.FloorWindows"/> shifts +x by driftMmPerTick, the same drift the telegraphs take. Pure: the
    /// input state is never touched; a copy is returned when anything moves, otherwise the input itself.
    /// </summary>
    /// <param name="state">The tick's state (read only).</param>
    /// <param name="rules">The encounter rules carrying the active Clause.</param>
    /// <returns>The state with its floor Windows drifted, or <paramref name="state"/> when nothing drifts.</returns>
    public static SimState DriftFloorWindows(SimState state, EncounterRules rules)
    {
        ArgumentNullException.ThrowIfNull(state);
        if (rules.ActiveClauseKind != ClauseKind.Tide || rules.DriftMmPerTick == 0 || state.FloorWindows.Count == 0)
        {
            return state;
        }
        var copy = state.Clone();
        foreach (var key in copy.FloorWindows.Keys.ToList())
        {
            var fw = copy.FloorWindows[key];
            fw.Pos = fw.Pos with { X = fw.Pos.X + rules.DriftMmPerTick };
        }
        return copy;
    }

    /// <summary>
    /// clauses.ts:134 shouldSilence: under Hush, true inside a pulse. The first pulse starts at tick
    /// <c>silenceIntervalTicks</c> (never tick 0) and lasts <c>silenceDurationTicks</c>. Ticks are non-negative, so
    /// <c>%</c> and <c>/</c> match the TS (ORACLE.md section 8: clauses.ts:136-137).
    /// </summary>
    public static bool ShouldSilence(int tick, EncounterRules rules)
    {
        if (rules.ActiveClauseKind != ClauseKind.Hush || rules.SilenceIntervalTicks <= 0 || tick <= 0)
        {
            return false;
        }
        var sincePulse = tick % rules.SilenceIntervalTicks;
        var pulseIndex = tick / rules.SilenceIntervalTicks;
        return pulseIndex >= 1 && sincePulse < rules.SilenceDurationTicks;
    }

    /// <summary>
    /// clauses.ts:142 applyPactDamageBp: under Pact, <c>trunc(damage * (10000 + bp) / 10000)</c> with the Combo or
    /// solo bp (ORACLE.md section 8: Math.trunc, so <see cref="IntMath.TruncDiv"/>); otherwise unchanged.
    /// </summary>
    public static long ApplyPactDamageBp(long baseDamage, bool wasCombo, EncounterRules rules)
    {
        if (rules.ActiveClauseKind != ClauseKind.Pact)
        {
            return baseDamage;
        }
        var bp = wasCombo ? rules.ComboDamageBp : rules.SoloDamageBp;
        return IntMath.TruncDiv(baseDamage * (Tempo.BpDenom + bp), Tempo.BpDenom);
    }

    /// <summary>
    /// clauses.ts:155 pendingEchoSpawns: under Echo, one request per <c>interrupt</c> event, at its tick plus the
    /// delay, sourced at the event's <c>targetId</c> (the interrupted caster).
    /// </summary>
    public static IReadOnlyList<EchoSpawnRequest> PendingEchoSpawns(IEnumerable<SimEvent> events, EncounterRules rules)
    {
        if (rules.ActiveClauseKind != ClauseKind.Echo)
        {
            return Array.Empty<EchoSpawnRequest>();
        }
        var requests = new List<EchoSpawnRequest>();
        foreach (var e in events)
        {
            if (e.Type != "interrupt")
            {
                continue;
            }
            if (e.Get("targetId") is not JsInt target)
            {
                throw new ArgumentException("interrupt event lacks an integer targetId", nameof(events));
            }
            requests.Add(new EchoSpawnRequest(e.Tick + rules.EchoSpawnDelayTicks, checked((int)target.Value), rules.EchoAddMaxHealth));
        }
        return requests;
    }

    private static string KindJs(ClauseKind? k) => k switch
    {
        ClauseKind.Echo => "echo",
        ClauseKind.Tide => "tide",
        ClauseKind.Hush => "hush",
        ClauseKind.Pact => "pact",
        _ => "none",
    };

    /// <summary>
    /// clauses.ts:171 hashEncounterRules: FNV-1a 32 over the UTF-16 code units of
    /// <c>key|drift|silenceInterval|silenceDuration|comboBp|soloBp|echoHealth|echoDelay|clauseId|clauseKind</c>
    /// (null as "none"), as 8 lowercase hex digits.
    /// </summary>
    public static string HashEncounterRules(EncounterRules rules)
    {
        static string N(long v) => v.ToString(CultureInfo.InvariantCulture);
        var fields = string.Join("|",
            N(rules.KeyLevel), N(rules.DriftMmPerTick), N(rules.SilenceIntervalTicks), N(rules.SilenceDurationTicks),
            N(rules.ComboDamageBp), N(rules.SoloDamageBp), N(rules.EchoAddMaxHealth), N(rules.EchoSpawnDelayTicks),
            rules.ActiveClauseId ?? "none", KindJs(rules.ActiveClauseKind));
        var hash = Fnv1a.OffsetBasis;
        unchecked
        {
            foreach (var ch in fields)
            {
                hash ^= ch;
                hash *= Fnv1a.Prime;
            }
        }
        return Fnv1a.Hex(hash);
    }
}
