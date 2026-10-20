# Fixtures/boss - lane 3F boss golden (not an oracle trace)

`hollow-warden-boss.trace.jsonl` is the seeded boss trace of `BossTraceTests` (seed 31337, 2000 ticks).

Why it is a C#-generated golden and not a TS oracle trace (ENGINE-PLAN 3F, section 5 phase 5 rule): the Sundered
Twins spawn, the 50% Lock with its 3 s Window, the party Setup-then-Payoff break and the three-fail enrage are NEW
behaviour (BIBLE s9). The frozen TS sim at `6e1548d` has no such rules (its Lock is a flat every-600-tick loop with
no 50% gate and no Twins spawn), so there is no oracle to compare against. The rules run only on a run armed with
`BossRules.Arm`; every oracle trace under `Fixtures/oracle/` is unarmed and stays byte-equal.

Format: line 1 is a header (`kind`, `lane`, `seed`, `ticks`, `traceHash` over the projection lines, `reason`); every
other line is one tick: `t` (post-step tick), `h` (FNV-1a of `Projection.Line`, as the oracle traces use) and `e`
(the tick's events, canonical JSON, in emission order).

Scenario: hollow-vault k2 content, boss 900 at 50% health, warden/chanter/cinder party. Lock 1 opens at tick 0 and is
broken by warden.strike (Setup, tick 5) then cinder.coldSnap (Payoff, tick 10, Shatter); the Twins' bar is emptied
off casts on both twins (ticks 40 to 44, `stagger`); Locks 2, 3 and 4 open unbroken, and the third failure enrages
the boss.

Regenerate only for a deliberate rules change: `KEYRUN_WRITE_BOSS_GOLDEN=1 dotnet test game/sim/Keyrun.Sim.Tests
--filter BossTraceTests`, then state the reason for the diff in the PR. Never regenerate to make a red test green.
