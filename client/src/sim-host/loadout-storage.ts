/**
 * client/src/sim-host/loadout-storage.ts
 *
 * Reads the screen's own persisted build (talent picks + gear) out of localStorage, on the MAIN
 * thread only (client/src/boot.ts) -- a Worker has no `localStorage` of its own (that API lives on
 * `Window`, not `WorkerGlobalScope`), so this cannot live in worker.ts/run-setup.ts; the parsed,
 * already-plain-data result instead rides the postMessage boundary as InitMessage's `loadout`
 * field (wire.ts) for worker.ts to resolve into real abilities (content/apply.ts's applyLoadout,
 * which needs the content registry only the worker has fetched).
 *
 * STORAGE KEY: mirrors screen/src/store.ts's `STORAGE_KEY = "keyrun.screen.store.v2"` and
 * screen/src/types.ts's `Character.talentChoices`/`Character.gear` shape exactly -- read-only
 * reference, not imported (screen/** is out of this lane's ownership, and client/screen are
 * separate esbuild bundles; server/src/content-setup.ts's own docstring already established this
 * "mirror, don't cross-import" convention between domains in this repo). If screen's schema
 * version or storage key ever changes, this drifts loudly by simply finding nothing (JSON.parse or
 * a shape mismatch returns null below -- "no persisted loadout", never a thrown error): a MISSING
 * record is the overwhelmingly common case (a fresh visitor, or `screen/` never opened this
 * session), not a caller bug. An ACTUALLY-invalid loadout (a talent/facet/item id this run's
 * content no longer has) is a different, louder failure -- content/apply.ts's `applyLoadout`
 * throws for that; worker.ts lets it propagate as an ErrorMessage (see that file).
 *
 * Usage: `const raw = readPersistedLoadout(window.localStorage);`
 */
import { GEAR_SLOT_IDS, type GearSlotId, type Item } from "../../../content/schema/facet.js";

export const SCREEN_STORAGE_KEY = "keyrun.screen.store.v2";

export interface RawLoadout {
  talentChoices: Record<string, string>;
  gear: Item[];
}

interface StorageLike {
  getItem(key: string): string | null;
}

function isItem(v: unknown): v is Item {
  return typeof v === "object" && v !== null && typeof (v as Item).id === "string" && typeof (v as Item).slot === "string";
}

/** Returns null for "no persisted loadout" (no storage, no record, or a shape that does not match
 * -- see file docstring). Never throws. */
export function readPersistedLoadout(storage: StorageLike | undefined | null): RawLoadout | null {
  if (!storage) return null;
  let raw: string | null;
  try {
    raw = storage.getItem(SCREEN_STORAGE_KEY);
  } catch {
    return null;
  }
  if (!raw) return null;
  try {
    const parsed = JSON.parse(raw) as { character?: { talentChoices?: unknown; gear?: Partial<Record<GearSlotId, unknown>> } };
    const character = parsed.character;
    if (!character) return null;
    const talentChoices =
      character.talentChoices && typeof character.talentChoices === "object" ? (character.talentChoices as Record<string, string>) : {};
    const gearRecord = character.gear ?? {};
    const gear: Item[] = GEAR_SLOT_IDS.map((slot) => gearRecord[slot]).filter(isItem);
    return { talentChoices, gear };
  } catch {
    return null;
  }
}
