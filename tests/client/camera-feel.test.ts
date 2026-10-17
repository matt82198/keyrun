/**
 * Camera feel pass (D10): pure-math checks for the third-person follow camera and the Space dash
 * intent. Everything here runs without Three.js or a DOM -- client/src/camera/cameraMath.ts holds
 * the math and CAMERA_TUNING; orbitCamera.ts only wires it to a real camera and pointer events.
 */
import { describe, expect, it } from "vitest";
import {
  CAMERA_TUNING,
  applyMouseLook,
  applyZoomStep,
  cameraOffset,
  clampPitch,
  expSmoothingAlpha,
  smoothFollow,
  smoothScalar,
} from "../../client/src/camera/cameraMath.js";
import { computeMoveDeltaMm } from "../../client/src/camera/movement.js";
import { KeyState } from "../../client/src/input/keyState.js";
import { DASH_CODE, deriveAbilityCommands } from "../../client/src/input/commands.js";
import { DEFAULT_KEYBINDS } from "../../client/src/input/keybinds.js";
import { resolveDashDir } from "../../client/src/sim-host/player-commands.js";
import { buildRunSetup } from "../../client/src/sim-host/run-setup.js";
import { SimRuntime } from "../../client/src/sim-host/sim-runtime.js";
import { loadContent } from "@content/index";
import { DASH_DISTANCE_MM } from "../../sim/combat/movement.js";

const NO_KEYS = { forward: false, back: false, left: false, right: false };

describe("follow smoothing", () => {
  it("lags behind a moved target on one frame (no snap) and converges over time", () => {
    let pos = { x: 0, y: 0, z: 0 };
    const target = { x: 4, y: 0, z: -3 };
    pos = smoothFollow(pos, target, 1 / 60);
    const firstGap = Math.hypot(target.x - pos.x, target.z - pos.z);
    expect(firstGap).toBeGreaterThan(0.5); // did not snap
    expect(firstGap).toBeLessThan(5); // but did move toward it
    for (let i = 0; i < 120; i++) pos = smoothFollow(pos, target, 1 / 60);
    expect(Math.hypot(target.x - pos.x, target.z - pos.z)).toBeLessThan(1e-3);
  });

  it("is monotone: the gap never grows and never overshoots", () => {
    let v = 0;
    let prevGap = 10;
    for (let i = 0; i < 60; i++) {
      v = smoothScalar(v, 10, 1 / 60, CAMERA_TUNING.followHalfLifeS);
      const gap = 10 - v;
      expect(gap).toBeGreaterThanOrEqual(0);
      expect(gap).toBeLessThan(prevGap);
      prevGap = gap;
    }
  });

  it("is frame-rate independent (one 1/30 s step == two 1/60 s steps)", () => {
    const one = smoothScalar(0, 1, 1 / 30, CAMERA_TUNING.followHalfLifeS);
    const two = smoothScalar(smoothScalar(0, 1, 1 / 60, CAMERA_TUNING.followHalfLifeS), 1, 1 / 60, CAMERA_TUNING.followHalfLifeS);
    expect(one).toBeCloseTo(two, 10);
  });

  it("covers exactly half the gap in one half-life", () => {
    expect(expSmoothingAlpha(CAMERA_TUNING.followHalfLifeS, CAMERA_TUNING.followHalfLifeS)).toBeCloseTo(0.5, 10);
  });

  it("snaps instead of gliding across the arena on a teleport-sized jump", () => {
    const far = { x: CAMERA_TUNING.snapDistanceM + 1, y: 0, z: 0 };
    expect(smoothFollow({ x: 0, y: 0, z: 0 }, far, 1 / 60)).toEqual(far);
  });

  it("does not snap when the direction of travel reverses", () => {
    // Target was moving +x, now steps back -x: the camera keeps easing, no jump to the new spot.
    let pos = { x: 0, y: 0, z: 0 };
    for (let i = 0; i < 10; i++) pos = smoothFollow(pos, { x: 1 + i * 0.06, y: 0, z: 0 }, 1 / 60);
    const before = pos.x;
    const next = smoothFollow(pos, { x: 1.4, y: 0, z: 0 }, 1 / 60);
    expect(Math.abs(next.x - before)).toBeLessThan(0.2);
  });
});

describe("mouse-look", () => {
  it("scales mouse movement by the sensitivity constant", () => {
    const v = applyMouseLook({ yaw: 1, pitch: -0.4 }, 10, 4);
    expect(v.yaw).toBeCloseTo(1 - 10 * CAMERA_TUNING.mouseSensitivityRadPerPx, 10);
    expect(v.pitch).toBeCloseTo(-0.4 - 4 * CAMERA_TUNING.mouseSensitivityRadPerPx, 10);
  });

  it("clamps pitch to [pitchMinRad, pitchMaxRad]", () => {
    expect(clampPitch(-10)).toBe(CAMERA_TUNING.pitchMinRad);
    expect(clampPitch(10)).toBe(CAMERA_TUNING.pitchMaxRad);
    expect(applyMouseLook({ yaw: 0, pitch: 0 }, 0, 100000).pitch).toBe(CAMERA_TUNING.pitchMinRad);
    expect(applyMouseLook({ yaw: 0, pitch: -1 }, 0, -100000).pitch).toBe(CAMERA_TUNING.pitchMaxRad);
  });

  it("keeps the camera above the ground at every allowed pitch", () => {
    for (const pitch of [CAMERA_TUNING.pitchMinRad, -0.5, CAMERA_TUNING.pitchMaxRad]) {
      expect(cameraOffset(0, pitch, CAMERA_TUNING.maxDistanceM).y).toBeGreaterThan(0);
    }
  });

  it("wraps yaw into [0, 2pi)", () => {
    const v = applyMouseLook({ yaw: 0.001, pitch: -0.4 }, 10, 0);
    expect(v.yaw).toBeGreaterThanOrEqual(0);
    expect(v.yaw).toBeLessThan(Math.PI * 2);
  });
});

