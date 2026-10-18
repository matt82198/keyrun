/**
 * tests/oracle/traces.ts -- runs the FROZEN TypeScript sim (and bots) through each oracle scenario
 * and renders the `<name>.trace.jsonl` + `<setup>.setup.json` fixture texts game/sim/ORACLE.md
 * specifies. Pure: returns strings, writes nothing (generate.ts does the I/O).
 *
 * Scenarios (all deterministic; parity means EQUAL, not winning):
 *   - content-pull: tests/sim/golden/build-content-pull.ts's scenario + command stream (real content).
 *   - scripted-pull: tests/sim/golden/scripted-pull.json against tests/sim/integration/fixtures.ts's
 *     hand-authored content.
 *   - bots-hv-k2-s7 / bots-hv-k2-s11 / bots-hv-k10-s7: Hollow Vault, warden in the human slot, ALL
 *     five slots bot-driven (bots-pull1-warden.test.ts's harness), run seed == bot PRNG seed, skill
 *     dial 100, played until the run ends (run_end event) or MAX_BOT_TICKS.
 *
 * Usage: `import { buildTraces } from "./traces";` -> Array<{ name, files: Record<filename, text> }>.
 */
import { readFileSync } from "node:fs";
import { fileURLToPath } from "node:url";
import { loadContent } from "@content/index";
import { createRun, stepRun, type PartyMemberSpec, type Run } from "@sim/index";
import type { Command } from "@sim/core/commands";
import type { SimEvent } from "@sim/core/events";
import { makeBot, runBots, defaultMemory, type Bot, type BotMemory } from "@bots/src/bot";
import { buildBotView } from "@bots/src/view";
import { seedPrng } from "@bots/src/skill";
import { buildRunSetup } from "../../client/src/sim-host/run-setup.js";
import { toBotTelegraph } from "../../client/src/sim-host/telegraph-map.js";
import { BASE_PARTY, CONTENT, DUNGEON } from "../sim/integration/fixtures";
import { buildCommands, buildScenario, SEED as CONTENT_SEED, TICKS as CONTENT_TICKS } from "../sim/golden/build-content-pull";
import { canonicalJson, fnv1a32, fnv1a32Raw, projectionLine } from "./format";

/** The frozen TS tree every fixture was generated from (ENGINE-PLAN freeze point, main 6e1548d). */
export const SOURCE_SHA = "6e1548d";
export const ORACLE_VERSION = 1;
/** A full projection is stored every FULL_EVERY ticks (and on the first and last record). */
export const FULL_EVERY = 20;
/** Hard stop for bot runs: 20 minutes of sim time, past Hollow Vault's 12-minute par. */
export const MAX_BOT_TICKS = 24000;

const EMPTY_KIT = { rotation: [], interruptAbilityId: null, defensiveAbilityId: null, healAbilityId: null };

/** Per-transition callback: given the run about to be stepped, return the caller commands. */
type CommandSource = (run: Run) => Command[];

interface ScenarioSpec {
  name: string;
  setupFile: string;
  setup: unknown;
  scenario: Record<string, unknown>;
  run: Run;
  commands: CommandSource;
  /** Stop after the transition that produced this tick count, unless the run ends first. */
  maxTicks: number;
  stopOnRunEnd: boolean;
}

export interface TraceOutput {
  name: string;
  traceFile: string;
  setupFile: string;
  traceHash: string;
  records: number;
  finalTick: number;
  result: string | null;
  files: Record<string, string>;
}

function projection(run: Run): string {
  return projectionLine(run.state.tick, run.encounter, Object.values(run.state.actors));
}

function runTrace(spec: ScenarioSpec): TraceOutput {
  let run = spec.run;
  const body: string[] = [];
  let chain = fnv1a32Raw("");
  const record = (r: Run, commands: Command[] | null, events: SimEvent[] | null, forceFull: boolean): void => {
    const line = projection(r);
    chain = fnv1a32Raw(`${line}\n`, chain);
    const t = r.state.tick;
    let rec = `{"t":${t},"h":"${fnv1a32(line)}"`;
    if (forceFull || t % FULL_EVERY === 0) rec += `,"p":${canonicalJson(line)}`;
    if (commands !== null && commands.length > 0) rec += `,"c":${canonicalJson(commands)}`;
    if (events !== null && events.length > 0) rec += `,"e":${canonicalJson(events)}`;
    body.push(`${rec}}`);
  };

  record(run, null, null, true);
  let lastFullForced = false;
  while (run.state.tick < spec.maxTicks) {
    const commands = spec.commands(run);
    const stepped = stepRun(run, commands);
    run = stepped.run;
    const ended = spec.stopOnRunEnd && run.runEnd.result !== null;
    const last = ended || run.state.tick >= spec.maxTicks;
    record(run, commands, stepped.events, last);
    lastFullForced = last;
    if (ended) break;
  }
  if (!lastFullForced) throw new Error(`${spec.name}: loop exited without a final record`);

  const traceHash = (chain >>> 0).toString(16).padStart(8, "0");
  const header = {
    kind: "keyrun-oracle-trace",
    version: ORACLE_VERSION,
    name: spec.name,
    source: { sha: SOURCE_SHA, tree: "sim/ bots/src/ content/ client/src/sim-host/ (frozen TypeScript)" },
    scenario: spec.scenario,
    setup: spec.setupFile,
    fullEvery: FULL_EVERY,
    records: body.length,
    firstTick: 0,
    finalTick: run.state.tick,
    result: run.runEnd.result,
    resultTick: run.runEnd.resultTick,
    traceHash,
  };
  const traceFile = `${spec.name}.trace.jsonl`;
  return {
    name: spec.name,
    traceFile,
    setupFile: spec.setupFile,
    traceHash,
    records: body.length,
    finalTick: run.state.tick,
    result: run.runEnd.result,
    files: { [traceFile]: `${canonicalJson(header)}\n${body.join("\n")}\n` },
  };
}

