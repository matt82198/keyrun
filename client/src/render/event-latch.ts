/**
 * client/src/render/event-latch.ts
 *
 * Accumulates Snapshot.events across snapshots that arrive between two render frames. The host
 * (worker or net) may deliver several snapshots per frame and hud-wiring.ts renders only the
 * latest STATE, but events are one-shot (present in exactly one snapshot), so they are latched
 * here on arrival and drained exactly once by the next frame. Bounded so a stalled render loop
 * (hidden tab: requestAnimationFrame paused while snapshots keep arriving) cannot grow it without
 * limit: past MAX_LATCHED_EVENTS the OLDEST events are dropped.
 *
 * Usage: `latch.push(snapshot.events)` in onSnapshot; `latch.drain()` once per frame.
 */
import { MAX_SNAPSHOT_EVENTS, type SnapshotEvent } from "../sim-host/snapshot.js";

/** Cap on events held between two frames: 8 snapshots' worth of a full event channel. */
export const MAX_LATCHED_EVENTS = MAX_SNAPSHOT_EVENTS * 8;

export class SnapshotEventLatch {
  private pending: SnapshotEvent[] = [];

  /** Appends one snapshot's events (in arrival order). */
  push(events: readonly SnapshotEvent[] | undefined): void {
    if (!events || events.length === 0) return;
    this.pending.push(...events);
    const over = this.pending.length - MAX_LATCHED_EVENTS;
    if (over > 0) this.pending.splice(0, over);
  }

  /** Returns every event latched since the last drain, in arrival order, and clears the latch. */
  drain(): SnapshotEvent[] {
    const out = this.pending;
    this.pending = [];
    return out;
  }
}
