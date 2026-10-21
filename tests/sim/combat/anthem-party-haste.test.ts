/**
 * R-ANTHEM-1 (finding f-7d5df7f9; design/BIBLE.md Sec 5 Anthem "Party gains 15% haste for 6 s."). The content combo
 * table's Anthem (Chanter Attuned, Warden Payoff) is `applyEffect hasted`, and applyComboEffects used to put it on the
 * Window's carrier: in the bot oracle runs that is the enemy, so Seed 108 got hasted and its seed.slam ended 3 ticks
 * early. The rule now: a combo's applyEffect whose effect carries positive haste is party haste. It goes to every
 * living actor of the Payoff actor's faction and never to the carrier. Twin: game/sim/Keyrun.Sim.Tests/
 * AnthemPartyHasteTests.cs (Combos.ApplyComboEffects).
 */
import { describe, expect, it } from "vitest";
import { loadContent, toComboTables } from "@content/index";
import { createActor } from "../../../sim/combat/actors";
import { applyComboEffects, lookupCombo } from "../../../sim/combat/combos";
import type { EffectDef } from "../../../sim/combat/effects";
import type { ActorState, SimState } from "../../../sim/combat/types";

const reg = loadContent();
const { combos } = toComboTables(reg.combos, [...reg.classes.keys()]);
const effectDefs: Record<string, EffectDef> = Object.fromEntries(reg.effects);

function at(tick: number, actors: ActorState[]): SimState {
  return { tick, seed: 1, prng: [0, 0, 0, 0], actors: Object.fromEntries(actors.map((a) => [a.id, a])), runTimerTicks: 0, phase: "combat", log: [] };
}

function player(id: number, classId: string, role: "tank" | "healer" | "damage"): ActorState {
  return createActor({ id, kind: "player", classId, role, pos: { x: id * 1000, y: 0, z: 0 }, maxHealth: 1000, maxResource: 100 });
}

function hasted(a: ActorState) {
  return a.effects.filter((e) => e.effectId === "hasted");
}

describe("R-ANTHEM-1: Anthem's haste goes to the party, never the combo's carrier", () => {
  it("the content Anthem closed on an enemy hastes every living party member for 6 s and not the enemy", () => {
    const anthem = lookupCombo(combos, "chanter", "warden");
    expect(anthem?.id).toBe("anthem");
    const warden = player(1, "warden", "tank");
    const cinder = player(2, "cinder", "damage");
    const chanter = player(3, "chanter", "healer");
    const seed = createActor({ id: 108, kind: "enemy", classId: "seed", role: "none", pos: { x: 500, y: 0, z: 0 }, maxHealth: 1000, maxResource: 0 });
    const s = at(40, [warden, cinder, chanter, seed]);

    applyComboEffects(s, warden, seed, anthem!.effects, effectDefs, () => {});

    expect(hasted(seed)).toEqual([]);
    for (const member of [warden, cinder, chanter]) {
      const inst = hasted(member);
      expect(inst).toHaveLength(1);
      expect(inst[0]!.endsTick).toBe(160);
      expect(inst[0]!.sourceId).toBe(1);
    }
  });

  it("skips a dead party member and never reaches another enemy", () => {
    const anthem = lookupCombo(combos, "chanter", "warden")!;
    const warden = player(1, "warden", "tank");
    const chanter = player(3, "chanter", "healer");
    chanter.alive = false;
    const seed = createActor({ id: 108, kind: "enemy", classId: "seed", role: "none", pos: { x: 500, y: 0, z: 0 }, maxHealth: 1000, maxResource: 0 });
    const other = createActor({ id: 109, kind: "enemy", classId: "seed", role: "none", pos: { x: 900, y: 0, z: 0 }, maxHealth: 1000, maxResource: 0 });
    const s = at(40, [warden, chanter, seed, other]);

    applyComboEffects(s, warden, seed, anthem.effects, effectDefs, () => {});

    expect(hasted(warden)).toHaveLength(1);
    expect(hasted(chanter)).toEqual([]);
    expect(hasted(seed)).toEqual([]);
    expect(hasted(other)).toEqual([]);
  });

  it("a combo debuff still lands on the carrier (Shatter's stun), so only haste is redirected", () => {
    const shatter = lookupCombo(combos, "warden", "cinder")!;
    const cinder = player(2, "cinder", "damage");
    const seed = createActor({ id: 108, kind: "enemy", classId: "seed", role: "none", pos: { x: 500, y: 0, z: 0 }, maxHealth: 1000, maxResource: 0 });
    const s = at(40, [cinder, seed]);

    applyComboEffects(s, cinder, seed, shatter.effects, effectDefs, () => {});

    expect(seed.effects.some((e) => e.effectId === "stunned")).toBe(true);
    expect(cinder.effects.some((e) => e.effectId === "stunned")).toBe(false);
  });
});
