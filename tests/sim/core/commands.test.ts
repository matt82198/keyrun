// Usage: vitest suite for sim/core/commands.ts command validation.
import { describe, expect, it } from "vitest";
import { validateCommand } from "@sim/core/commands";

describe("validateCommand: move", () => {
  it("accepts a well-formed move command", () => {
    const c = { tick: 5, actorId: 1, kind: "move", dir: { x: 1000, z: -1000 }, yaw: 0 };
    expect(validateCommand(c)).toEqual(c);
  });

  it("rejects dir components out of -1000..1000", () => {
    expect(validateCommand({ tick: 5, actorId: 1, kind: "move", dir: { x: 1001, z: 0 }, yaw: 0 })).toBeNull();
    expect(validateCommand({ tick: 5, actorId: 1, kind: "move", dir: { x: -1001, z: 0 }, yaw: 0 })).toBeNull();
  });

  it("rejects yaw out of 0..1023", () => {
    expect(validateCommand({ tick: 5, actorId: 1, kind: "move", dir: { x: 0, z: 0 }, yaw: 1024 })).toBeNull();
    expect(validateCommand({ tick: 5, actorId: 1, kind: "move", dir: { x: 0, z: 0 }, yaw: -1 })).toBeNull();
  });

  it("rejects missing or non-numeric dir", () => {
    expect(validateCommand({ tick: 5, actorId: 1, kind: "move", yaw: 0 })).toBeNull();
    expect(validateCommand({ tick: 5, actorId: 1, kind: "move", dir: { x: "1", z: 0 }, yaw: 0 })).toBeNull();
  });
});

describe("validateCommand: cast", () => {
  it("accepts a cast with a target", () => {
    const c = { tick: 1, actorId: 2, kind: "cast", abilityId: "fireball", targetId: 9 };
    expect(validateCommand(c)).toEqual(c);
  });

  it("accepts a cast with a ground point and no target", () => {
    const c = { tick: 1, actorId: 2, kind: "cast", abilityId: "blizzard", point: { x: 1, y: 2, z: 3 } };
    expect(validateCommand(c)).toEqual(c);
  });

  it("accepts a bare cast with neither target nor point", () => {
    const c = { tick: 1, actorId: 2, kind: "cast", abilityId: "selfheal" };
    expect(validateCommand(c)).toEqual(c);
  });

  it("rejects a missing or empty abilityId", () => {
    expect(validateCommand({ tick: 1, actorId: 2, kind: "cast" })).toBeNull();
    expect(validateCommand({ tick: 1, actorId: 2, kind: "cast", abilityId: "" })).toBeNull();
    expect(validateCommand({ tick: 1, actorId: 2, kind: "cast", abilityId: 7 })).toBeNull();
  });

  it("rejects a non-integer point component", () => {
    expect(
      validateCommand({ tick: 1, actorId: 2, kind: "cast", abilityId: "x", point: { x: 1.5, y: 0, z: 0 } }),
    ).toBeNull();
  });
});

describe("validateCommand: stopcast and target", () => {
  it("accepts stopcast", () => {
    const c = { tick: 3, actorId: 1, kind: "stopcast" };
    expect(validateCommand(c)).toEqual(c);
  });

  it("accepts target with an id or null", () => {
    expect(validateCommand({ tick: 3, actorId: 1, kind: "target", targetId: 42 })).toEqual({
      tick: 3,
      actorId: 1,
      kind: "target",
      targetId: 42,
    });
    expect(validateCommand({ tick: 3, actorId: 1, kind: "target", targetId: null })).toEqual({
      tick: 3,
      actorId: 1,
      kind: "target",
      targetId: null,
    });
  });

  it("rejects target missing the targetId field", () => {
    expect(validateCommand({ tick: 3, actorId: 1, kind: "target" })).toBeNull();
  });
});

describe("validateCommand: malformed input", () => {
  it("rejects non-objects", () => {
    expect(validateCommand(null)).toBeNull();
    expect(validateCommand(undefined)).toBeNull();
    expect(validateCommand(42)).toBeNull();
    expect(validateCommand("cast")).toBeNull();
    expect(validateCommand([])).toBeNull();
  });

  it("rejects an unknown kind", () => {
    expect(validateCommand({ tick: 1, actorId: 1, kind: "teleport" })).toBeNull();
  });

  it("rejects a negative or non-integer tick", () => {
    expect(validateCommand({ tick: -1, actorId: 1, kind: "stopcast" })).toBeNull();
    expect(validateCommand({ tick: 1.5, actorId: 1, kind: "stopcast" })).toBeNull();
  });

  it("rejects a non-integer actorId", () => {
    expect(validateCommand({ tick: 1, actorId: "1", kind: "stopcast" })).toBeNull();
  });
});
