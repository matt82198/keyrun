/**
 * tests/bots/real-content-setup.ts
 *
 * Side-effecting import for any tests/bots/*.test.ts file that exercises real content-driven
 * class kits (kits.test.ts, damage-behavior.test.ts, class-specific-roles.test.ts): injects
 * content's real ContentRegistry into bots/src/kits.ts once per test file. kits.ts deliberately
 * never loads content/ itself -- content/index.ts's loadContent() reads JSON via node:fs, and
 * bots/ is bundled into the browser through client/src/sim-host/worker.ts, so kits.ts takes its
 * registry by injection instead (see bots/src/kits.ts's header and setContentRegistry()).
 *
 * Usage: `import "./real-content-setup.js";` before any getClassKit()/resolveKit()/damageDecide()
 * call that needs real warden/cinder/chanter data.
 */
import { loadContent } from "@content/index";
import { setContentRegistry } from "@bots/src/kits.js";

setContentRegistry(loadContent());
