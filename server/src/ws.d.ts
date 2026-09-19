/**
 * server/src/ws.d.ts
 *
 * Minimal ambient types for the `ws` package -- it ships no TypeScript declarations of its own
 * (confirmed: `node_modules/ws/package.json` has no `types`/`typings` field) and this lane's
 * package.json edit is restricted to "add a ws dependency (pin exact) and a server script --
 * nothing else in it" (the dispatch brief), so `@types/ws` cannot be added as a second
 * devDependency. Covers only the surface server/src and tests/server actually call.
 */
declare module "ws" {
  export interface ServerOptions {
    port?: number;
    host?: string;
  }

  export type Data = string | Buffer | ArrayBuffer | Buffer[];

  export class WebSocket {
    constructor(address: string);
    readonly readyState: number;
    static readonly OPEN: number;
    send(data: string): void;
    close(): void;
    terminate(): void;
    on(event: "open", listener: () => void): this;
    on(event: "message", listener: (data: Buffer, isBinary: boolean) => void): this;
    on(event: "close", listener: (code: number, reason: Buffer) => void): this;
    on(event: "error", listener: (err: Error) => void): this;
    once(event: "open", listener: () => void): this;
    once(event: "error", listener: (err: Error) => void): this;
  }

  export interface AddressInfo {
    address: string;
    family: string;
    port: number;
  }

  export class WebSocketServer {
    constructor(options: ServerOptions, callback?: () => void);
    address(): AddressInfo | string | null;
    close(callback?: (err?: Error) => void): void;
    on(event: "connection", listener: (socket: WebSocket) => void): this;
  }
}
