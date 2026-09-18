# bots/ — party AI for bots-first play

**Purpose**: fill the four other party slots with competent, readable teammates so one human can play the
dungeon before networking exists. A bot is a pure function `(state, memory, actorId) -> {commands, memory}`
evaluated once per tick; it never mutates state.

## Rules
- Bots read only what a player could see (state snapshot, telegraph events with horizons); no hidden info.
- Role behaviours as small state machines: tank (hold threat, face away, use defensives at health thresholds),
  healer (triage by missing health, dispel priority, stay in range), damage (rotation priority list from the
  class definition, kick the priority cast, move out of telegraphs). Interrupt assignment is deterministic:
  the bot with the lowest actorId whose kick is ready takes it.
- Skill dial 0–100 that adds reaction delay ticks and mistake probability from the seeded PRNG, so tuning can
  test "average pug" versus "good group".
- Every behaviour has a test against a scripted sim scenario (e.g. "a telegraph appears under the bot; within
  N ticks it has moved out").

## Layout
`bots/src/bot.ts` (interface + runner), `roles/tank.ts`, `roles/healer.ts`, `roles/damage.ts`,
`shared/positioning.ts`, `shared/priority.ts`, `skill.ts`.
