// Keybind action ids and loading of client/keybinds.json (ten total binds).
// Movement (WASD), Space, and mouse-look/zoom are hardcoded camera/movement
// controls per client/CLAUDE.md and are not part of this configurable set.

export type KeybindAction =
  | "ability1"
  | "ability2"
  | "ability3"
  | "ability4"
  | "ability5"
  | "abilityQ"
  | "abilityE"
  | "abilityR"
  | "abilityF"
  | "targetCycle";

export type Keybinds = Record<KeybindAction, string>;

export const KEYBIND_ACTIONS: readonly KeybindAction[] = [
  "ability1",
  "ability2",
  "ability3",
  "ability4",
  "ability5",
  "abilityQ",
  "abilityE",
  "abilityR",
  "abilityF",
  "targetCycle",
];

export const DEFAULT_KEYBINDS: Keybinds = {
  ability1: "Digit1",
  ability2: "Digit2",
  ability3: "Digit3",
  ability4: "Digit4",
  ability5: "Digit5",
  abilityQ: "KeyQ",
  abilityE: "KeyE",
  abilityR: "KeyR",
  abilityF: "KeyF",
  targetCycle: "Tab",
};

/** Maps an ability action id to the abilityId used in cast commands; null for non-ability actions. */
export function abilityIdForAction(action: KeybindAction): string | null {
  if (action === "targetCycle") return null;
  return action.replace(/^ability/, "");
}

export async function loadKeybinds(url = "./keybinds.json"): Promise<Keybinds> {
  const res = await fetch(url);
  if (!res.ok) throw new Error(`failed to load keybinds from ${url}: ${res.status}`);
  return (await res.json()) as Keybinds;
}
