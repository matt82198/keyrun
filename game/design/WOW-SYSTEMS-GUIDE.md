# WoW systems guide for the keyrun dungeon

Purpose: give every lane that codes inside the dungeon the shape World of Warcraft's server uses for the same
system, the keyrun file that holds it today, and a verdict. Matt 2026-10-22: "maybe a wow engineer to guide the
coding, their architecture is spaghetti but at least they have the knowledge". Rule of use, from design/BIBLE.md
section 9: inside the dungeon WoW Mythic+ decides, unless the bible speaks. The bible speaks on: five buttons per
class plus the fixed Q/E/R/F/G slots (section 2), synergy Windows between two players (section 4), no shareable
score (sections 0 and 10), backfill instead of abandonment (section 10), every dangerous cast has a cast bar and a
floor telegraph (section 3), three teaching pulls and a boss that each teach one thing (section 9.6), and no world,
no quests, no shop (section 1). Everything else follows WoW.

WoW claims below name the system and its data table or script concept as the server and the well-known open
reimplementations (TrinityCore, AzerothCore) lay it out. They are shapes, not code. Keyrun claims cite
`file:line` on origin/main 3bb6eed5. Paths are relative to the repo root. `sim/` means
`game/sim/Keyrun.Sim/`, `model/` means `game/content/Keyrun.Content/Model/`, `gdata/` means `game/content/data/`,
`cdata/` means `content/data/`. The sim ticks at 20 Hz (`sim/Core/Tick.cs:28`), so 20 ticks = 1 s.

Verdicts: **has** (the WoW shape is there), **partial** (the shape is there with a named gap), **lacks** (absent).

## Verdict table

