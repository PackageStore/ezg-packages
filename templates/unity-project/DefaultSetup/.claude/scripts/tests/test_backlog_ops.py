"""Roundtrip + lint tests for backlog-ops.py (run end-to-end via subprocess).

backlog-ops resolves the backlog via `git rev-parse --git-common-dir` in the
process cwd, so every test runs the REAL script from the repo with cwd set to a
fresh temp git repo carrying a skeleton `.git/backlog/`. No monkeypatching —
each test exercises the exact shipped CLI behavior.

Nothing under `.git/backlog/` is ever git-tracked, which is the point of the
location: it is per-developer bookkeeping shared by every worktree of a clone,
invisible to merges. Tests therefore never `git add` a task file.

Run from the repo root:
    python3 -m unittest discover -s .claude/scripts/tests -v
"""

import json
import re
import shutil
import subprocess
import sys
import tempfile
import unittest
from pathlib import Path

REPO = Path(__file__).resolve().parents[3]
OPS = REPO / ".claude" / "scripts" / "backlog-ops.py"

SKELETON = """# Backlog

Index only — task bodies live in backlog/{todo,in-progress,done}/.

## TODO

- (none)

## IN PROGRESS

- (none)

## DONE

- (none)
"""


def run_git(cwd, *args):
    subprocess.run(["git", *args], cwd=cwd, check=True,
                   capture_output=True, text=True)


class OpsTestCase(unittest.TestCase):
    """Fresh temp repo per test with a clean skeleton index."""

    def setUp(self):
        self.dir = Path(tempfile.mkdtemp(prefix="backlog-ops-test-"))
        self.addCleanup(shutil.rmtree, self.dir, ignore_errors=True)
        run_git(self.dir, "init", "-q")
        run_git(self.dir, "config", "user.email", "test@test.local")
        run_git(self.dir, "config", "user.name", "test")
        (self.dir / "README.md").write_text("repo\n", encoding="utf-8")
        run_git(self.dir, "add", "-A")
        run_git(self.dir, "commit", "-qm", "init")
        # The backlog root — .git/backlog/, shared by every worktree of the clone.
        self.bl = self.dir / ".git" / "backlog"
        for d in ("planning", "todo", "in-progress", "done"):
            (self.bl / d).mkdir(parents=True)
        (self.bl / "BACKLOG.md").write_text(SKELETON, encoding="utf-8")

    def ops(self, *args, expect_rc=0):
        proc = subprocess.run(
            [sys.executable, str(OPS), *args],
            cwd=self.dir, capture_output=True, text=True)
        self.assertEqual(proc.returncode, expect_rc,
                         msg=f"args={args}\nstdout={proc.stdout}\nstderr={proc.stderr}")
        return json.loads(proc.stdout) if proc.stdout.strip().startswith(("{", "[")) else proc.stdout

    def write_planning(self, name, heading="### [HIGH] Test task", extra=""):
        p = self.bl / "planning" / name
        match = re.match(r"^\d{8}T\d{6,9}(?:-\d+)?-(XS|S|M|L)-", name)
        tier = match.group(1) if match else "M"
        p.write_text(f"{heading}\n\n**Tier:** {tier}\n\n{extra}\nBody.\n", encoding="utf-8")
        return p

    def backlog_text(self):
        return (self.bl / "BACKLOG.md").read_text(encoding="utf-8")


