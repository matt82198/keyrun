# STATE.md - Keyrun

**Intent (Matt, 2026-09-18):** "isolating m+ from WoW and just making it an open and queue game like Dark and Darker,
and let people manage their characters from a simple screen... study other mmos, dungeon games, gear progression, and
find specifically what people find exciting/fun, build talent trees, classes, the dungeon POC with initial enemies,
abilities, a boss. Mechanics like interrupts, but be more creative than blizzard devs... 4-5 rotational abilities per
class max, 10 total keybinds should be plenty for rotation, movement, utility... the goal is class synergy." No art
assets; pure POC.

## Current state (checkpoint 2026-10-18)
- Main tip: 10de063 (#106 squash, scaleStep keeps every). Prior main tips: 56c5686 (#105), b54eb40 (#104 docs), 2c8f8ea (#102), 54e69c5 (#99), 00e4ebb (#98).
- Merged today since the 23:30Z checkpoint: #101 99836b0 (window ring labels + payoff-ready key glow + ping context + aimed spot ping); #102 2c8f8ea (bots/src/roles/shared.ts inLull, tank-pressure.ts).
- MERGED 99f39a2: #103 feat/status-chips-downed-net-casts (status chips STUNNED/TAUNTED/BRACED from snapshot effects, DOWNED tag, dash trail, telegraph base opacity 0.15->0.4, net self-target fix via server/src/human-casts.ts, server cast/cast_failed/gcd events, joined carries display info).
- Phase: 3 closing into 3D MVP (D10). Phase 5 "Matt playing" is the acceptance test; not yet reached.
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
- 1D FINDING (2026-10-18, lane 1D, PR #111): content/data JSON diverges from the TS types. EffectDef.periodic is absent
  on 18 of 22 effects. Facet effectsAppend Window ops omit setupClass. Facets use `combo` with comboTable and
  `resource.resource` ops, which are not in the sim union. Facet shapes use `width`. The C# loader accepts the JSON as
  it is. WHY it matters: phase 2 ports the RUNTIME behaviour the sim actually executes, not the TS declarations; the
  TS types are not a reliable spec for the data. Parity is judged against the oracle traces (sim/ORACLE.md), not the types.
