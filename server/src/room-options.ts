/**
 * server/src/room-options.ts
 *
 * run-options-wiring: builds the `sim/index.ts` `RunOptions` a fresh Room's `createRun` call needs
 * from the ONE queue request that actually opened it (`RoomFirstQueuer` -- see room.ts's RUN
 * OPTIONS docstring for the full "fixed at room creation" contract this follows, same rule
 * `keyLevel` already uses). Pulled out of room.ts to keep that file under the repo's 190-LOC cap
 * (same reason command-queue.ts was already split out).
 *
 * Usage: `const options = firstQueuerOptions(serverContent, firstQueuer);`
 *        `const run = createRun(SEED, party, sc.dungeon, sc.content, options);`
 */
import type { RunOptions } from "../../sim/index";
import { ROLE_ROSTER, actorIdForSlot, type PartyRole } from "./roster.js";
import { resolveLoadoutAbilities, type ServerContent } from "./content-setup.js";
import type { LoadoutPayload } from "./protocol.js";

/** The queue request that opened this room -- see room.ts's RUN OPTIONS docstring. */
export interface RoomFirstQueuer {
  role: PartyRole;
  clauseId?: string;
  loadout?: LoadoutPayload;
}

/** DEVIATION/KNOWN LIMITATION: a human who backfills into an already-running room's bot slot
 * later (room.ts's `findOpenSlot`/`attachHuman`) does NOT get their own loadout applied
 * retroactively -- `Room.run` (and its `RunContent.abilities`) is built once, at construction,
 * before any backfill can happen; bots never carried a custom loadout either, so this is a
 * documented limitation, not a new regression. Either field throwing (an unknown clauseId, an
 * invariant-breaking loadout) propagates out of this call -- room.ts's constructor lets it
 * propagate further, up through lobby.ts to server/src/main.ts's queue handler, which turns it
 * into a clean `error` ServerMessage, never a crash. */
export function firstQueuerOptions(sc: ServerContent, firstQueuer?: RoomFirstQueuer): RunOptions | undefined {
  if (!firstQueuer) return undefined;
  const options: RunOptions = {};
  if (firstQueuer.clauseId !== undefined) options.clauseId = firstQueuer.clauseId;
  if (firstQueuer.loadout !== undefined) {
    const slotIndex = ROLE_ROSTER.findIndex((slot) => slot.role === firstQueuer.role);
    if (slotIndex >= 0) {
      const actorId = actorIdForSlot(slotIndex);
      const classId = ROLE_ROSTER[slotIndex]!.classId;
      options.loadouts = { [actorId]: resolveLoadoutAbilities(sc.registry, classId, firstQueuer.loadout) };
    }
  }
  return options;
}
