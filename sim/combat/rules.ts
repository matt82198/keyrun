/**
 * Composes actors/abilities/casting/cooldowns/effects/threat/damage/movement/targeting into a
 * single StepRules function for one 20 Hz tick. Fixed per-tick order:
 *   1. Complete any cast whose castEndsTick === state.tick (natural completion).
 *   2. Process this tick's commands in array order: target, stopcast, move, cast (instant
 *      abilities, castTicks === 0, complete immediately within this same step).
 *   3. Resolve actor-actor collisions and clamp to arena bounds.
 *   4. Run due periodic effect ticks, then expire effects whose endsTick <= state.tick.
 * This function never advances state.tick itself -- sim/core/tick.ts's step() is the single owner
 * of the tick (per sim/CLAUDE.md); a caller that drives this StepRules directly (bypassing step())
 * must advance state.tick itself between calls, exactly as step() does.
 * This fixed order is what makes an interrupt landing exactly at a target's castEndsTick
 * arrive "too late" (step 1 already completed it), while an interrupt effect that lands on an
 * earlier tick has already cleared the cast before that later tick is ever reached. See
 * casting.ts for the full interrupt-resolution contract.
 *
 * Windows and Combos (design/BIBLE.md §4, §5, §9's Lock) hook in right after each of those two
 * completeCast call sites: applyWindowsAndCombos (windows.ts) re-resolves the same ability's
 * targets to open any "window" ops and attempt to close one for a "payoff" op. Window/Lock
 * expiry is checked per actor (checkWindowExpiries) immediately before the existing generic
 * expireEffects sweep in step 4, and floor-patch Windows are checked once per tick
 * (checkFloorExpiries) alongside it — see windows.ts's file docstring for why target Windows live
 * as synthetic EffectInstance entries. Floor-patch Windows (onTarget:false) live in
 * `state.floorWindows` (sim/core/state.ts's additive `FloorWindow` field) rather than a value
 * closed over at makeRules-build-time: the WindowsContext built below is constructed fresh from
 * `state` on every StepRules invocation (not once per makeRules call) specifically so a floor
 * Window survives `stepRun` rebuilding `makeRules` on every call (sim/index.ts) — see
 * window-state.ts's file docstring for the full reasoning.
 *
 * Usage: const rules: StepRules = makeRules(cfg); rules(state, commandsForThisTick, emit);
 */
import type { ActorState, Command, SimEvent, SimState, StepRules } from "./types";
import type { AbilityDef, EffectOp } from "./abilities";
import type { EffectDef } from "./effects";
import { startCast, completeCast, stopcast, type CastContext } from "./casting";
import { applyMovement, resolveCollision, clampToBounds, DASH_DISTANCE_MM, type Bounds } from "./movement";
import { expireEffects, duePeriodicInstances, advancePeriodic, aggregateModifiers } from "./effects";
import { applyDamage, applyHeal } from "./damage";
import { addThreat } from "./threat";
import type { ComboTable } from "./combos";
import {
  applyWindowsAndCombos,
  checkFloorExpiries,
  checkWindowExpiries,
  type WindowsContext,
} from "./windows";
import { configureAffinityEffects } from "./affinity";

export interface RulesConfig {
  abilities: Record<string, AbilityDef>;
  effects: Record<string, EffectDef>;
  lockoutTicks: number;
  baseSpeedMmPerTick: number;
  actorRadiusMm: number;
  bounds: Bounds;
  /** [setupClass][payoffClass] -> ComboDef for full ("burst") combos, keyed as described in
   * combos.ts; also where the Lock's boss pseudo-class row lives. Omit for content that does not
   * use Windows and Combos yet — window/payoff ops then simply find no combo and no-op. */
  combos?: ComboTable;
  /** Same shape as `combos`, for the smaller "field" combos floor-patch Windows fire. */
  fieldCombos?: ComboTable;
}

