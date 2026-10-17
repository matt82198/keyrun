// tests/client/e2e/status-net.e2e.ts -- headless-Chromium check of items 5 / 12 / 13 and the
// deferred-from-#98 ?net=1 parity, on the REAL playable (scripts/build.mjs bundle served from this tree
// on an ephemeral 127.0.0.1 port -- never :8791) and the REAL server (startNetServer below, OS-assigned
// port -- never :8792). Reads client/src/readability.ts's read-only window.__keyrunReadability.
//  - local: Brace (E) puts a BRACED chip + tag over YOU; Space draws a dash trail; an idle warden at
//    ?hz=120 eventually dies and stays on the floor as a grey DOWNED tag,
//  - ?net=1: enemy name tags use the server's display names ("Choir Acolyte", not the humanized
//    "Acolyte"); Strike with nothing targeted hits an enemy (a "target" projection + its name label);
//    pressing Strike again on GCD shows "Strike: Not ready yet"; Brace shows BRACED over the network.
// Not part of `npm test`; run `npm run test:e2e`. Optional: KEYRUN_E2E_SHOT_DIR=<dir> saves sn-*.png.
import { after, before, describe, it } from "node:test";
import assert from "node:assert/strict";
import { spawnSync } from "node:child_process";
import { cp, mkdir, readFile } from "node:fs/promises";
import { createServer, type Server } from "node:http";
import type { AddressInfo } from "node:net";
import { extname, join, normalize, sep } from "node:path";
import { fileURLToPath, pathToFileURL } from "node:url";
import { setTimeout as sleep } from "node:timers/promises";
import { chromium, type Browser, type Page } from "playwright";
import { build } from "esbuild";

/** The REAL server (server/src/main.ts startServer) on port 0, bundled the way server/src/run.mjs
 * runs `npm run server` (Node's TS stripping does not resolve the repo's ".js" specifiers), with
 * content/data mirrored next to the bundle for content/index.ts's import.meta.url lookup. */
async function startNetServer(repoRoot: string): Promise<{ port: number; close(): Promise<void> }> {
  const outDir = join(repoRoot, "dist", "e2e-net-server");
  const outfile = join(outDir, "entry.mjs");
  await mkdir(outDir, { recursive: true });
  const contents = 'export { startServer } from "./server/src/main.ts"; export { loadServerRegistry } from "./server/src/content-setup.ts";';
  await build({ stdin: { contents, resolveDir: repoRoot, loader: "ts" }, outfile, bundle: true, platform: "node", format: "esm", target: "es2022", external: ["ws"], logLevel: "warning" });
  await cp(join(repoRoot, "content", "data"), join(outDir, "data"), { recursive: true });
  const mod = (await import(pathToFileURL(outfile).href)) as { startServer(p: number, r: unknown): Promise<{ port: number; close(): Promise<void> }>; loadServerRegistry(): unknown };
  return mod.startServer(0, mod.loadServerRegistry());
}

const REPO_ROOT = normalize(fileURLToPath(new URL("../../../", import.meta.url)));
const LOCAL = "/client/index.html?key=5&dungeon=hollow-vault&class=warden";
const MIME: Record<string, string> = { ".html": "text/html; charset=utf-8", ".js": "text/javascript; charset=utf-8", ".json": "application/json; charset=utf-8", ".css": "text/css; charset=utf-8", ".map": "application/json; charset=utf-8" };
const SHOTS = process.env.KEYRUN_E2E_SHOT_DIR;

function serveRepoRoot(): Promise<{ server: Server; origin: string }> {
  const rootPrefix = REPO_ROOT.endsWith(sep) ? REPO_ROOT : REPO_ROOT + sep;
  const server = createServer((req, res) => {
    const file = normalize(join(REPO_ROOT, decodeURIComponent(new URL(req.url ?? "/", "http://localhost").pathname)));
    if (!file.startsWith(rootPrefix)) return void res.writeHead(403).end();
    readFile(file).then(
      (body) => res.writeHead(200, { "content-type": MIME[extname(file)] ?? "application/octet-stream" }).end(body),
      () => res.writeHead(404).end(),
    );
  });
  return new Promise((resolve) => server.listen(0, "127.0.0.1", () => resolve({ server, origin: `http://127.0.0.1:${(server.address() as AddressInfo).port}` })));
}

