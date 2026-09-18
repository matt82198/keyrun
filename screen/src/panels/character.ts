/**
 * Usage: import { renderCharacterPanel } from "./panels/character.js";
 *        renderCharacterPanel(container, character);
 *
 * Renders the character band: class, level, and the five abilities with their
 * keybind and chosen talent id. Auto-equipped gear lives in panels/loadout.ts.
 */
import type { Character } from "../store.js";

export function renderCharacterPanel(root: HTMLElement, character: Character): void {
  root.innerHTML = "";
  root.className = "band band-character";

  const heading = document.createElement("h2");
  heading.textContent = "Character";
  root.appendChild(heading);

  const summary = document.createElement("p");
  summary.className = "char-summary";
  summary.textContent = `${capitalize(character.classId)} - Level ${character.level}`;
  root.appendChild(summary);

  const list = document.createElement("ul");
  list.className = "ability-list";
  for (const ability of character.abilities) {
    list.appendChild(renderAbilityRow(ability));
  }
  root.appendChild(list);
}

function renderAbilityRow(ability: Character["abilities"][number]): HTMLElement {
  const row = document.createElement("li");
  row.className = "ability-row";

  const key = document.createElement("span");
  key.className = "keybind";
  key.textContent = ability.keybind;
  row.appendChild(key);

  const name = document.createElement("span");
  name.className = "ability-name";
  name.textContent = ability.name;
  row.appendChild(name);

  const talent = document.createElement("span");
  talent.className = "talent-id";
  talent.textContent = ability.talentId;
  row.appendChild(talent);

  return row;
}

function capitalize(value: string): string {
  if (value.length === 0) return value;
  return value[0]!.toUpperCase() + value.slice(1);
}
