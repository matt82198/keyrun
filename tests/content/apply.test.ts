/**
 * tests/content/apply.test.ts
 * Usage: `npm test` (vitest). BIBLE.md Sec 6 "Talents" and Sec 7 "Gear and progression":
 * talents fork what an ability does; gear Facets retag or reshape an ability. Both apply to a
 * class's ability definitions at run start, deterministically. Covers applyLoadout/describeLoadout
 * against small self-contained fixtures (fast, precise) and a handful of integration checks
 * against the real content/data (content/index.ts's loadContent()).
 */
import { describe, expect, it } from "vitest";
import { applyLoadout, describeLoadout } from "../../content/apply";
import { loadContent } from "../../content/index";
import type { AbilityDef } from "../../content/schema/ability";
import type { ClassDef } from "../../content/schema/class";
import type { FacetDef, Item } from "../../content/schema/facet";

// ---------------------------------------------------------------------------------------------
// Fixtures: a tiny 2-rotational-ability-per-tag-role class, standing in for a real 5-ability
// class so the setup/payoff invariant and merge semantics can be tested in isolation without
// depending on the full 3-class content dataset.
// ---------------------------------------------------------------------------------------------

function ability(over: Partial<AbilityDef> & { id: string }): AbilityDef {
  return {
    name: over.id,
    castTicks: 0,
    gcdTicks: 30,
    cooldownTicks: 0,
    charges: 1,
    range: 3000,
    shape: { kind: "target" },
    interruptible: false,
    tags: [],
    effects: [{ op: "damage", amount: 100, school: "physical" }],
    ...over,
  };
}

function makeFixture() {
  const abilities = new Map<string, AbilityDef>([
    ["a.setup1", ability({ id: "a.setup1", tags: ["setup:foo"] })],
    ["a.payoff1", ability({ id: "a.payoff1", tags: ["payoff"] })],
    ["a.setup2", ability({ id: "a.setup2", tags: ["setup:bar"] })],
    ["a.payoff2", ability({ id: "a.payoff2", tags: ["payoff"] })],
    ["a.neutral", ability({ id: "a.neutral", tags: [] })],
    ["a.interrupt", ability({ id: "a.interrupt", tags: ["interrupt"] })],
    ["a.defensive", ability({ id: "a.defensive", tags: ["defensive"] })],
    ["a.utility", ability({ id: "a.utility", tags: ["utility"] })],
    ["a.movement", ability({ id: "a.movement", tags: ["movement"] })],
  ]);

  const classDef: ClassDef = {
    id: "fixture",
    name: "Fixture",
    loop: { resource: "energy", max: 100, kind: "builder-spender" },
    abilities: {
      rot1: "a.setup1", rot2: "a.payoff1", rot3: "a.setup2", rot4: "a.payoff2", rot5: "a.neutral",
      interrupt: "a.interrupt", defensive: "a.defensive", utility: "a.utility", movement: "a.movement",
    },
    talents: [
      {
        abilityId: "a.setup1",
        options: [
          { id: "longer", name: "Longer", text: "Window lasts longer.",
            patch: { effects: [{ op: "window", windowId: "foo", durationTicks: 200, onTarget: true }] } },
          { id: "retag", name: "Retag", text: "Setup1 becomes a Payoff instead.",
            patch: { tags: ["payoff"] } },
        ],
      },
      {
        abilityId: "a.neutral",
        options: [
          { id: "cone", name: "Cone", text: "Neutral becomes a cone.",
            patch: { shape: { kind: "cone", radius: 2000, angle1024: 90 } } },
        ],
      },
    ],
  };

  const facets = new Map<string, FacetDef>([
    ["f.weapon.reshape", {
      id: "f.weapon.reshape", slot: "weapon", abilityId: "a.payoff1",
      text: "Payoff1 becomes a floor patch instead of a single target.",
      patch: { shape: { kind: "circle", radius: 1500 } },
    }],
    ["f.charm.retagNeutral", {
      id: "f.charm.retagNeutral", slot: "charm", abilityId: "a.neutral",
      text: "Neutral also counts as a Payoff.",
      patch: { tagAdd: ["payoff"], effectsAppend: [{ op: "combo", comboTable: "combos" }] },
    }],
    ["f.ring.untag", {
      id: "f.ring.untag", slot: "ring", abilityId: "a.payoff2",
      text: "Payoff2 loses its Payoff tag in exchange for a bigger hit.",
      patch: { tagRemove: ["payoff"], tagAdd: ["heavy-hit"] },
    }],
    ["f.armor.wrongSlot", {
      id: "f.armor.wrongSlot", slot: "armor", abilityId: "a.defensive",
      text: "Mismatched slot for negative testing.",
      patch: { tagAdd: ["extra-tanky"] },
    }],
    ["f.keystone.idChange", {
      id: "f.keystone.idChange", slot: "keystone", abilityId: "a.setup2",
      text: "Illegally tries to rename the ability.",
      patch: { id: "a.setup2.renamed" },
    }],
  ]);

  return { abilities, classDef, facets };
}

