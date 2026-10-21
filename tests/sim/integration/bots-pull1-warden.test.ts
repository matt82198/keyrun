/**
 * tests/sim/integration/bots-pull1-warden.test.ts
 *
 * Hollow Vault key 5, pull 1 (the Pair: five Paired Warriors) with the WARDEN in the human slot
 * (actor 1) and a bot driving it, so the whole party is bots and the tank is active.
 * bots-pull1.test.ts puts an idle cinder in the human slot, which never exercised this path.
 *
 * The defect (deterministic, identical across seeds): pull 0 ends on a lone Choir healer aimed at
 * its own pack (a lull). The Warden sat at exactly 280/400 -- the healer's strict `< 0.7` triage
 * never picked it -- and dumped Reckoning into the healer at t871, GCD-locking itself to t901 with
 * 0 momentum. Pull 1's warriors spawned at t873 already aimed at the tank and four of them swung
 * for 70 each at t888: 280 damage on a 280-HP tank, dead before Brace (GCD-gated) could be cast.
 * Fix: during the lull (roles/shared.ts inLull) the tank banks its GCD and momentum
 * (roles/tank-pressure.ts pressureRotation), so it meets pull 1 with Brace castable at spawn.
 *
 * Same public path as bots-pull1.test.ts: real content, buildRunSetup, createRun/stepRun,
 * runBots + buildBotView and the real telegraph mapper. The run seed and the bot PRNG seed are
 * both swept (SEEDS); PULL1W_TRACE=1 prints the per-seed outcome line.
 *
 * Usage: `npx vitest run tests/sim/integration/bots-pull1-warden.test.ts`
 */
import { describe, expect, it } from "vitest";
import { loadContent } from "@content/index";
import { createRun, stepRun, type Run } from "@sim/index";
import { makeBot, runBots, defaultMemory, type Bot, type BotMemory } from "@bots/src/bot";
import { buildBotView } from "@bots/src/view";
import { seedPrng } from "@bots/src/skill";
import { inLull } from "@bots/src/roles/shared";
import type { ActorState, BotView, SimState } from "@bots/src/types";
import { buildRunSetup } from "../../../client/src/sim-host/run-setup.js";
import { toBotTelegraph } from "../../../client/src/sim-host/telegraph-map.js";

const MAX_TICKS = 6000;
const SURVIVE_TICKS = 60;
const SEEDS = [1, 2, 3, 4, 5];
const EMPTY_KIT = { rotation: [], interruptAbilityId: null, defensiveAbilityId: null, healAbilityId: null };

interface Outcome {
  pull1StartTick: number | null;
  clear1Tick: number | null;
  tankDeathTick: number | null;
  deaths: { tick: number; actorId: number }[];
  wiped: boolean;
}

function play(seed: number): Outcome {
  const setup = buildRunSetup(loadContent(), { keyLevel: 5, dungeonId: "hollow-vault", humanClassId: "warden" });
  let run: Run = createRun(seed, setup.party, setup.dungeon, setup.content, setup.runOptions);
  const bots: Record<number, Bot> = {};
  let memories: Record<number, BotMemory> = {};
  for (const p of setup.party) {
    bots[p.id] = makeBot(); // the warden human slot is bot-driven too
    memories[p.id] = defaultMemory(100);
  }
  let prng = seedPrng(seed);
  const tankId = setup.humanActorId; // the warden occupies the human slot
  const playerIds = new Set(setup.party.map((p) => p.id));
  const out: Outcome = { pull1StartTick: null, clear1Tick: null, tankDeathTick: null, deaths: [], wiped: false };
  let clear0: number | null = null;

  for (let t = 0; t < MAX_TICKS && out.clear1Tick === null; t++) {
    const view = buildBotView(run.state, run.encounter.telegraphs.map(toBotTelegraph), EMPTY_KIT);
    const r = runBots(view, bots, memories, prng, []);
    memories = r.memories;
    prng = r.prng;
    const stepped = stepRun(run, r.commands);
    run = stepped.run;
    for (const e of stepped.events) {
      const actorId = Number(e.actorId); // SimEvent is a loose record: payload fields are unknown
      if (e.type === "pull_clear" && Number(e.pullIndex) === 0) clear0 = e.tick;
      if (e.type === "pull_clear" && Number(e.pullIndex) === 1) out.clear1Tick = e.tick;
      if (e.type === "death" && playerIds.has(actorId)) {
        out.deaths.push({ tick: e.tick, actorId });
        if (actorId === tankId && out.tankDeathTick === null) out.tankDeathTick = e.tick;
      }
      // Pull 1 engages at the first enemy damage on a player after pull 0 clears.
      if (
        e.type === "damage" && clear0 !== null && out.pull1StartTick === null &&
        playerIds.has(Number(e.targetId)) && !playerIds.has(Number(e.sourceId))
      ) {
        out.pull1StartTick = e.tick;
      }
    }
    if (Object.values(run.state.actors).filter((a) => a.kind === "player").every((a) => !a.alive)) {
      out.wiped = true;
      break;
    }
  }
  return out;
}

