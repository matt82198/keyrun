# ORACLE.md: the TypeScript parity oracle

PURPOSE: this artifact exists to freeze the TypeScript sim's exact behaviour as fixtures so the C# port can be proven tick-for-tick identical before the TS code is deleted; status: oracle

The TS sim (sim/, bots/src/, content/, frozen at main `6e1548d`) is the reference. The fixtures in
`game/sim/Keyrun.Sim.Tests/Fixtures/oracle/` record what it does. The C# port (Keyrun.Sim, Keyrun.Bots,
Keyrun.Content) is correct when it reproduces those fixtures exactly. Parity means EQUAL, not winning:
the traces record whatever the TS does, including wipes.

- Generator (reference implementation of this spec): `tests/oracle/format.ts` (format), `tests/oracle/traces.ts`
  (scenarios), `tests/oracle/vectors.ts` (vectors), `tests/oracle/generate.ts` (manifest and I/O).
- Drift guard: `tests/oracle/oracle.test.ts`. It runs inside `npm test` (the required `test` check, about 2 s).
  It regenerates every fixture into a temp directory and asserts each one is byte-identical to the committed copy.
- Re-record (only for a deliberate, explained change): `ORACLE_WRITE=1 npx vitest run tests/oracle`.
- Fixture bytes: ASCII, LF line endings, trailing newline. `Fixtures/oracle/.gitattributes` pins `eol=lf`, so a
  Windows `core.autocrlf=true` checkout keeps the bytes the hashes cover.

## 1. Projection line

One ASCII line per tick, no trailing separator:

```
t=<tick>;pull=<pullIndex>;slack=<slackTicks>;a=<actor>|<actor>|...
<actor> = <id>,<hp>,<x>,<z>,<yaw>,<alive>,<res>,<castAbility>,<castEnd>
```

| Field | Source (TS) | Rendering |
|---|---|---|
| `t` | `run.state.tick` | decimal int |
| `pull` | `run.encounter.pullIndex` | decimal int |
| `slack` | `run.encounter.slackTicks` | decimal int |
| `id` | `ActorState.id` | decimal int |
| `hp` | `ActorState.health` | decimal int |
| `x`, `z` | `ActorState.pos.x`, `pos.z` (mm; `y` is not projected) | decimal int, may be negative |
| `yaw` | `ActorState.yaw` (1/1024 turn) | decimal int as stored (not re-normalised) |
| `alive` | `ActorState.alive` | `1` or `0` |
| `res` | `ActorState.resource` | decimal int |
| `castAbility` | `ActorState.castId` | the ability id, or `-` when null |
| `castEnd` | `ActorState.castEndsTick` | decimal int, or `-` when null |

- Actors: every actor in `state.actors`, dead ones included, sorted ascending by numeric id. Groups are joined
  with `|`. An empty actor set renders `a=` with nothing after it.
- Integers: base 10, no leading zeros, no `+`, a leading `-` for negatives, and `-0` renders `0`. The TS writer
  throws if a projected number is not a safe integer, so a float in sim state fails the oracle and is never
  recorded as if it were exact. C#: `long`/`int` with `ToString(CultureInfo.InvariantCulture)`.
- Ability ids must match `[A-Za-z0-9._:-]+` and must not be `-`. The writer throws otherwise.

## 2. Hashes

- FNV-1a 32: offset basis `0x811c9dc5`, prime `0x01000193`, for each byte `h = (h ^ b) * prime mod 2^32`.
  Rendered as 8 lowercase hex digits, zero-padded. Test vectors: `""` -> `811c9dc5`, `"a"` -> `e40c292c`,
  `"foobar"` -> `bf9cf968`.
- Record hash `h`: FNV-1a 32 over the projection line's ASCII bytes, with no newline.
- Trace hash (`traceHash` in the header and in manifest.json): FNV-1a 32 over the concatenation of every
  record's projection line, each followed by one `\n` (0x0a), in record order starting at t=0. That is the same
  as one FNV state chained across the lines.
- The TS `hashState` (sim/core/state.ts:118, FNV over JS JSON text) is NOT part of the oracle and is not
  reproduced in C#.

