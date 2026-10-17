// DOM-overlay HUD: ability bar (render/ability-bar.ts: key badge, ability name, cooldown sweep, GCD
// state, key legend), run timer, Slack bank, the top-left run readout (pull number, objective line,
// party markers) and a screen-centre crosshair. Kept separate from the 3D scene since it is plain 2D UI.
import type { Keybinds } from "../input/keybinds.js";
import type { SlotAbilities } from "../input/slot-abilities.js";
import { TICK_HZ, type Snapshot } from "../sim-host/snapshot.js";
import { abilityName, type DisplayInfo } from "../sim-host/display-info.js";

/** How long a "cast_failed" reason stays up: 1.5 s at 20 Hz. */
export const CAST_ERROR_TICKS = 30;
import { AbilityBarView } from "./ability-bar.js";
import { objectiveText } from "./objective.js";
import { colorForActor } from "./roleColor.js";
import { formatPullReadout, partyMarkers } from "./run-readout.js";

interface PartyMarkerEls {
  root: HTMLDivElement;
  fill: HTMLDivElement;
}

function styled<K extends "div" | "span">(tag: K, className: string, style: Partial<CSSStyleDeclaration>): HTMLElementTagNameMap[K] {
  const e = document.createElement(tag);
  e.className = className;
  Object.assign(e.style, style);
  return e;
}

export class Hud {
  private readonly root: HTMLDivElement;
  private readonly timerEl: HTMLDivElement;
  private readonly slackFillEl: HTMLDivElement;
  private readonly abilityBar: AbilityBarView;
  private readonly readoutEl: HTMLDivElement;
  private readonly pullEl: HTMLDivElement;
  private readonly objectiveEl: HTMLDivElement;
  private readonly partyEl: HTMLDivElement;
  private readonly partyMarkerEls: PartyMarkerEls[] = [];
  private readonly castErrorEl: HTMLDivElement;
  private castErrorUntil = -1;
  /** The host's display info (sim-host/display-info.ts); set once after whenReady(). */
  display: DisplayInfo | undefined;

  constructor(container: HTMLElement, keybinds: Keybinds) {
    this.root = document.createElement("div");
    this.root.className = "hud-root";
    container.appendChild(this.root);

    // D10 run readout (render/run-readout.ts): top-left, clear of the top-centre timer/Slack/boss bar,
    // the bottom-centre ability bar, the top-right net state and the bottom-left ?metrics=1 line.
    // Inherits .hud-root's pointer-events: none; restated so it never eats a canvas event.
    this.readoutEl = styled("div", "hud-run-readout", {
      position: "absolute", top: "18px", left: "18px", pointerEvents: "none",
      font: "14px/1.3 sans-serif", fontVariantNumeric: "tabular-nums",
    });
    this.pullEl = styled("div", "hud-pull", { fontSize: "18px", fontWeight: "600", letterSpacing: "0.04em" });
    this.pullEl.textContent = formatPullReadout({});
    this.objectiveEl = styled("div", "hud-objective", { fontSize: "15px", color: "#ffe066", marginTop: "2px", maxWidth: "340px" });
    this.partyEl = styled("div", "hud-party", { display: "flex", gap: "6px", marginTop: "6px" });
    this.readoutEl.appendChild(this.pullEl);
    this.readoutEl.appendChild(this.objectiveEl);
    this.readoutEl.appendChild(this.partyEl);
    this.root.appendChild(this.readoutEl);

    const crosshair = document.createElement("div");
    crosshair.className = "hud-crosshair";
    this.root.appendChild(crosshair);

    this.timerEl = document.createElement("div");
    this.timerEl.className = "hud-timer";
    this.root.appendChild(this.timerEl);

    // design/BIBLE.md Sec 8's Slack bank: a second bar under the run timer, drained by deaths and
    // wipes (sim/encounter/timer.ts) rather than a hard fail.
    const slackTrack = document.createElement("div");
    slackTrack.className = "hud-slack-track";
    this.slackFillEl = document.createElement("div");
    this.slackFillEl.className = "hud-slack-fill";
    slackTrack.appendChild(this.slackFillEl);
    this.root.appendChild(slackTrack);

    this.abilityBar = new AbilityBarView(this.root, keybinds);
    this.castErrorEl = document.createElement("div");
    this.castErrorEl.className = "hud-cast-error";
    this.castErrorEl.hidden = true;
    this.root.appendChild(this.castErrorEl);
  }

