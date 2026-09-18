import { describe, expect, it } from "vitest";
import { initialRunEndState, stepRunEnd, STUB_CARDS, WIPE_GRACE_TICKS } from "../../../client/src/hud/run-end.js";

const baseSignals = { tick: 0, bossDead: false, depleted: false, allPlayersDead: false, abandonPressed: false };

describe("hud/run-end: stepRunEnd", () => {
  it("stays running while nothing has happened", () => {
    const state = stepRunEnd(initialRunEndState(), { ...baseSignals, tick: 100 });
    expect(state.info).toBeNull();
  });

  it("ends 'timed' when the boss dies before Slack depletes", () => {
    const state = stepRunEnd(initialRunEndState(), { ...baseSignals, tick: 500, bossDead: true, depleted: false });
    expect(state.info).toEqual({ outcome: "timed", tick: 500 });
  });

  it("ends 'depleted' when the boss dies after Slack hit zero", () => {
    const state = stepRunEnd(initialRunEndState(), { ...baseSignals, tick: 500, bossDead: true, depleted: true });
    expect(state.info).toEqual({ outcome: "depleted", tick: 500 });
  });

  it("ends 'abandoned' immediately on an abandon-key press", () => {
    const state = stepRunEnd(initialRunEndState(), { ...baseSignals, tick: 10, abandonPressed: true });
    expect(state.info).toEqual({ outcome: "abandoned", tick: 10 });
  });

  it("does not end on a wipe before the grace window elapses", () => {
    let state = initialRunEndState();
    state = stepRunEnd(state, { ...baseSignals, tick: 0, allPlayersDead: true });
    state = stepRunEnd(state, { ...baseSignals, tick: WIPE_GRACE_TICKS - 1, allPlayersDead: true });
    expect(state.info).toBeNull();
    expect(state.wipeStartTick).toBe(0);
  });

  it("auto-abandons once the wipe grace window elapses", () => {
    let state = initialRunEndState();
    state = stepRunEnd(state, { ...baseSignals, tick: 0, allPlayersDead: true });
    state = stepRunEnd(state, { ...baseSignals, tick: WIPE_GRACE_TICKS, allPlayersDead: true });
    expect(state.info).toEqual({ outcome: "abandoned", tick: WIPE_GRACE_TICKS });
  });

  it("clears the wipe timer if the party recovers before the grace window elapses", () => {
    let state = initialRunEndState();
    state = stepRunEnd(state, { ...baseSignals, tick: 0, allPlayersDead: true });
    state = stepRunEnd(state, { ...baseSignals, tick: 10, allPlayersDead: false });
    expect(state.wipeStartTick).toBeNull();
    expect(state.info).toBeNull();
  });

  it("is terminal: further signals never change an already-ended state", () => {
    let state = stepRunEnd(initialRunEndState(), { ...baseSignals, tick: 5, abandonPressed: true });
    state = stepRunEnd(state, { ...baseSignals, tick: 999, bossDead: true });
    expect(state.info).toEqual({ outcome: "abandoned", tick: 5 });
  });
});

describe("hud/run-end: STUB_CARDS", () => {
  it("ships exactly three placeholder choice cards with distinct ids", () => {
    expect(STUB_CARDS).toHaveLength(3);
    expect(new Set(STUB_CARDS.map((c) => c.id)).size).toBe(3);
  });
});
