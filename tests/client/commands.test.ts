import { describe, expect, it } from "vitest";
import { deriveAbilityCommands } from "../../client/src/input/commands.js";
import { DEFAULT_KEYBINDS } from "../../client/src/input/keybinds.js";
import type { SlotAbilities } from "../../client/src/input/slot-abilities.js";

// A synthetic kit: ids deliberately unlike any slot label, so a label leaking through would show.
const SLOTS: SlotAbilities = {
  ability1: "kit.one",
  ability2: "kit.two",
  ability3: "kit.three",
  ability4: "kit.four",
  ability5: "kit.five",
  abilityQ: "kit.kick",
  abilityE: "kit.guard",
  abilityR: "kit.util",
  abilityF: "kit.dash",
};

describe("deriveAbilityCommands", () => {
  it("emits no commands when nothing was just pressed", () => {
    expect(deriveAbilityCommands(new Set(), DEFAULT_KEYBINDS, SLOTS, 42)).toEqual([]);
  });

  it("maps a rotational number key to a cast of the ability in that slot, tick attached", () => {
    const commands = deriveAbilityCommands(new Set(["Digit1"]), DEFAULT_KEYBINDS, SLOTS, 7);
    expect(commands).toEqual([{ type: "cast", abilityId: "kit.one", tick: 7 }]);
  });

  it("maps Q/E/R/F to casts of the abilities in those slots, never the key letter", () => {
    const commands = deriveAbilityCommands(new Set(["KeyQ", "KeyF"]), DEFAULT_KEYBINDS, SLOTS, 3);
    expect(commands).toEqual([
      { type: "cast", abilityId: "kit.kick", tick: 3 },
      { type: "cast", abilityId: "kit.dash", tick: 3 },
    ]);
  });

  it("casts nothing (fail closed) when the host reported no slot map", () => {
    expect(deriveAbilityCommands(new Set(["Digit1", "KeyE"]), DEFAULT_KEYBINDS, undefined, 3)).toEqual([]);
  });

  it("maps Tab to a targetCycle command, not a cast", () => {
    const commands = deriveAbilityCommands(new Set(["Tab"]), DEFAULT_KEYBINDS, SLOTS, 1);
    expect(commands).toEqual([{ type: "targetCycle", tick: 1 }]);
  });

  it("orders multiple simultaneous presses by keybind action order, not Set insertion order", () => {
    const pressed = new Set(["Tab", "KeyQ", "Digit5", "Digit1"]);
    const commands = deriveAbilityCommands(pressed, DEFAULT_KEYBINDS, SLOTS, 9);
    expect(commands.map((c) => (c.type === "cast" ? c.abilityId : c.type))).toEqual([
      "kit.one",
      "kit.five",
      "kit.kick",
      "targetCycle",
    ]);
  });

  it("ignores codes that are not bound to any action, and the ping key", () => {
    expect(deriveAbilityCommands(new Set(["KeyZ", "Escape", "KeyG"]), DEFAULT_KEYBINDS, SLOTS, 5)).toEqual([]);
  });
});
