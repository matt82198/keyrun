/**
 * tests/sim/balance/harness.ts
 *
 * Full-dungeon seeded sim harness for the key-2 survivability metric. One call plays one complete
 * Hollow Vault run through the public path -- real content (loadContent), the real client composer
 * (buildRunSetup, human = Warden in actor slot 1), createRun/stepRun, runBots + buildBotView with
 * the real telegraph mapper, bots at the playable client's skill dial -- with the human slot either
 * IDLE (sends nothing) or driven by the scripted competent human (human-warden.ts).
 *
 * Usage: `const o = playRun(3, "competent"); console.log(formatTable([o]));`
 */
import { loadContent } from "@content/index";
import { createRun, stepRun, type Run } from "@sim/index";
import type { SimEvent } from "@sim/core/events";
import type { Command } from "@sim/core/commands";
import { makeBot, runBots, defaultMemory, type Bot, type BotMemory } from "@bots/src/bot";
import { buildBotView } from "@bots/src/view";
import { seedPrng } from "@bots/src/skill";
import { buildRunSetup } from "../../../client/src/sim-host/run-setup.js";
import { toBotTelegraph } from "../../../client/src/sim-host/telegraph-map.js";
import { makeHumanWarden } from "./human-warden.js";
import { BALANCE_DUNGEON, BALANCE_KEY_LEVEL, BOT_SKILL, MAX_TICKS, humanReactionTicks } from "./seeds.js";

export type HumanMode = "competent" | "idle";
const EMPTY_KIT = { rotation: [], interruptAbilityId: null, defensiveAbilityId: null, healAbilityId: null };
/** The human always occupies actor slot 1 (content-party.ts buildParty). */
export const HUMAN_ACTOR_ID = 1;

export interface Death {
  tick: number;
  actorId: number;
  pull: number;
}

export interface RunOutcome {
  seed: number;
  mode: HumanMode;
  cleared: boolean;
  wiped: boolean;
  /** Pull index the run ended on (pulls.length when cleared); the boss is the last pull. */
  endPull: number;
  bossPull: number;
  endTick: number;
  deaths: Death[];
  /** [pullIndex][actorId] -> lowest HP that player reached while that pull was active. */
  minHpPull: Record<number, Record<number, number>>;
}

/** Optional per-tick observer (diagnostics only; never changes the run). */
export type StepObserver = (run: Run, events: readonly SimEvent[], commands: readonly Command[]) => void;

export function playRun(
  seed: number,
  mode: HumanMode,
  keyLevel: number = BALANCE_KEY_LEVEL,
  observe?: StepObserver,
): RunOutcome {
  const setup = buildRunSetup(loadContent(), { keyLevel, dungeonId: BALANCE_DUNGEON, humanClassId: "warden" });
  let run: Run = createRun(seed, setup.party, setup.dungeon, setup.content, setup.runOptions);
  const bots: Record<number, Bot> = {};
  let memories: Record<number, BotMemory> = {};
  for (const p of setup.party) {
    if (p.id === setup.humanActorId) continue;
    bots[p.id] = makeBot();
    memories[p.id] = defaultMemory(BOT_SKILL);
  }
  const human = mode === "competent" ? makeHumanWarden(humanReactionTicks()) : null;
  let prng = seedPrng(seed);
  const playerIds = new Set(setup.party.map((p) => p.id));
  const bossPull = setup.dungeon.pulls.length - 1;
  const out: RunOutcome = {
    seed, mode, cleared: false, wiped: false, endPull: 0, bossPull, endTick: 0, deaths: [], minHpPull: {},
  };

  for (let t = 0; t < MAX_TICKS && run.runEnd.result === null; t++) {
    const pull = run.encounter.pullIndex;
    const view = buildBotView(run.state, run.encounter.telegraphs.map(toBotTelegraph), EMPTY_KIT);
    const r = runBots(view, bots, memories, prng, []);
    memories = r.memories;
    prng = r.prng;
    const commands = human ? [...human.decide(view, setup.humanActorId), ...r.commands] : r.commands;
    const stepped = stepRun(run, commands);
    run = stepped.run;
    observe?.(run, stepped.events, commands);
    for (const e of stepped.events) {
      const actorId = Number(e.actorId);
      if (e.type === "death" && playerIds.has(actorId)) out.deaths.push({ tick: e.tick, actorId, pull });
    }
    const hp = (out.minHpPull[pull] ??= {});
    const players = Object.values(run.state.actors).filter((a) => a.kind === "player");
    for (const a of players) hp[a.id] = Math.min(hp[a.id] ?? a.maxHealth, a.health);
    if (players.every((a) => !a.alive)) out.wiped = true;
  }
  out.endPull = run.encounter.pullIndex;
  out.cleared = run.encounter.pullIndex >= setup.dungeon.pulls.length;
  out.endTick = run.state.tick;
  return out;
}

function deathsOnPull(o: RunOutcome, pull: number): number {
  return o.deaths.filter((d) => d.pull === pull).length;
}

/** Bot (non-human) deaths on the first two pulls -- covers both "pull 1" readings (0- or 1-indexed). */
export function earlyBotDeaths(o: RunOutcome): number {
  return o.deaths.filter((d) => d.pull <= 1 && d.actorId !== HUMAN_ACTOR_ID).length;
}

function minHp(o: RunOutcome, pull: number, actorId: number): string {
  const v = o.minHpPull[pull]?.[actorId];
  return v === undefined ? "-" : String(v);
}

/** One markdown row per run. */
export function formatTable(rows: readonly RunOutcome[]): string {
  const head =
    "| seed | mode | cleared | wiped | end pull | deaths | pull-0 deaths | pull-1 deaths | early bot deaths | warden min HP p0/p1 | end tick |";
  const sep = "|---|---|---|---|---|---|---|---|---|---|---|";
  const body = rows.map((o) => {
    const endPull = o.cleared ? "done" : o.endPull === o.bossPull ? `${o.endPull} (boss)` : `${o.endPull}`;
    const cells = [
      o.seed, o.mode, o.cleared ? "yes" : "no", o.wiped ? "yes" : "no", endPull, o.deaths.length,
      deathsOnPull(o, 0), deathsOnPull(o, 1), earlyBotDeaths(o),
      `${minHp(o, 0, HUMAN_ACTOR_ID)}/${minHp(o, 1, HUMAN_ACTOR_ID)}`, o.endTick,
    ];
    return `| ${cells.join(" | ")} |`;
  });
  return [head, sep, ...body].join("\n");
}

export interface MetricVerdict {
  a: number;
  b: number;
  c: number;
}

/** (a) competent clears with 0-2 deaths; (b) idle wipes before the boss; (c) competent runs with no
 * bot death on the first two pulls (earlyBotDeaths). Each is a count out of the runs given. */
export function scoreMetric(competent: readonly RunOutcome[], idle: readonly RunOutcome[]): MetricVerdict {
  return {
    a: competent.filter((o) => o.cleared && o.deaths.length <= 2).length,
    b: idle.filter((o) => !o.cleared && o.wiped && o.endPull < o.bossPull).length,
    c: competent.filter((o) => earlyBotDeaths(o) === 0).length,
  };
}
