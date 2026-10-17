/**
 * tests/client/render/telegraphStyle.test.ts
 *
 * D10 readability: render/telegraphFill.ts's pure class -> style table gives every telegraph class
 * its own colour AND its own edge pattern (shape plus colour, never colour alone -- BIBLE sec 3,
 * research/06), stays clear of the party role colours in render/roleColor.ts, and reuses
 * roleColor's enemy red for the boss so "red = the boss/enemy" stays one vocabulary.
 */
import { describe, expect, it } from "vitest";
import { COLOR_DAMAGE, COLOR_ENEMY, COLOR_HEALER, COLOR_TANK } from "../../../client/src/render/roleColor.js";
import { TELEGRAPH_STYLES, edgePattern, telegraphStyle } from "../../../client/src/render/telegraphFill.js";

const CLASSES = ["avoid", "spread", "stack", "boss"] as const;

describe("telegraphStyle: one colour + one edge pattern per class", () => {
  it("assigns four distinct colours", () => {
    const colours = CLASSES.map((c) => TELEGRAPH_STYLES[c].color);
    expect(new Set(colours).size).toBe(4);
  });

  it("assigns four distinct edge styles with four distinct line patterns", () => {
    const edges = CLASSES.map((c) => TELEGRAPH_STYLES[c].edge);
    expect(new Set(edges).size).toBe(4);
    const patterns = CLASSES.map((c) => JSON.stringify(edgePattern(TELEGRAPH_STYLES[c].edge)));
    expect(new Set(patterns).size).toBe(4);
  });

  it("the boss uses roleColor's enemy red; no class reuses a party role colour", () => {
    expect(TELEGRAPH_STYLES.boss.color).toBe(COLOR_ENEMY);
    for (const c of CLASSES) {
      expect([COLOR_TANK, COLOR_HEALER, COLOR_DAMAGE]).not.toContain(TELEGRAPH_STYLES[c].color);
    }
  });

  it("only the boss is red: avoid/spread/stack never fall back to the old danger red", () => {
    for (const c of ["avoid", "spread", "stack"] as const) expect(TELEGRAPH_STYLES[c].color).not.toBe(COLOR_ENEMY);
  });

  it("telegraphStyle looks up the class and falls back to avoid when a snapshot has none", () => {
    expect(telegraphStyle("spread")).toBe(TELEGRAPH_STYLES.spread);
    expect(telegraphStyle(undefined)).toBe(TELEGRAPH_STYLES.avoid);
  });

  it("edge patterns: solid = 1 loop no dash, double = 2 loops, dashed/dotted dash with different lengths", () => {
    expect(edgePattern("solid")).toEqual({ loops: 1, dash: null });
    expect(edgePattern("double").loops).toBe(2);
    const dashed = edgePattern("dashed").dash;
    const dotted = edgePattern("dotted").dash;
    expect(dashed).not.toBeNull();
    expect(dotted).not.toBeNull();
    expect(dashed!.dashSize).toBeGreaterThan(dotted!.dashSize);
  });
});
