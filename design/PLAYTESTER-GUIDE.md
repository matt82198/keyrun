# Playtester modes: guided and blind (R-PIVOT-1 amendment e)

PURPOSE: specify keyrun's two bot playtester modes, guided (bots follow a per-class guide and are the only runs that
judge balance, pacing and feel) and blind (bots learn from nothing and report time-to-learn), with the tank guide in
full, so implementation lanes can build them; status: real (design only, no code in this document's PR).

Matt, 2026-10-22, verbatim:

> "maybe the playtesters need a guide, not completely blind learning because they develop methods to cheese and not
> optimally play, which is a problem for testing lmao"

> "the blind testers are more useful in how long it will take to learn"

**What already exists.** design/PLAYTESTERS.md (STATE.md D33) is the blind design: learners that start from a
player-side guide with no answers, fail first, and learn through memory with receipts, driving `tools/Keyrun.Playtest`
(`Policy`, `PolicyBot`, `Hands`, `KeyLedger`, `PlaytesterMemory`). It stays as written; this document adds the guided
mode beside it and changes what each mode is allowed to conclude.

---

## 1. The two modes

| | GUIDED | BLIND |
|---|---|---|
| Knows | The per-class guide (s3): rotation with reasons, kick duties, telegraph responses, never-cast rules | PLAYTESTERS.md s1 only: no enemy names, no route, no numbers |
| Plays | The guide, through `Hands` (seeded reaction delays and fumbles), so it plays like a good human, not a perfect one | Its own plan file, revised run by run from its own summaries |
| Judges | Balance, pacing and feel: fork parity, tree tuning, par margins, boss length (R-BOSS-1), deaths per pull, kick coverage | Nothing about balance |
| Reports | The summary fields PLAYTESTERS.md s2 lists, plus the guided benchmark (s2.1) | One number: **time-to-learn** (s2.2) |
| Cheese | A guide never contains a cheese (s3.6); a guided run that finds one is a defect in the guide | A blind strategy that beats the guided benchmark by a wide margin is filed as an **exploit finding** for design, never as a balance verdict |

**Why the split.** A blind learner optimises the score it sees, so it finds what the rules allow, not what the design
intends: Matt saw packs pulled "one at a time" (STATE.md D64) and learners converge on cheeses. Balance judged on
cheese is wrong data. A guided bot plays the intended game, so its numbers measure the design. The blind learner's
real value is how long a stranger takes to reach that intended play, which is the test-3 question "strangers are chill
by default" asks of the onboarding (BIBLE s0).

**The rule, mechanical.** Every summary carries `mode: "guided" | "blind"`. The fork parity check (BIBLE s6), the tree
tuning (PROGRESSION-PIVOT.md s1.4) and any par or boss-length measurement read only `guided` rows and fail closed on a
`blind` row. The playtest report script refuses to print a balance line from a blind row.

---

## 2. What each mode reports

### 2.1 The guided benchmark

For one (build sha, dungeon, key, held-out seed set): three guided parties (each seat its class's guide, all trees
fully unlocked, bot default gear) play the held-out seeds. The benchmark is two numbers taken from those rows:

- `guidedTimedRate`: the share of held-out seeds the guided party timed.
- `guidedMedianClearTicks`: the median completion tick over the timed seeds.

It is recomputed whenever the build sha changes; rows from different builds are never pooled (PLAYTESTERS.md s2).

### 2.2 Time-to-learn (blind)

**Definition.** A blind learner's **time-to-learn** is the total number of sim ticks it has played, summed over every
run of its own history (training seeds, all results, wipes and timer-cap ends included), up to and including the first
batch after which its current plan, played on the same held-out seeds at the same key and build, **matches the guided
benchmark**:

- its held-out timed rate is at least `guidedTimedRate - 0.10`, and
- its held-out median clear is at most `guidedMedianClearTicks x 1.10`.

It is reported in ticks and, for reading, in hours of play (ticks / 20 / 3600). The run count at the match is recorded
in the same row as context, never as a second verdict. A learner that has not matched by the PLAYTESTERS.md s3 cap of
15 batches reports `> <ticks played>` and the word `unmatched`.

**Why ticks and not runs.** A run can end in 3 minutes or 40 (wipes, run-backs, the timer cap at 2x par), so a run
count hides how long a stranger actually plays before getting it. Ticks are play time.

**Why the 10% margins.** `Hands` gives every seat seeded reaction delays and fumbles, so two guided parties on one seed
set already differ; the margins keep a learner that plays as well as the guide from reading as unmatched on noise. The
margins are this design's own and are re-measured as the spread between the three guided parties once those rows
exist.

**What a blind run never reports:** a balance verdict, a "this fork is better" line, a par recommendation, or a feel
judgment. A blind plan that beats the benchmark by more than the margins (timed rate higher by 0.20, or median clear
under 0.80 x) is filed as an exploit finding with its plan and receipt, for design to read.

---

## 3. The tank guide (Warden), in full

Written from main's abilities (`content/data/abilities/warden.json`, `content/data/effects/player.json`,
`content/data/classes/warden.json`) and the bot thresholds already on main (`game/bots/Keyrun.Bots/Roles/Tank.cs:18-21`,
`TankPressure.cs:18-21`). Ticks are at 20 Hz. "Pressure" means three or more living enemies aimed at the Warden
(`TankPressure.PressureAttackers`).

