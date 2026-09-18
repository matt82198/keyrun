/**
 * sim/encounter/breakbar.ts — a shared bar drained by control/interrupt-tagged inputs.
 *
 * `drainBreakBar` applies this tick's tagged inputs (only "control" or "interrupt") in order; the
 * instant current hits 0 it emits exactly one "stagger" event and locks the bar (immune to further
 * drain) until `cooldownReadyTick`. `tickBreakBarReset` is called once per tick per bar to refill it
 * to max the tick the cooldown elapses. Two-function split keeps drain (event-producing, tick-scoped
 * inputs) and reset (time-only) independently testable.
 *
 * Usage: `bar = drainBreakBar(bar, "twins", inputsThisTick, tick, emit); bar = tickBreakBarReset(bar, tick);`
 *
 * CONTENT WIRING (break-bar-wiring, BIBLE.md Sec 9 "The Sundered Twins"): the rest of this file
 * turns `EnemyDef.breakBarId`/`breakBars` (content/schema/enemy.ts, already fully authored on
 * `hollowWarden.boss`/`twinA`/`twinB` in content/data/enemies/hollow-warden.json -- the Sundered
 * Twins are the boss's shared-add pair, not content/data/enemies/pair.json's unrelated "Pair"
 * trash pull) into real `BreakBarInput`s and effect patches, without sim importing content
 * (sim/CLAUDE.md) -- `BreakBarSource` is a local, minimal shape content's richer `BreakBarDef`
 * satisfies via a caller-side adapter, exactly the pattern sim/encounter/pulls.ts's `DungeonDef`
 * already establishes for the same reason.
 *
 * `deriveBreakBarInputs` is a deliberate simplification: it drains off a tagged "cast" COMMAND's
 * declared target this tick (not a confirmed hit/resolution), because re-deriving range/cooldown/
 * GCD validity here would duplicate sim/combat's job. `breakBarEffectPatches` turns a just-emptied
 * bar's `onEmpty` into data the caller applies to ActorState after combat resolves (this file, like
 * sim/encounter/index.ts, never mutates ActorState itself) -- only "applyEffect" onEmpty ops are
 * represented; other EffectOp kinds are unsupported here, same spirit as the documented
 * removeEffect-wildcard gap (content/CLAUDE.md), since no shipped content uses them.
 */
import type { ActorId, Command, SimEvent, Tick } from "./types";

export interface BreakBarState {
  current: number;
  max: number;
  actorIds: ActorId[];
  cooldownReadyTick: Tick;
}

export interface BreakBarInput {
  tick: Tick;
  actorId: ActorId;
  tag: "control" | "interrupt";
  amount: number;
}

// TUNE: 200 ticks = 10s at 20Hz before a drained bar can refill and be drained again.
export const RESET_COOLDOWN_TICKS = 200;

export function createBreakBar(max: number, actorIds: ActorId[]): BreakBarState {
  return { current: max, max, actorIds, cooldownReadyTick: 0 };
}

export function drainBreakBar(
  bar: BreakBarState,
  barId: string,
  inputs: BreakBarInput[],
  tick: Tick,
  emit: (event: SimEvent) => void
): BreakBarState {
  if (bar.current <= 0) return bar; // already staggered, waiting on reset
  let current = bar.current;
  for (const input of inputs) {
    if (input.tick !== tick) continue;
    if (input.tag !== "control" && input.tag !== "interrupt") continue;
    if (current <= 0) break;
    current -= input.amount;
  }
  if (current > 0) return { ...bar, current };
  emit({ tick, type: "stagger", barId, actorIds: bar.actorIds });
  return { current: 0, max: bar.max, actorIds: bar.actorIds, cooldownReadyTick: tick + RESET_COOLDOWN_TICKS };
}

export function tickBreakBarReset(bar: BreakBarState, tick: Tick): BreakBarState {
  const isStaggered = bar.current <= 0 && bar.cooldownReadyTick > 0;
  if (isStaggered && tick >= bar.cooldownReadyTick) {
    return { current: bar.max, max: bar.max, actorIds: bar.actorIds, cooldownReadyTick: 0 };
  }
  return bar;
}

/** onEmpty effect, minimal shape (see file docstring): only "applyEffect" ops are represented. */
export interface BreakBarOnEmptyEffect {
  effectId: string;
  durationTicks: Tick;
  stacks: number;
}

/** Sim-local, content-agnostic definition of a shared Break bar and which actors carry it. */
export interface BreakBarSource {
  id: string;
  actorIds: ActorId[];
  max: number;
  drainPerInterrupt: number;
  drainPerControl: number;
  onEmpty: BreakBarOnEmptyEffect[];
}

/** A patch the caller applies to `state.actors[actorId].effects` once combat has resolved this
 * tick (via sim/combat/effects.ts's real `applyEffectInstance`) -- see file docstring. */
export interface BreakBarEffectPatch {
  actorId: ActorId;
  effectId: string;
  durationTicks: Tick;
  stacks: number;
}

/**
 * Turns this tick's submitted `commands` into `BreakBarInput`s for one `source`: a "cast" command
 * whose ability is tagged "interrupt" or "control" (interrupt takes priority when an ability -- e.g.
 * warden.shieldBash -- carries both) and whose resolved target (the command's own `targetId`, else
 * the caster's current `targetId`) is one of `source.actorIds` drains that source by
 * drainPerInterrupt/drainPerControl. See file docstring for why this reads declared intent rather
 * than confirmed hit.
 */
export function deriveBreakBarInputs(
  commands: Command[],
  actors: Record<ActorId, { targetId: ActorId | null } | undefined>,
  abilityTags: Record<string, string[] | undefined>,
  source: BreakBarSource
): BreakBarInput[] {
  const barActors = new Set(source.actorIds);
  const inputs: BreakBarInput[] = [];
  for (const cmd of commands) {
    if (cmd.kind !== "cast") continue;
    const tags = abilityTags[cmd.abilityId];
    if (!tags) continue;
    const isInterrupt = tags.includes("interrupt");
    const isControl = tags.includes("control");
    if (!isInterrupt && !isControl) continue;
    const targetId = cmd.targetId ?? actors[cmd.actorId]?.targetId ?? null;
    if (targetId === null || !barActors.has(targetId)) continue;
    const tag: BreakBarInput["tag"] = isInterrupt ? "interrupt" : "control";
    const amount = isInterrupt ? source.drainPerInterrupt : source.drainPerControl;
    inputs.push({ tick: cmd.tick, actorId: cmd.actorId, tag, amount });
  }
  return inputs;
}

/** Builds the onEmpty effect patches for a bar that just staggered this call (`justStaggered`
 * true); [] otherwise, including for a bar that was already staggered coming in. */
export function breakBarEffectPatches(source: BreakBarSource, justStaggered: boolean): BreakBarEffectPatch[] {
  if (!justStaggered) return [];
  const patches: BreakBarEffectPatch[] = [];
  for (const actorId of source.actorIds) {
    for (const eff of source.onEmpty) {
      patches.push({ actorId, effectId: eff.effectId, durationTicks: eff.durationTicks, stacks: eff.stacks });
    }
  }
  return patches;
}
