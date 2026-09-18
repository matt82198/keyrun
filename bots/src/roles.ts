/**
 * bots/src/roles.ts
 *
 * Re-exports the three role state machines (bots/CLAUDE.md's stated layout: roles/tank.ts,
 * roles/healer.ts, roles/damage.ts) plus the small readiness helper pings-policy.ts also uses.
 * Split out of a single roles.ts to stay under the repo's <=190 LOC/file cap; no behaviour
 * change -- see roles/shared.ts, roles/tank.ts, roles/healer.ts, roles/damage.ts for the actual
 * logic and docs.
 *
 * Usage: `import { tankDecide, healerDecide, damageDecide } from "./roles.js"`
 */
export { tankDecide } from "./roles/tank.js";
export { healerDecide } from "./roles/healer.js";
export { damageDecide, pickResourceAwareRotation } from "./roles/damage.js";
export { abilityReady } from "./roles/shared.js";
