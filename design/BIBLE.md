# Keyrun — Design Bible v1

Written 2026-09-18 from nine research briefs (`research/`) and Matt's decisions (`STATE.md` D1–D9). Every
mechanic here cites the brief that supplies its *reason*; the *design* is Keyrun's own (D6). Marked `[DECIDE]`
where Matt's taste is needed; everything else is locked for Phase 2.

## 0. The three tests (D8)

Every decision in this document passes all three or it is out:

1. **Depth and difficulty.** Classes with real decisions on five buttons; keys that are genuinely hard because
   of what enemies *do*, not how much health they have.
2. **A high key inside a minute.** Open the screen, pick a key, press Queue, be in the dungeon. No gatekeeping,
   no score, no lobby negotiation.
3. **Strangers are chill by default.** The systems remove the reasons people are not: no shareable score to judge
   by, no run ruined by one mistake, backfill instead of abandonment, pings instead of typing.

## 1. The loop

Screen → Queue → Dungeon (timed, one boss) → End-of-run choice of three → Screen. Target run length for the POC
dungeon is 12 minutes at par; a session is "one more run" sized. The screen dwell for a returning player is under
15 seconds (research/08). Nothing exists outside this loop in the POC: no world, no quests, no shop, no stash.

## 2. Controls: the ten-keybind budget (D4)

WASD and mouse-look are the body; they are not keybinds. The ten are:

| Key | Slot | Rule |
|---|---|---|
| 1–5 | Rotational abilities | Exactly five per class, forever. Talents and gear change what they do, never add one. |
| Q | Interrupt | One per class, 24-second cooldown, scarce by design. |
| E | Defensive | One per class. |
| R | Class utility | Dispel, taunt, or stance swap depending on class. |
| F | Movement ability | Dash, leap, or blink. |
| G | Ping | Marks a target or a spot for the party; the only "communication" key. |

Fellowship's 16-slot bar and 45+ bindings produced heroes players called bland (research/09); Diablo IV, Lost Ark
and Hades get their depth from modifiers on a frozen small kit (research/03). Ten is the whole budget, including
pings, so secondary systems can never balloon past the core kit.

## 3. Combat model

- **Fixed tick, integer math, deterministic** (research/07; `sim/CLAUDE.md`). Interrupts resolve by intended
  tick with a fixed tiebreak, never arrival order: a race is never a coin flip.
- **Cast bars are the enemy's language.** Every dangerous enemy action is a cast with a visible bar and a floor
  telegraph (circle, cone, line) drawn ahead of resolution. Shape plus colour, never colour alone (research/06).
- **Soft roles, no trinity lock** (research/03). Every class has real self-sustain and some group utility. A party
  without a "healer" class is harder, never blocked from queueing. The queue matches on key level only.
- **Damage is a pipeline of integers**: base → source modifiers → target modifiers → absorbs → health. Mitigation
  and buffs are basis points. No hidden multipliers.

## 4. Synergy: Windows and Combos (Keyrun's own system)

This is the design's centre and the answer to "more creative than Blizzard": synergy is not a buff you press,
it is a *timing game between two people*.

- **Setup abilities open a Window.** Certain abilities tag their target (or a floor patch) with a **Window**: a
  visible ring that lasts 3 seconds (60 ticks). Each class has exactly two Setup abilities among its five.
- **Payoff abilities close a Window.** Any *other* class's Payoff ability landing inside the ring closes it and
  fires a **Combo**. A class cannot combo its own Window. Each class has exactly two Payoff abilities.
- **Combos are named and specific to the pair of classes.** Three starter classes give three pair-combos plus
  their mirror direction, six named effects total. They are the party's vocabulary ("call Shatter", "hold for
  Steam"). This is GW2's field-and-finisher idea (research/02, /03) rebuilt so the payoff depends on *who* you play
  with, not on a generic element table — which is what Deep Rock gets right and Fellowship missed (research/09).
- **Everything is readable on the floor and in the ring.** The Window ring shows which classes' Payoffs can close
  it by colour band. No addon, no spreadsheet.
- **Encounters are built on Windows.** Bosses and lieutenants use the same rule in reverse: some mechanics can only
  be solved by a Combo landed in the mechanic's own window (section 9). Mechanics require *a tag*, never *a specific
  class by name* (research/06), so every party composition has at least two ways to solve each one.

