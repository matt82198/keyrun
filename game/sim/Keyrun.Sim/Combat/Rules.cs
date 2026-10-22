using System;
using System.Collections.Generic;
using System.Linq;
using Keyrun.Sim.Core;

namespace Keyrun.Sim.Combat;

/// <summary>
/// Everything one run's combat rules read (sim/combat/rules.ts:178-191 RulesConfig), plus the run's Clause. The
/// movement fields are the TS ones; a host builds them from its <see cref="MovementConfig"/>
/// (<see cref="RulesConfig.WithMovement"/>).
/// </summary>
public sealed record RulesConfig
{
    /// <summary>Abilities by id.</summary>
    public required IReadOnlyDictionary<string, AbilityDef> Abilities { get; init; }

    /// <summary>Effect definitions by id.</summary>
    public required IReadOnlyDictionary<string, EffectDef> Effects { get; init; }

    /// <summary>School lockout length after an interrupt, ticks.</summary>
    public required int LockoutTicks { get; init; }

    /// <summary>Walk speed for an actor without its own, mm/tick.</summary>
    public required long BaseSpeedMmPerTick { get; init; }

    /// <summary>Actor radius for push-out, mm.</summary>
    public required long ActorRadiusMm { get; init; }

    /// <summary>Arena bounds.</summary>
    public required Bounds Bounds { get; init; }

    /// <summary>Burst ComboTable (target/self and Lock Windows), or null for none.</summary>
    public ComboTable? Combos { get; init; }

    /// <summary>Field ComboTable (floor Windows), or null for none.</summary>
    public ComboTable? FieldCombos { get; init; }

    /// <summary>
    /// The run's active Clause, or null. The TS installs it per tick around stepRun (clause-hooks.ts
    /// withEncounterRules); here it is part of the run's rules.
    /// </summary>
    public ICombatClause? Clause { get; init; }

    /// <summary>
    /// Class id to its Heat/Overheat rule (BIBLE section 5 Cinder), or null for none. Null on every oracle run (the TS
    /// has no Overheat); a host builds it from content (the heat-overheat class loop max and run.json heatOverheat).
    /// </summary>
    public IReadOnlyDictionary<string, OverheatRule>? Overheat { get; init; }

    /// <summary>
    /// The Pair's spread and stack mark rules (BIBLE section 9.6 item 2; <see cref="PairMarks"/>), or null for none.
    /// Null on every oracle run (the frozen TS marks carry no consequence); a host builds it from
    /// game/content/data/run.json <c>pairMarks</c>.
    /// </summary>
    public MarkRules? Marks { get; init; }

    /// <summary>
    /// L5 caster-los (R-SYNERGY-2): the sight predicate (caster position, target position) an enemy target-shaped cast
    /// must pass, or null for none. Null on every oracle run; a host builds it from the layout's obstacles.
    /// </summary>
    public Func<Vec3, Vec3, bool>? Sight { get; init; }

    /// <summary>
    /// The frozen TS push-out (sim/combat: every overlapping pair parts symmetrically, so an enemy can shove a player).
    /// True only on an oracle replay (Keyrun.Replay RunSetup), so the traces keep checking the TS rules tick for tick;
    /// false (the default) everywhere else, where STATE.md D54 ENEMIES-NEVER-DISPLACE holds.
    /// </summary>
    public bool TsSymmetricPushOut { get; init; }

    /// <summary>
    /// TEMPO-LOOP-CLAMP (f-57c04b4a): an enemy cast refused <c>not_ready</c> emits <c>cast_refused</c> (reason
    /// not_ready), as an out-of-range one always does. False (the default) on a frozen-TS replay, whose recorded traces
    /// hold the TS's not_ready refusals unreported; true on every run whose enemy scripts run the C# scheduler.
    /// </summary>
    public bool ReportsEnemyNotReady { get; init; }

    /// <summary>This config with its movement fields taken from <paramref name="movement"/>.</summary>
    /// <param name="movement">The host's validated movement rules.</param>
    /// <returns>A new config.</returns>
    public RulesConfig WithMovement(MovementConfig movement)
    {
        ArgumentNullException.ThrowIfNull(movement);
        return this with { BaseSpeedMmPerTick = movement.BaseSpeedMmPerTick, ActorRadiusMm = movement.ActorRadiusMm, Bounds = movement.Bounds };
    }
}