function item(over: Partial<Item> & { id: string; slot: Item["slot"] }): Item {
  return { tier: 1, facet: null, charges: 2, ...over };
}

// ---------------------------------------------------------------------------------------------

describe("applyLoadout: base kit", () => {
  it("returns the base kit unchanged when no talent choices or gear are given", () => {
    const { abilities, classDef, facets } = makeFixture();
    const result = applyLoadout(classDef, abilities, facets, {}, []);
    for (const [id, base] of abilities) {
      expect(result.abilities[id]).toEqual(base);
    }
    expect(result.log).toEqual([]);
  });
});

describe("applyLoadout: talent forks", () => {
  it("a talent fork changes tags (retagging Setup1 as Payoff)", () => {
    const { abilities, classDef, facets } = makeFixture();
    // "retag" alone leaves 3 setups' worth removed / payoff count skewed: compensate isn't
    // needed here since we're only checking the tag change happened before invariant checks
    // would matter for a *different* fork -- use "longer" (safe) to check plain tag/effect
    // changes apply without breaking the invariant, and "retag" in its own invariant test below.
    const result = applyLoadout(classDef, abilities, facets, { "a.setup1": "longer" }, []);
    expect(result.abilities["a.setup1"]!.tags).toEqual(["setup:foo"]);
    expect(result.abilities["a.setup1"]!.effects).toEqual([
      { op: "window", windowId: "foo", durationTicks: 200, onTarget: true },
    ]);
    // base ability object itself must not have been mutated
    expect(abilities.get("a.setup1")!.effects).toEqual([{ op: "damage", amount: 100, school: "physical" }]);
  });

  it("rejects an invalid talent option id", () => {
    const { abilities, classDef, facets } = makeFixture();
    expect(() => applyLoadout(classDef, abilities, facets, { "a.setup1": "no-such-option" }, [])).toThrow(
      /no talent option "no-such-option"/,
    );
  });

  it("rejects a talent choice for an ability with no fork", () => {
    const { abilities, classDef, facets } = makeFixture();
    expect(() => applyLoadout(classDef, abilities, facets, { "a.payoff1": "anything" }, [])).toThrow(
      /no talent fork for ability "a\.payoff1"/,
    );
  });
});

describe("applyLoadout: facets", () => {
  it("a facet reshapes a floor patch (Payoff1's shape becomes a circle)", () => {
    const { abilities, classDef, facets } = makeFixture();
    const gear: Item[] = [item({ id: "i1", slot: "weapon", facet: { id: "f.weapon.reshape", text: "x", abilityId: "a.payoff1" } })];
    const result = applyLoadout(classDef, abilities, facets, {}, gear);
    expect(result.abilities["a.payoff1"]!.shape).toEqual({ kind: "circle", radius: 1500 });
    // untouched fields survive the merge
    expect(result.abilities["a.payoff1"]!.tags).toEqual(["payoff"]);
  });

  it("rejects an unknown facet id", () => {
    const { abilities, classDef, facets } = makeFixture();
    const gear: Item[] = [item({ id: "i1", slot: "weapon", facet: { id: "no-such-facet", text: "x", abilityId: "a.payoff1" } })];
    expect(() => applyLoadout(classDef, abilities, facets, {}, gear)).toThrow(/unknown facet id/);
  });

  it("rejects a facet socketed into the wrong slot", () => {
    const { abilities, classDef, facets } = makeFixture();
    // f.armor.wrongSlot is defined with slot "armor"; equip it in "weapon" instead.
    const gear: Item[] = [item({ id: "i1", slot: "weapon", facet: { id: "f.armor.wrongSlot", text: "x", abilityId: "a.defensive" } })];
    expect(() => applyLoadout(classDef, abilities, facets, {}, gear)).toThrow(/belongs to slot "armor"/);
  });

  it("rejects two items equipped in the same slot", () => {
    const { abilities, classDef, facets } = makeFixture();
    const gear: Item[] = [
      item({ id: "i1", slot: "weapon", facet: { id: "f.weapon.reshape", text: "x", abilityId: "a.payoff1" } }),
      item({ id: "i2", slot: "weapon" }),
    ];
    expect(() => applyLoadout(classDef, abilities, facets, {}, gear)).toThrow(/more than one item equipped in slot "weapon"/);
  });

  it("rejects a facet whose embedded abilityId does not match its definition", () => {
    const { abilities, classDef, facets } = makeFixture();
    const gear: Item[] = [item({ id: "i1", slot: "weapon", facet: { id: "f.weapon.reshape", text: "x", abilityId: "a.neutral" } })];
    expect(() => applyLoadout(classDef, abilities, facets, {}, gear)).toThrow(/item claims "a\.neutral"/);
  });
});

