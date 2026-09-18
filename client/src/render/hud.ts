// DOM-overlay HUD: ability bar (keybind labels + cooldown sweep), run
// timer, target-cycle hint, and a screen-centre crosshair. Kept separate
// from the 3D scene since it is plain 2D UI.
import { KEYBIND_ACTIONS, abilityIdForAction, type Keybinds } from "../input/keybinds.js";
import { SIM_HZ } from "../sim-host/snapshot.js";

interface AbilitySlot {
  root: HTMLDivElement;
  sweep: HTMLDivElement;
  label: HTMLSpanElement;
  keyLabel: HTMLSpanElement;
}

export class Hud {
  private readonly root: HTMLDivElement;
  private readonly timerEl: HTMLDivElement;
  private readonly abilityBarEl: HTMLDivElement;
  private readonly slots = new Map<string, AbilitySlot>();
  private readonly cooldownPeaks = new Map<string, number>();

  constructor(container: HTMLElement, keybinds: Keybinds) {
    this.root = document.createElement("div");
    this.root.className = "hud-root";
    container.appendChild(this.root);

    const crosshair = document.createElement("div");
    crosshair.className = "hud-crosshair";
    this.root.appendChild(crosshair);

    this.timerEl = document.createElement("div");
    this.timerEl.className = "hud-timer";
    this.root.appendChild(this.timerEl);

    this.abilityBarEl = document.createElement("div");
    this.abilityBarEl.className = "hud-ability-bar";
    this.root.appendChild(this.abilityBarEl);

    for (const action of KEYBIND_ACTIONS) {
      const abilityId = abilityIdForAction(action);
      if (!abilityId) continue; // skip targetCycle: not an ability-bar slot
      const slot = this.buildSlot(keybinds[action]);
      this.abilityBarEl.appendChild(slot.root);
      this.slots.set(abilityId, slot);
    }
  }

  private buildSlot(keyLabelText: string): AbilitySlot {
    const root = document.createElement("div");
    root.className = "hud-ability-slot";
    const sweep = document.createElement("div");
    sweep.className = "hud-ability-sweep";
    root.appendChild(sweep);
    const keyLabel = document.createElement("span");
    keyLabel.className = "hud-ability-key";
    keyLabel.textContent = formatKeyLabel(keyLabelText);
    root.appendChild(keyLabel);
    const label = document.createElement("span");
    label.className = "hud-ability-cd";
    root.appendChild(label);
    return { root, sweep, label, keyLabel };
  }

  updateTimer(runTimerTicks: number, timerLimitTicks: number): void {
    const elapsed = formatClock(runTimerTicks / SIM_HZ);
    const limit = formatClock(timerLimitTicks / SIM_HZ);
    this.timerEl.textContent = `${elapsed} / ${limit}`;
    this.timerEl.classList.toggle("hud-timer-over", runTimerTicks > timerLimitTicks);
  }

  updateAbilityBar(cooldowns: Record<string, number>): void {
    for (const [abilityId, slot] of this.slots) {
      const remaining = Math.max(0, cooldowns[abilityId] ?? 0);
      const peak = Math.max(this.cooldownPeaks.get(abilityId) ?? 0, remaining);
      this.cooldownPeaks.set(abilityId, remaining > 0 ? peak : 0);
      const fraction = peak > 0 ? remaining / peak : 0;
      slot.sweep.style.height = `${Math.round(fraction * 100)}%`;
      slot.label.textContent = remaining > 0 ? String(Math.ceil(remaining / SIM_HZ)) : "";
    }
  }
}

function formatKeyLabel(code: string): string {
  if (code.startsWith("Digit")) return code.slice(5);
  if (code.startsWith("Key")) return code.slice(3);
  return code;
}

function formatClock(totalSeconds: number): string {
  const clamped = Math.max(0, totalSeconds);
  const minutes = Math.floor(clamped / 60);
  const seconds = Math.floor(clamped % 60);
  return `${minutes}:${String(seconds).padStart(2, "0")}`;
}
