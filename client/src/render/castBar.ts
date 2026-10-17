// Pure cast-bar render model for render/actors.ts (no Three.js, so unit-testable under Node --
// same split as telegraphFill.ts). D10: the interrupt is Keyrun's signature mechanic, so a cast
// that can be kicked must look different from one that cannot, and a landed kick must be seen.
import type { ActorCast, SnapshotEvent } from "../sim-host/snapshot.js";

/** Bright gold: this cast can be kicked -- go. */
export const CAST_FILL_KICKABLE = "#f4d35e";
/** Steel grey: this cast cannot be kicked -- do not spend the 24 s interrupt on it. */
export const CAST_FILL_UNKICKABLE = "#8d99ae";
/** Hot red full bar: a kick just landed and stopped this cast. */
export const INTERRUPT_FLASH_FILL = "#ff2e63";
/** How many sim ticks (20 Hz) the interrupt flash stays up: 0.5 s. */
export const INTERRUPT_FLASH_TICKS = 10;

export interface CastBarModel {
  visible: boolean;
  fraction: number;
  fill: string;
  flash: boolean;
}

const HIDDEN: CastBarModel = { visible: false, fraction: 0, fill: CAST_FILL_UNKICKABLE, flash: false };

/**
 * What the cast bar shows at `currentTick`. `interruptedAt` is the sim tick of the actor's most
 * recent landed kick (InterruptLatch), or null. The flash covers the N ticks after the kick lands
 * and wins over a cast in that window; otherwise a live cast fills by progress in its kickable or
 * unkickable colour; otherwise the bar is hidden.
 */
export function castBarModel(cast: ActorCast | null, currentTick: number, interruptedAt: number | null): CastBarModel {
  if (interruptedAt !== null && currentTick > interruptedAt && currentTick <= interruptedAt + INTERRUPT_FLASH_TICKS) {
    return { visible: true, fraction: 1, fill: INTERRUPT_FLASH_FILL, flash: true };
  }
  if (!cast) return HIDDEN;
  const span = Math.max(1, cast.endTick - cast.startTick);
  const fraction = Math.min(1, Math.max(0, (currentTick - cast.startTick) / span));
  return { visible: true, fraction, fill: cast.kickable === true ? CAST_FILL_KICKABLE : CAST_FILL_UNKICKABLE, flash: false };
}

/** Remembers each actor's latest landed kick from Snapshot.events (which are one-shot), so the
 * flash can outlive the single snapshot that carried it. Re-ingesting the same events is a no-op. */
export class InterruptLatch {
  private readonly lastKick = new Map<number, number>();

  ingest(events: readonly SnapshotEvent[] | undefined): void {
    for (const e of events ?? []) {
      if (e.type !== "interrupt") continue;
      const prev = this.lastKick.get(e.actorId);
      if (prev === undefined || e.tick > prev) this.lastKick.set(e.actorId, e.tick);
    }
  }

  interruptedAt(actorId: number): number | null {
    return this.lastKick.get(actorId) ?? null;
  }
}
