/**
 * bots/src/view.ts
 *
 * buildBotView(state, encounterTelegraphs, classKit) -- the one shared place that assembles a
 * BotView's `telegraphs` list from every real producer: sim/encounter's boss/ability telegraphs
 * (already mapped to bots' Telegraph shape by the caller -- client/server each keep their own
 * toBotTelegraph mapper, see below) plus sim/combat/floor-telegraphs.ts's deriveFloorTelegraphs
 * (hostile/friendly floor patches -- PR #47 added that producer, but nothing merged its output
 * into a bot's actual view until this file: bots/src/shared/floor.ts's hostile-patch avoidance and
 * friendly-Window approach were both wired to read `view.telegraphs`, yet no caller ever put a
 * floor patch there). Centralised here instead of duplicated per caller so a future telegraph
 * producer only needs wiring in one place.
 *
 * Existing call sites this same one-line change still needs (client/server are out of this fix's
 * edit scope -- sim/**, content/**, client/**, server/**, screen/**, tests/repo/** -- so left as a
 * documented follow-up rather than edited here; see the PR body):
 *   - client/src/sim-host/bot-commands.ts's `computeBotCommands`: replace its inline
 *     `const view: BotView = { state: run.state, telegraphs, classKit: EMPTY_CLASS_KIT };` with
 *     `const view = buildBotView(run.state, telegraphs, EMPTY_CLASS_KIT);`
 *   - server/src/room.ts's bot-tick handler: replace its inline
 *     `const view: BotView = { state: this.run.state, telegraphs: this.run.encounter.telegraphs.map(toBotTelegraph), classKit: EMPTY_CLASS_KIT };`
 *     with `const view = buildBotView(this.run.state, this.run.encounter.telegraphs.map(toBotTelegraph), EMPTY_CLASS_KIT);`
 *
 * Usage: `const view = buildBotView(run.state, run.encounter.telegraphs.map(toBotTelegraph), classKit);`
 */
import { deriveFloorTelegraphs } from "../../sim/combat/floor-telegraphs.js";
import type { BotView, SimState, Telegraph } from "./types.js";

export function buildBotView(state: SimState, encounterTelegraphs: readonly Telegraph[], classKit: BotView["classKit"]): BotView {
  return { state, telegraphs: [...encounterTelegraphs, ...deriveFloorTelegraphs(state)], classKit };
}
