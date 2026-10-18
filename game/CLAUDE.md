# game/ - Keyrun desktop build (Godot 4.7.2 .NET, C#)

Domain CLAUDE.md for the engine build. Decision: STATE.md D12; plan: design/ENGINE-PLAN.md. The game is the BIBLE
(design/BIBLE.md) played natively on Windows. The TypeScript browser client is not the product.

## Layout

- `project.godot`, `Keyrun.csproj`, `Keyrun.sln`, `export_presets.cfg` - Godot project and export preset "Windows Desktop".
- `scenes/` - Godot scenes (text .tscn). `src/` - Godot C# (Boot, Host, View, Hud). Godot types live only here.
- `sim/Keyrun.Sim/` - the deterministic integer rules engine, pure net8.0, no Godot reference.
- `sim/Keyrun.Sim.Tests/` - xUnit tests, PurityTests, and `Fixtures/oracle/` (parity data from lane 1B).
- `sim/ORACLE.md` - the parity contract: trace format, vector fixtures, which TS commit is the reference.
- `content/Keyrun.Content/` - JSON loader, validator and sim adapter (pure net8.0). Lane 1D, PR #111.
- `tools/Keyrun.Replay/` (repo-root tools/, in Keyrun.sln): the oracle trace replayer, landed in #129 (lane 2D).
- `bots/Keyrun.Bots/` (bots-core, lane 3A, PR #130 merged at 9c8a5ca) and `bots/Keyrun.Bots.Tests/` are present. `bots/Keyrun.Bots/Roles/` (lane 3B role implementations) lands via PR #133 and is not on main yet.

## Build and test

- .NET SDK 8.0.426 at `C:/Users/Mattt/.dotnet/dotnet.exe` (not on PATH); `dotnet` on PATH also works.
- Build: `dotnet build game/Keyrun.sln`
- Headless tests (no engine): `dotnet test game/Keyrun.sln`
- Godot 4.7.2 .NET console binary: `C:/Users/Mattt/AppData/Local/Microsoft/WinGet/Packages/GodotEngine.GodotEngine.Mono_Microsoft.Winget.Source_8wekyb3d8bbwe/Godot_v4.7.2-stable_mono_win64/Godot_v4.7.2-stable_mono_win64_console.exe`
  (override with `KEYRUN_GODOT`). Lanes call it only through `tools/godot/godot.ps1` or `tools/godot/godot.sh`, which
  also set DOTNET_ROOT.
- Solution build in the editor: `tools/godot/godot.ps1 --headless --path game --build-solutions --quit`
- Export: `tools/godot/godot.ps1 --headless --path game --export-release "Windows Desktop" ../build/keyrun.exe`
- Export templates are installed by `tools/godot/install-templates.ps1` (SHA512-checked, no login).

## Oracle rule (the port is checked against the frozen TS sim)

- `sim/`, `bots/src/`, `content/` (TS) and `client/`, `server/` are FROZEN at main `6e1548d` (STATE.md D12). No feature
  work there until phase 4A deletes them. `content/data/*.json` stays verbatim: the C# loader embeds it.
- The TS byte-equal oracle test (`tests/oracle/`, lane 1B, matched by vitest's `tests/**/*.test.ts` include) regenerates
  the fixtures in memory and fails when a frozen root changes. It runs under `npm test` in the required `tests.yml`
  gate (`test` job). A TS edit under a frozen root turns that check red. That is the intended behaviour.
- Phase 4A rehomes the oracle tests and fixtures. Until then, a C# behaviour change that diverges from the oracle
  needs a regenerated golden and a stated reason in the PR (ENGINE-PLAN section 7, R2). Never "fix during the port".

## Purity rule

- No `float`, `double`, `DateTime`, `System.Random` or Godot type in `Keyrun.Sim` or `Keyrun.Content`. Sim state is
  integers (mm, ticks, bp), and the PRNG lives in state. The purity tests (PurityTests.cs) enforce this by reflection.
- Rendering converts mm to metres as floats in `src/View/` only.
- Signed division: use `IntMath.FloorDiv` or `IntMath.TruncDiv`, never a bare `/` on signed values (JS `Math.floor`
  and `Math.trunc` differ on negatives).

## Test framework

- xUnit, because the 1A scaffold's `Keyrun.Sim.Tests` uses it and `dotnet test` runs it with no extra runner. No GUT or
  gdUnit4 in phase 1; engine-level proof uses the `--autorun` harness.

## Smart App Control (Windows security setting, not a lane concern)

- Matt approved leaving Windows Smart App Control ON on 2026-10-18 (D13). It can block a freshly built unsigned .NET
  or Godot binary on the desktop, intermittently (lane 1A's host load was blocked; lane 1D's `dotnet test` ran fine).
- CI `game-test` is the acceptance gate for C# lanes. A local `dotnet test` is run once and reported literally.
- No lane touches security settings. If a block happens, report the literal error and stop; do not turn SAC off, add
  exclusions, or retry around it.

## Gates that cover this domain

- `.github/workflows/game.yml`: `game-test` (dotnet test), `game-e2e` (headless smoke, export, exported-exe smoke).
  Both are non-required until phase 4A.
- `.github/workflows/tests.yml`: the required TS gate (`npm test` runs the oracle drift test).
- 1E arena (scenes/Arena.tscn, booted by Main.cs unless --smoke): `src/Host/SimDriver.cs` (20 Hz fixed accumulator,
  args --autorun --ticks N --log FILE --write-movie DIR), `PlayerInput.cs`, `RunLog.cs`, `DebugCleaveEmitter.cs`
  (TEMPORARY, 2E deletes it), `src/View/{ActorView,TelegraphView,CameraRig}.cs`. Scene test (not in CI yet):
  `pwsh game/src/Host/SimDriverSceneTest.ps1` asserts 40 TICK lines and frames=120 frame_ms=2000 at --fixed-fps 60.
- Host movement constants (L1): walk speed, actor radius and arena bounds come from `game/content/data/run.json` (C#-only;
  not under the root content/ tree, which is a frozen oracle root), loaded as `ContentRegistry.Run` and passed to
  Keyrun.Sim as `MovementConfig`. SimDriver holds no movement constants. `game/content/.gdignore` keeps Godot out of the
  content libraries (no .uid files there).
- 2A combat-damage (sim/Keyrun.Sim/Combat/{Types,Actors,Damage,Mitigation,Effects,EffectsRemove,Cooldowns,Threat,Targeting}.cs): Types.cs holds the shared AbilityDef/EffectOp/EffectDef/Affinity records lane 2B builds on; Pact weighting enters Damage/Mitigation as a `DamageWeighting` argument (null = no active rules), and `Targeting.ResolveTargets` takes the ability's Affinity from the caller. Gate: `CombatOracleDamageTests` replays every damage, heal and death event of the 5 oracle traces through the port against an hp ledger.
- 2C encounter (`sim/Keyrun.Sim/Encounter/` except Telegraphs.cs): scripts, script-target, chase, pulls, tempo (plus content-dungeon.ts scaleStep), timer/Slack, breakbar, clauses, echo, rewards, Seed Window loot (new, event-driven), `EncounterStep.Step` (index.ts stepEncounter), `EncounterJson` (setup and content readers). Oracle proof: `Keyrun.Sim.Tests/Encounter/EncounterTraceTests.cs` replays every encounter event of all five traces without combat; `TempoTests` checks the k2/k10 setup scripts against tempo-scaled content/data.
- Engine calls through `tools/godot/godot.ps1` never leave .NET build daemons: it defaults MSBUILDDISABLENODEREUSE=1, UseSharedCompilation=false, DOTNET_CLI_USE_MSBUILD_SERVER=0, because lingering MSBuild nodes and VBCSCompiler hold a piped stdout open for 10 to 15 min (the 1E game-e2e export hang). Godot `--export-release` exits 0 even when the .NET publish fails; game.yml checks export.log and `build/data_Keyrun_windows_x86_64`.
- 2B combat-cast (sim/Keyrun.Sim/Combat/{Abilities,Casting,Windows,WindowState,Combos,Payoff,FloorTelegraphs,Affinity,ClauseHooks,Rules}.cs): `Rules.Make(RulesConfig)` is the combat StepRules (rules.ts order); the run Clause enters as `RulesConfig.Clause` (`ICombatClause`: Hush silence + Pact weighting; 2D adapts 2C EncounterRules), and affinity takes the run effect table explicitly (no TS registry). Facet ops: `ComboOp(comboTable)` is a Payoff through the named table, `Abilities.LineShape(length, width)` maps width to the half-width radius, a facet resource op maps to `ResourceOp(delta)`; `Rules.Make` throws on any op kind the sim does not run. Gate: `CombatOracleCastTests` replays every cast/window/combo/interrupt event of the 5 oracle traces through the port (geometry inputs read from the trace are counted in its output line).
- 2D run root (`sim/Keyrun.Sim/Run/`, namespace `Keyrun.Sim.Runs`): `RunRoot.CreateRun`/`StepRun` (sim/index.ts order: EncounterStep, Tick.Step with `Rules.Make(RunWiring.RulesConfigOf(content, encounter.Rules))`, break-bar patches, next pull spawn, Echo, FixupTargeting, Slack, CheckRunEnd, `run_end`). The run Clause is `EncounterClause : ICombatClause` built per step from `encounter.Rules`; `RunOptions.Loadouts` (host-resolved AbilityDefs, facet Window ops included) merge onto the one global ability map; `RunContent.WithMovement(MovementConfig)` is the host seam. `tools/Keyrun.Replay` (namespace `Keyrun.Tools.Replay`, in Keyrun.sln, referenced by Keyrun.Sim.Tests): `RunSetup` reads an oracle setup fail-closed, `TraceReplayer` diffs hash/line/events/traceHash/result per tick; `dotnet tools/Keyrun.Replay/bin/Debug/net8.0/Keyrun.Replay.dll game/sim/Keyrun.Sim.Tests/Fixtures/oracle` prints one line per trace. Gate: `TraceReplayTests` replays all 5 traces on every tick (bot traces from recorded commands; Keyrun.Bots is 3A/3B).
- 3A bots-core (`bots/Keyrun.Bots/`, namespace `Keyrun.Bots`, shared/** in `Keyrun.Bots.Shared`; pure net8.0, refs Keyrun.Sim and Keyrun.Content; uses double where the TS does: skill.ts PRNG draw, positioning.ts trig): `Bot.MakeBot(RoleSet)`/`Bot.RunBots` (bot.ts order: danger dodge, pings, gate + mistake roll, role), `BotViews.BuildBotView(state, encounter.Telegraphs.Select(BotViews.ToBotTelegraph), ClassKitView.Empty, KitRegistry.From(ContentLoader.Load()))` is the oracle runner view; the TS module-level setContentRegistry is `BotView.Kits`. Lane 3B implements `IRole` for tank/healer/damage (roles/**) and reuses `Readiness.AbilityReady`. Gate: `Keyrun.Bots.Tests` (tests/bots/** non-roles cases by DisplayName, `BotPrngVectorTests` over botprng.vectors.json).
- 2E combat view (`src/Host/{HostRunSetup,SimDriver,PlayerInput,RunLog}.cs`, `src/View/{CastBarView,WindowRingView,LabelView}.cs`, `src/Hud/AbilityBar.cs`; DebugCleaveEmitter deleted): `HostRunSetup.Build(registry, "hollow-vault", key, "warden")` is the C# client/src/sim-host/run-setup.ts (pulls from actor 100, boss 900, Tempo-scaled, solo Warden at (0,4000)); SimDriver steps `RunRoot.StepRun` at 20 Hz (args add `--key N` default 2, `--seed N`; autorun default 400 ticks; Enter restarts an ended run); PlayerInput keys come from `KeyBinds.ClientDefaults` (client/keybinds.json port) onto the class kit slots, casts use `CastTargeting.For` (castTargetFor), `--autorun` uses `ScriptedWarden`. RunLog adds host lines `PULL spawn`, `CHASE`, `CAST start|done|stopped` (the sim has no cast_start event; it is read from CastId/CastStartTick). Scene test (not in CI yet): `powershell -ExecutionPolicy Bypass -File game/src/Host/CombatViewSceneTest.ps1` asserts pull 1 spawn, a chase, the Cantor heal start, the Warden kick and a named player cast.
- 3B bots-roles (`bots/Keyrun.Bots/Roles/`, namespace `Keyrun.Bots.Roles`): `TankRole`, `HealerRole`, `DamageRole` (IRole, plus static `TankDecide`/`HealerDecide`/`DamageDecide`), `TankEngage`, `TankPressure`, `HealPick`, `RoleShared` (shared.ts, plus V8's `Math.hypot` algorithm and JS `Math.round` for distances and centroids). Registry reads go through `view.Kits.Content`. `RoleSet.Default` wires the real roles (the TS `makeBot()`). Gate: `Keyrun.Bots.Tests` `RolesOracleTraceTests` drives all three bot traces from C#-generated commands (the per-tick commands must equal the trace's `c`, then the hash, line, events, trace hash and result), plus the 58 roles cases by DisplayName.
- 3C HUD (`src/Hud/{PartyFrames,Objective,TimerSlack,Pings,RunEndPanel}.cs`, `scenes/{Hud,RunEnd}.tscn`, mounted as `Hud/HudRoot` in Arena.tscn): every HUD node finds SimDriver (exported path, else nearest ancestor child) and only reads `Run`; under `--autorun` each prints `HUD party|objective|slack|ping|runend ...` proof lines. SPREAD/STACK come from sim state only (telegraphs whose ability applies `spreadMarked`/`stackMarked`, and actors carrying them), the same state BotView reads. A lasting wipe is reported by the sim as `depleted`; the panel titles it "Party wiped" from `PullIndex < Pulls.Count`. `PrivateRunLog.CausesFor(seat)` keys damage/death by the seat hit. SimDriver `--pull N` starts at pull N (actor ids and scripts kept). Scene test (not in CI yet): `powershell -ExecutionPolicy Bypass -File game/src/Host/HudSceneTest.ps1`.
- 3D screen + Clause pick (`sim/Keyrun.Sim/Run/{Clause,Ash,ScreenState}.cs`, namespace `Keyrun.Sim.Runs`; `src/Hud/{Screen,ClausePick}.cs`, `scenes/Screen.tscn` mounted as `Hud/Screen` in Arena.tscn): `ClausePick.Open(ids, dungeon, key)` offers three distinct Clauses (xoshiro128** seeded by FNV-1a of "dungeon:key", partial Fisher-Yates over sorted ids) and `ClauseChoice.ToRunOptions()` carries exactly one; `Crafting.Reroll(item, slotPool, prng)` spends one of two charges and draws only from the pool, a third is refused with nothing spent; `Progression` banks `Ash.ForResult` (50/30/10) every run, equips the taken reward with Tier capped at 10, and builds the "since last run" line; `ScreenState` is the Key band (stepper 2-20, re-offer per key, Queue locks and returns a `QueueRequest`). RunEndPanel's take calls `Screen.OpenFrom`; the screen's reroll pool is the content Facets of that slot that patch a class-kit ability. Enter queues and SimDriver still restarts at its own `--key` with no Clause: applying `QueueRequest` needs a SimDriver seam (lane 3H owns it). Scene test (not in CI yet): `powershell -ExecutionPolicy Bypass -File game/src/Hud/ScreenSceneTest.ps1`.
