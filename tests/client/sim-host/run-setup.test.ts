import { describe, expect, it } from "vitest";
import { loadContent } from "@content/index";
import { buildRunSetup } from "../../../client/src/sim-host/run-setup.js";

const registry = loadContent();

describe("run-setup: buildRunSetup", () => {
  it("wires every content ability/effect into RunContent, keyed by id", () => {
    const setup = buildRunSetup(registry, { keyLevel: 5, dungeonId: "hollow-vault", humanClassId: "warden" });
    expect(Object.keys(setup.content.abilities).length).toBe(registry.abilities.size);
    expect(Object.keys(setup.content.effects).length).toBe(registry.effects.size);
    expect(setup.content.abilities["warden.strike"]).toBeDefined();
  });

  it("uses a global lockoutTicks of 80, matching every content interrupt ability's own value", () => {
    const setup = buildRunSetup(registry, { keyLevel: 5, dungeonId: "hollow-vault", humanClassId: "warden" });
    expect(setup.content.lockoutTicks).toBe(80);
  });

  it("points bossActorId at the dungeon's final pull's sole spawn", () => {
    const setup = buildRunSetup(registry, { keyLevel: 5, dungeonId: "hollow-vault", humanClassId: "warden" });
    const lastPull = setup.dungeon.pulls[setup.dungeon.pulls.length - 1]!;
    expect(lastPull.spawns).toHaveLength(1);
    expect(setup.bossActorId).toBe(lastPull.spawns[0]!.actorId);
  });

  it("keeps the arena bounds wide enough to contain every pull and the boss spawn", () => {
    const setup = buildRunSetup(registry, { keyLevel: 5, dungeonId: "hollow-vault", humanClassId: "warden" });
    for (const pull of setup.dungeon.pulls) {
      for (const spawn of pull.spawns) {
        expect(spawn.pos.x).toBeGreaterThanOrEqual(setup.content.bounds.minX);
        expect(spawn.pos.x).toBeLessThanOrEqual(setup.content.bounds.maxX);
        expect(spawn.pos.z).toBeGreaterThanOrEqual(setup.content.bounds.minZ);
        expect(spawn.pos.z).toBeLessThanOrEqual(setup.content.bounds.maxZ);
      }
    }
  });
});
