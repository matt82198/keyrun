# Keyrun toolkit contract: every class has a toolkit, and enemies kill a player who ignores it

Status: design contract plus an audit of main at 1911c618 (2026-10-22). Docs only. Ruling R-TOOLKIT-1 (aesop event
seq 258); goal G2.1 as amended.

Matt, 2026-10-22, verbatim: "when i played i did the same thing, those two abilities are all thats needed, and im saying
that is an issue, you should die if you're not using defensives, mobility, the whole toolkit. Every class gets a toolkit
remember? For instance I want rogue to have grapples and smoke bombs to drop aggro in the future... not all need extreme
mobility, but one mobility button minimum, then you figure out the rest based on their class category".

Companion documents written by another lane (referenced, not edited here): `design/PROGRESSION-PIVOT.md` (ability
improvement trees and gear drops) and `design/PLAYTESTER-GUIDE.md` (the guided playtester guide). Section 6 of this file
is the text that guide must absorb.

## 0. Summary

1. **The kits are already shaped correctly; the buttons and the enemies are not.** Every class on main has five
   rotational abilities plus Q interrupt, E defensive, R utility and F movement (BIBLE s2, s5;
   `content/data/classes/*.json`). The gap is what those buttons do and what the enemies demand.
2. **Two of the three movement buttons do not move anyone.** Cinder's Blink is a no-op: its only effect is
   `resource delta 0` (`content/data/abilities/cinder.json:266-283`). Warden's Charge is a ranged line hit with a stun.
   The sim has no op that moves the caster: the effect ops are damage, damagePct, heal, applyEffect, removeEffect,
   resource, threat, window, payoff, interrupt and pull, and pull moves the *target* (`game/sim/Keyrun.Sim/Combat/Types.cs:49-85`,
   `Movement.cs:62`). Only Chanter's Glide (+50% speed for 60 ticks) is real mobility.
3. **No enemy cast on the audited pulls can kill a full-health player at key 2, and walking escapes every lethal-sized
   telegraph.** The scratch tabulation over the enemy data (section 3.3; numbers below are its literal output) found
   `casts lethal to a full-HP 250 seat at key 2: 0`. The biggest hits are Clangour 230 and Cleave 220 against the 250
   health of a Cinder or Chanter. The casts walking cannot escape on cast time alone are small (Hollow Warden Slam 150,
   Twins and Clapper Peal 90, Undertow 10), and their 10-tick lead brings even those inside walking range. Key scaling
   does not raise enemy damage below key 5 (Tempo scales cadence, lead and health only,
   `game/sim/Keyrun.Sim/Encounter/Tempo.cs:21-61`; Fortified and Tyrannical start at key 5, `run.json` `affixes`).
4. **Bots never press F, and only the tank bot presses E.** `MovementAbilityId` is read nowhere outside
   `game/bots/Keyrun.Bots/Kits.cs`. `DefensiveAbilityId` is cast only in `Roles/Tank.cs:42`, on a health or pressure
   threshold, never in answer to a telegraph.
5. So Matt's report is right, and the cause is design, not the bot selector. Rotation plus WASD clears every audited
   pull and boss as long as a healer seat is alive. **This contract makes the F, E and R buttons do something, and then
   gives every pull and every boss phase one threat that is lethal without mobility and one that is lethal without a
   defensive.**
6. The audit counts 6 class gaps (2 per class), 3 shared bot or sim gaps, and 14 pull gaps (2 on each of the 7 audited
   encounters). Section 5 turns them into 14 lanes in five waves, file-disjoint within each wave, each with the red test it must write first.

## 1. Rules this contract stands on

- **Ten keybinds, frozen** (BIBLE s2, D4): 1-5 rotation, Q interrupt, E defensive, R class utility, F movement, G ping.
  The toolkit fits inside these ten. Nothing in this contract adds a key. (Observation, not a gap: the client also
  binds Tab to `targetCycle`, `game/src/Host/PlayerInput.cs:326-338`. It is treated like mouse targeting, outside the
  ten. Recorded so nobody counts it later and finds eleven.)
- **Cast bars are the enemy's language** (BIBLE s3): every threat in section 3 is a cast with a bar and a floor
  telegraph, shape plus colour, never colour alone. An unavoidable spike is no exception: its telegraph is a ring at
  the room edge in its own colour and edge pattern.
