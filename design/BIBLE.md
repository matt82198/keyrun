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

Three pulls and a boss, forward-only, no backtracking (research/02). Each pull teaches one thing.

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
  offered to the queue at the same key; a joiner arrives at the next pull start with the run's Slack.
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
no single-mistake wipes; the one-screen hub; backfill; no exported score; fixed-tick determinism.

**Keyrun's own judgment**: Windows and Combos as the synergy core; the six named pair-combos; the three classes'
loops and kits; the Clause pick; Slack instead of flat death penalties; Tempo as the key's scaling axis; the Lock
as the boss's combo gate; the ten-key table including the ping.

`[DECIDE]` for Matt: the three class identities and names; the combo effects table; key range and par time; whether
Ascension is visible to other players or private.
