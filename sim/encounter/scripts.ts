/**
 * sim/encounter/scripts.ts — enemy behaviour as data-driven state machines.
 *
 * An EnemyScript is a flat list of steps; each step either repeats ("loop", every N ticks, first
 * firing at tick N — not tick 0) or fires once at an absolute tick. `stepEnemyScripts` is called
 * once per tick with the live SimState and a map of enemy actorId -> EnemyScript. It decides which
 * enemy casts what this tick (returned as `Command[]`, kind "cast") and, for steps carrying a
 * telegraph, emits the telegraph's SimEvent `leadTicks` ticks BEFORE the matching cast fires, with
 * `resolvesTick` set to the tick the cast itself lands on. Origin/yaw are snapshotted from the
 * casting actor's current position at telegraph-emit time (telegraphs lock in when announced, not
 * when they resolve). Dead or missing actors never cast.
 *
 * Usage: `const { commands, telegraphEvents } = stepEnemyScripts(state, scripts);`
 */
import type { ActorId, Command, SimEvent, SimState, Tick } from "./types";

export interface TelegraphSpec {
  shape: "circle" | "cone" | "line";
  radius?: number;
  angle1024?: number;
  length?: number;
  width?: number;
  leadTicks: number;
}

export interface ScriptStep {
  at: "loop" | number;
  every?: number;
  cast: string;
  telegraph?: TelegraphSpec;
}

export type EnemyScript = ScriptStep[];

function firesAt(step: ScriptStep, tick: Tick): boolean {
  if (step.at === "loop") {
    const every = step.every ?? 0;
    return every > 0 && tick > 0 && tick % every === 0;
  }
  return tick === step.at;
}

export interface StepEnemyScriptsResult {
  commands: Command[];
  telegraphEvents: SimEvent[];
}

export function stepEnemyScripts(
  state: SimState,
  scripts: Record<ActorId, EnemyScript>
): StepEnemyScriptsResult {
  const tick = state.tick;
  const commands: Command[] = [];
  const telegraphEvents: SimEvent[] = [];

  for (const key of Object.keys(scripts)) {
    const actorId = Number(key);
    const script = scripts[actorId];
    const actor = state.actors[actorId];
    if (!script || !actor || !actor.alive) continue;

    for (const step of script) {
      if (firesAt(step, tick)) {
        commands.push({ tick, actorId, kind: "cast", abilityId: step.cast });
      }
      const tg = step.telegraph;
      if (tg && firesAt(step, tick + tg.leadTicks)) {
        const event: SimEvent = {
          tick,
          type: "telegraph",
          actorId,
          cast: step.cast,
          shape: tg.shape,
          leadTicks: tg.leadTicks,
          resolvesTick: tick + tg.leadTicks,
          origin: actor.pos,
          yaw: actor.yaw,
        };
        if (tg.radius !== undefined) event.radius = tg.radius;
        if (tg.angle1024 !== undefined) event.angle1024 = tg.angle1024;
        if (tg.length !== undefined) event.length = tg.length;
        if (tg.width !== undefined) event.width = tg.width;
        telegraphEvents.push(event);
      }
    }
  }

  return { commands, telegraphEvents };
}
