# Dungeon synergy: the second axis (R-SYNERGY-1)

Status: design, docs only, written 2026-10-22 against main 1911c618. It implements nothing. It serves goal G2.5
(aesop STATE.md INTENT) and ruling R-SYNERGY-1, and it extends R-TOOLKIT-1. Implementation lanes (section 5) build
it red-first.

Matt, 2026-10-22, verbatim: "like consider this: in a +20 it might be optimal for a rogue to pop defensive, pull a
pack out of place leashing, and then drop smoke to clear aggro and path for skip... this is synergy to the dungeon and
not ability synergy, which i think we've missed the ball a little on".

Sister documents, referenced by name and not edited here: design/TOOLKIT-CONTRACT.md (what every class category
carries beyond its rotation), design/PROGRESSION-PIVOT.md (improvement trees and RNG drops) and
design/PLAYTESTER-GUIDE.md (guided and blind bots). Section 6 lists the lines the guide must carry.

Reading paths:
- **Deciding the design:** sections 0, 1 and 4, then the five calls in section 7.
- **Implementing it:** sections 2 and 5. Every rule in section 2 says what main does today, with file:line.
- **Writing bot guides:** sections 3 and 6.

## 0. Summary

1. **Synergy has two axes.** Axis one is BIBLE s4, unchanged: a timing game between two people (Windows and Combos).
   Axis two is new: the party's toolkit played against the dungeon's own systems. Those systems are leash, aggro and
   aggro drops, telegraph geometry, routes and skips, and the key-level scaling that decides which lines pay.
2. **The dungeon's systems are rules, not moods.** Each one is an integer, deterministic and documented rule
   (KEYRUN invariant 1), drawn on the floor where it can be (BIBLE s9.1 rings). A player who knows the rule can
   exploit it, and that is intended. Skips are legitimate play, as in WoW Mythic+ (G2.5).
3. **Main already has most of the machinery.** It has per-enemy aggro, assist and leash radii (run.json:43-53), a
   leash that walks a pack home and resets it (Leash.cs:22-157), the holder lock (Holder.cs:16-71) and a forces surplus
   on every route (BranchFill.cs:22-28). What it lacks is an **aggro drop**, a **veil** (unseen), a defined state for an
   **evading** pack, a **skip** record, **line of sight** for enemy casters, and a leash for **patrols**.
4. **The worked lines (section 3)** cover four class categories at +2, +10 and +20. Matt's rogue line is written out
   tick by tick. Each line is a sequence a guided bot can follow.
5. **Exploits are priced, never patched out (section 4).** The fix for a line that is too strong is a price the
   dungeon can show: leash radius, walk-home time, drop cooldown, veil length or key scaling. Making the exploit
   impossible is not a fix.

## 1. The two axes

| | Axis one: synergy between players (BIBLE s4) | Axis two: synergy with the dungeon (this document) |
|---|---|---|
| What is combined | One player's Setup and another player's Payoff | A player's toolkit (E, R, F and one rotational ability) and a dungeon rule |
| Where it shows | The Window ring on the enemy or floor | Aggro rings, the leash line, telegraph shapes and the route |
| The skill | Landing a Payoff inside the 60-tick Window | Pressing the right tool at the tick the rule turns over |
| When it pays most | Every key, on every pull | Mostly at high keys, where killing costs more and route time decides the timer |
| Failure looks like | A missed Combo, shown privately (BIBLE s4) | A pack that resets at full health, a drop that hands the pack to the healer, or a skip that runs out of forces |

The axes cross. A smoke that drops a pack onto the tank is a hand-off between two people (axis one timing) through
the holder rule (axis two). The bible's centre stays the centre. Axis two gives it a board to be played on.

**Budget.** Nothing here adds a key. BIBLE s2 holds: ten keys, five rotational. Every tool below sits on E, R or F, or
on one rotational ability through a Facet or fork (STATE.md D31 rule 2). BIBLE s9 still bans skip and invisibility
**consumables**. A class tool is not a consumable.

## 2. The dungeon's systems as exploitable rules

Each subsection has the same four parts: the rule as it should be read, what main does today (file:line), the
exploit it opens, and what must change.

