/**
 * Windows and Combos (design/BIBLE.md §4, §5) plus the Lock (§9), layered entirely on top of the
 * existing ActorState.effects: EffectInstance[] field and a small closure-scoped floor-patch
 * registry — sim/combat/types.ts is out of this lane's edit scope, so no new top-level SimState
 * field exists for Windows. A target/self Window is stored as an ordinary EffectInstance whose
 * synthetic effectId carries its payload ("window:<windowId>:<setupClass>", or
 * "lockwindow:<windowId>:<setupClass>" for an `alternate` Lock Window) and whose `sourceId` is the
 * setup actor's id; this rides along in hashState/cloneState for free since it is a real part of
 * ActorState. Lock bookkeeping (`lastSetupClass`, the 3-fail enrage counter) is likewise stored as
 * synthetic, effectively-permanent EffectInstance entries (endsTick = PERMANENT_TICK) on the boss
 * actor. Floor-patch Windows (onTarget: false) have no actor to live on, so they live in a
 * FloorWindowRegistry created once per makeRules() closure (sim/combat/rules.ts) and threaded
 * through every call here explicitly.
 *
 * Usage (see sim/combat/rules.ts for the real wiring): after completeCast resolves an ability's
 * targets, call `applyWindowsAndCombos(state, actor, def, wctx, emit)` once; it opens any "window"
 * ops and attempts to close one for any "payoff" op.
 */
import type { ActorId, ActorState, SimEvent, SimState, Tick, Vec3 } from "./types";
import type { AbilityDef, EffectOp } from "./abilities";
import type { EffectDef } from "./effects";
import { pushRawEffectInstance, removeEffectInstanceWhere } from "./effects";
import { inCircle } from "./targeting";
import { applyComboEffects, lookupCombo, type ComboTable } from "./combos";

export const PERMANENT_TICK: Tick = Number.MAX_SAFE_INTEGER;

/** Default radius (mm) a floor-patch Window covers, measured from the setup actor's position when
 * it was planted. Not part of the "window" EffectOp's fixed shape (design/BIBLE.md §4/§5 content
 * data does not carry a radius on the op); a future content/encounter lane can make this
 * per-ability once shape data for floor abilities is finalised. */
export const FLOOR_WINDOW_RADIUS_MM = 3000;

const WINDOW_PREFIX = "window:";
const LOCK_WINDOW_PREFIX = "lockwindow:";
const LAST_CLASS_PREFIX = "lock:lastclass:";
const LOCK_FAILS_ID = "lock:fails";
const LOCK_ENRAGE_THRESHOLD = 3;

type WindowOp = Extract<EffectOp, { op: "window" }>;

export interface FloorWindow {
  windowId: string;
  setupClass: string;
  setupActorId: ActorId;
  pos: Vec3;
  endsTick: Tick;
}

export interface FloorWindowRegistry {
  patches: Record<string, FloorWindow>;
}

export function createFloorRegistry(): FloorWindowRegistry {
  return { patches: {} };
}

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

function windowEffectId(windowId: string, setupClass: string, alternate: boolean): string {
  return `${alternate ? LOCK_WINDOW_PREFIX : WINDOW_PREFIX}${windowId}:${setupClass}`;
}

interface ParsedWindowId {
  windowId: string;
  setupClass: string;
  alternate: boolean;
}

function parseWindowEffectId(effectId: string): ParsedWindowId | null {
  const alternate = effectId.startsWith(LOCK_WINDOW_PREFIX);
  const plain = effectId.startsWith(WINDOW_PREFIX);
  if (!alternate && !plain) return null;
  const rest = effectId.slice((alternate ? LOCK_WINDOW_PREFIX : WINDOW_PREFIX).length);
  const sep = rest.indexOf(":");
  if (sep < 0) return null;
  return { windowId: rest.slice(0, sep), setupClass: rest.slice(sep + 1), alternate };
}

function isWindowEffectId(effectId: string): boolean {
  return effectId.startsWith(WINDOW_PREFIX) || effectId.startsWith(LOCK_WINDOW_PREFIX);
}

function findOpenWindow(target: ActorState, now: Tick) {
  return target.effects.find((e) => isWindowEffectId(e.effectId) && e.endsTick > now);
}

function readLastSetupClass(actor: ActorState): string | null {
  const inst = actor.effects.find((e) => e.effectId.startsWith(LAST_CLASS_PREFIX));
  return inst ? inst.effectId.slice(LAST_CLASS_PREFIX.length) : null;
}

function setLastSetupClass(actor: ActorState, classId: string): void {
  removeEffectInstanceWhere(actor, (e) => e.effectId.startsWith(LAST_CLASS_PREFIX));
  pushRawEffectInstance(actor, {
    effectId: LAST_CLASS_PREFIX + classId,
    sourceId: actor.id,
    stacks: 1,
    endsTick: PERMANENT_TICK,
    nextPeriodicTick: null,
  });
}

function incrementLockFailCount(actor: ActorState): number {
  const inst = actor.effects.find((e) => e.effectId === LOCK_FAILS_ID);
  if (inst) {
    inst.stacks += 1;
    return inst.stacks;
  }
  pushRawEffectInstance(actor, {
    effectId: LOCK_FAILS_ID,
    sourceId: actor.id,
    stacks: 1,
    endsTick: PERMANENT_TICK,
    nextPeriodicTick: null,
  });
  return 1;
}

