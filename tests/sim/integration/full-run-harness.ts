/**
 * tests/sim/integration/full-run-harness.ts
 *
 * Headless whole-dungeon harness: the real content registry (content/index.ts's loadContent), the
 * real client run setup (client/src/sim-host/run-setup.ts's buildRunSetup, which calls
 * content-dungeon.ts/content-party.ts), and an ALL-BOT party (the human slot is driven by a bot
 * too, so the run needs no input) stepped through sim/index.ts's public createRun/stepRun until the
 * run ends or `maxTicks` elapses. Imported read-only by tests; never by production code.
 *
 * Usage: `const r = runFullDungeon({ seed: 7, keyLevel: 2 }); r.result; r.lines;`
 */
import { loadContent } from "../../../content/index";
import { buildRunSetup } from "../../../client/src/sim-host/run-setup";
import { computeBotCommands } from "../../../client/src/sim-host/bot-commands";
import { createRun, stepRun, type Run } from "../../../sim/index";
import type { ActorId } from "../../../sim/core/state";
import type { SimEvent } from "../../../sim/core/events";
import { makeBot, defaultMemory, type Bot, type BotMemory } from "../../../bots/src/bot";
import { seedPrng } from "../../../bots/src/skill";

export interface FullRunOptions {
  seed: number;
  keyLevel: number;
  maxTicks?: number;
  skill?: number;
}

export interface FullRunResult {
  result: string | null;
  endTick: number | null;
  bossActorId: ActorId;
  events: SimEvent[];
  run: Run;
  /** Human-readable summary lines (pull clears, boss HP marks, Lock/combo/enrage/stagger events). */
  lines: string[];
}

const KEY_EVENT_TYPES = new Set([
  "pull_start", "pull_clear", "lock_start", "lock_enrage", "stagger", "run_end", "death", "stack_split",
]);

export function runFullDungeon(opts: FullRunOptions): FullRunResult {
  const registry = loadContent();
  const setup = buildRunSetup(registry, { keyLevel: opts.keyLevel, dungeonId: "hollow-vault", humanClassId: "warden" });
  let run = createRun(opts.seed, setup.party, setup.dungeon, setup.content, setup.runOptions);
  const bots: Record<ActorId, Bot> = {};
  let memories: Record<ActorId, BotMemory> = {};
  for (const member of setup.party) {
    bots[member.id] = makeBot();
    memories[member.id] = defaultMemory(opts.skill ?? 80);
  }
  let prng = seedPrng(opts.seed);
  const events: SimEvent[] = [];
  const lines: string[] = [];
  const maxTicks = opts.maxTicks ?? 20000;
  let lastBossMark = 100;
  for (let i = 0; i < maxTicks && run.runEnd.result === null; i += 1) {
    const bot = computeBotCommands(run, bots, memories, prng, []);
    memories = bot.memories;
    prng = bot.prng;
    const stepped = stepRun(run, bot.commands);
    run = stepped.run;
    for (const e of stepped.events) {
      events.push(e);
      if (e.type === "window_opened" && e.windowId === "lock") lines.push(`t${e.tick} lock window opened`);
      else if (e.type === "window_expired" && e.lock === true) lines.push(`t${e.tick} lock FAILED (window expired)`);
      else if (e.type === "combo" && e.windowId === "lock") lines.push(`t${e.tick} lock CLOSED by ${String(e.payoffClass)} (${String(e.comboId)})`);
      else if (e.type === "combo" && e.field !== true) lines.push(`t${e.tick} combo ${String(e.comboId)}`);
      else if (KEY_EVENT_TYPES.has(e.type)) {
        const rest = Object.entries(e).filter(([k]) => k !== "tick" && k !== "type").map(([k, v]) => `${k}=${JSON.stringify(v)}`).join(" ");
        lines.push(`t${e.tick} ${e.type} ${rest}`);
      }
    }
    const boss = run.state.actors[setup.bossActorId];
    if (boss && boss.alive) {
      const pct = Math.floor((boss.health * 100) / boss.maxHealth);
      const mark = Math.floor(pct / 25) * 25;
      if (mark < lastBossMark) {
        lines.push(`t${run.state.tick} boss hp ${boss.health}/${boss.maxHealth} (${pct}%)`);
        lastBossMark = mark;
      }
    }
  }
  return { result: run.runEnd.result, endTick: run.runEnd.resultTick, bossActorId: setup.bossActorId, events, run, lines };
}
