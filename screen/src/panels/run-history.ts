/**
 * Usage: import { renderPlayButton, renderDeltaLine, renderHistory } from "./panels/run-history.js";
 *
 * The Run band's tail: the Play button (clauses.ts's buildPlayUrl, now carrying the Clause pick
 * as `?clause=<id>` alongside `seed`/`key`), the "since last run" delta line, and the last five
 * run results with their picked reward.
 */
import { buildPlayUrl, computeRunDelta, type Character } from "../store.js";

export function renderPlayButton(character: Character): HTMLElement {
  const link = document.createElement("a");
  link.className = "play-button";
  link.textContent = "Play";
  link.href = buildPlayUrl(character, character.keyLevel, character.clauseId);
  return link;
}

export function renderDeltaLine(character: Character): HTMLElement {
  const wrap = document.createElement("p");
  wrap.className = "run-delta";
  const delta = computeRunDelta(character);
  wrap.textContent = delta ? `Since last run: ${delta.text}` : "Since last run: no runs yet.";
  return wrap;
}

export function renderHistory(history: Character["runHistory"]): HTMLElement {
  const wrap = document.createElement("div");
  wrap.className = "run-history";

  const heading = document.createElement("h3");
  heading.textContent = "Last 5 Runs";
  wrap.appendChild(heading);

  const list = document.createElement("ul");
  for (const run of history.slice(0, 5)) {
    const row = document.createElement("li");
    const reward = run.rewardPicked ? `T${run.rewardPicked.tier} ${run.rewardPicked.facet?.text ?? "no Facet"}` : "no pick";
    row.textContent = `${run.dungeon} +${run.keyLevel} - ${run.result} - ${run.durationSec}s - ${reward}`;
    list.appendChild(row);
  }
  wrap.appendChild(list);
  return wrap;
}