function resetLockFailCount(actor: ActorState): void {
  removeEffectInstanceWhere(actor, (e) => e.effectId === LOCK_FAILS_ID);
}

export function lockFailCount(actor: ActorState): number {
  return actor.effects.find((e) => e.effectId === LOCK_FAILS_ID)?.stacks ?? 0;
}

export function lastSetupClassOf(actor: ActorState): string | null {
  return readLastSetupClass(actor);
}

/** Mirrors casting.ts's private resolveTargets (unexported, so duplicated here) so windows.ts can
 * resolve the same targets an ability's own effects were just applied to. */
export function resolveAbilityTargets(state: SimState, actor: ActorState, def: AbilityDef): ActorState[] {
  const all = Object.values(state.actors);
  if (def.shape.kind === "self") return [actor];
  if (def.shape.kind === "target") {
    const t = actor.targetId !== null ? state.actors[actor.targetId] : undefined;
    return t ? [t] : [];
  }
  if (def.shape.kind === "circle") {
    return inCircle(actor.pos, def.shape.radius ?? 0, all)
      .map((id) => state.actors[id])
      .filter((a): a is ActorState => !!a);
  }
  // cone/line shapes are not used by any Setup/Payoff ability in the starter kits; treat them as
  // "no resolved targets" here rather than duplicating inCone/inLine's facing math for a case
  // Windows and Combos does not need yet.
  return [];
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

export type PayoffOutcome = "closed" | "denied" | "no_window";

/** Attempts to close a target/self (including Lock) Window carried by `target` via a Payoff from
 * `payoffActor`. Same-class Payoffs (or, for a Lock/alternate Window, a Payoff from whichever
 * class closed this actor's last alternate Window) are denied and emit "combo_denied" without
 * consuming the Window — any other class may still close it before it expires. */
export function tryClosePayoff(
  state: SimState,
  target: ActorState,
  payoffActor: ActorState,
  table: ComboTable,
  effectDefs: Record<string, EffectDef>,
  emit: (e: SimEvent) => void,
): PayoffOutcome {
  const now = state.tick;
  const inst = findOpenWindow(target, now);
  if (!inst) return "no_window";
  const parsed = parseWindowEffectId(inst.effectId);
  if (!parsed) return "no_window";

  const deniedClass = parsed.alternate ? readLastSetupClass(target) : parsed.setupClass;
  if (deniedClass !== null && payoffActor.classId === deniedClass) {
    emit({
      tick: now,
      type: "combo_denied",
      windowId: parsed.windowId,
      targetId: target.id,
      setupActorId: inst.sourceId,
      payoffActorId: payoffActor.id,
      reason: parsed.alternate ? "lock_alternate" : "self_combo",
    });
    return "denied";
  }

  const combo = lookupCombo(table, parsed.setupClass, payoffActor.classId);
  if (!combo) return "no_window";

  removeEffectInstanceWhere(target, (e) => e === inst);
  if (parsed.alternate) {
    setLastSetupClass(target, payoffActor.classId);
    resetLockFailCount(target);
  }
  applyComboEffects(state, payoffActor, target, combo.effects, effectDefs, emit);
  emit({
    tick: now,
    type: "combo",
    comboId: combo.id,
    windowId: parsed.windowId,
    setupActorId: inst.sourceId,
    setupClass: parsed.setupClass,
    payoffActorId: payoffActor.id,
    payoffClass: payoffActor.classId,
    targetId: target.id,
  });
  return "closed";
}

/** Attempts to close any floor-patch Window `payoffActor` is currently standing inside, using the
 * "field" (smaller) ComboTable. Floor Windows accept any other class's Payoff; there is no
 * per-window setup-actor exclusion beyond the universal self-combo rule. */
export function tryClosePayoffOnFloor(
  state: SimState,
  payoffActor: ActorState,
  floor: FloorWindowRegistry,
  fieldTable: ComboTable,
  effectDefs: Record<string, EffectDef>,
  emit: (e: SimEvent) => void,
): PayoffOutcome {
  const now = state.tick;
  for (const patch of Object.values(floor.patches)) {
    if (patch.endsTick <= now) continue;
    const inRange = inCircle(patch.pos, FLOOR_WINDOW_RADIUS_MM, [payoffActor]).length > 0;
    if (!inRange) continue;
    if (payoffActor.classId === patch.setupClass) {
      emit({
        tick: now,
        type: "combo_denied",
        windowId: patch.windowId,
        setupActorId: patch.setupActorId,
        payoffActorId: payoffActor.id,
        reason: "self_combo",
      });
      return "denied";
    }
    const combo = lookupCombo(fieldTable, patch.setupClass, payoffActor.classId);
    if (!combo) continue;
    delete floor.patches[patch.windowId];
    applyComboEffects(state, payoffActor, payoffActor, combo.effects, effectDefs, emit);
    emit({
      tick: now,
      type: "combo",
      comboId: combo.id,
      windowId: patch.windowId,
      setupActorId: patch.setupActorId,
      setupClass: patch.setupClass,
      payoffActorId: payoffActor.id,
      payoffClass: payoffActor.classId,
      field: true,
    });
    return "closed";
  }
  return "no_window";
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
 * Window on its first resolved target, falling back to any floor Window the caster is standing in.
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

  const targets = windowOps.length > 0 || payoff ? resolveAbilityTargets(state, actor, def) : [];
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
