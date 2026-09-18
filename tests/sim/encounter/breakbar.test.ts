import { describe, expect, it } from "vitest";
import {
  createBreakBar,
  drainBreakBar,
  tickBreakBarReset,
  RESET_COOLDOWN_TICKS,
  type BreakBarInput,
} from "../../../sim/encounter/breakbar";
import type { SimEvent } from "../../../sim/encounter/types";

describe("break bar", () => {
  it("drains only on control/interrupt-tagged inputs for the current tick", () => {
    let bar = createBreakBar(100, [10, 11]);
    const emitted: SimEvent[] = [];
    const emit = (e: SimEvent) => emitted.push(e);

    const inputs: BreakBarInput[] = [
      { tick: 5, actorId: 10, tag: "control", amount: 30 },
      { tick: 5, actorId: 11, tag: "interrupt", amount: 20 },
      { tick: 6, actorId: 10, tag: "control", amount: 999 }, // wrong tick, ignored
    ];
    bar = drainBreakBar(bar, "twins", inputs, 5, emit);
    expect(bar.current).toBe(50);
    expect(emitted).toHaveLength(0);
  });

  it("emits stagger exactly once at empty, then locks the bar until the cooldown elapses, then resets to max", () => {
    let bar = createBreakBar(100, [10, 11]);
    const emitted: SimEvent[] = [];
    const emit = (e: SimEvent) => emitted.push(e);

    bar = drainBreakBar(bar, "twins", [{ tick: 1, actorId: 10, tag: "control", amount: 60 }], 1, emit);
    bar = drainBreakBar(bar, "twins", [{ tick: 2, actorId: 10, tag: "control", amount: 60 }], 2, emit);
    expect(bar.current).toBe(0);
    expect(emitted).toHaveLength(1);
    expect(emitted[0]).toMatchObject({ tick: 2, type: "stagger", barId: "twins", actorIds: [10, 11] });
    const readyTick = bar.cooldownReadyTick;
    expect(readyTick).toBe(2 + RESET_COOLDOWN_TICKS);

    // further drain attempts while staggered are no-ops (no second stagger)
    bar = drainBreakBar(bar, "twins", [{ tick: 3, actorId: 10, tag: "control", amount: 999 }], 3, emit);
    expect(emitted).toHaveLength(1);
    expect(bar.current).toBe(0);

    // reset does not fire before the cooldown elapses
    bar = tickBreakBarReset(bar, readyTick - 1);
    expect(bar.current).toBe(0);

    // reset fires the tick the cooldown elapses
    bar = tickBreakBarReset(bar, readyTick);
    expect(bar.current).toBe(100);
    expect(bar.cooldownReadyTick).toBe(0);
  });
});
