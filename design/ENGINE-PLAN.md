# Keyrun engine plan: desktop engine rewrite to D10

Written 2026-10-18 (planning lane, opus per D11) from design/BIBLE.md, STATE.md, BUILDLOG.md, CLAUDE.md and
a read-only survey of main 6e1548d. MVP is unchanged (D10): ONE complete 3D Hollow Vault run Matt plays end
to end on his Windows desktop. This plan changes the tool, not the game. Items marked UNVERIFIED were not
executed by the planning lane.

PURPOSE: this artifact exists to replace the browser first-paint client with the real desktop game the BIBLE specifies; status: real.

## 1. Engine choice: Godot 4.7.2 (.NET build)

| Criterion (agent-driven dev) | Godot 4.7.2 .NET | Unity 6 (UNVERIFIED version) | Unreal 5 |
|---|---|---|---|
| Installed on Matt's desktop | YES, verified (`--version` -> 4.7.2.stable.official.ed1daf0bf) | NO | NO |
| Account/login needed | None (MIT licence) | Unity Hub sign-in + Personal licence activation | Epic account |
| Headless CLI build/test | `--headless`, `--build-solutions`, `--export-release`, `--quit-after` (docs; UNVERIFIED on 4.7.2) | `-batchmode -nographics -runTests` (needs activated licence) | UAT/BuildCookRun, hours |
| CI licence | none | ULF/serial secrets on a PUBLIC repo (game-ci) | none, but 100+ GB image |
| Text-diffable scenes | .tscn/.tres/project.godot are plain text | YAML only with Force Text + .meta GUID churn | .uasset binary |
| Windows export | export templates (.tpz from GitHub releases, no login) | built in | built in |
| Tests | the sim as a plain C# lib under `dotnet test` (xUnit); engine-level via autorun scenes | Unity Test Framework (NUnit) | Automation framework |
| Example code volume | large and growing; C# corpus overall is huge | largest | large, mostly Blueprints/C++ |

Decision: **Godot 4.7.2 .NET**. Unity has the most example code, but every lane would be blocked until Matt
signs in, and CI would need his licence as a secret on a public repo. Unreal fails the text and size tests.
Godot needs no account, is already installed, saves scenes as text, and builds from the CLI.

Engine paths (verified):
- `GODOT_MONO=C:/Users/Mattt/AppData/Local/Microsoft/WinGet/Packages/GodotEngine.GodotEngine.Mono_Microsoft.Winget.Source_8wekyb3d8bbwe/Godot_v4.7.2-stable_mono_win64/Godot_v4.7.2-stable_mono_win64_console.exe`
- .NET SDK 8.0.426 at `C:/Users/Mattt/.dotnet/dotnet.exe` (not on PATH). The engine's GodotSharp runtimeconfig
  targets net8.0 (verified), so this SDK matches. The wrapper sets `DOTNET_ROOT` and prepends it to PATH.
- Export templates are NOT installed (`%APPDATA%/Godot/export_templates` missing, verified). Lane 1A installs
  `Godot_v4.7.2-stable_mono_export_templates.tpz` from github.com/godotengine/godot/releases into
  `%APPDATA%/Godot/export_templates/4.7.2.stable.mono/` (asset name UNVERIFIED). No login.

## 2. Language: C#

- The sim and bots become plain `net8.0` class libraries with NO Godot reference. `dotnet test` runs about
  1000 ported tests in seconds without starting the engine. This is the main reason for C#.
- TS to C# is close to mechanical (types, interfaces, records, switch on discriminated `kind`). GDScript
  lacks interfaces, and its types are optional and checked only when it runs.
- Integer semantics are explicit: `int`/`long`, with `checked` blocks in tests. JS has doubles; C# `int`
  overflows, so squared mm distances and bp products use `long`.
- Presentation code (nodes, camera, input) is C# too, one language. The Godot C# API mirrors GDScript names
  in PascalCase, so GDScript examples carry over directly.
- Costs accepted: no web export (not wanted, the game is desktop); editor hot reload is weaker than GDScript's.
- GDScript: none. No addons in phase 1 (no GUT/gdUnit4); engine-level proof uses the autorun harness (section 4).

## 3. Port, with the TS sim as parity oracle