  updateTimer(runTimerTicks: number, timerLimitTicks: number): void {
    const elapsed = formatClock(runTimerTicks / TICK_HZ);
    const limit = formatClock(timerLimitTicks / TICK_HZ);
    this.timerEl.textContent = `${elapsed} / ${limit}`;
    this.timerEl.classList.toggle("hud-timer-over", runTimerTicks > timerLimitTicks);
  }

  updateSlack(slackTicks: number, maxSlackTicks: number, depleted: boolean): void {
    const fraction = maxSlackTicks > 0 ? Math.max(0, Math.min(1, slackTicks / maxSlackTicks)) : 0;
    this.slackFillEl.style.width = `${Math.round(fraction * 100)}%`;
    this.slackFillEl.classList.toggle("hud-slack-depleted", depleted);
  }

  /** "Pull <n>/<total>", the objective line, plus one marker per party member: role colour, HP fill,
   * dimmed + red when dead. Also surfaces a "cast_failed" event (sim-host/cast-feedback.ts) as the
   * red "<Ability>: <reason>" line over the bar for CAST_ERROR_TICKS. */
  updateRunReadout(snapshot: Pick<Snapshot, "actors" | "pullIndex" | "pullCount"> & Partial<Pick<Snapshot, "events" | "tick">>): void {
    this.showCastError(snapshot);
    this.pullEl.textContent = formatPullReadout(snapshot);
    this.objectiveEl.textContent = objectiveText(snapshot, this.display);
    const markers = partyMarkers(snapshot);
    while (this.partyMarkerEls.length > markers.length) this.partyEl.removeChild(this.partyMarkerEls.pop()!.root);
    while (this.partyMarkerEls.length < markers.length) this.partyMarkerEls.push(this.buildPartyMarker());
    markers.forEach((m, i) => {
      const els = this.partyMarkerEls[i]!;
      const color = `#${colorForActor("player", m.role).toString(16).padStart(6, "0")}`;
      els.root.dataset.actorId = String(m.id);
      els.root.dataset.alive = String(m.alive);
      els.root.title = `${m.role} #${m.id}: ${m.alive ? `${Math.round(m.hpFraction * 100)}%` : "dead"}`;
      els.root.style.borderColor = color;
      els.root.style.opacity = m.alive ? "1" : "0.4";
      els.root.style.background = m.alive ? "rgba(20, 24, 30, 0.85)" : "rgba(120, 20, 20, 0.6)";
      els.fill.style.background = color;
      els.fill.style.height = `${Math.round(m.hpFraction * 100)}%`;
    });
  }

  private buildPartyMarker(): PartyMarkerEls {
    const root = styled("div", "hud-party-marker", {
      position: "relative", width: "14px", height: "28px", borderRadius: "3px",
      border: "2px solid #999999", overflow: "hidden", boxSizing: "border-box",
    });
    const fill = styled("div", "hud-party-hp", { position: "absolute", left: "0", right: "0", bottom: "0", height: "100%" });
    root.appendChild(fill);
    this.partyEl.appendChild(root);
    return { root, fill };
  }

  private showCastError(snapshot: Partial<Pick<Snapshot, "events" | "tick">>): void {
    const tick = snapshot.tick ?? 0;
    const failed = (snapshot.events ?? []).filter((e) => e.type === "cast_failed").pop();
    if (failed?.type === "cast_failed") {
      this.castErrorEl.textContent = `${abilityName(this.display, failed.abilityId)}: ${failed.reason}`;
      this.castErrorUntil = tick + CAST_ERROR_TICKS;
    }
    this.castErrorEl.hidden = tick > this.castErrorUntil;
  }

  /** `payoffIds`: abilities whose press would close an open Window now (render/payoff-ready.ts). */
  updateAbilityBar(cooldowns: Record<string, number>, slotAbilities: SlotAbilities | undefined, gcdRemainingTicks = 0, payoffIds?: ReadonlySet<string>): void {
    this.abilityBar.update(cooldowns, slotAbilities, this.display, gcdRemainingTicks, payoffIds);
  }
}

function formatClock(totalSeconds: number): string {
  const clamped = Math.max(0, totalSeconds);
  const minutes = Math.floor(clamped / 60);
  const seconds = Math.floor(clamped % 60);
  return `${minutes}:${String(seconds).padStart(2, "0")}`;
}