async function open(browser: Browser, url: string): Promise<Page> {
  const page = await browser.newPage({ viewport: { width: 1280, height: 720 } });
  await page.goto(url, { waitUntil: "load" });
  await page.waitForFunction(() => document.querySelectorAll(".hud-ability-slot[data-ability-id]").length === 9 && !!window.__keyrunReadability, undefined, { timeout: 30_000 });
  await page.mouse.click(640, 360);
  await page.keyboard.press("KeyB"); // dismiss onboarding without casting
  return page;
}

const shot = async (page: Page, name: string) => (SHOTS ? page.screenshot({ path: join(SHOTS, `sn-${name}.png`) }) : undefined);

describe("status chips, Downed, dash trail, ?net=1 parity (headless Chromium)", () => {
  let web: Server | null = null;
  let net: { port: number; close(): Promise<void> } | null = null;
  let browser: Browser | null = null;
  let origin = "";

  before(async () => {
    const built = spawnSync(process.execPath, ["scripts/build.mjs"], { cwd: REPO_ROOT, encoding: "utf-8" });
    if (built.status !== 0) throw new Error(`scripts/build.mjs failed: ${built.stderr}`);
    const served = await serveRepoRoot();
    web = served.server;
    origin = served.origin;
    net = await startNetServer(REPO_ROOT);
    browser = await chromium.launch();
  });

  after(async () => {
    await browser?.close();
    await net?.close();
    const s = web;
    if (s) await new Promise<void>((resolve) => s.close(() => resolve()));
  });

  it("local: Brace (E) shows a BRACED chip and tag over YOU; Space leaves a dash trail", async () => {
    const page = await open(browser!, origin + LOCAL);
    await page.keyboard.press("KeyE");
    await page.waitForFunction(() => window.__keyrunReadability!.status(1).tag === "BRACED", undefined, { timeout: 5_000 });
    const s = await page.evaluate(() => window.__keyrunReadability!.status(1));
    assert.ok(s.chips.some((c) => c.startsWith("BRACED ")), JSON.stringify(s));
    await shot(page, "local-braced");
    await page.keyboard.press("Space");
    await page.waitForFunction(() => window.__keyrunReadability!.projections().includes("dash"), undefined, { timeout: 3_000 });
    await shot(page, "local-dash");
    await page.close();
  });

  it("local: a dead party member stays on the floor as a DOWNED tag", async () => {
    const page = await open(browser!, origin + LOCAL + "&hz=120");
    await page.waitForFunction(() => Object.values(window.__keyrunReadability!.nameTags()).includes("DOWNED"), undefined, { timeout: 120_000, polling: 250 });
    await shot(page, "local-downed");
    await page.close();
  });

  it("?net=1: server display names, a self-target-free Strike, cast_failed, and BRACED over the network", async () => {
    const page = await open(browser!, `${origin}${LOCAL}&net=1&server=ws://127.0.0.1:${net!.port}`);
    const tags = Object.values(await page.evaluate(() => window.__keyrunReadability!.nameTags()));
    assert.ok(tags.includes("Choir Acolyte") && tags.includes("YOU: Warden"), JSON.stringify(tags));
    await page.keyboard.press("Digit1");
    await page.waitForFunction(() => window.__keyrunReadability!.selfProjectionHistory().includes("target"), undefined, { timeout: 5_000 });
    assert.ok((await page.evaluate(() => window.__keyrunReadability!.castLabels())) > 0);
    await shot(page, "net-strike");
    await page.keyboard.press("Digit1");
    await page.waitForFunction(() => document.querySelector(".hud-cast-error")?.textContent === "Strike: Not ready yet", undefined, { timeout: 5_000 });
    await sleep(1600); // GCD over
    await page.keyboard.press("KeyE");
    await page.waitForFunction(() => window.__keyrunReadability!.status(1).tag === "BRACED", undefined, { timeout: 5_000 });
    await shot(page, "net-braced");
    await page.close();
  });
});
