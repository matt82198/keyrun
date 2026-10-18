# Keyrun

Keyrun is a proof-of-concept co-op dungeon game for Windows desktop, with 3D rendering, built in Godot 4.7.2 .NET with C#. It takes the Mythic+ loop out of WoW and aims for an open, queue-based game where players bring a class into a dungeon and get to hard keys quickly. Its classes, synergy system and mechanics are original, not a copy of an existing game, and each class is limited to five rotational abilities and ten keybinds in total. The simulation is headless and deterministic; the 3D renderer and input layer sit on top of it, and all geometry is placeholder. The MVP (STATE.md D10) is one complete 3D run that the project owner plays end to end and judges by feel: a third-person camera with WASD and mouse-look, the hollow-vault dungeon, the warden class with a bot party. Stack: Godot 4.7.2 .NET with C# (the simulation and content are ported from the frozen TypeScript oracle; the TypeScript trees are retired in ENGINE-PLAN phase 4).

## Status

Proof of concept, in the engine rebuild.

- ENGINE-PLAN phases 1 and 2 have landed on main.
- Phase 3 rows 3A-3J have landed on main (bot party, HUD, between-runs screen, e2e, boss, backfill, host wiring).
- Phase 3 exit is not recorded. D10 has a tuning gap: under the scripted autorun Warden every swept key wipes in pull 2, and no human-played run has been measured yet (the Lock proof waits on a tuning decision from Matt).
- Phase 4 stays blocked on D10. Decisions are recorded in [STATE.md](STATE.md); the domain map is in [CLAUDE.md](CLAUDE.md).

## Record and replay a run

A run's keys and camera yaw can be recorded from an interactive launch and replayed later at the same seed and key. The log is `keyrun.input-log/2`; camera pitch is not recorded. Build first:

```
powershell -NoProfile -ExecutionPolicy Bypass -File tools/godot/godot.ps1 --headless --path game --build-solutions --quit
```

Play a run and record it (the log is written at each run end and at quit):

```
powershell -NoProfile -ExecutionPolicy Bypass -File tools/godot/godot.ps1 --path game '--' --record-input game/fixtures/input/my-run.input-log.json --log build/my-run.txt
```

Replay the same log with `--replay-input` in place of `--record-input`, on the same path. A replay at a different seed or key is refused. Both flags are interactive only and are ignored under `--autorun`.

## Run (desktop build)

The real game is the Godot 4.7.2 .NET desktop build in `game/` (C#, decision D12 in [STATE.md](STATE.md)). Requires
Godot 4.7.2 .NET and the .NET 8 SDK (8.0.x).

```
dotnet build game/Keyrun.sln
dotnet test game/Keyrun.sln
tools/godot/godot.ps1 --headless --path game --build-solutions --quit
tools/godot/godot.ps1 --headless --path game --export-release "Windows Desktop" ../build/keyrun.exe
```

The export lands at `build/keyrun.exe` (the `build/` folder is gitignored). The Godot binary path is resolved by
`tools/godot/godot.ps1`, or set `KEYRUN_GODOT`. The engine plan and phase status are in
[design/ENGINE-PLAN.md](design/ENGINE-PLAN.md); the build's domain notes are in [game/CLAUDE.md](game/CLAUDE.md).

The TypeScript browser client below is first paint only and is not the product.

## Retired TypeScript browser client (first paint, parity oracle only)

This client is retired. It is kept only as the parity oracle until ENGINE-PLAN phase 4 deletes it; it is not the product.

Requires Node.js and npm.

```
npm ci
npm run dev
```

`npm run dev` builds the client, watches for changes and serves on port 8791. Open:

http://localhost:8791/client/index.html?key=5&dungeon=hollow-vault&class=warden

### Network path (in progress)

In a second terminal, start the game server:

```
npm run server
```

It listens on port 8792. Then open:

http://localhost:8791/client/index.html?net=1&key=5&dungeon=hollow-vault&class=warden

The client connects to `ws://127.0.0.1:8792` by default.

## Test

```
npm test
```

The last line of output is the Vitest summary, for example:

```
      Tests  880 passed (880)
```

## Open work

Open items are tracked in [STATE.md](STATE.md) (NEXT STEPS) and [BACKLOG.md](BACKLOG.md). Combo effects and numbers are not decided yet.

## Licence

MIT. See [LICENSE](LICENSE).
