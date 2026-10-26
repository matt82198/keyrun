# Keyrun threat model and the tank's taunt rule

Status: design study for finding f-e6e80660, written 2026-10-22 against main 910c18fd. It is a docs-only change and
does not implement any rule. It proposes R-TAUNT-3 for an implementation lane to build red-first.

**Amended 2026-10-25 (R-THREAT-2, finding f-7b374e72, against main a5b4595d): section 7 replaces the sticky holder
rule with WoW Mythic+ threat.** Sections 0.1, 1 and the D30 rows of section 2 describe the old rule and are marked
superseded where they conflict. R-TAUNT-3's decision table (section 4) stands.

Reading paths:
- **Deciding the rule:** section 0, then sections 4 and 5.
- **Implementing it:** sections 1, 4 and 5.
- **Questioning the diagnosis:** section 3 and Appendix A.

## 0. Summary

1. **SUPERSEDED 2026-10-25 by R-THREAT-2 (section 7, finding f-7b374e72).** The sticky clause below describes main
   a5b4595d and is no longer the rule: damage and healing now make threat, and a seat past 110% (melee) or 130% (range)
   of the holder's threat takes the enemy. Original text, kept for the record: *Holder-lock replaces aggro in keyrun.
   An enemy attacks one holder. The holder is the nearest living player at engage. After that only a taunt moves it,
   and the holder's death hands it to the nearest living player. Damage, healing and threat never move it (section 1).
   The threat table still fills, but nothing that picks a target reads it.*
2. **R-TAUNT-2 did not cause the key 10 seed 7 wipe on its own.** Measured on main today with the host autorun party,
   the Warden dies at tick 557 holding all three Choir enemies under every taunt rule: the current one, R-TAUNT-2 and the
   R-TAUNT-3 prototype. No taunt is needed before that tick. The first death comes from the tank's survival and the
   healer's throughput, not from threat (section 3.2).
3. **R-TAUNT-2's measurable defect is a wasted taunt.** At tick 854, R-TAUNT-2 spent the 160-tick Taunt on the Choir
   Cantor. The Cantor was in range but stood 10 m from its holder and was hurting nobody. Meanwhile an Acolyte out of range
   was beating the 250-health Chanter. R-TAUNT-2 also dropped the R-TAUNT-2b walk, so the Warden did not close the
   distance while Taunt cooled. That gap is what turned the first death into a cascade: the Warden died at
   557/1173/1745/2174, the Choir cleared at 7589 and there were 22 deaths. The current targeting has 9 deaths and clears
   the Choir at 4682 (section 3.3).
4. **R-TAUNT-3 is a five-row decision table (section 4).** Taunt the enemy that is hurting a non-tank seat. Never spend
   Taunt on a quiet enemy while a damaging one exists; walk at the damaging one instead. Never read the threat table.
   Both tank brains (the host's ScriptedWarden and the bots' TankEngage) use one shared function.
5. **R-TAUNT-3 does not by itself make key 10 seed 7 timed.** On 10 key and seed runs the prototype matched the current
   targeting on 9 run outcomes (Appendix A). It fixes the R-TAUNT-2 failure and states the invariants as tests. The key
   10 Choir wipe needs a separate lever (section 6, recommended finding F1).

