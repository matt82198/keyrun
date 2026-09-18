import { describe, expect, it } from "vitest";
import { loadContent } from "@content/index";
import { buildParty, resolveHumanClassId } from "../../../client/src/sim-host/content-party.js";

const registry = loadContent();

describe("content-party: resolveHumanClassId", () => {
  it("accepts the three real class ids case-insensitively", () => {
    expect(resolveHumanClassId("Cinder")).toBe("cinder");
    expect(resolveHumanClassId("CHANTER")).toBe("chanter");
    expect(resolveHumanClassId("warden")).toBe("warden");
  });

  it("falls back to warden for anything unknown or missing", () => {
    expect(resolveHumanClassId("paladin")).toBe("warden");
    expect(resolveHumanClassId(null)).toBe("warden");
    expect(resolveHumanClassId("")).toBe("warden");
  });
});

describe("content-party: buildParty", () => {
  it("always yields five party members with exactly one human (actorId 1)", () => {
    for (const humanClassId of ["warden", "cinder", "chanter"]) {
      const { party, humanActorId } = buildParty(registry, humanClassId);
      expect(party).toHaveLength(5);
      expect(humanActorId).toBe(1);
      expect(new Set(party.map((p) => p.id)).size).toBe(5);
    }
  });

  it("gives the fixed roster exactly one tank, one healer, three damage", () => {
    const { party } = buildParty(registry, "warden");
    const roles = party.map((p) => p.role).sort();
    expect(roles).toEqual(["damage", "damage", "damage", "healer", "tank"]);
  });

  it("puts the human in the roster slot matching their chosen class", () => {
    const { party, humanActorId } = buildParty(registry, "chanter");
    const human = party.find((p) => p.id === humanActorId)!;
    expect(human.classId).toBe("chanter");
    expect(human.role).toBe("healer");
  });

  it("resolves tankActorId to the warden slot", () => {
    const { party, tankActorId } = buildParty(registry, "cinder");
    expect(party.find((p) => p.id === tankActorId)?.classId).toBe("warden");
  });

  it("sets each member's maxResource from their class loop (bots/src/kits.ts owns rotation/heal resolution now)", () => {
    const { party } = buildParty(registry, "warden");
    const chanter = registry.classes.get("chanter")!;
    const human = party.find((p) => p.classId === "chanter")!;
    expect(human.maxResource).toBe(chanter.loop.max);
  });
});