class BootstrapTests(OpsTestCase):
    """The backlog is never committed, so a fresh clone starts without one.
    `init`/`promote` bootstrap it; every other command must fail LOUDLY rather
    than auto-create — a `pick` that quietly made an empty backlog would report
    `state: empty`, and the loop would announce "backlog is empty" when the real
    fault is a misconfigured checkout."""

    def wipe(self):
        shutil.rmtree(self.bl)

    def test_init_creates_backlog_root_and_lints_clean(self):
        self.wipe()
        r = self.ops("init")
        self.assertTrue(r["ok"], msg=r)
        self.assertTrue((self.bl / "BACKLOG.md").exists())
        for d in ("planning", "todo", "in-progress", "done"):
            self.assertTrue((self.bl / d).is_dir(), msg=d)
        self.assertTrue(r["lint"]["ok"], msg=r)
        self.assertEqual(self.ops("pick", expect_rc=2)["state"], "empty")

    def test_init_is_idempotent(self):
        r = self.ops("init")
        self.assertEqual(r["actions"], ["already initialised"])

    def test_pick_on_missing_backlog_exits_3_not_empty(self):
        self.wipe()
        r = self.ops("pick", expect_rc=3)
        self.assertFalse(r["ok"])
        self.assertNotEqual(r.get("state"), "empty")
        self.assertIn("init", r["hint"])

    def test_lint_on_missing_backlog_exits_3(self):
        self.wipe()
        self.ops("lint", expect_rc=3)

    def test_promote_bootstraps_a_missing_backlog(self):
        self.wipe()
        (self.bl / "planning").mkdir(parents=True)
        self.write_planning("20260101T000000001-S-first-ever.md")
        r = self.ops("promote", "backlog/planning/20260101T000000001-S-first-ever.md")
        self.assertTrue(r["ok"], msg=r)
        self.assertTrue((self.bl / "todo" / "001-S-first-ever.md").exists())
        self.assertTrue(r["lint"]["ok"], msg=r)


class BasicCommandTests(OpsTestCase):

    def test_timestamp_format(self):
        out = self.ops("timestamp")
        self.assertRegex(out.strip(), r"^\d{8}T\d{9}$")

    def test_lint_clean_skeleton(self):
        r = self.ops("lint")
        self.assertTrue(r["ok"], msg=r)
        self.assertEqual(r["errors"], [])

    def test_pick_empty_exits_2(self):
        r = self.ops("pick", expect_rc=2)
        self.assertEqual(r["state"], "empty")


class LintErrorTests(OpsTestCase):

    def test_lint_catches_leaked_markup(self):
        text = self.backlog_text().replace("## DONE", "<content>\n\n## DONE")
        (self.bl / "BACKLOG.md").write_text(text, encoding="utf-8")
        r = self.ops("lint", expect_rc=1)
        self.assertFalse(r["ok"])
        self.assertTrue(any("markup" in e for e in r["errors"]), msg=r)

    def test_lint_catches_orphan_and_dual_state(self):
        for d in ("todo", "in-progress"):
            (self.bl / d / "001-ghost.md").write_text("x\n", encoding="utf-8")
        r = self.ops("lint", expect_rc=1)
        self.assertTrue(any("dual-state" in e for e in r["errors"]), msg=r)
        self.assertTrue(any("no bullet" in e for e in r["errors"]), msg=r)

    def test_lint_forbids_done_bullets(self):
        (self.bl / "done" / "001-old.md").write_text("x\n", encoding="utf-8")
        text = self.backlog_text().replace(
            "## DONE\n\n- (none)",
            "## DONE\n\n- [HIGH] [M] [Old](backlog/done/001-old.md)")
        (self.bl / "BACKLOG.md").write_text(text, encoding="utf-8")
        r = self.ops("lint", expect_rc=1)
        self.assertTrue(any("DONE bullet forbidden" in e for e in r["errors"]), msg=r)

    def test_lint_catches_planning_filename_body_tier_mismatch(self):
        p = self.write_planning("20260101T000000001-M-tier-mismatch.md")
        p.write_text(p.read_text(encoding="utf-8").replace("**Tier:** M", "**Tier:** S"),
                     encoding="utf-8")
        r = self.ops("lint", expect_rc=1)
        self.assertTrue(any("filename=M, body=S" in e for e in r["errors"]), msg=r)

    def test_lint_catches_active_filename_body_bullet_tier_mismatch(self):
        task = self.bl / "todo" / "001-M-tiered.md"
        task.write_text("### [HIGH] Tiered\n\n**Tier:** L\n", encoding="utf-8")
        text = self.backlog_text().replace(
            "## TODO\n\n- (none)",
            "## TODO\n\n- [HIGH] [S] [Tiered](backlog/todo/001-M-tiered.md)")
        (self.bl / "BACKLOG.md").write_text(text, encoding="utf-8")
        r = self.ops("lint", expect_rc=1)
        self.assertTrue(any("filename=M, body=L, bullet=S" in e for e in r["errors"]), msg=r)


