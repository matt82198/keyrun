# STATE.md - Keyrun

Rotated 2026-11-20 (WHY: STATE.md was 2155 lines / 569 KB; compaction reads it whole): superseded checkpoints, ledgers and ruled history moved byte-exact to STATE-ARCHIVE-20261120-rotation.md. Read that file only for history.

## INTENT (pinned; the orchestrator reads this first after every compaction)
Why pinned: Matt 2026-10-19, verbatim: "there seems to be a misunderstaning with your responsiblity now, the workers context are small scope, your context is intent... over and over we are losing intent for context... even though its enforced with code" and "i think you've built a good start, it just does not align with the bible lmfao". Every lane is judged against design/BIBLE.md below, not against its own PR text.
- Core dungeon loop rule (Matt 2026-10-20, verbatim): "anything you want my decision on in the core dungeon loop, defer to wow". The orchestrator answers dungeon-loop design questions with what WoW Mythic+ does and records the call.
- Out-of-dungeon rule (Matt 2026-10-20, verbatim): "anything else like progression, builds, classes, lobby stuff more like dark and darker... character management screen, quests, progression, score, follow the bible but you get what im saying, just one screen with tabs should be enough before entering a game/queue". The orchestrator decides lobby, progression, build, class and character-management questions the way Dark and Darker does them, within design/BIBLE.md. Where the bible excludes something (it lists no quests, shop or stash in the POC, and no shareable score), the bible wins.
- Goal, design/BIBLE.md section 1, verbatim: "Screen → Queue → Dungeon (timed, one boss) → Completion roll (one roll per player, which can give nothing; it replaces the end-of-run choice of three, design/PROGRESSION-PIVOT.md s3) → Screen. (R-PIVOT-1, 2026-10-22)" "Nothing exists outside this loop in the POC: no world, no quests, no shop, no stash."
- The three tests, section 0, verbatim headings, each with its first sentence. Every lane's deliverable is judged against these three, not against its own PR description.
  - "**Depth and difficulty.** Classes with real decisions on five buttons; keys that are genuinely hard because of what enemies *do*, not how much health they have."
  - "**A high key inside a minute.** Open the screen, pick a key, press Queue, be in the dungeon."
  - "**Strangers are chill by default.** The systems remove the reasons people are not: no shareable score to judge by, no run ruined by one mistake, backfill instead of abandonment, pings instead of typing."
