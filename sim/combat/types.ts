/**
 * Shared simulation type contracts, copied verbatim from the cross-lane data contract so this
 * lane compiles standalone without sim/core. An integration lane will later switch imports of
 * these names to sim/core; do not change the shapes below without updating that contract.
 * Usage: import type { ActorState, Command, StepRules } from "./types";
 */
export type ActorId = number;
export type Tick = number;
export type Bp = number;

export interface Vec3 {
  x: number;
  y: number;
  z: number;
}

export interface ActorState {
  id: ActorId;
  kind: "player" | "enemy";
  classId: string;
  role: "tank" | "healer" | "damage" | "none";
  pos: Vec3;
  yaw: number;
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
}

export interface EffectInstance {
  effectId: string;
  sourceId: ActorId;
  stacks: number;
  endsTick: Tick;
  nextPeriodicTick: Tick | null;
}

export interface SimState {
  tick: Tick;
  seed: number;
  prng: [number, number, number, number];
  actors: Record<ActorId, ActorState>;
  runTimerTicks: number;
  phase: string;
  log: never[];
}

export type Command = { tick: Tick; actorId: ActorId } & (
  | { kind: "move"; dir: { x: number; z: number }; yaw: number }
  | { kind: "cast"; abilityId: string; targetId?: ActorId; point?: Vec3 }
  | { kind: "stopcast" }
  | { kind: "target"; targetId: ActorId | null }
);

export type SimEvent = { tick: Tick; type: string; [k: string]: unknown };

export type StepRules = (state: SimState, commands: Command[], emit: (e: SimEvent) => void) => void;
