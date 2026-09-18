# 04 — Talent Trees

## The question

What makes a talent/build system meaningful instead of bloat?

## What the best games do

1. **WoW, Mists of Pandaria (2012) — kill the 40-50-node tree, force situational choice.**
   Blizzard scrapped the vanilla/Wrath talent-tree model outright. Lead systems designer Greg "Ghostcrawler"
   Street: "Look, we tried the talent tree model for seven years. We think it's fundamentally flawed and
   unfixable... When you have such a gigantic matrix, the chances of having unbeatable synergies, or
   combinations of talents that just don't work together is really high." The fix: fewer choices, each one
   forced (e.g. root vs. snare), because "choosing between a talent that grants 10% damage and one that
   grants 5% damage... isn't interesting." His summary line is the whole thesis: "You will have fewer choices.
   But you will have more choices that *matter*." Source: [Engadget, Ghostcrawler on MoP talent
   philosophy](https://www.engadget.com/2011-12-08-ghostcrawler-on-seeing-the-forest-for-the-talent-trees.html).

2. **WoW, Dragonflight (2022) — bring the tree back, but budget the points so analysis stays tractable.**
   Blizzard's own preview states three goals: "increase player agency over your character's progression and
   build," "provide meaningful rewards while leveling," and "reinforce your character's connection to both
   their class and their spec." Players get 61 points (31 class + 30 spec) versus 7 in Shadowlands — but the
   article is explicit the goal is *not* complexity for its own sake: "The goal in designing the trees is not
   to trap players in complicated choices in order to make a functional build; instead, it's to set up the
   trees with intuitive paths that lead to reasonable builds." Trees "communicate dependencies and magnitude
   intuitively, without a lot of added rules." Source: [Blizzard, Dragonflight Talent
   Preview](https://worldofwarcraft.blizzard.com/en-gb/news/23797209/world-of-warcraft-dragonflight-talent-preview).

3. **WoW, The War Within (2024) — hero talents modify *how* a spec plays, not what buttons exist.**
   Hero talents are a third tree layered on class+spec, unlocked at 71, explicitly designed as an "evergreen"
   system (multi-expansion, not one-and-done like Azerite/Covenants). Blizzard's stated balance goal: both
   hero-talent options per spec should "feel similarly effective" across raid, M+, and PvP, avoiding a tree
   that becomes flatly "required." The design intent is a flavor/playstyle fork (e.g., a burst-window build vs.
   a sustained-cleave build of the *same* spec) rather than new independent buttons. Source: [GameRant, WoW
   devs on hero talents design](https://gamerant.com/world-warcraft-war-within-hero-talents-design/).

4. **Path of Exile — the tree is 1,000+ nodes deliberately, and the studio defends the complexity.**
   PoE (and PoE 2) share ~2,268 passive nodes across a single tree; a character allocates one per level-up.
   Grinding Gear Games has internally debated simplifying the *presentation* but rejected simplifying the
   *underlying system*, because "their data shows that if you make something easy to understand but don't
   change its underlying complexity, players will quit when it becomes more complicated later" — i.e., a
   friendly onboarding UI over a genuinely deep system just delays the wall, it doesn't remove it. Source:
   [Fandom, Chris Wilson interview on PoE
   philosophy](https://www.fandom.com/articles/2050-interview-chris-wilson-on-path-of-exile-2s-origins).

5. **Path of Exile — the tree fails newcomers via raw choice-paralysis at scale.**
   With "well over 1000 different passive points," new players report genuine decision paralysis; one
   community post describes needing to "attain a level of self-delusion about the decisions I'm making to
   free myself from choice paralysis." GGG's own mitigation is spatial, not systemic: starting positions are
   placed near the passives a class's default weapons/defenses actually want, and third-party guides cluster
   nodes into named groups to make the graph legible. The complexity is real and player-reported, not just a
   meme. Source: [PC Gamer, PoE2 passive tree
   overwhelm](https://www.pcgamer.com/games/rpg/path-of-exile-2s-huge-passive-skill-tree-used-to-scare-me-but-now-ive-started-treating-it-as-open-world-character-creation-ive-fallen-in-love-with-the-whole-thing/).

6. **Diablo IV — a deliberately layered system (skill tree first, paragon later) that still draws fire for
   killing experimentation.** Lead Class Designer Adam Z. Jackson described the intent as layered progression:
   simple skill tree + simple itemization early, paragon board added "late in the game for additional
   character customization" so new players aren't hit with two systems at once. In practice this backfired on
   respec: a level-100 full paragon-board respec costs on the order of 9,000,000 gold, and skill-point respec
   cost scales sharply past level 15 (~1,000 gold/point), with players refunding paragon nodes one at a time —
   "far from friendly to build experimentation." Sources: [PlayStation Blog, D4 paragon
   interview](https://blog.playstation.com/2023/06/02/diablo-iv-interview-paragon-system-pvp-replayability-and-more/);
   [GameRant, 9M gold respec](https://gamerant.com/diablo-4-player-full-respec-9-million-gold/).

7. **Last Epoch — per-skill specialization trees, widely praised for making leveling one skill feel like
   choosing a sub-class.** Every one of 100+ active skills has its own small specialization tree attached to
   it (not one shared character tree), so picking "Fireball" and then investing points *into Fireball* changes
   what Fireball fundamentally does (chains, becomes a DoT, becomes a meteor, etc.) rather than adding
   passive %s elsewhere. Guidance on the design explicitly frames good trees as ones where "many paths are
   viable" and every unchosen path would have been a real loss — the node is validated by the fact you'll miss
   it. Source: [Maxroll, Last Epoch Passives and Skills
   Overview](https://maxroll.gg/last-epoch/resources/passives-and-skills).

8. **Hades — boons are a run-scoped, ability-modifying build, not a permanent tree at all.** Each of 8 gods
   offers boons that attach to Zagreus's existing 4-5 base attacks (not new buttons) and are explicitly
   designed to combo non-obviously: "Zeus's lightning working perfectly with Poseidon's knockback" or "Ares's
   doom stacking with Dionysus's hangover effect." Because the build resets every run, there is zero respec
   cost and the game is *designed* around trying a different god combo next time — variety comes from
   randomized offer pools plus duo boons that only appear if you've taken prerequisite boons from two gods.
   Source: [Kokutech, Designing Overpowered Builds — Hades design
   lessons](https://www.kokutech.com/blog/gamedev/design-patterns/power-fantasy/hades).

9. **Risk of Rain 2 — items modify existing kit and stack without exceptions, so more items = more build, not
   more buttons.** The design ethos is that items "do weird and wild things and yet almost always work
   together in ways that never cancel out"; the team deliberately minimized hard caps so stacking keeps
   scaling, and cross-item synergies (attack-speed items + on-hit-proc items) are the intended discovery loop,
   not an edge case. No new abilities are added by items — only your existing kit is modified. Source: [Parry
   Everything, Everything Stacks With
   Everything](https://parryeverything.com/2021/12/03/everything-stacks-with-everything-ft-risk-of-rain-binging-of-isaac-etc/).

10. **Guild Wars 2 — elite specializations replace a weapon/playstyle wholesale rather than adding numbers.**
    ArenaNet's stated intent: "When we look at elite specializations, we really try to shift the profession
    mechanic into something more radical... so that the gameplay is significantly changed." A spec swaps a
    new weapon in, changes the profession mechanic, and is explicitly designed so "playstyles built around the
    other grandmasters are also viable" — i.e., the choice is which entire playstyle, not which stat bonus.
    Source: [PC Gamer, ArenaNet on new elite
    specs](https://www.pcgamer.com/games/mmo/guild-wars-2-exclusive-arenanet-breaks-down-the-changes-coming-to-all-9-new-elite-specs-in-the-next-expansion/).

11. **Lost Ark — tripods modify a skill's function, engravings modify the build's identity; both are praised
    for depth and blamed for onboarding.** Every non-awakening skill has 3 tripod tiers, one choice per tier,
    and tripods "range from statistical bumps to fundamental tweaks of how the skill performs in combat" —
    the same "modify, don't add" logic as Last Epoch/RoR2. But community sources flag tripods as "the most
    intimidating part of Lost Ark's Skill system" for new players precisely because the fundamental-vs-numeric
    tripods look identical in the UI until read closely. Source: [TheGamer, Lost Ark Tripod System
    Explained](https://www.thegamer.com/lost-ark-tripod-skill-upgrades-explained/); [VULKK, Lost Ark Engraving
    System](https://vulkk.com/2022/02/18/how-to-use-lost-arks-engraving-system/).

## What players hate

1. **Decision paralysis from raw node count.** PoE's ~2,268-node tree produces documented player reports of
   needing to consciously suppress analysis-paralysis to keep playing. Source: [PC Gamer, PoE2 passive tree
   overwhelm](https://www.pcgamer.com/games/rpg/path-of-exile-2s-huge-passive-skill-tree-used-to-scare-me-but-now-ive-started-treating-it-as-open-world-character-creation-ive-fallen-in-love-with-the-whole-thing/).

2. **Respec cost that punishes experimentation instead of enabling it.** D4's paragon respec costs (~9M
   gold at level 100, ~1,000 gold/skill-point past level 15) turn "try a different build" into a multi-hour
   gold grind, and Blizzard has "conveyed rather clearly there aren't any improvements to the system coming
   soon." Source: [GameRant, 9M gold
   respec](https://gamerant.com/diablo-4-player-full-respec-9-million-gold/).

3. **Non-choices ("multiple choice test" talents).** Pre-MoP WoW's problem per Ghostcrawler: picking between a
   10%-damage node and a 5%-damage node isn't a decision, it's arithmetic — and once datamining resolves the
   arithmetic, every build converges. Source: [Engadget, Ghostcrawler on MoP
   philosophy](https://www.engadget.com/2011-12-08-ghostcrawler-on-seeing-the-forest-for-the-talent-trees.html).

4. **Cookie-cutter convergence regardless of tree shape.** Even Dragonflight's rebuilt trees drew forum
   complaints that they play out "cookie-cutter/Spec-This-Way-Or-Denied-To-Group like it was back when they
   had talent trees before" — a reminder that a bigger tree doesn't automatically fix convergence if
   power-per-node stays uneven. Source: [Sportskeeda via search aggregation of WoW community
   reaction](https://sportskeeda.com/mmo/news-world-warcraft-game-director-ion-hazzikostas-discusses-11-0-war-bands-hero-talents-player-criticism-and-more-exclusive)
   `[unsourced opinion strength: community forum sentiment, not a formal survey]`.

5. **Bloated node fields with mostly-dead filler.** Some Druid players specifically cited "useless talents,
   poor pathing" in Dragonflight trees — nodes that exist to fill visual space on the tree rather than to
   offer a real fork. `[unsourced opinion — general community sentiment cited without a single named critic]`.

6. **Hidden complexity that looks identical to trivial complexity.** Lost Ark's tripods are flagged as
   intimidating specifically because a tier-1 "+5% damage" tripod and a tier-1 "this skill now pierces"
   tripod present the same way in the UI — the player can't tell which nodes are load-bearing without outside
   knowledge. Source: [TheGamer, Lost Ark Tripod System
   Explained](https://www.thegamer.com/lost-ark-tripod-skill-upgrades-explained/).

## Steal / adapt / avoid

**Steal**
- Modify-the-ability, not add-a-button (Last Epoch specialization trees, RoR2 items, Lost Ark tripods, Hades
  boons, GW2 elite specs). Directly compatible with Keyrun's ≤5-ability, ≤10-keybind constraint: a talent
  node changes what an existing keybind *does* instead of consuming a new one.
- MoP's "fewer choices, all of them matter" thesis. With only 4-5 rotational abilities to attach talents to,
  Keyrun cannot afford PoE-scale node counts — a small, forced-tradeoff structure per ability fits the
  synergy-first, no-art, fast-iteration brief far better.
- Hades' zero-respec-cost, run-scoped model. If Keyrun's build layer resets or cheaply re-rolls between
  dungeon runs (not permanently locked to a character), players can experiment every run instead of once
  per season — this matches "come back to a simple character screen and get stronger" without the D4 tax.
- GW2/hero-talent framing: a choice that reroutes *playstyle* (which ability variant defines this run) rather
  than a numeric bonus.

**Adapt**
- PoE's node-clustering-near-relevant-start-point fix — if Keyrun ever grows the tree, anchor each class's
  starting choices near the abilities that class actually opens with, not a shared neutral hub.
- Dragonflight's point budget discipline (61 points is a lot but the article stresses non-overwhelming
  intuitive paths) — the analogous move for Keyrun is a strict per-ability node cap (e.g., 2-3 forks per
  ability, not open-ended trees) so the total decision count stays small enough to hold in your head mid-run.

**Avoid**
- PoE's raw node count for a 5-ability kit — > 1000 nodes only works because PoE has dozens of skills and
  hundreds of item mods to interact with; Keyrun's smaller ability surface would turn that scale into pure
  noise.
- D4's paragon respec-cost tax — directly contradicts "come back... and get stronger" being fun; punishing
  experimentation is a solved anti-pattern, not a design frontier.
- Numeric-only "10% vs 5%" filler nodes (MoP's rejected pre-Cataclysm model) — every node Keyrun ships should
  be describable as "this ability now does X differently," never just a percentage.
- Same-looking-different-weight nodes (Lost Ark tripod critique) — if Keyrun UI can't visually distinguish a
  functional-change node from a numeric-tweak node, newcomers hit the same intimidation wall.

## Verdict

Meaningful build systems modify what a small set of abilities *do* (Last Epoch specialization trees, Hades
boons, RoR2 items, Lost Ark tripods, GW2 elite specs) rather than adding buttons or nodes that only move a
percentage (the pre-MoP WoW tree Ghostcrawler called "fundamentally flawed," and the filler nodes Dragonflight
still gets called cookie-cutter for). The two failure modes are opposite: PoE-scale trees (1,000+ nodes)
produce real, sourced decision-paralysis in newcomers even though experts love the depth; D4-style respec
taxes kill the experimentation the layered system was supposed to enable. **Proposal for Keyrun** (marked as
proposal, not yet adopted): one small talent fork attached to each of the 4-5 rotational abilities (2-3
mutually exclusive options per ability, ~12-15 choices total per class, zero shared "neutral" tree), where
every option is required to change the ability's function, targeting, or resource cost — never a flat
percentage — and respec is free or near-free between runs so the character screen becomes a build lab, not a
gold sink.
