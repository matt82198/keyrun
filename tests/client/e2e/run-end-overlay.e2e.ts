// tests/client/e2e/run-end-overlay.e2e.ts -- headless-Chromium regression for the run-end overlay.
//
// Defect: client/index.html's `.run-end-root { display: flex }` author rule beat the UA stylesheet's
// `[hidden] { display: none }`, so the overlay RunEndView (client/src/hud/run-end.ts) constructs with
// `hidden = true` still covered the whole viewport for the entire run: the scene rendered dimmed and
// the canvas never received mousedown/wheel/contextmenu, so mouse-look and zoom were dead.
//
// This drives the REAL playable -- the same esbuild bundle `npm run dev` serves, built by
// scripts/build.mjs into dist/ and served from the repo root exactly as its servedir "." does -- and
// asserts on observable browser behaviour: hit-testing, computed style, and the camera actually moving
// under a wheel / right-drag (read through boot.ts's read-only `window.__keyrunCamera`). Then it ends
// the run through the public abandon key (Backspace, client/src/sim-host/host-commands.ts
// ABANDON_KEY_CODE) and asserts the overlay DOES show, and hides again once a card is chosen.
//
// Not part of `npm test` (vitest's include is tests/**/*.test.ts and CI installs no browser).
// Run: `npm run test:e2e` (node --test; Node 24 strips the types). It builds, then serves this tree on
// an ephemeral 127.0.0.1 port of its own -- never :8791, which another worktree's `npm run dev` may
// hold (reusing it would silently test a different tree) -- and closes it when done.
// Optional: KEYRUN_E2E_SCREENSHOT=<png path> saves a mid-run screenshot before the run is ended.
import { after, before, describe, it } from "node:test";
import assert from "node:assert/strict";
import { spawnSync } from "node:child_process";
import { readFile } from "node:fs/promises";
import { createServer, type Server } from "node:http";
import type { AddressInfo } from "node:net";
import { extname, join, normalize, sep } from "node:path";
import { fileURLToPath } from "node:url";
import { setTimeout as sleep } from "node:timers/promises";
import { chromium, type Browser, type Page } from "playwright";

const PLAY_QUERY = "/client/index.html?key=5&dungeon=hollow-vault&class=warden";
const REPO_ROOT = normalize(fileURLToPath(new URL("../../../", import.meta.url)));
const VIEWPORT = { width: 1280, height: 800 };
const CENTRE = { x: VIEWPORT.width / 2, y: VIEWPORT.height / 2 };

const MIME: Record<string, string> = {
  ".html": "text/html; charset=utf-8",
  ".js": "text/javascript; charset=utf-8",
  ".json": "application/json; charset=utf-8",
  ".css": "text/css; charset=utf-8",
  ".map": "application/json; charset=utf-8",
};

/** Static file server over the repo root (scripts/build.mjs's servedir "."), loopback only. */
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

/** What sits at the viewport centre: its class list and whether it is (inside) the overlay. */
async function centreHit(page: Page): Promise<{ tag: string; className: string; inOverlay: boolean }> {
  return page.evaluate(({ x, y }) => {
    const el = document.elementFromPoint(x, y);
    return {
      tag: el?.tagName ?? "null",
      className: el instanceof Element ? String(el.className) : "",
      inOverlay: el instanceof Element && el.closest(".run-end-root") !== null,
    };
  }, CENTRE);
}

async function overlayState(page: Page): Promise<{ hidden: boolean; display: string }> {
  return page.evaluate(() => {
    const el = document.querySelector<HTMLElement>(".run-end-root");
    if (!el) throw new Error(".run-end-root not mounted");
    return { hidden: el.hidden, display: getComputedStyle(el).display };
  });
}

async function camera(page: Page): Promise<{ yaw: number; pitch: number; distance: number }> {
  return page.evaluate(() => {
    const cam = window.__keyrunCamera;
    if (!cam) throw new Error("window.__keyrunCamera missing (client/src/boot.ts)");
    return { yaw: cam.yaw, pitch: cam.pitch, distance: cam.distance };
  });
}

describe("run-end overlay (headless Chromium, real playable)", () => {
  let server: Server | null = null;
  let browser: Browser | null = null;
  let page: Page;

  before(async () => {
    const built = spawnSync(process.execPath, ["scripts/build.mjs"], { cwd: REPO_ROOT, encoding: "utf-8" });
    if (built.status !== 0) throw new Error(`scripts/build.mjs failed: ${built.stderr}`);
    const served = await serveRepoRoot();
    server = served.server;
    browser = await chromium.launch();
    page = await browser.newPage({ viewport: VIEWPORT });
    await page.goto(served.origin + PLAY_QUERY, { waitUntil: "load" });
    // First sim tick: the metrics singleton records a tick interval/step per snapshot that lands.
    await page.waitForFunction(
      () => {
        const m = window.__keyrunMetrics;
        return m !== undefined && (m.tickIntervalMs.count() > 0 || m.simStepMs.count() > 0);
      },
      undefined,
      { timeout: 20_000 },
    );
    await sleep(500); // a few render frames so the camera has its first update
  });

  after(async () => {
    await browser?.close();
    const s = server;
    if (s) await new Promise<void>((resolve) => s.close(() => resolve()));
  });

  it("the canvas, not the run-end overlay, is hit at the viewport centre while the run is live", async () => {
    const hit = await centreHit(page);
    assert.equal(hit.inOverlay, false, `centre hit is inside .run-end-root: ${JSON.stringify(hit)}`);
    assert.equal(hit.tag, "CANVAS", `centre hit is not the canvas: ${JSON.stringify(hit)}`);
    assert.match(hit.className, /\bscene-canvas\b/);
  });

  it("the hidden run-end overlay computes display:none while the run is live", async () => {
    const state = await overlayState(page);
    assert.equal(state.hidden, true);
    assert.equal(state.display, "none");
  });

  it("a wheel over the centre zooms the camera and a right-drag turns it", async () => {
    if (process.env.KEYRUN_E2E_SCREENSHOT) await page.screenshot({ path: process.env.KEYRUN_E2E_SCREENSHOT });
    const start = await camera(page);
    await page.mouse.move(CENTRE.x, CENTRE.y);
    await page.mouse.wheel(0, 400);
    await sleep(800); // zoom eases toward its target over zoomHalfLifeS
    const zoomed = await camera(page);
    assert.ok(Math.abs(zoomed.distance - start.distance) > 0.1, `distance did not change: ${start.distance} -> ${zoomed.distance}`);

    await page.mouse.down({ button: "right" });
    await page.mouse.move(CENTRE.x + 200, CENTRE.y, { steps: 10 });
    await page.mouse.up({ button: "right" });
    const turned = await camera(page);
    assert.notEqual(turned.yaw, zoomed.yaw, `yaw did not change under right-drag: ${zoomed.yaw}`);
  });

  it("ending the run (abandon key) shows the overlay over the scene, and choosing a card hides it", async () => {
    await page.keyboard.press("Backspace");
    await page.waitForFunction(() => document.querySelector<HTMLElement>(".run-end-root")?.hidden === false, undefined, {
      timeout: 10_000,
    });
    const shown = await overlayState(page);
    assert.equal(shown.display, "flex");
    assert.equal((await centreHit(page)).inOverlay, true, "run-end overlay is not on top at the centre once shown");
    assert.equal(await page.locator(".run-end-card").count(), 3);

    await page.locator(".run-end-card").first().click();
    const dismissed = await overlayState(page);
    assert.equal(dismissed.hidden, true);
    assert.equal(dismissed.display, "none");
  });
});
