/**
 * server/src/telegraph.ts
 *
 * Maps sim/encounter's live Telegraph registry entries to the two shapes that need them here:
 * the wire-facing TelegraphSnapshot (snapshot.ts) and bots/src's own Telegraph type (bots/src/
 * types.ts).
 *
 * D10 net parity: the wire snapshot is client/src/sim-host/telegraph-map.ts's own
 * toTelegraphSnapshot (imported, not mirrored), so every telegraph carries the same
 * `telegraphClass` (avoid/spread/stack/boss) and class-derived `danger` the local worker host
 * renders -- one authority for the class table. The bot view keeps this server's existing
 * `danger: true` for every shape (unchanged here: the server's bot decisions, and so its run,
 * stay exactly what they were).
 *
 * Usage: `const snap = toTelegraphSnapshot(t); const botTg = toBotTelegraph(t);`
 */
import type { EncounterState } from "../../sim/encounter/telegraphs";
import type { Telegraph as BotTelegraph } from "../../bots/src/types.js";
import { toTelegraphSnapshot as toClassedTelegraphSnapshot } from "../../client/src/sim-host/telegraph-map.js";
import type { TelegraphSnapshot } from "./snapshot.js";

type Telegraph = EncounterState["telegraphs"][number];

export function toTelegraphSnapshot(t: Telegraph): TelegraphSnapshot {
  return toClassedTelegraphSnapshot(t);
}

export function toBotTelegraph(t: Telegraph): BotTelegraph {
  return {
    id: String(t.id),
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
