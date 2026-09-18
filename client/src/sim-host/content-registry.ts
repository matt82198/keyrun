/**
 * client/src/sim-host/content-registry.ts
 *
 * A local, structurally-compatible copy of content/index.ts's `ContentRegistry` shape, plus a
 * browser-safe loader for it. content/index.ts's own `loadContent()` uses `node:fs`/`node:url` at
 * module scope, which esbuild cannot bundle for a browser target (client/CLAUDE.md: esbuild only,
 * no Node polyfills) -- so this file never imports content/index.ts (not even its pure helpers:
 * importing anything from that module pulls in its top-level `node:fs` import too). Instead it
 * fetches the same JSON files content/index.ts reads from disk, over HTTP, and assembles the same
 * `id -> def` Maps content/index.ts's own `byId()` builds. Types are re-declared locally rather
 * than imported from content/schema/*.ts to keep this file fully decoupled (those files are pure
 * types with zero runtime exports, so importing them would be safe too, but a local copy makes the
 * "this file never depends on content/ at runtime" property obvious at a glance).
 *
 * DEVIATION: this loader does NOT re-run content/index.ts's validate() (unknown-id refs, duplicate
 * ids, exactly-5-rotational-slots, etc.) -- that would mean duplicating its full validation logic a
 * second time for a browser-only code path. `tests/content/*.test.ts` already validates this exact
 * data under Node via the real loadContent(); this file trusts that content is well-formed data.
 *
 * Usage: `const registry = await fetchContentRegistry();` (browser only). Tests build a registry
 * via `content/index.ts`'s real `loadContent()` instead (works fine under Vitest/Node) and pass it
 * to the same `run-setup.ts` functions this registry feeds.
 */

// --- Minimal ability/effect/class/enemy/dungeon/combo shapes this lane actually reads. These are
// intentionally narrower than content/schema/*.ts's full types (e.g. no TalentFork) since this
// lane never applies talents -- see content-abilities.ts's file docstring for why.
export interface CShapeDef {
  kind: "target" | "self" | "cone" | "circle" | "line";
  radius?: number;
  angle1024?: number;
  length?: number;
  width?: number;
}
export type CEffectOp =
  | { op: "damage"; amount: number; school: string }
  | { op: "heal"; amount: number }
  | { op: "applyEffect"; effectId: string; durationTicks: number; stacks?: number }
  | { op: "removeEffect"; effectId: string }
  | { op: "interrupt"; lockoutTicks?: number }
  | { op: "resource"; resource?: string; delta: number }
  | { op: "threat"; amount?: number; multiplierBp?: number }
  | { op: "window"; windowId: string; durationTicks: number; onTarget: boolean }
  | { op: "combo"; comboTable: string };
export interface CAbilityDef {
  id: string;
  castTicks: number;
  gcdTicks: number;
  cooldownTicks: number;
  charges: number;
  cost?: { resource: string; amount: number };
  range: number;
  shape: CShapeDef;
  interruptible: boolean;
  tags: string[];
  effects: CEffectOp[];
  variants?: Record<string, CEffectOp[]>;
}
export interface CEffectDef {
  id: string;
  maxStacks: number;
  durationTicks?: number;
  modifiers?: { damageDoneBp?: number; damageTakenBp?: number; hasteBp?: number; movementBp?: number };
  periodic?: { everyTicks: number; op: CEffectOp };
  dispellable?: "magic" | "physical" | null;
}
export interface CClassSlots {
  rot1: string; rot2: string; rot3: string; rot4: string; rot5: string;
  interrupt: string; defensive: string; utility: string; movement: string;
}
export interface CClassDef {
  id: string;
  name: string;
  loop: { resource: string; max: number; kind: string };
  abilities: CClassSlots;
}
export interface CEnemyScriptStep {
  at: "loop" | "onDeath" | number;
  every?: number;
  cast: string;
  telegraph?: { shape: "cone" | "circle" | "line"; radius?: number; angle1024?: number; length?: number; leadTicks: number };
}
export interface CEnemyDef {
  id: string;
  name: string;
  maxHealth: number;
  role: string;
  abilities: string[];
  script: CEnemyScriptStep[];
}
export interface CPullEnemy {
  enemyId: string;
  count: number;
  spawn: { x: number; z: number };
}
export interface CDungeonDef {
  id: string;
  name: string;
  parTicks: number;
  pulls: { id: string; name: string; enemies: CPullEnemy[] }[];
  boss: { enemyId: string; mechanics: string[] };
}

