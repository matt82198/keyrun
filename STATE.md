# STATE.md - Keyrun

## INTENT (pinned; the orchestrator reads this first after every compaction)
Why pinned: Matt 2026-10-19, verbatim: "there seems to be a misunderstaning with your responsiblity now, the workers context are small scope, your context is intent... over and over we are losing intent for context... even though its enforced with code" and "i think you've built a good start, it just does not align with the bible lmfao". Every lane is judged against design/BIBLE.md below, not against its own PR text.
- Core dungeon loop rule (Matt 2026-10-20, verbatim): "anything you want my decision on in the core dungeon loop, defer to wow". The orchestrator answers dungeon-loop design questions with what WoW Mythic+ does and records the call.
- Out-of-dungeon rule (Matt 2026-10-20, verbatim): "anything else like progression, builds, classes, lobby stuff more like dark and darker... character management screen, quests, progression, score, follow the bible but you get what im saying, just one screen with tabs should be enough before entering a game/queue". The orchestrator decides lobby, progression, build, class and character-management questions the way Dark and Darker does them, within design/BIBLE.md. Where the bible excludes something (it lists no quests, shop or stash in the POC, and no shareable score), the bible wins.
- Goal, design/BIBLE.md section 1, verbatim: "Screen -> Queue -> Dungeon (timed, one boss) -> End-of-run choice of three -> Screen." "Nothing exists outside this loop in the POC: no world, no quests, no shop, no stash."
- The three tests, section 0, verbatim headings, each with its first sentence. Every lane's deliverable is judged against these three, not against its own PR description.
  - "**Depth and difficulty.** Classes with real decisions on five buttons; keys that are genuinely hard because of what enemies *do*, not how much health they have."
  - "**A high key inside a minute.** Open the screen, pick a key, press Queue, be in the dungeon."
  - "**Strangers are chill by default.** The systems remove the reasons people are not: no shareable score to judge by, no run ruined by one mistake, backfill instead of abandonment, pings instead of typing."
- Combat contract, section 3, verbatim: "**Cast bars are the enemy's language.** Every dangerous enemy action is a cast with a visible bar and a floor telegraph (circle, cone, line) drawn ahead of resolution. Shape plus colour, never colour alone (research/06)." Also: "Interrupts resolve by intended tick with a fixed tiebreak, never arrival order"; "Damage is a pipeline of integers".
- The design's centre, section 4, verbatim: "This is the design's centre and the answer to "more creative than Blizzard": synergy is not a buff you press, it is a *timing game between two people*."
- POC dungeon, section 9, verbatim: "Three pulls and a boss, forward-only, no backtracking (research/02). Each pull teaches one thing." Pulls: "**The Choir**" teaches the kick ledger (must-kick 4-second heal cast); "**The Pair**" teaches reading shape (spread ring, then stack circle); "**The Seed**" teaches the boss's core rule with no stakes. Boss "the Hollow Warden": Cleave, The Sundered Twins, The Lock.
- Gear, section 7, verbatim: "**Five slots**: Weapon, Armor, Charm, Ring, Keystone." "Tier is power and it is capped at 10." "**Facets change function**" "**End of every run: choose one of three**" "Ash buys only cosmetics and titles." "**After the cap: Ascension.** ... and Ascension marks on the character."
- Current milestone, STATE.md D10 (verbatim): "**MVP = 3D gameplay**" ... "the MVP deliverable is one complete 3D run Matt can play end to end and judge by feel, not a test count." Phase 3 exit waits on Matt's human-played run. MVP LINE (D10 question, RESOLVED 2026-10-20 under Matt's out-of-dungeon rule, see INTENT): the MVP is the full section 1 loop: one screen with tabs, then Queue, then the Dungeon, then the end-of-run choice of three, then back to the screen.
- Acceptance is Matt watching (D15): frames and video viewed before a visible row closes. Demo verdict 2026-10-19 (default run C at 75da3ef): KF1 combat animation reads badly; KF2 bots idle in the first room (the Choir is meant to teach the kick); KF3 the Seed elite spawns on entry and the bots cheese it. Known section 3 gaps: boss slam and Twins slam untelegraphed.
- Art and gear models: CC0 packs only (D17). How gear shows on the CC0 models was an open design question, RESOLVED 2026-10-20 under Matt's out-of-dungeon rule (see INTENT): as in Dark and Darker, equipped gear is visible on the character model, within the CC0 assets (D17). A BIBLE-ALIGN recon on 2026-10-19 is mapping the bible against main.

**Intent (Matt, 2026-09-18):** "isolating m+ from WoW and just making it an open and queue game like Dark and Darker,
and let people manage their characters from a simple screen... study other mmos, dungeon games, gear progression, and
find specifically what people find exciting/fun, build talent trees, classes, the dungeon POC with initial enemies,
abilities, a boss. Mechanics like interrupts, but be more creative than blizzard devs... 4-5 rotational abilities per
class max, 10 total keybinds should be plenty for rotation, movement, utility... the goal is class synergy." No art
assets; pure POC.

## Current state (checkpoint 2026-10-18)
- Main tip: 36feb7e (#174 squash, HOST-HEADING). Prior main tips: cfa40b9 (#173), 8ffe1e3 (#172), f72c738 (#171), 581212d (#170), 9df96ef (#169), 6ae14a1 (#168 docs), 30f5708 (#167), 10de063 (#106), 56c5686 (#105), b54eb40 (#104 docs), 2c8f8ea (#102), 54e69c5 (#99), 00e4ebb (#98).
- Merged today since the 23:30Z checkpoint: #101 99836b0 (window ring labels + payoff-ready key glow + ping context + aimed spot ping); #102 2c8f8ea (bots/src/roles/shared.ts inLull, tank-pressure.ts).
- MERGED 99f39a2: #103 feat/status-chips-downed-net-casts (status chips STUNNED/TAUNTED/BRACED from snapshot effects, DOWNED tag, dash trail, telegraph base opacity 0.15->0.4, net self-target fix via server/src/human-casts.ts, server cast/cast_failed/gcd events, joined carries display info).
- Phase: 3 rows 3A-3J all MERGED on main (2026-10-18, verified per PR, see NEXT STEPS). PHASE 3 EXIT NOT RECORDED. D10
  verdict (2026-10-18): FOURTH pass PARTIAL, PHASE 3 EXIT NOT-MET (Phase 3 block, FOURTH-PASS ORACLE VERDICT). It supersedes
  the third pass NOT-MET, the second pass MET-WITH-TUNING-GAP and the first pass NOT-MET (DECISIONS AND OBSERVATIONS (d)).
  Why: #158 (D14) lets an autorun reach and clear the boss, but no human-played run to the boss is measured, and only that
  closes Phase 3 exit. Phase 5 "Matt playing" is the acceptance test; not yet reached. Phase 4 (TS deletion) stays blocked on D10.
- Earlier landed (unchanged): #92 e3f622e, #95 e56955c, #97 08b9611, #98 00e4ebb, #99 54e69c5.
- Test count on 2c8f8ea: not restated in this checkpoint (not sourced for this head).
- NEXT (supersedes the status in NEXT STEPS item 1; rewritten 2026-10-18 from BUILDLOG.md and git log -15):
  Merged since the 23:55Z checkpoint: #104 b54eb40 (checkpoint docs); #105 56c5686 (SPREAD/STACK telegraph floor
  labels with countdown, render/telegraph-label.ts); #106 10de063 (sim-host scaleStep keeps `every`, so recurring
  STACK calls fire during The Pair; server/src/content-setup.ts ported identically). BUILDLOG.md has no line for
  #104-#106; these come from git log and the commit bodies.
  Earlier 2026-10-17 merges (BUILDLOG-recorded): #98 00e4ebb (named ability bar, cast projections, actor tags,
  objective line, GCD dim, hit flash); #101 99836b0 (window ring labels, payoff key glow, ping context, aimed spot
  ping); #102 2c8f8ea (tank banks GCD in a lull); #103 99f39a2 (status chips, DOWNED tag, dash trail, telegraph
  contrast 0.15->0.4, ?net=1 cast parity).
  BIBLE-vs-client gap ladder, remaining:
  - Slack delta (needs hud-wiring.ts prevSnapshot; deferred in #103 per decision 6 below).
  - Cast-bar ability names (ladder rung 7): in the #101 lane scope but not in its merged title; confirm on main before
    ticking it. Proof: boss bar reads "Cleave".
  - Class silhouettes and legend overlay: no BUILDLOG line either way; treat as not started.
  - Open from the ladder: boss Slam cast bar unnamed; Lock ring with no shield; boss-bar.ts has no phase field;
    window ring colour is time-only; setupClass never rendered; capsules have no names on the non-#98 path.
  - Balance: 3 of 4 bots die in pull 2 at key 2; with hz=60 the party is mostly DOWNED (see OBSERVATIONS). Tuning is
    Matt's call; bring the numbers, not a tuned constant.
  - Then NEXT STEPS below in Matt's priority.

## Locked decisions
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

## Phase plan
- Phase 1 (research day): 9 cited briefs (incl. Fellowship) -> design/BIBLE.md v1 (Matt reviews the taste calls).
- Phase 2: sim/ rules engine (deterministic, tested) + content v1 (3 classes, 1 dungeon, 1 boss).
- Phase 3: bots/ + client/ (Three.js) -> playable in a browser tab, one human + bots.
- Phase 4: server/ authoritative tick + queue/lobby + character screen -> 5 humans.
- Phase 5: tuning with Matt playing.

## Matt's verdicts on the 3D client (keep these in view)
- Pre-#98: "no abilities, ability projection, everything is a dumby" and "kovaaks with a minecraft bar".

## 2026-10-17 burn (D10) - ledger summary
Bound from GitHub (gh pr view / gh repo view). Full per-PR detail lives in BUILDLOG.md.
- Merged chain: #54 run-options P0; #60 enemy melee reaches party; #61 telegraph kinds (spread/stack/boss/avoid
  classes); #62 keybind slot map (single authority client/src/input/slot-abilities.ts); #63 kickable cast flag and
  cast-bar flash; #64 README run/net; #65 MIT LICENSE; #68 L8 net/wiring parity (92b03b8); #87 bots brace/hymn;
  #88 dev serve URL; #89 run-end overlay fixed + e2e (4/4); #90 CI e2e job (d7adf00); #91 sim mitigation wired
  (c454196); #93 tank pressure (6170be3); #94 dead actors inert (e8b42da, caused main red); #96 tank Bulwark cast
  fix (5b63ca2, main green again); #92 HUD pull readout (e3f622e); #95 bots docs (e56955c); #97 checkpoint
  re-land (08b9611); #98 first-run readability (00e4ebb).
