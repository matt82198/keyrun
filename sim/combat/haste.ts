/**
 * Cast time under haste (ruling R-HASTE-1: haste from every live effect shortens casts, as Bloodlust does in WoW).
 * Mirrors C# Casting.HastedCastTicks (game/sim/Keyrun.Sim/Combat/Casting.cs) exactly: a cast takes
 * castTicks * 10000 / (10000 + hasteBp) ticks, floored, with hasteBp summed over the caster's effects whose
 * endsTick > now (each times its stacks). BIBLE s5 Chill (-3000 bp makes 20 ticks 28) and the Anthem `hasted`
 * effect (+1500 bp makes 20 ticks 17). An instant stays instant; no effects map, zero haste, or a total at or below
 * -10000 leaves the content cast time unchanged.
 *
 * Usage: actor.castEndsTick = now + hastedCastTicks(actor, def.castTicks, effects, now);
 */
import type { ActorState, Tick } from "./types";
import type { EffectDef } from "./effects";

const BP_DENOM = 10000;

export function hastedCastTicks(
  actor: ActorState,
  castTicks: number,
  effects: Record<string, EffectDef> | undefined,
  now: Tick,
): number {
  if (!effects || castTicks <= 0) return castTicks;
  let haste = 0;
  for (const inst of actor.effects) {
    const def = inst.endsTick > now ? effects[inst.effectId] : undefined;
    if (def) haste += (def.modifiers.hasteBp ?? 0) * inst.stacks;
  }
  if (haste === 0 || BP_DENOM + haste <= 0) return castTicks;
  return Math.floor((castTicks * BP_DENOM) / (BP_DENOM + haste));
}
