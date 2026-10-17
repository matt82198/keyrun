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
- Threat is ONLY explicit threat ops in sim (damage adds none): the tank must taunt loose enemies and cast its rotation (roles/tank-engage.ts) or enemies stay on nearest-player. Heals are shape-gated: a self-shaped heal (Refrain) only heals the caster, so pick by reach (kit-heals.ts, roles/heal-pick.ts). Pull-0 idle-human repro: tests/sim/integration/bots-pull0.test.ts (PULL0_TRACE=1 prints the timeline).
- Pack play (Hollow Vault pull 1, five synced Paired Warriors): the tank Braces once 3+ enemies aim at it, not only under the 35% floor (a ~350 burst skips the floor); the healer casts its second group heal (Hymn, kit-heals.ts secondGroupHeal) and walks into its radius. Mitigation is live (sim #91): sim/combat/mitigation.ts dealDamage applies the target's damageTakenBp (Brace -4000 bp, Vigil Shield -3000 bp, content/data/effects/player.json:16-19) and the source's damageDoneBp.
- Momentum under pack pressure (roles/tank-pressure.ts, after sim #91 made Vigil real): with 3+ attackers the tank holds momentum for Vigil (no spend that leaves < Vigil's cost, so no Reckoning) and casts Vigil when Brace is down or has <= 30 ticks left; unpressured, Reckoning stays the finisher. Pull-1 repro: tests/sim/integration/bots-pull1.test.ts (PULL1_TRACE=1).
