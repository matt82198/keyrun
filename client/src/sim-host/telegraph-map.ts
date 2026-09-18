/**
 * client/src/sim-host/telegraph-map.ts
 *
 * One shared mapping from sim/encounter's live Telegraph registry entries to the two shapes that
 * need them: the render layer's TelegraphSnapshot (snapshot.ts) and bots/src's own Telegraph type
 * (bots/src/types.ts) -- both are structurally the same "origin/shape/yaw/resolvesTick" data,
 * previously duplicated inline in worker.ts in two places.
 *
 * Usage: `const snap = toTelegraphSnapshot(t); const botTg = toBotTelegraph(t);`
 */
import type { EncounterState } from "../../../sim/encounter/telegraphs.js";
import type { Telegraph as BotTelegraph } from "../../../bots/src/types.js";
import type { TelegraphSnapshot } from "./snapshot.js";

type Telegraph = EncounterState["telegraphs"][number];

export function toTelegraphSnapshot(t: Telegraph): TelegraphSnapshot {
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
