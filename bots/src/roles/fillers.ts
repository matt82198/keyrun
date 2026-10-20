/**
 * bots/src/roles/fillers.ts
 *
 * BOTS-FIVE-BUTTONS (design/BIBLE.md Sec 0 test 1: "Classes with real decisions on five buttons"):
 * the filler decision. A filler is a rotational ability that costs nothing and has no cooldown
 * (Warden's Strike and Guard Break, Cinder's Ember Bolt and Flare), so the base tier order alone
 * casts the same one forever and leaves the other button dead. Among the fillers, on the cast's
 * target: a Payoff filler first when the target carries a Window this class may close (The Lock),
 * then, when the target carries no open Window at all, the Setup filler (open one); otherwise the
 * first non-Setup filler. Every other rotation entry keeps its place; the fillers fill the filler
 * positions in that preference. Mirrors game/bots/Keyrun.Bots/Roles/Fillers.cs.
 *
 * Usage: `const rotation = arrangeFillers(view, self, kit, kit.rotation, self.targetId);`
 */
import type { ActorId, ActorState, BotView } from "../types.js";
import type { EffectiveKit } from "../kits.js";
import { getContentRegistry } from "../kits.js";
import { canClose, openWindowsOn } from "../shared/windows.js";

export function arrangeFillers(
  view: BotView,
  self: ActorState,
  kit: EffectiveKit,
  rotation: readonly string[],
  targetId: ActorId | null
): string[] {
  const reg = getContentRegistry();
  if (!reg || targetId === null) return [...rotation];
  const fillers = rotation.filter((id) => (kit.costByAbility[id] ?? 0) === 0 && reg.abilities.get(id)?.cooldownTicks === 0);
  if (fillers.length < 2) return [...rotation];

  const windows = openWindowsOn(view.state.actors, view.state.tick).filter((w) => w.targetId === targetId);
  const closable = windows.some((w) => canClose(w.setupClass, self.classId));
  const isSetup = (id: string) => kit.windowIdBySetupAbility[id] !== undefined;

  const preferred =
    (closable ? fillers.find((id) => kit.payoffAbilityIds.includes(id)) : undefined) ??
    (windows.length === 0 ? fillers.find(isSetup) : fillers.find((id) => !isSetup(id))) ??
    fillers[0]!;
  const ordered = [preferred, ...fillers.filter((id) => id !== preferred)];

  let next = 0;
  return rotation.map((id) => (fillers.includes(id) ? ordered[next++]! : id));
}
