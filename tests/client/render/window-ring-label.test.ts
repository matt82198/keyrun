/**
 * tests/client/render/window-ring-label.test.ts
 *
 * Window readability (D10 gap item 3): a Window ring must say WHOSE setup it is, not just how long
 * it has left. Drives the real render/window-ring.ts WindowRingPool against the recording fake of
 * "three" (tests/client/support/fake-three.ts, whose fake canvas logs every fillText), and asserts
 * the text actually drawn into the label sprite that rides on the ring: "<Class> Window: <Name>"
 * for a target Window, "LOCK: alternate classes" for the Lock, gone once the Window closes.
 */
import { describe, expect, it, vi } from "vitest";

vi.mock("three", () => import("../support/fake-three.js"));

import * as THREE from "three";
import { WindowRingPool, windowRingLabel } from "../../../client/src/render/window-ring.js";
import type { ActorSnapshot, ActorWindow } from "../../../client/src/sim-host/snapshot.js";
import type { InterpolatedActor } from "../../../client/src/sim-host/interpolate.js";
import type { FakeCanvas, Sprite } from "../support/fake-three.js";

const SUNDERED: ActorWindow = { windowId: "sundered", setupClass: "warden", endsTick: 160, durationTicks: 60, lock: false };
const LOCK: ActorWindow = { windowId: "lock", setupClass: "boss", endsTick: 160, durationTicks: 60, lock: true };

function enemy(window: ActorWindow | null): ActorSnapshot {
  return {
    id: 102, kind: "enemy", role: "lieutenant", classId: "choir.lieutenant", pos: { x: 2000, y: 0, z: -3000 }, yaw: 0,
    health: 10, maxHealth: 10, resource: 0, maxResource: 1, alive: true, targetId: null, cast: null, cooldowns: {}, window,
  };
}

function frame(a: ActorSnapshot): [InterpolatedActor[], Map<number, ActorSnapshot>] {
  return [[a as unknown as InterpolatedActor], new Map([[a.id, a]])];
}

function labelTexts(scene: THREE.Scene): string[] {
  const sprites = (scene as unknown as { children: unknown[] }).children.filter((c): c is Sprite => c instanceof (THREE as unknown as { Sprite: typeof Sprite }).Sprite);
  return sprites.flatMap((s) => (s.visible ? ((s.material.map?.image as FakeCanvas | undefined)?.ctx.texts ?? []) : []));
}

describe("windowRingLabel", () => {
  it("names the setup class and the Window", () => {
    expect(windowRingLabel(SUNDERED)).toBe("Warden Window: Sundered");
    expect(windowRingLabel({ ...SUNDERED, windowId: "kindled", setupClass: "cinder" })).toBe("Cinder Window: Kindled");
  });

  it("calls the Lock a LOCK", () => {
    expect(windowRingLabel(LOCK)).toBe("LOCK: alternate classes");
  });
});

describe("WindowRingPool label sprite", () => {
  it("draws the setup-class label beside the ring while the Window is open", () => {
    const scene = new THREE.Scene();
    const pool = new WindowRingPool(scene);
    pool.sync(...frame(enemy(SUNDERED)), 110);
    expect(labelTexts(scene)).toEqual(["Warden Window: Sundered"]);
  });

  it("relabels when a different Window replaces it, and removes the label when the Window closes", () => {
    const scene = new THREE.Scene();
    const pool = new WindowRingPool(scene);
    pool.sync(...frame(enemy(SUNDERED)), 110);
    pool.sync(...frame(enemy({ ...SUNDERED, windowId: "kindled", setupClass: "cinder" })), 111);
    expect(labelTexts(scene)).toEqual(["Cinder Window: Kindled"]);
    pool.sync(...frame(enemy(null)), 112);
    expect(labelTexts(scene)).toEqual([]);
    expect((scene as unknown as { children: unknown[] }).children).toHaveLength(0);
  });
});
