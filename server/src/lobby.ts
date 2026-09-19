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
 * DEVIATION (scope): one active room at a time -- a role with every matching slot already
 * human-controlled reports "no slot available" rather than opening a second concurrent room. Noted,
 * not silently scoped down; a follow-up lane can add a room pool.
 *
 * Usage: `const lobby = new Lobby(serverContent); const hit = lobby.handleQueue("tank");`
 */
import { Room } from "./room.js";
import type { ServerContent } from "./content-setup.js";
import type { ActorId } from "../../sim/core/state";
import type { PartyRole } from "./roster.js";

export interface QueueHit {
  room: Room;
  actorId: ActorId;
}

export class Lobby {
  private readonly content: ServerContent;
  private active: Room | null = null;
  private roomCounter = 0;

  constructor(content: ServerContent) {
    this.content = content;
  }

  private newRoom(): Room {
    this.roomCounter += 1;
    const room = new Room(`room-${this.roomCounter}`, this.content);
    room.start();
    this.active = room;
    return room;
  }

  /** Null means no open slot for this role right now (see DEVIATION above). */
  handleQueue(role: PartyRole): QueueHit | null {
    const room = this.active ?? this.newRoom();
    const actorId = room.findOpenSlot(role);
    if (actorId === null) return null;
    return { room, actorId };
  }

  /** Stops the active room's tick loop, if any -- test/server teardown. */
  stopAll(): void {
    this.active?.stop();
    this.active = null;
  }
}