## 3. Trace file `<name>.trace.jsonl`

Line 1 is the header. It is canonical JSON (section 5) with these keys: `kind` (`keyrun-oracle-trace`),
`version` (1), `name`, `source` (`sha`: `6e1548d`, the frozen TS tree), `scenario` (seeds, key, stop rule),
`setup` (the setup file name), `fullEvery` (20), `records`, `firstTick` (0), `finalTick`, `result` and
`resultTick` (from `run.runEnd`; null for golden runs), and `traceHash`.

Lines 2 and on hold one record per tick, in tick order, with the keys always in this order:
`{"t":<tick>,"h":"<hash>","p":"<projection>","c":[...],"e":[...]}`

- `t`/`h`: always present.
- `p`: the full projection line. Present on the first record, the last record, and every record with
  `t % 20 == 0`. A diverging C# tick is diffed against the nearest earlier `p`.
- `c`: present when non-empty. It holds the caller commands (script or bots) passed to `stepRun` for the
  transition INTO tick `t`, so each one is addressed to tick `t-1`. Encounter-generated enemy casts are not
  in `c`; the sim derives those itself.
- `e`: present when non-empty. It holds every SimEvent returned by that `stepRun` call, in emission order, each
  as canonical JSON.

Tick semantics: record t=0 is the state `createRun` returns, before any step. Record t (t >= 1) is
`run.state` after the `stepRun` call that advanced the tick from t-1 to t. Golden runs stop at their fixed
tick count. Bot runs stop after the first step whose `run.runEnd.result` is non-null (that step's `e` holds the
`run_end` event), or at tick 24000.

Recommended C# check: replay the setup, compute each tick's projection, and compare `h`. At the first
mismatch, report `t`, the expected and actual line (or the nearest earlier `p`), and that record's `e`.
Compare events by parsing the JSON or by re-rendering it canonically.

## 4. Setup file `<setup>.setup.json`

This is one canonical-JSON line holding the inputs `createRun` received, so the C# side can check its content
adapter (lane 1D) against the TS output field by field: `seed` (golden only), `party` (PartySpec[]), `dungeon`
(the sim DungeonDef), `content` (the RunContent exactly as `run.content` holds it after createRun), and
`runOptions`. The bot setups also carry `keyLevel`, `dungeonId`, `humanClassId`, `humanActorId`,
`tankActorId` and `bossActorId`. Bot runs at the same key share one setup file.

## 5. Canonical JSON

This is compact JSON with object keys sorted by UTF-16 code unit order (ordinal for ASCII). Members whose
value is `undefined` are omitted. There is no whitespace. Strings are escaped as JSON.stringify does and are
ASCII. Numbers use JS's shortest round-trip rendering: integers have no fraction or exponent, and `-0` renders
`0`. The current traces and setups contain no non-integer numbers (checked: 0 matches of `[:,[]-?\d+\.\d+`).
Floats appear only in `botprng.vectors.json` `value`.

## 6. Fixtures (main 6e1548d)

| File | What | Records / rows | traceHash |
|---|---|---|---|
| `content-pull.trace.jsonl` | tests/sim/golden/build-content-pull.ts scenario and commands, 400 ticks | 401 | `5bdadaf1` |
| `scripted-pull.trace.jsonl` | tests/sim/golden/scripted-pull.json on tests/sim/integration/fixtures.ts content, 600 ticks | 601 | `a2bf97d7` |
| `bots-hv-k2-s7.trace.jsonl` | Hollow Vault key 2, run seed 7 = bot PRNG seed 7, five bots (human slot = bot warden), skill 100 | 4974, `depleted` at t4973 | `3c5796a1` |
| `bots-hv-k2-s11.trace.jsonl` | same at seed 11 | 4974, `depleted` at t4973 | `3c5796a1` |
| `bots-hv-k10-s7.trace.jsonl` | same at key 10, seed 7 | 6985, `timed` at t6984 | `b947e653` |
| `content-pull.setup.json`, `scripted-pull.setup.json`, `hollow-vault-k2.setup.json`, `hollow-vault-k10.setup.json` | createRun inputs (section 4) | | |
| `fixed.vectors.json`, `atan.vectors.json`, `prng.vectors.json`, `botprng.vectors.json`, `intdiv.vectors.json` | section 7 | about 10k each | |
| `manifest.json` | sizes and FNV of every file, trace summaries, `loadContent()` counts, frozen-source hash | | |

