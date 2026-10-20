# 13 — Gear depth: keeping the chase alive without one best-in-slot set

## The question

How do games keep gear chasing alive past the first "solved" set, and what of it fits five slots, a Tier cap of 10
and Facets that change function (design/BIBLE.md section 7, STATE.md D32)?

Matt, 2026-10-20, verbatim: "my biggest concern with keyrun is there is not enough gear... like the end game will just
be hunting BiS lmfao... and we have 1 gear slot". The "1 gear slot" half is answered by the gap table in the D32 PR
body: all five slots exist in the sim and the screen, but the character starts empty, takes one item per run, Tier
changes no combat number, and only the human seat's gear reaches the run. The "hunting BiS" half is this brief.

Several pages below were read through search-result excerpts only, not a direct fetch; they are marked "(excerpt)".
Numbers move with patches.

## What the best games do

1. **WoW Mythic+: trinkets are context-correct, not globally best.** Archon's per-dungeon trinket pages show the
   pairing a spec runs shifting by dungeon: one Arcane Mage pairing is 81.4% of runs in Pit of Saron against a
   different pairing at 34.5% across all dungeons (excerpt,
   [Archon Pit of Saron](https://www.archon.gg/wow/builds/arcane/mage/mythic-plus/trinkets/high-keys/pit-of-saron/this-week),
   [Archon all dungeons](https://www.archon.gg/wow/builds/arcane/mage/mythic-plus/trinkets/10/all-dungeons/this-week)).
   Why it works: an on-use or proc trinket's value depends on fight length and pack timing, which sims flatten; a
   Raidbots/AskMrRobot thread notes the sim gap comes from "the timing of using two on-use trinkets at the same time,
   and just fight length" ([AskMrRobot forum](https://forums.askmrrobot.com/t/simcraft-raidbots-vs-askmrrobot-sims/3904)).
2. **WoW tier sets: the good ones change how a spec plays.** Wowhead's Dragonflight Season 1 Hunter review called the
   new 4-piece a simpler bonus that "mostly reinforces existing rotation abilities" and weaker than Shadowlands sets
   that "reshaped how the spec played" (excerpt,
   [Wowhead](https://www.wowhead.com/es/news/hunter-season-1-dragonflight-tier-set-bonuses-guide-writer-first-impressions-329296)).
   Why it works: a set bonus that adds a decision is remembered; one that adds a percentage is simmed and forgotten.
3. **Path of Exile: uniques that enable a build rather than top a list.** Shavronne's Wrappings carries "Chaos Damage
   taken does not bypass Energy Shield", reversing a core rule
   ([poedb](https://poedb.tw/us/Shavronnes_Wrappings); the rule itself, [poewiki](https://www.poewiki.net/wiki/Energy_Shield)).
   A GGG designer post on Delirium uniques states the goal as an item "you'd want to pick up and use, either by creating
   a new way of playing or enhancing a certain playstyle", widely usable early and "greatly desirable for very specific
   builds late" (excerpt, [GameBanshee](https://gamebanshee.com/36ryk)). Why it works: the item's value is conditional
   on the build, so "best" has no single answer.
4. **Path of Exile: crafting is the chase after the drop.** Currency is applied to gear to reroll or add affixes, so
   every drop feeds a lever (research/05 item 5, [Grindout](https://grindout.com/poe-2/guides/crafting)). Why it works:
   the target is a specific item for a specific build, and the route to it is chosen.
5. **Diablo IV: Aspects change skills, and the Codex makes the base power easy to get.** Aspects "add new interactions
   between skills", "make certain skills better or different, or just add new mechanics"; dungeons unlock an Aspect in
   the Codex of Power, account-wide, always "at the lowest level" (excerpt,
   [Icy Veins](https://www.icy-veins.com/d4/guides/legendary-aspects-codex-of-power-guide/)). Why it works: owning a
   power is cheap, so the chase moves to choosing and combining powers, not finding them.
6. **Diablo 3 to 4: six-piece sets were the BiS trap and D4 said so.** A D4 developer Q&A, as reported, said D3 sets
   ended "dominating the meta" and "locking players into rigid, high-multiplier builds"; skipping the six-piece left "an
   enormous amount of damage on the table". D4's answer is charms outside the gear slots and mixed 2/2/2 bonuses (excerpt,
   [Icy Veins](https://www.icy-veins.com/d4/news/diablo-4-devs-explain-how-sets-will-avoid-diablo-3s-biggest-mistake/)).
   Why it matters: a large multiplier on one complete set is exactly "the end game is hunting BiS".
7. **Last Epoch: Legendary Potential makes each unique a crafting question.** LP 1 to 4 sets how many affixes move from
   an Exalted item onto a Unique in the Temporal Sanctum's Eternity Cache; higher dungeon tiers let the player choose
   one affix to guarantee, once per run (excerpt, [Icy Veins crafting guide](https://www.icy-veins.com/last-epoch/crafting-guide);
   [Prima](https://primagames.com/tips/legendary-potential-in-last-epoch-explained)). Why it works: the same named unique
   is a different item per build, so two players chase different copies of it.
8. **Dark and Darker: gear is a stake, not a trophy.** Death loses what the character carried and wore; extraction
   keeps it ([ExpertBeacon](https://expertbeacon.com/what-do-you-lose-in-dark-and-darker/),
   [Pro Game Guides](https://progameguides.com/dark-and-darker/how-to-extract-in-dark-and-darker/)). Gear score buckets
   matchmaking (0-24, 25-124 normal; 125-224, 225+ High Roller) and inventory counts toward it
   ([wiki](https://darkanddarker.wiki.spellsandguns.com/Gear_Score)). Why it works: loss keeps gear flowing, so the
   chase never ends at a set. The stash-on-death detail differs between sources (UNCONFIRMED).
9. **Hades: boons are sidegrades offered three at a time, and Duo boons pay off a pairing.** Duo boons require boons
   from two specific gods already held (excerpt, [Hades wiki](https://hades.fandom.com/wiki/Boons?oldid=6594);
   [Prima, 28 Duo boons](https://primagames.com/tips/hades-what-are-duo-boons-how-get-duo-boons)). Kasavin named
   replayability "a foremost goal" through "multiplicative design" (excerpt,
   [GameBanshee interview](https://www.gamebanshee.com/news/121967-hades-interview.html)). Why it works: the right pick
   depends on what you already hold, so no boon is BiS in the abstract.

## What players hate

1. **Gear as a stat checklist.** "Primary Stats are barely relevant anymore", and Mastery "rarely (if ever) alters how
   you play the game" (excerpt, [Blizzard EU forums](https://eu.forums.blizzard.com/en/wow/t/gearing-in-wow-is-too-trivial-and-boring/544729?page=2)).
2. **BiS lists that make groups toxic.** A reply in "Spec stat priority is an obstacle" describes players leaving a
   dungeon after the first boss while fishing for one trinket (excerpt,
   [Blizzard EU forums](https://eu.forums.blizzard.com/en/wow/t/spec-stat-priority-is-an-obstacle/539354)). That is a
   direct hit on test 3 (BIBLE section 0).
3. **One complete set or nothing.** The D3 six-piece multiplier (item 6 above).
4. **Gear-score gatekeeping.** Dark and Darker players report low-score groups matched into 480+ trios (excerpt,
   [Steam discussion](https://steamcommunity.com/app/2016590/discussions/0/4407417621392097635)). Any visible gear number
   becomes a filter, which BIBLE test 2 forbids.
5. **Stale loops even in good loot games.** GGG conceded endgame had become "predictable and tedious" (research/05 item 6).

## Steal / adapt / avoid

**Steal**:
1. **Context-correct items (WoW trinkets).** Make the right Facet depend on the key's affix, Clause and enemy kits, so
   "best" is a question per key. Fits five slots: no new slot, only a new reason to swap.
2. **Function over number (D4 Aspects, PoE uniques, good tier sets).** Already BIBLE s7 "Facets change function"; push
   every Facet onto the D31 toolkit verbs (threat take, hand-off, kite, save) and the Window/Payoff tags.
3. **Pair payoffs (Hades Duo boons).** A Facet that only pays off when a partner's class opens or closes the Window,
   which is BIBLE s4's timing-between-two-people rule applied to gear.

**Adapt**:
- **D4 Codex of Power.** Owning a Facet once unlocks it for the character's armory; the chase is the set of Facets
  owned and their Tier, and the loadout is picked per key on the screen. Keeps the five-slot budget: the armory is
  storage, not a sixth slot. BIBLE s1 rules out a stash, so this needs Matt (conflict 3 in the PR body).
- **Last Epoch LP and the PoE crafting chase.** Keyrun's two reroll charges stay; a Tier-10 copy of a Facet is the one
  worth crafting toward, so two players chase different copies.
- **Dark and Darker risk.** Not gear loss (test 3 says no run is ruined by one mistake); the stake is the key level,
  already in BIBLE s8.

**Avoid**:
- **Set bonuses with large multipliers (D3).** No set bonus at all; a pair payoff lives on one Facet.
- **A public gear number.** No gear score, no item level, nothing a stranger can filter by (test 2, test 3).
- **Stat-only items past the cap.** BIBLE s7: power never moves after Tier 10.
- **Sims as the arbiter.** If one Facet loadout wins on every key, the BIBLE s6 parity check should catch it, run per
  affix and Clause.

## Verdict

Keep five slots, Tier capped at 10 and Facets that change function. Remove the single best-in-slot set by making the
right Facet depend on context: each Facet plugs into a D31 toolkit verb or a Window/Payoff tag, the key's affix,
Clause and enemy kits change which Facet is right, some Facets pay off only with a partner class, and every Facet the
character has owned stays available so the five equipped are re-picked on the screen per key. The endgame chase is a
deeper armory (every Facet owned, each at Tier 10) and harder keys cleared with the right loadout, then Ascension marks
earned per affix and Clause, never more power.