- Post-mortem (#94 -> main red): #94 was merged on a head that had not been re-tested against #93. Each PR
  was green alone; together they were red. Repo auto-merge is OFF; PRs land by direct squash, proven with
  pr_merged_check.py. See Decisions for Matt (strict up-to-date check).
- Full-run status at 6170be3: bot party clears hollow-vault pulls 0/1/2 and the boss (398 s, no deaths, seeds
  7 and 11). Balance margin is thin (tank low 65/400 at t1300). Balance is Matt's play call.
- Net human death (2026-10-17 smoke): REFUTED as network-specific; human warden dies at tick 140-142 under the
  scripted smoke input on net AND local. Inference: driver choreography / sim. Re-check after mitigation fixes.

## Verified/filed defects (refinesystem debt)
Filed 2026-10-17 from the #60-#68 review follow-ups; each carries the evidence the reviewer gave.
- (a) Telegraphs that start and end entirely inside skipped snapshots are never drawn (state-latest-wins). Design
  choice; revisit if telegraphs get short.
- (b) Stack call has no mechanical effect: no stack-in logic; BIBLE damage split unimplemented.
- (c) Golden never lands a melee hit.
- (d) Chase speed == player speed coupling.
- (e) CHASE_MARGIN_MM hardcoded.
- (f) Server toBotTelegraph danger always true.
- (g) Server bots use EMPTY_CLASS_KIT.
- (h) Default server run never clears pull 0 without a human.
- (i) Telegraph id counter shared across rooms.
- (j) keybinds.json has 11 binds vs BIBLE 10 [DECIDE Matt]. Client also hardcodes Space=dash, Escape=stopcast,
  Backspace=abandon (14 keybinds in total per the smoke lane).
- (k) Root CLAUDE.md said "Private repo"; repo is PUBLIC since 2026-10-17. Fixed in the docs PR (one line).
- (l) Single Backspace abandons the run with no confirm (host-commands.ts:21,35, per the smoke lane). [DECIDE Matt:
  hold-to-abandon recommended.]
- (m) sim/combat/payoff.ts:117 applies field combos to the payoff actor, so cinderfield-field's 80 fire damage hits
  the actor paying it off (pinned by tests/sim/combat/windows.test.ts:210). Needs a content/ design decision.
- (n) Sim applies heals/damage to dead actors - FIXED by #94 for damage/heal and targeting; re-verify on net path.
- (o) Net path lacks telegraphClass/kickable/events in some legs - FIXED by #68; keep the net/local parity check.

## Bible-vs-client gap ladder (recon 2026-10-17, static, HEAD 5b63ca2; #98 has since landed rungs 1, 2, 8, 11)
Source: scratchpad recon file keyrun-bible-client-gap.md (C:/Users/Mattt/AppData/Local/Temp/claude/
C--Users-Mattt-aesop/b9f018e1-be8a-4405-bb7f-70832185439c/scratchpad/keyrun-bible-client-gap.md).
Remaining rungs (post-#98), cheapest and most visible first:
3. Window readability: setupClass text on the ring + payoff-ready key highlight (window-ring.ts, hud-wiring.ts).
4. Ping context one-liner (hud-wiring.ts pass cast.kickable / window). Proof: ping mid-cast Cantor shows "Kick!".
5. Status chips from effects (snapshot-codec.ts, snapshot.ts:65-83, net/mapping.ts, actors.ts). Snapshot has no
   effects/threat field; stun/chill/braced/burning invisible. Touches server/src/snapshot.ts (cross-lane).
6. Spot ping at aimed floor point (hud-wiring.ts camera raycast).
7. Cast-bar ability name (actors.ts, castBar.ts). Proof: boss bar reads "Cleave".
12. Downed label + Slack delta (actors.ts:98, hud-wiring prevSnapshot).
13. Dash trail / Charge line (castProjection.ts).
14. Telegraph contrast (telegraphs.ts:143 base opacity 0.15 is faint on the 0x1b2027 floor).
- Also open: boss Slam cast bar unnamed; Lock ring with no shield; boss-bar.ts has no phase field; window ring
  colour is time-only; setupClass never rendered; capsules have no names on the non-#98 path; SPREAD/STACK
  telegraph text labels (no text yet); 3 of 4 bots die in pull 2 (balance).

## NEXT STEPS (engine plan, design/ENGINE-PLAN.md; Phase 5 "Matt playing" stays the acceptance test)
PURPOSE: Keyrun is a Windows desktop 3D dungeon game per design/BIBLE.md; the TypeScript browser client was first paint only (status: throwaway); the Godot build is the real thing (status: real).
TS feature work is FROZEN at main 6e1548d (sim/ bots/ client/ server/ screen/). Former items 1-7 (Slack delta, cast-bar
names, bot balance pull 2, Twins/Break bar, floor Windows, rewards/clause UI, Blink/F vs Space) move to the C# build
after parity (ENGINE-PLAN phase 5) and stay listed in Decisions for Matt.
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

## STATE-R9 checkpoint (2026-10-19 05:05Z)
WHY: #175 to #181 finished the Phase 3.5 ladder and added decisions D25 and D26; the next session must resume from disk, not
from chat, so this block records the ladder, the decisions and the open items for Matt in one place. It supersedes the lane
status in NEXT ORDER (the STATE-R8 text there stays as the record) and the 2.35 m plate note.
- Main tip 39ec436 (#181 LOCK-VIEW, squash 39ec4367eec72e4bf1f66ce6377ad8f7a853cd98). Merged since STATE-R8 (#175 2dd78e05):
  #176 49c127c5 PLATE-HEIGHT; #177 7318aa64 PARTY-FRAMING; #178 c2676604 ROOM-THEMING-1; #179 24a8d0ef ROOM-THEMING-2;
  #180 8b329434 ROOM-THEMING-3; #181 39ec4367 LOCK-VIEW. Each verified with `git log origin/main --oneline -12`.
- Ladder (design/ART-PLAN.md section 8): every lane is DONE. Lane 5 = #178; lane 6 = #179; lane 7 = #180 and #181; lane 10 =
  #172, #174, #176 and #177. Lanes 1 to 4, 8 and 9 are DONE per the Phase 3.5 block above.
- The only open Phase 3.5 item is the human-played run to the boss (Matt). It is also the sole mover of D10 and the Phase 3
  exit. D10 verdict stays PARTIAL; PHASE 3 EXIT stays NOT-MET (no change in this checkpoint).
- Built line on main (game/CLAUDE.md ROOM-THEMING-3 row): `props=65 decor=50 lights=13 themed=entrance,choir-room,pair-hall,
  seed-chamber,warden-arena`; max 343 draw calls at 1280x720 (budget 600).
- Test and gate figures (hand-back figures from the lane reports, not re-run by this docs lane): Content 210, Sim 559 plus 1
  skipped, Bots 139; `npx vitest run tests/oracle` 9 with manifest hash 7a65bec7 unchanged; E2eAssert digest b950474eff2d9967
  unchanged.
- Frames (D15): C:/Users/Mattt/Desktop/keyrun-screens/room-theming-3 (67 PNGs plus run.mp4) and
  C:/Users/Mattt/Desktop/keyrun-screens/lock-view (runc, lockrt and lock frames plus three mp4). The orchestrator D15 pass
  accepted run-058, run-059 and run-060 (arena) and runc-247, lockrt-018, runc-249 and lockrt-020 (Lock phases), per the lane
  hand-back.
- Decisions D25 (room dressing from theme.json) and D26 (Lock view) are recorded in the DECISIONS list below, after D24.
- Content and sim finding for Matt (not fixed; a fix would move the oracle hash): the boss slam (content/data/enemies/
  hollow-warden.json, enemies.json) and the Twins slam are untelegraphed while the cleave is telegraphed.
- Open items for Matt (listed again in the open-items list below): the human-played run to the boss; the slam telegraph; the
  arena light cap of 3 (raise only if Matt says so); Git LFS if assets exceed 50 MB; deleting the merged keyrun branches and
  worktrees (after pr_merged_check.py proves each merge); the ENEMIES merge commit 6fe70af and the first PARTY-FRAMING merge
  commit lack Co-Authored-By trailers; fixture regeneration with startHeading; a solo Warden cannot close the Lock by design
  (Boss.cs:321-322).

## Phase 3.5 "Look and route" (design/ART-PLAN.md section 8; D16, D17, D18)
- Lane 1 NAV-CORE: DONE. PR #163 MERGED, squash sha 76c7f3223156c1d8e50a5b72cd7eed6a3452765b, branch feat/nav-core,
  mergedAt 2026-10-19T00:54:35Z. Title: "feat(game): lane 1 NAV-CORE - rooms, corridors, room-entry pulls, bot follow (D18)".
- Landed: game/content/data/layout.json (nine boxes along +x: entrance, corridor-1, choir-room, corridor-2, pair-hall,
  corridor-3, seed-chamber, corridor-4, warden-arena; trigger, spawn, pull start, gates, route points, bot slots);
  Keyrun.Content loader and LayoutValidator (fail closed); Movement.ClampToWalkable (one box = ClampToBounds); Run.cs
  room-entry pull spawns with forward-only gates and room_enter events; HostRunSetup party in the Entrance, boss on arena
  entry, Twins at +/-4000 (was 2000); Follow.cs bot follow and ScriptedWarden route walk; GreyBox.cs CSG walls mounted in
  game/scenes/Arena.tscn (replaces the 40 m floor plane).
- Oracle: frozenSourceHash 7a65bec7 UNCHANGED; content/data/** and tests/oracle/** byte-identical; `npx vitest run
  tests/oracle` 9/9.
- Tests: Content 63/63, Sim 557/558 (1 skipped, as on main), Bots 139/139; HudSceneTest PASS (run C asserts room order,
  spawn-after-entry, both boss_add_spawn events); E2eAssert PASS ticks=3700; SMOKE ok frames=3. New: NavLayoutRunTests,
  NavRouteTests (3), WalkableTests, LayoutConfigTests (9). Mutation-proved: spawn without trigger, party on pull origin,
  no clamp, no follow.
- Goldens moved (old -> new, reason in PR #163):
  - example-enter-ping.input-log.json post-run Enter ticks 1860/1863 -> 1307/1310 (run end timed at 1304).
  - E2eAssert replay digest 94eb4f3d216477f9 -> b950474eff2d9967 (fixture unchanged; W walk now starts in the Entrance).
  - HudSceneTest runs A and B ticks 400 -> 600 (run A run_end 374 -> 413).
  - HudSceneTest runs F and G abandon 1@1000 -> 2@3000 (an idle abandoned leader spawns no next pull).
  - HudSceneTest run I --pull 4 -> --pull 2 (from pull 4 the bots now win; from pull 2 the party wipes at tick 896).
  - Unmoved: content-pull oracle trace, hollow-warden boss golden.
  - HudSceneTest.ps1:32 and :54 stale comments FIXED.
- D14 re-sweep after traversal (party autorun from pull 1; end tick old -> new; all cleared within par 14400):
  - key 2: s1 6787 -> 5168; s7 6887 -> 5320.
  - key 5: s1 7481 -> 5800; s7 7472 -> 5888.
  - key 8: s1 8441 -> 6396; s7 7985 -> 6651.
  - key 10: s1 8603 -> 6434; s7 8474 -> 6528.
- D15 frames: C:/Users/Mattt/Desktop/keyrun-screens/nav-core (67 PNGs at 1 s; run.mp4 1,201,340 bytes). PR lists
  run-001, run-039, run-040, run-047, run-058, run-066. Orchestrator viewed run-010, run-025, run-040, run-066: grey-box
  walls and corridors visible; Pull 1 starts at 0:40 after travel; Pair x5 at the far side of the hall with the party at
  the door; run cleared within par at 4:23 of 12:00. Enemy name plates overlap (ladder lane 8).
- Follow-ups (recorded, NOT tuned; Matt or a later lane decides):
  - Bots fight from doorways. In run C bots enter the Seed chamber 693 ticks and the arena 602 ticks after the leader
    (the 600-tick bound is asserted only in the C# test, NavRouteTests). Bot combat positioning in rooms is open.
  - Two variants that pulled bots into the room (2.5 m inside the door; a Warden holding 4 m deep) got the Warden killed
    by the Pair STACK at tick 3294 and the run stalled. Neither landed.
  - Pair melee (957 mm/tick) shoves the Warden back up to about 20 m along corridor 2; keys 5/s7 and 10/s1 re-enter choir-room.
  - The camera starts at yaw Pi (CameraRig, lane 10), so a human pressing W walks across the Entrance, not along the route.
  - Pull script loops run on absolute ticks (scripts.ts), so a later spawn shifts its phase. Unchanged.
  - game/content/data/run.json arenaBounds is no longer read by the host (file left in place).
- Lane 9 LOCK-PROOF: DONE. PR #165 MERGED, squash 1e0953842bb729ca13e55a5a9390ceb9a3a33bb4, mergedAt 2026-10-19T01:27:07Z,
  branch test/lock-proof, title "test(game): Lock winnability proof on the real sim (ART-PLAN lane 9, D17)". Landed:
  game/sim/Keyrun.Sim.Tests/LockWinnabilityTests.cs (ART-PLAN names LockWinnableTests; the shipped name is
  LockWinnabilityTests) with a control test kept. PROVEN on the real run (seed 1, key 2, boss 5150 hp, Lock window 60
  ticks): lock_open 381, Warden Strike (Setup) 382, Cinder Cold Snap (Payoff) 390, lock_broken 391; mutation (Payoff
  withheld) ends in lock_failed 441. The proof uses Warden plus Cinder because a same-class Payoff is combo_denied
  (Boss.cs:321-322). The run is not wired to the NAV layout (open item for Matt). Flake: the PR's e2e job failed once on
  tests/client/e2e/window-ping.e2e.ts:137 (run 39184740861, per the orchestrator) and passed on gh run rerun --failed.
- Lane 2 DUNGEON-SHELL: DONE. PR #166 MERGED, squash 49aeadb9debcda38abf9717377a6d30ddc68ecee, mergedAt 2026-10-19T01:39:42Z,
  branch feat/dungeon-shell. KayKit Dungeon Remastered 1.0 (CC0; github.com/KayKit-Game-Assets/KayKit-Dungeon-Remastered-1.0
  at commit b0ca9bd96a8072ab36a3a5464f00ed1e06a16d07); license quoted in game/assets/ATTRIBUTION.md; 1,428,402 bytes under
  game/assets/kaykit-dungeon-remastered/ (27 .glb, shared dungeon_texture.png, LICENSE.txt, dungeon_material.tres, 28
  .import sidecars force-added past root .gitignore line 15). Pure model game/content/Keyrun.Content/Model/DungeonPlan.cs
  with 9 tests in Keyrun.Content.Tests/DungeonPlanTests.cs (stub 9 fail, mutation 5 fail). game/src/View/DungeonBuilder.cs
  builds walls, floors, props and 12 OmniLights (plan said 8). GreyBox stays as the fallback ("DUNGEON fallback greybox").
  Arena.tscn holds ambient, cool sun and colour; floors cast no shadows. Max 229 draw calls at 1280x720. Tests: Content
  72/72, Bots 139/139; Sim.Tests blocked locally by Smart App Control (0x800711C7), CI green. Walls 1.6 m; the 3 m and 2 m
  passes are kept as dungeon-shell-v1-3m-walls, -v2-2m-walls and -v3-floor-shadows. Frames:
  C:/Users/Mattt/Desktop/keyrun-screens/dungeon-shell (44 PNGs, run.mp4 1,339,432 bytes). Wall fade or cut-away and the
  yaw-0 camera outside the walls go to lane 10.
- Lane 8 HUD-POLISH: DONE. PR #167 MERGED, squash 30f57080949f0d9de0d31a6891c68da7c92252b9, mergedAt 2026-10-19T01:52:08Z,
  branch feat/hud-polish. Pure layout game/content/Keyrun.Content/Model/HudLayout.cs with 17 HudLayoutTests; Slack bar moved
  top-right beside the objective panel; titles cut with "..." via HudLayout.FitTitle; plates on a screen-space 2D layer
  separated by HudLayout.Separate. Frames C:/Users/Mattt/Desktop/keyrun-screens/hud-polish (66 PNGs, run.mp4 1,760,793 bytes;
  orchestrator viewed run-041, run-048, run-058, run-062). Residual overlaps in Pings.cs SPREAD/STACK floor labels and
  CastBarView.cs cast bars filed as HUD-POLISH-2, now DONE (PR #170, next bullet block).
- Lane 8 HUD-POLISH-2: DONE. PR #170 MERGED, squash 581212d58e43e358629988a6f67475c1a683a22f, mergedAt 2026-10-19T02:17:06Z,
  branch feat/hud-polish-2. Pings.cs (file is game/src/Hud/Pings.cs, not game/src/View/) floor labels and CastBarView.cs bars
  are placed by HudLayout.PingLabels and HudLayout.CastBars (new statics beside Separate in
  game/content/Keyrun.Content/Model/HudLayout.cs; no existing signature changed). One layout per frame, shared with LabelView
  through NodePath exports. Lane 8 is DONE with #167 and #170.
- Lane 3 PLAYER-BOTS: DONE. PR #171 MERGED, squash f72c73846174e4a389b663162130d1d0070bec29, mergedAt 2026-10-19T02:28:24Z,
  branch feat/player-bots. KayKit Adventurers (CC0, commit 672074b73ba276876a19e8816ecdc5241817ab47): Knight = Warden, Mage =
  Cinder, Rogue_Hooded = Chanter; scales Knight 0.709, Mage 0.599, Rogue 0.777; ActorAnimator.cs plays the clips from sim state.
  Open: the Mage hat brim (about 0.63 m scaled) is wider than the sim actor radius 0.4 m and clips corridor walls; no death
  frame yet (no death in run C).
- Lane 4 ENEMIES: DONE. PR #173 MERGED, squash cfa40b9cc0d225a57075a8003a775863c8373878, mergedAt 2026-10-19T02:57:14Z,
  branch feat/enemies. CC0 KayKit Character Pack Skeletons 1.0 (commit 15b62b9bad122f72926c10fb14d622c73819fa54; 19,271,629
  bytes under game/assets/kaykit-skeletons/, license beside the assets) bound to every enemy class through ActorBinding rows
  with per-row target heights: choir.melee Warrior 1800; choir.lieutenant Mage 1800 tint 0xB070E0; pair.melee Minion 1800;
  seed.elite Rogue 2340 tint 0x88F088; twinA Warrior 2070 tint 0x7898FF; twinB Warrior 2070 tint 0xFF88C8; boss (Hollow Warden)
  = adventurers Knight 4687 tint 0x50505C; warden and chanter targets 1750; plates max(target, 1800) + 550 mm; ActorBindingTests
  13 to 28; draw calls 350 (final branch peak); 95 clips per glb. Frames: C:/Users/Mattt/Desktop/keyrun-screens/enemies.
  Findings: Sundered Twins hidden behind the Warden and boss at the arena (PARTY-FRAMING fixes); Mage plate floats about 0.55 m
  above the hat (PLATE-HEIGHT fixes). Total CC0 assets about 31.6 MB (Git LFS if over 50 MB is Matt's call). Decision D22.
- Lane 10 feel: DONE (STATE-R9). Slices 1 to 4 merged: CAMERA-FEEL #172, HOST-HEADING #174, PLATE-HEIGHT #176, PARTY-FRAMING
  #177 (PARTY-FRAMING and PLATE-HEIGHT were dispatched 2026-10-19 03:14Z as Opus lanes on disjoint files).
  - CAMERA-FEEL: DONE. PR #172 MERGED, squash 8ffe1e34951b13df04fa1f216a30d3bb34824348, mergedAt 2026-10-19T02:42:44Z,
    branch feat/camera-feel. CameraBoom.cs pure rule with 8 tests: StartHeading faces the route (yaw -1.5707964, +x down
    corridor 1); Clamp keeps the boom inside the walls by MarginMm 300; Occluding cuts away walls that cross the sight line to
    the camera or a living party body at 1.2 m, with 250 mm XZ padding. DungeonPlan gains WallBox, WallBoxes, FrameBoxes and
    SolidOf (add-only). Draw calls max 262 at 1280x720 (limit 341).
  - HOST-HEADING: DONE. PR #174 MERGED, squash 36feb7e15f34af6259a2f7a2da260d2bb3eca820, mergedAt 2026-10-19T03:07:40Z,
    branch feat/host-heading. Optional startHeading (radians) header field after key in the input log, allowed on schema /1
    and /2 with no id bump; a non-finite value is an ARENA error, exit 1; record mode samples the camera heading on the first
    frame and writes it; replay applies it before the first step and logs INPUT replay heading yaw=Y; an absent field keeps the
    legacy heading. HudSceneTest run M (M1 to M4) covers it. K1 compares against example-enter-ping minus the startHeading line
    (fixtures unchanged; regenerating them is Matt's call). Matt's record command from Desktop/keyrun now starts a run facing
    the route. Decision D23.
  - PARTY-FRAMING: DONE. PR #177 MERGED, squash 7318aa64132b6fd55a9b4635982388ecc96b3168 (branch feat/party-framing,
    base 36feb7e). CameraBoom.FitParty pure rule: boom and pitch fit to the party and target; yaw stays player-owned.
    Known camera limits, recorded not fixed: Cinder sits low under the HUD band at the Choir; the Twins line up behind the
    boss in the arena.
  - PLATE-HEIGHT: DONE. PR #176 MERGED, squash 49c127c54beef25ff93de252ef1059d476c5a0f9 (branch feat/plate-height, base
    36feb7e). PlateRule.cs: plate = max(target, scaled model top) + 250 mm, boss cap 5000. Supersedes the fixed plate
    height of the Lane 3 note below.
- Lane 5 ROOM-THEMING-1 (entrance and Choir room): DONE. PR #178 MERGED, squash c2676604441567095e3c88a51bdc1f3952f58b7c.
  Data-driven dressing from game/content/data/theme.json (D25).
- Lane 6 ROOM-THEMING-2 (Pair hall and Seed chamber): DONE. PR #179 MERGED, squash 24a8d0efd98095adf98cbef6c2752c7c9b86168b.
- Lane 7 boss arena and Lock visuals: DONE as two PRs. ROOM-THEMING-3 (Warden arena throne room) PR #180 MERGED, squash
  8b329434fb87a8cacebb6815aa825780014c68e5; LOCK-VIEW (Lock ring on the Hollow Warden) PR #181 MERGED, squash
  39ec4367eec72e4bf1f66ce6377ad8f7a853cd98 (D26). Both are render-only.
- Lane 3 model heads stay at or below 1.8 m for the Party classes; the PLATE-HEIGHT rule (#176) now sets every plate from
  the model's native height, so the fixed 2.35 m plate of the earlier note no longer applies.
- E2E FLAKE, resolver outcome (gate-escape-resolver, 2026-10-19 01:37Z to 01:42Z): the window-ping flake
  (tests/client/e2e/window-ping.e2e.ts:137) went through the resolver. It pushed guard/e2e-wall-clock-wait-ratchet 0123f68
  (tools/ci/e2e_wait_ratchet.py counts sleep and waitForTimeout per e2e spec against tools/ci/e2e-wait-baseline.json; wired
  into the e2e job of .github/workflows/tests.yml). It opened no PR and left window-ping.e2e.ts unchanged. Its verifier
  judged the guard INEFFECTIVE: raw setTimeout promises, nested directories and helper aliases bypass it. GUARD-HARDEN: DONE.
  PR #169 MERGED, squash 9df96ef2c77ca76a4da78449ed6f8a844d3f9e44, mergedAt 2026-10-19T02:10:54Z. tools/ci/e2e_wait_ratchet.py
  now counts raw setTimeout promises, nested directories and helper aliases (baseline 8 to 5, unit tests 40 OK);
  window-ping.e2e.ts fixed (waits on exposed state, PING_ATTEMPTS 10, no timeout raised). Ratchet on this branch:
  "e2e-wait-ratchet: OK (5 wall-clock waits across 5 files; baseline 5)". Decision D24 records the finding below.
  FINDING (orchestrator summary; not stated in the #169 body): that lane, denied by the BUILDLOG append-only hook during a merge
  conflict, improvised `git show origin/main:BUILDLOG.md > BUILDLOG.md`. The brain repo closed that hole (claude-config #118
  denial text, #119 Bash coverage, both MERGED); the sanctioned path is `git checkout origin/main -- BUILDLOG.md`, then re-append.

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

## DECISIONS FOR MATT (checkpoint 2026-10-17 23:55Z)
1. Spot ping aim: crosshair ray to y=0 lands 0.8-2 m past the player because the camera looks 1 m above the player; alternatives are a mouse-cursor raycast or a raised aim point.
2. Lock payoff glow is optimistic: the client cannot see which class closed the last Lock.
3. Net games (?net=1) get no payoff glow because the server sends no display info for them.
4. pingEvents in client/src/hud-wiring.ts grows unbounded (pre-existing).
5. idle-human-warden variant still wipes in sim (actor 5 dies t1591; 4x70 one-shots 250 HP cinders from 3000 mm; needs bots/src/roles/damage.ts + bots/src/shared/positioning.ts); harness artifact, not the play case.
6. #103 lane decisions taken silently: dead enemies stay hidden rather than tagged DOWNED; taunt detection = threat multiplier >= 5x; dash trail inferred from snapshot positions (a very large frame stall while walking could draw a false trail); Slack delta deferred (needs hud-wiring.ts).
7. BIBLE-vs-client gaps still open: Sundered Twins + Break bar not spawned; boss 50% Lock + enrage absent; stack damage split absent; Cinder Blink no displacement; F=Charge vs Space=dash authority; floor Windows + combo events need snapshot channels; death log/mercy absent; rewards run-end stub; clause pick UI + key level display absent; keybinds 11 vs 10; Cinder fills all 3 damage slots.

## DECISIONS AND OBSERVATIONS (checkpoint 2026-10-18)
- (a) #106 MERGED 10de063: scaleStep keeps `every` (Tempo-scaled via scaleCastEvery, same as a loop step; `at` stays
  the unscaled phase offset). Proof cited in the commit body: tests/server/stack-every.test.ts and
  tests/client/sim-host/content-dungeon.test.ts drive stepEnemyScripts over a 1200-tick key-2 pull-2 window and expect
  >=2 STACK telegraphs. The screenshot proof was reported as kept in the session scratchpad; this lane did not locate
  or re-check that file.
- (b) Telegraph-label decisions, from the #105 commit 56c5686 body (BUILDLOG.md has no telegraph-labels line):
  1. Label text "SPREAD: move apart" / "STACK: group up", drawn in the class colour.
  2. Constant-size billboard sprite at each spread/stack telegraph centre (render/telegraph-label.ts).
  3. Seconds countdown plus a bar draining as 1 - telegraphFillFraction.
  4. Fades over the last LABEL_FADE_TICKS.
  5. Same-class labels within LABEL_MERGE_M share one label (a clumped pull of five casters reads as one instruction).
  6. Labels draw above name tags and damage numbers.
  7. avoid (incl. seed.slam), boss and unclassed telegraphs get no label.
  8. TelegraphPool takes an optional TelegraphLabelPool that boot.ts injects; headless callers and existing telegraph
     tests keep drawing decals only.
- (c) OBSERVATION 2026-10-17: with hz=60 at key 2 the bot party is mostly DOWNED for much of the run (idle-human-warden
  sim). Needs a tuning decision from Matt. Source: orchestrator brief for this lane; not yet in BUILDLOG.md.
- (d) PHASE 3 ORACLE VERDICT 2026-10-18 (read-only plan-vs-output lane, ~18:25Z, source reads at c03f905): D10 NOT-MET
  on the end-to-end bar. Decisive line: game/src/Host/HudSceneTest.ps1:43-45 records that every swept key (2,5,8,10,12,15,20)
  wipes in pull 2 under the scripted autorun Warden, so no run in play reaches pull 3 or the boss without a start-pull
  flag. The code path for the whole run exists (host, bots, boss arm, twins, queue, backfill), but the human-played path
  is unmeasured, so even MET-WITH-TUNING-GAP is unproven. WHY this is recorded as NOT-MET and not MET: the bar is what Matt
  can play, and no measured run reaches the boss. PHASE 3 EXIT is NOT recorded.
  Drift per row (plan vs code, from the same lane):
  - 3C: banner "Run over ... press Enter" at game/src/Hud/AbilityBar.cs:114 is not in the plan (fix/hud-followup RUNNING).
  - 3D: the screen opens only from RunEndPanel.cs:219 after a reward take; ENGINE-PLAN.md:219-220 says it comes up before
    the run starts (OPEN, build gap; step 2 above).
  - 3E: game/fixtures/input/walk-kick.json does not drive the Warden; SimDriver reads no input file (OPEN, 3E-a
    recorded-input reader).
  - 3F: Lock is armed (SimDriver.cs:541 lockAt=50) but never asserted (step 3 above).
  - 3G: default abandon is 1@1000 while the plan acceptance says pull 2 (minor, note only).
  - 3I: Screen.cs:527-532 DrawQueue text is false since #141; P0 (step 1 above).
  Other open items carried from lane hand-backs:
  - 3H (c): human G-pings are not passed to RunBots. 3H (d): the scripted autorun party clears the Choir at about tick
    3150, then wipes on the Pair at key 2.
  - 3C: reward cards use placeholder ids; wipe title shows when PullIndex < Pulls.Count.
  - cinderfield self-hit: REAL-BY-DESIGN, MEDIUM, at game/sim/Keyrun.Sim/Encounter/Payoff.cs:153 (mirrors payoff.ts:117).
  - Open items from the lane 3, 4 and 10 hand-backs (STATE-R8, 2026-10-19; each is Matt's call, nothing is tuned here):
    - Mage hat brim (about 0.63 m scaled) is wider than the sim actor radius 0.4 m and clips corridor walls (#171).
    - Boss head-line per row: the boss Knight is 4687 mm at 1900 permille (D22) and stays open (#173).
    - Fixture regeneration with startHeading: the committed fixtures predate the line; K1 skips it (D23, #174).
    - Git LFS if the CC0 assets exceed 50 MB (about 31.6 MB today; #173).
    - Delete the merged keyrun worktrees and branches (after pr_merged_check.py proves each merge).
    - The #173 branch merge commit 6fe70af lacks a Co-Authored-By trailer (cosmetic). The first PARTY-FRAMING merge commit
      lacks one too (its sha is not recorded in the lane hand-back; STATE-R9).
    - STATE-R9 open items for Matt: the human-played run to the boss (the only mover of D10); the boss slam and the Twins
      slam are untelegraphed while the cleave is telegraphed (content/data/enemies/hollow-warden.json, enemies.json; a fix
      moves the oracle hash); the arena light cap of 3 stays unless Matt says raise; a solo Warden cannot close the Lock by
      design (Boss.cs:321-322; the proof uses Warden plus Cinder, D21).
    DECISION FOR MATT: should field combos hit the caster (BIBLE section 5)? Same as defect (m) above.
  - Cast-range finding from 3I: REFUTED by source recon (range checked at ScriptTarget.cs:31-32,100-108,120-122 and
    Casting.cs:84-97). The 13-17 m figure is the spawn gap.
  - D13 nuance: the Smart App Control block is intermittent. It did not block the 3D/3E/3F/3G/3I/3J test runs. A fresh
    exported exe sometimes crashes with `gd_mono.cpp:487 Failed to get GodotPlugins initialization function pointer`
    (rc -1073741819). Reported literally; no lane touches security settings.
  - DECISION FOR MATT: run difficulty tuning. Under autorun no key reaches the boss. At `--pull 4 --key 10 --seed 7` the
    party wipes at the boss by tick 400, before any Lock.

## Standing rules
- Every audit and integration test reproduces through the public path (createRun/stepRun, the bot loop, the server
  room over ws, the client worker host). A mechanic not wired into the composition root in the same PR is a P0.
- Net/local parity: a telegraph, kick flag, or event that exists on the local path must exist on the net path in
  the same PR. Client events are read only via client/CLAUDE.md SnapshotEventLatch, never nextSnapshot.events.
- Model: Opus for Keyrun development lanes (D11); Haiku for read-only recon, lint and bookkeeping.

## DECISIONS 2026-10-18 (engine)
- Matt, verbatim: "im honestly confused why the game is being built in typescript on a browser lol, that was supposed
  to be first paint but it'd be better just to install an actual tool people use for this you are familiar with"
- Matt, verbatim: "i mean its not a browser game though"
- Matt, verbatim: "how are you going to port ts to a 3d renderer lmfao" (answer: nothing visual is ported; only sim logic, with the seeded TS sim as parity oracle; rendering is rebuilt natively)
- Matt, verbatim: "just use a common productivity framework lmfao people vibecode games like crazy... opus make a
  better plan lmfao"
- Matt, verbatim: "because it was never supposed to be a browser game lmfao"
- D12 (recommended by the opus planning lane, design/ENGINE-PLAN.md; accepted by the orchestrator under the decide-and-report grant): Godot 4.7.2 .NET (installed, no account), C#
  throughout, sim and bots ported tick for tick to plain net8.0 libraries with the frozen TS sim as parity oracle;
  content/data JSON kept verbatim; TS trees deleted after parity (ENGINE-PLAN phase 4). Supersedes D1 (stack) and the
  browser part of D3; D3's headless-deterministic-sim / renderer split, D9 (20 Hz) and D10 (MVP) stand. Unity rejected:
  needs Matt's sign-in and a CI licence secret. Unreal rejected: account, size, binary assets.
- Intent-decay note: five feature lanes (#101-#106) were built on the throwaway browser client as if it were the product. The PURPOSE line above and the plan-vs-output check exist so this does not recur.
- D13 **Windows Smart App Control stays ON** (Matt, 2026-10-18: the orchestrator asked, Matt replied "go ahead"). Why:
  the orchestrator asked Matt whether to turn SAC off to get past the intermittent block, and Matt chose to keep it on. Rules that
  follow: CI game-test is the acceptance gate for C# lanes. A local `dotnet test` is run once and reported literally,
  because SAC blocked lane 1A's host load while lane 1D's local run worked, so the block is intermittent. No lane touches
  security settings; a block is reported literally and the lane stops (game/CLAUDE.md, Smart App Control section).
- D14 **Content tuned so an autorun reaches and clears the boss** (Matt, 2026-10-18 21:50Z, verbatim: "go ahead and tune the boss hp so a run reaches it"; lane TUNE-BOSS, orchestrator option (a): edit content/data, regenerate the oracle). Why: no autorun reached the boss (every key wiped on the Pair), so D10 and the Phase 3 exit could not be judged from a real run. Numbers (content/data/enemies, old -> new): boss maxHealth 60000 -> 5000; boss slam loop every 60 -> 300 (cleave stays every 160); Twins maxHealth 6000 -> 1000 each, twins.slam every 60 -> 400; Pair maxHealth 6000 -> 1200, pair.meleeSwing loop every 40 -> at 60 every 150; Seed maxHealth 6000 -> 3000; Choir unchanged. Result: HudSceneTest run C (key 2 seed 1 party autorun) reaches the boss, the Lock opens, and the run ends "Run cleared -- within par" at tick 6787; keys 2/5/8/10 at seeds 1 and 7 all clear; solo run B still wipes on pull 2. Oracle regenerated with tests/oracle (frozenSourceHash 44e0de9a -> 7a65bec7). The bot party never breaks the Lock in any sweep run (it wins through Lock fails), an observation for Matt.
- D15 **Visible work is judged from frames** (Matt, 2026-10-18 23:31Z, verbatim: "well you need to view screenshots and videos to
  refine the game to the objective lmfao"). Rule: every lane that changes anything visible (HUD, scenes, run flow, content pacing)
  ships screenshots and a frame sequence of the run (PNG frames plus an mp4 made with ffmpeg, installed on this box) into a stated
  folder, and the orchestrator views them before accepting the work. Phase 5 feel judgments use them too. A headless log alone no
  longer closes a visible row.
  Acceptance flow (2026-10-20): a lane flags the visible change in its PR. The orchestrator captures the frames one at a
  time under the launch lock. Matt watching is still the acceptance.
- D16 **The demo gets a real dungeon and real character models** (Matt, 2026-10-18 23:35Z, verbatim: "we need to design an actual
  dungeon and character models for the demo lmfao"; "it cant be just kovakks"). Rule: the art and navigation pass in
  design/ART-PLAN.md (Phase 3.5 "Look and route", a ten-lane ladder, lane 1 first) turns the capsules on a grey grid into a
  themed dungeon run with modelled, animated party, enemies and boss; the HUD stays; every lane ships D15 frames. Why: Matt
  judges the game by feel from viewed frames, and capsules on a grid read as an aim trainer, not a dungeon run.
- D17 **CC0 kits only; the Lock must be theoretically winnable** (Matt, 2026-10-18 23:43Z, verbatim: "no custom art lol its a
  vibecode game, bots don't have to beat the lock but it should be theoretically winnable lol..."). Rules: art is CC0 kits only
  (KayKit, Kenney, Quaternius or similar, free tiers; no paid or original art); each pack ships its license file beside the
  assets in game/assets/<pack>/ and an entry in game/assets/ATTRIBUTION.md. The Lock question (NEXT ORDER step 2) is closed: the
  bot party is not required to break the Lock; the Lock must be theoretically winnable, proven by a scripted-input run on the
  real sim where a Setup then a Payoff inside the window closes the Lock (the synthetic BossRulesTests.cs test alone is not the
  proof); beating the boss through Lock fails stays an acceptable autorun outcome. Why: the demo is a vibecode game built from
  free kits, and the Lock has to be a real, beatable mechanic even if the bots never beat it.
- D18 **Themed rooms with real navigation** (Matt, 2026-10-18 23:45Z, verbatim: "just thematically theme the dungeons, and why are
  all the mobs spawned on top of you? are we not going to have navigation in the dungeons lmfao?"). Rules: the Hollow Vault is
  themed room by room to its pull: the Entrance where the screen and Clause pick happen, the Choir room, the Pair hall, the Seed
  elite chamber and the Hollow Warden arena; theming uses kit pieces and lighting, no custom art (D17). Navigation is part of the
  demo: each pull lives in its own room at its own position, the party travels through corridors from room to room, and a pull
  triggers on entering its room, never on top of the player at run start. The bots follow the player between rooms and the
  autorun walks the route. This is sim-affecting (spawn positions and pull triggers move), so the goldens are regenerated under
  D11 rules (the C# goldens always; the TS oracle too if content/data/** changes); render-only dressing is no longer the
  preferred path for the layout. Why: today pull 1 spawns 3 to 5 m from the party at tick 0 and every later pull spawns in the
  same 22 x 15 m box the tick the previous one dies (design/ART-PLAN.md section 1), so there is no dungeon to travel.
- D19 **Out-of-brief plumbing accepted in #163** (orchestrator, 2026-10-19 00:54Z, under D18). Seven files outside the
  NAV-CORE brief's owned list were accepted as necessary for the room layout to reach the host, bots and backfill:
  game/sim/Keyrun.Sim/Run/RunTypes.cs (RunContent.Layout), game/src/Host/SimDriver.cs (party from setup.PartyFor; the
  PULL spawn index=0 log line only if pull 0 spawned), game/sim/Keyrun.Sim/Run/Backfill.cs (seat fill at the next
  pull_start), game/bots/Keyrun.Bots/Types.cs (BotView.Layout), game/src/Host/BotCommandSource.cs, game/src/Host/E2eAssert.ps1
  ($ReplayTickDigest) and game/fixtures/input/example-enter-ping.input-log.json (regenerated golden). Future sim lanes own
  RunTypes.cs, SimDriver.cs, Backfill.cs, Types.cs, BotCommandSource.cs and Bot.cs explicitly when the change needs them.
  Why: reversible, no data loss, within D18.
- D20 **Lane 2 dungeon-shell choices accepted** (orchestrator, 2026-10-19 01:39Z, PR #166 mergedAt): Orchestrator accepted
  lane 2's 1.6 m walls (2 m and 3 m hid bots at doorways), 12 OmniLights against the plan's 8, and force-added .import
  sidecars under game/assets as reversible, no-data-loss choices inside D17 and D18; wall fade or cut-away and the yaw-0
  camera outside the walls go to lane 10. Why: the doorway readability bar (ART-PLAN section 2) is judged from frames (D15),
  and each choice can be reverted without data loss.
- D21 **Lock winnability is proven on the real sim** (orchestrator, 2026-10-19 01:27Z, PR #165 mergedAt): Lock winnability
  is proven on the real sim by LockWinnabilityTests (Warden Setup plus Cinder Payoff inside a 60-tick window); the bot party
  is still not required to close it (D17). A solo Warden cannot close the Lock by design (Boss.cs:321-322). Why: D17 requires
  the Lock to be theoretically winnable, and only a real-sim run proves that, not the synthetic BossRulesTests.
- D22 **Enemy head-line targets per row accepted** (orchestrator, 2026-10-19 02:57Z, PR #173 mergedAt): the orchestrator accepted
  per-row head-line targets for enemies (boss Knight 4687 mm at 1900 permille, seed elite 2340, twins 2070, others 1800; warden
  and chanter 1750) as a reversible content choice; the boss head-line stays open for Matt. Why: the per-row table is an
  orchestrator decision within ART-PLAN section 4 (D16, D17), each row can be retuned without data loss, and the frames
  judge it (D15).
- D23 **A recorded run starts facing the route** (orchestrator, 2026-10-19 03:07Z, PR #174 mergedAt): the input log carries
  startHeading, replay applies it, and absent means the legacy heading; fixtures were not regenerated (the K1 comparison skips
  that line), and regeneration is Matt's call. Why: since #172 a run Matt records kept the legacy heading because the log never
  stored the frame-0 heading, so the recorded run did not face the route; carrying it in the log fixes that without moving the
  E2eAssert digest b950474eff2d9967.
- D24 **A lane denied by the BUILDLOG hook stops and uses the sanctioned path** (orchestrator, 2026-10-19 02:10Z, PR #169
  mergedAt): a lane that is denied by the BUILDLOG append-only hook stops and uses the sanctioned path (`git checkout origin/main
  -- BUILDLOG.md`, then re-append); the GUARD-HARDEN workaround (`git show origin/main:BUILDLOG.md > BUILDLOG.md`) is recorded as
  a finding, and the wall was built in the brain repo (claude-config #118 and #119, both MERGED). Why: an improvised rewrite of an
  append-only control file defeats the guard; the hook was fixed in the brain repo, not routed around.
- D25 **Every room is dressed from theme.json; the arena has no dais** (orchestrator, 2026-10-19, PRs #178 to #180; reversible;
  bound: view-only data dressing). Each room is dressed from game/content/data/theme.json rows (props, lights with an optional
  "kind" of torch or warden, decor, optional cracked). DungeonPlan.Dress is only the fallback for a room with no theme row,
  plus corridor sconces. The arena has no dais. MaxLightsPerRoom stays 3 (the arena went from 4 coded lamps to 3 data
  lights; 13 lights in total). chest_gold is reserved for the Seed chamber. The placement rule lives in RoomThemeTests
  (LaneHalfMm 1500, PropClearMm 2000). Twin sites come from the content registry via ThemeSite.Of, with ThemeSite.TwinOffsetMm
  4000 mirroring HostRunSetup.TwinOffsetMm (a drift test pins the two). Why: D16 and D18 need the rooms read as themed
  places, the rule is data, and each row can be retuned without data loss; judged from frames (D15).
- D26 **The Lock is shown as a view-only ring** (orchestrator, 2026-10-19, PR #181; reversible; bound: view-only). LockRule.cs
  is a pure integer rule (RadiusMm, AlphaPermille, Phase Idle, Open, Broken, Failed, Enraged): Open is violet 0xC77DFF
  shrinking from 4000 to 1500 mm; Broken is green 0x60E080 in a 30-tick flash; Failed is red 0xFF5050 in a 30-tick flash;
  Enraged is dim red 0xA03030 at 2000 mm. LockView.cs sits at the boss feet, wired as the Lock node after Telegraphs in
  game/scenes/Arena.tscn. WindowRingView keys violet on ParsedWindowId.Alternate for the lockwindow prefix. A `LOCK view
  phase=` stdout line exists and is not in the run log or the digest. Why: D17 needs the Lock to be a real, readable
  mechanic, and the view must not move the sim.
- D27 **ROOM-LOCK-IN: a pull starts when the living party is inside its room, and the gate shuts behind them**
  (Matt 2026-10-19, verbatim: "I think the party should have to enter the room to see the pull, not cheese from doorways
  lol, door should shut behind them"; lane ROOM-LOCK-IN; reversible; bound: layout runs only, every oracle run has no
  layout and is unchanged). A pull's pull_start fires on the first tick at least one living player and EVERY living player
  (human and bots) stands inside the pull's room box (`NavLayout.RoomFor`: the layout.json room that holds the pull's
  trigger; choir-room, pair-hall, seed-chamber, warden-arena). Until then the pull's enemies are not in the state, so they
  cannot be targeted or damaged and deal no damage. On that tick the sim emits `gate_closed {room}`; from the next tick
  until the pull's pull_clear `NavLayout.GateClamp` holds every actor: one inside the room box cannot leave it (clamped to
  the edge), one outside cannot enter it (move refused). `gate_open {room}` is emitted with the pull_clear. Dead members and
  abandoned unfilled seats (`StepRunOpts.OpenSeats`, passed by Backfill.Step) never hold a pull back. Integer math, no
  randomness. Consequences: the scripted autorun Warden walks to a room's far door while no enemy lives, so it can stand
  near the spawn at pull_start (NavRouteTests' 6 m spacing now covers the bots only; a host lane may make the leader wait);
  a backfill joiner outside a locked room waits for the clear. Run C (with WARDEN-ENGAGES): run_end timed 3457 -> 3522, BOTS hash
  6bffd4e6188d81b4 (986fa6e7666d7ab0 on the #187 base); run F abandons at 2@1700. Why: design/BIBLE.md sections 4 and 9 build each pull on the party landing together
  inside the room; before this a pull could be started and fought from the corridor (the Seed elite was killed from the
  doorway in the run Matt watched).
- D28 **ORACLE POLICY: the C# sim is the source of truth; the oracle fixtures and the replay digest are recordings**
  (orchestrator, 2026-10-19, lane ROOM-LOCK-IN; reversible). The TS parity oracle fixtures (tests/oracle, frozen-source hash
  in game/sim/Keyrun.Sim.Tests/Fixtures/oracle/manifest.json) and the E2eAssert replay digest (game/src/Host/E2eAssert.ps1
  `$ReplayTickDigest`) record the sim. From D27 on they are regenerated whenever a sim rule changes (`ORACLE_WRITE=1 npx
  vitest run tests/oracle`; the digest edited to the newly measured value), with the reason written next to the new value;
  they are no longer an independent parity check. D27 values: manifest hash 7a65bec7 -> 7a65bec7 and replay digest
  b950474eff2d9967 -> b950474eff2d9967, both unchanged and regenerated with reason "D27 room lock-in" (the oracle runs have no
  layout, and the 240 replayed TICK lines measured the same in two E2eAssert runs). Why: the TS sim is frozen (D12) and the C# sim now
  carries rules the TS one never had, so a check against TS can only record, not judge.
- D29 **lane row files: lanes write game/lanes/<branch>.md, the fold lane merges them post-squash (aesop BUILDLOG 2026-10-19 17:50Z)**
  (orchestrator, 2026-10-19, lane LANE-ROW-FILES; reversible). Why: three keyrun PRs in one day each needed an Opus resume only to append one line to game/CLAUDE.md and BUILDLOG.md.
- D32 **GEAR DEPTH: "best" depends on the key, the party and the build, so the endgame is not one best-in-slot set**
  (Matt 2026-10-20, verbatim: "my biggest concern with keyrun is there is not enough gear... like the end game will just
  be hunting BiS lmfao... and we have 1 gear slot"). Numbered D32 after D31 (PR #311, docs/skill-toolkit-direction) and
  D30 (PR #306, docs/aggro-taunt-direction), neither merged when this was written; rules 1 and 3 depend on D31's toolkit
  verbs and D30 rule 3's threat ops, rule 2 on the Fortified/Tyrannical affixes of PR #304 (open). Direction recorded;
  design/BIBLE.md is NOT amended (the conflicts and proposed wording wait for Matt in the D32 PR body;
  research/13-gear-depth.md is the reason). The budget holds: five slots (Weapon, Armor, Charm, Ring, Keystone), one
  Facet per item, Tier 1-10, four to five rotational abilities and ten keys, and no stat-only power past the cap.
  The rules:
  1. **Facets are sidegrades that change function.** Every Facet changes what an ability does: a Window/Payoff tag, a
     shape, a resource loop, or a D31 toolkit verb (threat take, hand-off, kite, save). A Facet that only adds a number
     is a defect. Tier is the only power number, and it stops at 10.
  2. **Key-specific correctness.** The key's affix (Fortified: trash harder; Tyrannical: boss harder; both from +10),
     its Clause and the dungeon's enemy kits change which Facet is right: a cleave or kite Facet for Fortified packs, a
     Payoff or save Facet for a Tyrannical boss, a kick-tagged Facet under Hush. The BIBLE s6 parity check runs per
     affix and per Clause: a Facet that wins on every key is a dominance defect, the same as a talent fork.
  3. **Party-synergy Facets (BIBLE s4).** Some Facets pay off only through a partner: they open a Window only another
     class's Payoff can close, or retag an ability so it closes a named partner's Window. Their value depends on who
     queued, so no global best exists for them.
  4. **A loadout swap between keys, not one set.** Every Facet the character has owned stays in an armory with its best
     Tier; on the screen, before Queue, the player equips five (one per slot) for this key. The swap is free and fast
     (test 2: Queue inside a minute; a remembered loadout per affix is one key press). Crafting's two charges per item
     stay. The armory is storage, not a sixth slot.
  5. **The chase.** The endgame is not one BiS set. It is (a) owning every Facet of the class, (b) each at Tier 10,
     where the last two tiers come only from boss mechanics done right on high keys (BIBLE s7), and (c) clearing harder
     keys under each affix and Clause with the right loadout. After the cap the chase is Ascension marks, earned per
     affix and Clause cleared timed above key 10 and shown on the character, never power and never a public score.
  Judged and kept: all five candidates, with two limits. Swapping is per key on the screen, never inside a run. Pair
  Facets never require a specific partner to finish a key (BIBLE s4 "a tag, never a class by name").
  Why: Matt's concern is that a single best Facet set at Tier 10 ends the game; BIBLE s7 currently says exactly that
  ("Best-in-slot is a specific Facet set at Tier 10 for your build"). Games that keep gear chasing alive make the right
  item depend on context (WoW per-dungeon trinkets, Hades Duo boons, PoE build-enabling uniques), and the one that
  collapsed into BiS used large set multipliers (D3 six-piece sets) (research/13-gear-depth.md, 2026-10-20).
- D33 **AGENT PLAYTESTERS: dispatched agents play keys headless, fail first, and learn through receipts and memory**
  (Matt 2026-10-20, verbatim: "the bots are your gametesters, they should learn and progress keys as well, they should be
  given an understanding of how to play the game, just use agent playtesters" and "ensure the bots can fail lol, they
  should not clear the first dungeon on the first try unless they have played wow lmfao, the point is agents can learn the
  game over time using the same machinery we are using right now"; design design/PLAYTESTERS.md, lane docs/agent-playtesters;
  D30-D32 are claimed by open PRs #306, #311, #315; reversible). A playtester is a Haiku subagent that gets only the
  player-side guide (no enemy ids, no answers), writes a plan per batch, plays it on 3 seeds in the headless sim through
  #305's `playtest run` (never Godot), and writes one lesson citing a run. Each run's receipt adds a traceHash
  (Projection.TraceHash, as Keyrun.Replay) and the build sha; `playtest verify --replay` recomputes it. The key moves by
  BIBLE s8 (timed +1/+2/+3, untimed stays). Learning counts only when learners beat an amnesiac control on held-out seeds,
  first-run timed rate stays at or under 20%, and a plan provenance check refuses any enemy id the playtester never saw.
  Cost: about $0.0035 per decision at Haiku list price, cap 3M input tokens per experiment; any recurring loop is NEEDS
  MATT. NEXT STEPS (smallest first): merge #305; fold the guide into GUIDE.md; traceHash + buildSha; provenance check;
  verify --replay; first-run failure measurement; report script; one cohort experiment; recurring loop NEEDS MATT.
- D34 **CINDERFIELD CLOSER COST: closing a Cinderfield still burns the closer, as a capped share of its max health**
  (Matt, 2026-10-20, asked "should closing a Cinderfield burn the closer?", verbatim: "probably, but as damage scales
  this will turn into a one shot, so abilities and damage reduction stuff needs to be considered lol"; lane
  CINDERFIELD-CLOSER-COST, finding f-17c18460; reversible). Implemented: the game/content/data/combo-riders.json rider
  for `cinderfield-field` replaces the frozen 80 fire with a C#-only `closerBurn` op: 15% of the closer's max health,
  amplified by the Pact, the closer's damageDoneBp and any damage-taken increase, hard-capped at 20% of max health,
  and then lowered by the closer's damage reduction (Brace, Vigil, ...), which applies after the cap so defensive play
  always counts. The validator requires 0 < base <= cap < 100%, so the burn alone can never kill a
  closer at full health at any key or gear Tier. Why: the closer is both source and target of a field Combo, so the
  Pact's comboDamageBp and any future gear power riding damageDoneBp scaled a raw number with no ceiling.
- Matt's 2026-10-20 dungeon, key and reward decisions (lane docs/record-matt-decisions-1020). Numbered D35-D42 because
  D30-D34 are claimed by open PRs #306, #311, #315, #320 and #338. The agent-playtester decisions (ledger f-4f45e954,
  f-4e8c87b7) are recorded by D33 on PR #320 and are not repeated here. Why these are here: they were held only in the
  aesop open-findings ledger, which is for work, so the decisions move to this file where every lane reads them.
- D35 **Everything is prespawned with an aggro distance** (Matt, 2026-10-20, verbatim: "everything should be prespawned
  with an aggro distance"; ledger f-6d538dd2). Every pack exists idle from the run's first tick and engages when the
  party enters that pack's aggro radius; this replaces spawn-on-room-entry (Run.cs:255-258, Pulls.SpawnPull,
  NavLayouts.PlaceDungeon). For the build: BIBLE section 9.1 ("Everything is prespawned with an aggro distance", patrols, aggro
  rings, skips). Build: not yet built (PR #308 PRESPAWN-AGGRO is open).
- D36 **Zero downtime means stakes, not spawns** (Matt, 2026-10-20, verbatim: "when i say 0 downtime, i mean the high
  stakes m+ introduces, not literally spawning enemies on the player"; ledger f-66f25047). Spawn-on-entry and
  density-rule readings of zero dead time are removed; the stakes come from the timer, forces, the route and pull size.
  For the build: BIBLE section 9.1 ("Zero downtime means stakes, not spawns"). Build: not yet built (the spawn-on-entry path is
  still on main; D35 / PR #308 removes it).
- D37 **Lean into Mythic+ inside the dungeon** (Matt, 2026-10-20, verbatim: "honestly just lean into m+ design heavily
  while in the dungeon"; ledger f-228202e1). Dungeon design follows Mythic+ conventions (timer and chest tiers, forces
  percent, mixed packs with kick and stop casters, patrols, skips, a death time penalty, rotating affixes, key upgrade);
  the screen, gear and social rules stay as the BIBLE has them. For the build: BIBLE section 9 principle, sections 9.2-9.3 and
  section 8. Build: partly built (timer tiers and affixes merged in #304; forces/route, stops and patrols not yet built,
  PR #302 and #308 open).
- D38 **Almost exactly WoW, with less button clutter** (Matt, 2026-10-20, verbatim: "it should feel almost exact to wow
  with less button clutter"; ledger f-3897cb62). WoW Mythic+ is the dungeon reference almost exactly; the only intended
  divergence is fewer buttons (the ten-keybind budget, D4). For the build: BIBLE section 9 principle and section 2. Build: a
  standing direction, not one build item; each Mythic+ convention ships under D37.
- D39 **An untimed key stays at the same level** (Matt, 2026-10-20, re-asked, as relayed: an untimed key stays the same
  level, no depletion drop; ledger f-7c7abdf4). A deliberate divergence from WoW for test 3. For the build: BIBLE section 8
  (timer and Slack). Build: built (#304, `Timer.NextKeyLevel` never lowers the key, game/sim/Keyrun.Sim/Encounter/Timer.cs:133).
- D40 **Private Mythic+ rating, own screen only** (Matt, 2026-10-20, as relayed: a private WoW-style M+ rating shown only
  on the player's own screen, never to others and never used to filter groups; ledger f-a3230c59). For the build: BIBLE section 0
  test 3 (no shareable score) and section 10 (the screen); the BIBLE has no rating line yet. Build: not yet built (PR #318
  PRIVATE-RATING is open).
- D41 **Loot quality comes from the key level; +2/+3 pays as key movement** (Matt, 2026-10-20, as relayed: loot quality
  comes from key level; +2/+3 pays by jumping the key 2 or 3 levels; choose-one-of-three stays; ledger f-baeb031a). Build:
  BIBLE section 7 ("Loot quality comes from the key level") and section 8. Build: partly built (the +2/+3 key jump is in
  #304, `Timer.UpgradeTier` and Screen.cs:670-671; reward tiers rising with the key level are not confirmed in code, and
  PR #317 for the end-of-run key display is open).
- D42 **Affixes follow current WoW** (Matt, 2026-10-20, as relayed: from +5 one of Fortified or Tyrannical rotates, from
  +10 both are on, a rotating Clause layers on top; ledger f-319aced5). For the build: BIBLE section 8 ("Fortified and
  Tyrannical", "The Clause"). Build: built (#304, Encounter/Affixes.cs, ClauseRotation in Run/Clause.cs and
  RunOptions.cs, values in game/content/data/run.json).
- D45 **Orchestrator decisions 2026-10-21, tune later** (orchestrator, 2026-10-21, under Matt's verbatim: "make the rest of the
  decisions and we will tune later"; reversible, tune later). The decisions:
  - Death penalty: 15 s per death (the current 300-tick Slack cost stays), WoW Midnight-style.
  - Gear tier power: keep the merged per-slot tier tables from #314; no flat +2%/tier.
  - Rewards: every finished run, timed or depleted, offers the choice of three; a player who leaves mid-run gets nothing.
  - Every enemy plate shows its cast bar.
  - MVP line (D10 question): the full section 1 loop: screen, queue, dungeon, end-of-run choice.
  - Tank: Warden passive armor 40% (PR #362), tune later.
  - #329 (stuns stop interruptible casts) and #344 (break-bar stagger stops the Twins) both land; #344 is the boss-specific case.
  - #325's held-cast/spansCast behaviour, #357 removing content-pull from the frozen set, and #358's dial-90 seed are accepted.
- D46 **MPLUS CLOCK ONLY: deaths add time to the run clock; a key times or not purely on that clock** (orchestrator,
  2026-10-20, from Matt's verbatim "honestly just lean into m+ design heavily while in the dungeon" and "it should feel
  almost exact to wow"; reversible, tune later). Each player death adds 300 ticks (15 s) to the run clock, counted per
  player including same-tick deaths (#373); a full wipe adds 500 ticks more. There is no Slack bank that fails the key
  once drained: a completed run is timed when elapsed plus that added time is within par, and the +2/+3 tiers are
  measured on the same clock (Timer.ClockTicks, KeyMove.ForRun). An unrecovered full wipe still ends the run depleted.
  This supersedes BIBLE section 8's Slack bank for the dungeon; the bank constants stay in Timer.cs as tunables. WHY:
  in WoW Mythic+ a death only costs time, it never fails the key on its own; under the bank, three deaths at +2 (900
  ticks against a 620-tick bank) depleted a key that was far ahead of par. The HUD bar now reads the run clock and
  "Deaths N (+Xs)" (TimerSlack.cs). Tune later: the 15 s / 25 s costs and whether the HUD bar keeps its shape.
- 1D FINDING (2026-10-18, lane 1D, PR #111): content/data JSON diverges from the TS types. EffectDef.periodic is absent
  on 18 of 22 effects. Facet effectsAppend Window ops omit setupClass. Facets use `combo` with comboTable and
  `resource.resource` ops, which are not in the sim union. Facet shapes use `width`. The C# loader accepts the JSON as
  it is. WHY it matters: phase 2 ports the RUNTIME behaviour the sim actually executes, not the TS declarations; the
  TS types are not a reliable spec for the data. Parity is judged against the oracle traces (sim/ORACLE.md), not the types.
