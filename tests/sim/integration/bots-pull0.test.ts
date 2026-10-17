/**
 * tests/sim/integration/bots-pull0.test.ts
 *
 * P0 (D10 "one complete run Matt can play end to end"): the bot party must clear Hollow Vault's
 * pull 0 (the Choir: two acolytes + a cantor) with an IDLE human in the party. Real content
 * (content/index.ts's loadContent), the real client composer (client/src/sim-host/run-setup.ts's
 * buildRunSetup, which also injects the bots' ContentRegistry), the real createRun/stepRun, the
 * real bots/src/bot.ts runBots() + buildBotView, and the real telegraph mapper -- the same path
 * server/src/room.ts and the client SimRuntime drive. The human (cinder, actor 1) sends nothing.
 *
 * Before the fix (PR #79 lane's verified wipe): two cinder bots died at tick 273, the tank at
 * 507, the third cinder at 819, enemies left at 1510/6020/5430 HP. Root causes (bots only):
 *   1. the tank bot never taunted, never walked to the pack and never cast its rotation --
 *      sim adds threat only via explicit threat ops, so enemies stayed on "nearest player";
 *   2. the healer cast Chanter's SELF-shaped Refrain "at" dying allies, healing only itself.
 * On failure the assertion message carries a per-100-tick timeline (party HP, enemy HP and
 * enemy target ids) for diagnosis.
 *
 * Usage: `npx vitest run tests/sim/integration/bots-pull0.test.ts`
 */
import { describe, expect, it } from "vitest";
import { loadContent } from "@content/index";
import { createRun, stepRun, type Run } from "@sim/index";
import { makeBot, runBots, defaultMemory, type Bot, type BotMemory } from "@bots/src/bot";
import { buildBotView } from "@bots/src/view";
import { seedPrng } from "@bots/src/skill";
import { buildRunSetup } from "../../../client/src/sim-host/run-setup.js";
import { toBotTelegraph } from "../../../client/src/sim-host/telegraph-map.js";

const MAX_TICKS = 3000; // 150s at 20Hz: generous ceiling, the fixed party clears in ~1500
const EMPTY_KIT = { rotation: [], interruptAbilityId: null, defensiveAbilityId: null, healAbilityId: null };

interface Outcome {
  clearTick: number | null;
  deaths: { tick: number; actorId: number }[];
  partyHpAtClear: Record<number, number>;
  timeline: string[];
}

function playPull0(humanClassId: string): Outcome {
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
  const out: Outcome = { clearTick: null, deaths: [], partyHpAtClear: {}, timeline: [] };
  const playerIds = new Set(setup.party.map((p) => p.id));

  for (let t = 0; t < MAX_TICKS && out.clearTick === null; t++) {
    const view = buildBotView(run.state, run.encounter.telegraphs.map(toBotTelegraph), EMPTY_KIT);
    const botResult = runBots(view, bots, memories, prng, []);
    memories = botResult.memories;
    prng = botResult.prng;
    const stepped = stepRun(run, botResult.commands);
    run = stepped.run;
    for (const e of stepped.events) {
      const actorId = Number(e.actorId); // SimEvent is a loose record: payload fields are unknown
      if (e.type === "death" && playerIds.has(actorId)) out.deaths.push({ tick: e.tick, actorId });
      if (e.type === "pull_clear" && e.pullIndex === 0) out.clearTick = e.tick;
    }
    if (t % 100 === 0 || out.clearTick !== null) {
      const actors = Object.values(run.state.actors);
      const party = actors.filter((a) => a.kind === "player").map((a) => `${a.id}:${a.health}`).join(" ");
      const foes = actors.filter((a) => a.kind === "enemy" && a.alive).map((a) => `${a.id}:${a.health}->${a.targetId}`).join(" ");
      out.timeline.push(`t${t} party[${party}] enemies[${foes}]`);
    }
  }
  for (const a of Object.values(run.state.actors)) if (a.kind === "player") out.partyHpAtClear[a.id] = a.health;
  return out;
}

describe("bots clear Hollow Vault pull 0 with an idle human", () => {
  it("idle cinder human: pull 0 clears with no party deaths", () => {
    const o = playPull0("cinder");
    const why = o.timeline.join("\n");
    // PULL0_TRACE=1 prints the timeline + outcome even when green (diagnosis aid).
    if (process.env.PULL0_TRACE) console.log(`${why}\nclearTick=${o.clearTick} hp=${JSON.stringify(o.partyHpAtClear)}`);
    expect(o.clearTick, `pull 0 never cleared\n${why}`).not.toBeNull();
    expect(o.clearTick!, why).toBeLessThan(MAX_TICKS);
    expect(o.deaths, `party deaths before the clear\n${why}`).toEqual([]);
    // The tank finishes the pull standing (it held the pack the whole time).
    expect(o.partyHpAtClear[2]!, why).toBeGreaterThan(0);
  }, 60_000);
});