export function makeRules(cfg: RulesConfig): StepRules {
  // The affinity registry (sim/combat/affinity.ts) is module-level and process-global between calls.
  // configureAffinityEffects(cfg.effects) runs at the top of EVERY invocation of the returned closure,
  // not once at makeRules-build time, so the registry is re-pointed to this run's effects before each
  // step resolves targets. Held closures from different runs are therefore safe to step interleaved.
  // resolveTargets has no effects map in scope and SimState carries none (sim/CLAUDE.md). See
  // affinity.ts's file docstring for the full contract (tests/sim/combat/rules-affinity-per-run.test.ts).
  const ctx: CastContext = { abilities: cfg.abilities, effects: cfg.effects, lockoutTicks: cfg.lockoutTicks };

  return (state: SimState, commands: Command[], emit: (e: SimEvent) => void) => {
    configureAffinityEffects(cfg.effects);
    // Built fresh from `state` every call (not hoisted above, once per makeRules call): floor.patches
    // is the SAME object as state.floorWindows, so mutations below persist on `state` itself and
    // survive both cloneState and stepRun rebuilding makeRules every tick (see this file's and
    // window-state.ts's docstrings). Lazily initialised for hand-built SimState fixtures (tests)
    // that predate the field and never set it.
    if (!state.floorWindows) state.floorWindows = {};
    const wctx: WindowsContext = {
      effects: cfg.effects,
      combos: cfg.combos ?? {},
      fieldCombos: cfg.fieldCombos ?? {},
      floor: { patches: state.floorWindows },
    };

    for (const actor of Object.values(state.actors)) {
      if (actor.castId !== null && actor.castEndsTick === state.tick) {
        const def = cfg.abilities[actor.castId];
        completeCast(state, actor, ctx, emit);
        if (def) applyWindowsAndCombos(state, actor, def, wctx, emit);
      }
    }

    for (const cmd of commands) {
      const actor = state.actors[cmd.actorId];
      if (!actor) continue;
      // DEATH-CAST EXCEPTION (death-trigger-wiring): sim/encounter/scripts.ts's "onDeath" script
      // step submits its cast the tick AFTER ActorState.alive flips false (its documented
      // ONE-TICK LAG), so a dead enemy's own onDeath ability (e.g. seed.deathBloom) can still
      // resolve through the ordinary cast pipeline instead of being silently dropped here. Every
      // other command kind for a dead actor is still dropped, exactly as before.
      if (!actor.alive && cmd.kind !== "cast") continue;
      if (cmd.kind === "target") {
        actor.targetId = cmd.targetId;
      } else if (cmd.kind === "stopcast") {
        stopcast(actor, emit, state.tick);
      } else if (cmd.kind === "move" || cmd.kind === "dash") {
        const mods = aggregateModifiers(actor, cfg.effects);
        // Walk speed is the ACTOR's (content-set at spawn, e.g. a faster melee chaser), never the
        // command's: commands are untrusted input and validateCommand rejects a speed field.
        const distance = cmd.kind === "dash" ? DASH_DISTANCE_MM : (actor.speedMmPerTick ?? cfg.baseSpeedMmPerTick);
        applyMovement(actor, cmd.dir, distance, mods.movementBp, cmd.yaw);
      } else if (cmd.kind === "cast") {
        const def = cfg.abilities[cmd.abilityId];
        if (!def) continue;
        const err = startCast(state, actor, def, cmd.targetId, cmd.point, state.tick);
        if (!err && def.castTicks === 0) {
          completeCast(state, actor, ctx, emit);
          applyWindowsAndCombos(state, actor, def, wctx, emit);
        }
      }
    }

    const actors = Object.values(state.actors);
    for (let i = 0; i < actors.length; i += 1) {
      for (let j = i + 1; j < actors.length; j += 1) {
        const a = actors[i]!;
        const b = actors[j]!;
        if (a.alive && b.alive) resolveCollision(a, b, cfg.actorRadiusMm);
      }
    }
    for (const actor of actors) clampToBounds(actor, cfg.bounds);

    for (const actor of actors) {
      for (const inst of duePeriodicInstances(actor, state.tick)) {
        const def = cfg.effects[inst.effectId];
        if (def?.periodic) {
          runPeriodicOp(state, actor, inst.sourceId, def.periodic.op, cfg.effects, emit);
          advancePeriodic(inst, def, state.tick);
        }
      }
      checkWindowExpiries(state, actor, emit);
      expireEffects(actor, state.tick);
    }
    checkFloorExpiries(state, wctx.floor, emit);
  };
}

function runPeriodicOp(
  state: SimState,
  target: ActorState,
  sourceId: number,
  op: EffectOp,
  effectDefs: Record<string, EffectDef>,
  emit: (e: SimEvent) => void,
): void {
  if (op.op === "damage") {
    applyDamage(state, target, op.amount, 0, 0, effectDefs, emit, sourceId);
  } else if (op.op === "heal") {
    applyHeal(state, target, op.amount, sourceId, emit);
  } else if (op.op === "resource") {
    target.resource = Math.max(0, Math.min(target.maxResource, target.resource + op.delta));
  } else if (op.op === "threat") {
    addThreat(target, sourceId, op.amount ?? 0, op.multiplierBp);
  }
}
