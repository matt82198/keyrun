# Ability trees: how each starting ability grows, and why the tree ends two-button play

Status: Design, 2026-10-23; pending Matt's read; no code.

PURPOSE: decide the Diablo-style improvement tree of every rotational ability precisely enough that S2 lanes can write
the red tests and the data. It builds on design/PROGRESSION-PIVOT.md (R-PIVOT-1), which already fixed the four-node
chain, the tree points and the key gates. This file keeps those and changes one thing: **the enhanced node of every
tree is an interaction node** (s1.2). That is the part that fixes the spam.

Matt, 2026-10-22 (G2.2), verbatim: "root cause why they spam one ability, the initial abilities will be like diablo...
re pivot design.... abilities will have improvement trees, unlocked by progression, and gear will be dropped by
completion. There is no guaranteed improvement at the end of every dungeon, its still RNG, its just that everyone gets
a chance to roll".

**Root cause of the spam, from content on main.** Each class has a two-button loop that pays for itself and that nothing
punishes. Warden: Strike and Guard Break both have no cooldown and no cost, both build 15 Momentum, and Strike sets the
Sundered Window that Guard Break pays off (`content/data/abilities/warden.json`). Cinder: Ember Bolt and Flare have no
cooldown and build 20 and 25 Heat, and Detonate (cooldown 60) is the only spender a two-button player needs
(`cinder.json`). Chanter: Cadence has no cooldown and sets Attuned, and Chorus or Dissonance pays it off
(`chanter.json`). The enemy half of the fix is the toolkit contract (enemies that kill a player who ignores E, F and
R, TOOLKIT-CONTRACT s0.5). The tree half is this file: as an ability grows, it starts to **read the state another
button leaves**, so the best rotation uses four or five buttons.

Rules this file stays inside: ten keybinds and five rotational abilities, frozen (BIBLE s2, D4; a node never adds a
button); determinism (keyrun CLAUDE.md invariant 1: every condition is read at resolve time from sim state); free
respec (BIBLE s6); trees on slots 1-5 only (PROGRESSION-PIVOT s5 call 3); Ash buys only cosmetics; power stops at full
trees and Tier 10, then Ascension (BIBLE s7).

## 1. Tree shape
### 1.1 Precedents
| Game | Mechanic | What Keyrun takes |
|---|---|---|
| Diablo II | Skill points, plus **synergies**: points in one skill strengthen another | A node can make one button better *because of* another button |
| Diablo III | Runes: one active rune per skill, unlocked by level, swapped free | The choice node (the BIBLE s6 fork) swaps free on the screen |
| Diablo IV | Skill tree: base, then **Enhanced** (one function change), then one of two upgrades. Many nodes are **conditional**: "against Vulnerable enemies", "while Fortified" | The chain order, and the enhanced node as a condition on state that another skill creates |

### 1.2 Four nodes per rotational ability (PROGRESSION-PIVOT s1.2, amended in the enhanced row)
| # | Kind | What it changes | Validator rule |
|---|---|---|---|
| 1 | `rank` (Rank II) | Numbers: integer deltas to effect fields the ability already has | Only `effectDelta`, each at most 25% of the base field |
| 2 | `enhanced` | **An interaction**: one `conditional` op (s5.2). When the condition holds at resolve time, the ability gains an effect, and may consume the state | The condition names a state another of the class's nine buttons produces, or a dungeon condition (s1.3). Never the ability's own output |
| 3 | `choice` | The BIBLE s6 fork, by reference to the class's `talents` entry | Unchanged; the fork parity check still judges it |
| 4 | `rank` (Rank III) | Numbers, as node 1 | As node 1 |

Numbers come from ranks; shape and added effects from the fork and gear Facets (s3); interactions, with another
ability or the dungeon (G2.5), from the enhanced node. **No node removes a decision** (BIBLE s0 test 1: five buttons, each with a real decision). Each rule is a load error:
1. A rank or enhanced node never lowers `cooldownTicks`, `castTicks`, `gcdTicks` or `cost` below the base. Those are
   where the decisions are. A free or instant press is fork work, judged by the parity check (Riposte is the precedent).
2. A node never gives one ability both a setup `window` and a `payoff`. A Combo always takes two presses (BIBLE s4).
3. An enhanced node never applies an effect that another button exists to apply (Strike never grants Vigil's
   `bracedSelf`). It reads the state; it never automates the button.
4. Across a class, the five enhanced nodes read states from **at least two other rotational abilities**. That is the
   structural guarantee of a third and a fourth button.
5. A `consume` removes only what the condition read, so using a state is a decision (spend it now, or keep it for what
   it was for).

