/**
 * tests/sim/balance/human-warden.ts
 *
 * The scripted "competent human" Warden for the key-2 survivability metric. It plays the tank's
 * job the way bots/src/roles/tank.ts describes it (taunt loose enemies, hold threat, Brace/Vigil
 * under pressure, kick the priority cast), but at HUMAN speed rather than bot speed:
 *   - it never makes "mistakes" (competent), but
 *   - it presses at most one ability per reaction window (`reactionTicks`, 250 ms of wall time at
 *     the run's hz -- 15 ticks at 60 Hz), and
 *   - it only starts dodging a danger telegraph once that telegraph has been visible for
 *     `reactionTicks` (a human sees the decal, then moves). Steering toward a target is continuous,
 *     as a held key is.
 * Bots react to danger on the first tick; this human does not. That is the whole difference.
 *
 * Usage: `const human = makeHumanWarden(humanReactionTicks()); const cmds = human.decide(view, actorId);`
 */
import type { ActorId, BotView, Command, Telegraph } from "@bots/src/types";
import { tankDecide } from "@bots/src/roles";
import { isInAnyDanger, nearestSafePoint } from "@bots/src/shared/positioning";
import { moveToward } from "@bots/src/shared/command";

export interface HumanWarden {
  decide(view: BotView, actorId: ActorId): Command[];
}

export function makeHumanWarden(reactionTicks: number): HumanWarden {
  const firstSeen = new Map<string, number>();
  let nextPressTick = 0;

  function seen(view: BotView): Telegraph[] {
    const tick = view.state.tick;
    const live = view.telegraphs.filter((t) => t.danger && t.resolvesTick > tick);
    for (const t of live) if (!firstSeen.has(t.id)) firstSeen.set(t.id, tick);
    return live.filter((t) => tick - (firstSeen.get(t.id) ?? tick) >= reactionTicks);
  }

  return {
    decide(view: BotView, actorId: ActorId): Command[] {
      const self = view.state.actors[actorId];
      if (!self || !self.alive) return [];
      const tick = view.state.tick;
      const out: Command[] = [];
      const noticed = seen(view);
      const pos = { x: self.pos.x, z: self.pos.z };
      if (isInAnyDanger(pos, noticed)) {
        const safe = nearestSafePoint(pos, noticed);
        const step = safe ? moveToward(tick, self.id, pos, safe) : null;
        if (step) out.push(step);
      }
      for (const c of tankDecide(view, self)) {
        if (c.kind === "move" && out.some((o) => o.kind === "move")) continue; // dodging wins the keys
        if (c.kind === "cast") {
          if (tick < nextPressTick) continue;
          nextPressTick = tick + reactionTicks;
        }
        out.push(c);
      }
      return out;
    },
  };
}