> **Correcting the finding text:** "the previous targeting held 371/371" is the Bots test suite count (commit
> dbce2bd90b in #573: "Bots 371/371 again, key 10 seed 7 clears"). It does not mean the party survived. On main
> 910c18fd the previous targeting at key 10 seed 7 still loses the Warden four times on the Choir, and the run ends
> Depleted at tick 10547 (section 3.1).

## 1. How threat works in keyrun today

### 1.1 Two mechanisms, only one of which picks targets

| Mechanism | Where | Moves the enemy's target? |
|---|---|---|
| **Holder** (`ActorState.HolderId`) | game/sim/Keyrun.Sim/Combat/Holder.cs:6-14 | **Yes.** Scripted casts and the chase go at the holder only |
| **Threat table** (`ActorState.Threat`, `SortedDictionary<int,long>`) | game/sim/Keyrun.Sim/Core/State.cs:333-334; Combat/Threat.cs:6-81 | **No** for any enemy with a holder. It is read only by the fallback for an enemy without one (ScriptTarget.cs:129-134) |

The holder rule is STATE.md D30 (Matt, 2026-10-20). D30 is on PR #306, which is still open, but the code that
implements it is merged (#307). Holder.cs:7-8 quotes Matt: "no aggro meters ... but no pulling aggro unless they
intentionally do". Holder.cs:12-13 adds: "Damage, healing and threat never move it. The threat table ... still fills but
no longer picks the target".

### 1.2 The holder lifecycle

- **Engage (body pull).** Each tick, `Holder.LockHolders` locks every living, non-idle enemy with no living holder onto
  the nearest living player (range, then lowest id) (Holder.cs:50-69). `RunWiring.FixupTargeting` calls it
  (game/sim/Keyrun.Sim/Run/RunWiring.cs:233). An idle prespawned enemy is skipped until its group engages
  (Holder.cs:55-61).
- **Taunt.** A completed cast of an ability that is target-shaped, tagged `utility` and carries a `threat` op is a
  taunt (Holder.cs:27-30). On completion, `Casting.Complete` calls `Holder.ApplyTaunt` for each target
  (Combat/Casting.Complete.cs:72-81). That sets `target.HolderId = caster.Id` when both are alive (Holder.cs:35-41).
  The change is permanent: nothing expires it.
- **Holder death.** The next `LockHolders` pass finds no living holder and locks the nearest living player
  (Holder.cs:62-68). The hand-off goes to the nearest player, not the highest threat. ScriptTarget.cs:114 says so
  explicitly: "a dead holder falls to the nearest living candidate, never the top threat".
- **Leash or evade.** The group drops target, threat and holder (game/sim/Keyrun.Sim/Encounter/Leash.cs:96-97).
- **Release.** A dead seat respawns at the entrance 100 ticks after death (RunWiring.cs:26-29). The respawned tank has
  to walk back to the pull.

### 1.3 How an enemy picks its target

`ScriptTarget.Resolve` (game/sim/Keyrun.Sim/Encounter/ScriptTarget.cs:96-145) works in this order:

1. A hostile pick by an enemy with a holder goes at that holder if the holder is in the ability's range
   (ScriptTarget.cs:115-120).
2. If the holder is alive but out of range, the cast is **blocked**. The enemy does not switch to a player in range
   (ScriptTarget.cs:121-126).
3. The threat-then-nearest pick (ScriptTarget.cs:87-89, :133) applies only to an enemy with no holder.

### 1.4 What still writes the threat table

- **Momentum spent:** one threat per point on the spender's current hostile target (Combat/Threat.cs:21-40, called
  from Combat/Casting.Start.cs:71). BIBLE s5 says "Threat is generated by Momentum spent" (design/BIBLE.md:122). D30
  ruling (3) overrides that line for target selection; PR #306 proposes rewording it.
- **`threat` ops:** applied on cast completion, in combos and in effect rules (Casting.Complete.cs:154-155,
  Combos.cs:186-187, Rules.cs:324-325).
- **The taunt itself:** `{op: threat, amount 1, multiplierBp 100000}` (content/data/abilities/warden.json:250-255). It
  adds 10 threat, which does not matter: the holder change is what counts.

### 1.5 The taunt as content

| Field | Value | Source |
|---|---|---|
| Cast | instant, off the GCD (`castTicks 0`, `gcdTicks 0`) | content/data/abilities/warden.json:238-239 |
| Cooldown | 160 ticks (8 s at 20 Hz) | warden.json:240 |
| Range | **3000 mm**, the same as the Choir melee reach (content/data/abilities/enemies.json:9) | warden.json:242 |
| Shape and tag | target, `utility` | warden.json:243-249 |

Other numbers that bound any pickup rule:

- The Warden moves at 180 mm per tick (game/content/data/run.json:3). A Choir Acolyte moves at 207 mm per tick
  (content/data/enemies/choir.json:7), so it out-paces him.
- The Acolyte swings every 40 ticks for 70 raw damage (choir.json:9-10; enemies.json:3-21), which is 91 raw at key 10
  (Appendix A).
- The Choir is two Acolytes plus the Cantor (`choir.lieutenant`), whose script is a heal cast every 200 ticks
  (choir.json:13-21). Seats have 400 health (Warden) and 250 (Chanter and Cinder) (probe log, tick 51).

### 1.6 The two tank brains

| Seat | Brain | Taunt rule today |
|---|---|---|
| Host seat 1 in autorun, HudSceneTest and HostPathThreatTelegraphTests | `ScriptedWarden` (game/src/Host/ScriptedWarden.cs) | Three presses in sequence, described below |
| Bot tank (Playtest scripted party; any bot Warden) | `TankRole` and `TankEngage` (game/bots/Keyrun.Bots/Roles/Tank.cs, TankEngage.cs) | Taunt the nearest loose enemy when it is in range (TankEngage.cs:143-148, :174-180); otherwise approach it (:182-193) |

ScriptedWarden's three presses:
- **LOOSE-PICKUP:** taunt the nearest loose enemy if it is in range; if it is out of range and Taunt is ready, walk
  at it (ScriptedWarden.cs:192-210).
- **R-TAUNT-2b:** while Taunt cools down, walk at the nearest enemy that is damaging a non-tank seat out of range
  (:203-215, :284-306).
- **HOLDER-LOCK press:** when LOOSE-PICKUP did not fire, taunt the nearest enemy in range that another living seat
  holds (:234-240, :308-327).

"Loose" means an enemy whose holder (or else its target) is another player (TankEngage.cs:60-85). The tank "holds" an
enemy exactly when he is its holder (TankEngage.cs:46-58). Tank.cs:33-40 skips defensives while a loose enemy exists
and the tank is above 35% health (R-BOTTANK-LEASH-1).

The two brains disagree today. ScriptedWarden has the R-TAUNT-2b walk and the HOLDER-LOCK press; TankEngage has
neither. R-TAUNT-3 gives both of them one rule (section 4.3).

## 2. What WoW Mythic+ does, limited to what this decision needs

The facts below are standard WoW mechanics stated from general knowledge. No source is cited here.

1. **Each enemy keeps its own threat table and attacks the top entry.** Tanks generate many times more threat per
   action than other roles, and their area abilities spread threat over the whole pack. That is how a tank holds a
   pack. Taunt is not the main tool.
2. **Healing generates threat** on every enemy in combat with the healer. A mob that gets loose, or whose tank dies,
   goes to the next entry on its table, which is often the healer.
3. **Taunt is a correction, not the plan.** Taunt puts the taunter at the top of the table and forces the target for a
   few seconds. It has a 30-yard range (far beyond melee) and an 8-second cooldown. When the forced window ends, the
   threat table decides again, and the tank's generation keeps the mob.
4. **Interrupting a caster does not make the caster attack the interrupter** in any way a tank relies on. The tank
   picks casters up by gathering them: line of sight, stops, and grips.

How this maps to keyrun under D30:

| WoW | Keyrun (D30 holder-lock) | Decided here |
|---|---|---|
| Threat generation holds the pack | Engage locks the holder; nothing but a taunt moves it | ~~Keep.~~ **Superseded by R-THREAT-2 (section 7):** threat generation holds the pack, as in WoW |
| Healing threat sends loose mobs to the healer | No healing threat. A dead holder's enemies go to the **nearest** living player (Holder.cs:66-68) | ~~Keep "nearest".~~ **Superseded by R-THREAT-2:** half-value healing threat, and a dead holder's enemy goes to the highest living threat (section 7.4) |
| Taunt forces the target for a few seconds, then threat decides | Taunt sets the holder permanently | **Keep permanent.** Under D30 nothing remains to return to after a timed fixate except the previous holder, which would undo the taunt |
| Taunt range 30 yd | 3000 mm | Not changed by this doc. The range lever was measured and is not decisive (Appendix A.3). The question is recorded in section 6 |
| Area threat retakes a pack after a death | No area take-threat exists, so one taunt retakes one enemy every 160 ticks | Open question Q1 (section 6) |

## 3. Failure analysis: key 10, seed 7

### 3.1 How the runs were reproduced

**Host path (the run behind the finding).** A scratch xUnit probe, not committed, drove the exact
HostPathThreatTelegraphTests setup (game/bots/Keyrun.Bots.Tests/HostPathThreatTelegraphTests.cs:80-139):
`HostRunSetup.Build` for hollow-vault at key 10 with the warden class, `StartFor`, `PartyFor`, `WithBotLoadouts`,
`RunRoot.CreateRun(seed 7)` and `BossRules.Arm`, then `ScriptedWarden` plus `BotCommandSource` stepped through
`Backfill.Step`. It logged every command and event plus a per-tick holder snapshot. Seat 1 is the Warden, seat 2 the
Chanter (healer), seat 3 the Cinder, and enemies 100 and 101 the Acolytes, 102 the Cantor.

The R-TAUNT-2 run swaps ScriptedWarden's taunt block for the code at 790484aced (#573 WIP, `TauntTarget`). The
R-TAUNT-3 prototype is described in section 4. Every swap was reverted with `git checkout` after measuring.

**Bot-tank path.** `dotnet run --project tools/Keyrun.Playtest -- run --key 10 --seed 7 --out DIR` (headless; the
game was never launched). With no policy, every seat including the Warden is the scripted bot party
(tools/Keyrun.Playtest/PlaytestRunner.cs:143-156), so this run measures `TankEngage`.

| Run (key 10, seed 7) | Result | Choir clear | Party deaths | Warden deaths (ticks) |
|---|---|---|---|---|
| Host, main (LOOSE-PICKUP + 2b + HOLDER-LOCK press) | Depleted, run end 10547 | 4682 | 9 | 557, 1151, 1481, 1910 |
| Host, **R-TAUNT-2** (790484aced rule) | Depleted, run end 13798 | 7589 | 23 | **557, 1173, 1745, 2174**, 2592, 3087 |
| Host, R-TAUNT-2 plus the 2b walk | Depleted, run end 10547 | 4682 | 9 | 557, 1151, 1481, 1910 |
| Host, R-TAUNT-3 prototype | Depleted, run end 10547 | 4682 | 9 | 557, 1151, 1481, 1910 |
| Bot tank (Playtest scripted party), main | untimed (wiped), 30 deaths, lost on the Choir | none | 30 | 359, 689, 942, 1173, 1833, ... |

The R-TAUNT-2 row reproduces the finding's death ticks exactly: 557, 1173, 1745 and 2174.

### 3.2 The first death is the same under every rule

The Warden holds all three Choir enemies from engage until he dies. Holder snapshots from the main host run (format:
enemy>holder@distance from holder in mm | seat:health/max):

```
H 51 100>1@7982 101>1@8777 102>1@8379 | 1:400/400 2:250/250 3:250/250
...                                     (no holder change for 506 ticks)
H 558 100>2@3136 101>2@3029 102>2@5489 | 1:0/400 2:250/250 3:250/250
```

No enemy is loose before tick 557, so no taunt rule can act: there are zero taunt commands in ticks 0-560 in every
variant. Over ticks 0-557 the Warden took 840 health damage (392 from Acolyte 100, 448 from Acolyte 101) and received
440 healing. Heals on the Warden came in five 40-point pulses at ticks 191-271, a 0 at 291, then **nothing from 291 to
471**, then 120 at 471 and 120 at 501. That gap is 180 ticks (9 s) with no healing on the tank. In the same window
the Chanter cast `chanter.dissonance` six times.

The other key 10 seeds show the same pattern. The Warden dies first, holding everything, at 623 (seeds 1 and 2), 656
(seed 3) and 689 (seed 11). Seed 5 is the only exception, with no death until 8084. The bot-tank Playtest run dies even
earlier, at 359, after 520 damage taken against 120 healing, and his first taunt is not until tick 793.

BIBLE s3.1 says the healer's sustained healing on the tank should match the tank's mitigated incoming damage on trash
(design/BIBLE.md:84-87). Here the healer delivers 440 / 840 = 52% of it. That is a healer-policy and survival question,
not a threat question (section 6, F1).

### 3.3 Where R-TAUNT-2 diverges: the wasted taunt at tick 854

When the Warden dies at 557, all three enemies go to the nearest living player, the Chanter (holder snapshot 558
above). The Warden respawns at 658 and walks back. Both rules taunt Acolyte 100 at 694. The divergence is the next
taunt, when it comes ready at 854:

```
main          854 -> 101 (Acolyte on the Chanter)  1014 -> 102 (Cantor)
R-TAUNT-2     854 -> 102 (Cantor)                  1030 -> 101 (Acolyte)

main      H 855 100>1@1944 101>1@3004 102>2@10034 | 1:360/400 2:68/250 3:250/250
R-TAUNT-2 H 855 100>1@2315 101>2@1995 102>1@2271  | 1:322/400 2:68/250 3:250/250
```

At 854 under R-TAUNT-2, Acolyte 101 is on the Chanter (101>2 at 1995 mm, in melee reach) and is outside the Warden's
3000 mm Taunt. The Cantor (102) is inside it and "held" by the Chanter, but it stands about 10 m from her and hurts
nobody. `TauntTarget`'s second criterion, "else the highest-threat enemy in range another seat holds", therefore picked
the Cantor and put Taunt on cooldown for 160 ticks. Two things made this worse:

- That criterion reads `e.Threat.Values.Max()`, a table that D30 retired from targeting (Holder.cs:12-13). Its values
  are Momentum noise.
- R-TAUNT-2 had dropped the walk while Taunt cools down, so for the next 176 ticks the Warden did not close on 101.

Under the current targeting, the R-TAUNT-2b walk had already carried the Warden toward 101, which was at 3004 mm after
the taunt landed. So 101 was the in-range loose enemy at 854. On the R-TAUNT-2 run the Chanter dropped to 53 health
by 1031. From there the run kept losing seats: the Warden at 1173, the Chanter at 1283, the Cinder at 1382. Each later
cycle ran the same way (Warden 1745, 2174, and so on): the respawned tank retakes one enemy per 160-tick Taunt while the
others beat the 250-health seats.

**Why the current targeting "survives":** it does not hold either. It loses the Warden at 557, 1151, 1481 and 1910 and
the Chanter and Cinder five more times. It wastes fewer taunts, so the cascade breaks after four cycles instead of
nine. The variant that pairs R-TAUNT-2's target choice with the 2b walk matches main exactly. On this seed the walk
decided the outcome, not the in-range choice.

### 3.4 The structural limit both rules share

After a tank death, every enemy goes to one player. With one single-target Taunt every 160 ticks, retaking k enemies
takes at least 160 × (k−1) ticks plus the walk. Measured on the main host run at key 10 seed 7 (loose episodes while
the Warden lived, ticks until he held the enemy again): 5, 30, 30, 34, 58, 58, 65, 65, 166, 190, 326, 350. The tail
(326, 350) is the third enemy of a pack. No targeting rule shortens it. Only an area take-threat, or not dying, does
(section 6, Q1).

## 4. Proposal: R-TAUNT-3 (candidate)

### 4.1 Terms

- **Seat:** a living player.
- **Tank:** the deciding Warden.
- **Aimed seat** of an enemy: its holder, or else its target (TankEngage.cs:65).
- **Damaging enemy:** a living, engaged (not idle) enemy whose aimed seat is a non-tank seat, and that is either within
  3000 mm of that seat (melee reach; `ScriptedWarden.OnSeatMm`, ScriptedWarden.cs:285) or casting at that seat
  (`CastTargetId`, else `TargetId`, equals the seat).
- **Quiet loose enemy:** a living, engaged enemy held by another living seat that is not damaging (for example a
  caster standing back).
- **In range:** `Casting.RangeMm(tank, enemy) <= taunt.Range`.
- **Ready:** Taunt is off cooldown. It has no cost and no GCD.

Ordering among damaging enemies, in this order:
1. The aimed seat's health fraction, lowest first (`health * 1000 / maxHealth`, integer).
2. Distance from the tank.
3. Lowest id.

This order is deterministic (BIBLE s3: never a coin flip) and protects the seat closest to death first. The threat
table is **never** read.

### 4.2 Decision table (evaluated every tick, before the rotation; Taunt is off the GCD)

| # | Situation | Taunt | Movement this tick |
|---|---|---|---|
| 1 | Ready, and a damaging enemy is in range | The first damaging enemy **in range** | No change (rotation and approach as today) |
| 2 | A damaging enemy exists, but none is in range (Taunt ready **or** cooling) | **None.** Taunt is held for it | Walk at the nearest damaging enemy (R-TAUNT-2b extended to "ready") |
| 3 | Ready, no damaging enemy, and a quiet loose enemy is in range | The nearest quiet loose enemy in range | No change |
| 4 | Ready, no damaging enemy, and quiet loose enemies exist only out of range | None | Walk at the nearest quiet loose enemy |
| 5 | No damaging or loose enemy (the tank holds everything) | None | Rotation, kick and defensives as today |

Rows apply top-down; the first match wins. The kick (R-WARDEN-KICK-1), the Cleave answer, R-PATROL-3 Brace and the
Lock hold are unchanged and keep their current order around this block. A movement from rows 2 and 4 replaces the
lieutenant approach for that tick, the same way R-TAUNT-2b does today (ScriptedWarden.cs:208-219).

What changes against today:

| Rule | Row 1 pick | Behaviour that R-TAUNT-3 removes |
|---|---|---|
| Main, ScriptedWarden | Nearest loose enemy, damaging or not | The HOLDER-LOCK press can taunt a quiet enemy in range while a damaging one is out of range (ScriptedWarden.cs:237-240). That is the 854 failure in a different position |
| R-TAUNT-2 | Damaging enemy in range | Fallback "highest threat in range" (reads a retired table) and no walk while cooling |
| TankEngage | Nearest loose enemy in range | No hold for a damaging enemy out of range; no walk while cooling |

### 4.3 One function for both brains

R-TAUNT-3 is one pure function in Keyrun.Bots, for example `Shared/TauntPick.cs`:

```csharp
TauntPick.Decide(SimState state, ActorState tank, AbilityDef taunt, int tick)
    -> (ActorState? Taunt, ActorState? WalkAt)
```

`ScriptedWarden.SampleRotation` and `TankEngage.TankEngageCommands` both call it. ScriptedWarden already links
Keyrun.Bots and calls `TankEngage.PickLooseEnemy` (ScriptedWarden.cs:195). The host autorun and the Playtest bot tank
then follow one rule, and one fixture suite covers both. `DamagingSeatOutOfReach` (ScriptedWarden.cs:295-306) and
`LooseEnemy` (:313-327) fold into it.

### 4.4 Invariants (each one becomes a property test)

| Id | Invariant |
|---|---|
| **I1 Never out of range** | No taunt command names an enemy outside `taunt.Range` at the command's tick. Taunt is never pressed into a refusal |
| **I2 No wasted taunt** | No taunt lands on a quiet loose enemy, or on an enemy the tank already holds, on a tick when a damaging enemy exists. This is the R-TAUNT-2 failure |
| **I3 Same-tick pickup** | On a tick where Taunt is ready, the tank is alive and a damaging enemy is in range, a taunt command is issued that tick (N = 0; Taunt is instant and off the GCD) |
| **I4 Bounded walk** | While row 2 holds, the tank issues a move toward the nearest damaging enemy every tick. A loose damaging enemy is taunted within N = max(0, cooldown remaining) + ceil((d₀ − 3000) / 180) + 40 ticks of the episode's start, where d₀ is the starting distance. The episode is exempt if the enemy dies, its seat dies, its holder changes, or the tank dies first. The 40-tick margin covers pathing around walls and an Acolyte (207 mm per tick) chasing a moving seat. On the prototype runs the first pickup of an episode took 5 to 65 ticks (Appendix A.2) |
| **I5 The healer never tanks a kicked caster** | (a) Sim: an interrupt by a non-tank seat never changes the interrupted enemy's holder. Only `Holder.IsTaunt` abilities move it (Holder.cs:27-30, Casting.Complete.cs:78-81, D30 rule 3). (b) Bots: a caster held by the healer while the tank lives is a damaging enemy (when casting at her) or a quiet loose enemy, so rows 1-4 take it back. Once no damaging enemy remains, the healer's hold on it lasts at most one Taunt cooldown plus the I4 walk bound |
| **I6 Determinism** | Same seed and command stream give byte-identical runs. No PRNG is used; ties break by id |

### 4.5 Acceptance test shape (red-first)

1. **Decision-table fixtures**, in a new file `game/bots/Keyrun.Bots.Tests/TauntPickTests.cs`. Each test uses a
   hand-placed `SimState` and checks one row, expecting `(Taunt, WalkAt)`. The case that must go red first:
   - **The 854 fixture:** the Chanter (250 max health, at 68) is held by Acolyte A at 2000 mm from her. A is 4000 mm
     from the Warden. The Cantor is held by the Chanter, 10000 mm from her and 2300 mm from the Warden. Taunt is ready.
   - **Expected:** `Taunt = null`, `WalkAt = A`. This is red against R-TAUNT-2 (it taunts the Cantor). It is also red
     against main's static rule: the nearest-loose pick plus the HOLDER-LOCK press taunt the Cantor when it is the
     nearest loose enemy.
   - **Other cases:** one case per row, including row 1 with two damaging enemies on seats at different health (the
     lower fraction wins) and a tie on id. Mutants: drop row 2's hold (red); order by distance instead of seat health
     (red); read `Threat` (red on a fixture where threat disagrees).
2. **Run properties**, in `tools/Keyrun.Playtest.Tests/TauntRulePropertyTests.cs`. Check I1-I4 on every tick of
   these runs:
   - **Host path:** the HostPathThreatTelegraphTests drive, with ScriptedWarden.
   - **Bot-tank path:** the Playtest scripted party, using `PlaytestRunner.Play` with no policy.
   - **Keys and seeds:** keys 2, 5 and 10, seeds 1, 2, 3, 5, 7 and 11.

   I5(a) is a sim unit test in `game/sim/Keyrun.Sim.Tests`: cast an interrupt by seat 2 on an enemy held by seat 1 and
   assert the holder is unchanged. Write the property tests red against R-TAUNT-2 by running them on 790484aced's
   `TauntTarget`; I2 fails at tick 854 of host key 10 seed 7.
3. **Outcome guard (measured, not a pass criterion of the taunt rule).** Keys 2 and 5 stay Timed with zero deaths on
   seeds 1 and 7, as they are on main (Appendix A.1). A change in the key 10 outcomes is printed in the PR body and not
   asserted, because the tank-death cascade dominates them (section 3.2).
4. **No oracle re-record.** Oracle runs have no layout, and ScriptedWarden is a host component. Bot oracle traces that
   move because TankEngage adopts the shared function need a re-record that carries a named property (R-ORACLE-1).

## 5. Rationale against the bible and WoW

- **BIBLE s3 ("a race is never a coin flip", design/BIBLE.md:49-50) and CLAUDE.md invariant 4 (fail closed):** every
  row is an integer comparison with id tie-breaks (I6).
- **BIBLE s3, soft roles:** a party without a healer is harder, never blocked. R-TAUNT-3 protects the seat closest to
  death, whatever its role. It names no class.
- **D30 rule 3 and rule 4:** the rule moves holders only through the taunt op. It reads no threat. It is written
  against "aimed seat" and "taunt ability", so a future take-threat op on another class (D31) plugs into the same
  function.
- **WoW:** a tank does not spend Taunt on a mob that is not hurting anyone while a mob beats the healer. It closes the
  distance and taunts the one that matters. Rows 1 and 2 are that habit, stated as a rule.

## 6. Open questions and follow-ups

Open questions, only where the bible and WoW are both silent or where D30 rules WoW's answer out:

- **Q1 Retaking a whole pack after a tank death.** WoW's answer is area threat, which D30 rule 3 forbids. Keyrun has
  no area take-threat, so a respawned Warden needs at least 160 × (k−1) ticks to retake k enemies (section 3.4). Should
  the D31 toolkit give the Warden an area take-threat (for example a Guard Break variant, a talent, or a longer-cooldown
  shout), or is the slow retake the intended cost of a tank death? This is a Matt question: it changes the Warden's
  five buttons (BIBLE s2 budget) or a talent fork (BIBLE s6).
- **Q2 Taunt range.** WoW uses 30 yd; keyrun uses 3000 mm, a melee taunt. The bible gives no number. Measured on
  10 key and seed runs, 10000 mm and 25000 mm moved key 10 outcomes in both directions (Appendix A.3), so this doc does
  not change it. Revisit after F1, when the tank-death cascade no longer masks the effect.

Recommended findings (not decided here; out of this lane's scope):

- **F1 Key 10 Choir survival.** The Warden dies holding the Choir at 557 to 689 on five of six key 10 seeds under
  every taunt rule. On seed 7 he receives 440 healing against 840 damage over 506 ticks, with a 180-tick gap with no
  healing (291-471) while the Chanter casts Dissonance. BIBLE s3.1 asks for the healer to match the tank's mitigated
  intake on trash. Owner: the HealerRole triage (game/bots/Keyrun.Bots/Roles/Healer.cs:26, `HealThresholdPct 0.7`) and
  the R-PATROL-3 Brace hold. Measure before tuning.
- **F2 The bot tank's first taunt.** In the Playtest bot-tank run at key 10 seed 7, the first taunt is at 793 and the
  tank is dead at 359. Adopting the shared function (section 4.3) gives TankEngage the row 2 walk.

## 7. Amendment R-THREAT-2: WoW Mythic+ threat (2026-10-25, finding f-7b374e72)

Matt, 2026-10-25, verbatim: "Tanks very often will run ahead of the group while they wait out of LOS, and grab all
the packs and bring them back... that is how high level m+ is played optimally actually... this maybe needs a
punishment in gameplay, because of our aggro rules... it allows the tank to just hit something and run ahead,
realistically if he just ditches the other guys with an existing pack tthey do need to de-aggro and kill the dps".
Standing rule: core dungeon-loop questions defer to WoW Mythic+.

**What this supersedes.** D30's sticky clause ("damage, healing and threat never move it"; Holder.cs:12-13, the
`LockHolders` "a living holder is never replaced" rule at Holder.cs:50-70) and the nearest-player hand-off on a
holder's death. The holder stays as the name for "the seat this enemy attacks" (WoW's current target), and ScriptTarget
still aims at it, so section 1.3 is unchanged. R-TAUNT-3 (section 4) stands as the rule for *when* the tank taunts; its
ban on reading the threat table is lifted, because the table is now live. Every number below is an integer in basis
points (bp, 10000 = 100%), per BIBLE s3 ("damage is a pipeline of integers").

