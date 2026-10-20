# How to play Keyrun (playtester guide)

You are a playtester. You play keys of the Hollow Vault with a party of three, you will fail at first, and you get
better run by run. Nobody hands you the answers: this guide tells you how the game works and how to tell your party
what to do. What the enemies do, and what to do about it, you find out by playing and reading what happened.

## The game in one paragraph

Keyrun is a timed dungeon, built to feel like a World of Warcraft Mythic+ key with fewer buttons. A **key** is a
number from 2 to 20. Higher keys make enemies cast more often, make floor warnings resolve faster and give enemies more
health. The dungeon has teaching pulls, then a boss. The run clock starts at once and counts against **par**. A
second bar, **Slack**, is a grace bank: a death costs some of it, a full wipe costs more.

## Your party

Three seats, always the same classes:

| Seat | Class   | Role   |
|------|---------|--------|
| 1    | warden  | tank   |
| 2    | chanter | healer |
| 3    | cinder  | damage |

Each class has nine buttons: five rotation buttons (1-5), an interrupt (Q), a defensive (E), a utility (R) and a
movement button (F). Run `playtest kit` to see every button with its cast time, cooldown, range, shape and talent
forks. The party walks the route between fights by itself and presses its rotation by itself. Everything else your
party does only if your plan says so.

Your party has human hands: every decision waits a short reaction time, and some presses are fumbled or late. A perfect
plan can still go wrong, so leave room for mistakes.

## The plan file

Before every run you write a plan: one JSON object. A field you leave out means your party does not do that thing
at all. An empty plan `{}` is a party that knows nothing: it presses its rotation and nothing else.

- `reason`: one line saying why this plan (it is copied into the run summary).
- `talents`: per class, which option to take on a talent fork, e.g.
  `{"cinder": {"<fork ability id>": "<option id>"}}`. Ids come from `playtest kit`. Forks you do not name keep the base
  ability.
- `kicks`: a list of `{"cast": "<enemy ability id>", "kickers": ["<class>", ...]}`. When an enemy starts that cast,
  the first named class whose interrupt is ready and in range presses it. A cast you never name is never kicked. Only
  interruptible casts may be named; the summary lists the interruptible casts you met.
- `dodge`: `{"telegraphs": true}` makes the party walk out of warning shapes on the floor before they resolve;
  `{"floor": true}` makes it walk out of lasting floor patches.
- `defensive`: per class, a health percent (1-99) at or under which that seat presses its defensive (E).
- `utility`: the classes allowed to press their utility (R), such as a taunt or a dispel.
- `hold`: per player ability id, the pulls it may be pressed in (`choir`, `pair`, `seed`, `boss`); in every other
  pull it is saved. Use it to keep a big cooldown for the fight that needs it.
- `focus`: enemy ids the damage dealers attack first, in order. Without it they attack the tank's target.
- `pull`: `{"restHealthPct": N}`: before walking into the next room, wait until every living seat is at N% health.

A field the reader does not know, an unknown id or a bad value is an error, and the message says which.

## Playing a run

From the repo root:

```
dotnet run --project tools/Keyrun.Playtest -- run --player YOUR-ID --policy plan.json --seed S --out game/playtest/runs/YOUR-ID/N
```

Your id is lowercase letters, digits and dashes (for example `tester-01`). You always play the key the ledger holds
for you; a new playtester starts at key 2. The run writes `summary.json` (what happened) and `run.log` (every command
and event), records the result in the ledger (`game/playtest/ledger.json`) and adds the run to your memory file
(`game/playtest/players/YOUR-ID.json`).

## Reading what happened

`summary.json` is your end-of-run screen. The fields that teach the most:

- `result`, `lostOnPull`, `clearSeconds` against `parSeconds`, `slackLeftSeconds`: how the key went and where it ended.
- `deaths`: who died, when, in which pull, to which enemy ability, from which enemy.
- `interruptibleCastsResolved` and `castsInterrupted`: enemy casts that could have been kicked, and how many were.
- `enemiesFought`: the enemy ids you traded blows with and their names (the ids `focus` takes).
- `damageTakenByEnemyAbility`: what hurt the party most.
- `kicks`: kick presses, kicks landed and late kicks per class; `dodgeTicks`: time spent walking out of warnings.
- `buttonsPressed` and `unusedButtons`: what each class pressed and never pressed.
- `notable`: boss events and whether the key depleted.

Your memory file keeps a short recap of each run (`saw:` lines) next to your lessons.

## The key rule

- A **timed** run (the boss dead with time left) raises your key by the tier: **+1** within par, **+2** within 80% of
  par, **+3** within 60% of par.
- An **untimed** run (a wipe, a depleted Slack bank, a clear past par, or a run that never finished) leaves your key
  where it was. An untimed key stays the same level, it never drops.

## Learning, run by run

1. Read your memory: `dotnet run --project tools/Keyrun.Playtest -- memory --player YOUR-ID`.
2. Write your plan from what you saw, not from what you guess.
3. Play the run.
4. Read `summary.json`. Find the one thing that cost you the most.
5. Write it down as a lesson against that run:
   `dotnet run --project tools/Keyrun.Playtest -- learn --player YOUR-ID --lesson "what I learned"`
   (add `--run N` to cite an earlier run). A lesson always cites a run, so anyone can check it against that run's
   receipt.
6. Change one or two things in the plan and play again.

Every run in your memory carries a receipt: the SHA-256 of its summary and of the plan you played. Check them with
`dotnet run --project tools/Keyrun.Playtest -- verify --player YOUR-ID`. Never edit a summary or the ledger by hand;
the receipts will not match.

## Fair play

- Learn from your own runs and your own memory. Do not read the game's source, content data or other playtesters'
  plans for answers.
- The seed changes the hands and the fumbles, not the dungeon. A plan that only works on one seed is luck.
