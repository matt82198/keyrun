/**
 * Usage: import { renderRunPanel } from "./panels/run.js";
 *        renderRunPanel(container, store, onChange);
 *
 * Renders the run band: a key-level stepper (2-20, locks while queued), a dungeon select (one
 * entry: Hollow Vault), the pick-of-three Clause chooser (clause-picker.ts), the bots-first queue
 * simulation (queue.ts), and the Play button / "since last run" delta / run history
 * (run-history.ts). Split across these files so no one file crosses the repo's 190-line-per-file
 * rule (tests/repo/loc.test.ts).
 */
import { renderClausePicker } from "./clause-picker.js";
import { renderQueueBand } from "./queue.js";
import { renderDeltaLine, renderHistory, renderPlayButton } from "./run-history.js";
import { clampKeyLevel, isKeyLocked, saveStore, type Character, type StoreV3 } from "../store.js";

const DUNGEONS = ["Hollow Vault"];

export function renderRunPanel(root: HTMLElement, store: StoreV3, onChange: () => void): void {
  root.innerHTML = "";
  root.className = "band band-run";
  const character = store.character;

  const heading = document.createElement("h2");
  heading.textContent = "Run";
  root.appendChild(heading);

  root.appendChild(renderKeyStepper(character, store, onChange));
  root.appendChild(renderDungeonSelect());
  root.appendChild(renderClausePicker(store, onChange));
  root.appendChild(renderQueueBand(store, onChange));
  root.appendChild(renderPlayButton(store.character));
  root.appendChild(renderDeltaLine(store.character));
  root.appendChild(renderHistory(store.character.runHistory));
}

function renderKeyStepper(character: Character, store: StoreV3, onChange: () => void): HTMLElement {
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
