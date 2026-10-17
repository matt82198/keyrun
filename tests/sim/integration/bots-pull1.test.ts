/**
 * tests/sim/integration/bots-pull1.test.ts
 *
 * P0 (D10 "one complete run Matt can play end to end"): after pull 0 (the Choir), the bot party
 * must also clear Hollow Vault's pull 1 (the Pair: five Paired Warriors) with an IDLE human in the
 * party and no deaths. Same public path as bots-pull0.test.ts: real content (loadContent), the real
 * client composer (buildRunSetup, which also injects the bots' ContentRegistry), the real
 * createRun/stepRun, the real runBots() + buildBotView and the real telegraph mapper. The human
 * (cinder, actor 1) sends nothing.
 *
 * On failure the assertion message carries a per-100-tick timeline: party HP, each living enemy's
 * HP -> target and its top threat holder, plus the damage taken / heals received per player since
 * the previous line. PULL1_TRACE=1 prints the timeline even when green.
 *
 * Usage: `npx vitest run tests/sim/integration/bots-pull1.test.ts`
 */
import { describe, expect, it } from "vitest";
import { loadContent } from "@content/index";
import { createRun, stepRun, type Run } from "@sim/index";
import { makeBot, runBots, defaultMemory, type Bot, type BotMemory } from "@bots/src/bot";
import { buildBotView } from "@bots/src/view";
import { seedPrng } from "@bots/src/skill";
import { buildRunSetup } from "../../../client/src/sim-host/run-setup.js";
import { toBotTelegraph } from "../../../client/src/sim-host/telegraph-map.js";

const MAX_TICKS = 6000; // 300s at 20Hz: pulls 0 + 1 with headroom
const EMPTY_KIT = { rotation: [], interruptAbilityId: null, defensiveAbilityId: null, healAbilityId: null };

interface Outcome {
  clearTicks: Record<number, number>;
  deaths: { tick: number; actorId: number; sourceId: number }[];
  partyHpAtClear1: Record<number, number>;
  timeline: string[];
}

function topThreat(threat: Record<number, number>): string {
  const entries = Object.entries(threat).sort((a, b) => b[1] - a[1]);
  return entries.length > 0 ? `${entries[0]![0]}` : "-";
}

function playThroughPull(humanClassId: string, lastPull: number): Outcome {
  const setup = buildRunSetup(loadContent(), { keyLevel: 5, dungeonId: "hollow-vault", humanClassId });
  let run: Run = createRun(1, setup.party, setup.dungeon, setup.content, setup.runOptions);
  const bots: Record<number, Bot> = {};
  let memories: Record<number, BotMemory> = {};
  for (const p of setup.party) {
    if (p.id === setup.humanActorId) continue; // idle human: no bot, no commands
    bots[p.id] = makeBot();
    memories[p.id] = defaultMemory(100);
  }
  let prng = seedPrng(7);
  const out: Outcome = { clearTicks: {}, deaths: [], partyHpAtClear1: {}, timeline: [] };
  const playerIds = new Set(setup.party.map((p) => p.id));
  let taken: Record<number, number> = {};
  let healed: Record<number, number> = {};

  for (let t = 0; t < MAX_TICKS && out.clearTicks[lastPull] === undefined; t++) {
    const view = buildBotView(run.state, run.encounter.telegraphs.map(toBotTelegraph), EMPTY_KIT);
    const botResult = runBots(view, bots, memories, prng, []);
    memories = botResult.memories;
    prng = botResult.prng;
    const stepped = stepRun(run, botResult.commands);
    run = stepped.run;
    for (const e of stepped.events) {
      const actorId = Number(e.actorId); // SimEvent is a loose record: payload fields are unknown
      const targetId = Number(e.targetId);
      if (e.type === "death" && playerIds.has(actorId)) out.deaths.push({ tick: e.tick, actorId, sourceId: Number(e.sourceId) });
      if (e.type === "pull_clear") out.clearTicks[Number(e.pullIndex)] = e.tick;
      if (e.type === "damage" && playerIds.has(targetId)) taken[targetId] = (taken[targetId] ?? 0) + Number(e.healthDamage);
      if (e.type === "heal" && playerIds.has(targetId)) healed[targetId] = (healed[targetId] ?? 0) + Number(e.amount);
    }
    if (t % 100 === 0 || out.clearTicks[lastPull] !== undefined) {
      const actors = Object.values(run.state.actors);
      const party = actors.filter((a) => a.kind === "player").map((a) => `${a.id}:${a.health}`).join(" ");
      const foes = actors
        .filter((a) => a.kind === "enemy" && a.alive)
        .map((a) => `${a.id}:${a.health}->${a.targetId}(top${topThreat(a.threat)})`)
        .join(" ");
      out.timeline.push(`t${t} party[${party}] enemies[${foes}] dmgTaken${JSON.stringify(taken)} healed${JSON.stringify(healed)}`);
      taken = {};
      healed = {};
    }
  }
  for (const a of Object.values(run.state.actors)) if (a.kind === "player") out.partyHpAtClear1[a.id] = a.health;
  return out;
}

describe("bots clear Hollow Vault pull 1 (the Pair) with an idle human", () => {
  it("idle cinder human: pulls 0 and 1 clear with no party deaths", () => {
    const o = playThroughPull("cinder", 1);
    const why = `${o.timeline.join("\n")}\nfirst party death: ${JSON.stringify(o.deaths[0] ?? null)}`;
    if (process.env.PULL1_TRACE) {
      console.log(`${why}\nclearTicks=${JSON.stringify(o.clearTicks)} deaths=${JSON.stringify(o.deaths)} hp=${JSON.stringify(o.partyHpAtClear1)}`);
    }
    expect(o.clearTicks[0], `pull 0 never cleared\n${why}`).toBeDefined();
    expect(o.clearTicks[1], `pull 1 never cleared\n${why}`).toBeDefined();
    expect(o.deaths, `party deaths before the pull-1 clear\n${why}`).toEqual([]);
    expect(o.partyHpAtClear1[2]!, why).toBeGreaterThan(0);
  }, 120_000);
});
