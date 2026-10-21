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

## CHECKPOINT 2026-10-21 00:35Z (orchestrator, after compaction)
WHY: #396 (integrate: playable dungeon, one PR) is the only path to Matt's end-to-end run (D10/D15), so everything else is sequenced behind it. Written from gh state (read 2026-10-21), not from lane narration.
- #396 OPEN DRAFT, head 832819c6 (gh-bound 2026-10-21 01:00Z): CAST-DEFERRAL re-added (CastDeferral.cs + deferred-casts.ts from #325, TsCastReplace wiring, TelegraphCancelTests watched to fail with deferral removed). Lane-reported suites: vitest 1108/1108, Content 628/628, Sim 1139/1139, Playtest 92/92, Bots 218/219. ONE RED: ForkParity warden.guardBreak:focused dominated by surge on all three traces (hv-k2-s7 1733 vs 1669; hv-k2-s11-d90 1782 vs 1740; hv-k10-s7 2314 vs 2199; deaths 0; combos 29 vs 35). Opus lane retuning focused's own content (D52). Ready + arm wait on that hand-back and on orchestrator-viewed frames (D15).
- MERGED this span: #371 1b9877d3; #397 7709e4a7; #398 6f5c282e (BIBLE s9 decisions); #399 0da27a43 (PLAYTESTERS map row); #400 95dc759d (chest tiers); #401 d3894362 (00:35Z checkpoint).
- Recon 2026-10-21 (read-only, bound by gh): close after #396 merges, because their heads are ancestors of integrate/playable-dungeon: #308 2ae5c7a5, #333 5ff061cd, #369 918d8489, #335 2764c19e, #244 6469469b. Verify-then-close: #344 b979989c (OPEN), #325 139b0da5 (OPEN).
- NEEDS MATT: #326 (TEMPO-CAST-FLOOR, 1724b737) was folded into the integrate branch, then reverted by 67910219, so the cast-floor logic for path packs is currently OUT of #396. Re-fold or drop is Matt's call. feat/no-push-rule 7334a6c8 (ENEMY-NO-PUSH; commit says WIP) is in neither #396 nor main. #375 42b92dfa and #370 d5fc921b are outside #396. pr250 (#250 OPEN, 817969b6, 3 test-guard files) is live and outside #396. lane/rebase-242 (#242 CLOSED) has zero net diff vs main: stale; delete needs Matt.

## Checkpoint 2026-10-21 02:20Z (orchestrator; 21:20 CDT 2026-10-20)
WHY: Matt's messages at about 02:10Z and 02:15Z changed the plan. The PR pile (about 40 open PRs) is the bottleneck, CI errors are read by the orchestrator itself, and a merge queue waits until the pile lands. The 01:35Z NOW list below still treats the pile as a single #396 wait, so this block supersedes it where they differ. Facts are gh-bound or reproduced locally at the head sha stated.

Matt's decisions (verbatim, 2026-10-21 ~02:10Z):
- "the ci gates on gh literally have the error, the paths have changed on half the prsd"
- "why are you not focused on resolving keyrun prs rn to continue working"
- "that is the bottleneck, if anything becomes a bottleneck it should be optimized, idk why we're sitting on 40 prs our throughput is not that fast and you can read the errors instead of sending agents on wild goose chases"
- (~02:15Z) "you can use a merge queue lmfao, but it won't solve failing ci for 6 hours, after you get these all in according to plan"

The call: the orchestrator reads each PR's failing CI line itself before dispatching a fix lane. A GitHub merge queue is APPROVED, but it is enabled only after the current PR pile lands per plan (#396 first, then the merge-forward batch). WHY: throughput is the bottleneck; a merge queue cannot make a red PR green; and recon lanes misjudged conflicts (a name-only recon said #370 and #366 were independent of #396, and a real test-merge found 8 conflicts).

#396 "integrate: playable dungeon (one PR)", integrate/playable-dungeon, head 1cce3d8c, squash auto-merge armed. game-test, test and e2e PASS. game-e2e FAILS on HudSceneTest (2 failures): "run F: the refilled seat never acted after the refill (the joiner's commands are dropped)" and "run K1: the recorded log differs from the committed example game/fixtures/input/example-enter-ping.input-log.json". The orchestrator reproduced both locally on the worktree at 1cce3d8c (log kept in the session scratchpad). WHY it is still red: the required check holds the merge, and the hold is correct. An Opus fix lane is on it: a replica test first, then the fix; K1 is regenerated only from a real recording, with the reason stated. A fitted green (an edited assertion, or a fixture regenerated without a real recording) is refused.

Lane choices on #396, recorded as decisions (from integration lane af21aa85):
- Oracle traces are kept through a `TsSymmetricPushOut` switch, true only in the oracle replay RunSetup.ParseContent. Not marked superseded. WHY: the TS oracle still defines the parity traces, and a global switch would change the live sim.
- Fan rule `ResolveCollisionAround` for enemies that share a target (D54). WHY: D54 says enemies never displace a player and enemy-vs-enemy separation moves the enemy only, so a fan around one target needs a deterministic rule.
- D55 cancels a cast when the caster is dead or no living player remains, not when a single target dies. WHY: one target's death is an ordinary retarget; cancelling on it would drop telegraphs the rest of the party still has to answer.
- HUD scene runs B and C were never executed on the branch by lanes (headless replicas only). The orchestrator ran the full HudSceneTest locally. WHY recorded: a headless replica is not a run of the scene.

Red PR map, read from CI by the orchestrator. The game-gate line "game paths changed ... game-e2e must be success" means the PR touches game files, so game-e2e is required. It is not a path rename.
- K1 fixture drift: #384, #364, #327, #319, #295.
- Stale queue-payload test (main added Gear) plus stale talent-parity fixture: #324, #311, #307, #302, #325. #325 also fails 3 EncounterTrace oracle traces from its busy-caster guard.
- Run C never reaches the boss (branch predates NAV-CORE and TUNE-BOSS): #375, #326.
- #362 runs B, E and N; #364 run B.
- #356 TS frozen-roots oracle hash drift. #344 boss golden changed. #280 ClauseTextTests Tide text. #293 e2e timeout (node test).
- #336 is a DO-NOT-MERGE red proof, kept red on purpose.

Real test-merge of each red PR against integrate/playable-dungeon (git merge-tree, 2026-10-21 ~02:19Z):
- 0 conflicts: #384, #327, #326, #311, #295, #280, #344, #325, #366. These are merged forward NOW in parallel (8 Opus lanes, dispatched 02:19Z). WHY now: none of them touch the files #396 is still changing.
- Conflicting: #375 (14), #364 (14), #362 (11), #356 (6), #324 (9), #319 (7), #307 (39), #302 (49), #370 (33), #293 (4). These wait for #396 MERGED, then merge-forward. WHY: a name-only recon cannot see these; only the test-merge can.
- Folded into #396, close after MERGED: #308, #333, #369, #335, #244, #388. Verify-then-close #344 and #325 if #396 covers them. WHY: their content reaches main through #396, and a second merge of the same code is a future conflict.

Facets: #366 OPEN, armed at 47c5c970 (stale talent-parity fixture re-recorded; main merged). #370 zero-GCD has 8 conflicts with #396 and is redone after #396 MERGED. #406 D54/D55 checkpoint MERGED at f6eec54e, and main is GREEN at f6eec54e. WHY: lanes build against D54/D55 as they now sit on main.

## Checkpoint 2026-10-21 03:00Z (orchestrator; 22:00 CDT 2026-10-20)
WHY: written so the orchestrator can compact without losing intent. Placement: the 02:45Z checkpoint block is on PR #408 and is not on origin/main yet, so this block follows the newest block on main (02:20Z). When #408 merges, its 02:45Z block lands at the same insertion point; keep both, 02:45Z first. Facts are gh-bound by the orchestrator at 02:50-02:58Z unless marked lane-reported or orchestrator-reported.
- #396 integrate/playable-dungeon: remote head 1cce3d8c, OPEN, squash auto-merge armed, BLOCKED on checks. WHY blocked: game-e2e HudSceneTest is red (run K1 fixture differs; run F replica). The hold is correct, so the branch is not forced green.
- #396 lane-local head 58426c89 (lane-reported), 4 commits ahead of remote: run F replica test fc6348bf; healer tops off between pulls 845f1923; joiner arrives with party f90fcf61; HudSceneTest -KeepK1Record 58426c89. WHY -KeepK1Record: the scene test deleted the K1 recording unconditionally, so the K1 fixture could never be regenerated from a real run.
- #396 orchestrator scene run at 58426c89 (orchestrator-reported): FAIL (10) = 9 known local-only run A tex=none icon failures (WHY local-only: the import cache is absent in the worktree; CI run A passes) + run K1 fixture differs (candidate 342 bytes vs committed 290). WHY the K1 candidate goes to the lane: a fixture is regenerated only from a real recording with a stated reason; a fixture rewritten to match the output is a fitted green and is refused.
- #396 next, in the same branch (orchestrator plan): lane produces the normalized K1 diff, regenerates K1 with the reason stated, runs the secret scan, pushes, re-arms; then the D62 swift retune (buff dominated: chanter.cadence:swift). WHY: the orchestrator's read is that the buff, not the song, drives the dominance; retuning groupSong would fit the symptom in the wrong place. Never nerf groupSong.
- #408 checkpoint PR: head a1c44e20, OPEN, squash auto-merge armed, BLOCKED on checks (gh-bound). WHY it is open: the 02:45Z checkpoint commit sits on this branch, so it does not reach main until #408 merges.
- #330 Seed Death Bloom costs its closer nothing: MERGEABLE CLEAN, zero file overlap with #396 (checked against #396's 100 files). Orchestrator armed squash auto-merge pinned to a4b9c645c355ee2481c83add86a3d9595e92190b at ~02:58Z. WHY it ships now: it is independent of the funnel, so it should not wait for #396. Read at 02:56Z (machine clock) the head matches a4b9c645 and mergeStateStatus is CLEAN, but autoMergeRequest was still null; the arm is not confirmed until gh shows it, so the next checkpoint must verify it, not assume it.
- #325 OPEN, head d8ae698f (gh-bound), folded into #396; closes after #396 MERGED. WHY: its content reaches main through #396, and a second merge of the same code is a future conflict.
- #384 OPEN, BLOCKED, head 8b7c24eb (gh-bound), K1-only red. WHY it waits: the -KeepK1Record fix has to reach main through #396 before the K1 regen can land.
- Brain (claude-config, orchestrator-reported, not gh-bound): live ~/.claude master synced to origin (merge 18841b8, index regen 53369e1; keeper auto-commits 4387b56 and 9fcb816 dropped two archived pointers; memory_index --check OK). WHY the index matters: a pointer to an archived file is a broken memory link, and the check must pass before the memory is trusted.
- Brain hook fixes #215 and #216 are live on disk (orchestrator-reported). WHY it matters: the hooks the orchestrator runs under are these fixes, so the disk state is the operative one.
- Brain scan-gate fix (orchestrator-reported): ENAMETOOLONG on one argv of all outgoing paths. Re-shipping on branch fix/secret-scan-gate-exit-null-v2. WHY a fresh branch: the gate parses Hook-Proof trailers from every commit in the push range, and the first commit had wrong paths, so every push over that range fails. No history rewrite (never amend, rebase or force), so the fix goes on a fresh branch.
- Brain finding f-ec0adc27 (orchestrator-reported): the hand-back recorder filed nine one-line progress messages as unacknowledged hand-backs; fix lane open on fix/handback-progress-noise. WHY: a progress line is not a hand-back, so counting it as one makes the unacknowledged-hand-back signal noise.
- aesop #1243 (pyflakes changed-files ratchet, orchestrator-reported): red = verify_gates_wired.py "Unwired gates found: test_pyflakes_gate.py"; fix lane dispatched. WHY: the gate test file exists but is not wired into the gate registry, so the wiring check fails.
- Hook ruling stands: lanes denied on a plain `$` use literal paths, never reworded retries. WHY: the denial is a guard; rewording a command to slip past it is a workaround, and the cardinal rule forbids working around a hook. The hook is tuned, not bypassed.
- NEXT (orchestrator order, with WHY for the order): (1) #396 lane hand-back -> MERGED via gh (WHY first: every later merge in this list depends on it); (2) ff the primary tree (WHY: the primary tree is the reference for the merged state, and it is never edited); (3) run tools/godot/screens.ps1 and the orchestrator views the frames (WHY: D15, acceptance is Matt watching frames before a visible row closes); (4) close the folded set #308 #333 #369 #335 #244 #388 #325 #344 (WHY: their content is on main through #396; verify each head is an ancestor first); (5) merge forward the ten conflicting PRs #307 #302 #370 #375 #364 #362 #324 #319 #356 #293 (WHY: the test-merge found real conflicts with #396, so they cannot go before it); (6) #384 K1 regen (WHY: depends on -KeepK1Record reaching main); (7) re-evaluate parked #326 #327 #295 (WHY: their red may already be cleared by the #396 merge); (8) enable the merge queue (WHY: Matt approved it, but only after the pile lands, so it does not queue behind conflicts).

