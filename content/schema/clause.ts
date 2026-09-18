/**
 * content/schema/clause.ts
 * ADDED schema (not part of the sim-combat AbilityDef/EffectDef contract): BIBLE.md Sec 8 Clauses
 * are content-only data the queue offers before a key starts. sim reads `kind` + `params` to
 * apply a run-wide modifier; exactly one Clause runs per key, never stacked.
 */

export type ClauseKind = "echo" | "tide" | "hush" | "pact";

export interface ClauseDef {
  id: string;
  name: string;
  kind: ClauseKind;
  params: Record<string, number>;
  description: string;
}