function setupText(setup: unknown): string {
  return `${canonicalJson(setup)}\n`;
}

/** Commands addressed to the run's current tick, from a flat command list. */
function fromList(list: Command[]): CommandSource {
  const byTick = new Map<number, Command[]>();
  for (const c of list) {
    const bucket = byTick.get(c.tick) ?? [];
    bucket.push(c);
    byTick.set(c.tick, bucket);
  }
  return (run) => byTick.get(run.state.tick) ?? [];
}

function contentPull(): { spec: ScenarioSpec; setup: string } {
  const { run } = buildScenario();
  const party: PartyMemberSpec[] = Object.values(run.state.actors)
    .filter((a) => a.kind === "player")
    .map((a) => ({ id: a.id, classId: a.classId, role: a.role, pos: a.pos, maxHealth: a.maxHealth, maxResource: a.maxResource }));
  const setup = { seed: CONTENT_SEED, party, dungeon: run.dungeon, content: run.content, runOptions: null };
  return {
    spec: {
      name: "content-pull",
      setupFile: "content-pull.setup.json",
      setup,
      scenario: {
        kind: "golden",
        seed: CONTENT_SEED,
        ticks: CONTENT_TICKS,
        from: "tests/sim/golden/build-content-pull.ts buildScenario + buildCommands",
      },
      run,
      commands: fromList(buildCommands()),
      maxTicks: CONTENT_TICKS,
      stopOnRunEnd: false,
    },
    setup: setupText(setup),
  };
}

interface ScriptedFixture {
  seed: number;
  ticks: number;
  commands: Command[];
}

function scriptedPullScenario(): { spec: ScenarioSpec; setup: string } {
  const path = fileURLToPath(new URL("../sim/golden/scripted-pull.json", import.meta.url));
  const fx = JSON.parse(readFileSync(path, "utf8")) as ScriptedFixture;
  const run = createRun(fx.seed, BASE_PARTY, DUNGEON, CONTENT);
  const setup = { seed: fx.seed, party: BASE_PARTY, dungeon: DUNGEON, content: CONTENT, runOptions: null };
  return {
    spec: {
      name: "scripted-pull",
      setupFile: "scripted-pull.setup.json",
      setup,
      scenario: {
        kind: "golden",
        seed: fx.seed,
        ticks: fx.ticks,
        from: "tests/sim/golden/scripted-pull.json + tests/sim/integration/fixtures.ts",
      },
      run,
      commands: fromList(fx.commands),
      maxTicks: fx.ticks,
      stopOnRunEnd: false,
    },
    setup: setupText(setup),
  };
}

function botRun(seed: number, keyLevel: number): { spec: ScenarioSpec; setup: string } {
  const built = buildRunSetup(loadContent(), { keyLevel, dungeonId: "hollow-vault", humanClassId: "warden" });
  const run = createRun(seed, built.party, built.dungeon, built.content, built.runOptions);
  const bots: Record<number, Bot> = {};
  let memories: Record<number, BotMemory> = {};
  for (const p of built.party) {
    bots[p.id] = makeBot();
    memories[p.id] = defaultMemory(100);
  }
  let prng = seedPrng(seed);
  const commands: CommandSource = (r) => {
    const view = buildBotView(r.state, r.encounter.telegraphs.map(toBotTelegraph), EMPTY_KIT);
    const out = runBots(view, bots, memories, prng, []);
    memories = out.memories;
    prng = out.prng;
    return out.commands;
  };
  const setupFile = `hollow-vault-k${keyLevel}.setup.json`;
  const setup = {
    keyLevel,
    dungeonId: "hollow-vault",
    humanClassId: "warden",
    humanActorId: built.humanActorId,
    tankActorId: built.tankActorId,
    bossActorId: built.bossActorId,
    party: built.party,
    dungeon: built.dungeon,
    content: built.content,
    runOptions: built.runOptions,
  };
  return {
    spec: {
      name: `bots-hv-k${keyLevel}-s${seed}`,
      setupFile,
      setup,
      scenario: {
        kind: "bots",
        runSeed: seed,
        botPrngSeed: seed,
        keyLevel,
        dungeonId: "hollow-vault",
        humanClassId: "warden",
        botSlots: "all five party actors (human slot bot-driven), ascending actorId, skill dial 100",
        botView: "buildBotView(state, encounter.telegraphs.map(toBotTelegraph), EMPTY_KIT), pings []",
        maxTicks: MAX_BOT_TICKS,
      },
      run,
      commands,
      maxTicks: MAX_BOT_TICKS,
      stopOnRunEnd: true,
    },
    setup: setupText(setup),
  };
}

/** Every oracle trace, in a fixed order. Each entry's files include its trace and its setup. */
export function buildTraces(): TraceOutput[] {
  const scenarios = [contentPull(), scriptedPullScenario(), botRun(7, 2), botRun(11, 2), botRun(7, 10)];
  return scenarios.map(({ spec, setup }) => {
    const out = runTrace(spec);
    out.files[spec.setupFile] = setup;
    return out;
  });
}
