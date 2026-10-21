/**
 * bots/src/roles/healer-setup.ts
 *
 * CADENCE-SETUP (finding f-b31e8f34; design/BIBLE.md Sec 4 "synergy is a timing game between two
 * people", Sec 5 Chanter Cadence "Setup: applies Attuned Window"). With nobody under the heal
 * threshold, the healer opens its on-target Setup Window on an enemy a Payoff partner is about to
 * hit, the way a WoW Mythic+ healer adds damage setups during downtime. Before this, Cadence was
 * cast only as a heal, so Attuned always landed on an ally while its Payoffs (Anthem, Flashfire)
 * are cast at enemies, and no Attuned Combo ever closed in bot play.
 *
 * The rule, in order:
 *   1. either verse: Cadence plays the caster's verse variant on its target, but its HoT (Mend) or
 *      shield (Ward) never lands on an enemy (casting.ts HEAL-NEVER-HOSTILE), so only Attuned does;
 *   2. the Setup is the first kit Setup ability that is target-shaped in content (a circle Setup
 *      such as Hymn or Group Song resolves onto friendlies), ready and affordable;
 *   3. the partner is the first living damage-role ally (actor id order) of another class, within
 *      PAYOFF_ALLY_RANGE_MM of the healer, with no Window it could already close, with no Setup of
 *      its own ready (the healer fills the gaps in the partner's own Window chain, never takes the
 *      slot from it), and with a Payoff affordable now and off cooldown by the tick the Setup lands
 *      (landingTick + castTicks).
 *      Damage role only: it is the role that hunts Windows to close (damage.ts payoffOnWindow); a
 *      tank's Payoff closes only what sits on its own target, and its Reckoning is the party's
 *      Forge closer on Cinder's Kindled, so an Attuned on the tank's target trades Forge away
 *      (measured before the own-Setup rule: hv-k2-s7 clear 1625 -> 2226 with the tank as a partner,
 *      1764 without; 1730 with both rules);
 *   4. the target is that partner's target: a living enemy carrying no open Window (the sim's
 *      Payoff reads the target's first open Window, so an older one would shadow Attuned) and in
 *      the Setup's range.
 * Twin: game/bots/Keyrun.Bots/Roles/HealerSetup.cs.
 *
 * Usage: `const cmds = setupCommands(view, self, kit);`
 */
import type { ActorState, BotView, Command, Tick } from "../types.js";
import { getContentRegistry, resolveKit, type EffectiveKit } from "../kits.js";
import { landingTick } from "../shared/ledger.js";
import { findClosableWindow } from "../shared/windows.js";
import { WINDOW_EFFECT_PREFIX } from "../shared/pings.js";
import { affordable, readyAndAffordable } from "./shared.js";
import { PAYOFF_ALLY_RANGE_MM } from "./damage.js";

const LOCK_WINDOW_PREFIX = "lockwindow:";

function dist(a: ActorState, b: ActorState): number {
  return Math.hypot(a.pos.x - b.pos.x, a.pos.z - b.pos.z);
}

function hasOpenWindow(actor: ActorState, tick: Tick): boolean {
  return actor.effects.some(
    (e) => e.endsTick > tick && (e.effectId.startsWith(WINDOW_EFFECT_PREFIX) || e.effectId.startsWith(LOCK_WINDOW_PREFIX))
  );
}

function payoffReadyBy(view: BotView, partner: ActorState, landing: Tick): boolean {
  const kit = resolveKit(partner.classId, view.classKit);
  return kit.payoffAbilityIds.some((id) => (partner.cooldowns[id] ?? 0) <= landing && affordable(partner, id, kit));
}

function ownSetupReady(view: BotView, partner: ActorState, tick: Tick): boolean {
  const kit = resolveKit(partner.classId, view.classKit);
  return kit.setupAbilityIds.some((id) => readyAndAffordable(partner, id, tick, kit));
}

export function setupCommands(view: BotView, self: ActorState, kit: EffectiveKit): Command[] {
  const tick = view.state.tick;
  const actors = view.state.actors;
  const reg = getContentRegistry();
  if (!reg) return [];
  const setupId = kit.setupAbilityIds.find(
    (id) => reg.abilities.get(id)?.shape.kind === "target" && readyAndAffordable(self, id, tick, kit)
  );
  const setup = setupId !== undefined ? reg.abilities.get(setupId) : undefined;
  if (setupId === undefined || !setup) return [];
  const landing = landingTick(self, tick) + setup.castTicks;
  for (const partner of Object.values(actors)) {
    if (
      partner.kind !== "player" ||
      !partner.alive ||
      partner.id === self.id ||
      partner.role !== "damage" ||
      partner.classId === self.classId ||
      dist(partner, self) > PAYOFF_ALLY_RANGE_MM ||
      findClosableWindow(actors, tick, partner.classId) !== null ||
      ownSetupReady(view, partner, tick) ||
      !payoffReadyBy(view, partner, landing)
    ) {
      continue;
    }
    const target = partner.targetId !== null ? actors[partner.targetId] : undefined;
    if (!target || target.kind !== "enemy" || !target.alive || hasOpenWindow(target, tick) || dist(target, self) > setup.range) {
      continue;
    }
    return [{ tick, actorId: self.id, kind: "cast", abilityId: setupId, targetId: target.id }];
  }
  return [];
}
