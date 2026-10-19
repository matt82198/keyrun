# Asset attribution and provenance

Every third-party asset in game/assets/ is CC0 (STATE.md D17: "no custom art", CC0 kits only). CC0 needs no
attribution; this file exists so the provenance of each pack is on disk. One entry per pack (design/ART-PLAN.md
section 5).

## KayKit Dungeon Remastered 1.0

- Folder: `game/assets/kaykit-dungeon-remastered/`
- Author: Kay Lousberg (www.kaylousberg.com)
- Source used: https://github.com/KayKit-Game-Assets/KayKit-Dungeon-Remastered-1.0 (branch main, archive
  `codeload.github.com/KayKit-Game-Assets/KayKit-Dungeon-Remastered-1.0/zip/refs/heads/main`; the zip comment names commit
  b0ca9bd96a8072ab36a3a5464f00ed1e06a16d07). The same pack is published at https://kaylousberg.itch.io/kaykit-dungeon-remastered.
- Downloaded: 2026-10-18
- License as written in the pack's `addons/kaykit_dungeon_remastered/Assets/LICENSE.txt` (copied unchanged beside the
  assets as `LICENSE.txt`):

  > License: (Creative Commons Zero, CC0)
  > http://creativecommons.org/publicdomain/zero/1.0/
  >
  > This content is free to use in personal, educational and commercial projects.
  >
  > Support me by crediting Kay Lousberg, www.kaylousberg.com (this is not mandatory)

- Files copied (pack file names kept; from `Assets/gltf/` unless noted):
  `banner_patternA_blue.gltf.glb`, `banner_patternC_red.gltf.glb`, `banner_shield_red.gltf.glb`, `banner_thin_red.gltf.glb`,
  `barrel_large.gltf.glb`, `barrel_small_stack.gltf.glb`, `candle_triple.gltf.glb`, `chair.gltf.glb`, `chest_gold.glb`,
  `crates_stacked.gltf.glb`, `floor_dirt_large.gltf.glb`, `floor_tile_large.gltf.glb`, `floor_tile_small.gltf.glb`,
  `floor_tile_small_broken_A.gltf.glb`, `floor_tile_small_weeds_A.gltf.glb`, `pillar.gltf.glb`, `pillar_decorated.gltf.glb`,
  `rubble_large.gltf.glb`, `table_long.gltf.glb`, `table_medium_broken.gltf.glb`, `table_small_decorated_A.gltf.glb`,
  `torch_lit.gltf.glb`, `torch_mounted.gltf.glb`, `wall.gltf.glb`, `wall_arched.gltf.glb`, `wall_cracked.gltf.glb`,
  `wall_gated.gltf.glb` (27 meshes), `Assets/texture/dungeon_texture.png` (the shared colour atlas) and `Assets/LICENSE.txt`.
- Local additions (ours, not the pack's): the Godot `.import` sidecars (embedded images discarded, light baking off: every
  .glb embeds a copy of `dungeon_texture.png`, so the one shared copy is used instead) and `dungeon_material.tres` (the one
  StandardMaterial3D every kit mesh is drawn with).

## KayKit Adventurers Character Pack 1.0

- Folder: `game/assets/kaykit-adventurers/`
- Author: Kay Lousberg (www.kaylousberg.com)
- Source used: https://github.com/KayKit-Game-Assets/KayKit-Character-Pack-Adventures-1.0 (branch main, commit
  672074b73ba276876a19e8816ecdc5241817ab47, files fetched from `raw.githubusercontent.com` at that commit). The same pack is
  published at https://kaylousberg.itch.io/kaykit-adventurers.
- Downloaded: 2026-10-18
- License as written in the pack's `addons/kaykit_character_pack_adventures/LICENSE.txt` (byte-identical to the repo-root
  `LICENSE.txt`; copied unchanged beside the assets as `LICENSE.txt`):

  > KayKit : Adventurers Character Pack (1.0)
  >
  > Created/distributed by Kay Lousberg (www.kaylousberg.com)
  > Creation date: 13/03/2023 09:00
  >
  > ------------------------------
  >
  > License: (Creative Commons Zero, CC0)
  > http://creativecommons.org/publicdomain/zero/1.0/
  >
  > This content is free to use in personal, educational and commercial projects.
  >
  > Support me by using a brand resource provided in this pack or by crediting Kay Lousberg, www.kaylousberg.com (this is not mandatory)

  (The file continues with a Patreon thank-you, a supporter list and social links; it carries no further terms.)

- Files copied (pack file names kept; from `addons/kaykit_character_pack_adventures/Characters/gltf/` unless noted):
  `Knight.glb` (the Warden), `Mage.glb` (Cinder), `Rogue_Hooded.glb` (the Chanter) and
  `addons/kaykit_character_pack_adventures/LICENSE.txt`. Each .glb embeds its own colour texture and the 76 Rig_Medium
  animation clips, so no separate texture or animation pack is used.
- Local additions (ours, not the pack's): the Godot `.import` sidecars (embedded images kept uncompressed,
  `gltf/embedded_image_handling=3`; light baking off).

## KayKit Character Pack : Skeletons 1.0

- Folder: `game/assets/kaykit-skeletons/`
- Author: Kay Lousberg (www.kaylousberg.com)
- Source used: https://github.com/KayKit-Game-Assets/KayKit-Character-Pack-Skeletons-1.0 (branch main, commit
  15b62b9bad122f72926c10fb14d622c73819fa54, archive `api.github.com/repos/KayKit-Game-Assets/KayKit-Character-Pack-Skeletons-1.0/zipball/15b62b9bad122f72926c10fb14d622c73819fa54`;
  the zip comment names that commit).
- Downloaded: 2026-10-18
- License as written in the pack's `addons/kaykit_character_pack_skeletons/LICENSE.txt` (copied unchanged beside the
  assets as `LICENSE.txt`):

  > KayKit Character Pack : Skeletons (1.0)
  >
  > Created/distributed by Kay Lousberg (www.kaylousberg.com)
  > Creation date: 02/02/2024 09:00
  >
  > ------------------------------
  >
  > License: (Creative Commons Zero, CC0)
  > http://creativecommons.org/publicdomain/zero/1.0/
  >
  > This content is free to use in personal, educational and commercial projects.
  >
  > Support me by crediting Kay Lousberg, www.kaylousberg.com (this is not mandatory)

  (The file continues with a Patreon thank-you, a supporter list and social links; it carries no further terms.)

- Files copied (pack file names kept; from `addons/kaykit_character_pack_skeletons/Characters/gltf/` unless noted):
  `Skeleton_Warrior.glb` (Choir Acolyte, both Sundered Twins), `Skeleton_Mage.glb` (Choir Cantor), `Skeleton_Minion.glb`
  (Paired Warrior), `Skeleton_Rogue.glb` (the Seed) and `addons/kaykit_character_pack_skeletons/LICENSE.txt`. Each .glb
  embeds its own `skeleton_texture` image and the 95 Rig_Medium plus skeleton clips (`Spawn_Ground_Skeletons`,
  `Death_C_Skeletons` among them), so the pack's separate `skeleton_texture.png` and weapon models are not used. The Hollow
  Warden boss reuses the Adventurers `Knight.glb` above (no copy).
- Local additions (ours, not the pack's): the Godot `.import` sidecars (embedded images kept uncompressed,
  `gltf/embedded_image_handling=3`; light baking off), the same settings as the Adventurers sidecars.
