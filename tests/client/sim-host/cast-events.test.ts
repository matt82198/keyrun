/**
 * tests/client/sim-host/cast-events.test.ts
 *
 * D10 projections (Matt's "no ability projection"): every completed cast must reach the renderer as
 * a one-shot Snapshot.events "cast" entry carrying its shape and whoever it hit. Drives the REAL
 * client worker host (sim-host/sim-runtime.ts's SimRuntime over the real content registry) and
 * reads the snapshots it actually emits.
 */
import { describe, expect, it } from "vitest";
import { loadContent } from "@content/index";
import { buildRunSetup } from "../../../client/src/sim-host/run-setup.js";
import { SimRuntime } from "../../../client/src/sim-host/sim-runtime.js";
import type { CastSnapshotEvent, Snapshot } from "../../../client/src/sim-host/snapshot.js";

const registry = loadContent();

function casts(s: Snapshot): CastSnapshotEvent[] {
  return (s.events ?? []).filter((e): e is CastSnapshotEvent => e.type === "cast");
}

function wardenRun() {
  const setup = buildRunSetup(registry, { keyLevel: 5, dungeonId: "hollow-vault", humanClassId: "warden" });
  return { setup, host: new SimRuntime(setup) };
}

describe("snapshot cast events", () => {
  it("the human's Guard Break (cone) arrives once with its cone geometry and the enemies it hit", () => {
    const { setup, host } = wardenRun();
    let snap = host.step();
    for (let i = 0; i < 12; i++) {
      host.applyCommand({ type: "move", dx: 0, dz: -180, tick: snap.tick }); // toward the two acolytes (-z)
      snap = host.step();
    }
    let found: CastSnapshotEvent | undefined;
    for (let i = 0; i < 120 && !found; i++) {
      if (i % 5 === 0) host.applyCommand({ type: "cast", abilityId: "warden.guardBreak", tick: snap.tick });
      snap = host.step();
      found = casts(snap).find((e) => e.actorId === setup.humanActorId && e.abilityId === "warden.guardBreak");
    }
    expect(found).toBeDefined();
    expect(found).toMatchObject({ shape: "cone", radius: 3000, angle1024: 140 });
    const enemies = new Set(snap.actors.filter((a) => a.kind === "enemy").map((a) => a.id));
    expect(found!.hitIds.length).toBeGreaterThan(0);
    expect(found!.hitIds.every((id) => enemies.has(id))).toBe(true);
    // One-shot: the next snapshot does not repeat it.
    const next = host.step();
    expect(casts(next).some((e) => e.tick === found!.tick && e.abilityId === "warden.guardBreak")).toBe(false);
  });

  it("bots' and enemies' casts are reported too (the whole fight is visible, not just the human)", () => {
    const { setup, host } = wardenRun();
    const seen: CastSnapshotEvent[] = [];
    for (let i = 0; i < 400; i++) seen.push(...casts(host.step()));
    const byKind = (kind: string) => {
      const ids = new Set(setup.party.map((m) => m.id));
      return seen.filter((e) => (kind === "bot" ? ids.has(e.actorId) && e.actorId !== setup.humanActorId : !ids.has(e.actorId)));
    };
    expect(byKind("bot").length).toBeGreaterThan(0);
    const enemy = byKind("enemy");
    expect(enemy.length).toBeGreaterThan(0);
    const swing = enemy.find((e) => e.shape === "target");
    expect(swing?.targetId).not.toBeNull();
  });
});
