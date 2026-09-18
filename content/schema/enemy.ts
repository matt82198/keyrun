/**
 * content/schema/enemy.ts
 * Local copy of the canonical EnemyDef contract shared with the sim-combat lane, plus content-
 * lane additions needed for Hollow Vault's boss (documented inline and in content/CLAUDE.md).
 * Data-shape only. Never import sim/ from here.
 */

import type { EffectOp } from "./ability";

export type EnemyRole = "melee" | "caster" | "lieutenant" | "elite" | "boss";

export interface TelegraphDef {
  shape: "cone" | "circle" | "line";
  radius?: number;
  angle1024?: number;
  length?: number;
  width?: number;
  leadTicks: number;
}

/**
 * ADDED: "onDeath" beyond the canonical "loop" | number, so The Seed (elite) can script a
 * death-triggered mechanic (dropping a Window on the floor) instead of only loop/tick casts.
 */
export type ScriptAt = "loop" | "onDeath" | number;

/**
 * ADDED: encodes BIBLE.md Sec 9 "The Lock"  -  the boss's self-Window accepts a Payoff only from a
 * class other than whichever class's Setup last tagged it. sim enforces this; content only
 * records the rule as data (tested in tests/content).
 */
export interface WindowRuleDef {
  windowId: string;
  kind: "alternate-setup-class";
}

export interface ScriptStep {
  at: ScriptAt;
  every?: number;
  cast: string;
  telegraph?: TelegraphDef;
  windowRule?: WindowRuleDef;
}

/**
 * ADDED: a Break bar shared by two or more enemies (Sundered Twins, BIBLE.md Sec 9). Any
 * interrupt- or control-tagged ability drains it; at 0, `onEmpty` effects fire on both adds.
 */
export interface BreakBarDef {
  id: string;
  max: number;
  drainPerInterrupt: number;
  drainPerControl: number;
  onEmpty: EffectOp[];
}

export interface EnemyDef {
  id: string;
  name: string;
  maxHealth: number;
  role: EnemyRole;
  abilities: string[];
  script: ScriptStep[];
  /** ADDED: id of a BreakBarDef (declared on the encounter's boss entry) this enemy shares. */
  breakBarId?: string;
  /** ADDED: only populated on the encounter's primary boss entry; adds reference it by id. */
  breakBars?: BreakBarDef[];
}
