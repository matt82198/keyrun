/**
 * client/src/render/status-chips.ts
 *
 * Pure (Three-free) model for item 5's status readability: ActorSnapshot.effects (the item-5 status
 * channel, sim-host/snapshot-codec.ts toEffects) -> small labelled chips ("STUN 1.0s") drawn above
 * the health bar, plus the one-word status tag render/actors.ts puts on the name tag's second line
 * ("STUNNED" / "TAUNTED" / "BRACED"). Colours: crowd control red, damage-over-time orange, slows and
 * chill ice blue, mitigation gold, buffs green, encounter marks in their telegraph colours.
 * Also the Downed (item 12) colour. Display only -- it never decides a rule.
 *
 * Usage: `const chips = statusChips(actor.effects); const tag = statusTag(actor.effects, taunted);`
 */
import type { ActorEffect } from "../sim-host/snapshot.js";
import { TICK_HZ } from "../sim-host/snapshot.js";
import { humanizeId } from "../sim-host/display-info.js";

export interface StatusChip {
  id: string;
  label: string;
  /** "<LABEL> <seconds>s", one decimal. */
  text: string;
  color: string;
}

const CC = "#ff5a5a";
const DOT = "#ff9a3c";
const ICE = "#8fd6ff";
const GUARD = "#ffd166";
const BUFF = "#7dffb0";

const CHIP: Record<string, [string, string]> = {
  stunned: ["STUN", CC], silenced: ["SILENCE", CC], blinded: ["BLIND", CC],
  slowed: ["SLOW", ICE], chill: ["CHILL", ICE], burning: ["BURN", DOT],
  braced: ["BRACED", GUARD], braceSelf: ["BRACED", GUARD], bracedSelf: ["BRACED", GUARD],
  ashenSkin: ["SHIELD", GUARD], aegisShield: ["SHIELD", GUARD], wardShield: ["SHIELD", GUARD], wardPulse: ["SHIELD", GUARD],
  hasted: ["HASTE", BUFF], gliding: ["GLIDE", BUFF], hymnSpeed: ["SPEED", BUFF],
  mendingHot: ["HOT", BUFF], hymnHeal: ["HOT", BUFF], consecrateHeal: ["HOT", BUFF],
  lockShield: ["LOCK", "#c77dff"], spreadMarked: ["SPREAD", "#cc79a7"], stackMarked: ["STACK", "#56b4e9"],
};

/** The TAUNTED chip/tag colour (render/actors.ts's taunt latch). */
export const TAUNT_COLOR = "#ff8c42";
/** Item 12: the grey of a downed party member's tag and ground ring. */
export const DOWNED_COLOR = "#9aa0a6";

function seconds(ticks: number): string {
  return `${(ticks / TICK_HZ).toFixed(1)}s`;
}

export function chipFor(id: string, ticks: number): StatusChip {
  const [label, color] = CHIP[id] ?? [humanizeId(id).toUpperCase().split(" ").slice(0, 2).join(" ").slice(0, 10), "#c9ccd1"];
  return { id, label, text: `${label} ${seconds(ticks)}`, color };
}

export function statusChips(effects: readonly ActorEffect[] | undefined): StatusChip[] {
  return (effects ?? []).map((e) => chipFor(e.id, e.ticks));
}

/** The TAUNTED chip for `ticksLeft` more ticks (render/actors.ts prepends it to the effect chips). */
export function tauntChip(ticksLeft: number): StatusChip {
  return { id: "taunted", label: "TAUNTED", text: `TAUNTED ${seconds(ticksLeft)}`, color: TAUNT_COLOR };
}

/** The name tag's status word: STUNNED > TAUNTED > BRACED (mitigation), else "". */
export function statusTag(effects: readonly ActorEffect[] | undefined, taunted: boolean): string {
  const labels = new Set(statusChips(effects).map((c) => c.label));
  if (labels.has("STUN")) return "STUNNED";
  if (taunted) return "TAUNTED";
  return labels.has("BRACED") ? "BRACED" : "";
}

/** Colour of the status tag line (matches its chip). */
export function statusTagColor(tag: string): string {
  return tag === "STUNNED" ? CC : tag === "TAUNTED" ? TAUNT_COLOR : GUARD;
}
