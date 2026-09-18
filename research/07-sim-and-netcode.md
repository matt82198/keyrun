# 07 — How should the sim and netcode be built for a 5-player co-op ability game?

## 1. The question

How should a 5-player co-op action game with cooldowns, cast bars, interrupts and buffs be
simulated so it is deterministic, testable headless, playable solo with bots first, and later
networked with an authoritative server — what tick rate, what data flow, what package layout?

## 2. What works

1. **Fixed timestep as the determinism foundation.** Glenn Fiedler: "the behavior of your physics
   simulation depends on the delta time you pass in" — a fully fixed dt is required before
   determinism is even possible, because float rounding makes "close enough" steps diverge. The
   accumulator pattern (`accumulator += frameTime; while (accumulator >= dt) { step(dt);
   accumulator -= dt }`) decouples render rate from sim rate; render interpolates with
   `alpha = accumulator / dt`. Source: https://gafferongames.com/post/fix_your_timestep/
2. **Deterministic lockstep sends inputs, not state.** Age of Empires and Factorio both simulate
   identically on every machine and exchange only commands; bandwidth is proportional to input
   size, not world size — "a physics simulation of one million objects with the same bandwidth as
   just one." Sources: https://gafferongames.com/post/deterministic_lockstep/ ;
   https://www.gamedeveloper.com/programming/1500-archers-on-a-28-8-network-programming-in-age-of-empires-and-beyond
3. **AoE's turn-delay scheduling.** Commands issued during turn N execute at turn N+2 (turns ≈
   200ms) so every peer has time to receive it before it's due — the origin of the "input buffer"
   idea later networking models reuse. Source: same gamedeveloper.com article.
