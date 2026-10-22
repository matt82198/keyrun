# content/data/  -  classes, abilities, dungeon, boss, and clauses as data

**Purpose**: `content/data/` holds Keyrun's game content (three classes, their abilities and talents, the Hollow Vault
dungeon, its enemies and boss, the four Clauses, the combos and the facets) as JSON. It is the frozen oracle root.
The C# loader embeds it (`game/content/Keyrun.Content/Keyrun.Content.csproj`, one glob over `content/data/**/*.json`)
and validates it at load time. Content is data: it is never the place combat rules live. The TypeScript code that sat
beside this folder was removed in PR #553; the C# loader and validator now live in `game/content/Keyrun.Content/`.

## Data conventions
- `AbilityDef.variants` (a per-stance effect override, keys `"mend"` / `"ward"`) is used only by Chanter, so one
  ability plays two ways without a second ability id.
- `combos.json` `payoffClass` may be the literal string `"*"` ("any class other than setupClass") for the three
  floor-Window field combos plus The Seed's `seedbloom-field`.
- The Sundered Twins share one Break bar: `breakBarId` / `breakBars` are declared once on the boss entry in
  `data/enemies/hollow-warden.json` and referenced by id from each add.
- `op: "removeEffect"` with `effectId: "*"` is the dispel convention: remove the target's active dispellable effect
  matching the ability's own dispel kind, rather than one fixed id (Cinder's Purge, Chanter's Cleanse).
- The Lock (BIBLE.md section 9) is data only: `hollowWarden.lock`'s `"window"` op sets `onTarget: true, self: true,
  alternate: true`. No enemy-script rule is needed for it.
- Enemy scripts: a `ScriptStep.at` is `"loop"`, `"onDeath"` or a number. `"loop"` repeats every `every` ticks, first
  firing at tick `every`. A numeric `at` with no `every` fires once; with an `every` it is a phase offset that fires
  at `at`, `at+every`, and so on (`pair.stackCall` in `data/enemies/pair.json` is `at: 600, every: 400`). `"onDeath"`
  fires once, on the first tick after its actor is dead (The Seed's `seed.deathBloom` in `data/enemies/seed.json`).
  `pair.spreadMark` ("loop", every 400) and `pair.stackCall` (offset 600, every 400) do not share a phase.
- Content keeps the `"setup:"` / `"payoff"` ability tag convention.
- Facets (`data/facets/`) are gear patches applied on top of a class's abilities, like talents; a facet never adds
  a new ability.

## File layout
- `data/classes/{warden,cinder,chanter}.json`  -  one `ClassDef` each: loop, ability slots, talents.
- `data/abilities/{warden,cinder,chanter,enemies}.json`  -  `AbilityDef[]`, by class + one shared
  file for every enemy/boss ability.
- `data/effects/{player,enemy}.json`  -  `EffectDef[]`.
- `data/combos.json`  -  `ComboEntry[]`: the six named pair-Combos, three floor field-Combos, three
  Lock boss-pseudo-class combos (`lock-warden`/`lock-cinder`/`lock-chanter`), and one field combo
  for The Seed's floor Window (`seedbloom-field`).
- `data/enemies/{choir,pair,seed,hollow-warden}.json`  -  `EnemyDef[]` per Hollow Vault pull/boss.
- `data/dungeons/hollow-vault.json`  -  the one `DungeonDef`.
- `data/facets/{warden,cinder,chanter}.json`  -  `FacetDef[]`, gear Facets per class.
- `data/clauses.json`  -  `ClauseDef[]`: Echo, Tide, Hush, Pact.
- `data/TUNING.md`  -  every placeholder number, table form, marked `// TUNE`.
- C#-only content with no TS counterpart lives in `game/content/data/`, which the same loader reads under the
  `content/data/` logical namespace.

## Rules
1. Content is data. No sim logic, no randomness, no ticks advanced here  -  `content/data/` only
   describes what exists; `game/sim/` (Keyrun.Sim, C#) decides what happens.
2. Every id referenced anywhere (ability, effect, class, combo, enemy, facet) must resolve inside
   the loaded registries. A bad reference fails the load; the loader never drops data silently.
3. Talent/facet patches change function (tags/shape/cost/cooldown/effects), never a flat
   percentage, and never add a new ability (no `id` in a patch).
4. Each class has exactly 5 rotational abilities (BIBLE.md section 2).
5. Tests live in `game/content/Keyrun.Content.Tests/` (C#), not here.
