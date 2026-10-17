/**
 * client/src/readability.ts
 *
 * D10 first-run readability mount, kept out of boot.ts (190-LOC gate): builds render/cast-fx.ts's
 * projection pool and hud/onboarding.ts's first-10-seconds overlay, and installs a READ-ONLY
 * `window.__keyrunReadability` view (getters over live state, nothing that drives the game) that the
 * headless e2e (tests/client/e2e/first-run-readability.e2e.ts) reads, the same pattern as boot.ts's
 * `window.__keyrunCamera`.
 *
 * Usage: `const r = mountReadability(app, scene, keybinds, actorPool);` then pass `r.castFx` into
 * runGameLoop and call `r.onReady(display, humanClassId)` once the host is ready.
 */
import type * as THREE from "three";
import type { Keybinds } from "./input/keybinds.js";
import type { ActorPool } from "./render/actors.js";
import { CastFxPool } from "./render/cast-fx.js";
import { OnboardingOverlay } from "./hud/onboarding.js";
import { actorName, type DisplayInfo } from "./sim-host/display-info.js";

export interface KeyrunReadabilityView {
  /** Shapes of the cast projections currently on the ground ("cone", "circle", "target", ...). */
  projections(): string[];
  /** Total projections spawned since boot (survives their fade-out). */
  projectionsSpawned(): number;
  /** Shapes of the human's own last projections ("circle" after Bulwark, "cone" after Guard Break). */
  selfProjectionHistory(): string[];
  /** Floating ability-name labels currently up. */
  castLabels(): number;
  /** Name-tag title per live actor id. */
  nameTags(): Record<number, string>;
  onboardingDismissed(): boolean;
}

declare global {
  interface Window {
    __keyrunReadability?: KeyrunReadabilityView;
  }
}

export function mountReadability(app: HTMLElement, scene: THREE.Scene, keybinds: Keybinds, actorPool: ActorPool) {
  const castFx = new CastFxPool(scene);
  const onboarding = new OnboardingOverlay(app, keybinds, window);
  window.__keyrunReadability = Object.freeze({
    projections: () => castFx.activeShapes(),
    projectionsSpawned: () => castFx.spawnedCount(),
    selfProjectionHistory: () => castFx.selfProjectionHistory(),
    castLabels: () => castFx.activeLabels(),
    nameTags: () => actorPool.tagTitles(),
    onboardingDismissed: () => onboarding.root.dataset.dismissed === "true",
  });
  return {
    castFx,
    onReady(display: DisplayInfo | undefined, humanClassId: string | undefined): void {
      if (humanClassId) onboarding.setHeroName(actorName(display, humanClassId));
    },
  };
}