Rewriting from the BIBLE alone would throw away fixes already earned: 7 wiring P0s, mitigation, dead-actor
inertness, telegraph facing, scaleStep `every`, bot tank policy. Porting keeps them, and recorded traces
prove the port matches tick for tick. Nothing visual is ported: the Three.js client is rebuilt natively.

- FREEZE now: no feature work in sim/ bots/ client/ server/ screen/ from this PR on. Only the oracle
  exporter (1B) may touch tests/. BIBLE gaps (Twins, Lock, stack split) are built in C# AFTER parity.
- KEEP verbatim: `content/data/**/*.json` (the BIBLE numbers live there), `content/data/TUNING.md`,
  `tests/sim/golden/*.json` (command streams), the bot full-run seeds 7 and 11.
- KEEP (with a changed role): the TS sim stays runnable until phase 4 as the oracle generator.
- PORT: `sim/**` (4004 LOC: core 547, combat 1959, encounter 1498), `sim/index.ts` and run-*.ts, `bots/src/**` (2276),
  the `content/` loader/validator/sim-adapt (1044).
- REBUILD natively (not ported): `client/src/**` (6581 LOC Three.js) becomes Godot scenes. Its readability
  decisions (labels, chips, ring text, 0.4 telegraph opacity) are requirements, not code to port.
- DELETE in phase 4 (after parity): `sim/ bots/ server/ client/ screen/ scripts/ tests/ (TS) content/*.ts
  content/schema/ package.json package-lock.json tsconfig.json vitest.config.ts dist/`. `server/` is out of
  scope (BIBLE s11: networking is a later phase). It is deleted, not ported, and can be recovered from git history.

Oracle format (lane 1B defines it in `game/sim/ORACLE.md`):
- Per-tick projection line, ASCII: `t=<tick>;pull=<i>;slack=<n>;a=<id>,<hp>,<x>,<z>,<yaw>,<alive>,<res>,<castAbility|->,<castEnd>|...`
  (actors sorted by id). Hash = FNV-1a 32 over that line. Both sides implement this format, which is small.
  The TS `hashState` (sim/core/state.ts:118 hashes JS JSON text) is NOT reproduced.
- Fixture per run: `<name>.trace.jsonl` holding one record per tick `{t,h}`, the full projection every 20 ticks,
  and every SimEvent as compact JSON. The C# test reports the FIRST diverging tick plus a diff against the
  nearest full projection.
- Vector fixtures: `fixed.vectors.json`, `atan.vectors.json`, `prng.vectors.json` (10k seeded inputs each).
  HAZARD: TS mixes `Math.floor` (fixed.ts:21, atan.ts:48) and `Math.trunc` (damage.ts:30, movement.ts:46). These differ
  on negative values, so C# gets `IntMath.FloorDiv` and `IntMath.TruncDiv` and never a bare `/` on signed values.
- Fixture runs: golden `content-pull` and `scripted-pull` (sim only), bot full run seeds 7 and 11 at key 2
  (sim and bots), plus one key-10 seed. Traces record whatever happens (current main loses 3 of 4 bots in pull 2);
  parity means EQUAL, not winning.

## 4. Architecture in the engine

Single process, no server, no worker. Sim at 20 Hz in integer mm; the engine renders by interpolating.

```
game/
  project.godot                 main scene res://scenes/Main.tscn; physics unused for gameplay
  Keyrun.csproj                 Godot.NET.Sdk/4.7.2, refs sim/bots/content libs
  Keyrun.sln
  export_presets.cfg            preset "Windows Desktop" -> ../build/keyrun.exe
  scenes/  Main.tscn  Arena.tscn  Actor.tscn  Telegraph.tscn  Hud.tscn  RunEnd.tscn
  src/
    Boot/        Main.cs (parses OS.GetCmdlineUserArgs: --autorun --seed --key --ticks --log --input)
    Host/        SimDriver.cs (accumulator: fixed 50 ms ticks from _Process delta; holds prev/curr
                 snapshot; alpha for interpolation), PlayerInput.cs (WASD + mouse -> Command per tick),
                 ScriptedInput.cs (replays a recorded human command file; same path as PlayerInput),
                 RunLog.cs (JSONL: tick, events, projection hash, run_end)
    View/        ActorView.cs, TelegraphView.cs (circle/cone/line meshes from sim geometry, mm->m),
                 CastBarView.cs, WindowRingView.cs, LabelView.cs, CameraRig.cs (third-person, mouse-look,
                 collision-free flat arena)
    Hud/         AbilityBar.cs, PartyFrames.cs, Objective.cs, TimerSlack.cs, Pings.cs, RunEndPanel.cs
  sim/Keyrun.Sim/            net8.0, NO Godot reference. Core/ Combat/ Encounter/ Run/ (mirrors sim/)
  sim/Keyrun.Sim.Tests/      xUnit; Fixtures/oracle/** (data from 1B)
  bots/Keyrun.Bots/          net8.0, NO Godot reference (mirrors bots/src)
  bots/Keyrun.Bots.Tests/
  content/Keyrun.Content/    net8.0; loader + validator + SimAdapt; JSON embedded as EmbeddedResource
  content/Keyrun.Content.Tests/
  tools/Keyrun.Replay/       console app: run a fixture headless, print projection lines, diff vs oracle
```

