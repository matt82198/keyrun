/**
 * bots/src/shared/floor.ts
 *
 * Hostile floor-patch avoidance (design/BIBLE.md Sec 4/5: persistent ground effects such as enemy
 * fire, or a class's own floor Window like Cinderfield), plus melee re-approach and a friendly
 * Window approach once the bot is clear.
 *
 * Contract note: `Telegraph` (types.ts) carries no "this is a persistent floor patch, not a
 * one-shot impact warning" flag and no faction/source field yet (bots/CLAUDE.md's dispatch brief:
 * real source/faction tagging lands in fix/aoe-faction). Following the same "no new field
 * invented" convention as shared/pings.ts's WINDOW_EFFECT_PREFIX and shared/windows.ts's effectId
 * prefix: a floor patch is any circle Telegraph whose `id` carries FLOOR_PATCH_PREFIX, and it
 * always carries `danger: false` -- "danger" (bot.ts's dangerAvoidCommand) means "imminent,
 * skill-dial-free reflex dodge"; a floor patch is a standing hazard handled at a lower,
 * skill-dial-gated tier by this module instead, so it never collides with bot.ts's tier. A
 * *friendly* floor Window (Bulwark/Cinderfield/Hymn -- BIBLE.md Sec 4 "Setup abilities open a
 * Window... or a floor patch") further encodes `<windowId>:<setupClass>` after
 * shared/pings.ts's own WINDOW_EFFECT_PREFIX, inline: `floor:window:<windowId>:<setupClass>` --
 * the same effectId scheme shared/windows.ts already uses, so parsing stays consistent
 * byte-for-byte. Until a real adapter emits that encoding, every floor patch is conservatively
 * hostile -- exactly the interim behaviour the backlog item calls for ("even friendly cinderfield"
 * gets avoided until fix/aoe-faction lands).
 *
 * bots/CLAUDE.md / design/BIBLE.md Sec 4: "allies must STAND IN friendly Windows to close them --
 * never flee those." So a friendly floor Window is excluded from the hazard set entirely,
 * regardless of whether this bot's own class can close it (the Lock only gates who may *close* a
 * Window, never who may stand in one -- shared/windows.ts's canClose()).
 *
 * Usage: `import { floorEscapeCommand, meleeReapproachCommand, friendlyWindowApproachCommand } from "./floor.js"`
 */
import type { ActorState, BotView, Command, Telegraph } from "../types.js";
import { isInAnyDanger, nearestSafePoint, type PointMM } from "./positioning.js";
import { moveToward } from "./command.js";
import { WINDOW_EFFECT_PREFIX } from "./pings.js";
import { canClose } from "./windows.js";

export const FLOOR_PATCH_PREFIX = "floor:";
const DEFAULT_MARGIN_MM = 500;
/** ActorState.pos is already integer mm (sim/CLAUDE.md), so this 5m design constant is converted
 * to mm once, here, never by re-scaling an already-mm position (that was positioning.ts's toMM()
 * bug, since deleted). */
const MELEE_RANGE_MM = 5 * 1000;
/** design/BIBLE.md Sec 5: Warden is the only starter melee-flavoured class; every tank is melee. */
const MELEE_CLASS_IDS = new Set(["warden"]);

function isFloorPatchTelegraph(tg: Telegraph): boolean {
  return tg.shape === "circle" && tg.id.startsWith(FLOOR_PATCH_PREFIX);
}

/** True if `tg` encodes a friendly floor Window (see file header) that must never be fled. */
function isFriendlyWindowPatch(tg: Telegraph): boolean {
  return tg.id.slice(FLOOR_PATCH_PREFIX.length).startsWith(WINDOW_EFFECT_PREFIX);
}

/** Active hostile floor patches: prefixed circles, not yet expired, not a friendly Window. */
function hostileFloorPatches(telegraphs: readonly Telegraph[], tick: number): Telegraph[] {
  return telegraphs.filter(
    (tg) => tg.resolvesTick > tick && isFloorPatchTelegraph(tg) && !isFriendlyWindowPatch(tg)
  );
}

/** `telegraphs` with each radius inflated by `marginMM`, so the escape point clears with room to
 * spare. `tg.radius` is already integer mm (sim/CLAUDE.md), so the margin is added directly --
 * dividing it by 1000 here (a leftover from when `radius` was wrongly assumed to be metres) used
 * to shrink a 500mm margin down to ~0.5mm. */
function inflated(telegraphs: Telegraph[], marginMM: number): Telegraph[] {
  return telegraphs.map((tg) => ({ ...tg, danger: true, radius: (tg.radius ?? 0) + marginMM }));
}

/** True if `self` is inside an active *ordinary* (non-floor) danger telegraph -- bot.ts's own
 * always-on tier already produces the escape move for those; this module defers to it rather than
 * emitting a second, possibly conflicting move (priority: telegraph escape > floor escape). */