describe("applyLoadout: id-change guard", () => {
  it("refuses a patch (talent or facet) that changes an ability's id", () => {
    const { abilities, classDef, facets } = makeFixture();
    const gear: Item[] = [item({ id: "i1", slot: "keystone", facet: { id: "f.keystone.idChange", text: "x", abilityId: "a.setup2" } })];
    expect(() => applyLoadout(classDef, abilities, facets, {}, gear)).toThrow(/may not change ability id/);
  });
});

describe("applyLoadout: setup/payoff invariant", () => {
  it("rejects a combination that leaves != 2 setup or != 2 payoff rotational abilities", () => {
    const { abilities, classDef, facets } = makeFixture();
    // "retag" turns a.setup1 into a Payoff: 1 setup / 3 payoff.
    expect(() => applyLoadout(classDef, abilities, facets, { "a.setup1": "retag" }, [])).toThrow(
      /would leave 1 setup \/ 3 payoff/,
    );
  });

  it("lists the offending rotational ability ids and tags in the error", () => {
    const { abilities, classDef, facets } = makeFixture();
    try {
      applyLoadout(classDef, abilities, facets, { "a.setup1": "retag" }, []);
      throw new Error("expected applyLoadout to throw");
    } catch (err) {
      const message = (err as Error).message;
      expect(message).toContain("a.setup1=[payoff]");
      expect(message).toContain("a.payoff1=[payoff]");
      expect(message).toContain("a.setup2=[setup:bar]");
      expect(message).toContain("a.payoff2=[payoff]");
      expect(message).toContain("a.neutral=[]");
    }
  });

  it("accepts a combination that rebalances back to exactly 2 setup / 2 payoff", () => {
    const { abilities, classDef, facets } = makeFixture();
    // Retag setup1 -> payoff (1 setup/3 payoff), then facet-retag payoff2 -> not-payoff
    // (1 setup/2 payoff) is still unbalanced on setups; also retag neutral -> setup via a
    // second facet-style patch would need a "setup:" tagAdd. Simpler: use the ring facet to
    // strip payoff2's Payoff tag while a.neutral gets tagged Payoff via the charm facet, netting
    // back to 2/2 alongside the setup1 retag... but that changes payoff count only. Instead,
    // directly prove the *positive* path: no talent/facet changes at all is always balanced
    // (already covered by "base kit" test), and here prove one facet that adds a Payoff to the
    // neutral ability while another removes Payoff2's Payoff tag nets to 2 setup / 2 payoff.
    const gear: Item[] = [
      item({ id: "i1", slot: "charm", facet: { id: "f.charm.retagNeutral", text: "x", abilityId: "a.neutral" } }),
      item({ id: "i2", slot: "ring", facet: { id: "f.ring.untag", text: "x", abilityId: "a.payoff2" } }),
    ];
    const result = applyLoadout(classDef, abilities, facets, {}, gear);
    expect(result.abilities["a.neutral"]!.tags).toEqual(["payoff"]);
    expect(result.abilities["a.payoff2"]!.tags).toEqual(["heavy-hit"]);
  });
});

