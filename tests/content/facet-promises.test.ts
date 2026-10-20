/**
 * tests/content/facet-promises.test.ts
 * Usage: `npx vitest run tests/content/facet-promises.test.ts`. BIBLE.md Sec 7 "Gear and
 * progression": a Facet changes what an ability does, and its text is the contract the player
 * reads. Two ring Facets promised an upside their data never delivered:
 *   - cinder.ring.overflowValve ("Partial Dump") appended a -30 Heat op to Cold Snap, whose base
 *     -100 already empties a 100 Heat bar, so the Facet dumped nothing less (and undid the
 *     Overflow talent's partial dump).
 *   - chanter.ring.cheapDissonance ("Cheap Cast") set a 0 Verse cost on an ability that already
 *     costs nothing, so it had no upside at all.
 * Each case resolves the shipped Facet through the real loadout path (content/apply.ts
 * applyLoadout -> content/apply-patch.ts applyPatch) and then drives the real sim cast path
 * (sim/combat/casting.ts startCast/completeCast, sim/combat/cooldowns.ts isAbilityReady) to
 * observe the promised effect on the caster.
 */
import { describe, expect, it } from "vitest";
import { loadContent } from "../../content/index";
import { applyLoadout } from "../../content/apply";
import type { AbilityDef as ContentAbilityDef } from "../../content/schema/ability";
import type { Item } from "../../content/schema/facet";
import { createActor } from "../../sim/combat/actors";
import type { AbilityDef } from "../../sim/combat/abilities";
import { completeCast, startCast, type CastContext } from "../../sim/combat/casting";
import { isAbilityReady } from "../../sim/combat/cooldowns";
import type { EffectDef } from "../../sim/combat/effects";
import type { ActorState, SimState } from "../../sim/combat/types";

const reg = loadContent();

/** The class's ability `abilityId` after the talent choices and (optionally) one ring Facet. */
function resolved(classId: string, abilityId: string, facetId: string | null, talents: Record<string, string> = {}): AbilityDef {
  const cls = reg.classes.get(classId)!;
  const gear: Item[] = [];
  if (facetId !== null) {
    const facet = reg.facets.get(facetId)!;
    gear.push({ id: "ring-1", slot: facet.slot, tier: 1, charges: 2, facet: { id: facet.id, text: facet.text, abilityId: facet.abilityId } });
  }
  const { abilities } = applyLoadout(cls, reg.abilities, reg.facets, talents, gear);
  return abilities[abilityId] as ContentAbilityDef as unknown as AbilityDef;
}

function duel(maxResource: number): { state: SimState; caster: ActorState } {
  const caster = createActor({ id: 1, kind: "player", classId: "x", role: "damage", pos: { x: 0, y: 0, z: 0 }, maxHealth: 1000, maxResource });
  const target = createActor({ id: 2, kind: "enemy", classId: "y", role: "none", pos: { x: 1000, y: 0, z: 0 }, maxHealth: 100000, maxResource: 0 });
  const state: SimState = { tick: 0, seed: 1, prng: [0, 0, 0, 0], actors: { 1: caster, 2: target }, runTimerTicks: 0, phase: "combat", log: [] };
  return { state, caster };
}

function castCtx(def: AbilityDef): CastContext {
  const effects: Record<string, EffectDef> = {};
  for (const [id, e] of reg.effects) effects[id] = e as unknown as EffectDef;
  return { abilities: { [def.id]: def }, effects, lockoutTicks: 60 };
}

/** Heat left on a caster who casts `def` from a full 100 Heat bar (the Cinder loop max). */
function heatAfterCast(def: AbilityDef): number {
  const { state, caster } = duel(reg.classes.get("cinder")!.loop.max);
  expect(startCast(state, caster, def, 2, undefined, 0)).toBeNull();
  completeCast(state, caster, castCtx(def), () => {});
  return caster.resource;
}

/** Whether `def` can be cast again `ticks` after a cast at tick 0. */
function readyAfter(def: AbilityDef, ticks: number): boolean {
  const { state, caster } = duel(100);
  expect(startCast(state, caster, def, 2, undefined, 0)).toBeNull();
  completeCast(state, caster, castCtx(def), () => {});
  return isAbilityReady(caster, def, ticks);
}

describe("cinder.ring.overflowValve keeps Heat banked", () => {
  const text = () => reg.facets.get("cinder.ring.overflowValve")!.text;

  it("the patched Cold Snap dumps less than the base Cold Snap from a full bar", () => {
    const max = reg.classes.get("cinder")!.loop.max;
    const baseDump = max - heatAfterCast(resolved("cinder", "cinder.coldSnap", null));
    const valveDump = max - heatAfterCast(resolved("cinder", "cinder.coldSnap", "cinder.ring.overflowValve"));
    expect(baseDump).toBe(100);
    expect(valveDump, `Overflow Valve Cold Snap dumped ${valveDump} Heat; text: "${text()}"`).toBeLessThan(baseDump);
  });

  it("keeps 40 more Heat banked than the same Cold Snap without it, with and without the Overflow talent", () => {
    for (const talents of [{}, { "cinder.coldSnap": "overflow" }, { "cinder.coldSnap": "deepFreeze" }]) {
      const without = heatAfterCast(resolved("cinder", "cinder.coldSnap", null, talents));
      const withValve = heatAfterCast(resolved("cinder", "cinder.coldSnap", "cinder.ring.overflowValve", talents));
      expect(withValve - without, `talents ${JSON.stringify(talents)}: ${without} -> ${withValve}`).toBe(40);
    }
    expect(text()).toMatch(/40 more Heat banked/);
  });
});

describe("chanter.ring.cheapDissonance has a real upside", () => {
  const text = () => reg.facets.get("chanter.ring.cheapDissonance")!.text;

  it("the text claims no Verse saving the base ability cannot give", () => {
    const base = resolved("chanter", "chanter.dissonance", null);
    const patched = resolved("chanter", "chanter.dissonance", "chanter.ring.cheapDissonance");
    if (/less Verse|cheaper/i.test(text())) expect(patched.cost.amount).toBeLessThan(base.cost.amount);
    expect(patched.cost).toEqual(base.cost);
  });

  it("Dissonance is castable again sooner with the Facet than without it", () => {
    const base = resolved("chanter", "chanter.dissonance", null);
    const patched = resolved("chanter", "chanter.dissonance", "chanter.ring.cheapDissonance");
    expect(patched.cooldownTicks).toBeLessThan(base.cooldownTicks);
    const at = patched.cooldownTicks;
    expect(readyAfter(base, at), `base Dissonance ready at tick ${at}`).toBe(false);
    expect(readyAfter(patched, at), `Cheap Dissonance ready at tick ${at}; text: "${text()}"`).toBe(true);
    expect(text()).toMatch(/cooldown falls/);
  });
});
