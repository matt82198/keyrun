/**
 * tests/client/sim-host/worker-entry.test.ts
 *
 * The dispatch brief's headless real-Worker test: drives client/src/sim-host/worker.ts -- the
 * ACTUAL Worker entry point esbuild now bundles as its own output (scripts/build.mjs) -- by faking
 * the `self` global (addEventListener/postMessage) instead of a real Worker thread, then dispatches
 * a synthetic "init" message and lets the file's own real setInterval-driven tick loop run for 200
 * ticks. This is deliberately NOT the same test as sim-runtime.test.ts: that one calls
 * SimRuntime.step() directly and needs no faking at all; this one proves worker.ts's own
 * message-handling/scheduling wiring actually works end to end.
 *
 * content-registry.ts's fetchContentRegistry() uses `fetch` (browser/Worker-only) to load
 * content/data/*.json over HTTP -- no dev server is running under Vitest, so it is mocked here to
 * resolve via the real Node loader (content/index.ts's loadContent(), same registry
 * sim-runtime.test.ts uses) instead of hitting the network. Everything else in worker.ts (the
 * scheduler, SimRuntime, boot-params.ts's option resolution) runs completely unmocked.
 */
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { loadContent } from "@content/index";
import type { ReadyMessage, SnapshotMessage, WorkerToMainMessage } from "../../../client/src/sim-host/wire.js";

vi.mock("../../../client/src/sim-host/content-registry.js", () => ({
  fetchContentRegistry: async () => loadContent(),
}));

type MessageListener = (ev: { data: unknown }) => void;

function installFakeSelf(): { posted: WorkerToMainMessage[]; fire: MessageListener } {
  const posted: WorkerToMainMessage[] = [];
  let handler: MessageListener | null = null;
  (globalThis as unknown as { self: unknown }).self = {
    addEventListener: (_type: string, listener: MessageListener) => {
      handler = listener;
    },
    postMessage: (msg: WorkerToMainMessage) => posted.push(msg),
  };
  return {
    posted,
    fire: (ev) => {
      if (!handler) throw new Error("worker.ts never registered a message listener");
      handler(ev);
    },
  };
}

async function waitUntil(pred: () => boolean, timeoutMs = 10_000): Promise<void> {
  const startedAt = Date.now();
  while (!pred()) {
    if (Date.now() - startedAt > timeoutMs) throw new Error("worker-entry test: timed out waiting for condition");
    await new Promise((resolve) => setTimeout(resolve, 5));
  }
}

describe("worker.ts: the real Worker entry, headless (fake self/postMessage)", () => {
  beforeEach(() => {
    vi.resetModules();
  });
  afterEach(() => {
    delete (globalThis as unknown as { self?: unknown }).self;
  });

  it("boots off a fake init message and steps 200 ticks, posting ready then one snapshot per tick", async () => {
    const { posted, fire } = installFakeSelf();
    await import("../../../client/src/sim-host/worker.js");

    // tickMs=1: 200 ticks of real setInterval time is ~200ms, fast enough for a normal test timeout
    // without reaching for fake timers (which fight the mocked fetchContentRegistry's real Promise).
    fire({ data: { type: "init", search: "?key=5&dungeon=hollow-vault&class=warden", tickMs: 1 } });

    await waitUntil(() => posted.some((m) => m.type === "ready"));
    const ready = posted.find((m): m is ReadyMessage => m.type === "ready")!;
    expect(ready.humanActorId).toBeGreaterThanOrEqual(0);
    expect(ready.tickMs).toBe(1);

    await waitUntil(() => posted.filter((m) => m.type === "snapshot").length >= 200, 15_000);
    const snapshots = posted.filter((m): m is SnapshotMessage => m.type === "snapshot");
    expect(snapshots.length).toBeGreaterThanOrEqual(200);
    // Ticks are contiguous and start at 1 (createRun starts state.tick at 0; the first step lands on 1).
    for (let i = 0; i < 200; i++) {
      expect(snapshots[i]!.snapshot.tick).toBe(i + 1);
      expect(snapshots[i]!.simStepMs).toBeGreaterThanOrEqual(0);
    }
  });

  it("applies a queued command before the next step (abandon reaches a terminal run-end)", async () => {
    const { posted, fire } = installFakeSelf();
    await import("../../../client/src/sim-host/worker.js");

    fire({ data: { type: "init", search: "?key=5&dungeon=hollow-vault&class=warden", tickMs: 1 } });
    await waitUntil(() => posted.some((m) => m.type === "ready"));

    // tick 0 is only meaningful when applied before the first step (sim-runtime.test.ts covers
    // that exact case synchronously); here the command crosses a real postMessage-shaped boundary
    // after `ready`, so a few ticks may already have run -- assert the outcome, not a fixed tick.
    fire({ data: { type: "command", command: { type: "abandon", tick: 0 } } });

    await waitUntil(() => posted.some((m) => m.type === "snapshot" && m.snapshot.runEnd !== null && m.snapshot.runEnd !== undefined));
    const ended = posted.find((m): m is SnapshotMessage => m.type === "snapshot" && !!m.snapshot.runEnd)!;
    expect(ended.snapshot.runEnd?.outcome).toBe("abandoned");
    expect(ended.snapshot.runEnd?.tick).toBeGreaterThanOrEqual(0);
  });

  // run-options-wiring: the exact end-to-end path boot.ts really drives -- an InitMessage's
  // `loadout`/`?clause=` reaching sim/index.ts's createRun through worker.ts -> run-setup.ts.
  it("a real, valid loadout on init boots normally (ready + snapshots), same as no loadout", async () => {
    const { posted, fire } = installFakeSelf();
    await import("../../../client/src/sim-host/worker.js");

    fire({
      data: {
        type: "init",
        search: "?key=5&dungeon=hollow-vault&class=warden",
        tickMs: 1,
        loadout: { talentChoices: { "warden.strike": "riposte" }, gear: [] },
      },
    });

    await waitUntil(() => posted.some((m) => m.type === "ready"));
    await waitUntil(() => posted.filter((m) => m.type === "snapshot").length >= 5);
    expect(posted.some((m) => m.type === "error")).toBe(false);
  });

  it("a real ?clause= on init boots normally -- never a crash", async () => {
    const { posted, fire } = installFakeSelf();
    await import("../../../client/src/sim-host/worker.js");
    const { loadContent } = await import("@content/index");
    const [firstClauseId] = [...loadContent().clauses.keys()];

    fire({ data: { type: "init", search: `?key=5&dungeon=hollow-vault&class=warden&clause=${firstClauseId}`, tickMs: 1 } });

    await waitUntil(() => posted.some((m) => m.type === "ready"));
    await waitUntil(() => posted.filter((m) => m.type === "snapshot").length >= 5);
    expect(posted.some((m) => m.type === "error")).toBe(false);
  });

  it("an invariant-breaking loadout posts a clean ErrorMessage -- never a silent base-kit fallback, never a crash", async () => {
    const { posted, fire } = installFakeSelf();
    await import("../../../client/src/sim-host/worker.js");

    fire({
      data: {
        type: "init",
        search: "?key=5&dungeon=hollow-vault&class=warden",
        tickMs: 1,
        loadout: { talentChoices: { "not-a-real-ability": "nope" }, gear: [] },
      },
    });

    await waitUntil(() => posted.some((m) => m.type === "error"));
    const err = posted.find((m): m is { type: "error"; message: string } => m.type === "error")!;
    expect(err.message).toMatch(/no talent fork/);
    expect(posted.some((m) => m.type === "ready")).toBe(false);
  });
});