| # | System | WoW shape | Keyrun status | file:line |
|---|---|---|---|---|
| 1.1 | Spell record | One spell row: cast time, cooldown, charges, GCD, range, target type, interrupt flag | has | `sim/Combat/Types.cs:98-138` |
| 1.2 | Spell effects | Up to N effect rows per spell (damage, heal, apply aura, interrupt, pull, threat) | has | `sim/Combat/Types.cs:44-95` |
| 1.3 | Auras | Aura row: max stacks, dispel type, periodic tick, stat modifiers | has | `sim/Combat/Types.cs:147-165` |
| 1.4 | Cooldown and GCD category | Spells share a category cooldown; a kick locks a spell school | partial: per-spell GCD and school lockout, no shared category | `sim/Combat/Cooldowns.cs:23`, `sim/Combat/Casting.cs:489-516` |
| 1.5 | Mechanic flags and immunity | Each effect carries a mechanic (stun, root, silence); creatures carry an immunity mask | partial: stun immunity by class id list only | `sim/Run/ControlStops.cs:18-20`, `:122` |
| 1.6 | Interrupt flags | Flags for what breaks a cast (kick, movement, damage) | partial: one `Interruptible` bool | `sim/Combat/Types.cs:125` |
| 1.7 | Telegraph from spell target type | The ground marker is drawn from the spell's target type and radius | lacks: telegraph authored again on the script step | `sim/Encounter/EncounterTypes.cs:38-50`, `:85`; `gdata/enemies/vault.json:57-58` vs `gdata/abilities/vault.json:89-91` |
| 1.8 | Spell scripts for exceptions | Named script bound to a spell id only when data cannot express it | partial: exceptions are hand-wired sim modules | `sim/Encounter/Boss.cs:150`, `sim/Run/ControlStops.cs:28` |
| 2.1 | Creature template | Template row: level, health and damage modifiers, armor, faction, flags, AI name, script name | partial: id, health, role, abilities, script, speed; no damage modifier, armor, faction, script name | `model/Encounter.cs:166-197` |
| 2.2 | Spawns and spawn groups | Spawn rows point at a template; groups and formations bind a pack | partial: two concepts (dungeon pulls, layout packs) | `sim/Encounter/EncounterTypes.cs:143-168`, `model/Layout.cs:147-175`, `sim/Encounter/LayoutPacks.cs:17` |
| 2.3 | Respawn | Mythic+ never respawns cleared trash | has | `design/BIBLE.md:403` |
| 2.4 | Forces value per creature | Each creature counts a set amount toward the enemy-forces criteria | partial: separate table keyed by enemy id; the vault creatures have no entry | `gdata/forces.json:3-9`, `sim/Encounter/Forces.cs:54-67` |
| 3.1 | Threat table | Damage, healing and taunts add threat; highest threat is the target | partial: table exists; damage adds no threat | `sim/Combat/Threat.cs:29-81`, `sim/Combat/Casting.cs:468` |
| 3.2 | Target selection | Top threat, with taunt and scripted overrides (random, farthest) | has | `sim/Encounter/ScriptTarget.cs:12-21`, `sim/Combat/Holder.cs:35-50` |
| 3.3 | Aggro radius | Base radius adjusted by level difference | has the bible shape (flat radius per group; keyrun has no levels) | `sim/Encounter/Pulls.cs:16`, `sim/Encounter/Aggro.cs:140` |
| 3.4 | Assist and link | A pack engages together; neighbours within an assist radius join | partial: whole-group engage and explicit links, no assist radius | `sim/Encounter/Aggro.cs:140-163` |
| 3.5 | Leash and evade | Too far from home or no reachable target: evade, reset health and threat | lacks | none (no leash or evade symbol in `sim/`) |
| 3.6 | Movement generators | Idle, chase, follow, waypoint patrol, random, flee | partial: idle and chase only; the patrol tick is unused | `sim/Encounter/Chase.cs:51-90`, `sim/Encounter/Aggro.cs:86` |
| 3.7 | Data AI rows | SmartAI rows: event, condition, action, target | partial: triggers are loop, at-tick, on-death; no conditions; the only action is cast | `sim/Encounter/EncounterTypes.cs:8-18`, `:76-107` |
| 4.1 | Navmesh pathing | Navmesh path, then a spline of waypoints sent to the client | partial: walkable boxes plus one route point per box; enemies chase in a straight line | `sim/Combat/Movement.cs:319-355`, `sim/Encounter/Pulls.cs:229`, `sim/Encounter/Chase.cs:85-87` |
| 4.2 | Stop distance and repath | Chase stops at melee reach; repaths when the target moves | has | `sim/Encounter/Chase.cs:18`, `:74` |
| 5.1 | Boss script | Boss AI with phases, an event map of timers, health-percent triggers | partial: flat loops plus the hard-wired Lock at 50% | `cdata/enemies/hollow-warden.json:7-14`, `sim/Encounter/Boss.cs:26-56` |
| 5.2 | Enrage | Soft or hard enrage on a timer or a failed mechanic | has (Tempo stacks per failed Lock) | `sim/Encounter/Boss.cs:39-42`, `:276-290` |
| 5.3 | Encounter Journal | Journal sections list each mechanic and the spell behind it | partial: name list, not bound to spells | `cdata/dungeons/hollow-vault.json:28` |
| 5.4 | Boss duration tuning | Boss health set from expected group DPS so a low key lasts 90-180 s | lacks: boss health 5000 is below one Choir trash mob (6000); a measured bot boss pull lasted 601 ticks (30 s) | `cdata/enemies/hollow-warden.json:5`, `cdata/enemies/choir.json:5`, `tools/Keyrun.Playtest.Tests/MitigateThroughLedgerKickTests.cs:31` |
| 5.5 | Adds die with the boss | Boss death despawns its adds | has | `sim/Encounter/Boss.cs:146`, `:491` |
| 5.6 | Instance script | One script owns boss state, pull order, doors and the challenge state | partial: spread across the run root; the door is not applied | `sim/Run/Run.cs:346-381`, `sim/Encounter/Pulls.cs:206` |
| 6.1 | Keystone scaling | Key level scales enemy health and damage | has (Tempo plus affixes) | `sim/Encounter/Tempo.cs:42-61`, `sim/Encounter/Affixes.cs:77-92` |
| 6.2 | Timer and chest tiers | Time limit per map; +2 at 80%, +3 at 60% | has | `sim/Encounter/Timer.cs:121-133`, `:178-209`; `cdata/dungeons/hollow-vault.json:4` |
| 6.3 | Death penalty | Each death removes time from the timer | has | `sim/Encounter/Timer.cs:35-38` |
| 6.4 | Completion rule | Done when every boss is dead and forces are at 100% | partial: done on the last pull; forces are not checked | `sim/Run/RunWiring.cs:368-371`, `sim/Run/Run.cs:376-377` |
| 6.5 | Weekly affixes | Affixes rotate by week and turn on by key level | has | `sim/Encounter/Affixes.cs:54-65`, `sim/Run/Clause.cs:47` |
| 6.6 | Key result | Timed: upgrade by tier; untimed: WoW drops one level | has (bible divergence: untimed keeps its level) | `sim/Encounter/Timer.cs:196-209` |
| 7.1 | Group finder | Role-checked group built by players | replaced by the bible: queue matches on key only | `sim/Run/Backfill.cs:71` |
| 7.2 | Leaver | A leaver breaks the key | replaced by the bible: backfill | `sim/Run/Backfill.cs:93-134` |
| 7.3 | Release and run back | Release to the graveyard, run back, timer keeps going | has | `sim/Run/Release.cs:24-54`, `sim/Run/RunWiring.cs:29` |
| 7.4 | Loot | End chest plus a weekly vault | replaced by the bible: choose one of three every run | `sim/Encounter/Rewards.cs:24-30` |
| 8.1 | Seeded dungeon build | (keyrun's own ask) | lacks: the layout is hand-written; the PRNG exists | `gdata/layout.json`, `sim/Core/Prng.cs:28` |

## 1. Spells as data

**WoW does this.** A spell is a row in the spell tables. The main row joins: cast time (SpellCastTimes), cooldown,
category cooldown and the GCD category (SpellCooldowns, SpellCategories), charges (SpellCategory charge data),
range (SpellRange), power cost, an interrupt-flags field, aura-interrupt flags, and up to a fixed number of effect
rows (SpellEffect). Each effect row has an effect type (school damage, heal, apply aura, interrupt cast, knock or
pull), base points, a radius index, an implicit target type (unit target, cone in front of the caster, area at a
destination, area around the caster), a mechanic (stun, root, silence, snare) and, for an aura, the aura type
and period. Creature immunity checks the effect's mechanic against the creature's immunity mask. Spell scripts
(the `spell_script_names` binding) exist only for what the rows cannot say. A telegraph is the client drawing
the spell's own target type and radius; the server does not author a second shape for it.

**Keyrun has this.** `AbilityDef` (`sim/Combat/Types.cs:98-138`) holds cast ticks (`:104`), GCD ticks (`:107`),
cooldown (`:110`), charges (`:113`), cost (`:116`), range (`:119`), shape (`:122`), interruptible (`:125`),
effect ops (`:128`) and tags (`:131`). Shape kinds are target, self, cone, circle and line (`:12-29`). That is
the WoW implicit target set cut to five. Effect ops (`:44-95`) are the effect types. `EffectDef` (`:165`) is the
aura row: max stacks, a dispel tag, per-stack modifiers, periodic tick. Content is plain JSON
(`gdata/abilities/vault.json`). Start checks run in WoW's order: already casting, silenced, not ready, resource,
school locked, target and range (`sim/Combat/Casting.cs:69-87`). A kick puts a school lockout on the target
(`:489-516`).

**Gaps, in order of cost.**
1. *Telegraph drift (lacks).* A script step carries its own `TelegraphSpec` (`sim/Encounter/EncounterTypes.cs:38-50`,
   `:85`). The two shapes can disagree. The Drowned Chorister's Undertow telegraph is a 25000 mm cone
   (`gdata/enemies/vault.json:57-58`), but the ability resolves on one target (`gdata/abilities/vault.json:89-91`).
   The floor shows a cone that the cast never uses. The WoW shape is to derive the telegraph from `AbilityDef.Shape`
   plus a lead and a style, and to author a separate marker only for a target-type cast that marks the target's
   spot (Searing Hex's circle under the target).
2. *Mechanics (partial).* Stun immunity is a class-id list (`sim/Run/ControlStops.cs:18-20`, `:122`). Put a
   `mechanic` on the effect op (stun, root, silence, pull, snare) and an `immune` list on the creature template,
   and one check covers boss immunity, Undertow's root and every future CC.
3. *Category cooldowns (partial).* There is per-ability GCD (`sim/Combat/Cooldowns.cs:23`) and a school lockout. WoW's
   shared category (two spells on one cooldown) has no field. Add it only when a kit needs it. The five-button
   rule makes it rare.
4. *Interrupt flags (partial).* One bool (`sim/Combat/Types.cs:125`). WoW splits "kickable" from "breaks on move".
   Keyrun casts are stand-still or not by class rules. Keep the bool until a cast needs a second flag.
5. *Scripts (partial).* Exceptions live as named sim modules (`sim/Encounter/Boss.cs:150` BossRules,
   `sim/Run/ControlStops.cs:28`). That is fine while there is one boss. A second boss needs a registry keyed by
   ability or creature id, so a new exception is one file plus one binding, never an edit to the run root.

## 2. Creatures as templates

**WoW does this.** `creature_template` is the creature class: level range, health and damage modifiers on a
per-level base table, armor, faction, unit flags, movement speed, an `AIName` (SmartAI or a C++ AI) and a
`ScriptName`. `creature` rows are spawns: template id, position, facing, map, spawn group. Spawn groups,
formations and linked respawns bind a pack. Mythic+ never respawns cleared trash. The challenge-mode criteria
give each creature a forces value; bosses, summoned adds and most minor creatures count for nothing.

**Keyrun has this.** `EnemyDef` (`model/Encounter.cs:166-197`) is the template: id, name, max health, role, ability
ids, script, speed, chase margin, break bar. Roles are melee, caster, lieutenant, elite and boss (`:28-46`).
Spawns come in two shapes. Teaching pulls are `PullDef` (`sim/Encounter/EncounterTypes.cs:155-168`) with
`PullSpawn` rows (`:143`), and their content lives in `cdata/dungeons/hollow-vault.json`. Optional packs are
`LayoutPack` (`model/Layout.cs:147-175`), sim `PackGroup` (`sim/Encounter/LayoutPacks.cs:17`), content in
`gdata/layout.json:57-75`. Cleared groups stay dead (`design/BIBLE.md:403`).

**Gaps.**
1. *Forces are not on the template (partial).* `gdata/forces.json:3-9` is a separate table keyed by enemy id. A
   missing id silently counts 0 (`sim/Encounter/Forces.cs:61`). The vault creatures (Hound, Hexer, Brute, Keeper,
   Chorister; `gdata/enemies/vault.json`) have no rows, so the BIBLE 9.2 table cannot be met yet. Either move
   `forces` onto `EnemyDef`, or make the validator fail an enemy with no forces row and no explicit 0.
2. *No damage modifier or armor on the template (partial).* Damage is per ability and scaled by Tempo and affixes.
   WoW tunes a whole creature with one damage modifier. Add `damageBp` and `healthBp` on the template so tuning is
   one number per creature, not one edit per ability.
3. *Two spawn concepts (partial).* Pulls and packs share the aggro rules (`sim/Encounter/Aggro.cs:83-101`) but differ
   in storage and ids. WoW has one spawn row and a group. The seeded build (section 8) needs one: a spawn group
   with a role (teaching, pack, boss), a slot list and links.

## 3. Creature AI

**WoW does this.** The threat manager holds one table per creature. Damage, healing (split across enemies) and
taunts add threat. The target is the top of the table; a new target must pass a margin to pull aggro (110% in
melee, 130% at range). Taunts override it. Aggro radius is about 20 yards, changed by about one yard per level of
difference, with a floor and a cap. A creature that aggros calls for help. Its formation or link joins it, and
nearby same-faction creatures can assist. A creature that goes too far from home, or has no reachable target,
evades: it walks home and resets health and threat. Motion is a stack of movement generators: idle, random,
waypoint patrol (`waypoint_data`), chase, follow, flee and home. SmartAI (`smart_scripts`) makes a mob data: each
row is an event (combat start, update in combat with a timer range, health below a percent, spell hit, death),
a condition from the conditions table, an action (cast, summon, set phase, flee, talk) and a target type.

**Keyrun has this.** The threat table (`sim/Combat/Threat.cs:50-81`) and the highest-source pick (`:63`). Target
selection (`sim/Encounter/ScriptTarget.cs:12-21`): heals go to the lowest-health ally, everything else to top
threat then nearest, with self, threat and random overrides. A taunt holder lock (`sim/Combat/Holder.cs:35-50`).
Prespawned groups idle until a living player enters the ring or a member takes damage, then engage whole, and
links chain transitively (`sim/Encounter/Aggro.cs:115-165`). The default ring is 4000 mm (`sim/Encounter/Pulls.cs:16`).
Chase (`sim/Encounter/Chase.cs:51-90`) closes to the shortest reach among the script's casts, minus a margin.
Scripts are data: loop, at-tick and on-death triggers (`sim/Encounter/EncounterTypes.cs:8-18`), with a cast,
an optional telegraph and a target override per step (`:76-107`).

**Gaps.**
1. *Damage does not make threat (partial).* Threat comes only from Momentum spent (`sim/Combat/Threat.cs:29`) and
   explicit threat ops (`sim/Combat/Casting.cs:468`). A DPS that out-damages the tank never pulls aggro, and a mob
   with an empty table goes to the nearest player. WoW: damage times a class modifier, healing split, tank
   multiplier, and a pull-over margin. This is what makes "the tank holds the pack" a skill.
2. *No leash or evade (lacks).* Nothing in `sim/` resets a mob. M+ needs a cheap version: past N mm from home, or
   with no living player in the room, return and reset. Without it a kited mob follows across the map.
3. *No patrol (partial).* `Aggro.GroupsOf` takes a tick for the patrol ring and discards it (`sim/Encounter/Aggro.cs:86`).
   BIBLE 9.1 names the W1 Kennel patrol. The WoW shape is a waypoint list on the spawn group, walked at a fixed pace
   (deterministic by tick), with the ring centre following the leader.
4. *No assist radius (partial).* Only explicit links (`sim/Encounter/Aggro.cs:145-163`). That is the bible's "No
   POC pack is linked by default" rule, and it keeps chain pulls visible. Keep it. Do not add an implicit assist.
5. *Events are time-only (partial).* No health-percent, in-combat-timer-range or target-in-range events, and no
   conditions. The Lock's 50% gate is therefore C# (`sim/Encounter/Boss.cs:26-27`). Add `onHealthBelowBp` and
   `phase` on `ScriptStep` and the next boss is data.

## 4. Pathing

**WoW does this.** The server builds navmeshes offline from map geometry (mmaps, Recast and Detour). A path query
returns a polygon corridor and a smoothed point list. The server sends the client a spline: start point, waypoints,
speed and flags. Both sides move the unit along it on a clock, so the server sends only path changes. Chase
repaths when the target leaves a tolerance around the old end point. It stops at combat reach. Line of sight is a
separate query against the map's geometry.

**Keyrun has this.** Walkable space is a list of axis-aligned boxes that overlap at doorways. A move outside every
open box is clamped back (`sim/Combat/Movement.cs:319-355`). A layout has one route point per box, the doorway to the
next one (`sim/Encounter/Pulls.cs:229`). The bots' layout adapter builds that route from content
(`game/bots/Keyrun.Bots/shared/Follow.cs:21`). Enemies
chase straight at their target (`sim/Encounter/Chase.cs:85-87`), stop inside reach (`:18`, `:74`) and re-aim every tick.

**What the integer sim should copy.**
- *A graph, not a mesh.* Rooms and corridors are already a graph. Make it explicit: nodes are boxes, edges are
  doorway points. A path is a breadth-first walk over boxes (a few dozen nodes, integer, deterministic), then the
  doorway points in order. That replaces straight-line chase across rooms, which today ends on a box wall.
- *Smoothing.* Inside one box the straight line is legal (a box is convex). Skip a doorway point when the next
  box is reachable in a straight line. That gives the WoW feel without a funnel algorithm.
- *Stop distance.* Keep `ChaseMarginMm`. WoW stops at combat reach (both bounding radii plus a constant). Derive
  it from both actors' radius and the ability's range, not a flat 500 mm.
- *Repath cadence.* Recompute the box path only when the target changes box, or every 10 ticks (0.5 s). Inside
  the same box, re-aim every tick, as today.
- *Defer.* Line of sight, obstacles inside a room, dynamic doors and jumping. WoW handles these in scripts and
  geometry. Keyrun rooms are empty boxes until a design needs pillars.

## 5. Encounters

**WoW does this.** A boss is a boss AI script with an event map: events scheduled by id at a time (often a range),
with phase masks, rescheduled after each cast. Phases change on health percent or on events. Each boss has a
berserk timer, or a soft enrage through stacking buffs. The Encounter Journal lists each mechanic with its spell.
The instance script owns encounter state (not started, in progress, done), doors that close on pull and open on
the kill, objects, and the challenge-mode state. Boss health is tuned against expected group DPS at that
difficulty, so a fight lasts the length the designer wants. For dungeon bosses at low keys that is 90 to 180 s.
Adds a boss summoned despawn when it dies.

**Keyrun has this.** The Hollow Warden's script is three flat loops: slam every 300, cleave every 160, lock every 600
(`cdata/enemies/hollow-warden.json:7-14`). `BossConfig` (`sim/Encounter/Boss.cs:18-56`) replaces the flat lock
loop with the 50% gate (`:26-27`), a 60-tick window (`:30`), a 600-tick repeat (`:33`), and enrage after three
fails (`:39`) as a Tempo spike (`:42`). Each fail adds one Tempo stack (`:276-290`). The Twins despawn on boss
death (`:146`, `:491`). The dungeon's boss entry lists mechanics by name (`cdata/dungeons/hollow-vault.json:28`).

**Gaps.**
1. *Boss length (lacks).* Boss max health is 5000 (`cdata/enemies/hollow-warden.json:5`). One Choir trash mob has 6000
   (`cdata/enemies/choir.json:5`). A recorded bot measurement puts the boss pull at ticks 8162-8763, which is 601 ticks
   or 30 s (`tools/Keyrun.Playtest.Tests/MitigateThroughLedgerKickTests.cs:31`). That is a third of WoW's low end.
   The boss's three mechanics, sequenced, need the room: Lock at 50% repeating every 30 s gives at most one Lock in
   a 30 s fight. Tune it the WoW way. Measure the bot party's sustained DPS on the boss (damage per tick over the pull)
   at key 2, then set health = target seconds x 20 x DPS for a 120 s fight. Tyrannical then adds its 25% on top
   (`sim/Encounter/Affixes.cs:77-78`).
2. *No event map or phases (partial).* Loops fire on fixed multiples from engage. WoW schedules the next cast when
   the last one lands, and phases gate which events run. Mechanics are "sequenced, never layered" (BIBLE 9.6), so
   phases are the natural fit: phase 1 cleave, phase 2 Twins, phase 3 the Lock. Today all three run at once,
   with the Lock gated only by health.
3. *Journal not bound (partial).* The mechanic names are strings. Bind each to the ability ids it uses, so a test
   can assert every listed mechanic is telegraphed (BIBLE section 3) and the screen can show it.
4. *Instance script (partial).* Pull order, room entry, aggro and the door live in the run root
   (`sim/Run/Run.cs:346-381`). The box gate field exists (`sim/Encounter/Pulls.cs:206`), but no box is gated, and
   the Hollow Door is not applied (`sim/Run/Run.cs:376-377`). One `InstanceState` record (encounter states, door
   states, forces, timer) owned by one step function is the WoW shape. It also gives the HUD one read.

## 6. Mythic+

**WoW does this.** A keystone names a dungeon and a level. Activating it starts the challenge: a countdown, then the
timer for that map. The level scales enemy health and damage by a per-level multiplier. Affixes switch on at set
levels and rotate weekly (Fortified and Tyrannical, plus the seasonal affix). Enemy forces is a criteria counter.
Each counted creature adds its value on death, and the requirement is 100%. Bosses, most summons and some
creatures count 0. Each death adds a fixed time penalty (15 s in current seasons). The run completes when the last
boss dies *and* forces are at 100%. If the last boss dies first, the run completes on the trash kill that fills
the bar. The result is by time: inside 60% of the limit +3, inside 80% +2, inside the limit +1. Over time, the key
drops one level.

**Keyrun has this.** Tempo scales cast cadence and telegraph lead per key (`sim/Encounter/Tempo.cs:42-61`).
Fortified and Tyrannical by key and rotation parity (`sim/Encounter/Affixes.cs:54-65`), applied as bp to health
and damage (`:77-92`). The Clause rotates by key and week (`sim/Run/Clause.cs:47`). Par is 14400 ticks, 12 min
(`cdata/dungeons/hollow-vault.json:4`). The death and wipe cost (`sim/Encounter/Timer.cs:35-38`), the 80% and 60%
tiers (`:121-124`), the cap at twice par (`:133`) and key movement (`:196-209`). Forces are tallied from dead
actors (`sim/Encounter/Forces.cs:54-67`) and drawn on the HUD (`game/src/Hud/ForcesBar.cs:82`).

**Gaps.**
1. *Completion ignores forces (partial; fix first).* `CheckRunEnd` completes the run when the pull index passes the
   last pull (`sim/Run/RunWiring.cs:368-371`). Forces are never read there. The comment at `sim/Run/Run.cs:376-377`
   says the bar "is a run-completion condition", but no code checks it. A party can kill the boss at 60% forces and
   time the key. Under WoW's rule, and BIBLE 9.5's door rule, it cannot. Fix it one of two ways. Either the door
   (BIBLE 9.5) admits the boss pull only at 1000 points, or `CheckRunEnd` completes only when the last boss is dead
   and `Forces.Read(...).Full` is true. The bible says door, so build the door.
2. *Forces data (partial).* See 2, gap 1. The bar's denominator is 1000 (`gdata/forces.json:2`). Every countable
   enemy needs a row, or the BIBLE 9.2 arithmetic is fiction.
3. *Divergences the bible owns (keep).* An untimed key keeps its level (BIBLE section 8). Slack is a grace bank on top
   of the death cost (BIBLE section 9). The private rating only (BIBLE section 10).

## 7. Party and loot flow

| WoW | Bible ruling | Keyrun |
|---|---|---|
| Group finder with role check and leader invites | Replaced: one Queue button, key level only, ready check (BIBLE sections 0 and 10) | `sim/Run/Backfill.cs:71` |
| Leaver breaks or stalls the key | Replaced: backfill at the next pull start (BIBLE section 10) | `sim/Run/Backfill.cs:93-134` |
| Release, graveyard, run back with the timer running | Kept (R-BACKFILL-2) | `sim/Run/Release.cs:24-54`, `sim/Run/RunWiring.cs:29` |
| Vote to abandon | Kept, with nobody's key depleted (BIBLE section 10) | `sim/Run/RunWiring.cs:372-375` |
| End chest plus weekly Great Vault | Replaced: choose one of three every run, quality from key level (BIBLE section 7) | `sim/Encounter/Rewards.cs:24-30` |
| Public rating and score sites | Replaced: private rating, no export (BIBLE section 10) | n/a |
| Personal loot from bosses | Replaced by the run-end choice; the Seed's Window bonus is keyrun's own | `sim/Encounter/SeedLoot.cs:44-72` |

## 8. Order of operations: a seeded dungeon in the WoW shape

Matt: "make everything reusable, except the dungeons, and they should generate by seed honestly... then we fill
with enemies... like consider order of operations". Keyrun today has one hand-written layout (`gdata/layout.json`)
and a seeded PRNG (`sim/Core/Prng.cs:28`) that nothing uses for layout. Build it in the order WoW's data
depends on itself. Each step is its own lane, and each consumes only the steps before it.

1. **Layout graph from the seed.** Output: boxes (rooms, corridors), doorway points, the critical path
   (entrance to three teaching rooms to boss arena), one fork with two branches that rejoin before the third
   teaching room (BIBLE 9.1), and the room tags (teaching, pack, hazard, boss). Integer only, drawn from `Prng`.
   It validates with the existing `LayoutValidator` (`game/content/Keyrun.Content/Loading/LayoutValidator.cs`). Test: the same
   seed gives the same bytes, and every room is reachable.
2. **Spawn groups and slots.** For each room, a list of slots: position, facing, role (melee, caster, lieutenant,
   elite, boss), group id, aggro radius and optional links. Teaching rooms take fixed slot templates (the Choir is
   two melee and a caster; BIBLE 9.6). Pack rooms take a slot budget. This is WoW's spawn row plus spawn group,
   before any creature is chosen.
3. **Creature templates and AI.** Fill each slot with an `EnemyDef` that matches its role, from a pool, by seed.
   The template carries its abilities, its script (section 3), its forces value and its immunities. Nothing about a
   creature is decided by the room. Then check the forces budget. The critical path stays near 60%, and each branch
   offers about 120%, so a skip is real (BIBLE 9.2). Re-draw the slots on a branch until both hold.
4. **Encounter script.** Bind the boss template and its phases (section 5) to the boss room. Set the door rule
   (boss pull admitted at 100% forces and the third teaching pull cleared; BIBLE 9.5).
5. **Mythic+ state.** Par from route length and expected pull time. Forces required = 1000. Affixes and Clause from
   key and week (already done). One `InstanceState` owns them (section 5, gap 4).
6. **Tuning, last.** Only now set health and damage. Run the bot party over N seeds at keys 2, 5 and 10, and measure
   pull time, boss time (target 90-180 s at key 2), deaths and the share of runs timed. Tune template modifiers, never
   per-seed numbers.

**Defer until a run is testable (WoW pushes these into scripts):** line of sight and pillars; mobs that flee at
low health; casters that keep range and kite; patrol paths that cross rooms; respawn and reset rules past a basic
leash; spawn overlap with doorways (reject the slot instead); diminishing returns on CC; summons with their own
templates; hazard rooms (BIBLE 9.4) inside a generated layout. A seed that cannot meet step 3's forces rule is
rejected and the next seed is drawn. Never patch it by hand.

## 9. Reviewer checklist: ten questions for any keyrun PR inside the dungeon

1. Is the new behaviour **data on a template or spell** (`EnemyDef`, `AbilityDef`, `EffectDef`), or a new branch in
   the run root? A new mob or cast should need no C# change.
2. Does every new dangerous cast have a **cast bar and a floor telegraph derived from its own shape**, and does the
   telegraph match what the cast hits (section 1, gap 1)?
3. Is the cast **interruptible, stoppable or neither**, and is that on the ability (and, once it exists, on the
   effect's mechanic), not on a hard-coded id list?
4. Does the enemy **pick its target through threat** (`ScriptTarget`), and does the change keep the tank the top
   of the table in normal play?
5. Does the group still **engage whole, by ring, damage or link**, and stay idle (no script, no move) until it does
   (BIBLE 9.1)?
6. If an actor moves, does it **stay on walkable space and stop at reach**, with no straight-line walk through a wall?
7. Does the change **count toward or read forces correctly**? Does every new killable enemy have a forces value (0 is
   explicit)? Does completion still need the last boss and 100%?
8. Are times **in ticks at 20 Hz**, and are values integer and basis points, scaled once by Tempo and affixes, with
   no float and no second hidden multiplier (BIBLE section 3)?
9. Does a **boss change keep the fight 90-180 s at key 2** by measurement, with mechanics sequenced, not layered, and do
   its adds despawn on its death?
10. Does the change cross a **bible line**: a sixth button, a mechanic that names a class instead of a tag, a public
    score, a leaver penalty on the players who stay, or anything outside the dungeon loop? If so, it needs a Matt
    decision, not a lane.
