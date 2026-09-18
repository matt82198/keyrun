/**
 * Usage: import { renderQueueBand } from "./panels/queue.js";
 *        root.appendChild(renderQueueBand(store, onChange));
 *
 * The Run band's bots-first queue simulation: role-scarcity readout, a simulated readiness
 * countdown, and -- since there is no server yet (design/BIBLE.md section 11 puts networking out
 * of scope) -- resolving the run right here via progression.ts's completeRun on "Ready!", which
 * banks Ash and rolls the end-of-run choice-of-three for panels/reward.ts to render.
 */
import { completeRun, saveStore, type Character, type RunResult, type StoreV3 } from "../store.js";

const ROLE_SLOTS: ReadonlyArray<{ role: string; count: number }> = [
  { role: "Tank", count: 1 },
  { role: "Healer", count: 1 },
  { role: "DPS", count: 3 },
];
const READY_SECONDS = 5;
// TUNE: POC par time a bots-first run is measured against, before per-key/result jitter.
const BASE_RUN_DURATION_SEC = 180;

let countdownHandle: ReturnType<typeof setInterval> | undefined;

export function renderQueueBand(store: StoreV3, onChange: () => void): HTMLElement {
  const character = store.character;
  const wrap = document.createElement("div");
  wrap.className = "queue-band";

  const roles = document.createElement("ul");
  roles.className = "role-slots";
  for (const slot of ROLE_SLOTS) {
    const row = document.createElement("li");
    row.textContent = `${slot.role}: ${character.queued ? slot.count : 0}/${slot.count}`;
    roles.appendChild(row);
  }
  wrap.appendChild(roles);

  const countdown = document.createElement("span");
  countdown.className = "ready-countdown";
  wrap.appendChild(countdown);

  const toggle = document.createElement("button");
  toggle.type = "button";
  toggle.className = "queue-toggle";
  toggle.textContent = character.queued ? "Cancel Queue" : "Queue (bots fill instantly)";
  toggle.addEventListener("click", () => onQueueToggle(store, countdown, onChange));
  wrap.appendChild(toggle);
  return wrap;
}

function onQueueToggle(store: StoreV3, countdown: HTMLElement, onChange: () => void): void {
  const character = store.character;
  if (countdownHandle) {
    clearInterval(countdownHandle);
    countdownHandle = undefined;
  }
  if (character.queued) {
    character.queued = false;
    saveStore(store);
    onChange();
    return;
  }
  character.queued = true;
  saveStore(store);
  onChange();
  let remaining = READY_SECONDS;
  countdown.textContent = `Ready in ${remaining}s`;
  countdownHandle = setInterval(() => {
    remaining -= 1;
    if (remaining <= 0) {
      if (countdownHandle) clearInterval(countdownHandle);
      countdownHandle = undefined;
      countdown.textContent = "Ready!";
      resolveQueuedRun(store, onChange);
      return;
    }
    countdown.textContent = `Ready in ${remaining}s`;
  }, 1000);
}

/** Simulates the local bots-first run's outcome deterministically from the character + moment, per POC. */
function simulateRunResult(character: Character, now: number): { result: RunResult; durationSec: number } {
  const basis = `${character.classId}:${character.keyLevel}:${character.runHistory.length}:${now}`;
  let hash = 0;
  for (let i = 0; i < basis.length; i += 1) hash = (hash * 31 + basis.charCodeAt(i)) >>> 0;
  const roll = hash % 10;
  // TUNE: bots-first outcome weighting -- mostly timed, occasionally depleted, rarely abandoned.
  const result: RunResult = roll < 7 ? "timed" : roll < 9 ? "depleted" : "abandoned";
  const jitter = hash % 41; // 0-40s of deterministic jitter
  const durationSec = Math.max(30, BASE_RUN_DURATION_SEC - character.keyLevel * 2 + jitter - 20);
  return { result, durationSec };
}

function resolveQueuedRun(store: StoreV3, onChange: () => void): void {
  const character = store.character;
  if (!character.queued) return;
  const now = Date.now();
  const { result, durationSec } = simulateRunResult(character, now);
  const outcome = completeRun(character, { result, keyLevel: character.keyLevel, durationSec }, now);
  store.character = { ...outcome.character, queued: false };
  saveStore(store);
  onChange();
}
