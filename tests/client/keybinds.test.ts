import { describe, expect, it } from "vitest";
import { readFileSync } from "node:fs";
import { fileURLToPath } from "node:url";
import { KEYBIND_ACTIONS } from "../../client/src/input/keybinds.js";

const keybindsPath = fileURLToPath(new URL("../../client/keybinds.json", import.meta.url));

describe("client/keybinds.json", () => {
  const data = JSON.parse(readFileSync(keybindsPath, "utf8"));

  it("binds exactly the known keybind actions, no more no fewer", () => {
    expect(Object.keys(data)).toHaveLength(KEYBIND_ACTIONS.length);
  });

  it("binds every known keybind action to a non-empty string", () => {
    for (const action of KEYBIND_ACTIONS) {
      expect(typeof data[action]).toBe("string");
      expect(data[action].length).toBeGreaterThan(0);
    }
  });

  it("has no unknown actions and no duplicate physical keys", () => {
    const keys = Object.keys(data);
    expect(keys.sort()).toEqual([...KEYBIND_ACTIONS].sort());
    const codes = Object.values(data) as string[];
    expect(new Set(codes).size).toBe(codes.length);
  });
});
