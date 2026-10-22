# Golden replay fixtures (frozen oracle data)

A golden fixture pins the final hash of a deterministic replay of the TypeScript simulation, so
the C# port can be checked against what that simulation did: same seed + same command stream +
same rules must always produce the same hash. The two fixtures here, `scripted-pull.json` and
`content-pull.json`, are kept as frozen oracle data. The TypeScript sim and bots were removed in
PR #553, so nothing in this tree recomputes their hashes. The generator
(`build-content-pull.ts`) and the test that loads them (`golden.test.ts`) import the removed
modules and cannot run. `npx vitest run tests/lanes` does not run this folder.

## Format

A golden fixture is a plain object (kept as a `.json` file, as the two fixtures here are):

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

- `seed` -- the RNG seed the replay starts from.
- `commands` -- the command stream the replay applies, in tick order, as the removed TypeScript sim
  defined it.
- `expectedHash` -- the 8-hex-char FNV-1a value of the final state, as the removed TypeScript sim
  computed it. It is pinned, never recomputed here.

## Changing a fixture

A fixture is never hand-edited, and its hash is never hand-derived. If a fixture's `expectedHash`
ever stops matching a live replay, that is either a real behaviour change (update the fixture
deliberately, in the same commit that explains why) or a regression (fix the code, do not retune
the fixture to match -- see the project's "no fitted green" rule). The regenerator (`build-content-pull.ts`)
needs the removed TypeScript sim and cannot re-record a fixture in this tree.
