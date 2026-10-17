/**
 * tests/client/sim-host/cast-feedback.test.ts
 *
 * D10 first-run readability: a key press that casts nothing must say why. Drives the real client
 * host (SimRuntime over real content) and reads the one-shot "cast_failed" snapshot events.
 */
import { describe, expect, it } from "vitest";
import { loadContent } from "@content/index";
import { buildRunSetup } from "../../../client/src/sim-host/run-setup.js";
import { SimRuntime } from "../../../client/src/sim-host/sim-runtime.js";
import type { Snapshot } from "../../../client/src/sim-host/snapshot.js";

const registry = loadContent();
const failures = (s: Snapshot) => (s.events ?? []).filter((e) => e.type === "cast_failed");

function run() {
  const setup = buildRunSetup(registry, { keyLevel: 5, dungeonId: "hollow-vault", humanClassId: "warden" });
  return { setup, host: new SimRuntime(setup) };
}

describe("cast_failed feedback", () => {
  it("Strike far from every enemy reports Out of range (and casts nothing)", () => {
    const { host } = run();
    let snap = host.step();
    for (let i = 0; i < 30; i++) {
      host.applyCommand({ type: "move", dx: 0, dz: 180, tick: snap.tick }); // away from the pack (+z)
      snap = host.step();
    }
    host.applyCommand({ type: "cast", abilityId: "warden.strike", tick: snap.tick });
    snap = host.step();
    expect(failures(snap)).toEqual([expect.objectContaining({ abilityId: "warden.strike", reason: "Out of range", actorId: 1 })]);
    expect((snap.events ?? []).some((e) => e.type === "cast" && e.actorId === 1)).toBe(false);
  });

  it("a second GCD ability on the next tick reports Not ready yet; a successful cast reports nothing", () => {
    const { host } = run();
    let snap = host.step();
    host.applyCommand({ type: "cast", abilityId: "warden.bulwark", tick: snap.tick });
    snap = host.step();
    expect(failures(snap)).toEqual([]);
    host.applyCommand({ type: "cast", abilityId: "warden.vigil", tick: snap.tick });
    snap = host.step();
    expect(failures(snap)).toEqual([expect.objectContaining({ abilityId: "warden.vigil", reason: "Not ready yet" })]);
  });
});