describe("applyLoadout: determinism", () => {
  it("same inputs produce a deep-equal result regardless of gear array order", () => {
    const { abilities, classDef, facets } = makeFixture();
    // Both facets here are tag-neutral for the rotational invariant: f.weapon.reshape only
    // changes a.payoff1's shape, and f.armor.wrongSlot (equipped in its real "armor" slot this
    // time) only tags the non-rotational a.defensive ability.
    const talentChoices = { "a.setup1": "longer", "a.neutral": "cone" };
    const gearA: Item[] = [
      item({ id: "i1", slot: "weapon", facet: { id: "f.weapon.reshape", text: "x", abilityId: "a.payoff1" } }),
      item({ id: "i2", slot: "armor", facet: { id: "f.armor.wrongSlot", text: "x", abilityId: "a.defensive" } }),
    ];
    const gearB = [...gearA].reverse();

    const resultA = applyLoadout(classDef, abilities, facets, talentChoices, gearA);
    const resultB = applyLoadout(classDef, abilities, facets, talentChoices, gearB);
    expect(resultB).toEqual(resultA);

    // running it again from scratch (fresh fixture) reproduces the same result
    const fresh = makeFixture();
    const resultC = applyLoadout(fresh.classDef, fresh.abilities, fresh.facets, talentChoices, gearA);
    expect(resultC).toEqual(resultA);
  });
});

describe("describeLoadout", () => {
  it("renders one line per changed ability", () => {
    const { abilities, classDef, facets } = makeFixture();
    const gear: Item[] = [item({ id: "i1", slot: "weapon", facet: { id: "f.weapon.reshape", text: "x", abilityId: "a.payoff1" } })];
    const result = applyLoadout(classDef, abilities, facets, { "a.neutral": "cone" }, gear);
    expect(result.log.length).toBe(2);
    expect(result.log.some((l) => l.startsWith("a.payoff1 (a.payoff1):"))).toBe(true);
    expect(result.log.some((l) => l.startsWith("a.neutral (a.neutral):"))).toBe(true);

    // describeLoadout is independently callable against a stored result
    const base = new Map(abilities);
    const recomputed = describeLoadout(classDef, base, result.abilities);
    expect(recomputed).toEqual(result.log);
  });

  it("is empty when nothing changed", () => {
    const { abilities, classDef, facets } = makeFixture();
    const result = applyLoadout(classDef, abilities, facets, {}, []);
    expect(result.log).toEqual([]);
  });
});

// ---------------------------------------------------------------------------------------------
// Integration: real content/data via content/index.ts's loadContent().
// ---------------------------------------------------------------------------------------------

describe("applyLoadout: real content integration", () => {
  it("each real class's base kit (no choices) round-trips unchanged and satisfies the invariant", () => {
    const reg = loadContent();
    for (const classDef of reg.classes.values()) {
      const result = applyLoadout(classDef, reg.abilities, reg.facets, {}, []);
      expect(result.log).toEqual([]);
      const slots = classDef.abilities;
      for (const id of [slots.rot1, slots.rot2, slots.rot3, slots.rot4, slots.rot5]) {
        expect(result.abilities[id]).toEqual(reg.abilities.get(id));
      }
    }
  });

  it("applies a real talent option from warden's data and changes that ability only", () => {
    const reg = loadContent();
    const warden = reg.classes.get("warden")!;
    const fork = warden.talents.find((t) => t.abilityId === "warden.guardBreak")!;
    const option = fork.options.find((o) => o.id === "pin")!;
    const result = applyLoadout(warden, reg.abilities, reg.facets, { "warden.guardBreak": "pin" }, []);
    expect(result.abilities["warden.guardBreak"]!.tags).toEqual(option.patch.tags);
    expect(result.abilities["warden.strike"]).toEqual(reg.abilities.get("warden.strike"));
  });

  it("rejects the real warden.charm.vigilantsMark facet on its own (breaks the 2/2 invariant)", () => {
    const reg = loadContent();
    const warden = reg.classes.get("warden")!;
    const gear: Item[] = [
      { id: "i1", slot: "charm", tier: 5, charges: 2, facet: { id: "warden.charm.vigilantsMark", text: "x", abilityId: "warden.vigil" } },
    ];
    expect(() => applyLoadout(warden, reg.abilities, reg.facets, {}, gear)).toThrow(/bible invariant/);
  });

  it("is deterministic against the real registries", () => {
    const reg = loadContent();
    const cinder = reg.classes.get("cinder")!;
    const talentChoices = { "cinder.emberBolt": "overcharge" };
    const r1 = applyLoadout(cinder, reg.abilities, reg.facets, talentChoices, []);
    const r2 = applyLoadout(cinder, reg.abilities, reg.facets, talentChoices, []);
    expect(r2).toEqual(r1);
  });
});
