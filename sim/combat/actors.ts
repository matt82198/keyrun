/**
 * Actor factory and small state helpers. Usage:
 *   const a = createActor({ id: 1, kind: "player", classId: "warrior", role: "tank",
 *     pos: { x: 0, y: 0, z: 0 }, maxHealth: 1000, maxResource: 100 });
 */
import type { ActorId, ActorState, Vec3 } from "./types";

export interface CreateActorInput {
  id: ActorId;
  kind: "player" | "enemy";
  classId: string;
  role: "tank" | "healer" | "damage" | "none";
  pos: Vec3;
  yaw?: number;
  maxHealth: number;
  maxResource: number;
}

export function createActor(input: CreateActorInput): ActorState {
  return {
    id: input.id,
    kind: input.kind,
    classId: input.classId,
    role: input.role,
    pos: input.pos,
    yaw: input.yaw ?? 0,
    health: input.maxHealth,
    maxHealth: input.maxHealth,
    resource: input.maxResource,
    maxResource: input.maxResource,
    alive: true,
    targetId: null,
    castId: null,
    castEndsTick: null,
    castStartTick: null,
    cooldowns: {},
    gcdReadyTick: 0,
    effects: [],
    threat: {},
  };
}

export function isCasting(actor: ActorState): boolean {
  return actor.castId !== null;
}

export function reviveActor(actor: ActorState, health?: number): void {
  actor.alive = true;
  actor.health = health ?? actor.maxHealth;
}
