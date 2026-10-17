/**
 * server/src/telegraph.ts
 *
 * Maps sim/encounter's live Telegraph registry entries to the two shapes that need them here:
 * the wire-facing TelegraphSnapshot (snapshot.ts) and bots/src's own Telegraph type (bots/src/
 * types.ts).
 *
 * D10 net parity: BOTH shapes are client/src/sim-host/telegraph-map.ts's own functions
 * (imported, not mirrored), so the wire snapshot carries the same `telegraphClass`
 * (avoid/spread/stack/boss) the local worker host renders, and server bots read the same
 * class-derived `danger` the local host's bots do (a stack circle is not danger, so bots stand in
 * it rather than flee it -- STATE.md defect (f); the client fixed its copy in PR #61). One
 * authority for the class table.
 *
 * Usage: `const snap = toTelegraphSnapshot(t); const botTg = toBotTelegraph(t);`
 */
import type { EncounterState } from "../../sim/encounter/telegraphs";
import type { Telegraph as BotTelegraph } from "../../bots/src/types.js";
import {
  toTelegraphSnapshot as toClassedTelegraphSnapshot,
  toBotTelegraph as toClassedBotTelegraph,
} from "../../client/src/sim-host/telegraph-map.js";
import type { TelegraphSnapshot } from "./snapshot.js";

type Telegraph = EncounterState["telegraphs"][number];

export function toTelegraphSnapshot(t: Telegraph): TelegraphSnapshot {
  return toClassedTelegraphSnapshot(t);
}

export function toBotTelegraph(t: Telegraph): BotTelegraph {
  return toClassedBotTelegraph(t);
}
