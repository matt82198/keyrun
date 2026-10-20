/**
 * client/src/sim-host/telegraph-map.ts
 *
 * One shared mapping from sim/encounter's live Telegraph registry entries to the two shapes that
 * need them: the render layer's TelegraphSnapshot (snapshot.ts) and bots/src's own Telegraph type
 * (bots/src/types.ts) -- both are structurally the same "origin/shape/yaw/resolvesTick" data,
 * previously duplicated inline in worker.ts in two places.
 *
 * Telegraph classes (D10 readability): the sim registers a telegraph per telegraphed enemy cast
 * (content/data/enemies/*.json script steps with a `telegraph`), carrying the casting abilityId.
 * Each such ability gets one render class here -- avoid (generic "get out"), spread (stand apart),
 * stack (stand together), boss (the boss's own mechanic). `danger` is derived from the class
 * (a stack circle is the one telegraph you should stand IN, so it is not danger), never hardcoded.
 * Unknown abilities fall back to "avoid". No "soak" class: nothing in the sim/content emits one.
 *
 * Snapshot field: `telegraphClass` lives ONLY on this file's own output shape
 * (ClassedTelegraphSnapshot, a structural superset of snapshot.ts's TelegraphSnapshot), so
 * snapshot-codec.ts carries it to the renderer without any change to snapshot.ts.
 *
 * Usage: `const snap = toTelegraphSnapshot(t); const botTg = toBotTelegraph(t);`
 */
import type { EncounterState } from "../../../sim/encounter/telegraphs.js";
import type { Telegraph as BotTelegraph } from "../../../bots/src/types.js";
import type { TelegraphSnapshot } from "./snapshot.js";

type Telegraph = EncounterState["telegraphs"][number];

export type TelegraphClass = "avoid" | "spread" | "stack" | "boss";

export interface ClassedTelegraphSnapshot extends TelegraphSnapshot {
  telegraphClass: TelegraphClass;
}

/** Every telegraphed cast the shipped content emits (tests/client/sim-host/telegraph-map.test.ts
 * fails if a new one appears without an entry here). */
export const TELEGRAPH_CLASS_BY_ABILITY: Readonly<Record<string, TelegraphClass>> = {
  "pair.spreadMark": "spread",
  "pair.stackCall": "stack",
  "hollowWarden.cleave": "boss",
  "seed.slam": "avoid",
  // SLAM-TELEGRAPH (BIBLE section 3): the boss and Twins slams are "get out" circles like seed.slam.
  // "avoid" equals the unlisted fallback, so bots/Keyrun.Bots/View.cs (which does not list them) agrees.
  "hollowWarden.slam": "avoid",
  "twins.slam": "avoid",
  // KF3-SEED-ENGAGE: the Seed's lash (the target-shaped cast that makes it walk to the party) is another "get out"
  // circle; "avoid" equals the unlisted fallback, so bots/Keyrun.Bots/View.cs agrees.
  "seed.lash": "avoid",
};

export function telegraphClassFor(abilityId: string): TelegraphClass {
  return TELEGRAPH_CLASS_BY_ABILITY[abilityId] ?? "avoid";
}

/** Danger = "get out of this shape". Everything but a stack circle. */
export function isDangerClass(c: TelegraphClass): boolean {
  return c !== "stack";
}

function geometry(t: Telegraph): BotTelegraph {
  return {
    id: String(t.id),
    shape: t.shape,
    center: { x: t.origin.x, z: t.origin.z },
    yaw: t.yaw,
    resolvesTick: t.resolvesTick,
    danger: isDangerClass(telegraphClassFor(t.abilityId)),
    ...(t.radius !== undefined ? { radius: t.radius } : {}),
    ...(t.angle1024 !== undefined ? { angle1024: t.angle1024 } : {}),
    ...(t.length !== undefined ? { length: t.length } : {}),
    ...(t.width !== undefined ? { width: t.width } : {}),
  };
}

export function toTelegraphSnapshot(t: Telegraph): ClassedTelegraphSnapshot {
  return { ...geometry(t), telegraphClass: telegraphClassFor(t.abilityId) };
}

/** DEAD-CASTER-TELEGRAPH: the bot shape also carries the caster (bots/src/shared/floor.ts isSpent); the render
 * snapshot does not. */
export function toBotTelegraph(t: Telegraph): BotTelegraph {
  return { ...geometry(t), sourceId: t.sourceId };
}
