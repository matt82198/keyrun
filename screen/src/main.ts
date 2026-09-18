/**
 * Usage: served as a module script by screen/index.html.
 *
 * Wires the four bands (character, loadout, run, reward) onto the page in that order, backed by the
 * versioned localStorage store in store.ts. The loadout's "new" delta flags are computed against
 * recentlyEquipped on the first render, then cleared so a reload starts clean. The reward band
 * renders whatever choice-of-three is pending on the character (set by progression.ts's completeRun,
 * cleared by reward.ts's Pick button) -- every band now takes the whole store, since talent picks,
 * gear/craft actions, the Clause pick, and run actions all need to saveStore, not just read the
 * character.
 */
import { loadStore, markLoadoutSeen, saveStore } from "./store.js";
import { renderCharacterPanel } from "./panels/character.js";
import { renderLoadoutPanel } from "./panels/loadout.js";
import { renderRunPanel } from "./panels/run.js";
import { renderRewardPanel } from "./panels/reward.js";

const store = loadStore();

const characterRoot = document.getElementById("character-band");
const loadoutRoot = document.getElementById("loadout-band");
const runRoot = document.getElementById("run-band");
const rewardRoot = document.getElementById("reward");

function renderAll(): void {
  if (characterRoot) renderCharacterPanel(characterRoot, store, renderAll);
  if (loadoutRoot) renderLoadoutPanel(loadoutRoot, store, renderAll);
  if (runRoot) renderRunPanel(runRoot, store, renderAll);
  if (rewardRoot) renderRewardPanel(rewardRoot, store, renderAll);
}

renderAll();

store.character = markLoadoutSeen(store.character);
saveStore(store);
