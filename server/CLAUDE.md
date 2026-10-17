# server/ — the authoritative multiplayer host (phase 4)

**Purpose**: the same `sim/` package (`createRun`/`stepRun`, `sim/CLAUDE.md`'s contract), now run by a
trusted Node process instead of a browser worker. Clients send `Command`s over WebSocket; this
process is the only one whose `stepRun` output is truth. Mirrors `client/src/sim-host`'s Snapshot
and Command shapes (read-only reference, not imported) so a later lane can swap the client's local
worker host for a thin network client with minimal shape changes.

## Hard rules (inherited from sim/CLAUDE.md, restated for this domain)
- `server/` never forks `sim/` logic — it imports `sim/index.ts`'s `createRun`/`stepRun` unchanged
  and ticks at `sim/core/tick.ts`'s `TICK_HZ` (single source of truth; never a local literal).
- Every inbound WebSocket message is untrusted: parse defensively, validate every `cmd` payload
  against `sim/core/commands.ts`'s `validateCommand` (after forcing `actorId` to the sender's bound
  actor — never trust a client-supplied `actorId`), and never let a malformed message crash the
  process. Reject/ignore, never throw.
- Determinism is the product: given the same seed, party, content, and command stream, the server's
  `hashState(run.state)` must equal a local `createRun`/`stepRun` replay of the same stream. Party
  composition (role → actorId → classId → spawn) is fixed by `roster.ts`'s `ROLE_ROSTER`, never by
  join order, so a room's underlying `Run` is bit-identical regardless of who (human or bot) ends up
  controlling which slot.
- Late commands (a client's declared `tick` already in the past by the time the server processes it)
  are never silently dropped: `room.ts` re-stamps them to the room's current tick and reports a
  `late` message back to the sender. This is a server-level policy on top of `sim/core/tick.ts`'s own
  silent-drop behavior, which stays as the fail-closed floor for anything that slips through.
- One `content/index.ts` `loadContent()` call per process (`content-setup.ts`); `sim/` never imports
  `content/` internals (unchanged from `sim/CLAUDE.md`) — `server/` does the wiring, same role
  `client/src/sim-host/run-setup.ts` plays for the browser.

## Layout (each file ≤190 LOC)
- `protocol.ts` — `PROTOCOL_VERSION`, the client/server WebSocket message unions, and inbound
  validation (`parseClientMessage`).
- `snapshot.ts` — `ActorSnapshot`/`TelegraphSnapshot`/`Snapshot` (mirrors `client/src/sim-host/
  snapshot.ts`'s field names) plus `buildSnapshot(run)`, extended with a `stateHash` field (server-
  only addition — the wire-determinism proof; client has no equivalent need for it). D10 net
  parity: `ActorCast.kickable` and the one-shot `events` channel are built by IMPORTING the client
  host's pure `snapshot-codec.ts` `toCast`/`interruptEvents` — one rule, never a server copy.
- `telegraph.ts` — `toTelegraphSnapshot` is `client/src/sim-host/telegraph-map.ts`'s own (imported:
  `telegraphClass` + class-derived `danger` on the wire); `toBotTelegraph` is too (same class-derived `danger`).
- `joined` (main.ts) carries `slotAbilities` from `client/src/input/slot-abilities.ts`'s
  `resolveSlotAbilities` — the same single authority the local worker host uses.
- Shared pure client modules (`snapshot-codec.ts`, `cast-events.ts`, `cast-feedback.ts`, `player-commands.ts`, `display-info.ts`, `telegraph-map.ts`, `slot-abilities.ts`) are the
  only client imports allowed here; they must stay DOM-free.
- `content-setup.ts` — loads content once, builds `RunContent` + the expanded `DungeonDef` (Hollow
  Vault + its boss pull). **Deviation**: no `sim/encounter/tempo.ts` key-level scaling yet (the
  client's `content-dungeon.ts` applies it) — every key level runs Hollow Vault's base numbers. A
  follow-up lane should port that scaling in; noted, not silently matched.
- `roster.ts` — `ROLE_ROSTER` (tank=warden, healer=chanter, damage×3=cinder — the only role/class
  binding `content/data/classes` currently supports), fixed actorId-by-slot-index assignment, and
  `buildPartySpec` (positions/health/resource per slot, same numbers as `content-party.ts`).
- `run-end.ts` — `buildRunEndPayload`: the `run_end` wire payload (reward roll) once a run has
  ended. Targeting fixup, Slack spend on death/wipe, and run-end DETECTION (timed/depleted/abandoned,
  WHEN it happens) live inside `sim/index.ts`'s `stepRun` itself now (policy-inside-stepRun, P2) —
  no server-side file decides or duplicates any of that; `room.ts` reads `run.runEnd` and forwards
  an explicit human abandon in as `stepRun`'s `abandonRequested` option.
- `lobby.ts` — role queue; instantly forms a room on the first queue (bot-fills every other slot) or
  hands a queued human a room's existing bot-controlled slot of the right role (the same mechanism
  serves initial fill and mid-run backfill).
- `room.ts` — one authoritative `Run`: the fixed-timestep tick loop, per-tick command buffering with
  late re-stamping, bot decision-making for bot-controlled slots, disconnect → bot-takeover-next-tick,
  per-tick broadcast (`snapshot` + `event`), and — once `run.runEnd` is set — `run_end`.
- `main.ts` — `npm run server`: starts the `ws` `WebSocketServer` on `:8792`, wires connections to
  `lobby.ts`/`room.ts`, and is the only file allowed to touch `node:http`/`ws` directly.

## Tests — `tests/server/` (Vitest + a headless `ws` client)
Join/queue/snapshot roundtrip, two-clients-one-room, backfill (disconnect → bot takeover → a new
human re-queuing for that role reattaches), late-command re-stamping, malformed-message rejection,
and the wire-determinism replay (`hashState` from a local `createRun`/`stepRun` run against the exact
command stream the test sent, compared to the server's reported `stateHash`).
- D10 Window net parity: `ActorSnapshot.window` is snapshot-codec.ts's `toWindow` (imported, never copied); `tests/server/snapshot-window.test.ts` proves per-tick parity with the client host and the ws open-then-null lifecycle.
- Bots (STATE (f)/(g)): server bots read the client host's class-derived telegraph `danger` (stack circles are not fled; `tests/server/bot-telegraph-danger.test.ts`) and real content class kits via `loadServerRegistry`'s `setContentRegistry` (room.ts's `EMPTY_CLASS_KIT` is only the unknown-class fallback, same as the client; `tests/server/bot-class-kits.test.ts` proves bots cast over ws).
- Net readability (items 5, #98 deferred a/b): `ActorSnapshot.effects` (snapshot-codec.ts `toEffects`) + `gcdRemainingTicks`, and `cast` events (cast-events.ts `castEvents` over the step SimEvents) are built from the client host rules (imported). `human-casts.ts`: a human `cast` with no targetId gets player-commands.ts `castTargetFor` against THIS tick state (bots and explicit targets untouched), and a press that cast nothing becomes `cast_failed` (cast-feedback.ts). A replay must apply the same resolution to match `stateHash`. `joined.display` = display-info.ts `buildDisplayInfo` (additive; no PROTOCOL_VERSION bump). tests/server/snapshot-net-readability.test.ts.
