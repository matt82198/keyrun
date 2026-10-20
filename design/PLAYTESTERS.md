# Agent playtesters (STATE.md D33)

PURPOSE: design keyrun's agent playtesters, which learn the game run by run through the same receipts machinery
Aesop uses; status: real (design only, no sim code in this document's PR).

Matt, 2026-10-20, verbatim:

- f-4f45e954: "the bots are your gametesters, they should learn and progress keys as well, they should be given an
  understanding of how to play the game, just use agent playtesters".
- f-4e8c87b7: "ensure the bots can fail lol, they should not clear the first dungeon on the first try unless they
  have played wow lmfao, the point is agents can learn the game over time using the same machinery we are using right
  now".

**What already exists (do not rebuild it).** PR #305 (`feat/playtest-harness`, open at the time of writing) adds
`tools/Keyrun.Playtest`: a plan-file policy (`Policy`, `PolicyBot`), human hands with seeded reaction delays and
fumbles (`Hands`), the key ledger (`KeyLedger`), per-playtester memory with SHA-256 receipts (`PlaytesterMemory`),
the session (`playtest run --player`) and a CLI guide (`tools/Keyrun.Playtest/GUIDE.md`, tested to name no enemy
and no enemy ability). This design is the layer above it: the player-side guide (section 1), the agent that writes
the plans and lessons (section 2), the proof that it fails first and learns for real (section 3), and the cost
(section 4). Every piece below names the #305 file it reuses or the small change it needs.

---

## 1. How to play Keyrun (the playtester's guide, player's side only)

This section is written to the playtester. It is the only game knowledge a fresh playtester gets, together with
GUIDE.md (the plan-file syntax) and `playtest kit` (its own party's buttons). It names no enemy, no enemy ability,
no route and no tuning number from the game's data, and it does not say what to do in any fight. You find that out
by playing.

### 1.1 What a run is

You open the **Screen**, see the key you hold, press **Queue**, and you are in a dungeon with a party of three. The
dungeon is a short chain of rooms and ends at one boss. A clock starts on the first tick and counts against **par**.
Under it a second bar, **Slack**, is a grace bank: each death spends some of it and a full wipe spends more. When the
boss dies you get a run-end panel and return to the Screen.

- **Timed**: the boss died no later than par plus the Slack you still had. Your key goes up: +1, +2 if you were well
  inside par, +3 if you were far inside it.
- **Untimed**: anything else (a wipe, an empty Slack bank, too slow, never finished). Your key stays where it was. It
  never drops, so failing costs you a run, never progress.

A higher key makes enemies act more often, gives you less warning before their attacks land, and makes them tougher.
Higher keys also add rules shown on the Screen before you queue; you never pick them.

### 1.2 The screen

One screen, three bands: **Character** (your gear, a "since last run" line, your five abilities), **Key** (the level
you hold and any rule that key carries) and **Queue** (one button). A private strip shows your last three runs. As a
playtester you do not click the screen: the ledger holds your key and `playtest run` queues it for you. The screen's
information is in your `summary.json` and your memory file.

### 1.3 Your keys

Ten keys per seat, plus walking:

| Key | What it is |
|---|---|
| 1-5 | Five rotation abilities. Your party presses these by itself. |
| Q | Interrupt ("kick"). Stops an enemy cast that can be stopped. Long cooldown: kicks are scarce. |
| E | Defensive. Survives a hit you could not avoid. |
| R | Utility: a taunt, a dispel, a stance swap, depending on class. |
| F | Movement: a dash, leap or blink. |
| G | Ping: marks a target or a spot for the party. |

You do not press keys one by one. You write a **plan** (GUIDE.md) that tells each seat when to use Q, E, R and
movement, and your party's hands carry it out with human reaction time and the occasional fumble.

### 1.4 Roles

Roles are soft: every class can keep itself alive, and every class helps the group a little.

- **Tank (warden, seat 1)**: holds the enemies' attention and leads the party from room to room. Enemies hit whoever
  holds them; if the tank loses them they go for someone softer.
- **Healer (chanter, seat 2)**: keeps the party standing and can remove some harmful effects with R.
- **Damage (cinder, seat 3)**: kills things. Its resource rewards pushing, and punishes pushing too far.

Classes also work in pairs. Some abilities leave a ring on a target or on the floor for a few seconds (a **Window**);
another class's matching ability landing inside the ring fires a **Combo**. A class cannot finish its own Window. The
ring's colours tell you which classes can finish it. `playtest kit` lists which of your buttons open and which close
Windows.

### 1.5 Reading enemies: cast bars and telegraphs

- **Every dangerous enemy action is a cast** with a visible bar. Nothing dangerous comes without one.
- **A cast that can be kicked** shows an open bar. A cast that cannot be kicked has the same bar with a shield edge.
  A kick on a shielded cast is wasted.
- **The kick window** is the last part of a cast. A kick counts on the tick you intended it, so a kick near the end is
  safe and a kick at the very start is fine too, but a kick after the bar ends does nothing.
- **Floor telegraphs** are drawn on the floor before an attack lands: circles, cones and lines, each with its own
  outline and colour. Standing inside one when it resolves means you are hit. Walking out in time means you are not.
  Some patches stay on the floor after they land and hurt while you stand in them.
- **Not every attack can be stopped or dodged.** Some are meant to be taken, with a defensive or a heal ready.

### 1.6 The kick ledger

Your party has three kicks, each with a long cooldown, so most packs have more casts than you have kicks. Which casts
you kick, and who kicks them, is the most important decision you make.

- In the game, each party frame shows its seat's kick as a ready pip with a draining arc, so you can see who has a
  kick before a cast starts. When a pack engages, each kickable enemy shows a small number: the order your kicks come
  off cooldown. You may ignore it.
- Two seats kicking the same cast waste one kick. Name one kicker first and a backup second.
- After every run, `summary.json` is your kick ledger: which kickable casts you met, how many resolved, how many you
  stopped, and per class how many kicks were pressed, landed and late. A cast you never named in your plan is never
  kicked.
- Ask of every run: which uncovered cast cost the most, and could a kick have been spent there instead of where it was?

### 1.7 How to get better

- Read your memory before every run. Change one or two things, not everything.
- Find the one thing that cost the most (a death, a resolved cast, damage from one ability) and write it down as a
  lesson that cites the run.
- A plan that times a key on one seed may be luck. Your hands and fumbles change with the seed; the dungeon does not.

---

## 2. The playtester loop

One loop per playtester. Each step names the file it writes and the code it reuses. **No step launches Godot.** The
run is the headless deterministic sim, driven the way `RolesOracleTraceTests.Drive` and `WildfireRunEndsTests` already
drive whole runs in tests: `RunRoot.CreateRun` then `RunRoot.StepRun` per tick, with `Bot.RunBots` for the rotation.
#305's `PlaytestRunner` is that loop wrapped for a CLI.

```
  memory + guide ──> agent writes plan.json ──> playtest run (headless sim) ──> summary.json + run.log
        ^                                                                           │
        │                     ledger.json: key moves by Matt's rule <───────────────┤
        └──── playtest learn: lesson citing run N <── agent reads summary ──────────┘
```

| Step | Who | Writes | Reuses |
|---|---|---|---|
| 1. Read | agent | nothing | `playtest guide` (GUIDE.md + section 1 above), `playtest kit`, `playtest memory --player ID` |
| 2. Plan | agent | `game/playtest/runs/ID/N/plan.json` | #305 `PolicyReader` (rejects unknown fields, ids and values) |
| 3. Run | `playtest run --player ID --policy plan.json --seed S --out game/playtest/runs/ID/N` | `summary.json`, `run.log` | #305 `PlaytestSession.Play` -> `PlaytestRunner` -> `RunRoot.StepRun`; `PolicyBot` + `Hands` per seat |
| 4. Receipt | session | receipt fields in `summary.json` and the memory entry | #305 `MemoryReceipt` (SHA-256 of summary and plan) plus `traceHash` (below) |
| 5. Key | session | `game/playtest/ledger.json` | #305 `KeyLedger.Record`: timed +1/+2/+3 at par/80%/60%, untimed stays (BIBLE s8) |
| 6. Learn | agent | `game/playtest/players/ID.json` (lessons) | #305 `playtest learn --player ID --lesson TEXT --run N` |

**The run receipt** (one per run, `summary.json`, hashed into the memory entry):

- `traceHash`: NEW. `Fnv1a.Hex(Projection.TraceHash(lines))` over the run's per-tick projection lines, the same digest
  `tools/Keyrun.Replay/TraceReplayer.cs:164` computes for the oracle traces. Because the sim is deterministic,
  (build sha, key, seed, plan) always yields the same traceHash.
- `buildSha`: NEW. The git HEAD the run was played on, plus the content manifest hash. Runs on different builds are
  never pooled in the learning metrics (section 3).
- `key`, `result` (timed tier or untimed reason), `deaths` (who, tick, pull, enemy ability), `clearSeconds`,
  `parSeconds`, `slackLeftSeconds`: already in #305 `Summary`.
- `plan` SHA-256 and `summary` SHA-256: already in #305 `MemoryReceipt`.

**Verification** reuses the same idea as Aesop's receipt gate (recompute, never trust): `playtest verify --player ID`
re-hashes every file (#305), and NEW `--replay` re-plays each run from (buildSha, key, seed, plan) and compares the
traceHash. A ledger row whose replay does not match is struck, and the key is recomputed from the surviving rows.

**Files** (all under `game/playtest/`, git-tracked so a lane's learning lands as a reviewable diff):

- `ledger.json`: one row per run per playtester; the key each playtester holds.
- `players/ID.json`: runs with receipts, `saw:` lines the session writes, lessons the agent writes.
- `runs/ID/N/{plan.json,summary.json,run.log}`: the evidence. `run.log` can be large; if it is, it is kept out of git
  and only its SHA-256 and the traceHash are committed (replay recreates it).

**The agent** is a dispatched subagent (Haiku by default) with a fixed brief: "you are playtester ID; read the guide
and your memory; write one plan; run it on the seeds given; read the summaries; write at most one lesson citing a run;
stop." It never edits the ledger, summaries or memory runs by hand (the receipts catch it), never changes code, and
never launches Godot.

## 3. Failure by default, and proof the learning is real

**Starting state.** A fresh playtester has the guide (section 1 and GUIDE.md), `playtest kit`, an empty memory and key
2. It has never seen an enemy id: the guide names none, and the plan provenance check below (NEW, next step 4) makes
`PolicyReader` refuse an id the playtester could not have met.

**Expected first result.** The first key 2 run is untimed on most seeds. #305 `FreshPlaytesterTests` pins the
floor case (the empty plan `{}` does not time key 2). The design target for an agent's first plan is: timed on at most
2 of 10 seeds. Above that, either the game is too easy for a stranger or knowledge leaked; both are findings.

**Leak controls** (knowledge comes only from the playtester's own runs):

1. **Sandbox.** The agent's working directory holds only the playtest CLI build, GUIDE.md and its own
   `game/playtest/players/ID.json` and `runs/ID/`. No repo checkout, no `design/`, no `content/data/`, no other
   playtester's files. BIBLE s9.3 lists the answers; it must be unreachable.
2. **Plan provenance check** (NEW, mechanical). Every enemy id or enemy ability id in a plan must appear in an earlier
   `summary.json` of the same playtester. A plan that names an id it never saw is refused with the id in the message.
   This is the test that catches leaked knowledge, independent of how it leaked.
3. **Lessons cite runs.** #305 already requires a lesson to cite a run number; the cited run's receipt must verify.

**Measuring real learning** (one build sha, fixed seed sets, all numbers from ledger rows, never narrated):

- **Cohort.** N = 3 learners, 15 runs each, each run played on 3 seeds from a fixed training set (one plan per run).
- **Control.** 3 amnesiac playtesters on the same seeds: identical brief, but memory is wiped before every run, so
  they only ever have the guide.
- **Held-out check.** After runs 5, 10 and 15 each learner's current plan is played on 5 seeds it has never seen.
- **Pass** (all of): first-run timed rate across learners at most 20%; learners' held-out timed rate at run 15 at
  least 50% and at least 30 points above the control's; mean key held by learners at run 15 at least 4 (two timed
  steps up); no plan refused by the provenance check.
- **Fail modes and what they mean.** Learners do not beat control: the guide or the summary does not teach (fix the
  summary, not the guide's answers). Control already times key 2: too easy or leaked. Learners pass training seeds but
  not held-out: they memorised seed luck.

The report (`game/playtest/report.md`, written by a script from the ledger) is the evidence; a lane quotes its
numbers, never its own impression.

## 4. Cost

Token estimate per decision (Haiku 5.5, list price $0.10 / $0.50 per MTok, from the fleet's 2026-10-13 pricing
note; verify before any spend): guide + kit + memory + 3 summaries is about 25k input tokens, a plan and a lesson about
2k output. That is roughly $0.0035 per decision. The sim runs locally and costs nothing.

- **Per run** (one dispatch per seed): 3x the decisions, and every decision sees one noisy result. Rejected.
- **Per batch** (chosen): one dispatch reads memory once, writes one plan, plays it on 3 seeds, writes one lesson.
  One cohort experiment (6 playtesters x 15 batches = 90 decisions) is about 2.3M input tokens, roughly $0.30 at list.
- **Budget cap**: 3M input tokens per experiment and 150 decisions per day, counted from the dispatch log; the loop
  stops at the cap and writes `BUDGET-STOP` into the report.
- **NEEDS MATT**: any recurring or scheduled run of this loop (a nightly cohort, a per-PR learning check) is recurring
  spend, whether through the API key or the subscription. Nothing recurring is scheduled until Matt approves it. A
  single experiment dispatched by the orchestrator inside a session is within the standing grant.

## 5. Decision and next steps

STATE.md D33 records this design. NEXT STEPS, smallest first:

1. Merge #305 (harness) once its CI is green. Everything below builds on it.
2. Fold section 1 into `tools/Keyrun.Playtest/GUIDE.md` (one guide, not two) and extend `GuideTests` to forbid every
   enemy id and enemy ability id in section 1's text.
3. Add `traceHash` and `buildSha` to `Summary` and `MemoryReceipt`, with a test that two runs of the same
   (key, seed, plan) give the same traceHash and a changed plan gives a different one.
4. Plan provenance check in `PolicyReader`: refuse an enemy id the playtester's own summaries never showed (test: a
   fresh playtester naming a real kickable cast is refused).
5. `playtest verify --replay` (re-run and compare traceHash; strike non-matching ledger rows).
6. First-run failure measurement: 10 fresh agents, one plan each, key 2; record the timed rate. Orchestrator-run, one
   session, within the standing grant.
7. `playtest report` script: cohort metrics from the ledger (section 3).
8. One full cohort experiment (learners vs amnesiac control, held-out seeds), under the section 4 cap.
9. NEEDS MATT: any scheduled or recurring playtester loop.
