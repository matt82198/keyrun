/**
 * tests/client/render/telegraph-contrast.test.ts
 *
 * Item 14: a telegraph must be readable the moment it appears. At the old 0.15 base fill opacity a
 * pull-2 spread circle (0xcc79a7) over the 0x1b2027 floor blended to ~1.2:1 contrast -- faint. The
 * blended first-sight colour of EVERY telegraph class must now reach MIN_CONTRAST against the floor
 * (WCAG relative-luminance ratio, alpha-blended in sRGB the way the transparent decal composites),
 * and the REAL render/telegraphs.ts TelegraphPool must draw a fresh decal at that opacity, still
 * filling in toward resolvesTick.
 */
import { describe, expect, it, vi } from "vitest";

vi.mock("three", () => import("../support/fake-three.js"));

import * as THREE from "three";
import { TelegraphPool, telegraphOpacity, TELEGRAPH_BASE_OPACITY } from "../../../client/src/render/telegraphs.js";
import { TELEGRAPH_STYLES } from "../../../client/src/render/telegraphFill.js";

/** client/src/render/scene.ts's ground plane colour. */
const FLOOR = 0x1b2027;
const MIN_CONTRAST = 1.6;

const channels = (hex: number): number[] => [(hex >> 16) & 255, (hex >> 8) & 255, hex & 255];
function luminance(rgb: number[]): number {
  const lin = rgb.map((c) => {
    const s = c / 255;
    return s <= 0.03928 ? s / 12.92 : ((s + 0.055) / 1.055) ** 2.4;
  });
  return 0.2126 * lin[0]! + 0.7152 * lin[1]! + 0.0722 * lin[2]!;
}
function contrastOver(color: number, alpha: number): number {
  const floor = channels(FLOOR);
  const blended = channels(color).map((c, i) => c * alpha + floor[i]! * (1 - alpha));
  const [hi, lo] = [luminance(blended), luminance(floor)].sort((a, b) => b - a);
  return (hi! + 0.05) / (lo! + 0.05);
}

describe("telegraph contrast at first sight (item 14)", () => {
  for (const [cls, style] of Object.entries(TELEGRAPH_STYLES)) {
    it(`${cls} reaches ${MIN_CONTRAST}:1 against the floor the moment it appears`, () => {
      expect(contrastOver(style.color, telegraphOpacity(0))).toBeGreaterThanOrEqual(MIN_CONTRAST);
    });
  }

  it("still fills in toward resolve: opacity rises from the base to a stronger, not opaque, peak", () => {
    expect(telegraphOpacity(0)).toBe(TELEGRAPH_BASE_OPACITY);
    expect(telegraphOpacity(0.5)).toBeGreaterThan(telegraphOpacity(0));
    expect(telegraphOpacity(1)).toBeGreaterThan(telegraphOpacity(0.5));
    expect(telegraphOpacity(1)).toBeLessThanOrEqual(0.8);
  });

  it("the TelegraphPool draws a just-appeared spread circle at the base opacity", () => {
    const scene = new THREE.Scene();
    const pool = new TelegraphPool(scene as never);
    pool.sync([{ id: "t1", shape: "circle", center: { x: 0, z: 0 }, radius: 3000, resolvesTick: 1000, danger: true, telegraphClass: "spread" } as never], 0);
    const mesh = (scene as unknown as { children: { material: { opacity: number } }[] }).children[0]!;
    expect(mesh.material.opacity).toBe(TELEGRAPH_BASE_OPACITY);
  });
});
