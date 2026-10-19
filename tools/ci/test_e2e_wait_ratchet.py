"""Tests for tools/ci/e2e_wait_ratchet.py. Fixtures live in tempfile dirs; nothing touches the cwd."""
import contextlib
import io
import json
import tempfile
import unittest
from pathlib import Path

import e2e_wait_ratchet as ratchet

SPEC_DIR = "tests/client/e2e"

# Shape of the pre-fix window-ping spec: two wall-clock pacing sleeps plus the retry-less
# ping read. Only the sleep calls matter to the guard.
WINDOW_PING_BASE = """\
import { setTimeout as sleep } from "node:timers/promises";
async function pingAndRead(page) {
  await page.keyboard.press("KeyG");
  await page.waitForFunction((n) => window.__keyrunPings.labels().length > n, 0, { timeout: 3_000 });
}
it("spot", async () => {
  await sleep(1_100); // ping rate limit: one per 20 ticks
  await pingAndRead(page);
});
it("target", async () => {
  for (let i = 0; i < 12; i++) {
    await sleep(1_100);
    await pingAndRead(page);
  }
});
"""

# The escape shape: a third wall-clock sleep is added in front of the single ping read, so
# the spec again paces presses by wall time instead of waiting on exposed state.
WINDOW_PING_ESCAPE = WINDOW_PING_BASE.replace(
    "async function pingAndRead(page) {\n",
    "async function pingAndRead(page) {\n  await page.waitForTimeout(1_100);\n",
)

CLEAN_SPEC = """\
it("waits on exposed state", async () => {
  await page.keyboard.press("KeyG");
  await page.waitForFunction(() => window.__keyrunPings.labels().length > 0, undefined, { timeout: 3_000 });
});
"""


def baseline_json(counts):
    return json.dumps({"total": sum(counts.values()), "counts": counts})


class FixtureCase(unittest.TestCase):
    def setUp(self):
        self._tmp = tempfile.TemporaryDirectory()
        self.root = Path(self._tmp.name)
        (self.root / SPEC_DIR).mkdir(parents=True)
        self.baseline = self.root / "baseline.json"

    def tearDown(self):
        self._tmp.cleanup()

    def write_spec(self, name, text):
        (self.root / SPEC_DIR / name).write_text(text, encoding="utf-8")

    def write_under_spec_dir(self, relative, text):
        """Write any file below tests/client/e2e (nested directories are created)."""
        path = self.root / SPEC_DIR / relative
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_text(text, encoding="utf-8")

    def write_baseline(self, text):
        self.baseline.write_text(text, encoding="utf-8")

    def run_gate(self):
        out, err = io.StringIO(), io.StringIO()
        with contextlib.redirect_stdout(out), contextlib.redirect_stderr(err):
            code = ratchet.main(["--root", str(self.root), "--baseline", str(self.baseline)])
        return code, out.getvalue(), err.getvalue()


class EscapeReproductionTest(FixtureCase):
    """Recreates e2e-window-ping-flake: the guard must flag the wall-clock wait that was added."""

    def test_added_wall_clock_wait_in_window_ping_is_flagged(self):
        self.write_spec("window-ping.e2e.ts", WINDOW_PING_ESCAPE)
        self.write_baseline(baseline_json({f"{SPEC_DIR}/window-ping.e2e.ts": 2}))
        code, _, err = self.run_gate()
        self.assertEqual(code, 1, err)
        self.assertIn("window-ping.e2e.ts: 2 -> 3", err)

    def test_pre_fix_window_ping_at_baseline_passes(self):
        # The recorded state (2 waits in the fixture, baseline 2) is the ratchet's ceiling, not a violation.
        self.write_spec("window-ping.e2e.ts", WINDOW_PING_BASE)
        self.write_baseline(baseline_json({f"{SPEC_DIR}/window-ping.e2e.ts": 2}))
        code, _, err = self.run_gate()
        self.assertEqual(code, 0, err)

    def test_new_spec_adding_a_sleep_is_flagged(self):
        self.write_spec("new-flaky.e2e.ts", 'await sleep(500);\n')
        self.write_baseline(baseline_json({}))
        code, _, err = self.run_gate()
        self.assertEqual(code, 1, err)
        self.assertIn("new-flaky.e2e.ts: 0 -> 1", err)

    def test_new_spec_adding_waitForTimeout_is_flagged(self):
        self.write_spec("new-flaky.e2e.ts", 'await page.waitForTimeout(800);\n')
        self.write_baseline(baseline_json({}))
        code, _, err = self.run_gate()
        self.assertEqual(code, 1, err)