- **Keys are hard because of what enemies do, not their health** (BIBLE s0 test 1).
- **Class design follows Dark and Darker within the bible; dungeon design defers to WoW Mythic+** (keyrun STATE.md
  INTENT, Matt 2026-10-20). The class categories in section 2 are class design. The threat types in section 3 are
  dungeon design and each one names its Mythic+ precedent.
- **Determinism** (keyrun CLAUDE.md invariant 1): every new op is integer and deterministic. A threat that targets "a
  random player" picks by the sim's seeded PRNG or a fixed rule, never by arrival order.
- **Holder-lock threat** (design/THREAT-MODEL.md s0): only a taunt or the holder's death moves an enemy's target. The
  rogue's smoke bomb (section 2.5) is therefore a new holder rule, not a threat-table trick.

### 1.1 Constants every number here uses (key 2, no affixes)

| Constant | Value | Source |
|---|---|---|
| Tick rate | 20 Hz | BIBLE s3 |
| Walk speed | 180 mm/tick | `game/content/data/run.json:3` |
| Warden health, armor | 400, 3500 bp off every hit | `game/src/HostCore/HostRunSetup.cs:86`, `run.json` `classArmorBp` |
| Cinder and Chanter health | 250 | `HostRunSetup.cs:89` |
| Tempo | casts +2% more often and leads -1.5% per key above 1; health +3% per key | `Tempo.cs:21-33` |
| Affixes | Fortified (trash +30% damage) or Tyrannical (boss +15%) from key 5, both from key 10 | `run.json` `affixes` |

## 2. The class contract

### 2.1 Every class, every category

| Slot | Contract | Cooldown band (ticks) | Why it is a decision |
|---|---|---|---|
| 1-5 rotation | Exactly five: two Setup, two Payoff, one neutral (BIBLE s4, s5) | 0-200 | Unchanged by this contract |
| Q interrupt | One kick, 480 ticks (BIBLE s2) | 480 | Three kicks per 24 s against more casters than that (BIBLE s9.3) |
| E defensive | At least 3500 bp damage taken off for 60-100 ticks | 300-600 | A lethal spike comes every 400 ticks or slower (section 3), so E is back for each one if it was not spent early on trash. The decision is when: the duration must cover the hit's resolve tick |
| R utility | The class category's utility (2.2-2.6) | 160-600 | Spent on the right target at the right time (dispel, taunt, external, aggro drop) |
| F mobility | One button minimum, every class. Either an instant displacement of at least 6000 mm, or at least +5000 bp speed for at least 60 ticks. **Every F also frees the user from rooted and slowed on use** | 200-400, 1-2 charges | A charge spent for DPS uptime is a charge not held for the next mobility threat |
| G ping | Unchanged | none | |

"Not all need extreme mobility" (Matt): the band sets a floor, and categories differ above it. Ranged and rogue get
instant displacement. Tank gets a gap closer that also works as an escape. Healer gets the speed form.

A defensive or mobility button that does nothing in the sim fails the contract even if the data tags it correctly.
The content validator rule in lane L1 checks the effect, not the tag.

### 2.2 Tank (Warden on main)

- F: **gap closer that is also an escape.** Charge moves the Warden up to 8000 mm toward its target, or straight along
  its facing when no enemy is in the line, then hits and stuns what it reaches. WoW precedent: Warrior Charge and
  Heroic Leap, Demon Hunter Infernal Strike.
- E: Brace, -4000 bp for 60 ticks (unchanged). Vigil (rotation 5, -3000 bp for 50 Momentum) is the second layer, so a
  tank buster is answered by Brace or by banked Momentum, which is a real resource decision (BIBLE s3.1).
- R: Taunt (unchanged).
- Rotational mitigation: Bulwark (party patch, -1500 bp) and Vigil stay where they are.

### 2.3 Healer (Chanter on main)

- F: Glide, +5000 bp speed for 60 ticks, plus root and slow removal on use (new).
- E: **Aegis becomes "self or one ally"**: one button, the healer's external. Pressed with an ally targeted it lands
  on that ally, otherwise on the Chanter. WoW precedent: Priest Pain Suppression, Monk Life Cocoon. This keeps the ten
  keys and gives the healer the one button that can save a partner who missed their own E.
