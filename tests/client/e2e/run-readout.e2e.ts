// tests/client/e2e/run-readout.e2e.ts -- headless-Chromium check of render/hud.ts's run readout (D10).
//
// Drives the REAL playable (scripts/build.mjs bundle, served from this tree on an ephemeral 127.0.0.1
// port -- never :8791) at 1280x800 and asserts observable browser state: the `.hud-run-readout`
// element exists, reads "Pull 1/<n>" (1-based) at run start with one party marker per party member, sits fully
// on-screen without intersecting the ability bar or the run timer, and never takes a pointer event.
// Same harness shape as run-end-overlay.e2e.ts. Not part of `npm test`; run `npm run test:e2e`.
// Optional: KEYRUN_E2E_READOUT_SCREENSHOT=<png path> saves a screenshot of the live run.
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

interface Rect {
  left: number;
  top: number;
  right: number;
  bottom: number;
}

function intersects(a: Rect, b: Rect): boolean {
  return a.left < b.right && b.left < a.right && a.top < b.bottom && b.top < a.bottom;
}

async function rectOf(page: Page, selector: string): Promise<Rect> {
  return page.evaluate((sel) => {
    const el = document.querySelector(sel);
    if (!el) throw new Error(`${sel} not mounted`);
    const r = el.getBoundingClientRect();
    return { left: r.left, top: r.top, right: r.right, bottom: r.bottom };
  }, selector);
}

describe("HUD run readout (headless Chromium, real playable)", () => {
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
    await page.waitForFunction(
      () => {
        const m = window.__keyrunMetrics;
        return m !== undefined && (m.tickIntervalMs.count() > 0 || m.simStepMs.count() > 0);
      },
      undefined,
      { timeout: 20_000 },
    );
    await sleep(300);
  });

  after(async () => {
    await browser?.close();
    const s = server;
    if (s) await new Promise<void>((resolve) => s.close(() => resolve()));
  });

  it("shows 'Pull 1/<n>' and one live marker per party member at run start", async () => {
    if (process.env.KEYRUN_E2E_READOUT_SCREENSHOT) await page.screenshot({ path: process.env.KEYRUN_E2E_READOUT_SCREENSHOT });
    const text = await page.locator(".hud-run-readout .hud-pull").textContent();
    assert.match(text ?? "", /^Pull 1\/[1-9]\d*$/, `pull readout text: ${JSON.stringify(text)}`);
    const alive = await page.locator(".hud-run-readout .hud-party-marker").evaluateAll((els) =>
      els.map((e) => (e as HTMLElement).dataset.alive),
    );
    assert.deepEqual(alive, ["true", "true", "true", "true", "true"]);
  });

  it("sits on-screen and does not intersect the ability bar or the run timer at 1280x800", async () => {
    const readout = await rectOf(page, ".hud-run-readout");
    const bar = await rectOf(page, ".hud-ability-bar");
    const timer = await rectOf(page, ".hud-timer");
    assert.ok(readout.right > readout.left && readout.bottom > readout.top, `readout has no area: ${JSON.stringify(readout)}`);
    assert.ok(readout.left >= 0 && readout.top >= 0 && readout.right <= VIEWPORT.width && readout.bottom <= VIEWPORT.height);
    assert.equal(intersects(readout, bar), false, `readout ${JSON.stringify(readout)} overlaps ability bar ${JSON.stringify(bar)}`);
    assert.equal(intersects(readout, timer), false, `readout ${JSON.stringify(readout)} overlaps timer ${JSON.stringify(timer)}`);
  });

  it("never takes a pointer event: computed pointer-events none, the canvas is hit beneath it", async () => {
    const hit = await page.evaluate(() => {
      const el = document.querySelector(".hud-run-readout");
      if (!el) throw new Error(".hud-run-readout not mounted");
      const r = el.getBoundingClientRect();
      const under = document.elementFromPoint(r.left + r.width / 2, r.top + r.height / 2);
      return { pointerEvents: getComputedStyle(el).pointerEvents, tag: under?.tagName ?? "null" };
    });
    assert.equal(hit.pointerEvents, "none");
    assert.equal(hit.tag, "CANVAS");
  });
});
