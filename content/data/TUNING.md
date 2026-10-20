# content/data/TUNING.md

Usage: every number below is placeholder tuning, not a balance claim. `// TUNE` cannot live
inline in JSON, so this file is the single marker: **every number in every file under
`content/data/` is `// TUNE`** unless it is a structural id/count (tick constants, tag strings,
window ids). Ticks are 20 Hz (50 ms); GCD is fixed at 30 ticks (1.5 s) for every class per
BIBLE.md D4. Player health 2000, Choir trash health 6000, Pair 1200, Seed 3000, boss health 5000
(Hollow Warden; tuned 2026-10-18 per Matt, was 60000; see the Dungeon section).

## Abilities (all `// TUNE`)

| id | castTicks | gcdTicks | cooldownTicks | charges | cost | damage/heal |
|---|---|---|---|---|---|---|
| warden.strike | 0 | 30 | 0 | 1 | - | dmg:80 |
| warden.guardBreak | 0 | 30 | 0 | 1 | - | dmg:100 |
| warden.bulwark | 20 | 30 | 200 | 1 | - | - |
| warden.reckoning | 0 | 30 | 0 | 1 | momentum 100 | dmg:400 |
| warden.vigil | 0 | 30 | 120 | 1 | momentum 50 | - |
| warden.shieldBash | 0 | 0 | 480 | 1 | - | - |
| warden.brace | 0 | 0 | 300 | 1 | - | - |
| warden.taunt | 0 | 0 | 160 | 1 | - | - |
| warden.charge | 0 | 0 | 200 | 2 | - | dmg:80 |
| cinder.emberBolt | 20 | 30 | 0 | 1 | - | dmg:90 |
| cinder.flare | 30 | 30 | 0 | 1 | - | dmg:110 |
| cinder.coldSnap | 0 | 30 | 120 | 1 | - | dmg:100 |
| cinder.detonate | 0 | 30 | 60 | 1 | heat 30 | dmg:250 |
| cinder.cinderfield | 20 | 30 | 200 | 1 | - | - |
| cinder.quench | 0 | 0 | 480 | 1 | - | - |
| cinder.ashenSkin | 0 | 0 | 300 | 1 | - | - |
| cinder.purge | 0 | 0 | 160 | 1 | - | - |
| cinder.blink | 0 | 0 | 200 | 2 | - | - |
| chanter.cadence | 20 | 30 | 0 | 1 | - | - (variants: +hot/+shield) |
| chanter.refrain | 0 | 30 | 40 | 1 | - | - (variants: heal 150/shield) |
| chanter.chorus | 0 | 30 | 120 | 1 | - | - (variants: heal 120/wardPulse) |
| chanter.dissonance | 0 | 30 | 60 | 1 | - | dmg:130 |
| chanter.hymn | 20 | 30 | 200 | 1 | - | - |
| chanter.rebuke | 0 | 0 | 480 | 1 | - | - |
| chanter.aegis | 0 | 0 | 300 | 1 | - | - |
| chanter.cleanse | 0 | 0 | 160 | 1 | - | - |
| chanter.glide | 0 | 0 | 200 | 2 | - | - |
| choir.meleeSwing | 0 | 0 | 40 | 1 | - | dmg:70 |
| choir.healCast | 80 | 0 | 200 | 1 | - | heal:300 |
| pair.meleeSwing | 0 | 0 | 40 | 1 | - | dmg:70 |
| pair.spreadMark / stackCall | 40 | 0 | 400 | 1 | - | mark only |
| seed.slam | 20 | 0 | 100 | 1 | - | dmg:150 |
| seed.lash | 20 | 0 | 200 (script `at: 50, every: 200`) | 1 | - | dmg:70 |
| seed.deathBloom | 0 | 0 | 0 | 1 | - | window only |
| hollowWarden.slam | 0 | 0 | 60 | 1 | - | dmg:150 |
| hollowWarden.cleave | 40 | 0 | 160 | 1 | - | dmg:220 |
| hollowWarden.lock | 0 | 0 | 0 (script `every: 600`) | 1 | - | shield + window |
| twins.slam | 0 | 0 | 60 | 1 | - | dmg:90 |

Resources (`// TUNE`): momentum max 100, builder gains 15, finisher cost 100; heat max 100,
filler gain 20-25, dump -100, detonate cost 30; verse is a 0/1 stance flag, not a spent pool.

## Window / effect durations (ticks, `// TUNE`)

