/**
 * sim/combat/floor-telegraphs.ts
 *
 * Derives bot-consumable "floor patch" telegraph events (bots/src/shared/floor.ts's consumer
 * contract) from SimState.floorWindows -- the floor-patch Window registry sim/combat/window-state.ts
 * / sim/combat/windows.ts already populate on cast completion (Bulwark, Cinderfield, Hymn, all
 * party-sourced; seed.deathBloom, boss-sourced -- design/BIBLE.md §4/§5/§9). PR #40 taught bots to
 * flee hostile floor patches and stand in friendly Windows, keyed on a specific id encoding, but no
 * producer ever emitted that shape (the #40 merge grep proved it); this file is that producer.
 *
 * ID ENCODING -- byte-for-byte matching bots/src/shared/floor.ts's FLOOR_PATCH_PREFIX /
 * isFriendlyWindowPatch (which itself reuses bots/src/shared/pings.ts's WINDOW_EFFECT_PREFIX
 * convention). Every floor patch's id starts with "floor:" (FLOOR_PATCH_PREFIX). A FRIENDLY floor
 * Window -- opened by a *player* actor (Bulwark/Cinderfield/Hymn today) -- additionally nests
 * "window:<windowId>:<setupClass>" after that prefix, exactly sim/combat/window-state.ts's own
 * windowEffectId() encoding ("window:" + windowId + ":" + setupClass), so a friendly floor id
 * reads "floor:window:<windowId>:<setupClass>". A HOSTILE floor patch -- opened by an *enemy*
 * actor (seed.deathBloom today, setupClass "boss"), or one whose setup actor can no longer be
 * found -- fails closed to hostile (the same default sim/combat/affinity.ts uses for a signal it
 * cannot positively classify) -- carries just "floor:<windowId>", no nested "window:" segment, so
 * isFriendlyWindowPatch (which tests for that nested prefix) correctly reads it as hostile.
 * `danger` is always false on every floor-patch Telegraph (bots/src/shared/floor.ts's own
 * contract note: "danger" means bot.ts's always-on reflex-dodge tier; a floor patch is handled at
 * this module's own lower, skill-dial-gated tier instead).
 *
 * UNITS -- `FloorWindow.pos`/`radiusMm` are raw integer millimetres, the same units
 * ActorState.pos already carries (sim/CLAUDE.md: "Positions in millimetres (int)... Integer math
 * only"). This file passes them straight through, UNconverted, into the emitted Telegraph's
 * `center`/`radius` fields -- even though bots/src/shared/positioning.ts's toMM() doc comment
 * describes its input as "sim-space (metres, float)". That comment predates any real producer
 * (this file is the first) and does not match sim's actual mm-int state; but toMM() ends up
 * applied uniformly to BOTH a Telegraph's center/radius AND an actor's raw pos right before every
 * geometric comparison (bots/src/shared/positioning.ts's inTelegraph/isInAnyDanger), so the
 * point-in-circle test is scale-invariant under that shared *1000 factor as long as both operands
 * start in the SAME unit -- which raw-mm passthrough guarantees against the real ActorState.pos
 * values a bot actually reads. Converting to metres here instead would silently break every real
 * comparison by 1000x while still passing bots' own existing unit tests (which hand-author small
 * toy position numbers, not real sim state). See tests/sim/integration/floor-telegraph.test.ts and
 * the bots floor-telegraph loop test, which both drive real mm-scale content and prove the
 * geometry lines up. One pre-existing wrinkle this does NOT fix (out of this lane's scope):
 * bots/src/shared/floor.ts's own `inflated()` helper adds a raw `marginMM / 1000` safety margin to
 * `tg.radius` before toMM doubles it again, so in practice that margin lands as a ~0.5mm buffer
 * instead of the intended 500mm -- a floor.ts-internal quirk, not an id-format mismatch, so left
 * untouched per this lane's brief (prefer matching the producer to the existing consumer).
 *
 * Usage: `const telegraphs = deriveFloorTelegraphs(state);` -- called once per tick by whatever
 * builds a bot's telegraph list. sim/index.ts (out of this lane's edit scope) exposes no BotView
 * today -- the one-line wiring a future bots-loop caller needs is: merge
 * `deriveFloorTelegraphs(run.state)` into that BotView's `telegraphs` array alongside any boss
 * telegraphs sim/encounter produces.
 */