// ReadonlyMap (not Map) so a real content/index.ts ContentRegistry -- built with the richer
// content/schema/*.ts types, under Node in tests -- is structurally assignable here too: Map's
// mutating methods (`set`) make Map<string, A> invariant in A, while ReadonlyMap's read-only
// surface is covariant, so any registry whose def types are a superset of these fields fits.
export interface ContentRegistry {
  abilities: ReadonlyMap<string, CAbilityDef>;
  effects: ReadonlyMap<string, CEffectDef>;
  classes: ReadonlyMap<string, CClassDef>;
  enemies: ReadonlyMap<string, CEnemyDef>;
  dungeons: ReadonlyMap<string, CDungeonDef>;
}

function byId<T extends { id: string }>(items: T[]): ReadonlyMap<string, T> {
  const map = new Map<string, T>();
  for (const item of items) map.set(item.id, item);
  return map;
}

async function fetchJson<T>(path: string): Promise<T> {
  const res = await fetch(path);
  if (!res.ok) throw new Error(`content-registry: failed to fetch "${path}": ${res.status}`);
  return (await res.json()) as T;
}

/**
 * Fetches every content/data/*.json file over HTTP and assembles the same registries
 * content/index.ts's loadContent() builds. Requires the page to be served from the repo root (the
 * same convention scripts/build.mjs's own dev server already uses -- servedir "." -- and the one
 * client/index.html itself relies on for `../dist/client/src/main.js`), so `/content/data/...`
 * resolves; a server rooted at `client/` alone cannot see these paths.
 */
export async function fetchContentRegistry(): Promise<ContentRegistry> {
  const [warden, cinder, chanter, enemyAbilities] = await Promise.all([
    fetchJson<CAbilityDef[]>("/content/data/abilities/warden.json"),
    fetchJson<CAbilityDef[]>("/content/data/abilities/cinder.json"),
    fetchJson<CAbilityDef[]>("/content/data/abilities/chanter.json"),
    fetchJson<CAbilityDef[]>("/content/data/abilities/enemies.json"),
  ]);
  const [playerEffects, enemyEffects] = await Promise.all([
    fetchJson<CEffectDef[]>("/content/data/effects/player.json"),
    fetchJson<CEffectDef[]>("/content/data/effects/enemy.json"),
  ]);
  const [wardenClass, cinderClass, chanterClass] = await Promise.all([
    fetchJson<CClassDef>("/content/data/classes/warden.json"),
    fetchJson<CClassDef>("/content/data/classes/cinder.json"),
    fetchJson<CClassDef>("/content/data/classes/chanter.json"),
  ]);
  const [choir, pair, seed, hollowWarden] = await Promise.all([
    fetchJson<CEnemyDef[]>("/content/data/enemies/choir.json"),
    fetchJson<CEnemyDef[]>("/content/data/enemies/pair.json"),
    fetchJson<CEnemyDef[]>("/content/data/enemies/seed.json"),
    fetchJson<CEnemyDef[]>("/content/data/enemies/hollow-warden.json"),
  ]);
  const hollowVault = await fetchJson<CDungeonDef>("/content/data/dungeons/hollow-vault.json");

  return {
    abilities: byId([...warden, ...cinder, ...chanter, ...enemyAbilities]),
    effects: byId([...playerEffects, ...enemyEffects]),
    classes: byId([wardenClass, cinderClass, chanterClass]),
    enemies: byId([...choir, ...pair, ...seed, ...hollowWarden]),
    dungeons: byId([hollowVault]),
  };
}
