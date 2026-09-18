/**
 * client/src/sim-host/content-party.ts
 *
 * Builds the fixed Hollow Vault party roster: one Warden tank, one Chanter healer, three Cinder
 * damage -- content/data/classes only ships three classes, see the DEVIATION note below. Bot
 * class kits are no longer built here: bots/src/kits.ts's `getClassKit()` resolves a rich,
 * content-derived ClassKit per actor internally (rotation priority tiers, Setup/Payoff ability
 * ids, resource economics) once `setContentRegistry()` is called (see run-setup.ts) -- this file
 * only has to place actors and assign roles.
 *
 * DEVIATION from the dispatch brief: it asks for "?class=<one of the four class ids>", but
 * design/BIBLE.md Sec 5 is titled "The three starter classes" and content/data/classes/ ships
 * exactly three (warden, cinder, chanter) -- there is no fourth class anywhere in this repo.
 * `resolveHumanClassId` accepts any of the three (case-insensitive), defaulting to "warden" for
 * anything else. Because only Cinder currently fills a damage slot, all three damage roster
 * entries are Cinder; the human's chosen class determines which single roster slot they occupy
 * (always actor id 1, matching every other client file's hardcoded PLAYER_ID convention), the
 * other four slots become bots.
 *
 * Usage: `const { party, humanActorId, tankActorId } = buildParty(registry, "cinder");`
 */
import type { PartyMemberSpec } from "../../../sim/index.js";
import type { ActorId } from "../../../sim/core/state.js";
import type { ContentRegistry } from "../../../content/schema/registry.js";

export type PartyRole = "tank" | "healer" | "damage";

interface RosterSlot {
  classId: string;
  role: PartyRole;
}

const ROSTER: readonly RosterSlot[] = [
  { classId: "warden", role: "tank" },
  { classId: "chanter", role: "healer" },
  { classId: "cinder", role: "damage" },
  { classId: "cinder", role: "damage" },
  { classId: "cinder", role: "damage" },
];

const PARTY_SPAWN_Z = 4000;
const SPAWN_X = [0, -2100, -700, 700, 2100];
const HUMAN_MAX_HEALTH: Record<PartyRole, number> = { tank: 400, healer: 250, damage: 250 };
const FACE_MINUS_Z = 512;

export function resolveHumanClassId(param: string | null): string {
  const normalized = (param ?? "").toLowerCase();
  if (normalized === "warden" || normalized === "cinder" || normalized === "chanter") return normalized;
  return "warden";
}

export interface PartyBuild {
  party: PartyMemberSpec[];
  humanActorId: ActorId;
  tankActorId: ActorId;
}

export function buildParty(registry: ContentRegistry, humanClassId: string): PartyBuild {
  const humanSlot = ROSTER.findIndex((slot) => slot.classId === humanClassId);
  const resolvedHumanSlot = humanSlot >= 0 ? humanSlot : 0;

  const party: PartyMemberSpec[] = [];
  let botNextId = 2;
  let tankActorId = 1;

  ROSTER.forEach((slot, i) => {
    const isHuman = i === resolvedHumanSlot;
    const actorId = isHuman ? 1 : botNextId++;
    if (slot.role === "tank") tankActorId = actorId;
    const cls = registry.classes.get(slot.classId);
    party.push({
      id: actorId,
      classId: slot.classId,
      role: slot.role,
      pos: { x: SPAWN_X[i] ?? 0, y: 0, z: PARTY_SPAWN_Z },
      yaw: FACE_MINUS_Z,
      maxHealth: HUMAN_MAX_HEALTH[slot.role],
      maxResource: cls?.loop.max ?? 100,
    });
  });

  return { party, humanActorId: 1, tankActorId };
}
