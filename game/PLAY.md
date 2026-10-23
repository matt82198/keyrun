# Playing Keyrun: the tutorial as any class

<!-- Sources: tools/play.ps1 (launch, switches -Record and -Hours), tools/godot/godot.ps1 (Godot path, .NET root, KEYRUN_USER_ARGS), game/src/Host/ArenaOptions.cs (the flags), game/src/Host/PlayerInput.cs (keys), game/src/Hud/Screen.Input.cs and Screen.TabCharacter.cs (screen keys), game/src/Hud/Queue.cs (Group found), game/src/Hud/RunEndPanel.cs (choice of three), game/src/HostCore/Lobby.cs and HostRunSetup.cs (classes), design/BIBLE.md s9 (dungeon). Verified against main 1bbbff68. -->

This page tells you how to play the tutorial dungeon, Hollow Vault at key 2, as Warden, Chanter or Cinder.
The launch is one command for every class. The class is picked in the game.

## Prerequisites

- **`tools/play.ps1` is on main** (PR #592, merged as 1497ee4f).
- **.NET SDK.** The launcher uses `DOTNET_ROOT` if set, else `C:\Users\Mattt\.dotnet` when `dotnet.exe` is there, else `dotnet` on PATH. <!-- tools/play.ps1:32-43 -->
- **Godot 4.7.2 .NET console binary.** The wrapper uses `$env:KEYRUN_GODOT` if set, else
  `%LOCALAPPDATA%\Microsoft\WinGet\Packages\GodotEngine.GodotEngine.Mono_Microsoft.Winget.Source_8wekyb3d8bbwe\Godot_v4.7.2-stable_mono_win64\Godot_v4.7.2-stable_mono_win64_console.exe`.
  A missing binary stops the launch with exit 2. <!-- tools/godot/godot.ps1:43-52 -->
- **Repo root.** The script builds `game/Keyrun.csproj`, imports the CC0 models once if missing, and caps the session at 4 hours (`-Hours`, 1 to 24). <!-- tools/play.ps1:25, 46-69 -->

## The command

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File tools/play.ps1
```

`tools/play.ps1` takes three switches: `-Record` (also writes the session's keys to `play-<stamp>.input-log.json`), `-Hours` and `-Dungeon` (a dungeon id, default `hollow-vault`). <!-- tools/play.ps1:26-30 -->
It has no class, key or seed parameter.

| Class | Launch line | Pick on the Character tab |
|---|---|---|
| Warden | `powershell -NoProfile -ExecutionPolicy Bypass -File tools/play.ps1` | Default (`Lobby.DefaultClassId = "warden"`, Lobby.cs:43) |
| Chanter | same line | Up/Down until Chanter, or click its card |
| Cinder | same line | Up/Down until Cinder, or click its card |

The game opens on the Character tab (<!-- game/src/Hud/Screen.TabCharacter.cs; tests/ScreenTabs S1 -->). Then:

1. Pick your class.
2. Press **Enter** to queue, then **Enter** again at "Group found" to say you are ready. Press **Esc** there to decline. <!-- game/src/Hud/Queue.cs:193-203 -->
3. Play Hollow Vault at key 2 as seat 1. Bots fill the other seats. The log's `RUN seats human=... bots=...` line lists who is seated (game/src/Host/SimDriver.cs:448).

## Keybinds

From `KeyBinds.ClientDefaults` (game/src/Host/PlayerInput.cs:324-337, the port of client/keybinds.json):

| Key | Action | Code |
|---|---|---|
| W A S D | Move, relative to the camera heading (held) | PlayerInput.cs:220-253 |
| 1 to 5 | Rotational abilities 1 to 5 | PlayerInput.cs:289-296 |
| Q | Interrupt (kick). Goes at the enemy under your cursor or plate if there is one, else at your target | PlayerInput.cs:291-296 |
| E | Defensive | PlayerInput.cs:324-337 |
| R | Utility | PlayerInput.cs:324-337 |
| F | Movement | PlayerInput.cs:324-337 |
| Tab | Cycle hostile target | PlayerInput.cs:271-281 |
| G | Ping: your current target, or a spot ping at you if none | PlayerInput.cs:284-287 |
| Left click | Target the unit under the cursor (body or name plate) | PlayerInput.cs:193-196 |
| Hold right mouse | Mouselook (cursor captured); release frees the cursor | PlayerInput.cs:197-199 |
| V | Flip the camera preset | game/content/Keyrun.Content/Model/CameraMode.cs, View/CameraRig.cs:196 |

The class's ability in each slot (`content/data/classes/<class>.json` and `content/data/abilities/<class>.json`, display names from `name`; key-to-slot mapping at `game/src/HostCore/HostRunSetup.Party.cs:253-261`):

| Key | Warden | Chanter | Cinder |
|---|---|---|---|
| 1 | Strike | Cadence | Ember Bolt |
| 2 | Guard Break | Refrain | Flare |
| 3 | Bulwark | Chorus | Cold Snap |
| 4 | Reckoning | Dissonance | Detonate |
| 5 | Vigil | Hymn | Cinderfield |
| Q | Shield Bash | Rebuke | Quench |
| E | Brace | Aegis | Ashen Skin |
| R | Taunt | Cleanse | Purge |
| F | Charge | Glide | Blink |

**Screen keys** (before and between runs, `game/src/Hud/Screen.Input.cs:97-141`):
Up/Down pick a class on Character (Screen.TabCharacter.cs:107); on the Key tab Up/Down (or a click on the "< name >" row under the run strip) pick the dungeon by name and the run uses that pick (Lobby.NextDungeon, Screen.TabKey.cs CycleDungeon); Left/Right or `-`/`+` step the key level (Screen.cs:167-176); **Q/E** or **F1** to **F5** move between tabs (Screen.Input.cs:145-165); **T** opens talents; **Tab** cycles the selected item (Screen.Input.cs:130-132).
At the end of a run, keys **1, 2, 3** take one of the three cards (RunEndPanel.cs:459-469), and **Enter** returns to the screen (RunEndPanel.cs:336).

## What the tutorial teaches

The Hollow Vault has three teaching pulls, and they lead straight to the boss, the Hollow Warden, with no door or gate in between (forward-only route, D44). The pulls, in order, follow BIBLE s9.6 (design/BIBLE.md:540-547):

1. **The Choir.** Two melee and a caster lieutenant. Its 4-second heal must be kicked. This teaches the **kick** (Q).
2. **The Pair.** Five melee. Every 20 s one player is marked to **spread** (stand apart in a ring). Ten seconds later the pack calls a **stack** (stand together in a circle, damage split). This teaches **reading shape**.
3. **The Seed.** An elite that drops a Window on death. A Combo landed in it gives bonus loot at run end. This teaches the **boss rule** with no stakes.

**The Hollow Warden** (design/BIBLE.md:549-558) has three mechanics, never layered:

- **Cleave.** A slow frontal cone. Stand behind or beside the boss.
- **The Sundered Twins.** Two adds share a Break bar. Interrupts and controls drain it. At empty both are stunned. The boss's death ends the fight, and any surviving adds despawn.
- **The Lock.** At 50% the boss shields and opens a Window on itself for 3 s, every 30 s. Only a Combo closes it, and the setter and payer must alternate. Three failed Locks enrage the boss into a Tempo spike.

## End of run: choose one of three

Each run ends with three cards. Each shows its slot, tier and Facet text (RunEndPanel.cs:300-313). Press 1, 2 or 3 to take one. Enter goes back to the screen. Taking a card does not bank it: progression belongs to the screen (RunEndPanel.cs:336).

## Other dungeons

**The Sunken Bellfoundry.** Start it with `powershell -NoProfile -ExecutionPolicy Bypass -File tools/play.ps1 -Dungeon sunken-bellfoundry`. The script passes `--dungeon sunken-bellfoundry` to the game (`ArenaOptions.DungeonId`, `DungeonPick.Resolve`), and the pre-run screen opens on that dungeon; an id the content lacks stops the launch and lists the known ids. The Bellfoundry has three bosses, one per segment, and the run ends only when the last one dies (R-MULTIBOSS-1). Its rooms are generated from a seed: the run seed (`--seed N`, default 1) unless `--dungeon-seed N` is given, so the same seed always lays out the same dungeon. Without `-Dungeon` the play command plays the Hollow Vault, hand layout, unless `KEYRUN_USER_ARGS` adds `--dungeon-seed`.

## Flags the game reads but play.ps1 does not pass

`play.ps1` passes only `--camera vertical --log <file>` (plus `--record-input` under `-Record`). Any other `ArenaOptions` flag needs `KEYRUN_USER_ARGS`. `tools/godot/godot.ps1` appends that variable's whitespace-split value after `--` on every launch (godot.ps1:80-84), so it reaches play.ps1 too:

```powershell
$env:KEYRUN_USER_ARGS = '--key 3 --seed 7'
powershell -NoProfile -ExecutionPolicy Bypass -File tools/play.ps1
```

The player-facing flags in ArenaOptions.cs (Parse, lines 167-240). The rest of that switch (`--ticks`, `--pull`, `--press-*`, `--abandon-seat`, `--careless-seat`, `--joiner-key`, `--ready-hold`, `--bots-hold`, `--requeue`, `--write-movie`, `--replay-input`) drives scripted proof runs and is not covered here:

| Flag | Meaning |
|---|---|
| `--key N` | Starting key level. Default 2. The Key tab can still move it. The floor is 2 (ScreenState.cs:62). |
| `--seed N` | Run seed. Default 1. |
| `--dungeon ID` | The dungeon to play, `hollow-vault` (default) or `sunken-bellfoundry`; `play.ps1 -Dungeon` passes it. (ArenaOptions.cs, DungeonPick.cs) |
| `--dungeon-seed N` | Plays a seeded two-branch dungeon generated from N (SeededLayout) in place of the hand-built Hollow Vault layout. The seed picks the branch the bots walk. For the Bellfoundry it overrides `--seed` as the layout seed. It is a seed, not a dungeon id. (ArenaOptions.cs, SimDriver.cs) |
| `--talent SEAT:TALENT` | Pick a talent for a seat, repeatable, e.g. `--talent 1:rend` for a Warden run. Seat ids are fixed: Warden 1, Chanter 2, Cinder 3. Talent ids are in `content/data/classes/<class>.json`. (SeatTalents.cs:12-40). |
| `--rotation N` | Pin the weekly affix rotation (`AffixWeek.ParseOverride`). |
| `--camera chase\|vertical` | Starting camera preset (CameraMode.cs:34). play.ps1 uses vertical. |
| `--solo` | Only your seat, no bots. `--autorun` is scripted and not for play. |

## Troubleshooting

- Godot not found: set `KEYRUN_GODOT`. Exit 6 (`PREFLIGHT`): run the printed build or import line. Exit 4: another Godot holds the launch lock. Exit 124: the session cap (default 4 h) was reached. (tools/godot/godot.ps1:17-27)