### 7.1 Threat sources

| Source | Threat written | Notes |
|---|---|---|
| **Damage** (every point) | `trunc(dmg * abilityBp * classBp / 100_000_000)` on the damaged enemy, to the damage's source | `dmg` is the pipeline amount after armor, before absorbs, not capped by remaining health. A DoT tick credits its applier; Combo damage credits the seat whose Payoff fired it |
| Ability coefficient `abilityBp` | 10000 by default. An ability's existing amount-less `threat` op now sets it: Guard Break 15000, Reckoning 20000, Lock (Warden) 20000 | Those ops write 0 today (`t.Amount ?? 0`, Casting.Complete.cs:159-160); they become coefficients |
| **Healing** | `trunc(heal * 5000 * classBp / 100_000_000)`, divided evenly (integer division, remainder dropped) across living, engaged, non-evading enemies whose table holds the healed seat | `heal` is effective healing (overheal excluded). Absorb shields make none. Healing never engages a pack. WoW: half, split |
| Flat `threat` ops with an amount (Shield Bash 50, Taunt 1 x 100000 bp) | Unchanged amounts, then `* classBp / 10000` | |
| **Momentum spent** (Threat.cs:29-40) | **Retired.** Reckoning's 20000 coefficient carries the spender's threat | BIBLE s5:127 "Threat is generated by Momentum spent" needs a BIBLE wording PR (not this lane) |
| Engage (body pull) | The locked holder gets an entry of 0 | So a heal on the tank splits onto a pack he body-pulled |

