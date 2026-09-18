/**
 * bots/src/types.ts
 *
 * Local copy of the sim-facing types the bots package consumes. The sim
 * package (sim/) is being built in parallel by another lane; these types
 * are copied verbatim from the bots lane contract so bots/ has zero
 * compile-time dependency on sim/. Do not import from "sim" here.
 *
 * Usage: `import type { BotView, Command, ActorState } from "./types.js"`
 */

export type ActorId = number;
export type Tick = number;

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
  effects: Array<{
    effectId: string;
    sourceId: ActorId;
    stacks: number;
    endsTick: Tick;
    nextPeriodicTick: Tick | null;
  }>;
  threat: Record<ActorId, number>;
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

/** A boss/enemy AoE warning. Shapes are evaluated in the xz plane. */
export interface Telegraph {
  id: string;
  shape: "circle" | "cone" | "line";
  center: { x: number; z: number };
  radius?: number;
  angle1024?: number;
  yaw?: number;
  length?: number;
  width?: number;
  resolvesTick: number;
  danger: boolean;
}

/** What a bot's decide() call is handed each tick. */
export interface BotView {
  state: SimState;
  telegraphs: Telegraph[];
  classKit: {
    rotation: string[];
    interruptAbilityId: string | null;
    defensiveAbilityId: string | null;
    healAbilityId: string | null;
  };
}
