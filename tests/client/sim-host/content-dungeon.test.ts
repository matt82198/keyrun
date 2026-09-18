import { describe, expect, it } from "vitest";
import { loadContent } from "@content/index";
import { buildDungeon } from "../../../client/src/sim-host/content-dungeon.js";

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

  it("drops onDeath script triggers (seed.elite) but keeps the loop step", () => {
    const { dungeon, enemyScripts } = buildDungeon(registry, "hollow-vault", 5);
    const seedPull = dungeon.pulls.find((p) => p.spawns.some((s) => s.classId === "seed.elite"))!;
    const seedId = seedPull.spawns[0]!.actorId;
    const script = enemyScripts[seedId]!;
    // sim's own ScriptStep type is "loop" | number (no "onDeath" variant at all), so a
    // successful conversion here is a compile-time guarantee, not just a runtime one.
    expect(script.some((s) => s.cast === "seed.slam")).toBe(true);
    expect(script.some((s) => s.cast === "seed.deathBloom")).toBe(false);
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
