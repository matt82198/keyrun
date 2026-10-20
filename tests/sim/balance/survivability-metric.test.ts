/**
 * tests/sim/balance/survivability-metric.test.ts
 *
 * The key-2 survivability metric (orchestrator-set 2026-10-18; reversible constants, Matt's play
 * overrules). Ten seeds (seeds.ts), Hollow Vault at key 2, hz=60 human reaction, bots at the
 * playable client's skill dial:
 *   (a) competent scripted human Warden + 4 bots clears the full dungeon with 0-2 deaths in >= 8/10;
 *   (b) IDLE human Warden + 4 bots wipes before the boss in >= 8/10 (the human must matter);
 *   (c) no bot dies on the first two pulls in any of the 10 competent runs.
 * BALANCE_TABLE=1 prints the 10x2 outcome table (harness.ts formatTable).
 *
 * Usage: `BALANCE_TABLE=1 npx vitest run tests/sim/balance/survivability-metric.test.ts`
 */
import { describe, expect, it } from "vitest";
import { BALANCE_SEEDS } from "./seeds.js";
import { formatTable, playRun, scoreMetric, type RunOutcome } from "./harness.js";

const THRESHOLD = 8;

describe("key-2 survivability metric (hollow-vault, hz=60, 10 seeds)", () => {
  it("competent human matters: (a) >= 8/10 clear with <= 2 deaths, (b) >= 8/10 idle wipes before the boss, (c) 10/10 no early bot death", () => {
    const competent: RunOutcome[] = BALANCE_SEEDS.map((s) => playRun(s, "competent"));
    const idle: RunOutcome[] = BALANCE_SEEDS.map((s) => playRun(s, "idle"));
    const table = formatTable([...competent, ...idle]);
    const v = scoreMetric(competent, idle);
    const why = `${table}\nscore a=${v.a}/10 b=${v.b}/10 c=${v.c}/10`;
    if (process.env.BALANCE_TABLE) console.log(why);
    expect(v.a, `(a) competent clears with <= 2 deaths\n${why}`).toBeGreaterThanOrEqual(THRESHOLD);
    expect(v.b, `(b) idle human wipes before the boss\n${why}`).toBeGreaterThanOrEqual(THRESHOLD);
    expect(v.c, `(c) no bot death on the first two pulls\n${why}`).toBe(BALANCE_SEEDS.length);
  }, 600_000);
});
