# Lane: fix/bot-taunt-thrift (finding f-dc7eff7d, ruling R-TAUNT-5)

- Goal: G2.1 bot tank taunt thrift.
- Branch: fix/bot-taunt-thrift (red commit 51da6441; implementation commit 1985a138).
- Change: TankEngage.TauntTarget takes the in-range loose enemies in LooseEnemies order and
  partitions them stably so not-doomed enemies come first; a doomed enemy is taunted only when
  nothing else is in taunt range. Doomed-ness (DiesInTauntWindow, CommittedDamage) is an ordering
  key, never a veto. Rule (b) unchanged.
- Tests: TauntThriftTests rule (a) test now asserts ordering; new single-doomed-still-taunted test.
  BotTankTakesHolderTests and all other tests unchanged.
- Gate: fastcheck -All FAIL. Content 1044/1044, Sim 1637/1637, Bots 480/480 pass.
  Playtest 383 run, 2 failed:
  - "The tutorial run (key 2, seed 1) kills the boss with seat 1 as each class (bots on every seat, headless)(classId: cinder)"
  - "The bot Warden holds a member of every pack within 60 ticks of it engaging, human Cinder (seeds 1..5)(seed: 3)"
  Whether the tutorial failure predates this change is not established (not bisected).
- Bisect (targeted playtests, Keyrun.Playtest.Tests filter on BotTankTakesHolderTests + Tutorial_completes_as_each_class):
  | config | tutorial Cinder | seed-3 holder |
  | as-is 8ce165f1 (a + c) | FAIL | FAIL |
  | a off, c on | PASS | PASS |
  | a on, c off | FAIL | FAIL |
- Rule (a) REMOVED at ship: the doomed ordering in TauntTarget and DiesInTauntWindow, plus the two rule-a tests.
  blocked: breaks BotTankTakesHolderTests seed 3 (and the tutorial Cinder run) when enabled.
  Rule (c) (LooseEnemies SeatHealthKey ordering) kept. CommittedDamage (public R-TAUNT-4a estimate) left in place, now unused.
- Tutorial red was caused by rule (a) on this branch, not pre-existing; main was not checked because the bisect already attributes it.
- Status: ship candidate; fastcheck -All gate pending on the rule-(a)-removed commit.
