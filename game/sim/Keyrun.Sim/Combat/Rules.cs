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
/// its Windows and Payoff). A dead actor's commands are dropped except cast (the onDeath DEATH-CAST EXCEPTION).</item>
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
            var ctx = new CastContext(cfg.Abilities, cfg.Effects, cfg.LockoutTicks) { Hooks = hooks, Overheat = cfg.Overheat };
            var wctx = new WindowsContext(cfg.Effects, combos, fieldCombos, new FloorWindowRegistry(state.FloorWindows), hooks);

            foreach (var actor in state.Actors.Values.ToList())
            {
                if (actor.CastId is not null && actor.CastEndsTick == state.Tick)
                {
                    // The verse variant is resolved before CompleteCast credits the cast's own resource ops (a swap).
                    var def = cfg.Abilities.TryGetValue(actor.CastId, out var found) ? Abilities.ResolveVerse(found, actor) : null;
                    Casting.CompleteCast(state, actor, ctx, emit);
                    if (def is not null)
                    {
                        Windows.ApplyWindowsAndCombos(state, actor, def, wctx, emit);
                    }
                }
            }

            foreach (var cmd in commands)
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
                        actor.TargetId = t.TargetId;
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
                        if (err is null && def.CastTicks == 0)
                        {
                            var resolved = Abilities.ResolveVerse(def, actor);
                            Casting.CompleteCast(state, actor, ctx, emit);
                            Windows.ApplyWindowsAndCombos(state, actor, resolved, wctx, emit);
                        }
                        break;
                }
            }

            var actors = state.Actors.Values.ToList();
            for (var i = 0; i < actors.Count; i++)
            {
                for (var j = i + 1; j < actors.Count; j++)
                {
                    // PRESPAWN-AGGRO (C#-only, BIBLE section 9.1 "an idle group ... does not move unless it is a patrol"):
                    // push-out never shoves an idle body, so a stack the content puts on one spawn point stands still
                    // until its group engages and spreads on that tick, as a pull spawned on entry did. A party member
                    // close enough to touch an idle body is, under the 4000 mm default ring, inside its aggro ring and engages it this tick. No actor of
                    // an oracle run is idle, so the TS push-out is unchanged there.
                    if (actors[i].Alive && actors[j].Alive && !actors[i].Idle && !actors[j].Idle)
                    {
                        Movement.ResolveCollision(actors[i], actors[j], cfg.ActorRadiusMm);
                    }
                }
            }
            foreach (var actor in actors)
            {
                Movement.ClampToBounds(actor, cfg.Bounds);
            }

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
                Mitigation.DealDamage(state.Tick, source, target, d.Amount, effectDefs, emit, sourceId, hooks.Weighting);
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
