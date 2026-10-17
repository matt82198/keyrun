/**
 * client/src/render/ability-bar-model.ts
 *
 * Pure (DOM-free) model behind render/hud.ts's ability bar: for each ability keybind slot, which
 * ability it shows, its display name, how many ticks of cooldown remain (and what fraction of the
 * ability's own cooldown that is), and whether the global cooldown is holding it. Cooldowns arrive
 * keyed by ABILITY id (sim ActorState.cooldowns via sim-host/snapshot-codec.ts), so a slot reads its
 * ability id from the same input/slot-abilities.ts map the cast path uses -- never the slot label.
 *
 * `info` (sim-host/display-info.ts, from the host's ready message) supplies names and timings; when a
 * host reports none, names fall back to the humanized id and the sweep to the slot's observed peak
 * (render/hud.ts). `gcdRemainingTicks` is ActorSnapshot.gcdRemainingTicks: a slot whose ability has
 * gcdTicks > 0 is `gcdActive` while it is positive; off-GCD abilities (gcdTicks 0) never are.
 *
 * Usage: `abilityBarModel(slotAbilities, player.cooldowns, info, player.gcdRemainingTicks)`
 */
import { ABILITY_ACTIONS, type AbilityAction, type SlotAbilities } from "../input/slot-abilities.js";
import { abilityName, type DisplayInfo } from "../sim-host/display-info.js";

export interface AbilityBarSlot {
  action: AbilityAction;
  /** null when the host reported no slot map (nothing to sweep). */
  abilityId: string | null;
  /** Display name ("" when the slot has no ability). */
  name: string;
  remainingTicks: number;
  onCooldown: boolean;
  /** remaining / the ability's own cooldownTicks, in [0, 1]; null when the cooldown length is unknown. */
  cooldownFraction: number | null;
  gcdActive: boolean;
  /** Not on cooldown and not held by the GCD: pressing the key will try to cast. */
  ready: boolean;
  /** Ready AND in `payoffIds` (render/payoff-ready.ts): pressing it now closes an open Window. */
  payoffReady: boolean;
}

export function abilityBarModel(
  slotAbilities: SlotAbilities | undefined,
  cooldowns: Readonly<Record<string, number>>,
  info?: DisplayInfo,
  gcdRemainingTicks = 0,
  payoffIds?: ReadonlySet<string>,
): AbilityBarSlot[] {
  return ABILITY_ACTIONS.map((action) => {
    const abilityId = slotAbilities?.[action] ?? null;
    const remainingTicks = abilityId === null ? 0 : Math.max(0, cooldowns[abilityId] ?? 0);
    const display = abilityId === null ? undefined : info?.abilities[abilityId];
    const cdLen = display?.cooldownTicks ?? 0;
    const cooldownFraction = remainingTicks <= 0 ? 0 : cdLen > 0 ? Math.min(1, remainingTicks / cdLen) : null;
    const onGcd = display ? display.gcdTicks > 0 : true;
    const gcdActive = abilityId !== null && remainingTicks <= 0 && onGcd && gcdRemainingTicks > 0;
    const onCooldown = remainingTicks > 0;
    const ready = abilityId !== null && !onCooldown && !gcdActive;
    return {
      action,
      abilityId,
      name: abilityId === null ? "" : abilityName(info, abilityId),
      remainingTicks,
      onCooldown,
      cooldownFraction,
      gcdActive,
      ready,
      payoffReady: ready && abilityId !== null && payoffIds !== undefined && payoffIds.has(abilityId),
    };
  });
}
