# screen/ — the character and queue screen

**Purpose**: the single out-of-dungeon screen: your character, your gear and talents, your key, the queue,
your run history. Bots-first it launches a local run; later it talks to the server's queue.

## Rules
- Plain HTML + TypeScript, same esbuild pipeline as the client, no framework. One page, three panels:
  Character (class, level, five abilities with keybinds, talents summary), Loadout (gear slots), Run (key level,
  dungeon, Queue/Play button, last five runs with time and result).
- State in `localStorage` for the POC via one small store module with a versioned schema; later a server API
  with the same interface.
- Session-length target: under 60 seconds from opening the screen to being in a run.
- No stash management, no currencies beyond what the bible defines, no shop.

## Layout
`screen/index.html`, `screen/src/main.ts`, `store.ts`, `panels/character.ts`, `panels/loadout.ts`,
`panels/run.ts`. Tests: store versioning/migration, panel rendering from a fixture character.