4. **Server-authoritative + client prediction (Overwatch, Valorant).** The server is sole truth;
   each client locally predicts its own actions for instant feedback and reconciles when the
   server disagrees ("the server commits its prediction as truth, and that client is told to
   adjust"). Overwatch runs the client ~RTT/2 + one command-frame ahead so commands arrive "on
   time." Sources: "Overwatch Gameplay Architecture and Netcode" (Tim Ford, GDC 2017) —
   https://www.gdcvault.com/play/1024001/-Overwatch-Gameplay-Architecture-and , summarized at
   https://us.forums.blizzard.com/en/overwatch/t/game-architecture-and-netcode-gdc/301462 and
   https://www.youtube.com/watch?v=vTH2ZPgYujQ ; Valorant: https://www.riotgames.com/en/news/peeking-valorants-netcode
5. **Lag-compensated resolution, not arrival-order resolution.** Valorant rewinds server state to
   "what you were looking at when you pulled the trigger" rather than judging by packet arrival
   order — the server evaluates a command against the tick the *client* intended, using an
   estimated latency offset. Source: https://www.riotgames.com/en/news/peeking-valorants-netcode
6. **Command-stream replay = free regression testing.** If the entire non-deterministic input to
   the sim is an ordered per-tick command list, then seed + command list fully reproduces a run;
   storing that list (not state snapshots) is both the replay format and the test fixture. Same
   principle Factorio and AoE rely on for their own sync-checking.
7. **Spell queueing softens tick/latency edges (WoW).** Patch 2.3.0 let players queue their next
   cast up to ~300ms before the current one ends, so the server executes it the instant the GCD is
   free instead of punishing input timing with a dead zone — the fix for "felt like the server
   dropped my input" complaints. Source: https://us.forums.blizzard.com/en/wow/t/test-your-actual-spell-q-latency/1197000
8. **Genre-appropriate tick rate.** "Twitch shooters need 64Hz or higher; slower co-op and PvE
   games are fine at 20-30Hz" — tick rate is a cost/precision dial, not a universal constant; even
   Fortnite/Apex/PUBG run ~30Hz because per-player CPU cost scales with tick rate, and reflex-aim
   precision isn't the genre's skill test. Source:
   https://edgegap.com/blog/game-server-tick-rate-explained-gameplay-precision-vs-infrastructure-cost
9. **Golden-master / property tests over replays.** Seed the RNG, record command streams, assert
   the resulting state hash is byte-identical on every CI run (golden replay); separately, fuzz
   randomized legal command streams and assert invariants (health never negative, replaying the
   same seed+commands twice yields identical hashes) — a direct restatement of Keyrun's own
   invariant #1. Source: https://stevenschwenke.de/whatIsTheGoldenMasterTechnique

## 3. What fails / what breaks

1. **Lockstep over unreliable transport hitches hard.** If frame N can't simulate without input N,
   the whole match stalls waiting for a resend; at high latency + packet loss this produces visible
   freezes for every player, not just the laggy one. Source:
   https://gafferongames.com/post/deterministic_lockstep/
2. **Lockstep bottlenecks on the slowest peer and has no cheat boundary.** AoE's "Speed Control"
   throttled every player's sim to the weakest machine's frame rate; and since every peer runs the
   authoritative sim with no server arbiter, a modified client can lie about its own state — fine
   for trusted co-op friends, wrong once "authoritative server" is a stated goal.
3. **Floating point silently breaks cross-platform determinism.** Different compilers, instruction
   sets, and especially transcendental functions (sin/cos/pow/exp) are not required to produce
   bit-identical results across environments — "you can't use SSE or SSE2 for floating point,
   because it's too under-specified to be deterministic." Source:
   https://gafferongames.com/post/floating_point_determinism/
4. **Silent desyncs are nearly undebuggable in production.** Factorio's own postmortems describe
   subtle per-tick differences that "multiply over time" until a checksum mismatch appears minutes
   after the actual bug — by which point the trigger is long gone from the visible state. Source:
   https://factorio.com/blog/post/fff-188
5. **Arrival-order resolution feels unfair for interrupts/races.** Judging a kick-vs-cast race by
   whichever packet the socket read first is nondeterministic (depends on OS scheduling / network
   jitter) and irreproducible in a replay — exactly the "coin flip" Keyrun's CLAUDE.md forbids.
6. **WebRTC DataChannel's unreliable/unordered delivery is a poor fit for command streams.** A
   command-stream sim needs every command in order — hand-rolling reliability atop an intentionally
   unreliable transport is more work than using one that already guarantees it. Source:
   https://www.videosdk.live/developer-hub/webrtc/websocket-vs-webrtc-datachannel

## 4. Adopt / adapt / avoid — and the concrete architecture proposal

**Adopt:** fixed-timestep accumulator loop (Fiedler); server-authoritative + client-prediction as
the eventual network model (Overwatch/Valorant); command-stream-as-replay for testing; lag
compensation by intended-tick, not arrival order; fixed/integer math + a seeded PRNG over floats
and `Math.random`.

**Adapt:** lockstep's *idea* (send inputs, simulate identically everywhere) survives as the shape
of the shared `sim/` package — the same `step(state, commands, tick)` runs under a bot loop, a solo
client loop, and later a server loop. What changes between topologies is only *who is trusted to
call it* and *how commands arrive*, never the function. WoW's spell-queue window becomes Keyrun's
cast-bar interrupt window: size it in ticks with an explicit latency margin, not balance feel alone.

**Avoid:** true peer-to-peer lockstep (no server-authority path, slowest-peer bottleneck, no cheat
boundary); transcendental math (sin/cos/pow/exp) in sim-affecting code; `Math.random`; judging
races by network arrival order.

### Tick rate: 30 Hz (33.33ms)

Keyrun is an ability/cooldown/interrupt game, not a twitch-aim shooter — per §2.8, 20-30Hz is the
established band for that genre. 20Hz (50ms) is coarse enough to make interrupt-window edges feel
arbitrary; 60Hz buys precision this game's skill test (rotation, kick timing, positioning) doesn't
need, at 2x the server CPU and bandwidth of 30Hz. 30Hz gives ~33ms granularity — well under human
reaction time (~200ms) — with headroom to size cast-bar interrupt windows (§ below) comfortably
above real-world RTT. One constant, used by the bot loop, the solo client loop, and the future
server loop, so switching topology never requires retuning feel.

### Package layout (maps onto Keyrun CLAUDE.md's declared stack)

- `sim/` — pure TS, no DOM/Node/browser imports. `initState(seed, party)`, `step(state, commands,
  tick) -> state`. Owns `sim/math/fixed.ts` (fixed-point arithmetic) and `sim/math/rng.ts` (seeded
  PRNG, e.g. mulberry32/xorshift128+). Depends only on `content/`.
- `content/` — class/talent/gear/dungeon/boss data + small pure scripts; sim stays generic.
- `bots/` — party AI reading sim state and emitting the same `Command` objects a human would; built
  *before* any human client, to prove the command interface end-to-end.
- `client/` — Canvas 2D render + input capture; imports `sim/` and `bots/` directly in phase 1
  (client calls `sim.step` itself — solo play is trivially "lockstep of one").
- `server/` (later phase) — Node process importing the identical `sim/` package unchanged; becomes
  sole authority; adds WebSocket transport, per-tick command buffering, client-offset lag comp.

**What is shared:** `sim/`, `content/`, and the `Command`/`State` types — one package, imported
unmodified by `bots/`, `client/`, and `server/`. Only transport, rendering, and input capture differ
per target; game rules are never duplicated.

### Data flow

Phase 1 (solo/bots, no network): input or bot policy → `Command{type, actorId, targetId?, tick}` →
local queue → `sim.step(state, commands[tick], tick)` every fixed tick → new state → Canvas render,
interpolated between tick N-1 and N using `alpha = accumulator/dt` each `requestAnimationFrame`.
Discrete outcomes (cast started, interrupt landed, aura applied) snap exactly on tick boundaries —
never interpolated — so nothing game-affecting shows as a blended fiction.

Phase 2 (networked): same `Command` objects, now over WebSocket; server buffers commands per
declared tick using each client's estimated clock offset (Overwatch-style) so "tick 500" means the
same instant for every sender; `server.sim.step()` is the only call whose output is truth; client
keeps predicting locally with the identical `sim.step()` for zero-latency feedback and reconciles
(snap or resimulate-from-last-confirmed-tick) on mismatch.

### Transport: WebSocket over WebRTC DataChannel

Ordered-reliable delivery is a feature here, not a cost — a command stream needs every command in
order, which WebSocket already guarantees, versus WebRTC's unreliable/unordered strength (built for
droppable state like FPS snapshots) plus signaling/ICE/STUN/TURN infra this project doesn't need.
Revisit only if playtesting shows head-of-line blocking hurting feel.

