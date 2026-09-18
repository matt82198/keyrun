/**
 * Usage: import { renderRunPanel } from "./panels/run.js";
 *        renderRunPanel(container, store, onChange);
 *
 * Renders the run band: a key-level stepper (2-20, locks while queued), a dungeon select (one entry:
 * Hollow Vault), a bots-first queue-state readout with party role slots and a simulated readiness
 * countdown, a Play button that navigates to the client with a derived seed and the chosen key
 * level, the "since last run" delta line, and the last five run results with their picked reward.
 *
 * Bots-first queue simulation: when the countdown reaches "Ready!" the POC resolves the run right
 * here (no server yet -- design/BIBLE.md section 11 puts networking out of scope) via rules.ts's
 * completeRun, which banks Ash and rolls the end-of-run choice-of-three for panels/reward.ts to
 * render. `onChange` is called after every mutation so main.ts can re-render the other bands.
 */
import {
  buildPlayUrl,
  clampKeyLevel,
  completeRun,
  computeRunDelta,
  isKeyLocked,
  saveStore,
  type Character,
  type RunResult,
  type StoreV2,
} from "../store.js";

const DUNGEONS = ["Hollow Vault"];
const ROLE_SLOTS: ReadonlyArray<{ role: string; count: number }> = [
  { role: "Tank", count: 1 },
  { role: "Healer", count: 1 },
  { role: "DPS", count: 3 },
];
const READY_SECONDS = 5;
// TUNE: POC par time a bots-first run is measured against, before per-key/result jitter.
const BASE_RUN_DURATION_SEC = 180;

let countdownHandle: ReturnType<typeof setInterval> | undefined;

export function renderRunPanel(root: HTMLElement, store: StoreV2, onChange: () => void): void {
  root.innerHTML = "";
  root.className = "band band-run";
  const character = store.character;

  const heading = document.createElement("h2");
  heading.textContent = "Run";
  root.appendChild(heading);

  root.appendChild(renderKeyStepper(character, store, onChange));
  root.appendChild(renderDungeonSelect());
  root.appendChild(renderQueueBand(character, store, onChange));
  root.appendChild(renderPlayButton(character));
  root.appendChild(renderDeltaLine(character));
  root.appendChild(renderHistory(character.runHistory));
}

function renderKeyStepper(character: Character, store: StoreV2, onChange: () => void): HTMLElement {
  const wrap = document.createElement("div");
  wrap.className = "key-stepper";

  const label = document.createElement("span");
  label.className = "key-label";
  label.textContent = "Key Level";
  wrap.appendChild(label);

  const locked = isKeyLocked(character);
  const minus = document.createElement("button");
  minus.type = "button";
  minus.textContent = "-";
  minus.className = "key-btn key-minus";
  minus.disabled = locked;

  const value = document.createElement("span");
  value.className = "key-value";
  value.textContent = String(character.keyLevel);

  const plus = document.createElement("button");
  plus.type = "button";
  plus.textContent = "+";
  plus.className = "key-btn key-plus";
  plus.disabled = locked;

  const step = (delta: number) => {
    character.keyLevel = clampKeyLevel(character.keyLevel + delta);
    saveStore(store);
    onChange();
  };
  minus.addEventListener("click", () => step(-1));
  plus.addEventListener("click", () => step(1));

  wrap.appendChild(minus);
  wrap.appendChild(value);
  wrap.appendChild(plus);
  if (locked) {
    const lockNote = document.createElement("span");
    lockNote.className = "key-locked-note";
    lockNote.textContent = "(locked while queued)";
    wrap.appendChild(lockNote);
  }
  return wrap;
}

function renderDungeonSelect(): HTMLElement {
  const wrap = document.createElement("div");
  wrap.className = "dungeon-select";

  const label = document.createElement("label");
  label.textContent = "Dungeon";
  label.htmlFor = "dungeon-select";
  wrap.appendChild(label);

  const select = document.createElement("select");
  select.id = "dungeon-select";
  for (const dungeon of DUNGEONS) {
    const opt = document.createElement("option");
    opt.value = dungeon;
    opt.textContent = dungeon;
    select.appendChild(opt);
  }
  wrap.appendChild(select);
  return wrap;
}

function renderQueueBand(character: Character, store: StoreV2, onChange: () => void): HTMLElement {
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
  toggle.addEventListener("click", () => onQueueToggle(character, store, countdown, onChange));
  wrap.appendChild(toggle);
  return wrap;
}

function onQueueToggle(character: Character, store: StoreV2, countdown: HTMLElement, onChange: () => void): void {
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

function resolveQueuedRun(store: StoreV2, onChange: () => void): void {
  const character = store.character;
  if (!character.queued) return;
  const now = Date.now();
  const { result, durationSec } = simulateRunResult(character, now);
  const outcome = completeRun(character, { result, keyLevel: character.keyLevel, durationSec }, now);
  store.character = { ...outcome.character, queued: false };
  saveStore(store);
  onChange();
}

function renderPlayButton(character: Character): HTMLElement {
  const link = document.createElement("a");
  link.className = "play-button";
  link.textContent = "Play";
  link.href = buildPlayUrl(character, character.keyLevel);
  return link;
}

function renderDeltaLine(character: Character): HTMLElement {
  const wrap = document.createElement("p");
  wrap.className = "run-delta";
  const delta = computeRunDelta(character);
  wrap.textContent = delta ? `Since last run: ${delta.text}` : "Since last run: no runs yet.";
  return wrap;
}

function renderHistory(history: Character["runHistory"]): HTMLElement {
  const wrap = document.createElement("div");
  wrap.className = "run-history";

  const heading = document.createElement("h3");
  heading.textContent = "Last 5 Runs";
  wrap.appendChild(heading);

  const list = document.createElement("ul");
  for (const run of history.slice(0, 5)) {
    const row = document.createElement("li");
    const reward = run.rewardPicked ? `T${run.rewardPicked.tier} ${run.rewardPicked.facet.text}` : "no pick";
    row.textContent = `${run.dungeon} +${run.keyLevel} - ${run.result} - ${run.durationSec}s - ${reward}`;
    list.appendChild(row);
  }
  wrap.appendChild(list);
  return wrap;
}
