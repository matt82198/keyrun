// Keybind action ids and loading of client/keybinds.json. Movement (WASD),
// Space, and mouse-look/zoom are hardcoded camera/movement controls per
// client/CLAUDE.md and are not part of this configurable set.
//
// Note: design/BIBLE.md §2 counts 1-5 + Q/E/R/F + G(ping) as the ten-bind
// budget; "targetCycle" (Tab) predates the pings lane and already occupies
// a configurable slot here, so this file currently carries eleven binds.
// Reconciling that with the bible's ten is out of scope for the pings
// backlog item -- flagged, not silently fixed.

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
  | "targetCycle"
  | "ping";

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
  "ping",
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
  ping: "KeyG",
};

// An ability action names a SLOT, not an ability id: the ability in each slot is resolved per class
// kit by input/slot-abilities.ts's resolveSlotAbilities (the single authority; see that file).

export async function loadKeybinds(url = "./keybinds.json"): Promise<Keybinds> {
  const res = await fetch(url);
  if (!res.ok) throw new Error(`failed to load keybinds from ${url}: ${res.status}`);
  return (await res.json()) as Keybinds;
}