Paths: `Sim/` is `game/sim/Keyrun.Sim/`. `run.json` is `game/content/data/run.json`. Ability and effect data is
under `content/data/` (the content the C# loader embeds).

### 2.1 Constants a line is planned from

| Quantity | Value | Source |
|---|---|---|
| Tick | 50 ms (20 Hz) | BIBLE s3, STATE.md D9 |
| Player walk speed | 180 mm/tick (3.6 m/s) | run.json:2-5 |
| Enemy walk speed | 207 mm/tick; hounds and fast melee 260 | game/content/data/enemies/*.json `speedMmPerTick` |
| Gliding (Chanter F) | +5000 bp movement for 60 ticks, 2 charges: 270 mm/tick | content/data/effects/player.json `gliding`; content/data/abilities/chanter.json `chanter.glide` |
| Hymn, Ward verse | +2000 bp movement: 216 mm/tick | content/data/effects/player.json `hymnSpeed` |
| Blink (Cinder F) / Charge (Warden F) | 8000 mm, 2 charges, 200-tick cooldown; Charge also stuns 20 ticks | content/data/abilities/cinder.json, warden.json |
| Per-enemy aggro (sight) radius | 8000 mm, measured from the enemy's anchor | run.json:43-46; Sim/Encounter/EnemyAggro.cs:45 |
| Assist radius | 3000 mm | run.json:45 |
| Leash radius | 30000 mm from home, straight line | run.json:46; Sim/Encounter/Leash.cs:85 |
| Pack ring (layout) | 3000 to 7100 mm per pack; bible default 4000 | game/content/data/layout/hollow-vault/packs/*.json; BIBLE s9.1 |
| Bosses | aggro 0, assist 0, leash 0 (no leash, no sight, no assist) | run.json:47-52 |
| Death / wipe cost | +300 / +500 ticks on the run clock | Sim/Encounter/Timer.cs:35, :38 |
| Release delay | 100 ticks, then respawn at the entrance | Sim/Run/RunWiring.cs:29 |

Two consequences shape every line. First, **enemies outrun an unaided player** (207 against 180), so a drag costs
health unless a defensive, Gliding, Hymn Ward, Blink or a stun pays for it. Second, **a 30 m leash is about 167 ticks
of unaided walking**, so a drag is a few seconds, not a minute.

### 2.2 Leash

**The rule.** Every idle enemy has a **post**, the spot it stands on, or its wander anchor (IDLE-WANDER). Once its
pack is engaged, if any living member stands more than its leash radius from its post (straight line, XZ), the
**whole pack evades**. It drops its casts, holder and threat, walks home along the grid path, and on the tick the last
member arrives it **resets**: full health, no effects, idle, engageable again.

**Main today.**
- The post updates every tick while the enemy is idle, to `WanderAnchor ?? Pos` (Leash.cs:44-47). The post is the
  enemy's own spot, not the place it was pulled from.
- Evade trigger is any member past the leash (Leash.cs:85). It applies to the whole pack: stopcast, target, holder and
  threat cleared, idle and evading set, `pack_evade` emitted, telegraphs cancelled (Leash.cs:89-112).
- The walk home is an A* grid path at the enemy's own speed (Leash.cs:128-141; called from
  Sim/Run/Phases/CombatPhase.cs:26). Arrival snaps within one step (Leash.cs:57-63).
- Reset sets full health, clears effects and emits `pack_reset` (Leash.cs:64-73). The rule runs once per tick after
  combat and before the aggro check (Sim/Run/Run.cs:263-264).
- An evading pack engages on nothing: no ring, no damage trigger, no sight and no assist (Aggro.cs:146-150, :165;
  EnemyAggro.cs:102-103). Evaders are also never assisted into a fight (EnemyAggro.cs:88).
- An evading enemy runs no script and never chases (Idle is set, Chase.cs:60). But it **is still a legal target**:
  hostile area and target filters exclude only dormant enemies (Sim/Combat/Targeting.cs:156, :171).
- Patrols get **no leash** (game/src/HostCore/HostRunSetup.Aggro.cs:46, `leash: false`).
- Proven today by AggroChaseTests.Leash_walks_home_and_resets_at_full_health (Keyrun.Sim.Tests/Encounter/
  AggroChaseTests.cs:179).

**The exploit, and its price.** A pack dragged out of place leaves its post empty for the drag **plus** its walk
home, and while it walks home it sees nothing. That empty, blind interval is the **skip window**. Its length is set
by where the pack starts to evade (distance d from its post):

```
window_ticks  ~= drag_ticks + walk_home_ticks
drag_ticks    ~= d / carrier_speed          (180 unaided, 216 under Hymn Ward, 270 Gliding)
walk_home     ~= path_len(d) / enemy_speed  (207; never less than d / 207)
```

A longer drag buys a longer window and costs more swings taken, and the leash caps it at 30000 mm. That trade is
the price.

**What must change.**
1. **Evaders are immune** (WoW's evade rule). Today a party can kill a leashing pack while it walks home and cannot
   fight back: free forces with no price (Targeting.cs:156, :171 do not exclude `Evading`). See call 1 in section 7.
2. **Patrols leash** to the point on their loop where they were pulled, then rejoin the loop
   (HostRunSetup.Aggro.cs:46). Without this, a patrol can be dragged anywhere forever.
3. **The leash line is drawn.** While a pack is engaged, its post and leash radius show as a faint line from each
   member to its post. The line turns solid inside the last 3000 mm. A rule that decides a line must be readable on
   the floor (BIBLE s4 "Everything is readable").
4. **`pack_evade` carries its cause**: `leash`, or `aggro_drop` (2.3). The run log and the guided bots read it.

### 2.3 Aggro, the holder, and the aggro drop

**The rule today (D30 HOLDER-LOCK).** An engaged enemy attacks one **holder**. At engage the holder is the nearest
living player (range, then lowest id). After that only a taunt moves it. When the holder dies, the enemy goes to the
nearest living player. Damage, healing and threat never move it (Holder.cs:6-15, :50-69, :66-68; Casting.Complete.cs:72-80).
The threat table still fills but picks nothing (Threat.cs:50-55, :63-75; design/THREAT-MODEL.md section 1).

**Main today has no aggro drop.** No effect op lets a player shed its holder role. `Threat.ClearThreat` exists
(Threat.cs:81), but nothing calls it, and clearing threat would not move the holder anyway.

**The aggro drop (new, generic op `aggroDrop`; D31 rule 3: any class's ability may carry take, give or drop).** When a
player X presses a tool carrying `aggroDrop`, then on that tick, for every living engaged enemy E whose holder is X,
in ascending enemy id:

1. **Hand-off.** E's new holder is the nearest living player who is not X, not veiled (2.4) and within E's own aggro
   radius (8000 mm, measured from E). Ties go to the lowest id. This is the existing "holder's death" rule with a range
   limit added and the dropper treated as gone (Holder.cs:66-68). The receiver is visible from the floor: whoever stands
   inside E's sight ring.
2. **No receiver.** E keeps no holder. If, after step 1, **no living member of E's pack has a holder**, the whole pack
   evades exactly as a leash does (2.2), with `pack_evade {cause: "aggro_drop"}`. If some members were handed off and
   some were not, the members without a receiver stand still (no chase, no script) and are held by the next
   `Holder.LockHolders` pass only when a non-veiled player enters their sight ring.
3. **The dropper is not re-locked.** `Holder.LockHolders` (Holder.cs:50-69) must skip veiled players. Without that it
   would re-lock the nearest player, often the dropper, on the very next tick.
4. **Bosses ignore it.** A boss has aggro 0 (run.json:48-51), so it has no receiver and no leash to evade on. A drop on
   a boss is a visible "Immune" miss, never silent.

Why the nearest player in range and not the top of the threat table: D30 settled that threat never picks the target
("no aggro meters"), and THREAT-MODEL.md R-TAUNT-3 forbids reading the table. A range rule keeps the drop readable
from the floor. If you want the pack, stand in its ring. If you want it to reset, nobody stands there. See call 2 in
section 7.

**What an aggro drop does to a pack mid-pull.** Three cases cover it:

| Who stands in the pack's sight rings at the drop tick | Result | What it is for |
|---|---|---|
| The tank (closer than anyone else) | Every enemy the dropper held moves to the tank, with no taunt spent | A hand-off: the skirmisher or ranged opens the pull, then gives it to the tank |
| The healer (closer than the tank) | The pack moves to the healer | A mistake the floor showed in advance. It is priced, not prevented |
| Nobody | The pack evades home blind | A reset: the start of a skip, or a save |

### 2.4 The veil (unseen)

**The rule (new effect `veiled`).** A veiled player is not seen. Aggro rings and personal sight ignore them
(Aggro.cs:127-129 builds the player list; EnemyAggro.cs:45). No holder lock picks them (2.3 step 3), and an assist
cannot be triggered by them. The veil breaks on the tick the player casts a hostile ability or deals damage, and that
hit then engages its target's pack by the ordinary damage rule (Aggro.cs:151, :180-182). It does not hide a player
from a telegraph: a veiled player standing in a cone is hit.

**Main today.** There is nothing like it. The player list in Aggro.Triggered filters only dead players and open
seats (Aggro.cs:127-129).

**The tool that carries it.** "Smoke" in Matt's line is a skirmisher-category R tool (design/TOOLKIT-CONTRACT.md owns
the kit). It is a 3000 mm circle on the caster that, on its cast tick, (a) runs `aggroDrop` for every ally inside it
who holds an enemy, and (b) veils every ally inside it for 100 ticks. That is 18000 mm of walking at 180 mm/tick.
Example values: tune them, never patch them (section 4).

### 2.5 Telegraph geometry as a tool

**The rule.** A telegraph's shape, origin and facing are fixed on the tick it is drawn. It hits whoever stands in it
when it resolves. Enemy area abilities hit only players (Targeting.cs:169-171), and **hazards hit enemies too**
(BIBLE s9.4).

**Main today.**
- The `Telegraph` record copies the caster's yaw verbatim at draw time (Sim/Encounter/Telegraphs.cs:19, :248). It is
  resolved by integer point-in-circle, point-in-cone and point-in-line against every living actor except the source
  (Telegraphs.cs:149-185).
- A target cast keeps its start target (CAST-KEEPS-START-TARGET, Targeting.cs:154-156).
- Tempo shrinks the lead by 150 bp per key, down to a 10-tick floor (Sim/Encounter/Tempo.cs:25, :28).

**The exploits.**
- **Bait a frontal.** The holder stands so the cast starts facing an empty wall, then steps out of the cone during
  the bar. The cone does not follow. Example: Vault Brute Crushing Slam, a 90-degree, 3500 mm cone with a 40-tick
  cast (BIBLE s9.3).
- **Stack a soak.** A split-damage stack (the Pair's Stack Call) shares its damage among everyone inside it, so a
  full stack is the cheapest stack.
- **Drag through a hazard.** Vent Gout deals 15% of max health to enemies and Floodjet drenches enemies (BIBLE s9.4).
  The tank parks or walks a pack through the set that is about to fire.
- **Kick into geometry.** An Undertow that lands just before a jet drags its target into the arc. Kicking the
  Undertow, or letting it land on purpose on a pack member's holder at the right moment, is a choice (BIBLE s9.4).

**What must change.** Nothing in the resolver. Two gaps sit around it:
1. Hazards hitting enemies needs a test that pins it: a pack parked in an erupting vent loses 15% of max health per
   member on the eruption tick.
2. At +20 the lead is 71.5% of base (2850 bp of shrink). The bait windows must stay at or above the 10-tick floor, and
   the guided lines in section 3 are timed from cast start, not from telegraph draw.

### 2.6 Routes, skips and what a skip gains

**The rule.** A route is a branch choice plus a set of packs killed. Forces count dead enemies only (Sim/Encounter/
Forces.cs: `Points` sums dead enemies), and the key completes at 100% forces with the final boss dead (BIBLE s9.5).
Every route offers more than 100%: the seeded fill gives each branch at least 120% (Sim/Dungeon/BranchFill.cs:22-28),
and ContentValidator.CheckSkipBar requires two disjoint optional sets that each reach the bar (lane
content-skip-bar-s4, R-PACKS-1). **The surplus is the skip budget.**

**A skip is the party walking past a pack's post without killing it.** These ways exist or are designed here:

| Skip | Mechanism | Main today |
|---|---|---|
| Wall-walk | Walk outside every ring | Works: rings are the only sight (Aggro.cs:151; EnemyAggro.cs:45) |
| Drag-and-drop | One player drags the pack off the route, then drops it (2.3). The party walks past during the window (2.2) | Drag works. Drop is missing |
| Leash-out | The same, but the drag runs to 30000 mm and the leash ends it | Works today. It costs the most swings |
| Veil walk | Allies inside smoke walk past the ring unseen for the veil's length | Missing (2.4) |
| Death skip | Die past a pack and respawn ahead of it | **Impossible**: respawn is at the entrance (RunWiring.cs:26-29). See call 4 in section 7 |
| LoS pull | Pull casters around a corner so they come to the tank | Missing: enemy casts have no line of sight. NavGrid.SegmentCrosses (Sim/Nav/NavGrid.cs:171) is the primitive to use |

**What counts as skipped.** A pack is **skipped** on the first tick that every living seated player stands in a room
that the route reaches only past that pack's post, while the pack is alive and not engaged, or is evading. The sim
emits `pack_skipped {packId, forcesLeft}`. A skipped pack can still be pulled later; skipped is a log fact, not a
state. The skip itself is never penalised: no penalty ticks (EncounterState.PenaltyTicks moves only on deaths and
wipes, Timer.cs:35-38), and no forces are lost.

**What the timer gains.** The pack's kill time, minus the time the drag carrier spends away from the party (that
player's lost damage), minus any detour, minus any deaths. The forces bar sets the hard limit: a skip that leaves the
route short of 100% must be paid back by walking back to the pack (BIBLE s9.1). That walk is a time cost the party
chose.

**What must change.** The `pack_skipped` event and its record (no file on main has it). Bots also need a planner
that chooses a skip set against the forces surplus. ForcesPlanner (game/bots/Keyrun.Bots/shared/ForcesPlanner.cs)
plans kills, not skips.

### 2.7 Key scaling: why these lines are optimal at high keys

Per key above 1 (Sim/Encounter/Tempo.cs:22-34): enemy health +300 bp, cast frequency +200 bp, and telegraph lead
-150 bp (floor 10 ticks). Fortified adds +2000 bp health and +3000 bp damage to non-boss enemies on its weeks from +5,
and always from +10 (run.json:24-30; BIBLE s8).

| | +2 | +10 | +20 |
|---|---|---|---|
| Trash health (bp of base) | 10300 | 12700 x 1.2 = 15240 | 15700 x 1.2 = 18840 |
| Kill time against +2 (party power fixed) | 1.00x | 1.48x | 1.83x |
| Trash damage | 1.00x | 1.30x | 1.30x |
| Cast frequency (bp) | 10200 | 11800 | 13800 |
| Telegraph lead (share of base) | 98.5% | 86.5% | 71.5% |

The cost of a skip, in walking ticks, does not depend on the key. The cost of a kill rises by 83% from +2 to +20, and
the danger of a kill rises with it: more casts, less lead, harder hits. So:

- **At +2** killing is cheap and safe, and the lines below mostly kill. A skip saves little and risks a reset.
- **At +10** partial skips pay: veil walks past small packs, and chain pulls instead of single pulls.
- **At +20** the best route kills close to exactly 100% and skips the rest. The drag-and-drop line becomes optimal,
  which is Matt's point. The difficulty curve D67 asks for ("master a longer route/path and perfect coordination")
  comes from this, not from more health.

These multipliers come from the constants. Measured tick costs come from lane L8 (section 5). Party power also grows
with gear (BIBLE s7), and that shifts every column equally.

## 3. Worked lines

**Notation.** T is the tick of `pack_engage` for the pack being worked. Ticks before T are negative. Distances are
mm. "Post" is the pack's home (2.2). Lines name the class **category**. The skirmisher (rogue) category has no class
on main yet; its kit (E defensive, R smoke, F grapple) is assumed from design/TOOLKIT-CONTRACT.md. Tank = Warden,
healer = Chanter, ranged = Cinder. A guided bot (design/PLAYTESTER-GUIDE.md) executes a line as a list of
(condition, action) steps, where each condition is a sim fact the bot view already exposes or that section 5 adds.

### 3.1 Skirmisher (rogue category): Matt's line, +20, in full

Setting: the party is on the route at staging point S, outside every ring. Pack P (3 to 5 trash, no elite) stands on
the route ahead. A side corridor C leads off the route from P's room. The route's forces surplus covers P (2.6). No
other idle pack's assist ring (3000 mm) touches the drag path.

| Tick | Who | Action | Rule that fires (file) |
|---|---|---|---|
| T-20 | Party | Tank, healer and ranged stop at S, 9000+ mm from every member of P, and outside every ring | Sight is 8000 from the anchor (EnemyAggro.cs:45) |
| T-1 | Skirmisher | Walks into P's ring alone | |
| T | Skirmisher | **E defensive** (TOOLKIT-CONTRACT skirmisher E; 60 ticks) | `pack_engage`. Holder = nearest player = skirmisher (Holder.cs:66-68) |
| T+1 | Skirmisher | Walks into C, away from the route. At 180 mm/tick P closes 27 mm/tick, so melee reach lands from about T+15 | Chase on the A* path (CombatPhase.cs:24) |
| T+0 to T+110 | Party | Starts the skip walk along the route past P's post. P is engaged and chasing, so its rings do not re-trigger and nothing else can take it (Holder lock) | Aggro.cs:133 skips engaged groups |
| T+40 | Skirmisher | **F grapple** forward when the defensive's last 20 ticks begin. This resets swing reach | |
| T+111 | Skirmisher | At about 20000 mm from P's post (inside the 30000 leash, so the leash has not fired), **R smoke** on self | `aggroDrop`: nobody is in P's sight rings, so no receiver, so `pack_evade {cause: aggro_drop}`. Skirmisher veiled 100 ticks (2.3, 2.4) |
| T+112 to about T+210 | P | Walks home blind on its grid path, about 97 ticks or more (20000 / 207) | Leash.cs:128-141. Evaders see nothing (Aggro.cs:146) |
| by T+205 | Party | Every member is past P's post and 8000+ mm beyond its sight. **Deadline: P's reset tick** | `pack_skipped {packId: P}` when the last seat enters the next room |
| about T+210 | P | Arrives home: `pack_reset`, full health, idle, rings live again | Leash.cs:64-73 |
| T+112 to T+211 | Skirmisher | Walks back through C and past P (veiled for 100 ticks, about 18000 mm), and rejoins | Veil (2.4) |

The line fails, visibly, in three ways. The skirmisher dies during the drag: the holder passes to the nearest player,
possibly a party member walking past, and P engages the party mid-skip (Holder.cs:66-68). The healer stands inside
P's sight ring at T+111: P is handed to her (2.3). The party is still inside P's sight when P resets at about T+210:
P engages again at full health.

Variants: at +10 the same line is worth it only for a pack whose kill time exceeds about 220 ticks of the
skirmisher's absence. At +2 it never is (2.7).

### 3.2 Skirmisher, +2 and +10

- **+2, open and hand off.** T: the skirmisher body-pulls P (holder = skirmisher). T+1 to T+20: it backs toward the
  tank, who stands 4000 mm behind it. T+20: R smoke. The tank is the nearest non-veiled player in every member's
  8000 sight, so every enemy moves to the tank with no taunt spent (2.3 case 1). The tank's Taunt stays ready for a
  loose caster.
- **+10, veil walk past a small pack.** At T-30 the party stacks inside 3000 mm of the skirmisher at a spot 9000 mm
  short of pack Q's sight edge, on the wall side. T-30: R smoke (nobody holds anything, so only the veil applies),
  and all four are veiled for 100 ticks. T-29 to T+70: the party walks 18000 mm past Q. Nobody attacks; any hostile
  cast breaks that player's veil and engages Q. Q is never engaged (T does not occur): `pack_skipped {Q}`.

### 3.3 Tank (Warden)

- **+2, bait the frontal.** T: the Warden body-pulls a pack holding a Vault Brute. The Brute's Crushing Slam starts
  a 40-tick cast with its yaw locked at draw (Telegraphs.cs:19). On the Slam's cast-start tick, the Warden stands so
  the cone faces the wall. Bar tick 1 to 20: the Warden steps 2000 mm sideways out of the cone. Bar tick 40: the cone
  resolves on nobody. Brace is kept for the next Slam if the step is late.
- **+10, chain inside the leash.** Packs A and B have posts 14000 mm apart. T: the Warden pulls A. T+1 to T+60: it
  walks A to a corner 2000 mm inside B's assist radius (3000 mm, measured from B's anchor). A is about 12000 mm from
  its post and B is at its own, both inside 30000. B joins by assist (EnemyAggro.cs:72-99, the body pull, test
  AggroChaseTests.cs:229). T+60: Charge (8000 mm line, 20-tick stun) into the casters as the first stop. T+61: the
  party kills A and B together. **The leash constraint:** if the corner were more than 30000 mm from A's post, A would
  evade mid-fight and reset at full health (Leash.cs:85). The chain spot is chosen inside both leashes.
- **+20, hazard and line of sight (needs lanes L5 and L1).** T: the Warden pulls a pack with two casters. T+1 to T+40:
  it walks the pack around a pillar so the casters lose line of sight and must walk in (LoS rule, L5). The melee
  follow on the A* path and the casters collapse onto the melee. T+40: it parks the stack on vent set A as set A's
  60-tick telegraph draws (Bellows: 160-tick cycle). The Warden steps onto idle set B. T+100: set A erupts, and every
  enemy loses 15% of max health. T+100 onward: Brace on each Slam bar. Kicks follow the kick order; Shield Bash is
  480 ticks, so one Warden kick per 24 s.

### 3.4 Healer (Chanter)

- **+2, stand where no holder can find you.** At every engage the Chanter stands farther from the pack than the tank
  and outside every sight ring. The holder rule picks the nearest player at engage and on a holder's death
  (Holder.cs:66-68), so the healer's position decides who catches a loose enemy.
- **+10, speed the drag.** When a dragger (tank or skirmisher) starts a drag at T, the Chanter swaps to Ward
  (Refrain) and places Hymn on the drag lane at T+5. Inside it the dragger moves at 216 mm/tick, faster than 207-speed
  chasers, so swings stop landing. At T+30, a Ward Dissonance silence on a dragged caster mid-cast counts as a stop
  (BIBLE s9.3), and the kick ledger credits it (R-KICK-WARD-1).
- **+20, receiver geometry and the skip walk.** In Matt's line (3.1), the Chanter stays 9000+ mm from every member of P
  at T+111, so the drop resets P and does not hand it to her. Then: T+112, Ward Hymn on the route at P's post (2500 mm,
  160-tick window). Party members crossing it move at 216 mm/tick and cross P's 16000 mm sight diameter in about 75
  ticks, well inside the walk-home window. T+190: Gliding (270 mm/tick, 60 ticks) to clear the last ring before P's
  reset tick.

### 3.5 Ranged (Cinder)

- **+2, the tag pull.** The Warden stands at the chosen fighting spot F, closer to the pack than any other seat. At T,
  the Cinder hits one pack member with Ember Bolt from 20000+ mm. The pack engages by damage (Aggro.cs:151, :180-182),
  and the holder is the nearest player at engage, the Warden at F, not the Cinder. The pack runs to F. This is a pull
  placed by two people.
- **+10, stops and Purge.** At T+k, when a Bellows Keeper starts Stoke (70 ticks), the Cinder Cold Snaps it as a stop
  inside the cast (BIBLE s9.3), or Purges Stoked off the Brute within 10 ticks of it landing. When a Drowned
  Chorister's Undertow cone targets the Cinder, Blink 8000 mm sideways out of the 30-degree cone before it resolves.
- **+20, pull into the hazard and place the corpse.** At T-10 the Warden stands on idle vent set B. At T, as set A's
  telegraph draws, the Cinder tag-pulls the pack so its walk to the Warden crosses set A. At T+60, A erupts on the
  pack. Kill order puts each Ash Hexer's death (Cinders, 2000 mm on-death circle) on set A's grates, off the party's
  standing set. Never tag an evading pack: it is immune (2.2 change 1), and the damage would be wasted on a pack that
  resets to full health.

## 4. Anti-patterns: exploits are priced, never patched out

A deterministic exploit that a player can read from the floor and pay for is a line (G2.5: "Leash, aggro and routes
are designed as deterministic, exploitable systems; skips are legitimate play"). These changes are forbidden. Each
names the price to turn instead.

| Forbidden patch | Why it is wrong | Price to turn instead |
|---|---|---|
| A pack teleports home or resets the tick it leashes | Deletes the skip window | Leash radius; walk-home speed |
| Unlimited chase, or a leash that grows with key | Deletes the drag-and-drop line at exactly the keys where it should be optimal | Drag damage, which already scales with Fortified |
| A random leash radius, aggro radius or drop outcome | Breaks invariants 1 and 4 (a coin flip is a bug) | None. Every value is an integer in content |
| A forces or time penalty for skipping, or a route that offers exactly 100% | Deletes the route decision (R-PACKS-1 skip bar) | Forces values per pack |
| A pack that "remembers" the party after a reset (pre-aggro, wider sight) | Hidden state, not readable on the floor | Pack ring size, drawn |
| An aggro drop that silently fails on some enemies | Unreadable. Bosses must show "Immune" | Drop cooldown; the receiver rule |
| Hazards that stop hitting enemies | Deletes the hazard-drag line (BIBLE s9.4) | Hazard enemy damage percent |
| Line of sight that lets enemies see through walls to stop corner pulls | Hidden, and unfair | Caster walk-in speed |
| Hidden diminishing returns on smoke or the veil | Invisible state | Veil length; the veil-break rule; cooldown |

Evade immunity (2.2 change 1) is **not** an anti-cheese patch. A pack walking home blind and unable to fight back is
not a line: it costs the party nothing the dungeon can see, so there is nothing to price. The exploit that stays is
the window the walk home creates.

## 5. Implementation order: file-disjoint lanes, each red-first

All paths are under `game/`. Every lane writes its named red test first and proves it red against main, then green.
Lanes in the same wave own disjoint files.

| Lane | Wave | Owns | Red test written first |
|---|---|---|---|
| **L1 leash-rules** | 1 | sim/Keyrun.Sim/Encounter/Leash.cs (adds a public `EvadeGroup(..., cause)`), sim/Keyrun.Sim/Combat/Targeting.cs (excludes `Evading`), src/HostCore/HostRunSetup.Aggro.cs (patrol leash) | `LeashRuleTests`: (a) a pack displaced 30001 mm from its post emits `pack_evade {cause: leash}` that tick and `pack_reset` on the tick its last member reaches home, which is at least ceil(path/207) ticks later; (b) an evading enemy takes no damage from a player cast; (c) a dragged patrol leashes back to its loop point |
| **L4 skip-ledger** | 1 | sim/Keyrun.Sim/Encounter/Skips.cs (new), one call in sim/Keyrun.Sim/Run/Run.cs after :264 | `SkipLedgerTests`: a party that walks past a pack's post while the pack evades emits `pack_skipped {packId}`, and the encounter's PenaltyTicks is unchanged |
| **L5 caster-los** | 1 | sim/Keyrun.Sim/Encounter/Chase.cs, sim/Keyrun.Sim/Combat/Casting.Start.cs | `CasterLosTests`: an enemy caster whose segment to its holder crosses an obstacle (NavGrid.SegmentCrosses) starts no cast and walks its grid path until the segment is clear |
| **L2 aggro-drop** | 2 (after L1) | sim/Keyrun.Sim/Combat/Holder.cs (`Drop`, and LockHolders skips veiled players), sim/Keyrun.Sim/Combat/Casting.Complete.cs, the op record in sim/Keyrun.Sim/Combat/Types.cs, and the op parse in content/Keyrun.Content | `AggroDropTests`: (a) a drop mid-pull with the tank 4000 mm from the pack hands every dropped enemy to the tank, with no taunt cast; (b) a drop with no player in sight evades the whole pack `{cause: aggro_drop}`; (c) the dropper is not re-locked on the next tick; (d) a drop on a boss emits `immune` and changes nothing |
| **L3 veil** | 2 (parallel to L2) | sim/Keyrun.Sim/Encounter/Aggro.cs (player list), sim/Keyrun.Sim/Encounter/EnemyAggro.cs (sight and assist), content/data/effects/player.json (`veiled`) | `VeilTests`: (a) a veiled player inside a ring engages nothing for the veil's length; (b) the veil breaks on a hostile cast, and that hit engages the pack; (c) a veiled player in a cone is still hit |
| **L6 skirmisher-tools** | 3 (after L2, L3 and design/TOOLKIT-CONTRACT.md) | content only: the skirmisher class's E, R smoke and F grapple | `SmokeLineTests` (Keyrun.Sim.Tests): the 3.1 line scripted as commands. The pack evades at T+111, `pack_skipped` fires, and no seat is engaged by P |
| **L7 guided-lines** | 3 | tools/Keyrun.Playtest guided policies (per design/PLAYTESTER-GUIDE.md) | One guided-bot test per category line in section 6, each asserting the line's observable (for example, the tank chain: B's `pack_engage` within 5 ticks of the corner, and no `pack_evade`) |
| **L8 key-pricing** | 4 | tools/Keyrun.Playtest.Tests/DungeonSynergyPricingTests.cs | At +20 the drag-and-drop route finishes in fewer ticks than the kill-it route on the same seed. At +2 it does not. Both are measured, and the margin is printed |

Holder.cs is owned by L2 alone, and the veil's holder half lives there. L3 owns only the sight side. L1's
`EvadeGroup` is the one shared entry, so L2 starts after L1 merges. L8 is the test that shows the design delivered
G2.5: the line is optimal at +20 and not at +2.

## 6. Lines design/PLAYTESTER-GUIDE.md must carry

One line per class category for the **guided** bots. Blind bots carry none (G2.4: they measure time-to-learn).

- **Tank:** "Chain only inside both leashes: pull A, walk it to within 3000 mm of B's post, and keep the fight spot
  within 30000 mm of every engaged pack's post. Bait every frontal by facing the wall at cast start, then stepping out."
- **Healer:** "Never be the nearest player at engage, and stand outside the sight ring of any pack a teammate is about
  to drop. When a drag starts, Ward Hymn the drag lane, then the skip path."
- **Ranged:** "Open pulls by tagging from range while the tank stands at the fighting spot, so the holder is the tank.
  Never damage an evading pack. Put Hexer deaths on the hazard, not on the party."
- **Skirmisher:** "From +15, if the route's forces surplus covers pack P: defensive at engage, drag P off the route
  inside its leash, smoke with nobody in its sight, and rejoin veiled. The party crosses P's post before P's reset tick."

Every line is checked against its observable event (`pack_engage`, `pack_evade` and its cause, `pack_reset`,
`pack_skipped`, `telegraph_hit` contents), never against the bot's own narration.

## 7. The five calls Matt might overrule

1. **Evading packs are immune and blind** (WoW's evade rule). The party cannot farm a leashing pack. The skip window
   stays.
2. **An aggro drop goes to the nearest unveiled player within the enemy's own sight radius (8000 mm). Otherwise the
   whole pack evades.** It does not use the threat table (D30, R-TAUNT-3). The alternative is WoW's rule, next on the
   threat table.
3. **Smoke is a skirmisher R tool that drops aggro and veils allies inside it for 100 ticks. The veil breaks on any
   hostile action.** It is not a consumable and not a new key. Values are examples to tune.
4. **No death skips for now.** Release respawns at the entrance (R-BACKFILL-2), so dying past a pack gains nothing.
   WoW's rule is that the respawn point advances after each boss. Adopting it for D67's three-boss dungeons would make
   death skips a priced line (+300 ticks each) in later segments.
5. **The leash is a straight 30000 mm from the post, and a chained pack past its own leash resets mid-fight.** Chain
   geometry is priced by the leash instead of letting packs follow forever. Patrols gain a leash too.

## Appendix: glossary

- **Post:** where an idle enemy stands; its leash home (Leash.cs:44-47).
- **Holder:** the one player an engaged enemy attacks (D30, Holder.cs).
- **Aggro drop:** an op that sheds the holder role (2.3).
- **Veil:** unseen by rings, sight, assist and holder lock, until a hostile action (2.4).
- **Evade:** a pack that gave up, walking home blind and immune, then resetting (2.2).
- **Skip window:** the drag plus the walk home, while a pack's post is empty or the pack is blind.
- **Skip:** walking past a pack's post without killing it; logged as `pack_skipped` (2.6).
