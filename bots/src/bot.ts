/**
 * bots/src/bot.ts
 *
 * The Bot interface and runBots() batch runner. Priority order each tick, highest first:
 *   1. Danger-telegraph avoidance -- always overrides everything below it, skill-dial-free.
 *   2. Party-ping reactions (bots/src/pings-policy.ts) -- also skill-dial-free ("pings are
 *      urgent party calls, not rotation"); if a ping reaction includes a cast, role dispatch is
 *      skipped this tick so a bot never issues two casts in one tick.
 *   3. Role decisions (target acquisition, rotation, heals, interrupts -- see roles.ts), gated by
 *      the skill dial's reaction delay and mistake roll.
 *
 * Usage: `import { makeBot, runBots, defaultMemory } from "./bot.js"`
 */
import type { ActorId, ActorState, BotView, Command, Tick } from "./types.js";
import { isInAnyDanger, nearestSafePoint } from "./shared/positioning.js";
import { moveToward } from "./shared/command.js";
import { type PrngState, reactionDelayTicks, rollMistake } from "./skill.js";
import { tankDecide, healerDecide, damageDecide } from "./roles.js";
import { pingReactionCommands } from "./pings-policy.js";
import type { PingEvent } from "./shared/pings.js";
import { isSpent } from "./shared/floor.js";

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
    prng: PrngState,
    pings?: readonly PingEvent[]
  ): { commands: Command[]; memory: BotMemory; prng: PrngState };
}

function dangerAvoidCommand(view: BotView, self: ActorState): Command | null {
  const tick = view.state.tick;
  const selfMM = { x: self.pos.x, z: self.pos.z };
  // DEAD-CASTER-TELEGRAPH: a telegraph whose caster died before its cast began warns of nothing (shared/floor.ts isSpent).
  const active = view.telegraphs.filter((t) => t.danger && t.resolvesTick > tick && !isSpent(t, view.state));
  if (!isInAnyDanger(selfMM, active)) return null;
  const safe = nearestSafePoint(selfMM, active);
  if (!safe) return null;
  return moveToward(tick, self.id, selfMM, safe);
}

function decideForActor(
  view: BotView,
  memory: BotMemory,
  actorId: ActorId,
  prng: PrngState,
  pings: readonly PingEvent[] = []
): { commands: Command[]; memory: BotMemory; prng: PrngState } {
  const self = view.state.actors[actorId];
  if (!self || !self.alive) return { commands: [], memory, prng };
  const tick = view.state.tick;

  const avoid = dangerAvoidCommand(view, self);
  const commands: Command[] = avoid ? [avoid] : [];

  const pingCommands = pingReactionCommands(view, self, pings);
  commands.push(...pingCommands);
  const pingCast = pingCommands.some((c) => c.kind === "cast");

  if (tick < memory.nextActionTick) {
    return { commands, memory, prng };
  }

  const { mistake, next } = rollMistake(memory.skillDial, prng);
  const nextMemory: BotMemory = { ...memory, nextActionTick: tick + reactionDelayTicks(memory.skillDial) };
  if (mistake) {
    return { commands, memory: nextMemory, prng: next };
  }

  if (!pingCast) {
    if (self.role === "tank") commands.push(...tankDecide(view, self));
    else if (self.role === "healer") commands.push(...healerDecide(view, self));
    else if (self.role === "damage") commands.push(...damageDecide(view, self));
  }

  return { commands, memory: nextMemory, prng: next };
}

export function makeBot(): Bot {
  return { decide: decideForActor };
}

/**
 * Runs every bot's decide() in ascending actorId order, threading the PRNG state through.
 * `pings` (default: none) is forwarded to every bot's decide() unchanged -- one snapshot of live
 * party pings per tick, same as `view` is one state snapshot per tick.
 */
export function runBots(
  view: BotView,
  bots: Record<ActorId, Bot>,
  memories: Record<ActorId, BotMemory>,
  prng: PrngState,
  pings: readonly PingEvent[] = []
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
    const result = bot.decide(view, memory, id, nextPrng, pings);
    commands.push(...result.commands);
    nextMemories[id] = result.memory;
    nextPrng = result.prng;
  }
  return { commands, memories: nextMemories, prng: nextPrng };
}
