/**
 * Usage: npm test (vitest picks this up via tests/**\/*.test.ts).
 *
 * Renders each of the three panels this lane touches from a fixture character (dom-stub.ts's
 * minimal fake DOM -- this repo has no jsdom dependency) and asserts on the resulting tree: the
 * Character band's talent fork radios and live build summary, the Loadout band's five Facet
 * pickers, and the Run band's key stepper and Clause chooser. Each also simulates one real user
 * interaction (a radio/select "change" or a button "click") and checks the store update.
 */
import { beforeEach, describe, expect, it } from "vitest";
import { renderCharacterPanel } from "../../screen/src/panels/character.js";
import { renderLoadoutPanel } from "../../screen/src/panels/loadout.js";
import { renderRunPanel } from "../../screen/src/panels/run.js";
import { createFixtureCharacter, pickClauseOffers, type StoreV3 } from "../../screen/src/store.js";
import { installFakeDocument, type FakeElement } from "./dom-stub.js";

function makeStore(): StoreV3 {
  return { schemaVersion: 3, character: createFixtureCharacter(1000) };
}

let root: FakeElement;
let store: StoreV3;
let changeCount: number;
const onChange = () => {
  changeCount += 1;
};

beforeEach(() => {
  root = installFakeDocument();
  store = makeStore();
  changeCount = 0;
});

describe("character panel", () => {
  it("renders the class, level, and all five abilities with a talent fork", () => {
    renderCharacterPanel(root as unknown as HTMLElement, store, onChange);
    expect(root.textContent).toContain("Warden");
    expect(root.textContent).toContain("Level 10");
    const radios = root.findAll((el) => el.tagName === "INPUT" && el.type === "radio");
    // 5 abilities x (1 "(Base)" + each fork's own options) -- every warden rotational ability is forked.
    expect(radios.length).toBeGreaterThanOrEqual(5 * 2);
  });

  it("picking a talent option updates the store and is reflected in the build summary on re-render", () => {
    renderCharacterPanel(root as unknown as HTMLElement, store, onChange);
    const warcry = root.find((el) => el.tagName === "INPUT" && el.value === "warcry");
    expect(warcry).toBeDefined();
    warcry!.dispatch("change");

    expect(store.character.talentChoices["warden.strike"]).toBe("warcry");
    expect(changeCount).toBe(1);

    root = installFakeDocument();
    renderCharacterPanel(root as unknown as HTMLElement, store, onChange);
    expect(root.textContent).toContain("warden.strike");
  });
});

describe("loadout panel", () => {
  it("renders all five gear slots, each with a tier/Facet summary and a Facet picker", () => {
    renderLoadoutPanel(root as unknown as HTMLElement, store, onChange);
    for (const label of ["Weapon", "Armor", "Charm", "Ring", "Keystone"]) {
      expect(root.textContent).toContain(label);
    }
    const selects = root.findAll((el) => el.tagName === "SELECT");
    expect(selects).toHaveLength(5);
    expect(selects[0]!.children.length).toBeGreaterThan(1); // more than one Facet on offer
  });

  it("picking a Facet from the picker persists it on the character", () => {
    renderLoadoutPanel(root as unknown as HTMLElement, store, onChange);
    const weaponSelect = root.find((el) => el.tagName === "SELECT" && el.id === "facet-picker-weapon")!;
    const otherOption = weaponSelect.children.find((o) => o.value !== store.character.gear.weapon.facet?.id)!;
    weaponSelect.value = otherOption.value;
    weaponSelect.dispatch("change");

    expect(store.character.gear.weapon.facet?.id).toBe(otherOption.value);
    expect(changeCount).toBe(1);
  });
});

describe("run panel", () => {
  it("renders the key stepper and a pick-of-three Clause chooser with no 'none' option", () => {
    renderRunPanel(root as unknown as HTMLElement, store, onChange);
    expect(root.textContent).toContain("Key Level");
    expect(root.textContent).toContain("Clause");

    const offers = pickClauseOffers(store.character.keyLevel);
    expect(offers).toHaveLength(3);
    const offerIds = new Set(offers.map((c) => c.id));
    for (const id of offerIds) {
      expect(root.findAll((el) => el.tagName === "INPUT" && el.type === "radio" && el.value === id)).toHaveLength(1);
    }
    const clauseRadios = root.findAll((el) => el.tagName === "INPUT" && el.type === "radio" && offerIds.has(el.value));
    expect(clauseRadios).toHaveLength(3);
    expect(root.findAll((el) => el.tagName === "INPUT" && el.type === "radio" && el.value === "none")).toHaveLength(0);
  });

  it("stepping the key level up updates the store", () => {
    renderRunPanel(root as unknown as HTMLElement, store, onChange);
    const plus = root.find((el) => el.tagName === "BUTTON" && el.className.includes("key-plus"))!;
    const before = store.character.keyLevel;
    plus.dispatch("click");
    expect(store.character.keyLevel).toBe(before + 1);
  });
});