### Who wins a kick at 80ms

Boss cast starts at server tick `S`, duration `D` ticks; the interrupt window is the last ~9 ticks
(~300ms at 30Hz, sized like WoW's spell-queue window). Player A's kick is evaluated against the
tick the server believes A intended (RTT/2-adjusted), not the tick the packet happened to arrive
on. At 80ms RTT, well under the 300ms window, a kick pressed against the player's own locally
predicted cast bar lands with ~220ms of margin. Two interrupts in the same window (rare in 5-player
co-op) break by fixed player-id order — deterministic and replay-reproducible, never "whichever
socket read first." Size every interrupt window in ticks with margin > expected RTT + 1-2 ticks.

### Determinism in JS specifically

Basic `+ - * /` on JS numbers are IEEE-754 doubles, spec-deterministic within an engine —
reassuring since client and server are both very likely V8. The real risks are `Math.sin/cos/pow/
exp` (not spec-guaranteed bit-identical across engines/versions) and `Math.random` (unseedable,
unspecified algorithm). Fix: ban transcendentals from sim-affecting code (top-down grid movement
doesn't need trig anyway); represent every sim-affecting value — position, cooldown, cast timer,
health, damage — as fixed-point/integer ticks, never float seconds; replace `Math.random` with a
seeded integer PRNG owned by `sim/math/rng.ts`, advanced only inside `step()`.

### Testing

Golden replays: record seed + command stream, assert final/per-tick state hash is byte-identical on
every CI run. Property tests (`fast-check`): fuzz randomized legal command streams, assert
invariants (no negative health, no early cooldown) and determinism itself (same seed+commands
replayed 3x → identical hash, catching Map/Set iteration-order bugs). Replay-diffing bisects which
tick a regression entered — the technique Factorio's postmortems describe for hunting desyncs.

### Build order, bots-first

1. `sim/` headless core + fixed-point math + seeded PRNG + `Command`/`State` types, driven purely
   by Vitest — no rendering, no input. First golden-replay test is the project's birth certificate.
2. `content/` minimal slice: one class, one room, one boss with one cast-bar ability — enough to
   exercise a cooldown, a cast bar, and an interrupt.
3. `bots/` emitting legal `Command`s for all 5 party slots — proves the command interface and
   yields a fully-automated 5-bot run usable as more golden replays and fuzz seeds.
4. `client/` Canvas 2D render + one human slot replacing one bot, local-authority loop (client calls
   `sim.step` directly) — "playable solo with bots," the first time a human feels the tick rate and
   interrupt window before any networking exists.
5. Only then: `server/` authoritative tick + WebSocket transport + prediction/reconciliation, reusing
   `sim/` unchanged — networking adds an authority and a wire protocol, never rewrites the rules.

Every step before #5 is a strict subset of what #5 needs — nothing built is thrown away when the
server phase opens.

## 5. Verdict

Build one pure, deterministic `sim/` package — fixed-point math, a seeded PRNG, no transcendentals,
`step(state, commands, tick)` at a fixed 30Hz — and never fork it per target. Bots, the solo
client, and the future authoritative server all call the identical function; only who's trusted to
call it and how commands arrive over the wire changes. This makes the command stream the one
replay/test format (golden replays + property fuzzing) from day one, makes solo-with-bots and
later-networked the same codepath instead of two builds, and resolves interrupts by lag-compensated
intended-tick with a fixed tiebreak — never arrival order — satisfying Keyrun's own "never a coin
flip" invariant for free.
