import { it } from "vitest";
import { runFullDungeon } from "./sim/integration/full-run-harness";

it("scratch full run", () => {
  const verbose = process.env.FULLRUN_VERBOSE === "1";
  for (const skill of [80, 100]) {
    for (const seed of [1, 7, 11, 13]) {
      const r = runFullDungeon({ seed, keyLevel: 2, skill });
      const deaths = r.events.filter((e) => e.type === "death" && Number(e.actorId) < 100).map((e) => `${String(e.actorId)}@${e.tick}`);
      const lines = verbose ? r.lines : r.lines.filter((l) => !l.includes(" combo ") && !l.includes(" death "));
      console.log(`SEED ${seed} SKILL ${skill} result=${r.result} endTick=${r.endTick} playerDeaths=${deaths.join(",")}\n` + lines.join("\n"));
    }
  }
}, 600000);
