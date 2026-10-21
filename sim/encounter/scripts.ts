/**
 * sim/encounter/scripts.ts — enemy behaviour as data-driven state machines.
 *
 * An EnemyScript is a flat list of steps. `at` is one of:
 *   - `"loop"`: repeats every `every` ticks, first firing at tick `every` (not tick 0).
 *   - a number with no `every`: fires once, at that absolute tick.
 *   - a number with `every`: fires at `at`, `at+every`, `at+2*every`, ... indefinitely (a phase
 *     offset onto a loop — e.g. a mechanic that starts 10s into a 20s cadence, design/BIBLE.md §9's
 *     Pair pull). Nothing in this file bounds "phase end": the effective end is whatever stops the
 *     tick loop from calling `stepEnemyScripts` for this actor at all (pull clear, run end) or the
 *     actor's own death (see below) — exactly how an unbounded `"loop"` step already ends today.
 *   - `"onDeath"`: fires once, the first tick this function is called with that actor already dead
 *     (`alive === false`). Dead-actor gating is per-step here, not per-script, so an `"onDeath"`
 *     step is the one kind of step that still evaluates once its actor has died.
 *
 * `stepEnemyScripts` is called once per tick with the live SimState and a map of enemy actorId ->
 * EnemyScript. It decides which enemy casts what this tick (returned as `Command[]`, kind "cast")
 * and, for steps carrying a telegraph, emits the telegraph's SimEvent `leadTicks` ticks BEFORE the
 * matching cast fires, with `resolvesTick` set to the tick the cast itself lands on. For an
 * `"onDeath"` step the death tick isn't knowable in advance (death is an outcome, not a schedule),
 * so its telegraph — if any — is emitted on the SAME tick the onDeath cast fires, resolving
 * `leadTicks` later, rather than being predicted ahead of time. Origin/yaw are snapshotted from the
 * casting actor's current position at telegraph-emit time (telegraphs lock in when announced, not
 * when they resolve). A missing actor never casts.
 *
 * ONE-TICK LAG on `"onDeath"` (deterministic ordering, sim/index.ts, out of scope for this file):
 * `stepRun` calls `stepEncounter` (which calls this function) BEFORE it hands commands to
 * sim/combat for the same tick, so the state this function sees never reflects a death that
 * happens on `state.tick` itself — only one whose `alive: false` was already written by a prior
 * tick's combat resolution. The rule this file guarantees: an `"onDeath"` step fires on the first
 * `stepEnemyScripts` call whose `state.actors[id].alive` is false, which is exactly the tick AFTER
 * the actor's "death" SimEvent's own `tick` field — never the same tick, and never more than once.
 *
 * FIRE-ONCE CONTRACT: since ActorState carries no death tick and this function is otherwise a pure
 * function of its arguments, "fires once" for `"onDeath"` is a contract the CALLER upholds by
 * threading `firedOnDeath` (this call's third argument) forward from the previous call's result;
 * sim/encounter/index.ts's `stepEncounter` does (and rules.ts's DEATH-CAST EXCEPTION resolves it).
 *
 * TARGETING (the P0 fix; full rules in sim/encounter/script-target.ts): every cast carries a
 * `targetId` from `resolveScriptTarget`; the 4th parameter (`abilities`) unlocks its range/shape-aware
 * ruleset. A `blocked` resolution skips the command that tick.
 *
 * CHASE: with `abilities`, commands also carry chase.ts's `move`s closing a melee enemy on its target.
 *
 * BUSY CASTER (STARTCAST-BUSY, sim/encounter/deferred-casts.ts): a step that fires while its caster is
 * busy is held (5th argument / `deferredCasts` result) and issued the first tick the caster is free.
 *
 * Usage: `const { commands, telegraphEvents, firedOnDeath, deferredCasts } =
 *   stepEnemyScripts(state, scripts, encounter.firedOnDeath, content.abilities, encounter.deferredCasts);`
 */
import type { ActorId, ActorState, Command, SimEvent, SimState, Tick } from "./types";
import type { AbilityDef } from "../combat/abilities";
import { resolveScriptTarget, type ScriptTargetOverride } from "./script-target";
import { chaseCommands } from "./chase";
import { casterGate, type DeferredCast } from "./deferred-casts";

export interface TelegraphSpec {
  shape: "circle" | "cone" | "line";
  radius?: number;
  angle1024?: number;
  length?: number;
  width?: number;
  leadTicks: number;
}

export interface ScriptStep {
  at: "loop" | "onDeath" | number;
  every?: number;
  cast: string;
  telegraph?: TelegraphSpec;
  /** Overrides the default target-selection rule for this step's cast. See script-target.ts. */
  target?: ScriptTargetOverride;
}

export type EnemyScript = ScriptStep[];