### 3.1 The kit, and why each button exists

| Key | Ability | Numbers on main | What it is for |
|---|---|---|---|
| 1 | Strike | 80 damage, +15 Momentum, GCD 30, Sundered Window 60 ticks on target | The Setup: opens **Sundered** for the Cinder (Shatter: burst and a 1 s stun, which also stops a cast) and the Chanter (Consecrate: burst and a heal pulse) |
| 2 | Guard Break | 100 damage, +15 Momentum, 140/1024 cone (about 49 degrees), threat 150% | The Payoff and the cleave: more damage than Strike, hits the pack, closes an ally's Window |
| 3 | Bulwark | 20-tick cast, 200 cooldown, 2500 mm floor patch, Braced (-15% damage taken) for 160 ticks | Group mitigation under pressure, and a floor Window any ally's Payoff can close |
| 4 | Reckoning | Costs 100 Momentum, 400 damage, threat 200% | The Payoff finisher: the big hit, best on an ally's Window |
| 5 | Vigil | Costs 50 Momentum, 120 cooldown, Vigil Shield (-30%) for 100 ticks | The sustain button: mitigation the Warden earns by hitting |
| Q | Shield Bash | 480 cooldown (24 s), interrupt, control | One kick per 24 s: the kick ledger (3.3) |
| E | Brace | 300 cooldown, -40% for 60 ticks | The "press it for the big hit" cooldown (BIBLE s3.1) |
| R | Taunt | 160 cooldown, threat x10 on the target | Picks up one loose enemy |
| F | Charge | 2 charges, 200 cooldown, 8000 mm line, 80 damage and a 20-tick stun | Engage and gap close |

Passive armor removes 35% of every hit (BIBLE s3.1; `run.json` `classArmorBp`). Brace, Vigil, Bulwark and armor
multiply: the full stack takes 9.75% of a hit, never 0.

### 3.2 Rotation, in priority order

The bot evaluates these top to bottom every decision and takes the first that applies. Each line carries its reason so
a guided summary can say which rule fired.

