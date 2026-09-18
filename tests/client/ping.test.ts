/**
 * tests/client/ping.test.ts
 *
 * G-key -> PingEvent derivation (client/src/input/ping.ts) and the
 * pure label/expiry rules the ping marker render pool uses
 * (client/src/render/pings.ts).
 *
 * Usage: `npx vitest run tests/client/ping.test.ts`
 */
import { describe, expect, it } from "vitest";
import { derivePingEvent, PingRateLimiter, PING_RATE_LIMIT_TICKS } from "../../client/src/input/ping.js";
import type { PingEvent } from "../../client/src/input/ping.js";
import {
  pingLabelText,
  isPingExpired,
  pingLifeRemainingFraction,
  PING_LABEL_LIFETIME_TICKS,
} from "../../client/src/render/pingLabel.js";

const PING_KEY = "KeyG";

describe("derivePingEvent", () => {
  it("emits nothing when G was not just pressed", () => {
    expect(derivePingEvent(new Set(), PING_KEY, 10, 1, null, { x: 0, z: 0 }, new PingRateLimiter())).toBeNull();
  });

  it("emits a target ping when the actor has a current target", () => {
    const event = derivePingEvent(new Set([PING_KEY]), PING_KEY, 10, 1, 42, { x: 0, z: 0 }, new PingRateLimiter());
    expect(event).toEqual({ tick: 10, actorId: 1, kind: "target", targetId: 42 });
  });

  it("emits a spot ping at the ground point when the actor has no target", () => {
    const event = derivePingEvent(
      new Set([PING_KEY]),
      PING_KEY,
      10,
      1,
      null,
      { x: 3.5, z: -2 },
      new PingRateLimiter(),
    );
    expect(event).toEqual({ tick: 10, actorId: 1, kind: "spot", point: { x: 3.5, z: -2 } });
  });

  it("ignores a key that is not the configured ping bind", () => {
    expect(
      derivePingEvent(new Set(["KeyH"]), PING_KEY, 10, 1, 42, { x: 0, z: 0 }, new PingRateLimiter()),
    ).toBeNull();
  });

  it("rate-limits an actor to one ping per 20 ticks", () => {
    const limiter = new PingRateLimiter();
    const first = derivePingEvent(new Set([PING_KEY]), PING_KEY, 0, 1, 42, { x: 0, z: 0 }, limiter);
    expect(first).not.toBeNull();

    const tooSoon = derivePingEvent(
      new Set([PING_KEY]),
      PING_KEY,
      PING_RATE_LIMIT_TICKS - 1,
      1,
      42,
      { x: 0, z: 0 },
      limiter,
    );
    expect(tooSoon).toBeNull();

    const allowedAgain = derivePingEvent(
      new Set([PING_KEY]),
      PING_KEY,
      PING_RATE_LIMIT_TICKS,
      1,
      42,
      { x: 0, z: 0 },
      limiter,
    );
    expect(allowedAgain).not.toBeNull();
  });

  it("rate-limits per actor, not globally", () => {
    const limiter = new PingRateLimiter();
    derivePingEvent(new Set([PING_KEY]), PING_KEY, 0, 1, 42, { x: 0, z: 0 }, limiter);
    const otherActor = derivePingEvent(new Set([PING_KEY]), PING_KEY, 1, 2, 42, { x: 0, z: 0 }, limiter);
    expect(otherActor).not.toBeNull();
  });
});

describe("PingRateLimiter", () => {
  it("reset() clears history so any actor may ping immediately", () => {
    const limiter = new PingRateLimiter();
    limiter.record(1, 0);
    expect(limiter.canPing(1, 1)).toBe(false);
    limiter.reset();
    expect(limiter.canPing(1, 1)).toBe(true);
  });
});

describe("pingLabelText", () => {
  const spot: PingEvent = { tick: 0, actorId: 1, kind: "spot", point: { x: 0, z: 0 } };
  const target: PingEvent = { tick: 0, actorId: 1, kind: "target", targetId: 9 };

  it("labels a spot ping 'Stack here'", () => {
    expect(pingLabelText(spot)).toBe("Stack here");
  });

  it("labels a target ping on an interruptible cast 'Kick!'", () => {
    expect(pingLabelText(target, { targetIsInterruptibleCast: true, targetHasWindow: false })).toBe("Kick!");
  });

  it("labels a target ping on a Window carrier 'Window!'", () => {
    expect(pingLabelText(target, { targetIsInterruptibleCast: false, targetHasWindow: true })).toBe("Window!");
  });

  it("prefers 'Kick!' over 'Window!' when both are true", () => {
    expect(pingLabelText(target, { targetIsInterruptibleCast: true, targetHasWindow: true })).toBe("Kick!");
  });

  it("falls back to 'Focus' for a plain target ping", () => {
    expect(pingLabelText(target)).toBe("Focus");
  });
});

describe("ping expiry", () => {
  const event: PingEvent = { tick: 100, actorId: 1, kind: "spot", point: { x: 0, z: 0 } };

  it("is not expired the tick it was created", () => {
    expect(isPingExpired(event, 100)).toBe(false);
  });

  it("is not expired just under the 3-second lifetime", () => {
    expect(isPingExpired(event, 100 + PING_LABEL_LIFETIME_TICKS - 1)).toBe(false);
  });

  it("expires exactly at the 3-second lifetime", () => {
    expect(isPingExpired(event, 100 + PING_LABEL_LIFETIME_TICKS)).toBe(true);
  });

  it("fades life-remaining fraction from 1 to 0 across the lifetime", () => {
    expect(pingLifeRemainingFraction(event, 100)).toBe(1);
    expect(pingLifeRemainingFraction(event, 100 + PING_LABEL_LIFETIME_TICKS)).toBe(0);
    expect(pingLifeRemainingFraction(event, 100 + PING_LABEL_LIFETIME_TICKS / 2)).toBeCloseTo(0.5, 10);
  });
});
