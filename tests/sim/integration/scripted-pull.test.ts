/**
 * tests/sim/integration/scripted-pull.test.ts — drives sim/index.ts's createRun/stepRun through a
 * real 600-tick pull: the player casts on a loop, the boss's enemy script fires on its own loop
 * with a telegraph lead, and a party member that never receives a command (DUMMY_ID -- see
 * fixtures.ts) fails to move out of the boss's cleave and takes the damage every cycle. Three more
 * party slots are filled by real bots/src bots each tick, so the run exercises sim/core + sim/combat
 * + sim/encounter + bots/src together, not sim in isolation.
 */
import { describe, expect, it } from "vitest";
import { createRun, stepRun, type Run } from "@sim/index";
import { cloneState, hashState } from "@sim/core/state";
import { makeBot, runBots, defaultMemory, type Bot, type BotMemory } from "@bots/src/bot";
import { seedPrng, type PrngState } from "@bots/src/skill";
import {
  BOSS_ID,
  BOT_CLASS_KIT,
  BOT_IDS,
  CONTENT,
  DUMMY_ID,
  DUNGEON,
  FULL_PARTY,
  SEED,
  TICKS,
  mapTelegraphsForBots,
  scriptedPlayerCommands,
} from "./fixtures";

interface Harness {
  run: Run;
  bots: Record<number, Bot>;
  memories: Record<number, BotMemory>;
  prng: PrngState;
}

function freshHarness(seed: number): Harness {
  const run = createRun(seed, FULL_PARTY, DUNGEON, CONTENT);
  // Bots never do target acquisition themselves outside the tank role (see bots/src/roles.ts);
  // point them at the boss once, up front, so their rotation ability has a legal target.
  for (const id of BOT_IDS) {
    const actor = run.state.actors[id];
    if (actor) actor.targetId = BOSS_ID;
  }
  const bots: Record<number, Bot> = {};
  const memories: Record<number, BotMemory> = {};
  for (const id of BOT_IDS) {
    bots[id] = makeBot();
    memories[id] = defaultMemory(100); // perfect skill dial: no reaction delay, no mistakes
  }
  return { run, bots, memories, prng: seedPrng(seed) };
}

/** Advances `harness` from its current tick through (but not including) `untilTick`. */
function advance(harness: Harness, untilTick: number): Harness {
  let { run, memories, prng } = harness;
  const playerCommands = scriptedPlayerCommands(TICKS);
  while (run.state.tick < untilTick) {
    const tick = run.state.tick;
    const view = {
      state: run.state,
      telegraphs: mapTelegraphsForBots(run.encounter.telegraphs),
      classKit: BOT_CLASS_KIT,
    };
    const botResult = runBots(view, harness.bots, memories, prng);
    memories = botResult.memories;
    prng = botResult.prng;

    const commands = [...playerCommands.filter((c) => c.tick === tick), ...botResult.commands];
    const stepped = stepRun(run, commands);
    run = stepped.run;
  }
  return { run, bots: harness.bots, memories, prng };
}

function runFullScenario(seed: number): Run {
  return advance(freshHarness(seed), TICKS).run;
}

describe("scripted pull integration", () => {
  it("runs 600 ticks and produces the same final hash on a second independent run", () => {
    const a = runFullScenario(SEED);
    const b = runFullScenario(SEED);
    expect(hashState(a.state)).toBe(hashState(b.state));
  });

  it("matches a run continued from a mid-run cloneState with the uncloned continuation", () => {
    const harness = freshHarness(SEED);
    const midpoint = advance(harness, TICKS / 2);

    // Continuation A: keep advancing the live harness.
    const continuedLive = advance(midpoint, TICKS);

    // Continuation B: clone the mid-run state into a fresh Run and advance an independent harness
    // from there, reusing the same (already-advanced) bot memories/prng so both continuations see
    // identical inputs.
    const clonedRun: Run = { ...midpoint.run, state: cloneState(midpoint.run.state) };
    const continuedFromClone = advance(
      { run: clonedRun, bots: midpoint.bots, memories: midpoint.memories, prng: midpoint.prng },
      TICKS,
    );

    expect(hashState(continuedFromClone.run.state)).toBe(hashState(continuedLive.run.state));
    // The clone must not have been an alias: mutating the live continuation's state never touched
    // the cloned run's already-captured snapshot.
    expect(clonedRun.state).not.toBe(midpoint.run.state);
  });

  it("damages the party member that never moves, via the announced telegraph's real cast", () => {
    const { run } = advance(freshHarness(SEED), TICKS);
    const dummy = run.state.actors[DUMMY_ID]!;
    // 5 cleave casts land (ticks 100..500 inclusive) at 20 damage each; dummy never moves or heals.
    expect(dummy.health).toBe(300 - 5 * 20);
    expect(dummy.pos).toEqual({ x: 1000, y: 0, z: 0 }); // its spawn (fixtures.ts BASE_PARTY): it never moved
  });

  it("the player's looping casts and the bots' rotations both damaged the boss", () => {
    const { run } = advance(freshHarness(SEED), TICKS);
    const boss = run.state.actors[BOSS_ID]!;
    expect(boss.alive).toBe(true);
    expect(boss.health).toBeLessThan(5000);
    expect(boss.health).toBeGreaterThanOrEqual(0);
  });

  it("never produces NaN or out-of-range health across every actor", () => {
    const { run } = advance(freshHarness(SEED), TICKS);
    for (const actor of Object.values(run.state.actors)) {
      expect(Number.isNaN(actor.health)).toBe(false);
      expect(actor.health).toBeGreaterThanOrEqual(0);
      expect(actor.health).toBeLessThanOrEqual(actor.maxHealth);
    }
  });
});