| # | Press | When | Why |
|---|---|---|---|
| 0 | (wait) | Before the first hit on a new pack: the human seat is farther than 8000 mm (the aggro radius, `run.json` `enemyAggro.aggroMm`) | R-BOTTANK-LEASH-1: a bot tank never pulls a pack before the human seat can reach it |
| 1 | Q Shield Bash | An interruptible cast the kick ledger (3.3) gives the Warden is inside its last 6 ticks, or is the Warden's first-kicker cast and Shield Bash is ready | The named kick window is the last 6 ticks (BIBLE s3); a kick is judged on its intended tick |
| 2 | E Brace | A telegraphed tank hit (3.4) resolves within 20 ticks on the Warden and cannot be faced away or stepped out of; or health at or under 35% | Brace is for the big hit; 35% is the bot's hard defensive floor (`Tank.DefensiveThresholdPct`) |
| 3 | 5 Vigil | Momentum at least 50, Vigil Shield not up, and either under pressure with Brace down or within 30 ticks of ending, or health under 60% | Chains mitigation after Brace (`TankPressure.BraceExpiryLeadTicks` 30; `Tank.SustainThresholdPct` 0.6) |
| 4 | 3 Bulwark | Under pressure, Braced not up, not standing in a telegraph, party within 2500 mm of the Warden's feet | The ring protects everyone in it and gives allies a floor Window to close |
| 5 | 4 Reckoning | Momentum 100, and the target carries an ally's Window (Kindled: Forge refunds 40 Momentum; Attuned: Anthem gives the party 15% haste), or no Window is coming within 60 ticks | The biggest hit is worth most as a Payoff |
| 6 | 2 Guard Break | Two or more enemies in the cone, or the target carries an ally's Window | More damage than Strike on any pack, and a Payoff |
| 7 | 1 Strike | Otherwise, on the kill target (the party's focus, else the lowest-health enemy the Warden holds) when it carries no Sundered | Opens Sundered for the Cinder's stop and the Chanter's heal |
| 8 | 2 Guard Break | Otherwise | Filler: it out-damages Strike once Sundered is already up |

Utility, not in the GCD rotation:

- **R Taunt** only an enemy aimed at another seat that the Warden does not hold, and never one already held (a wasted
  160-tick cooldown).
- **F Charge** to engage the pack the route pulls, after rule 0 clears, or to reach a loose enemy on the healer. Keep
  one charge for that.

### 3.3 Kick ledger duties

Three seats, three kicks per 24 s. The Warden takes casts from enemies in its melee or aimed at it, because it faces
them and is in range (3000 mm); the Cinder takes ranged casters first (Quench refunds 20 Heat on a kick); the Chanter
backs both and kicks what threatens the healer. Order is first kicker, then backups.

| Dungeon | Cast (id) | What it does | Kick order | Why |
|---|---|---|---|---|
| Hollow Vault | `choir.healCast` Choral Mending | Heals the pack (80-tick cast) | warden, cinder, chanter | The Choir's lesson; the Warden stands on the Cantor (#515 kick on main) |
| Hollow Vault | `bellows.stoke` Stoke | Enemies within 4000 mm of the Keeper gain 30% damage and 20% cast speed | warden, cinder, chanter | Stoked enemies are the ones hitting the Warden |
| Hollow Vault | `ash.searingHex` Searing Hex | 25% max health burn over 6 s, dispellable | cinder, chanter | A missed kick costs an R (Purge or Cleanse), not a death; the Warden keeps its kick for Stoke |
| Hollow Vault | `drowned.undertow` Undertow | Pulls its target 5000 mm and roots | chanter, cinder | It targets the backline; deadly in the Sluice before a jet |
| Hollow Vault | `choir.patrolSweep` Patrol Sweep | The patrol's kickable cast | warden, chanter | The patrol walks onto the tank |
| Sunken Bellfoundry | `ommet.temper` Temper (boss) | Heals Ommet 2000 | warden, cinder, chanter | The boss faces the tank; an uncovered heal adds a long stretch of fight |
| Sunken Bellfoundry | `greatBell.resonance` Resonance (boss) | 125 damage and a stun in a circle | warden, chanter | Stops a party stun |
| Sunken Bellfoundry | `brineMother.drowningHymn` Drowning Hymn (boss) | 110 damage cone | warden, cinder | The cone comes at the tank |
| Sunken Bellfoundry | `furnaceThrall.stokeCore`, `scaldHound.boilingBlood` | Self-buffs on melee enemies | warden, chanter | They stand on the tank |
| Sunken Bellfoundry | `slagHexer.moltenBrand`, `brineMender.bronzeWard`, `pitTender.fanTheCoals`, `knellSinger.dirge` | Caster debuffs, wards and a pull | cinder, chanter, warden | Ranged casters; the Warden is last backup |

Rules that apply to every row: never kick a cast with the shield edge (uninterruptible); never double kick (read the
kick-ready pips, BIBLE s10); a Chanter Dissonance silence that ends a Choral Mending is good play, not a missed kick
(R-KICK-WARD-1). A cast no row names is not kicked, as PLAYTESTERS.md's plan rule has it.

### 3.4 Telegraph responses

| Cast (id) | Shape | Response | Fallback |
|---|---|---|---|
| `hollowWarden.cleave` Cleave | Slow frontal cone | Hold the boss facing away from the party; the Warden takes it | Brace (Cleave is 220 raw, 143 after armor) |
| `vault.crushingSlam` Crushing Slam | 90-degree, 3500 mm frontal cone, 40% max health | Turn the Brute away from the party (research/06:30) | Brace |
| `ingotBrute.anvilDrop`, `slagRam.moltenCharge` | Telegraphed hits on the tank | Step out if the shape allows and the pack stays faced away; otherwise take it | Brace |
| `pair.spreadMark` Spread Mark | Ring on a marked player | If marked, walk out to 3000 mm from every ally | none |
| `pair.stackCall` Stack Call | Circle | Stand with the party inside 1500 mm; the damage splits | none |
| Bellows Vent Gout, Sluice Floodjet (hazards) | Grate circles, wall cones | Drag the pack into the set about to erupt; stand the party on the idle set (BIBLE s9.4) | Brace if caught |
| `seed.lash` Seed Lash | Caster-to-target line | Step sideways out of the line | Vigil |
| Any telegraph not listed | Its floor shape | Step out if the shape covers the Warden and the step keeps the pack faced away | Brace, then Vigil |

### 3.5 When NOT to cast

- **No Bulwark while a telegraph covers you.** Its 20-tick cast pins you in the shape; move first.
- **No Reckoning that leaves Vigil unaffordable under pressure.** With Brace down and three or more enemies on you,
  Vigil (50) comes before Reckoning (100).
- **No Strike on a target that already carries an ally's Window.** Strike is a Setup and cannot close it; Guard Break
  or Reckoning can.
- **No Strike to refresh your own Sundered while it has more than 20 ticks left.** Guard Break does more damage.
- **No Charge into a pack the route skips,** and none before rule 0 clears (R-BOTTANK-LEASH-1).
- **No Taunt on an enemy you hold.**
- **No kick on a shielded cast, no double kick,** and no Shield Bash on Searing Hex while Stoke or Choral Mending is
  due inside the next 480 ticks.
- **No Brace on a hit you can face away or step out of.** It has a 300-tick cooldown, and the next telegraphed hit
  needs it.

### 3.6 What the guide forbids as cheese

The guided bot never leash-resets a pack by walking it past its leash radius (30000 mm, `run.json`
`enemyAggro.leashMm`) to drop its casts, never kites a boss out of its arena to dodge a mechanic, and never holds the
party outside a hazard room's trigger to skip the hazard while still pulling its pack. A guided run that does any of
these is a guide defect (the guide is fixed), not data.

---

## 4. The guide as data

A guide is a JSON file per class at `game/content/data/guides/<classId>.json` (the C#-only data tree, embedded under
the `content/data/` logical namespace; no csproj change). Bots load it when the run's mode is guided. The blind sandbox
already excludes `content/data/` (PLAYTESTERS.md s3 leak control 1), so a blind learner can never read a guide.

```json
{
  "classId": "warden",
  "version": 1,
  "forks": { "warden.strike": "rend" },
  "rotation": [
    {
      "press": "warden.shieldBash",
      "when": { "all": [ { "kickDuty": true }, { "castTicksLeftAtMost": 6 } ] },
      "reason": "The named kick window is the last 6 ticks (BIBLE s3)."
    },
    {
      "press": "warden.vigil",
      "when": { "all": [
        { "resourceAtLeast": 50 },
        { "effectNotUp": "bracedSelf" },
        { "any": [
          { "all": [ { "attackersOnSelfAtLeast": 3 }, { "effectTicksLeftAtMost": { "effectId": "braceSelf", "ticks": 30 } } ] },
          { "healthBelowPct": 60 }
        ] }
      ] },
      "reason": "Chain mitigation after Brace; 60% is the sustain floor."
    },
    {
      "press": "warden.strike",
      "when": { "all": [ { "targetIsKillTarget": true }, { "targetWindowNot": "sundered" } ] },
      "reason": "Open Sundered for Shatter and Consecrate."
    }
  ],
  "never": [
    { "press": "warden.bulwark", "when": { "insideTelegraph": true }, "reason": "The cast pins you in the shape." }
  ],
  "kicks": [
    { "cast": "choir.healCast", "order": [ "warden", "cinder", "chanter" ], "reason": "The Choir's lesson." }
  ],
  "telegraphs": [
    { "cast": "hollowWarden.cleave", "response": "faceAway", "fallback": "warden.brace", "reason": "Cleave is a frontal cone." }
  ],
  "engage": { "waitForSeat": 1, "withinMm": 8000, "reason": "R-BOTTANK-LEASH-1." }
}
```

The excerpt shows three of the nine rotation rules; the shipped file holds every row of s3.2-s3.5.

**Validation (fail closed, a load error naming the file and rule):**

- `press`, `fallback` and every `forks` key are ability ids of `classId`; `forks` values are option ids of that
  ability's fork (`content/data/classes/<class>.json` `talents`).
- `kicks[].cast` and `telegraphs[].cast` are enemy ability ids in content; a `kicks` cast is `interruptible: true`.
- `kicks[].order` holds class ids only, each at most once. The three class guides, read together, name the same order
  for any cast two of them list.
- `when` uses only this vocabulary: `all`, `any`, `not`, `resourceAtLeast`, `healthBelowPct`, `attackersOnSelfAtLeast`,
  `enemiesInShapeAtLeast`, `effectNotUp`, `effectTicksLeftAtMost`, `targetWindowNot`, `targetHasAllyWindow`,
  `targetIsKillTarget`, `insideTelegraph`, `kickDuty`, `castTicksLeftAtMost`, `abilityReady`, `seatWithinMm`. Each is
  read from the bot `View` (`game/bots/Keyrun.Bots/View.cs`), never from hidden sim state. Anything else fails.
- `telegraphs[].response` is one of `faceAway`, `stepOut`, `spread`, `stack`, `dragIntoHazard`, `take`.
- Every `reason` is non-empty: a guided summary quotes the rule that fired.

**Where it plugs in** (read-only survey; an implementation lane owns these):

| File | Change |
|---|---|
| `game/content/Keyrun.Content/Model/` + `Loading/Rules/` | New `Guides.cs` and `GuidesRule.cs` (the validation above) |
| `game/bots/Keyrun.Bots/Roles/Tank.cs`, `TankEngage.cs`, `TankPressure.cs`, `KitsPriority.cs` | Today's scripted tank is the guide's ancestor (same thresholds). A `GuidedRole` evaluates the guide's rules in order and falls back to nothing, so the guide is the whole behaviour; the scripted roles stay for non-guided runs until a guide exists for every class |
| `tools/Keyrun.Playtest/Policy.cs`, `PolicyBot.cs`, `PlaytestSession.cs`, `Program.cs` | `--mode guided|blind`. Guided: kicks, dodges, defensives and forks come from the guides and a plan file is refused. Blind: today's plan file, unchanged |
| `tools/Keyrun.Playtest/Summary.cs`, `KeyLedger.cs` | `mode` on every row; guided rows carry the benchmark fields, blind rows `timeToLearnTicks` |
| `game/playtest/report.md` script | Benchmark and time-to-learn tables; refuses a balance line from a blind row |

**Smallest order:** (1) the guide schema and loader with `guides/warden.json` and validator tests; (2) `GuidedRole` for
the Warden, proven by a test that a guided Warden kicks Choral Mending in its last 6 ticks and never casts Bulwark
inside a telegraph; (3) the Chanter and Cinder guides (written by the same rules as s3); (4) `--mode` and the `mode`
field, with the parity check reading guided rows only; (5) the benchmark and time-to-learn in the report; (6) one blind
cohort measured against the benchmark, under PLAYTESTERS.md s4's cost cap.
