/**
 * tests/server/harness.ts
 *
 * Starts a real server/src/main.ts WebSocketServer on an OS-assigned ephemeral port (`port: 0`) so
 * parallel test files never collide on :8792. Not a *.test.ts file -- a shared helper.
 *
 * Usage: `const srv = await startTestServer(); ...; await srv.handle.close();`
 */
import { startServer, type ServerHandle } from "../../server/src/main.js";
import { loadServerRegistry } from "../../server/src/content-setup.js";
import type { ContentRegistry } from "../../content/schema/registry";

export interface TestServer {
  handle: ServerHandle;
  port: number;
  registry: ContentRegistry;
}

export async function startTestServer(): Promise<TestServer> {
  const registry = loadServerRegistry();
  const handle = await startServer(0, registry);
  return { handle, port: handle.port, registry };
}