The ring is also the anti-toxicity device: when the party fails a Combo, the game shows which half of the pair
was late, in the run log, privately. Nobody is guessing who to blame.

## 5. The three starter classes

Each: 5 rotational (two Setup, two Payoff, one neutral), Q interrupt, E defensive, R utility, F movement, G ping.
Resource loops are never repeated across classes (research/03).

### Warden — the anchor (Momentum builder/spender)
- Loop: **Momentum** (0–100) builds on hits, spends on the finisher. Threat is generated by Momentum spent, so
  tanking is a choice, not a spec.
- 1 `Strike` — builder. Setup: applies **Sundered** Window to the target.
- 2 `Guard Break` — builder, cone. Payoff.
- 3 `Bulwark` — plants a stone floor patch that reduces damage taken by allies standing in it. Setup (floor Window).
- 4 `Reckoning` — Momentum finisher, big single hit. Payoff.
- 5 `Vigil` — neutral: convert Momentum to a self-shield; the sustain button.
- Q `Shield Bash` (interrupt) · E `Brace` · R `Taunt` · F `Charge` · G ping.

### Cinder — the detonator (Heat push-your-luck)
- Loop: **Heat** rises per cast. At 100 the next cast is a free **Overheat** that dumps Heat to 0 and applies
  **Chill** (slower casts for 3 s). Riding the edge is the skill.
- 1 `Ember Bolt` — filler, +Heat. Neutral.
- 2 `Flare` — +Heat, applies **Kindled** Window to the target. Setup.
- 3 `Cold Snap` — dumps Heat, clears Chill, freezes the target briefly. Payoff.
- 4 `Detonate` — Payoff; on a Kindled target it is also Cinder's biggest hit.
- 5 `Cinderfield` — floor patch that ticks fire; Setup (floor Window).
- Q `Quench` (interrupt; refunds 20 Heat on a successful kick, tying the kick economy to the resource) · E `Ashen
  Skin` · R `Purge` (dispel a magic effect) · F `Blink` · G ping.

### Chanter — the conductor (Verse stance)
- Loop: always in **Mend** or **Ward** verse; `Refrain` swaps and fires a burst on the swap. Every other ability
  behaves differently per verse: one kit, two characters.
- 1 `Cadence` — Mend: heal over time on target. Ward: shield on target. Setup: applies **Attuned** Window.
- 2 `Refrain` — swap verse; burst heal (to Mend) or burst shield (to Ward). Neutral.
- 3 `Chorus` — Mend: party heal pulse. Ward: party mitigation pulse. Payoff.
- 4 `Dissonance` — damage; Mend: slows. Ward: silences briefly. Payoff.
- 5 `Hymn` — floor patch: Mend heals allies in it, Ward speeds them. Setup (floor Window).
- Q `Rebuke` (interrupt) · E `Aegis` · R `Cleanse` (dispel) · F `Glide` · G ping.

### The six Combos
| Setup by | Payoff by | Combo | Effect |
|---|---|---|---|
| Warden (Sundered) | Cinder | **Shatter** | The Sundered target takes a burst and is stunned 1 s. |
| Warden (Sundered) | Chanter | **Consecrate** | Burst plus a 6-second heal pulse for allies near the target. |
| Cinder (Kindled) | Warden | **Forge** | Burst; Warden gains 40 Momentum. |
| Cinder (Kindled) | Chanter | **Steam** | Burst; a cloud that blinds enemies inside (they miss) for 4 s. |
| Chanter (Attuned) | Warden | **Anthem** | Party gains 15% haste for 6 s. |
| Chanter (Attuned) | Cinder | **Flashfire** | Burst; refunds Cinder 30 Heat and clears Chill. |

Floor Windows (Bulwark, Cinderfield, Hymn) accept any other class's Payoff for a smaller "field" version of the
pair combo, so there is always something to close even when the enemy Window was missed. `[DECIDE]` the numbers.

## 6. Talents: forks on the five (research/04)

- Each rotational ability has one **fork** of two or three mutually exclusive options; 12–15 choices per class
  in total; no shared tree, no percentage-only nodes. Every option changes function, targeting, resource cost, or
  which Window/Payoff tag the ability carries.