class CleanStateTest(FixtureCase):
    """No false positives: clean specs and an accurate baseline pass."""

    def test_spec_with_only_waitForFunction_passes_without_baseline_entry(self):
        self.write_spec("clean.e2e.ts", CLEAN_SPEC)
        self.write_baseline(baseline_json({}))
        code, _, err = self.run_gate()
        self.assertEqual(code, 0, err)

    def test_decrease_passes_and_notes_the_lower_baseline(self):
        self.write_spec("status-net.e2e.ts", CLEAN_SPEC)
        self.write_baseline(baseline_json({f"{SPEC_DIR}/status-net.e2e.ts": 1}))
        code, out, err = self.run_gate()
        self.assertEqual(code, 0, err)
        self.assertIn("1 -> 0", out)

    def test_baseline_entry_for_missing_spec_passes_with_note(self):
        self.write_spec("clean.e2e.ts", CLEAN_SPEC)
        self.write_baseline(baseline_json({f"{SPEC_DIR}/gone.e2e.ts": 2}))
        code, out, err = self.run_gate()
        self.assertEqual(code, 0, err)
        self.assertIn("gone.e2e.ts", out)

    def test_counts_both_call_forms_once_each(self):
        text = 'await sleep(1);\nawait page.waitForTimeout(2);\n// sleepy and sleeping are not calls\n'
        self.assertEqual(ratchet.count_waits(text), 2)


class FailClosedTest(FixtureCase):
    """Exit 2 whenever the gate cannot evaluate, so a broken baseline never blesses a regression."""

    def test_missing_baseline_exits_2(self):
        self.write_spec("clean.e2e.ts", CLEAN_SPEC)
        code, _, err = self.run_gate()
        self.assertEqual(code, 2, err)

    def test_unparseable_baseline_exits_2(self):
        self.write_spec("clean.e2e.ts", CLEAN_SPEC)
        self.write_baseline("{not json")
        code, _, err = self.run_gate()
        self.assertEqual(code, 2, err)

    def test_total_mismatch_exits_2(self):
        self.write_spec("clean.e2e.ts", CLEAN_SPEC)
        self.write_baseline(json.dumps({"total": 99, "counts": {f"{SPEC_DIR}/a.e2e.ts": 1}}))
        code, _, err = self.run_gate()
        self.assertEqual(code, 2, err)

    def test_boolean_count_exits_2(self):
        self.write_spec("clean.e2e.ts", CLEAN_SPEC)
        self.write_baseline(json.dumps({"total": 1, "counts": {f"{SPEC_DIR}/a.e2e.ts": True}}))
        code, _, err = self.run_gate()
        self.assertEqual(code, 2, err)

    def test_no_specs_found_exits_2(self):
        self.write_baseline(baseline_json({}))
        code, _, err = self.run_gate()
        self.assertEqual(code, 2, err)


class RawTimerBypassTest(FixtureCase):
    """Bypass 1: a wall-clock wait written without the sleep alias (raw setTimeout or waitForTimeout)."""

    def test_raw_settimeout_promise_wait_is_flagged(self):
        self.write_spec("raw.e2e.ts", 'it("x", async () => {\n  await new Promise((r) => setTimeout(r, 1100));\n});\n')
        self.write_baseline(baseline_json({}))
        code, _, err = self.run_gate()
        self.assertEqual(code, 1, err)
        self.assertIn("raw.e2e.ts: 0 -> 1", err)

    def test_timers_promises_namespace_call_is_flagged(self):
        self.write_spec("ns.e2e.ts", 'import * as timers from "node:timers/promises";\nawait timers.setTimeout(1100);\n')
        self.write_baseline(baseline_json({}))
        code, _, err = self.run_gate()
        self.assertEqual(code, 1, err)
        self.assertIn("ns.e2e.ts: 0 -> 1", err)

    def test_page_waitForTimeout_call_is_flagged(self):
        self.write_spec("wft.e2e.ts", 'await page.waitForTimeout(1100);\n')
        self.write_baseline(baseline_json({}))
        code, _, err = self.run_gate()
        self.assertEqual(code, 1, err)
        self.assertIn("wft.e2e.ts: 0 -> 1", err)