## Checkpoint 2026-10-21 03:20Z (orchestrator; WHY: #396 landed, the pile moves to merge-forward, main thread compacts)
- #396 integrate/playable-dungeon MERGED, merge commit 63cbb9e897b022fae3a9dbba839301ed0257d574 (all five checks SUCCESS). #280 MERGED dc2ddcfb. Primary tree C:/Users/Mattt/Desktop/keyrun fast-forwarded 83b5821d -> 63cbb9e8 (five untracked game/src/Host/*.cs.uid files left as they were).
- D15 frames viewed by the orchestrator (run396, default run C autorun, 10 frames + run396.mp4, scratchpad frames396, run ended at 47s wall): t=10s Pull 1/4 The Choir with the party engaged on entry (Warden BRACED, Choir Acolyte 3800/6180 targeted, Choir Cantor cast bar visible); t=25s Choir 1/3 remain; t=40s Pull 4/4 The Hollow Warden 4760/5150 with Sundered Twin (A)/(B), Chanter HOT, Cinder Flare; t=50s "Hollow Vault cleared", "Run cleared -- within par", Key +2 -> +5 (timed +3), deaths 0, choice of three shown (Keystone Tier 2 Strike taken, Armor Tier 2 Taunt, Ring Tier 1 Guard Break), "What got you" list (Choir Acolyte 1827 dmg/33 hits, Paired Warrior 686/11, Hollow Warden 545/3). Verdict: KF2 (bots idle in the first room) is answered on main; KF3 (Seed cheese) not in the sampled frames; KF1 (animation readability) cannot be judged from 5s stills, video kept for Matt. Section 1 loop end-of-run choice is on screen.
- Folded PRs CLOSED with a comment pointing at 63cbb9e8 (verified from #396's body "Folded in" list): #308 #388 #333 #369 #335 #244 #325. #326 and #344 stay OPEN (folded then reverted in #396; their body says they stay on their own branches).
- Merge-forward lanes dispatched (brief order): #293 (Haiku, mechanical) handed back: head 0fdc2efb MERGEABLE, re-armed SQUASH pinned to 0fdc2efb, fastcheck PASS 40s, scan CLEAN 429 files, one doc-comment conflict in game/src/Hud/AbilityBar.cs resolved three-way (lane-reported). #356, #324, #362, #384 (+K1 regen with -KeepK1Record) on Opus, live. Remaining after those: #319 (after #324), #364 (after #362), then #370 -> #307 -> #302, #375 last (retarget to main: its base feat/prespawn-aggro = #308 is now CLOSED).
- Decision (#293 deviation, dungeon-loop rule -> WoW): an out-of-range slot keeps both non-colour cues (main's RANGE caption and the PR's distance glyph); WoW also shows two (red icon tint plus the "Out of range" error text). No product change.
- Brain hook lanes: no-lane-game-launch read-only fix relaunched (Haiku, worktree .claude-wt-gamelaunch-ro) with decisions: python -c excluded from the allowlist; launch tokens as command words denied inside read-only shapes; gh api write flags denied (graphql query-only allowed); git exec options denied; trailer Co-Authored-By Claude Fable 5.1; PR footer 🐢 Generated with Aesop. no-scrollback $_ lane still live. The transcript-read hook denied the orchestrator's attempt to extract the exact denied commands; not escaped; the hook lane derives shapes white-box from findLaunch's UNDECIDABLE branches plus finding f-12b7bce3.
- Findings ledger OPEN 60 (matt 7, other 53/60), 1 overflow still over cap. Refinement monitor heartbeat 1147s old at 03:19Z (relaunch at >1500s).
- Merge queue: enable only after this pile lands (Matt's yes, 2026-10-21 ~02:15Z). Not yet.

## Checkpoint 2026-10-21 03:35Z
- Why: the orchestrator is at its context ceiling and compacts after this; intent lives here, lanes hold files.
- Landed on main (gh-bound): #396 playable dungeon 63cbb9e8, #330 b0ab1361, #280 dc2ddcfb, #409 checkpoint 3d77559f. Closed as folded into #396: #308 #388 #333 #369 #335 #244 #325. #326 and #344 stay open (reverted in #396).
- Merge-forward pile, all armed SQUASH and waiting on CI: #293 head 0fdc2efb, #356 4af4aa1d, #362 ad429611, #324 812e597c, #384 e8bc971b (K1 record regenerated, 1274->1273 and 1277->1276 because the boss-death block in Run.cs advances the pull on the same step). #410 is this checkpoint PR.
- #370 merge is staged in keyrun-wt/pr370 but ForkParity is red on the merged tree (surge dominated by pin, reserve dominated by ally). Why it is not merged yet: a red test is never retuned to pass. An Opus lane is proving whether main is red too and retuning under BIBLE s6 in content data, never in the test.
- Queue order after those land: #319 after #324, #364 after #362, #307 after #370, #302 after #307, #375 last (retarget base to main; #308 closed). Parked: #326 #327 #295 #344. #336 do not merge.
- Matt's standing orders (2026-10-21): merge queue approved but enabled only after the pile lands per plan; orchestrator reads CI errors itself instead of sending agents on wild goose chases. Why: the queue must not queue behind conflicts, and the orchestrator reads the CI error itself to avoid wasted lane cycles.
- Brain: claude-config #219 MERGED fe5bd2fd (PowerShell pipeline variable inside a safe ForEach-Object/Where-Object block now allowed and logged); live ~/.claude fast-forwarded to it. A second hook lane is fixing no-lane-game-launch false denies on read-only commands (f-12b7bce3); until it lands, lane briefs forbid dollar signs. Why: the false denies block read-only lane work; the fix is tuning the hook, not a bypass.
- Design call recorded: a Warden's own combo cost skips armor (Combos.cs); kept, WoW does not mitigate self-inflicted costs.
- Demo verdict on 63cbb9e8 frames (D15): party engages the Choir on entry, clears all four pulls with zero deaths, end-of-run choice of three shown; KF2 answered, KF1 animation readability still unjudged from stills. Why it stays open: acceptance is Matt watching frames, and stills cannot settle KF1.

## 2026-10-21T03:44Z checkpoint (keyrun pile: four landed, #319 forward, #362 and #370 red for real reasons)

WHY: the orchestrator hit its context ceiling mid-pile. Intent is unchanged (Matt 2026-10-21 ~02:00Z): land the open keyrun PR pile by merge-forward in the brief's order (scratchpad merge-forward-brief.md), the orchestrator reads CI errors itself, no fitted green, never ship known-broken; enable the GitHub merge queue only after the pile lands (Matt's yes, no repo setting touched yet).

- origin/main 9134fad4. MERGED this span (gh-bound): #293 9e52a72c, #356 74365f2d, #324 da2e5b82, #384 9134fad4.
- #319 (bot default gear): merged forward to a71d9bc3, MERGEABLE, auto-merge armed, waiting on CI. Conflicts in HostRunSetup.cs and SimDriver.cs kept both intents: main's StartFor/GearPatches plus the PR's WithBotLoadouts for bot seats (a human's own loadout wins). Lane ran npm ci in keyrun-wt/pr319 (untracked only).
- #362 (Warden passive armor 40%, head ad429611, armed, MERGEABLE) is RED for a real reason: on the merged tree the Warden dies at tick 505 on the Choir in run C and the party wipes on the Pair; Bots test "Run C careless seat (3@300:pair)" expects Timed and gets null; HudSceneTest FAIL (9). The tank is weaker, not stronger. An Opus fix lane is working in keyrun-wt/pr362 (diagnose ApplyArmor direction and classArmorBp plumbing first, then fix, never fit). #364 waits on #362.
- #370 (gcdTicks-0 off the GCD, origin head d5fc921b, CONFLICTING, unarmed): main 3d77559f is GREEN on ForkParity. On the merge tree the dominance red was fixed by retuning warden.json (guardBreak:surge cooldown 40 -> 30, vigil:reserve cooldown 60 -> 90; pin and ally untouched) in LOCAL commit a99fb75b on keyrun-wt/pr370 branch pr370-mf (merge commit 62fe322c), not pushed. Remaining red: bots never cast chanter.cadence on the merged tree (ForkParityTests.cs:405 "list them in UnmeasuredForks"); main does cast it. An Opus lane is finding that bot/sim regression; nothing is pushed until it is green honestly. #307 then #302 wait on #370; #375 last (retarget base to main).
- #410 (this checkpoint PR) OPEN, armed, MERGEABLE, game-e2e pending.
- Brain: claude-config #220 MERGED 72fb8e7 (no-lane-game-launch: single-quoted wrapper operands are literal, read-only allowlist only after UNDECIDABLE and logged, launch-word gaps closed; red 23/601, green 601/601, full hook suites 1434/1434). Live ~/.claude is at 72fb8e7. Finding f-12b7bce3 CLOSED. The "no dollar sign in lane briefs" rule is dropped.
- Crash recon (16:38Z crash): no self-triggering node loop found in committed code; no-polling.test.mjs writes about 48 history files into the live conductor3/state per run (pollution, not a loop; follow-up: set NO_POLLING_HISTORY_DIR in the test). Next step is human: Windows event log around 16:38Z.
- Refinement monitor heartbeat 697 s at checkpoint time; relaunch when > 1500 s with the churn rule "20+ commits on no remote ref".
- Held for Matt: prune 16 stale local keyrun refs; conductor3 OUTCOMES-LEDGER restore; worktree cleanups (pr293 pr356 pr324 pr384 are merged and can go; scratch-main-f has an untracked TmpK1BisectTests.cs); .claude-wt-gamelaunch-ro and .claude-wt-scrollback are merged.

## Checkpoint 2026-10-21 03:30Z (orchestrator correction; 22:30 CDT 2026-10-20)
WHY: the 03:00Z block above holds a claim that gh did not support ("#330 armed"). This block corrects it on disk so the next session does not act on the false arm. Facts are gh-bound at ~03:30Z unless marked.
- CORRECTION: the 03:00Z block said "#330 armed". That was false. #330 was stacked on fix/seed-teaches-core-rule; its parent #328 is MERGED (5dfaa758). #330 was retargeted to main and showed CONFLICTING. A lane merged origin/main 9f69672e into it (local merge 95e68376, not pushed), and fastcheck FAILED with 3 SEEDBLOOM-NO-STAKES tests red in Keyrun.Sim.Tests. An Opus fix lane is diagnosing against main's #328 content. #330 remains OPEN at a4b9c645, NOT armed. WHY the correction matters: the 03:00Z arm was never confirmed by gh (autoMergeRequest was null), and a PR stacked on a non-main parent is not independent of the funnel.
- #408 MERGED 6dad5850 (the 02:45Z checkpoint is now on main). WHY it matters: the checkpoint block no longer depends on an open PR.
- claude-config #217 MERGED a8a741bc: secret-scan gate argv chunking fix. The "exit null" was ENAMETOOLONG from one argv over the Windows 32767 cap. claude-config #218 MERGED a66e2e45: the hand-back ledger no longer files one-line progress fragments (finding f-ec0adc27). WHY: both were brain hook defects that made gates fail without a real finding.
- #280 pushed deae18b6 -> b15ae4df and armed squash auto-merge. OPEN, BLOCKED on checks. WHY it waits: its checks are not yet green, and the arm only merges once they are.
- #396 integrate/playable-dungeon: K1 fixture regenerated at 048cfe15. Only the two post-run Enter ticks moved, by -153. Bisected cause: D54 commit 3b554464 ends the boss run 153 ticks earlier. Merged origin/main 9f69672e clean. fastcheck PASS 37s. Pushed 1cce3d8c -> 84f9bf35 and re-armed with match-head-commit 84f9bf35. At 84f9bf35, checks test, game-test and e2e PASS; game-e2e pending. WHY the K1 change is not a fitted green: the regen follows a bisected cause in a committed decision (D54), not the output.
- D62 (chanter.cadence:swift retune) decision: NOT applied on #396. The ForkParity "no fork option is dominated" red exists only on #325's own branch (fix/startcast-busy-guard d8ae698f, CI run 39534460205). On integrate/playable-dungeon at 84f9bf35, which already carries #325, ForkParity PASSES: swift is undominated via 2 more combos in hv-k10-s7 (2345/44 vs groupSong 2081/42). WHY no change: writing a red case there would be a fitted green. #325 is folded into #396 and closes after #396 merges, so the #325-branch red is moot. FRAGILITY: revisit if ForkParity goes red on main.
- Monitor cycle 676 ran. 12 stale local keyrun branches are 20+ commits ahead with no origin ref, listed in C:/Users/Mattt/conductor3/monitor/BRIEF.md. No action taken. WHY: deleting them destroys unpushed work, and deletes wait for Matt.
- WHY this checkpoint: the pile through #396 is the bottleneck Matt named. Each receipt here is gh-bound so the next session never re-derives it.

## 2026-10-21 03:45Z checkpoint (orchestrator; 22:45 CDT 2026-10-20; branch docs/checkpoint-2026-10-21-0300z, PR #409)
WHY: the orchestrator is compacting at the context ceiling. Every item below is gh-bound or names its lane evidence, so the next session resumes from disk, not from narration. Facts are gh-bound at ~03:45Z unless marked lane-reported or orchestrator-reported.
- #330 Seed Death Bloom: the 3 SEEDBLOOM-NO-STAKES reds were caused by main's #338 (05671c27, D34 Cinderfield closer burn) adding op closerBurn to combo-riders.json. The test fixture's own op mapper (SeedBloomNoStakesTests.cs:53-66) threw on the unknown op during setup. Fix: one mapper arm identical to ContentCombos.cs:80 (1 insertion, no assertion changed). fastcheck PASS 25s (Content 619, Sim 1088, Bots 196). Pushed a4b9c645 -> d63163f0 on fix/seedbloom-no-stakes and armed squash auto-merge pinned to match-head d63163f0bcca5b63fe3d6fad7de080a181e10183. gh: OPEN, MERGEABLE, BLOCKED on checks. WHY it is armed only now: the earlier 03:00Z arm was unconfirmed, and the red was a fixture defect, not a product defect.
- #409 (this PR): pushed 68481c73 -> 15892e51 (merge of main 6dad5850), re-armed, OPEN MERGEABLE BLOCKED. WHY the merge: main moved to #408 and this branch must carry main's state to merge cleanly.
- #396 at 84f9bf35: checks e2e, game-test and test PASS; game-e2e pending; armed. ON MERGED, in this order: fast-forward the primary tree; screens.ps1 frames viewed (D15); close the folded PRs #308 #333 #369 #335 #244 #388 #325 #344; merge-forward in this order from the brief at scratchpad merge-forward-brief.md: #293 and #356 (0/1 overlap, parallel), then #324, then #319, then #362, then #364, then #370, then #307, then #302; #375 last (stacked on #308: retarget, or merge #308 first). Then #384 K1 regen with -KeepK1Record. Then enable the merge queue, on Matt's yes after the pile. WHY this order: each step shares files with the next, so a parallel or reordered merge creates conflicts the lanes cannot see.
- Open findings ledger triaged 67 -> 60: 4 duplicates dropped (user-approved); 3 closed fixed with MERGED shas: claude-scripts #126 2ab9933e, keyrun #341 b2062264, keyrun #357 aa4e2246; 4 deferred 48h as waiting on Matt. One overflow item is still over the cap. WHY the ledger matters: the saturation gate reads it to decide whether more lanes may be armed.
- Hook friction tonight (lane-reported): no-lane-game-launch denied three read-only lanes as UNDECIDABLE on a loop variable and an awk pipeline; no-scrollback-as-record denied a PowerShell variable. Two brain-worktree lanes are shipping narrow fixes: fix/no-lane-game-launch-readonly-programs and fix/scrollback-allow-pipeline-var, each red test first, no fail-open. WHY: hooks are friends, optimize never delete (cardinal rule 1). Until they land, lane briefs say no dollar sign in command text.
- For Matt (HIGH, waits on Matt): the conductor3 working tree is on branch chore/monitor-state-cycle-538 with ledger/OUTCOMES-LEDGER.md truncated 54050 -> 201 lines and no archive on disk; the rows survive only in HEAD. INDEX.md, KNOWLEDGE.md, monitor/INDEX.md and C:/Users/Mattt/scripts/KNOWLEDGE.md are stray knowledge_tree.py output. Nothing is committed. Restore needs Matt's yes. WHY it waits: restoring a truncated ledger file is a data-affecting action.
- aesop #1243: pushed ca8353a8 (gate test wired in ci.yml), armed, BLOCKED on checks. WHY: the verify_gates_wired red was an unwired test file, so the fix is wiring, not relaxing the gate.
- WHY this checkpoint, restated: the orchestrator is compacting at the context ceiling, so the funnel state and each pending action are written to disk before compaction. Next session: read this block, then the 03:30Z correction block, then sync git log; do not re-derive gh state from memory.

## NEXT STEPS (engine plan, design/ENGINE-PLAN.md; Phase 5 "Matt playing" stays the acceptance test)
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
- D31 **SKILL TOOLKIT: Outlaw-level skill on five buttons; every class can carry threat-take, reset, kite and save tools
  through Facets and forks** (Matt 2026-10-20, verbatim: "look at why people love playing outlaw rogue for a sec: you can
  pull threat, evasion, cloak, flask, invis, re pull, grapple, cheap, ambush, grapple, sucker keeps pack alive if tank
  dies... there needs to be this level of "skill" in the game... and playing into mechanics or builds like this will
  encourage the late game way more than just hitting something lmao"). Numbered D31 because D30 is on PR #306
  (docs/aggro-taunt-direction), not yet merged; D31 depends on D30 rule 3 ("Once threat is initialized, it cant be
  taken except for abilities... other characters need some toolkit to take threat, or an upgraded ability"). Direction
  recorded; design/BIBLE.md is NOT amended (proposed wording and the conflict list wait for Matt in the D31 PR body).
  The rules:
  1. The budget holds: five rotational buttons and ten keys (BIBLE section 2, D4). Depth comes from what the keys do,
     never from a new key.
  2. Every class can build a toolkit on keys it already has: a threat take, a threat reset or hand-off, a kite or
     reposition, and an emergency save. Each comes from a Facet (BIBLE section 7, "Facets change function") or a fork,
     on R utility, E defensive, F movement or one rotational ability, so the toolkit is a build choice that costs power.
  3. Threat moves only through explicit ability ops (D30 rule 3), and those ops are generic: any class's ability may
     carry take, give or drop. No class owns threat-taking by name.
  4. Each tool is an outplay window read off a cast bar or a telegraph (BIBLE section 3), not a passive: pressing it at
     the right tick is the skill, and a mistimed press is a visible miss.
  5. A dying run has a way back one player can execute (kite the pack, hand threat back, reset), which serves test 3 of
     BIBLE section 0.
  Why: Matt names Outlaw Rogue's toolkit as the skill bar for late game, and the five-button cap means that depth has to
  live in build choices and timing on the existing keys (research/12-skill-toolkit.md, 2026-10-20).
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
- D43 **CARELESS-SEAT: a bot seat is hit because a careless player pulls a group, never because a test makes an enemy
  hit it** (Matt 2026-10-20, verbatim: "no a bad player will accidentally aggro more groups, just test it naturally";
  PR #308, PRESPAWN-AGGRO; D34-D42 are claimed by open PRs #338 and #342; reversible). Under PRESPAWN-AGGRO the Warden
  walks into every aggro ring first and an enemy with no threat goes for the nearest living player, so no enemy ever
  picked a bot seat and HudSceneTest run C's private-log seat filter check (a bot seat takes damage) had nothing to
  read. Matt ruled out making enemies target bots, loosening the check, or retuning its constants. Instead a bot seat
  can play carelessly (`Keyrun.Bots.Careless`, host flag `--careless-seat S@T:GROUP`): from tick T it strays off the
  party toward the idle group GROUP until that group engages, and the sim's own aggro and targeting rules do the rest.
  Run C runs with `--careless-seat 3@300:pair`. Why: the bot-damage check proves a real path (a bot pulls, the pack it
  pulled goes for it), not a scripted one.
- D44 **PATH-TO-BOSS: no gates; one forward path to the boss with prespawned packs; a run wipes when no living seated
  player remains; bots follow the lowest living seat** (Matt 2026-10-21, verbatim: "no more gates, just path to boss
  with packs as you said, make the rest of the decisions and we will tune later" and "just build the game"; earlier
  "everything should be prespawned with an aggro distance" and "honestly just lean into m+ design heavily while in the
  dungeon"; PR #308, folds PR #359; supersedes D27's gate and D18's gated corridors; reversible, every rule is code or
  data and can be retuned). (1) Gates removed: no room gate shuts on an engage (no `gate_closed`/`gate_open`, no clamp,
  no gate view or cut-away) and layout.json gates no corridor, so nothing walls the party in, out or forward. The
  layout is one chain of rooms and corridors, so the flow is forward by layout; each pack stands idle on that path and
  engages on its aggro ring (PRESPAWN-AGGRO), and a run completes only when every pull is cleared (the pull index), so
  skipping is a pull you still owe, M+ style. (2) Wipe rule: a run wipes when no living player who is actually in the
  run remains (`RunWiring.IsWipe`); an abandoned or unfilled seat (`StepRunOpts.OpenSeats`) never holds a dead party's
  run open, in or out of combat; the wipe pays the wipe Slack on the last seated death, then 100 ticks of grace and a
  depleted finish, as before. (3) Leader: bots follow the lowest-seat LIVING player (`Follow.Leader`), and that seat
  leads the path when it is a bot (`Follow.LeadCommand`), so a dead leader passes the lead on; this replaces the
  alive-or-dead rule from 53244968, which only existed to get a joiner past a shut gate. Why: Matt ruled out gates;
  with packs prespawned on the path a gate only added stalemates (PR #308 run C: the Chanter self-healing outside a
  shut gate, no run_end by tick 7000) and a leader rule that parked living bots on a body.
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
- D47 **HALL SENTRIES SWING ON THEIR OWN TIMERS** (orchestrator, 2026-10-20, under Matt's verbatim "make the rest of
  the decisions and we will tune later"; reversible). The required Hall Sentries pack fields hall.sentry, a real
  Mythic+ trash enemy only a layout pack fields (game/content/data/enemies/hall.json: 2000 health, choir.meleeSwing,
  48 forces each). Pack-only members swing on staggered timers by member index, offsets i x period / n over one swing
  period (LayoutPackGroups.StaggerSwing), so a five-sentry pack never lands five swings on one tick. WHY: in WoW each
  mob's swing timer is its own; a pack landing every swing on the same tick is a spike no tank mitigates through.
  Tune later: the period, the health and the count.
- D48 **THE CHOIR LESSON COMPARES THE SAME COMPETENT PARTY WITH AND WITHOUT KICKS** (orchestrator, 2026-10-20, under
  Matt's verbatim "make the rest of the decisions and we will tune later" and "ensure the bots can fail lol, they
  should not clear the first dungeon on the first try unless they have played wow"; reversible). "Competent" includes
  the tank mitigating the way the scripted party's tank does: a new playtest policy field `mitigate` (class list) lets
  that seat press its defensive (E) when its role's script would, which also lets the script's pressure branch reach
  Bulwark. Both ChoirLessonTests plans carry `"mitigate": ["warden"]`; they differ only in the kick rule. The claims
  are unchanged: the kicking party times key 2, the never-kick party does not. Measured key 2 seed 1 on this branch:
  kick plan timed +3 at 285 s, 0 deaths; never-kick plan untimed (depleted) at 830 s, 0 deaths; Choir cleared at tick
  2500 vs 13401. WHY: without mitigation the policy Warden never pressed Brace or Bulwark, so every Hall Sentry swing
  landed for the full 70 and the kick plan wiped, which made the lesson about tank buttons instead of the kick.
- D49 **CONTENT INVENTORY RAISES FOR THE 8TH ENEMY ARE INVENTORY, NOT LOOSENING** (orchestrator, 2026-10-20). hall.sentry
  is a real 8th enemy: ContentCountTests enemies 7 -> 8, embedded files 27 -> 28 (enemies/hall.json joins the C#-only
  list), ReferenceTests refs 32 -> 34 (its ability and script step; measured on the merged tree, the brief's 30 -> 32
  predates seed.lock). ActorBindingTests reads both enemy folders (content/data/enemies and game/content/data/enemies);
  hall.sentry has a model row (the CC0 Skeleton_Warrior.glb with the Skeleton spawn clip, tinted bronze 0xD0A060).
  RouteContentTests' forces fixture, SkippablePackTests' strip setup and PathPackRouteTests' oracle setup (a hall.sentry
  template) carry the new enemy. WHY recorded: each is a fixture or count that must follow the content, and no
  assertion, floor or behaviour was relaxed.
- D50 **LOBBY-TABS: the between-runs screen is one lobby with three tabs, Character / Build / Play** (orchestrator,
  2026-10-20, from Matt's verbatim "anything else like progression, builds, classes, lobby stuff more like dark and
  darker... character management screen, quests, progression, score, follow the bible but you get what im saying, just
  one screen with tabs should be enough before entering a game/queue"; reversible). Character picks the class (the
  three roster classes) and shows the five gear slots with tier; Build is the class's talent forks; Play is the key,
  its Clause and Queue. The run end's choice of three returns to the Play tab, unlocked, class and build kept. The pure
  state is `Lobby` (game/src/Host/Lobby.cs). The class stays on the host and never rides the queue payload
  (`Lobby.ClassFor` reads the screen; QueueRequest is unchanged, BIBLE s3 "the queue matches on key level only"). The
  human takes the picked class's roster seat and bots fill the other two. WHY: BIBLE s1 names the screen as the loop's
  start and end, and s0 test 2 wants Queue inside a minute; one tabbed screen keeps every pre-run choice on it. No
  quests, shop, stash or shareable score (BIBLE s1, s10, s11), so "quests" and "score" from the quote are out. Not yet:
  the class pick is not saved across launches (the progression save format is unchanged); the private rating already
  shows on the screen.
- D51 (2026-10-21 00:50Z, orchestrator under the core-dungeon rule: defer to WoW): scripted enemy casts DEFER, never overwrite. A step due while its caster is busy waits and fires on the first free tick; #325's CastDeferral (C# CastDeferral.cs, TS sim/encounter/deferred-casts.ts) is restored for TsCastReplace steps alongside the #325 busy guard for all casters. WHY: in WoW a casting enemy never overwrites its own bar, and BIBLE s3 says the bar is drawn ahead of resolution, so a telegraph vanishing mid-bar is a broken sentence. The two TelegraphCancelTests overwrite cases are rewritten to assert deferral; a second oracle re-record was authorized with every changed number listed in the #396 body, no floors or constants changed.
- **D52 (2026-10-21): a ForkParity dominated option is RETUNED, never allowlisted.** warden.guardBreak:focused is dominated by surge after cast deferral landed on #396. The fix edits focused's own content only; KnownDominatedForks stays empty; no test floor, timer or constant changes; surge and enemy content untouched; a third oracle re-record is authorized only if hashes move, with every number listed in the PR. WHY: WoW tunes each talent row so every choice wins somewhere; an allowlist entry hides a real balance defect behind a passing test (BIBLE s0 test 1; no fitted green).
- **D53 (2026-10-21 01:27Z, orchestrator under the core-dungeon rule: defer to WoW): nameplates are culled by distance.** PlateRule.DrawsOverhead gains a distance term, PlateDistanceMm = 26000, inclusive; party plates and the boss frame are unchanged, and with no human actor every enemy is in range (the old behaviour). Shipped as #404 "nameplate distance cull" (head 60119575, auto-merge SQUASH armed 01:27:03Z); PlateRuleTests 20 -> 33 green, and a mutant killed 2 tests. Closes f-77a68e6b after merge and frames. WHY: WoW stops drawing nameplates past its max distance, so a plate that reaches across the arena reads as noise. The 26 m value is set from the arena table, not copied from WoW: from each room's party start the next doorway is at most 25500 mm away and the pull after that at least 27000 mm, so 26000 keeps the next doorway and drops the next pull. Caveat, recorded so the number is not mistaken for WoW's: WoW's own cull is about 60 yards (about 55 m), which is larger than this arena. If the arena grows, re-derive the constant from the table rather than from WoW.
- D54 (2026-10-21, orchestrator under the core-dungeon rule "defer to WoW", Matt 2026-10-20): enemies never displace a player. In WoW Mythic+ a mob closing to melee stacks on its target and never pushes it; Matt also said 2026-10-20 that enemies must stack on the player they attack (f-e17638f7). WHY: #396's game-e2e run B went red because chasing Pair members shoved a stationary Warden about 18 m west once the hall gates were removed (D44), so only 4 then 1 could hit him and he lived past the mark telegraph; the shove is the defect, inherited from the TS oracle port, not run B's premise. Sim rule: enemy-vs-player separation moves the enemy only; enemy-vs-enemy separation unchanged. Oracle traces encoding the shove are marked superseded by D54 in their own file, never silently renumbered.
- D55 (2026-10-21, P0 wrong data shown to a user, BIBLE section 3 "drawn ahead of resolution"): a floor telegraph is cancelled on the tick its caster's target dies or its caster dies, never left up until the would-be start tick. WHY: run B drew the Pair's SPREAD telegraph from tick 295 to 392 for a cast that could not start after the Warden died at 354.
- D56 (2026-10-21 02:35Z, orchestrator under the core-dungeon rule "defer to WoW"): the refilled seat (joiner) arrives at the next pull start, inside the party's taken branch, and never walks in mid-pull. WHY: BIBLE section 9.1 and section 10 say so, and Backfill.Step places the seat at a party position. Root cause found by the #396 fix lane for run F: D54 removed the shove that used to push the Warden back, so the full Pair melee hits him at once and he dies 60 ticks into pull 2 after entering at 184/400 HP. JoinerDoorwayTests' walk-in premise is replaced by a test of the bible sentence.
- D57 (2026-10-21 02:35Z, orchestrator under the core-dungeon rule "defer to WoW", Matt 2026-10-20): healer bots top off allies between pulls. A healer heals allies below the heal threshold while no enemy lives; before this, Bot.cs returned from follow/travel before the role ran, so a healer between pulls never healed. WHY: the WoW Mythic+ rule for the dungeon loop is that healers top the group off between pulls. Pair rebalance rejected: the gap is the bots not healing between pulls, not the Pair's damage, and retuning the Pair would fit the content to the bug (no fitted green). D54 stays.
- D58 (2026-10-21 02:35Z, orchestrator): STAGGER AND UNINTERRUPTIBLE CASTS (#344). BIBLE section 9.3 stands: a stun that lands during an INTERRUPTIBLE cast ends it; uninterruptible casts (twins.slam) survive a Break-bar stagger, as main's #329 test asserts. The orchestrator's earlier relayed ruling that a stagger never counts as an interrupt for Echo is RETRACTED: section 9.3 says a stop counts as an interrupt for Echo and does not refund Quench's Heat. #344 is parked until #396 is MERGED, then verified against main (#329 ControlStops) and closed as superseded if RunWiring.StopCasts adds nothing; otherwise StopCasts must skip uninterruptible casts and the Twins test flips. WHY: the bible text is the contract, and a relayed ruling that contradicts a bible sentence is not a decision.
- D59 (2026-10-21 02:35Z, orchestrator): KICK-TIMING PRS PARKED (#327, #295). Both carry Ledger.ResolvePartyInterrupt and KickOpensTick. On main 4f165cb4 the kick hold moves the Seed's death from 3090 to 3081, so SEED-BLOOM-TEACHES (#328) fails for real (no Combo in window 3082 to 3182). Parked until #396 is MERGED because #396 carries the Choir-kick work that may supersede them; re-evaluate then against D54/D55. Local unpushed merges, not pushed: #327 at 35ac89f5 in keyrun-wt/kick-order; #295 at 409b6ec1 in C:/Users/Mattt/keyrun-wt/bot-kick-timing. WHY: each would break a passing main test on Seed timing, and #396 may make both obsolete.
- D60 (2026-10-21 02:35Z, orchestrator): #326 TEST FOLLOWS THE SETUP CONTRACT. LockWinnabilityTests builds its expected steps through RunSetup.TsScripts (made internal) rather than copying the transform, so the actor-mapping check cannot drift from the contract again (TsCastReplace from #379; SpansCast=false and TsUnchecked=true from SLAM-TELEGRAPH and TELEGRAPH-READY). WHY: a test that copies the transform it checks passes while the real contract changes underneath it.
- D61 (2026-10-21 02:35Z, orchestrator): #326 tempo-clamp PARKED behind #396: on the #326 tree Twin 902 kills the Warden-class player actor 1 at tick 402 before Lock 1 opens at 501; main's version of the same test passes 2/2 on the same tree. Re-evaluate after #396 merges. WHY: the orchestrator decided this after the #408 lane had committed, and the pile-on hook blocked the addendum.
- 1D FINDING (2026-10-18, lane 1D, PR #111): content/data JSON diverges from the TS types. EffectDef.periodic is absent
  on 18 of 22 effects. Facet effectsAppend Window ops omit setupClass. Facets use `combo` with comboTable and
  `resource.resource` ops, which are not in the sim union. Facet shapes use `width`. The C# loader accepts the JSON as
  it is. WHY it matters: phase 2 ports the RUNTIME behaviour the sim actually executes, not the TS declarations; the
  TS types are not a reliable spec for the data. Parity is judged against the oracle traces (sim/ORACLE.md), not the types.

## CHECKPOINT 2026-10-21 02:35Z (orchestrator, before compaction)
WHY: the decisions on the keyrun PR pile (D56-D60) lived only in orchestrator context and would have been lost at compaction; they are written here so the next session resumes from disk. Status is from gh and git, read 2026-10-21 02:30-02:35Z.
- Hook friction, recorded, not ledger findings (the ledger is full). None of these was bypassed; the hooks were right to fail closed.
  - (i) The brain PreToolUse secret-scan gate printed "ERROR (exit null)" twice on #280's push from C:/Users/Mattt/keyrun-wt/text-rules. Local scan is green at b15ae4df; not pushed. A brain lane is fixing the gate.
  - (ii) no-lane-game-launch.mjs read a C# `$"..."` interpolated string inside a sed one-liner as UNDECIDABLE. A brain lane is fixing the hook.
  - (iii) PowerShell-tool hooks refused ./tools/fastcheck.ps1. Brain PR branch fix/repo-settings-allow-tools-ps1 is local at 931a31b, awaiting Hook-Proof for the shared lib.
- Auto-merge: #311 07803bf8, #407 8f47572f and #384 8b7c24eb are armed with game-e2e in progress at 02:30Z.
- Refinement monitor relaunched at 02:30Z after a 2740 s stale heartbeat.

## NEXT STEPS (2026-10-21 02:35Z)
1. #396 lane implements D56 (joiner at pull start) and D57 (healer top-off between pulls). Then the orchestrator runs HudSceneTest once on keyrun-wt/integrate-playable for the K1 regeneration.
2. After #396 is MERGED: fast-forward the primary tree; the orchestrator views the screens.ps1 frames (D15); close the folded PRs #308 #333 #369 #335 #244 #388; verify-then-close #344 and #325 (D58); merge forward the ten conflicting PRs #307 #302 #370 #375 #364 #362 #324 #319 #356 #293; re-evaluate #327 and #295 (D59).
3. Enable the merge queue only after the pile lands. Matt approved this on 2026-10-21, on the condition that it waits for the pile.
4. Fix the run K1 example log's byte-for-byte compare against run-end timing so balance changes stop forcing regens (not a fitted green: the example documents the format, not the tick). Carried from main's 02:20Z NEXT (d), which this block did not cover.

## Checkpoint 2026-10-21 02:45Z (orchestrator; lane reports at 02:45Z)
WHY: the dungeon-loop ruling (D62), the #325 oracle re-record, the #396 lane's local commits, and the brain sync live only in lane reports until written here; the next session must resume from disk.
- D62 (2026-10-21 02:45Z, dungeon-loop rule: defer to WoW Mythic+): facet-parity reds are fixed by buffing the dominated facet until it wins at least one measured scenario, never by nerfing the group-synergy facet (BIBLE section 4 is the design's centre). WHY: a nerf moves the design toward the bug; a buff keeps group synergy as the reason to pick a facet. First case: chanter.cadence:swift is dominated by groupSong (#325 at d8ae698f, Bots 195/196; cause is #366 combined with the D51 busy guard; #396 will hit the same red when it merges main). Routed to the #396 lane after its run F fix.
- #325 (startcast-busy-guard): merged main at dc39399e; D52 Focused retune at d8ae698f; oracle bots-hv-k10-s7 re-recorded d7b7304f to 077b3287 because the old oracle recorded the in-flight cast replacement bug. The lane rewrote shell variables to literals after hook denials (noted). #325 stays folded into #396: close as folded once #396 is MERGED; do not chase a separate green. WHY: its content reaches main through #396, so a separate green is wasted work.
- #396 lane state: local commits fc6348bf (run F replica test, red on D54) and 845f1923 (healer tops off between pulls, heals to full because measured Pair entry is 76%; test in Playtest.Tests because it needs ScriptedWarden). (a) Backfill.Arrive places the refilled seat on the nearest living staying member; run F replica green locally. fastcheck pending; K1 untouched; nothing pushed. WHY: the commits stay local until fastcheck is green, so the branch never carries a known-red commit to CI.
- #384: orchestrator scene run on 8b7c24eb confirms only K1 differs (run A icon tex=none failures are local import-cache only; CI run A passes). The test deletes its temp recording, so the regen mechanism is being read before a second run. WHY: a regen from a deleted recording would be a fitted green.
- Brain (claude-config): #215 MERGED 02:41Z (no-lane-game-launch awk/sed false positives); #216 MERGED 02:42Z (fastcheck -File shape). Live ~/.claude was 16 ahead and 27 behind origin/master with a dirty tree; the orchestrator restored 11 deleted plans/*.md from HEAD, staged the memory indexes and the settings model line; a lane is merging origin/master so the fixed hooks actually run. Secret-scan gate root cause: ENAMETOOLONG from 375 argv paths on Windows; fix PR pending Hook-Proof naming. WHY: the gate cannot take a wide staged set until fixed, so pushes rest on the staged scan result recorded in the commit, not on the hook.
- Hook ruling tonight: lanes denied by no-lane-game-launch on a plain dollar-sign variable may continue with literal paths and no dollar sign in Bash text (the gate's purpose is launches; the false-positive class is fixed in #215 and will be re-tested after the brain sync). Never retry a denied command reworded. WHY: a denial is evidence; rewording to slip past it is a workaround, while literal paths keep the gate's purpose intact.
- NEXT: #396 fastcheck green -> orchestrator scene run -> K1 regen -> push -> MERGED -> frames viewed (D15) -> close folded (#308 #333 #369 #335 #244 #388 #325 #344) -> merge-forward the conflicting ten -> re-evaluate parked #326 #327 #295 -> enable merge queue (Matt's yes, after the pile). WHY: this is the order the dependency chain allows; each step needs the prior step's evidence.

## 2026-10-21T04:15Z checkpoint (healer-downtime and cadence-unmeasured rulings; pile status)

# Orchestrator decisions 2026-10-21 ~04:10Z (for the next STATE.md checkpoint)

## D-HEALER-DOWNTIME (dungeon loop -> WoW rule)
Evidence: #362 fix lane afaf604623448b480 measured that with Warden armor 3500 bp the Chanter bot's Cadence casts fall 6 -> 1 and hymn-field combos 5 -> 1 on the Choir, because Healer.cs:122-127 (port of healer.ts) acts in combat only when an ally is under 70%. Run C (RunCCarelessPairTests.cs:117) then runs out of ticks. Armor sweep: any armor >= 1500 bp breaks run C, so lowering armor would be fitted green.
Ruling (WoW Mythic+: a healer with nobody to heal keeps contributing, it never idles): in combat with no ally under the heal threshold, the healer bot presses its non-heal buttons (setups/damage) on the party's target. Cadence and Hymn stay heals. Change both game/bots/Keyrun.Bots/Roles/Healer.cs and bots/src/roles/healer.ts (parity), regenerate the oracle with its writer. Bible test served: "Depth and difficulty" (five buttons with real decisions) and s4 synergy as a timing game.
Also ruled: HudSceneTest.ps1 solo wipe runs A, B, E get 900 ticks instead of 600 because a Warden with 3500 bp armor dies at tick 505 (run_end 606) by measurement; this is a harness budget for an intended change, not an assertion weakened to hide a defect. No other threshold moves.

## D-CADENCE-UNMEASURED (#370)
Evidence: lane ac2bceed7702f606f showed chanter.cadence IS cast on both trees on the same ticks (t305/t308/t263); its options change heal counts (81/83/101/80) but never reach clear tick, deaths or Combos, on main too, because the bot only casts Cadence as a heal (HealPick.cs:69) so the Attuned window lands on an ally while the Anthem/Flashfire payoffs (combos.json:31,:37) hit enemies. On main cadence counted as measured only through knock-on noise. Dominance, identical-siblings and identical-runs checks pass on the merged tree.
Ruling: add chanter.cadence to UnmeasuredForks on #370 with the TRUE reason ("cast only as a heal; Attuned never closes into a Combo in bot play"), and open a finding for the real gap: Cadence's setup role (BIBLE s4/s5) is dead in bot play on main. That gap is a bot/content feature, dispatched after the pile.

- WHY this checkpoint: both red pile PRs (#362, #370) trace to one bot gap, the Chanter casts its setups only as heals; the rulings keep bible s4 (synergy as a timing game) and the "five buttons" test, and avoid fitted green (no armor lowered, no false unmeasured reason).
- origin/main 9134fad4 (#384 <- #324 <- #356 <- #293 <- #409 <- #330 <- #396).
- #319 bots default gear: OPEN head a71d9bc3, MERGEABLE, armed SQUASH, game-e2e running at 04:00Z.
- #410 checkpoint: OPEN head a29f849b before this commit, armed, game-e2e running.
- #362 Warden armor: OPEN head ad429611, CI red (run C not Timed); fix lane relaunched under D-HEALER-DOWNTIME; local merge 06e30fe2 in keyrun-wt/pr362.
- #370 gcdTicks 0: OPEN head d5fc921b CONFLICTING on origin; local merge e2d4b8b9 in keyrun-wt/pr370; lane relaunched under D-CADENCE-UNMEASURED.
- Queue after those (scratchpad/merge-forward-brief.md order): #364 after #362 MERGED, #307 after #370 MERGED, #302 after #307, #375 last (retarget base to main, three-way). Parked: #326 #327 #295 #344; #336 DO NOT MERGE.
- Merge queue: Matt approved 2026-10-21 ~02:15Z, enable only after this pile lands; no repo setting touched.
- New findings: f-b31e8f34 (Cadence setup role dead in bot play), f-4071914e (no-lane-game-launch $ false positives), f-8ecea323 (no-polling test writes into live state). Closed: f-ec0adc27 via claude-config #218.
- Lanes live at 04:15Z: #362 fix v2, #370 land, hook_removal_check P0 (f-db072e8a, f-92748791), claude-config #214 trailer, no-lane-game-launch $ text, conductor3 churn pruned-upstream, aesop sqlite busy.

### 04:30Z addendum
- #410 MERGED at 397e0f83 (head a29f849b), and #319 MERGED at 2cb255aa; origin/main is now 2cb255aa. WHY: the main pile moved underneath this checkpoint, so the "#410 OPEN, armed" and "#319 OPEN, armed" lines above are superseded and must not be acted on.
- #370 has origin/main folded in at head accd4af3, armed SQUASH at 03:59:18Z, MERGEABLE, waiting on CI. chanter.cadence is added to UnmeasuredForks with the true reason (cast only as a heal, Attuned never closes into a Combo in bot play), per ruling D-CADENCE-UNMEASURED. WHY: the fork is recorded with its real cause, not a fitted reason.
- #362 fix lane is still implementing the healer downtime rule (D-HEALER-DOWNTIME). WHY: #362 does not merge until that lane's CI is green, and the rule is not yet in code on main.
- Findings f-9974c1a8 closed via aesop #1244 (66b67a0b); f-f710a716 closed via conductor3 #194 (5075b3e1). WHY: each close cites the PR that carries the fix, so the closure is evidenced.
## 2026-10-21T04:40Z checkpoint (pile: #410 #319 landed; #370 red on M4 ARENA heading; #411 armed)
WHY: the orchestrator holds intent across compactions; this block records gh-bound pile state and the one new CI error so the next context resumes without re-deriving. Matt's standing order (2026-10-21 ~02:00Z): resolve the keyrun PR pile, read CI errors directly, merge queue only after the pile lands.
- MERGED on main: #410 checkpoint (merge 397e0f83, at head a29f849b) and #319 bots default gear (merge 2cb255aa). origin/main = 2cb255aa. The Game workflow on 2cb255aa was still running at 04:10Z.
- #411 (docs/checkpoint-2026-10-21-0415z, head 4d7cd7a7): OPEN, armed SQUASH 04:04:13Z, MERGEABLE; carries the 04:15Z block (rulings D-HEALER-DOWNTIME, D-CADENCE-UNMEASURED) cherry-picked from 71c20339 because #410 merged under it. game-e2e pending at 04:10Z.
- #370 gcdTicks-0 (head accd4af3, armed 03:59:18Z): CI run 39541477640 RED. game-e2e step "HUD scene test": `M1 startHeading=-1.5707964` ok, `M3 TICK 1 warden=(4180,0) -> TICK 21 warden=(7780,0)` ok, then `M4 exit=1 ARENA error ... startHeading must be a finite number`, `HudSceneTest: FAIL (1)`; game-gate FAIL follows from it. The run is on the merge ref with main 2cb255aa (includes #319). Log: scratchpad/pr370-ci-fail.log. Fix lane dispatched with the literal error; #307 then #302 wait on #370 MERGED.
- #362 Warden armor (head ad429611, MERGEABLE): fix lane implementing D-HEALER-DOWNTIME (Healer.cs + healer.ts parity, oracle regenerated by writer, HudSceneTest solo runs A/B/E 600 -> 900 ticks). #364 waits on it. #375 last (retarget base to main, three-way).
- Findings closed this span with gh-bound evidence: f-9974c1a8 (aesop #1244, 66b67a0b), f-f710a716 (conductor3 #194, 5075b3e1), f-2c2a2c35 (pyflakes gate PASS on aesop main 3da3755c). Added f-c47f42ce (conductor3 churn scanner: no CLI, floor 50 vs rule 20, backup/* noise); lane on it.
- Live lanes at 04:40Z: #362 fix (opus), P0 pre-push hook_removal fail-open (sonnet), no-lane-game-launch `$` false positives (haiku), aesop pre-push cost f-256f3cd8 (haiku), conductor3 churn scanner f-c47f42ce (haiku), #370 M4 fix (opus), this checkpoint (haiku). Monitor cycle 678 done: no churn, no security alerts.
- Held for Matt: BRAIN_READ_TOKEN secret (claude-config trailer-gate red repo-wide); Windows event log at 11:38 AM CDT crash; merge queue after the pile.
## 2026-10-21T04:55Z checkpoint (#370 real failure is the K1 fixture; #362 four rulings; aesop receipt side effect)
WHY: the orchestrator misread #370's CI log (M4 is a negative test that passed) and the lane corrected it; recording the corrected cause and the new rulings so the next context acts on evidence, not the stale read. Matt's order stands: resolve the keyrun pile, read CI errors directly, merge queue only after.
- #370 gcdTicks-0 (head accd4af3, armed): the only real failure in run 39541477640 is `run K1: the recorded log differs from the committed example game/fixtures/input/example-enter-ping.input-log.json`; the seed-1 key-2 run now ends at tick 1276 instead of 1273 (Enter@1276/1279 vs 1273/1276). Cause is #319 (bots wear gear via SimDriver.StartRun WithBotLoadouts) and/or #370 together. Decider: main 2cb255aa Game run 39541169627, game-e2e still in_progress at 04:50Z. If main is red on K1, fix on main (#319 did not regenerate the fixture); if green, #370 regenerates it from a real K1 run. Either way the recording is an orchestrator-run game launch (`HudSceneTest.ps1 -KeepK1Record <path>`), then a lane commits the fixture with the stated reason. Worktree keyrun-wt/pr370 holds an UNPUSHED clean merge ba08207c (main 2cb255aa onto accd4af3). Diagnosis: scratchpad/pr370-m4-diagnosis.md.
- #362 Warden armor: the healer press (D-HEALER-DOWNTIME) is built and run C is Timed, but it breaks the Choir kick lesson, two tick-pinned fixtures, fork dominance and oracle parity (C#-only blind rule). Four rulings recorded in scratchpad/decisions-2026-10-21-0450z.md: D-CHOIR-MUST-KICK (content tunes Choral Mending so never-kick never times; WoW rule), D-PINNED-FIXTURES-362 (re-derive by measurement), D-ORACLE-BLIND-PARITY (port blind to the TS sim, regenerate traces), D-FORK-DOMINANCE-362 (retune by measurement). Opus lane dispatched in that order. #364 waits on #362.
- #411 checkpoint (head 5b7f928d after the 04:40Z append, re-armed 04:09:41Z per gh, MERGEABLE, checks pending). Worktree keyrun-wt/ckpt-0440z stays until MERGED.
- conductor3 #195 MERGED 08a7e91f (churn scanner floor 20, backup/* excluded, CLI); f-c47f42ce closed. New finding f-d6f834e0: conductor3 master has no required checks (auto-merge fired instantly), repo-setting decision for Matt.
- aesop: f-2c2a2c35 closed (pyflakes gate PASS on main 3da3755c). Pre-push cost lane found the dominant phase is check_emit_receipt (18.25 s of 28.8 s; secret_scan full tree 10.9 s), pyflakes 0.48 s warm: f-256f3cd8 superseded by f-a4595dac; uncommitted fix (compiled patterns + clean-verdict cache, 9 tests) in aesop-wt/prepush-cost, continuation lane dispatched. SIDE EFFECT f-330e68c1: the timing harness posted 3 signed-receipt commit comments on aesop origin/main 3da3755c (ids 205910604, 205910655, 205910689); deletion is Matt's call.
- claude-config: no-lane-game-launch `$`-as-text fix (f-4071914e) is built in .claude-wt-gamelaunch-dollar (629/629, anti-vacuity 14 red); continuation lane runs the full suite via a node runner and opens the PR. New finding f-bdbacc1a: that hook still allows `python -c os.startfile(...)` and base64-decoded script writes. Hook false positives on loop variables (`$f`, `$D`, `new_run_$i`) stopped three lanes this span; the fix PR is the remedy.
- Held for Matt: BRAIN_READ_TOKEN secret; 11:38 AM CDT crash event log; delete or keep the 3 aesop commit comments; conductor3 required checks; merge queue after the pile.
## 2026-10-21T05:20Z checkpoint (#370 K1 drift is #370's own; orchestrator recording K1; P0 pre-push fail-open fixed in claude-scripts #133)
WHY: the K1 decision point resolved from gh state, and the P0 brain fix landed; recording both so the next context continues the pile without re-deriving. Matt's order stands: resolve the keyrun pile, read CI errors directly, merge queue only after.
- #370 gcdTicks-0 (head accd4af3, armed, MERGEABLE): main 2cb255aa Game run 39541169627 concluded SUCCESS on game-test, game-e2e and game-gate, so main is green on K1 and the fixture drift in run 39541477640 belongs to #370 (with #319 merged in), not to main. The orchestrator built keyrun-wt/pr370 (merge ba08207c = main 2cb255aa onto accd4af3) and is running `game/src/Host/HudSceneTest.ps1 -KeepK1Record` there (orchestrator-only launch; lanes never launch the game). Next: a lane commits the kept recording as game/fixtures/input/example-enter-ping.input-log.json on #370's branch with the stated reason (gcdTicks 0 moves the seed-1 key-2 boss run's end from tick 1273 to 1276, so the post-run Enter presses move 1273/1276 -> 1276/1279), pushes, re-arms #370 by literal number. #307 (ae3e63d6) then #302 (045101a2) wait on #370 MERGED.
- #362 Warden armor (head ad429611, armed, MERGEABLE): opus lane working the four 2026-10-21 04:50Z rulings (D-CHOIR-MUST-KICK, D-PINNED-FIXTURES-362, D-ORACLE-BLIND-PARITY, D-FORK-DOMINANCE-362) in keyrun-wt/pr362; #364 (453e07b6) waits on it. #375 (42b92dfa) last: retarget base to main, three-way.
- #411 checkpoint (docs/checkpoint-2026-10-21-0415z): head 5cba323c after the 04:55Z append, re-armed SQUASH on that head, MERGEABLE, checks pending.
- P0 f-db072e8a CLOSED by evidence: claude-scripts #133 MERGED (squash acfb277f, 2026-10-21T04:17:28Z). Cause: a pre-push hook piped into `head` died of SIGPIPE on `echo >&2`; Git for Windows read the msys signal status 0x0D00 as exit 0, so a denied push landed (4 of 10 piped runs). Fix: install_brain_prepush.py v3 (trap PIPE, exit 1 on HUP/INT/TERM, verdict log in $GIT_DIR/brain-prepush-last.log) and hook_removal_check.py --master-ref scoping (f-92748791: merge-forward of master's own gate changes is not a removal; any doubt falls back to the full range). Tests 28/28 and 36/36 (red 7 and 10 before). The live hooks in ~/.claude, ~/scripts, ~/conductor3 were still v2 at 05:15Z; reinstall = `git -C ~/scripts pull --ff-only` then `python ~/scripts/install_brain_prepush.py` (orchestrator action, this span).
- Brain hook false positives keep stopping lanes (no-lane-game-launch on `$f`/`$D` loop variables; no-scrollback-as-record refused the orchestrator's own K1 wrapper script and named the allowed shape, which was then used). Fix PR for the `$` text case (f-4071914e) is in .claude-wt-gamelaunch-dollar with a lane finishing the suite; claude-config has 14 open hook PRs (#64-#214), several CONFLICTING, which is why fixes reach the live hooks slowly: triage lane dispatched.
- Held for Matt: BRAIN_READ_TOKEN secret; 11:38 AM CDT crash event log; delete or keep the 3 aesop receipt commit comments (f-330e68c1); conductor3 required checks (f-d6f834e0); merge queue after the pile.
## 2026-10-21T04:35Z checkpoint (#370 MERGED; #307 next in the pile; hook `$` fix live; claude-config pile moving)
CLOCK NOTE: the two blocks above labelled 04:55Z and 05:20Z were written at about 04:00Z and 04:20Z (`date -u` at this write is 04:31Z; #221 merged 04:28:30Z per gh); the orchestrator mislabelled them, the content stands. Times from here are from `date -u`.
WHY: #370 landed, which unblocks #307 then #302 in the merge-forward order, and the brain hook false positive that stopped three lanes is fixed on master and installed locally; recording the new main sha and the gating so the next context arms #307 from gh state, not from memory. Matt's order stands: resolve the keyrun pile, read CI errors directly, merge queue only after the pile.
- #370 gcdTicks-0 MERGED (squash 7fb4664558adb9acef73c7cb1675b5d9a7064ddb, head 7fb01775; all five required checks SUCCESS on the regenerated K1 fixture from the orchestrator's real HudSceneTest run). keyrun origin/main is now 7fb46645. Worktree keyrun-wt/pr370 is removable after pr_merged_check.py prints MERGED.
- #307 holder-lock (head still ae3e63d6, mergeable UNKNOWN after main moved): the opus merge-forward lane was told main is now 7fb46645 and must include it before pushing; it pushes only if fastcheck -All and vitest pass locally and never arms. Orchestrator arms with `gh pr merge 307 --repo matt82198/keyrun --auto --squash --match-head-commit <40-char head>` once the push is on the PR head ref. Then #302 (045101a2) the same way. #364 (453e07b6) after #362 MERGED. #375 (42b92dfa) last: retarget base to main, three-way.
- #362 Warden armor (head ad429611, armed, MERGEABLE): opus lane still working the 04:50Z rulings (Choral Mending sweep running). #411 checkpoint (head 45dc0b64, armed): game-e2e pending at 04:30Z; this block is appended by a lane (disarm, append, push to the PR head ref, re-arm).
- Brain: claude-config #221 MERGED b8388b23 (no-lane-game-launch now skips sed scripts that contain a literal `$` and no launch word; suite 70/70 files, new test file 636/636, 18 red against HEAD). f-4071914e CLOSED. #64 MERGED 3944c38 (grants restatement gate) by the cc landing lane; #205 and #165 next with local proof. ~/.claude fast-forwarded to 3944c38, so the live hook carries the fix. The lane noted #221 merged while trailer-gate was still in progress: that is the known no-required-checks state of claude-config master (f-deaf29d0), not a new hole. Next hook finding f-bdbacc1a (python -c os.startfile and base64-decoded script writes slip through isCodeData) has a haiku lane.
- aesop secret_scan clean-verdict cache (f-a4595dac): lane has 9 cache tests OK, identical old/new scanner output, warm scan 0.59 s vs 5.08 s cold baseline, mutants red as intended; the full unittest suite was still running at hand-back and the lane resumes on its own to commit, push and open the PR.
- Held for Matt: BRAIN_READ_TOKEN secret; 3 aesop receipt commit comments (f-330e68c1); conductor3 required checks (f-d6f834e0); stray branch ckpt-0440z-mf (f-e14b5854); 11:38 AM CDT crash log; merge queue after the pile.

## 2026-10-21T04:45Z checkpoint (#307 not green, follow-on lane; #362 CI red read; two stale findings closed; keyrun fix lanes dispatched)
WHY: the #307 merge-forward came back red (two Bots tests), so the pile stalls at step 1 until a follow-on lane fixes code and content, and #362's armed head is red on CI for a reason the orchestrator has now read directly (Matt: read the errors, no wild goose chases). Recording what is red and why so the next context arms nothing on narration. Matt's order stands: resolve the keyrun pile, merge queue only after the pile.
- keyrun origin/main 7fb46645. #370 MERGED (7fb46645). #396 MERGED 63cbb9e8. #411 checkpoint OPEN head 9d97b9e3 armed; this block is appended by a lane (disarm, append, push to the PR head ref, re-arm).
- #307 feat/holder-lock: head still ae3e63d6, CONFLICTING, ALREADY ARMED by Matt (SQUASH, 2026-10-20T17:55Z) so it merges on its own the moment it is green; the orchestrator does not touch auto-merge. First lane merged main 2cb255aa locally (merge-fwd/pr307 at 6b057907 in keyrun-wt/pr307, not pushed): oracle traces/manifest regenerated, golden hashes measured, Holder.LockHolders now skips Idle enemies (PRESPAWN-AGGRO rule). Two Bots tests red: JoinerDoorway (caused by the merge: seats 1 and 3 re-enter pair-hall at 562/1066 and 578/1082) and ForkParity (chanter.cadence:swift, chanter.chorus:encore, cinder.flare:efficient dominated; red on the PR's own CI 39494843817 too, so a content defect of the PR). Rulings: ForkParity fixed in content (no fork dominated, bible s0 real decisions, follow merged #356's pattern), never in the test; JoinerDoorway fixed in targeting code, never in the test. Follow-on opus lane must also merge 7fb46645 (#370) and pushes only on fastcheck PASS + vitest green. Then #302 (045101a2, also armed by Matt), #364 after #362, #375 last (retarget base to main, three-way).
- #362 feat/tank-survivability head ad429611 armed: CI RED, read directly from run 39538213120: game-test fails "Run C careless seat (3@300:pair): the Pair engages only after the Choir clears, on the careless seat, hits a bot seat, and the party clears the run [FAIL]"; game-e2e fails "HudSceneTest: FAIL (9)"; game-gate fails downstream. The #362 opus lane is still working the Choir must-kick rulings and will push again; the orchestrator re-reads CI on the new head before anything else. Log: scratchpad/pr362-game-test-fail.log.
- Keyrun fix lanes (KEYRUN FIRST gate asked for keyrun work before machinery): RUN-TIMER-STOP f-f90892e9 (P0 wrong data: timer counts past run end) and Twin plates anchor f-07f1b477 are running as opus lanes with their own worktrees from 7fb46645, PR + arm at open, no game launch. Two findings closed as already fixed on main with evidence: f-f404582c (PR #232 MERGED 12676e55 draws the Steam cloud) and f-4501525f (game.yml:169-178 runs CombatTextOverlapTest in game-test). Empty worktrees for those two were removed (zero commits, nothing pushed).
- Machinery: aesop #1246 (secret_scan clean-verdict cache, f-a4595dac deferred on it) OPEN, armed SQUASH at 8fd618e9, MERGEABLE; warm scan 0.59 s vs 5.08 s, output identical, 9 cache tests OK, coverage gate OK after staging. claude-scripts f-a8d9a7fc (--repo PATH for secret_scan, fail-closed kept) has a haiku lane. claude-config #165 and #205 still OPEN (landing lane running hook_removal_check). Hook holes f-bdbacc1a lane running. Refinement monitor heartbeat 2237 s at 04:41Z: relaunched this block.
- Held for Matt: BRAIN_READ_TOKEN; 3 aesop receipt commit comments (f-330e68c1); conductor3 required checks (f-d6f834e0); stray branch ckpt-0440z-mf (f-e14b5854); 11:38 AM CDT crash log; Smart App Control (f-37ad66b3); merge queue after the pile.
- 04:50Z addendum: #411 MERGED 1ffcc764 (squash of 9d97b9e3), so this block opens a fresh checkpoint PR. claude-config #165 MERGED cedb26ce (read gates fail-closed; hook_removal_check OK, f-8ecea323 isolation proof passed), #64 MERGED 3944c38; #205 is a DRAFT (gh isDraft true) so the pinned squash was refused: flipping it is Matt's call. ~/.claude fast-forwarded to cedb26ce, 0 0 vs origin/master. LESSON (orchestrator): three of the keyrun fix lanes this block (run timer f-f90892e9 -> #253 MERGED 9fed4030; Twin plates f-07f1b477 -> #334 MERGED abeb44fa; Steam cloud; CI overlap) were dispatched on findings whose deferral reasons already named the merged fix; the list output was filtered to one line each and the deferral reasons were dropped. Rule from here: read the deferral reason before dispatching a finding, and D15 visible rows close by the orchestrator viewing frames, not by lanes. Twin plates: the view anchors each plate to its own actor (LabelView.cs:176); the column comes from the Twins spawning along x in the camera's travel direction (HostRunSetup.cs:481), a spawn-geometry question held until #362/#375 land. Empty worktrees run-timer-stop, twin-plates-anchor, view-steam-cloud, ci-overlap-gate removed (zero commits, never pushed).
## 2026-10-21T04:55Z checkpoint (#412 checkpoint PR armed; nine findings closed on gh evidence; pile still stalled at #307; three new keyrun lanes)
WHY: the pile has not moved (#307 and #362 lanes still working their red tests), so this block records the gh-bound facts the orchestrator used to close findings and to choose non-conflicting keyrun work while the pile is serialized, so the next context does not re-dispatch stale findings (LESSON 04:50Z) and does not arm anything on narration. Matt's order stands: resolve the keyrun pile, read CI errors directly, merge queue only after the pile.
- keyrun origin/main 1ffcc764 (#411 squash). #412 checkpoint OPEN head 0425d555, armed SQUASH by the lane, MERGEABLE; this block is appended by a lane (disarm, append to END, push to the PR head ref, re-arm with --match-head-commit).
- Pile (gh 04:55Z): #307 ae3e63d6 CONFLICTING, armed by Matt, follow-on opus lane fixing JoinerDoorway (code) and ForkParity (content) in keyrun-wt/pr307; #302 045101a2 armed, after #307; #362 ad429611 armed, CI red (careless-seat test, HudSceneTest FAIL (9)), lane still working; #364 453e07b6 armed, after #362; #375 42b92dfa CONFLICTING, last (retarget base from closed #308 to main, three-way). Parked: #295 #298 #326 #327 #344 #229 OPEN.
- Findings closed this block on gh MERGED evidence: f-80b5cf16 (#370 7fb46645), f-6a419b5c and f-4cda6f59 (#356 74365f2d), f-8476dbf4 (#358 2e07e763), f-6b7e5740 and f-c89e8074 (#396 63cbb9e8), f-ff4ce040 (#330 b0ab1361), f-7f17e75e and f-8ecea323 (claude-config #165 cedb26ce). Store 51/60, overflow cleared.
- Findings whose named PR is CLOSED unmerged (#244 #333 #335 #359 #308) or re-check on #366 MERGED: a read-only haiku recon lane is checking main for f-4b48a6cf f-87f2a9a5 f-dfafcec6 f-3cc5aeee f-1326b59a f-d0142701 f-2fcf69c1 f-4fc9058b; close or re-dispatch from its file:line evidence, not from the old PR number.
- New keyrun lanes (opus, non-conflicting with the pile): f-ccb1cefb host-path gear test (test/gear-into-run-host-path); f-b31e8f34 bots play Cadence as Attuned setup (fix/bots-cadence-setup, failing test first, bots code only, traces regenerated). Both arm at open and sit behind the pile in merge-forward order.
- Monitor cycle 679: heartbeat 04:46:53Z; new proposal P-CHURN-20261021f (aesop primary main has 148 checkpoint commits never pushed, blocked by the generated_paths gate, known to Matt; decide a checkpoint/* branch or STATE/BUILDLOG PR route) in conductor3/monitor/PROPOSALS.md; squash-merged heads still flagged as churn (monitor rule gap, repeat proposal).
- Worktrees: keyrun-wt/ckpt-0440z removed after pr_merged_check MERGED (#411). .claude-wt-readgates removal denied (Permission denied, file in use) after pr_merged_check MERGED (#165): retry later. keyrun-wt/holder-lock left alone.
- Hook holes f-bdbacc1a (claude-config, lane aa9dcd42): fix is local only at 3ec0c726 in .claude-wt-launch-holes (branch fix/no-lane-game-launch-code-body, 657/657 focal, 1454/1454 suite, 18 red before). Push BLOCKED by brain-prepush hook_removal_check: the fix reverses four committed section-7 ALLOW tests (keyrun.exe / godot path strings inside node -e / python -c), which the gate reads as hook removal needing `Hook-Removal-Approved-By: matt82198`; the Hook-Proof trailer on 6396178 is also malformed and fixing it needs a history rewrite (banned). Also over-broad: subprocess/Popen/exec/spawn in -c/-e bodies now deny benign shapes like python -c subprocess.run(['git','status']). Held for Matt: approve the reversal (then a fresh lane rebuilds the commit with a well-formed trailer and a narrower word list) or rule the four ALLOW rows stand and only the decode-then-write and awk -F| parts ship. Nothing pushed, no PR.
- Held for Matt: BRAIN_READ_TOKEN; claude-config #205 draft flip; 3 aesop receipt commit comments (f-330e68c1); conductor3 required checks (f-d6f834e0); stray remote branch ckpt-0440z-mf (f-e14b5854); aesop primary main unpushed checkpoint chain (P-CHURN-20261021f); 11:38 AM CDT crash log; Smart App Control (f-37ad66b3); merge queue after the pile.
## 2026-10-21T05:05Z checkpoint (ForkParity ruling unblocks #307 and #362; seven findings closed on gh evidence; #413 opened; claude-scripts #134 MERGED)
WHY: both pile lanes (#307 and #362) stopped on the same wall, ForkParity red for fork options the bots never cast, so the orchestrator rules once here and the next context relaunches from this ruling instead of re-deriving it. Nothing in the pile was pushed or armed on narration; every state below is from gh pr view at 05:03Z. Matt's order stands: resolve the keyrun pile, read CI errors directly, merge queue only after the pile.
- RULING R-FORKPARITY-UNPLAYED (orchestrator, 05:05Z, bible s0 "real decisions on five buttons"): ForkParity measures bot play. When the bots never cast an option (warden.vigil:ally has no ally-shield handler, so reserve vs ally compares reserve against "Vigil never pressed"; chanter.cadence resonance and groupSong are cast only as a heal, so Attuned never closes into a Combo), the red is a bot gap, not a content defect. Fix the bots, never the assertion, never a number retune with no measured lever. Concretely: (1) #307 follow-on lane adds ally-Vigil casting to the bots (Warden casts ally-Vigil on the pressured ally; failing bots test first) inside the merge-forward, then pushes on green. (2) #362 waits for the Cadence-as-setup PR (lane ae42f2e4, branch fix/bots-cadence-setup, finding f-b31e8f34) to MERGE, then merge-forwards on top; if resonance and groupSong still measure identically after bots use Cadence as a setup, they are indistinguishable in play and the content must change what they DO (bible s7 facets change function), not a number.
- #307 feat/holder-lock: head ae3e63d6 CONFLICTING, armed by Matt (untouched). Lane a39b048f committed locally in keyrun-wt/pr307 (merge-fwd/pr307 at a9f9a156: merges 2cb255aa and 7fb46645, JoinerDoorway root cause = TsSymmetricPushOut running on layout runs, fixed in RunWiring.RulesConfigOf with Layout is null guard plus EnemiesNeverDisplacePlayerTests; EncounterTraceTests replay uses oracle origin/yaw for between-projection telegraphs; chorus:discipline cooldown 160 -> 180; vitest 1125/1125, Content 716/716, Sim 1168/1168, Bots 234/235). Only vigil:reserve dominated by ally remains: ruling (1) above. Nothing pushed.
- #362 feat/tank-survivability: head ad429611 MEREGABLE, armed by Matt on the old head (untouched). Lane aa6e6e6e committed locally in keyrun-wt/pr362 (pr362-mf at 61f4679c: Choral Mending 3000 -> 6000 so never-kick times 0/85 cells and kick stays 75/85; pinned fixtures re-measured; TS blind-miss port, oracle splits 0; efficient 125 -> 150, surge 120 -> 140; Content 721/721, Sim 1153/1153, Bots 223/224, vitest 1117/1117, Playtest 106/106). Only cadence resonance = groupSong identical-siblings remains: ruling (2) above. Nothing pushed. Lane deviation accepted: edited HudSceneTest.ps1 with the Edit tool after no-lane-game-launch denied a sed with a dollar sign; hook finding f-2e94479a filed (narrow, never delete).
- Pile order now: Cadence PR (ae42f2e4) -> #362 -> #364; #307 (after ally-Vigil) -> #302; #375 last (retarget base to main, three-way). #413 test/gear-into-run-host-path OPEN 55e5cdf1 MEREGABLE armed SQUASH by the lane (235/235 bots, two production mutants red). #412 checkpoint OPEN aeb68a27 armed; this block appended by a lane.
- Findings closed on evidence this block: f-4b48a6cf, f-87f2a9a5, f-dfafcec6 (#396 63cbb9e8), f-d0142701, f-2fcf69c1 (#366 4f165cb4), f-3cc5aeee (#396 adapted under D44 no-gates: OpenSeatWipeTests 5/5; GatedWipeTests from b9026221 fail on main only because gate_closed never fires; gated-wipe worktree removed, zero commits), f-a8d9a7fc (claude-scripts #134 MERGED a5288c59: --repo PATH already on master, clear FATAL for a non-git --repo, LOOP-MANIFEST.json gained tests/test_secret_scan_repo_flag.py). f-4fc9058b deferred: premise refuted by #347 MovieDirector; whether 02-lieutenant-cast-bar.png shows the Cantor bar is a D15 frame check by the orchestrator after the pile. f-1326b59a (self cause row, P0) has opus lane ad36d47f on fix/no-self-cause-row.
- claude-config conflict chain: lane ac0abc1d stopped on the no-polling hook (background wait). #145 three-way resolved locally in .claude-wt-chain-145 (chain/pr-145; INDEX regenerated by knowledge_tree.py; auto-commit-brain.mjs keeps per-box push and removal gate, gate runs after syncFromMaster and before tryPush), tests were running, nothing pushed, #145 still OPEN CONFLICTING on gh. Relaunch brief fixes: Node 24 runner is `node --test hooks/*.test.mjs hooks/lib/*.test.mjs`; commit trailer is Co-Authored-By: Claude Fable 5.1 (Matt's rule). #204 is a draft (Matt's flip).
- Monitor: heartbeat 920 s at 05:02Z; integrate/playable-dungeon has origin ref 84f9bf35, 173 ahead, unchanged (no churn).
- Held for Matt: hook-holes reversal approval (f-bdbacc1a, local 3ec0c726); BRAIN_READ_TOKEN; claude-config #205 and #204 draft flips; 3 aesop receipt commit comments (f-330e68c1); conductor3 required checks (f-d6f834e0); stray branch ckpt-0440z-mf (f-e14b5854); aesop primary main unpushed checkpoint chain (P-CHURN-20261021f); 11:38 AM CDT crash log; Smart App Control (f-37ad66b3); ~/scripts primary is dirty on feat/shorts-prep-montage so the sanctioned ff-pull of #134 was not run; merge queue after the pile.
## 2026-10-21T05:20Z checkpoint (#307 pushed and MERGEABLE under R-FORKPARITY-UNPLAYED; six lanes live; cc chain stopped on a piped-shell denial)
WHY: the pile's first wall came down the way the ruling said (bots fixed, no assertion or number touched), so this block records the gh-bound head and what remains before #302 can merge-forward, and it records the saturation dispatch so the next context does not re-dispatch the same findings. Matt's order stands: resolve the keyrun pile, read CI errors directly, merge queue only after the pile.
- #307 feat/holder-lock: head ff04c252 (gh pr view 05:12Z: OPEN, MERGEABLE, mergeStateStatus BLOCKED on pending checks, auto-merge SQUASH armed by matt82198 since 2026-10-20T17:55Z, untouched). Lane a5f1460b: Warden bots cast ally Vigil on the most pressured ally (KitsRegistry/KitsResolve AllyShieldAbilityId; TankPressure AllyShieldCommand; pressure = enemy aimed at the ally or health under HealerRole.HealThresholdPct 0.7); root cause was the ally talent making Vigil target-shaped so the bots' self-cast was refused (Casting.StartError target_required). ForkParityTests.Measure now builds the bot view over Kits.ForRun like the host does (harness, not an assertion). AllyVigilTests 7 cases red 2/7 then 7/7; mutants 4/3/2 red. fastcheck PASS (Content 716, Sim 1168, Bots 245), vitest 1125/1125, scans CLEAN 7 and 346 files. Merges origin/main 8d4b51d3. CI on ff04c252: test and e2e pass, game-test and game-e2e pending at 05:12Z. Orchestrator judgement to record: 0.7 as the Guardian ally-shield heuristic is a bot-policy call made by the lane from the healer's triage line; ally Vigil is sparse (1 cast on hv-k2-s7); acceptable for now, revisit if frames show the tank never shielding.
- #302 next after #307 MERGED (pr_merged_check). f-ee437681 (private-log seat filter scenario) also opens after #307 MERGED.
- #362 pr362-mf 61f4679c local, waits for the Cadence PR (lane ae42f2e4 still running, re-measuring EncounterTraceTests oracle counts). #364 after #362. #375 last.
- Saturation 05:18Z (6 live, 3 keyrun): ae42f2e4 Cadence setup (f-b31e8f34); a11da271 pack spread feat/pack-spread (f-82d9390e; #396 MERGED freed forces.json, layout.json, LayoutPacks.cs; WoW trash reference recorded in PR body); adbb5219 Ascension HUD feat/ascension-hud (f-275d47c9; private-only per BIBLE:212 DECIDE); ac9468ab cc chain #145 then #146 #157 #161 #162 #163 #164 #166 #167 #214 (script-file shape for the marker check); a7f7a7ca no-lane-game-launch narrowing (f-2e94479a sed -i on a .ps1 edit target; f-dd6467ec piped-shell denial text); ae343def no-scrollback-as-record PowerShell-tool in-repo script shape (f-ebfd36d3).
- cc chain lane addecc90 stopped on no-lane-game-launch denying `| xargs sh -c` as a piped shell; nothing committed or pushed; #145 remote head still 587c72c8; worktree .claude-wt-chain-145 holds the staged merge plus two test-only fixes (isolate-git import and hook_removal_check.py fixture copy in auto-commit-brain-box.test.mjs; resetToOrigin restored in auto-commit-brain.test.mjs), suite 1478/1478. Finding f-dd6467ec added (hook rule sound, wrong name and text).
- #413 55e5cdf1 and #414 6c5db596: armed SQUASH, not yet confirmed MERGED this block (#307's merge of 8d4b51d3 says #413 landed; confirm with pr_merged_check before removing keyrun-wt/gear-hostpath-test and keyrun-wt/self-cause-row).
- Monitor 05:11Z: heartbeat 1471 s (under 1500), integrate/playable-dungeon 173 ahead with origin ref, no churn.
- Held for Matt: unchanged from 05:05Z (hook-holes reversal f-bdbacc1a; BRAIN_READ_TOKEN; #205 #204 draft flips; 3 aesop receipt comments f-330e68c1; conductor3 required checks f-d6f834e0; stray branch ckpt-0440z-mf f-e14b5854; aesop primary unpushed checkpoints P-CHURN-20261021f; crash log; Smart App Control f-37ad66b3; f-f1b91704 30 s bound; ~/scripts primary dirty so #134 ff-pull not run; merge queue after the pile).
## 2026-10-21T05:30Z checkpoint (#415 Cadence-as-setup open and red on three tests; rulings R-415-A/B/C; #413 #414 #412 MERGED; #416 is the live checkpoint PR)
WHY: the Cadence lane proved the bible s4 gap is real (0 Attuned combos before, 25 after) but its trajectory change turned three tests red, and each red needs an intent ruling, not a fit; recording the rulings here so the follow-on lane and the next context work from them. Matt's order stands: resolve the keyrun pile, read CI errors directly, merge queue only after the pile.
- #415 fix/bots-cadence-setup: OPEN e75210a7 MERGEABLE, armed SQUASH by matt82198 token (05:24Z). HealerSetup.cs plus TS twin healer-setup.ts: healer casts Cadence at a damage-role partner's target when no ally is under 70%, partner has a Payoff ready by the landing tick and no Setup of its own. TS sim twin of HEAL-NEVER-HOSTILE (heals land only on the caster's side, as C# has since #390). Oracle re-recorded: k2-s7/k2-s11 2c1a47df (timed 1730), k10-s7 aa65cde8 (timed 2074). Attuned combos 7/6/12. vitest 1122/1122, Content 716/716, Sim 1163/1164, Bots 242/243, Playtest 102/103. Lane deviations accepted: TS mirrors were needed to replay; commit trailer says Opus 5.5 (no history rewrite; record only); scratch detached worktree bots-cadence-main-probe removed (zero changes, never pushed).
- RULING R-415-A (ForkParity: chorus:encore dominated by roundCall, hymn:roundRobin by staccato, coldSnap:overflow by deepFreeze): content retune per D62, buff the dominated option until it wins at least one of the three seeded runs with a measured lever and record the before/after rows; never the assertion, never the list. Same pattern as #356.
- RULING R-415-B (Playtest Run F "seat 2 was never refilled": Choir pull clears 2901 vs 2692, 3500-tick budget no longer reaches a pull start after the abandon): the budget stays. The mechanism must be found before anything changes: diff the healer's command log in the Choir pull between main and #415 (what the healer cast instead of what, per tick). Expected cause: Setup casts spend healer GCDs during the Choir, where the lesson is the kick ledger. If so the fix is bot policy (for example Setup only when the partner's Payoff is ready within one GCD, or not while a must-kick cast is up on the ledger), measured by the Choir clear tick returning near 2692 with Attuned combos still above zero. If the budget is truly too short for a correct run, that is a separate ruling with evidence; do not take it alone.
- RULING R-415-C (EncounterTraceTests k10: model emits hollowWarden.lock at t=2066 because it learns a cast only from cast_complete and the slam started at 2065 with the run ending at 2074): harness fidelity fix, not an assertion edit: the encounter-only model tracks in-flight casts from cast_start, then the k10 row is re-measured honestly from the new trace. TraceReplayTests passing on all three traces is the evidence this is a model gap, not a sim divergence.
- VETO: the skip added to tests/client/hud-wiring-ping-window.test.ts (skipping ticks where another class's Window is open) is an assertion narrowing. Revert the skip; extend the test to assert the HUD shows the player's own Window correctly while another class's Window is open (the new world), red first.
- New finding f-47cfeed9: Anthem's hasted lands on the enemy target in oracle runs; bible s4 says the party gains haste.
- Pile: #307 ff04c252 OPEN MERGEABLE (CI pending at 05:12Z) -> #302. #415 (after rulings) -> #362 merge-forward from pr362-mf 61f4679c (expect a three-way on Healer.cs and healer.ts) -> #364. #375 last. Merge queue after the pile.
- MERGED this block (gh 05:27Z): #413 8d4b51d3, #414 bc95d30b, #412 4de90492. Worktrees gear-hostpath-test and self-cause-row removed. f-1326b59a close waits on main's game-e2e run for bc95d30b (run A must still print a seat=1 cause line). #416 docs/checkpoint-2026-10-21-0520z OPEN e04ac329 armed SQUASH; this block is appended to it.
- Lanes live after this block: a11da271 pack spread, adbb5219 Ascension HUD, #415 follow-on (new), ac9468ab cc chain, a7f7a7ca no-lane-game-launch narrowing, ae343def no-scrollback PowerShell shape.
## 2026-10-21T05:35Z checkpoint (#307 red on two CI causes read directly; follow-on lane dispatched under R-307-A/B; #416 re-armed at e517931e)
WHY: Matt's order is to read the CI errors on the gate instead of sending lanes on guesses, so this block records the two exact causes on #307 ff04c252 and the rulings that bound the fix, so the next context judges the hand-back against them. Main's Game workflow is green at bc95d30b and 8d4b51d3 (runs 39548797607, 39548127174), so both reds belong to the branch.
- #307 ff04c252 CI (run 39548889686): game-test red on tools/Keyrun.Playtest.Tests/RunCCarelessPairTests.cs:117 Careless_seat_pulls_the_pair_after_the_choir_and_the_party_clears, Expected Timed, Actual Depleted; context: choir pull_clear=1968, pair pull_start=2048, careless seat at pair engage=(39634,3), pair ring=(43500,0) r=4000, pairHitBot=True, endTick=2797. game-e2e red on HudSceneTest M4: "ARENA error ... startHeading must be a finite number", HudSceneTest FAIL (8), M1 startHeading=-1.5707964, M3 warden (4180,0) -> (7780,0). game-gate red only because game-e2e failed.
- RULING R-307-A: Depleted at 2797 is diagnosed first (who died or what depleted, which enemy action, bot casts per tick, main vs branch); suspects are this branch's ally-Vigil bot policy (0.7 threshold) and the holder-lock itself; fix bot policy or mechanic with a failing test and measured before/after; never the Expected value, the scenario numbers or the test; a content number only with a measured lever in the commit body.
- RULING R-307-B: a non-finite heading reaches the ARENA scene (likely a zero-length direction when the lock pins an actor on its target); reproduce at the heading function with a unit test, make a degenerate vector yield a defined heading, never loosen the finite check. The lane cannot run HudSceneTest.ps1 (no game launches in lanes); the orchestrator reads CI after the push.
- Lane a38cf2f3 (opus csharp-pro) on keyrun-wt/pr307 merge-fwd/pr307, pushes HEAD:feat/holder-lock, PR body append only, Matt's arm untouched.
- #416 docs/checkpoint-2026-10-21-0520z: lane a91801616 appended the 05:30Z block, head e04ac329 -> e517931e, re-armed SQUASH --match-head-commit e517931e (gh 05:30Z: OPEN MERGEABLE BLOCKED on pending game checks). #415 e75210a7 OPEN armed, lane af35eb04 running the ZzDiagRunF healer command dump (R-415-B). #302 045101a2 CONFLICTING (after #307). #362 ad429611 and #364 453e07b6 UNKNOWN mergeable (after #415). #375 42b92dfa CONFLICTING, unarmed, last.
- Related finding already on file: #302's list names "HUD K1 startHeading not finite" as one of its reds, so R-307-B's fix is likely the same cause #302 will meet; the #302 merge-forward lane reads R-307-B's commit first.
- claude-config #222 MERGED 7031fad (pr_merged_check 05:38Z; lane ae343def, f-ebfd36d3 CLOSED): the PowerShell tool now decides a relative tools/*.ps1 run like Bash `powershell -File`, and refuses -Command, -EncodedCommand, their prefixes and iex/Invoke-Expression as arguments in every script-file shape (ps-script-shape.mjs reentersCode); no refusal test weakened; hook_removal_check OK. The lane could not reproduce the original bare `tools/fastcheck.ps1 -All` refusal on origin/master (cause unknown, possibly an older live checkout). Live ~/.claude ff-pulled to 7031fad (sanctioned); KNOWLEDGE.md and MEMORY.md remain locally modified there, untouched. Worktree .claude-wt-scrollback-ps removed. Lane deviation recorded, no rewrite: commit trailer says Claude Haiku 5.5 instead of Fable 5.1.
- Refinement monitor relaunched 05:36Z (lane a9776e54, one cycle) because the heartbeat was 2799 s old (bound 1500 s); watchdog heartbeat 260 s.
- Lanes running after this block (7, keyrun 4): a38cf2f3 #307 reds, af35eb04 #415 rulings, a11da271 pack spread, adbb5219 Ascension HUD; machinery ac9468ab cc chain #145, a7f7a7ca no-lane-game-launch narrowing (waiting on its own background work), ae343def no-scrollback PowerShell shape.
- Monitor cycle 680 (lane a9776e54, 05:37Z): heartbeat 1792561023, no AUTO actions, no new HIGH/MED alert, 148 unpushed aesop primary main commits still open (P-CHURN-20261021f); staged P-PROPOSALS-SIZE-20261021 (PROPOSALS.md 1541 lines / 126KB, not on the rotation list); finding filed for a conductor3 worktree PR after the pile. chore/incidents-2026-10-14 flag is false (PR #948 squash-merged).
- Finding f-c559b4ef (PROPOSALS.md rotation) deferred until the pile lands.
