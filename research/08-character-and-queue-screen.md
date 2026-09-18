# 08 — Character & Queue Screen

## The question

What is the minimum out-of-dungeon experience that keeps the loop tight — a "simple screen like Dark and Darker" — and what should it contain?

## What the best games do

1. **Dark and Darker — one lobby screen gates everything.** Character creation and gear are resolved before you ever
   see a queue button; the "Enter the Lobby" flow is class → gear check → queue, no separate town/hub to walk
   through. It works because travel time between "decide to play" and "in a match" is seconds, not minutes — the
   loop's real content is the dungeon, so the screen gets out of the way. Source:
   [Beginners Guide, Dark and Darker Wiki](https://darkanddarker.wiki.spellsandguns.com/Beginners_Guide).
2. **Dark and Darker — gear-bracketed queues signal risk before you commit.** Queues are split by gear score
   (Normal 0-125, 125-224, High Roller 225+) so a player sees, at the queue button, roughly what kind of fight
   they're walking into. It works because it turns "what am I about to face" into a legible pre-commitment instead
   of a surprise. Source: [Restrict full HR geared characters from queuing into normal lobbies, Dark and Darker feature board](https://darkanddarker.featureupvote.com/suggestions/490239/restrict-full-hr-geared-characters-from-queuing-into-normal-lobbies).
3. **Deep Rock Galactic — the hub is legible in one screen's worth of terminals.** The Space Rig has a fixed, small
   set of stations (Mission Terminal, Equipment Terminal, Assignment/Perk terminal, Forge, trading) and nothing
   else gates play; a review calls it "a hub area that is an absolute joy to come back to" specifically because it
   is walkable in seconds and every terminal has one job. It works because a hub with a bounded, named set of
   stations reads as "systems," not "chores." Source: [Deep Rock Galactic's Hub Area is an Entire Game, GameGrin](https://www.gamegrin.com/articles/deep-rock-galactics-hub-area-is-an-entire-game/).
4. **FFXIV — Adventurer in Need shows role scarcity at the point of queueing.** The Duty Finder tags which role
   (tank/healer/DPS) is short for a given queue and pays a bonus (gil/XP) to whoever queues into the gap. It works
   because it converts an invisible supply problem (not enough tanks) into a visible, incentivized choice right on
   the queue screen — the player doesn't need external knowledge to understand why their queue is long. Source:
   [Duty Finder, FFXIV Wiki (Fandom)](https://finalfantasy.fandom.com/wiki/Duty_Finder).
5. **FFXIV — role queue times are a structural signal, not a bug to hide.** Tanks and healers queue near-instantly,
   DPS waits longest, because the game is honest that role scarcity is a trinity-math problem, not a matchmaking
   failure — and exposes the number rather than hiding it. It works because showing the real wait lets players
   self-select into the role that gets them playing fastest. Source:
   [FFXIV Steam community queue-time discussion](https://steamcommunity.com/app/39210/discussions/0/1456202492174210220).
6. **WoW / Deserter and FFXIV / Duty Finder penalty — a soft cost for abandoning a formed group.** WoW locks a
   player who leaves a dungeon out of matchmakers for 30 minutes; FFXIV penalizes a third same-day withdrawal.
   It works because it raises the cost of ditching a group just enough to discourage casual abandonment without
   being so harsh that misclicks or emergencies ruin someone's night. Source:
   [Queue Dodging / penalty design summary](https://support.riotgames.com/en-us/league-of-legends/gameplay/queue-dodging); [Party Play, FFXIV Game Manual](https://na.finalfantasyxiv.com/game_manual/pp/).
7. **Lost Ark — Matching Mode locks difficulty after the first gate and shares entry count across difficulties.**
   Once you clear gate 1 in matchmaking, later gates lock to that difficulty, and leaving before rewards costs your
   entry; voting to leave before claiming preserves it for those still inside. It works because it removes
   mid-run renegotiation (a huge PUG friction) by making the hard choices happen once, up front. Source:
   [Matching Mode, Lost Ark Wiki (Fandom)](https://lostark.fandom.com/wiki/Matching_Mode).
8. **Hades — the hub *is* the character screen, and it advances the story every single return.** Every trip back to
   the House of Hades changes a conversation, and death is reframed as narrative progress rather than a loss
   screen. It works because it gives the "out of run" beat emotional payoff instead of dead time — the player
   *wants* to see the hub, not just tolerate it. Source:
   [Hades: Fast Paced Storytelling, Game Developer](https://www.gamedeveloper.com/design/hades-fast-paced-storytelling).
9. **WoW Mythic+ group finder — a visible score (raider.io) lets key-holders and joiners filter fast, at a social
   cost.** Score functions as a legible risk signal for pickup groups, but it over-gates: "people often want
   1000+ score for a +5 key," and a score badge doesn't predict PUG-specific performance (coordination, comms).
   It's the load-bearing lesson for what to avoid, not steal outright. Source:
   [Raider IO is ruining Mythic+ group finder, Blizzard forums](https://us.forums.blizzard.com/en/wow/t/raider-io-is-ruining-mythic-group-finder/3834/238).

## What players hate

1. **Dark and Darker's lobby is clunky for simple tasks.** Players report the lobby UI takes "multiple steps and
   significant effort" to do basic things, and community feedback pushed devs to move buttons around mid-season.
   Source: [Dark and Darker Wiki, Beginners Guide](https://darkanddarker.wiki.spellsandguns.com/Beginners_Guide).
2. **Dark and Darker's queue list is fragmented into ~39 separate queues** across maps/modes/gear brackets, which
   players say splits the population thin enough that lobbies feel empty. Source:
   [Remove the multi waiting lobbies system, Dark and Darker feature board](https://darkanddarker.featureupvote.com/suggestions/551119/remove-the-multi-waiting-lobbies-system).
3. **Escape from Tarkov's stash and hideout are a second, mandatory game.** Hideout upgrades gate power behind
   expensive builds with real-time 24-hour construction timers, and stash space is deliberately small unless
   purchased — a widely-cited "laborious," constant-grind time sink. Source:
   [Hideout, Escape from Tarkov Wiki (Fandom)](https://escapefromtarkov.fandom.com/wiki/Hideout); [Smart Inventory Management Guide](https://www.eyeonannapolis.net/2025/10/escape-from-tarkov-smart-inventory-management-guide/).
4. **WoW's manual LFG tool leaves key-holders with no reliability signal.** Key-holders "have no way of knowing if
   players will ever show up," and posting a key can burn over an hour with no tank interest because tanking a
   low key pays worse than a high one for identical effort — a role-incentive mismatch that starves the queue.
   Source: [The Mythic+ Group Finder is broken, Blizzard forums](https://us.forums.blizzard.com/en/wow/t/the-mythic-group-finder-is-broken-and-the-current-crest-incentive-structure-is-making-it-worse/2281681).
5. **Raider.io score gatekeeping punishes exactly the players the system should welcome.** Groups demand scores far
   above what the key actually requires, and a score doesn't predict how someone performs with *this* specific PUG
   — so the "legible risk signal" curdles into exclusion. Source:
   [Raider IO is ruining Mythic+ group finder, Blizzard forums](https://us.forums.blizzard.com/en/wow/t/raider-io-is-ruining-mythic-group-finder/3834/238).
6. **DPS queue times in trinity MMOs are structurally long and nobody loves waiting.** FFXIV's own community
   discussion confirms DPS queues are consistently the longest because tank/healer supply is the bottleneck, not a
   fixable UI problem. Source: [FFXIV Steam discussion, queue times](https://steamcommunity.com/app/39210/discussions/0/1456202492174210220).

## Steal / adapt / avoid

**Steal** (direct fit for ≤5 abilities / ≤10 keybinds / synergy-first / bots-first-then-5-humans):
- DRG's bounded, named-station hub metaphor — collapse it further into *sections of one screen* (character, key
  select, queue) rather than a walkable space, since Keyrun has no art and one screen is the whole hub.
- FFXIV's Adventurer in Need role-scarcity display — surface which party role (tank/healer/dps-equivalent, or
  Keyrun's class-synergy slot) is short, right at the queue button, so bots-first later maps cleanly to human
  role-fill incentives.
- Lost Ark's lock-difficulty-after-gate-one and entry-count-preserved-on-vote — apply to Keyrun's dungeon/key
  select: once queued, the run's difficulty is fixed; leaving before the boss costs the attempt, voting to abandon
  as a group does not penalize the ones who stayed.
- Hades' hub-advances-something-every-visit — give the character screen a one-line "since last run" delta (gear
  upgrade, boss defeated, best time) so the return screen has a payoff beat, at zero art cost (text only).

**Adapt** (needs Keyrun-specific shaping):
- Dark and Darker's gear-bracket queue split — Keyrun doesn't need 39 queues; adapt to a single key-level number
  (like an M+ key) with a coarse readiness gate (item level or class unlocks), not a fragmenting bracket list.
- WoW's Deserter-style penalty — adapt to a short, single-tier queue-lock (not 30 minutes; POC session lengths are
  short) so bots-first testing isn't punished by a real-money-game-scale penalty.
- Raider.io's legible score signal — adapt as an internal, unexported "run history" (times, deaths, completion) on
  the *character's own* screen for self-assessment, not a shareable score that can be used to gatekeep strangers.

**Avoid**:
- EFT's mandatory second-game stash/hideout loop — Keyrun's character screen must never require a maintenance pass
  (sorting, upgrading, waiting on timers) between runs; if a system needs upkeep, it's the wrong system for a POC
  whose loop is "queue → dungeon → screen → queue."
- Dark and Darker's multi-step clunky lobby — no more than the moves needed to change key/gear/queue; every extra
  click before "queue" is measured against the ≤10-keybind, tight-loop constraint.
- Raider.io-style external score gatekeeping — do not build or expose a portable score that PUGs can use to
  pre-reject strangers; it directly caused the friction WoW players cite as why they won't pug.
- WoW's opaque manual-LFG no-show problem — never ship a queue with no ready-check and no reliability signal; that
  gap is what makes key-holders burn an hour for nothing.

## Proposal (POC)

*The following is a proposal, not yet an adopted design.*

Keyrun's out-of-dungeon experience is **one screen**, no navigation, no separate stash/lobby/social hub:

**Top band — Character.** Class name, level/gear-score-equivalent (a single number), the 4-5 equipped abilities as
icons+keybind labels, and a one-line "since last run" delta (Hades-style payoff: "New best time: 4:12" or "Talent
unlocked: X"). No inventory grid, no sorting — gear auto-equips best-in-slot on pickup (kills the Tarkov trap by
design, not by discipline).

**Middle band — Key/Dungeon select.** One dungeon (POC scope), one key-level stepper (+/-, like an M+ key), a
readiness gate shown as a single pass/fail line ("Ready" / "Under-geared for this key" — informational, never
blocking bots-first play). Difficulty locks the moment queue is pressed (Lost Ark rule); no mid-run renegotiation.

**Bottom band — Queue state.** A single Queue button. While queued: role-scarcity readout ("party needs: 1 tank
role" — FFXIV Adventurer-in-Need style) so a 5-human phase makes the wait legible instead of silent. A ready check
on pop (accept within N seconds or return to queue, no penalty for a POC). A visible, short queue-lock only after
a mid-dungeon abandon (not a leave-before-pull cancel) — proportional to Keyrun's short session length, not WoW's
30 minutes.

**Run history** lives as a collapsed strip under the queue state, not a separate screen: last 3 runs, time, result.
No shareable/exportable score — this is the player's own read on themselves, not a gatekeeping artifact for
strangers.

Session length target: character screen dwell time under 15 seconds for a returning player who already knows the
build (equip is automatic, only key-level and queue are live decisions); everything else is dungeon time.

## Verdict

The out-of-dungeon screen is one screen with three bands — character (auto-equipped, delta-flagged), key select
(single stepper, locks on queue), queue state (role-scarcity readout, ready check, short abandon-penalty only) —
and nothing that requires upkeep between runs. Steal DRG's bounded-hub legibility and Hades' "something changed"
payoff; adapt FFXIV's role-scarcity display and Lost Ark's lock-on-commit rule to keep PUGs from renegotiating
mid-run; avoid EFT's mandatory stash/hideout grind and raider.io-style portable gatekeeping scores entirely — both
are the exact failure modes ("45-minute stash management," "won't pug under 1000 score") this screen exists to
prevent. Auto-equip is the load-bearing decision: it is what makes "simple like Dark and Darker" true instead of
aspirational.