No decay. An entry clears on the source's death (adopts the pending fix/death-clears-threat), on evade, and on wipe.

### 7.2 The tank multiplier

**`classBp`: Warden 20000, Cinder 10000, Chanter 10000**, a new `threatBp` field on each class json (default 10000).
It is a class number, not a stance, so a Facet or fork may change it later (D31 rule 3). How it holds aggro without
ThreatOp spam, per target, using content numbers:

- Warden Guard Break (circle 2500, every 30-tick GCD): 100 x 1.5 x 2 = **300 per member per GCD, 10 a tick**.
- Warden single target, Strike x7 then Reckoning: (7 x 160 + 1600) / 240 ticks = **11.3 a tick**.
- Cinder best single target, Ember Bolt + Detonate per 60 ticks: 340 / 60 = **5.7 a tick**. Chanter Dissonance: 2.2.

A rotating Warden runs at 1.76x to 2x the Cinder, above the 1.3x range bar, so a DPS never takes an enemy from a
tank who keeps hitting it. A Warden who presses Strike only (5.3 a tick) loses a focused enemy to the Cinder: that
is intended, since threat is earned by playing the rotation.

### 7.3 Overtake and taunt

- **Melee range is 3000 mm** (`Casting.RangeMm(enemy, seat) <= 3000`), the Acolyte reach, Strike range and Taunt range.
- **Overtake:** a seat `c` takes enemy `e` from holder `h` when `c * 10000 > h * 11000` with `c` in melee range of
  `e`, or `c * 10000 > h * 13000` otherwise (strictly greater). Several qualifiers: highest threat, then lowest id.
  It is checked once per tick in the holder pass (where `LockHolders` runs, RunWiring.cs:233), after the engage lock,
  on every living, non-idle, non-evading enemy. Against a holder at 0, any positive threat qualifies.
