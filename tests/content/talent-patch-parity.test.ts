/**
 * tests/content/talent-patch-parity.test.ts
 * Usage: `npx vitest run tests/content/talent-patch-parity.test.ts`
 * Re-record (only when content/data or the frozen content/apply*.ts changes, reason in the PR):
 *   `TALENT_PARITY_WRITE=1 npx vitest run tests/content/talent-patch-parity.test.ts`
 *
 * C#/TS parity for the talent and facet patch path. The TS half of the harness: for every shipped
 * talent option (content/data/classes) and every shipped facet (content/data/facets) it applies the
 * patch to the base ability through the frozen TS patch engine (content/apply-patch.ts applyPatch,
 * the engine content/apply.ts applyLoadout calls) and records the resulting ability definition,
 * plus the verdict of the full production path (applyLoadout with only that option picked or only
 * that facet equipped): "ok", or the message it throws. When applyLoadout accepts, its ability must
 * equal the recorded one (asserted here), so the snapshot is the production result.
 *
 * The snapshot is game/content/Keyrun.Content.Tests/Fixtures/talent-patch-parity.json; this test
 * regenerates it in memory and asserts it is byte-identical to the committed copy (the drift-guard
 * shape of tests/oracle). The C# half, game/content/Keyrun.Content.Tests/TalentPatchParityTests.cs,
 * reads the snapshot and asserts TalentPicks.Resolve produces the same ability definitions.
 */
import { describe, expect, it } from "vitest";
import { readFileSync, writeFileSync } from "node:fs";
import { fileURLToPath } from "node:url";
import { applyLoadout } from "../../content/apply";
import { applyPatch } from "../../content/apply-patch";
import { loadContent } from "../../content/index";
import type { AbilityDef } from "../../content/schema/ability";
import type { ClassDef } from "../../content/schema/class";
import type { FacetPatch, Item } from "../../content/schema/facet";

const SNAPSHOT = fileURLToPath(
  new URL("../../game/content/Keyrun.Content.Tests/Fixtures/talent-patch-parity.json", import.meta.url),
);

/** Recursively sorts object keys so the snapshot bytes do not depend on JS insertion order. */
function sortKeys(value: unknown): unknown {
  if (Array.isArray(value)) return value.map(sortKeys);
  if (value !== null && typeof value === "object") {
    const out: Record<string, unknown> = {};
    for (const key of Object.keys(value as Record<string, unknown>).sort()) {
      const v = (value as Record<string, unknown>)[key];
      if (v !== undefined) out[key] = sortKeys(v);
    }
    return out;
  }
  return value;
}

interface Row {
  classId: string;
  abilityId: string;
  ability: AbilityDef;
  loadout: string;
}

/** The patch engine's result, and the production path's verdict (asserting both agree when it accepts). */
function row(
  cls: ClassDef,
  base: AbilityDef,
  patch: FacetPatch,
  label: string,
  production: () => Record<string, AbilityDef>,
): Row {
  const ability = applyPatch(base, patch, label);
  let loadout = "ok";
  try {
    expect(production()[base.id]).toEqual(ability);
  } catch (e) {
    if (e instanceof Error && e.message.startsWith("applyLoadout:")) loadout = e.message;
    else throw e;
  }
  return { classId: cls.id, abilityId: base.id, ability, loadout };
}

function ownerOf(classes: Iterable<ClassDef>, abilityId: string): ClassDef {
  for (const c of classes) {
    if (Object.values(c.abilities as unknown as Record<string, string>).includes(abilityId)) return c;
  }
  throw new Error(`no class uses ability "${abilityId}"`);
}

/** Builds the full snapshot from the shipped content through the TS patch path. */
function buildSnapshot(): string {
  const reg = loadContent();
  const ability = (id: string): AbilityDef => {
    const def = reg.abilities.get(id);
    if (!def) throw new Error(`unknown ability "${id}"`);
    return def;
  };
  const talents = [];
  for (const cls of reg.classes.values()) {
    for (const fork of cls.talents) {
      for (const option of fork.options) {
        talents.push({
          optionId: option.id,
          ...row(cls, ability(fork.abilityId), option.patch, `talent "${option.id}" on "${fork.abilityId}"`, () =>
            applyLoadout(cls, reg.abilities, reg.facets, { [fork.abilityId]: option.id }, []).abilities,
          ),
        });
      }
    }
  }
  const facets = [];
  for (const [facetId, facet] of reg.facets) {
    const cls = ownerOf(reg.classes.values(), facet.abilityId);
    const item: Item = {
      id: `parity-${facetId}`,
      slot: facet.slot,
      tier: 1,
      facet: { id: facetId, text: facet.text, abilityId: facet.abilityId },
      charges: 0,
    };
    facets.push({
      facetId,
      ...row(cls, ability(facet.abilityId), facet.patch, `facet "${facetId}"`, () =>
        applyLoadout(cls, reg.abilities, reg.facets, {}, [item]).abilities,
      ),
    });
  }
  const snapshot = { kind: "keyrun-talent-patch-parity", version: 1, talents, facets };
  return `${JSON.stringify(sortKeys(snapshot), null, 2)}\n`;
}

describe("talent and facet patch parity snapshot (TS half)", () => {
  it("covers every shipped talent option and facet", () => {
    const reg = loadContent();
    const parsed = JSON.parse(buildSnapshot()) as { talents: unknown[]; facets: unknown[] };
    let options = 0;
    for (const cls of reg.classes.values()) for (const fork of cls.talents) options += fork.options.length;
    expect(options).toBeGreaterThan(0);
    expect(parsed.talents).toHaveLength(options);
    expect(reg.facets.size).toBeGreaterThan(0);
    expect(parsed.facets).toHaveLength(reg.facets.size);
  });

  it("the committed snapshot is byte-identical to the TS patch path's output", () => {
    const fresh = buildSnapshot();
    if (process.env.TALENT_PARITY_WRITE === "1") writeFileSync(SNAPSHOT, fresh, "utf8");
    expect(readFileSync(SNAPSHOT, "utf8").replace(/\r\n/g, "\n")).toBe(fresh);
  });
});
