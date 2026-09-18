import { describe, expect, it } from "vitest";
import {
  ABANDON_KEY_CODE,
  deriveHostControlCommands,
  STOPCAST_KEY_CODE,
} from "../../../client/src/sim-host/host-commands.js";

describe("host-commands: deriveHostControlCommands", () => {
  it("emits a stopcast command when the stopcast key was just pressed", () => {
    const commands = deriveHostControlCommands(new Set([STOPCAST_KEY_CODE]), 42);
    expect(commands).toEqual([{ type: "stopcast", tick: 42 }]);
  });

  it("emits an abandon command when the abandon key was just pressed", () => {
    const commands = deriveHostControlCommands(new Set([ABANDON_KEY_CODE]), 7);
    expect(commands).toEqual([{ type: "abandon", tick: 7 }]);
  });

  it("emits both, in a stable order, when both keys land on the same tick", () => {
    const commands = deriveHostControlCommands(new Set([ABANDON_KEY_CODE, STOPCAST_KEY_CODE]), 1);
    expect(commands).toEqual([
      { type: "stopcast", tick: 1 },
      { type: "abandon", tick: 1 },
    ]);
  });

  it("emits nothing for unrelated keys (edge-triggered, no false positives)", () => {
    expect(deriveHostControlCommands(new Set(["KeyW", "Digit1"]), 3)).toEqual([]);
  });

  it("emits nothing for an empty just-pressed set", () => {
    expect(deriveHostControlCommands(new Set(), 3)).toEqual([]);
  });
});