- **Taunt** (Holder.IsTaunt, unchanged detection; range 3000, cooldown 160): the taunter becomes holder, its entry is
  raised to the table's top value after the taunt's own threat op lands, and the enemy is **fixated for 60 ticks**
  (WoW's 3 s), during which no overtake is checked. After the fixate, an equal entry does not overtake, so the
  taunter keeps it until someone passes the bar.
- **Holder death:** the highest living entry takes it (ties lowest id), with no bar. An empty table falls back to
  the nearest living player (the body-pull rule).
- **Bosses** follow the same rules.

### 7.4 Gather pulls and the leash

- **A pack nobody else damaged follows the tank.** Its table holds only the tank (and halved, split healing), so
  nothing passes the bar and it chases its holder. Gather check: a Guard Break tag puts 300 on each member; for the
  healer to take a member at range she must pass 390, which takes more than 780 x (members) effective healing on the
  tank during the gather: over 1560 for a two-member pack, nearly four times the Warden's 400 health.
- **The leash stays 30000 mm (packs) and 17000 mm (boss), measured from each member's own post** (run.json:43-52,
  Leash.cs:85). A gathered pack keeps following anywhere inside it. The fight spot is chosen inside every gathered
  pack's leash, as DUNGEON-SYNERGY's "+10 chain inside the leash" already requires.