- R: Cleanse (dispel, unchanged).
- Rotational party mitigation: Ward-verse Chorus (-2000 bp pulse) and Ward Cadence/Refrain shields stay.

### 2.4 Ranged (Cinder on main)

- F: **Blink**: an instant 8000 mm displacement along the facing, which also frees from rooted and slowed. WoW
  precedent: Mage Blink, which teleports forward and frees the mage from stuns and roots.
- E: Ashen Skin, -3500 bp for 60 ticks (unchanged).
- R: Purge (dispel a magic effect, or an enrage from an enemy, unchanged).
- Kiting: Cold Snap's 20-tick freeze is the kiting tool already in the rotation. No change in the POC.

### 2.5 Rogue-type (future class, contract only)

- F: **Grapple**: a displacement to a target point up to 12000 mm, over gaps. WoW precedent: Outlaw Rogue Grappling
  Hook.
- R: **Smoke Bomb**: every enemy whose holder is the rogue, within the smoke radius, drops the rogue as holder and
  re-picks the nearest other living player (so the tank, if he is close). This is a new holder rule beside taunt and
  death (THREAT-MODEL s1.2). Dark and Darker has a Rogue Smoke Pot that breaks targeting; WoW's equivalents are Feint
  and Vanish.
- E: an evasion (damage taken down, or the next melee hits miss).
- No rogue lane is cut now. The class does not exist, and Matt said "in the future".

### 2.6 Melee DPS (future class, contract only)

- F: a leap or gap closer, at least 6000 mm.
- R: aggro shed (the holder-drop rule from 2.5, on one enemy) or a stop.
- E: a defensive in the 2.1 band.

## 3. The enemy contract

### 3.1 The rule

**Every pull and every boss phase carries at least one threat that kills a seat that does not use its F, and at least
one threat that kills a seat that does not use its E.** "Kills" means from full health at key 2, without affixes, with
the healer seat alive and healing. Each threat is a telegraphed cast: a bar, a floor shape and its own colour and edge
pattern (BIBLE s3). Kicks and dispels stay their own threats (BIBLE s9.3). A kickable cast never counts as the E threat,
because a perfect party would then never press E.

### 3.2 Threat types and their Mythic+ precedents

Each type is an invariant with starting numbers. A tuning lane may move the numbers but never break the invariant.
Each invariant becomes a property test.