class PromoteLifecycleTests(OpsTestCase):

    def test_promote_check_clean_is_read_only(self):
        source = "backlog/planning/20260101T000000001-M-clean-check.md"
        self.write_planning("20260101T000000001-M-clean-check.md")
        r = self.ops("promote", "--check", source)
        self.assertTrue(r["ok"], msg=r)
        self.assertTrue(r["check_only"])
        self.assertEqual(r["moved"], [])
        self.assertTrue((self.bl.parent / source).exists())
        self.assertFalse((self.bl / "todo" / "001-M-clean-check.md").exists())
        self.assertIn("## TODO\n\n- (none)", self.backlog_text())

    def test_promote_start_done_roundtrip(self):
        self.write_planning("20260101T000000001-M-glory-pass.md")

        r = self.ops("promote", "backlog/planning/20260101T000000001-M-glory-pass.md")
        self.assertTrue(r["ok"], msg=r)
        self.assertEqual(r["moved"][0]["nnn"], "001")
        self.assertEqual(r["moved"][0]["tier"], "M")
        self.assertTrue((self.bl / "todo" / "001-M-glory-pass.md").exists())
        self.assertIn("- [HIGH] [M] [Test task](backlog/todo/001-M-glory-pass.md)",
                      self.backlog_text())
        self.assertNotIn("## TODO\n\n- (none)", self.backlog_text())
        self.assertTrue(r["lint"]["ok"], msg=r)

        p = self.ops("pick")
        self.assertEqual((p["state"], p["nnn"], p["tier"]), ("todo", "001", "M"))

        r = self.ops("start", "001")
        self.assertTrue(r["ok"], msg=r)
        self.assertTrue((self.bl / "in-progress" / "001-M-glory-pass.md").exists())
        self.assertTrue(r["lint"]["ok"], msg=r)

        p = self.ops("pick")
        self.assertEqual((p["state"], p["resume"]), ("in-progress", True))

        r = self.ops("done", "001")
        self.assertTrue(r["ok"], msg=r)
        self.assertTrue((self.bl / "done" / "001-M-glory-pass.md").exists())
        # `- (none)` is restored right under the IN PROGRESS header (the script
        # does not guarantee a blank line between header and placeholder)
        self.assertRegex(self.backlog_text(), r"## IN PROGRESS\n+- \(none\)")
        self.assertTrue(r["lint"]["ok"], msg=r)

        # atomic-write hygiene: no tmp file may linger after any transition
        self.assertFalse((self.bl / "BACKLOG.md.tmp").exists())

    def test_promote_moves_file_without_touching_git(self):
        # The backlog lives inside .git/, so a transition is a plain filesystem
        # move and must never stage anything — a `git mv` here would hard-fail
        # ('not under version control') and dead-end an entire batch promote.
        self.write_planning("20260101T000000001-S-quick-fix.md")
        r = self.ops("promote", "backlog/planning/20260101T000000001-S-quick-fix.md")
        self.assertTrue(r["ok"], msg=r)
        self.assertTrue((self.bl / "todo" / "001-S-quick-fix.md").exists())
        staged = subprocess.run(["git", "diff", "--cached", "--name-only"],
                                cwd=self.dir, capture_output=True, text=True).stdout
        self.assertEqual(staged.strip(), "", msg=f"promote staged files: {staged!r}")

    def test_promote_accepts_a_bare_filename(self):
        # /add-to-backlog and the planning skills pass whatever spelling is at
        # hand; a bare name must resolve against the backlog's planning dir.
        self.write_planning("20260101T000000001-S-bare-name.md")
        r = self.ops("promote", "20260101T000000001-S-bare-name.md")
        self.assertTrue(r["ok"], msg=r)
        self.assertTrue((self.bl / "todo" / "001-S-bare-name.md").exists())

    def test_promote_warns_on_missing_dependency(self):
        self.write_planning(
            "20260101T000000001-M-dependent.md",
            extra="**Depends on:** 20990101T000000000-M-nonexistent\n")
        r = self.ops("promote", "--check",
                     "backlog/planning/20260101T000000001-M-dependent.md", expect_rc=1)
        self.assertFalse(r["ok"], msg=r)
        self.assertEqual(len(r["dependency_warnings"]), 1)
        self.assertFalse((self.bl / "todo" / "001-M-dependent.md").exists())
        self.assertTrue((self.bl / "planning" /
                         "20260101T000000001-M-dependent.md").exists())

        # The mutating command enforces the same preflight even if a caller
        # forgets to run --check first.
        r = self.ops("promote", "backlog/planning/20260101T000000001-M-dependent.md",
                     expect_rc=1)
        self.assertFalse(r["ok"], msg=r)
        self.assertEqual(r["moved"], [])

    def test_dependency_line_keeps_only_the_file_reference_not_the_prose(self):
        # Review revisions annotate this line ("`x.md` (chỉ một chiều — `-12-` là **chủ**)").
        # Comma-splitting kept the commentary inside the token, so a satisfied dependency
        # matched nothing and promote blocked a batch that was actually well-ordered.
        self.write_planning("20260101T000000001-M-first.md")
        self.write_planning(
            "20260101T000000002-S-second.md",
            extra="**Depends on:** `20260101T000000001-M-first.md` (chỉ một chiều — task `-01-` là **chủ**)\n")
        r = self.ops("promote", "--check",
                     "backlog/planning/20260101T000000001-M-first.md",
                     "backlog/planning/20260101T000000002-S-second.md")
        self.assertTrue(r["ok"], msg=r)
        self.assertEqual(r["dependency_warnings"], [])

    def test_promote_batch_keeps_task_order_regardless_of_argv(self):
        self.write_planning("20260101T000000001-M-first.md")
        self.write_planning("20260101T000000002-S-second.md")
        # argv deliberately reversed — the script must sort by (timestamp, index)
        r = self.ops("promote",
                     "backlog/planning/20260101T000000002-S-second.md",
                     "backlog/planning/20260101T000000001-M-first.md")
        self.assertTrue(r["ok"], msg=r)
        self.assertEqual([m["nnn"] for m in r["moved"]], ["001", "002"])
        self.assertEqual(r["moved"][0]["path"], "backlog/todo/001-M-first.md")

    def test_defer_moves_bullet_to_tail_and_noop_when_single(self):
        self.write_planning("20260101T000000001-M-first.md")
        self.write_planning("20260101T000000002-S-second.md")
        self.ops("promote",
                 "backlog/planning/20260101T000000001-M-first.md",
                 "backlog/planning/20260101T000000002-S-second.md")

        r = self.ops("defer", "001")
        self.assertTrue(r["ok"], msg=r)
        p = self.ops("pick")
        self.assertEqual(p["nnn"], "002")

        # single-bullet defer is a no-op with an explanatory note
        self.ops("start", "002")
        self.ops("done", "002")
        r = self.ops("defer", "001")
        self.assertIn("no-op", r.get("note", ""), msg=r)


