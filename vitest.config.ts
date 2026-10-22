import { defineConfig } from "vitest/config";

// The only TypeScript suite left is the lane row-file gate (tests/lanes). The frozen TS sim, client,
// bots, screen and server were removed; tests/sim/golden stays as oracle data (R-ORACLE-1) and its
// .ts files no longer run (their imports into sim/ are gone).
export default defineConfig({
  test: { include: ["tests/lanes/**/*.test.ts"], environment: "node" },
});