- **Amended: the leash evades per member, not per pack.** A member past its leash evades alone (stopcast, drop holder,
  threat and effects, walk home, reset on arrival); its pack-mates fight on. Without this, the untouched member the
  tank drags past 30 m would evade the whole pack, including the members that turned on the DPS, and the punishment
  in 7.5 would never land. A pack all of whose members are dragged still evades whole, as today.

### 7.5 The punishment, tick by tick

Setup: a two-Acolyte pack (100, 101) stacked at post P. The party waits at S, 10000 mm west of P (outside the 8000
ring). The Cinder focuses 100 (Ember Bolt, Detonate on cooldown at 30 Heat, circle 1500 catching both); the Chanter
casts Dissonance (circle 2500) on cooldown. Both stay inside their 25000 mm range. All are at range: the 130% bar.

| Tick | Event | 100: Warden / Cinder / Chanter | 101: Warden / Cinder / Chanter | Holder |
|---|---|---|---|---|
| 0 | Warden Guard Break at P (300 each), then runs east at 180 mm/tick. Chanter Dissonance | 300 / 0 / 130 | 300 / 0 / 130 | Warden; the pack chases him at 207 |
| 20 | Ember Bolt lands | 300 / 90 / 130 | 300 / 0 / 130 | Warden |
| 50 | Ember Bolt lands (Heat 40) | 300 / 180 / 130 | 300 / 0 / 130 | Warden |
| 60 | Detonate (250 both) and Dissonance. Bar is 390 | 300 / **430** / 260 | 300 / 250 / 260 | **100 turns onto the Cinder**; 101 stays on the Warden (10800 mm east of P) |
| 60-160 | 100 runs about 20 m back to the Cinder at 207 mm/tick | Cinder keeps growing | 101 out of Detonate's circle; Dissonance 390 at 120 is not over 390 | 101 stays on the Warden |
| ~160 | 100 reaches melee and swings at the Cinder (250 health), every 40 ticks | | | |
| ~170-185 | 101 passes 30000 mm from P behind the Warden | | Evades alone, walks home (about 145 ticks), resets near tick 320 | none |
| ~240 | At key 10 (91 a swing) the third swing kills the Cinder, unless she Blinks, uses Ashen Skin, or the tank comes back and Taunts 100 | | | |

