/**
 * Usage: import { renderLoadoutPanel } from "./panels/loadout.js";
 *        renderLoadoutPanel(container, character);
 *
 * Renders the loadout band: five auto-equipped gear slots (weapon, armor,
 * trinket, two rings), each flagged "NEW" when acquired after the
 * character's lastSeenAt. No stash management in the POC.
 */
import { GEAR_SLOTS, getNewGearSlots, type Character, type GearSlot } from "../store.js";

const SLOT_LABELS: Record<GearSlot, string> = {
  weapon: "Weapon",
  armor: "Armor",
  trinket: "Trinket",
  ring1: "Ring 1",
  ring2: "Ring 2",
};

export function renderLoadoutPanel(root: HTMLElement, character: Character): void {
  root.innerHTML = "";
  root.className = "band band-loadout";

  const heading = document.createElement("h2");
  heading.textContent = "Loadout";
  root.appendChild(heading);

  const newSlots = new Set(getNewGearSlots(character));

  const list = document.createElement("ul");
  list.className = "gear-list";
  for (const slot of GEAR_SLOTS) {
    list.appendChild(renderGearRow(slot, character.gear[slot], newSlots.has(slot)));
  }
  root.appendChild(list);

  const note = document.createElement("p");
  note.className = "loadout-note";
  note.textContent = "Auto-equipped by item level. No stash.";
  root.appendChild(note);
}

function renderGearRow(slot: GearSlot, item: Character["gear"][GearSlot], isNew: boolean): HTMLElement {
  const row = document.createElement("li");
  row.className = "gear-row";

  const label = document.createElement("span");
  label.className = "gear-slot-label";
  label.textContent = SLOT_LABELS[slot];
  row.appendChild(label);

  const name = document.createElement("span");
  name.className = "gear-item-name";
  name.textContent = `${item.name} (ilvl ${item.ilvl})`;
  row.appendChild(name);

  if (isNew) {
    const badge = document.createElement("span");
    badge.className = "new-badge";
    badge.textContent = "NEW";
    row.appendChild(badge);
  }

  return row;
}