- Example (Warden `Strike`): *Rend* (Sundered lasts 5 s instead of 3) / *Riposte* (Strike after a Brace is free)
  / *Warcry* (Strike also applies the Window to two adjacent enemies).
- Respec is free on the screen, always. The screen is a build lab (research/04, /09).

## 7. Gear and progression: bounded, with a real end (D7)

- **Five slots**: Weapon, Armor, Charm, Ring, Keystone. Each item has a **Tier** (1–10) and one **Facet**.
- **Tier is power and it is capped at 10.** Ten is the end. Best-in-slot is a specific Facet set at Tier 10 for
  your build, and it is hard: the last two tiers drop only from boss mechanics done correctly on high keys
  (Monster Hunter's transparent, target-farmable sources — research/05). No item level after 10, ever.
- **Facets change function** (Diablo IV's lesson — research/05): a Facet retags an ability's Window, adds a
  second Payoff to a neutral ability, changes a floor patch's shape, or alters the resource loop. Facets are the
  chase; numbers are just the tier.
- **End of every run: choose one of three** (a vault-lite, per run, not per week — research/01): the options are
  shown with their Facet text and tier; there is never a trap choice (research/05).
- **Crafting is two charges per item**: reroll the Facet from that slot's listed pool, twice, then it is what it is
  (Last Epoch scaled down — research/05).
- **Every run banks Ash, win or lose** (Hades — research/05). Ash buys only cosmetics and titles.
- **After the cap: Ascension.** Higher keys, cosmetics, the key ladder, and Ascension marks on the character. Power
  never moves again. This is the WoW shape Matt named, with the treadmill removed.

## 8. The key, the Clause, and Tempo

- **The key** is a number, 2–20 for the POC. It scales **Tempo**, not health: enemies cast more often, telegraphs
  resolve faster, and pulls gain one extra mechanic at fixed thresholds (research/06 and Blizzard's own scaling
  lessons in research/01). Health scales gently so kills still happen.
- **The Clause**: before queueing, the party is offered three modifiers and picks **one**; exactly one Clause runs
  at a time, never stacked (research/01). Clauses change plans, not stats: *Echo* (every interrupted cast spawns a
  weak add), *Tide* (floor patches drift), *Hush* (silence pulses every 45 s; save the kicks), *Pact* (Combos deal
  more, uncombo'd hits deal less). Every Clause is tested against every class before it ships.
- **The timer and Slack.** The run has a par time. Deaths and wipes cost **Slack** (a soft time bank shown as a
  second bar), not a flat penalty; when Slack is gone the key is *depleted*: you may still finish for the run reward
  and Ash, but not the key upgrade. One mistake never flips a clearable run by itself (research/01).

## 9. The POC dungeon: Hollow Vault

Three teaching pulls, optional trash packs on two branching paths, an enemy-forces bar, and a boss behind a door
the bar opens. Each teaching pull still teaches one thing. The packs and the bar make route choice and pull size
the run's standing decision against the timer (research/01:29-33, research/06:42-46, research/06:55-59).

**Decision (Matt, 2026-10-20).** The earlier shape, "three pulls and a boss, forward-only", was judged too thin.
Matt, verbatim: "the room design is just super lame... it does not even look fun... there is so much more to
mythic+ that makes it fun". His chosen option, verbatim: "Forces + route layer: Keep the boss and the three teaching
pulls, and add 4-6 optional trash packs across two branching paths, plus a 100% enemy-forces bar that must be full
before the boss door opens. Route choice and pull size become real decisions against the timer. Mixed packs give
several kickable casters, and two rooms get floor hazards." This section is that option made concrete. It does not
change par (an open `[DECIDE]`, section 12) or the key's 5/10/15 mechanic unlocks (section 8).

### 9.1 The map

```
Entrance -> Choir room (pull 1) -> Pair hall (pull 2) -> Fork landing
  West: Undercroft Steps (W1) -> Bellows Hall (W2, hazard)                              -> Seed chamber (pull 3)
  East: Reading Gallery (E1) + Cantor's Loft alcove (E2) -> Sluice Gallery (E3, hazard) -> Seed chamber (pull 3)
Seed chamber -> Hollow Door -> Warden arena (boss)
```

- **Branching, no backtracking past a taken branch.** The Choir and Pair gates work as before: each opens when its
  pull is cleared. At the Fork landing the party picks West or East. The first living party member to cross the
  threshold trigger of a branch's first room *seals* the branch. The doorway back into the Pair hall and the other
  branch's doorway close for the rest of the run, and so does the other branch's stair into the Seed chamber.
  Inside the taken branch every room stays open in both directions, and so does the stair between the branch and the
  Seed chamber. A party that skipped too much can walk back to a pack it skipped in its own branch. That walk is a
  time cost the party chose, not a softlock. Nothing reopens a sealed doorway. Dead travel is the most-cited killer
  of a run's fun (research/02:50-54, :113-114), so a walk back only ever undoes a skip the party made itself.
- **Optional packs do not gate.** A pack stands beside the walking line with its **aggro ring** drawn on the floor:
  a 4000 mm grey dashed ring. It is not a danger shape, so it never reads as a telegraph. The whole pack engages at
  once when a living party member enters its ring, or when party damage or a hazard hits any of its members.
  Walking the far wall past an untouched ring is a **skip**. Walking into two rings together is a **chain pull**. The
  read is visual, a radius on the floor, not memorised geography (research/01:106-107).
- **Backfill** (section 10): a joiner arrives at the next teaching pull or pack engage, inside the party's taken
  branch.

### 9.2 Enemy forces

- **The bar.** One **Forces** bar, shown under the timer and Slack bars, fills from 0% to 100%. Forces are integer
  points and **1000 points = 100%**. Each enemy carries a fixed point value from content. The value is credited on
  the enemy's death tick, whatever killed it, hazards included. Forces never go down: a death or wipe costs Slack
  (section 8), never forces. The boss, the Sundered Twins and Echo-spawned adds are worth 0, so nothing in the run can
  be farmed for forces.
- **Point values** (per enemy; a pack's value is the sum of its members):

| Pack | Where | Members (points each) | Pack points |
|---|---|---|---|
| Choir (pull 1) | critical path | 2 Choir Acolyte (40), 1 Choir Cantor (80) | 160 (16%) |
| Pair (pull 2) | critical path | 5 Paired Warrior (52) | 260 (26%) |
| Seed (pull 3) | critical path | 1 Seed elite (180) | 180 (18%) |
| W1 Hound Kennel | West, Undercroft Steps | 4 Hollow Hound (35), 1 Ash Hexer (80) | 220 (22%) |
| W2 Bellows Crew | West, Bellows Hall (hazard) | 2 Vault Brute (90), 2 Bellows Keeper (70), 1 Ash Hexer (80) | 400 (40%) |
| E1 Reading Watch | East, Reading Gallery | 2 Choir Acolyte (40), 1 Drowned Chorister (60) | 140 (14%) |
| E2 Loft Choir | East, Cantor's Loft alcove | 1 Choir Acolyte (40), 1 Choir Cantor (80), 1 Drowned Chorister (60) | 180 (18%) |
| E3 Floodgate Watch | East, Sluice Gallery (hazard) | 1 Vault Brute (90), 1 Choir Acolyte (40), 1 Drowned Chorister (60), 1 Bellows Keeper (70) | 260 (26%) |

- **The arithmetic is the decision.** The three teaching pulls give 60%, so no run reaches 100% on the critical path
  alone and every route needs at least one optional pack. **West** offers 122% in all (60 + 22 + 40). W2 alone exactly
  fills the bar (100%) and W1 alone does not (82%), so West's choice is whether to skip the Kennel and how to take
  the Crew: alone, or chained with the Kennel down the Steps (620 points in one fight). **East** offers 118% in all
  (60 + 14 + 18 + 26). E3 is required (without it East peaks at 92%), plus E1 or E2 (100% or 104%). West is fewer,
  bigger pulls on a short walk. East is more casters in smaller pulls on a longer walk. This is the route puzzle
  against a required trash percentage that Mythic+ route tools solve (research/06:55-59). The ~20% surplus on either
  path is what makes a skip a real choice (research/01:29-33).

### 9.3 Packs and the kick economy

Packs mix melee and casters, so the party splits its attention between "where do I stand" and "who do I kick", and
the ratio changes from pack to pack (research/06:47-50). A party of three has three interrupts per 24 seconds (Q,
section 2), about one kick every 8 seconds. One caster casting every 9-12 s can be kicked every time, which is the
Choir's lesson. Two casters cannot, and a chain pull with four cannot. The cost of a bigger pull is how many uncovered
casts the party chooses to survive (research/01:23-26, research/06:22, :117-121). Every dangerous cast below has a
cast bar and a floor telegraph that differs from the others by shape and colour (section 3; research/06:100-104,
:147-151).

**Kickable casters** (four distinct casts across the dungeon; any class's Q interrupts each one):

| Caster | Cast | Cast time | Cooldown | Telegraph | Effect if it lands |
|---|---|---|---|---|---|
| Choir Cantor (existing) | **Choral Mending** | 80 ticks (4 s) | 200 ticks | unchanged from the Choir | Heals its pack. Left uncovered, the pack outheals the timer. |
| Ash Hexer | **Searing Hex** | 50 ticks (2.5 s) | 200 ticks | 1200 mm violet circle under the target, with an inner ring | A magic burn of 25% of the target's max health over 6 s. Cinder's Purge and Chanter's Cleanse remove it, so a missed kick costs an R, not a death. |
| Bellows Keeper | **Stoke** | 70 ticks (3.5 s) | 240 ticks | 4000 mm ember-orange circle centred on the Keeper, with a toothed edge | Enemies inside gain 30% damage and 20% cast speed for 8 s (**Stoked**). |
| Drowned Chorister | **Undertow** | 60 ticks (3 s) | 180 ticks | 30-degree sea-green cone from the Chorister toward the target | Pulls the target 5000 mm toward the Chorister and roots it for 1 s. Small damage, but in the Sluice Gallery it drags the target into the next jet. |

**Melee** (not kickable; the dangerous swing is telegraphed and avoided, not kicked):
- **Choir Acolyte** (existing, `choir.melee`): filler melee.
- **Hollow Hound**: fast (260 mm/tick), fragile melee. The Kennel's four test cleave and peel for the backline.
- **Vault Brute**: **Crushing Slam**, a 40-tick (2 s) cast, a 90-degree 3500 mm stone-grey frontal cone, 40% of max
  health, not interruptible. The tank turns it away from the party (research/06:30).

Kill order, kick assignment and dispel are the pack puzzle. The Choir's lieutenant framing carries over
(research/06:51-54). The key's Tempo makes every caster cast more often and every telegraph lead less (section 8),
so the same pack asks for more kicks on a higher key. The packs gain their extra mechanic at keys 5, 10 and 15 by
the same rule as the teaching pulls. This amendment moves no threshold.

### 9.4 The two hazard rooms

A hazard is an **environment caster**: an untargetable, unkillable room actor whose casts are not interruptible.
Each cast shows a bar on a room hazard frame under the objective line and a floor telegraph before it resolves, so
section 3's contract holds for the room exactly as it does for an enemy. Hazards hit enemies as well as players,
which is what makes them part of pulling and not just damage on the floor. Tempo scales their cadence and lead like
any cast (the 10-tick lead floor holds). Tide (section 8) does not move them, because they are telegraphs, not floor
patches.

- **Bellows Hall (West, W2): Vent Gout.** Six floor grates in two rows of three, in set A (1, 3, 5) and set B
  (2, 4, 6). Every 160 ticks (8 s) one set erupts, alternating A, B, A. Each grate's telegraph is a 2500 mm circle with
  a white-hot core and a jagged orange edge, leading by 60 ticks. An eruption deals 30% of max health to players and
  15% to enemies standing in it. The hazard runs from the tick a party member first enters the hall until the Bellows
  Crew is dead, then stops, so walking back through costs nothing. *Pulling:* the vent cycle is the pull clock. The
  tank drags the Crew across the set about to erupt while the party stands on the idle set. A Stoked Brute standing
  in a vent is the same risk turned around.
- **Sluice Gallery (East, E3): Floodjet.** Two wall nozzles, north and south. Every 200 ticks (10 s) one fires,
  alternating, as a 90-degree cone of radius 10000 mm from its wall. The telegraph is pale blue with a chevron (wave)
  edge, leading by 80 ticks. A jet deals 25% of max health and **Drenched** (30% slower movement for 3 s) to players
  and enemies alike. It runs while the Floodgate Watch lives and a party member is in the gallery. *Pulling:* the
  tank parks the Watch in the arc about to fire. The kick to time is the Chorister's Undertow, because an Undertow
  that lands just before a jet drags its target into the arc.

### 9.5 The boss door

The corridor from the Seed chamber to the Warden arena ends at the **Hollow Door**, the gate the Seed's clear used
to open on its own. It now opens on the first tick on which **both** the Seed pull is cleared **and** Forces are at
or above 1000 points (100%). While it is shut, the door shows the bar and the shortfall ("Forces 87%"). A party that
clears the Seed short of 100% walks back into its own branch for a skipped pack. Forces past 100% are shown (for
example 104%) and do nothing else. The fight beyond the door is unchanged.

### 9.6 The teaching pulls (unchanged lessons)

1. **The Choir** — two melee and a caster lieutenant whose 4-second heal cast must be kicked; teaches the kick
   ledger. Low positional demand.
2. **The Pair** — five melee; every 20 s one player is marked to *spread* (a ring: stand apart), ten seconds
   later the pack calls a *stack* (a circle: stand together, damage split). Teaches reading shape.
3. **The Seed** — an elite that on death drops a Window on the floor; a Combo landed in it grants bonus loot at
   run end. Teaches the boss's core rule with no stakes.

**Boss: the Hollow Warden.** Three mechanics, sequenced, never layered (research/06):
1. **Cleave** — a slow frontal cone, readable from the boss's facing. Stand behind or beside.
2. **The Sundered Twins** — two adds share one **Break bar**; any interrupt- or control-tagged ability drains it;
   at empty both are stunned for a burst window. Add priority plus shared control.
3. **The Lock** — at 50%, the boss shields and opens a **Window on itself** for 3 seconds, repeating every 30 s.
   Only a **Combo** closes it (any pair; the boss's Window accepts any Payoff from a class other than whichever
   Setup last tagged it — so the party must alternate who sets and who pays). Fail three Locks and the boss enrages
   into a Tempo spike, not an instant wipe.

## 10. The screen, the queue, and being chill (research/08, /09)

- One screen, three bands: **Character** (auto-equipped, "since last run" delta line, the five abilities with
  keybinds), **Key** (stepper, Clause pick, locks on queue), **Queue** (one button; while queued, a role-scarcity
  readout; ready check on pop; a short lock only after a mid-run abandon).
- **Backfill is in the first playable** (Fellowship's named defect — research/09): a slot that empties mid-run is
  offered to the queue at the same key; a joiner arrives at the next pull start (a teaching pull or a pack engage,
  section 9.1) with the run's Slack.
- **No exported score.** Run history is private to the character. Matching uses key level and completion, nothing
  a stranger can read to reject you.
- **Pings are the communication.** G marks a target or a spot; the game turns it into the party's language
  ("kick", "stack here", "Window!"). No chat in the POC.
- **Death is not a scolding.** The run log privately shows each player what got them; nobody else's screen does.

## 11. Out of scope for the POC

Networking (phase 4), more than one dungeon, more than three classes, a shop, cosmetics beyond colour swaps,
narrative. The sim, content, bots and client are built so none of these require a rewrite.

## 12. Data versus judgment

**From the research** (cited above): the timer spine and one-modifier rule; soft roles and the queue evidence;
depth from modifiers on a frozen kit; forks not trees; facets not item level; per-run choice of three; transparent
drop sources; bounded crafting; Ash on every run; telegraph readability rules; sequenced not layered mechanics;
no single-mistake wipes; the one-screen hub; backfill; no exported score; fixed-tick determinism; route choice
and pull size against a required enemy-forces percentage; mixed melee and caster packs with priority kicks.

**Keyrun's own judgment**: Windows and Combos as the synergy core; the six named pair-combos; the three classes'
loops and kits; the Clause pick; Slack instead of flat death penalties; Tempo as the key's scaling axis; the Lock
as the boss's combo gate; the ten-key table including the ping; the branch seal (no backtracking past a taken
branch); the forces point table; hazards as environment casters that hit enemies too; the Hollow Door rule.

`[DECIDE]` for Matt: the three class identities and names; the combo effects table; key range and par time; whether
Ascension is visible to other players or private.
