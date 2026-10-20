# content/  -  classes, abilities, dungeon, boss, and clauses as data

**Purpose**: Keyrun's game content (three classes, their abilities/talents, the Hollow Vault
dungeon, its enemies and boss, and the four Clauses) expressed entirely as JSON, validated at
load time. Content is data: it is never the place combat rules live.

## Schema version (aligned to the real sim/combat contract)

`content/schema/*.ts` re-exports the canonical `AbilityDef`/`EffectOp`/`EffectDef`/`ComboDef`/
`ComboTable` contract from `sim/combat/*.ts` (landed via PR #10/#13, "windows-combos") instead of
carrying a local duplicate copy. `content/schema/ability.ts`'s `AbilityDef` and
`content/schema/effect.ts`'s `EffectDef` are the real sim shapes plus the handful of fields sim has
no notion of (a display `name`, Chanter's `variants` stance override, `tags` promoted from
optional to required) -- the bare re-exported sim types are available as `SimAbilityDef`/
`SimEffectDef` for anyone who wants to say so explicitly. `EnemyDef`/`DungeonDef`/`ClassDef`/
`ClauseDef` stay content-only: sim has no equivalent (an enemy's AI behaviour is a separate
`EnemyScript`, a dungeon "recipe" is expanded to concrete actor spawns by `toSimDungeon`, and
class/clause structure is entirely a content concern). **Content never imports `sim/` internals**
-- only the type-and-validator surface (`validateAbilityDef`, `AbilityDef`, `EffectOp`, `ComboDef`,
`ComboTable`, `EnemyScript`/`ScriptStep`, `DungeonDef`/`PullSpawn`) that exists for exactly this
purpose. `content/index.ts`'s `loadContent()` runs every ability through the REAL sim validator,
not a local copy (`content/validate.ts`'s `validateAbilitiesAgainstSim`).

`content/sim-adapt.ts` exports the two converters that turn a loaded registry into the plain data
`sim/index.ts`'s `createRun`/`RunContent` and `sim/combat/rules.ts`'s `RulesConfig` actually
consume: `toComboTables` (expands `data/combos.json`'s flat `ComboEntry` rows, including the `"*"`
payoffClass wildcard, into the real `[setupClass][payoffClass]` `ComboTable`s) and `toSimDungeon`
(expands a content `DungeonDef` "recipe" plus the `EnemyDef` registry into concrete sim actor
spawns and an `enemyScripts` map). See `tests/sim/integration/content.test.ts` for the full wiring
of loaded content through a real run.

### Two sim gaps flagged, not fixed here (sim/** is out of this domain's edit scope)
- **Hard control has no mechanical hook.** The real `EffectModifiers` is basis-points-only
  (damageDoneBp/damageTakenBp/hasteBp/movementBp/absorb); `sim/combat/casting.ts`'s `startCast`
  never consults an actor's effects for a control state (only a school lockout). The
  `stunned`/`silenced`/`blinded` effect ids in `data/effects/player.json` apply and expire like any
  other `EffectInstance` but do not currently prevent a cast or move.
- **`removeEffect` has no wildcard/dispel-any semantics.** `sim/combat/effects.ts`'s
  `removeEffectInstances` does literal `effectId` string equality. Cinder's Purge and Chanter's
  Cleanse (`op: "removeEffect", effectId: "*"`) record the intent as data (and
  `content/index.ts`'s validator special-cases `"*"` so it never needs to resolve to a real effect
  id) but the op is currently a no-op against a live actor's effects list until sim adds
  dispellableTag-driven "any" removal.
- **`sim/encounter/scripts.ts`'s `ScriptStep.at` is `"loop" | "onDeath" | number`.** `"loop"`
  repeats every `every` ticks, first firing at tick `every`. A numeric `at` with no `every` fires
  once; with an `every` it is a phase offset that fires at `at`, `at+every`, ... (`pair.stackCall`
  in `content/data/enemies/pair.json` is `at: 600, every: 400`). `"onDeath"` fires once, on the
  first tick after its actor is dead (`firedOnDeath`, threaded by `sim/encounter/index.ts`). The
  Seed's `seed.deathBloom` is scripted that way (`content/data/enemies/seed.json`), and sim/combat's
  rules.ts lets a dead actor's `cast` through for it. `pair.spreadMark` (`"loop"`, every 400) and
  `pair.stackCall` (offset 600, every 400) no longer share a phase.
- **Sundered Twins' Break bar is wired.** `sim/encounter/index.ts`'s `stepEncounter` builds a
  `BreakBarState` from `breakBarWiring` once both twins are alive and drains it per tick with
  `drainBreakBar`. `sim/index.ts`'s `stepRun` supplies that wiring from `content.breakBarSources`
  (`EnemyDef.breakBarId`/`breakBars` in `content/schema/enemy.ts` is the authoring side).

### Schema additions beyond the canonical contract (documented here, not silent)
- `AbilityDef.variants?: Record<string, EffectOp[]>`  -  per-stance effect override, used only by
  Chanter (`"mend"` / `"ward"` keys) so one ability plays two ways without a second ability id.
- `ComboEntry.payoffClass` (content/schema/combo.ts, the authoring row shape for
  `data/combos.json`) may be the literal string `"*"` ("any class other than setupClass")  -  a
  convention, not a type change  -  for the three floor-Window field combos plus The Seed's
  `seedbloom-field`; `toSimDungeon`'s sibling `toComboTables` expands it into one concrete table
  cell per other known class.
- `EnemyDef.breakBarId?` / `EnemyDef.breakBars?: BreakBarDef[]`  -  a Break bar shared by the
  Sundered Twins; declared once on the boss entry, referenced by id from each add. See "sim gaps"
  above -- the type exists, the sim wiring doesn't yet.
- `EffectOp` with `op: "removeEffect", effectId: "*"`  -  dispel convention: remove the target's
  active dispellable effect matching the ability's own dispel kind, rather than one fixed id. See
  "sim gaps" above.
- The Lock (BIBLE.md Sec 9) is expressed entirely with real sim fields, not a content-only
  addition: `hollowWarden.lock`'s "window" op sets `onTarget: true, self: true, alternate: true`,
  and `sim/combat/windows.ts` fully implements the alternation-denial and 3-fail enrage counter
  from that flag alone. No enemy-script-level rule needed (an earlier `windowRule`/`WindowRuleDef`
  addition has been removed now that the real mechanism landed).
- Content keeps the pre-integration `"setup:"`/`"payoff"` ability tag convention (not
  sim/combat/windows.ts's own internal `"window:"` synthetic-effectId prefix, a different,
  unrelated namespace) because `content/apply.ts`'s `isSetupTag`/`isPayoffTag` hardcode it.
- `content/schema/facet.ts` (another lane, BIBLE.md Sec 7 "Gear and progression") adds
  `FacetDef`/`Item`/`GearSlotId` on top of this contract; `content/apply.ts` applies talents then
  facets to a class's ability definitions. Not owned by this note's author -- see that file's own
  docstring.

## File layout
- `schema/*.ts`  -  the typed contract (see above). Data-shape only, no logic.
  `schema/registry.ts` holds the `ContentRegistry` map-of-maps type only (kept separate so
  `index.ts` and `validate.ts` can both reference it under sim/CLAUDE.md's <=190-LOC-per-file cap).
- `data/classes/{warden,cinder,chanter}.json`  -  one `ClassDef` each: loop, ability slots, talents.
- `data/abilities/{warden,cinder,chanter,enemies}.json`  -  `AbilityDef[]`, by class + one shared
  file for every enemy/boss ability.
- `data/effects/{player,enemy}.json`  -  `EffectDef[]`.
- `data/combos.json`  -  `ComboEntry[]`: the six named pair-Combos, three floor field-Combos, three
  Lock boss-pseudo-class combos (`lock-warden`/`lock-cinder`/`lock-chanter`), and one field combo
  for The Seed's floor Window (`seedbloom-field`).
- `data/enemies/{choir,pair,seed,hollow-warden}.json`  -  `EnemyDef[]` per Hollow Vault pull/boss.
- `data/dungeons/hollow-vault.json`  -  the one `DungeonDef`.
- `data/facets/{warden,cinder,chanter}.json`  -  `FacetDef[]`, gear Facets per class (another lane).
- `data/clauses.json`  -  `ClauseDef[]`: Echo, Tide, Hush, Pact.
- `data/TUNING.md`  -  every placeholder number, table form, marked `// TUNE`.
- `index.ts`  -  `loadContent()`: reads every JSON file, builds `id -> def` registries, and
  fail-loudly validates them via `validate.ts` (unknown id references, duplicate ids, a class
  without exactly 5 rotational abilities, a talent/facet patch that adds/renames an ability, an
  unknown effect reference, a combo whose setupClass equals its payoffClass, and every ability
  against the REAL sim validator). Also exports `canPayoffCloseWindow` (a content-side restatement
  of The Lock's alternation rule for tests/design review -- real enforcement is
  `sim/combat/windows.ts`'s `tryClosePayoff`) and re-exports `sim-adapt.ts`'s converters.
- `validate.ts`  -  every `loadContent()` invariant check, split out of `index.ts` for the LOC cap.
- `sim-adapt.ts`  -  `toComboTables`/`toSimDungeon`, split out of `index.ts` for the LOC cap.
- `apply.ts`  -  `applyLoadout`/`describeLoadout`: applies talent + facet patches to a class's
  ability definitions before a run starts (another lane -- not owned by this note's author).

## Rules
1. Content is data. No sim logic, no randomness, no ticks advanced here  -  `content/` only
   describes what exists; `sim/` decides what happens.
2. Every id referenced anywhere (ability, effect, class, combo, enemy, facet) must resolve inside
   these registries  -  `loadContent()` throws rather than silently dropping bad data.
3. Talent/facet patches change function (tags/shape/cost/cooldown/effects), never a flat
   percentage, and never add a new ability (no `id` in a patch).
4. Tests live in `tests/content/*.test.ts`, not here.
