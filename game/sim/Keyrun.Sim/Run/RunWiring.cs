using System;
using System.Collections.Generic;
using System.Linq;
using Keyrun.Sim.Combat;
using Keyrun.Sim.Core;
using Keyrun.Sim.Encounter;

namespace Keyrun.Sim.Runs;

/// <summary>
/// The run root's adapters and per-tick host policy (sim/run-wiring.ts). Adapters: the 2C
/// <see cref="EncounterRules"/> as 2B's <see cref="ICombatClause"/>, <see cref="RunContent"/> as a
/// <see cref="RulesConfig"/>, AbilityDefs as the encounter's <see cref="ScriptAbility"/> view and break-bar tags.
/// Policy (P2, policy-inside-stepRun): enemy and damage-role auto-targeting, Slack spend on player death and wipe, and
/// run-end detection. <see cref="RunRoot"/> is the only production caller.
/// </summary>
public static class RunWiring
{
    /// <summary>run-wiring.ts:46 WIPE_GRACE_TICKS: a full wipe auto-ends the run after 5 s at 20 Hz.</summary>
    public const int WipeGraceTicks = 100;

    /// <summary>
    /// run-wiring.ts:48 rulesConfigOf, plus the Clause the TS installs per tick (clause-hooks.ts withEncounterRules):
    /// <paramref name="encounterRules"/> becomes <see cref="RulesConfig.Clause"/>. Null = no rules at all (the TS
    /// makeRules + core step path outside stepRun), which weights nothing and silences nothing.
    /// </summary>
    /// <param name="content">The run content.</param>
    /// <param name="encounterRules">The run's current encounter rules, or null.</param>
    /// <returns>The combat rules config.</returns>
    public static RulesConfig RulesConfigOf(RunContent content, EncounterRules? encounterRules = null)
    {
        ArgumentNullException.ThrowIfNull(content);
        return new RulesConfig
        {
            Abilities = content.Abilities,
            Effects = content.Effects,
            LockoutTicks = content.LockoutTicks,
            BaseSpeedMmPerTick = content.BaseSpeedMmPerTick,
            ActorRadiusMm = content.ActorRadiusMm,
            Bounds = content.Bounds,
            Combos = content.Combos,
            FieldCombos = content.FieldCombos,
            Clause = encounterRules is null ? null : new EncounterClause(encounterRules),
            Overheat = content.Overheat,
        };
    }