function line(seed: number, o: Outcome): string {
  return `seed=${seed} pull1Start=${o.pull1StartTick} tankDeath=${o.tankDeathTick ?? "none"} clear1=${o.clear1Tick ?? "none"} wiped=${o.wiped} deaths=${JSON.stringify(o.deaths)}`;
}

describe("Hollow Vault key 5 pull 1 with a bot warden in the human slot", () => {
  for (const seed of SEEDS) {
    it(`seed ${seed}: the tank survives the opening volley and pull 1 clears with no deaths`, () => {
      const o = play(seed);
      const why = line(seed, o);
      if (process.env.PULL1W_TRACE) console.log(why);
      expect(o.pull1StartTick, `pull 1 never engaged\n${why}`).not.toBeNull();
      const survivedWindow = o.tankDeathTick === null || o.tankDeathTick > o.pull1StartTick! + SURVIVE_TICKS;
      expect(survivedWindow, `tank died within ${SURVIVE_TICKS} ticks of pull 1\n${why}`).toBe(true);
      expect(o.wiped, `party wiped\n${why}`).toBe(false);
      expect(o.clear1Tick, `pull 1 never cleared\n${why}`).not.toBeNull();
      expect(o.deaths, `party deaths before the pull-1 clear\n${why}`).toEqual([]);
    }, 120_000);
  }
});

function actor(o: Partial<ActorState>): ActorState {
  return {
    id: 1, kind: "player", classId: "priest", role: "healer", pos: { x: 0, y: 0, z: 0 }, yaw: 0,
    health: 100, maxHealth: 100, resource: 0, maxResource: 0, alive: true, targetId: null,
    castId: null, castEndsTick: null, castStartTick: null, cooldowns: {}, gcdReadyTick: 0,
    effects: [], threat: {}, ...o,
  };
}

function viewOf(actors: ActorState[]): BotView {
  const state: SimState = {
    tick: 10, seed: 1, prng: [1, 2, 3, 4], actors: Object.fromEntries(actors.map((a) => [a.id, a])),
    runTimerTicks: 0, phase: "combat", log: [],
  };
  return { state, telegraphs: [], classKit: { rotation: [], interruptAbilityId: null, defensiveAbilityId: null, healAbilityId: "flash-heal" } };
}

describe("lull signal (roles/shared.ts inLull)", () => {
  const healer = actor({ id: 1, role: "healer" });
  const tank = actor({ id: 2, role: "tank", pos: { x: 1, y: 0, z: 0 } });

  // R-SEED-1a (finding f-244d102b): a living enemy is engaged whatever it is aimed at, so a caster tending its own pack
  // is NOT a lull and the tank fights it. The C# rule (RoleShared.InLull) is "every living enemy is idle"; the TS sim
  // has no idle state, so no living enemy is idle here and inLull is false in every row, including the pack-aimed one.
  it("a living enemy is engaged, whatever it is aimed at, so it is not a lull (R-SEED-1a, f-244d102b)", () => {
    const healerFoe = (targetId: number | null) => actor({ id: 102, kind: "enemy", role: "damage", targetId });
    const ally = actor({ id: 101, kind: "enemy", role: "damage", alive: false });
    expect(inLull(viewOf([healer, tank, ally, healerFoe(101)]))).toBe(false);
    expect(inLull(viewOf([healer, tank, ally, healerFoe(102)]))).toBe(false);
    expect(inLull(viewOf([healer, tank, ally, healerFoe(2)]))).toBe(false);
    expect(inLull(viewOf([healer, tank, ally, healerFoe(null)]))).toBe(false);
  });

  it("no living enemy is never a lull (at least one living enemy is required)", () => {
    expect(inLull(viewOf([healer, tank]))).toBe(false);
  });
});
