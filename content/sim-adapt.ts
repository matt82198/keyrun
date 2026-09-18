/**
 * content/sim-adapt.ts
 * The two converters that turn loaded content into the plain data sim/index.ts's
 * `createRun`/`RunContent` and sim/combat/rules.ts's `RulesConfig` actually consume -- see
 * tests/sim/integration/content.test.ts for the full wiring. Pulled out of content/index.ts purely
 * to keep that file under sim/CLAUDE.md's <=190 LOC-per-file cap; re-exported from there.
 */
import type { ComboEntry, ComboDef, ComboTable } from "./schema/combo";
import type { DungeonDef } from "./schema/dungeon";
import type { EnemyDef } from "./schema/enemy";
import type { DungeonDef as SimDungeonDef, PullDef as SimPullDef, PullSpawn } from "../sim/encounter/pulls";
import type { EnemyScript } from "../sim/encounter/scripts";

/**
 * Expands a `Map<string, ComboEntry>` (data/combos.json, as loaded) into the two real
 * `ComboTable`s sim/combat/rules.ts's `RulesConfig` wants: `combos` (target/self Window
 * closures, keyed by exact setupClass/payoffClass) and `fieldCombos` (floor-patch Window
 * closures -- every `payoffClass: "*"` entry is expanded into one cell per other known class,
 * since sim/combat/windows.ts's `tryClosePayoffOnFloor` takes a fully concrete table and has no
 * wildcard concept of its own).
 */
export function toComboTables(entries: Map<string, ComboEntry>, classIds: string[]): { combos: ComboTable; fieldCombos: ComboTable } {
  const combos: ComboTable = {};
  const fieldCombos: ComboTable = {};
  for (const entry of entries.values()) {
    const def: ComboDef = { id: entry.id, effects: entry.effects };
    const isField = entry.payoffClass === "*";
    const table = isField ? fieldCombos : combos;
    const payoffClasses = isField ? classIds.filter((c) => c !== entry.setupClass) : [entry.payoffClass];
    for (const payoffClass of payoffClasses) {
      const row = table[entry.setupClass] ?? {};
      row[payoffClass] = def;
      table[entry.setupClass] = row;
    }
  }
  return { combos, fieldCombos };
}

export interface ExpandedDungeon {
  dungeon: SimDungeonDef;
  enemyScripts: Record<number, EnemyScript>;
}

/**
 * Expands a content `DungeonDef` (an authoring recipe of `{enemyId, count, spawn}` rows, see
 * schema/dungeon.ts) plus the loaded `EnemyDef` registry into the real sim/encounter `DungeonDef`
 * (concrete actor ids/classId/pos/yaw/health per spawn) and a matching `enemyScripts` map, both
 * consumable directly by `sim/index.ts`'s `createRun`/`RunContent`. Actor ids are assigned
 * sequentially starting at `firstActorId` in pull/enemy-group/count order, so the mapping is
 * deterministic for a given content snapshot (not stable across content edits -- callers that need
 * a specific actor id for a specific spawn should read it back off the returned pull's `spawns`).
 */
export function toSimDungeon(dungeon: DungeonDef, enemies: Map<string, EnemyDef>, firstActorId = 1000): ExpandedDungeon {
  let nextId = firstActorId;
  const enemyScripts: Record<number, EnemyScript> = {};
  const pulls: SimPullDef[] = dungeon.pulls.map((pull) => {
    const spawns: PullSpawn[] = [];
    for (const group of pull.enemies) {
      const def = enemies.get(group.enemyId);
      if (!def) throw new Error(`content: toSimDungeon: pull "${pull.id}" references unknown enemy "${group.enemyId}"`);
      for (let i = 0; i < group.count; i += 1) {
        const actorId = nextId;
        nextId += 1;
        spawns.push({
          actorId,
          classId: def.id,
          pos: { x: group.spawn.x, y: 0, z: group.spawn.z },
          yaw: 0,
          health: def.maxHealth,
        });
        enemyScripts[actorId] = def.script;
      }
    }
    return { id: pull.id, spawns };
  });
  return { dungeon: { id: dungeon.id, pulls }, enemyScripts };
}
