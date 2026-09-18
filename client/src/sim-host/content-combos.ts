/**
 * client/src/sim-host/content-combos.ts
 *
 * Thin wrapper around content/sim-adapt.ts's official `toComboTables` (browser-safe: pure logic,
 * only type-only imports), which expands content's flat `data/combos.json` rows into the two real
 * `ComboTable`s sim/combat/rules.ts's `RulesConfig` wants -- `combos` (target/self Window
 * closures) and `fieldCombos` (floor-patch closures, one cell per class for every
 * `payoffClass: "*"` row). Kept as its own file mainly so run-setup.ts stays readable; the real
 * work is entirely content/sim-adapt.ts's.
 *
 * Usage: `const { combos, fieldCombos } = buildComboTables(registry, ["warden","cinder","chanter"]);`
 */
import { toComboTables } from "../../../content/sim-adapt.js";
import type { ComboTable } from "../../../sim/combat/combos.js";
import type { ContentRegistry } from "../../../content/schema/registry.js";

export interface ComboTables {
  combos: ComboTable;
  fieldCombos: ComboTable;
}

export function buildComboTables(registry: ContentRegistry, classIds: string[]): ComboTables {
  return toComboTables(registry.combos, classIds);
}
