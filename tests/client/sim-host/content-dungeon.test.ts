import { describe, expect, it } from "vitest";
import { loadContent } from "@content/index";
import { buildDungeon } from "../../../client/src/sim-host/content-dungeon.js";
import { createState } from "../../../sim/core/state.js";
import { spawnPull } from "../../../sim/encounter/pulls.js";
import { stepEnemyScripts } from "../../../sim/encounter/scripts.js";
import { scaleCastEvery, scaleLeadTicks } from "../../../sim/encounter/tempo.js";

const registry = loadContent();

describe("content-dungeon: buildDungeon", () => {
  it("expands every pull's enemy counts and appends the boss as a final pull", () => {
    const { dungeon } = buildDungeon(registry, "hollow-vault", 5);
    // choir (2+1), pair (5), seed (1), boss (1) == 4 pulls
    expect(dungeon.pulls.map((p) => p.spawns.length)).toEqual([3, 5, 1, 1]);
    expect(dungeon.pulls[dungeon.pulls.length - 1]!.id).toBe("boss");
  });

  it("assigns every spawned enemy a distinct actorId", () => {
    const { dungeon } = buildDungeon(registry, "hollow-vault", 5);
    const ids = dungeon.pulls.flatMap((p) => p.spawns.map((s) => s.actorId));
    expect(new Set(ids).size).toBe(ids.length);
  });

  it("scales enemy health up with key level (sim/encounter/tempo.ts's scaleHealth)", () => {
    const low = buildDungeon(registry, "hollow-vault", 2);
    const high = buildDungeon(registry, "hollow-vault", 15);
    const lowHealth = low.dungeon.pulls[0]!.spawns[0]!.health;
    const highHealth = high.dungeon.pulls[0]!.spawns[0]!.health;
    expect(highHealth).toBeGreaterThan(lowHealth);
  });

  it("keeps both the loop step and the onDeath trigger (seed.elite)", () => {
    // STALE COMMENT FIX (death-trigger-wiring, backlog P1): this test used to assert the opposite
    // -- that "onDeath" was dropped -- on the claim that sim's ScriptStep type had no "onDeath"
    // variant. That was already false when written (sim/encounter/scripts.ts's ScriptStep.at is
    // "loop" | "onDeath" | number, landed by PR #28); `content/data/enemies/seed.json` simply had
    // no onDeath step yet, so buildDungeon's straight pass-through (scaleStep's non-"loop" branch)
    // never had one to carry. Now that seed.json declares `{"at":"onDeath","cast":
    // "seed.deathBloom"}` and sim/index.ts's real stepRun resolves it (see
    // tests/sim/integration/death.test.ts), buildDungeon correctly keeps it: worker.ts drives the
    // playable client through that same real stepRun, so this is what makes deathBloom fire there.
    const { dungeon, enemyScripts } = buildDungeon(registry, "hollow-vault", 5);
    const seedPull = dungeon.pulls.find((p) => p.spawns.some((s) => s.classId === "seed.elite"))!;
    const seedId = seedPull.spawns[0]!.actorId;
    const script = enemyScripts[seedId]!;
    expect(script.some((s) => s.cast === "seed.slam")).toBe(true);
    expect(script.some((s) => s.cast === "seed.deathBloom" && s.at === "onDeath")).toBe(true);
  });

  it("scales every enemy script step's cadence (loop `every`) with key level", () => {
    const low = buildDungeon(registry, "hollow-vault", 1);
    const high = buildDungeon(registry, "hollow-vault", 20);
    const pairPullLow = low.dungeon.pulls.find((p) => p.spawns.some((s) => s.classId === "pair.melee"))!;
    const pairPullHigh = high.dungeon.pulls.find((p) => p.spawns.some((s) => s.classId === "pair.melee"))!;
    const lowId = pairPullLow.spawns[0]!.actorId;
    const highId = pairPullHigh.spawns[0]!.actorId;
    const lowStep = low.enemyScripts[lowId]!.find((s) => s.cast === "pair.spreadMark")!;
    const highStep = high.enemyScripts[highId]!.find((s) => s.cast === "pair.spreadMark")!;
    expect(lowStep.at).toBe("loop");
    expect(highStep.at).toBe("loop");
    // Higher key level -> shorter `every` (faster cast cadence), never past sim/encounter/tempo.ts's floor.
    expect(highStep.every!).toBeLessThan(lowStep.every!);
  });

  it("throws on an unknown dungeon id", () => {
    expect(() => buildDungeon(registry, "no-such-dungeon", 5)).toThrow();
  });
});

// scale-step-keeps-every: a numeric-`at` step WITH `every` (sim/encounter/scripts.ts: fires at
// `at`, `at+every`, ...) must keep its `every`, Tempo-scaled exactly like a "loop" step's. Dropping
// it made pair.stackCall (`at: 600, every: 400`) fire once at global tick 600 -- during pull 1,
// before The Pair even spawns -- and never again (live play: no STACK telegraph in pull 2).
describe("content-dungeon: phased recurring steps keep `every`", () => {
  const KEY = 2;
  const PULL2_START_TICK = 1500; // well past the step's `at` (600), as in live play
  const WINDOW_TICKS = 1200; // 60s at 20Hz

  function stackStepAndScripts(keyLevel: number) {
    const build = buildDungeon(registry, "hollow-vault", keyLevel);
    const pairIndex = build.dungeon.pulls.findIndex((p) => p.spawns.some((s) => s.classId === "pair.melee"));
    const pairId = build.dungeon.pulls[pairIndex]!.spawns.find((s) => s.classId === "pair.melee")!.actorId;
    const step = build.enemyScripts[pairId]!.find((s) => s.cast === "pair.stackCall")!;
    return { build, pairIndex, step };
  }

  it("scales pair.stackCall's `every` with scaleCastEvery and leaves `at` as the phase offset", () => {
    const { step } = stackStepAndScripts(KEY);
    expect(step.at).toBe(600);
    expect(step.every).toBe(scaleCastEvery(400, KEY));
    expect(step.every).not.toBe(400); // key 2 really does scale (392), so "unscaled" is caught
    expect(step.telegraph!.leadTicks).toBe(scaleLeadTicks(100, KEY));
  });

  it("a scripted pull-2 run at key 2 emits >=2 STACK telegraphs during The Pair", () => {
    const { build, pairIndex } = stackStepAndScripts(KEY);
    const state = createState(1);
    state.tick = PULL2_START_TICK;
    for (const actor of spawnPull(build.dungeon, pairIndex, PULL2_START_TICK, () => {})) state.actors[actor.id] = actor;

    const stackResolves = new Set<number>();
    for (let t = PULL2_START_TICK; t < PULL2_START_TICK + WINDOW_TICKS; t++) {
      state.tick = t;
      const { telegraphEvents } = stepEnemyScripts(state, build.enemyScripts);
      for (const e of telegraphEvents) {
        if (e.type === "telegraph" && e.cast === "pair.stackCall" && typeof e.resolvesTick === "number") stackResolves.add(e.resolvesTick);
      }
    }
    expect(stackResolves.size).toBeGreaterThanOrEqual(2);
  });
});