class MockupWarningTests(OpsTestCase):
    """promote preflight blocks before mutation while a /new-ui task's
    groundTruth is still a PENDING-* marker (see /ui-mockup)."""

    def test_promote_warns_on_pending_mockup(self):
        self.write_planning(
            "20260101T000000001-M-shop-screen.md",
            extra="**Backed by workflow:** `/new-ui`\n"
                  "**Workflow args:** `ShopScreen | groundTruth=PENDING-MOCKUP`\n")
        r = self.ops("promote", "--check",
                     "backlog/planning/20260101T000000001-M-shop-screen.md", expect_rc=1)
        self.assertFalse(r["ok"], msg=r)
        self.assertEqual(len(r["mockup_warnings"]), 1)
        self.assertIn("PENDING-MOCKUP", r["mockup_warnings"][0])
        self.assertFalse((self.bl / "todo" / "001-M-shop-screen.md").exists())

    def test_promote_warns_on_pending_approval_with_path(self):
        self.write_planning(
            "20260101T000000001-S-gem-pack.md",
            extra="**Workflow args:** `GemPack | "
                  "groundTruth=PENDING-APPROVAL:TechSpec/Mockups/GemPack/GemPack.html`\n")
        r = self.ops("promote", "--check",
                     "backlog/planning/20260101T000000001-S-gem-pack.md", expect_rc=1)
        self.assertEqual(len(r["mockup_warnings"]), 1)
        self.assertIn("PENDING-APPROVAL:TechSpec/Mockups/GemPack/GemPack.html",
                      r["mockup_warnings"][0])

    def test_promote_silent_on_approved_or_clone_groundtruth(self):
        self.write_planning(
            "20260101T000000001-M-approved.md",
            extra="**Workflow args:** `X | groundTruth=TechSpec/Mockups/X/X.png`\n")
        self.write_planning(
            "20260101T000000002-M-cloned.md",
            extra="**Workflow args:** `Y | groundTruth=clone:DailyGift`\n")
        r = self.ops("promote",
                     "backlog/planning/20260101T000000001-M-approved.md",
                     "backlog/planning/20260101T000000002-M-cloned.md")
        self.assertEqual(r["mockup_warnings"], [])

    def test_pending_marker_inside_html_comment_is_ignored(self):
        # Template files document the markers inside <!-- --> comments — a
        # leftover comment must never trigger the warning (comment-skip parity
        # with DEPENDS_RE parsing).
        self.write_planning(
            "20260101T000000001-M-commented.md",
            extra="<!--\nWorkflow args: `X | groundTruth=PENDING-MOCKUP`\n-->\n")
        r = self.ops("promote", "backlog/planning/20260101T000000001-M-commented.md")
        self.assertEqual(r["mockup_warnings"], [])

    def test_promote_warns_on_newui_signal_without_any_groundtruth(self):
        # The /new-feature-HYBRID hole: a task that authors a new screen (it
        # references /new-ui) but emits NO groundTruth token at all silently
        # skipped the mockup gate. It must now block until the author makes an
        # explicit mockup decision.
        self.write_planning(
            "20260101T000000001-M-evolution-popup.md",
            extra="**Backed by workflow:** `/new-feature`\n"
                  "**Required skills:** `/compile-check`, `/new-ui` (prefab work)\n")
        r = self.ops("promote", "--check",
                     "backlog/planning/20260101T000000001-M-evolution-popup.md",
                     expect_rc=1)
        self.assertFalse(r["ok"], msg=r)
        self.assertEqual(len(r["mockup_warnings"]), 1)
        self.assertIn("references /new-ui", r["mockup_warnings"][0])
        self.assertFalse(
            (self.bl / "todo" / "001-M-evolution-popup.md").exists())

    def test_promote_silent_on_newui_with_none_or_pending_decision(self):
        # An explicit decision satisfies the gate: `none` (only wiring an
        # existing screen) is silent; PENDING-MOCKUP is caught by the OTHER
        # branch (draft-not-yet-approved), never double-warned.
        self.write_planning(
            "20260101T000000001-M-wire-only.md",
            extra="**Required skills:** `/new-ui`\n"
                  "**Mockup:** groundTruth=none — only wires an existing button\n")
        self.write_planning(
            "20260101T000000002-M-new-screen.md",
            extra="**Required skills:** `/new-ui`\n"
                  "**Mockup:** groundTruth=PENDING-MOCKUP (screen=Foo/Bar)\n")
        r = self.ops(
            "promote", "--check",
            "backlog/planning/20260101T000000001-M-wire-only.md",
            "backlog/planning/20260101T000000002-M-new-screen.md",
            expect_rc=1)
        # Only the PENDING task warns, and it warns once (pending branch).
        self.assertEqual(len(r["mockup_warnings"]), 1)
        self.assertIn("PENDING-MOCKUP", r["mockup_warnings"][0])
        self.assertIn("new-screen", r["mockup_warnings"][0])

    def test_promote_silent_when_no_newui_signal(self):
        # A task that never references /new-ui is not a UI-screen task; absence
        # of a groundTruth token must NOT warn (no over-blocking of plain work).
        self.write_planning(
            "20260101T000000001-M-plain-logic.md",
            extra="**Backed by workflow:** `/new-feature`\n"
                  "**Required skills:** `/compile-check`\n")
        r = self.ops("promote",
                     "backlog/planning/20260101T000000001-M-plain-logic.md")
        self.assertEqual(r["mockup_warnings"], [])


if __name__ == "__main__":
    unittest.main()
