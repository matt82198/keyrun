import { describe, expect, it } from "vitest";
import { computeTicksToRun, DEFAULT_MAX_CATCH_UP_TICKS } from "../../../client/src/sim-host/scheduler.js";

const TICK_MS = 50; // 20Hz

describe("scheduler: computeTicksToRun", () => {
  it("runs zero ticks when less than one tick's worth of time has passed", () => {
    const plan = computeTicksToRun(0, 20, TICK_MS);
    expect(plan.ticks).toBe(0);
    expect(plan.nextAccumulatedMs).toBe(20);
  });

  it("runs exactly one tick for one tick's worth of elapsed time", () => {
    const plan = computeTicksToRun(0, 50, TICK_MS);
    expect(plan.ticks).toBe(1);
    expect(plan.nextAccumulatedMs).toBe(0);
  });

  it("carries a sub-tick remainder forward across calls", () => {
    const first = computeTicksToRun(0, 70, TICK_MS);
    expect(first.ticks).toBe(1);
    expect(first.nextAccumulatedMs).toBe(20);
    const second = computeTicksToRun(first.nextAccumulatedMs, 40, TICK_MS);
    expect(second.ticks).toBe(1);
    expect(second.nextAccumulatedMs).toBe(10);
  });

  it("catches up multiple ticks after a stall, up to the cap", () => {
    const plan = computeTicksToRun(0, 3 * TICK_MS, TICK_MS, 5);
    expect(plan.ticks).toBe(3);
    expect(plan.nextAccumulatedMs).toBe(0);
  });

  it("caps catch-up and drops the excess backlog rather than growing it", () => {
    const plan = computeTicksToRun(0, 1000 * TICK_MS, TICK_MS, 5);
    expect(plan.ticks).toBe(5);
    expect(plan.nextAccumulatedMs).toBe(0);
  });

  it("uses DEFAULT_MAX_CATCH_UP_TICKS when no cap is given", () => {
    const plan = computeTicksToRun(0, 1000 * TICK_MS, TICK_MS);
    expect(plan.ticks).toBe(DEFAULT_MAX_CATCH_UP_TICKS);
  });

  it("never runs a negative number of ticks for negative/zero elapsed time", () => {
    const plan = computeTicksToRun(10, -5, TICK_MS);
    expect(plan.ticks).toBe(0);
    expect(plan.nextAccumulatedMs).toBe(10);
  });
});
