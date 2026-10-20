/**
 * tests/oracle/merge-file.ts -- runs `git merge-file` on three texts, the way git merges a file two
 * branches both changed. Used by the oracle and golden tests to prove that two independent edits leave
 * the generated hash files (manifest.json, content-pull.json) mergeable without a conflict.
 *
 * Usage: `gitMergeFile(base, ours, theirs)` -> { clean, text }.
 */
import { spawnSync } from "node:child_process";
import { mkdtempSync, rmSync, writeFileSync } from "node:fs";
import { tmpdir } from "node:os";
import { join } from "node:path";

/** Three-way merges `ours` and `theirs` against `base`; `clean` is false when git reports a conflict. */
export function gitMergeFile(base: string, ours: string, theirs: string): { clean: boolean; text: string } {
  const dir = mkdtempSync(join(tmpdir(), "keyrun-merge-"));
  try {
    const [o, b, t] = ["ours", "base", "theirs"].map((n) => join(dir, n));
    writeFileSync(o!, ours);
    writeFileSync(b!, base);
    writeFileSync(t!, theirs);
    const r = spawnSync("git", ["merge-file", "-p", o!, b!, t!], { encoding: "utf8" });
    if (r.error !== undefined || r.status === null || r.status < 0) {
      throw new Error(`git merge-file failed to run: ${String(r.error ?? r.stderr)}`);
    }
    return { clean: r.status === 0, text: r.stdout };
  } finally {
    rmSync(dir, { recursive: true, force: true });
  }
}
