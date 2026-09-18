# 06 — Encounter and Boss Design

## 1. The question

What makes trash pulls and bosses exciting in a 5-player co-op dungeon, how do you build a mechanic
vocabulary more creative than Blizzard's own, and how do you design trash, pacing, and difficulty
scaling so a 20-30 minute run stays tense without becoming a numbers-inflation contest?

**Client-shape note for this brief:** per the 2026-09-18 design update, Keyrun's client is a 3D MVP
(Three.js, third-person camera, WASD + mouse-look, placeholder geometry) on a flat arena — not
top-down 2D. Positional mechanics (facing, cones, cleaves) are evaluated below against that shape:
the sim already tracks facing for frontal cones per `sim/CLAUDE.md`'s 3D update, so cone/cleave
telegraphs are cheaper here than in a 2D game, while classic top-down "read the whole arena at a
glance" telegraphs (FFXIV's floor markers) need a camera-aware adaptation, covered in §4.

## 2. What the best games do

### 2a. Boss mechanic vocabulary

| Mechanic | Game | What it does | Why it works | Source |
|---|---|---|---|---|
| Priority-interrupt casts | WoW | Multiple mobs/bosses cast at once; only specific casts (heal, silence, enrage-buff) are worth a scarce kick, forcing real-time triage of *which* cast matters most | Turns a binary "press kick" button into a continuous judgment call under time pressure — the interrupt itself is trivial, the *decision* is the content | [BoostRoom, CC & Kill Orders](https://boostroom.com/blog/heroics-for-beginners-crowd-control-kill-orders-and-survival) |
| Dispel | WoW | A support-tagged ability strips a buff/shield from an enemy or a harmful effect from an ally mid-fight | Gives non-healer/non-tank roles an active, timed responsibility beyond raw damage, so every class has a "my job right now" moment, not just a DPS rotation | [Red Bull, WoW terminology glossary](https://www.redbull.com/gb-en/world-of-warcraft-terminology-raid-glossary) |
| Soak | WoW | A raid-wide or large AoE hit is reduced per player standing inside it; too few players inside = someone takes lethal damage | Converts "stand still" into a headcount coordination problem the group solves together, not an individual dodge check | [WoW Rookie lexicon, Engadget](https://www.engadget.com/2011-06-16-wow-rookie-a-raid-rookies-lexicon-of-raiding-language.html) |
| Spread | WoW | Random players are marked and must move apart before a personal AoE detonates near them | Punishes clumping, which forces constant spatial awareness instead of parking on one "safe" tile for the whole fight | [Red Bull, WoW terminology glossary](https://www.redbull.com/gb-en/world-of-warcraft-terminology-raid-glossary) |
| Stack | WoW | The inverse of spread: players must group tightly to split incoming damage evenly | The same spatial-awareness pressure as spread, but the *opposite* motor response — alternating the two inside one fight prevents players from running on autopilot | [Red Bull, WoW terminology glossary](https://www.redbull.com/gb-en/world-of-warcraft-terminology-raid-glossary) |
| Tank swap | WoW | A stacking debuff on the tank makes them die if hit again; a second tank must intercept aggro before the stack refreshes | Makes the tank role a duet, not a solo job, and gives a second class a scripted moment of individual responsibility inside a shared fight | [Red Bull, WoW terminology glossary](https://www.redbull.com/gb-en/world-of-warcraft-terminology-raid-glossary) |
| Add priority | WoW | Reinforcements spawn with different threat levels (a healer add outranks a melee add); the party must retarget mid-fight instead of cleaving everything evenly | Tests whether the group can break its default rotation on demand — the skill being tested is *target discipline*, not damage output | [gaaia.org, How Blizzard Designs Raid Encounters](https://gaaia.org/how-blizzard-designs-raid-encounters-behind-the-scenes/) |
| Enrage timer | WoW | The boss becomes unkillable (or lethal) if the fight runs past a fixed duration | Sets a hard floor on raw damage output so a boss can't be "turtled" through with pure caution, forcing a caution/aggression trade-off every pull | [Wowpedia, Enrage (mechanic)](https://wowpedia.fandom.com/wiki/Enrage_(mechanic)) |
| Positional cleave / frontal cone | WoW | A boss's melee swing or breath attack only hits a facing arc; standing behind or to the side avoids it entirely | Rewards spatial reasoning about *orientation*, not just distance — cheap to telegraph (a wedge, not a whole-arena shape) and directly maps onto Keyrun's 3D facing model | [gaaia.org, How Blizzard Designs Raid Encounters](https://gaaia.org/how-blizzard-designs-raid-encounters-behind-the-scenes/) |
| Ground-plane AoE telegraph | FFXIV | Attacks are preceded by a colored decal on the floor (usually orange) showing exactly where damage will land, with time to read and move before it resolves | Separates *knowledge* (where is the damage) from *execution* (can you move there in time), so failure reads as a skill gap, not bad luck — though color choice is itself a fairness lever (see §3) | [PCGamesN, Yoshida on AoE marker accessibility](https://www.pcgamesn.com/final-fantasy-xiv-a-realm-reborn/yoshida-interview-accessibility) |
| Counter window | Lost Ark | Specific boss attacks are preceded by a blue glow; a class with a counter skill can interrupt the attack for a big damage window if timed inside a short glow duration | A precision-timing check layered on top of positioning — success is binary and immediate, giving a sharp risk/reward spike distinct from a slow damage race | [gamepressure.com, Lost Ark: Stagger — what is it?](https://guides.gamepressure.com/lost-ark/guide.asp?ID=63480) |
| Stagger/poise bar | Lost Ark | A secondary bar under the boss's health fills from "stagger" tagged hits; emptying it locks the boss in a daze state that halts its attacks entirely | Gives the party a shared secondary objective besides raw DPS, so a class built for utility (not damage) still has a measurable, visible contribution | [MMOGAH, Lost Ark Mechanics Guide: Counter, Stagger, Weak Point](https://www.mmogah.com/news/lost-ark/lost-ark-mechanics-guide-counter-stagger-weak-point) |
| Breakbar / defiance CC check | GW2 | A boss is immune to hard crowd control until a visible bar (fed by any CC-tagged skill) is emptied, at which point a scripted vulnerability window opens | Turns crowd-control abilities — normally dead weight against boss-tier enemies — into a mandatory, trackable group contribution with a clear on/off state | [GW2 Wiki, Defiance bar](https://wiki.guildwars2.com/wiki/Defiance_bar) |
| Part break | Monster Hunter | Individual monster body parts (head, tail, wings) have separate HP pools; breaking one changes the monster's moveset, drops unique loot, or removes an attack pattern | Makes target selection spatial and tactical rather than "always hit center mass" — the reward structure openly wants players choosing *where* to aim, not just *when* | [GDC Vault, Monster Hunter: World Postmortem (Capcom, 2018)](https://www.gdcvault.com/play/1024981/-Monster-Hunter-World-Postmortem) |
| Puzzle / allocation boss | WoW | Dark Animus (Halls of Flesh-Shaping style encounter) requires players to distribute stacking essences across the party in a specific pattern before the boss can be damaged at all | Breaks the "everyone does their rotation" default entirely for one fight, proving the vocabulary can hold a fight with almost no combat in it and still be memorable | [GamesRadar+, How to build a better boss fight](https://www.gamesradar.com/boss-dissecting-mmo-boss-encounters/) |
| Environmental mechanic | GW2 | World bosses use terrain and map objects (cannons, updrafts, breakable walls) as part of the fight itself, not just backdrop | Widens the verb set beyond each class's own kit — a shared environmental tool is class-agnostic, so it can't be trivialized by one strong build | [Massively Overpowered, MMO Mechanics: Brilliant MMO boss mechanics](https://massivelyop.com/2015/08/02/mmo-mechanics-brilliant-mmo-boss-mechanics/) |
| Synergy-demanding combo mechanic | WoW | Professor Putricide's Festergut/Rotface (Icecrown Citadel) require interrupt classes to control gas stacks while other classes manage ooze positioning simultaneously — no single class's kit resolves the mechanic alone | The mechanic is unsolved by any one player being good; it is *only* solved by two different kits' tools landing in the same window, which is the direct precedent for Keyrun's field/finisher cross-class hooks (research/03) | [PCGamesN, Raid design lessons from WoW, GW2, and FFXIV](https://www.pcgamesn.com/mmo-raids) |

### 2b. Trash design

- **Pull size is the tank's core decision, not a fixed pack list.** In WoW Mythic+, the tank chooses
  how many packs to chain-pull before resetting cooldowns; bigger pulls clear faster but risk a wipe,
  and the "right" size shifts with healer mana and DPS burst available that pull. Source:
  [raider.io, The Weekly Route](https://raider.io/news/741-the-weekly-route-voidbound-tyrannical);
  [BoostRoom, CC & Kill Orders](https://boostroom.com/blog/heroics-for-beginners-crowd-control-kill-orders-and-survival).
- **Caster/melee mix forces a target-priority puzzle, not a tab-target cleave.** A pack of pure
  melee is a positioning problem only; adding casters with must-interrupt heals or silences means the
  party has to split attention between "where do I stand" and "who do I kick," and a well-designed
  pack varies that ratio pull to pull. Source: [BoostRoom, CC & Kill Orders](https://boostroom.com/blog/heroics-for-beginners-crowd-control-kill-orders-and-survival).
- **"Lieutenant" mobs — a named elite inside a trash pack that anchors the pack's threat level.**
  `[unsourced opinion]`: no single cited source names this pattern, but it's the generalized form of
  WoW's "priority caster" trash design (e.g. Coilfang Hate-Screamer-style silencers) — one mob the
  pack's difficulty is actually built around, with the rest as filler that dies to cleave regardless.
- **Pack composition as a route-planning puzzle, not a fixed obstacle.** Community M+ route tools
  (Mythic Dungeon Tools) simulate different pull orders against a required trash-percentage target,
  turning "which packs, in what order" into the same kind of optimization space as a speedrun route,
  planned before the run and adjusted live. Source: [raider.io, The Weekly Route](https://raider.io/news/741-the-weekly-route-voidbound-tyrannical);
  see also research/01 §2.4 for the pull-size/routing skill-expression claim.

### 2c. Pacing of a 20-30 minute run

A run this short only works if decision density stays high the whole way, not just at the boss.
WoW Mythic+ enforces this with a single continuous timer across the whole dungeon (not per-pull),
explicitly designed so the dungeon reads as "a single encounter," per game director Ion Hazzikostas —
every trash pull inherits the same stakes as the final boss instead of being a rest stop between real
content. Source (cited in research/01 §2.1): [Wowhead, Bellular interview with Ion Hazzikostas](https://www.wowhead.com/news/bellular-patch-10-1-interview-with-ion-hazzikostas-adding-new-mythic-affixes-and-331833).
The practical pacing consequence for a 20-30 minute Keyrun run: 3-5 trash "beats" of escalating
decision complexity (see §5 proposal) feeding one boss, with no beat long enough for attention to
lapse — a single continuous timer, not per-room ones, so a slow trash pull has the same felt cost as
a slow boss attempt.

### 2d. Difficulty scaling that isn't just more HP

- **WoW's own affix system was redesigned specifically to stop meaning "more numbers."** The War
  Within's Xal'atath's Bargain affixes replaced pure-penalty modifiers with ones that also grant a
  player-usable buff, an explicit design change to make the weekly modifier something to "opt into
  using well," not just survive — Blizzard treating raw stat inflation as a design failure to correct
  away from. Source: [MMO-Champion, Affix System Updates in The War Within](https://www.mmo-champion.com/content/12398-Affix-System-Updates-in-The-War-Within)
  (cited in research/01 §2.8). Community analysis of the same scaling curve confirms per-level health/
  damage scaling was explicitly *reduced* (from 10% to 7% per level through the mid tiers) once
  Blizzard judged flat stat scaling was crowding out mechanical difficulty as the real lever. Source:
  [Kaylriene, Blizzard's Announced Dungeon Difficulty Changes — An Analysis](https://kaylriene.com/2024/03/12/blizzards-announced-dungeon-difficulty-changes-an-analysis/).
- **GW2's Agony is a scaling knob that is explicitly a gear check, not a skill check, and the game
  treats that as a deliberate boundary, not the main difficulty lever.** Agony (fractal content)
  deals rising percent-max-health damage per scale tier, mitigated only by Agony Resistance
  infusions equipped on gear — it exists to gate content by investment, separate from the
  mechanics-based difficulty of dodging attacks, avoiding one-shots, and breakbars. Source:
  [GW2 Wiki, Agony](https://wiki.guildwars2.com/wiki/Agony).
- **Lost Ark's tiered difficulty (Normal/Hard and progressive raid Gates) escalates mechanical
  complexity and count, not just numbers** — higher tiers add new attack patterns, more
  simultaneous stagger/counter checks, and stricter positional requirements on the same boss kit,
  with developers framing higher-tier encounters as teaching "more complex mechanics," not bigger
  health bars. `[unsourced opinion — general community consensus, no single primary-source quote
  located]`: the pattern is consistent across every Lost Ark raid gate, but no developer interview
  was found stating this as explicit policy in the sources checked for this brief.

## 3. What players hate

1. **Unreadable telegraphs, especially color/contrast failures.** FFXIV's ground AoE markers are
   locked to orange with no recolor option, and a mostly red/orange boss arena (Pandemonium
   Asphodelos: The Third Circle Savage) drew direct criticism for making the telegraph nearly
   invisible against its own background — the mechanic was fair in theory and unreadable in
   practice. Source: [PCGamesN, Yoshida on AoE marker accessibility](https://www.pcgamesn.com/final-fantasy-xiv-a-realm-reborn/yoshida-interview-accessibility).
2. **One-shots without a tell.** Design consensus across boss-design retrospectives is that any
   attack capable of one-shotting a player needs a visually distinct, in-fiction-readable warning —
   an unavoidable or unreadable one-shot reads as the game cheating, not the player failing.
   `[unsourced opinion — synthesized from general design-discourse search results, no single
   authoritative interview located]`.
3. **Mechanics that punish the whole group for one player's mistake.** WoW's Mythic+ death timer
   penalty (subtracting seconds from the run clock per death) was repeatedly called out as
   disproportionate because a single mistake by one player — often a role more exposed to
   mechanics, like melee or healers — could singlehandedly fail an otherwise clean run for
   everyone; Blizzard eventually removed it at low key levels in direct response. Source (cited in
   research/01 §3.1): [Blizzard Forums, "15s Death Penalty in M+ needs to go"](https://eu.forums.blizzard.com/en/wow/t/15s-death-penalty-in-m-needs-to-go/541746);
   [Icy Veins, penalty removed at Mythic 2/3](https://www.icy-veins.com/wow/news/no-more-death-time-penalty-for-mythic-2-and-3/).
4. **Interrupt/kick collision in uncoordinated groups.** With multiple classes able to interrupt the
   same cast and no in-game signal of "who's got this," pickup groups routinely overlap kicks on
   cooldown-gated abilities, wasting the tool and leaving the party with no interrupts for the next
   dangerous cast — a coordination failure the base game gives no tool to prevent. Source (cited in
   research/03 §3.3): [tech.yahoo.com, Fellowship solved the interrupt UI problem](https://tech.yahoo.com/gaming/articles/fellowship-just-solved-ui-problem-155902850.html).
5. **Exclusionary modifiers — a difficulty lever only some classes/roles can play around.**
   Blizzard's own stated reason for retiring several Mythic+ affixes (Sanguine, Bolstering, Raging,
   and others) was that "applying bonuses unevenly to players feels exclusionary regardless of
   overall impact" — a difficulty knob that privileges specific kits is treated by the affix's own
   designer as a design failure, not spice. Source (cited in research/01 §3.2): [MMO-Champion,
   Affix System Updates in The War Within](https://www.mmo-champion.com/content/12398-Affix-System-Updates-in-The-War-Within).

## 4. Steal / adapt / avoid

Constraint set: ≤5 rotational abilities, ≤10 keybinds total, synergy-first, deterministic sim, **3D
third-person client with facing/frontal-cone support** (updated from top-down 2D per 2026-09-18
design change), no art.

**Steal**
- **Priority-interrupt casts and the scarce, shared kick** — already load-bearing in Keyrun's class
  design (research/03's Cinder kick), and directly reuses the WoW-proven "who kicks this" live
  decision without costing an extra keybind.
- **Soak/spread/stack as a pair, not solo mechanics** — cheap to implement as radius checks against
  the sim's cylinder/AABB collision (per the 3D sim update), and forcing the party to alternate
  spread and stack behavior inside one fight is the single cheapest way to defeat "stand on one safe
  tile" autopilot.
- **Positional frontal cones** — the highest-leverage steal specifically *because* of the 3D pivot:
  the sim already tracks boss facing for cleaves, so a cone telegraph is a native geometry check
  (angle between boss forward vector and player position), not a bolted-on shape. This is strictly
  cheaper in 3D-with-facing than it would have been in top-down 2D.
- **Ground-plane AoE telegraphs, rendered as flat decals on the arena floor.** A circle/cone/line
  decal reads identically whether the camera is top-down or third-person — it's a floor texture, not
  a UI element — so this steals cleanly across the client-shape change. Use a fixed, high-contrast
  color (not FFXIV's orange-on-red failure mode from §3.1) and never rely on color alone (pair with
  shape/edge outline) for the accessibility reasons in that same citation.
- **Synergy-demanding combo mechanics (Festergut/Rotface model) as the boss-design default, not the
  exception** — every proposal boss should have at least one mechanic that only resolves when two
  different classes' kit tags land in the same window, directly extending the field/finisher hooks
  already designed in research/03.

**Adapt**
- **Breakbar/stagger as a shared secondary bar, simplified to one state** — Keyrun's low ability
  count can't support Lost Ark's multi-tier stagger intensity system; adapt it to a single
  fill/empty bar fed by any "CC-tagged" ability (reuses the field/finisher tag system from
  research/03 rather than adding a new mechanic type).
- **Part breaks, adapted as boss "weak state" windows rather than separate HP pools** — tracking
  per-part HP pools is state the deterministic sim would have to replicate exactly on every replay;
  cheaper and equally readable is a boss that telegraphs a temporary vulnerable state (bonus damage
  window) when a specific class-tagged ability lands, keeping Monster Hunter's "aim matters" reward
  without a second HP-pool system.
- **Puzzle/allocation bosses, scaled down** — Dark Animus's full essence-allocation puzzle assumes a
  20-40 person raid's worth of simultaneous state; a 5-player Keyrun boss can borrow the *shape*
  (a non-combat puzzle beat gates damage) at much smaller scale — see the proposal below.

**Avoid**
- **FFXIV-savage-style layered choreography (5+ simultaneous telegraph types stacked on one boss).**
  A ≤5-ability, ≤10-keybind kit has no spare attention budget for tracking that many concurrent
  telegraphs; Keyrun bosses should telegraph one or two mechanic types at a time, sequenced, not
  layered.
- **Class-exclusive mechanic gates (Lost Ark's counter-skill-only classes).** If only specific
  classes can ever resolve a mechanic type, that contradicts the synergy-first, no-hard-trinity
  stance already set in research/01 and research/03 — mechanics should require *a* class with a
  given tag, never *one specific* class by name.
- **Enrage timer, or any single numeric scaling knob, as the primary difficulty lever.** Both
  Blizzard's own affix redesign (§2d) and GW2's explicit separation of Agony (gear check) from
  mechanical difficulty (skill check) treat "just bigger numbers" as the difficulty axis to
  de-emphasize, not lean on — this also follows directly from project invariant 2 (fun is measured,
  not assumed) and invariant 3 (constraint is the design).
- **A group-wide punishment for one player's mechanic failure with no mitigation path**, per §3.3 —
  any Keyrun mechanic that can fail the whole pull/boss from a single player's single mistake needs
  either a party-level save (a Chanter-style Cleanse/shield window) or a graduated penalty, not an
  instant hard fail.

## 5. Proposal — one dungeon (3 pulls) and one boss `[PROPOSAL]`

Uses the Warden/Cinder/Chanter class kit from research/03 (Stone/Fire-Frost/Water-Light fields,
Cinder's `Detonate` universal finisher, Chanter's `Cleanse` dispel, each class's single scarce
interrupt/utility slot).

**Pull 1 — "Caster mix" (teaches priority-interrupt).** A small pack: 2 melee trash + 1 caster
lieutenant `[unsourced opinion: lieutenant-mob framing per §2b]` whose heal cast must be kicked or
the pack becomes unkillable in the timer. Tests: target priority, scarce interrupt discipline. Low
positional demand — this pull exists to teach the kick economy before anything else is layered on.

**Pull 2 — "Spread/stack pair" (teaches spatial alternation).** A medium pack of 4-5 melee that
periodically marks one player for a spread detonation, then 10 seconds later calls a stack (heavy
group AoE, damage split by headcount). Tests: reading the telegraph type and moving to the *correct*
formation, not just moving. Introduces the ground-plane decal telegraph pattern from §4 at low
stakes.

**Pull 3 — "Field pre-seed" (teaches the boss's core mechanic early).** A pack whose elite drops a
Stone-field-equivalent hazard patch on death that persists if not detonated; any class's
finisher-tagged ability clears it for bonus loot, foreshadowing the boss's required combo. Tests:
optional-objective awareness under the run timer (worth doing, not required to clear).

**Boss — "The Sundering Warden" (elite reskin of a Warden-type enemy, no new art needed: bigger
scale + palette shift per project's shapes-and-color-only constraint).** Three mechanics:

1. **Frontal cleave (positional, native to 3D facing).** A slow wind-up melee swing hits a ~120°
   forward cone; the boss's facing is visible from its model orientation (no separate arrow needed —
   this is exactly what the sim's facing-tracking update makes cheap). Players must be behind or to
   the side of the boss when it resolves. Punishes standing still more than it punishes bad
   positioning — recoverable by any player who reacts to the wind-up animation.
2. **Sundered adds (add priority + stagger-lite).** Two adds spawn with a shared "Sundered" bar (the
   §4-adapted single-state breakbar); any CC/interrupt-tagged ability against either add drains the
   shared bar, and emptying it staggers both adds for a burst window. Tests add-priority triage
   (which add to focus) plus the shared-bar cooperative-CC pattern adapted from GW2's defiance bar.
3. **Field-lock core (synergy-demanding, requires two classes) `[the two-class-cooperation
   mechanic]`.** At 50% HP, the boss shields itself and drops a **Stone field** patch under its own
   feet (a Warden-family ability, thematically consistent) that must be detonated to break the
   shield — but a Warden alone cannot both hold aggro/positioning *and* land the finisher inside the
   3-second field window. Only a **Cinder's `Detonate`** landing on that Stone field breaks the
   shield; if no Cinder is present or in range, a **Chanter** can substitute by chaining `Cadence`
   (Water field) directly under the boss first and calling for *any* finisher-tagged ability instead
   — giving two viable two-class combinations (Warden-seeds-are-not-needed since the boss seeds its
   own field; the real requirement is "a field exists" + "a finisher lands on it," which by
   research/03's design can only be satisfied by at least two different classes' kits landing in the
   same 3-second window, directly instantiating the Festergut/Rotface-style synergy-demanding
   mechanic from §2a).

## 6. Verdict

Build Keyrun's boss vocabulary from the field/finisher synergy hooks already locked in research/03,
not from raw mechanic count: every boss should sequence one or two mechanic types at a time (never
FFXIV's layered five), always telegraph on the ground plane in high-contrast shape-plus-color decals
(never color alone), and gate at least one mechanic per boss behind two different classes' tags
landing in the same short window — that is Keyrun's answer to "more creative than Blizzard's
vocabulary": the vocabulary itself (interrupts, soak/spread/stack, breakbars, cones) is proven and
should be stolen outright, but the *combination requirement* is the novel layer. Trash pulls should
scale mechanic density, not health, across a run's 3-5 pulls, and no mechanic may hard-fail the whole
group from one player's single mistake without a class-level save available.
