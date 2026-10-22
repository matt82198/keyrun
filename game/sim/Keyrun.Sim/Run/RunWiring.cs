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
/// Policy (P2, policy-inside-stepRun): enemy and damage-role auto-targeting, death and wipe time added to the run clock (D46), and
/// run-end detection. <see cref="RunRoot"/> is the only production caller.
/// </summary>
public static class RunWiring
{
    /// <summary>
    /// run-wiring.ts:46 WIPE_GRACE_TICKS: a full wipe auto-ends the run after 5 s at 20 Hz. Layout-less runs only (the
    /// frozen TS oracle's rule); a layout run releases instead (<see cref="ReleaseDelayTicks"/>, R-BACKFILL-2).
    /// </summary>
    public const int WipeGraceTicks = 100;

    /// <summary>
    /// WIPE-IS-RELEASE (ruling R-BACKFILL-2, BIBLE s9.1; WoW Mythic+): on a layout run a dead seat releases and respawns
    /// where the run placed it (the dungeon entrance) this many ticks after its death, 5 s at 20 Hz.
    /// </summary>
    public const int ReleaseDelayTicks = 100;

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
            Marks = content.Marks,
            // The TS push-out is a frozen-TS replay rule, and a replay has no layout (RunContent.Layout is null on every
            // oracle run). A layout run is a game run, so D54 ENEMIES-NEVER-DISPLACE holds there whatever setup it
            // was loaded from: under the TS rule a pack locked on a standing tank (HOLDER-LOCK D30) shoves him
            // back through a doorway the party already crossed.
            TsSymmetricPushOut = content.TsSymmetricPushOut && content.Layout is null,
            // TEMPO-LOOP-CLAMP: a frozen-TS replay marks every enemy step TsCastReplace (Keyrun.Replay RunSetup.TsScripts);
            // any other run's scheduler never fires a cast before its cooldown, so a not_ready refusal is reported.
            ReportsEnemyNotReady = !content.EnemyScripts.Values.Any(s => s is not null && s.Any(step => step.TsCastReplace)),
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
                AreaReachMm = def.Shape.Kind is ShapeKind.Circle or ShapeKind.Cone && def.Effects.Any(op => op is DamageOp)
                    ? def.Shape.Radius
                    : null,
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
    /// C#-only (f-8b3fff18, BIBLE s9 Twins and s9.3 "Stops as well as kicks"): every listed actor still casting has its
    /// cast ended with a <c>cast_stopped</c> event (<see cref="Casting.Stopcast"/>) at the state's tick, in list order.
    /// The run root calls it right after the stagger stun is applied, post-combat of the stagger tick, so a cast due on
    /// the next tick or later never resolves, while one already resolved this tick stands (casts complete before
    /// commands: the same intended-tick rule as a kick). Stopped actors are copied first; the input state is never
    /// modified.
    /// </summary>
    /// <param name="state">The post-combat state.</param>
    /// <param name="actorIds">The actors whose casts end (this tick's staggered bar actors).</param>
    /// <param name="emit">Event sink.</param>
    /// <returns><paramref name="state"/> itself when no cast ended, else a new state.</returns>
    public static SimState StopCasts(SimState state, IReadOnlyList<int> actorIds, Action<SimEvent> emit)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(actorIds);
        ArgumentNullException.ThrowIfNull(emit);
        SortedDictionary<int, ActorState>? actors = null;
        foreach (var actorId in actorIds)
        {
            var current = actors ?? state.Actors;
            if (!current.TryGetValue(actorId, out var actor) || actor.CastId is null)
            {
                continue;
            }
            var stopped = actor.Clone();
            Casting.Stopcast(stopped, emit, state.Tick);
            actors ??= new SortedDictionary<int, ActorState>(state.Actors);
            actors[actorId] = stopped;
        }
        return actors is null ? state : WithActors(state, actors);
    }

    /// <summary>
    /// run-wiring.ts:109 fixupTargeting, in place: every untargeted living enemy targets the party's tank (the first
    /// player with role tank, alive or not); every living damage-role player without a living enemy target targets
    /// the first living enemy (ascending id). Safe every tick and at creation. First, HOLDER-LOCK (STATE.md D30):
    /// <see cref="Holder.LockHolders"/> locks every living enemy without a living holder onto the nearest living player.
    /// </summary>
    /// <remarks>
    /// RETARGET-FIZZLE (RULING R-RETARGET-1, finding f-f1ab879b; WoW): every write goes through
    /// <see cref="Casting.SetTarget"/>, so a cast whose start target died mid-cast stays pinned on that dead target and
    /// fizzles (cast_complete, no effect) instead of hopping to the auto-picked next enemy; the new target is for the
    /// next press. The oracle traces bots-hv-k2-s7/k2-s11 (t=421) and k10-s7 (t=1502) record such a Cinder flare.
    /// </remarks>
    /// <param name="state">The state to fix up.</param>
    public static void FixupTargeting(SimState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        Holder.LockHolders(state);
        var tank = state.Actors.Values.FirstOrDefault(a => a.Kind == ActorKind.Player && a.Role == ActorRole.Tank);
        // PRESPAWN-AGGRO: an idle enemy is never auto-targeted and never auto-targets (no idle actor in an oracle run).
        var enemies = state.Actors.Values.Where(a => a.Kind == ActorKind.Enemy && a.Alive && !a.Idle).ToList();
        if (tank is not null)
        {
            foreach (var enemy in enemies.Where(e => e.TargetId is null))
            {
                Casting.SetTarget(enemy, tank.Id);
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
                Casting.SetTarget(actor, firstEnemy.Id);
            }
        }
    }

    /// <summary>
    /// run-wiring.ts:129 applyDeathsAndWipe: each distinct player death in this tick's events adds the death cost to the
    /// run clock (<see cref="Timer.ChargeDeath"/>), plus the wipe cost when the party has wiped (<see cref="IsWipe"/>,
    /// D44) (D46). Emits nothing.
    /// </summary>
    /// <param name="state">The post-tick state.</param>
    /// <param name="encounter">The encounter state.</param>
    /// <param name="events">Every event of the tick so far.</param>
    /// <param name="tick">The post-step tick.</param>
    /// <param name="emit">Event sink.</param>
    /// <param name="openSeats">Abandoned or unfilled seats' actor ids (<see cref="StepRunOpts.OpenSeats"/>), or null.</param>
    /// <returns>The encounter with the deaths charged (itself when no player died).</returns>
    public static EncounterState ApplyDeathsAndWipe(SimState state, EncounterState encounter, IReadOnlyList<SimEvent> events, int tick, Action<SimEvent> emit, IReadOnlyList<int>? openSeats = null)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(encounter);
        ArgumentNullException.ThrowIfNull(events);
        // MPLUS-DEATH-PENALTY (Matt 2026-10-20, BIBLE s8): as on a Mythic+ timer every player death costs its own
        // death cost, so two players killed on one tick cost two deaths (the TS reference charged once per tick).
        // Distinct actor ids, so a repeated death event for one player is still one death.
        var playerDeaths = events
            .Where(e => e.Type == "death")
            .Select(e => e.Get("actorId"))
            .OfType<JsInt>()
            .Where(id => id.Value >= int.MinValue && id.Value <= int.MaxValue)
            .Select(id => (int)id.Value)
            .Where(id => state.Actors.TryGetValue(id, out var a) && a.Kind == ActorKind.Player)
            .Distinct()
            .Count();
        if (playerDeaths == 0)
        {
            return encounter;
        }
        // D46 (Matt 2026-10-20 "almost exact to wow"): deaths and wipes only add time to the run clock; nothing drains,
        // so nothing is emitted here (the old Slack bank's "depleted" event is gone). tick and emit keep the host's call shape.
        _ = tick;
        _ = emit;
        var next = encounter;
        for (var i = 0; i < playerDeaths; i++)
        {
            next = Timer.ChargeDeath(next);
        }
        if (IsWipe(state, openSeats))
        {
            next = Timer.ChargeWipe(next);
        }
        return next;
    }

    /// <summary>
    /// True when the party has wiped: the state holds at least one player and no living player who is actually in the
    /// run remains (PATH-TO-BOSS, STATE.md D44; run-wiring.ts allPlayersDead when no seat is open). A seat in
    /// <paramref name="openSeats"/> (abandoned, or unfilled while it waits for a joiner) is not in the run, so its idle
    /// body never holds a dead party's run open, in or out of combat. It resolves like any full wipe: the wipe cost on
    /// the run clock (D46), then on a layout run a release and run-back (<see cref="Release"/>, R-BACKFILL-2), and on a
    /// layout-less run <see cref="WipeGraceTicks"/> and a depleted finish (the frozen TS rule).
    /// </summary>
    /// <param name="state">The post-tick state.</param>
    /// <param name="openSeats">Abandoned or unfilled seats' actor ids, or null (none open).</param>
    /// <returns>True on a wipe.</returns>
    public static bool IsWipe(SimState state, IReadOnlyList<int>? openSeats = null)
    {
        ArgumentNullException.ThrowIfNull(state);
        var anyPlayer = false;
        foreach (var a in state.Actors.Values)
        {
            if (a.Kind != ActorKind.Player)
            {
                continue;
            }
            anyPlayer = true;
            if (a.Alive && (openSeats is null || !openSeats.Contains(a.Id)))
            {
                return false;
            }
        }
        return anyPlayer;
    }

    /// <summary>
    /// run-wiring.ts:153 checkRunEnd: idempotent once ended. The run is complete when the pull index is past the last
    /// pull (finish timed or depleted); an abandon request finishes it abandoned. Without <paramref name="release"/> (a
    /// layout-less run, the frozen TS rule) a wipe (<see cref="IsWipe"/>) lasting <see cref="WipeGraceTicks"/> finishes
    /// it with depleted forced. With <paramref name="release"/> (WIPE-IS-RELEASE, R-BACKFILL-2: a layout run) a wipe
    /// never ends the run; the dead seats release and run back (<see cref="Release"/>), and the run clock reaching the
    /// timer cap (<see cref="Timer.CapTicks"/>) finishes it with depleted forced. Each finish emits <c>finish</c>.
    /// </summary>
    /// <param name="prev">The previous terminal state.</param>
    /// <param name="state">The post-tick state.</param>
    /// <param name="encounter">The encounter state.</param>
    /// <param name="dungeon">The dungeon.</param>
    /// <param name="tick">The post-step tick.</param>
    /// <param name="abandonRequested">The host's abandon signal.</param>
    /// <param name="emit">Event sink.</param>
    /// <param name="openSeats">Abandoned or unfilled seats' actor ids (<see cref="StepRunOpts.OpenSeats"/>), or null.</param>
    /// <param name="release">True on a layout run: a wipe releases instead of ending the run, and the timer cap ends it.</param>
    /// <param name="forces">The Enemy Forces bar, or null for none. With a bar the cleared pulls end the run only once the bar
    /// is full (R-FORCES-COMPLETE-1): a final boss killed below 100% leaves the run going until the packs fill it.</param>
    /// <returns>The next terminal state.</returns>
    public static RunEndState CheckRunEnd(RunEndState prev, SimState state, EncounterState encounter, DungeonDef dungeon, int tick, bool abandonRequested, Action<SimEvent> emit, IReadOnlyList<int>? openSeats = null, bool release = false, RunForces? forces = null)
    {
        ArgumentNullException.ThrowIfNull(prev);
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(encounter);
        ArgumentNullException.ThrowIfNull(dungeon);
        if (prev.Result is not null)
        {
            return prev;
        }
        if (encounter.PullIndex >= dungeon.Pulls.Count && (forces is null || Forces.Read(state, forces.PointsByEnemy, forces.Required).Full))
        {
            return new RunEndState(Timer.FinishRun(encounter, tick, true, emit), tick, prev.WipeStartTick);
        }
        if (abandonRequested)
        {
            return new RunEndState(Timer.FinishRun(encounter, tick, false, emit), tick, prev.WipeStartTick);
        }
        if (release)
        {
            if (Timer.ClockTicks(encounter, tick) >= Timer.CapTicks(encounter))
            {
                return new RunEndState(Timer.FinishRun(encounter with { Depleted = true }, tick, true, emit), tick, prev.WipeStartTick);
            }
            // The wipe's start is kept while every seat is down (for the host's read); it never ends the run.
            return IsWipe(state, openSeats) ? new RunEndState(null, null, prev.WipeStartTick ?? tick) : RunEndState.Initial;
        }
        if (IsWipe(state, openSeats))
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
