/**
 * server/src/telegraph.ts
 *
 * Maps sim/encounter's live Telegraph registry entries to the two shapes that need them here:
 * the wire-facing TelegraphSnapshot (snapshot.ts) and bots/src's own Telegraph type (bots/src/
 * types.ts). Mirrors client/src/sim-host/telegraph-map.ts field-for-field (read-only reference,
 * not imported -- see server/CLAUDE.md) so a future client net layer can consume this server's
 * telegraph wire shape without translation.
 *
 * Usage: `const snap = toTelegraphSnapshot(t); const botTg = toBotTelegraph(t);`
 */
import type { EncounterState } from "../../sim/encounter/telegraphs";
import type { Telegraph as BotTelegraph } from "../../bots/src/types.js";
import type { TelegraphSnapshot } from "./snapshot.js";

type Telegraph = EncounterState["telegraphs"][number];

function sharedFields(t: Telegraph): Omit<TelegraphSnapshot, "id"> {
  return {
    shape: t.shape,
    center: { x: t.origin.x, z: t.origin.z },
    yaw: t.yaw,
    resolvesTick: t.resolvesTick,
    danger: true,
    ...(t.radius !== undefined ? { radius: t.radius } : {}),
    ...(t.angle1024 !== undefined ? { angle1024: t.angle1024 } : {}),
    ...(t.length !== undefined ? { length: t.length } : {}),
    ...(t.width !== undefined ? { width: t.width } : {}),
  };
}

export function toTelegraphSnapshot(t: Telegraph): TelegraphSnapshot {
  return { id: String(t.id), ...sharedFields(t) };
}

export function toBotTelegraph(t: Telegraph): BotTelegraph {
  return { id: String(t.id), ...sharedFields(t) };
}