class AliasedWaitBypassTest(FixtureCase):
    """Bypass 2: a setTimeout imported under another name, or a local helper whose body waits on the clock."""

    def test_aliased_settimeout_import_call_is_flagged(self):
        self.write_spec(
            "alias.e2e.ts",
            'import { setTimeout as pause } from "node:timers/promises";\nawait pause(1100);\n',
        )
        self.write_baseline(baseline_json({}))
        code, _, err = self.run_gate()
        self.assertEqual(code, 1, err)
        self.assertIn("alias.e2e.ts: 0 -> 1", err)

    def test_local_delay_helper_wrapping_settimeout_is_flagged(self):
        self.write_spec(
            "helper.e2e.ts",
            "function delay(ms) {\n  return new Promise((r) => setTimeout(r, ms));\n}\n"
            "it(\"x\", async () => {\n  await delay(1100);\n});\n",
        )
        self.write_baseline(baseline_json({}))
        code, _, err = self.run_gate()
        self.assertEqual(code, 1, err)
        self.assertIn("helper.e2e.ts: 0 -> 2", err)

    def test_nested_spec_directory_is_scanned(self):
        self.write_under_spec_dir("nested/deep.e2e.ts", "await sleep(500);\n")
        self.write_baseline(baseline_json({}))
        code, _, err = self.run_gate()
        self.assertEqual(code, 1, err)
        self.assertIn("nested/deep.e2e.ts: 0 -> 1", err)

    def test_wait_helper_module_in_nested_directory_is_flagged(self):
        self.write_under_spec_dir(
            "helpers/wait.ts",
            "export const wait = (ms: number) => new Promise((r) => setTimeout(r, ms));\n",
        )
        self.write_spec("clean.e2e.ts", CLEAN_SPEC)
        self.write_baseline(baseline_json({}))
        code, _, err = self.run_gate()
        self.assertEqual(code, 1, err)
        self.assertIn("helpers/wait.ts: 0 -> 1", err)


class CommentAndStringTest(FixtureCase):
    """Bypass 3 hardening: only executed code counts. Comments and string literals never do."""

    def test_wait_text_inside_comments_does_not_count(self):
        text = (
            "// await sleep(1100);\n"
            "/* await page.waitForTimeout(800); setTimeout(r, 5) */\n"
            'it("waits on exposed state", async () => {});\n'
        )
        self.write_spec("commented.e2e.ts", text)
        self.write_baseline(baseline_json({}))
        code, _, err = self.run_gate()
        self.assertEqual(code, 0, err)

    def test_wait_text_inside_string_literal_does_not_count(self):
        self.write_spec("quoted.e2e.ts", 'const note = "await sleep(1100)";\nconst t = `page.waitForTimeout(9)`;\n')
        self.write_baseline(baseline_json({}))
        code, _, err = self.run_gate()
        self.assertEqual(code, 0, err)

    def test_real_wait_after_a_comment_still_counts(self):
        self.assertEqual(ratchet.count_waits("// pacing note: sleep(1)\nawait sleep(1100);\n"), 1)

    def test_wait_inside_template_expression_still_counts(self):
        self.assertEqual(ratchet.count_waits("const x = `${await sleep(5)}`;\n"), 1)

    def test_helper_declaration_itself_is_not_a_call(self):
        self.assertEqual(ratchet.count_waits("function delay(ms) { return ms; }\n"), 0)


class RepositoryTest(unittest.TestCase):
    """The committed baseline must describe the committed specs."""

    def test_repository_holds_its_baseline(self):
        out, err = io.StringIO(), io.StringIO()
        with contextlib.redirect_stdout(out), contextlib.redirect_stderr(err):
            code = ratchet.main([])
        self.assertEqual(code, 0, err.getvalue())


if __name__ == "__main__":
    unittest.main()
