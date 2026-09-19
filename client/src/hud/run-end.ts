/**
 * client/src/hud/run-end.ts
 *
 * A small DOM overlay: the result banner and the end-of-run choice-of-three stub the dispatch brief
 * calls for. No progression logic lives here -- PR #14's screen/ owns that; choosing a card only
 * `postMessage`s the choice out (see `postRunEndChoice`) for a later lane to consume, matching
 * "posts a message the screen can later consume" and this lane's boundary (screen/** is off-limits).
 *
 * P2 (policy-inside-stepRun): this file used to also own a `stepRunEnd` state machine (deciding
 * WHEN the run ended, from signals worker.ts assembled every tick) -- deleted. sim/index.ts's
 * stepRun now decides that itself (sim/run-wiring.ts's checkRunEnd) and threads the result through
 * `Run.runEnd`; sim-runtime.ts reads it straight off and hands it to `RunEndView.show` via
 * `Snapshot.runEnd`, the local host's exact analogue of the net host reading the server's `run_end`
 * message (client/src/net/net-client.ts). This file renders that result -- it has no run-end
 * knowledge of its own.
 *
 * Usage: `if (snapshot.runEnd) runEndView.show(snapshot.runEnd);` (hud-wiring.ts already does this).
 */
import type { RunEndInfo, RunOutcome } from "../sim-host/snapshot.js";

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
