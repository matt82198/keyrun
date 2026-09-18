/**
 * sim/encounter/types.ts — LOCAL copies of the shared sim contracts.
 *
 * sim/core and sim/combat are being built in parallel by other lanes in separate worktrees and are
 * not present in this worktree yet. Per the encounter-sim lane brief, these types are copied
 * verbatim from sim/CLAUDE.md's companion contract so sim/encounter/* has zero import dependency on
 * sim/core or sim/combat while those lanes land. When they land, this file should be deleted and
 * sim/encounter/* repointed at the real sim/core/state.ts (and friends) — same shapes, one source.
 *
 * DEVIATION (documented in the lane's final report): the brief lists `EffectInstance` as one of the
 * types to copy verbatim, but the companion contract text only defines the effect shape inline on
 * `ActorState.effects`. It is pulled out here as a named `EffectInstance` interface and reused on
 * `ActorState.effects` so the export exists without changing the inline shape's fields.
 */

export type ActorId = number;
export type Tick = number;

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
