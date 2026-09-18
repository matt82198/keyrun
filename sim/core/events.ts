// Usage: shared simulation event shape. Core emits none itself; StepRules (owned by sim/combat)
// emits gameplay events via the `emit` callback passed into step(). `type` is a free-form tag
// ("damage", "heal", "cast-start", ...); extra fields ride along in the index signature.

import type { Tick } from "./state";

export type SimEvent = {
  tick: Tick;
  type: string;
  [k: string]: unknown;
};
