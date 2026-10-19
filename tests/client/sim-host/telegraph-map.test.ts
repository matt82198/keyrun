/**
 * tests/client/sim-host/telegraph-map.test.ts
 *
 * D10 readability: every telegraph the sim can emit gets a render class derived from the casting
 * ability (sim/encounter/telegraphs.ts's Telegraph.abilityId), and `danger` is derived from that
 * class instead of being hardcoded true. The emitted-kind list is read from the real content data
 * (content/data/enemies/*.json script steps carrying a `telegraph`), so a new telegraphed cast with
 * no class entry fails here rather than silently rendering as a generic hazard.
 */
import { describe, expect, it } from "vitest";
import { readFileSync, readdirSync } from "node:fs";
import { join } from "node:path";
import { fileURLToPath } from "node:url";
import { loadContent } from "@content/index";
import { createRun } from "../../../sim/index.js";
import type { Telegraph } from "../../../sim/encounter/telegraphs.js";
import { buildRunSetup } from "../../../client/src/sim-host/run-setup.js";
import { buildSnapshot } from "../../../client/src/sim-host/snapshot-codec.js";
import {
  TELEGRAPH_CLASS_BY_ABILITY,
  telegraphClassFor,
  toBotTelegraph,
  toTelegraphSnapshot,
  type ClassedTelegraphSnapshot,
} from "../../../client/src/sim-host/telegraph-map.js";

const ENEMIES_DIR = fileURLToPath(new URL("../../../content/data/enemies/", import.meta.url));

/** Every ability id the shipped content can make the sim register as a telegraph. */
function emittedTelegraphAbilities(): string[] {
  const ids = new Set<string>();
  for (const file of readdirSync(ENEMIES_DIR)) {
    if (!file.endsWith(".json")) continue;
    const enemies = JSON.parse(readFileSync(join(ENEMIES_DIR, file), "utf8")) as Array<{
      script?: Array<{ cast: string; telegraph?: unknown }>;
    }>;
    for (const enemy of enemies) for (const step of enemy.script ?? []) if (step.telegraph) ids.add(step.cast);
  }
  return [...ids].sort();
}

function tg(abilityId: string, shape: Telegraph["shape"] = "circle"): Telegraph {
  return { id: 7, sourceId: 900, abilityId, shape, origin: { x: 1000, y: 0, z: 2000 }, yaw: 0, radius: 3000, resolvesTick: 120 };
}

describe("telegraph-map: render class per emitted telegraph kind", () => {
  it("finds exactly the six telegraphed casts the content ships today", () => {
    expect(emittedTelegraphAbilities()).toEqual([
      "hollowWarden.cleave", "hollowWarden.slam", "pair.spreadMark", "pair.stackCall", "seed.slam", "twins.slam",
    ]);
  });

  it("every emitted telegraph has an explicit class entry (no silent fallback)", () => {
    for (const id of emittedTelegraphAbilities()) expect(TELEGRAPH_CLASS_BY_ABILITY[id], id).toBeDefined();
  });

  it("maps spread mark / stack call / boss cleave / seed slam to four distinct classes", () => {
    expect(telegraphClassFor("pair.spreadMark")).toBe("spread");
    expect(telegraphClassFor("pair.stackCall")).toBe("stack");
    expect(telegraphClassFor("hollowWarden.cleave")).toBe("boss");
    expect(telegraphClassFor("seed.slam")).toBe("avoid");
    const classes = ["pair.spreadMark", "pair.stackCall", "hollowWarden.cleave", "seed.slam"].map(telegraphClassFor);
    expect(new Set(classes).size).toBe(classes.length);
  });

  it("maps the boss and Twins slams to avoid, the seed.slam circle's class (SLAM-TELEGRAPH)", () => {
    expect(TELEGRAPH_CLASS_BY_ABILITY["hollowWarden.slam"]).toBe("avoid");
    expect(TELEGRAPH_CLASS_BY_ABILITY["twins.slam"]).toBe("avoid");
    expect(toTelegraphSnapshot(tg("hollowWarden.slam")).danger).toBe(true);
    expect(toBotTelegraph(tg("twins.slam")).danger).toBe(true);
  });

  it("an unknown ability falls back to the generic avoid class", () => {
    expect(telegraphClassFor("nobody.knows")).toBe("avoid");
    expect(toTelegraphSnapshot(tg("nobody.knows")).danger).toBe(true);
  });

  it("derives danger from the class: a stack circle is not danger, the others are", () => {
    expect(toTelegraphSnapshot(tg("pair.stackCall")).danger).toBe(false);
    expect(toTelegraphSnapshot(tg("pair.spreadMark")).danger).toBe(true);
    expect(toTelegraphSnapshot(tg("hollowWarden.cleave", "cone")).danger).toBe(true);
    expect(toTelegraphSnapshot(tg("seed.slam")).danger).toBe(true);
    expect(toBotTelegraph(tg("pair.stackCall")).danger).toBe(false);
    expect(toBotTelegraph(tg("hollowWarden.cleave", "cone")).danger).toBe(true);
  });

  it("carries the class on the render snapshot", () => {
    expect(toTelegraphSnapshot(tg("pair.spreadMark")).telegraphClass).toBe("spread");
    expect(toTelegraphSnapshot(tg("hollowWarden.cleave", "cone")).telegraphClass).toBe("boss");
  });
});

describe("telegraph-map: the class survives a real snapshot build + JSON wire round trip", () => {
  it("buildSnapshot -> JSON -> parse keeps each telegraph's class and derived danger", () => {
    const setup = buildRunSetup(loadContent(), { keyLevel: 5, dungeonId: "hollow-vault", humanClassId: "warden" });
    const run = createRun(1, setup.party, setup.dungeon, setup.content, setup.runOptions);
    run.encounter.telegraphs = [
      { ...tg("pair.spreadMark"), id: 1 },
      { ...tg("pair.stackCall"), id: 2 },
      { ...tg("hollowWarden.cleave", "cone"), id: 3, angle1024: 200 },
      { ...tg("seed.slam"), id: 4 },
    ];
    const wire = JSON.parse(JSON.stringify(buildSnapshot(run, setup, 0, null))) as { telegraphs: ClassedTelegraphSnapshot[] };
    expect(wire.telegraphs.map((t) => [t.id, t.telegraphClass, t.danger])).toEqual([
      ["1", "spread", true],
      ["2", "stack", false],
      ["3", "boss", true],
      ["4", "avoid", true],
    ]);
  });
});