describe("movement basis follows camera yaw", () => {
  it("W moves exactly away from the camera (camera-forward) at every yaw", () => {
    for (let i = 0; i < 16; i++) {
      const yaw = (i / 16) * Math.PI * 2;
      const off = cameraOffset(yaw, -0.45, 10);
      const move = computeMoveDeltaMm({ ...NO_KEYS, forward: true }, yaw, 1000);
      const offLen = Math.hypot(off.x, off.z);
      // forward == -(horizontal camera offset), normalised
      expect(move.dx / 1000).toBeCloseTo(-off.x / offLen, 2);
      expect(move.dz / 1000).toBeCloseTo(-off.z / offLen, 2);
    }
  });
});

describe("scroll zoom", () => {
  it("steps by zoomStepM per notch and clamps to [minDistanceM, maxDistanceM]", () => {
    expect(applyZoomStep(10, 100)).toBeCloseTo(10 + CAMERA_TUNING.zoomStepM, 10);
    expect(applyZoomStep(10, -100)).toBeCloseTo(10 - CAMERA_TUNING.zoomStepM, 10);
    expect(applyZoomStep(CAMERA_TUNING.maxDistanceM, 100)).toBe(CAMERA_TUNING.maxDistanceM);
    expect(applyZoomStep(CAMERA_TUNING.minDistanceM, -100)).toBe(CAMERA_TUNING.minDistanceM);
    expect(applyZoomStep(10, 0)).toBe(10);
  });
});

describe("Space dash intent", () => {
  it("emits exactly one dash per press, none while held, one again after release", () => {
    const keys = new KeyState();
    const dashes = (): number =>
      deriveAbilityCommands(keys.sampleAndClearJustPressed(), DEFAULT_KEYBINDS, undefined, 7).filter((c) => c.type === "dash").length;
    expect(DASH_CODE).toBe("Space");
    keys.onKeyDown(DASH_CODE);
    keys.onKeyDown(DASH_CODE); // OS key-repeat while held
    expect(dashes()).toBe(1);
    expect(dashes()).toBe(0); // still held, next tick
    keys.onKeyUp(DASH_CODE);
    expect(dashes()).toBe(0);
    keys.onKeyDown(DASH_CODE);
    expect(dashes()).toBe(1);
  });

  it("carries the tick and adds nothing to the configurable keybind set", () => {
    const cmds = deriveAbilityCommands(new Set([DASH_CODE]), DEFAULT_KEYBINDS, undefined, 42);
    expect(cmds).toEqual([{ type: "dash", tick: 42 }]);
    expect(Object.values(DEFAULT_KEYBINDS)).not.toContain(DASH_CODE);
  });

  it("moves the human a dash-length burst through the real SimRuntime input path, once", () => {
    const setup = buildRunSetup(loadContent(), { keyLevel: 5, dungeonId: "hollow-vault", humanClassId: "warden" });
    const host = new SimRuntime(setup);
    const posOf = (s: ReturnType<SimRuntime["step"]>) => s.actors.find((a) => a.id === setup.humanActorId)!.pos;
    const p0 = posOf(host.step());
    const p1 = posOf(host.step()); // idle tick: control
    expect(Math.hypot(p1.x - p0.x, p1.z - p0.z)).toBeLessThan(200);
    for (const cmd of deriveAbilityCommands(new Set([DASH_CODE]), DEFAULT_KEYBINDS, undefined, 2)) host.applyCommand(cmd);
    host.applyCommand({ type: "move", dx: 180, dz: 0, tick: 2 });
    const p2 = posOf(host.step());
    expect(p2.x - p1.x).toBeGreaterThanOrEqual(DASH_DISTANCE_MM); // walk step + dash, along +x
    const p3 = posOf(host.step()); // the dash does not repeat on the next tick
    expect(Math.hypot(p3.x - p2.x, p3.z - p2.z)).toBeLessThan(200);
  });

  it("dashes along this tick's move, else along current facing", () => {
    expect(resolveDashDir({ dx: 0, dz: -180 }, 0)).toEqual({ x: 0, z: -180 });
    expect(resolveDashDir(undefined, 0)).toEqual({ x: 0, z: 1000 }); // sim yaw 0 faces +z
    expect(resolveDashDir(undefined, 256)).toEqual({ x: 1000, z: 0 }); // yaw 256 faces +x
    expect(resolveDashDir({ dx: 0, dz: 0 }, 512)).toEqual({ x: 0, z: -1000 });
  });
});