| Type | Answer | Invariant (key 2) | Starting numbers | Mythic+ precedent |
|---|---|---|---|---|
| **M1 Outrun** | F | Warning W = lead + cast ticks; escape distance D satisfies `180*W + 1000 <= D <= 270*W - 1000`, so walking fails by at least 1 m and the weakest F (Glide) clears by at least 1 m. Damage at least 12000 bp of max health | W = 30, D = 6500 mm circle or the half-width of a wide line | Halls of Valor Hymdall's Dancing Blade and the Volcanic affix teach dodging; here the dodge is sized so only the movement button reaches safety, as Mythic+ big circles force a movement cooldown at high keys |
| **M2 Bind and strike** | F (frees the root) | Root (`rooted`, -10000 bp move) on every player in a zone, then a strike on those spots inside the root's duration, at least 12000 bp | Root 50 ticks; strike circle 2500 mm at 30 ticks | Ki'katal's Cosmic Singularity (Ara-Kara) pulls the party in to be hit; Blink's "frees you from roots" is the answer this copies |
| **D1 Unavoidable spike** | E (or the healer's external on you) | Hits every player wherever they stand; lethal from full with only the healer's party mitigation (Ward Chorus -2000 bp); survivable with E alone with at least 10% health left; not interruptible (shield-edge bar); cadence 400 ticks or slower | 13000 bp of max health, non-physical, cast 60, every 400 | Neltharion's Lair Rokmora's Shatter; the Bursting affix's stacking party damage that needs personal defensives |
| **D2 Tank buster** | Tank E or Vigil | On the holder; lethal after armor without Brace or Vigil; survivable with either | Raw 700 physical: 455 after armor (dead), 273 with Brace, 318 with Vigil | Every Mythic+ boss's tank buster that needs active mitigation |
| Frontal (existing) | Positioning, tank facing | Unchanged; at least 12000 bp on non-holders so the cone is lethal to stand in | Cleave, Pour Slag, Clangour, Crushing Slam | Mythic+ trash and boss frontals |
| Soak (existing) | Stack | Unchanged | Pair's Stack call | Mythic+ soaks |

Two notes on D1:
- **Armor is physical-only** (WoW: armor reduces physical damage only). On main the armor stage applies to every
  school (`game/sim/Keyrun.Sim/Combat/Damage.cs:68,80`; BIBLE s3.1 says "every hit"), so the Warden would take D1 at
  65% and never need E for it. Lane L3 makes armor
  physical-only and makes D1 non-physical, so D1 is lethal for the tank too. This is overrule call 1 (section 7).
- The Chanter's Aegis on an ally (2.3) is the one way a partner who missed E survives D1. That is synergy, and the
  healer's E is then not on the healer when D1 lands, which is the healer's own decision.

### 3.3 How the "two buttons suffice" verdict was measured

Scratch script `threat_table.py` (lane scratch dir, not committed) read every scripted cast of the audited enemies
from the data files. It compared each cast's escape distance with `180 * castTicks` and its damage with 250 health.
Literal summary lines of its run on 1911c618:

```
casts lethal to a full-HP 250 seat at key 2: 0
damaging casts walking cannot escape: [('hollowWarden.slam', 150), ('twins.slam', 90), ('twins.slam', 90), ('brineMother.undertow', 10), ('greatBell.peal', 90), ('greatBell.peal', 90)]
```

The second line counts cast time only. Those slams also have a 10-tick telegraph lead (`spansCast: true`), which puts
their 3000 mm radius inside walking range (180 * 20 = 3600). This is arithmetic over data, not a sim run. The measured
proof is lane L8's two-button seat (section 5), which must be shown surviving on main before the threat lanes and
dying after them.

## 4. Audit of main (1911c618)

### 4.1 Classes

Role key: ROT rotational, MOB mobility, DEF defensive, UTL utility, INT interrupt.

**Warden** (`content/data/abilities/warden.json`)

| Slot | Ability | Role | Real effect | Contract |
|---|---|---|---|---|
| 1 | Strike | ROT | 80 damage, Sundered window | ok |
| 2 | Guard Break | ROT | 100 cone, threat x1.5, payoff | ok |
| 3 | Bulwark | ROT (party mitigation) | -1500 bp patch, 200 cd | ok |
| 4 | Reckoning | ROT | 400 finisher, payoff | ok |
| 5 | Vigil | ROT (self mitigation) | -3000 bp for 100 ticks, 50 Momentum, 120 cd (`:152`) | ok |
| Q | Shield Bash | INT | interrupt, 480 | ok |
| E | Brace | DEF | -4000 bp for 60 ticks, 300 cd (`:208`) | ok |
| R | Taunt | UTL | taunt, 160 | ok |
| F | Charge | MOB (nominal) | 8000 mm line: 80 damage and a 20-tick stun. **The Warden does not move** (`:263`) | **GAP W1** |
| F | Charge | | Needs an enemy in the line, so it cannot be used to escape | **GAP W2** |

Warden: **2 gaps.**

**Cinder** (`content/data/abilities/cinder.json`)

| Slot | Ability | Role | Real effect | Contract |
|---|---|---|---|---|
| 1 | Ember Bolt | ROT | 90 fire, +20 Heat | ok |
| 2 | Flare | ROT | 110, Kindled window | ok |
| 3 | Cold Snap | ROT (control, stop) | 100, dumps Heat, 20-tick stun | ok (kiting tool) |
| 4 | Detonate | ROT | 250 payoff, 30 Heat | ok |
| 5 | Cinderfield | ROT | fire patch window | ok |
| Q | Quench | INT | interrupt, +20 Heat | ok |
| E | Ashen Skin | DEF | -3500 bp for 60, 300 cd (`:211`) | ok |
| R | Purge | UTL | dispel or soothe | ok |
| F | Blink | MOB (nominal) | **`resource delta 0`: nothing happens** (`:266-283`) | **GAP C1** |
| F | Blink | | No root or slow removal | **GAP C2** |

Cinder: **2 gaps.**

**Chanter** (`content/data/abilities/chanter.json`)

| Slot | Ability | Role | Real effect | Contract |
|---|---|---|---|---|
| 1 | Cadence | ROT (Ward: shield on target) | HoT or -3000 bp shield, Attuned window | ok |
| 2 | Refrain | ROT | verse swap, 150 heal or shield | ok |
| 3 | Chorus | ROT (Ward: party -2000 bp) | party heal or mitigation pulse | ok |
| 4 | Dissonance | ROT (Ward: silence stop) | 130 radiant, slow or silence | ok |
| 5 | Hymn | ROT (Ward: party speed +2000 bp) | heal or speed patch | ok |
| Q | Rebuke | INT | interrupt | ok |
| E | Aegis | DEF | -3500 bp for 60, **self only** (`:314`) | **GAP CH1** (no healer external) |
| R | Cleanse | UTL | dispel | ok |
| F | Glide | MOB | +5000 bp speed for 60, 2 charges (`:369`) | speed form passes; **no root or slow removal: GAP CH2** |

Chanter: **2 gaps.**

**Shared gaps (sim and bots)**

| Id | Gap | Evidence |
|---|---|---|
| S1 | No bot ever presses F | `MovementAbilityId` read only in `game/bots/Keyrun.Bots/Kits.cs:197` |
| S2 | Only the tank bot presses E, on a health or pressure threshold, never against a telegraph | `game/bots/Keyrun.Bots/Roles/Tank.cs:42` |
| S3 | The sim has no self-displacement op. A free, uncooled `DashCommand` moves 3000 mm in one tick (`Combat/Rules.cs:186`, `Movement.cs:75`, documented as "placeholder; no recorded decision"). No client or bot issues it today. If anything ever did, it would make F pointless | `grep "DashCommand("` finds only tests and `CommandJson.cs:101` |

### 4.2 Encounters

M = a threat lethal without mobility, D = a threat lethal without a defensive. "Two buttons suffice" means rotation plus
WASD clears it with a living healer seat (section 3.3).

**Hollow Vault** (`content/data/enemies/*.json`, `content/data/abilities/enemies.json`)

| Pull | Threats on main | Answering button today | M | D | Two buttons suffice | Gaps |
|---|---|---|---|---|---|---|
| Choir | Acolyte swing 70 (40 ticks); Cantor Choral Mending (kick, heals 6000) | Q kicks; nothing else | none | none | yes | **2** |
| Pair | Swings 70; Spread mark (80 to each within 3000); Stack call (150 split) | WASD | none (walkable) | none | yes | **2** |
| Seed | Slam 150 circle 2000 (cast 20 + lead 20); Lash 70 line; Lock window (combo) | WASD; payoff | none (walkable) | none (150 < 250) | yes | **2** |
| Hollow Warden | Cleave 220 cone 4000 (cast 40); Slam 150 circle 3000; Twins slam 90 (break bar); Lock (combo) | WASD; Brace on the tank is optional while healed (143 after armor) | none | none for non-tanks; the tank D is soft (143 per Cleave plus Cleaved stacks, healable) | yes | **2** |

**Sunken Bellfoundry bosses** (`game/content/data/enemies/bellfoundry.json`, `abilities/bellfoundry.json`)

| Boss | Threats on main | Answering button today | M | D | Two buttons suffice | Gaps |
|---|---|---|---|---|---|---|
| Ommet | Pour Slag 160 cone (cast 40); Temper (kick, heals 2000) | WASD, Q | none | none | yes | **2** |
| Brine Mother (both phases) | Undertow pulls 9000 (10 damage); Boiling Surge 170 circle 4500 (cast 50, after the pull); Drowning Hymn 110 cone below 60% (kick) | WASD (walking covers 9000 mm in the 50-tick cast); Q | none (closest to M2, but walkable and 170 < 250) | none (Surge plus Hymn is 280 only if both land) | yes | **2** |
| Old Sorrow | Clangour 230 cone (cast 40); Resonance 125 plus stun (kick or break); Clapper Peal 90; Still the Bell (lock) | WASD, Q, break bar | none | none (230 < 250) | yes | **2** |

**Encounter gap total: 14 (2 on each of 7).** Every audited encounter is cleared by two buttons.

Out of the brief's scope but under the same rule: the seeded Bellfoundry roster packs (`game/content/data/roster/*.json`)
and the Hollow Vault optional packs. Lane L13 adds the roster rule so the seeded fill (D46) never places a pack set
without an M and a D member.

## 5. Implementation lanes

Order: the brief asked for content/data edits first, then sim. Two dependencies change that order, and the order
below is the dependency order. (a) The mobility buttons need a sim op that does not exist (S3), so the data change for
Blink and Charge follows the sim op. (b) Lethal threats landing before bots press E and F would wipe the bot parties
and turn the existing Playtest survival suites red, which would push the next lane to fit green. So the bots learn the
toolkit before the threats land. Content-only work goes first wherever nothing blocks it (L1 in wave 1, L6 as soon as L1 lands).

File ownership is disjoint inside each wave. One coupling to fix first: `ContentLoader.cs:23` reads ability files from
an explicit list. L1 adds an ordinal directory read for `game/content/data/abilities/threats/` and
`game/content/data/enemies-threats/` (allowed by R-LOADER-1 with byte-identical trace proof). The threat lanes then
add files there and never touch the loader. Both content roots embed under one logical `content/data/` path
(`Keyrun.Content.csproj:35,42`), so every new file name must be unique across the two roots.

Every red test below is written first, shown red on the base, then turned green by the lane (LANE-CONTRACT s2).

### Wave 1 (parallel)

| Lane | Files | Change | Red test it writes first |
|---|---|---|---|
| **L1 TOOLKIT-RULE** (content) | new `Keyrun.Content/Loading/Rules/ToolkitContractRule.cs`; `ContentLoader.cs` (directory read); new `game/content/data/toolkit-gaps/<encounter-or-class>.json`, one per encounter and one per class; `Keyrun.Content.Tests/ToolkitContractRuleTests.cs` | Validator: every class has 5 rotation plus Q/E/R/F; E's effect has damageTakenBp <= -3500; F's effect is a displacement of at least 6000 or movementBp of at least +5000 and clears rooted; every encounter has a cast tagged `threat:mobility` and one tagged `threat:defensive` (or `threat:tankbuster` plus a D1), each with a telegraph. Known gaps live in per-encounter files that may only shrink (one file per encounter, so threat lanes never collide) | A fixture class whose F is `resource delta 0` is rejected. The 7 encounter files and 3 class files on main list exactly the 14 encounter gaps and 6 class gaps in section 4, and deleting any one turns the rule red (L4 and L6 delete the class files, the threat lanes the encounter files) |
| **L2 SIM-DISPLACE** (sim) | new `game/sim/Keyrun.Sim/Combat/Displace.cs`; the op dispatch in `Combat/Casting.Complete.cs` (beside `DamagePctOp`, `:125`) and the op lists in `Combat/Abilities.cs:192` and `Combat/Affinity.cs:36`; op parse in `Keyrun.Content/Json` | `displace` op: the caster moves `distanceMm` along facing, or toward the target point capped at the distance, in one tick. It is clamped by bounds and the walkable layout (R-PATH-1, integer). `freesBinds: true` removes rooted and slowed | Sim test: casting a fixture ability with `displace 8000` moves the caster 8000 mm along +x and removes `rooted`. Red on main: the op is unknown. Also asserts no player-reachable `DashCommand` source (S3 guard) |
| **L3 SIM-ARMOR-SCHOOL** (sim) | `game/sim/Keyrun.Sim/Combat/Damage.cs` (`ApplyArmor`) only | Class armor applies to `physical` only (WoW) | Sim test: a Warden hit by 100 shadow takes 100. Red on main: `Damage.cs:68` calls `ApplyArmor` (`:80`) with no school check, so he takes 65 |

### Wave 2 (after L1; L4 and L5 also after L2)

| Lane | Files | Change | Red test |
|---|---|---|---|
| **L6 CHANTER-TOOLKIT** (content) | `content/data/abilities/chanter.json`; deletes `toolkit-gaps/chanter.json` | Aegis: shape `target`, range 25000, friendly or self (as Cadence targets allies). Glide gains a `removeEffect` of rooted and slowed | Content plus sim test: Aegis cast with an ally targeted puts `aegisShield` on the ally, not the Chanter. Glide used while rooted moves. Both red on main |
| **L4 MOBILITY-BUTTONS** (content) | `content/data/abilities/cinder.json`, `content/data/abilities/warden.json`; deletes `toolkit-gaps/cinder.json` and `toolkit-gaps/warden.json` | Blink: `displace 8000 facing, freesBinds`. Charge: `displace` toward the target capped at 8000, else along facing, then the existing damage and stun | Sim test: a Cinder at the centre of a 6500 mm fixture circle with W = 30 survives by pressing F at tick 1 and dies by walking. Same for the Warden with no target in front |
| **L5 TOOLTIP-TRUTH** (content model) | `Keyrun.Content/Model/AbilityTooltip.cs` | Movement tooltips read the displace op's distance, not range | Tooltip test for Blink shows "8 m blink". On main it shows the range |

### Wave 3 (bots, before any threat)

| Lane | Files | Change | Red test |
|---|---|---|---|
| **L7 BOT-TOOLKIT** (bots) | new `game/bots/Keyrun.Bots/Roles/Toolkit.cs`; one call site per role in `Roles/Tank.cs`, `Roles/Healer.cs`, `Roles/Damage.cs` | Every role presses E when a cast tagged `threat:defensive` or `threat:tankbuster` will resolve on it within E's duration. It presses F when it stands in a `threat:mobility` telegraph whose edge is farther than `speed * ticksLeft`, or when rooted inside one. The healer presses Aegis on an ally whose E is on cooldown when D1 starts | Bots test with fixture threats: a bot that never presses its defensive dies to a fixture D1 at key 2; this bot survives it. A bot in a Blink-only circle survives with F; the same bot with F stripped dies |
| **L8 TWO-BUTTON-SEAT** (playtest) | new `tools/Keyrun.Playtest/TwoButtonPolicy.cs`; `tools/Keyrun.Playtest.Tests/TwoButtonSeatTests.cs` | A policy seat that presses only rot1 and rot2 and walks, with the rest of the party on toolkit bots | Per encounter: "the two-button seat survives <encounter> at key 2 seed 1" is listed in the shrink-only gap files from L1. The test asserts it dies on every encounter not listed. On main every encounter is listed, so it is green on main, and each threat lane deletes its row and makes the death real |

### Wave 4 (threats; parallel by encounter file)

New casts go in new files under `abilities/threats/`. Scripts change only in the encounter's own enemy file.

| Lane | Files | M threat | D threat | Red test |
|---|---|---|---|---|
| **L9 CHOIR** | `content/data/enemies/choir.json`; new `abilities/threats/choir.json`; `toolkit-gaps/choir.json` (delete) | Cantor "Toll Ring": M1 circle 6500 mm on the Cantor, cast 20 + lead 10, 12000 bp, every 400 | Cantor "Last Verse": D1, cast 60, every 400, offset 200 from Toll Ring, not kickable. Killing the Cantor first ends both, which keeps the Choir's kill-order and kick lesson | The two-button seat dies on the Choir at key 2 seed 1. A seat that never presses E dies to Last Verse. A seat that never presses F dies to Toll Ring. The toolkit party clears at key 2 and key 10 |
| **L10 PAIR** | `content/data/enemies/pair.json`; new `abilities/threats/pair.json`; gap file | "Shield Wall Advance": M1 line 14000 x half-width 6500 from one Warrior along its facing, cast 20 + lead 10 | "Warcry": D1, cast 60, every 400 | As L9, on the Pair |
| **L11 SEED** | `content/data/enemies/seed.json`; new `abilities/threats/seed.json`; gap file | "Grasping Roots" then "Bloom Burst": M2, root 50 ticks within 5000, then a 2500 mm strike on each rooted spot at 30 ticks | "Pollen Burst": D1 every 400; plus "Thorn Lash": D2 on the holder | As L9; plus the tank without Brace or Vigil dies to Thorn Lash |
| **L12 HOLLOW-WARDEN** | `content/data/abilities/enemies.json` (slam and cleave entries only); `content/data/enemies/hollow-warden.json`; gap file | Slam retuned to M1: radius 6500, cast 20 + lead 10, 12000 bp | Cleave retuned to D2 on the holder (raw 700) while staying a frontal for others; the Lock's shield-up adds a D1 pulse "Hollow Toll". Stays three mechanics (BIBLE s9.6) and R-BOSS-1's 90-180 s with each answered twice | As L9; plus R-BOSS-1's length test stays green |
| **L13 BELLFOUNDRY** | `game/content/data/enemies/bellfoundry.json`; `game/content/data/abilities/bellfoundry.json`; new roster rule in `ToolkitContractRule.cs` (a follow-up to L1, run after it); 3 gap files | Ommet "Slag Flood" M1 (new); Brine Mother: Undertow applies Drenched and Surge drops to cast 20, so it becomes M2-shaped (walk 2520 < 4500, Glide and Blink clear); Old Sorrow: Peal retuned to M1 radius 6500 | Ommet: Pour Slag becomes D2 plus frontal, and "Bellows Blast" D1 (new). Brine Mother: "Brine Crush" D1, running in both phases. Old Sorrow: Clangour becomes D2 plus frontal; Still the Bell pulses "Death Knell" D1 at shield-up | As L9 for each boss; R-BOSS-1 length per boss; the roster rule rejects a fixture pack set with no M member |

### Wave 5

| Lane | Files | Change | Red test |
|---|---|---|---|
| **L14 TUNE-AT-KEY** (playtest measure) | `tools/Keyrun.Playtest.Tests/ToolkitKeyCurveTests.cs` | Measures the toolkit party at keys 2, 5, 10 and 15 on both dungeons. Tempo shrinks the lead 1.5% per key, so M1 at W = 30 drops under Glide's reach near key 10. The test pins the key at which Glide stops sufficing, so that is a recorded design fact and not an accident | Asserts Glide clears M1 at key 2 and records the first key where it fails. The value is set by measurement, never fitted |

Parked, not cut now: rogue Grapple and Smoke Bomb (needs the class, the holder-drop rule and `displace` to a point),
and the melee category.

## 6. What the guided playtester guide must say, one line per toolkit button

For the `design/PLAYTESTER-GUIDE.md` lane to absorb verbatim or reword. Each line says when to press, not what the
button is.

- **Warden Q Shield Bash**: kick the cast with an open bar (no shield edge); healers first, then the suggested kick number.
- **Warden E Brace**: press when a tank buster's bar is on you, inside its last 60 ticks; Vigil covers the next one if Brace is down.
- **Warden R Taunt**: take any enemy that is hitting someone who is not you; never taunt one that is hurting nobody.
- **Warden F Charge**: closes on a loose enemy; with nothing ahead it is your way out of a ring you cannot walk out of.
- **Cinder Q Quench**: kick the open-bar cast; a landed kick refunds 20 Heat.
- **Cinder E Ashen Skin**: press when an unavoidable spike's room ring appears; it is back in time for the next one if you did not spend it on trash.
- **Cinder R Purge**: strip an enrage (Stoked, Boiling Blood) from an enemy or a magic burn from an ally.
- **Cinder F Blink**: an instant 8 m jump along your facing that also breaks roots; face out of the circle first.
- **Chanter Q Rebuke**: kick the open-bar cast; a Ward Dissonance silence counts as a stop (R-KICK-WARD-1).
- **Chanter E Aegis**: on yourself for a spike, or on the ally who has no defensive left; you cannot do both.
- **Chanter R Cleanse**: remove the magic burn or root from the ally who will die of it first.
- **Chanter F Glide**: sprint out of a ring and shake off a root or slow; it is shorter than a Blink, so go early.
- **G Ping (all)**: mark the caster you will kick, or the spot to stand on.

## 7. Calls Matt might overrule

1. **Armor is physical-only** (WoW). Unavoidable magic spikes then threaten the Warden too, which is what makes his E
   matter outside tank busters. This amends BIBLE s3.1's "every hit".
2. **The tutorial is lethal too.** The Hollow Vault's teaching pulls carry their M and D threats at key 2 with the
   same numbers. The tutorial teaches by the first death, as Matt's "you should die" asks, with no softer variant.
3. **The healer's external is the E button**, Aegis "self or one ally", instead of a new key or an R change. The
   alternative, moving Cleanse off R, would lose the healer's dispel.
4. **Every F frees from roots and slows, and Warden's Charge works with no target.** This makes M2 (bind and strike)
   answerable by every class. It follows Blink's tooltip in WoW, but it is more generous than WoW's Charge.
5. **Bosses gain or retune mechanics to carry M and D.** The Hollow Warden stays at three mechanics: Slam and Cleave
   are retuned, and the Lock adds a pulse. Ommet gains Slag Flood and Bellows Blast, and the Brine Mother gains Brine
   Crush. This deliberately stretches BIBLE s9.6's "three mechanics, sequenced" for the non-tutorial bosses.
