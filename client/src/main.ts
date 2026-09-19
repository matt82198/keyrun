// Entry point (scripts/build.mjs's esbuild entry). All boot logic lives in boot.ts (research/11
// D9 follow-up split main.ts into boot.ts + hud-wiring.ts to stay under the repo's 190-LOC-per-file
// gate) -- this file is kept to a thin shim so the entry point name/path never has to change.
import { boot } from "./boot.js";

boot().catch((err) => {
  console.error("keyrun client failed to boot", err);
  const app = document.getElementById("app");
  if (app) app.textContent = `Failed to start: ${String(err)}`;
});
