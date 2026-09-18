/**
 * Usage: import { renderCharacterPanel } from "./panels/character.js";
 *        renderCharacterPanel(container, store, onChange);
 *
 * Renders the character band: class, level, and the five rotational abilities each as a radio
 * group of its talent fork options (content/data/classes/*.json's TalentFork -- design/BIBLE.md
 * sec 6). Picking is free: no cost, no cooldown, reversible any time (a "(Base)" option reverts
 * an ability to its unpatched definition). The band also shows describeLoadout's live build
 * summary (content/apply.ts, via talents.ts's computeLoadout) -- every ability whose final
 * definition differs from its base one.
 */
import type { TalentFork } from "../../../content/schema/class.js";
import { getAbilityRegistry, getClassDef } from "../content.js";
import { computeLoadout, setTalentChoice } from "../talents.js";
import { saveStore, type Character, type StoreV3 } from "../store.js";
import { KEYBIND_TABLE } from "../types.js";

const ROT_SLOTS = ["rot1", "rot2", "rot3", "rot4", "rot5"] as const;

export function renderCharacterPanel(root: HTMLElement, store: StoreV3, onChange: () => void): void {
  root.innerHTML = "";
  root.className = "band band-character";
  const character = store.character;
  const classDef = getClassDef(character.classId);
  const abilities = getAbilityRegistry();

  const heading = document.createElement("h2");
  heading.textContent = "Character";
  root.appendChild(heading);

  const summary = document.createElement("p");
  summary.className = "char-summary";
  summary.textContent = `${classDef.name} - Level ${character.level}`;
  root.appendChild(summary);

  const list = document.createElement("ul");
  list.className = "ability-list";
  ROT_SLOTS.forEach((slot, index) => {
    const abilityId = classDef.abilities[slot];
    const ability = abilities.get(abilityId);
    if (!ability) return;
    const fork = classDef.talents.find((f) => f.abilityId === abilityId);
    list.appendChild(
      renderAbilityRow(KEYBIND_TABLE[index] ?? "?", ability.name, abilityId, fork, store, onChange),
    );
  });
  root.appendChild(list);

  root.appendChild(renderLoadoutSummary(character));
}

function renderAbilityRow(
  keybind: string,
  name: string,
  abilityId: string,
  fork: TalentFork | undefined,
  store: StoreV3,
  onChange: () => void,
): HTMLElement {
  const row = document.createElement("li");
  row.className = "ability-row";

  const key = document.createElement("span");
  key.className = "keybind";
  key.textContent = keybind;
  row.appendChild(key);

  const label = document.createElement("span");
  label.className = "ability-name";
  label.textContent = name;
  row.appendChild(label);

  if (fork) row.appendChild(renderTalentFork(abilityId, fork, store, onChange));
  return row;
}

function renderTalentFork(abilityId: string, fork: TalentFork, store: StoreV3, onChange: () => void): HTMLElement {
  const group = document.createElement("div");
  group.className = "talent-fork";
  const current = store.character.talentChoices[abilityId] ?? "";

  const options = [{ id: "", name: "(Base)", text: "Unpatched." }, ...fork.options];
  for (const option of options) {
    const wrap = document.createElement("label");
    wrap.className = "talent-option";

    const radio = document.createElement("input");
    radio.type = "radio";
    radio.name = `talent-${abilityId}`;
    radio.value = option.id;
    radio.checked = option.id === current;
    radio.addEventListener("change", () => {
      store.character = setTalentChoice(store.character, abilityId, option.id === "" ? null : option.id);
      saveStore(store);
      onChange();
    });
    wrap.appendChild(radio);

    const text = document.createElement("span");
    text.textContent = `${option.name} — ${option.text}`;
    wrap.appendChild(text);

    group.appendChild(wrap);
  }
  return group;
}

function renderLoadoutSummary(character: Character): HTMLElement {
  const wrap = document.createElement("div");
  wrap.className = "loadout-summary";

  const heading = document.createElement("h3");
  heading.textContent = "Build";
  wrap.appendChild(heading);

  const { result, error } = computeLoadout(character);
  if (error) {
    const p = document.createElement("p");
    p.className = "loadout-error";
    p.textContent = error;
    wrap.appendChild(p);
    return wrap;
  }

  const lines = result?.log ?? [];
  if (lines.length === 0) {
    const p = document.createElement("p");
    p.className = "loadout-empty";
    p.textContent = "No changes from base yet.";
    wrap.appendChild(p);
    return wrap;
  }

  const list = document.createElement("ul");
  list.className = "loadout-log";
  for (const line of lines) {
    const item = document.createElement("li");
    item.textContent = line;
    list.appendChild(item);
  }
  wrap.appendChild(list);
  return wrap;
}
