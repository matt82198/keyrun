/**
 * client/src/sim-host/run-policy.ts
 *
 * Host-level policy worker.ts applies around each sim/index.ts stepRun call: targeting fixups
 * (sim has no enemy AI target-acquisition -- see file docstring below), Slack spending on player
 * deaths/wipes (sim/encounter/timer.ts exposes these as pure functions but nothing in
 * sim/index.ts's createRun/stepRun calls them automatically), and the signals hud/run-end.ts's
 * state machine needs. Split out of worker.ts to keep it under the repo's 190-LOC-per-file gate.
 *
 * Enemies never auto-target (sim/combat has no nearest-enemy AI, only sim/combat/targeting.ts's
 * manual `cycleTarget` tab-cycle helper) and damage bots do not acquire their own targets either
 * (bots/src/roles.ts's damageDecide casts at `self.targetId` only, by design -- see bots/src/bot.ts's
 * own comment on the pre-existing single-pull scaffold this playable-v1 replaces). `fixupTargeting`
 * points every untargeted living enemy at the tank and every damage-role party member without a
 * living enemy target at the current pull's first living enemy.
 *
 * Usage: `fixupTargeting(run, setup); run = applyDeathsAndWipe(run, events, tick);`
 */
import type { Run } from "../../../sim/index.js";
import type { SimEvent } from "../../../sim/core/events.js";
import type { ActorId, Tick } from "../../../sim/core/state.js";
import { spendSlackOnDeath, spendSlackOnWipe } from "../../../sim/encounter/timer.js";
import type { RunSetup } from "./run-setup.js";
import type { RunEndSignals } from "../hud/run-end.js";

export function fixupTargeting(run: Run, setup: RunSetup): void {
  const state = run.state;
  const tank = state.actors[setup.tankActorId];
  const fallbackTargetId = tank && tank.alive ? tank.id : setup.humanActorId;
  const enemies = Object.values(state.actors).filter((a) => a.kind === "enemy" && a.alive);
  for (const enemy of enemies) {
    if (enemy.targetId === null) enemy.targetId = fallbackTargetId;
  }
  const firstEnemy = enemies[0];
  if (!firstEnemy) return;
  for (const member of setup.party) {
    if (member.role !== "damage") continue;
    const actor = state.actors[member.id];
    if (!actor || !actor.alive) continue;
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

export function computeRunEndSignals(run: Run, setup: RunSetup, abandonPressed: boolean, tick: Tick): RunEndSignals {
  const boss = run.state.actors[setup.bossActorId];
  const players = Object.values(run.state.actors).filter((a) => a.kind === "player");
  return {
    tick,
    bossDead: boss !== undefined && !boss.alive,
    depleted: run.encounter.depleted,
    allPlayersDead: players.length > 0 && players.every((a) => !a.alive),
    abandonPressed,
  };
}