function ordinaryTelegraphDangerActive(view: BotView, self: ActorState): boolean {
  const tick = view.state.tick;
  const selfMM = { x: self.pos.x, z: self.pos.z };
  const ordinary = view.telegraphs.filter((t) => t.danger && t.resolvesTick > tick && !isFloorPatchTelegraph(t));
  return isInAnyDanger(selfMM, ordinary);
}

export interface FloorEscapeOptions {
  marginMM?: number;
  /** Healer range-keeping: prefer an escape point within `maxDistMM` of `anchor`; falls back to
   * the nearest safe point outright (escaping danger always outranks staying in range) when no
   * qualifying point exists. */
  keepNear?: { anchor: PointMM; maxDistMM: number };
}

/**
 * A move command to clear the nearest hostile floor patch `self` currently stands in, or null if
 * none. The lower-priority, skill-dial-gated tier below bot.ts's own (skill-dial-free) telegraph
 * avoidance -- call from inside a role's decide() so it inherits that gating for free.
 */
export function floorEscapeCommand(view: BotView, self: ActorState, opts: FloorEscapeOptions = {}): Command | null {
  if (ordinaryTelegraphDangerActive(view, self)) return null;
  const tick = view.state.tick;
  const patches = hostileFloorPatches(view.telegraphs, tick);
  if (patches.length === 0) return null;
  const selfMM = { x: self.pos.x, z: self.pos.z };
  const danger = inflated(patches, opts.marginMM ?? DEFAULT_MARGIN_MM);
  if (!isInAnyDanger(selfMM, danger)) return null;

  let safe: PointMM | null = null;
  if (opts.keepNear) {
    const { anchor, maxDistMM } = opts.keepNear;
    safe = nearestSafePoint(selfMM, danger, 500, 20, (p) => {
      const dx = p.x - anchor.x;
      const dz = p.z - anchor.z;
      return dx * dx + dz * dz <= maxDistMM * maxDistMM;
    });
  }
  if (!safe) safe = nearestSafePoint(selfMM, danger);
  if (!safe) return null;

  return moveToward(tick, self.id, selfMM, safe);
}

function isMelee(self: ActorState): boolean {
  return self.role === "tank" || MELEE_CLASS_IDS.has(self.classId);
}

/**
 * After a floor escape leaves a melee bot's target out of range, walk back in. Null when `self`
 * isn't melee, has no alive target, is still clearing a hostile floor patch (that escape takes
 * priority), or is already close enough.
 */
export function meleeReapproachCommand(view: BotView, self: ActorState): Command | null {
  if (!isMelee(self) || self.targetId === null) return null;
  const target = view.state.actors[self.targetId];
  if (!target || !target.alive) return null;
  if (floorEscapeCommand(view, self) !== null) return null;

  const dx = target.pos.x - self.pos.x;
  const dz = target.pos.z - self.pos.z;
  const dist = Math.hypot(dx, dz);
  if (dist <= MELEE_RANGE_MM) return null;
  return moveToward(view.state.tick, self.id, self.pos, target.pos);
}

interface FriendlyWindow {
  center: PointMM;
  radiusMM: number;
  setupClass: string;
}

function parseFriendlyWindow(tg: Telegraph): FriendlyWindow | null {
  if (!isFloorPatchTelegraph(tg) || !isFriendlyWindowPatch(tg)) return null;
  const rest = tg.id.slice(FLOOR_PATCH_PREFIX.length + WINDOW_EFFECT_PREFIX.length);
  const setupClass = rest.split(":").pop();
  if (!setupClass) return null;
  return { center: { x: tg.center.x, z: tg.center.z }, radiusMM: tg.radius ?? 0, setupClass };
}

/**
 * Move toward a friendly floor Window this bot's class can close (design/BIBLE.md Sec 4: "the bot
 * stays or moves in when it can close it"), reusing shared/windows.ts's canClose() so the Lock
 * stays the single source of truth for who may close what. Null once already inside it (closing it
 * is a cast, a role concern -- not this module's) or when no closable friendly Window is present.
 */
export function friendlyWindowApproachCommand(view: BotView, self: ActorState): Command | null {
  const tick = view.state.tick;
  const selfMM = { x: self.pos.x, z: self.pos.z };
  for (const tg of view.telegraphs) {
    if (tg.resolvesTick <= tick) continue;
    const win = parseFriendlyWindow(tg);
    if (!win || !canClose(win.setupClass, self.classId)) continue;
    const dx = win.center.x - selfMM.x;
    const dz = win.center.z - selfMM.z;
    if (dx * dx + dz * dz <= win.radiusMM * win.radiusMM) return null;
    return moveToward(tick, self.id, selfMM, win.center);
  }
  return null;
}
