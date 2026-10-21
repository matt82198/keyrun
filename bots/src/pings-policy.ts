/**
 * bots/src/pings-policy.ts
 *
 * Deterministic reactions to party G-pings (design/BIBLE.md §2, §10),
 * layered alongside roles.ts's role state machines. NOT wired into
 * bot.ts's runBots() by this lane -- roles.ts and bot.ts are out of
 * scope for this backlog item (see keyrun-wt/pings dispatch brief);
 * pingReactionCommands() is the integration point a follow-up lane calls
 * from decideForActor() alongside the existing role dispatch.
 *
 * Reactions, each pure and skill-dial-free (pings are urgent party calls,
 * not rotation -- no reaction delay or mistake roll applied here):
 *
 *  - Target ping on an enemy mid-cast: the damage bot with the lowest
 *    actorId whose interrupt is ready kicks the pinged enemy specifically
 *    (same lowest-actorId rule as roles.ts's isInterruptResponsible; this
 *    layer additionally lets a ping choose *which* casting enemy to kick
 *    when more than one is casting).
 *  - Spot ping: healer/damage bots move toward the pinged point (the
 *    tank holds position/threat facing per tankDecide). "Within N ticks"
 *    falls out of calling this once per tick while the ping stays live --
 *    see tests/bots/pings.test.ts for the arrival simulation.
 *  - Target ping on a Window carrier: a payoff-capable bot (one whose
 *    class rotation contains a Payoff ability -- local convention: an
 *    ability id prefixed "payoff:", since classKit carries no explicit
 *    Payoff flag) casts its payoff on the pinged target ahead of its
 *    normal rotation pick.
 *
 * Priority when more than one applies: interrupt > Window payoff > spot
 * move, matching damageDecide's own interrupt-first ordering in roles.ts.
 *
 * Usage: `import { pingReactionCommands } from "./pings-policy.js"`
 */
import type { ActorId, ActorState, BotView, Command, Tick } from "./types.js";
import { abilityReady } from "./roles.js";
import { resolveKit } from "./kits.js";
import { isInterruptResponsible } from "./shared/priority.js";
import { moveToward } from "./shared/command.js";
import { isPingLive, latestLivePing, WINDOW_EFFECT_PREFIX, type PingEvent } from "./shared/pings.js";

/** Local convention: a class's Payoff ability id is prefixed this way in classKit.rotation. See file header. */
export const PAYOFF_ABILITY_PREFIX = "payoff:";

/** Ticks a healer/damage bot has to close on a spot ping (2s @ 20Hz) before it stops issuing move commands. */
export const PING_MOVE_TICKS = 40;

/** Once within this many mm of a spot ping's point, a bot is considered "there". */
const SPOT_ARRIVE_MM = 500;

function firstEnemyWithCast(actors: Record<ActorId, ActorState>, targetId: ActorId): ActorState | null {
  const target = actors[targetId];
  if (!target || !target.alive || target.kind !== "enemy" || target.castId === null) return null;
  return target;
}

/**
 * A ping on a casting enemy: the lowest-actorId damage bot with its
 * interrupt ready kicks the pinged enemy. Empty if no live target ping
 * names a casting enemy, or `self` is not the responsible bot.
 */
export function interruptPingCommands(view: BotView, self: ActorState, pings: readonly PingEvent[]): Command[] {
  const tick: Tick = view.state.tick;
  const interruptId = view.classKit.interruptAbilityId;
  if (!interruptId || self.role !== "damage" || !self.alive) return [];

  const ping = latestLivePing(pings, tick, undefined, "target");
  if (!ping || ping.targetId === undefined) return [];
  const target = firstEnemyWithCast(view.state.actors, ping.targetId);
  if (!target) return [];

  const selfKit = resolveKit(self.classId, view.classKit);
  const eligible = Object.values(view.state.actors)
    .filter((a) => a.kind === "player" && a.alive && a.role === "damage")
    .filter((a) => abilityReady(a, interruptId, tick, selfKit))
    .map((a) => a.id);
  if (!abilityReady(self, interruptId, tick, selfKit) || !isInterruptResponsible(self.id, eligible)) return [];

  return [{ tick, actorId: self.id, kind: "cast", abilityId: interruptId, targetId: target.id }];
}

/**
 * A ping on a Window carrier: a payoff-capable bot targets and casts its
 * payoff ability on the pinged target, ahead of its normal rotation pick.
 * Empty if this class has no payoff ability, no live target ping names a
 * Window carrier, or the payoff is not yet ready.
 */
export function windowPingPayoffCommands(view: BotView, self: ActorState, pings: readonly PingEvent[]): Command[] {
  const tick: Tick = view.state.tick;
  if (!self.alive) return [];
  const payoffId = view.classKit.rotation.find((id) => id.startsWith(PAYOFF_ABILITY_PREFIX));
  if (!payoffId) return [];

  const ping = latestLivePing(pings, tick, undefined, "target");
  if (!ping || ping.targetId === undefined) return [];
  const target = view.state.actors[ping.targetId];
  if (!target || !target.alive) return [];
  const hasWindow = target.effects.some((e) => e.effectId.startsWith(WINDOW_EFFECT_PREFIX));
  if (!hasWindow || !abilityReady(self, payoffId, tick)) return [];

  const commands: Command[] = [];
  if (self.targetId !== target.id) {
    commands.push({ tick, actorId: self.id, kind: "target", targetId: target.id });
  }
  commands.push({ tick, actorId: self.id, kind: "cast", abilityId: payoffId, targetId: target.id });
  return commands;
}

/**
 * A spot ping: healer/damage bots move toward the pinged point until
 * within SPOT_ARRIVE_MM of it. Empty for the tank (holds position/threat
 * facing), once arrived, or once the ping is no longer live.
 */
export function spotPingMoveCommands(view: BotView, self: ActorState, pings: readonly PingEvent[]): Command[] {
  const tick: Tick = view.state.tick;
  if (!self.alive || (self.role !== "healer" && self.role !== "damage")) return [];

  const ping = latestLivePing(pings, tick, PING_MOVE_TICKS, "spot");
  if (!ping || !ping.point) return [];

  const dx = ping.point.x - self.pos.x;
  const dz = ping.point.z - self.pos.z;
  const distMM = Math.hypot(dx, dz);
  if (distMM <= SPOT_ARRIVE_MM) return [];

  const move = moveToward(tick, self.id, self.pos, ping.point);
  return move ? [move] : [];
}

/** Combines the three reactions above in priority order: interrupt > Window payoff > spot move. */
export function pingReactionCommands(view: BotView, self: ActorState, pings: readonly PingEvent[]): Command[] {
  const interrupt = interruptPingCommands(view, self, pings);
  if (interrupt.length > 0) return interrupt;

  const payoff = windowPingPayoffCommands(view, self, pings);
  if (payoff.length > 0) return payoff;

  return spotPingMoveCommands(view, self, pings);
}
