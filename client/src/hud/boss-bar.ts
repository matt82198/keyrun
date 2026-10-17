/**
 * client/src/hud/boss-bar.ts
 *
 * Top-centre boss HP bar (D10: one dungeon with readable enemies and the boss). A DOM overlay like
 * hud/run-end.ts and render/hud.ts: shown while any live actor the actor pool colours COLOR_BOSS
 * (render/roleColor.ts: an enemy whose classId ends ".boss") is in the snapshot, one stacked row per
 * boss body (a two-body boss gets two bars), ordered by actor id so rows never swap between snapshots.
 * Each row: name (derived from the classId -- the snapshot carries no display name), a COLOR_BOSS
 * fill at the HP fraction, and the percent. Snapshot state only (D3); no phase markers because the
 * snapshot exposes no phase field, and the Lock / twins' combo are not modelled here.
 *
 * Usage: `const bossBar = new BossBarView(app);` then `bossBar.update(snapshot.actors)` per snapshot
 * (hud-wiring.ts does this via GameLoopDeps.bossBar).
 */
import { COLOR_BOSS, colorForActor } from "../render/roleColor.js";
import type { ActorSnapshot } from "../sim-host/snapshot.js";

/** CSS form of render/roleColor.ts's COLOR_BOSS, so the bar matches the boss capsule. */
export const BOSS_BAR_COLOR = `#${COLOR_BOSS.toString(16).padStart(6, "0")}`;

export interface BossBarRow {
  id: number;
  name: string;
  /** health / maxHealth clamped to [0, 1]; 0 when maxHealth <= 0. */
  fraction: number;
  /** ceil(fraction * 100): a live boss with any health never reads 0%. */
  percent: number;
}

/** "hollowWarden.boss" -> "Hollow Warden": drop the suffix, split camelCase, capitalise words. */
export function bossDisplayName(classId: string): string {
  const base = classId.replace(/\.boss$/, "").split(".").pop() ?? classId;
  return base
    .replace(/([a-z0-9])([A-Z])/g, "$1 $2")
    .split(/[\s_-]+/)
    .filter((w) => w.length > 0)
    .map((w) => w[0]!.toUpperCase() + w.slice(1))
    .join(" ");
}

function isLiveBoss(a: ActorSnapshot): boolean {
  return a.alive && colorForActor(a.kind, a.role, a.classId) === COLOR_BOSS;
}

/** One row per live boss body in `actors`, sorted by actor id (stable across snapshots). */
export function bossBarModel(actors: readonly ActorSnapshot[]): BossBarRow[] {
  return actors
    .filter(isLiveBoss)
    .sort((a, b) => a.id - b.id)
    .map((a) => {
      const fraction = a.maxHealth > 0 ? Math.min(1, Math.max(0, a.health / a.maxHealth)) : 0;
      return { id: a.id, name: bossDisplayName(a.classId), fraction, percent: Math.ceil(fraction * 100) };
    });
}

interface RowEls {
  root: HTMLDivElement;
  name: HTMLSpanElement;
  fill: HTMLDivElement;
  percent: HTMLSpanElement;
}

function el<K extends "div" | "span">(tag: K, className: string, style: Partial<CSSStyleDeclaration>): HTMLElementTagNameMap[K] {
  const e = document.createElement(tag);
  e.className = className;
  Object.assign(e.style, style);
  return e;
}

export class BossBarView {
  private readonly root: HTMLDivElement;
  private readonly rows: RowEls[] = [];

  constructor(container: HTMLElement) {
    // Below render/hud.ts's run timer (top 18px) and Slack track (top 54px, 8px tall).
    this.root = el("div", "boss-bar-root", {
      position: "absolute", top: "72px", left: "50%", transform: "translateX(-50%)", width: "360px",
      pointerEvents: "none", color: "#eef2f6", font: "12px/1.3 sans-serif", textShadow: "0 1px 3px rgba(0,0,0,0.8)",
    });
    this.root.hidden = true;
    container.appendChild(this.root);
  }

  update(actors: readonly ActorSnapshot[]): void {
    const model = bossBarModel(actors);
    while (this.rows.length > model.length) this.root.removeChild(this.rows.pop()!.root);
    while (this.rows.length < model.length) this.rows.push(this.buildRow());
    model.forEach((row, i) => {
      const els = this.rows[i]!;
      els.name.textContent = row.name;
      els.percent.textContent = `${row.percent}%`;
      els.fill.style.width = `${row.percent}%`;
    });
    this.root.hidden = model.length === 0;
  }

  private buildRow(): RowEls {
    const root = el("div", "boss-bar-row", { marginBottom: "6px" });
    const label = el("div", "boss-bar-label", { display: "flex", justifyContent: "space-between" });
    const name = el("span", "boss-bar-name", { fontWeight: "bold" });
    const percent = el("span", "boss-bar-percent", { fontVariantNumeric: "tabular-nums" });
    const track = el("div", "boss-bar-track", { height: "12px", borderRadius: "3px", background: "rgba(255,255,255,0.15)", overflow: "hidden" });
    const fill = el("div", "boss-bar-fill", { height: "100%", width: "100%", background: BOSS_BAR_COLOR, transition: "width 0.2s linear" });
    label.appendChild(name);
    label.appendChild(percent);
    track.appendChild(fill);
    root.appendChild(label);
    root.appendChild(track);
    this.root.appendChild(root);
    return { root, name, fill, percent };
  }
}
