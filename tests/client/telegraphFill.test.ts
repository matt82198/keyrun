import { describe, expect, it } from "vitest";
import { telegraphFillFraction } from "../../client/src/render/telegraphFill.js";

describe("telegraphFillFraction", () => {
  it("is 0 a full warning window before it resolves", () => {
    expect(telegraphFillFraction(0, 60, 60)).toBe(0);
  });

  it("is 1 exactly on the resolving tick", () => {
    expect(telegraphFillFraction(60, 60, 60)).toBe(1);
  });

  it("is 0.5 halfway through the warning window", () => {
    expect(telegraphFillFraction(30, 60, 60)).toBeCloseTo(0.5, 10);
  });

  it("clamps to 1 after the resolving tick has passed", () => {
    expect(telegraphFillFraction(100, 60, 60)).toBe(1);
  });

  it("clamps to 0 well before the warning window starts", () => {
    expect(telegraphFillFraction(-1000, 60, 60)).toBe(0);
  });

  it("defaults warnTicks when not provided", () => {
    expect(telegraphFillFraction(0, 60)).toBe(0);
  });
});