### 1.3 The conditions (closed list, all deterministic)
| Condition | Reads | Example |
|---|---|---|
| `selfHas` `{effect or window id}` | The caster's active effects or Windows | Strike while standing in the Warden's own Bulwark (`braced`) |
| `targetHas` `{effect or window id}` | The primary target's effects or Windows, a partner class's Window included | Detonate on a `burning` target (Cinderfield) |
| `selfResourceAtLeast` `{n}` | The class resource before the cost is paid | Cold Snap at 80 Heat or more |
| `targetCasting` | The target has a cast bar up (dungeon, G2.5). Never adds an interrupt or stun: the kick stays Q's (BIBLE s9.3) | A Flare that punishes a caster mid-cast |
| `hitCountAtLeast` `{n}` | Enemies the shape hit on this cast (pack size, G2.5) | A cone that pays off on a big pull |

## 2. Unlocks: deterministic progress, RNG gear (PROGRESSION-PIVOT s1.3, kept)
- **Currency: one tree point per completion with that class.** A completion is a timed run or one completed past par
  (PIVOT s2.2). A timer-cap end, a wipe or an abandon gives no point. The cap is 20 points per class.
- **Gates: the highest key completed with that class.** Choice needs key 4 or higher, Rank III key 7 or higher. Rank II
  and Enhanced are ungated, so a new player gets an interaction node after two completions, before the fork.
  Interactions come first because they fix the spam, and Diablo IV also lays the simple layer before the deep one
  (research/04:55-62).
- **What does not unlock nodes.** Completion-roll items never carry a node, and no roll result changes a tree. Ash never
  buys a node. Ascension never adds one: power stops at 20 nodes and Tier 10, and Ascension is marks and cosmetics only.
- **Unlocks are deterministic; gear is RNG.** A tree point is the one thing every completion is guaranteed to move, so a
  player always has a path forward, even through a run of empty rolls. Matt's "its still RNG" applies to the roll only.
- **Dark and Darker precedent:** character level comes only from playing, and perk slots open at fixed levels. Perks and
  skills swap freely in the lobby, and gear comes only out of the dungeon, by chance. Keyrun maps it directly:
  completions for experience, key gates for level gates, free respec on the screen, gear from the roll. All of it is
  private (BIBLE s10).

## 3. Trees and Facets: Facets stay gear-borne modifiers on top of the tree
**Decision:** Facets do not become tree nodes. A Facet stays a gear-borne modifier, applied **after** the full tree in
the composition order (base, fork, enhanced, ranks, then Facets; PIVOT s1.2; `RunStart.cs:18-19` already stacks Facets
on the talented ability). Diablo IV Legendary Aspects are this shape: gear that changes how a skill works, layered on
the skill tree. Keeping the two apart keeps one RNG source (the roll) and one deterministic source (the tree), which is
the s2 promise.
- Facets keep "change function" and keep their reach over all nine abilities (`content/data/facets/warden.json` patches
  Shield Bash and Taunt, which have no tree). Facets may also carry a `conditional`, so an item can add a second
  interaction: a deeper chase with no new vocabulary.
- **Validator:** a Facet's patch may not equal a tree node's edit on the same ability, so a drop can never duplicate a
  node the player owns (BIBLE s7 "never a trap choice"). The tooltip names both layers.
- **The BIBLE s7 sentence that changes.** After "**Facets change function** (Diablo IV's lesson, research/05): a Facet
  retags an ability's Window, adds a second Payoff to a neutral ability, changes a floor patch's shape, or alters the
  resource loop." append: "A Facet is a gear-borne modifier applied after the ability's improvement tree
  (design/ABILITY-TREES.md s3). It may change any of the nine abilities, may add an interaction, never repeats a tree
  node, and goes with the item. The tree is what the character keeps." The orchestrator owns the BIBLE edit.

## 4. Anti-spam: one worked example per class
Base kit on main; two enhanced nodes turn the two-button loop into a four-button one. The class's other three enhanced
nodes go in the tree PR under the same rules; the bots' anti-spam test (s5.5) proves the result.
### 4.1 Warden: Bulwark and Vigil become part of the loop
Main: Strike (no cooldown, +15 Momentum, Sundered 60 ticks), Guard Break (no cooldown, +15 Momentum, threat 150%,
payoff), Bulwark (cooldown 200, `braced` for 160 ticks in a 2500 mm circle), Reckoning (100 Momentum, 400 damage),
Vigil (50 Momentum, `bracedSelf` for 100 ticks).

| Node | Name | Edit |
|---|---|---|
| `warden.strike.enhanced` | Braced Strike | `conditional` `selfHas braced`: `resource +15` (Strike inside Bulwark builds 30 Momentum) |
| `warden.reckoning.enhanced` | Vigilant Reckoning | `conditional` `selfHas bracedSelf`, consume: `damage +200` (spends the Vigil shield for a 600 hit) |

