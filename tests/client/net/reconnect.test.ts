// Usage: client/src/net/reconnect.ts's backoff and backfill-gap detection are pure and
// deterministic -- no jitter, no clock reads -- so they're asserted as exact sequences here.
import { describe, expect, it } from "vitest";
import { DEFAULT_BACKOFF, ReconnectPolicy, detectBackfill } from "../../../client/src/net/reconnect.js";

describe("reconnect: ReconnectPolicy", () => {
  it("produces a deterministic capped-exponential delay sequence", () => {
    const policy = new ReconnectPolicy();
    expect(policy.nextDelayMs()).toBe(250);
    expect(policy.nextDelayMs()).toBe(500);
    expect(policy.nextDelayMs()).toBe(1000);
    expect(policy.nextDelayMs()).toBe(2000);
    expect(policy.nextDelayMs()).toBe(4000);
    expect(policy.nextDelayMs()).toBe(5000); // capped at maxMs
    expect(policy.nextDelayMs()).toBe(5000); // stays capped
  });

  it("reset() restarts the sequence from the base delay", () => {
    const policy = new ReconnectPolicy();
    policy.nextDelayMs();
    policy.nextDelayMs();
    expect(policy.attemptsSoFar()).toBe(2);
    policy.reset();
    expect(policy.attemptsSoFar()).toBe(0);
    expect(policy.nextDelayMs()).toBe(DEFAULT_BACKOFF.baseMs);
  });

  it("honors a custom backoff config", () => {
    const policy = new ReconnectPolicy({ baseMs: 100, maxMs: 300, factor: 3 });
    expect(policy.nextDelayMs()).toBe(100);
    expect(policy.nextDelayMs()).toBe(300); // 100*3 = 300, exactly at the cap
    expect(policy.nextDelayMs()).toBe(300); // 100*9 would be 900, capped to 300
  });
});

describe("reconnect: detectBackfill", () => {
  it("is never a backfill on the very first snapshot", () => {
    expect(detectBackfill(null, 0)).toBe(false);
    expect(detectBackfill(null, 500)).toBe(false);
  });

  it("is not a backfill for consecutive ticks", () => {
    expect(detectBackfill(10, 11)).toBe(false);
  });

  it("is a backfill when the tick jumps by more than one", () => {
    expect(detectBackfill(10, 12)).toBe(true);
    expect(detectBackfill(10, 200)).toBe(true);
  });
});
