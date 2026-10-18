# Keyrun

Keyrun is a proof-of-concept co-op dungeon game. It takes the Mythic+ loop out of WoW and aims for an open, queue-based game where players bring a class into a dungeon and get to hard keys quickly. Its classes, synergy system and mechanics are original, not a copy of an existing game, and each class is limited to five rotational abilities and ten keybinds in total. The simulation is headless and deterministic; the 3D client (Three.js, TypeScript) is only the renderer and input layer on top of it, and all geometry is placeholder. The MVP is one complete 3D run: a third-person camera with WASD and mouse-look, the hollow-vault dungeon, the warden class with a bot party, judged by playing it rather than by test counts. Stack: Godot 4.7.2 .NET with C# (the simulation and content are ported from the frozen TypeScript oracle; the TypeScript trees are retired in ENGINE-PLAN phase 4).

## Status

Proof of concept, in the engine rebuild. ENGINE-PLAN phase 1 is in progress: lanes 1A (scaffold), 1B (oracle), 1C (sim core), 1D (content loader) and 1F (docs) are merged; 1E (arena scene) is in progress. Phase 2 (full sim parity and playable combat) is next. Decisions are recorded in [STATE.md](STATE.md); the domain map is in [CLAUDE.md](CLAUDE.md).

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

## Run locally (TypeScript browser client, first paint only)

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
