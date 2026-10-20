# 03 — Ability & Class Design

## The question

How do you design classes with at most 5 rotational abilities and 10 total keybinds that are still deep, and
make class SYNERGY (not individual rotation complexity) the point?

## What the best games do

1. **Diablo IV — a hard 6-slot skill bar forces build-defining trade-offs instead of "press every button."** No
   way to bind more than 6 skills; the Druid must fill one slot per category (Basic/Spirit/Defensive/Wrath/
   Companion/Ultimate), turning bar construction into the depth, not rotation length. Source:
   [GameRevolution](https://www.gamerevolution.com/guides/940842-diablo-4-get-more-than-six-skill-bar-slots-hot-keys);
   [Wowhead](https://www.wowhead.com/classic/news/diablo-iv-skill-trees-explained-re-specs-depth-of-customization-sense-of-329742).

2. **WoW Rogue — combo points make a builder/spender loop a whole class identity in two button-types.** Generators
   (Sinister Strike) bank points; finishers (Eviscerate, Kidney Shot) spend them, payoff scaling with points
   banked — "when do I stop building and cash in" as a constant, replayable decision on ~2 ability classes.
   Source: [MMORPG.com forums](https://forums.mmorpg.com/discussion/474986/builder-spender-core-mechanic-pantheon-class-design);
   [WoWWiki, Combo point](https://wowwiki-archive.fandom.com/wiki/Combo_point).

3. **Guild Wars 2 — combo fields + finishers turn five skills per player into party-scale combinatorics.** A
   *field* skill (fire/water/light/poison) occupies ground; a *finisher* skill (blast/leap/projectile/whirl)
   triggering in it produces an effect that depends on the field — heal-on-blast in water, burn-on-blast in
   fire. No class needs more abilities; depth is emergent from which classes stack which fields/finishers
   together — exactly the "synergy is the point" thesis. Source: [GW2 Wiki, Combo](https://wiki.guildwars2.com/wiki/Combo);
   [Snow Crows](https://snowcrows.com/guides/new-player-guides/combat/how-to-use-combo-fields-and-finishers).

4. **Lost Ark — 8 skills, but 3-tier mutually-exclusive tripods per skill put the depth in modification, not slot
   count.** Players can't fully tripod every skill, so the decision is "which of my buttons do I invest in and
   how do I reshape it," not how many buttons exist — the strongest precedent for talents that retag a fixed
   kit instead of adding buttons. Source: [Fextralife, Tripods](https://lostark.wiki.fextralife.com/Tripods);
   [Dignitas](https://dignitas.gg/articles/how-to-utilize-the-skill-tree-and-tripods-system-in-lost-ark).

5. **Deep Rock Galactic — four classes, one traversal + one support tool each, tuned so any class covers for a
   missing one but never replaces it.** Scout lights/mines with flare gun + grapple; Engineer places platforms
   and turrets; Driller tunnels and crowd-controls; Gunner defends and zip-lines. Explicit design target: each
   tool "flexible enough to cover another's slack but not... overpowered enough to render another useless."
   Source: [Overanalyzing overclocks](https://ljvmiranda921.github.io/notebook/2022/12/02/drg/).

6. **Hades — weapon Aspects reskin a fixed 4-verb kit (Attack/Special/Cast/Dash) into radically different
   playstyles without adding buttons.** Verb count stays constant; build depth comes entirely from boon and
   Aspect modifiers layered on the same four inputs. Source: [Supergiant Games blog](https://www.supergiantgames.com/blog/7/);
   [GDC Podcast ep.16, Kasavin on Hades](https://gdconf.com/article/roguelikes-and-narrative-design-with-hades-creative-director-greg-kasavin-gdc-podcast-ep-16/)
   (`[unsourced opinion]`: that the fixed-verb constraint was deliberate rather than emergent — the podcast
   doesn't state this outright).

7. **Risk of Rain 2 — a rigid 4-slot template (Primary/Secondary/Utility/Special) per survivor keeps every kit
   instantly legible while items, not buttons, carry the depth layer.** All 19+ survivors share the template;
   build depth is item-stacking on top of a constant control surface. Source:
   [RoR2 Wiki, Shared Design](https://riskofrain2.fandom.com/wiki/Shared_Design).

8. **FFXIV — the 2-minute raid-buff window makes "when parties pool cooldowns" the central synergy decision, at
   a flexibility cost.** After early expansions saw jobs struggle to align burst (Dragoon/White Mage) or sit
   undertuned outside alignment (Bard), Square Enix standardized nearly all raid buffs to a 2-minute cadence so
   every job bursts together — a real synchronization point, but cited by players as the single most
   restrictive part of job design. Source: [Square Enix Forums](https://forum.square-enix.com/ffxiv/threads/462826).

9. **WoW interrupts + Bloodlust — a scarce, shared binary ability becomes a coordination test.** "Kick"
   interrupts and locks a spell school 6s; because every class has only 1-2 interrupts, *who* kicks *which*
   cast among 3-5 players is the puzzle, not the button. Bloodlust/Heroism (~40s raid-wide haste, long cooldown)
   creates one high-value "when do we pop it" party decision every class plans its burst around. Source:
   [Wowhead, Kick](https://www.wowhead.com/spell=1766/kick); [Wowhead, Bloodlust](https://www.wowhead.com/spell=2825/bloodlust);
   [Wowpedia](https://wowpedia.fandom.com/wiki/Bloodlust_effect).

10. **Monster Hunter World — part-breaking rewards matching a small weapon-verb-set to anatomy, not more
    buttons.** No weapon is best at everything: hammers break heads, ranged breaks wings faster, blades sever
    tails, mounting damages whatever part the hunter rides. The "right tool" call is made once at loadout, then
    executed with a small fixed per-weapon kit. Source: [Fextralife, Partbreaker](https://monsterhunterworld.wiki.fextralife.com/partbreaker).

## What players hate

1. **Rotation-only depth goes rote.** Builder/spender "does tend to be prone to repetitive combat patterns" and
   can feel tedious once nothing outside the character (party, environment, target) changes the decision.
   Source: [MMORPG.com forums](https://forums.mmorpg.com/discussion/474986/builder-spender-core-mechanic-pantheon-class-design).

2. **Forced synchronization windows read as a straitjacket, not depth.** FFXIV players: the raidbuff window is
   "probably the most restrictive element in the game," and recent changes "force a 20 sec burst -> 1:40
   downtime" — a synergy mechanic mandatory for every class stops feeling like a choice. Source:
   [Square Enix Forums](https://forum.square-enix.com/ffxiv/threads/462826).

3. **Overlapping interrupts in uncoordinated groups feel bad, not skillful.** With several players able to
   interrupt the same cast, pickup groups routinely double up wasted kicks with no in-game signal of who
   called it — a gap other games have had to patch with UI (Fellowship). Source:
   [Yahoo Tech, Fellowship UI](https://tech.yahoo.com/gaming/articles/fellowship-just-solved-ui-problem-155902850.html).

4. **Holy-trinity role imbalance produces structurally long DPS queues, not a skill problem.** "The RPG Holy
   Trinity is bottlenecking the dungeon finder... most players don't want to play tank or healer." Community
   reports cite ~45-minute DPS waits against ~2-minute tank/healer waits in the same window. Overwatch's
   role-locked 5v5 hits the same math: "in a 5v5 lobby one in five players must be a tank, and in most
   communities fewer than one in five want to be" — tank has "always been the least popular role." Sources:
   [Blizzard Forums](https://us.forums.blizzard.com/en/wow/t/the-rpg-holy-trinity-is-bottlenecking-dungeon-finder/2184492);
   [Team Up Blog](https://teamupgg.com/blog/overwatch-2-role-queue-customs-discord).

5. **Tank responsibility overload compounds scarcity.** "Tank players often bear the responsibility of
   commanding the entire team... this leads to excessive pressure" — a scarce role that's also highest-blame
   self-selects an even smaller population. Source: [IGGM](https://www.iggm.com/news/wow-tbc-classic-anniversary-toxic-community-kill-tank-population-reason-for-long-group-queue-times).

6. **"Add buttons for depth" is a trap the genre keeps re-learning.** Lost Ark's 8-skill design and Diablo IV's
   hard 6-slot cap are explicit reactions against older MMOs' 20+-button bars; the genre has already tested
   "more buttons = more depth" and moved away from it at the top. Source:
   [GameRevolution](https://www.gamerevolution.com/guides/940842-diablo-4-get-more-than-six-skill-bar-slots-hot-keys).

## Steal / adapt / avoid

**Steal**:

- **GW2 combo fields/finishers** — tag a subset of each class's 5 abilities as a *field* (ground shape, color)
  or a *finisher*; any class's finisher reading another's field pays off. Readable in top-down 2D with shapes
  and color alone, costs zero extra keybinds — the single highest-leverage steal for "synergy is the point."
- **WoW-style scarce, shared interrupt** — one interrupt per class, one of the 5 rotational slots; boss casts
  need *any* interrupt but only one, making "who kicks this" a live call. Ship a visible party-frame cooldown
  readout from day one to avoid the overlap complaint above — free, no art budget.
- **Rogue-style builder/spender for one class only** — reserve it for one starter class so it reads as a
  distinct playstyle, not a mandatory layer on every kit.
- **Lost Ark tripods, shrunk** — talents modify what the 5 fixed abilities *do* (add a tag, change a cooldown)
  rather than granting new ones, preserving the cap forever while adding loadout-time depth.
- **Hades' fixed-verb + modifier-layer model** — ability count per class never changes across the game; power
  creep (talents, gear) modifies the existing 5 verbs, never adds a 6th.

**Adapt**:

- **FFXIV's synchronized burst window, optional not mandatory** — a party-wide cooldown-pooling bonus can exist
  as high-ceiling optional play, never balanced as required baseline (per the backlash cited above).
- **Deep Rock's "cover but don't replace" tuning** — apply to Keyrun's utility slot: a party missing a class
  should be harder, never impossible; no class's utility should trivially duplicate another's.
- **Monster Hunter's loadout-time matching** — reward *ability-tag* matching against boss/dungeon design (e.g.
  an armored segment wants a "sunder" tag from any class) instead of a tank/healer/dps checklist.

**Avoid**:

- **Hard trinity role-lock (1-of-5 tank/healer ratio)** — WoW dungeon finder and OW2 role-queue evidence is
  unambiguous: a mandatory scarce role produces long queues and burnout. `[unsourced opinion]`: even a *soft*
  1-healer requirement is probably too strict for a 3-5 person party — a missing soft-heal role should degrade
  a run, never block the queue.
- **A 6th rotational ability or 11th keybind to "fix" a shallow class** — every precedent above (Diablo IV,
  Lost Ark, Hades) gets depth from modifiers and interaction on a fixed small kit, never from more slots.
- **Repetitive builder/spender as every class's core loop** — sourced fatigue (item 1) caps this pattern at
  one or two starter classes; the rest should get decision density from synergy tags and cooldown timing.
- **Mandatory synchronized burst windows** — don't force all classes onto one shared cooldown cadence the way
  FFXIV's 2-minute meta does; it's cited by that game's own players as its most restrictive element.

## Concrete proposal — three Keyrun starter classes `[PROPOSAL]`

Each class: 5 rotational + 1 interrupt + 1 dispel/utility + 1 movement + 1 consumable + 1 defensive cooldown =
10 keybinds total. Talents may retag these abilities but never add a 6th rotational slot.

1. **Warden** (builder/spender bruiser). Resource **Grit**, built by landing hits, spent on one finisher.
   `Strike`/`Guard Break` (builders — Guard Break applies "Sundered," a synergy tag doubling the next ally
   finisher's damage on that target) / `Bulwark` (builder, drops a **Stone field**) / `Reckoning` (grit
   finisher) / `Taunt` (soft-tank utility). Synergy: Stone field + any ally blast/dash-through triggers a
   shared damage-reduction pulse (GW2 model); Sundered rewards call-outs, WoW-kick-style but for burst.

2. **Cinder** (heat/overheat caster — push-your-luck, not combo points). **Heat** rises while casting; at 100
   the next cast free-casts "Overheat" then dumps to 0 and Chills the caster (reduced cast speed, 3s).
   `Ember Bolt`/`Flare` (heat generators — Flare drops a **Fire field**) / `Cold Snap` (heat dump, **Frost
   field**, removes Chill) / `Detonate` (finisher: **blast**, consumes any field under it — fire = burning
   nova, frost = freeze nova) / `Kick` (interrupt; refunds 20 Heat on hit, tying the kick economy to the
   resource). Synergy: `Detonate` is the universal finisher any Cinder drops on *any* ally's field — the
   cross-class glue ability.

3. **Chanter** (soft-role support, not a hard-trinity healer). No meter — a **Verse** stance-dance: always in
   `Mend` or `Ward`, swapped by one ability, every other ability behaving differently per stance (Diablo-Druid
   category idea, no extra buttons). `Cadence` (Mend: heal + **Water field**; Ward: shield + **Light field**) /
   `Refrain` (stance swap, burst on swap) / `Chorus` (Mend: party heal pulse; Ward: party mitigation pulse —
   FFXIV-style burst tool, kept optional per Avoid) / `Cleanse` (dispel) / `Rally` (haste + brief
   root-immunity — protect-the-kicker tool). Synergy: Water/Light fields are built to be detonated by Cinder's
   `Detonate` or any future blast finisher — Chanter lays fields, Warden or Cinder finishes, party-wide payoff,
   with no mandatory shared cooldown and no locked role (Cadence's heal is rotational, not a role gate).

Cross-class read: Warden and Chanter both drop fields (Stone, Water, Light); Cinder drops a field and is the
dedicated finisher via `Detonate` — a 3-class group reads as "field-layers + one detonator," a soft-role shape
the GW2/Overwatch evidence above suggests avoids the queue-time and burnout failure mode of a hard trinity.

## Verdict

Cap every class at 5 rotational abilities forever; get depth from three orthogonal levers instead of button
count — (1) a resource loop unique per class (builder/spender, heat/overheat, stance-dance, never repeated, so
each class feels structurally different on the same 5-slot chassis), (2) a GW2-style field/finisher tag on a
subset of abilities so synergy is drawn on the ground in shapes and color at zero art and zero keybind cost,
and (3) one scarce, shared interrupt per class with a visible party cooldown readout so "who kicks this" stays
a live decision, not an addon problem. Reject hard trinity role-lock: WoW dungeon-finder and Overwatch 2
role-queue evidence shows a mandatory 1-tank/1-healer ratio produces long queues and burnout, so Keyrun's 3-5
person party should use soft roles (any class can layer a field; one class is the dedicated finisher) where a
missing "healer" degrades a run rather than blocking the queue. Never add a 6th ability or 11th keybind to fix
a shallow class — depth comes from modifiers and cross-ability interaction on a frozen small kit, per every
precedent cited (Diablo IV's 6-slot cap, Lost Ark's tripods, Hades' fixed 4-verb kit), never from more buttons.

Addendum 2026-10-20: the Outlaw Rogue toolkit (STATE.md D31) moved to research/12-skill-toolkit.md.
