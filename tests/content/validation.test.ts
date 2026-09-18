/**
 * tests/content/validation.test.ts
 * Usage: `npm test` (vitest). Proves content/index.ts fails loudly on bad data: unknown ids,
 * duplicate ids, a class without exactly 5 rotational abilities, a talent patch that adds an
 * ability, an unknown effect reference, and a combo whose setupClass/payoffClass match.
 */
import { describe, expect, it } from "vitest";
import { byId, loadContent, validate } from "../../content/index";
import type { AbilityDef } from "../../content/schema/ability";

describe("content validation: bad fixtures", () => {
  it("throws on a duplicate id within a registry", () => {
    const dupe: AbilityDef = {
      id: "dup", name: "Dup", castTicks: 0, gcdTicks: 0, cooldownTicks: 0, charges: 1,
      range: 0, shape: { kind: "self" }, interruptible: false, tags: [], effects: [],
    };
    expect(() => byId([dupe, { ...dupe }], "ability")).toThrow(/duplicate ability id "dup"/);
  });

  it("throws on an unknown ability id referenced by a class slot", () => {
    const reg = loadContent();
    (reg.classes.get("warden")!.abilities as unknown as Record<string, string>).rot1 = "nonexistent.ability";
    expect(() => validate(reg)).toThrow(/unknown ability/);
  });

  it("throws when a class does not have exactly 5 rotational abilities", () => {
    const reg = loadContent();
    delete (reg.classes.get("warden")!.abilities as unknown as Record<string, string>).rot5;
    expect(() => validate(reg)).toThrow(/expected 5/);
  });

  it("throws when a talent option's patch adds/renames an ability id", () => {
    const reg = loadContent();
    const fork = reg.classes.get("warden")!.talents[0]!;
    fork.options[0]!.patch = { ...fork.options[0]!.patch, id: "warden.newAbility" };
    expect(() => validate(reg)).toThrow(/adds\/renames an ability id/);
  });

  it("throws on a combo whose setupClass and payoffClass match", () => {
    const reg = loadContent();
    reg.combos.get("shatter")!.payoffClass = "warden";
    expect(() => validate(reg)).toThrow(/matching setupClass\/payoffClass/);
  });

  it("throws on an unknown effect id referenced by an ability", () => {
    const reg = loadContent();
    reg.abilities.get("warden.vigil")!.effects.push({
      op: "applyEffect", effectId: "no-such-effect", durationTicks: 20,
    });
    expect(() => validate(reg)).toThrow(/unknown effect/);
  });

  it("allows a combo field-payoff wildcard ('*') even though it never equals setupClass", () => {
    const reg = loadContent();
    expect(() => validate(reg)).not.toThrow();
    expect(reg.combos.get("bulwark-field")!.payoffClass).toBe("*");
  });
});
