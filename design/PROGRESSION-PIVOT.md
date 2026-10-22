# Progression pivot: ability trees and the completion roll (R-PIVOT-1)

PURPOSE: amend keyrun's progression for Matt's 2026-10-22 pivot (Diablo-style ability improvement trees, gear dropped
by completion as an RNG roll every player gets) precisely enough that implementation lanes can follow it; status: real
(design only, no code in this document's PR).

Matt, 2026-10-22, verbatim (ruling R-PIVOT-1, finding f-6fec76e6):

> "the initial abilities will be like diablo... re pivot design.... abilities will have improvement trees, unlocked by
> progression, and gear will be dropped by completion. There is no guaranteed improvement at the end of every dungeon,
> its still RNG, its just that everyone gets a chance to roll instead of wow loot cap and trading garbage lol"

**Rules this document stays inside** (STATE.md INTENT; BIBLE s0, s1, s7, s10):

- The core dungeon loop (BIBLE s1-s4, s9) is untouched and defers to WoW Mythic+. Nothing here changes a pull, a
  cast, the timer, forces or the key upgrade.
- Out of the dungeon, Dark and Darker is the reference within the bible; for **ability growth** Matt now names Diablo.
- Kept as written: five gear slots, Tier capped at 10, one Facet per item, two crafting charges, Ash buys only
  cosmetics, Ascension after the cap, the private rating and last-3-runs strip, free respec.
- Still banned: a shareable score, a shop, a stash, quests (BIBLE s0 test 3, s1, s10).
- The budget is frozen: five rotational abilities and ten keybinds (BIBLE s2, D4). A tree changes what a button does;
  it never adds one.

Reading order: s1 trees, s2 the roll, s3 the loop text, s4 migration, s5 the calls Matt may overrule.

---

## 1. Ability improvement trees

### 1.1 The precedents, and what each one lends

| Game | Mechanic | What Keyrun takes | What it leaves |
|---|---|---|---|
| Diablo II | Skill levels: each point in a skill raises its numbers; points come from progression | **Rank nodes**: a small, flat, integer bump to an ability the player already presses | 20 levels per skill and synergy point-sinks; the hard respec |
| Diablo III | Runes: each skill has five runes unlocked by character level; one is active; swapping is free | **Free swap of the choice node** and unlocks gated by progression | Five options per skill; Keyrun keeps the 2-3 forks of BIBLE s6 |
| Diablo IV | Skill tree: a base skill, then an **Enhancement** (one function change), then a choice of one of two **Upgrades**; deeper tiers gated by points spent | **The node chain**: rank, then enhancement, then a choice, then rank | Gold-priced respec (research/04:55-62 records the backlash); passive clusters off the skill |
| Lost Ark | Tripods: tiered, mutually exclusive modifiers per skill; you cannot max every skill at once | Depth comes from modifiers on a fixed kit (research/03:29-33, :116) | Eight skills |

Research/04:55-62 also records Diablo IV's own rationale: a simple skill tree first and deeper customisation later, so a
new player is not handed two systems at once. Keyrun's key gates (1.3) follow that layering. The Diablo II and Diablo
III rows are cited from the games themselves; no research brief covers them yet, and a research lane may add one.

**Why a chain and not a graph.** BIBLE s6 already gives every rotational ability one fork of 2-3 options that change
function, with a dominance check (Matt 2026-10-20). A Diablo IV shaped chain keeps that fork as its choice node, so
the pivot adds growth around the forks without replacing them or the parity check. A shared graph (Path of Exile,
research/04:46-53) is the complexity the bible rejected.

### 1.2 Shape: one tree per rotational ability, four nodes in a chain

Every rotational ability (slots 1-5 of every class: 15 trees for the three classes) has exactly one tree of four nodes,
taken in this order:

| # | Kind | What it does | Rule |
|---|---|---|---|
| 1 | `rank` (Rank II) | Integer deltas to the ability's existing effect numbers | Numbers only, never function. Not a choice, so it can never be a trap pick |
| 2 | `enhanced` | One function change: adds an op or a tag, changes the shape or the targeting | Must change function (validator: at least one of `effectsAppend`, `tagAdd`, `tagRemove`, `shape`) |
| 3 | `choice` | The ability's BIBLE s6 fork: pick one option; swapping is free on the screen | Its options are the class's existing `talents` entry for that ability, by reference |
| 4 | `rank` (Rank III) | Integer deltas, as node 1 | Numbers only |

- **Q, E, R and F have no trees.** Matt said "the initial abilities"; Keyrun reads that as the five rotational
  buttons. The interrupt, defensive, utility and movement keys stay fixed so the kick economy (one 24-second kick per
  seat, BIBLE s2, s9.3) never drifts with progression. (Call 3 in s5.)
- **Power is bounded.** A tree has four nodes and the class has twenty. When the trees are full, ability power never
  moves again, the same end that Tier 10 gives gear (BIBLE s7 "Ten is the end").
- **Composition order** (one deterministic order, fail closed): base ability, then the chosen fork option's `patch`
  (today's `TalentPicks.ApplyPatch`), then the enhanced node's edit, then each rank node's `effectDelta`, then the gear
  Facets (today's `GearFacets` apply, which already "stacks on the talented ability", `game/sim/Keyrun.Sim/Run/RunStart.cs:18-19`).
  Ranks go after the fork so a fork that replaces the whole `effects` list (Rend, Focused Break) still gets its ranks.
  An `effectDelta` that finds no op of its kind after the fork patch is a content error at load time, never a silent
  skip (Invariant 4).

### 1.3 How nodes unlock: tree points from completions, gates from the key reached

- **One tree point per completion** with that class (a completion is defined in 2.2: timed, or completed past par;
  never a timer-cap end, a wipe end or an abandon). Points are per character and per class: playing the Cinder grows
  the Cinder's trees (D66 lets the human play any class). The cap is 20 points per class, one per node.
- **Key gates** use the highest key level the character has **completed** with that class:

| Node | Point cost | Requires | Highest completed key |
|---|---|---|---|
| Rank II | 1 | none | any |
| Enhanced | 1 | Rank II | any |
| Choice (the fork) | 1 | Enhanced | 4 or higher |
| Rank III | 1 | Choice | 7 or higher |

- **Respec is free, always, on the screen** (BIBLE s6). Points can be pulled out of any node and spent elsewhere,
  and the choice node's option swaps freely (Diablo III runes). Pulling a node refunds every node after it in the
  chain. A key gate, once met, stays met.
- **Never a score.** Points, gates and the highest completed key live in the private save (`progression.json`). No
  screen shows another player's tree, points or key; matching stays on key level only (BIBLE s10). The highest
  completed key is not the private rating, and neither is shown to anyone else.
- **Pace.** A fresh character has its first fork after two completions and a key-4 completion (Rank II, Enhanced,
  Choice on one ability). Filling all twenty nodes needs twenty completions and a key-7 completion: about six hours at
  15-20 minutes a dungeon (D65). Tuning that pace is call 2 in s5.
- **Bots and tests.** Bot seats and the fork parity check run with a fully unlocked tree (`TreeState.AllUnlocked`, 4.2)
  so balance is judged on the finished kit, the same way the parity check judges forks today (BIBLE s6).

### 1.4 The full example: Warden `Strike`

Base on main (`content/data/abilities/warden.json`, `warden.strike`): damage 80 physical, +15 Momentum, Sundered Window
60 ticks on target, GCD 30, range 3000 mm, and **no threat op**, so today Strike generates no threat at all (the sim
adds threat only through explicit threat ops, `game/bots/Keyrun.Bots/Roles/TankEngage.cs:11-12`).

| Node | Name | Edit (exact) | Strike after this node (with no fork picked) |
|---|---|---|---|
| Rank II | Rank II | `damage.amount +15` | 95 damage, +15 Momentum, Sundered 60 ticks, no threat |
| Enhanced | Provoking Strike | append `{"op": "threat", "multiplierBp": 15000}` | as above, and Strike now holds what it hits: threat at 150% of its damage, Guard Break's multiplier |
| Choice | Rend / Riposte / Warcry | the three `warden.strike` options in `content/data/classes/warden.json`, unchanged | Rend: Sundered 100 ticks. Riposte: no GCD after Brace. Warcry: 170/1024 cone, Sundered on two |
| Rank III | Rank III | `damage.amount +15`, `resource.delta +5` | 110 damage, +20 Momentum |

Why these nodes: the enhancement turns the Warden's builder into a single-target pickup, a real tank function that the
kit lacks today (Guard Break and Reckoning hold threat; Strike does not). The ranks add 37.5% to Strike's damage and a
third more Momentum per press at Rank III, so a full Warden reaches its 100-Momentum Reckoning in five Strikes instead
of seven. Rend with every node: damage 110, +20 Momentum, Sundered 100 ticks, threat 15000 bp.

The tree as it would ship in `game/content/data/trees/warden.json` (the C#-only data tree; it shares the `content/data/`
logical namespace through the second `EmbeddedResource` glob in `game/content/Keyrun.Content/Keyrun.Content.csproj`,
so it needs no csproj change):

```json
{
  "classId": "warden",
  "trees": [
    {
      "abilityId": "warden.strike",
      "nodes": [
        {
          "id": "warden.strike.rank2",
          "kind": "rank",
          "name": "Rank II",
          "text": "Strike deals 15 more damage.",
          "requires": [],
          "minKey": 0,
          "edit": { "effectDelta": [ { "op": "damage", "field": "amount", "delta": 15 } ] }
        },
        {
          "id": "warden.strike.enhanced",
          "kind": "enhanced",
          "name": "Provoking Strike",
          "text": "Strike also generates threat: 150% of its damage. The Warden holds what it Strikes.",
          "requires": [ "warden.strike.rank2" ],
          "minKey": 0,
          "edit": { "effectsAppend": [ { "op": "threat", "multiplierBp": 15000 } ] }
        },
        {
          "id": "warden.strike.choice",
          "kind": "choice",
          "name": "Strike fork",
          "text": "Pick one: Rend, Riposte or Warcry. Swapping is free on the screen.",
          "requires": [ "warden.strike.enhanced" ],
          "minKey": 4,
          "optionsFrom": "talents"
        },
        {
          "id": "warden.strike.rank3",
          "kind": "rank",
          "name": "Rank III",
          "text": "Strike deals 15 more damage and builds 5 more Momentum.",
          "requires": [ "warden.strike.choice" ],
          "minKey": 7,
          "edit": {
            "effectDelta": [
              { "op": "damage", "field": "amount", "delta": 15 },
              { "op": "resource", "field": "delta", "delta": 5 }
            ]
          }
        }
      ]
    }
  ]
}
```

The other fourteen trees are content work for the implementation lane: same shape, numbers tuned against the
fully-unlocked bots at keys 2 and 10, as the fork parity check already runs (BIBLE s6).

### 1.5 Tree schema (validator rules)

One file per class, `game/content/data/trees/<classId>.json`. Fails closed: every rule below is a load error naming the
file and node, never a warning.

| Field | Type | Rule |
|---|---|---|
| `classId` | string | A class in `content/data/classes/` |
| `trees` | array | Exactly one entry per rotational ability of the class (`abilities.rot1` to `rot5`), no other ability |
| `trees[].abilityId` | string | The rotational ability the tree grows |
| `trees[].nodes` | array of 4 | Kinds exactly `rank`, `enhanced`, `choice`, `rank`, in that order |
| `nodes[].id` | string | Unique across all trees; convention `<abilityId>.<rank2/enhanced/choice/rank3>` |
| `nodes[].name`, `nodes[].text` | string | Non-empty; the text names the numbers it changes (the tooltip, BIBLE s7 "no trap choice") |
| `nodes[].requires` | string[] | Empty for the first node; else exactly the previous node's id |
| `nodes[].minKey` | int | 0 to 20; non-decreasing along the chain |
| `nodes[].edit` | object | `rank`: only `effectDelta`. `enhanced`: at least one of `effectsAppend`, `tagAdd`, `tagRemove`, `shape` (the existing GearPatch vocabulary, `game/content/Keyrun.Content/Model/GearFacets.cs:33`), and no `effectDelta`. `choice`: no `edit` |
| `effectDelta[]` | `{op, field, delta}` | `op` an effect op name, `field` an integer field of that op, `delta` a non-zero integer. Applied to the first op of that kind |
| `nodes[].optionsFrom` | `"talents"` | `choice` only: the class's `talents` entry whose `abilityId` matches; that entry must exist |

Cross-checks at load time: every fork option of the ability composed with both rank nodes still has an op for every
`effectDelta` (Focused Break drops Guard Break's `resource` op, so a Guard Break rank may not delta `resource`); no
delta takes a number below zero; a tree never names Q, E, R or F.

---

## 2. Gear by completion: the completion roll

### 2.1 The rule

On a completed run, **every player in the run gets one roll** on the completion table. The roll **can give nothing**.
There is **no loot cap** (no fixed number of items per party, unlike WoW's end-of-dungeon chest) and **no trading**:
each roll is personal, the item it gives is bound to the player who rolled it, and nobody can need, greed, pass or
hand it over. This replaces BIBLE s7 "End of every run: choose one of three".

### 2.2 What counts as a completion (defer to WoW Mythic+)

A **completion** is a run that reaches the completion rule of BIBLE s9.5 (100% forces and the final boss dead, in
either order; R-MULTIBOSS-1 for multi-boss dungeons).

| Run end | `RunResult` on main | Completion? | Table | Tree point | Ash |
|---|---|---|---|---|---|
| Completion inside par | `Timed` | yes | timed | +1 | yes |
| Completion past par (the key is "depleted") | `Depleted`, not out of time | yes | depleted | +1 | yes |
| Timer cap (2x par, `Timer.CapParBp`) before completion | `Depleted`, `RunEndTitle` out of time | no | none | 0 | yes |
| Unrecovered full wipe (layout-less runs only) | `Depleted` | no | none | 0 | yes |
| Left mid-run | `Abandoned` | no | none | 0 | 0 (D45) |

**Recorded call (defer to WoW):** in WoW Mythic+ a key finished over time still completes and still opens the
end-of-dungeon chest; timing it is what upgrades the key. Keyrun keeps that shape: a depleted key still completes and
still rolls. Timing it gives the better table (a higher drop chance). Verify WoW's current out-of-time loot rule against
the live game before tuning, the same caveat BIBLE s8 puts on its thresholds. The table split by result is Keyrun's own
number, not WoW's.

### 2.3 The roll table

Each roll is four draws in a fixed order (the first is new; the other three are today's `DrawItem` order,
`game/sim/Keyrun.Sim/Encounter/Rewards.cs`):

| Draw | What | Timed | Depleted (completed past par) |
|---|---|---|---|
| 0. Drop | `NextInt(10000) < dropBp`; otherwise the roll is **nothing** and the next three draws are skipped | `dropBp` 6000 (60%) | `dropBp` 3500 (35%) |
| 1. Slot | Uniform over the five slots | 1/5 each | same |
| 2. Facet | Uniform over the rolling player's class pool for that slot (`content/data/facets/<class>.json`) | 1/pool size | same |
| 3. Tier | Uniform over the key's tier band, `Rewards.TierBand` unchanged: band `b = min(8, floor((key-2)/2) + 1)`, tiers `max(1, b-1)` to `min(8, b+1)`; tiers 9-10 open at key 15 and up for either result (f-baeb031a) | the band | the band |

- **Every item still has one Facet and a Tier** (BIBLE s7), and starts with two crafting charges.
- **Loot quality still comes from the key level** (f-baeb031a): the tier band is a function of the key only. Timing
  pays through the key jump and the drop chance, never through a better tier band.
- **Per-player seed.** Seat `s` rolls from `Prng.SeedState(unchecked(EndOfRun.RewardSeed(run) * 31 + s))`, so every
  roll is a pure function of (run seed, end tick, seat) and replays exactly (Invariant 1).
- **The Seed bonus.** A Combo landed in the Seed's death Window (BIBLE s9.6, `SeedLoot.cs`) gives every player a
  **second independent roll** on the same table (seed `* 31 + s + 16`). It is still a roll and can still give nothing.
- **Bots roll too.** Bot seats roll by the same rule and the panel shows their result, so the party sees that everyone
  rolled. Bots keep their fixed loadouts (`BotGearTests`); a bot's roll changes no save.
- **Expected pace, for tuning:** a timed run drops an item 60% of the time, one slot in five, so a given slot sees a
  new item about once every 8 timed runs. With no guarantee, a slot can go many runs without an upgrade; that is the
  RNG Matt asked for, and the tree point is what every completion still moves (2.5).

### 2.4 Taking a drop

- The roll result is shown on the **completion roll screen** (s3). A drop is one card: slot, tier, Facet text, and an
  "upgrade" marker when `Rewards.IsUpgrade` says so (higher tier, or a new Facet at the same tier).
- The player picks **Equip** or **Leave**. Equip replaces the slot's item, as `Progression.Take` does today. Leave
  discards it. There is no stash, no bag and no salvage (BIBLE s1, s7): a drop not equipped is gone.
- A drop is never forced on the player and never auto-equipped, so a lower tier with a wanted Facet is a real choice,
  not a trap.

### 2.5 What every completion still moves

Matt's 2026-10-20 rule "every clear moves something visible: choose-one-of-three always includes an upgrade or
Ascension progress" is superseded in its guarantee: no roll is a guaranteed upgrade. What every completion still moves,
visibly, on the screen and the last-3-runs strip:

1. **Ash**, always (BIBLE s7, Hades).
2. **One tree point**, until that class's twenty nodes are full.
3. **The roll line**: "weapon tier 4, Keen Edge (equipped)", "armor tier 3, Mired Ground (left)" or "nothing this time".
4. **At the gear cap** (all five slots at tier 10), each completion adds one **Ascension mark** automatically. This
   replaces the Ascension card the choice of three carried (`Rewards.WithAscensionCard`). Rolls continue at the cap, so
   the Facet chase at tier 10 (BIBLE s7 "Best-in-slot is a specific Facet set at Tier 10") goes on.

### 2.6 What this replaces

| Choice of three (main) | Completion roll (R-PIVOT-1) |
|---|---|
| Three cards per run, the human picks one | One roll per player per completion; the result may be nothing |
| One card is always a strict upgrade (`RollRewardChoices` guarantee, D32 increment 2) | No guarantee |
| Offered on every finished run, timed or depleted, including the timer cap and a wipe (`EndOfRun.Offer`) | Only on a completion; the timer cap and a wipe bank Ash only |
| At the cap, the third card is an Ascension card | At the cap, every completion adds an Ascension mark |
| The run result does not change the roll | Timed rolls at 60%, depleted at 35% |
| Only the human seat sees cards | Every seat rolls and the panel shows each result |

---

## 3. The section 1 loop text

**Changes.** BIBLE s1's loop line becomes: Screen -> Queue -> Dungeon -> **Completion roll** -> Screen. The run-end
panel's choice of three becomes the completion roll screen: one row per seat (class, result line, the drop card or
"nothing this time"), Equip and Leave on the human's own card, Ash and the tree point under it, then Continue back to
the screen. The screen gains a **Trees** view on the existing Build tab (`game/src/Hud/Screen.TabBuild.cs`,
`TalentPanel.cs`): the five chains, points free, key gates, and the fork as the choice node.

**Unchanged.** Screen, Queue, ready check, the dungeon and everything in it (BIBLE s2-s4, s8, s9), the key upgrade
tiers, Ash, crafting, the private rating, the last-3-runs strip, Ascension after the cap, "nothing exists outside this
loop", and the dwell target for a returning player.

**Other bible lines now stale, not edited in this PR** (the brief limits the bible diff to s1 and s7): s8 "Chest tiers"
("The one-of-three choice at the end of a run is unchanged"), s9 principle ("the end-of-run choice"), s9 Decisions
("Choose-one-of-three stays"), s9.6 the Seed ("grants bonus loot", now a second roll, 2.3), s9.8 ("the end-of-run
choice of three"), s10 last-3-runs strip ("what the clear moved"), s12 ("forks not trees", "per-run choice of three",
"every clear moves something visible"). STATE.md INTENT quotes the old s1 and s7 lines verbatim. The orchestrator owns
those edits.

---

## 4. Migration

### 4.1 What implements the choice of three and facets on main

| File | What it does today | Pivot change |
|---|---|---|
| `game/sim/Keyrun.Sim/Encounter/Rewards.cs` | `RollRewardChoices` (three cards, the upgrade guarantee), `WithAscensionCard`, `TierBand`, `DrawItem`, `IsUpgrade`, the placeholder `FacetPools` | Add `CompletionRoll.Roll(seed, key, result, facetPools)` reusing `TierBand` and `DrawItem`; delete the guarantee and the Ascension card after the swap; `IsUpgrade` stays (the card marker) |
| `game/sim/Keyrun.Sim/Run/EndOfRun.cs` | `Offer`: three cards for any finished run, empty when abandoned | `Rolls(run, poolsBySeat)`: one result per seat, none unless completed (2.2) |
| `game/sim/Keyrun.Sim/Encounter/RunEndTitle.cs` | Tells a timer-cap end (out of time) from a completion past par | Source of the completion test in 2.2; no change |
| `game/sim/Keyrun.Sim/Encounter/SeedLoot.cs` | One bonus-loot entry for a Combo in the Seed Window | Bonus becomes a second roll per seat |
| `game/sim/Keyrun.Sim/Run/Ash.cs` | `Progression` (Equipped, Talents, History of `RunRecord`), `Take`, `RunDelta.Text` | Add `TreePoints` and `TreeNodes` per class and `HighestCompletedKey`; `RunDelta` gets the roll line |
| `game/sim/Keyrun.Sim/Run/Ascension.cs` | Marks from the Ascension card | A mark per completion at the cap |
| `game/sim/Keyrun.Sim/Run/ProgressionSave.cs` | Save `Version = 1` with `talents` | Version 2 with `trees`; a v1 save loads with points = its completions in `History` and keeps each fork pick its gates allow |
| `game/sim/Keyrun.Sim/Run/RunStart.cs`, `RunOptions.cs` | Base kit, then the talent loadout, then gear Facets | Base, fork, enhanced, ranks, Facets (1.2) |
| `game/content/Keyrun.Content/Model/Classes.cs`, `TalentPicks.cs`, `Loading/Rules/ClassesRule.cs`, `Model/GearFacets.cs` | Fork options and their patch; Facet patches | New `Model/Trees.cs` and `Loading/Rules/TreesRule.cs` (1.5); `effectDelta` added beside the GearPatch vocabulary |
| `game/src/Hud/RunEndPanel.cs`, `RunEndCardLines.cs`, `RunEndRewardsProbe.cs` (+ `.tscn`), `RunEndBonusSceneTest.ps1`, `RunEndPanelOpaqueSceneTest.ps1` | The three-card panel and its scene tests | The per-seat roll screen with Equip and Leave |
| `game/src/Hud/Screen.cs`, `Screen.Gear.cs`, `Screen.TabCharacter.cs`, `Screen.TabBuild.cs`, `TalentPanel.cs`, `AscensionReadout.cs` | "Since last run", gear, the talent panel, Ascension | Roll line, Trees view, auto marks |
| `game/src/HostCore/CharacterSheet.cs`, `Lobby.cs`, `QueuedKit.cs` | Character sheet and the queued kit | Queued kit carries the tree state |
| `tools/Keyrun.Playtest/Policy.cs` (`talents`) | Any fork, any time | Bots: fully unlocked; a guided guide may pin a fork (PLAYTESTER-GUIDE.md) |

Tests that pin the choice of three and must be rewritten by the lane that swaps it (never relaxed to pass):
`game/sim/Keyrun.Sim.Tests/EndOfRunChoiceTests.cs`, `Encounter/RewardsTests.cs`, `Encounter/RewardsDistinctFacetsTests.cs`,
`Encounter/RewardsContentPoolTests.cs`, `Encounter/RewardQualityFromKeyLevelTests.cs`, `Encounter/SeedLootAwardTests.cs`,
`AscensionTests.cs`, `AscensionMarksTests.cs`, `AscensionReadoutTests.cs`, `RunEndCardLinesTests.cs`,
`AbandonedRunGetsNothingTests.cs`, `ProgressionSaveTests.cs`, `ProgressionTalentRestartTests.cs`,
`game/content/Keyrun.Content.Tests/RewardCardTierBonusTests.cs` (the card body in `Model/Gear.cs:88` stays as the drop
card's body), and
`game/bots/Keyrun.Bots.Tests/RewardDropFacetsTests.cs`, `HostRunGearTests.cs`.

### 4.2 The smallest implementation order

Each step is one PR, red test first, and leaves main playable.

1. **Roll, sim only.** `CompletionRoll.Roll` plus tests: the drop rate converges to 6000 and 3500 bp over 10000 seeds;
   the same (seed, seat) gives the same result; tier stays inside `TierBand`; a timer-cap end and an abandon give no
   roll. Nothing calls it yet.
2. **Swap the end of the run.** `EndOfRun.Rolls` and the run-end panel show the roll with Equip and Leave; the choice of
   three, its guarantee and the Ascension card are deleted with their tests rewritten (4.1). Seed bonus as a second roll.
   Ascension mark per completion at the cap.
3. **Tree data.** `Trees.cs`, `TreesRule.cs`, the `effectDelta` apply, and `game/content/data/trees/warden.json` with the
   s1.4 Strike tree. Validator tests from 1.5.
4. **Trees for every class.** The other fourteen trees as data; the fork parity check runs with `TreeState.AllUnlocked`.
5. **Progression.** Points, gates, `HighestCompletedKey`, save v2 with the v1 load path; the kit composes in the 1.2 order.
6. **Screen.** Trees view on the Build tab; points free; respec.
7. **Capture for Matt** (D15): one completion with a drop, one with nothing, and a tree unlock on the screen.

Steps 1-2 deliver the gear half of the pivot alone; steps 3-6 the trees. They are file-disjoint after step 2 except
`Ash.cs` (steps 2 and 5), which go in order.

---

## 5. Calls Matt may overrule

1. **The fork is the tree's choice node, gated at key 4.** Today any fork is pickable on a fresh character; after the
   pivot a fresh character has the plain kit until a key-4 completion.
2. **Pace: one point per completion, twenty per class, Rank III at key 7.** About six hours to a full tree.
3. **No trees on Q, E, R or F.** "Initial abilities" is read as the five rotational buttons.
4. **Drop chance 60% timed, 35% depleted, and no roll on a timer-cap end.** A depleted key still completes and rolls
   (WoW); the split is Keyrun's own number.
5. **The visible-progress guarantee shrinks to Ash plus a tree point**; a drop is never guaranteed, and a drop you leave
   is gone (no stash).
