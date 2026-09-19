// scripts/measure-tick-rate.mjs — research/11-tick-rate-measurement.md (backlog item
// tick-rate-measure, D9). Drives the REAL playable in a real headless Chromium via Playwright at
// two tick rates (?hz=20 default arm, ?hz=30 the client-side override client/src/sim-host/worker.ts
// now accepts -- see boot-params.ts's parseHz), same seed (SimHost's SEED constant is fixed),
// same scripted human input, for >=60s of measured playtime each. Pulls window.__keyrunMetrics
// (client/src/sim-host/metrics.ts) after each arm, screenshots the ?metrics=1 HUD, and writes both
// arms' summaries as JSON for research/11 to cite numbers from.
//
// Usage: node scripts/build.mjs --watch --serve (separately, or let this script spawn it) then
//   node scripts/measure-tick-rate.mjs [--duration=65] [--out=research/tick-rate-results.json]
// Spawns its own dev server unless one is already answering on port 8791.
import { chromium } from "playwright";
import { spawn } from "node:child_process";
import { mkdirSync, writeFileSync } from "node:fs";
import { setTimeout as sleep } from "node:timers/promises";

const PORT = 8791;
const BASE = `http://127.0.0.1:${PORT}/client/index.html`;
const WARMUP_MS = 5000; // let boot/registry-fetch/first pull settle before the measured window
const DEFAULT_DURATION_MS = 65_000;
const ARMS = [20, 30];

function argValue(name, fallback) {
  const hit = process.argv.find((a) => a.startsWith(`--${name}=`));
  return hit ? hit.slice(name.length + 3) : fallback;
}

async function serverIsUp() {
  try {
    const res = await fetch(BASE);
    return res.ok || res.status === 404; // any HTTP response means the port is answering
  } catch {
    return false;
  }
}

async function ensureServer() {
  if (await serverIsUp()) return { proc: null };
  console.log("[measure] starting dev server: node scripts/build.mjs --watch --serve");
  const proc = spawn(process.execPath, ["scripts/build.mjs", "--watch", "--serve"], {
    stdio: "inherit",
    cwd: new URL("..", import.meta.url),
  });
  for (let i = 0; i < 60; i += 1) {
    if (await serverIsUp()) return { proc };
    await sleep(500);
  }
  throw new Error("dev server did not come up on port 8791 within 30s");
}

/** Scripted human input, deterministic in structure (not seeded RNG -- a fixed choreography) so
 * both arms see the same command shape. Movement is WASD chords; abilities cycle 1..5, Q, E, R, F. */
async function scriptedInput(page, durationMs) {
  const abilityKeys = ["Digit1", "Digit2", "Digit3", "Digit4", "Digit5", "KeyQ", "KeyE", "KeyR", "KeyF"];
  const moveKeys = ["KeyW", "KeyA", "KeyS", "KeyD"];
  const deadline = Date.now() + durationMs;
  let abilityIdx = 0;
  let moveIdx = 0;
  while (Date.now() < deadline) {
    const move = moveKeys[moveIdx % moveKeys.length];
    moveIdx += 1;
    await page.keyboard.down(move);
    await page.keyboard.press(abilityKeys[abilityIdx % abilityKeys.length]);
    abilityIdx += 1;
    await sleep(450);
    await page.keyboard.up(move);
    await sleep(120);
  }
  for (const k of moveKeys) await page.keyboard.up(k).catch(() => {});
}

async function runArm(browser, hz, durationMs, outDir) {
  console.log(`[measure] arm hz=${hz}: booting playable`);
  const page = await browser.newPage({ viewport: { width: 1280, height: 800 } });
  const url = `${BASE}?key=5&dungeon=hollow-vault&class=warden&hz=${hz}&metrics=1`;
  await page.goto(url, { waitUntil: "load" });
  await page.waitForFunction(() => typeof window.__keyrunMetrics !== "undefined", { timeout: 15_000 });
  await page.click("body").catch(() => {}); // focus the page so keydown events land
  await sleep(WARMUP_MS);
  await page.evaluate(() => window.__keyrunMetrics.reset()); // clean measured window, keep resolved hz
  console.log(`[measure] arm hz=${hz}: running scripted input for ${durationMs / 1000}s`);
  await scriptedInput(page, durationMs);
  await sleep(500); // let the last few ticks/frames land before sampling
  const summary = await page.evaluate(() => window.__keyrunMetrics.summary());
  const shotPath = `${outDir}/hz-${hz}-hud.png`;
  await page.screenshot({ path: shotPath });
  console.log(`[measure] arm hz=${hz}: cpuMs/simSecond=${summary.cpuMsPerSimSecond.toFixed(2)} bytes/s=${summary.snapshotBytesPerSecond.toFixed(0)}`);
  await page.close();
  return { hz, summary, screenshot: shotPath };
}

async function main() {
  const durationMs = Number(argValue("duration", DEFAULT_DURATION_MS / 1000)) * 1000;
  const outPath = argValue("out", "research/tick-rate-results.json");
  const imgDir = "research/img";
  mkdirSync(imgDir, { recursive: true });

  const { proc } = await ensureServer();
  const browser = await chromium.launch();
  const results = [];
  try {
    for (const hz of ARMS) {
      results.push(await runArm(browser, hz, durationMs, imgDir));
    }
  } finally {
    await browser.close();
    if (proc) proc.kill();
  }

  writeFileSync(outPath, JSON.stringify({ ranAtMs: Date.now(), durationMs, arms: results }, null, 2));
  console.log(`[measure] wrote ${outPath}`);
}

main().catch((err) => {
  console.error("[measure] failed:", err);
  process.exit(1);
});
