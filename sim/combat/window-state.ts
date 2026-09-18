/**
 * Windows and Combos (design/BIBLE.md §4, §5) plus the Lock (§9) — shared low-level state this
 * lane's two other files (windows.ts for open/expiry, payoff.ts for payoff resolution) both need:
 * the synthetic EffectInstance encoding a Window rides in on (effectId = "window:<windowId>:
 * <setupClass>", or "lockwindow:<windowId>:<setupClass>" for an `alternate` Lock Window; `sourceId`
 * is the setup actor's id), the floor-patch registry a Window with `onTarget: false` lives in
 * (threaded explicitly through calls rather than a new SimState field — sim/combat/types.ts is out
 * of this lane's edit scope), and the Lock bookkeeping (`lastSetupClass`, the 3-fail enrage
 * counter) stored the same synthetic way, effectively-permanent (endsTick = PERMANENT_TICK) entries
 * on the boss actor.
 *
 * Usage: windows.ts imports these to open/parse/expire Windows; payoff.ts imports them to find and
 * close a Window and to read/update Lock alternation state. Neither of those two files imports from
 * the other.
 */
import type { ActorId, ActorState, Tick, Vec3 } from "./types";
import { pushRawEffectInstance, removeEffectInstanceWhere } from "./effects";

export const PERMANENT_TICK: Tick = Number.MAX_SAFE_INTEGER;

/** Default radius (mm) a floor-patch Window covers, measured from the setup actor's position when
 * it was planted. Not part of the "window" EffectOp's fixed shape (design/BIBLE.md §4/§5 content
 * data does not carry a radius on the op); a future content/encounter lane can make this
 * per-ability once shape data for floor abilities is finalised. */
export const FLOOR_WINDOW_RADIUS_MM = 3000;

export const WINDOW_PREFIX = "window:";
export const LOCK_WINDOW_PREFIX = "lockwindow:";
const LAST_CLASS_PREFIX = "lock:lastclass:";
const LOCK_FAILS_ID = "lock:fails";
export const LOCK_ENRAGE_THRESHOLD = 3;

export interface FloorWindow {
  windowId: string;
  setupClass: string;
  setupActorId: ActorId;
  pos: Vec3;
  endsTick: Tick;
  /** Catch radius (mm) a Payoff must stand within to close this patch; content-set per ability via
   * the "window" op's `radiusMm`, falling back to `FLOOR_WINDOW_RADIUS_MM` when omitted. Always
   * populated by windows.ts's `openWindow`. */
  radiusMm: number;
}

export interface FloorWindowRegistry {
  patches: Record<string, FloorWindow>;
}

export function createFloorRegistry(): FloorWindowRegistry {
  return { patches: {} };
}

/** Builds a FloorWindow patch, resolving `radiusMm` against the default. Split out of windows.ts's
 * openWindow for the LOC cap. */
export function makeFloorWindow(
  windowId: string,
  setupClass: string,
  setupActorId: ActorId,
  pos: Vec3,
  endsTick: Tick,
  radiusMm: number | undefined,
): FloorWindow {
  return { windowId, setupClass, setupActorId, pos, endsTick, radiusMm: radiusMm ?? FLOOR_WINDOW_RADIUS_MM };
}

export function windowEffectId(windowId: string, setupClass: string, alternate: boolean): string {
  return `${alternate ? LOCK_WINDOW_PREFIX : WINDOW_PREFIX}${windowId}:${setupClass}`;
}

export interface ParsedWindowId {
  windowId: string;
  setupClass: string;
  alternate: boolean;
}

export function parseWindowEffectId(effectId: string): ParsedWindowId | null {
  const alternate = effectId.startsWith(LOCK_WINDOW_PREFIX);
  const plain = effectId.startsWith(WINDOW_PREFIX);
  if (!alternate && !plain) return null;
  const rest = effectId.slice((alternate ? LOCK_WINDOW_PREFIX : WINDOW_PREFIX).length);
  const sep = rest.indexOf(":");
  if (sep < 0) return null;
  return { windowId: rest.slice(0, sep), setupClass: rest.slice(sep + 1), alternate };
}

export function isWindowEffectId(effectId: string): boolean {
  return effectId.startsWith(WINDOW_PREFIX) || effectId.startsWith(LOCK_WINDOW_PREFIX);
}

export function findOpenWindow(target: ActorState, now: Tick) {
  return target.effects.find((e) => isWindowEffectId(e.effectId) && e.endsTick > now);
}

function readLastSetupClass(actor: ActorState): string | null {
  const inst = actor.effects.find((e) => e.effectId.startsWith(LAST_CLASS_PREFIX));
  return inst ? inst.effectId.slice(LAST_CLASS_PREFIX.length) : null;
}

export function lastSetupClassOf(actor: ActorState): string | null {
  return readLastSetupClass(actor);
}

export function setLastSetupClass(actor: ActorState, classId: string): void {
  removeEffectInstanceWhere(actor, (e) => e.effectId.startsWith(LAST_CLASS_PREFIX));
  pushRawEffectInstance(actor, {
    effectId: LAST_CLASS_PREFIX + classId,
    sourceId: actor.id,
    stacks: 1,
    endsTick: PERMANENT_TICK,
    nextPeriodicTick: null,
  });
}

export function incrementLockFailCount(actor: ActorState): number {
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

export function resetLockFailCount(actor: ActorState): void {
  removeEffectInstanceWhere(actor, (e) => e.effectId === LOCK_FAILS_ID);
}

export function lockFailCount(actor: ActorState): number {
  return actor.effects.find((e) => e.effectId === LOCK_FAILS_ID)?.stacks ?? 0;
}
