/**
 * client/src/render/ability-bar.ts
 *
 * The DOM ability bar (D10 first-run readability, split out of render/hud.ts): one slot per ability
 * keybind with a big key badge, the ability's NAME (render/ability-bar-model.ts, from the host's
 * display info), the slot's role caption for Q/E/R/F (interrupt / defensive / utility / movement),
 * a cooldown sweep + seconds left, and a GCD state (the slot dims while the global cooldown holds
 * it). A persistent key legend sits above the bar. Each slot exposes `data-action`,
 * `data-ability-id` and `data-state` ("ready" | "cooldown" | "gcd" | "empty") for the e2e check.
 *
 * Usage: `const bar = new AbilityBarView(root, keybinds); bar.update(cooldowns, slots, info, gcd);`
 */
import type { Keybinds } from "../input/keybinds.js";
import { ABILITY_ACTIONS, ACTION_CLASS_SLOT, type AbilityAction, type SlotAbilities } from "../input/slot-abilities.js";
import { TICK_HZ } from "../sim-host/snapshot.js";
import type { DisplayInfo } from "../sim-host/display-info.js";
import { abilityBarModel } from "./ability-bar-model.js";

const SLOT_CAPTION: Partial<Record<string, string>> = {
  interrupt: "KICK",
  defensive: "DEFEND",
  utility: "UTILITY",
  movement: "MOVE",
};

interface SlotEls {
  root: HTMLDivElement;
  sweep: HTMLDivElement;
  name: HTMLSpanElement;
  cd: HTMLSpanElement;
}

export function formatKeyLabel(code: string): string {
  if (code.startsWith("Digit")) return code.slice(5);
  if (code.startsWith("Key")) return code.slice(3);
  return code;
}

function el<K extends "div" | "span">(tag: K, className: string, text?: string): HTMLElementTagNameMap[K] {
  const e = document.createElement(tag);
  e.className = className;
  if (text !== undefined) e.textContent = text;
  return e;
}

export class AbilityBarView {
  readonly root: HTMLDivElement;
  private readonly slots = new Map<AbilityAction, SlotEls>();
  private readonly peaks = new Map<AbilityAction, number>();

  constructor(container: HTMLElement, keybinds: Keybinds) {
    const legend = el("div", "hud-key-legend",
      "1-5 rotation   Q kick   E defend   R utility   F move   Tab target   Space dash   right-drag look");
    container.appendChild(legend);
    this.root = el("div", "hud-ability-bar");
    container.appendChild(this.root);
    for (const action of ABILITY_ACTIONS) {
      const root = el("div", "hud-ability-slot");
      root.dataset.action = action;
      root.dataset.state = "empty";
      const sweep = el("div", "hud-ability-sweep");
      const key = el("span", "hud-ability-key", formatKeyLabel(keybinds[action]));
      const caption = SLOT_CAPTION[ACTION_CLASS_SLOT[action]];
      const name = el("span", "hud-ability-name");
      const cd = el("span", "hud-ability-cd");
      root.appendChild(sweep);
      root.appendChild(key);
      if (caption) root.appendChild(el("span", "hud-ability-caption", caption));
      root.appendChild(name);
      root.appendChild(cd);
      this.root.appendChild(root);
      this.slots.set(action, { root, sweep, name, cd });
    }
  }

  update(cooldowns: Record<string, number>, slotAbilities: SlotAbilities | undefined, info?: DisplayInfo, gcdRemainingTicks = 0): void {
    for (const s of abilityBarModel(slotAbilities, cooldowns, info, gcdRemainingTicks)) {
      const els = this.slots.get(s.action);
      if (!els) continue;
      const peak = s.remainingTicks > 0 ? Math.max(this.peaks.get(s.action) ?? 0, s.remainingTicks) : 0;
      this.peaks.set(s.action, peak);
      const cdFraction = s.cooldownFraction ?? (peak > 0 ? s.remainingTicks / peak : 0);
      const gcdLen = s.abilityId ? info?.abilities[s.abilityId]?.gcdTicks ?? 0 : 0;
      const fraction = s.onCooldown ? cdFraction : s.gcdActive ? Math.min(1, gcdLen > 0 ? gcdRemainingTicks / gcdLen : 1) : 0;
      els.sweep.style.height = `${Math.round(fraction * 100)}%`;
      els.cd.textContent = s.onCooldown ? String(Math.ceil(s.remainingTicks / TICK_HZ)) : "";
      if (els.name.textContent !== s.name) els.name.textContent = s.name;
      if (s.abilityId) els.root.dataset.abilityId = s.abilityId;
      els.root.dataset.state = s.abilityId === null ? "empty" : s.onCooldown ? "cooldown" : s.gcdActive ? "gcd" : "ready";
      els.root.title = s.name;
    }
  }
}
