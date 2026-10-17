/**
 * tests/client/render/ground-frame.test.ts
 *
 * D10 projections must cover exactly what the sim resolves. render/ground-frame.ts maps decal-local
 * points to the ground the way three.js does (flatten, then yaw under Euler "YXZ"); these tests
 * push points from INSIDE and OUTSIDE each drawn shape through that mapping and ask the SIM's own
 * resolvers whether they hit: sim/encounter/telegraphs.ts and sim/combat/targeting.ts, which share
 * ONE facing convention (faceDir, yaw 0 = +x). Also drives the real TelegraphPool to prove it
 * applies that frame (order "YXZ", the telegraph yaw) and lays a line decal flat (no BoxGeometry wall).
 */
import { describe, expect, it, vi } from "vitest";

vi.mock("three", () => import("../support/fake-three.js"));

import { pointInCone, pointInLine } from "../../../sim/encounter/telegraphs.js";
import { inCone, inLine } from "../../../sim/combat/targeting.js";
import type { ActorState } from "../../../sim/core/state.js";
import { combatYawToThree, decalPointToGround, telegraphYawToThree } from "../../../client/src/render/ground-frame.js";

const YAWS = [0, 64, 128, 256, 300, 512, 700, 768, 900];
const O = { x: 5000, z: -3000 };
const mm = (p: { x: number; z: number }) => ({ x: Math.round(p.x * 1000), y: 0, z: Math.round(p.z * 1000) });
const groundM = (lx: number, ly: number, rot: number) => mm(decalPointToGround(lx, ly, rot, { x: O.x / 1000, z: O.z / 1000 }));

describe("telegraph decals land where sim/encounter resolves them", () => {
  for (const yaw of YAWS) {
    it(`cone at yaw ${yaw}: drawn interior hits, drawn behind misses`, () => {
      const rot = telegraphYawToThree(yaw);
      const origin = { x: O.x, y: 0, z: O.z };
      // Cone of 200/1024 turns (~70 deg), radius 4 m: a point 3 m forward, 20 deg off-axis, is inside.
      const a = (20 * Math.PI) / 180;
      expect(pointInCone(origin, yaw, 200, 4000, groundM(3 * Math.cos(a), 3 * Math.sin(a), rot))).toBe(true);
      expect(pointInCone(origin, yaw, 200, 4000, groundM(-3, 0, rot))).toBe(false);
      expect(pointInCone(origin, yaw, 200, 4000, groundM(0.3, 3, rot))).toBe(false); // well outside the half-angle
    });
    it(`line at yaw ${yaw}: drawn strip hits, beside it misses`, () => {
      const rot = telegraphYawToThree(yaw);
      const origin = { x: O.x, y: 0, z: O.z };
      expect(pointInLine(origin, yaw, 8000, 2000, groundM(6, 0.8, rot))).toBe(true);
      expect(pointInLine(origin, yaw, 8000, 2000, groundM(6, 1.6, rot))).toBe(false);
      expect(pointInLine(origin, yaw, 8000, 2000, groundM(-1, 0, rot))).toBe(false);
    });
  }
});

describe("cast projections land where sim/combat resolves them", () => {
  const caster = (yaw: number) => ({ id: 1, pos: { x: O.x, y: 0, z: O.z }, yaw }) as unknown as ActorState;
  const at = (p: { x: number; y: number; z: number }) => [{ id: 2, pos: p }] as unknown as ActorState[];
  for (const yaw of YAWS) {
    it(`Guard Break cone and Charge line at combat yaw ${yaw}`, () => {
      const rot = combatYawToThree(yaw);
      const a = (15 * Math.PI) / 180; // Guard Break: 140/1024 turns (~49 deg), radius 3 m
      expect(inCone(caster(yaw), 140, 3000, at(groundM(2.4 * Math.cos(a), 2.4 * Math.sin(a), rot)))).toEqual([2]);
      expect(inCone(caster(yaw), 140, 3000, at(groundM(-2.4, 0, rot)))).toEqual([]);
      expect(inLine(caster(yaw), 8000, 1000, at(groundM(5, 0.6, rot)))).toEqual([2]);
      expect(inLine(caster(yaw), 8000, 1000, at(groundM(5, 1.6, rot)))).toEqual([]);
    });
  }
});

describe("TelegraphPool applies the frame", () => {
  it("sets Euler order YXZ, the telegraph yaw, and builds a line as a flat ShapeGeometry", async () => {
    const THREE = await import("three");
    const { TelegraphPool } = await import("../../../client/src/render/telegraphs.js");
    const scene = new THREE.Scene();
    new TelegraphPool(scene).sync([{ id: "l", shape: "line", center: { x: 0, z: 0 }, length: 8000, width: 2000, yaw: 256, resolvesTick: 10, danger: true }], 0);
    const mesh = scene.children[0] as unknown as { rotation: { x: number; y: number; order?: string }; geometry: object };
    expect(mesh.rotation.order).toBe("YXZ");
    expect(mesh.rotation.x).toBeCloseTo(-Math.PI / 2);
    expect(mesh.rotation.y).toBeCloseTo(telegraphYawToThree(256));
    expect(mesh.geometry.constructor.name).toBe("ShapeGeometry");
  });
});
