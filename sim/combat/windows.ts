/**
 * Windows and Combos (design/BIBLE.md §4, §5) plus the Lock (§9): opening a Window (target/self or
 * floor-patch), checking Window/Lock/floor-patch expiry, and the single entry point rules.ts calls
 * after an ability's normal effects resolve. Payoff resolution (closing a Window, the self-combo
 * and Lock-alternation denial rules) lives in payoff.ts; the shared low-level Window/Lock state
 * (the synthetic EffectInstance encoding, the floor-patch registry, Lock bookkeeping) lives in
 * window-state.ts — both windows.ts and payoff.ts import from there. See window-state.ts's file
 * docstring for why Windows live as synthetic EffectInstance entries / a closure-scoped registry
 * rather than new SimState fields (sim/combat/types.ts is out of this lane's edit scope).
 *
 * Usage (see sim/combat/rules.ts for the real wiring): after completeCast resolves an ability's
 * targets, call `applyWindowsAndCombos(state, actor, def, wctx, emit)` once; it opens any "window"
 * ops and attempts to close one for any "payoff" op.
 */
import type { ActorState, SimEvent, SimState } from "./types";
import type { AbilityDef, EffectOp } from "./abilities";
import type { EffectDef } from "./effects";
import { pushRawEffectInstance } from "./effects";
import { resolveTargets } from "./targeting";
import { tryClosePayoff, tryClosePayoffOnFloor } from "./payoff";
import type { ComboTable } from "./combos";
import {
  createFloorRegistry,
  incrementLockFailCount,
  LOCK_ENRAGE_THRESHOLD,
  parseWindowEffectId,
  resetLockFailCount,
  windowEffectId,
  type FloorWindowRegistry,
} from "./window-state";

export {
  PERMANENT_TICK,
  FLOOR_WINDOW_RADIUS_MM,
  createFloorRegistry,
  lastSetupClassOf,
  lockFailCount,
  type FloorWindow,
  type FloorWindowRegistry,
} from "./window-state";

type WindowOp = Extract<EffectOp, { op: "window" }>;

export interface WindowsContext {
  effects: Record<string, EffectDef>;
  combos: ComboTable;
  fieldCombos: ComboTable;
  floor: FloorWindowRegistry;
}

export function createWindowsContext(
  effects: Record<string, EffectDef>,
  combos: ComboTable = {},
  fieldCombos: ComboTable = {},
  floor: FloorWindowRegistry = createFloorRegistry(),
): WindowsContext {
  return { effects, combos, fieldCombos, floor };
}

export function openWindow(
  state: SimState,
  setupActor: ActorState,
  op: WindowOp,
  floor: FloorWindowRegistry,
  emit: (e: SimEvent) => void,
  target: ActorState | undefined,
): void {
  const endsTick = state.tick + op.durationTicks;
  const alternate = op.alternate === true;
  if (op.onTarget) {
    const dest = op.self ? setupActor : target;
    if (!dest) return;
    pushRawEffectInstance(dest, {
      effectId: windowEffectId(op.windowId, op.setupClass, alternate),
      sourceId: setupActor.id,
      stacks: 1,
      endsTick,
      nextPeriodicTick: null,
    });
    emit({
      tick: state.tick,
      type: "window_opened",
      windowId: op.windowId,
      onTarget: true,
      setupActorId: setupActor.id,
      setupClass: op.setupClass,
      targetId: dest.id,
      endsTick,
    });
    return;
  }
  floor.patches[op.windowId] = {
    windowId: op.windowId,
    setupClass: op.setupClass,
    setupActorId: setupActor.id,
    pos: { x: setupActor.pos.x, y: setupActor.pos.y, z: setupActor.pos.z },
    endsTick,
  };
  emit({
    tick: state.tick,
    type: "window_opened",
    windowId: op.windowId,
    onTarget: false,
    setupActorId: setupActor.id,
    setupClass: op.setupClass,
    endsTick,
  });
}

/** Called once per actor per tick, before the generic expireEffects sweep (sim/combat/rules.ts),
 * so an expiring Window can emit "window_expired" / advance the Lock fail counter before the
 * generic sweep silently drops its EffectInstance. */
export function checkWindowExpiries(state: SimState, actor: ActorState, emit: (e: SimEvent) => void): void {
  const now = state.tick;
  for (const inst of actor.effects) {
    if (inst.endsTick > now) continue;
    const parsed = parseWindowEffectId(inst.effectId);
    if (!parsed) continue;
    emit({
      tick: now,
      type: "window_expired",
      windowId: parsed.windowId,
      onTarget: true,
      setupActorId: inst.sourceId,
      setupClass: parsed.setupClass,
      targetId: actor.id,
      lock: parsed.alternate,
    });
    if (parsed.alternate) {
      const fails = incrementLockFailCount(actor);
      if (fails >= LOCK_ENRAGE_THRESHOLD) {
        resetLockFailCount(actor);
        emit({ tick: now, type: "lock_enrage", actorId: actor.id });
      }
    }
  }
}

/** Called once per tick (not per actor) to expire floor-patch Windows nobody closed in time. */
export function checkFloorExpiries(state: SimState, floor: FloorWindowRegistry, emit: (e: SimEvent) => void): void {
  const now = state.tick;
  for (const patch of Object.values(floor.patches)) {
    if (patch.endsTick > now) continue;
    delete floor.patches[patch.windowId];
    emit({
      tick: now,
      type: "window_expired",
      windowId: patch.windowId,
      onTarget: false,
      setupActorId: patch.setupActorId,
      setupClass: patch.setupClass,
    });
  }
}

function hasOp(def: AbilityDef, op: "window" | "payoff"): boolean {
  return def.effects.some((e) => e.op === op);
}

/**
 * The single entry point sim/combat/rules.ts calls right after an ability's normal effects have
 * been applied via completeCast: opens any "window" ops on the ability's resolved targets (or the
 * caster itself for `self: true`), then, if the ability carries a "payoff" op, attempts to close a
 * Window on its first resolved target (payoff.ts), falling back to any floor Window the caster is
 * standing in.
 */
export function applyWindowsAndCombos(
  state: SimState,
  actor: ActorState,
  def: AbilityDef,
  ctx: WindowsContext,
  emit: (e: SimEvent) => void,
): void {
  const windowOps = def.effects.filter((e): e is WindowOp => e.op === "window");
  const payoff = hasOp(def, "payoff");
  if (windowOps.length === 0 && !payoff) return;

  const targets = windowOps.length > 0 || payoff ? resolveTargets(state, actor, def) : [];
  for (const op of windowOps) {
    openWindow(state, actor, op, ctx.floor, emit, targets[0]);
  }

  if (!payoff) return;
  const primary = targets[0];
  if (primary) {
    const outcome = tryClosePayoff(state, primary, actor, ctx.combos, ctx.effects, emit);
    if (outcome !== "no_window") return;
  }
  tryClosePayoffOnFloor(state, actor, ctx.floor, ctx.fieldCombos, ctx.effects, emit);
}
