/**
 * client/src/render/objective.ts
 *
 * D10 first-run readability: the one-line "what am I doing" objective under the pull readout.
 * Snapshot state only. A live boss (classId ends ".boss", render/roleColor.ts's convention) wins:
 * "Boss: <name> <pct>%". Otherwise the live enemies of the current pull, grouped by name in id
 * order: "Clear pull 1/4: 2x Choir Acolyte, Choir Cantor". No live enemy: "Pull cleared".
 *
 * Usage: `objectiveText(snapshot, display)` (render/hud.ts's updateRunReadout).
 */
import type { Snapshot } from "../sim-host/snapshot.js";
import { actorName, type DisplayInfo } from "../sim-host/display-info.js";
import { formatPullReadout } from "./run-readout.js";

export function objectiveText(snapshot: Pick<Snapshot, "actors" | "pullIndex" | "pullCount">, info?: DisplayInfo): string {
  const live = snapshot.actors.filter((a) => a.kind === "enemy" && a.alive).sort((a, b) => a.id - b.id);
  const boss = live.find((a) => a.classId.endsWith(".boss"));
  if (boss) {
    const pct = boss.maxHealth > 0 ? Math.ceil((boss.health / boss.maxHealth) * 100) : 0;
    return `Boss: ${actorName(info, boss.classId)} ${pct}%`;
  }
  if (live.length === 0) return "Pull cleared";
  const counts = new Map<string, number>();
  for (const a of live) {
    const name = actorName(info, a.classId);
    counts.set(name, (counts.get(name) ?? 0) + 1);
  }
  const parts = [...counts].map(([name, n]) => (n > 1 ? `${n}x ${name}` : name));
  return `Clear ${formatPullReadout(snapshot).toLowerCase()}: ${parts.join(", ")}`;
}
