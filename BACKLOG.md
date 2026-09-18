# BACKLOG — Keyrun (one shared backlog; generation writes, execution reads)

Format: `- [P<n>] <id> — <what> | files: <owned files> | evidence: <why> | status: open|lane|pr#|merged|refuted|deferred`
Ranked by impact-per-effort; file-disjoint items may run concurrently (/highvelocity).

## In flight (2026-09-18)
- [P0] tooling — vitest/tsc/esbuild/CI | merged PR #1
- [P0] docs — research briefs + BIBLE v1 + domain contracts | status: pr (docs/research-and-bible)
- [P0] sim-core — fixed math, prng, step, commands/events, replay | files: sim/core/** | status: pr#4 (55 tests)
- [P0] sim-combat — abilities, casting, interrupts, effects, damage, movement, targeting | files: sim/combat/** | status: lane
- [P0] client-shell — Three.js third-person, input→commands, telegraph decals, fake snapshot stream | files: client/** | status: lane
- [P1] bots-shell — role state machines, telegraph escape, kick assignment | files: bots/** | status: pr#3 (18 tests)
- [P1] screen-shell — one-screen hub, store, key stepper, Play URL | files: screen/** | status: lane
- [P0] content-v1 — classes, combos, talents, enemies, Hollow Vault, clauses as data | files: content/** | status: lane
- [P1] market — is the genre cooked (research/10) | status: lane

## Ready (unblocked once the above merge)
- [P0] integrate-sim — replace local type copies in sim/combat, bots, client, content with imports from sim/core; one step() through content abilities; golden replay of a scripted pull | files: sim/combat/types.ts, bots/src/types.ts, client/src/sim-host/snapshot.ts, content/schema/*, tests/sim/golden/** | evidence: three lanes carry duplicated type contracts by design
- [P0] windows-and-combos — implement BIBLE §4 in sim/combat: window effects with class-band, payoff resolution, combo table lookup, self-combo forbidden, boss self-window alternation rule | files: sim/combat/windows.ts, sim/combat/combos.ts, tests/sim/combat/windows*.test.ts | evidence: bible §4, §9
- [P0] encounter-sim — enemy scripts as state machines, telegraph events with tick horizon, break bar, pull spawning, run timer + Slack, key Tempo scaling | files: sim/encounter/** | evidence: bible §8, §9
- [P0] playable-v1 — client runs the real sim in a worker with bots + content; Hollow Vault end to end; end-of-run choice of three (stub) | files: client/src/sim-host/**, client/src/hud/run-end.ts | evidence: phase 3
- [P1] bots-v1 — bots use content kits (rotation, setup/payoff windows, kicks by ledger, telegraph escape with real shapes) | files: bots/roles/**, bots/shared/** | evidence: bible §4, §5
- [P1] talents-and-facets — apply talent patches and gear facets to ability defs at run start; screen shows forks and five slots; free respec | files: content/apply.ts, screen/src/panels/** | evidence: bible §6, §7
- [P1] progression-loop — tier cap 10, choice-of-three at run end, Ash, crafting charges, run history | files: screen/src/store.ts, screen/src/panels/run.ts, sim/encounter/rewards.ts | evidence: bible §7 (D7)
- [P2] clauses — the four Clauses as sim modifiers, one active per run, pick-of-three on the screen | files: sim/encounter/clauses.ts, screen/src/panels/run.ts | evidence: bible §8
- [P2] pings — G key marks target/spot; bots respond to pings | files: client/src/input/ping.ts, bots/shared/pings.ts | evidence: bible §10
- [P2] tick-rate-measure — measure 20 vs 30 Hz feel on the playable; decide D9 with numbers | evidence: research/07
- [P3] server — authoritative tick, WebSocket, lobby/queue, backfill (phase 4) | evidence: research/07, bible §10

- [P2] integer-atan2 — `sim/core/fixed.ts` yawBetween uses Math.atan2 then rounds to a 1024 bucket; replace with an integer CORDIC/lookup atan2 so no float exists on the sim path (invariant 1) | files: sim/core/fixed.ts, tests/sim/core/fixed.test.ts | evidence: sim-core lane's own deviation note, PR #4 | status: open

## Refuted / deferred (with evidence)
- (none yet)
