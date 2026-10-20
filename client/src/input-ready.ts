/**
 * client/src/input-ready.ts
 *
 * Input readiness for the headless e2e (tests/client/e2e/window-ping.e2e.ts): the game is ready for a
 * key press once hud-wiring.ts's per-tick input sampler (the closure that turns KeyState into commands
 * and pings) has run with a snapshot in hand AND hud/onboarding.ts's overlay is dismissed. Before
 * that, a press can be lost to the boot sequence rather than to the game, and an e2e cannot tell the
 * two apart. Read-only from the page's side: `window.__keyrunInputReady` (boolean getter) and
 * `window.__keyrunInputTicks` ({ input, sim }, the sampler's local tick and the latest snapshot tick
 * it saw, for failure diagnostics). Nothing here drives the game.
 *
 * Usage: hud-wiring.ts calls `inputReadiness.noteInputTick(localTick, simTick)` once per sampled tick;
 * readability.ts calls `inputReadiness.bindOnboarding(isDismissed)` and `installInputReady(window)`.
 */

export interface InputTicks {
  /** hud-wiring.ts's local input-sample tick (stamps commands and pings); 0 before the first sample. */
  input: number;
  /** The latest snapshot tick the sampler saw, or null before the first snapshot. */
  sim: number | null;
}

declare global {
  interface Window {
    /** Read-only: true once the input sampler is live and onboarding is dismissed (input-ready.ts). */
    readonly __keyrunInputReady?: boolean;
    /** Read-only diagnostics (input-ready.ts): the sampler's input tick and the latest sim tick. */
    readonly __keyrunInputTicks?: InputTicks;
  }
}

export class InputReadiness {
  private onboardingDismissed: () => boolean = () => false;
  private ticks: InputTicks = { input: 0, sim: null };

  /** Supplies the onboarding overlay's dismissed state (readability.ts owns the overlay). */
  bindOnboarding(isDismissed: () => boolean): void {
    this.onboardingDismissed = isDismissed;
  }

  /** Called by the input sampler on every sampled tick. */
  noteInputTick(input: number, sim: number | null): void {
    this.ticks = { input, sim };
  }

  /** The sampler has run with a snapshot in hand, and the onboarding overlay is gone. */
  get ready(): boolean {
    return this.ticks.input > 0 && this.ticks.sim !== null && this.onboardingDismissed();
  }

  get inputTicks(): InputTicks {
    return { ...this.ticks };
  }
}

/** The page's one readiness tracker (module singleton, like sim-host/metrics.ts's `metrics`). */
export const inputReadiness = new InputReadiness();

/** Installs the read-only `__keyrunInputReady` / `__keyrunInputTicks` getters on `target` (window). */
export function installInputReady(target: object, readiness: InputReadiness = inputReadiness): void {
  Object.defineProperty(target, "__keyrunInputReady", { get: () => readiness.ready, configurable: true });
  Object.defineProperty(target, "__keyrunInputTicks", { get: () => readiness.inputTicks, configurable: true });
}
