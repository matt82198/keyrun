/**
 * client/src/hud/onboarding.ts
 *
 * D10 first-10-seconds onboarding: a centred, pointer-transparent panel that names the controls and
 * the goal the moment the run starts. Shown once per run (one OnboardingOverlay per page boot) and
 * dismissed for good by the first key press (`keydown` on `target`, never re-shown); the key press
 * itself still reaches the game (the listener only observes). Key labels come from the live keybinds
 * so a rebind is reflected. Exposes `.hud-onboarding` with `data-dismissed` for the e2e check.
 *
 * Usage: `new OnboardingOverlay(app, keybinds, window);`
 */
import type { Keybinds } from "../input/keybinds.js";
import { formatKeyLabel } from "../render/ability-bar.js";

interface KeyTarget {
  addEventListener(type: "keydown", fn: () => void): void;
  removeEventListener(type: "keydown", fn: () => void): void;
}

/** The overlay's rows: [keys, what they do]. Pure, so the copy is testable without a DOM. */
export function onboardingRows(k: Keybinds): Array<[string, string]> {
  const keys = (...actions: Array<keyof Keybinds>) => actions.map((a) => formatKeyLabel(k[a])).join(" ");
  return [
    ["W A S D", "move (relative to the camera)"],
    ["Right-drag", "look around; scroll to zoom"],
    [keys("ability1", "ability2", "ability3", "ability4", "ability5"), "your rotation (names on the bar below)"],
    [keys("abilityQ"), "KICK: interrupt an enemy cast with a yellow cast bar"],
    [keys("abilityE", "abilityR", "abilityF"), "defend, utility, movement"],
    [formatKeyLabel(k.targetCycle), "cycle target (yellow ring)"],
    ["Space", "dash"],
  ];
}

export class OnboardingOverlay {
  readonly root: HTMLDivElement;
  private readonly onKey = (): void => this.dismiss();

  constructor(container: HTMLElement, keybinds: Keybinds, private readonly target: KeyTarget) {
    this.root = document.createElement("div");
    this.root.className = "hud-onboarding";
    this.root.dataset.dismissed = "false";
    const title = document.createElement("div");
    title.className = "hud-onboarding-title";
    title.textContent = "Clear every pull, then kill the boss before the timer runs out.";
    this.root.appendChild(title);
    for (const [keys, text] of onboardingRows(keybinds)) {
      const row = document.createElement("div");
      const kbd = document.createElement("kbd");
      kbd.textContent = keys;
      row.appendChild(kbd);
      row.appendChild(document.createTextNode(text));
      this.root.appendChild(row);
    }
    const hint = document.createElement("div");
    hint.style.opacity = "0.7";
    hint.textContent = "Press any key to start.";
    this.root.appendChild(hint);
    container.appendChild(this.root);
    target.addEventListener("keydown", this.onKey);
  }

  /** Name the human's class in the title once the host reports it. */
  setHeroName(name: string): void {
    const title = this.root.firstChild as HTMLDivElement | null;
    if (title) title.textContent = `You are the ${name} (YOU, brightest, with the white ring). Clear every pull, then kill the boss before the timer.`;
  }

  dismiss(): void {
    if (this.root.dataset.dismissed === "true") return;
    this.root.dataset.dismissed = "true";
    this.root.hidden = true;
    this.target.removeEventListener("keydown", this.onKey);
  }
}
