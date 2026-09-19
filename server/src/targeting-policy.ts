/**
 * server/src/targeting-policy.ts
 *
 * Host-level policy applied around every stepRun call, same role client/src/sim-host/run-policy.ts
 * plays (read-only reference, not imported -- see server/CLAUDE.md): sim has no enemy
 * target-acquisition of its own (only sim/combat/targeting.ts's manual `cycleTarget` tab-cycle),
 * and damage-role actors do not acquire targets either (bots/src/roles.ts's damageDecide casts at
 * `self.targetId` only). `fixupTargeting` points every untargeted living enemy at the tank and
 * every damage-role party member without a living enemy target at the current pull's first living
 * enemy. `applyDeathsAndWipe` spends the run's Slack bank on player deaths/wipes (sim/encounter/
 * timer.ts exposes these as pure functions; nothing in sim/index.ts calls them automatically).
 *
 * Usage: `fixupTargeting(run, tankActorId); run = applyDeathsAndWipe(run, events, tick);`
 */
import type { Run } from "../../sim/index";
import type { SimEvent } from "../../sim/core/events";
import type { ActorId, Tick } from "../../sim/core/state";
import { spendSlackOnDeath, spendSlackOnWipe } from "../../sim/encounter/timer";

export function fixupTargeting(run: Run, tankActorId: ActorId): void {
  const state = run.state;
  const tank = state.actors[tankActorId];
  const fallbackTargetId = tank && tank.alive ? tank.id : tankActorId;
  const enemies = Object.values(state.actors).filter((a) => a.kind === "enemy" && a.alive);
  for (const enemy of enemies) {
    if (enemy.targetId === null) enemy.targetId = fallbackTargetId;
  }
  const firstEnemy = enemies[0];
  if (!firstEnemy) return;
  for (const actor of Object.values(state.actors)) {
    if (actor.kind !== "player" || actor.role !== "damage" || !actor.alive) continue;
    const current = actor.targetId !== null ? state.actors[actor.targetId] : undefined;
    const currentIsLivingEnemy = current !== undefined && current.kind === "enemy" && current.alive;
    if (!currentIsLivingEnemy) actor.targetId = firstEnemy.id;
  }
}

export function applyDeathsAndWipe(run: Run, events: SimEvent[], tick: Tick): Run {
  const anyPlayerDeath = events.some((e) => e.type === "death" && run.state.actors[e.actorId as ActorId]?.kind === "player");
  if (!anyPlayerDeath) return run;
  const noop = (): void => {};
  let next = { ...run, encounter: spendSlackOnDeath(run.encounter, tick, noop) };
  const players = Object.values(next.state.actors).filter((a) => a.kind === "player");
  if (players.length > 0 && players.every((a) => !a.alive)) {
    next = { ...next, encounter: spendSlackOnWipe(next.encounter, tick, noop) };
  }
  return next;
}
