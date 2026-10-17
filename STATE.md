# STATE.md — Keyrun

**Intent (Matt, 2026-09-18):** "isolating m+ from WoW and just making it an open and queue game like Dark and Darker,
and let people manage their characters from a simple screen... study other mmos, dungeon games, gear progression, and
find specifically what people find exciting/fun, build talent trees, classes, the dungeon POC with initial enemies,
abilities, a boss. Mechanics like interrupts, but be more creative than blizzard devs... 4-5 rotational abilities per
class max, 10 total keybinds should be plenty for rotation, movement, utility... the goal is class synergy." No art
assets; pure POC.

## Locked decisions
- D1 **Stack**: TypeScript for sim, content, client, bots, server; Vitest; esbuild; Three.js for the 3D client; no engine.
- D2 **Bots-first**: the first playable is one human plus bot party against the dungeon; networking is phase 4.
- D3 **3D MVP** (Matt, 2026-09-18: "2d is not playable, please 3d mvp"): third-person camera, WASD + mouse-look,
  placeholder geometry only. The sim stays headless and deterministic; 3D is the renderer plus a flat-arena
  movement model (no navmesh in the POC). Readability of mechanics beats everything.
- D4 **Constraints as design**: ≤5 rotational abilities, ≤10 keybinds, synergy-first classes.
- D5 **Research before design**: no class, talent, gear, or boss mechanic enters the bible without a cited
  precedent for WHY it is fun — but the precedent is the reason, never the design.
- D6 **Original, not a copy** (Matt, 2026-09-18: "it does not need to be a copy of any of the big games, use your
  creative reasoning"): the bible must invent Keyrun's own classes, synergy system, mechanics vocabulary and loop.
  Fellowship (Chief Rebel, 2025) is the closest precedent — reviewed well while admittedly half-baked — and is studied
  specifically for the gap between what it promised and what it shipped.

- D7 **Bounded progression** (Matt, 2026-09-18: "there is an ending to progression, a technical bis that is hard to
  achieve... because otherwise characters scale infinitely and break the game"): power has a cap; best-in-slot is
  reachable but hard; post-cap progression is cosmetic, prestige, and new keys — never more stats. Progression is
  the most important system in the bible.

- D8 **North star** (Matt, 2026-09-18): "m+ at its core is extremely fun, but it lives in a game where every class is
  gutted, way too easy, and it is just boring right now. I miss being able to log in during late DF to early TWW and
  get into high keys instantly and play with people who were chill." Three tests every design decision must pass:
  (1) classes with real depth and keys that are genuinely hard; (2) from launch to a high key in under a minute,
  no gatekeeping; (3) the social defaults make strangers chill — mercy, backfill, no score-shaming.
- D9 **Tick rate — DECIDED 2026-09-18 (orchestrator, unattended; research/11):** stay at 20 Hz. Measured on the
  playable (65 s/arm, same seed): 30 Hz cut input→tick p95 from 51 ms to 34 ms but cost +43% CPU, +50% bytes/s,
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
- Phase 1 (research day): 9 cited briefs (incl. Fellowship) → design/BIBLE.md v1 (Matt reviews the taste calls).
- Phase 2: sim/ rules engine (deterministic, tested) + content v1 (3 classes, 1 dungeon, 1 boss).
- Phase 3: bots/ + client/ (Three.js) → playable in a browser tab, one human + bots.
- Phase 4: server/ authoritative tick + queue/lobby + character screen → 5 humans.
- Phase 5: tuning with Matt playing.

## 2026-10-17 highvelocity burn (D10)
Ledger bound from GitHub (gh pr view / gh repo view, 2026-10-17). Brief-supplied heads for #61 and #63 were stale; live values recorded.
- D10 (MVP = 3D gameplay) and D11 (Opus for Keyrun dev lanes) are recorded above (PR #59, f5f4ac6b).
- Recon (read-only, main 8e97ada): run-options P0 LANDED (#54 be56655). typecheck 0; vitest 724.
- VERIFIED DEFECTS at recon: (a) keypress never cast; the key label was sent as the ability id. (b) no enemy ever moved, so melee never landed.
- MISSING vs the 3D bar: kickable flag and interrupt feedback; telegraph kinds; Window ring; boss HP/phase HUD; Sundered Twins spawn; boss colour; Space jump.
- Only BIBLE [DECIDE] blocking 3D: combo effects and numbers (Lock gate, Window payoffs).
- #60 fix/enemy-melee-reaches-party: MERGED 2e332e38 (gh mergeCommit). sim/encounter/chase.ts wired at scripts.ts; first hit t38 seed 1; golden content-pull hash 614b2c96 -> 755428e2 (eventCounts identical); vitest 726.
- #61 feat/telegraph-kinds: MERGED 589b6e68 (gh mergeCommit; headRefOid at merge d80e5d75). Classes: spread #CC79A7 dashed; stack #56B4E9 dotted, not danger; boss #E74C3C double; avoid #F0E442 solid. Bots no longer flee stack circles. Net path lacks telegraphClass.
- #62 fix/keybind-slot-map: MERGED c0483801 (gh mergeCommit). Single authority client/src/input/slot-abilities.ts resolveSlotAbilities (1-5 rot1-5, Q interrupt, E defensive, R utility, F movement), computed in sim-host/run-setup.ts and carried in worker ready; read by deriveAbilityCommands and render/ability-bar-model.ts; abilityIdForAction deleted. 14 public-path tests; vitest 739.
- #63 feat/kickable-cast-flag: OPEN, headRefOid ddfe3bf5 (gh). ActorCast.kickable; Snapshot.events (one-shot, cap 16); render/castBar.ts gold #f4d35e / grey #8d99ae / red flash #ff2e63 for 10 ticks. Flash needs hud-wiring.ts to pass nextSnapshot.events to actorPool.sync. Net path lacks kickable and events.
- #64 docs/readme-run-and-net: OPEN, headRefOid fdb18368 (gh).
- Boss colour: attempt stopped. Boss is identifiable only via ActorSnapshot.classId ending ".boss" (content-dungeon.ts:79) or id 900. Plan: actors.ts:86 passes raw.classId; roleColor COLOR_BOSS 0x9b59b6.
- Open source: repo matt82198/keyrun visibility is PRIVATE (gh repo view, 2026-10-17). Matt 2026-10-17: "you can open source the code for keyrun too". Lane running: full-history secret scan, MIT LICENSE PR, visibility flip gated on zero hits.
- Landing: repo auto-merge is OFF; PRs land by direct squash, proven with pr_merged_check.

## NEXT STEPS (2026-10-17 ladder, D10)
Ranked. Every rung must be playable and judged in the 3D client; Phase 5 "Matt playing" stays the acceptance test.
1. L8 net and wiring parity: hud-wiring events argument (pass nextSnapshot.events to actorPool.sync); actors.ts classId hook and roleColor boss (COLOR_BOSS 0x9b59b6); server joined slot map; server snapshot kickable, events and telegraphClass.
2. L4 Window ring.
3. L5 boss HP bar and Sundered Twins (Lock trigger needs the combo [DECIDE]).
4. L6 camera feel and Space jump.
5. L10 keybinds.json 11 -> 10 per D4.
6. Review follow-ups from #60-#64.
Matt decisions parked: enable repo auto-merge?; combo effects and numbers; MIT vs other licence.
Phase 5 remains "Matt playing".
Standing rule: every audit and integration test reproduces through the public path (createRun/stepRun, the bot loop, the server room over ws, the client worker host); a mechanic that is not wired into the composition root in the same PR is a P0, not a note.
