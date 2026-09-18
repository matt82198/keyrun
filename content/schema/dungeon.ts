/**
 * content/schema/dungeon.ts
 * `DungeonDef` has no direct sim equivalent: sim/encounter/pulls.ts's own `DungeonDef` is a
 * *resolved* shape (`{id, pulls: [{id, spawns: PullSpawn[]}]}` -- concrete actorId/classId/
 * pos/yaw/health per enemy). Content's `DungeonDef` is the authoring recipe one level up --
 * `{enemyId, count, spawn}` rows that reference `EnemyDef`s by id -- so a level designer edits one
 * enemyId/count/spawn-point per pull instead of hand-assigning actor ids. It stays content-only
 * per that gap; `content/index.ts`'s `toSimDungeon` expands a content DungeonDef + the loaded
 * EnemyDef registry into the real sim/encounter DungeonDef (and a matching `EnemyScript` map) that
 * `sim/index.ts`'s `createRun`/`stepRun` actually consume.
 */

export interface SpawnPoint {
  x: number;
  z: number;
}

export interface PullEnemy {
  enemyId: string;
  count: number;
  spawn: SpawnPoint;
}

export interface PullDef {
  id: string;
  name: string;
  enemies: PullEnemy[];
  teaches: string;
}

export interface BossEncounter {
  enemyId: string;
  mechanics: string[];
}

export interface DungeonDef {
  id: string;
  name: string;
  parTicks: number;
  pulls: PullDef[];
  boss: BossEncounter;
}
