/**
 * client/src/sim-host/content-registry.ts
 *
 * Browser-safe loader for content/schema/registry.ts's real `ContentRegistry`. content/index.ts's
 * own `loadContent()` uses `node:fs`/`node:url` at module scope, which esbuild cannot bundle for a
 * browser target (client/CLAUDE.md: esbuild only, no Node polyfills) -- so this file never imports
 * content/index.ts (importing anything from that module pulls in its top-level `node:fs` import
 * too). Instead it fetches the same JSON files content/index.ts reads from disk, over HTTP, and
 * assembles the same `id -> def` Maps content/index.ts's own `byId()` builds. Every value type
 * here is imported with `import type` directly from content/schema/*.ts -- those files are, since
 * the real sim/combat integration landed, pure re-exports/thin extensions of the real sim types
 * with zero runtime imports of their own (confirmed: only `export type`/type-only re-exports plus
 * a couple of pure functions this file never touches), so `import type` erases them completely at
 * build time and this file carries no dependency on content/ at runtime either.
 *
 * DEVIATION: this loader does NOT re-run content/validate.ts's validateRegistry() (unknown-id
 * refs, duplicate ids, the sim ability validator, etc.) -- that would mean duplicating real
 * validation logic for a browser-only code path. tests/content/*.test.ts already validates this
 * exact data under Node via the real loadContent(); this file trusts that content is well-formed.
 *
 * Usage: `const registry = await fetchContentRegistry();` (browser only). Tests build a registry
 * via `content/index.ts`'s real `loadContent()` instead (works fine under Vitest/Node) and pass it
 * to the same run-setup.ts functions this registry feeds.
 */
import type { AbilityDef } from "../../../content/schema/ability.js";
import type { EffectDef } from "../../../content/schema/effect.js";
import type { ClassDef } from "../../../content/schema/class.js";
import type { ComboEntry } from "../../../content/schema/combo.js";
import type { EnemyDef } from "../../../content/schema/enemy.js";
import type { DungeonDef } from "../../../content/schema/dungeon.js";
import type { ClauseDef } from "../../../content/schema/clause.js";
import type { FacetDef } from "../../../content/schema/facet.js";
import type { ContentRegistry } from "../../../content/schema/registry.js";

function byId<T extends { id: string }>(items: T[]): Map<string, T> {
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
    fetchJson<AbilityDef[]>("/content/data/abilities/warden.json"),
    fetchJson<AbilityDef[]>("/content/data/abilities/cinder.json"),
    fetchJson<AbilityDef[]>("/content/data/abilities/chanter.json"),
    fetchJson<AbilityDef[]>("/content/data/abilities/enemies.json"),
  ]);
  const [playerEffects, enemyEffects] = await Promise.all([
    fetchJson<EffectDef[]>("/content/data/effects/player.json"),
    fetchJson<EffectDef[]>("/content/data/effects/enemy.json"),
  ]);
  const [wardenClass, cinderClass, chanterClass] = await Promise.all([
    fetchJson<ClassDef>("/content/data/classes/warden.json"),
    fetchJson<ClassDef>("/content/data/classes/cinder.json"),
    fetchJson<ClassDef>("/content/data/classes/chanter.json"),
  ]);
  const [choir, pair, seed, hollowWarden] = await Promise.all([
    fetchJson<EnemyDef[]>("/content/data/enemies/choir.json"),
    fetchJson<EnemyDef[]>("/content/data/enemies/pair.json"),
    fetchJson<EnemyDef[]>("/content/data/enemies/seed.json"),
    fetchJson<EnemyDef[]>("/content/data/enemies/hollow-warden.json"),
  ]);
  const [hollowVault, combos] = await Promise.all([
    fetchJson<DungeonDef>("/content/data/dungeons/hollow-vault.json"),
    fetchJson<ComboEntry[]>("/content/data/combos.json"),
  ]);

  return {
    abilities: byId([...warden, ...cinder, ...chanter, ...enemyAbilities]),
    effects: byId([...playerEffects, ...enemyEffects]),
    classes: byId([wardenClass, cinderClass, chanterClass]),
    combos: byId(combos),
    enemies: byId([...choir, ...pair, ...seed, ...hollowWarden]),
    dungeons: byId([hollowVault]),
    // Queue-time-only data (Clauses) and gear progression (Facets) are out of playable-v1's scope
    // (see design/BIBLE.md Sec 10/Sec 7) -- empty maps keep ContentRegistry's shape complete.
    clauses: new Map<string, ClauseDef>(),
    facets: new Map<string, FacetDef>(),
  };
}
