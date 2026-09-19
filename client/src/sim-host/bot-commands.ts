/**
 * client/src/sim-host/bot-commands.ts
 *
 * Runs every bot in one bots/src/bot.ts `runBots()` call. Per-class rotation/Setup/Payoff/resource
 * behaviour no longer needs a per-class BotView.classKit grouping here: bots/src/kits.ts's
 * `getClassKit()` resolves the real, content-derived ClassKit per actor internally (via the
 * ContentRegistry injected once by run-setup.ts's `setContentRegistry()` call), falling back to
 * `view.classKit` only for a classId content doesn't define -- which never happens in this
 * playable-v1 (every party member is warden/cinder/chanter). `EMPTY_CLASS_KIT` below is therefore
 * a placeholder that is structurally required but never actually read.
 *
 * `pings` forwards the party's live G-pings (client/src/input/ping.ts's PingEvent, structurally
 * identical to bots/src/shared/pings.ts's own copy) so bots/src/bot.ts's pingReactionCommands can
 * kick a pinged interruptible cast, chase a spot ping, or Payoff a pinged Window carrier.
 *
 * Usage: `const result = computeBotCommands(run, bots, memories, prng, pings);`
 */
import type { Run } from "../../../sim/index.js";
import type { ActorId } from "../../../sim/core/state.js";
import type { Command } from "../../../sim/core/commands.js";
import type { Bot, BotMemory } from "../../../bots/src/bot.js";
import { runBots } from "../../../bots/src/bot.js";
import type { BotView } from "../../../bots/src/types.js";
import { buildBotView } from "../../../bots/src/view.js";
import type { PrngState } from "../../../bots/src/skill.js";
import type { PingEvent } from "../../../bots/src/shared/pings.js";
import { toBotTelegraph } from "./telegraph-map.js";

export interface BotCommandsResult {
  commands: Command[];
  memories: Record<ActorId, BotMemory>;
  prng: PrngState;
}

const EMPTY_CLASS_KIT: BotView["classKit"] = { rotation: [], interruptAbilityId: null, defensiveAbilityId: null, healAbilityId: null };

export function computeBotCommands(
  run: Run,
  bots: Record<ActorId, Bot>,
  memories: Record<ActorId, BotMemory>,
  prng: PrngState,
  pings: readonly PingEvent[],
): BotCommandsResult {
  const view = buildBotView(run.state, run.encounter.telegraphs.map(toBotTelegraph), EMPTY_CLASS_KIT);
  const result = runBots(view, bots, memories, prng, pings);
  return { commands: result.commands, memories: result.memories, prng: result.prng };
}
