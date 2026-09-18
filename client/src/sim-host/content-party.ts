/**
 * client/src/sim-host/content-party.ts
 *
 * Builds the fixed Hollow Vault party roster (one Warden tank, one Chanter healer, three Cinder
 * damage -- content/data/classes only ships three classes, see file docstring below) and each
 * class's bot classKit (bots/src/types.ts's BotView["classKit"]) from content class data.
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
 * Usage: `const { party, humanActorId, tankActorId, classKits } = buildParty(registry, "cinder");`
 */
import type { PartyMemberSpec } from "../../../sim/index.js";
import type { ActorId } from "../../../sim/core/state.js";
import type { BotView } from "../../../bots/src/types.js";
import type { CClassDef, ContentRegistry } from "./content-registry.js";

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

/** Only Chanter heals a single ally directly (see content-abilities.ts's Mend-only deviation);
 * Cadence's Mend variant is a targeted HoT, unlike Refrain (self-only) or Chorus (caster-centred
 * area) -- the only base-kit ability that fits classKit's single `healAbilityId` slot contract. */
const HEAL_ABILITY_BY_CLASS: Record<string, string> = { chanter: "chanter.cadence" };

export function resolveHumanClassId(param: string | null): string {
  const normalized = (param ?? "").toLowerCase();
  if (normalized === "warden" || normalized === "cinder" || normalized === "chanter") return normalized;
  return "warden";
}

export function buildClassKit(cls: CClassDef): BotView["classKit"] {
  return {
    rotation: [cls.abilities.rot1, cls.abilities.rot2, cls.abilities.rot3, cls.abilities.rot4, cls.abilities.rot5],
    interruptAbilityId: cls.abilities.interrupt,
    defensiveAbilityId: cls.abilities.defensive,
    healAbilityId: HEAL_ABILITY_BY_CLASS[cls.id] ?? null,
  };
}

export interface PartyBuild {
  party: PartyMemberSpec[];
  humanActorId: ActorId;
  tankActorId: ActorId;
  classKits: Record<string, BotView["classKit"]>;
  /** actorId -> classId, for grouping bot decide() calls by class (each class needs its own
   * classKit view; bots/src/bot.ts's runBots() takes one classKit per call). */
  actorClassId: Record<ActorId, string>;
}

export function buildParty(registry: ContentRegistry, humanClassId: string): PartyBuild {
  const humanSlot = ROSTER.findIndex((slot) => slot.classId === humanClassId);
  const resolvedHumanSlot = humanSlot >= 0 ? humanSlot : 0;

  const party: PartyMemberSpec[] = [];
  const actorClassId: Record<ActorId, string> = {};
  let botNextId = 2;
  let tankActorId = 1;

  ROSTER.forEach((slot, i) => {
    const isHuman = i === resolvedHumanSlot;
    const actorId = isHuman ? 1 : botNextId++;
    if (slot.role === "tank") tankActorId = actorId;
    actorClassId[actorId] = slot.classId;
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

  const classKits: Record<string, BotView["classKit"]> = {};
  for (const cls of registry.classes.values()) classKits[cls.id] = buildClassKit(cls);

  return { party, humanActorId: 1, tankActorId, classKits, actorClassId };
}
