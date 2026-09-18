/**
 * client/src/hud/run-end.ts
 *
 * Run-end state machine (pure, unit-tested) plus a small DOM overlay: the result banner and the
 * end-of-run choice-of-three stub the dispatch brief calls for. No progression logic lives here --
 * PR #14's screen/ owns that; choosing a card only `postMessage`s the choice out (see
 * `postRunEndChoice`) for a later lane to consume, matching "posts a message the screen can later
 * consume" and this lane's boundary (screen/** is off-limits).
 *
 * `stepRunEnd` decides the run's outcome, one tick at a time, from signals worker.ts already has:
 *  - boss dead -> "timed" (Slack never hit 0) or "depleted" (it did) -- sim/encounter/timer.ts's
 *    own RunResult distinction, via `depleted`.
 *  - the human pressed the abandon key (client/src/sim-host/host-commands.ts) -> "abandoned".
 *  - every player actor is dead with no rez mechanic in sim -> auto-"abandoned" after a short
 *    grace window (WIPE_GRACE_TICKS), rather than soft-locking the run forever. This is a
 *    playable-v1-only policy choice, not something design/BIBLE.md Sec 8 specifies (its Slack
 *    model assumes deaths get recovered, which this five-actor POC has no mechanic for) -- see
 *    worker.ts's file docstring.
 * Once `info` is set the state is terminal; further calls are no-ops (`stepRunEnd` is idempotent
 * post-end) so worker.ts can call it unconditionally every tick.
 *
 * Usage: `state = stepRunEnd(state, signals); if (state.info) runEndView.show(state.info, ...);`
 */
import type { RunEndInfo, RunOutcome } from "../sim-host/snapshot.js";

export const WIPE_GRACE_TICKS = 100; // 5s at 20Hz

export interface RunEndSignals {
  tick: number;
  bossDead: boolean;
  depleted: boolean;
  allPlayersDead: boolean;
  abandonPressed: boolean;
}

export interface RunEndState {
  info: RunEndInfo | null;
  wipeStartTick: number | null;
}

export function initialRunEndState(): RunEndState {
  return { info: null, wipeStartTick: null };
}

export function stepRunEnd(state: RunEndState, signals: RunEndSignals): RunEndState {
  if (state.info) return state;
  if (signals.bossDead) {
    return { info: { outcome: signals.depleted ? "depleted" : "timed", tick: signals.tick }, wipeStartTick: state.wipeStartTick };
  }
  if (signals.abandonPressed) {
    return { info: { outcome: "abandoned", tick: signals.tick }, wipeStartTick: state.wipeStartTick };
  }
  if (signals.allPlayersDead) {
    const wipeStart = state.wipeStartTick ?? signals.tick;
    if (signals.tick - wipeStart >= WIPE_GRACE_TICKS) {
      return { info: { outcome: "abandoned", tick: signals.tick }, wipeStartTick: wipeStart };
    }
    return { info: null, wipeStartTick: wipeStart };
  }
  return { info: null, wipeStartTick: null };
}

export interface RunEndCard {
  id: string;
  title: string;
  description: string;
}

/** Placeholder choice-of-three per the dispatch brief: "no progression logic" -- these three
 * cards are static content, not derived from the run in any way. */
export const STUB_CARDS: readonly RunEndCard[] = [
  { id: "gilded-echo", title: "Gilded Echo", description: "A shard that hums with the Vault's last Window." },
  { id: "hollow-mark", title: "Hollow Mark", description: "A brand from the Warden's shield -- worn, not won." },
  { id: "warden-boon", title: "Warden's Boon", description: "A promise of favour, redeemable nowhere yet." },
];

const OUTCOME_LABEL: Record<RunOutcome, string> = {
  timed: "Run cleared -- within par",
  depleted: "Run cleared -- key depleted",
  abandoned: "Run abandoned",
};

/** Posts the chosen card out for a later lane (screen/) to consume; window.postMessage is the
 * cross-app boundary since screen/** is off-limits to this lane. */
export function postRunEndChoice(cardId: string, outcome: RunOutcome): void {
  window.postMessage({ type: "keyrun:run-end-choice", cardId, outcome }, "*");
}

export class RunEndView {
  private readonly root: HTMLDivElement;
  private readonly title: HTMLDivElement;
  private readonly cardsEl: HTMLDivElement;

  constructor(container: HTMLElement) {
    this.root = document.createElement("div");
    this.root.className = "run-end-root";
    this.root.hidden = true;

    this.title = document.createElement("div");
    this.title.className = "run-end-title";
    this.root.appendChild(this.title);

    this.cardsEl = document.createElement("div");
    this.cardsEl.className = "run-end-cards";
    this.root.appendChild(this.cardsEl);

    container.appendChild(this.root);
  }

  show(info: RunEndInfo, onChoose: (cardId: string) => void = (id) => postRunEndChoice(id, info.outcome)): void {
    this.title.textContent = OUTCOME_LABEL[info.outcome];
    this.cardsEl.replaceChildren();
    for (const card of STUB_CARDS) {
      const el = document.createElement("button");
      el.type = "button";
      el.className = "run-end-card";
      el.innerHTML = `<strong>${card.title}</strong><span>${card.description}</span>`;
      el.addEventListener("click", () => {
        onChoose(card.id);
        this.hide();
      });
      this.cardsEl.appendChild(el);
    }
    this.root.hidden = false;
  }

  hide(): void {
    this.root.hidden = true;
  }
}
