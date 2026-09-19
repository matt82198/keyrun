/**
 * tests/server/ws-client.ts
 *
 * A headless `ws` test client: connects, sends protocol.ts ClientMessages, and lets a test await a
 * specific ServerMessage (buffering anything received before anyone asked for it, so ordering
 * races between "send" and "waitFor" never lose a message). Not a *.test.ts file itself -- a
 * shared helper every tests/server/*.test.ts file imports.
 *
 * Usage: `const c = await TestClient.connect(port); c.send({v:1,type:"join"});
 * const welcome = await c.waitForType("welcome");`
 */
import { WebSocket } from "ws";
import type { ClientMessage, ServerMessage } from "../../server/src/protocol.js";

type Waiter = { pred: (m: ServerMessage) => boolean; resolve: (m: ServerMessage) => void; reject: (e: Error) => void; timer: ReturnType<typeof setTimeout> };

export class TestClient {
  private readonly queue: ServerMessage[] = [];
  private readonly waiters: Waiter[] = [];

  private constructor(private readonly ws: WebSocket) {
    ws.on("message", (data: Buffer) => this.onMessage(data.toString()));
  }

  static connect(port: number): Promise<TestClient> {
    return new Promise((resolve, reject) => {
      const ws = new WebSocket(`ws://127.0.0.1:${port}`);
      ws.once("open", () => resolve(new TestClient(ws)));
      ws.once("error", reject);
    });
  }

  private onMessage(text: string): void {
    let msg: ServerMessage;
    try {
      msg = JSON.parse(text) as ServerMessage;
    } catch {
      return;
    }
    const idx = this.waiters.findIndex((w) => w.pred(msg));
    if (idx >= 0) {
      const [w] = this.waiters.splice(idx, 1);
      clearTimeout(w!.timer);
      w!.resolve(msg);
      return;
    }
    this.queue.push(msg);
  }

  send(msg: ClientMessage): void {
    this.ws.send(JSON.stringify(msg));
  }

  /** Bypasses ClientMessage validity entirely -- for malformed-input tests. */
  sendRaw(text: string): void {
    this.ws.send(text);
  }

  waitFor(pred: (m: ServerMessage) => boolean, timeoutMs = 4000): Promise<ServerMessage> {
    const bufferedIdx = this.queue.findIndex(pred);
    if (bufferedIdx >= 0) {
      const [m] = this.queue.splice(bufferedIdx, 1);
      return Promise.resolve(m as ServerMessage);
    }
    return new Promise((resolve, reject) => {
      const timer = setTimeout(() => {
        const at = this.waiters.indexOf(waiter);
        if (at >= 0) this.waiters.splice(at, 1);
        reject(new Error("TestClient.waitFor: timed out"));
      }, timeoutMs);
      const waiter: Waiter = { pred, resolve, reject, timer };
      this.waiters.push(waiter);
    });
  }

  waitForType(type: ServerMessage["type"], timeoutMs?: number): Promise<ServerMessage> {
    return this.waitFor((m) => m.type === type, timeoutMs);
  }

  close(): void {
    this.ws.close();
  }
}
