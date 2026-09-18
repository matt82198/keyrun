/**
 * Usage: import { renderLoadoutPanel } from "./panels/loadout.js";
 *        renderLoadoutPanel(container, store, onChange);
 *
 * Renders the loadout band: five auto-equipped gear slots (weapon, armor, charm, ring, keystone),
 * each showing its tier and Facet text, with a free Facet picker (facet-picker.ts) from the
 * class's real Facet pool and a charge-gated random Reroll button ("crafting is two charges per
 * item" -- design/BIBLE.md section 7). A slot flagged "NEW" was auto-equipped since the loadout
 * was last viewed. A slot with a parked "compare" item (a tie on tier -- auto-equip never
 * overwrites a chosen facet) shows an Equip / Discard choice. No stash management in the POC.
 */
import { renderFacetPicker } from "./facet-picker.js";
import {
  GEAR_SLOT_IDS,
  MAX_ITEM_TIER,
  discardCompare,
  equipCompare,
  getNewGearSlots,
  rerollFacet,
  saveStore,
  type Character,
  type GearSlotId,
  type Item,
  type StoreV3,
} from "../store.js";

const SLOT_LABELS: Record<GearSlotId, string> = {
  weapon: "Weapon",
  armor: "Armor",
  charm: "Charm",
  ring: "Ring",
  keystone: "Keystone",
};

export function renderLoadoutPanel(root: HTMLElement, store: StoreV3, onChange: () => void): void {
  root.innerHTML = "";
  root.className = "band band-loadout";
  const character = store.character;

  const heading = document.createElement("h2");
  heading.textContent = "Loadout";
  root.appendChild(heading);

  const newSlots = new Set(getNewGearSlots(character));

  const list = document.createElement("ul");
  list.className = "gear-list";
  for (const slot of GEAR_SLOT_IDS) {
    list.appendChild(renderGearRow(slot, character, newSlots.has(slot), store, onChange));
  }
  root.appendChild(list);

  const note = document.createElement("p");
  note.className = "loadout-note";
  note.textContent = `Auto-equipped by tier (cap ${MAX_ITEM_TIER}). No stash.`;
  root.appendChild(note);
}

function renderGearRow(
  slot: GearSlotId,
  character: Character,
  isNew: boolean,
  store: StoreV3,
  onChange: () => void,
): HTMLElement {
  const item = character.gear[slot];
  const row = document.createElement("li");
  row.className = "gear-row";

  const label = document.createElement("span");
  label.className = "gear-slot-label";
  label.textContent = SLOT_LABELS[slot];
  row.appendChild(label);

  row.appendChild(renderItemSummary(item));

  if (isNew) {
    const badge = document.createElement("span");
    badge.className = "new-badge";
    badge.textContent = "NEW";
    row.appendChild(badge);
  }

  row.appendChild(renderFacetPicker(item, store, onChange));

  const reroll = document.createElement("button");
  reroll.type = "button";
  reroll.className = "reroll-btn";
  reroll.textContent = `Reroll (${item.charges} left)`;
  reroll.disabled = item.charges <= 0;
  reroll.addEventListener("click", () => {
    store.character = rerollFacet(store.character, item.id);
    saveStore(store);
    onChange();
  });
  row.appendChild(reroll);

  const pending = character.compareGear[slot];
  if (pending) row.appendChild(renderCompareRow(pending, slot, store, onChange));

  return row;
}

function renderItemSummary(item: Item): HTMLElement {
  const name = document.createElement("span");
  name.className = "gear-item-name";
  name.textContent = `T${item.tier} — ${item.facet?.text ?? "no Facet"}`;
  return name;
}

function renderCompareRow(pending: Item, slot: GearSlotId, store: StoreV3, onChange: () => void): HTMLElement {
  const wrap = document.createElement("div");
  wrap.className = "compare-row";

  const label = document.createElement("span");
  label.className = "compare-label";
  label.textContent = `Compare: T${pending.tier} — ${pending.facet?.text ?? "no Facet"}`;
  wrap.appendChild(label);

  const equip = document.createElement("button");
  equip.type = "button";
  equip.className = "compare-equip-btn";
  equip.textContent = "Equip";
  equip.addEventListener("click", () => {
    store.character = equipCompare(store.character, slot);
    saveStore(store);
    onChange();
  });
  wrap.appendChild(equip);

  const discard = document.createElement("button");
  discard.type = "button";
  discard.className = "compare-discard-btn";
  discard.textContent = "Discard";
  discard.addEventListener("click", () => {
    store.character = discardCompare(store.character, slot);
    saveStore(store);
    onChange();
  });
  wrap.appendChild(discard);

  return wrap;
}