    /// <summary>
    /// run-wiring.ts:61 mergeActors: a new state whose actor map adds (or replaces) <paramref name="spawned"/>. The
    /// state's other fields and every existing actor object are shared, as the TS spread shares them.
    /// </summary>
    /// <param name="state">The state.</param>
    /// <param name="spawned">Actors to add.</param>
    /// <returns><paramref name="state"/> itself when nothing spawned, else a new state.</returns>
    public static SimState MergeActors(SimState state, IReadOnlyList<ActorState> spawned)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(spawned);
        if (spawned.Count == 0)
        {
            return state;
        }
        var actors = new SortedDictionary<int, ActorState>(state.Actors);
        foreach (var actor in spawned)
        {
            actors[actor.Id] = actor;
        }
        return WithActors(state, actors);
    }

    private static SimState WithActors(SimState state, SortedDictionary<int, ActorState> actors) => new()
    {
        Tick = state.Tick,
        Seed = state.Seed,
        Prng = state.Prng,
        Actors = actors,
        RunTimerTicks = state.RunTimerTicks,
        Phase = state.Phase,
        FloorWindows = state.FloorWindows,
        FloorClouds = state.FloorClouds,
    };

    /// <summary>run-wiring.ts:70 abilityTagsOf: ability id to its tags (null = untagged), for break-bar drains.</summary>
    /// <param name="abilities">The run's abilities.</param>
    /// <returns>The tag map.</returns>
    public static IReadOnlyDictionary<string, IReadOnlyList<string>?> AbilityTagsOf(IReadOnlyDictionary<string, AbilityDef> abilities)
    {
        ArgumentNullException.ThrowIfNull(abilities);
        var tags = new Dictionary<string, IReadOnlyList<string>?>(StringComparer.Ordinal);
        foreach (var kv in abilities)
        {
            tags[kv.Key] = kv.Value.Tags;
        }
        return tags;
    }

    /// <summary>
    /// The encounter's view of each ability (what sim/encounter/script-target.ts and chase.ts read off the AbilityDef
    /// stepEncounter receives): id, range, shape kind as its TS string, and whether any op heals.
    /// </summary>
    /// <param name="abilities">The run's abilities.</param>
    /// <returns>Ability id to its script view.</returns>
    public static IReadOnlyDictionary<string, ScriptAbility> ScriptAbilitiesOf(IReadOnlyDictionary<string, AbilityDef> abilities)
    {
        ArgumentNullException.ThrowIfNull(abilities);
        var views = new Dictionary<string, ScriptAbility>(StringComparer.Ordinal);
        foreach (var kv in abilities)
        {
            var def = kv.Value;
            views[kv.Key] = new ScriptAbility(def.Id, def.Range, ShapeKindJs(def.Shape.Kind), def.Effects.Any(op => op is HealOp))
            {
                CastTicks = def.CastTicks,
                Def = def,
            };
        }
        return views;
    }

    private static string ShapeKindJs(ShapeKind kind) => kind switch
    {
        ShapeKind.Target => "target",
        ShapeKind.Self => "self",
        ShapeKind.Cone => "cone",
        ShapeKind.Circle => "circle",
        ShapeKind.Line => "line",
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "unknown shape kind"),
    };

    /// <summary>
    /// run-wiring.ts:81 applyBreakBarPatches: each staggered bar's onEmpty effect applied with the staggered actor as
    /// its own source. Patched actors are copied first, so the input state is never modified; an unknown actor or
    /// effect id is skipped, as the TS.
    /// </summary>
    /// <param name="state">The post-combat state.</param>
    /// <param name="patches">This tick's patches.</param>
    /// <param name="effects">The run's effect definitions.</param>
    /// <returns><paramref name="state"/> itself when nothing applied, else a new state.</returns>
    public static SimState ApplyBreakBarPatches(SimState state, IReadOnlyList<BreakBarEffectPatch> patches, IReadOnlyDictionary<string, EffectDef> effects)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(patches);
        ArgumentNullException.ThrowIfNull(effects);
        if (patches.Count == 0)
        {
            return state;
        }
        SortedDictionary<int, ActorState>? actors = null;
        foreach (var patch in patches)
        {
            var current = actors ?? state.Actors;
            if (!current.TryGetValue(patch.ActorId, out var actor) || !effects.TryGetValue(patch.EffectId, out var def))
            {
                continue;
            }
            var patched = actor.Clone();
            Combat.Effects.ApplyEffectInstance(patched, def, patch.ActorId, patch.DurationTicks, patch.Stacks, state.Tick);
            actors ??= new SortedDictionary<int, ActorState>(state.Actors);
            actors[patch.ActorId] = patched;
        }
        return actors is null ? state : WithActors(state, actors);
    }

    /// <summary>
    /// run-wiring.ts:109 fixupTargeting, in place: every untargeted living enemy targets the party's tank (the first
    /// player with role tank, alive or not); every living damage-role player without a living enemy target targets
    /// the first living enemy (ascending id). Safe every tick and at creation.
    /// </summary>
    /// <param name="state">The state to fix up.</param>
    public static void FixupTargeting(SimState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        var tank = state.Actors.Values.FirstOrDefault(a => a.Kind == ActorKind.Player && a.Role == ActorRole.Tank);
        // PRESPAWN-AGGRO: an idle enemy is never auto-targeted and never auto-targets (no idle actor in an oracle run).
        var enemies = state.Actors.Values.Where(a => a.Kind == ActorKind.Enemy && a.Alive && !a.Idle).ToList();
        if (tank is not null)
        {
            foreach (var enemy in enemies.Where(e => e.TargetId is null))
            {
                enemy.TargetId = tank.Id;
            }
        }
        if (enemies.Count == 0)
        {
            return;
        }
        var firstEnemy = enemies[0];
        foreach (var actor in state.Actors.Values)
        {
            if (actor.Kind != ActorKind.Player || actor.Role != ActorRole.Damage || !actor.Alive)
            {
                continue;
            }
            var currentIsLivingEnemy = actor.TargetId is int id && state.Actors.TryGetValue(id, out var current)
                && current.Kind == ActorKind.Enemy && current.Alive;
            if (!currentIsLivingEnemy)
            {
                actor.TargetId = firstEnemy.Id;
            }
        }
    }

    /// <summary>
    /// run-wiring.ts:129 applyDeathsAndWipe: when this tick's events carry a player death, spends the death cost, plus
    /// the wipe cost when every player is dead, forwarding <c>depleted</c> through <paramref name="emit"/>.
    /// </summary>
    /// <param name="state">The post-tick state.</param>
    /// <param name="encounter">The encounter state.</param>
    /// <param name="events">Every event of the tick so far.</param>
    /// <param name="tick">The post-step tick.</param>
    /// <param name="emit">Event sink.</param>
    /// <returns>The encounter after Slack spend (itself when no player died).</returns>
    public static EncounterState ApplyDeathsAndWipe(SimState state, EncounterState encounter, IReadOnlyList<SimEvent> events, int tick, Action<SimEvent> emit)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(encounter);
        ArgumentNullException.ThrowIfNull(events);
        var anyPlayerDeath = events.Any(e => e.Type == "death" && e.Get("actorId") is JsInt id
            && id.Value >= int.MinValue && id.Value <= int.MaxValue
            && state.Actors.TryGetValue((int)id.Value, out var a) && a.Kind == ActorKind.Player);
        if (!anyPlayerDeath)
        {
            return encounter;
        }
        var next = Timer.SpendSlackOnDeath(encounter, tick, emit);
        if (AllPlayersDead(state))
        {
            next = Timer.SpendSlackOnWipe(next, tick, emit);
        }
        return next;
    }

    private static bool AllPlayersDead(SimState state)
    {
        var players = state.Actors.Values.Where(a => a.Kind == ActorKind.Player).ToList();
        return players.Count > 0 && players.All(a => !a.Alive);
    }

    /// <summary>
    /// run-wiring.ts:153 checkRunEnd: idempotent once ended. The run is complete when the pull index is past the last
    /// pull (finish timed or depleted); an abandon request finishes it abandoned; a full wipe lasting
    /// <see cref="WipeGraceTicks"/> finishes it with depleted forced. Each finish emits <c>finish</c>.
    /// </summary>
    /// <param name="prev">The previous terminal state.</param>
    /// <param name="state">The post-tick state.</param>
    /// <param name="encounter">The encounter state.</param>
    /// <param name="dungeon">The dungeon.</param>
    /// <param name="tick">The post-step tick.</param>
    /// <param name="abandonRequested">The host's abandon signal.</param>
    /// <param name="emit">Event sink.</param>
    /// <returns>The next terminal state.</returns>
    public static RunEndState CheckRunEnd(RunEndState prev, SimState state, EncounterState encounter, DungeonDef dungeon, int tick, bool abandonRequested, Action<SimEvent> emit)
    {
        ArgumentNullException.ThrowIfNull(prev);
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(encounter);
        ArgumentNullException.ThrowIfNull(dungeon);
        if (prev.Result is not null)
        {
            return prev;
        }
        if (encounter.PullIndex >= dungeon.Pulls.Count)
        {
            return new RunEndState(Timer.FinishRun(encounter, tick, true, emit), tick, prev.WipeStartTick);
        }
        if (abandonRequested)
        {
            return new RunEndState(Timer.FinishRun(encounter, tick, false, emit), tick, prev.WipeStartTick);
        }
        if (AllPlayersDead(state))
        {
            var wipeStart = prev.WipeStartTick ?? tick;
            if (tick - wipeStart >= WipeGraceTicks)
            {
                return new RunEndState(Timer.FinishRun(encounter with { Depleted = true }, tick, true, emit), tick, wipeStart);
            }
            return new RunEndState(null, null, wipeStart);
        }
        return RunEndState.Initial;
    }
}

/// <summary>
/// The 2C <see cref="EncounterRules"/> adapted to what combat (2B) reads of the active Clause: Hush silence through
/// <see cref="Clauses.ShouldSilence"/> and Pact weighting through <see cref="Clauses.ApplyPactDamageBp"/>. Built from
/// the run's current rules on every step (the TS withEncounterRules span), so no Clause outlives its run.
/// </summary>
public sealed class EncounterClause : ICombatClause
{
    /// <summary>Wraps <paramref name="rules"/>.</summary>
    /// <param name="rules">The run's encounter rules.</param>
    public EncounterClause(EncounterRules rules) => Rules = rules ?? throw new ArgumentNullException(nameof(rules));

    /// <summary>The wrapped rules.</summary>
    public EncounterRules Rules { get; }

    /// <inheritdoc />
    public bool IsSilenced(int tick) => Clauses.ShouldSilence(tick, Rules);

    /// <inheritdoc />
    public long WeightDamage(long rawAmount, bool wasCombo) => Clauses.ApplyPactDamageBp(rawAmount, wasCombo, Rules);
}