import type { FloorWindow, SimState } from "./types";

export const FLOOR_PATCH_PREFIX = "floor:";
const WINDOW_NEST_PREFIX = "window:";

/**
 * Structurally matches bots/src/types.ts's `Telegraph` (circle-only, since every floor patch is a
 * circle). sim never imports from bots/ (sim/CLAUDE.md), so this is a local shape rather than an
 * import -- the same convention bots/src/types.ts itself documents for its own local copies of
 * sim-facing concepts.
 */
export interface FloorTelegraph {
  id: string;
  shape: "circle";
  center: { x: number; z: number };
  radius: number;
  resolvesTick: number;
  danger: false;
}

/**
 * True if the actor that opened `fw` is a live party member. Fails closed to hostile (false) if
 * the setup actor can no longer be found (already dead and pruned, or a bad reference) -- the same
 * fail-closed-hostile default sim/combat/affinity.ts uses for a signal it cannot positively
 * classify friendly.
 */
export function isFriendlyFloorWindow(state: SimState, fw: FloorWindow): boolean {
  return state.actors[fw.setupActorId]?.kind === "player";
}

/** The exact bots/src/shared/floor.ts id for `fw`; see file docstring for the full encoding. */
export function floorTelegraphId(fw: FloorWindow, friendly: boolean): string {
  return friendly
    ? `${FLOOR_PATCH_PREFIX}${WINDOW_NEST_PREFIX}${fw.windowId}:${fw.setupClass}`
    : `${FLOOR_PATCH_PREFIX}${fw.windowId}`;
}

function toFloorTelegraph(state: SimState, fw: FloorWindow): FloorTelegraph {
  return {
    id: floorTelegraphId(fw, isFriendlyFloorWindow(state, fw)),
    shape: "circle",
    center: { x: fw.pos.x, z: fw.pos.z },
    radius: fw.radiusMm,
    resolvesTick: fw.endsTick,
    danger: false,
  };
}

/**
 * Every currently-open floor patch (SimState.floorWindows) as a bots-consumable Telegraph
 * snapshot, cheap enough to call every tick: no allocation beyond the output array, one actor
 * lookup per patch. Excludes patches whose endsTick has already passed -- the same
 * `resolvesTick > tick` filter bots/src/shared/floor.ts's hostileFloorPatches/
 * friendlyWindowApproachCommand apply on their own end; filtering here too keeps a caller that
 * skips checkFloorExpiries's per-tick cleanup (e.g. a hand-built SimState in a test) from leaking
 * an expired patch to consumers.
 */
export function deriveFloorTelegraphs(state: SimState): FloorTelegraph[] {
  const patches = state.floorWindows;
  if (!patches) return [];
  const out: FloorTelegraph[] = [];
  for (const fw of Object.values(patches)) {
    if (fw.endsTick <= state.tick) continue;
    out.push(toFloorTelegraph(state, fw));
  }
  return out;
}

/**
 * Single-patch version for sim/combat/windows.ts's openWindow to enrich its "window_opened" emit
 * for the onTarget:false (floor) branch with the same telegraph fields, without a second state
 * read -- the "emitted once when opened" half of this producer; deriveFloorTelegraphs above is the
 * "cheap per-tick presence list" half.
 */
export function floorWindowOpenedTelegraph(state: SimState, fw: FloorWindow): FloorTelegraph {
  return toFloorTelegraph(state, fw);
}