- Combat contract, section 3, verbatim: "**Cast bars are the enemy's language.** Every dangerous enemy action is a cast with a visible bar and a floor telegraph (circle, cone, line) drawn ahead of resolution. Shape plus colour, never colour alone (research/06)." Also: "Interrupts resolve by intended tick with a fixed tiebreak, never arrival order"; "Damage is a pipeline of integers".
- The design's centre, section 4, verbatim: "This is the design's centre and the answer to "more creative than Blizzard": synergy is not a buff you press, it is a *timing game between two people*."
- POC dungeon, section 9, verbatim: "Three pulls and a boss, forward-only, no backtracking (research/02). Each pull teaches one thing." Pulls: "**The Choir**" teaches the kick ledger (must-kick 4-second heal cast); "**The Pair**" teaches reading shape (spread ring, then stack circle); "**The Seed**" teaches the boss's core rule with no stakes. Boss "the Hollow Warden": Cleave, The Sundered Twins, The Lock.
- Gear, section 7, verbatim: "**Five slots**: Weapon, Armor, Charm, Ring, Keystone." "Tier is power and it is capped at 10." "**Facets change function**" "**End of every completed run: one roll per player.**" "Ash buys only cosmetics and titles." "**After the cap: Ascension.** ... and Ascension marks on the character."
- Current milestone, STATE.md D10 (verbatim): "**MVP = 3D gameplay**" ... "the MVP deliverable is one complete 3D run Matt can play end to end and judge by feel, not a test count." Phase 3 exit waits on Matt's human-played run. MVP LINE (D10 question, RESOLVED 2026-10-20 under Matt's out-of-dungeon rule, see INTENT): the MVP is the full section 1 loop: one screen with tabs, then Queue, then the Dungeon, then the end-of-run choice of three, then back to the screen.
- Acceptance is Matt watching (D15): frames and video viewed before a visible row closes. Demo verdict 2026-10-19 (default run C at 75da3ef): KF1 combat animation reads badly; KF2 bots idle in the first room (the Choir is meant to teach the kick); KF3 the Seed elite spawns on entry and the bots cheese it. KF2 on main (1f8fed7f, #515, "scripted Warden kicks the casting lieutenant"): the Choir kick works, capture kf2-kick-1f8fed7f shows Choral Mending interrupted at ticks 312, 508 and 706 by seats 1, 2 and 3; KF2 is re-captured and awaiting Matt's view, not open. Known section 3 gaps (recon 2026-10-21 16:18Z): every Warden, Twins and Seed threat is a cast with a spansCast telegraph. The seed.lash shape mismatch is fixed on main: its telegraph is a caster-to-target line (shape line, length 1500, width 1200; content/data/enemies/seed.json, #486 6f699097), drawn as a rectangle by TelegraphView (#485 651a9d75). TelegraphAnchor caster|target is pending #539, not merged.
- Boss door and room gate are gone on main (D44; #515 1f8fed7f removed the boss door and the DOOR-100 detour; 6c6746bb removed the no-gates route). Run.cs:377 says BossDoor.Admit is not applied on the forward route. No gate_closed or gate_open event is emitted, and game/src/View/RoomGateView.cs and the GateView and GateCutaway scene tests are absent. Any STATE or map line that calls the boss door or a room gate live is stale.
- Art and gear models: CC0 packs only (D17). How gear shows on the CC0 models was an open design question, RESOLVED 2026-10-20 under Matt's out-of-dungeon rule (see INTENT): as in Dark and Darker, equipped gear is visible on the character model, within the CC0 assets (D17). A BIBLE-ALIGN recon on 2026-10-19 is mapping the bible against main.

**Intent (Matt, 2026-09-18):** "isolating m+ from WoW and just making it an open and queue game like Dark and Darker,
and let people manage their characters from a simple screen... study other mmos, dungeon games, gear progression, and
find specifically what people find exciting/fun, build talent trees, classes, the dungeon POC with initial enemies,
abilities, a boss. Mechanics like interrupts, but be more creative than blizzard devs... 4-5 rotational abilities per
class max, 10 total keybinds should be plenty for rotation, movement, utility... the goal is class synergy." No art
assets; pure POC.

## Current state (2026-10-22, main 73b706c0)
WHY: the 2026-10-18 note here was four days stale. This replaces it with main as of #560, from `git log ed8ad21f..73b706c0` and gh.
The full 2026-10-18 text is in git history (this file at 73b706c0).
- Main: 73b706c0 (#560, 2026-10-22 00:56Z). Previous recency point: ed8ad21f (#508, CHECKPOINT 2026-10-21 21:12Z below).
- Main is RED on game-test until #569 lands (clip row, scoreboard denominator, boss refuses blind).
  #569 required checks all pass at 01:35Z; non-required balance fails. Auto-merge armed.
- Merged since ed8ad21f (50 PRs), by area:
  - Game loop: #505 Seed activates on room entry (KF3); #514 key sets the Clause (R1); #516 pick-of-three code deleted;
    #518 Queue readout branches (B57, B58); #512 Ascension card on the run-end panel (U10); #515 taunt never roots the tank.
  - Dungeon and packs: #502 Hall Sentries optional (G5); #511 LayoutPack.Branch; #517 per-branch forces totals;
    #513 packs.json retired (R7); #537 skip bar (R-PACKS-1); #538 vault kits (S1); #528 corridor-2 patrol;
    #533 teaching pulls pinned to BIBLE s9; #536 pack build slices doc.
  - Sim: #551 integer A* pathing for bots and enemy chase (R-PATH-1); #561 boss fight 90-180 s (R-BOSS-1);
    #539 telegraphs anchor on the caster's target; #560 TelegraphDraw record; #559 no dominated Cadence fork.
  - View and HUD: #519 #523 #531 KF1 strike cue; #522 telegraph palette (R8); #530 seats line off the bar; #541 party
    bodies opaque; #543 --camera chase|vertical and V; #544 per-ability CC0 clips with ANIM lines; #545 scoreboard kills; #526 GEAR lines for the Warden only.
  - Refactor and deletion: #553 TypeScript tree deleted (phase 4A); #547 Casting partials; #550 SimEvent views;
    #554 directory loader (unwired); #556 Screen partials; #557 one file per validator rule.
  - Tests, CI, tools, docs: #503 #507 #509 #527 #529 #532 #534 #535; #504 #521 #524 #525 #548 #549.
- Armed queue behind #569: #570 #568 #567 #566 #564 #563 #562 #558; merge-forward (DIRTY) #555 #540 #542.
- Unarmed, waiting on Matt: #546 (Mythic+ dungeon plan), #552 (seeded dungeon architecture, D46).
- Matt's product gaps (2026-10-21), status on main:
  - Ability animations: landed #544. Pathing: landed #551. Boss length and teaching: landed #561.
  - Party solid: landed #541. Vertical camera for captures: landed #543.
  - Pulling and aggro range: packs engage on aggro radius (Aggro.cs); pull feel not judged by Matt yet.
  - Non-linear rooms: not built; route is one path (layout.json route, 9 points). D45, D46, #552 unarmed.
  - Pack diversity: vault kits landed #538; pack links #564 armed.
  - Landed means merged code, not accepted. Acceptance is Matt's played run (D10, D15).
- Current milestone (2026-10-22, D65, f-8fc6e252): by 2026-10-22 13:00Z a human plays the tutorial (Hollow Vault) as seat 1 end to end, and a second dungeon with at least two bosses is on main.
- Row files: 50 in game/lanes/ wait for a fold lane into BUILDLOG.md and the game/CLAUDE.md lane list.

## Locked decisions
Duplicate ids D45/D46: the entries near the top (branching rooms; seeded dungeons) are the 2026-10-21/22 rulings; the later ones (near 1566/1576) are the 2026-10-19 originals; new ids start at D65.
- D1 **Stack**: TypeScript for sim, content, client, bots, server; Vitest; esbuild; Three.js for the 3D client; no engine.
- D2 **Bots-first**: the first playable is one human plus bot party against the dungeon; networking is phase 4.
- D3 **3D MVP** (Matt, 2026-09-18: "2d is not playable, please 3d mvp"): third-person camera, WASD + mouse-look,
  placeholder geometry only. The sim stays headless and deterministic; 3D is the renderer plus a flat-arena
  movement model (no navmesh in the POC). Readability of mechanics beats everything.
- D4 **Constraints as design**: <=5 rotational abilities, <=10 keybinds, synergy-first classes.
- D5 **Research before design**: no class, talent, gear, or boss mechanic enters the bible without a cited
  precedent for WHY it is fun - but the precedent is the reason, never the design.
- D6 **Original, not a copy** (Matt, 2026-09-18: "it does not need to be a copy of any of the big games, use your
  creative reasoning"): the bible must invent Keyrun's own classes, synergy system, mechanics vocabulary and loop.
  Fellowship (Chief Rebel, 2025) is the closest precedent - reviewed well while admittedly half-baked - and is studied
  specifically for the gap between what it promised and what it shipped.
- D7 **Bounded progression** (Matt, 2026-09-18: "there is an ending to progression, a technical bis that is hard to
  achieve... because otherwise characters scale infinitely and break the game"): power has a cap; best-in-slot is
  reachable but hard; post-cap progression is cosmetic, prestige, and new keys - never more stats. Progression is
  the most important system in the bible.
- D8 **North star** (Matt, 2026-09-18): "m+ at its core is extremely fun, but it lives in a game where every class is
  gutted, way too easy, and it is just boring right now. I miss being able to log in during late DF to early TWW and
  get into high keys instantly and play with people who were chill." Three tests every design decision must pass:
  (1) classes with real depth and keys that are genuinely hard; (2) from launch to a high key in under a minute,
  no gatekeeping; (3) the social defaults make strangers chill - mercy, backfill, no score-shaming.
- D9 **Tick rate - DECIDED 2026-09-18 (orchestrator, unattended; research/11):** stay at 20 Hz. Measured on the
  playable (65 s/arm, same seed): 30 Hz cut input->tick p95 from 51 ms to 34 ms but cost +43% CPU, +50% bytes/s,
  +31% render stalls, and content is authored in raw ticks so the flip alone runs the game 1.5x fast. Reverses if:
  a real Web Worker removes the scheduler-shaped stalls AND content constants are rescaled in lockstep AND the
  latency gain still shows in play tests.
- D10 **MVP = 3D gameplay** (Matt, 2026-10-17: "I would like to restate the mvp on that is 3d gameplay"): the
  MVP deliverable is one complete 3D run Matt can play end to end and judge by feel, not a test count. Concretely:
  third-person camera + WASD + mouse-look that feel right in hand; one dungeon (hollow-vault) with readable enemies,
  the interrupt mechanic, and the boss; one class (warden) plus bot party; placeholder geometry stays, but readability
  of every mechanic in 3D is the bar. D3 still governs the renderer/sim split (headless deterministic sim, 3D is the
  renderer + flat-arena movement). Phase 5 "Matt playing" is the acceptance test. Supersedes nothing; sharpens D3.
- D11 **Model grant for Keyrun** (Matt, 2026-10-17: "use opus 5.5 for development on that"): development lanes for
  this project run on Opus (claude-opus-5-5). Per-project exception to the fleet Haiku-default rule; recorded in
  ~/.claude/docs/grants.md with provenance. Does not relax the Haiku rule for any other project. Read-only recon,
  lint and bookkeeping lanes stay Haiku.

### Rulings 2026-10-21 23:00Z to 2026-10-22 00:2xZ (from the orchestrator checkpoint ledger)
WHY: lanes and future sessions must read rulings from the repo, not the orchestrator scratch ledger. Each entry gives its date, Matt's verbatim quote where the ledger has one, and the reason.

- D45 **Branching rooms, not one path** (2026-10-21 23:5xZ). Supersedes the "one forward path" wording of D44; D44's no gates and prespawned packs stand. Matt: "and the rooms should not force you to one path, where in the bible does it say linear dungeon?" WHY: BIBLE s9 says forward-only, no backtracking, never linear. Rooms branch; the player chooses the route and which packs to pull. Scoreboard = timer with par, enemy forces %, kill count, deaths (WoW M+). The orchestrator had drifted into one corridor.
- D46 **Seeded dungeons, reusable everything else** (2026-10-22 00:0xZ). Matt: "we need a video game productivity engineer to help out architecturally, like make everything reusable, except the dungeons, and they should generate by seed honestly... then we fill with enemies... like consider order of operations... everything can be reusable and quick, and we can attack the rare edge cases once the game is testable per the objective". Rule: dungeons generate by seed (forward-only with a fork, D45) and are filled from a pack roster; everything else is reusable data-driven components; edge cases wait until one full run is testable. WHY: an architecture that is fast to change.
- D63 (2026-10-22): seeded dungeon architecture is the plan of record; generator in Sim/Dungeon; see game/design/ARCHITECTURE.md. Contract: one fork right after the entrance into two branches of 2 or 3 rooms that converge on the boss, entrance-to-branch edges are one-way doors. WHY: D47 (the id first proposed) is taken by HALL SENTRIES and D46 is used twice, so this takes the next free id; main's entries are not renumbered.
- R-BOSS-1 (2026-10-21 23:5xZ). Boss fight 90 to 180 s at key 2 = 1800 to 3600 ticks at 20 Hz; Cleave, Twins and Lock each at least twice, each answered by a bot. Matt: "the boss should be like a minute still on learning dungeons minimum lol, 10second boss is insane". WHY: a boss that dies in seconds teaches nothing.
- R-PATH-1 (2026-10-21 23:5xZ). Deterministic integer pathing in the sim, never Godot navigation. Matt: "still no pathing which was supposed to be done yesterday, just a linear route... drifted." WHY: D3 keeps the sim headless and deterministic; the route was 9 straight points at z=0 with no pathfinder.
- R-DECLUTTER-1 (2026-10-21 23:42Z). The target frame cast bar belongs to the current target; a cast left mid-retarget draws overhead. Proof lines are sampled from one moment, not two. No Matt quote in the ledger. WHY: the #528 game-e2e failure came from proof sampling (unitframe line at step time, castbar line at view time).
- R-PATROL-3 (2026-10-21 23:00Z, WoW trash; amended by R-PATROL-3c 00:3xZ). Patrol melee swing >= 40 ticks; patrol HP down so Warden plus Cinder kill it in 400-600 ticks at k2; ScriptedWarden holds Brace for an enemy targeting it under half HP. Red test: half-HP Warden and one DPS, no healer, survives the corridor patrol. Guard: a three-seat party still loses >= 20% Warden HP (floor 80, amended from 25%). No Matt quote in the ledger. WHY: probe root cause was the healer abandoned at 1571, Warden unhealed 305 damage, Brace on cooldown; bots do target the patrol (invisible-patrol hypothesis refuted).
- R-PACKS-7 (2026-10-21 23:5xZ). S2 folds into S7: forces values ship with the packs that place them; the earner rule stays. WHY: ContentValidator's earner rule rejects forces values with no placement, which blocked the S2 lane twice.
- R-ORACLE-1 (2026-10-22 00:1xZ). Whole-run hash asserts are non-required; kept as byte-identical proof for mechanical splits; deleted once every ruling has a named property test. Amends D28. Math vector oracles, the frozenSources check and anti-vacuity guards stay. Matt (23:4xZ): "this seems like an architectural issue, you are losing understanding of your code base so quick". WHY: hash-pinned trace oracles make every behaviour change a re-record and a conflict.
- R-LOADER-1 (2026-10-22 00:1xZ). An ordinal-order directory loader is allowed, with byte-identical trace proof first. No Matt quote in the ledger. WHY: the seams review found serialization tied to explicit file lists and single validator lists.
- R-STALE-1 (2026-10-22 00:2xZ). An open PR blocks a refactor slice only if it is mergeable (not DIRTY) or was updated within 12 h. Stale DIRTY PRs with no live lane do not block; they are re-cut later. Closing them is presented to Matt first. WHY: stale DIRTY PRs such as #298 owned HudSceneTest.ps1 and blocked every split (the S3 lane stopped on it).
- D65 **Dungeons are 15 to 20 minutes with multiple bosses; the Hollow Vault is the tutorial; a human plays seat 1 from one launch** (2026-10-22 04:1xZ, ledger f-8fc6e252, event store seq 89). Matt: "tomorrow morning i want to be able to play the tutorial and have at least 1 other dungeon with multiple bosses, something the bible got wrong is dungeon length was 15-20min, not 1min lmfao...". Rule: (1) a dungeon is 15 to 20 minutes of play at key 2 (18000 to 24000 ticks at 20 Hz); par is set per dungeon from its route (bots at key 2 inside par by a margin the content states), not a fixed 12:00. (2) The Hollow Vault stays the tutorial: three teaching pulls and the Hollow Warden on the full forces route (R-FORCES-1: 100% forces and the last boss), target 8 to 12 minutes at key 2, longer than today's five. (3) Every other dungeon has at least two bosses, each inside R-BOSS-1, joined by branching segments per D45/D46, seeded as entrance, fork, boss, fork, final boss. (4) The human plays seat 1 end to end (screen with five tabs #583, Queue, dungeon, choice of three, screen) from a one-line launch documented in README.md. Every earlier ruling stands; the "12:00 par" and one-boss lines elsewhere are history (amended 2026-10-22: D65). BIBLE s1, s9 intro, s9.8, s12. WHY: the bible's one-boss, 12-minute par shape produced runs of 3 to 5 minutes (the 10-21 captures in this file: 0:34, 3:14, 3:21, 4:23 of 12:00), which is not a Mythic+ dungeon.
- D66 **Themed mobs, huge pulls, any class** (2026-10-22 04:2xZ). Matt: "you can be creative with mechanics and use inspiration from other games, ensure its themed properly to the name, and has unique mobs" / "I also want to be able to play any class, not just the warden" / "if you dont know, defer to wow, huge pulls are fun". Rule: each dungeon has its own themed mobs with new mechanics (some reused); layouts and packs allow chain-pulling several packs, and the danger is what the mobs do together, never a scripted wipe; the human plays any of the three classes with bots filling the other roles; when unsure, defer to WoW. BIBLE s9.9. WHY: unique, themed dungeons and big pulls are the fun of Mythic+, and a Warden-only human seat tests one class.
- D67 **Three bosses minimum past the tutorial; five-boss dungeons later** (2026-10-22 04:3xZ). Amends D65 point (3) from two bosses to three. Matt: "any dungeon past tutorial should have 3 bosses" "min" "some players like longer dungeons, ultimately we'll end up with some 5 boss dungeons that drop the bis items and take longer, its inevitable, you need some difficulty curve other than scaling though and thats master a longer route/path and perfect coordination". Rule: every dungeon past the tutorial has at least three bosses, seeded as three segments (entrance, fork, boss, fork, boss, fork, final boss); five-boss dungeons come later, take longer and drop the best items; the difficulty curve is mastering a longer route and perfect coordination, not only key scaling. BIBLE s9 intro, s9.8. WHY: key scaling alone is not a difficulty curve.
- D68 **D65 progress, merged on main** (2026-10-22, orchestrator-verified on GitHub). #592 MERGED 1497ee4f (tools/play.ps1 human play path; any class via HumanClassSeatsTests warden/chanter/cinder). #591 MERGED 63003ec7 (seeded three-segment generator). #595 MERGED 78d396e9 (Sunken Bellfoundry content: bosses Ommet, Brine Mother, Great Bell; par 24000 ticks). #600 MERGED ffb85600 (single-seed pins converted to properties). #598 MERGED da44dcfd. WHY: D65 needs a second multi-boss dungeon and a human-played seat 1 on main; these are the pieces that landed.
- D69 **D65 progress, open PRs at fold** (2026-10-22, orchestrator-verified on GitHub). #604 (Bellfoundry fill and multi-boss run: SegmentFill, SeededLayout.BuildSegmented/ArmSegmented, BossRules.ArmAll/Advance, EncounterState.NextBosses, boss actor ids 900/910/920, HostRunSetup.Build dungeonSeed, LayoutDef.BossPullIds boss-s1/boss-s2/boss) OPEN at eff81bbb, ready, auto-merge armed, waiting on checks. Autorun key 2 seed 1 on #604: Ommet killed at tick 8024, Brine Mother 16664, Great Bell 22903, run_end Timed 22904 of par 24000. #605 (dungeon select: --dungeon flag, DungeonPick, Key-tab dungeon row with no-map refusal) DRAFT at fdcf09bf, forwards after #604. #607 (pack aggro/chase property tests, 25 cases over seeds 1..5) OPEN 31898d6c armed: main already has per-mob aggro 8000 mm, assist 3000 mm, leash 30000 mm, whole-pack engage and A* chase from #573 85b3ad98; the capture that showed packs standing still predates #573. #602 (game/PLAY.md play guide) OPEN ffed621c armed. #594 (ability tooltips and themed names, content/data/ability-text.json) OPEN 6c636c4c armed; #603 (names everywhere) stacked on #594. #606 (policy bots pull to 100 percent forces; contains #599 scene tick budgets 13000 and #576 forces-before-final-boss) OPEN d3d664e9 armed, being forwarded.
- D70 **Parked PRs, closing is Matt's call** (2026-10-22). #577 (dungeon branches view, 44a1cf6c) and #578 (branch choice) are DIRTY against the segmented layout; after #604 lands a lane re-targets the branch view onto the segmented layout. #599 (a9bf39a2) has five real reds on the merged tree and is superseded by #606. #576 content is carried by #606. WHY: the segmented layout replaces the single-route shape these PRs were cut against.
- D71 **Captures owed to Matt (D15)** (2026-10-22). Per-class human play via tools/play.ps1; Bellfoundry 3-boss run via --dungeon after #605; moving packs on current main; one label per idle pack after #596; ability names after #603; the #605 Key tab dungeon row. Acceptance is Matt watching, not CI green (D15).
- R-MULTIBOSS-1 (2026-10-22). A multi-segment dungeon's run ends on the FINAL segment boss's death only. Earlier boss deaths emit boss_death and the run continues. Each boss's rules arm on pull engage and disarm on death. WHY: a run that ended at the first boss would make the Bellfoundry a one-boss dungeon, against D67.
- R-KICK-WARD-1 (2026-10-22). A Chanter Dissonance ward silence that pre-empts a Choral Mending is good play, not a missed kick. WHY: the kick ledger must not punish the silence that is the Chanter's correct answer.
- R-BOTTANK-LEASH-1 (2026-10-22). A faster bot tank chase is wanted, but a bot tank never out-runs its party: it does not engage a pack before the human seat can reach it. WHY: a bot tank that pulls alone leaves the human seat behind and breaks the party.
- P-REFORWARD (process, 2026-10-22). A DIRTY armed PR is forwarded once per main advance with a haiku lane; it is never re-cut.
- Open findings worth a line (2026-10-22). f-f117e3de: policy bot parties finish timed on about 2 of 8 seeds at key 2 with pull-to-100 on (Warrior Remnant kick economy: two Cantors each heal every 200 ticks against three 480-tick interrupts; candidate fixes: readiness hold before a Cantor pack, teach PolicyBot CC stops). f-82f89359: multi-boss follow-ups from #604 (PullHeader has no title for boss-s1 and boss-s2; --pull N undefined for multi-boss; SimDriver must pass dungeonSeed; HUD boss-frame switch capture owed). f-61f01ca3: nothing selects the Bellfoundry until #605 lands (play.ps1 -Dungeon follows).

### Rulings 2026-10-22 01:5xZ to 02:4xZ (from the event store, conductor3 events/log)
- R-WARDEN-FILLER-2 is WITHDRAWN (Strike is the Warden's builder and Sundered setup by design, BIBLE s4).
- R-WARDEN-FILLER-3 (01:5xZ, shipped on #540). #540's filler gate applies to trash pulls only; in the boss pull the ScriptedWarden keeps Guard Break priority. Accepted Lock hold: the Warden bot spends no global while a boss Lock waits for a partner's Setup. WHY: the boss-pull filler opened 74 windows whose Consecrate heals masked a careless seat.
- R-BREAKBAR-1 (01:5xZ, #558). The Twins' shared Break bar drains 50 per interrupt-tagged kick and 20 (DrainPerControl) per control-tagged cast at a Twin, casting or not; two kicks on one tick drain 100; a broken bar does not move. Single-seed partial-fill pins become properties (R-ORACLE-1).
- R-BOSS-4 (02:4xZ). An unanswered Hollow Warden Cleave applies a stacking Cleaved debuff (+damage taken per stack, 20 s, refreshed on hit). WHY: carelessness escalates past healing, so the careless-seat tier loss is a property of the mechanism, not of one seed's deaths.
- R-PINS-1 (02:4xZ, R-ORACLE-1 applied). A Playtest assertion pinning one seed's trajectory (exact tick, HP, which seat) is replaced by the property it guards; re-pins only for documented drift detection, with the cause named in the commit.
- R-FORCES-1 (#576). A key completes only when enemy forces reach 100% and the final boss is dead, in either order; the timer keeps running; "timed" only if the completion tick is within par.
- R-AGGRO-1 (#573). Each enemy has its own aggro and assist radius; a pack aggroes together; enemies chase along the R-PATH-1 A* path; past the leash a pack evades home and resets at full health; pull_start fires on the first member that aggroes. Defaults in run.json enemyAggro: aggro 8000 mm, assist 3000, leash 30000, boss exempt.
- Plan PRs (01:5xZ, under D64): #546 CLOSED (contradicted D46 seeded dungeons and R-PACKS-7). #552 is the plan of record; its D63 entry lands with #552 (still OPEN at main 9a5f4f90).
- D64 **Matt's verdicts 2026-10-22.** Matt: "why is there still no pathing? and the first couple packs just stand still? doesnt really make positioning important at all and you can just go one at a time lol"; "like we discussed, m+ style routes with enemies, two seperate branches to discover, not a linear dungeon"; "just make decisions lmao". Rule: the orchestrator decides balance reds, ledger triage and plan PRs itself and reports; hard stops unchanged. WHY: packs that never aggro and a linear route make positioning meaningless (R-AGGRO-1, R-PATH-1, D45).

**Time note (2026-10-22 00:0xZ).** The sim ticks at 20 Hz (Core/Tick.cs:28), so durations quoted in ticks divide by 20. The earlier "10 second boss" figure was a 60 Hz error. The boss pull was about 29 s (585 ticks); Choir was about 82 s (1651 ticks).

**Pointers.** Architecture reviews, read-only on main 1f8fed7f: comprehensive-review-architect-review a81a3506 (seams, splits, migration order); dotnet-architect afae3be1 (C# faults, top 10 by lanes unblocked); honest-opinions a688dbb0 (vanity vs product, freeze list). Plan PR #546 (docs/mplus-dungeon-plan). Review doc: https://claude.ai/artifact/NjLQMFt8fNTTojNwfP6S45

## Phase plan
- Phase 1 (research day): 9 cited briefs (incl. Fellowship) -> design/BIBLE.md v1 (Matt reviews the taste calls).
- Phase 2: sim/ rules engine (deterministic, tested) + content v1 (3 classes, 1 dungeon, 1 boss).
- Phase 3: bots/ + client/ (Three.js) -> playable in a browser tab, one human + bots.
- Phase 4: server/ authoritative tick + queue/lobby + character screen -> 5 humans.
- Phase 5: tuning with Matt playing.

## Matt's verdicts on the 3D client (keep these in view)
- Pre-#98: "no abilities, ability projection, everything is a dumby" and "kovaaks with a minecraft bar".

## 2026-10-21T15:45Z checkpoint (after #476 landed; #475/#477 updated and re-armed; #478 audited, not armed; compaction at the 380k mark)
WHY: write-before-act rule 4: this block names every unpushed sha and every armed head at the compaction boundary, so the next span rebuilds from disk and gh, not from memory.
- keyrun main 6e64c33e (#476 run N scenario 2@300, pr_merged_check MERGED). #475 tools abs-path: disarmed, update-branch, re-armed at d8bfd965 (15:35:00Z). #477 lull fix (R-SEED-1a): update-branch, re-armed at 382ac6d4 (15:35:02Z); its first game-e2e failed only on run N, digest pin held. #474 checkpoint: GitHub PR object stuck at 7046b583 while the ref is e57acee6 (update-branch now refuses "head sha didn't match"); a haiku lane is opening ckpt/1540z (cherry-picks of origin/main..7046b583 plus the ckpt-1435z block via the fold shape), closes #474 with a comment, arms last.
- #478 ci/docs-only-single-check-run c4b84701 UNARMED. Read-only audit: proof items 1-4, 6, 7 YES with file:line (docs-only.yml deleted; no paths filters; no job-level if except game-gate always() with needs; real steps gated code=='true' && success(); one job per required context; Gate-Proof trailer). Verdict DO NOT ARM: (a) checks incomplete, then game-test FAIL (cut from 4a21de2b, before #476; run N); (b) rename hole: scope uses git diff --name-only without --no-renames (game_gate.py:62-64 has it); (c) *.md under tools/ is code (tools/Keyrun.Playtest/GUIDE.md is an EmbeddedResource asserted in game-test); .github/CODEOWNERS classed docs. Follow-up lane on the same branch: merge origin/main, --no-renames with a repro, narrow the docs set, replay cases, `gh pr edit`, no arm. Arm only after a second check-run listing shows one run per required name and PASS. Hook false positive to narrow via brain PR: `git log -n 400` read as --no-verify by the no-verify hook.
- Open keyrun PRs after this: #475 (armed), #477 (armed), #478 (unarmed), ckpt/1540z PR (lane arms). Over the WIP cap of two; nothing new opens until two land. R-SEED-1b (warrior-remnant link + seed fact, keyrun-wt/seed-room-2 uncommitted 8 files + fact) waits for #477 in main. gate_change_check (f-ac8d99b4) waits for the cap.
- aesop #1250 (ordering + discovery fields) and #1251 (hook-safe shapes) armed; contract lines still owed: Fable trailer and Aesop footer stated explicitly; GATE-CHANGE PRs armed only after a lane reads the diff (memory gate-change-prs-read-diff-before-arming written); `~` ban is clock-only; scratch folder named in every brief; retro path C:/Users/Mattt/scripts-wt/ledger-retro/dispatch_ledger.py.
- Monitor b0h9ht7dp over 474/475/477/478 keyed on ls-remote heads, expires ~16:05Z. Refinement monitor cycle 694 at 15:14Z (heartbeat fresh).
Unpushed: keyrun-wt/seed-room-2 uncommitted (R-SEED-1b WIP); keyrun-wt/leader-reach local commit 07898efa on fix/leader-closes-to-reach (superseded); scratchpad ckpt-1435z.md (being folded by the lane) and this block; aesop primary main 148 commits off-remote per the collector (Matt decision, not verified by me); nothing else local.

## 2026-10-21T16:20Z block (after the 1545z block was handed to the fold lane; new lines go here)
WHY: the 1545z block is being copied verbatim by the ckpt/1615z fold lane, so later facts land in this file for the next fold.
- 16:18Z aesop #1252 (LANE-CONTRACT: attribution, gate-change proof shape, clock-only tilde, scratch folder, retro path, STATE-only fold diff, merge-check shape, hand-back fields) gh state MERGED, mergeCommit 88a9c331. Lanes dispatched from now on read these rules from the contract, not from briefs.
- Clock correction: the "16:14Z" and "16:18Z" labels in the 1545z block's last two lines and the line above were estimates, not clock reads; gh says #478 merged before 16:09Z and #480 was armed at 16:10:50Z. Timestamps come from the clock script or gh, never estimated.
- 16:10:50Z keyrun #480 ckpt/1615z at 2bed02e9 armed by the fold lane a2b0e17ef92d5fb7c (VERIFY PASS on the STATE-only diff, 27 lines; log +28/0). Its first check-run listing showed test, e2e, game-test, game-e2e, balance queued once each, no detect job, game-gate not yet posted; the pass-through proof is the listing after completion (orchestrator reads it once on the monitor's PASS or MERGED line). Contract line owed: the folded block sits directly above `## NEXT STEPS` with no blank separator (the verifier counts every added line).
- 16:11Z PROOF of #478's docs-only pass-through, live on #480 at 2bed02e9 (check-runs listing read once by the orchestrator): test 16:10:51-16:10:56Z success; e2e 16:10:51-16:10:59Z success; game-test 16:10:51-16:11:03Z success; game-e2e 16:10:51-16:11:06Z success; balance 16:10:51-16:11:05Z success; game-gate 16:11:09-16:11:17Z success. Each required name exactly once, no detect job, no skipped duplicates, every job completed as success in 5-15 s (pass-through steps inside the real jobs). The #473 fail-open shape is gone on both a code PR (#478 itself) and a docs-only PR (#480).
- keyrun #480 ckpt/1615z gh state MERGED (monitor line merge=1fce4bec; bound by gh pr view below in the span). keyrun main carries the 1545z checkpoint. Refinement monitor cycle 696 launched (lane ab6224cc4856ddbb0) at heartbeat age 1634 s.
- 16:13Z R-SEED-1b relaunch a56f2b8be322085f6 BLOCKED again, no commit, no PR; branch origin head still ebac6128 (superseded). Its attribution runs are the evidence: with the warrior-remnant link removed, 199/200 playtest tests pass (only the link-requiring test fails); with the link, five real regressions (careless seat, Bulwark under pressure, healer kick-ledger, party scene depleted at 17734, Choir lesson Depleted) because the Seed pull becomes a 7-body fight the bots lose. Forces are per enemy in forces.json and pinned by bible 9.2 (Pair 5x52, Choir 2x40+1x80, Seed 180); required route on main sums 840, with the link 1260; the bible says the route needs an optional pack.
  RULING WITHDRAWN (bible wins over the orchestrator's earlier call): rulings (a) and (d), the warrior-remnant link into the Seed pull, are dropped. A 7-body Seed pull contradicts section 9 ("teaches the boss's core rule with no stakes") and 9.2's forces arithmetic. Ruling (e) is moot (no per-pack weight, no bible value changes). Ruling (f) stands (#477 is the one engage rule; the lane confirmed the carried close-in caused the parity failures). The Links schema, validator and test from the WIP are not shipped. The KF3 "no Seed-elite event before its pull" test passes without the link, so entry-spawn is already handled on main.
  What the Seed room still owes section 9: the Seed (actor 108) dies at its own pull tick 592 to one 130-point hit, so the core rule never plays. Next: a read-only recon of the Seed pull on main against the section 9 Seed paragraph (quoted verbatim), then one slice (R-SEED-1c) that makes the Lock rule play at least once with no stakes.
  One-source-per-fact gap found by the lane: briefs point lanes at C:/Users/Mattt/Desktop/aesop/LANE-CONTRACT.md, but that primary checkout is Matt's diverged local main (282 ahead per the collector) and does not carry #1252; the contract lanes must follow is origin/main. From now on every brief says: `git -C C:/Users/Mattt/Desktop/aesop fetch origin` then `git -C C:/Users/Mattt/Desktop/aesop show origin/main:LANE-CONTRACT.md > <scratch>/LANE-CONTRACT.md` and read that file. Matt decision pending: reconcile the aesop primary main.
- 16:15Z refinement monitor cycle 696 (ab6224cc4856ddbb0): no auto-applies; staged P-CHURN-KEYRUN-20261021g (five stale local keyrun branches 20+ commits past origin/main with no origin ref: lane/bots-ci-449 41, measure/integrate-bots-mint 27, pr250 25, exp/cast-tiebreak-307 23, lane/rebase-242 20; none is a live lane, Matt's GC call). conductor3 live checkout holds uncommitted collector output (Matt or orchestrator, not deleted).
- 16:18Z recon-slam (a8e0218bb98e533bb, static read of origin/main 1fce4bec, no run; its unit-test run was denied by no-lane-game-launch on heredoc text with `$()`, f-88930434 class): STATE.md's known gap "boss slam and Twins slam untelegraphed" is STALE on main. Every Warden, Twins and Seed threat has castTicks>0 and a spansCast floor telegraph (landed via #313 SLAM-TELEGRAPH 92d1333b, #302, #257, #389, #323/#351/#378): hollowWarden.slam 10t circle r3000; cleave 40t cone r4000; lock 20t circle r4000 via CastLock (HostRunSetup.cs:523, BossRules.cs:317-393) plus LockView ring; twins.slam 10t circle r3000; seed.slam 20t circle r2000; seed.lock and seed.deathBloom instant, no damage, not threats. View renders disc and sector (TelegraphView.cs:135-187); boss bar UnitFrames.cs:315-335. One mismatch: seed.lash is a target-shaped cast (abilities/enemies.json:169-171, range 1500) drawn as a caster-centred circle r1500 (enemies/seed.json:12-13; Scripts.cs:145-154), over-stating the hit.
  RULING (dungeon loop, WoW way, within s3 "shape plus colour"): a telegraph draws the area that is hit. seed.lash hits one target, so it draws a line from caster to target (a listed s3 shape), not a caster-centred disc; slice R-TELEGRAPH-LASH = seed.json telegraph kind line to target + Scripts.cs origin handling + EveryEnemyAbilityTelegraphedTests assertion that target-shaped casts draw line or target-circle, never caster-circle. Low priority behind the Seed slice. Also owed: a host-path threat test (the in-play Theory runs on the oracle fixture, not the host composition; new file under game/bots/Keyrun.Bots.Tests), lock included in EveryEnemy coverage, boss-frame cast mark is colour-only (UnitFrames.cs:327, LOW), and the STATE.md known-gap sentence corrected in the next fold (this block does it).
- 16:19Z recon-seed (aef357eff244b66c9, static read of 1fce4bec): the Seed on main already carries the lesson the bible asks for. Bible 9.6 item 3 verbatim: "**The Seed** — an elite that on death drops a Window on the floor; a Combo landed in it grants bonus loot at run end. Teaches the boss's core rule with no stakes." On main: seed.elite maxHealth 3000 (3090 at key 2), prespawned idle, engages on aggro ring or damage (Aggro.cs:70-90), opens a 60-tick self-Window "seedlock" at engage+20 that any non-boss Payoff closes through the boss Lock combos (Payoff.cs:77 keys on classes, not window id), slam 150/lash 70 telegraphed, deathBloom window with a no-damage Combo and bonus loot (SeedBloomNoStakesTests, SeedLoot). Landed by #195, #289, #374, #328, #330, #197, #188. Guards: SeedPullTeachesTests.cs:168-184 (a Combo closes the Seed's own window before it dies), :150-159, SeedOpenerTests, SeedEngageTests. The R-SEED-1b lane's "dies at tick 592 to one 130-point hit" cannot be true from full health against 3090 HP with no phase logic (Damage.cs:52-135); that claim is RETRACTED as misattributed; the finding carried earlier is withdrawn. The bible is silent on Seed HP, on whether it may hit the party during the lesson, and on a WoW precedent; nothing beyond the bible is built.
  DECISION: no R-SEED-1c build. The Seed room on main is bible-complete as far as tests pin it; KF3 (f-70c82aca) closes only by D15, Matt watching a capture of main. Next action is the orchestrator's capture of main 1fce4bec (single-instance, orchestrator-only) and a VERDICT on the Seed room, then Matt's human-played run.
- 16:2xZ D15 RECEIPT main 1fce4bec: worktree keyrun-wt/demo-1fce4bec built, Godot-imported (120 assets), captured (14 frames at 4 s, run_end at 53 s wall), five frames viewed by the orchestrator as the PM, demo.log read by a lane. VERDICT.md at C:\Users\Mattt\Desktop\keyrun-screens\demo-1fce4bec\VERDICT.md. Facts: all four pulls in order, 0 deaths, timed 3:21 of par 12:00, Enemy Forces 84% (required route 840/1000, optional packs skipped, 14 enemies left standing). Seed: pull_start 3182, seedlock opened 3202, closed by Combo lock-cinder 3215, lash hit 31 at 3252, slam miss 3300, death 3366, seedbloom Combo 3389, bonus on the end panel. Choir: five Choral Mending casts, five kicks, zero heals. Boss: one Lock cast 3817, broken by a forge Combo 3884; every damaging boss and twin cast telegraphed. KF3 f-70c82aca was already closed at 06:21Z (resolution fixed, #396); this receipt re-confirms it on main with log ticks. Still wrong: boss camera inside the boss model (KF1 f-fa28e36e, unchanged since d56758fe); the Seed room reads as a Hall Sentry trash fight first and a 9 s lesson second (bible-legal, feel is Matt's call). Telemetry oddities carried (ledger full): ticks continue 315 past run_end; duplicate EVENT lines; 82/119 heal events amount 0; telegraph_hit lists the caster boss/twins as affected; seed.slam telegraph_hit vs miss with no cause; CAST lines carry no tick; run_end has no deaths/time; no loot award event. STATE.md known-gap sentence "boss slam and Twins slam untelegraphed" is stale and is corrected by this block.
  NEXT for the demo: KF1 camera (boss fills the viewport) is the one visible fault left on the default run; slice R-CAM-BOSS = camera keeps the boss's head in frame when the player is inside its reach (client camera code only, judged by frames at the boss). Then Matt's human-played run (Phase 3 exit).
- 16:3xZ R-CAM-BOSS dispatched (lane a1be7a0c7c05ebba4, branch fix/boss-camera-framing, client camera only, unit test on the framing math with a red proof, lane arms; judged by an orchestrator capture at the boss after merge). WIP: s3-hosttest + cam-boss = 2, at the cap.
- 16:28:29Z keyrun #481 test/s3-host-path-threats at 3cddc1bc OPEN, armed by the lane pinned to the sha (a546457e994771d5c). HostPathThreatTelegraphTests drives the default host composition at key 2 seed 1 and key 10 seed 7; red proof: cleave telegraph removed from hollow-warden.json => "actor 900 hollowWarden.cleave start=3620 land=3660: no floor telegraph in time" (restored). Bots.Tests 331/331 gated; balance fork-parity red locally and on main's own game.yml runs (a31b77b9, 4a21de2b: groupSong<swift, fortress<mobile), a standing balance finding, not gated. Judgement call ACCEPTED by the orchestrator: the Bots test project now links BotCommandSource.cs, ScriptedWarden.cs, CastTargeting.cs and a HostCommandSourceShim (the same pattern Keyrun.Playtest uses); test-project only. Lane used the Fable trailer from the origin/main contract.
- 16:32:36Z keyrun #482 fix/boss-camera-framing at 1fcd8531 OPEN, armed by the lane (a1be7a0c7c05ebba4). Rule: CameraHeadroom.Adjust (new pure file in Keyrun.Content/Model beside the existing CameraFraming math) pulls the boom back by the least amount that keeps the target's top inside the HUD top band (850 permille of the half-frame), pitching flatter in 25 mrad steps only past the 12 m zoom ceiling; CameraRig.HeadroomTarget feeds it the Warden's current target (top from ActorBinding heights) and eases at BoomRate. Red proof: "Range: (6500 - 7200) / Actual: 5000" with Adjust returning its inputs. Content.Tests 775/775, Sim 1328, Bots 325, balance red (standing). Risks to read from CI: CameraFramingSceneTest.ps1 asserts Warden 200..350 permille at default zoom (not run by the lane). Lane used the Haiku trailer despite the origin/main contract (second lane this hour; the contract text is not being honoured over the harness reminder; Matt or a hook decides). Judged by an orchestrator capture at the boss after merge.
- Live lanes: none. Monitors over #481 and #482. a546457e994771d5c (test-only PR on test/s3-host-path-threats: host-composition threat telegraph coverage for actors 108, 900, 901, 902, red proof required, lane arms). R-TELEGRAPH-LASH waits behind the Seed slice (both touch enemies/seed.json). R-SEED-1b ended BLOCKED (see above); no keyrun PR open from this span. a56f2b8be322085f6 (rulings e, f) and fold lane a2b0e17ef92d5fb7c (ckpt/1615z, first live docs-only pass-through under #478). Monitor bbtmxy4bg (478, 1252) is spent: both MERGED; restart over the two new PRs when their numbers arrive.
- 16:50Z keyrun #481 (host-path telegraph test) gh state MERGED, mergeCommit cef520bd; balance FAILURE at 3cddc1bc (fork-parity, reported not gated per Matt's item; the standing groupSong<swift, fortress<mobile finding), every required name SUCCESS once. No disarm this time (the #477 thrash shape was not repeated). #482 at 1fcd8531 OPEN, armed, game-e2e in progress, balance FAILURE (same standing finding); wait = not PASS. Monitor cadence tick 16:4xZ: heartbeat 1416 s, churn 173 unchanged, nothing relaunched. WIP: one slot open under the cap. Decision: R-TELEGRAPH-LASH dispatches now into that slot (content + Scripts.cs + one test assertion; no file overlap with #482's camera files); the Seed slice it was queued behind is withdrawn. The fold of this block waits until #482 lands and the boss capture verdict exists, so one docs-only PR carries both. Lane reads the contract from aesop origin/main 88a9c331 via the scratch copy.
- 16:5xZ R-TELEGRAPH-LASH lane abb972cd77effd8f3 BLOCKED-LANDING, no PR, nothing committed (worktree keyrun-wt/lash-line at cef520bd with one uncommitted test edit; patch at scratchpad/lash-lane/lash-line.patch). Red proof holds (Sim.Tests EveryEnemyAbilityTelegraphedTests: "seed.elite seed.lash: telegraph must be a line of length range 1500 from caster to target, is circle"). Why blocked: (1) Content.Tests guard EnemyLineTelegraphGuardTests (NO-ENEMY-LINE-TELEGRAPH) forbids enemy line telegraphs because game/src/View/TelegraphView.cs draws fans only (the Pair-room line in the demo frames is a player facet line); (2) the oracle setup fixtures hollow-vault-k2/k10 and bots-hv traces embed the lash circle (re-record only with ORACLE_WRITE=1 and a reason); (3) LockWinnabilityTests x2 go red because the hit area moves (circle r1500 hits every melee body; a line hits the target's lane). Tags: contract-ambiguous (the general target-shape rule also flags pair.spreadMark and pair.stackCall; the lane narrowed to lash), code. Discovery: seed content lives at content/data/enemies/seed.json (frozen root, embedded by Keyrun.Content.csproj:35), not under game/content; the brief's path was wrong.
  RULING (dungeon loop, the WoW way): a single-target lash from a boss-class enemy is drawn as a beam from caster to target; the circle stays wrong (it tells the whole melee they are hit when only the target is). R-TELEGRAPH-LASH is re-scoped as two PRs, in order, after the #482 capture verdict and its fold: LASH-1 view: TelegraphView draws a rectangle for enemy line telegraphs, the guard test is replaced by a drawability test (never deleted, never skipped); LASH-2 content: seed.json lash becomes the line, oracle fixtures and traces re-recorded with ORACLE_WRITE=1 and the reason "seed.lash telegraph circle->line (BIBLE s3)", Lock winnability re-read: if the lock is still winnable the goldens are re-recorded, if not the Seed room design is wrong and Matt hears it. New finding to carry (ledger FULL): pair.spreadMark (range 25000, circle r3000) and pair.stackCall may be caster-centred circles on target-marked abilities; WoW marks the player, so the circle must anchor on the marked target; recon before any lane.
  Monitor cadence tick 16:5xZ: heartbeat 2505 s, refinement monitor relaunched as cycle 697 (haiku per the lever); churn 173 unchanged, 590 local branches.
- 16:57Z keyrun #482 (boss camera headroom) gh state MERGED, mergeCommit a8c033f3. main = a8c033f3. No live keyrun PR open (old pile of 14 untouched).
- 17:00Z D15 RECEIPT main a8c033f3 (VERDICT.md at C:\Users\Mattt\Desktop\keyrun-screens\demo-a8c033f3\VERDICT.md; frames 011/012/013/014 of 14; worktree keyrun-wt/demo-a8c033f3 built and imported, kept for captures). KF1 f-fa28e36e NOT CLOSED by #482: the headroom rule frames the Warden's current target (works at demo-013, Twin A's head in frame) but at demo-012 the target is Twin B and the boss, the tallest body in reach, still crops its head at the top edge as in demo-1fce4bec-012. RULING (dungeon loop, WoW way): the camera serves readability of the thing that will kill you; the headroom target is the tallest engaged hostile inside the camera's view-cone and the Warden's reach (boss over twin over anything), not the current target. Slice R-CAM-BOSS-2 dispatched now (CameraRig.HeadroomTarget selection + a CameraHeadroomTests case: target=twin, boss adjacent and taller => the boss top drives the boom; red proof on main). Secondary new fault from the boom pulling back: untextured foreground bodies between camera and Warden drawn solid at demo-013 (green/blue blobs); near-camera fade or collision clamp is a follow-up slice after R-CAM-BOSS-2 is judged. Run outcome identical to 1fce4bec (seed-identical sim, client-only change): 0 deaths, 3:21, Forces 84%, Death Bloom bonus paid.
- 16:59Z refinement monitor cycle 697 done (a0fb40ae1b286bc53): heartbeat beaten; 0 new HIGH/MED alerts (18 unresolved); staged P-CHURN-KEYRUN-20261021h: NEW no-upstream branches 20+ past origin/main fix/pile-e2e-reds 81, fix/pile-chorus-parity 80, fix/r-bots-3-press-first 29 (none live; Matt's GC call, with the 20261021g set); plus a proposal for a committed churn-count script under ~/scripts. Discovery: rotate_logs.py did not rotate ACTIONS.log at 38 lines / 20686 bytes (byte threshold may not match the charter's 20KB); machinery, carry.
- 17:0xZ fold lane dispatched for this whole block (docs-only PR via the fold shape; STATE-only diff verify; block directly above `## NEXT STEPS`, no blank separator; it corrects the stale "boss slam and Twins slam untelegraphed" known-gap sentence in INTENT with the recon result; lane arms last; reports the check-run listing as the second live docs-only pass-through proof). WIP after dispatch: R-CAM-BOSS-2 + fold = 2, at the cap.
## CHECKPOINT 2026-10-21 21:12Z (orchestrator)
WHY: this block folds the unfolded orchestrator checkpoints 17:05Z to 21:12Z (source: the orchestrator scratch file ckpt-1705z.md, every block marked UNFOLDED) so the durable record matches gh state, and records why the work took the shape it did. Matt (20:0xZ, verbatim): "so i think i've kinda noticed the issue. MVPs have bugs, they just need to be playable. You treat every bug as "act now" and that is waterfall, which causes huge rewrited. We need to lean into a way more agile workflow, split the units of work, hyper parallelize again, because that realiziation hit me last night". Rule taken from it: capture-found bugs go to a triaged backlog unless P0 or unplayable (wrong data stays P0); polish is batched into sweep lanes at phase boundaries; the MVP loop is split into disjoint-file units run by 6-8 parallel haiku lanes. That rule supersedes the 2-PR keyrun WIP cap, which was the answer to nothing merging (eight PRs merged in four hours). Matt (20:1xZ, verbatim): "and keep attacking the game". The orchestrator's own slip, recorded: the #484-#494 camera and telegraph chain was one serial bug chain from captures (ActorView.cs and CameraNearFade.cs rewritten five times) with no MVP-loop unit advancing; that is the waterfall shape Matt named.
- Main at fold time: ed8ad21f (#508 merged 21:05:52Z). The MVP fleet units that have landed on it are U2 (#499), U3 (#495), U4 (#496), U5 (#501), U6 (#497) and U9 (#500); U8 (#502) is still open and armed.
- Rulings: orchestrator rulings, recorded for Matt to overrule. Verbatim from the source:
  - R1 (s8, Clause): "bible wins (out-of-dungeon rule): Clause is set by the key, the pre-queue pick of three is removed -> unit CLAUSE-BY-KEY."
  - R2 (6-tick kick window): "WoW resolves an interrupt the moment it lands while the cast is live; the bible 6-tick window is lag tolerance for intended-tick resolution -> phase-4 networking, bots keep 40 (BOT-KICK-TIMING)."
  - R3 (s9.5 door gate, s9.1 W1 pack): "bible decided, dispatch ROUTE-ON W1 + BOSS-DOOR-GATE."
  - R4 (s7 tier text, s0 queue wording, auto-attack exemption): "bible amendment doc PR recording the decisions." (landed as #506)
  - R5 (KF2, follower lead): "WoW follower dungeons let a follower lead when the player does not; the idle-human handover after 200 ticks stands; CarelessSeatTests.cs:183-190 control is re-premised to "nothing engages before the handover window (ticks 20..199)" with this ruling cited; Matt may overrule." (landed as #508)
  - R6: named only as "R6 Seed room entry" in the source (amendment inside #506); its text is not in ckpt-1705z.md, so it is not restated here.
  - R7 (one pack layer): "one pack layer = layout.json LayoutPack (WoW: one map); packs.json retired (FOLD-U3), branch key on LayoutPack (FOLD-U1), branch totals from layout packs (FOLD-U2, after U1)."
  - DOOR-100 follow-up (R7 note): "raise bossDoorRequiredBp to 10000 once a bots route test proves the default party reaches 1000 by pulling one optional pack (recon: teaching 600 + cantor 400 or warrior 420; hall 240 becomes optional with #502)." (PR #515 DOOR-100 armed by Matt 21:06:25Z.)
  - R8 and "MVP LINE": neither appears in ckpt-1705z.md; nothing is restated for them. The MVP-MAP rulings the source does record: G1 bible wins (no Clause pick); G5 sentries become optional forces packs (WoW way).
  - KF3 (Seed spawns on room entry, f-70c82aca): "WoW rule: pulls inert until reached" (PR #505 armed).
  - Camera and Pair rulings (source lines 39-53): the camera serves readability of the thing that will kill you (headroom target = tallest engaged hostile in the view cone and reach); the near-camera fade applies to party bodies only, enemies never fade under any camera rule; enemies never flash white on hit (party keeps it); a Pair spread or stack mark is a ring on the marked player, nothing drawn round the caster; lock and deathBloom (window casts, no damage) need no floor telegraph, the exemption named (#491).
  - Gates keep their purpose, scenarios move (ruling on HudSceneTest check 8, #489): the check's purpose moved to the bot run where marks land; same strength, not deleted.
- Merged 2026-10-21 (gh, merge order, merge sha): #478 d509f8ae; #480 1fce4bec; #481 cef520bd; #482 a8c033f3 (KF1 camera, boss head in frame); #483 2dc6637d; #485 651a9d75 (LASH-1 enemy line view); #484 5e34e26b (camera headroom body = boss); #486 6f699097 (seed.lash is a line); #487 0e560876 (CAMERA-FG distance fade); #488 7c57b090 (CAMERA-FG occlusion fade); #489 ae5f42db (Pair caster circles removed); #490 4019355e (CAMERA-FG screen-height cap); #491 fa54e174 (named telegraph exemptions); #492 04ed8247 (mark_applied / mark_detonated log lines); #493 b24d596b (enemy bodies stable); #494 b060966f (label cull on death); #496 37d9c934 (MVP-U4 kick pip); #497 03a51ef4 (MVP-U6 gear visual); #495 1f442ffc (MVP-U3 queue wait); #501 c0ac2ba0 (MVP-U5 kick order); #500 1a7996a8 (MVP-U9 Ascension card); #499 ef93562f (MVP-U2 screen band); #506 aaddd2e8 (bible amend R4); #508 ed8ad21f (KF2 bots engage the Choir, R5).
- Open and armed (auto-merge enabled, gh 21:12Z, head sha): #515 492587e5 (DOOR-100); #514 fccdd5d0 (key sets the Clause, R1); #513 28a626ce (retire packs.json, R7); #512 942536d8 (Ascension card line U10); #511 91a618b0 (LayoutPack branch, FOLD-U1); #509 aef671c9 (pair spread-to-stack pin); #507 46ca43cf (R1 key-set pin); #505 91e7e73b (KF3 Seed activates on entry); #504 398fad67 (end-of-run row docs); #503 97ea1405 (KickReady and GearVisual scene tests in game-e2e); #502 fb28d56b (hall sentries optional, G5). Older armed, still open: #454 70dcdb52, #451 5792a774, #382 79ad8783, #364 e7e2092b, #362 ad429611, #306 adfa9673, #298 e423de5e, #295 aceb6666, #250 817969b6.
- Open, not armed: #516 5ad871c6 (delete dead pick-of-three code, R1); #498 0fc2273d (plain --autorun to run_end, not armed); #418 7cca4080 (BLOCKED: the Cantor's Guard makes the Choir a six-body pack, f-82d9390e); #375 42b92dfa (MPLUS-PATROLS); #336 9e4eaf7e (DO NOT MERGE red proof); #327 da3efc43 (cross-class kick order, f-9a4e663c).
- Closed, not merged: #326 closed 20:41:21Z by the orchestrator (auto-merge disarmed first): "Tempo never shrinks a cast loop below its cooldown or cast time (f-57c04b4a)"; reason recorded in the source: the tempo clamp already on main (a7069768), head stale, the PR would have reverted HostRunSetup.cs. #510 closed 21:07:57Z: "Hollow Door gate: Warden engages only after Seed clear and forces bar (R3, BIBLE 9.5)"; the source does not record its close reason; its direction is carried by #515 DOOR-100 (the threshold moves to 100 percent once the bots route test passes).
- Findings carried (not blocking): a dead Seed's lash still resolves in the oracle (CancelUnlanded applies only with CastStartTick); telegraph leadTicks 17 in the event vs 20 in seed.json spansCast, unexplained; oracle resolves the stack telegraph at cast start while casts complete later (contradicts spansCast); the Pair stack call marks everyone inside a caster circle at landing (WoW marks one chosen player; Matt's call); TUNING.md:114 still describes Pair caster telegraphs (stale doc); the TS web client no longer shows Pair shapes (Matt decides whether the TS client is retired); a Paired Warrior label and "Pull 3/4" header while a straggler lives (pull_clear with a straggler, or a label mix-up); demo.log ticks continue after run_end (sentries alive, harmless); balance is red and reported, not gated (standing, non-required).
- Capture record: the source holds captures at a8c033f3, 0e560876, 7c57b090, ae5f42db and 4019355e (receipts at VERDICT.md under keyrun-screens). It holds no capture after 4019355e (19:2xZ). Judged so far: KF1 closed by frames at 5e34e26b (boss head in frame); Chanter and Cinder fade rule proven by CAMERA lines at 4019355e; Pair room rings with no caster circle at ae5f42db.
- NEXT STEPS (orchestrator; the first heading below this block stays the fold anchor):
  - Capture main (ed8ad21f or later) once the armed queue lands, and judge: the boss opaque at every boss frame with zero FLASH lines for actors >= 100 and enemy CAMERA lines applied=1000; the Pair mark_applied and mark_detonated lines; the HUD kick order lines "HUD kick order seat=S n=N" (#501).
  - Run the scene tests the orchestrator owns (not in game.yml unless #503 lands): game/src/Hud/ScreenSceneTest.ps1 (expect "SCREEN clause key=3 id=none" and "SCREEN queue dungeon=hollow-vault key=3 clause=", and no "SCREEN clause n=" lines, from #514), game/src/Hud/KickReadySceneTest.ps1 (MVP-U4), src/View/GearVisualSceneTest.ps1 (MVP-U6), BossAutorunSceneTest.ps1 when #498 is ready.
  - FOLD-U2 (branch totals from layout packs, after FOLD-U1 #511 merges), then FOLD-U3 (#513 retires packs.json).
  - DOOR-100 follow-up: raise bossDoorRequiredBp to 10000 after a bots route test proves the default party reaches 1000 by pulling one optional pack (R7 note above).
  - B57 and B58 (BUG-BACKLOG rows: the Queue readout's open-seat and seats-filled branches are unreachable from the node; from the U3 #495 review).
  - Named in the fold brief, not described in ckpt-1705z.md, so scope them from the owner's brief: TELEGRAPH-PALETTE, the KF1 strike cue, and the #503 k3 fix.
  - Queued slices from the source: boss-frame cast shape mark; dead-Seed lash resolve in the oracle; leadTicks 17 vs 20; telemetry oddities (ticks after run_end, Pull header with a straggler); ORACLE.md section 6 table stale against manifest.json; TUNING.md:114 stale Pair text; Pings.cs stackPending is telegraph-only and dead for Pair (harmless).
  - Matt's rulings still owed: R1 through R7 may be overruled; the Pair stack-call design (one chosen player or everyone in the circle); whether the TS web client is retired; the room design under f-82d9390e (#418 blocked).
  - Matt's human-played run (Phase 5, "Matt playing") remains the acceptance test for the MVP loop, after the captures above.

## NEXT STEPS (engine plan, design/ENGINE-PLAN.md; Phase 5 "Matt playing" stays the acceptance test)
NOW 09:55Z (ckpt/1055z; supersedes the 02:20Z and 01:35Z lists below where they differ):
- Bind the remaining five fleet hand-backs from gh, one PR at a time.
- D15 rows on the integrated build.
- #375 after the bots integration.
- Ask Matt to close #362 #364 #326 and to rule on the six-body Choir.
- Merge queue after #302, the bots integration, #418 and #375.
- Renew the model lever before ~11:26Z.
NOW 02:20Z (supersedes the 01:35Z list below where they differ):
a. #396 green, then MERGED (verified with gh, never from narration). Then the orchestrator fast-forwards the primary tree, runs tools/godot/screens.ps1, and views the frames (D15) before closing the demo-verdict rows KF1, KF2 and KF3. WHY: D15 acceptance is Matt watching frames; a unit test alone does not close a visible row.
b. Merge-forward the conflicting set after #396 MERGED. WHY: each conflict is resolved against the real merged base, keeping both intents.
c. Enable the GitHub merge queue (Matt approved) after the pile lands per plan. WHY: a queue on a red pile only serialises the failures.
d. Design smell, to fix after the pile: the run K1 example input log compares byte-for-byte against a recording whose Enter ticks depend on run-end timing, so every bot or combat change forces a regen. Make the comparison independent of run timing, or compare structure only. This is not a fitted green: the example documents the format, not the tick. WHY: a byte compare on timing turns every balance change into a false regression.

NOW (checkpoint 2026-10-21 01:35Z; gh-bound facts read by the orchestrator at ~01:35Z; this supersedes the 00:35Z block above where they differ, because #396 has moved from draft to armed):
1. #396 "integrate: playable dungeon (one PR)" is OPEN and READY, auto-merge SQUASH armed at 01:15:54Z pinned to head 2e720e2f (a merge of origin/main de0d1c8d into f61ed34f). It waits on required checks. WHY ready: the capture at f61ed34f showed a black void and far labels, and both were classified as pre-existing or environmental, not regressions. The void is the missing game/.godot/imported cache in a fresh worktree (gitignored; GD.Load of .glb returns null). The far labels are PlateRule having no distance term (fixed by D53). Captures must come from the primary tree, never a fresh worktree.
2. #404 "nameplate distance cull" is OPEN, auto-merge SQUASH armed at 01:27:03Z, head 60119575. WHY: the D53 core-loop call; it closes f-77a68e6b after merge and frames.
3. After #396 is MERGED (verify with gh, never from narration): fast-forward the primary tree (it is at 83b5821d with 6 uncommitted entries, which stay untouched until Matt rules on them), re-capture with tools/godot/screens.ps1 from the primary tree, and the orchestrator views the frames (D15) before any visible row closes. WHY: D15 says Matt's acceptance is by frames and video, and a worktree capture cannot show the real game.
4. Close the folded PRs #308, #333, #369, #335, #244 (each head is an ancestor of integrate/playable-dungeon). Verify-then-close #344 and #325 (both OPEN). WHY: their content already reached main through #396, so leaving them open invites a second merge of the same code.
5. Close the D15 rows f-fa28e36e f-210c3939 f-70c82aca f-c89e8074 f-e880cd4c f-07f1b477 f-6b1cd469 f-f404582c f-f90892e9 f-add02b73 f-b78d1567 f-e933a2b7 only after the frames are viewed. WHY: D15 acceptance is Matt watching; a row closed on a unit test alone is the fitted-green pattern.
6. Dispatch the deferred findings: f-ccb1cefb (host-path gear test via SimDriver.cs) and f-4501525f (CombatTextOverlapTest game.yml wiring). Revisit #370 (healer cadence parity) now that #396 is the base. WHY: f-ccb1cefb closes the gear-into-run path on the host, which the P0 closure above only proved on code; f-4501525f is a test that exists but is not wired into CI.
7. NEEDS MATT (unchanged, not decided by the orchestrator): f-37ad66b3 Smart App Control on lane-built DLLs; f-77c8d9f9 merge throughput and branch protection; the verdicts on #326, #375, #370, #250 and lane/rebase-242 (#242 is CLOSED with zero net diff to main, so deleting it is a data-loss call for Matt). WHY: each one changes what ships or what gets deleted.
8. Hook friction, recorded as findings for claude-config lanes (not worked around here): no-lane-game-launch false positives on awk `$0` and `$f` (f-3c816682) and on sed `\&\& (` (f-d6f386a3); git-stash-block fails closed on a nested `powershell -File tools/fastcheck.ps1` run through the PowerShell tool (f-25c70659; the Bash path works). WHY: a false positive blocks necessary work, so each one is an optimize-the-hook item, not a reason to bypass the hook.
PURPOSE: Keyrun is a Windows desktop 3D dungeon game per design/BIBLE.md; the TypeScript browser client was first paint only (status: throwaway); the Godot build is the real thing (status: real).
TS feature work is FROZEN at main 6e1548d (sim/ bots/ client/ server/ screen/). Former items 1-7 (Slack delta, cast-bar
names, bot balance pull 2, Twins/Break bar, floor Windows, rewards/clause UI, Blink/F vs Space) move to the C# build
after parity (ENGINE-PLAN phase 5) and stay listed in Decisions for Matt.
- P0 f-f9b80112 GEAR-INTO-RUN CLOSED 2026-10-21 on code evidence: origin/main d3894362 SimDriver.cs:921 StartFor -> :924 RunRoot.CreateRun(start.Options); HostRunSetup.cs:289-290 via RunStart.Of; RunStart.cs:53-54 GearLoadout.Of; RunStartGearTests 4 cases; same on the #396 head. Follow-up f-ccb1cefb (host-path gear test) dispatches after #396 MERGED.
- Land keyrun #396 "integrate: playable dungeon (one PR)", branch integrate/playable-dungeon, opened as a draft 2026-10-20T23:44Z at d9cb7c2b. It folds #308 #388 #333 #369 #335 #244 and reverts #325 #326 #344 (oracle/parity went red when folded; each stays open on its own branch). Still to fold: feat/no-push-rule 7334a6c8 (renumber its D50, #395 took D50) and the #326 path-pack scripts. WHY: one integrated PR is the only shape Matt can play and judge (D10, D15); folding one PR at a time with a push after each is the receipt-first rule.
  - After #396 is MERGED (verify with gh, never from narration): fast-forward the primary tree, run tools/godot/screens.ps1 capture, orchestrator views frames (D15), then close or retarget #308 #375 #333 #369 #335 #344 #325 #326 #244.
- #370 (healer cadence parity) is DEFERRED until #396 is MERGED. WHY: option 3 made parity green but slowed base clear about 30 percent and needed an assertion narrowing (fitted green, refused); the TS and C# sims disagree on whether a Cadence HoT can land on an enemy (frozen TS allows, C# HEAL-NEVER-HOSTILE refuses). The TS/C# divergence is its own later finding.
- Receipt-first rule now governs every keyrun build lane (aesop LANE-CONTRACT 8d): draft PR within 20 minutes of first commit, push at least every 45 minutes, state self-reverts in the PR body.
Phase 1 lanes (Opus per D11 unless noted; owned files in ENGINE-PLAN s5). Status as of 2026-10-18:
1. 1A scaffold - DONE, PR #109 (59d45d1). game/ Godot 4.7.2 .NET project, Keyrun.Sim lib + purity test, tools/godot
   wrapper + export templates, .gitignore/.gitattributes, .github/workflows/game.yml.
2. 1B oracle - DONE, PR #110 (59a7acb). tests/oracle/** writes fixtures to game/sim/Keyrun.Sim.Tests/Fixtures/oracle/
   (vectors, golden traces, bot seeds 7/11, key 10); game/sim/ORACLE.md.
3. 1C sim core port - DISPATCHED 2026-10-18. Core/**, Combat/Movement.cs, Encounter/Telegraphs.cs; vector parity.
4. 1D content loader - PR #111 OPEN (game/content/Keyrun.Content, EmbeddedResource from content/data). Not yet on main.
5. 1E arena scene - PR #117 OPEN (feat/arena-1e), auto-merge armed. SimDriver 20 Hz, PlayerInput, CameraRig,
   ActorView, TelegraphView, temporary DebugCleaveEmitter; scene test SimDriverSceneTest.ps1 (not wired into CI).
6. 1F docs - THIS PR (docs/phase1-1f): CLAUDE.md domain map, game/CLAUDE.md, README run section, ENGINE-PLAN 3D/3F
   additions, D13, the 1D content finding. Haiku, docs only.
Next after 1F: phase 2 (ENGINE-PLAN section 5: 2A-2E), dispatched after 1E lands on main.
Phase 3 now also carries 3D screen + Clause pick and 3F Sundered Twins + Lock (ENGINE-PLAN s5), added 2026-10-18 by
the intent check: those BIBLE items were in the MVP but no phase planned them.
Phase 1 exit: Matt double-clicks build/keyrun.exe and walks the Warden around a floor with a Cleave cone firing every 8 s.
Every keyrun checkpoint runs the plan-vs-output check (aesop LANE-CONTRACT section 10) against design/BIBLE.md.

Phase status from git evidence (checkpoint 2026-10-18; `git log --oneline -40 origin/main`, origin/main 4572642):
- Phase 1 complete: 1A 59d45d1 (#109), 1B 59a7acb (#110), 1F 9d75ed1 (#112), 1D 727e0ea (#111), 1C 2bab32f (#114), 1E 3f720b0 (#117).
- Phase 2 complete, exit at #131 merge 4572642: 2A #121 c329c02, 2B #124 f5afcf6, 2C #122 a53a68a, 2D #129 2919ad0, 2E #131 4572642.
- Phase 3 rows all MERGED on main (2026-10-18; each PR checked with `gh pr view N --json state,mergeCommit`):
  - 3A bots core #130 9c8a5ca (supporting #132 fa65528, #134 f5361b62).
  - 3B bots roles #133 138595a9; 3C HUD #135 3fc7fc69; 3D screen + Clause pick #138 301d0555; 3E e2e #139 be5ee827;
    3F boss Twins + Lock #137 66d457f3; 3G backfill #140 fd0924a5; 3H bot party #136 e0278237; 3I host wiring #141
    6036fdb0; 3J host backfill #142 c03f9055.
  - PHASE 3 EXIT NOT RECORDED (2026-10-18). Merging proves the code paths exist, not that Matt can play the run. Phase 3
    exit is recorded only after the human-played path is measured, not on CI.
  - 3B must verify 3A's hand-corrected floor keepNear expectation against the frozen TS test (3A hand-back caveat).
  - 2026-10-18 FOURTH-PASS ORACLE VERDICT (read-only Haiku oracle, ~23:30Z, source reads at origin/main a67192a; supersedes
    the third pass below): D10 PARTIAL, PHASE 3 EXIT NOT-MET. Decisive line: the autorun now proves the content path through
    the boss (game/src/Host/HudSceneTest.ps1:353, 356, 360) and the Warden plus Cinder and Chanter party (HostRunSetup.cs:78-82),
    but no human-played run to the boss has been measured. WHY: the plan records Phase 3 exit only from a measured human-played
    run (STATE.md:175), and only the autorun has reached the boss so far. Secondary gap: Twins from pull 1 are unasserted (run C
    arms the boss at ps1:371 but does not assert boss_add_spawn; only run D with --pull 4 asserts it at ps1:385).
  - LANDED 2026-10-18: #158 MERGED a67192a (TUNE-BOSS, Opus lane under D14; `gh pr view 158 --repo matt82198/keyrun --json
    state,mergeCommit` confirms MERGED, mergeCommit a67192a). Content data (content/data/enemies/*.json, TUNING.md): boss
    maxHealth 60000 -> 5000; boss slam every 60 -> 300; twinA/twinB 6000 -> 1000; twins.slam every 60 -> 400; pair.melee 6000 ->
    1200 with meleeSwing at 60 every 150; seed.elite 6000 -> 3000; cleave every 160 unchanged. Sweep keys 2, 5, 8, 10 at seeds 1
    and 7: every run reaches the boss, opens the Lock and clears within par (end ticks 6787, 6887, 7481, 7472, 8441, 7985, 8603,
    8474), zero party deaths; solo Warden still wipes on pull 2 at tick 308. The bot party never breaks the Lock in any run; the
    boss falls through one to three Lock fails. HudSceneTest run C asserts boss reached, lock_open and `Run cleared -- within
    par`; runs I and K moved to `--pull 4`; oracle regenerated (frozenSourceHash 44e0de9a -> 7a65bec7); boss golden 9a99f823 ->
    bd8a7fa5; content-pull golden contentHash 2cea6e84 -> 19436649; 13 C# expectations re-anchored. CI on the merge commit:
    game-test, game-e2e, game-gate, e2e and test passed (runs 39175877212 and 39175877285).
  - Per-row fourth-pass status: 3A MET; 3B PARTIAL unchanged (no k10-s11 trace, minor); 3C MET (was PARTIAL: clear title
    asserted at ps1:353, RunEndPanel.cs:139, run in CI via game.yml:91-99); 3D MET; 3E PARTIAL unchanged (CI drives
    ScriptedWarden, game.yml:192-201; recorded replay proven by HudSceneTest runs K and L); 3F PARTIAL (Lock OPEN asserted live
    at ps1:360-362; Lock BROKEN never asserted on a real run, only synthetic BossRulesTests.cs:115; ps1:388 lock_broken only echoed
    at ps1:810; a party that never breaks the Lock is plan drift against ENGINE-PLAN.md:211 and BIBLE.md:177-179: Matt decision);
    3G MET; 3H MET (HostRunSetup.cs:78-82); 3I MET (SimDriver.cs:342-343); 3J MET (SimDriver.cs:346-347).
  - Known LOW gaps (later lane; this lane does not own game/**): HudSceneTest.ps1:32 comment says "--ticks 3700" but the code
    uses 7000 (ps1:156, 304); FIXED by #163. ps1:54 comment says "key 10 still wipes" but the #158 sweep shows key 10 clearing at both seeds; FIXED by #163;
    game.yml:3-4 comments call game-test and game-e2e non-required (branch protection is the truth; note for Matt).
  - 2026-10-18 THIRD-PASS ORACLE VERDICT (read-only Haiku plan-vs-output oracle, ~21:10Z, source reads at beefb47, before
    #154; supersedes the second pass below): D10 NOT-MET, PHASE 3 EXIT NOT-MET. Decisive line:
    game/src/Host/HudSceneTest.ps1:43-50 (every swept key 2,5,8,10,12,15,20 at seeds 1 and 7 wipes in pull 2 under autorun;
    the boss is reached only with `--pull 4`). WHY: the plan's exit criterion (ENGINE-PLAN.md:219-221) names the boss in a
    full run, and no run reaches it in play. The D10 text requires one complete run through the dungeon including the boss;
    it does not say "survivable", so the oracle reads the requirement as reaching the boss in play. Only Matt's boss-HP,
    lock-threshold or bot-DPS tuning decision can move it.
  - Landed since the second pass (each PR checked with `gh pr view N --repo matt82198/keyrun --json state,mergeCommit`):
    #146 MERGED ecbe0f0 (3D-c wipe-to-screen); #147 MERGED 924898c (3D follow-up); #148 MERGED 9afadc8 (3H-c G-ping route
    to bots; the runner passes ClassKitView.Empty, so no bot reaction fires in autorun; DECISION FOR MATT); #149 MERGED
    f233727; #150 MERGED 8b1913a (recorded input: --record-input and --replay-input, schema keyrun.input-log); #151 MERGED
    9a9100f (3E-c camera yaw recorded, schema keyrun.input-log/2, HudSceneTest run L); #152 MERGED 29a855c (3C-c sim
    content-pool rewards, five-argument Rewards.RollRewardChoices); #153 MERGED beefb47 (3C-d run-end title `Party wiped on
    pull N of T -- key depleted` via ObjectiveModel.Build; RunEndView.Of rolls from the content pool; `HUD runend choice`
    lines carry id=<facet id>); #154 MERGED 606e623 (CI: game.yml game-e2e runs HudSceneTest, CombatViewSceneTest and
    ScreenSceneTest; all PASS on the runner). #155 MERGED f31311d (PLAN-DOCS: ENGINE-PLAN rows 3H 3I 3J,
    README status and record command; verified with `gh pr view 155 --json state,mergeCommit`). #156 MERGED 28415a6 (3E-a2: E2eAssert.ps1 replay pass driven by example-yaw-walk.input-log.json,
    committed digest 94eb4f3d216477f9 over TICK 0-239; the CI proof of the human-input path for row 3E).
  - Per-row third-pass status: 3A MET; 3B PARTIAL (RolesOracleTraceTests.cs:148 k2-s11 and :151 k10-s7 traces exist; no
    k10-s11 trace, minor); 3C PARTIAL (RunEndPanel.cs:127 rolls from the pool, :138 wipe title; HudSceneTest asserts both;
    no autorun clears, so the clear title has no live proof; the CI gap the oracle saw is closed by #154 after the read);
    3D MET; 3E PARTIAL (ENGINE-PLAN row 3E says a recorded file drives the Warden in CI; CI runs ScriptedWarden at
    E2eAssert.ps1:50-57; recorded replay proven in HudSceneTest runs K and L and, since #156 MERGED 28415a6, by the
    game-e2e replay pass in E2eAssert.ps1 (merged after the oracle's read at beefb47, so the 3E status above is unchanged); 3F PARTIAL (no test asserts the Lock opens on a real run; BossRulesTests.cs:86 and :99 are synthetic;
    HudSceneTest.ps1:376 $bossRule is only echoed at :798; Boss.cs:26 LockThresholdBp=5000 compared at Boss.cs:180; base HP
    60000 (content hollow-warden.json:5) is Tempo-scaled by Tempo.cs:60-64 to about 76200 at key 10); 3G MET with minor drift
    (abandon at pull 1 tick 1000, HudSceneTest.ps1:70; key-3 joiner refused); 3H MET (HostRunSetup.cs:80-82, SimDriver.cs:491);
    3I MET (SimDriver.cs:342, unreached in play); 3J MET (SimDriver.cs:347).
  - Plan-vs-output seam: ENGINE-PLAN.md:189-221 defined rows 3A-3G only until #155; rows 3H-3J existed only in STATE.md and
    BUILDLOG.md. The Lock measurement in the second-pass block still stands (boss base 60000, about 76200 at key 10).
  - Record command for Matt (from game/CLAUDE.md; flags parsed in SimDriver.cs). Build:
    `powershell -NoProfile -ExecutionPolicy Bypass -File tools/godot/godot.ps1 --headless --path game --build-solutions --quit`.
    Record: `powershell -NoProfile -ExecutionPolicy Bypass -File tools/godot/godot.ps1 --path game '--' --record-input
    game/fixtures/input/my-run.input-log.json --log build/my-run.txt`. Replay with `--replay-input` on the same path. Camera
    yaw is recorded, pitch is not.
  - Launch lock (keyrun PR #301, merged as 69a0e1e2): every game launch now goes through the single-instance launch lock.
    A second launch exits with code 4. Each run is capped at 300 seconds (override with KEYRUN_GODOT_TIMEOUT_SEC), and the
    process tree is killed when the cap is hit.
  - 2026-10-18 SECOND-PASS ORACLE VERDICT (SUPERSEDED by the third pass above; read-only Haiku oracle, ~19:08Z, source reads at 4b35c98): D10
    MET-WITH-TUNING-GAP. Supersedes the first-pass NOT-MET in DECISIONS AND OBSERVATIONS (d). Code path exists for every
    Phase 3 row and the screen-to-run path is wired; the gap is balance, not wiring. Decisive line:
    game/src/Host/HudSceneTest.ps1:43-45 (every swept key wipes in pull 2 under the scripted autorun Warden). No
    human-played run is measured anywhere. If D10 requires a survivable run to the boss, the verdict is NOT-MET.
    WHY: the first pass's wiring drift (3C, 3D-b, 3I) is closed on main; what remains open is balance, which only Matt's
    play measures. PHASE 3 EXIT stays NOT recorded; Phase 4 stays blocked on D10.
  - Per-row second-pass status: 3A, 3B, 3C, 3G, 3I, 3J BUILT-AS-INTENDED. 3D BUILT-BUT-DRIFTED (Enter after a wipe with
    no reward take restarts via StartRun(null) at --key with no Clause, SimDriver.cs:441-445, bypassing the Screen; fix
    lane feat/wipe-to-screen RUNNING per the orchestrator brief, not verified against a PR here). 3E BUILT-BUT-DRIFTED
    (walk-kick.json does not drive the Warden; 3E-a recorded-input reader still OPEN). 3F BUILT-BUT-DRIFTED (Lock logic
    exists, Boss.cs:26, 228-240, but HudSceneTest.ps1:292 captures and only echoes at :414, never asserts; blocked on the
    tuning decision). 3H BUILT-BUT-DRIFTED (human G-pings not routed to bots: Pinged raised at PlayerInput.cs:148 with no
    subscriber in game/src/Host; pull 2 wipe still open).
  - Drift CLOSED since the first pass: 3C banner (AbilityBar.cs) via #144; 3I DrawQueue caption (Screen.cs:40) via #144;
    3D-b screen first (SimDriver.cs:391) via #145.
  - "Matt sees" clauses (second pass): launch screen-first PROVEN-IN-CODE; picked key and Clause PROVEN-IN-CODE; Warden
    plus Cinder and Chanter bots PROVEN-IN-CODE (HostRunSetup.cs:78-83); through three pulls NOT-REACHABLE-IN-PLAY (pull 3
    only via --pull N); Twins PROVEN-ONLY-UNDER-TEST-FLAGS (--pull 4 --key 10 --seed 7); Lock NOT-REACHABLE-IN-PLAY
    (tuning); run-end panel PROVEN-IN-CODE.
  - Lock measurement (lock-assert lane, no PR, no file changes, stopped per brief): 190 autorun runs, keys 2-20 x seeds
    1-10, 1200 ticks, --pull 4: lock_open 0, lock_broken 0, lock_failed 0, boss_enrage 0 everywhere; run_end wipe in 155
    of 190. Cause: Boss.cs opens the Lock at LockThresholdBp 5000 (50 percent) of a 60000 HP boss
    (content/data/enemies/hollow-warden.json); the party dealt about 470 damage before wiping. The 3B claim that bots
    break 1 of 4 Locks at k10 is NOT reproduced. DECISION FOR MATT: boss HP, lock threshold, or bot DPS tuning before any
    Lock assertion is written (no fitted green).
- NEXT ORDER (2026-10-18, fourth pass; resume here, in this order). STATE-R8 (2026-10-19, after #174): lanes 1, 2, 3, 4, 8 and 9
  DONE; lane 10 PARTIAL (CAMERA-FEEL #172 and HOST-HEADING #174 DONE; PARTY-FRAMING and PLATE-HEIGHT running, PR pending);
  lanes 5, 6, 7 not started; GUARD-HARDEN is #169 MERGED. Next, in order: lane 10 remaining slices (the PARTY-FRAMING and
  PLATE-HEIGHT PRs) land next; then lanes 5 to 7 room theming (Opus); then the human-played run to the boss, which stays the
  only mover of D10 and the Phase 3 exit. Lanes 3, 4, 8 and 10 build on lane 2's kit, walls and DungeonBuilder, and on lane 9's
  proof. Lane 8 HUD-POLISH-2 landed as #170 (its Pings.cs and CastBarView.cs residue is closed).
  WHY the themed run is listed first: Matt judges feel from frames (D15, D16), so the NAV-and-art run is the one to measure.
  WHY reordered (fourth pass): D15 (frames before feel) and #158 (boss
  reachable in autorun) change what a human run must measure. Steps 1-3 of the second pass are kept below as the record.
  0. SCREENS proof lane (running 2026-10-18; output C:/Users/Mattt/Desktop/keyrun-screens/) makes the first frames of run C.
     Follow-up SCREENS-TOOL lane promotes the capture script to tools/godot/screens.ps1, adds the ffmpeg frames-to-mp4 step,
     and documents both in game/CLAUDE.md. WHY: D15, a visible change is judged from frames, not a headless log.
     Frame and video captures are taken by the orchestrator, not by lanes. Lanes never launch Godot, keyrun.exe,
     *SceneTest.ps1, screens.ps1, or E2eAssert replays. WHY: parallel game launches froze the host on 2026-10-20.
  1. Matt records a human-played run to the boss (record command above) and judges it by feel, with the D15 frames. Only this
     moves D10 and the Phase 3 exit.
  1a. DONE 2026-10-19 00:54Z: ART-PLAN ladder lane 1 NAV-CORE, PR #163 MERGED 76c7f32 (block "Phase 3.5" below). Lanes
     dispatched about 01:05Z and both have landed: lane 2 DUNGEON-SHELL (PR #166, 49aeadb) and lane 9 LOCK-PROOF
     (PR #165, 1e09538). Then ladder lanes 3 (player
     and bots), 4 (enemies), 5 to 7 (room theming), 8 (HUD polish; name plates overlap confirmed in nav-core frames) and
     10 (feel; camera yaw). Ladder: design/ART-PLAN.md section 8; D16, D17, D18. WHY: Matt judges
     feel from frames (D15), and the run he judges must be the themed dungeon with navigation, not capsules in one box.
  2. RESOLVED by D17 (2026-10-18 23:43Z): bots are not required to break the Lock; beating the boss through Lock fails is an
     acceptable autorun outcome; the Lock must be theoretically winnable (ENGINE-PLAN.md:211 vs BIBLE.md:177-179 closed).
  2a. DONE 2026-10-19 01:27Z: Lock winnability proof, PR #165 MERGED 1e09538 (LockWinnabilityTests; design/ART-PLAN.md lane 9).
  3. DONE via #163 (2026-10-19 00:54Z): run C asserts both boss_add_spawn events and the ps1:32 and ps1:54 comments are fixed.
     Still no lock_broken assertion in run C (it would fail today).
  4. Target-ping bot reaction (runner passes ClassKitView.Empty) and cinderfield self-hit intended target (BIBLE s5) stay Matt decisions.
  5. Only then re-run the plan-vs-output check and record PHASE 3 EXIT and D10. Phase 4 (TS deletion) stays blocked on step 5.
  DONE 2026-10-18 since the second pass: #158 a67192a (TUNE-BOSS, D14). The second-pass steps below are kept for the record.
  Done since the first pass, each verified with `gh pr view N --repo matt82198/keyrun --json state,mergeCommit`:
  #144 MERGED ed1f2ec7 (HUD follow-up: AbilityBar banner removed; Screen.QueueCaption "Enter starts the next run at this
  key and Clause."); #145 MERGED 4b35c983 (3D-b: SimDriver.StartsOnScreen, Screen.OpenFirst; HudSceneTest run H asserts
  `SCREEN first open=1 key=2` and no RUN start on a no-flag launch).
  1. DONE 2026-10-18: #146 ecbe0f0 (3D-c). Enter after a wipe must go through the Screen
     (SimDriver.cs:441-445). WHY first: it shares SimDriver.cs with step 3.
  2. Matt decides Lock/boss tuning (boss HP vs lock threshold vs bot DPS) and general run difficulty. Bring the numbers
     from the Lock measurement above, not a tuned constant (DECISION FOR MATT, below).
  3. DONE 2026-10-18: #148 9afadc8 routes G-pings to bots (3H-c); the runner still passes ClassKitView.Empty (DECISION FOR MATT).
  4. Matt plays a run (Phase 5 measurement).
  5. Only then re-run the s10 plan-vs-output check and record PHASE 3 EXIT and D10 (MET / MET-WITH-TUNING-GAP / NOT-MET).
  6. Phase 4: TS deletion after parity. Blocked on step 5.
  D31. Skill toolkit (NOT implemented; depends on D30, PR #306; the per-class table and the BIBLE wording wait for Matt in
     the D31 PR body; research/12-skill-toolkit.md is the reason). First increment, data before sim:
     - Ops: three generic threat ops any ability may carry (take to self, give to a target ally, drop to the next
       holder), the "explicit take-threat ability op" of D30 rule 3. Sim: game/sim/Keyrun.Sim/Combat/Threat.cs,
       Combat/Effects.cs and Combat/Abilities.cs, read by Encounter/ScriptTarget.cs (file names from the D30 list, not
       re-verified on main); the loader and validator in game/content/ must accept the new op names.
     - Content: one toolkit Facet per class in content/data/facets/warden.json, cinder.json and chanter.json (the
       Warden Taunt hand-off, the Cinder Cold Snap lure and Ashen Skin veil, the Chanter Ward-verse Dissonance), the
       E/R/F rows they patch in content/data/abilities/*.json, and any fork rows in content/data/classes/*.json.
     - Parity: the BIBLE section 6 fork check needs a saves measure (runs survived after a tank death) before a
       toolkit option is compared with a damage option; that lives in the bots suite (game/bots/).
  D32. Gear depth (NOT implemented; rules 1 and 3 depend on D31, PR #311, and D30, PR #306; rule 2 on PR #304; the
     BIBLE wording waits for Matt in the D32 PR body; research/13-gear-depth.md is the reason). Increments, smallest first:
     1. All five slots exist end to end (sim, content, loader, view readout). Today the sim (`Rewards.GearSlots`,
        game/sim/Keyrun.Sim/Encounter/Rewards.cs), content (content/data/facets/*.json, 4 Facets per slot per class),
        loader (`GearFacets.Resolve`, game/content/Keyrun.Content/Model/GearFacets.cs) and the screen readout
        (game/src/Hud/Screen.cs) all name five, but the character starts empty and fills one slot per run, two Facets
        (warden.weapon.twinStrike, cinder.keystone.infernoBolt) are skipped because a line width is not in the sim
        shape, and only the human seat's gear reaches the run. Done means: a new character's first screen shows five
        slots with a starter Facet each, every shipped Facet applies in a run, and the run-end card and the screen agree.
     2. Strict-upgrade guarantee on the choice of three (BIBLE s7, Matt 2026-10-20 "every clear moves something
        visible"): `Rewards.RollRewardChoices` takes the equipped set and guarantees one card is a higher Tier in a slot
        or a Facet not owned; missing on main.
     3. The armory (D32 rule 4): `Progression` keeps every owned Facet with its best Tier; ProgressionSave version 2
        reads version 1. Screen: equip from the armory per slot before Queue.
     4. Remembered loadout per affix: one key press on the screen equips the loadout last used under this key's
        affix and Clause. Needs PR #304's affix readout on the screen.
     5. Facet audit against rule 1: list each of the 60 Facets with the verb or tag it changes; rewrite the ones that
        only add a number (the ring "banks bonus Heat/Momentum/Verse" rows are the candidates). Content only.
     6. Toolkit and pair Facets: one D31 toolkit Facet and one pair Facet per class (content plus the D30 threat ops).
     7. Parity per affix and Clause: the BIBLE s6 bots parity check over Facet loadouts, keys 2 and 10, Fortified and
        Tyrannical separately (game/bots/).
     8. Ascension marks (rule 5): `Ascension` records timed clears above key 10 per affix and Clause; today
        game/sim/Keyrun.Sim/Run/Ascension.cs says marks are "not exposed" because Progression records no key levels.
     9. Tier as power: Tier changes no combat number on main (no read of `Item.Tier` outside rewards, progression and
        the HUD). Matt decides what a Tier does (for example a small integer scale on the patched ability's
        numbers, or no combat effect at all) before this lands.

## Decisions for Matt
Verbatim from the recon file's "Decisions for Matt / sim lanes (not client work)" list:
- Sundered Twins + Break bar not spawned (content-dungeon.ts:20-23; remote feat/break-bar-wiring exists).
- Lock at 50% + enrage after 3 failed Locks absent.
- Stack damage split absent.
- Cinder Blink has no displacement (cinder.json:266).
- F=Charge vs Space=dash authority.
- Floor Windows + combo events need snapshot channels.
- Death log/mercy no module.
- Rewards run-end.ts:29-33 stub.
- Clause pick UI + key level display absent.
- Keybinds 11 vs 10.
- Cinder fills all 3 damage slots.

Additional parked decisions (from the burn ledger):
- Combo effects and numbers (Lock gate, Window payoffs) - the only BIBLE [DECIDE] blocking 3D.
- Repo setting "Require branches to be up to date before merging" (strict status checks) or a merge queue, so a PR
  cannot merge against a main it never tested with (#94 -> main red). Recommended.
- Enable repo auto-merge? (currently OFF; PRs land by direct squash.)
- Make the 'e2e' job a required check? (#90 added it; not required.)
- Hold-to-abandon for Backspace (defect l).
- Licence MIT (#65) - landed; object if not wanted.
- Play-feel: tank lowest HP in pull 1 at skill 100 is 22 (thin margin); balance after the fixes is Matt's call.
- Lock test and NAV layout (2026-10-19): LockWinnabilityTests runs the flat-arena boss pull, because the Sim test csproj was
  off-limits for lane 9. Wire the NAV layout into it, or leave it flat?
- Lane 2 wall height (1.6 m) and light count (12, plan said 8) are reversible; say if you want other values.
- Git LFS if game/assets exceeds 50 MB (currently 1.43 MB from the KayKit dungeon kit).

## Standing rules
- Every audit and integration test reproduces through the public path (createRun/stepRun, the bot loop, the server
  room over ws, the client worker host). A mechanic not wired into the composition root in the same PR is a P0.
- Net/local parity: a telegraph, kick flag, or event that exists on the local path must exist on the net path in
  the same PR. Client events are read only via client/CLAUDE.md SnapshotEventLatch, never nextSnapshot.events.
- Model: Opus for Keyrun development lanes (D11); Haiku for read-only recon, lint and bookkeeping.

## NEXT STEPS (2026-10-21 02:35Z)
1. #396 lane implements D56 (joiner at pull start) and D57 (healer top-off between pulls). Then the orchestrator runs HudSceneTest once on keyrun-wt/integrate-playable for the K1 regeneration.
2. After #396 is MERGED: fast-forward the primary tree; the orchestrator views the screens.ps1 frames (D15); close the folded PRs #308 #333 #369 #335 #244 #388; verify-then-close #344 and #325 (D58); merge forward the ten conflicting PRs #307 #302 #370 #375 #364 #362 #324 #319 #356 #293; re-evaluate #327 and #295 (D59).
3. Enable the merge queue only after the pile lands. Matt approved this on 2026-10-21, on the condition that it waits for the pile.
4. Fix the run K1 example log's byte-for-byte compare against run-end timing so balance changes stop forcing regens (not a fitted green: the example documents the format, not the tick). Carried from main's 02:20Z NEXT (d), which this block did not cover.

