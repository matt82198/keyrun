/**
 * bots/src/roles/shared.ts
 *
 * Helpers shared by every role state machine (tank/healer/damage): cooldown/resource readiness
 * checks, and the kick-ledger command every role attempts first each tick. Split out of the
 * former single-file roles.ts to stay under the repo's <=190 LOC/file cap; no behaviour change.
 *
 * Usage: `import { abilityReady, readyAndAffordable, ledgerKickCommand } from "./shared.js"`
 */
import type { ActorState, BotView, Command, Tick } from "../types.js";
import { resolveInterrupt } from "../shared/ledger.js";
import { resolveKit, priorityInterruptAbilityIds, type EffectiveKit } from "../kits.js";

export function abilityReady(self: ActorState, abilityId: string, tick: Tick): boolean {
  return (self.cooldowns[abilityId] ?? 0) <= tick && self.gcdReadyTick <= tick;
}

export function affordable(self: ActorState, abilityId: string, kit: EffectiveKit): boolean {
  return self.resource >= (kit.costByAbility[abilityId] ?? 0);
}

export function readyAndAffordable(self: ActorState, abilityId: string, tick: Tick, kit: EffectiveKit): boolean {
  return abilityReady(self, abilityId, tick) && affordable(self, abilityId, kit);
}

/**
 * The kick ledger, shared by all three roles: if it is `self`'s turn to kick the party's
 * priority-cast target, return that cast command and nothing else this tick. Every alive player
 * whose resolved kit uses the same interrupt ability id as `self` is a candidate kicker, matching
 * bots/CLAUDE.md's "the bot with the lowest actorId whose kick is ready takes it" -- content's
 * enemy abilities tagged "healer" (BIBLE.md Sec 9 "The Choir") outrank every other cast.
 */
export function ledgerKickCommand(view: BotView, self: ActorState, kit: EffectiveKit): Command | null {
  const tick = view.state.tick;
  const interruptId = kit.interruptAbilityId;
  if (!interruptId || !self.alive) return null;

  const eligible = Object.values(view.state.actors)
    .filter((a) => a.kind === "player" && a.alive)
    .filter((a) => resolveKit(a.classId, view.classKit).interruptAbilityId === interruptId)
    .map((a) => a.id);

  const assignment = resolveInterrupt(view.state.actors, interruptId, eligible, tick, priorityInterruptAbilityIds());
  if (!assignment || assignment.kickerId !== self.id) return null;
  return { tick, actorId: self.id, kind: "cast", abilityId: interruptId, targetId: assignment.targetId };
}
