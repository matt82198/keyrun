/**
 * bots/src/bot.ts
 *
 * The Bot interface and runBots() batch runner. Danger-telegraph avoidance
 * always overrides the reaction-delay/skill gate below it; role decisions
 * (target acquisition, rotation, heals, interrupts -- see roles.ts) are
 * gated by the skill dial's reaction delay and mistake roll.
 *
 * Usage: `import { makeBot, runBots, defaultMemory } from "./bot.js"`
 */
import type { ActorId, ActorState, BotView, Command, Tick } from "./types.js";
import { toMM, isInAnyDanger, nearestSafePoint } from "./shared/positioning.js";
import { type PrngState, reactionDelayTicks, rollMistake } from "./skill.js";
import { tankDecide, healerDecide, damageDecide } from "./roles.js";

export interface BotMemory {
  skillDial: number;
  nextActionTick: Tick;
}

export function defaultMemory(skillDial = 100): BotMemory {
  return { skillDial, nextActionTick: 0 };
}

export interface Bot {
  decide(
    view: BotView,
    memory: BotMemory,
    actorId: ActorId,
    prng: PrngState
  ): { commands: Command[]; memory: BotMemory; prng: PrngState };
}

function dangerAvoidCommand(view: BotView, self: ActorState): Command | null {
  const tick = view.state.tick;
  const selfMM = { x: toMM(self.pos.x), z: toMM(self.pos.z) };
  const active = view.telegraphs.filter((t) => t.danger && t.resolvesTick > tick);
  if (!isInAnyDanger(selfMM, active)) return null;
  const safe = nearestSafePoint(selfMM, active);
  if (!safe || (safe.x === selfMM.x && safe.z === selfMM.z)) return null;
  const dx = safe.x - selfMM.x;
  const dz = safe.z - selfMM.z;
  const len = Math.hypot(dx, dz) || 1;
  return { tick, actorId: self.id, kind: "move", dir: { x: dx / len, z: dz / len }, yaw: Math.atan2(dz, dx) };
}

function decideForActor(
  view: BotView,
  memory: BotMemory,
  actorId: ActorId,
  prng: PrngState
): { commands: Command[]; memory: BotMemory; prng: PrngState } {
  const self = view.state.actors[actorId];
  if (!self || !self.alive) return { commands: [], memory, prng };
  const tick = view.state.tick;

  const avoid = dangerAvoidCommand(view, self);
  const commands: Command[] = avoid ? [avoid] : [];

  if (tick < memory.nextActionTick) {
    return { commands, memory, prng };
  }

  const { mistake, next } = rollMistake(memory.skillDial, prng);
  const nextMemory: BotMemory = { ...memory, nextActionTick: tick + reactionDelayTicks(memory.skillDial) };
  if (mistake) {
    return { commands, memory: nextMemory, prng: next };
  }

  if (self.role === "tank") commands.push(...tankDecide(view, self));
  else if (self.role === "healer") commands.push(...healerDecide(view, self));
  else if (self.role === "damage") commands.push(...damageDecide(view, self));

  return { commands, memory: nextMemory, prng: next };
}

export function makeBot(): Bot {
  return { decide: decideForActor };
}

/** Runs every bot's decide() in ascending actorId order, threading the PRNG state through. */
export function runBots(
  view: BotView,
  bots: Record<ActorId, Bot>,
  memories: Record<ActorId, BotMemory>,
  prng: PrngState
): { commands: Command[]; memories: Record<ActorId, BotMemory>; prng: PrngState } {
  const commands: Command[] = [];
  const nextMemories: Record<ActorId, BotMemory> = { ...memories };
  let nextPrng = prng;
  const ids = Object.keys(bots)
    .map(Number)
    .sort((a, b) => a - b);
  for (const id of ids) {
    const bot = bots[id];
    if (!bot) continue;
    const memory = memories[id] ?? defaultMemory();
    const result = bot.decide(view, memory, id, nextPrng);
    commands.push(...result.commands);
    nextMemories[id] = result.memory;
    nextPrng = result.prng;
  }
  return { commands, memories: nextMemories, prng: nextPrng };
}
