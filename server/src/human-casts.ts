/**
 * server/src/human-casts.ts
 *
 * ?net=1 parity for a human's ability presses, built from the client worker host's OWN pure rules
 * (imported, never copied -- server/CLAUDE.md):
 *  - `resolveHumanCasts`: a human `cast` that arrives with no `targetId` gets
 *    client/src/sim-host/player-commands.ts's castTargetFor (the D10 nearest-live-enemy rule for a
 *    hostile single-target ability; else the current target, else the caster), resolved against
 *    THIS tick's authoritative state -- exactly what the local host sends. A cast that carries an
 *    explicit targetId is left alone; bot commands are never touched.
 *  - `predictHumanFailures` / `humanCastFailures`: sim-host/cast-feedback.ts's predict-before /
 *    confirm-after pair, per human, so a press that cast nothing reaches the client as a one-shot
 *    `cast_failed` event ("Not ready yet", "Out of range", ...). Display only.
 *
 * Usage (room.ts tick): `const { stepped, failed } = stepWithHumans(run, due, botCommands, isHuman, opts);`
 */
import { stepRun, type Run } from "../../sim/index";
import type { Command } from "../../sim/core/commands";
import type { ActorId } from "../../sim/core/state";
import type { SimEvent } from "../../sim/core/events";
import { castTargetFor } from "../../client/src/sim-host/player-commands.js";
import { castFailures, predictFailReasons } from "../../client/src/sim-host/cast-feedback.js";
import type { CastFailedSnapshotEvent } from "../../client/src/sim-host/snapshot.js";

type IsHuman = (actorId: ActorId) => boolean;
type StepOptions = NonNullable<Parameters<typeof stepRun>[2]>;

export function resolveHumanCasts(run: Run, due: readonly Command[], isHuman: IsHuman): Command[] {
  return due.map((c) =>
    c.kind === "cast" && c.targetId === undefined && isHuman(c.actorId) ? { ...c, targetId: castTargetFor(run, c.actorId, c.abilityId) } : c,
  );
}

export function predictHumanFailures(run: Run, due: readonly Command[], isHuman: IsHuman): Map<ActorId, Map<string, string>> {
  const casts = new Map<ActorId, string[]>();
  for (const c of due) {
    if (c.kind !== "cast" || !isHuman(c.actorId)) continue;
    casts.set(c.actorId, [...(casts.get(c.actorId) ?? []), c.abilityId]);
  }
  return new Map([...casts].map(([id, abilityIds]) => [id, predictFailReasons(run, id, abilityIds)]));
}

export function humanCastFailures(run: Run, reasons: Map<ActorId, Map<string, string>>, events: readonly SimEvent[]): CastFailedSnapshotEvent[] {
  return [...reasons].flatMap(([id, r]) => castFailures(run, id, r, events));
}

/** room.ts's one step: resolve human casts, predict failures, stepRun, confirm failures. */
export function stepWithHumans(run: Run, due: readonly Command[], botCommands: readonly Command[], isHuman: IsHuman, options: StepOptions) {
  const humanDue = resolveHumanCasts(run, due, isHuman);
  const reasons = predictHumanFailures(run, humanDue, isHuman);
  const stepped = stepRun(run, [...humanDue, ...botCommands], options);
  return { stepped, failed: humanCastFailures(stepped.run, reasons, stepped.events) };
}