The loop: Bulwark (button 3) first, then Strike and Guard Break inside it, with Strike building twice as fast. With 150
Momentum, Vigil (button 5) and then Reckoning (button 4) is the big hit. **The decision:** spend the Vigil shield on
Reckoning, or keep it for the tank buster the toolkit contract puts on every pull (s2.2). A Warden who presses only
Strike and Guard Break reaches Reckoning at the old pace and never lands the 600 hit. **This replaces PIVOT s1.4's
Provoking Strike** (Strike plus threat). That node had no interaction, and it made the two-button loop stronger, because
Strike would also hold threat. The PIVOT's Rank II and Rank III numbers and the fork stand.

### 4.2 Cinder: Cinderfield and Cold Snap become part of the loop
Main: Ember Bolt (+20 Heat), Flare (+25 Heat, Kindled), Cold Snap (cooldown 120, vents all Heat, stun 20), Detonate
(30 Heat, 250 damage, cooldown 60), Cinderfield (cooldown 200, `burning` for 160 ticks). Heat caps at 100
(`heat-overheat`).

| Node | Name | Edit |
|---|---|---|
| `cinder.detonate.enhanced` | Fieldburst | `conditional` `targetHas burning`, consume: `damage +150` (400 on a target in Cinderfield) |
| `cinder.coldSnap.enhanced` | Vented Heat | `conditional` `selfResourceAtLeast 80`: `damage +200` (Cold Snap at high Heat becomes a dump) |

Cinderfield (button 5) now sets up Detonate, and Cold Snap (button 3) now competes with Detonate for Heat. **The
decision:** spend 30 Heat on Detonate now, or bank toward 80 and Vent, with overheat at 100 as the risk. Ember Bolt and
Flare plus Detonate still works, just measurably worse, which is the bar (keyrun CLAUDE.md invariant 2, fun measured).

### 4.3 Chanter: Hymn and Refrain become part of the loop
Main: Cadence (no cooldown, Attuned 60 ticks on target), Refrain (cooldown 40, flips `verse` 0/1, the stance), Chorus
(cooldown 120, payoff in 5000 mm), Dissonance (cooldown 60, 130 radiant, payoff), Hymn (cooldown 200, Hymn Window 160
ticks in 2200 mm).

| Node | Name | Edit |
|---|---|---|
| `chanter.chorus.enhanced` | Hymnal Chorus | `conditional` `selfHas hymn`: append `heal` 60 to every ally Chorus reaches |
| `chanter.dissonance.enhanced` | Discordant Verse | `conditional` `selfResourceAtLeast 1`, then `resource -1`: `damage +130` (spends the verse) |

Hymn (button 5) has to be down before Chorus is worth pressing, and Dissonance spends the stance, so Refrain (button 2)
comes back every cycle. **The decision:** spend the verse on damage, or keep the stance its heal needs. This fits next
to R-KICK-WARD-1 (a Dissonance silence is correct play), and the kick stays on Q.

## 5. Data shape and the first slice
### 5.1 Files
- **Tree data:** `game/content/data/trees/<classId>.json`, as PIVOT s1.4-s1.5 specify. The brief's `content/data/trees/`
  is in the frozen TypeScript tree (keyrun CLAUDE.md: `content/data/*.json` stays verbatim through phase 4A), so the
  C#-only data tree is the home. It needs no csproj change (the second `EmbeddedResource` glob). Node fields are PIVOT
  s1.5's (`id`, `kind`, `name`, `text`, `requires`, `minKey`, `edit` or `optionsFrom`), under the s1.2 rules above.
- **Model and validator:** new `game/content/Keyrun.Content/Model/Trees.cs` and `Loading/Rules/TreesRule.cs`.

### 5.2 How the sim applies a node: reuse the patch mechanism
A node's `edit` is a GearPatch (`effectsAppend`, `tagAdd`, `tagRemove`, `shape`;
`game/content/Keyrun.Content/Model/GearFacets.cs:33`) plus `effectDelta` for ranks. It goes through the apply path forks
and Facets already use (`TalentPicks.ApplyPatch`, then the Facet apply that `game/sim/Keyrun.Sim/Run/RunStart.cs`
calls), in the PIVOT s1.2 order, so the tree compiles into the ability before the run starts. At resolve time the sim
never asks what a player has unlocked; it runs a plain ability, so replays stay a function of (seed, command stream).

**One new effect op, `conditional`:** `{"op": "conditional", "if": {"kind": "selfHas", "id": "braced"}, "consume":
false, "then": [ ...existing ops ]}`. It goes in the op list in `game/sim/Keyrun.Sim/Combat/Types.cs:49-85`, with the
resolver beside it, and the condition kinds in s1.3 are the only new sim logic. It is generic on purpose (D46). Main
expresses conditions only as hard-coded tags read in code (`riposte:free-after-brace`), one rule per tag, and the
trees would need fifteen. An `if` whose id no class can produce is a load error (Invariant 4).

