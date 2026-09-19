/**
 * tests/client/sim-host/snapshot.test.ts
 *
 * research/11-tick-rate-measurement.md (D9) found the client's real-time pacing constant
 * (formerly snapshot.ts's own `SIM_HZ = 20` literal) wired independently from sim/core/tick.ts's
 * real `TICK_HZ` -- a latent footgun the two just happened to agree on. `SIM_HZ` is deleted;
 * snapshot.ts now re-exports sim/core/tick.ts's TICK_HZ directly and derives SIM_TICK_MS from it.
 * This test is the coupling assertion the brief calls for: the client's pacing constant literally
 * IS the sim's constant, not a copy that could drift.
 */
import { describe, expect, it } from "vitest";
import { TICK_HZ as SimTickHz } from "../../../sim/core/tick.js";
import { TICK_HZ as ClientTickHz, SIM_TICK_MS } from "../../../client/src/sim-host/snapshot.js";

describe("snapshot.ts: the client's pacing constant is the sim's constant", () => {
  it("re-exports sim/core/tick.ts's TICK_HZ unchanged (same value, same identity as a re-export)", () => {
    expect(ClientTickHz).toBe(SimTickHz);
  });

  it("derives SIM_TICK_MS from TICK_HZ rather than a separate literal", () => {
    expect(SIM_TICK_MS).toBeCloseTo(1000 / SimTickHz);
    expect(SIM_TICK_MS).toBeCloseTo(1000 / ClientTickHz);
  });
});
