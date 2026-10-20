// tests/client/e2e/window-ping.e2e.ts -- headless-Chromium check of the D10 window + ping readability
// slice (gap items 3, 4, 6), against the REAL playable (scripts/build.mjs bundle, served from this tree
// on an ephemeral 127.0.0.1 port -- never :8791) at 1280x720, playing warden.
//
// 6. A spot ping (G, no target) lands at the crosshair's floor point: tilting the camera (right-drag)
//    between two pings moves the ping, though the pinger has not moved.
// 4. Target pings carry context: pinging a targeted enemy that carries an open Window reads
//    "Window!" (read from ping-wiring.ts's read-only window.__keyrunPings).
// 3. While a cinder bot's Kindled Window is open on an enemy, warden's payoff keys (Guard Break,
//    Reckoning) glow on the bar (data-payoff="ready").
// Not part of `npm test`; run `npm run test:e2e`.
// Optional: KEYRUN_E2E_WP_SHOTS=<dir> saves wp-*-after.png screenshots there.
import { after, before, describe, it } from "node:test";
import assert from "node:assert/strict";
import { spawnSync } from "node:child_process";
import { readFile } from "node:fs/promises";
import { createServer, type Server } from "node:http";
import type { AddressInfo } from "node:net";
import { extname, join, normalize, sep } from "node:path";
import { fileURLToPath } from "node:url";
import { chromium, type Browser, type Page } from "playwright";

const PLAY_QUERY = "/client/index.html?key=5&dungeon=hollow-vault&class=warden";
const REPO_ROOT = normalize(fileURLToPath(new URL("../../../", import.meta.url)));
const SHOTS = process.env.KEYRUN_E2E_WP_SHOTS;
// Optional: KEYRUN_E2E_CPU_THROTTLE=<n> slows the page's main thread n-fold (CDP), to mimic a slow CI runner.
const CPU_THROTTLE = Number(process.env.KEYRUN_E2E_CPU_THROTTLE ?? "1");
// A G press is dropped silently when the actor pinged fewer than PING_RATE_LIMIT_TICKS (20) sim ticks
// earlier (client/src/input/ping.ts derivePingEvent): no label, no signal. pingAndRead therefore re-presses
// G after each per-attempt waitForFunction timeout. The bound is PING_ATTEMPTS, so a ping that never lands
// still fails, after at most PING_ATTEMPTS * 3 s.
const PING_ATTEMPTS = 10;
const MIME: Record<string, string> = {
  ".html": "text/html; charset=utf-8",
  ".js": "text/javascript; charset=utf-8",
  ".json": "application/json; charset=utf-8",
  ".css": "text/css; charset=utf-8",
  ".map": "application/json; charset=utf-8",
};

function serveRepoRoot(): Promise<{ server: Server; origin: string }> {
  const rootPrefix = REPO_ROOT.endsWith(sep) ? REPO_ROOT : REPO_ROOT + sep;
  const server = createServer((req, res) => {
    const pathname = decodeURIComponent(new URL(req.url ?? "/", "http://localhost").pathname);
    const file = normalize(join(REPO_ROOT, pathname));
    if (!file.startsWith(rootPrefix)) {
      res.writeHead(403).end();
      return;
    }
    readFile(file).then(
      (body) => res.writeHead(200, { "content-type": MIME[extname(file)] ?? "application/octet-stream" }).end(body),
      () => res.writeHead(404).end(),
    );
  });
  return new Promise((resolve) => {
    server.listen(0, "127.0.0.1", () => {
      const { port } = server.address() as AddressInfo;
      resolve({ server, origin: `http://127.0.0.1:${port}` });
    });
  });
}

type Shown = { kind: string; targetId: number | null; label: string; x: number; z: number };
const pings = (page: Page): Promise<Shown[]> => page.evaluate(() => window.__keyrunPings!.labels());
const shot = async (page: Page, name: string): Promise<void> => {
  if (SHOTS) await page.screenshot({ path: join(SHOTS, `wp-${name}-after.png`) });
};

// The page state a ping failure is diagnosed from: labels drawn (vs `before`), the sim tick and the input
// sampler's tick (client/src/input-ready.ts; a ping is stamped with the input tick but expires against the
// sim tick), and the input-ready flag.
async function pingDiagnostics(page: Page, before: number): Promise<string> {
  const state = await page.evaluate(() => ({
    pingsLength: window.__keyrunPings?.labels().length ?? null,
    simTick: window.__keyrunInputTicks?.sim ?? null,
    inputTick: window.__keyrunInputTicks?.input ?? null,
    inputReady: window.__keyrunInputReady ?? null,
  }));
  return `[window-ping diagnostics after ${PING_ATTEMPTS} G presses: ${JSON.stringify({ pingsBefore: before, ...state })}]`;
}

// Presses G and waits, on the pings label list, for the ping to be drawn. Each attempt waits up to 3 s for
// the label; a timeout means the press was rate-limited (see PING_ATTEMPTS above), so G is pressed again.
// The last attempt's timeout still fails the test, rethrown with the page's ping/tick/readiness state
// appended (pingDiagnostics) so a CI failure says WHY no label was drawn.
async function pingAndRead(page: Page): Promise<Shown> {
  const before = (await pings(page)).length;
  for (let attempt = 1; ; attempt++) {
    await page.keyboard.press("KeyG");
    try {
      await page.waitForFunction((n) => window.__keyrunPings!.labels().length > n, before, { timeout: 3_000 });
      break;
    } catch (err) {
      if (attempt >= PING_ATTEMPTS) throw new Error(`${(err as Error).message} ${await pingDiagnostics(page, before)}`, { cause: err });
    }
  }
  const all = await pings(page);
  return all[all.length - 1]!;
}

