// server/src/run.mjs -- `npm run server`'s actual entry point.
//
// Plain esbuild-bundle-then-run bootstrap (same esbuild devDependency and es2022/esm conventions
// scripts/build.mjs uses for the browser entries), needed because main.ts's relative imports use
// ".js" specifiers pointing at sibling ".ts" files (the repo's established TS-source convention --
// see tsconfig.json's "moduleResolution": "Bundler") which Node's own native TypeScript execution
// does not resolve on its own. esbuild already understands that resolution (it is what
// scripts/build.mjs and vitest's own esbuild-backed transform both rely on), so bundling once and
// running the plain-JS result sidesteps the gap without adding a runtime TS loader dependency.
//
// The bundle is written under dist/ (already .gitignore'd, same as scripts/build.mjs's own
// outdir) rather than the OS temp dir, so its bare `import "ws"` resolves the normal Node way --
// walking up to this repo's node_modules -- instead of needing `ws` bundled in too (which trips
// over ws's own conditional `require("events")`/`require("stream")` under esbuild's CJS-in-ESM
// interop).
import { build } from "esbuild";
import { cp, mkdir } from "node:fs/promises";
import { pathToFileURL } from "node:url";

const outDir = "dist/server-run";
const outfile = `${outDir}/main.mjs`;
await mkdir(outDir, { recursive: true });

await build({
  entryPoints: ["server/src/main.ts"],
  outfile,
  bundle: true,
  platform: "node",
  format: "esm",
  target: "es2022",
  external: ["ws"],
  logLevel: "warning",
});

// content/index.ts's loadContent() reads JSON via `new URL("./data/...", import.meta.url)`;
// bundling collapses every module's `import.meta.url` to this one outfile, so its "./data" lookup
// needs a real "data" directory sitting next to it -- mirror content/data/ there.
await cp("content/data", `${outDir}/data`, { recursive: true });

const mod = await import(pathToFileURL(outfile).href);
mod.boot();
