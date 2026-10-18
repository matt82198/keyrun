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
- Planned (ENGINE-PLAN section 4): `bots/Keyrun.Bots/` (phase 3), `tools/Keyrun.Replay/` (phase 2D).

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
