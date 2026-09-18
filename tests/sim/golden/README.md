# Golden replay fixtures

A golden fixture pins the final hash of a deterministic replay so a future refactor of the sim
cannot silently change simulation behaviour. It is the `replay.ts` -> `goldenHash()` contract
made durable: same seed + same command stream + same rules must always produce the same hash.

## Format

A golden fixture is a plain object (inline in a test file, or a `.json` file loaded by one):

```json
{
  "seed": 12345,
  "commands": [
    { "tick": 0, "actorId": 1, "kind": "move", "dir": { "x": 1000, "z": 0 }, "yaw": 0 },
    { "tick": 3, "actorId": 1, "kind": "stopcast" }
  ],
  "expectedHash": "a1b2c3d4"
}
```

- `seed` -- passed to `replay.apply(seed, commands, rules)` / `createState(seed)`.
- `commands` -- a `Command[]` as defined in `sim/core/commands.ts`; each command must pass
  `validateCommand` or it is silently dropped by `step()` (see `sim/core/tick.ts`).
- `expectedHash` -- the 8-hex-char FNV-1a value from `sim/core/state.ts`'s `hashState()`, computed
  once (`goldenHash(seed, commands, rules)`) and then pinned. A test asserts the two are equal.

## Regenerating

If a fixture's `expectedHash` no longer matches, that is either a real behaviour change (update
the fixture deliberately, in the same commit that explains why) or a regression (fix the code,
do not retune the fixture to match -- see the project's "no fitted green" rule). Regenerate by
calling `goldenHash(seed, commands, rules)` once, in a scratch script or REPL, and copying the
printed value into the fixture; never hand-derive a hash.

## Rules used at this layer

`sim/core` owns no gameplay rules -- `StepRules` is supplied by `sim/combat` (a separate lane).
Core-level golden tests in `tests/sim/core/replay.test.ts` therefore use a small local `StepRules`
stub (spawning/mutating an actor directly) to exercise `step`/`apply`/`hashState` determinism in
isolation. Golden fixtures that assert real gameplay (ability effects, combat math, boss
mechanics) belong under `sim/combat`'s own test suite once that lane's real rules exist, and
should follow this same `{ seed, commands, expectedHash }` shape.