- Content is data. `Keyrun.Content.csproj` embeds the JSON through
  `<EmbeddedResource Include="../../../content/data/**/*.json" LinkBase="data" />` (path relative to
  game/content/Keyrun.Content/). There is no duplicate copy and the TS keeps working during parity. Phase 4
  moves the JSON to `game/content/data/` and edits that one glob. Loading uses System.Text.Json with records
  matching content/schema/*.ts. The validator runs at startup and fails closed (the app exits with an error, no
  silent defaults).
- Determinism: sim state is plain records, ints only, and the xoshiro128** PRNG lives in state, as in sim/CLAUDE.md.
  No `float`, `double`, `System.Random`, `DateTime` or Godot type inside Keyrun.Sim/Bots/Content. Lane 1A adds
  a test that reflects over those assemblies and fails if any field or property has a float/double or Godot type.
  Rendering converts mm to metres as floats in View/ only.
- Movement: sim-owned flat-arena capsule movement (sim/combat/movement.ts port). Godot physics is not used for
  gameplay; the floor is a visual MeshInstance3D.
- Headless sim tests: `dotnet test game/Keyrun.sln`. The engine is not involved.
- Engine-level behavioural proof without a human:
  - `godot --headless --path game -- --autorun --seed 7 --key 2 --ticks 9000 --log build/proof/run.jsonl`
    runs the real scene tree with the bot party (dummy renderer), writes RunLog, and exits non-zero on exception
    or validator failure. Its last line is `RUN_END result=<finish|depleted|wipe> tick=<n>`.
  - Screenshots (needs a GPU, so Matt's desktop or xvfb; CI UNVERIFIED): `godot --path game --write-movie
    build/proof/frame.png --fixed-fps 20 --quit-after 200 -- --autorun --input fixtures/walk.json` writes a
    numbered PNG sequence (Movie Maker mode; UNVERIFIED on 4.7.2). The lane opens chosen frames with the Read
    tool and pastes the paths.
- Wrapper: `tools/godot/godot.ps1` and `tools/godot/godot.sh` resolve `$env:KEYRUN_GODOT` (default: the
  winget path above), set `DOTNET_ROOT`, prepend it to PATH, then exec. Lanes never hardcode the path elsewhere.

## 5. Phased plan to D10

Rules for every lane: LANE-CONTRACT.md; worktree under C:/Users/Mattt/keyrun-wt/<name>; tests first; anti-vacuity
by breaking production code and pasting RED then GREEN; the files listed below are the only files the lane owns.
Opus for development (D11), Haiku for recon, lint and docs. Every lane brief carries the PURPOSE line (LANE-CONTRACT 9b).

### Phase 1: native window, Warden walks, one telegraph
| Lane | Model | Owns | Tests first | Proof to paste |
|---|---|---|---|---|
| 1A scaffold | Opus | game/project.godot, game/Keyrun.csproj, game/Keyrun.sln, game/export_presets.cfg, game/scenes/Main.tscn, game/src/Boot/Main.cs, game/sim/Keyrun.Sim/Keyrun.Sim.csproj (empty lib), game/sim/Keyrun.Sim.Tests/{csproj,PurityTests.cs}, tools/godot/**, .gitignore, .gitattributes, .github/workflows/game.yml | PurityTests (reflection: no float/double/Godot types in Keyrun.Sim; seeded with one deliberate float field to show RED) | `dotnet test` summary; `godot --headless --path game --build-solutions --quit` exit 0; `--export-release "Windows Desktop" ../build/keyrun.exe` file size; `build/keyrun.exe --headless -- --smoke` prints `SMOKE ok`; game.yml run green on the PR |
| 1B oracle | Opus | tests/oracle/** (TS, vitest), game/sim/ORACLE.md, game/sim/Keyrun.Sim.Tests/Fixtures/oracle/** (generated data) | tests/oracle/traces.test.ts: with ORACLE_WRITE=1 it writes fixtures; otherwise it regenerates in memory and asserts byte-equal (so TS drift goes red inside `test`) | fixture list with byte sizes; `npx vitest run tests/oracle` pass; a deliberate change in sim/combat/damage.ts turns it RED, then revert |
| 1C sim core | Opus | game/sim/Keyrun.Sim/Core/** (IntMath, Fixed, Atan, Prng, State, Commands, Events, Tick), Combat/Movement.cs, Encounter/Telegraphs.cs (geometry + lifecycle only), matching tests | vector parity vs 1B fixtures; ported tests/sim/core/*.test.ts cases | `dotnet test` counts before and after; FloorDiv swapped for TruncDiv shows RED on the fixed vectors |
| 1D content | Opus | game/content/Keyrun.Content/**, game/content/Keyrun.Content.Tests/** | load all 19 JSON files; validator rejects a mutated ability (missing id) | test counts; loaded counts (abilities, enemies, classes) match the TS loadContent counts printed by 1B |
| 1E arena | Opus | game/scenes/{Arena,Actor,Telegraph}.tscn, game/src/Host/{SimDriver,PlayerInput,RunLog}.cs, game/src/View/{ActorView,TelegraphView,CameraRig}.cs, game/src/Host/DebugCleaveEmitter.cs (TEMPORARY, deleted in 2E) | SimDriver tick-accumulator unit test (headless scene test via --autorun --ticks 40 asserts 40 ticks logged for 2.0 s of frames) | headless autorun log tail; 3 PNG frames from --write-movie (Warden mid-walk, cone mid-fill, cone resolved); exported exe launched by the lane on the desktop |
| 1F docs | Haiku | CLAUDE.md (domain map lines), game/CLAUDE.md, README.md run section | none (docs) | `git diff --stat`; every command in README re-run with output pasted |

Order: wave 1 = 1A || 1B; wave 2 = 1C || 1D (both need 1A; 1C needs 1B); wave 3 = 1E; wave 4 = 1F.
Telegraph in 1E: DebugCleaveEmitter reads `hollowWarden.cleave`'s telegraph from content (cone r 4000 mm,
angle1024 200, lead 40 ticks, every 160 ticks, hollow-warden.json) and drives the ported Telegraphs.cs lifecycle
in front of a static box. It is the only temporary code in the plan and 2E deletes it.
Matt sees: double-click build/keyrun.exe. A native window opens: a grey floor with a grid, a blue Warden capsule,
third-person camera, WASD to move, mouse to look (Esc frees the mouse), and every 8 s a red cone that fills on the
floor in front of a grey box, then flashes and fades.

### Phase 2: full sim parity + playable combat (one human Warden, no bots)
- 2A combat-damage (Opus): Combat/{Actors,Damage,Mitigation,Effects,EffectsRemove,Cooldowns,Threat,Targeting,Types}.cs.
- 2B combat-cast (Opus): Combat/{Abilities,Casting,Windows,WindowState,Combos,Payoff,FloorTelegraphs,Affinity,ClauseHooks,Rules}.cs.
  It takes 2A's public types as given. If 2A is not merged, 2B starts after it, since they share Types.cs.
- 2C encounter (Opus): Encounter/** except Telegraphs.cs (scripts, pulls, chase, tempo, timer, breakbar, clauses, echo, rewards, script-target).
- 2D run root (Opus, after 2A-2C): Run/{Run,RunWiring,RunOptions,RunTypes}.cs + tools/Keyrun.Replay. GATE: content-pull and
  scripted-pull traces match on every tick.
- 2E combat view (Opus, parallel with 2D on View/Hud only): game/src/View/{CastBarView,WindowRingView,LabelView}.cs,
  game/src/Hud/AbilityBar.cs, game/src/Host/PlayerInput.cs (1-5, Q, E, R, F, G per BIBLE s2, from a keybinds.json port),
  deletes DebugCleaveEmitter.cs.
- Tests: each lane ports its tests/sim/<area>/*.test.ts cases 1:1 (names kept, so counts can be compared).
- Matt sees: Hollow Vault pull 1 spawns. The Choir melee chase the Warden, the lieutenant's 4 s heal shows a cast
  bar, Q kicks it, 1-5 cast with the named ability bar. Solo it is hard; that is expected.

### Phase 3: bots + full HUD = D10 candidate
- 3A bots-core (Opus): game/bots/Keyrun.Bots/{Bot,Kits*,Skill,View,Types,Roles}.cs + shared/**.
- 3B bots-roles (Opus, after 3A): roles/** (tank, tank-engage, tank-pressure, healer, heal-pick, damage, shared).
  GATE: seeds 7 and 11 plus the key-10 traces match on every tick.
- 3C HUD (Opus, parallel): game/src/Hud/{PartyFrames,Objective,TimerSlack,Pings,RunEndPanel}.cs, game/scenes/{Hud,RunEnd}.tscn:
  status chips, DOWNED, SPREAD/STACK labels, Slack bar, run-end choice of three (BIBLE s7).
- 3D screen + Clause pick (Opus, after 3C): the BIBLE s10 screen (three bands: Character with the five abilities and
  keybinds, Key with stepper and Clause pick, Queue as one button, no backfill yet) and the BIBLE s8 Clause pick (one of
  Echo, Tide, Hush, Pact; exactly one runs). Also the BIBLE s7 progression loop: end-of-run choose one of three with
  Facet text and tier, Ash banked per run, cap at Tier 10. Files: game/src/Hud/{ClausePick,Screen}.cs, game/scenes/Screen.tscn,
  and sim-side Clause and Ash rules as pure net8.0 code in game/sim/Keyrun.Sim (no Godot reference).
  WHY (intent check 2026-10-18): s7, s8 and s10 are in the BIBLE MVP, but phases 1 to 3 never planned the screen,
  the Clause pick or the progression loop. The D8 test "high key in under a minute" needs them.
- 3E e2e (Opus, renamed from 3D so IDs stay unique): `--autorun` assertions in .github/workflows/game.yml job `game-e2e`;
  a recorded human input file game/fixtures/input/walk-kick.json drives a scripted Warden through pull 1.
- 3F boss: Sundered Twins + Break bar, and the Lock (Opus, after 3A and 3B): game/sim/Keyrun.Sim/Encounter/** boss rules
  and the 50% Lock with its 3 s Window and enrage (BIBLE s9). Bots (3B roles) must alternate Setup and Payoff for the Lock.
  These are NEW behaviour, so they diverge from the TS oracle and get regenerated goldens with a stated reason (section 5
  phase 5 rule). WHY (intent check 2026-10-18): Twins and Lock are in the BIBLE s9 MVP, but phases 1 to 3 omitted them.
  Matt's first playtest is the D10 MVP, so they land before it, not in phase 5.
- Matt sees: launch, then a full run with the Warden plus a Cinder and Chanter bot party through three pulls and the boss
  (Twins and Lock included), ending on the run-end panel. The screen and Clause pick come up before the run starts.

### Phase 4: retire TS (orchestrator decides once both phase 2 and phase 3 gates are green)
- 4A one PR: delete the paths listed in section 3; `git mv content/data game/content/data` + edit the one csproj glob;
  oracle fixtures stay as frozen goldens (owned by C# from now on); rename jobs in game.yml to `test` and `e2e`
  and delete tests.yml in the SAME PR, so the required checks keep their names.
- Matt sees: nothing changes in the game.

### Phase 5: Matt plays (D10 acceptance)
- Feel lanes from his verdicts; BIBLE gaps not already in phase 3 (stack damage split; Twins and Lock moved to 3F) as C#
  features. These deliberately diverge from the oracle, so each regenerates its C# goldens with a stated reason.

### CI
- `.github/workflows/game.yml` (1A): runs-on windows-latest, which is the target platform. Steps: checkout;
  actions/setup-dotnet 8.0.x; cache + download the Godot 4.7.2 mono win64 zip and export templates from GitHub
  releases (URLs UNVERIFIED); `dotnet test game/Keyrun.sln`; `godot --headless --path game --build-solutions --quit`;
  `--export-release "Windows Desktop"`; upload build/keyrun.exe as an artifact. Jobs `game-test` and `game-e2e` are
  non-required until phase 4 renames them `test` and `e2e`.
- tests.yml (`test`, `e2e`) keeps running unchanged until 4A, so the oracle check (1B) stays in a required check.
- Screenshots in CI: UNVERIFIED (no GPU on hosted runners; the d3d12 WARP path is untested). Frames are
  local-desktop proof until a lane proves a runner path.

## 6. Repo layout
- Engine project: `game/` at repo root. `build/` (gitignored) holds exports and proof frames.
- sim/ server/ client/ bots/ screen/ scripts/ and the TS tests: frozen now, deleted in 4A. research/ design/ STATE.md
  BUILDLOG.md BACKLOG.md LICENSE README.md stay.
- .gitignore additions: `game/.godot/`, `build/`, `**/bin/`, `**/obj/`, `*.user`, `game/.mono/`, `*.tmp`.
- .gitattributes (new): `* text=auto eol=lf`; `*.ps1 text eol=crlf`; `*.png *.jpg *.glb *.wav *.ogg *.ttf *.tpz binary`.
- LFS: NO. Placeholder geometry only (CLAUDE.md "No art assets"); primitives live in .tscn as text. Revisit if any
  binary over 1 MB is proposed.
- Commit `*.uid` files Godot generates next to scripts (4.4+ behaviour; UNVERIFIED for 4.7.2); never commit
  `.godot/`.
- CLAUDE.md domain-map lines (1F):
  - `game/` - Godot 4.7.2 .NET desktop game: scenes, views, HUD, input, and the in-process sim host. Read game/CLAUDE.md.
  - `game/sim/` - Keyrun.Sim, the deterministic integer rules engine (C#, no Godot reference); ORACLE.md defines parity.
  - `game/bots/` - Keyrun.Bots, party AI producing Commands from sim views (C#, no Godot reference).
  - `game/content/` - Keyrun.Content, JSON loader/validator/sim adapter; the data stays in content/data until 4A.
  - `tools/godot/` - engine wrapper scripts (binary path, DOTNET_ROOT) and export-template installer.
  - D1 in STATE.md is superseded: replace "TypeScript ... Three.js ... no engine" with D12 (section 7).

## 7. Risks and the first three orchestrator decisions

Risks:
- R1 Parity drift from floor/trunc, int overflow or JS number edge cases. Mitigation: vector fixtures first (1C),
  `long` for products, the first-divergence-tick report.
- R2 The oracle may encode bugs (pull 2 balance, defect (m) field combo self-hit). Accepted: parity first, then fix in C#
  with regenerated goldens and a stated reason. Never "fix during the port".
- R3 The CLI flags or template asset names differ on 4.7.2 (UNVERIFIED). 1A proves each by running it; if one is
  wrong, the lane edits this file's command.
- R4 Lanes cannot see the window. Mitigation: --write-movie frames + RunLog. The feel verdict is Matt's alone (D10).
- R5 Hook policy may block lanes from launching a GUI process on the desktop (UNVERIFIED). Fallback: headless autorun
  + movie frames on Matt's session through the orchestrator.
- R6 Freeze violation: someone patches TS during phases 1-3 and the oracle changes under the port. 1B's byte-equal
  test catches it in the required `test` check.

First three decisions for the orchestrator (no Matt needed; decided 2026-10-18 under the decide-and-report grant):
1. D12 = Godot 4.7.2 .NET, C#, port with oracle (this plan). Bound: no file outside game/, tools/godot/, tests/oracle/
   and docs changes until 4A; reverting is deleting game/. No data loss.
2. Freeze TS feature work at main 6e1548d. Bound: the freeze is a STATE.md rule, not a deletion. Any frozen lane branch
   stays on origin and can be replayed into C# later.
3. Embed content JSON from content/data (no copy) until 4A. Bound: one csproj glob; TS and C# read the same bytes, so
   they cannot drift. The move in 4A is a `git mv`.