Observed facts the C# side should expect:
- Seeds 7 and 11 at key 2 give byte-identical record bodies (only the header differs). At skill dial 100 neither
  the run seed nor the bot PRNG seed changes anything that is projected or emitted, which agrees with STATE.md's
  "deterministic, identical across seeds". The seed still enters `state.prng`, which is not projected.
- Key 2 clears pulls 0, 1 and 2 (t1022, t2411, t2706), then the party dies on the boss and the run ends
  `depleted` at t4973. Key 10 clears all four pulls (t1202, t2861, t3197, t6983) and ends `timed` at t6984.
- `loadContent()` counts (for lane 1D): abilities 38, effects 22, classes 3, combos 13, enemies 7,
  dungeons 1, clauses 4, facets 60.

## 7. Vector fixtures

Each vector file is one JSON object: `header` (file, kind, source sha, version), then `functions[]`. Each
function has `meta` (`name`, `from`, `columns`, `notes`, `count`) and `rows`, one per line. Each row lists
the input columns, then the output columns. An output of `"throw"` means the TS function throws for that
input. Inputs are hand-picked edge cases (zero, negatives, uint32 wrap, rounding ties, octant and axis
boundaries, extremes), followed by seeded random rows drawn from sim/core/prng.ts.

| File | Functions | Edge focus |
|---|---|---|
| `fixed.vectors.json` | isqrt, length, scale, normaliseTo, yawBetween, angleDiff | isqrt perfect squares and their -1 up to 2^50, negatives and fractions throw; scale and normaliseTo ties (round half AWAY from zero, sign from `n<0 !== d<0`); angleDiff with negative inputs and the 512 boundary (result in (-512, 512]) |
| `atan.vectors.json` | yawFromComponents | (0,0) -> 0, the four axes, the diagonals, the octant swap at az == ax, large magnitudes |
| `prng.vectors.json` | seedState, nextInt | seeds 0, -1, 2^31, 2^32 (wraps to 0), fractions (ToUint32 truncates); nextInt for n = 1, 2^31+1 (rejection path), 2^32 (the raw xoshiro output); chained draws per seed; n <= 0 and fractional n throw |
| `botprng.vectors.json` | seedPrng, stepPrng | the 2^53 product hazard (below); `value` is a double `n3 / 4294967295` |
| `intdiv.vectors.json` | jsIntDiv: floor, trunc, ceil, round of n/d, and JS `n % d` | every sign combination and the .5 ties; `round` is JS Math.round (ties toward +infinity, so -2.5 -> -2) |

## 8. Integer hazards: what the TS uses where

