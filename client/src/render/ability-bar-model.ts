/**
 * client/src/render/ability-bar-model.ts
 *
 * Pure (DOM-free) model behind render/hud.ts's ability bar: for each ability keybind slot, which
 * ability it shows and how many ticks of cooldown remain. Cooldowns arrive keyed by ABILITY id
 * (sim ActorState.cooldowns via sim-host/snapshot-codec.ts), so a slot reads its ability id from
 * the same input/slot-abilities.ts map the cast path uses -- never the slot label itself.
 *
 * Usage: `abilityBarModel(slotAbilities, player.cooldowns).find((s) => s.action === "abilityE")`
 */
import { ABILITY_ACTIONS, type AbilityAction, type SlotAbilities } from "../input/slot-abilities.js";

export interface AbilityBarSlot {
  action: AbilityAction;
  /** null when the host reported no slot map (nothing to sweep). */
  abilityId: string | null;
  remainingTicks: number;
  onCooldown: boolean;
}

export function abilityBarModel(slotAbilities: SlotAbilities | undefined, cooldowns: Readonly<Record<string, number>>): AbilityBarSlot[] {
  return ABILITY_ACTIONS.map((action) => {
    const abilityId = slotAbilities?.[action] ?? null;
    const remainingTicks = abilityId === null ? 0 : Math.max(0, cooldowns[abilityId] ?? 0);
    return { action, abilityId, remainingTicks, onCooldown: remainingTicks > 0 };
  });
}
