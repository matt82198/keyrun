/**
 * server/src/roster.ts
 *
 * The fixed 5-slot party shape: one tank, one healer, three damage -- content/data/classes only
 * ships three classes (warden/chanter/cinder), each bound to exactly one role, same simplification
 * client/src/sim-host/content-party.ts makes (read-only reference, not imported). Slot -> actorId
 * is a fixed index (1..5), independent of join order, so a room's underlying party spec -- and
 * therefore its Run -- is bit-identical no matter which slots end up human vs. bot-controlled.
 *
 * Usage: `const spec = buildPartySpec(registry);` then `spec[i].id` is the actorId for
 * `ROLE_ROSTER[i]`.
 */
import type { PartyMemberSpec } from "../../sim/index";
import type { ContentRegistry } from "../../content/schema/registry";

export type PartyRole = "tank" | "healer" | "damage";

export interface RosterSlot {
  classId: string;
  role: PartyRole;
}

export const ROLE_ROSTER: readonly RosterSlot[] = [
  { classId: "warden", role: "tank" },
  { classId: "chanter", role: "healer" },
  { classId: "cinder", role: "damage" },
  { classId: "cinder", role: "damage" },
  { classId: "cinder", role: "damage" },
];

const PARTY_SPAWN_Z = 4000;
const SPAWN_X = [0, -2100, -700, 700, 2100];
const MAX_HEALTH: Record<PartyRole, number> = { tank: 400, healer: 250, damage: 250 };
const FACE_MINUS_Z = 512;

/** actorId is 1-indexed by ROLE_ROSTER position -- ROLE_ROSTER[i] always spawns as actor i+1. */
export function actorIdForSlot(slotIndex: number): number {
  return slotIndex + 1;
}

export function buildPartySpec(registry: ContentRegistry): PartyMemberSpec[] {
  return ROLE_ROSTER.map((slot, i) => {
    const cls = registry.classes.get(slot.classId);
    return {
      id: actorIdForSlot(i),
      classId: slot.classId,
      role: slot.role,
      pos: { x: SPAWN_X[i] ?? 0, y: 0, z: PARTY_SPAWN_Z },
      yaw: FACE_MINUS_Z,
      maxHealth: MAX_HEALTH[slot.role],
      maxResource: cls?.loop.max ?? 100,
    };
  });
}
