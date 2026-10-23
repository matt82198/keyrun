# Tutorial route: offset pulls, bent route, corridor trash (Hollow Vault)

Status: Design, 2026-10-23; pending Matt's read; Slice A (corridors 9 m) and the Seed ring rule ship separately; this doc is Slice B

Status: S1 SPEC, design only. Owner of the facts below: the keyrun content and bots lanes that follow.
Answers f-1232d0aa ("the mobs are all center of the room, they should be offset so the player can decide what
to pull"), f-9607c9c0 ("the corridors are too narrow and where some of the trash should be placed") and
f-82d9390e ("room design is lame ... not following the bible"). Standing rules: core dungeon loop defers to WoW,
"huge pulls are fun", G2.5 skips are legitimate play. Bible: s3, s4, s9 (9.1 prespawn and "bots pull on purpose",
9.2 forces, 9.6 teaching pulls). Evidence: R-LAYOUT-2 (event store seq 78, lane a5a4ce6a).

## 0. Why the last attempt broke, in one line

Lane a5a4ce6a moved the Choir, Pair and Seed 4 m off z=0 but left `dungeon.json` running along z=0, so the
leader met each ring at a tangent. The bots' pull rules were right; the route stopped going to the pulls. The fix
is to bend the route to the pull (WoW: the tank walks to the pack), not to loosen any of the 19 facts.

Facts read from origin/main 07e2be4b (they win over the brief where the two differ):
- The route is one point per box (`NavLayout` ctor, game/sim/Keyrun.Sim/Encounter/Pulls.cs:285). A bend needs
  a new field. It cannot be done by adding route points.
- Corridors on main are still z +-1500. Section 3 needs the +-4500 widening to land first.
- `hall-sentries` stands in pair-hall at (51500, 4500), ring 3000, not in corridor-3. It stays there.
- An optional pack must clear the walking line (start plus route points) by its ring plus the widest formation
  slot, 1000 mm (LayoutValidator.cs:321-329). A required pack's ring must meet that line (:330-335).
- A group's ring is centred on the mean of its spawns (`Pulls.RingCenter`), not on the room origin.

## 1. Pull placement

**Rule.** Every ring except the boss's stands with its edge more than
1000 mm off the z=0 line, which is the widest formation slot. One clearance rule covers every pull and pack: the validator
already applies it to optional packs, and it is what lets a whole party in formation walk the skip lane. A bare
500 mm would let the Cinder's +1000 slot pull the Choir from the centre line. The sides alternate N, S, N in
teaching order (BIBLE 9.6). Each room's optional pack stands on the side opposite its teaching pull. Every
optional pack's ring centre is at least 8000 mm from every other group's, so each pull is a separate choice and a chain pull is always a decision
the party makes (BIBLE 9.1 "pull size is a decision, read from the floor").

| Group | Box | Origin / centre (x, z) | Ring centre | Ring | Edge vs z=0 | Side, why |
|---|---|---|---|---|---|---|
| choir (pull 1) | choir-room +-8000 | origin (21500, 5000) | (21500, 5333) | 4000 | +1333 | N: the Cantor's -1000 offset puts it at z=4000, the member nearest the approach, so the kick lesson starts with the kick target closest |
| cantor-vigil (opt) | choir-room | centre (14000, -6000), unchanged | (14000, -5858) | 4000 | -1858 | S, opposite the Choir; 13472 mm from it |
| pair (pull 2) | pair-hall +-6000 | origin (38500, -5100) | (43500, -5100) | 4000 | -1100 | S: alternation, and the tank drags the five to mid-hall where spread/stack has the full 12 m |
| hall-sentries (opt) | pair-hall | centre (51500, 4500), unchanged | (51300, 4500) | 3000 | +1500 | N, opposite the Pair; 12369 mm from it |
| seed (pull 3) | seed-chamber +-7000 | origin (63500, 5500) | (73500, 5500) | 4000 | +1500 | N: alternation; engages on its ring like every other pull (R-SEED-2) |
| warrior-remnant (opt) | seed-chamber | centre (69000, -5500), was +5500 | (69000, -5357) | 4000 | -1357 | S, opposite the Seed; 11752 mm from it (7386 on main, which fails the 8000 rule) |
| boss | warden-arena | (93500, 0), unchanged | (93500, 0) | 7100 | n/a | final boss, never skippable |

Spawn offsets do not change (`content/data/dungeons/hollow-vault.json`), so formation shapes and the
MinSpawnFromDoor depth rule (8000-12000 mm along x) still hold. Every member stays at least 900 mm inside its
room's z walls.

**WoW precedent.** M+ trash stands beside the walking path, not on it. The route planner players use (Mythic
Dungeon Tools) is a choice of which packs to pull because packs stand apart. A pack is engaged when the tank walks
into it. Two packs come together only when someone chooses that.

## 2. Route: bend to the pull; the straight line is the skip lane

**Schema.** A `dungeon.json` route entry may carry `via`, an ordered list of `{x, z, group}`. Each point lies
inside that entry's box, and `group` names the pull or pack the point exists to engage. Route entry k is then
"walk box k through its live vias, then to the doorway". The walking line becomes start, then each entry's vias,
then its point. The sim keeps one route point per box. Vias are a parallel per-box list, so no index into the
route moves. A via is live while its group has a living, idle member. Once the group engages, the roles take over
as they do today (`Follow.AnyLivingEnemy`). Once it is dead, the walker skips the via. The rule reads only the
view, so it is stateless.

**Approach rule.** Each pull is entered on a +x leg at the ring centre's z, from a staging point 3000 mm outside
the ring edge. The formation slots stand behind in -x, so the leader is always the first actor in the ring, and
every follower is at least 2500 mm outside it on the engage tick. That preserves "every pull engages on its ring
with the tank in it".

| Route k | Box | via (group) | Point (doorway) | Change |
|---|---|---|---|---|
| 0 | entrance | none | (5500, 0) | none |
| 1 | corridor-1 | none | (10500, 0) | none |
| 2 | choir-room | (14500, 0) hold; (14500, 5333); (21500, 5333), all `choir` | (25500, 0) | vias added |
| 3 | corridor-2 | none (corridor-patrol walks z=0 here, unchanged) | (32500, 0) | none |
| 4 | pair-hall | (36500, 0) hold; (36500, -5100); (43500, -5100), all `pair` | (55500, -3600) | vias added; doorway moves to the corridor pack's z |
| 5 | corridor-3 | (59000, -3600) `fork-sentries` | (62500, 0) | via added |
| 6 | seed-chamber | (66500, 0) hold; (66500, 5500); (73500, 5500), all `seed` | (75500, 3600) | vias added; the Seed engages on its ring like every other pull (R-SEED-2); doorway moves to the corridor pack's z |
| 7 | corridor-4 | (79000, 3600) `stair-choir` | (82500, 0) | via added |
| 8 | warden-arena | none | (96000, 0) | none |

Clearances checked by hand against the optional rule (ring + 1000): cantor-vigil to leg 2, 5879 mm (needs 5000).
hall-sentries to the pair-to-doorway leg, 8559 mm (needs 4000). warrior-remnant to the seed-chamber legs, 5912 mm
(needs 5000). The choir and pair holds are today's points, (14500, 0) and (36500, 0), 4000 mm along the line from the
doorway. The seed hold, (66500, 0), is 4000 mm along the line from route point 5, (62500, 0). So LEADER-WAITS
geometry (followers 2000 mm inside the room) is unchanged.

**Skip lane.** A party walking z=0 from the start to (82500, 0) clears every ring in section 1 by at least
1100 mm (the pair ring; the Seed's ring is 1500 mm out), so it pulls nothing in any room, the seed-chamber
included, or in corridor-3 or corridor-4. It cannot skip the boss. It must time the corridor-2 patrol, which is
WoW's patrol skip. Skipping the
corridor packs as well leaves the bar at 600 + optional packs, so a skip route needs the vigil or two other packs.
That is the gamble against par in BIBLE 9.1, and G2.5 makes it legitimate.

**Cost.** The two room bends add about 10.7 m of walking per room, about 60 ticks each at 180 mm/tick. That is
under 0.2 percent of the 9600-14400 tick target (BIBLE 9.8).

## 3. Corridor trash

Corridor-3 and corridor-4 each get one wall-hugging **required** pack (`optional: false`). Under the PATH-PACKS
rule, the default line walks into it. A party skips it on the far wall. Corridor-1 stays empty because it comes
before the first lesson, the kick (BIBLE 9.6 order). Corridor-2 keeps its patrol, which is its trash. Each pack
hugs the wall on the side of the pull just killed, so the default line stays short. The skip lane on the far wall
is the straighter line toward the next pull, so a skip saves real time, as it does in WoW.

| Pack | Box | Centre | Ring | Members (points) | Forces | Skip lane |
|---|---|---|---|---|---|---|
| fork-sentries | corridor-3 | (59000, -3600) | 2500 | 3 hall.sentry (48) | 144 | z > -1100 to the north wall |
| stair-choir | corridor-4 | (79000, 3600) | 2500 | 2 pair.melee (52), 1 choir.lieutenant (80) | 184 | z < 1100 to the south wall |

The stair-choir carries a kickable caster into the last hall before the boss. WoW's caster-in-the-hallway pack
asks for a kick in a tight space before the boss, and the Choir taught the kick (BIBLE 9.3).

**Forces (1000 = 100%).** The walking line holds 600 (teaching pulls: Choir, Pair and Seed, each bent to on the default line) + 144 + 184 = 928,
which is short of the bar. PATH-PACKS requires that some optional pack is always part of the route decision. Any one optional pack then
fills the bar: vigil 400, sentries 240, remnant 392. The total on offer is 1960. Without any one optional pack the
total stays at 1560 or more, which satisfies SKIPPABLE-PACKS. SkipBarRule holds with disjoint sets {vigil} and
{sentries, remnant}. Branch totals are 1568 (choir-room) and 1560 (seed-chamber). There are two pulls inside 8000
mm of each other along x (sentries and fork-sentries, 7700 apart). Their ring centres are 11176 mm apart, which
passes the 8000 rule, and dragging the sentries into the corridor is the "huge pulls are fun" chain the
party may choose.

## 4. Rule changes the bots and content need

1. `via` is part of the route entry schema and loader (game/content/Keyrun.Content/Model/Layout.cs, Loading/ContentLoader.cs).
2. The walking line is start, then each entry's vias, then its point. The teaching-pull and required-pack rings
   must meet it, each entered on a leg heading within 45 degrees of +x. Every ring except the boss's
   clears z=0 by more than the widest slot, and every optional pack's ring centre is at least 8000 mm from every other group's
   (game/content/Keyrun.Content/Loading/LayoutValidator.cs).
3. `NavLayout` gains `Via`, an init-only per-box list that defaults to empty. The ctor's one-point-per-box invariant
   stays as it is (game/sim/Keyrun.Sim/Encounter/Pulls.cs), and `NavLayouts.ToSim` copies it
   (game/bots/Keyrun.Bots/shared/Follow.cs).
4. `Follow.WalkRoute` walks box k at its first live via (its group alive and idle), else at route point k
   (game/bots/Keyrun.Bots/shared/Follow.cs).
5. `Follow.HoldPoint` is measured along the route: 4000 mm from route point k-1 toward box k's first via, or
   toward route point k when the box has none. It is not 4000 mm along x (game/bots/Keyrun.Bots/shared/Follow.cs).
6. Tank-first engage from the bent waypoint needs no new rule. The +x approach puts the leader in the ring first.
   Before a via whose group is a corridor pack, the leader waits at the previous line point until every living
   seated party member is within 4000 mm of him. This is R-BOTTANK-LEASH-1, and `LeadRoute` and `LeadCommand`
   share one helper for it (game/bots/Keyrun.Bots/shared/Follow.cs).
7. A required pack counts as committed forces, never as a planner choice. `PackGroup` carries `Optional`
   (game/sim/Keyrun.Sim/Encounter/LayoutPacks.cs, game/bots/Keyrun.Bots/shared/ForcesPlanner.cs).
8. The kicker's approach needs no rule change. Entering the Choir ring at (17500, 5333) puts the Cantor at
   4216 mm, against 4110 mm on main. The difference is 106 mm, about 0.6 ticks, and inside the 60-tick window.
   The 2-5 tick overrun came from the tangent entry (game/bots/Keyrun.Bots/Roles/Healer.cs and KickOrder stay as
   they are).

## 5. Facts: S1 red-first, then re-derived

**S1, written first and red on main 07e2be4b:**
- game/content/Keyrun.Content.Tests/TutorialRouteLayoutTests.cs:
  - (a) Every ring except the boss's clears z=0 by more than 1000 mm. Red: the Choir and Pair rings straddle z=0.
  - (b) Every optional pack's ring centre is at least 8000 mm from every other group's. Red: warrior-remnant to seed is 7386 mm.
  - (c) The walking line enters the choir, pair, seed, fork-sentries and stair-choir rings on +x legs. Red: there is no `via`.
  - (d) Corridor-3 and corridor-4 each hold one required pack with a 2000-3000 mm ring. Red: there are none.
  - (e) The line holds 928 forces, and each optional pack alone fills the bar. Red: the line holds 600.
- game/content/Keyrun.Content.Tests/LayoutViaValidatorTests.cs: the validator rejects a via outside its box, a via
  naming an unknown group, and a required pack whose ring no leg enters. Red: there is no schema.
- game/sim/Keyrun.Sim.Tests/NavLayoutViaTests.cs: `Via` defaults to empty and the route stays one point per box. Red: the member does not exist.
- game/bots/Keyrun.Bots.Tests/ViaRouteTests.cs: WalkRoute takes the first live via and skips engaged or dead ones,
  and HoldPoint is measured along the route. Red.
- game/bots/Keyrun.Bots.Tests/CorridorPackPullTests.cs: the scripted leader emits pull_start for fork-sentries and
  stair-choir while inside the ring with every follower outside, and the corridor hold waits for a lagging seat. Red.
- game/bots/Keyrun.Bots.Tests/ForcesPlannerRequiredPackTests.cs: a required pack is committed and never picked. Red.
- tools/Keyrun.Playtest.Tests/SkipLaneTests.cs: a human walking z=0 to (82500, 0) with bots following emits no
  pull_start at all, the Seed's included, with the patrol avoided by timing. Red: the Choir engages on z=0 today.

**The existing facts re-derived, never loosened.** Each keeps its assertion. Only the positions it computes from
change, and they are read from the layout data, never retyped as literals:
- game/bots/Keyrun.Bots.Tests/NavRouteTests.cs: Every_pull_engages_on_its_aggro_ring_never_before,
  No_group_engages_with_a_party_member_deep_inside_its_ring, Every_bot_enters_each_room_within_600_ticks_of_the_leader.
- game/bots/Keyrun.Bots.Tests/KickReactionWindowTests.cs: First_choir_heal_is_kicked_inside_the_window,
  Every_seed_kicks_every_heal_inside_the_window. The 60-tick window does not move.
- tools/Keyrun.Playtest.Tests/PackAggroChasePropertyTests.cs: Kiting_the_choir_into_the_pair_stacks_both_pulls
  (the kite target is derived from the Pair ring centre, not from z=0), and The_bots_clear_key_two_within_par.
- tools/Keyrun.Playtest.Tests/HumanReplayTests.cs: Tab_cycles_the_fight_not_the_dungeon. Which idle groups fall
  inside 40 m is recomputed from the new centres, and "Tab never selects an idle group" stays.
- tools/Keyrun.Playtest.Tests/BotTankTakesHolderTests.cs: Bot_warden_takes_a_pack_member_from_the_human.
- The leader-tangent pull_start facts (the rest of the 19) are in the a5a4ce6a red list (seq 78). Candidates by
  file: game/bots/Keyrun.Bots.Tests/LeadRouteHoldTests.cs, LeadCommandPrespawnTests.cs,
  BotPartyEngagesChoirTests.cs, ChoirEngageTests.cs, and tools/Keyrun.Playtest.Tests/RunCEngagesChoirTests.cs.
  The S2 lane takes the exact method list from that run, not from this document.

**Order.** The corridor widening to +-4500 lands first. Then come the S1 facts and the schema plus validator
(rules 1-3). Then the bots (rules 4-7). The layout data lands last, and the re-derived facts land in the same
pull request as the data.
