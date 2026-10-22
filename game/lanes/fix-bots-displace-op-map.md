# fix/bots-displace-op-map (finding f-022c6905, G2.1)

## What changed
- The bot op map `ContentCombos.ToSimOp` had no arm for the content `DisplaceOp` (keyrun #645, merged as 0ff94f0f).
  An ability carrying a displace op failed run setup with "has an op the sim does not run".
- Added `C.DisplaceOp d => new DisplaceOp(d.DistanceMm, d.FreesBinds, d.TowardTarget)` (field order from the sim record
  `Keyrun.Sim.Combat.DisplaceOp(long DistanceMm, bool FreesBinds, bool TowardTarget)`, Displace.cs:18).
- Added the matching arm to the `ForkParityTests` op switch.
- New red test file `game/bots/Keyrun.Bots.Tests/DisplaceOpMapTests.cs` (two facts: field mapping, toward-target flags).

## The three sites
1. `game/bots/Keyrun.Bots/shared/ContentCombos.cs` `ToSimOp` (the fix).
2. `game/bots/Keyrun.Bots.Tests/ForkParityTests.cs` op switch (mirror, arm added).
3. `game/sim/Keyrun.Sim.Tests/EveryEnemyAbilityTelegraphedTests.cs` (mirror): NOT changed. It reads ability JSON by
   string key (`"damage"`, `"pull"`, ...) and its default arm throws "C#-only kit op ... is not read by this test" by
   design. The displace op is C#-only (EffectOps.cs:48 doc: "No oracle content carries it"), so there is no JSON shape
   to map. The brief placed this file under Keyrun.Bots.Tests; the real path is game/sim/Keyrun.Sim.Tests.

## Verification
- Premise checked on origin/main 2c35567f before any edit: no DisplaceOp arm in ContentCombos.cs; sim DisplaceOp present.
- Red-first: commit 6d78d8bb adds the red test. The red run was not executed separately; the first test run was the
  post-fix fastcheck.
- `tools/fastcheck.ps1 -All`: fastcheck: PASS in 297s (Content 1035, Sim 1637, Bots 474, Playtest 385).
