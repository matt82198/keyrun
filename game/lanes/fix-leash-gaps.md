# Lane row: fix-leash-gaps (G2.5, finding f-1c94a912)

- Goal: close the leash follow-up gaps so evade is a deterministic, exploitable system (R-SYNERGY-2, R-BOTTANK-LEASH-1).
- Base: origin/main 56bb133e (#648).
- Premise check: all four gaps confirmed open on origin/main (none refuted).
  - Gap 1 (evader immunity only at target resolution): `Combat/Casting.Start.cs` StartError had no Evading check. Fixed.
  - Gap 2 (DoT on an evader ticks until reset): `Encounter/Leash.cs` cleared Effects only in the reset branch. Fixed at evade start.
  - Gap 3 (reset patrol teleports via Patrols.Walk): `Encounter/Patrols.cs` Walk assigned `Pos = MemberAt`. Fixed with `ActorState.ResetWalk` (`Core/State.cs`).
  - Gap 4 (cast started on an evader): same StartError path as gap 1. Fixed.
- Tests: `game/sim/Keyrun.Sim.Tests/Encounter/LeashGapsTests.cs` (red commit 58a50696 observed failing for all three gaps before the fix).
- Error code added: `evading` (actor is evading and casts nothing).
- Untouched: bot code, content balance numbers.
