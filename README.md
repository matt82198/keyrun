# Keyrun

Proof-of-concept co-op dungeon game: the Mythic+ loop, isolated. See CLAUDE.md for the domain map and STATE.md for decisions.

## Run
- `npm ci` then `npm test` (Vitest), `npm run typecheck`, `npm run build` (esbuild to dist/), `npm run dev` (serve on :8791).

## Play it

Local solo run:
1. `npm ci`, then `npm run dev` (esbuild watch + static server on port 8791; `scripts/build.mjs:31`, serves `/client/` per `scripts/build.mjs:32`).
2. Open `http://localhost:8791/client/index.html?key=5&dungeon=hollow-vault&class=warden`.

Networked run (client and server in two terminals):
1. Terminal A: `npm run server` (`server/src/run.mjs`, from `package.json:12`). The ws server listens on port 8792 (`server/src/main.ts:19`; no env override in `main.ts`).
2. Terminal B: `npm run dev` as above.
3. Open `http://localhost:8791/client/index.html?net=1&key=5&dungeon=hollow-vault&class=warden`. `?net=1` selects the net host (`client/src/boot.ts:43-46`). The default server URL is `ws://127.0.0.1:8792` (`client/src/net/host-handle.ts:20`). To point elsewhere, add `&server=ws://host:port` (`client/src/net/net-client.ts:77`).

Keybinds (`client/keybinds.json`, mapped to slots in `client/src/input/slot-abilities.ts:26-34`):
- `1`-`5`: rotational abilities 1-5 (`Digit1`-`Digit5`, keybinds.json:2-6).
- `Q`: interrupt (`KeyQ`, keybinds.json:7).
- `E`: defensive (`KeyE`, keybinds.json:8).
- `R`: utility (`KeyR`, keybinds.json:9).
- `F`: movement ability (`KeyF`, keybinds.json:10).
- `Tab`: cycle target (`targetCycle`, keybinds.json:11).
- `G`: ping (`KeyG`, keybinds.json:12).
- WASD, Space and mouse-look are hardcoded camera/movement controls, outside keybinds.json (`client/src/input/keybinds.ts:1-2`).
