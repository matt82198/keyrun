/**
 * tests/client/render/objective.test.ts
 *
 * client/src/render/objective.ts: the D10 objective line. Driven by the REAL client host's first
 * snapshot for hollow-vault (pull 1 = 2 Choir Acolytes + 1 Choir Cantor) and by the real boss pull,
 * with names from the real worker-side display info.
 */
import { describe, expect, it } from "vitest";
import { loadContent } from "@content/index";
import { buildRunSetup } from "../../../client/src/sim-host/run-setup.js";
import { SimRuntime } from "../../../client/src/sim-host/sim-runtime.js";
import { buildDisplayInfo } from "../../../client/src/sim-host/display-info.js";
import { objectiveText } from "../../../client/src/render/objective.js";

const registry = loadContent();
const setup = () => buildRunSetup(registry, { keyLevel: 5, dungeonId: "hollow-vault", humanClassId: "warden" });

describe("objective line", () => {
  it("pull 1 names what to kill, grouped, with the 1-based pull number", () => {
    const s = setup();
    const snap = new SimRuntime(s).step();
    const info = buildDisplayInfo(registry, s.content.abilities);
    expect(objectiveText(snap, info)).toBe(`Clear pull 1/${s.dungeon.pulls.length}: 2x Choir Acolyte, Choir Cantor`);
  });

  it("the boss pull says Boss: <name> <pct>%", () => {
    const s = setup();
    const bossPull = s.dungeon.pulls.find((p) => p.id === "boss")!;
    const snap = new SimRuntime({ ...s, dungeon: { ...s.dungeon, pulls: [bossPull] } }).step();
    expect(objectiveText(snap, buildDisplayInfo(registry, s.content.abilities))).toBe("Boss: The Hollow Warden 100%");
  });

  it("no live enemy reads Pull cleared", () => {
    expect(objectiveText({ actors: [], pullIndex: 0, pullCount: 4 })).toBe("Pull cleared");
  });
});