The tank reaches 40 m at tick 222, by which time he has lost the damaged enemy to the DPS and the untouched one to the
leash. If the DPS had held fire (a gather), both Acolytes follow him; if they AoE every member, every member turns by
the second Detonate (tick 120, 500 > 390) and nothing leashes.

### 7.6 Impact and slice order

**Tests re-premised (S1 owns them).**
- game/sim/Keyrun.Sim.Tests/HolderLockTests.cs: `:74` inverts (a DPS past 130% takes the enemy); `:99` gains the
  set-to-top value and the 60-tick fixate; `:122` the highest living threat takes it, not the nearest; `:141` a body
  pull holds only until someone passes the bar; `:162` (holder out of range blocks the cast) is unchanged.
- game/sim/Keyrun.Sim.Tests/WardenMomentumThreatTests.cs: rewritten as damage-coefficient tests (Momentum threat
  retired). CombatThreatCooldownTests.cs, CoreStepTests.cs, Encounter/ScriptTargetTests.cs, ScriptTargetRandomTests.cs
  and EncounterTraceTests.cs: threat-table values move; re-derive them, never fit.
- game/bots/Keyrun.Bots.Tests/HolderLockBotTests.cs `:27`, `:33`, `:48`, `:83`: the table now decides.
- tools/Keyrun.Playtest.Tests/BotTankTakesHolderTests.cs (its R-THREAT-2 comment at :26), RunCWardenPicksUpLooseTests.cs
  `:143`, ScriptedWardenTauntTests.cs.

**Bot behaviour that changes.**
- **Bot tank threat upkeep** (Roles/TankEngage.cs, Tank.cs, game/src/Host/ScriptedWarden.cs): an engaged member on
  which a non-tank seat holds at least 90% of its overtake bar is the next Guard Break or Strike target, ahead of the
  rotation's damage pick. R-TAUNT-3's rows still pick the taunt.
- **Bot tank gather** (f-7b374e72 observable 3): the tank picks a fight spot S inside every target pack's leash, tags,
  and walks back to S where the party waits. R-BOTTANK-LEASH-1 is amended to allow running ahead only on a gather that
  returns to the party.
