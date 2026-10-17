// tests/client/e2e/first-run-readability.e2e.ts -- headless-Chromium check of the D10 first-run
// readability pass (Matt's live play: "i cant even tell whats going on", "no abilities, ability
// projection, everything is a dumby").
//
// Drives the REAL playable (scripts/build.mjs bundle, served from this tree on an ephemeral 127.0.0.1
// port -- never :8791) at 1280x720 and asserts observable browser state: the onboarding overlay is up
// at boot and gone after the first key; the ability bar names all nine warden abilities with key
// badges; the objective line says what to kill; every actor wears a name tag (YOU / bots / enemies);
// pressing 3 (Bulwark) puts YOUR circle projection on the ground and starts its cooldown sweep.
// Reads client/src/readability.ts's read-only window.__keyrunReadability. Same harness shape as
// run-readout.e2e.ts. Not part of `npm test`; run `npm run test:e2e`.
// Optional: KEYRUN_E2E_READABILITY_SCREENSHOT=<png path> saves a screenshot after the cast.
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
const WARDEN_NAMES = ["Strike", "Guard Break", "Bulwark", "Reckoning", "Vigil", "Shield Bash", "Brace", "Taunt", "Charge"];
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

describe("first-run readability (headless Chromium, real playable)", () => {
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
    await page.goto(served.origin + PLAY_QUERY, { waitUntil: "load" });
    await page.waitForFunction(
      () => document.querySelectorAll(".hud-ability-slot[data-ability-id]").length === 9 && !!window.__keyrunReadability,
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

  it("shows the onboarding overlay at run start, naming the controls", async () => {
    const overlay = page.locator(".hud-onboarding");
    assert.equal(await overlay.isVisible(), true);
    const text = (await overlay.textContent()) ?? "";
    for (const needle of ["W A S D", "Right-drag", "1 2 3 4 5", "KICK", "Tab", "Space", "Warden"]) assert.ok(text.includes(needle), `overlay lacks ${needle}: ${text}`);
  });

  it("the ability bar names all nine warden abilities, with key badges, all ready", async () => {
    const slots = await page.locator(".hud-ability-slot").evaluateAll((els) =>
      els.map((e) => ({
        key: e.querySelector(".hud-ability-key")?.textContent ?? "",
        name: e.querySelector(".hud-ability-name")?.textContent ?? "",
        state: (e as HTMLElement).dataset.state,
        visible: (e as HTMLElement).getBoundingClientRect().width > 40,
      })),
    );
    assert.deepEqual(slots.map((s) => s.name), WARDEN_NAMES);
    assert.deepEqual(slots.map((s) => s.key), ["1", "2", "3", "4", "5", "Q", "E", "R", "F"]);
    assert.ok(slots.every((s) => s.visible && s.state === "ready"), JSON.stringify(slots));
    assert.equal(await page.locator(".hud-key-legend").isVisible(), true);
  });

  it("the objective line says what to kill on pull 1", async () => {
    const text = await page.locator(".hud-objective").textContent();
    assert.match(text ?? "", /^Clear pull 1\/\d+: 2x Choir Acolyte, Choir Cantor$/);
  });

  it("every actor wears a name tag: YOU, role-lettered bots, named enemies", async () => {
    const tags = Object.values(await page.evaluate(() => window.__keyrunReadability!.nameTags()));
    assert.ok(tags.includes("YOU: Warden"), JSON.stringify(tags));
    assert.ok(tags.includes("[H] Chanter") && tags.includes("[D] Cinder"), JSON.stringify(tags));
    assert.ok(tags.includes("Choir Acolyte") && tags.includes("Choir Cantor"), JSON.stringify(tags));
  });

  it("the first key press dismisses the overlay for good", async () => {
    await page.mouse.click(640, 360);
    await page.keyboard.press("KeyB"); // unbound: dismisses without casting
    assert.equal(await page.locator(".hud-onboarding").isVisible(), false);
    assert.equal(await page.evaluate(() => window.__keyrunReadability!.onboardingDismissed()), true);
  });

  it("pressing 3 (Bulwark) draws YOUR circle projection and sweeps the 3 slot's cooldown", async () => {
    await page.keyboard.press("Digit3");
    await page.waitForFunction(() => window.__keyrunReadability!.selfProjectionHistory().includes("circle"), undefined, { timeout: 5_000 });
    if (process.env.KEYRUN_E2E_READABILITY_SCREENSHOT) await page.screenshot({ path: process.env.KEYRUN_E2E_READABILITY_SCREENSHOT });
    const slot3 = page.locator('.hud-ability-slot[data-action="ability3"]');
    assert.equal(await slot3.getAttribute("data-state"), "cooldown");
    const sweep = await slot3.locator(".hud-ability-sweep").evaluate((e) => parseFloat((e as HTMLElement).style.height));
    assert.ok(sweep > 50, `cooldown sweep ${sweep}%`);
  });
});
