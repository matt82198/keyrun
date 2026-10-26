# game/bots - domain map (v2, S5 review; source origin/main 96e6dcfb, fetched 2026-10-26)

Navigation only. Read this file, then the one source file you need. Every row below was checked against origin/main: a test file is named only where its header comment or a symbol reference in the test file confirms the link. Rows with no confirmed test say so.

## What this domain is
- **Keyrun.Bots** - party AI, pure net8.0. Hard rule (Keyrun.Bots.csproj header): no Godot reference, no System.Random, no clock. Uses double for PRNG-draw parity with the TS bots.
- Source: `game/bots/Keyrun.Bots/` (folders `Roles/`, `Guides/`, `shared/`). Tests: `game/bots/Keyrun.Bots.Tests/` (xUnit, names kept 1:1 from the TS tests).
- Playtester guide doc: `game/bots/GUIDE.md`. `game/bots/.gdignore` keeps Godot from importing this folder.

## Source map (one row per area: responsibility | test file)
- **Bot.cs** - BotMemory skill dial (dial to reaction delay) | SkillDialTests.cs (header: port of skill-dial.test.ts)
- **Skill.cs** - bot-side xorshift128 PRNG state (SeedPrng, StepPrng) | BotPrngVectorTests.cs (header); SkillDialTests.cs for the dial
- **Types.cs, View.cs** - integer floor point; bot view built from sim state | no dedicated test file
- **Reason.cs** - `role:rule` reason id on each command | BotCommandReasonTests.cs
- **Careless.cs** - careless seat plan | CarelessSeatTests.cs
- **Roles.cs** - role state-machine entry (tank/healer/damage decide) | RolesTankTests.cs (tank bulwark), RolesHealTests.cs (heal pick), RolesDamageBehaviorTests.cs (damage)
- **Roles/Tank.cs, TankEngage.cs, TankPressure.cs** - tank state machine, engage pickup, pack pressure | RolesTankTests.cs; TankLeashIgnoresReturningSeatTests.cs (leash and chain pull); TankHoldsDefensiveWhileTakingPackTests.cs (pack pressure, symbol reference)
- **Roles/Healer.cs, HealerAggroMove.cs, HealerIdle.cs, HealerSetup.cs, HealPick.cs** - healer state machine, shape-aware heal choice | HealerRespondsToAggroTests.cs (aggro move); HealerKeepsPressingTests.cs (press rule); HealerAoeIdlePackTests.cs (HealerIdle, symbol reference); RolesHealTests.cs (HealPick, header); HealerSetup.cs has no test file
- **Roles/Damage.cs, Fillers.cs** - damage state machine; filler rotation | RolesDamageBehaviorTests.cs (header); FillerVarietyTests.cs (header: least-recently-cast filler)
- **Roles/RoleShared.cs** - helpers every role shares (readiness, kick) | no dedicated test file (symbol references exist in many tests; none is a RoleShared test)
- **Kits.cs, KitsHeals.cs, KitsPriority.cs, KitsRegistry.cs, KitsResolve.cs, KitsResources.cs** - ClassKit decision view derived from content | KitsTests.cs (header: port of kits.test.ts; RolesHealTests.cs header places the heal classification in KitsTests)
- **Guides/GuideSet.cs, Guides/GuidedTankRole.cs** - guide JSON parse (fails closed); guided tank role | GuidedWardenTests.cs (header); GuidedWardenGuideSectionsTests.cs (header)
- **shared/AoeGate.cs** - AoE rotation rule (R-TARGET-1 a) | AoeGateIdlePackTests.cs (header)
- **shared/ChainPull.cs** - chain pulls; party leash constant | TankLeashIgnoresReturningSeatTests.cs (header; references ChainPull.PartyLeashMm)
- **shared/KickOrder.cs** - cross-class kick rotation | KickOrderTests.cs (header)
- **shared/BreakBarKick.cs** - Twins break bar kick | no dedicated test file
- **shared/LockAnswer.cs** - lock answer | no test file references it
- **shared/CastFacing.cs** - face target on a targeted cast (R-FACING-2) | BotCastStartFacingTests.cs
- **shared/Command.cs** - walk command type | no dedicated test file
- **shared/PathMoves.cs** - route moves | SeedBloomPathsAroundObstacleTests.cs and TankTakesHeldPackBeforeBloomTests.cs (symbol references)
- **shared/Follow.cs** - follow goal behind the lowest living seat; WalkTo | FollowPathingTests.cs (header)
- **shared/Floor.cs** - floor escape | RolesFloorEscapeTests.cs (header: port of floor-escape.test.ts); FloorEscapeWalkableTests.cs (symbol reference)
- **shared/Positioning.cs** - layout nav and shape tests | TelegraphLineDodgeTests.cs (header: line dodge out of floor telegraph); RolesFloorTelegraphLoopTests.cs (symbol reference)
- **shared/ContentCombos.cs** - combo tables from content | ContentCombosTests.cs (header)
- **shared/LayoutPackGroups.cs** - pack groups | PackLinkAdapterTests.cs, PathPackRouteTests.cs (symbol references)
- **shared/SeededLayout.cs** - seeded dungeon as the run layout | SeededLayoutTests.cs (header)
- **shared/SeededRoster.cs** - pack roster for the seeded fill | SeededFillContentTests.cs (symbol reference)
- **shared/ForcesPlanner.cs** - forces values per placed pack | SeededFillContentTests.cs (header: forces values ship with the packs that place them)
- **shared/EncounterWindow.cs** - encounter windows | SeedBloomPathsAroundObstacleTests.cs and TankTakesHeldPackBeforeBloomTests.cs (symbol references)
- **shared/Windows.cs** - window helpers | WindowsTests.cs (symbol reference)
- **shared/Ledger.cs** - cast ledger | LedgerTests.cs (header: port of ledger.test.ts)
- **shared/EngagedTargets.cs** - engaged target choice | EngagedTargetsTests.cs (header)
- **shared/Priority.cs** - target priority | InterruptTests.cs, SharedSeamTests.cs (symbol references)
- **shared/Pings.cs** - G-ping reactions | PingsTests.cs (header: port of pings.test.ts)
- **shared/PingWords.cs** - ping word text | PingWordsTests.cs (header)
- **shared/PingsPolicy.cs** - ping policy wiring into decideForActor | PingWiringTests.cs (header); PingsTests.cs (symbol reference)
- **shared/PlaytesterMode.cs, BotReceipts.cs** - run mode; receipt lines | PlaytesterModeReceiptTests.cs (header; uses IBotReceiptSink)
- **shared/QueueWait.cs** - queue gate and readout | QueueReadoutBranchesTests.cs (header)
- **shared/Readiness.cs** - cooldown readiness check | no dedicated test file

## Test project only (not bot logic)
- **ForkParity*.cs, ForkParity.margins.txt, fork-parity-noise.json** - parity margins and noise study | ForkParityTests.cs (header)
- **OracleTraceRecorder.cs, OracleSetupWriter.cs, LobbySetupForTests.cs, BotTestKit.cs, RolesTestKit.cs** - test harness (files confirmed present)

## Test command shape
- Solution (CI game-test job, per root CLAUDE.md): `dotnet test game/Keyrun.sln` (file confirmed present)
- Project: `dotnet test game/bots/Keyrun.Bots.Tests/Keyrun.Bots.Tests.csproj` (file confirmed present)
- Single class (xUnit FullyQualifiedName filter; shape not executed): `dotnet test game/bots/Keyrun.Bots.Tests/Keyrun.Bots.Tests.csproj --filter FullyQualifiedName~<TestClassName>`
- Project references (grep of the csproj): Keyrun.Bots, Keyrun.Sim (`sim/Keyrun.Sim`), Keyrun.Content (`content/Keyrun.Content`), Keyrun.Replay (`tools/Keyrun.Replay`), Keyrun.HostCore (`game/src/HostCore`).
