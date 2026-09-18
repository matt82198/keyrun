/**
 * client/src/sim-host/bot-commands.ts
 *
 * Groups the run's bots by class (each class needs its own BotView.classKit -- see
 * bots/src/types.ts; bots/src/bot.ts's runBots() takes exactly one classKit per call) and runs
 * them, threading the shared PRNG state through in ascending-class-then-actorId order so results
 * stay deterministic. Split out of worker.ts to keep it under the repo's 190-LOC-per-file gate.
 *
 * Usage: `const result = computeBotCommands(run, setup, bots, memories, prng);`
 */
import type { Run } from "../../../sim/index.js";
import type { ActorId } from "../../../sim/core/state.js";
import type { Command } from "../../../sim/core/commands.js";
import type { Bot, BotMemory } from "../../../bots/src/bot.js";
import { runBots, defaultMemory } from "../../../bots/src/bot.js";
import type { BotView } from "../../../bots/src/types.js";
import type { PrngState } from "../../../bots/src/skill.js";
import type { RunSetup } from "./run-setup.js";
import { toBotTelegraph } from "./telegraph-map.js";

export interface BotCommandsResult {
  commands: Command[];
  memories: Record<ActorId, BotMemory>;
  prng: PrngState;
}

export function computeBotCommands(
  run: Run,
  setup: RunSetup,
  bots: Record<ActorId, Bot>,
  memories: Record<ActorId, BotMemory>,
  prng: PrngState,
): BotCommandsResult {
  const telegraphs = run.encounter.telegraphs.map(toBotTelegraph);
  const idsByClass = new Map<string, ActorId[]>();
  for (const [idStr, classId] of Object.entries(setup.actorClassId)) {
    const id = Number(idStr);
    if (!bots[id]) continue;
    idsByClass.set(classId, [...(idsByClass.get(classId) ?? []), id]);
  }

  let commands: Command[] = [];
  let nextMemories = memories;
  let nextPrng = prng;
  for (const [classId, ids] of idsByClass) {
    const classKit = setup.classKits[classId];
    if (!classKit) continue;
    const view: BotView = { state: run.state, telegraphs, classKit };
    const botsForClass: Record<number, Bot> = {};
    const memoriesForClass: Record<number, BotMemory> = {};
    for (const id of ids) {
      const bot = bots[id];
      if (!bot) continue;
      botsForClass[id] = bot;
      memoriesForClass[id] = nextMemories[id] ?? defaultMemory();
    }
    const result = runBots(view, botsForClass, memoriesForClass, nextPrng);
    commands = commands.concat(result.commands);
    nextMemories = { ...nextMemories, ...result.memories };
    nextPrng = result.prng;
  }
  return { commands, memories: nextMemories, prng: nextPrng };
}
