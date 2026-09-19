/**
 * server/src/lobby.ts
 *
 * Role queue. design/BIBLE.md Sec 10 / STATE.md D8 test 2 ("launch to a high key in under a
 * minute, no gatekeeping"): the instant a role is queued, a room exists and is ticking -- there is
 * no waiting for the other four roles to fill with humans, bots take every open slot immediately.
 * A human queuing later for an already-running room's bot-controlled role slot is the *same*
 * mechanism as mid-run backfill (room.ts's `findOpenSlot`/`attachHuman`); this file just decides
 * which room a queue request goes to.
 *
 * v2 (server-run-lifecycle, P1): CHANGE from v0/v1 -- a role with every matching slot already
 * human-controlled no longer reports "no slot available" (there was never a design reason to block
 * it, only a v0/v1 scope cut, and it contradicts D8's own "no gatekeeping" test); it now opens (or
 * reuses) ANOTHER concurrent room instead, same as WoW's own M+ has no room cap. `keyLevel` is
 * fixed at room-creation time from whichever queue request opened that room (content-setup.ts's
 * Tempo scaling, DEFAULT_KEY_LEVEL if unspecified) -- a later queue into the SAME room cannot
 * change it, same as a real key can't be re-leveled mid-run. A room is "released" (removed from
 * `rooms`, so no future queue ever reaches it) the instant server/src/room.ts reports it ended
 * (server/src/run-end.ts's result) -- never reused for a fresh run.
 *
 * Usage: `const lobby = new Lobby(registry); const hit = lobby.handleQueue("tank", 5);`
 */
import { Room } from "./room.js";
import { buildServerContent, DEFAULT_KEY_LEVEL, DUNGEON_ID } from "./content-setup.js";
import type { ContentRegistry } from "../../content/schema/registry";
import type { ActorId } from "../../sim/core/state";
import type { PartyRole } from "./roster.js";

export interface QueueHit {
  room: Room;
  actorId: ActorId;
}

export class Lobby {
  private readonly registry: ContentRegistry;
  private readonly rooms = new Map<string, Room>();
  private roomCounter = 0;

  constructor(registry: ContentRegistry) {
    this.registry = registry;
  }

  private newRoom(keyLevel: number): Room {
    this.roomCounter += 1;
    const id = `room-${this.roomCounter}`;
    const content = buildServerContent(this.registry, DUNGEON_ID, keyLevel);
    const room = new Room(id, content, (endedId) => this.rooms.delete(endedId));
    this.rooms.set(id, room);
    room.start();
    return room;
  }

  /** First room (in creation order) with an open slot for `role`; never null -- opens a fresh room
   * (v2 CHANGE, see file docstring) rather than reporting "role_full". */
  handleQueue(role: PartyRole, keyLevel: number = DEFAULT_KEY_LEVEL): QueueHit {
    for (const room of this.rooms.values()) {
      const actorId = room.findOpenSlot(role);
      if (actorId !== null) return { room, actorId };
    }
    const room = this.newRoom(keyLevel);
    const actorId = room.findOpenSlot(role);
    if (actorId === null) throw new Error("lobby: a freshly created room has no open slot -- ROLE_ROSTER is misconfigured");
    return { room, actorId };
  }

  /** Stops every room's tick loop -- test/server teardown. */
  stopAll(): void {
    for (const room of this.rooms.values()) room.stop();
    this.rooms.clear();
  }
}
