import { describe, expect, it } from "vitest";
import { deriveAbilityCommands } from "../../client/src/input/commands.js";
import { DEFAULT_KEYBINDS } from "../../client/src/input/keybinds.js";

describe("deriveAbilityCommands", () => {
  it("emits no commands when nothing was just pressed", () => {
    expect(deriveAbilityCommands(new Set(), DEFAULT_KEYBINDS, 42)).toEqual([]);
  });

  it("maps a rotational number key to a cast command with the tick attached", () => {
    const commands = deriveAbilityCommands(new Set(["Digit1"]), DEFAULT_KEYBINDS, 7);
    expect(commands).toEqual([{ type: "cast", abilityId: "1", tick: 7 }]);
  });

  it("maps Q/E/R/F to cast commands keyed by letter", () => {
    const commands = deriveAbilityCommands(new Set(["KeyQ", "KeyF"]), DEFAULT_KEYBINDS, 3);
    expect(commands).toEqual([
      { type: "cast", abilityId: "Q", tick: 3 },
      { type: "cast", abilityId: "F", tick: 3 },
    ]);
  });

  it("maps Tab to a targetCycle command, not a cast", () => {
    const commands = deriveAbilityCommands(new Set(["Tab"]), DEFAULT_KEYBINDS, 1);
    expect(commands).toEqual([{ type: "targetCycle", tick: 1 }]);
  });

  it("orders multiple simultaneous presses by keybind action order, not Set insertion order", () => {
    const pressed = new Set(["Tab", "KeyQ", "Digit5", "Digit1"]);
    const commands = deriveAbilityCommands(pressed, DEFAULT_KEYBINDS, 9);
    expect(commands.map((c) => (c.type === "cast" ? c.abilityId : c.type))).toEqual([
      "1",
      "5",
      "Q",
      "targetCycle",
    ]);
  });

  it("ignores codes that are not bound to any action", () => {
    expect(deriveAbilityCommands(new Set(["KeyZ", "Escape"]), DEFAULT_KEYBINDS, 5)).toEqual([]);
  });
});
