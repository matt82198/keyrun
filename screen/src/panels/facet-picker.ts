/**
 * Usage: import { renderFacetPicker } from "./panels/facet-picker.js";
 *        row.appendChild(renderFacetPicker(item, store, onChange));
 *
 * The Loadout band's per-slot Facet picker (design/BIBLE.md sec 7): a `<select>` listing every
 * Facet in that slot's real class pool (content/data/facets/*.json, via craft.ts's `pickFacet`).
 * Free and unlimited -- "crafting charges/Ash gate acquiring [a fresh random roll], not swapping
 * [to a Facet the pool already publishes]" (dispatch); this never touches `item.charges`.
 */
import { getFacetsForSlot } from "../content.js";
import { pickFacet } from "../craft.js";
import { saveStore, type Item, type StoreV3 } from "../store.js";

export function renderFacetPicker(item: Item, store: StoreV3, onChange: () => void): HTMLElement {
  const wrap = document.createElement("div");
  wrap.className = "facet-picker";

  const label = document.createElement("label");
  label.className = "facet-picker-label";
  label.textContent = "Facet";
  label.htmlFor = `facet-picker-${item.slot}`;
  wrap.appendChild(label);

  const select = document.createElement("select");
  select.id = `facet-picker-${item.slot}`;
  select.className = "facet-picker-select";

  const pool = getFacetsForSlot(store.character.classId, item.slot);
  for (const facet of pool) {
    const opt = document.createElement("option");
    opt.value = facet.id;
    opt.textContent = facet.text;
    opt.selected = facet.id === item.facet?.id;
    select.appendChild(opt);
  }

  select.addEventListener("change", () => {
    store.character = pickFacet(store.character, item.id, select.value);
    saveStore(store);
    onChange();
  });
  wrap.appendChild(select);

  return wrap;
}