/// <summary>
/// Port of sim/combat/rules.ts: one 20 Hz tick of combat, in the fixed TS order.
/// <list type="number">
/// <item>Complete every cast whose castEndsTick is this tick (actors ascending), then its Windows and Payoff.</item>
/// <item>Run the tick's commands in order: target, stopcast, move/dash, cast (an instant cast completes now, then
/// its Windows and Payoff). A held spell-queue press whose lock is over joins them as its actor's cast command
/// (<see cref="SpellQueue"/>). A dead actor's commands are dropped except cast (the onDeath DEATH-CAST EXCEPTION).</item>
/// <item>Push live actors apart, then clamp every actor to the bounds.</item>
/// <item>Per actor: due periodic ticks, Window expiries, then the generic effect expiry; then floor expiries, then
/// the C#-only combo clouds (<see cref="Combos.TickClouds"/>; none on an oracle run).</item>
/// </list>
/// The rules never advance state.Tick; <see cref="Core.Tick.Step"/> owns the tick.
/// </summary>
public static class Rules
{
    /// <summary>
    /// rules.ts:193-278 makeRules. Fails closed first: any ability or periodic op of a kind the sim does not run
    /// (or a combo op naming an unknown table) throws, listing every offender. Each invocation builds its
    /// contexts fresh from the state, so the floor registry is <c>state.FloorWindows</c> itself and the Clause's
    /// combo flag starts clear.
    /// </summary>
    /// <param name="cfg">The run's rules.</param>
    /// <returns>The step rules.</returns>
    /// <exception cref="ArgumentException">An op the sim does not run.</exception>
    public static StepRules Make(RulesConfig cfg)
    {
        ArgumentNullException.ThrowIfNull(cfg);
        var errors = new List<string>();
        foreach (var kv in cfg.Abilities)
        {
            foreach (var op in kv.Value.Effects)
            {
                errors.AddRange(Abilities.OpKindErrors(op).Select(e => kv.Key + ": " + e));
            }
            foreach (var variant in kv.Value.Variants ?? new Dictionary<string, IReadOnlyList<EffectOp>>())
            {
                foreach (var op in variant.Value)
                {
                    errors.AddRange(Abilities.OpKindErrors(op).Select(e => kv.Key + " variant " + variant.Key + ": " + e));
                }
            }
        }
        foreach (var kv in cfg.Effects)
        {
            if (kv.Value.Periodic is not null)
            {
                errors.AddRange(Abilities.OpKindErrors(kv.Value.Periodic.Op).Select(e => "effect " + kv.Key + " periodic: " + e));
            }
        }
        if (errors.Count > 0)
        {
            throw new ArgumentException("rules: " + string.Join("; ", errors), nameof(cfg));
        }
        var combos = cfg.Combos ?? new ComboTable();
        var fieldCombos = cfg.FieldCombos ?? new ComboTable();

        return (state, commands, emit) =>
        {
            var hooks = new ClauseHooks(cfg.Clause);
            var noClause = new ClauseHooks(null);
            var ctx = new CastContext(cfg.Abilities, cfg.Effects, cfg.LockoutTicks) { Hooks = hooks, Overheat = cfg.Overheat, Marks = cfg.Marks, Sight = cfg.Sight };
            var wctx = new WindowsContext(cfg.Effects, combos, fieldCombos, new FloorWindowRegistry(state.FloorWindows), hooks) { Overheat = cfg.Overheat };

            foreach (var actor in state.Actors.Values.ToList())
            {
                if (actor.CastId is not null && actor.CastEndsTick == state.Tick)
                {
                    // The verse variant is resolved before CompleteCast credits the cast's own resource ops (a swap).
                    var def = cfg.Abilities.TryGetValue(actor.CastId, out var found) ? Abilities.ResolveVerse(found, actor) : null;
                    // R2: CompleteCast ends the cast whole (pin included) and hands it back; CAST-KEEPS-START-TARGET,
                    // its Windows resolve on the ended cast's start target, as its effects did.
                    var ended = Casting.CompleteCast(state, actor, ctx, emit);
                    if (def is not null)
                    {
                        Windows.ApplyWindowsAndCombos(state, actor, def, ended, wctx, emit);
                    }
                }
            }

            // The spell queue window: a held press whose cast/GCD lock is over runs as its actor's cast command, in the
            // fixed order (Combat/SpellQueue.cs).
            var fired = new HashSet<Command>(ReferenceEqualityComparer.Instance);
            foreach (var cmd in SpellQueue.WithFiring(state, commands, cfg.Abilities, fired))
            {
                if (!state.Actors.TryGetValue(cmd.ActorId, out var actor))
                {
                    continue;
                }
                if (!actor.Alive && cmd is not CastCommand)
                {
                    continue;
                }
                switch (cmd)
                {
                    case TargetCommand t:
                        // CAST-KEEPS-START-TARGET (f-bd8fbbb1): a mid-cast retarget is for the next press.
                        Casting.SetTarget(actor, t.TargetId);
                        break;
                    case StopCastCommand:
                        Casting.Stopcast(actor, emit, state.Tick);
                        break;
                    case MoveCommand m:
                        Movement.ApplyMovement(actor, m.Dir, actor.SpeedMmPerTick ?? cfg.BaseSpeedMmPerTick, Effects.AggregateModifiers(actor, cfg.Effects).MovementBp, m.Yaw);
                        break;
                    case DashCommand d:
                        Movement.ApplyMovement(actor, d.Dir, Movement.DashDistanceMm, Effects.AggregateModifiers(actor, cfg.Effects).MovementBp, d.Yaw);
                        break;
                    case CastCommand c:
                        if (!cfg.Abilities.TryGetValue(c.AbilityId, out var def))
                        {
                            break;
                        }
                        // Hush silences the party only (BIBLE s8 "save the kicks"; clauses.json "hits the party"):
                        // an enemy's cast, scripted or not, never sees the silence predicate. Enemies get a
                        // no-Clause hooks object, not null, so StartCast's `hooks ??= ctx.Hooks` (the Overheat
                        // context) cannot hand the silence back.
                        var castHooks = actor.Kind == ActorKind.Player ? hooks : noClause;
                        var err = Casting.StartCast(state, actor, def, c.TargetId, c.Point, state.Tick, castHooks, ctx);
                        SpellQueue.AfterPress(actor, def, c, err, fired.Contains(c), state.Tick);
                        // CAST-REFUSED (f-fa598b6b): an enemy cast refused out of range never starts. Its command is
                        // encounter-generated, so it is in no trace record; this event is how a replay without combat
                        // (EncounterCastLedger) learns the caster stayed free. Players' presses are in the record, and
                        // other reasons are not reported. Mirrors sim/combat/rules.ts.
                        // TEMPO-LOOP-CLAMP (f-57c04b4a): off a frozen-TS replay (ReportsEnemyNotReady), an enemy cast
                        // refused not_ready is reported too. The script scheduler never fires a cast before its cooldown,
                        // so such a refusal is a scheduling bug, and tests assert shipped content has none.
                        if (actor.Kind == ActorKind.Enemy && (err == "out_of_range" || (err == "not_ready" && cfg.ReportsEnemyNotReady)))
                        {
                            emit(new SimEvent(state.Tick, "cast_refused", ("actorId", JsValue.Int(actor.Id)), ("abilityId", JsValue.Str(def.Id)), ("reason", JsValue.Str(err))));
                        }
                        // L5 caster-los: a cast withheld for sight never started; Chase walks the caster until the ray is clear.
                        if (actor.Kind == ActorKind.Enemy && err == Casting.NoSightError)
                        {
                            emit(new SimEvent(state.Tick, "cast_los_withheld", ("actorId", JsValue.Int(actor.Id)), ("abilityId", JsValue.Str(def.Id)), ("targetId", JsValue.Int(c.TargetId ?? actor.TargetId ?? 0))));
                        }
                        if (err is null && def.CastTicks == 0)
                        {
                            var resolved = Abilities.ResolveVerse(def, actor);
                            var ended = Casting.CompleteCast(state, actor, ctx, emit);
                            Windows.ApplyWindowsAndCombos(state, actor, resolved, ended, wctx, emit);
                        }
                        break;
                }
            }
            // BIBLE 9.3 stops are not resolved here: the run root ends a stopped cast after combat (Runs.ControlStops,
            // STUN-STOPS-CAST #329 and the f-c63b43bf ruling), so oracle runs, which set no stop effects, are untouched.

            var actors = state.Actors.Values.ToList();
            for (var i = 0; i < actors.Count; i++)
            {
                for (var j = i + 1; j < actors.Count; j++)
                {
                    // PRESPAWN-AGGRO (C#-only, BIBLE section 9.1 "an idle group ... does not move unless it is a patrol"):
                    // push-out never shoves an idle body, so an idle group stands exactly where it was placed until it
                    // engages; a stack the content puts on one spawn point is placed as a non-overlapping row at spawn
                    // (Pulls.SpawnAllIdle), so it never needs a shove. A party member
                    // close enough to touch an idle body is, under the 4000 mm default ring, inside its aggro ring and engages it this tick. No actor of
                    // an oracle run is idle, so the TS push-out is unchanged there.
                    if (actors[i].Alive && actors[j].Alive && !actors[i].Idle && !actors[j].Idle)
                    {
                        // D54 ENEMIES-NEVER-DISPLACE (defer to WoW; f-e17638f7): an enemy never pushes a player. It
                        // closes to melee and stacks on its target, so enemy-versus-player separation moves the enemy by
                        // the whole overlap and the player stays. Enemy-enemy and player-player stay symmetric, so a
                        // pack parts around its target. This supersedes the TS port's symmetric rule (sim/combat).
                        if (cfg.TsSymmetricPushOut)
                        {
                            Movement.ResolveCollision(actors[i], actors[j], cfg.ActorRadiusMm);
                        }
                        else if (actors[i].Kind == ActorKind.Player && actors[j].Kind == ActorKind.Enemy)
                        {
                            Movement.ResolveCollisionMoverOnly(actors[i], actors[j], cfg.ActorRadiusMm);
                        }
                        else if (actors[i].Kind == ActorKind.Enemy && actors[j].Kind == ActorKind.Player)
                        {
                            Movement.ResolveCollisionMoverOnly(actors[j], actors[i], cfg.ActorRadiusMm);
                        }
                        else if (actors[i].Kind == ActorKind.Enemy && actors[j].Kind == ActorKind.Enemy &&
                            actors[i].TargetId is { } ti && ti == actors[j].TargetId &&
                            state.Actors.TryGetValue(ti, out var shared) && shared.Alive && shared.Kind == ActorKind.Player)
                        {
                            // D54: two enemies on one player part and the blocked one slides around him (a pack fans).
                            Movement.ResolveCollisionAround(actors[i], actors[j], shared.Pos, cfg.ActorRadiusMm);
                        }
                        else
                        {
                            Movement.ResolveCollision(actors[i], actors[j], cfg.ActorRadiusMm);
                        }
                    }
                }
            }
            foreach (var actor in actors)
            {
                Movement.ClampToBounds(actor, cfg.Bounds);
            }

            // C#-only (null on every oracle run): the Pair's spread and stack marks resolve on the tick they run out,
            // after movement and before the generic expiry below drops them.
            PairMarks.Resolve(state, cfg.Marks, cfg.Effects, emit, hooks.Weighting);

            foreach (var actor in actors)
            {
                foreach (var inst in Effects.DuePeriodicInstances(actor, state.Tick))
                {
                    if (cfg.Effects.TryGetValue(inst.EffectId, out var def) && def.Periodic is not null)
                    {
                        RunPeriodicOp(state, actor, inst.SourceId, def.Periodic.Op, cfg.Effects, emit, hooks);
                        Effects.AdvancePeriodic(inst, def, state.Tick);
                    }
                }
                Windows.CheckWindowExpiries(state, actor, emit);
                Effects.ExpireEffects(actor, state.Tick);
            }
            Windows.CheckFloorExpiries(state, wctx.Floor, emit);
            Combos.TickClouds(state, cfg.Effects);
        };
    }

