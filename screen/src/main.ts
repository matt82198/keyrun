/**
 * Usage: served as a module script by screen/index.html.
 *
 * Wires the three bands (character, loadout, run) onto the page in that
 * order, backed by the versioned localStorage store in store.ts. The
 * loadout's "new" delta flags are computed against lastSeenAt on the first
 * render, then lastSeenAt is bumped so a reload clears them.
 */
import { loadStore, markLoadoutSeen, saveStore } from "./store.js";
import { renderCharacterPanel } from "./panels/character.js";
import { renderLoadoutPanel } from "./panels/loadout.js";
import { renderRunPanel } from "./panels/run.js";

const store = loadStore();

const characterRoot = document.getElementById("character-band");
const loadoutRoot = document.getElementById("loadout-band");
const runRoot = document.getElementById("run-band");

function renderAll(): void {
  if (characterRoot) renderCharacterPanel(characterRoot, store.character);
  if (loadoutRoot) renderLoadoutPanel(loadoutRoot, store.character);
  if (runRoot) renderRunPanel(runRoot, store, renderAll);
}

renderAll();

store.character = markLoadoutSeen(store.character);
saveStore(store);
