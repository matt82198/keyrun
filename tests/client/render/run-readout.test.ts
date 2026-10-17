/**
 * tests/client/render/run-readout.test.ts
 *
 * client/src/render/run-readout.ts: the pure model behind render/hud.ts's top-left run readout
 * (D10: readability of every mechanic). `formatPullReadout` turns the snapshot's pullIndex/pullCount
 * into "Pull <n>/<total>" (or "Pull <n>" when the count is not carried), and `partyMarkers` picks
 * one marker per party member (kind "player"), ordered by id, with alive/dead and HP fraction.
 * Also drives the REAL client snapshot projection (snapshot-codec via SimRuntime) for hollow-vault
 * so the readout is proven against what the host actually emits, not only hand-built literals.
 */
import { describe, expect, it } from "vitest";

import { loadContent } from "@content/index";
import { buildRunSetup } from "../../../client/src/sim-host/run-setup.js";
import { SimRuntime } from "../../../client/src/sim-host/sim-runtime.js";
import { formatPullReadout, partyMarkers } from "../../../client/src/render/run-readout.js";
import type { ActorSnapshot, Snapshot } from "../../../client/src/sim-host/snapshot.js";

function actor(id: number, over: Partial<ActorSnapshot> = {}): ActorSnapshot {
  return {
    id,
    kind: "player",
    role: "damage",
    classId: "warden",
    pos: { x: 0, y: 0, z: 0 },
    yaw: 0,
    health: 1000,
    maxHealth: 1000,
    resource: 0,
    maxResource: 0,
    alive: true,
    targetId: null,
    cast: null,
    cooldowns: {},
    ...over,
  };
}

function snapshot(over: Partial<Snapshot> = {}): Snapshot {
  return { tick: 0, actors: [], telegraphs: [], runTimerTicks: 0, timerLimitTicks: 0, ...over };
}

describe("formatPullReadout", () => {
  it("shows pull index over pull count when both are carried", () => {
    expect(formatPullReadout(snapshot({ pullIndex: 0, pullCount: 4 }))).toBe("Pull 0/4");
    expect(formatPullReadout(snapshot({ pullIndex: 3, pullCount: 4 }))).toBe("Pull 3/4");
  });

  it("falls back to 'Pull <n>' when the snapshot carries no pull count", () => {
    expect(formatPullReadout(snapshot({ pullIndex: 2 }))).toBe("Pull 2");
  });

  it("treats a missing pull index as pull 0 (run start)", () => {
    expect(formatPullReadout(snapshot({ pullCount: 4 }))).toBe("Pull 0/4");
    expect(formatPullReadout(snapshot())).toBe("Pull 0");
  });

  it("ignores a non-positive or non-finite pull count", () => {
    expect(formatPullReadout(snapshot({ pullIndex: 1, pullCount: 0 }))).toBe("Pull 1");
    expect(formatPullReadout(snapshot({ pullIndex: 1, pullCount: Number.NaN }))).toBe("Pull 1");
  });
});

describe("partyMarkers", () => {
  it("returns one marker per party member (players only), ordered by id", () => {
    const markers = partyMarkers(
      snapshot({
        actors: [actor(3), actor(10, { kind: "enemy" }), actor(1, { role: "tank" }), actor(2, { role: "healer" })],
      }),
    );
    expect(markers.map((m) => m.id)).toEqual([1, 2, 3]);
    expect(markers.map((m) => m.role)).toEqual(["tank", "healer", "damage"]);
  });

  it("carries alive/dead and the HP fraction clamped to [0, 1]", () => {
    const markers = partyMarkers(
      snapshot({
        actors: [
          actor(1, { health: 250, maxHealth: 1000 }),
          actor(2, { health: 0, alive: false }),
          actor(3, { health: 2000, maxHealth: 1000 }),
          actor(4, { health: 5, maxHealth: 0 }),
        ],
      }),
    );
    expect(markers).toEqual([
      { id: 1, role: "damage", alive: true, hpFraction: 0.25 },
      { id: 2, role: "damage", alive: false, hpFraction: 0 },
      { id: 3, role: "damage", alive: true, hpFraction: 1 },
      { id: 4, role: "damage", alive: true, hpFraction: 0 },
    ]);
  });

  it("a dead member always reads 0 HP even if the snapshot still has health", () => {
    const [m] = partyMarkers(snapshot({ actors: [actor(1, { alive: false, health: 400 })] }));
    expect(m).toEqual({ id: 1, role: "damage", alive: false, hpFraction: 0 });
  });

  it("is empty when no party member is in the snapshot", () => {
    expect(partyMarkers(snapshot({ actors: [actor(9, { kind: "enemy" })] }))).toEqual([]);
  });
});

describe("run readout over the REAL hollow-vault snapshot (SimRuntime + snapshot-codec)", () => {
  it("reads 'Pull 0/<dungeon pulls>' and five live full-HP party markers on the first tick", () => {
    const setup = buildRunSetup(loadContent(), { keyLevel: 5, dungeonId: "hollow-vault", humanClassId: "warden" });
    const snap = new SimRuntime(setup).step();
    expect(formatPullReadout(snap)).toBe(`Pull 0/${setup.dungeon.pulls.length}`);
    const markers = partyMarkers(snap);
    expect(markers).toHaveLength(5);
    expect(markers.every((m) => m.alive && m.hpFraction === 1)).toBe(true);
  });
});