    /// <summary>
    /// rules.ts:280-299 runPeriodicOp: damage (the source's damageDoneBp only while it still exists), heal, resource
    /// on the carrier (clamped), threat. Other ops do nothing.
    /// </summary>
    private static void RunPeriodicOp(
        SimState state,
        ActorState target,
        int sourceId,
        EffectOp op,
        IReadOnlyDictionary<string, EffectDef> effectDefs,
        Action<SimEvent> emit,
        ClauseHooks hooks)
    {
        switch (op)
        {
            case DamageOp d:
                state.Actors.TryGetValue(sourceId, out var source);
                Mitigation.DealDamage(state.Tick, source, target, d.Amount, effectDefs, emit, sourceId, hooks.Weighting, d.School);
                break;
            case DamagePctOp p:
                // BIBLE 9.3 Searing Hex: a share of the carrier's max health per tick (C#-only, no oracle content).
                state.Actors.TryGetValue(sourceId, out var pctSource);
                Mitigation.DealDamage(state.Tick, pctSource, target, Effects.PctOfMaxHealth(target, p.MaxHealthBp), effectDefs, emit, sourceId, hooks.Weighting, p.School);
                break;
            case HealOp h:
                Damage.ApplyHeal(state.Tick, target, h.Amount, sourceId, emit);
                break;
            case ResourceOp r:
                target.Resource = Math.Max(0, Math.Min(target.MaxResource, target.Resource + r.Delta));
                break;
            case ThreatOp t:
                Threat.AddThreat(target, sourceId, t.Amount ?? 0, t.MultiplierBp);
                break;
        }
    }
}