### 5.3 What ProgressionSave persists
`ProgressionSave.Version` goes from 1 to 2 (`game/sim/Keyrun.Sim/Run/ProgressionSave.cs:29`). It adds one canonical
member in ordinal class order: `"trees": {"<classId>": {"earned": n, "bestKey": k, "nodes": ["<node id>", ...]}}`.
- `earned` is the points earned, at most 20. Free points are `earned - nodes.Count`, derived and never stored, so the save
  cannot disagree with itself. `bestKey` is the highest key completed with that class. The fork pick stays in the
  existing `talents` member, unchanged.
- Parse fails closed, like the current codec: an unknown node id, a missing `requires`, a `minKey` above `bestKey`, or
  more nodes than `earned` makes the save unusable, and it is moved aside (`PreserveIfUnreadable`), never overwritten.
- **v1 load path:** v1 history has no class per run, so a v1 save loads with `earned` equal to its timed and depleted
  `History` entries, credited to the class the save last queued. `bestKey` is the highest of those keys, and `nodes`
  starts empty. Fork picks whose new gate is not met are dropped and shown once on the screen. `RunRecord` gains
  `classId` from v2 on.

### 5.4 Where it lives: the Build tab (Dark and Darker's one lobby screen with tabs)
The Trees view sits on the existing Build tab (`game/src/Hud/Screen.TabBuild.cs`, beside `TalentPanel.cs`): five chains
in key order 1-5, free points at the top, key gates on locked nodes, and the fork options at the choice node. Click a
node to take it. Right-click refunds it and every node after it, with no confirm and no cost (BIBLE s6). A node's
tooltip names its condition and the button that feeds it ("Feeds from: Bulwark (3)"), so the screen teaches the
interaction.

### 5.5 S1 tests to write first (red before code)
1. `game/content/Keyrun.Content.Tests/TreesRuleTests.cs`: the four kinds in order; `requires` chains; `minKey` never
   goes down; each s1.2 decision rule as a rejected fixture; an enhanced node reading its own output is rejected; a
   class whose enhanced nodes read fewer than two other rotational abilities is rejected.
2. `game/sim/Keyrun.Sim.Tests/Combat/ConditionalOpTests.cs`: each condition kind fires only when its state holds;
   `consume` removes exactly what it read; the same seed and commands give a byte-identical log.
3. `game/sim/Keyrun.Sim.Tests/Run/TreeCompositionTests.cs`: base, fork, enhanced, ranks, Facets, in order; Rend with every
   node has the Braced Strike conditional and 110 damage; a Facet equal to a node is rejected.
4. `game/sim/Keyrun.Sim.Tests/Run/TreeUnlockTests.cs`: a point per completion only (a timer cap, a wipe and an abandon
   give none); key gates; refund cascade; `TreeState.AllUnlocked`.
5. `game/sim/Keyrun.Sim.Tests/ProgressionSaveTreesTests.cs`: a canonical v2 round trip; the v1 migration; every
   fail-closed case in s5.3.
6. **Anti-spam, measured:** `game/bots/Keyrun.Bots.Tests/TreeAntiSpamTests.cs`. For each class, a fully unlocked bot on
   its full rotation beats the same bot restricted to the s4 two-button loop by at least 15% damage over one seeded
   Hollow Vault segment at keys 2 and 10. It is red until the s4 nodes load and green after. The 15% may be raised with
   a measurement, never lowered to pass.

### 5.6 PR order (one PR each, red test first, main playable after each)
1. `conditional` op in the sim, with test 2; nothing uses it yet.
2. Tree model, `TreesRule`, apply path, `trees/warden.json` (PIVOT ranks, s4.1 nodes), with tests 1 and 3.
3. `trees/cinder.json` and `trees/chanter.json` (the s4 nodes first) and the remaining enhanced nodes of all three
   classes. Test 6 goes green here, and the fork parity check runs on `TreeState.AllUnlocked`.
4. Progression: points, gates, save v2 with the v1 path (tests 4 and 5). This goes after PIVOT s4.2 step 2, because both
   touch `Ash.cs`.
5. The Trees view on the Build tab, with tooltips that name the feeding button.
6. Capture for Matt (D15): a first interaction node unlocking; a Warden pull with Braced Strike and Vigilant Reckoning.

PRs 1-3 are file-disjoint from the completion-roll PRs (PIVOT s4.2 steps 1-2) and can run alongside them.

## 6. Calls Matt may overrule
(1) Enhanced is always an interaction node, replacing the PIVOT's free-form enhancement and Provoking Strike. (2) It is
ungated, so it arrives before the key-4 fork. (3) Facets stay on gear, after the tree. (4) The anti-spam bar is 15%.
