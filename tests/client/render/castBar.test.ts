/**
 * tests/client/render/castBar.test.ts
 *
 * Render model behind render/actors.ts's cast bar (D10: readability of the interrupt). Pure -- no
 * Three.js -- so it runs under Node: given an actor's cast, the current tick and the interrupt
 * latch fed from Snapshot.events, what does the bar show?
 */
import { describe, expect, it } from "vitest";
import {
  CAST_FILL_KICKABLE,
  CAST_FILL_UNKICKABLE,
  INTERRUPT_FLASH_FILL,
  INTERRUPT_FLASH_TICKS,
  InterruptLatch,
  castBarModel,
} from "../../../client/src/render/castBar.js";
import type { ActorCast, SnapshotEvent } from "../../../client/src/sim-host/snapshot.js";

const cast = (kickable: boolean): ActorCast => ({ abilityId: "choir.healCast", startTick: 100, endTick: 180, kickable });

describe("castBarModel: kickable casts look different from unkickable ones", () => {
  it("the two cast-bar fills are visibly distinct colours, and distinct from the flash", () => {
    expect(new Set([CAST_FILL_KICKABLE, CAST_FILL_UNKICKABLE, INTERRUPT_FLASH_FILL]).size).toBe(3);
  });

  it("a kickable cast renders in the kickable style with its progress", () => {
    expect(castBarModel(cast(true), 140, null)).toEqual({ visible: true, fraction: 0.5, fill: CAST_FILL_KICKABLE, flash: false });
  });

  it("an unkickable cast renders in the unkickable style", () => {
    expect(castBarModel(cast(false), 140, null)).toEqual({ visible: true, fraction: 0.5, fill: CAST_FILL_UNKICKABLE, flash: false });
  });

  it("no cast and no recent interrupt hides the bar", () => {
    expect(castBarModel(null, 140, null).visible).toBe(false);
  });
});

describe("interrupt flash: shown for exactly INTERRUPT_FLASH_TICKS after the kick lands", () => {
  const kick: SnapshotEvent = { type: "interrupt", tick: 191, actorId: 102, sourceId: 1, school: "physical" };

  it("latches from a snapshot's events and flashes the bar for N ticks, then clears", () => {
    expect(INTERRUPT_FLASH_TICKS).toBeGreaterThan(1);
    const latch = new InterruptLatch();
    latch.ingest([kick]);
    latch.ingest([kick]); // the same snapshot re-read on every render frame is idempotent
    const at = latch.interruptedAt(102);
    expect(at).toBe(191);
    expect(latch.interruptedAt(1)).toBeNull();

    for (let t = 192; t <= 191 + INTERRUPT_FLASH_TICKS; t++) {
      expect(castBarModel(null, t, at)).toEqual({ visible: true, fraction: 1, fill: INTERRUPT_FLASH_FILL, flash: true });
    }
    expect(castBarModel(null, 192 + INTERRUPT_FLASH_TICKS, at).visible).toBe(false);
  });

  it("a new cast started after the flash window renders normally", () => {
    const later = { abilityId: "choir.healCast", startTick: 400, endTick: 480, kickable: true };
    expect(castBarModel(later, 440, 191)).toEqual({ visible: true, fraction: 0.5, fill: CAST_FILL_KICKABLE, flash: false });
  });

  it("an empty or missing events list leaves the latch untouched", () => {
    const latch = new InterruptLatch();
    latch.ingest(undefined);
    latch.ingest([]);
    expect(latch.interruptedAt(102)).toBeNull();
  });
});
