/**
 * content/schema/dungeon.ts
 * Local copy of the canonical DungeonDef contract shared with the sim-combat lane.
 * Data-shape only. Never import sim/ from here.
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
