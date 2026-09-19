/**
 * server/src/main.ts
 *
 * `npm run server`: starts the `ws` WebSocketServer on :8792 and wires connections to lobby.ts /
 * room.ts. The only file allowed to touch `ws`/`node:http` directly (server/CLAUDE.md) -- every
 * other file talks in plain messages and lets this one move bytes.
 *
 * Usage (tests): `const handle = startServer(0, loadServerContent()); ... await handle.close();`
 * (port 0 asks the OS for an ephemeral free port -- `handle.port` reports which one it got.)
 */
import { WebSocketServer, type WebSocket } from "ws";
import { Lobby } from "./lobby.js";
import { loadServerContent, type ServerContent } from "./content-setup.js";
import { parseClientFrame, errorMessage, PROTOCOL_VERSION, type ClientMessage, type ServerMessage } from "./protocol.js";
import type { Room } from "./room.js";
import type { ActorId } from "../../sim/core/state";

export const DEFAULT_PORT = 8792;
const OPEN = 1;

interface Session {
  ws: WebSocket;
  actorId: ActorId | null;
  room: Room | null;
}

function send(ws: WebSocket, msg: ServerMessage): void {
  if (ws.readyState === OPEN) ws.send(JSON.stringify(msg));
}

function handleMessage(lobby: Lobby, session: Session, msg: ClientMessage): void {
  switch (msg.type) {
    case "join":
      send(session.ws, { v: PROTOCOL_VERSION, type: "welcome", protocolVersion: PROTOCOL_VERSION });
      return;
    case "queue": {
      const hit = lobby.handleQueue(msg.role);
      if (!hit) {
        send(session.ws, errorMessage("role_full"));
        return;
      }
      hit.room.attachHuman(hit.actorId, session.ws);
      session.actorId = hit.actorId;
      session.room = hit.room;
      const classId = hit.room.classIdForActor(hit.actorId) ?? "";
      send(session.ws, { v: PROTOCOL_VERSION, type: "joined", actorId: hit.actorId, role: msg.role, classId, roomId: hit.room.id });
      return;
    }
    case "cmd": {
      if (session.actorId === null || !session.room) {
        send(session.ws, errorMessage("not_joined"));
        return;
      }
      const result = session.room.enqueueCommand(session.actorId, msg.cmd);
      if (!result.ok) {
        send(session.ws, errorMessage(result.reason));
        return;
      }
      if (result.late) send(session.ws, { v: PROTOCOL_VERSION, type: "late", kind: "cmd", originalTick: result.late.originalTick, newTick: result.late.newTick });
      return;
    }
    case "leave": {
      if (session.room && session.actorId !== null) {
        session.room.detachHuman(session.actorId);
        session.room = null;
        session.actorId = null;
      }
      return;
    }
  }
}

export interface ServerHandle {
  wss: WebSocketServer;
  port: number;
  close: () => Promise<void>;
}

/** Resolves once the WebSocketServer is actually bound and listening -- `port: 0` asks the OS for
 * an ephemeral free port, so `handle.port` is the only reliable way to learn which one it got. */
export function startServer(port: number, content: ServerContent): Promise<ServerHandle> {
  const lobby = new Lobby(content);
  const sessions = new Map<WebSocket, Session>();

  return new Promise((resolve) => {
    const wss = new WebSocketServer({ port }, () => {
      const address = wss.address();
      const actualPort = typeof address === "object" && address !== null ? address.port : port;
      resolve({
        wss,
        port: actualPort,
        close: () =>
          new Promise<void>((closeResolve) => {
            lobby.stopAll();
            for (const session of sessions.values()) session.ws.terminate();
            wss.close(() => closeResolve());
          }),
      });
    });

    wss.on("connection", (ws: WebSocket) => {
      const session: Session = { ws, actorId: null, room: null };
      sessions.set(ws, session);

      ws.on("message", (data: Buffer) => {
        const msg = parseClientFrame(data.toString());
        if (!msg) {
          send(ws, errorMessage("malformed_message"));
          return;
        }
        handleMessage(lobby, session, msg);
      });

      ws.on("close", () => {
        if (session.room && session.actorId !== null) session.room.detachHuman(session.actorId);
        sessions.delete(ws);
      });
    });
  });
}

/** run.mjs's actual bootstrap step (bundle main.ts, then call this) -- separated from a bare
 * "am I the entry module" self-check because run.mjs bundles and dynamically `import()`s this
 * file, under which `import.meta.url` never equals `process.argv[1]` no matter how the process was
 * invoked. */
export function boot(): void {
  startServer(DEFAULT_PORT, loadServerContent()).then((handle) => {
    console.log(`keyrun server listening on :${handle.port}`);
  });
}
