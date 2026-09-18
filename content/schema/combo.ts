/**
 * content/schema/combo.ts
 * Local copy of the canonical ComboDef contract shared with the sim-combat lane.
 * Data-shape only. Never import sim/ from here.
 */

import type { EffectOp } from "./ability";

export interface ComboDef {
  id: string;
  name: string;
  setupClass: string;
  /**
   * ADDED convention (not a type change  -  still a plain string): "*" means "any class other
   * than setupClass". Used by the three floor-Window field combos (Bulwark/Cinderfield/Hymn),
   * which BIBLE.md Sec 5 says "accept any other class's Payoff for a smaller field version".
   */
  payoffClass: string;
  windowId: string;
  effects: EffectOp[];
}
