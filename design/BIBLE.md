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
- **Damage is a pipeline of integers**: base → source modifiers → target modifiers → class armor → absorbs →
  health. Mitigation and buffs are basis points. No hidden multipliers (section 3.1 names the one armor stage).
- **The sim runs at 20 Hz** (one tick is 50 ms; STATE.md D9, measured in research/11). Every duration in this
  document is in ticks at that rate.
- **The kick window is sized in ticks, with a lag margin.** A kick counts when its intended tick falls inside the
  cast. The last 6 ticks (300 ms) of every interruptible cast are the named kick window that pings and bots aim
  for. When networking arrives (phase 4), the window must stay larger than the expected round trip plus 2 ticks, and
  a kick is judged on the tick the player intended, never on the tick its packet arrived (research/07:150-156).
- **Discrete outcomes snap to ticks.** Cast started, interrupt landed, aura applied and death show on the tick they
  happen and are never blended between ticks. Only motion is interpolated (research/07:131-133).
- **Telegraph contrast and outline.** Every floor telegraph has a solid, high-contrast outline. Its fill colour is
  checked against the room's floor colour so it never sinks into the background. Each telegraph type keeps its own
  shape and edge pattern, so it still reads with the colour removed (research/06:100-104, :147-151).

### 3.1 Tank survivability (decided 2026-10-20, WoW M+ model)

Matt: "as damage scales this will turn into a one shot, so abilities and damage reduction stuff needs to be
considered". Before this, the Warden (400 health) had no passive mitigation: alone in the Choir pull at key 1 with
Brace on cooldown it died at tick 160 (8 s), and one key-10 Fortified Acolyte swing took 91 (23% of its health).
WoW M+ keeps a tank alive with three layers, and Keyrun copies all three:

1. **Passive armor (always on).** The Warden removes **35% (3500 bp)** of every hit, after the bp modifiers
   (game/content/data/run.json `classArmorBp`; `Damage.ApplyArmor`). It is a percent, so it scales with Fortified
   and Tempo instead of being outgrown, and it **multiplies** with the active layer instead of adding to it, so the
   full stack never reaches immunity. 35% sits just under a WoW plate tank's 40% against same-level mobs: it is the measured value that keeps the Playtest suite green with key scale pressure (#364).
2. **Active mitigation (buttons already in the kit).** Brace -40% for 60 ticks every 300 (20% uptime, the "press it
   for the big hit" cooldown); Vigil -30% for 100 ticks for 50 Momentum (the Shield Block / Ironfur rhythm the
   Warden earns by hitting); Bulwark -15% for anyone in the patch. Taken during Brace: 60% x 65% = 39%. Brace + Vigil
   + Bulwark + armor: 15% x 65% = 9.75%, never 0.
3. **Healer throughput.** The tank alone buys the healer's reaction budget: **an unhealed Warden with only Brace on
   cooldown lives at least 10 s (200 ticks) on a key-1 pull** (measured at 3500 bp: Choir pull 200 ticks, exactly the window; key 5 259, key 10
   231). The healer's job is the rest: sustained healing on the tank should match the tank's mitigated incoming
   damage on trash (key-1 Choir: two Acolytes x 42 per 40 ticks = 42 health/s, about 10% of the Warden per second)
   and burst past it after a big hit. The Chanter's healing (Consecrate pulses, the Hymn field) is not retuned here.

**One-shot guard:** at key 10 (Fortified + Tempo), no trash hit may take a fifth of the tank's health (measured: 59
of 400). Boss hits are bigger by design (Cleave 220 raw, 143 after armor) and are telegraphed: that is Brace's job.
Tests: tools/Keyrun.Playtest.Tests/TankSurvivabilityTests.cs.

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
- **Decision (Matt, 2026-10-20), recorded verbatim:** "talents = keep per-ability forks + add a balance/dominance
  check". Forks stay. No option of a fork may dominate the others. **Parity check:** the bots suite runs each fork's
  options through the same seeded Hollow Vault runs at keys 2 and 10, measuring clear time, deaths and Combos landed.
  An option that is at least as good on every measure, and strictly better on one, is a defect that blocks the
  content change, the same way a red test does. The aim is Blizzard's own stated goal that options "feel similarly
  effective" and none becomes "required" (research/04:30-34). A tree shape alone does not stop cookie-cutter
  convergence (research/04:125-127).

## 7. Gear and progression: bounded, with a real end (D7)

