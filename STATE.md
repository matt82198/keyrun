# STATE.md - Keyrun

**Intent (Matt, 2026-09-18):** "isolating m+ from WoW and just making it an open and queue game like Dark and Darker,
and let people manage their characters from a simple screen... study other mmos, dungeon games, gear progression, and
find specifically what people find exciting/fun, build talent trees, classes, the dungeon POC with initial enemies,
abilities, a boss. Mechanics like interrupts, but be more creative than blizzard devs... 4-5 rotational abilities per
class max, 10 total keybinds should be plenty for rotation, movement, utility... the goal is class synergy." No art
assets; pure POC.

## Current state (checkpoint 2026-10-17 23:55Z)
- Main tip: 2c8f8ea (#102 squash, tank banks GCD in a lull so it survives pull 1). Prior main tips: 54e69c5 (#99), 00e4ebb (#98).
- Merged today since the 23:30Z checkpoint: #101 99836b0 (window ring labels + payoff-ready key glow + ping context + aimed spot ping); #102 2c8f8ea (bots/src/roles/shared.ts inLull, tank-pressure.ts).
- MERGED 99f39a2: #103 feat/status-chips-downed-net-casts (status chips STUNNED/TAUNTED/BRACED from snapshot effects, DOWNED tag, dash trail, telegraph base opacity 0.15->0.4, net self-target fix via server/src/human-casts.ts, server cast/cast_failed/gcd events, joined carries display info).
- Phase: 3 closing into 3D MVP (D10). Phase 5 "Matt playing" is the acceptance test; not yet reached.
- Earlier landed (unchanged): #92 e3f622e, #95 e56955c, #97 08b9611, #98 00e4ebb, #99 54e69c5.
- Test count on 2c8f8ea: not restated in this checkpoint (not sourced for this head).
- NEXT (supersedes the status in NEXT STEPS item 1): after #103 lands, SPREAD/STACK telegraph text labels (render/telegraphs.ts); then Slack delta (needs hud-wiring.ts); then the gap list above by Matt's priority.

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

## NEXT STEPS (ranked; Phase 5 "Matt playing" stays the acceptance test)
1. LAND the 3 live lanes (each must pass main's gates and be re-tested against the current main head, not an
   older base - see the #94 post-mortem):
   a. fix/sim-telegraph-facing - sim/encounter/telegraphs.ts:51 uses yaw0=+z while sim/combat/targeting.ts:57 uses
      yaw0=+x; scripts.ts:109 copies the combat yaw, so cones/lines resolve 90 degrees off.
   b. feat/window-ping-castbar-readability - ladder rungs 3, 4, 6, 7.
   c. feat/status-chips-downed-net-casts - ladder rungs 5, 12, 13, 14, plus net/mapping.ts:118 self-target and
      server snapshot cast / cast_failed / gcd events.
2. SPREAD/STACK telegraph text labels (open item from #98).
3. Bot balance on pull 2 (3 of 4 bots die); balance is Matt's play call - bring the numbers, not a tuned constant.
4. Sundered Twins / Break bar spawn decision (content-dungeon.ts:20-23 intentionally omits them; remote branch
   feat/break-bar-wiring exists). Needs Matt's scope call first.
5. Floor Windows (Lock/Window payoffs) and combo event snapshot channels (snapshot.ts:38-48, 110-129).
6. Rewards stub (run-end.ts:29-33) and clause pick UI + key level display.
7. Cinder Blink displacement (cinder.json:266) and F=Charge vs Space=dash authority - see Decisions for Matt.

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

## Standing rules
- Every audit and integration test reproduces through the public path (createRun/stepRun, the bot loop, the server
  room over ws, the client worker host). A mechanic not wired into the composition root in the same PR is a P0.
- Net/local parity: a telegraph, kick flag, or event that exists on the local path must exist on the net path in
  the same PR. Client events are read only via client/CLAUDE.md SnapshotEventLatch, never nextSnapshot.events.
- Model: Opus for Keyrun development lanes (D11); Haiku for read-only recon, lint and bookkeeping.