function firesAt(step: ScriptStep, tick: Tick): boolean {
  if (step.at === "loop") {
    const every = step.every ?? 0;
    return every > 0 && tick > 0 && tick % every === 0;
  }
  if (step.at === "onDeath") return false; // handled directly in stepEnemyScripts
  const at = step.at;
  if (step.every !== undefined && step.every > 0) {
    return tick >= at && (tick - at) % step.every === 0;
  }
  return tick === at;
}

function telegraphEventAt(tick: Tick, actorId: ActorId, actor: ActorState, cast: string, tg: TelegraphSpec): SimEvent {
  const event: SimEvent = {
    tick,
    type: "telegraph",
    actorId,
    cast,
    shape: tg.shape,
    leadTicks: tg.leadTicks,
    resolvesTick: tick + tg.leadTicks,
    origin: actor.pos,
    yaw: actor.yaw,
  };
  if (tg.radius !== undefined) event.radius = tg.radius;
  if (tg.angle1024 !== undefined) event.angle1024 = tg.angle1024;
  if (tg.length !== undefined) event.length = tg.length;
  if (tg.width !== undefined) event.width = tg.width;
  return event;
}

export interface StepEnemyScriptsResult {
  commands: Command[];
  telegraphEvents: SimEvent[];
  /** `firedOnDeath` carried forward + any `"onDeath"` casts fired this call. See file docstring's
   * FIRE-ONCE CONTRACT: pass this back in as the next call's third argument to keep the guarantee. */
  firedOnDeath: Record<ActorId, string[]>;
  /** STARTCAST-BUSY: scripted casts still held for a busy caster; pass back in as the 5th argument. */
  deferredCasts: DeferredCast[];
}

function castCommand(
  state: SimState, actorId: ActorId, actor: ActorState, step: ScriptStep, abilities: Record<string, AbilityDef>,
): Command | null {
  const ability = abilities[step.cast];
  const { targetId, blocked } = resolveScriptTarget({
    state, caster: actor,
    ...(step.target !== undefined ? { override: step.target } : {}),
    ...(ability !== undefined ? { ability } : {}),
  });
  if (blocked) return null;
  return { tick: state.tick, actorId, kind: "cast", abilityId: step.cast, ...(targetId !== undefined ? { targetId } : {}) };
}

export function stepEnemyScripts(
  state: SimState,
  scripts: Record<ActorId, EnemyScript>,
  firedOnDeath: Record<ActorId, readonly string[]> = {},
  abilities: Record<string, AbilityDef> = {},
  deferredCasts: readonly DeferredCast[] = [],
): StepEnemyScriptsResult {
  const tick = state.tick;
  const commands: Command[] = [];
  const telegraphEvents: SimEvent[] = [];
  const nextFired: Record<ActorId, string[]> = {};
  for (const key of Object.keys(firedOnDeath)) {
    nextFired[Number(key)] = [...(firedOnDeath[Number(key)] ?? [])];
  }
  const nextDeferred: DeferredCast[] = [];

  for (const key of Object.keys(scripts)) {
    const actorId = Number(key);
    const script = scripts[actorId];
    const actor = state.actors[actorId];
    if (!script || !actor) continue;

    // STARTCAST-BUSY (deferred-casts.ts): held casts are issued or re-held here; fire() gates this tick's steps.
    const fire = casterGate(actorId, actor, tick, abilities, deferredCasts, nextDeferred, (step, redraw) => {
      const cmd = castCommand(state, actorId, actor, step, abilities);
      if (!cmd) return false;
      commands.push(cmd);
      if (redraw && step.telegraph) telegraphEvents.push(telegraphEventAt(tick, actorId, actor, step.cast, step.telegraph));
      return true;
    });

    for (const step of script) {
      if (step.at === "onDeath") {
        if (actor.alive) continue;
        const fired = nextFired[actorId] ?? (nextFired[actorId] = []);
        if (fired.includes(step.cast)) continue;
        fired.push(step.cast);
        const cmd = castCommand(state, actorId, actor, step, abilities);
        if (cmd) {
          commands.push(cmd);
          if (step.telegraph) telegraphEvents.push(telegraphEventAt(tick, actorId, actor, step.cast, step.telegraph));
        }
        continue;
      }

      if (!actor.alive) continue;
      if (firesAt(step, tick)) fire(step);
      const tg = step.telegraph;
      if (tg && firesAt(step, tick + tg.leadTicks)) {
        telegraphEvents.push(telegraphEventAt(tick, actorId, actor, step.cast, tg));
      }
    }
  }

  commands.push(...chaseCommands(state, scripts, abilities));

  return { commands, telegraphEvents, firedOnDeath: nextFired, deferredCasts: nextDeferred };
}
