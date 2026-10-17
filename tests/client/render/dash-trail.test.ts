/**
 * tests/client/render/dash-trail.test.ts
 *
 * Item 13: the Space dash (sim/combat/movement.ts DASH_DISTANCE_MM in ONE tick) leaves a brief
 * ground trail from where the actor was to where it landed, drawn by the REAL render/cast-fx.ts
 * CastFxPool (into tests/client/support/fake-three.ts) from snapshot positions alone -- so it works
 * on the local host and ?net=1 alike, with no new event. Walking, standing still, re-syncing the same
 * snapshot, and teleport-sized jumps never draw one; the trail fades out.
 * (The Charge line is #98's existing "line" projection: tests/client/render/cast-fx.test.ts.)
 */
import { describe, expect, it, vi } from "vitest";

vi.mock("three", () => import("../support/fake-three.js"));

import * as THREE from "three";
import { CastFxPool } from "../../../client/src/render/cast-fx.js";
import { DASH_TRAIL_SECONDS, detectDashes } from "../../../client/src/render/dash-trail.js";
import { DASH_DISTANCE_MM } from "../../../sim/combat/movement.js";
import type { ActorSnapshot } from "../../../client/src/sim-host/snapshot.js";

function actor(id: number, x: number, z: number, alive = true): ActorSnapshot {
  return { id, kind: "player", role: "tank", classId: "warden", pos: { x, y: 0, z }, yaw: 0, health: 1, maxHealth: 1, resource: 0, maxResource: 1, alive, targetId: null, cast: null, cooldowns: {} };
}
const byId = (...a: ActorSnapshot[]) => new Map(a.map((x) => [x.id, x]));
const noPos = () => null;

describe("dash trail (item 13)", () => {
  it("a one-tick DASH_DISTANCE_MM jump draws a dash trail, once, that fades out", () => {
    const fx = new CastFxPool(new THREE.Scene() as never);
    fx.configure(1, undefined);
    fx.spawn([], byId(actor(1, 0, 0)), noPos);
    const landed = byId(actor(1, DASH_DISTANCE_MM, 0));
    fx.spawn([], landed, noPos);
    expect(fx.activeShapes()).toEqual(["dash"]);
    fx.spawn([], landed, noPos); // the same snapshot again next frame: no second trail
    expect(fx.activeShapes()).toEqual(["dash"]);
    for (let i = 0; i < Math.ceil(DASH_TRAIL_SECONDS * 60) + 2; i++) fx.update(1 / 60);
    expect(fx.activeShapes()).toEqual([]);
  });

  it("walking, a dead actor, and a teleport-sized jump draw nothing", () => {
    expect(detectDashes(new Map([[1, { x: 0, z: 0 }]]), byId(actor(1, 180, 0)))).toEqual([]);
    expect(detectDashes(new Map([[1, { x: 0, z: 0 }]]), byId(actor(1, DASH_DISTANCE_MM, 0, false)))).toEqual([]);
    expect(detectDashes(new Map([[1, { x: 0, z: 0 }]]), byId(actor(1, 40000, 0)))).toEqual([]);
    expect(detectDashes(new Map(), byId(actor(1, DASH_DISTANCE_MM, 0)))).toEqual([]); // first sighting
  });

  it("reports the dash's start and end in mm, and updates the remembered positions", () => {
    const last = new Map([[1, { x: 0, z: 0 }], [2, { x: 500, z: 500 }]]);
    expect(detectDashes(last, byId(actor(1, 0, -2900), actor(2, 600, 500)))).toEqual([{ id: 1, from: { x: 0, z: 0 }, to: { x: 0, z: -2900 } }]);
    expect(last.get(1)).toEqual({ x: 0, z: -2900 });
    expect(last.get(2)).toEqual({ x: 600, z: 500 });
  });
});
