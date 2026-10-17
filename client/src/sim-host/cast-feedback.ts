/**
 * client/src/sim-host/cast-feedback.ts
 *
 * D10 first-run readability: a key press that casts NOTHING must say why ("Out of range", "Not
 * ready", ...) instead of silently doing nothing. Display only -- the sim's startCast stays the one
 * authority on whether a cast starts. `castFailReason` reads the same pre-step state startCast will
 * see, through the sim's own exported predicates (sim/combat/cooldowns.ts isAbilityReady,
 * sim/combat/casting.ts rangeMm), to NAME the likely reason; `castFailures` then reports only the
 * human casts that really did not start in that step (no cast_complete, no cast in progress).
 *
 * Usage (sim-runtime.ts): `const reasons = predictFailReasons(run, humanId, casts);` before stepRun,
 * then `castFailures(run, humanId, reasons, stepped.events)` after it.
 */
import type { Run } from "../../../sim/index.js";
import type { SimEvent } from "../../../sim/core/events.js";
import { isAbilityReady } from "../../../sim/combat/cooldowns.js";
import { rangeMm } from "../../../sim/combat/casting.js";
import { castTargetFor } from "./player-commands.js";
import type { CastFailedSnapshotEvent } from "./snapshot.js";

export function castFailReason(run: Run, humanId: number, abilityId: string): string {
  const actor = run.state.actors[humanId];
  const def = run.content.abilities[abilityId];
  if (!actor || !def) return "Can't cast that";
  if (!actor.alive) return "You are dead";
  if (!isAbilityReady(actor, def, run.state.tick)) return "Not ready yet";
  if (actor.resource < def.cost.amount) return "Not enough resource";
  if (def.shape.kind === "target") {
    const target = run.state.actors[castTargetFor(run, humanId, abilityId)];
    if (!target || !target.alive) return "No target";
    if (rangeMm(actor, target) > def.range) return "Out of range";
  }
  return "Can't cast that now";
}

export function predictFailReasons(run: Run, humanId: number, casts: readonly string[]): Map<string, string> {
  const out = new Map<string, string>();
  for (const id of casts) if (!out.has(id)) out.set(id, castFailReason(run, humanId, id));
  return out;
}

export function castFailures(run: Run, humanId: number, reasons: Map<string, string>, events: readonly SimEvent[]): CastFailedSnapshotEvent[] {
  const out: CastFailedSnapshotEvent[] = [];
  const actor = run.state.actors[humanId];
  for (const [abilityId, reason] of reasons) {
    const completed = events.some((e) => e.type === "cast_complete" && e.actorId === humanId && e.abilityId === abilityId);
    const started = actor?.castId === abilityId;
    if (!completed && !started) out.push({ type: "cast_failed", tick: run.state.tick - 1, actorId: humanId, abilityId, reason });
  }
  return out;
}
