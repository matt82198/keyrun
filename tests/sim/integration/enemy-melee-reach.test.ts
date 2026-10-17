/**
 * tests/sim/integration/enemy-melee-reach.test.ts
 *
 * Repro-not-citation regression guard for "does an enemy ever melee the party?" Driven through the
 * real public path the browser host uses: client/src/sim-host/run-setup.ts's buildRunSetup (real
 * content, real hollow-vault party spawn positions), sim/index.ts's createRun/stepRun, and real
 * bots via client/src/sim-host/bot-commands.ts's computeBotCommands. The human (warden) is passive:
 * it never submits a command, exactly like a player standing still at the first pull.
 *
 * Before the fix, nothing in sim/ ever moved an enemy: the party spawns at z=4000 and the Choir's
 * melee pair at (+-400, 1000) -- about 3026mm apart, just outside choir.meleeSwing's 3000mm range --
 * so sim/encounter/script-target.ts range-gated (blocked) every scripted swing and not one landed
 * in 6000 ticks. sim/encounter/chase.ts now closes a melee enemy onto its target.
 *
 * Usage: `npx vitest run tests/sim/integration/enemy-melee-reach.test.ts`
 */
import { describe, expect, it } from "vitest";
import { loadContent } from "@content/index";
import { createRun, stepRun, type Run } from "@sim/index";
import { rangeMm } from "@sim/combat/casting";
import { makeBot, defaultMemory, type Bot, type BotMemory } from "@bots/src/bot";
import { seedPrng } from "@bots/src/skill";
import { buildRunSetup } from "../../../client/src/sim-host/run-setup.js";
import { computeBotCommands } from "../../../client/src/sim-host/bot-commands.js";

const SEED = 1;
/** Pull 0 (The Choir) spawns at createRun; the swing loops every 40 ticks, so 200 ticks is 5 swings. */
const TICK_BUDGET = 200;
const CHECKPOINTS = new Set([1, 40, 80, 120, 200]);

interface MeleeHit {
  tick: number;
  enemyId: number;
  targetId: number;
  healthDamage: number;
  hpBefore: number;
  hpAfter: number;
}

interface Trace {
  checkpoints: string[];
  closestMm: number;
  hits: MeleeHit[];
}

function runTrace(): Trace {
  const setup = buildRunSetup(loadContent(), { keyLevel: 5, dungeonId: "hollow-vault", humanClassId: "warden" });
  let run: Run = createRun(SEED, setup.party, setup.dungeon, setup.content, setup.runOptions);
  const bots: Record<number, Bot> = {};
  let memories: Record<number, BotMemory> = {};
  for (const m of setup.party) {
    if (m.id === setup.humanActorId) continue;
    bots[m.id] = makeBot();
    memories[m.id] = defaultMemory(80);
  }
  let prng = seedPrng(SEED);

  const meleeIds = new Set(
    Object.entries(setup.content.enemyScripts)
      .filter(([, script]) => script.some((s) => s.cast.endsWith(".meleeSwing")))
      .map(([id]) => Number(id)),
  );
  expect(meleeIds.size).toBeGreaterThan(0);

  const trace: Trace = { checkpoints: [], closestMm: Number.MAX_SAFE_INTEGER, hits: [] };
  for (let i = 0; i < TICK_BUDGET; i += 1) {
    const hpBefore: Record<number, number> = {};
    for (const a of Object.values(run.state.actors)) if (a.kind === "player") hpBefore[a.id] = a.health;

    const b = computeBotCommands(run, bots, memories, prng, []);
    memories = b.memories;
    prng = b.prng;
    const stepped = stepRun(run, b.commands); // the human submits nothing: passive
    run = stepped.run;

    // casting.ts applies a cast's effects (the damage event) BEFORE it emits cast_complete, so
    // collect this tick's swingers first, then attribute their damage events.
    const swung = new Set<number>();
    for (const e of stepped.events) {
      if (e.type === "cast_complete" && meleeIds.has(e.actorId as number) && String(e.abilityId).endsWith(".meleeSwing")) {
        swung.add(e.actorId as number);
      }
    }
    for (const e of stepped.events) {
      if (e.type === "damage" && swung.has(e.sourceId as number)) {
        const target = run.state.actors[e.targetId as number];
        if (target?.kind === "player") {
          trace.hits.push({
            tick: run.state.tick,
            enemyId: e.sourceId as number,
            targetId: target.id,
            healthDamage: e.healthDamage as number,
            hpBefore: hpBefore[target.id]!,
            hpAfter: target.health,
          });
        }
      }
    }

    const players = Object.values(run.state.actors).filter((a) => a.kind === "player" && a.alive);
    const melee = Object.values(run.state.actors).filter((a) => meleeIds.has(a.id) && a.alive);
    for (const m of melee) for (const p of players) trace.closestMm = Math.min(trace.closestMm, rangeMm(m, p));

    if (CHECKPOINTS.has(run.state.tick)) {
      const parts = melee.map((m) => {
        const nearest = Math.min(...players.map((p) => rangeMm(m, p)));
        return `e${m.id}@(${m.pos.x},${m.pos.z}) near=${nearest}mm`;
      });
      trace.checkpoints.push(`t${run.state.tick}: ${parts.join(" ")}`);
    }
  }
  return trace;
}

describe("enemy melee reaches the party (hollow-vault, passive human, seed 1)", () => {
  it("a melee enemy closes to swing range and a meleeSwing lands, reducing a party member's HP", () => {
    const trace = runTrace();
    // Printed so the trace is inspectable in CI logs and in the PR body.
    console.log(trace.checkpoints.join("\n"));
    console.log(`closest=${trace.closestMm}mm hits=${trace.hits.length} first=${JSON.stringify(trace.hits[0] ?? null)}`);

    expect(trace.closestMm).toBeLessThanOrEqual(3000); // choir.meleeSwing range
    expect(trace.hits.length).toBeGreaterThan(0);
    const first = trace.hits[0]!;
    expect(first.healthDamage).toBeGreaterThan(0);
    expect(first.hpAfter).toBeLessThan(first.hpBefore);
  });

  it("produces an identical trace on two runs (determinism)", () => {
    expect(runTrace()).toEqual(runTrace());
  });
});
