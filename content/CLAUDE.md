# content/  -  classes, abilities, dungeon, boss, and clauses as data

**Purpose**: Keyrun's game content (three classes, their abilities/talents, the Hollow Vault
dungeon, its enemies and boss, and the four Clauses) expressed entirely as JSON, validated at
load time. Content is data: it is never the place combat rules live.

## Schema version

Local copies of the canonical sim-combat contract (`AbilityDef`/`EffectOp`/`EffectDef`/
`ClassDef`/`ComboDef`/`EnemyDef`/`DungeonDef`) live in `content/schema/*.ts`, written to match
the sim-combat lane's parallel definitions field-for-field so integration is a re-import, not a
rewrite. **Content never imports `sim/`**  -  `sim` is design-agnostic and receives content as
plain data; `content` never reads `sim` internals either. `content/schema/clause.ts` is a
content-only addition (Clauses are queue-time data, not combat rules).

### KNOWN DRIFT vs the merged sim/combat (feat/sim-combat landed on main after this lane started)
This lane's schema/*.ts was written from the AbilityDef/EffectOp/EffectDef contract text given at
dispatch, matching it field-for-field as instructed. `feat/sim-combat` (PR #10) merged to main
mid-lane with a narrower real contract in `sim/combat/abilities.ts` / `sim/combat/effects.ts`.
Grepped: `sim/combat/*.ts` has zero references to "window", "combo", or "tags" anywhere -
Setup/Payoff/Window/Combo (BIBLE.md Sec 4, this content lane's core deliverable) is not yet
implemented in any merged sim/ code. Exact drift, for whoever wires content into sim next:
- `AbilityDef.tags` does not exist on the real type (no Setup/Payoff/interrupt/dispel tagging).
- `EffectOp` real union has only damage/heal/applyEffect/removeEffect/interrupt/resource/threat
  - no `"window"` or `"combo"` op. `interrupt` takes no `lockoutTicks`; `resource` takes only
  `delta` (no `resource` name - presumably one resource per actor). `applyEffect.stacks` is
  required, not optional.
- `AbilityShape` has no `width`; a line's half-width is its `radius` field, not a separate field.
- `AbilityDef.cost` is required (not optional) on the real type.
- `EffectDef` real shape: `{ id, maxStacks, dispellableTag: string|null, modifiers (required,
  adds `absorb?: number`), periodic }` - no `name`, no `durationTicks` (duration is passed at
  apply time, not stored on the def).
This content data still encodes the full Window/Combo/tag model the bible requires; that model
needs either a small sim addition (tags + window/combo ops) or a synergy layer above sim/combat
before content's abilities can be re-imported as-is. Flagged to the orchestrator in this lane's
final report - do not silently reconcile by dropping tags/windows from content.

### Schema additions beyond the canonical contract (documented here, not silent)
- `AbilityDef.variants?: Record<string, EffectOp[]>`  -  per-stance effect override, used only by
  Chanter (`"mend"` / `"ward"` keys) so one ability plays two ways without a second ability id.
- `EffectDef.modifiers.controlKind?: "stun"|"silence"|"root"|"blind"`  -  the canonical bp-only
  modifiers set has no way to express hard control; one slot covers all four.
- `ComboDef.payoffClass` may be the literal string `"*"` ("any class other than setupClass")  - 
  a convention, not a type change  -  for the three floor-Window field combos.
- `EnemyDef.script[].at` accepts `"onDeath"` beyond `"loop" | number`, for The Seed's death-
  triggered Window drop.
- `EnemyDef.script[].windowRule?: { windowId; kind: "alternate-setup-class" }`  -  encodes The
  Lock's alternation rule (BIBLE.md Sec 9) as data; sim enforces it.
- `EnemyDef.breakBarId?` / `EnemyDef.breakBars?: BreakBarDef[]`  -  a Break bar shared by the
  Sundered Twins; declared once on the boss entry, referenced by id from each add.
- `EffectOp` with `op: "removeEffect", effectId: "*"`  -  dispel convention: remove the target's
  active dispellable effect matching the ability's own dispel kind, rather than one fixed id.

## File layout
- `schema/*.ts`  -  the typed contract (see above). Data-shape only, no logic.
- `data/classes/{warden,cinder,chanter}.json`  -  one `ClassDef` each: loop, ability slots, talents.
- `data/abilities/{warden,cinder,chanter,enemies}.json`  -  `AbilityDef[]`, by class + one shared
  file for every enemy/boss ability.
- `data/effects/{player,enemy}.json`  -  `EffectDef[]`.
- `data/combos.json`  -  `ComboDef[]`: the six named pair-Combos plus three floor field-Combos.
- `data/enemies/{choir,pair,seed,hollow-warden}.json`  -  `EnemyDef[]` per Hollow Vault pull/boss.
- `data/dungeons/hollow-vault.json`  -  the one `DungeonDef`.
- `data/clauses.json`  -  `ClauseDef[]`: Echo, Tide, Hush, Pact.
- `data/TUNING.md`  -  every placeholder number, table form, marked `// TUNE`.
- `index.ts`  -  `loadContent()`: reads every JSON file, builds `id -> def` registries, and
  fail-loudly validates them (unknown id references, duplicate ids, a class without exactly 5
  rotational abilities, a talent patch that adds/renames an ability, an unknown effect
  reference, a combo whose setupClass equals its payoffClass). Also exports
  `canPayoffCloseWindow` (The Lock's alternation rule as a pure function).

## Rules
1. Content is data. No sim logic, no randomness, no ticks advanced here  -  `content/` only
   describes what exists; `sim/` decides what happens.
2. Every id referenced anywhere (ability, effect, class, combo, enemy) must resolve inside these
   registries  -  `loadContent()` throws rather than silently dropping bad data.
3. Talent patches change function (tags/shape/cost/cooldown/effects), never a flat percentage,
   and never add a new ability (no `id` in a patch).
4. Tests live in `tests/content/*.test.ts`, not here.
