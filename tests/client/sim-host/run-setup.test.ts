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

// run-options-wiring: buildRunSetup's new 3rd (loadout) param + opts.clauseId, both previously
// non-existent -- the exact P0 ("no clause and no loadout") this lane fixes on the client side.
describe("run-setup: buildRunSetup runOptions (Clause + loadout)", () => {
  it("carries content.clauses through, keyed by id, for createRun's options.clauseId to resolve", () => {
    const setup = buildRunSetup(registry, { keyLevel: 5, dungeonId: "hollow-vault", humanClassId: "warden" });
    expect(Object.keys(setup.content.clauses ?? {}).length).toBe(registry.clauses.size);
  });

  it("runOptions is empty when neither clauseId nor a loadout is given (unchanged behaviour)", () => {
    const setup = buildRunSetup(registry, { keyLevel: 5, dungeonId: "hollow-vault", humanClassId: "warden" });
    expect(setup.runOptions).toEqual({});
  });

  it("threads opts.clauseId straight into runOptions.clauseId", () => {
    const [firstId] = [...registry.clauses.keys()];
    expect(firstId).toBeDefined();
    const setup = buildRunSetup(registry, { keyLevel: 5, dungeonId: "hollow-vault", humanClassId: "warden", clauseId: firstId as string });
    expect(setup.runOptions.clauseId).toBe(firstId);
  });

  it("resolves a real loadout (warden.strike -> riposte) into runOptions.loadouts for the human's actorId", () => {
    const setup = buildRunSetup(
      registry,
      { keyLevel: 5, dungeonId: "hollow-vault", humanClassId: "warden" },
      { talentChoices: { "warden.strike": "riposte" }, gear: [] },
    );
    const base = registry.abilities.get("warden.strike");
    const patched = setup.runOptions.loadouts?.[setup.humanActorId]?.["warden.strike"];
    expect(patched).toBeDefined();
    expect(patched?.gcdTicks).toBe(0); // riposte's patch: "Strike costs no GCD ..."
    expect(patched?.gcdTicks).not.toBe(base?.gcdTicks);
  });

  it("leaves runOptions.loadouts undefined for null/undefined loadout (no persisted build)", () => {
    const withNull = buildRunSetup(registry, { keyLevel: 5, dungeonId: "hollow-vault", humanClassId: "warden" }, null);
    const withUndefined = buildRunSetup(registry, { keyLevel: 5, dungeonId: "hollow-vault", humanClassId: "warden" }, undefined);
    expect(withNull.runOptions.loadouts).toBeUndefined();
    expect(withUndefined.runOptions.loadouts).toBeUndefined();
  });

  it("propagates applyLoadout's own thrown error for an invariant-breaking loadout -- never a silent base-kit fallback", () => {
    expect(() =>
      buildRunSetup(
        registry,
        { keyLevel: 5, dungeonId: "hollow-vault", humanClassId: "warden" },
        { talentChoices: { "not-a-real-ability": "nope" }, gear: [] },
      ),
    ).toThrow(/no talent fork/);
  });
});
