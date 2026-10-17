# Keyrun

Keyrun is a proof-of-concept co-op dungeon game. It takes the Mythic+ loop out of WoW and aims for an open, queue-based game where players bring a class into a dungeon and get to hard keys quickly. Its classes, synergy system and mechanics are original, not a copy of an existing game, and each class is limited to five rotational abilities and ten keybinds in total. The simulation is headless and deterministic; the 3D client (Three.js, TypeScript) is only the renderer and input layer on top of it, and all geometry is placeholder. The MVP is one complete 3D run: a third-person camera with WASD and mouse-look, the hollow-vault dungeon, the warden class with a bot party, judged by playing it rather than by test counts. Stack: TypeScript, Vitest, esbuild, Three.js.

## Status

Proof of concept. Phase 5 (tuning from play, with the project owner playing as the acceptance test) is next. Decisions are recorded in [STATE.md](STATE.md); the domain map is in [CLAUDE.md](CLAUDE.md).

## Run locally

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