describe("window + ping readability (headless Chromium, real playable)", () => {
  let server: Server | null = null;
  let browser: Browser | null = null;
  let page: Page;

  before(async () => {
    const built = spawnSync(process.execPath, ["scripts/build.mjs"], { cwd: REPO_ROOT, encoding: "utf-8" });
    if (built.status !== 0) throw new Error(`scripts/build.mjs failed: ${built.stderr}`);
    const served = await serveRepoRoot();
    server = served.server;
    browser = await chromium.launch();
    page = await browser.newPage({ viewport: { width: 1280, height: 720 } });
    if (CPU_THROTTLE > 1) await (await page.context().newCDPSession(page)).send("Emulation.setCPUThrottlingRate", { rate: CPU_THROTTLE });
    await page.goto(served.origin + PLAY_QUERY, { waitUntil: "load" });
    await page.waitForFunction(() => document.querySelectorAll(".hud-ability-slot[data-ability-id]").length === 9 && !!window.__keyrunPings, undefined, { timeout: 20_000 });
    await page.mouse.click(640, 360);
    await page.keyboard.press("KeyB"); // dismiss onboarding without casting
    // No G before the input sampler is live and the overlay is gone (client/src/input-ready.ts).
    await page.waitForFunction(() => window.__keyrunInputReady === true, undefined, { timeout: 20_000 });
  });

  after(async () => {
    await browser?.close();
    const s = server;
    if (s) await new Promise<void>((resolve) => s.close(() => resolve()));
  });

  it("a spot ping lands at the crosshair's floor point, and follows the camera tilt", async () => {
    const first = await pingAndRead(page);
    assert.equal(first.kind, "spot");
    assert.equal(first.label, "Stack here");
    await shot(page, "6-spot-ping");
    const pitch0 = await page.evaluate(() => window.__keyrunCamera!.pitch);
    await page.mouse.move(640, 360);
    await page.mouse.down({ button: "right" });
    for (let i = 1; i <= 6; i++) await page.mouse.move(640, 360 - i * 12);
    await page.mouse.up({ button: "right" });
    const pitch1 = await page.evaluate(() => window.__keyrunCamera!.pitch);
    assert.notEqual(pitch1, pitch0, "right-drag did not tilt the camera");
    // The rate-limit gap (one ping per 20 ticks) is absorbed by pingAndRead's bounded re-press, not by a sleep.
    const second = await pingAndRead(page);
    assert.equal(second.kind, "spot");
    await shot(page, "6-spot-ping-tilted");
    const moved = Math.hypot(second.x - first.x, second.z - first.z);
    assert.ok(moved > 0.5, `spot ping did not follow the crosshair: ${JSON.stringify([first, second])}`);
  });

  it("warden's payoff keys glow while a cinder bot's Kindled Window is open on an enemy", async () => {
    // Walk W until the payoff keys light, not for a fixed 600 ms: the glow is the state this test needs.
    // Read the lit ids in the same DOM pass that detects them: the HUD re-renders each frame, so a
    // separate read after the wait can race a transient clear and see [] (CI flake, PR #111 e2e).
    await page.keyboard.down("KeyW");
    const litHandle = await page
      .waitForFunction(
        () => {
          const ids = Array.from(document.querySelectorAll('.hud-ability-slot[data-payoff="ready"]')).map((e) => (e as HTMLElement).dataset.abilityId);
          return ids.length > 0 ? ids : false;
        },
        undefined,
        { timeout: 20_000 },
      )
      .finally(() => page.keyboard.up("KeyW")); // W is released even when the glow never appears
    const lit = (await litHandle.jsonValue()) as string[];
    assert.ok(lit.length > 0 && lit.every((id) => id === "warden.guardBreak" || id === "warden.reckoning"), JSON.stringify(lit));
    await shot(page, "3-window-payoff");
  });

  // "Kick!" is proven in vitest (tests/client/hud-wiring-ping-window.test.ts): the Cantor's heal is
  // the pull's only kickable cast and the bots stop it within 3-8 ticks, too narrow to hit reliably
  // with wall-clock key presses here. "Window!" is deterministic: the cinder bots keep Kindled up.
  it("a target ping on an enemy carrying an open Window reads Window! (was always Focus)", async () => {
    await page.keyboard.press("Tab");
    const labels: string[] = [];
    for (let i = 0; i < 12 && !labels.includes("Window!"); i++) {
      const p = await pingAndRead(page);
      if (p.kind === "target") labels.push(p.label);
      if (p.label === "Window!") await shot(page, "4-window-ping");
      else await page.keyboard.press("Tab");
    }
    assert.ok(labels.includes("Window!"), `no Window! ping in ${JSON.stringify(labels)}`);
    assert.ok(labels.every((l) => l === "Window!" || l === "Focus" || l === "Kick!"), JSON.stringify(labels));
  });
});