- **DPS threat awareness** (Roles/Damage.cs): open on an enemy only once the tank's entry on it is above 0; when the
  seat's own threat reaches 90% of the bar on its target, switch target or cast a non-damaging filler.
- **Guided bots G2.4** (Guides/GuidedTankRole.cs, game/bots/GUIDE.md, GuidedWardenGuideSectionsTests.cs): the tank
  guide gains threat upkeep and gather lines; the DPS guides gain "let the tank tag first".

**Oracle setups to re-record** (game/sim/Keyrun.Sim.Tests/Fixtures/oracle/, R-ORACLE-1: non-required, each re-record
names the property test that moved it): bots-hv-k2-s7, bots-hv-k2-s11, bots-hv-k10-s7, content-pull and scripted-pull
traces, plus manifest.json.

**Other docs to amend later (not edited here):** DUNGEON-SYNERGY.md 2.2 (whole-pack evade becomes per member), 2.3
(the aggro drop's receiver becomes the highest living threat in sight, not the nearest), and the ranged tag line at
:362-363 (a Cinder tag from range now pulls the pack to the Cinder); BIBLE s5:127.

**Slice order** (each slice opens with one failing test):
1. **S1 sim threat:** sources (7.1), `classBp` (7.2), overtake and holder death (7.3), owning every re-premised test.
   Red first: a Cinder past 130% of the Warden's threat at range takes the enemy (HolderLockTests:74 inverted).
2. **S2 taunt fixate:** red first: a taunted enemy is not overtaken for 60 ticks, and the taunter's entry equals the top.
3. **S3 per-member leash** (Leash.cs): red first: a split pack where the untouched member evades alone and the turned
   member stays engaged.
4. **S4 punishment and gather properties** (f-7b374e72 observables 1 and 2): the 7.5 scenario as a sim test (100
   turns onto the Cinder at tick 60) and a gather where an undamaged pack follows the tank to S.
5. **S5 bot tank upkeep and gather** (observable 3). **S6 DPS threat awareness.** **S7 guided G2.4 guides.** Each
   re-records the oracle traces it moves.

## Appendix A. Measurements

All runs were headless and the Godot game was never launched. The host path is the scratch probe from section 3.1 on
main 910c18fd. The scratch files were removed before commit. "Prototype" is R-TAUNT-3 as written in section 4.2,
patched into ScriptedWarden for measurement only.

### A.1 Rules × key and seed (host path, ScriptedWarden; Taunt range 3000)

| Key / seed | Main | R-TAUNT-2 | R-TAUNT-3 prototype |
|---|---|---|---|
| 2 / 1 | Timed 5545, 0 deaths | Timed 5545, 0 | Timed 5545, 0 |
| 2 / 7 | Timed 5702, 0 | Timed 5702, 0 | Timed 5702, 0 |
| 5 / 7 | Timed 8032, 0 | Timed 8032, 0 | Timed 8032, 0 |
| 10 / 1 | Timed 9143, 9 | Depleted 9303, 13 | Timed 9143, 9 |
| 10 / 2 | Timed 9285, 5 | Depleted 3887 (Choir wipe), 48 | Timed 9285, 5 |
| 10 / 3 | Depleted 3500 (Choir wipe), 44 | Depleted 11457, 27 | Depleted 3500, 44 |
| 10 / 5 | Timed 8946, 4 | Timed 8946, 4 | Timed 8946, 4 |
| 10 / 7 | Depleted 10547, 9 | Depleted 13798, 23 | Depleted 10547, 9 |
| 10 / 11 | Depleted 5407 (Choir wipe), 50 | Timed 9128, 4 | Depleted 5300 (Choir wipe), 48 |
| 15 / 7 | Depleted 8577, 59 | Depleted 14800, 38 | Depleted 8577, 59 |

Warden first-death tick at key 10 under all three rules: seed 1: 623, seed 2: 623, seed 3: 656, seed 5: 8084, seed 7:
557, seed 11: 689. These are identical across the rules, because no enemy is loose before them.

Seed 11 is the one run where R-TAUNT-2 beat main. Its order (Cantor at 994, then the Acolyte at 1162) let the Warden
outlive the Chanter at 1151 and break the cascade. That is a different death order, not a better pickup. It is one
seed against three where R-TAUNT-2 was worse.

### A.2 Loose-enemy pickup delays (host, key 10 seed 7; ticks from an enemy going loose with the Warden alive to his holding it)

- Main and the prototype: 5, 30, 30, 34, 58, 58, 65, 65, 166, 190, 326, 350.
- R-TAUNT-2: 22 episodes, maximum 366, including 160, 186, 189, 190 and 205. Seat deaths while loose: the Chanter at
  1281-1284 and 2700-2703.

### A.3 Taunt range lever (host; same runs, key 10 rows only)

| Key 10 seed | Main @3000 | Main @10000 | Main @25000 | Prototype @10000 | Prototype @25000 |
|---|---|---|---|---|---|
| 1 | Timed | Depleted | Timed | Depleted | Depleted |
| 2 | Timed | Depleted | Timed | Depleted | Timed |
| 3 | Choir wipe | Depleted | Depleted | Depleted | Depleted |
| 5 | Timed | Timed | Timed | Timed | Timed |
| 7 | Depleted | Timed | Depleted | Depleted | Depleted |
| 11 | Choir wipe | Timed | Timed | Depleted | Timed |

Keys 2 and 5 are unchanged in every column. No column dominates another, so the range is not the lever for the key 10
Choir (Q2).

### A.4 Bot-tank path (Playtest, main, key 10 seed 7)

Result: "untimed (wiped)", 30 deaths, lost on the Choir, clear 810 s against par 655 s. The first death is the Warden
at 359 from `choir.meleeSwing` (Acolyte 101), after taking 520 damage against 120 healing. His first taunt command is
at 793.
