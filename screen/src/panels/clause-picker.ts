/**
 * Usage: import { renderClausePicker } from "./panels/clause-picker.js";
 *        root.appendChild(renderClausePicker(store, onChange));
 *
 * The Run band's pick-of-three Clause chooser (design/BIBLE.md sec 8): three offers, seeded
 * deterministically from the key level (clauses.ts's pickClauseOffers), rendered as a radio
 * group. Exactly one Clause is always active -- "none" is never offered -- so ensureClauseSelected
 * keeps the stored pick valid whenever the offers change (e.g. the key level moved since the last
 * pick), persisting the fix-up before render so the Play button always carries a real pick.
 */
import { ensureClauseSelected, pickClauseOffers, selectClause } from "../clauses.js";
import { isKeyLocked, saveStore, type StoreV3 } from "../store.js";

export function renderClausePicker(store: StoreV3, onChange: () => void): HTMLElement {
  const fixed = ensureClauseSelected(store.character);
  if (fixed !== store.character) {
    store.character = fixed;
    saveStore(store);
  }
  const character = store.character;

  const wrap = document.createElement("div");
  wrap.className = "clause-picker";

  const heading = document.createElement("h3");
  heading.textContent = "Clause";
  wrap.appendChild(heading);

  const locked = isKeyLocked(character);
  const offers = pickClauseOffers(character.keyLevel);
  for (const clause of offers) {
    const label = document.createElement("label");
    label.className = "clause-option";

    const radio = document.createElement("input");
    radio.type = "radio";
    radio.name = "clause-pick";
    radio.value = clause.id;
    radio.checked = clause.id === character.clauseId;
    radio.disabled = locked;
    radio.addEventListener("change", () => {
      store.character = selectClause(store.character, clause.id);
      saveStore(store);
      onChange();
    });
    label.appendChild(radio);

    const text = document.createElement("span");
    text.textContent = `${clause.name} — ${clause.description}`;
    label.appendChild(text);

    wrap.appendChild(label);
  }
  return wrap;
}
