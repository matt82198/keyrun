# Keyrun art and navigation plan: from capsules on a grid to a themed dungeon run

PURPOSE: this plan turns the keyrun demo from coloured capsules on a grey grid into a themed dungeon run (rooms,
corridors, real characters and enemies with animations), so Matt judges the game by feel from viewed frames
instead of seeing an aim trainer. Status: real. Decisions: STATE.md D15 (frames), D16 (this plan), D17 (CC0 kits
only; the Lock must be theoretically winnable), D18 (themed rooms with real navigation).

Matt, 2026-10-18, verbatim: "we need to design an actual dungeon and character models for the demo lmfao";
"it cant be just kovakks"; "no custom art lol its a vibecode game, bots don't have to beat the lock but it should
be theoretically winnable lol..."; "just thematically theme the dungeons, and why are all the mobs spawned on top
of you? are we not going to have navigation in the dungeons lmfao?"

Sources read for this plan (origin/main c0ba4f5): the frames in C:/Users/Mattt/Desktop/keyrun-screens/,
design/BIBLE.md, design/ENGINE-PLAN.md, STATE.md, game/CLAUDE.md, game/scenes/*.tscn, game/src/Host/HostRunSetup.cs,
game/src/Host/SimDriver.cs, game/src/View/{ActorView,LabelView,CameraRig}.cs, game/sim/Keyrun.Sim/{Run/Run.cs,
Encounter/Pulls.cs,Encounter/Chase.cs,Combat/Movement.cs}, content/data/dungeons/hollow-vault.json,
game/content/data/run.json, tests/oracle/generate.ts, and tools/godot/screens.ps1 on origin/feat/screens-tool.

## 1. What the demo looks like today (facts from the frames and the code)

Frames: C:/Users/Mattt/Desktop/keyrun-screens/game-NNN-tXXXs.png, one every 4 s of wall clock, run C (autorun, key 2,
seed 1, Warden plus Cinder and Chanter bots), window 1280x720 titled "Keyrun (DEBUG)".

- Geometry: one flat grey plane with a dark grid shader under a flat dark sky (game/scenes/Arena.tscn:58-59, a
  40 x 40 m PlaneMesh). No walls, no doors, no props, no lighting beyond one sun and the environment. Every frame
  (game-002-t004s through game-024-t092s) shows the same empty plane.
- Bodies: every actor is a CapsuleMesh 0.4 m radius, 1.8 m tall (game/src/View/ActorView.cs:50) with a small white
  box "nose" for facing (:53). Party capsules are blue, enemies red, the Choir lieutenant purple (game-002), the boss
  and Twin orange (game-022, game-023). Colour is the only identity; Warden, Cinder and Chanter are indistinguishable
  except by their floating name plates. Bodies come from LabelView.cs:107-109 (Actor.tscn instance, BodyColor).
- Labels: each actor carries a floating name and HP text plate; cast bars float above casters ("Hymn 0.2s",
  "Cadence 0.8s" in game-022, game-023); an interrupted cast shows "Interrupted: Choral Mending" (game-002).
- Telegraphs: flat cyan, blue and pink rings on the floor, plus the text "SPREAD: move apart 1.9s" as a banner and
  as floor labels (game-016). The Lock and the Window rings read as more rings of the same kind.
- Camera: third person behind and above the Warden (CameraRig.cs:21 Distance 7.5 m, :25 pivot 1.4 m), so the
  Warden's capsule fills the lower centre of every frame and partly hides the fight.
- HUD (works, stays): top help line; pull banner "Pull N/4 <name>" with the objective line (the content teaches note is designer intent, not shown); Slack bar
  with timer and par ("Slack 31.0s +2 0:11 / par 12:00"); party frames with status chips (BRACED, HOT); Warden
  health and Momentum bars; ten-slot ability bar with cooldown numbers; run-end panel "Run cleared -- within par"
  with the choice of three and the private "What got you" log (game-024-t092s).
- HUD defects seen: at pull 4 the pull title "The Hollow Warden - cleave, sundered-tw..." is clipped behind the
  Slack bar (game-022, game-023); the "Sundered Twin (A)" plate overlaps "The Hollow Warden" plate, and the two
  orange capsules overlap each other (game-022, game-023).
- The screen and Clause pick are never captured: frame game-002-t004s already shows pull 1 at 0:11. Autorun skips
  the screen by design: SimDriver.cs:430 `StartsOnScreen => !Options.Autorun && !Options.Solo`, and run C is an
  autorun.

Where the pulls spawn today, relative to the player (Matt's question, answered from the code):
- The party spawns on the line z = 4000 mm: Warden at (0, 4000), Cinder at (-700, 4000), Chanter at (-2100, 4000)
  (HostRunSetup.cs:48 WardenSpawn, :78-82 Roster, :243 party spec).
- Pull 1, the Choir, is created at run start, tick 0 (Run.cs:53 `Pulls.SpawnPull(dungeon, 0, ...)`): two melee at
  (0, 1000), 3 m in front of the Warden, and the lieutenant at (0, -1000), 5 m away
  (content/data/dungeons/hollow-vault.json pull "choir"; HostRunSetup.cs:407 copies the x/z verbatim).
- Pull 2, the Pair, five melee at (5000, 0), about 6.4 m from the party start; pull 3, the Seed elite, at
  (10000, 0); the boss at (15000, 0) (HostRunSetup.cs:42 BossSpawn, :422) with the Twins at boss x +/- 2000 mm
  (:69 TwinOffsetMm, :183).
- Each next pull spawns on the same tick the previous pull clears (Run.cs:104 after Pulls.CheckPullAdvance,
  Pulls.cs:82), wherever the party happens to stand, and its melee chase at once: there is no aggro radius, no
  leash and no trigger (Chase.cs: any living enemy outside its engage range moves straight at its target).
- The whole run is one axis-aligned box, x -4000..18000 mm, z -6000..9000 mm, 22 m x 15 m
  (game/content/data/run.json arenaBounds; Movement.cs:146 ClampToBounds). There is no traversal between pulls:
  every pull is fought in the same arena within a few metres of where the last one died. Matt's reading is correct.

## 2. Target look

A readable low-poly dungeon run, not photoreal. The Hollow Vault is a sealed stone vault gone hollow: cold blue-grey
stone, warm torchlight at doorways, each room lit and dressed for the one thing it teaches (BIBLE s9). The tone is
clean and toy-like (KayKit and Kenney style) so mechanics, not texture noise, carry the screen; BIBLE s3 "cast bars
are the enemy's language" and "shape plus colour, never colour alone" stay the first rule.

Camera and readability rule (binding for every lane):
- At 1280x720 a still frame must show, without the HUD: where every telegraph lands, whether the Lock is open, which
  body is a Twin and which is the boss, and where each bot stands. If a prop, wall or light hides any of these, the
  prop loses.
- Floor telegraphs draw above floor decals and below bodies; no floor texture may use the telegraph colours
  (cyan, pink, the Window colour bands). Floors stay mid-value, desaturated.
- Party members keep a blue rim or base ring, enemies a red one; the boss and the Twins get distinct silhouettes and
  scales, not just colours.
- The camera may pull up and back in the boss arena (more pitch, distance 9-10 m) and must never clip into walls:
  rooms are open-topped (no ceilings rendered) and walls nearer the camera than the Warden fade or cut away.
- The HUD stays exactly as it is, plus the two polish fixes from section 1.

## 3. Environment: the Hollow Vault as rooms and corridors (D18)

Layout (proposed numbers, mm, x forward along the route; final numbers belong to lane 1):

| Room | Purpose and theme | Walkable box (x; z) |
|---|---|---|
| Entrance | the screen and Clause pick backdrop; banners, a sealed vault door, the party's start | -6000..6000; -6000..6000 |
| Corridor 1 | torch-lit passage | 5000..11000; -1500..1500 |
| Choir room | a ruined chapel: pews, candles, a lectern for the lieutenant (the kick ledger) | 10000..26000; -8000..8000 |
| Corridor 2 | | 25000..33000; -1500..1500 |
| Pair hall | a long open hall with pillars at the edges and a clear centre for spread and stack | 32000..56000; -6000..6000 |
| Corridor 3 | | 55000..63000; -1500..1500 |
| Seed chamber | an overgrown crypt, roots and a cracked floor seal where the Seed's Window drops | 62000..76000; -7000..7000 |
| Corridor 4 | descending stairs (visual only, floor stays y 0) | 75000..83000; -1500..1500 |
| Warden arena | a round-cornered vault, 28 m square, empty centre, braziers on the rim, the boss throne | 82000..110000; -14000..14000 |

The extents above are copied from game/content/data/layout.json (boxes, minX..maxX; minZ..maxZ) on 2026-10-19 (STATE-R9);
layout.json is the truth and this table must follow it, not the other way round.

Rules:
- Each pull lives in its own room. Pull spawn points become room-relative (the room centre plus the spawn offset
  the content already carries), so the Choir still stands at +/-1000 z from its room centre and the formation shapes
  do not change.
- Trigger rule: room entry. A pull spawns (its existing `pull_start`) on the first tick any party member stands
  inside that room's trigger box (the room box shrunk 2000 mm from the entry door). Why room entry and not a
  proximity radius: an integer box test is deterministic and cheap, the doorway is the visible line the player sees,
  a radius would fire through walls and need per-pull tuning, and it keeps pull scripts' tick offsets unchanged
  (scripts already start at `pull_start`). Enemies appear with the kit's spawn animation (skeletons rising) at the
  far side of the room, 8-12 m from the door, never on top of the player.
- Forward-only (BIBLE s9): the corridor out of a room is not walkable until that room's pull clears; the gate opens
  on `pull_clear`. Backtracking into a cleared room is allowed and harmless.
- The boss arena keeps today's boss logic: the boss spawns on arena entry facing the door; the Twins spawn on
  `boss_add_spawn` at boss x +/- 4000 mm (wider than today's 2000, so bodies and plates do not overlap) and the Lock
  ring draws on the boss.
- Walkable space becomes an ordered list of boxes that overlap at doorways. A move is accepted if the new position is
  inside any open box; otherwise it is clamped into the box that held the pre-move position. A list of one box is
  exactly today's ClampToBounds, so the flat-arena oracle fixtures keep their meaning.

Files that change (lane 1, sim-affecting):
- game/content/data/layout.json (new, C#-only like run.json): rooms, corridors, trigger boxes, gate order, route
  waypoints. Pull spawn offsets stay in content/data/dungeons/hollow-vault.json and are read as room-relative.
- game/content/Keyrun.Content/** loader, validator and model for layout.json (fails closed on a pull without a room).
- game/sim/Keyrun.Sim/Combat/Movement.cs (walkable list), game/sim/Keyrun.Sim/Encounter/Pulls.cs and
  game/sim/Keyrun.Sim/Run/Run.cs (spawn on room entry instead of on the previous clear; gates).
- game/src/Host/HostRunSetup.cs (room-relative spawns, boss and Twin placement, party start in the Entrance).
- game/bots/Keyrun.Bots/shared/Follow.cs (new) and the role entry points: with no living enemy, each bot walks the
  route to its formation slot 2-3 m behind the leader (slots from the roster SpawnX offsets).
- game/src/Host/PlayerInput.cs ScriptedWarden (:359): with no living enemy, walk the layout waypoints to the next
  room; then today's combat script.

Oracle and parity consequence:
- If the lane leaves content/data/** byte-identical (layout in game/content/data/layout.json, spawns read
  room-relative), frozenSourceHash does not move (tests/oracle/generate.ts:28 FROZEN_ROOTS includes `content`), and
  `npx vitest run tests/oracle` stays green with the committed manifest. If the lane must edit content/data/**, the
  hash moves and the same lane regenerates the TS oracle the way #158 did, with the old and new hash in its PR body.
- Either way the C# goldens that run the real host layout move (positions and end ticks): the content-pull golden,
  the boss golden, the HudSceneTest expectations. Lane 1 regenerates them with a stated reason (D11 rules; ENGINE-PLAN
  phase 5 rule).
- Parity contract: the TS oracle has no room concept and TS feature work is frozen (STATE.md NEXT STEPS). Choice:
  narrow the oracle scope to combat. The oracle vectors and traces keep running on the single-box flat arena, which
  the walkable list reproduces exactly; the route (rooms, triggers, gates, follow) is proven by the Godot e2e
  (HudSceneTest run C reaches every room and the boss) and by new C# sim tests. Why not port rooms to TS: it would
  unfreeze a tree Phase 4 deletes. Phase 4 TS deletion stays blocked on D10.

## 4. Characters and enemies

All rigs are KayKit humanoid rigs (Rig_Medium) so one animation library serves everyone (section 5). Every model is
a child of the existing ActorView node (Actor.tscn): the model scene replaces the Body capsule and the Nose box,
keeps the node's position and yaw from ActorView.cs:72-75 interpolation, and the capsule stays as the fallback when
a model fails to load.

| Actor (sim class) | Model (pack, free tier) | Read at a glance | Animations needed |
|---|---|---|---|
| Warden, player (warden) | KayKit Adventurers: Knight, shield and sword | blue tabard, shield | idle, run, 1H attack, shield block (Brace), charge, hit, death |
| Cinder bot (cinder) | KayKit Adventurers: Mage | orange-red robe, staff | idle, run, cast loop, cast release, hit, death |
| Chanter bot (chanter) | KayKit Adventurers: Rogue_Hooded, recoloured green-gold | hooded, no weapon glow | idle, run, cast loop (song), heal release, hit, death |
| Choir melee (choir.melee) | KayKit Skeletons: Skeleton Warrior | sword and shield | spawn, idle, run, 1H attack, hit, death |
| Choir lieutenant (choir.lieutenant) | KayKit Skeletons: Skeleton Mage | robed caster, the kick target | spawn, idle, cast loop (heal), interrupted (hit), death |
| Pair melee x5 (pair.melee) | KayKit Skeletons: Skeleton Minion | small, many | spawn, idle, run, attack, hit, death |
| Seed elite (seed.elite) | KayKit Skeletons: Skeleton Rogue at 1.3x scale, green emissive tint | bigger, glowing | spawn, idle, run, attack, slam cast, death (drops the Window) |
| twinA, twinB | KayKit Skeletons: Skeleton Warrior at 1.15x, one blue and one pink trim | a matched pair | spawn, idle, run, attack, stun (stagger), death |
| Hollow Warden boss | KayKit Adventurers: Knight at 1.9x scale, dark hollow material, no face colour | a giant fallen warden | idle, walk, cleave (2H chop), slam (2H overhead), Lock cast loop, enrage, hit, death |

Notes:
- The boss is the player's own Knight model hollowed out: thematically the "Hollow Warden", and it needs no paid
  Golem or Necromancer (those are KayKit paid "Extra" tier, ruled out by D17).
- Lock telegraph: the boss plays its cast loop while a translucent shield shell (a Godot SphereMesh with a fresnel
  shader, no asset) and the existing Window ring show; `lock_broken` shatters it (Kenney particle sprites);
  `lock_failed` flashes it red; `boss_enrage` tints the boss.
- Cleave and slam keep their existing floor telegraphs; the animation's impact frame is timed to the cast end tick,
  never the other way round (the sim owns timing).

## 5. Asset sourcing (CC0 only, D17)

Paid or original art is ruled out (D17: "no custom art lol its a vibecode game"). Only free-tier CC0 packs; paid
tiers of the same packs ("Extra", "Source") are not used.

| Pack | URL | License as written on the page (checked 2026-10-18) | Covers |
|---|---|---|---|
| KayKit Dungeon Remastered | https://kaylousberg.itch.io/kaykit-dungeon-remastered | "Creative Commons Zero v1.0 Universal"; "Free for personal and commercial use, no attribution required. (CC0 Licensed)" | walls, floors, stairs, doors, chests, barrels, tables, crates, traps, banners; .gltf/.bin, .fbx, .obj |
| KayKit Adventurers (free: Knight, Barbarian, Rogue, Rogue_Hooded, Mage, Ranger) | https://kaylousberg.itch.io/kaykit-adventurers | "Creative Commons Zero v1.0 Universal"; "These assets are CC0 so you can use them freely. But please don't resell unmodified copies or claim them as your own." | Warden, Cinder, Chanter, the boss; rigged, basic movement clips; .gltf, .fbx |
| KayKit Skeletons (free: Warrior, Rogue, Mage, Minion) | https://kaylousberg.itch.io/kaykit-skeletons | "Creative Commons Zero v1.0 Universal"; "Free for personal and commercial use, no attribution required. (CC0 Licensed)" | Choir, Pair, Seed, Twins; rigged, basic movement clips |
| KayKit Character Animations | https://kaylousberg.itch.io/kaykit-character-animations | "Creative Commons Zero v1.0 Universal"; "Free for personal and commercial use, no attribution required. (CC0 Licensed)" | 150+ clips for Rig_Medium and Rig_Large: idle, hit, death, spawn, run, 1H and 2H melee, block, spellcasting, skeleton variants |
| Kenney (particle sprites, UI icons, extra props as needed) | https://kenney.nl/support | "all game assets on the asset pages are public domain licensed (CC0)" | VFX sprites for the Lock, combo bursts, torches; each pack's own page and LICENSE file are checked at download |

Excluded until proven: Quaternius. Individual pack pages (for example https://quaternius.com/packs/ultimatemonsters.html)
say "CC0", but https://quaternius.com/license.html now publishes the "Quaternius Asset License (QAL) v1.0", which is
not CC0. A Quaternius pack enters only if the license file inside its download says CC0, and the lane quotes it.

Gaps and how they are filled (no Blender): the Lock shield shell, Window rings, gate bars and floor seals are Godot
primitives (SphereMesh, TorusMesh, CSG boxes) with kit textures or simple shaders; the boss is a scaled, re-materialed
Knight; Twin identity is a trim colour plus scale. A second CC0 pack is the fallback if a silhouette fails the
readability rule.

Folder convention: downloads happen in later lanes, never in this one, into game/assets/<pack-id>/ (for example
game/assets/kaykit-dungeon-remastered/), with the pack's own license file copied beside the assets unchanged, and one
entry per pack in game/assets/ATTRIBUTION.md (pack name, author, URL, license as written, download date, files used).
CC0 needs no attribution; the file exists so the provenance is on disk.

## 6. Godot pipeline

- Import: glTF 2.0 (.gltf + .bin) only, one wrapper scene per model under game/assets/<pack-id>/scenes/. Import
  settings: root type Node3D; meshes generate LODs on, shadow meshes on, light baking disabled; animations imported,
  idle and run looped; materials extracted once to .tres so tints are edited in one place.
- Scale: 1 Godot unit = 1 metre, as the scenes already use (ActorView.cs:14 MmToM 0.001; capsule 1.8 m tall). Each
  wrapper scene scales its model so a standard character stands 1.8 m and its feet sit at y 0; the sim never sees
  scale. The lane prints the imported AABB height before and after.
- Materials: KayKit packs share one gradient atlas texture per pack, so one material per pack and few draw calls.
  Team rim and Twin trims are a per-instance shader parameter, not duplicated materials.
- Environment: kit tiles are placed through a GridMap with a MeshLibrary built from the dungeon kit (batched draws),
  generated from layout.json by an editor-free C# builder at scene load, so the render geometry and the sim boxes come
  from the same file and cannot drift.
- Animation: an AnimationTree per actor: a locomotion blend driven by the interpolated speed (ActorView position
  delta), a cast loop while ActorState.CastId is set, and one-shots from the sim events the host already publishes on
  SimDriver.Stepped (SimDriver.cs:458, raised at :920): `damage` (hit), `death`, `cast_stopped` (interrupted),
  `stagger` (Twins stunned), `pull_start` (spawn), `boss_add_spawn`, `lock_open`, `lock_broken`, `lock_failed`,
  `boss_enrage`, `telegraph` (cleave and slam wind-up). Views read; they never write sim state.
- Budget at 1280x720, 60 fps, integrated GPU, Forward+ (project.godot:24): under 600 draw calls and 300k visible
  triangles; one directional light with shadows plus at most 8 omni lights, of which at most 2 cast shadows; rooms
  outside the current and next room are hidden. If the budget fails on Forward+, the lane may switch the project to
  the Mobile renderer and report it (reversible).
- Export smoke stays green: `build/keyrun.exe --headless -- --smoke` prints `SMOKE ok`, and the game.yml export job
  passes; assets must import headless on the CI runner.

## 7. Proof per D15

Every lane that changes anything visible ships PNG frames plus an mp4 of run C into
C:/Users/Mattt/Desktop/keyrun-screens/<lane>/ and the orchestrator views them before accepting.

- Tool: tools/godot/screens.ps1 (being promoted by lane SCREENS-TOOL on feat/screens-tool; this plan does not write
  it). Flags: `-OutDir <dir>` (required), `-IntervalSeconds 4`, `-MaxSeconds 150`, `-Name run`, `-NoVideo`, and
  `-GodotArgs` whose default is run C: `--path game --fixed-fps 60 --resolution 1280x720 --position 0,0 -- --autorun
  --ticks 7000 --key 2 --seed 1 --log build/screens-run.txt`. Frames are named `<name>-NNN-tXXXs.png` by elapsed
  seconds; the mp4 is made with ffmpeg 9.0.1 (on PATH on this box).
- Command: `powershell -NoProfile -ExecutionPolicy Bypass -File tools/godot/screens.ps1 -OutDir
  C:/Users/Mattt/Desktop/keyrun-screens/<lane> -Name <lane>`. After lane 1 the route is longer, so a lane raises
  `-MaxSeconds` until the run-end panel is in the last frame.
- Entrance frames: autorun skips the screen (SimDriver.cs:430), so lanes that touch the Entrance also capture a
  non-autorun launch (the screen opens first) with `-GodotArgs` lacking `--autorun` and `-MaxSeconds 10`.
- The hand-back names at least six frames by file name and says what each shows against the section 2 rule.

## 8. Lanes (Phase 3.5 "Look and route"; one PR each)

Every lane: Opus per D11 (development); its recon and its docs bookkeeping on Haiku. Every lane keeps `SMOKE ok`,
game.yml green, `npx vitest run tests/oracle` green with the committed manifest, and ships the section 7 frames.
Off-limits for every lane unless listed as owned: content/data/**, tests/oracle/**, sim/**, bots/src/**, client/**,
server/**, .github/**, STATE.md (orchestrator), other lanes' files.

1. **Navigation core: rooms, corridors, room-entry pulls, bot follow, autorun route** (SIM-AFFECTING).
   Owned: game/content/data/layout.json, game/content/Keyrun.Content/**, game/sim/Keyrun.Sim/{Combat/Movement.cs,
   Encounter/Pulls.cs,Run/Run.cs}, game/src/Host/{HostRunSetup,PlayerInput}.cs, game/bots/Keyrun.Bots/shared/Follow.cs
   and the role hooks, game/src/Host/HudSceneTest.ps1, their tests, the regenerated C# goldens; grey-box walls as plain
   CSG boxes from layout.json (game/scenes/Arena.tscn) so the frames show rooms. Frames: run C showing the party
   walking each corridor and each pull spawning at the far side of its room. Acceptance: HudSceneTest run C reaches
   all four rooms in order and ends `Run cleared -- within par`; a sim test proves no pull spawns before room entry
   and no enemy spawns within 6 m of a party member; the D14 sweep (keys 2, 5, 8, 10 at seeds 1 and 7) is re-run and
   its end ticks are reported; goldens regenerated with the reason stated; the oracle hash unchanged (or regenerated
   with old and new hash if content/data had to change).
2. **Dungeon shell: KayKit dungeon kit on the layout** (render-only). Owned: game/assets/kaykit-dungeon-remastered/**,
   game/assets/ATTRIBUTION.md, game/src/View/DungeonBuilder.cs (new), game/scenes/Arena.tscn, lighting and
   environment. Frames: every room and corridor in run C. Acceptance: walls and floors match the lane 1 boxes
   (a test compares GridMap cells to layout.json), budget met, oracle and goldens untouched.
3. **Player and bots: Knight, Mage, Rogue_Hooded with animations** (render-only). Owned:
   game/assets/kaykit-adventurers/**, game/assets/kaykit-character-animations/**, game/src/View/ActorView.cs,
   game/src/View/ActorAnimator.cs (new), game/scenes/Actor.tscn, ATTRIBUTION.md entries. Frames: the party running a
   corridor, casting, Brace, a death if one occurs. Acceptance: the three classes are told apart in a frame without
   name plates; idle, run, cast and hit play from sim events; capsule fallback works when a model is missing.
4. **Enemies: KayKit Skeletons for the Choir, the Pair and the Seed** (render-only). Owned:
   game/assets/kaykit-skeletons/**, enemy wrapper scenes, the ActorView model map. Frames: each pull's spawn
   animation, the lieutenant's heal cast being kicked, the Pair spread call, the Seed's death and Window. Acceptance:
   each enemy class has its model and the clips in section 4.
5. DONE (PR #178 MERGED c2676604, ROOM-THEMING-1). **Room theming: Entrance and Choir room** (render-only). Owned: theme scenes for those rooms under
   game/scenes/rooms/, their lights and props. Frames: the screen over the Entrance (non-autorun launch) and the Choir
   fight. Acceptance: section 2 readability rule holds in every frame of the room.
6. DONE (PR #179 MERGED 24a8d0ef, ROOM-THEMING-2). **Room theming: Pair hall and Seed chamber** (render-only). Owned: game/scenes/rooms/{PairHall,SeedChamber}.tscn
   and props. Frames: spread and stack in the hall, the Seed Window on the cracked seal. Acceptance: telegraphs
   readable over the dressed floor.
7. DONE as two PRs: #180 MERGED 8b329434 (ROOM-THEMING-3, arena) and #181 MERGED 39ec4367 (LOCK-VIEW, Lock). **Boss arena, Hollow Warden, Twins and Lock visuals** (render-only, except the Twin offset 2000 to 4000 mm in
   HostRunSetup.cs:69, which is sim-affecting and moves the boss golden; the lane states it). Owned:
   game/scenes/rooms/WardenArena.tscn, boss and Twin wrapper scenes, game/src/View/LockShieldView.cs (new),
   HostRunSetup.cs:69. Frames: cleave wind-up, slam, Twins spawn and stagger, Lock open, Lock fail or break, enrage,
   run end. Acceptance: boss and Twins never overlap in frames; the Lock shell is visible whenever `lock_open` is live.
8. **HUD polish** (render-only). Owned: game/src/Hud/{Objective,TimerSlack}.cs, game/scenes/Hud.tscn,
   game/src/View/LabelView.cs. Fixes: the pull 4 title clipped behind the Slack bar (game-022, game-023), the
   "Sundered Twin (A)" plate overlapping the boss plate, plates riding the model head height. Frames: pull 4 with the
   full title and separate plates. Acceptance: no HUD text overlaps another in any run C frame.
9. **Lock winnability proof** (additive sim test, no behaviour change). Owned:
   game/sim/Keyrun.Sim.Tests/LockWinnableTests.cs (new) and, if needed, game/fixtures/input/lock-break.input-log.json.
   A scripted-input run on the real sim (HostRunSetup plus Run.Step, the same path the host steps, not the synthetic
   BossRulesTests.cs:115 setup): from `--pull 4`, at `lock_open` the Warden lands a Setup on the boss and the Cinder a
   Payoff inside the 60-tick Window; assert `lock_broken`. Acceptance: test green on CI game-test; no assertion is
   added that the bot party breaks the Lock (D17).
10. DONE (PR #172 8ffe1e34 CAMERA-FEEL, PR #174 36feb7e1 HOST-HEADING, PR #176 49c127c5 PLATE-HEIGHT, PR #177 7318aa64 PARTY-FRAMING). **Feel and performance pass** (render-only). Owned: camera tuning in game/src/View/CameraRig.cs (wall fade, arena
    pull-back), LOD and light budgets, the export smoke. Frames: the full run C plus an mp4 Matt can watch end to end.
    Acceptance: budget measured and reported (draw calls, fps on this box), `SMOKE ok`, frames viewed.

Order: 1 then 2 (biggest visual change first, on a route that already exists), then 3, 4, then 5, 6, 7 in parallel
where files allow, then 8, 9 (9 can run any time after 1), then 10.

## 9. Risks

- Smart App Control (D13): a fresh exported exe can be blocked or crash with `gd_mono.cpp:487`. Lanes report it
  literally and stop; CI game-test stays the acceptance gate; nobody touches security settings.
- Asset scale mismatch: KayKit models are not authored at 1.8 m; every wrapper scene normalises to 1 unit = 1 m and
  the lane prints the AABB. Wrong scale shows instantly in frames.
- Animation retargeting: all characters stay on KayKit Rig_Medium (the boss is a scaled Knight), so no retargeting is
  needed; a non-KayKit model would need Godot retargeting, which the animation pack warns may look wrong.
- Frame budget: dungeon tiles and lights can exceed an integrated GPU; GridMap batching, room culling and the light
  cap in section 6; the Mobile renderer is the fallback.
- TS parity oracle: render-only lanes must not move frozenSourceHash; each proves it with `npx vitest run
  tests/oracle` green against the committed manifest and no diff under content/data/** or tests/oracle/**. Lane 1 is
  the only planned lane that may regenerate, and only the C# goldens unless content/data changes.
- Traversal changes run timing: walking about 110 m at 3.6 m/s (180 mm per tick, run.json) adds about 30 s per run,
  so par, the Slack bar, the end ticks and the D14 tuning margins move. Lane 1 re-runs the D14 sweep and reports;
  if any key stops clearing within par, a follow-up content tuning lane (Opus, content/data and an oracle
  regeneration, as #158) re-sweeps; the numbers go to Matt, not a fitted constant.
- Bot follow can strand a bot on a corner of the walkable boxes; the follow walks route waypoints, and lane 1 asserts
  every bot enters each room within 10 s of the human.
- Quaternius license drift (section 5): excluded unless the downloaded license file says CC0.

## 10. Phase placement

Phase 3.5 "Look and route", between Phase 3 and Phase 4, not inside Phase 5. Why: D18 makes navigation part of the
demo and it changes the sim (spawns, triggers, gates, timing), so it must land before the Phase 3 exit and D10 are
judged on a human-played run; judging feel on capsules in one box and then changing the run would make that judgment
stale. Phase 5 stays Matt's tuning on the finished look. Matt may still record a human run earlier (NEXT STEPS); the
Phase 3 exit is re-checked on the Phase 3.5 build. Phase 4 TS deletion stays blocked on D10.
