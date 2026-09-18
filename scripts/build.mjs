// Bundle the browser entry points with esbuild. Usage: node scripts/build.mjs [--watch] [--serve]
// Three.js is loaded from cdnjs by client/index.html and marked external here, per client/CLAUDE.md.
import { build, context } from "esbuild";
import { existsSync } from "node:fs";

const entries = ["client/src/main.ts", "screen/src/main.ts"].filter((p) => existsSync(p));
const options = {
  entryPoints: entries,
  bundle: true,
  format: "esm",
  target: "es2022",
  outdir: "dist",
  outbase: ".",
  sourcemap: true,
  external: ["three"],
  logLevel: "info",
};
const watch = process.argv.includes("--watch");
const serve = process.argv.includes("--serve");
if (entries.length === 0) {
  console.log("[build] no entry points yet (client/src/main.ts, screen/src/main.ts)");
  process.exit(0);
}
if (watch || serve) {
  const ctx = await context(options);
  await ctx.watch();
  if (serve) {
    const { host, port } = await ctx.serve({ servedir: ".", port: 8791 });
    console.log(`[dev] serving http://${host}:${port}/client/ and /screen/`);
  }
} else {
  await build(options);
}