- **Five slots**: Weapon, Armor, Charm, Ring, Keystone. Each item has a **Tier** (1–10) and one **Facet**.
- **Tier is power and it is capped at 10.** Ten is the end. Best-in-slot is a specific Facet set at Tier 10 for
  your build, and it is hard: the last two tiers drop only from boss mechanics done correctly on high keys
  (Monster Hunter's transparent, target-farmable sources — research/05). No item level after 10, ever.
- **Facets change function** (Diablo IV's lesson — research/05): a Facet retags an ability's Window, adds a
  second Payoff to a neutral ability, changes a floor patch's shape, or alters the resource loop. Facets are the
  chase; numbers are just the tier.
- **End of every run: choose one of three** (a vault-lite, per run, not per week — research/01): the options are
  shown with their Facet text and tier; there is never a trap choice (research/05). **Decision (Matt, 2026-10-20),
  recorded verbatim:** "vault = every run". The per-run cadence stands, and the weekly vault of research/05:9-11 is
  rejected.
- **Every clear moves something visible.** **Decision (Matt, 2026-10-20), recorded verbatim:** "rewards = keep
  Tier-10 cap, cosmetic Ash, no public score, but every clear moves something visible: choose-one-of-three always
  includes an upgrade or Ascension progress, plus private last-3-runs strip". So at least one of the three options on
  every clear is a strict upgrade for the character: a higher tier in some slot, or a Facet the character does not
  own yet. At the cap, that option is Ascension progress instead. Fellowship's top complaint was that most drops
  upgrade nothing (research/09:60-62). The private **last-3-runs strip** is on the screen (section 10).
- **Loot quality comes from the key level.** **Decision (Matt, 2026-10-20), as relayed:** loot quality comes from
  the key level, and a +2 or +3 pays by moving the key up 2 or 3 levels. The tiers offered at the end of a run rise
  with the key it was run at. A fast clear's reward is the higher key it earns, not a bigger chest. Choose one of
  three, the Tier-10 cap and cosmetic Ash all stay.
- **Crafting is two charges per item**: reroll the Facet from that slot's listed pool, twice, then it is what it is
  (Last Epoch scaled down — research/05).
- **Every run banks Ash, win or lose** (Hades — research/05). Ash buys only cosmetics and titles.
- **After the cap: Ascension.** Higher keys, cosmetics, the key ladder, and Ascension marks on the character. Power
  never moves again. This is the WoW shape Matt named, with the treadmill removed.

## 8. The key, the Clause, and Tempo

- **The key** is a number, 2–20 for the POC. It scales **Tempo**: enemies cast more often and telegraphs resolve
  faster (research/06 and Blizzard's own scaling lessons in research/01). Health scales gently so kills still happen.
- **Fortified and Tyrannical** (they replace the earlier "one extra mechanic at keys 5, 10 and 15" `[DECIDE]`).
  **Decision (Matt, 2026-10-20), as relayed:** "follow current WoW. From +5, one of Fortified (trash harder) or
  Tyrannical (boss harder) is active and rotates. From +10, both are on. A rotating Clause (like Xal'atath's affix)
  layers on top."
  - **Keys 2-4:** neither is on.
  - **Keys 5-9:** exactly one is on, and the two alternate on a fixed rotation. The rotation index comes with the key
    from the host (the ISO week), so the sim stays deterministic, and the screen shows it.
  - **Key 10 and up:** both are on.
  - **Values** are WoW's current ones, in basis points. **Fortified:** non-boss enemies get +2000 bp health and
    +3000 bp damage. **Tyrannical:** the boss and its adds get +2500 bp health and +1500 bp damage.
- **The Clause**: **Decision (Matt, 2026-10-20), recorded verbatim:** "Clause = no pre-queue pick, the key sets it
  (rotating) and Clauses start at a higher key (e.g. +5)". Nobody picks a Clause. The key carries it: the Clause is
  set by the key level and a fixed rotation, and the screen shows the key's Clause before Queue is pressed, so no
  lobby negotiation remains (test 2; research/08:122-124). Below the starting key no Clause runs. Matt's example of
  the starting key is +5, and the exact key is tuned. Modifiers start at a higher tier, as Fellowship's curses start
  at Adept (research/09:29-31). Exactly one Clause runs at a time, rotating like Xal'atath's affix, and it layers on
  top of Fortified and Tyrannical. Two Clauses never stack. Research/01:108-110 advised a single modifier, and Matt's
  affix decision above sets this layering instead. Clauses change plans,
  not stats: *Echo* (every interrupted cast spawns a
  weak add), *Tide* (floor patches drift), *Hush* (silence pulses every 45 s; save the kicks), *Pact* (Combos deal
  more, uncombo'd hits deal less). Every Clause is tested against every class before it ships.
- **The timer and Slack.** The run has a par time, and the run timer counts against it from the first tick, as in
  Mythic+ (section 9 principle). **Slack** is a grace bank shown as a second bar: 30 s plus 1 s per key level above 1.
  A death costs 15 s of Slack and a full wipe 25 s, the same 15 s a death costs a Mythic+ timer. The run is
  **timed** when it finishes no later than par plus the Slack still left. Otherwise the key is *depleted*: the party
  may still finish for the run reward and Ash, but not the key upgrade. **Decision (Matt, 2026-10-20), as relayed:**
  an untimed key stays at the same level, with no drop. This is a deliberate divergence from WoW, for test 3. A timed
  run upgrades the key by **+1**, **+2** if it finishes within 80% of par, and **+3** within 60% of par. A +2 or +3
  pays out as that key movement (section 7). These are Mythic+'s upgrade tiers, measured
  against par, and they do not change the par value (a `[DECIDE]`, section 12). **Test 3 holds with a death penalty:**
  the bank always covers at least two deaths, so one death never depletes a run that was on pace, and only repeated
  mistakes or a slow route can. One mistake never flips a clearable run by itself (research/01).

## 9. The POC dungeon: Hollow Vault

- **Principle: lean into Mythic+ inside the dungeon.** **Direction (Matt, 2026-10-20), recorded verbatim:**
  "honestly just lean into m+ design heavily while in the dungeon". Inside the dungeon, World of Warcraft Mythic+ is
  the reference design. Where this section, or the timer rules of section 8, diverges from a Mythic+ in-dungeon
  convention, Mythic+ wins unless a section 0 test or a recorded Matt decision says otherwise, and every such
  divergence names its reason. This applies to the dungeon only. The screen, the queue, gear, the end-of-run choice
  and the social rules (sections 7 and 10) stay as written.
- **Almost exactly WoW, with fewer buttons.** **Direction (Matt, 2026-10-20), recorded verbatim:** "it should feel
  almost exact to wow with less button clutter". The one intended divergence from WoW Mythic+ in the dungeon is
  fewer buttons: the ten-keybind budget (section 2, D4). For the same reason there are no invisibility or skip
  consumables, because the budget has no slot for them.
- **Divergences that remain, each named.**
  - An untimed key stays at its level. This is Matt's deliberate divergence, for test 3 (section 8).
  - Slack is a grace bank on top of the Mythic+ death cost. It is kept for test 3, and it is the orchestrator's
    reading, not a Matt decision, so it is open to removal under the direction above.
  - The key range and par stay an open `[DECIDE]` (section 12).

Three teaching pulls, optional trash packs on two branching paths, an enemy-forces bar, and a boss behind a door
the bar opens. Each teaching pull still teaches one thing. The packs and the bar make route choice and pull size
the run's standing decision against the timer (research/01:29-33, research/06:42-46, research/06:55-59).

**Decision (Matt, 2026-10-20).** The earlier shape, "three pulls and a boss, forward-only", was judged too thin.
Matt, verbatim: "the room design is just super lame... it does not even look fun... there is so much more to
mythic+ that makes it fun". His chosen option, verbatim: "Forces + route layer: Keep the boss and the three teaching
pulls, and add 4-6 optional trash packs across two branching paths, plus a 100% enemy-forces bar that must be full
before the boss door opens. Route choice and pull size become real decisions against the timer. Mixed packs give
several kickable casters, and two rooms get floor hazards." This section is that option made concrete. It does not
change par (an open `[DECIDE]`, section 12). The key's affixes are set in section 8.

### 9.1 The map

```
Entrance -> Choir room (pull 1) -> Pair hall (pull 2) -> Fork landing
  West: Undercroft Steps (W1) -> Bellows Hall (W2, hazard)                              -> Seed chamber (pull 3)
  East: Reading Gallery (E1) + Cantor's Loft alcove (E2) -> Sluice Gallery (E3, hazard) -> Seed chamber (pull 3)
Seed chamber -> Hollow Door -> Warden arena (boss)
```

- **Everything is prespawned with an aggro distance.** **Decision (Matt, 2026-10-20), recorded verbatim:**
  "everything should be prespawned with an aggro distance". Every enemy group exists from the run's first tick,
  standing idle in formation in its own room: the three teaching pulls, every optional pack and the boss. Nothing
  appears when the party walks in. Each group has an **aggro radius**, an integer number of mm set per group in
  content (4000 mm unless the content says otherwise) and drawn on the floor as its aggro ring (below). A group
  **engages** on the first tick a living party member is inside its radius, or when party damage or a hazard hits any
  of its members, and then the whole group engages at once. An idle group casts nothing and runs no script, and it
  does not move unless it is a **patrol**.
  - **Patrols.** As in Mythic+, a patrol walks a fixed loop in its room at a fixed pace (deterministic per tick), and
    its aggro ring travels with it. Timing a pull or a skip around a patrol is part of the route. In the POC, the W1
    Hound Kennel patrols a loop on the Undercroft Steps. No other group patrols.
  - **Scripts start at engage.** Every scripted timer of a group (a teaching pull's spread and stack, the Seed's
    opener, the boss's cleave) counts from its own engage tick, never from the run start or room entry. A group's
    first telegraph therefore always follows its engage, whatever tick the party arrives on.
  - **Room lock-in (STATE.md D27) moves to engage.** A teaching pull's room gate shuts behind the party on the tick
    that pull engages, not when someone steps through the door, and opens on its clear as before. The aggro radius
    of a teaching pull lies inside its room, so the party is in the room when the gate shuts. Optional packs never
    shut a gate.
  - **The teaching beats are unchanged.** The Choir still teaches the kick, and the Pair still teaches spread and
    stack. The Seed elite stands idle in its chamber until it is engaged, so it cannot be opened on by a party that
    has not engaged it, and it drops its Window on death as before. The boss stands idle in the Warden arena behind
    the Hollow Door (9.5). It can only be engaged once the door is open, and the Lock's 50% and 30-second rhythm
    counts from its own fight.
  - **Linked groups chain-pull.** Content may link groups. A link is drawn on the floor as a dashed line between
    their rings, and engaging one engages the others. A linked pair is a chain pull the party can see before it
    steps in, under the pull-size rule below. No POC pack is linked by default.
  - **Bots pull on purpose.** Bots path around the rings they mean to skip and step into the rings they mean to
    pull, with the tank first. They never engage a group by walking into it on the way somewhere else.
- **Branching, no backtracking past a taken branch.** The Choir and Pair gates work as before: each opens when its
  pull is cleared. At the Fork landing the party picks West or East. The first living party member to cross the
  threshold trigger of a branch's first room *seals* the branch. The doorway back into the Pair hall and the other
  branch's doorway close for the rest of the run, and so does the other branch's stair into the Seed chamber.
  Inside the taken branch every room stays open in both directions, and so does the stair between the branch and the
  Seed chamber. A party that skipped too much can walk back to a pack it skipped in its own branch. That walk is a
  time cost the party chose, not a softlock. Nothing reopens a sealed doorway. The walk back is paid for on the
  run timer, so a skip is a gamble against par, and a skip that falls short costs the party its own time.
- **Optional packs do not gate.** A pack stands beside the walking line with its **aggro ring** drawn on the floor:
  a grey dashed ring at the pack's aggro radius (4000 mm by default). It is not a danger shape, so it never reads as
  a telegraph. The pack engages by the prespawn rule above.
  Walking the far wall past an untouched ring is a **skip**. Walking into two rings together is a **chain pull**. The
  read is visual, a radius on the floor, not memorised geography (research/01:106-107).
- **Branches rejoin before the next teaching pull.** Both branches end at the Seed chamber, so every route plays all
  three teaching pulls in order, and the run stays forward-only apart from the in-branch walk back above. The branch
  is a choice of packs, not a different dungeon. Fellowship's 100% kill-score across branching paths is the
  precedent (research/09:22-24).
- **Pull size is a decision, read from the floor.** How many packs to take at once is the run's main skill axis
  (research/01:29-34, research/06:42-46). The aggro ring is the visible pull radius. Every ring along a branch can be
  seen from that branch's doorway, so the tank can plan a chain pull (two rings entered together) or a single pull
  before stepping in, and adjust it live (research/01:106-107).
- **Zero downtime means stakes, not spawns.** **Clarification (Matt, 2026-10-20), recorded verbatim:** "when i say
  0 downtime, i mean the high stakes m+ introduces, not literally spawning enemies on the player". No moment of the
  run is free, because one continuous timer runs against par from the first tick (section 8), so every pull and
  every walk spends the same clock as the boss (research/06:64-71). The forces bar must be filled before the Hollow
  Door (9.5), so every skip, chain pull and walk back is a choice made against that clock (research/01:29-34). A wipe
  costs Slack, and a slow or greedy route costs par and can deplete the key (section 8). Walking between packs is
  normal and is part of the route decision. No rule here limits walking distance or puts enemies on the party.
- **What an optional pack pays.** Fellowship's trash pays nothing (research/09:60-61). Here, every optional pack
  killed adds Ash to the run's bank, shown on the run-end panel. A run that opens the Hollow Door at 110% forces or
  more raises one of the three end-of-run options by one tier, never past 10. That overflow upgrade is the reward
  for pulling more than the bar needs, against the timer.
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
| Bellows Keeper | **Stoke** | 70 ticks (3.5 s) | 240 ticks | 4000 mm ember-orange circle centred on the Keeper, with a toothed edge | Enemies inside gain 30% damage and 20% cast speed for 8 s (**Stoked**). Stoked is an enrage: Cinder's Purge removes it from an enemy, as a Mythic+ soothe or purge does. |
| Drowned Chorister | **Undertow** | 60 ticks (3 s) | 180 ticks | 30-degree sea-green cone from the Chorister toward the target | Pulls the target 5000 mm toward the Chorister and roots it for 1 s. Small damage, but in the Sluice Gallery it drags the target into the next jet. |

**Melee** (not kickable; the dangerous swing is telegraphed and avoided, not kicked):
- **Choir Acolyte** (existing, `choir.melee`): filler melee.
- **Hollow Hound**: fast (260 mm/tick), fragile melee. The Kennel's four test cleave and peel for the backline.
- **Vault Brute**: **Crushing Slam**, a 40-tick (2 s) cast, a 90-degree 3500 mm stone-grey frontal cone, 40% of max
  health, not interruptible. The tank turns it away from the party (research/06:30).

**Mythic+ pack conventions** (section 9 principle):
- **Stops as well as kicks.** A stun, freeze or silence that lands on a caster during an interruptible cast also ends
  the cast, as a Mythic+ stop does: Shatter's stun, Cold Snap's freeze, Ward Dissonance's silence. A stop counts as an
  interrupt for Echo, but it does not refund Quench's Heat. The kick plan becomes a kick-and-stop plan
  (research/01:23-26).
- **On-death effects.** When an Ash Hexer dies it leaves **Cinders**: a 2000 mm violet-and-ash circle, telegraphed 40
  ticks ahead, that burns players standing in it for 6 s. Kill order is then also a question of where a Hexer dies.
  The Seed elite's death Window stays the dungeon's other on-death effect.
- **Cast bars say whether they can be stopped.** On a nameplate, an interruptible cast shows an open bar. A cast that
  cannot be interrupted shows the same bar with a shield edge, so the difference is a shape, not a colour.
- **A suggested kick order.** When a pack engages, each of its kickable casters shows a small number on its nameplate:
  the order in which the party's kicks come off cooldown, taken from the kick-ready readout (section 10). It stands in
  for the kick assignment Mythic+ groups make. Players may ignore it (research/06:117-121).

Kill order, kick assignment and dispel are the pack puzzle. The Choir's lieutenant framing carries over
(research/06:51-54). The key's Tempo makes every caster cast more often and every telegraph lead less (section 8),
so the same pack asks for more kicks on a higher key. Fortified hardens every pack and teaching pull, and Tyrannical
hardens the boss, by the key rule in section 8.

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
   at empty both are stunned for a burst window. Add priority plus shared control. **Decision (Matt, 2026-10-20),
   as relayed:** the Twins die with the Hollow Warden. The boss's death ends the encounter, and any adds still alive
   despawn, as in WoW.
3. **The Lock** — at 50%, the boss shields and opens a **Window on itself** for 3 seconds, repeating every 30 s.
   Only a **Combo** closes it (any pair; the boss's Window accepts any Payoff from a class other than whichever
   Setup last tagged it — so the party must alternate who sets and who pays). Fail three Locks and the boss enrages
   into a Tempo spike, not an instant wipe.

## 10. The screen, the queue, and being chill (research/08, /09)

- One screen, three bands: **Character** (auto-equipped, "since last run" delta line, the five abilities with
  keybinds), **Key** (stepper, the key's Clause shown and never picked (section 8), locks on queue), **Queue** (one
  button; while queued, a role-scarcity readout; ready check on pop; a short lock only after a mid-run abandon).
- **The queue screen is honest.** While queued, the screen shows the real expected wait, not a spinner, so players
  can self-select (research/08:28-32). Under the key stepper a single **readiness line** reads "Ready" or
  "Under-geared for this key". It is information and never blocks Queue (research/08:122-124). The ready check on
  pop times out (10 seconds) and returns everyone to the queue, with no penalty for declining or timing out
  (research/08:127-128).
- **Last-3-runs strip.** A collapsed, private strip under the queue state shows the character's last three runs:
  time, result, and what the clear moved (section 7). It lives on the screen, not in a separate menu
  (research/08:131-132), and no one else can see it.
- **Backfill is in the first playable** (Fellowship's named defect, research/09:65, :112, :125): a slot that empties
  mid-run is offered to the queue at the same key. A joiner arrives at the next pull start (a teaching pull or a
  pack engage, section 9.1) with the run's Slack.
- **Vote to leave keeps the key for those who stay.** A player who leaves alone costs only their own attempt (the
  short lock above). If the party votes to abandon together, nobody's key is depleted and nobody gets
  the lock. One player leaving never costs the players who stay their key (test 3; research/08:37-42, :85-88).
- **Kick-ready readout on the party frames.** Each party frame shows its player's Q cooldown as a ready pip and a
  draining arc, so "who has a kick" is visible without a ping. This stops the double kick that wastes two
  interrupts on one cast, which the Choir punishes (research/03:111-113, research/06:117-121).
- **No exported score.** Run history is private to the character. Matching uses key level and completion, nothing
  a stranger can read to reject you. A shareable or exported score stays banned. **Decision (Matt, 2026-10-20), as
  relayed:** a private WoW-style Mythic+ rating, shown only on the player's own screen. It is never shown to others
  and never used to filter groups.
- **Pings are the communication.** G marks a target or a spot; the game turns it into the party's language
  ("kick", "stack here", "Window!"). No chat in the POC. Strangers by default need day-one signalling, and
  Fellowship's weak pings were a named complaint (research/09:76, :139-140).
- **Death is not a scolding.** The run log privately shows each player what got them; nobody else's screen does.

## 11. Out of scope for the POC

Networking (phase 4), more than one dungeon, more than three classes, a shop, cosmetics beyond colour swaps,
narrative. The sim, content, bots and client are built so none of these require a rewrite.

## 12. Data versus judgment

**From the research** (cited above): the timer spine and one-Clause-at-a-time rule; soft roles and the queue evidence;
depth from modifiers on a frozen kit; forks not trees; facets not item level; per-run choice of three; transparent
drop sources; bounded crafting; Ash on every run; telegraph readability rules; sequenced not layered mechanics;
no single-mistake wipes; the one-screen hub; backfill; no exported score; fixed-tick determinism; route choice
and pull size against a required enemy-forces percentage; mixed melee and caster packs with priority kicks;
Clauses starting at a higher key; the kick window in ticks with a lag margin; tick-snapped outcomes; the
kick-ready party-frame readout; vote-to-leave keeping the key; the honest queue screen.

**Matt's 2026-10-20 decisions** (recorded verbatim in sections 6, 7 and 8): forks kept, with a dominance check; the
Clause set by the key, never picked; every clear moves something visible; the vault every run; prespawn with
aggro distance and zero downtime as stakes (section 9.1); Mythic+ as the dungeon's reference, "almost exact to wow
with less button clutter" (section 9); an untimed key keeps its level, the upgrade tiers and loot quality from the
key level (sections 7 and 8); Fortified and Tyrannical with a rotating Clause on top (section 8); the Twins dying
with the boss (section 9.6); the private rating (section 10).

**Keyrun's own judgment**: Windows and Combos as the synergy core; the six named pair-combos; the three classes'
loops and kits; the Clause carried by the key; Slack instead of flat death penalties; Tempo as the key's scaling axis; the Lock
as the boss's combo gate; the ten-key table including the ping; the branch seal (no backtracking past a taken
branch); the forces point table; hazards as environment casters that hit enemies too; the Hollow Door rule.

`[DECIDE]` for Matt: the three class identities and names; the combo effects table; key range and par time; whether
Ascension is visible to other players or private.

Not yet decided, and not specified here: tank swap, a puzzle beat, a boss vulnerability window, Rally, Cinder field
payoffs, a solo or low-population path, balance cadence, a control-feel bar, and replay depth past one dungeon.