Setup windows: sundered/kindled/attuned 60 (3 s, per BIBLE.md Sec 4). Floor windows: bulwark 160,
cinderfield 160, hymn 160 (8 s). Boss `seedbloom` 100, `lock` 60 (repeats every 600 = 30 s per
BIBLE.md Sec 9). Control effects: stunned 20 (Charge)/40 (Cold Snap deep freeze)/100 (Break bar
empty), silenced 40, blinded 80, chill 60. Shields/mitigation are `damageTakenBp`: braced -1500,
bracedSelf -3000, braceSelf -4000, ashenSkin -3500, aegisShield -3500, wardShield -3000, wardPulse
-2000, lockShield -10000 (effectively immune for 3 s). Break bar (Sundered Twins): max 100,
drains 34/interrupt, 20/control (~3 interrupts to break).

### Floor Window catch radii (mm, `// TUNE`, window-radius-field backlog item)

`sim/combat/windows.ts`'s `FLOOR_WINDOW_RADIUS_MM` (3000mm) is only the fallback default now; each
floor Window ability sets its own `radiusMm` on the `window` op per BIBLE.md Sec 4/5 ("Bulwark,
Cinderfield and Hymn... different sizes"). Bible does not name exact numbers (`[DECIDE]`), so these
are picked here, smaller than the default to keep the field-combo timing a real positional check
rather than "anywhere near the patch": warden.bulwark (defensive zone, forgiving so allies can
actually stand in it) 2500; cinder.cinderfield (punishing fire patch, tightest -- the highest-skill
floor combo) 1800; chanter.hymn (support zone, medium) 2200. Boss `seed.deathBloom`'s `seedbloom`
floor Window (BIBLE.md Sec 9.3) is out of this backlog item's scope and keeps the 3000mm default.

## Combos (`// TUNE`)

Shatter dmg 180 + stun 20t; Consecrate dmg 150 + 120t heal-pulse (40/tick@20t); Forge dmg 180 +
40 momentum; Steam dmg 150 + 80t blind; Anthem +15% haste 120t; Flashfire dmg 150 + 30 heat +
clear chill. Field combos (any-other-class payoff): bulwark-field braced 80t, cinderfield-field
dmg 80, hymn-field heal 60.

## Dungeon

`parTicks` 14400 (12 min at 20 Hz, per BIBLE.md Sec 1). Choir trash 6000 hp; Pair 1200 hp; Seed 3000 hp;
Hollow Warden boss 5000 hp; Sundered Twins 1000 hp each (tuned 2026-10-18 per Matt, STATE.md D14: was Pair/Seed
6000, boss 60000, Twins 6000, so that the bot-party autorun reaches and clears the boss).
Enemy script cadences (tuned 2026-10-18 per Matt): `pair.meleeSwing` at 60 every 150 (was loop every 40);
`hollowWarden.slam` loop every 300 (was 60); `twins.slam` loop every 400 (was 60); `hollowWarden.cleave` stays
loop every 160. The ability cooldown columns above are unchanged.

Boss and elite slams (values as the data files hold them; `abilities/enemies.json` plus the script telegraphs in
`enemies/*.json`; ticks at 20 Hz):
- `seed.slam` (The Seed, elite, 3000 hp): circle shape radius 2000, `range` 0, damage 150 physical, castTicks 20,
  cooldownTicks 100; script `loop every 100` with telegraph circle radius 2000, leadTicks 20.
- `hollowWarden.slam` (The Hollow Warden, boss, 5000 hp): target shape, `range` 3000, damage 150 physical, castTicks 10,
  cooldownTicks 60; script `loop every 300` (see cadences above) with telegraph circle radius 3000, leadTicks 10,
  `spansCast: true`.
- `twins.slam` (Sundered Twin A, elite, 1000 hp; Twin B shares the ability): target shape, `range` 3000, damage 90
  physical, castTicks 10, cooldownTicks 60; script `loop every 400` (see cadences above) with telegraph circle
  radius 3000, leadTicks 10, `spansCast: true` (both Twins' scripts).

`spansCast` (SLAM-TELEGRAPH, C# sim only; the frozen TS ignores it and oracle replays clear it): the telegraph resolves
on the tick the cast lands (fire tick + castTicks) instead of the tick it starts, so with leadTicks 10 = castTicks 10
the circle appears with the cast bar and resolves with the hit. Without it the circle resolved 10 ticks before the damage.

## Enemy movement (`// TUNE`)

[DECIDE Matt] `choir.melee` and `pair.melee` `speedMmPerTick` 207 = provisional 1.15x the 180 mm/tick
player base, so a player fleeing at base speed is caught (tests/sim/integration/chase-speed.test.ts).
Every other enemy omits it and walks at base. `chaseMarginMm` is unset everywhere (default 500,
sim/encounter/chase.ts's CHASE_MARGIN_MM).
An enemy chases only when its script casts a hostile target-shaped ability (chase.ts engage range). Since
KF3-SEED-ENGAGE the Seed's script carries `seed.lash` (target, range 1500, inside the 2000 slam circle), so it
walks at base to 1000 mm of its target instead of slamming from its spawn while a party parked at the
seed-chamber doorway kills it for free.