JS numbers are doubles. Every sim value stays below 2^53, so `Math.floor(n/d)` and `Math.trunc(n/d)` give the
exact integer quotient. They differ only when `n/d` is negative and not an integer: floor rounds toward
-infinity, trunc toward 0. The C# port needs `IntMath.FloorDiv`, `IntMath.TruncDiv` (C# `/` on integers),
`IntMath.CeilDiv`, `IntMath.RoundHalfUp` (JS Math.round), `IntMath.RoundHalfAwayDiv` (fixed.ts roundDiv), and
`IntMath.JsRem` (C# `%`, which has the same truncating sign rule as JS `%`). A bare `/` on a signed value that
can be negative is a review defect unless the TS used trunc at that site.

| Site (6e1548d) | TS operation | C# |
|---|---|---|
| sim/core/fixed.ts:21 roundDiv | `sign * Math.floor((|n| + floor(|d|/2)) / |d|)`: half away from zero | RoundHalfAwayDiv |
| sim/core/fixed.ts:36 isqrt | Math.floor on non-negative values | `/` (non-negative) |
| sim/core/fixed.ts:93 angleDiff | `((b - a) % 1024) + 1024`, then `% 1024`; JS `%` truncates | JsRem (= C# `%`) |
| sim/core/atan.ts:48, :57 | Math.floor on non-negative values (roundDivNonNeg, idx) | `/` (non-negative) |
| sim/combat/damage.ts:30-31 | Math.trunc of `x * (10000 + bp) / 10000` | TruncDiv |
| sim/combat/movement.ts:46-48, :61, :67-68 | Math.trunc (speed, step dx/dz, half, push) | TruncDiv |
| sim/combat/targeting.ts:99-100 | Math.trunc of forward/perp over FACING_Q (4096) | TruncDiv |
| sim/combat/targeting.ts:53 | `Math.round(angle1024 / 8)`, then double `%`; the argument can be a half-integer (`angle1024 / 2`, :75) | RoundHalfUp over a rational (2*angle / 16), then JsRem |
| sim/combat/threat.ts:11 | Math.trunc | TruncDiv |
| sim/combat/cooldowns.ts:26 | Math.ceil(ticksRemaining / cooldownTicks) | CeilDiv |
| sim/combat/effects.ts:132 | Math.ceil(used / perStack) | CeilDiv |
| sim/encounter/telegraphs.ts:88 | `Math.round(r / STEP)` with `r = halfAngle`, which can be x.5 | RoundHalfUp over a rational |
| sim/encounter/telegraphs.ts:109 | `halfAngle = clamp(angle1024, 0, 511) / 2`: a NON-INTEGER double for odd angles | keep it as a numerator over 2, never `int / 2` |
| sim/encounter/telegraphs.ts:96-121, targeting.ts cone and line math | products such as `dot*dot` and `distSq*cos*cos` reach about 1e15 | `long` |
| sim/encounter/clauses.ts:137 | Math.floor(tick / interval), non-negative | `/` |
| sim/encounter/clauses.ts:136, scripts.ts:80, :85 | JS `%` on non-negative ticks | `%` |
| sim/encounter/clauses.ts:145, tempo.ts:34, :40, :46 | Math.trunc | TruncDiv |
| sim/encounter/rewards.ts:77 | Math.round(keyLevel) | RoundHalfUp |
| sim/encounter/rewards.ts:83 | Math.floor((key - 2) / 2); key is clamped >= 2 | FloorDiv |
| sim/core/prng.ts | uint32 via `>>> 0` and Math.imul; `(s1 * 5)` and `* 9` stay below 2^53, so they are exact | `uint` arithmetic, unchecked |
| sim/core/prng.ts nextInt | `limit = floor(2^32 / n) * n`, so n > 2^32 never returns | `ulong` limit |
| bots/src/skill.ts seedPrng | `(s * 2654435761) >>> 0` in DOUBLES: above 2^53 the product can be rounded first (seed -1: JS 1640531968, uint32 multiply 1640531535) | reproduce the double product (seeds 7 and 11 are exact) |
| bots/src/skill.ts stepPrng | the float `value = w / 0xffffffff` decides mistakes | double division, same IEEE result |

Mutation evidence (2026-10-18, lane 1B):
- Changing damage.ts:30 from Math.trunc to Math.floor left all 5 traces byte-identical. No trace hits a negative
  modifier product. The drift test still went red, but only through manifest.json's frozen-source hash. The
  floor/trunc distinction at that site is pinned only by `intdiv.vectors.json`, so C# unit tests must cover it
  directly.
- Changing damage.ts:32 to `afterTarget + 1` turned all 5 traces red at the first damaging tick (content-pull
  and scripted-pull at t=21, the bot runs at t=31).

## 9. Freeze and drift

`manifest.json` `source.frozenSourceHash` is an FNV-1a 32 over the path, byte length and bytes of every file
under `sim/`, `bots/src/`, `content/`, `client/src/sim-host/`, `client/src/input/`, `tests/sim/golden/` and
`tests/sim/integration/fixtures.ts`. CR bytes are stripped, so CRLF and LF checkouts hash the same. Any edit
there, comments included, fails `oracle.test.ts`, because the fixtures claim to come from 6e1548d.
Behaviour drift also shows as a byte diff in the trace or vector it hits. The failure message names the file,
line and column.
