// Usage: SimState shape shared by sim/core and sim/combat, plus createState/cloneState/hashState.
// hashState is FNV-1a over a canonical (sorted-key) JSON rendering, so two structurally equal
// states hash equal regardless of the order fields or actors were inserted in.

import { seedState, type PrngState } from "./prng";

export type ActorId = number;
export type Tick = number;
export type Bp = number; // basis points

export interface Vec3 {
  x: number;
  y: number;
  z: number;
}

export interface EffectInstance {
  effectId: string;
  sourceId: ActorId;
  stacks: number;
  endsTick: Tick;
  nextPeriodicTick: Tick | null;
}

/** A floor-patch Window (design/BIBLE.md §4/§5, a "window" op with onTarget:false -- e.g.
 * warden.bulwark): keyed by windowId in SimState.floorWindows so it survives sim/combat/rules.ts's
 * makeRules being rebuilt every stepRun call (sim/index.ts), unlike a plain closure-local registry
 * would. See sim/combat/window-state.ts for the read/write helpers. */
export interface FloorWindow {
  windowId: string;
  setupClass: string;
  setupActorId: ActorId;
  pos: Vec3;
  endsTick: Tick;
  radiusMm: number;
}

export interface ActorState {
  id: ActorId;
  kind: "player" | "enemy";
  classId: string;
  role: "tank" | "healer" | "damage" | "none";
  pos: Vec3;
  yaw: number; // 0..1023
  health: number;
  maxHealth: number;
  resource: number;
  maxResource: number;
  alive: boolean;
  targetId: ActorId | null;
  castId: string | null;
  castEndsTick: Tick | null;
  castStartTick: Tick | null;
  cooldowns: Record<string, Tick>;
  gcdReadyTick: Tick;
  effects: EffectInstance[];
  threat: Record<ActorId, number>;
  /** Per-actor walk speed (mm/tick) for `move` commands; absent = RulesConfig.baseSpeedMmPerTick.
   * Set from content at spawn (sim/encounter/pulls.ts), never from a command. */
  speedMmPerTick?: number;
  /** Per-actor override of sim/encounter/chase.ts's CHASE_MARGIN_MM; absent = that constant. */
  chaseMarginMm?: number;
  /** The one press held in the spell queue window (sim/combat/spell-queue.ts); absent when none. Players only. */
  queuedCast?: QueuedPress;
}

/** A press held in the spell queue window: the cast command's ability, target and point, and the press tick. */
export interface QueuedPress {
  abilityId: string;
  targetId?: ActorId;
  point?: Vec3;
  pressTick: Tick;
}

export interface SimState {
  tick: Tick;
  seed: number;
  prng: PrngState;
  actors: Record<ActorId, ActorState>;
  runTimerTicks: number;
  phase: string;
  log: never[];
  /** Floor-patch Windows keyed by windowId (see FloorWindow's docstring). Optional so hand-built
   * SimState fixtures in tests that never touch floor Windows need not set it; createState always
   * populates it and sim/combat/rules.ts lazily initialises it otherwise. */
  floorWindows?: Record<string, FloorWindow>;
}

export function createState(seed: number): SimState {
  return {
    tick: 0,
    seed,
    prng: seedState(seed),
    actors: {},
    runTimerTicks: 0,
    phase: "init",
    log: [],
    floorWindows: {},
  };
}

export function cloneState(s: SimState): SimState {
  return structuredClone(s);
}

function canonicalStringify(value: unknown): string {
  if (Array.isArray(value)) {
    return `[${value.map(canonicalStringify).join(",")}]`;
  }
  if (value !== null && typeof value === "object") {
    const keys = Object.keys(value).sort();
    const body = keys.map((k) => `${JSON.stringify(k)}:${canonicalStringify((value as Record<string, unknown>)[k])}`);
    return `{${body.join(",")}}`;
  }
  return JSON.stringify(value);
}

/** FNV-1a (32-bit) over the canonical JSON rendering of `value`, returned as an 8-char hex string. */
function fnv1a(str: string): string {
  let hash = 0x811c9dc5;
  for (let i = 0; i < str.length; i++) {
    hash ^= str.charCodeAt(i);
    hash = Math.imul(hash, 0x01000193) >>> 0;
  }
  return hash.toString(16).padStart(8, "0");
}

export function hashState(s: SimState): string {
  return fnv1a(canonicalStringify(s));
}
