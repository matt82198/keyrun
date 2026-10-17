/**
 * Unit coverage for sim/combat/combos.ts: table lookup and the effect-application pipeline a
 * closed Window's Combo fires through (damage/heal/applyEffect/removeEffect/resource/threat, all
 * routed through the same integer primitives casting.ts's completeCast uses).
 */
import { describe, expect, it } from "vitest";
import { createActor } from "../../../sim/combat/actors";
import type { EffectDef } from "../../../sim/combat/effects";
import type { SimEvent, SimState } from "../../../sim/combat/types";
import { applyComboEffects, lookupCombo, type ComboTable } from "../../../sim/combat/combos";

const table: ComboTable = {
  warden: {
    cinder: { id: "shatter", effects: [{ op: "damage", amount: 40, school: "physical" }] },
    chanter: {
      id: "consecrate",
      effects: [
        { op: "damage", amount: 20, school: "physical" },
        { op: "applyEffect", effectId: "heal_pulse", durationTicks: 120, stacks: 1 },
      ],
    },
  },
  cinder: {
    warden: { id: "forge", effects: [{ op: "resource", delta: 40 }] },
  },
};

const healPulse: EffectDef = { id: "heal_pulse", maxStacks: 1, dispellableTag: null, modifiers: {}, periodic: null };

function state(): SimState {
  return { tick: 0, seed: 1, prng: [0, 0, 0, 0], actors: {}, runTimerTicks: 0, phase: "combat", log: [] };
}

describe("lookupCombo", () => {
  it("finds the combo for a setupClass/payoffClass pair", () => {
    expect(lookupCombo(table, "warden", "cinder")?.id).toBe("shatter");
    expect(lookupCombo(table, "cinder", "warden")?.id).toBe("forge");
  });

  it("returns undefined for an unlisted pair", () => {
    expect(lookupCombo(table, "chanter", "warden")).toBeUndefined();
  });
});

describe("applyComboEffects", () => {
  it("applies damage to the target and attributes it to the payoff actor", () => {
    const source = createActor({ id: 1, kind: "player", classId: "cinder", role: "damage", pos: { x: 0, y: 0, z: 0 }, maxHealth: 100, maxResource: 0 });
    const target = createActor({ id: 2, kind: "enemy", classId: "boss", role: "none", pos: { x: 0, y: 0, z: 0 }, maxHealth: 100, maxResource: 0 });
    const s = state();
    const events: SimEvent[] = [];
    applyComboEffects(s, source, target, table.warden!.cinder!.effects, {}, (e) => events.push(e));
    expect(target.health).toBe(60);
    const dmg = events.find((e) => e.type === "damage");
    expect(dmg?.sourceId).toBe(1);
    expect(dmg?.targetId).toBe(2);
  });

  it("credits a Forge-style combo's resource delta to the payoff actor, not the Window carrier", () => {
    // Forge is setupClass cinder -> payoffClass warden: payoff.ts passes the WARDEN (the payoff
    // actor) as `source` and the Window's carrier (an enemy) as `target`.
    const source = createActor({ id: 1, kind: "player", classId: "warden", role: "tank", pos: { x: 0, y: 0, z: 0 }, maxHealth: 100, maxResource: 100 });
    const target = createActor({ id: 2, kind: "enemy", classId: "boss", role: "none", pos: { x: 0, y: 0, z: 0 }, maxHealth: 100, maxResource: 100 });
    source.resource = 0;
    target.resource = 0;
    const s = state();
    applyComboEffects(s, source, target, table.cinder!.warden!.effects, {}, () => {});
    expect(source.resource).toBe(40);
    expect(target.resource).toBe(0);
  });

  it("applies a bundled applyEffect alongside damage (Consecrate)", () => {
    const source = createActor({ id: 1, kind: "player", classId: "chanter", role: "healer", pos: { x: 0, y: 0, z: 0 }, maxHealth: 100, maxResource: 0 });
    const target = createActor({ id: 2, kind: "enemy", classId: "boss", role: "none", pos: { x: 0, y: 0, z: 0 }, maxHealth: 100, maxResource: 0 });
    const s = state();
    applyComboEffects(s, source, target, table.warden!.chanter!.effects, { heal_pulse: healPulse }, () => {});
    expect(target.health).toBe(80);
    expect(target.effects.some((e) => e.effectId === "heal_pulse")).toBe(true);
  });
});
