/**
 * tests/client/net/integration.test.ts
 *
 * NetHostClient against a REAL server/src/main.ts WebSocketServer (imported and started in-process
 * on an OS-assigned port, same as tests/server/harness.ts -- no shell, no child process) and a
 * real `ws` WebSocket (the one runtime WebSocket dependency this repo has, already used by
 * tests/server/ws-client.ts's headless test client). Proves the whole net-client.ts stack --
 * handshake, per-tick snapshot streaming, and a real command round-trip -- works end to end, not
 * just against the fakes in net-client.test.ts.
 */
import { afterEach, describe, expect, it } from "vitest";
import { WebSocket as NodeWebSocket } from "ws";
import { startServer, type ServerHandle } from "../../../server/src/main.js";
import { loadServerContent } from "../../../server/src/content-setup.js";
import { NetHostClient } from "../../../client/src/net/net-client.js";
import type { WebSocketLike } from "../../../client/src/net/host-handle.js";
import type { Snapshot } from "../../../client/src/sim-host/snapshot.js";

async function waitUntil(pred: () => boolean, timeoutMs: number): Promise<void> {
  const startedAt = Date.now();
  while (!pred()) {
    if (Date.now() - startedAt > timeoutMs) throw new Error("integration test: timed out waiting for condition");
    await new Promise((resolve) => setTimeout(resolve, 10));
  }
}

describe("net-client: integration against the real in-process server", () => {
  let handle: ServerHandle | undefined;
  let stop: (() => void) | undefined;

  afterEach(async () => {
    stop?.();
    stop = undefined;
    await handle?.close();
    handle = undefined;
  });

  it(
    "joins/queues as warden, streams >=20 real snapshots, and a move command moves the actor",
    async () => {
      handle = await startServer(0, loadServerContent());
      const port = handle.port;
      const factory = (url: string): WebSocketLike => new NodeWebSocket(url) as unknown as WebSocketLike;

      const client = new NetHostClient(`?class=warden&server=ws://127.0.0.1:${port}`, 10, factory);
      const { humanActorId } = await client.whenReady();
      expect(humanActorId).toBe(1); // roster.ts: warden is the fixed tank slot, actorId 1

      const snapshots: Snapshot[] = [];
      stop = client.start((s) => snapshots.push(s));

      await waitUntil(() => snapshots.length >= 20, 10_000);
      expect(snapshots.length).toBeGreaterThanOrEqual(20);
      const self = snapshots.at(-1)!.actors.find((a) => a.id === humanActorId);
      expect(self).toBeDefined();

      const before = { x: self!.pos.x, z: self!.pos.z };
      client.applyCommand({ type: "move", dx: 200, dz: 0, tick: 0 }); // tick 0 is stale by now -- exercises the server's late re-stamp

      await waitUntil(() => {
        const latest = snapshots.at(-1)!.actors.find((a) => a.id === humanActorId);
        return !!latest && (latest.pos.x !== before.x || latest.pos.z !== before.z);
      }, 10_000);

      const after = snapshots.at(-1)!.actors.find((a) => a.id === humanActorId)!;
      expect(after.pos.x !== before.x || after.pos.z !== before.z).toBe(true);
    },
    20_000,
  );
});
