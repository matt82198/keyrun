// TEMPORARY diagnostics -- not committed.
import { it } from "vitest";
import { playRun } from "./harness.js";

it("trace", () => {
  const seed = Number(process.env.TSEED ?? 3);
  const mode = (process.env.TMODE ?? "competent") as "competent" | "idle";
  const from = Number(process.env.TFROM ?? 0);
  const to = Number(process.env.TTO ?? 99999);
  const lines: string[] = [];
  playRun(seed, mode, 2, (run, events, commands) => {
    const t = run.state.tick;
    if (t < from || t > to) return;
    for (const e of events) {
      if (e.type === "death" || e.type === "pull_start" || e.type === "pull_clear") lines.push(`t${t} EV ${JSON.stringify(e)}`);
      if (e.type === "cast_complete" && Number(e.actorId) < 10) lines.push(`t${t} CAST ${e.actorId} ${e.abilityId}`);
      if (e.type === "damage" && Number(e.targetId) < 10) lines.push(`t${t} DMG ${e.sourceId}->${e.targetId} ${e.healthDamage} (${e.abilityId ?? ""})`);
      if (e.type === "heal" && Number(e.targetId) < 10) lines.push(`t${t} HEAL ${e.sourceId}->${e.targetId} ${e.amount}`);
    }
    if (t % 20 === 0) {
      const actors = Object.values(run.state.actors);
      const party = actors.filter((a) => a.kind === "player").map((a) => `${a.id}:${a.health}${a.alive ? "" : "X"}[${a.effects.map((x) => x.effectId).join(",")}]r${a.resource}@${a.pos.x},${a.pos.z}`).join(" ");
      const foes = actors.filter((a) => a.kind === "enemy" && a.alive).map((a) => `${a.id}:${a.health}->${a.targetId}@${a.pos.x},${a.pos.z}`).join(" ");
      lines.push(`t${t} P[${party}] E[${foes}] tg=${run.encounter.telegraphs.length}`);
    }
    void commands;
  });
  console.log(lines.join("\n"));
}, 600_000);
