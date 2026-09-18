/**
 * Usage: import { renderRewardPanel } from "./panels/reward.js";
 *        renderRewardPanel(container, store, onChange);
 *
 * Renders the end-of-run choice-of-three (design/BIBLE.md section 7): each pending reward
 * (store.character.rewardChoices, set by rules.ts's completeRun) shown with its slot, tier, and
 * facet text, and a Pick button that resolves it via rules.ts's pickReward -- which equips it (or
 * parks it as a "compare" on a tier tie) and attaches it to the latest run history entry. When
 * nothing is pending it renders a quiet placeholder instead of an empty band.
 */
import { pickReward, saveStore, type GearSlotId, type Item, type StoreV3 } from "../store.js";

const SLOT_LABELS: Record<GearSlotId, string> = {
  weapon: "Weapon",
  armor: "Armor",
  charm: "Charm",
  ring: "Ring",
  keystone: "Keystone",
};

export function renderRewardPanel(root: HTMLElement, store: StoreV3, onChange: () => void): void {
  root.innerHTML = "";
  root.className = "band band-reward";

  const heading = document.createElement("h2");
  heading.textContent = "Reward";
  root.appendChild(heading);

  const choices = store.character.rewardChoices;
  if (!choices || choices.length === 0) {
    const empty = document.createElement("p");
    empty.className = "reward-empty";
    empty.textContent = "No pending reward. Finish a run to choose one of three.";
    root.appendChild(empty);
    return;
  }

  const list = document.createElement("ul");
  list.className = "reward-choices";
  for (const item of choices) {
    list.appendChild(renderRewardRow(item, store, onChange));
  }
  root.appendChild(list);
}

function renderRewardRow(item: Item, store: StoreV3, onChange: () => void): HTMLElement {
  const row = document.createElement("li");
  row.className = "reward-row";

  const slot = document.createElement("span");
  slot.className = "reward-slot-label";
  slot.textContent = SLOT_LABELS[item.slot];
  row.appendChild(slot);

  const detail = document.createElement("span");
  detail.className = "reward-item-detail";
  detail.textContent = `T${item.tier} — ${item.facet?.text ?? "no Facet"}`;
  row.appendChild(detail);

  const pick = document.createElement("button");
  pick.type = "button";
  pick.className = "reward-pick-btn";
  pick.textContent = "Pick";
  pick.addEventListener("click", () => {
    store.character = pickReward(store.character, item.id);
    saveStore(store);
    onChange();
  });
  row.appendChild(pick);

  return row;
}
