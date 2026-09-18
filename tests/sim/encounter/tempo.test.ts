import { describe, expect, it } from "vitest";
import {
  scaleCastEvery,
  scaleLeadTicks,
  scaleHealth,
  unlockedMechanicCount,
  tempoMultiplierBp,
  BP_DENOM,
  TEMPO_BP_PER_KEY,
  LEAD_SHRINK_BP_PER_KEY,
  LEAD_FLOOR_TICKS,
  EVERY_FLOOR_TICKS,
  HEALTH_BP_PER_KEY,
} from "../../../sim/encounter/tempo";

describe("Tempo", () => {
  it("key 1 applies no scaling", () => {
    expect(tempoMultiplierBp(1)).toBe(BP_DENOM);
    expect(scaleCastEvery(100, 1)).toBe(100);
    expect(scaleHealth(1000, 1)).toBe(1000);
  });

  it("key 15 shrinks `every` by the documented basis-point formula", () => {
    const mult = BP_DENOM + TEMPO_BP_PER_KEY * 14;
    const expected = Math.max(EVERY_FLOOR_TICKS, Math.trunc((100 * BP_DENOM) / mult));
    expect(scaleCastEvery(100, 15)).toBe(expected);
    expect(scaleCastEvery(100, 15)).toBeLessThan(100);
  });

  it("`every` never shrinks below its documented floor, however high the key", () => {
    expect(scaleCastEvery(40, 100)).toBe(EVERY_FLOOR_TICKS);
  });

  it("key 15 shrinks telegraph leadTicks by the documented basis-point formula", () => {
    const shrinkBp = LEAD_SHRINK_BP_PER_KEY * 14;
    const expected = Math.max(LEAD_FLOOR_TICKS, Math.trunc((40 * (BP_DENOM - shrinkBp)) / BP_DENOM));
    expect(scaleLeadTicks(40, 15)).toBe(expected);
    expect(scaleLeadTicks(40, 15)).toBeLessThan(40);
  });

  it("leadTicks never shrinks below its documented floor, however high the key", () => {
    expect(scaleLeadTicks(40, 60)).toBe(LEAD_FLOOR_TICKS);
  });

  it("health scales gently by the documented basis-point formula", () => {
    const bp = BP_DENOM + HEALTH_BP_PER_KEY * 9;
    expect(scaleHealth(1000, 10)).toBe(Math.trunc((1000 * bp) / BP_DENOM));
  });

  it("unlocks one extra mechanic at each of keys 5, 10, 15", () => {
    expect(unlockedMechanicCount(4)).toBe(0);
    expect(unlockedMechanicCount(5)).toBe(1);
    expect(unlockedMechanicCount(9)).toBe(1);
    expect(unlockedMechanicCount(10)).toBe(2);
    expect(unlockedMechanicCount(15)).toBe(3);
    expect(unlockedMechanicCount(20)).toBe(3);
  });
});
