/**
 * content/schema/combo.ts
 * Re-exports the real `ComboDef`/`ComboTable` from sim/combat/combos.ts. The real ComboDef is just
 * `{id, effects}` -- the setupClass/payoffClass/windowId that pick which table cell a combo lives
 * in are folded into the ComboTable's `[setupClass][payoffClass]` key structure, not carried on
 * the def itself. `ComboEntry` is content's authoring/validation shape (keeps those three fields
 * plus a display `name` as flat, greppable, validate-able JSON rows in data/combos.json);
 * `content/index.ts` expands a `ComboEntry[]` into the real `ComboTable` two ways:
 *   - exact (setupClass, payoffClass) pairs go straight into the "burst" table.
 *   - `payoffClass: "*"` ("any class other than setupClass", still just a string convention, see
 *     content/CLAUDE.md "Schema additions") is expanded into one table cell per other class for
 *     the smaller "field" table floor-patch Windows use (sim/combat/windows.ts's
 *     tryClosePayoffOnFloor takes a *separate* ComboTable for exactly this reason).
 *   - `setupClass: "boss"` is the enemy-side pseudo-class sim/combat/combos.ts's own docstring
 *     names as the convention for a boss/enemy Window's row (e.g. The Lock, BIBLE.md Sec 9).
 */
export type { ComboDef, ComboTable } from "../../sim/combat/combos";

import type { EffectOp } from "./ability";

export interface ComboEntry {
  id: string;
  name: string;
  setupClass: string;
  /** "*" means "any class other than setupClass" -- see the file docstring. */
  payoffClass: string;
  windowId: string;
  effects: EffectOp[];
}
