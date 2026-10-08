"""`park` tests for backlog-ops.py — the self-heal loop's give-up path.

A task whose self-heal budget is spent is parked: moved to the tail of TODO so
the loop carries on, with its partial work saved to refs/backlog/parked/<NNN>
and taken out of the tree (the next task's `git add -A` must not sweep it, and
half-done code must not break the next task's compile check).

Cross-platform: agent sessions are throw-away Python sleepers named through
BACKLOG_OWNER_PID; killing one is the session dying.

Run from the repo root:
    python3 -m unittest discover -s .claude/scripts/tests -p "test_backlog_park.py" -v
"""

import json
import os
import shutil
import subprocess
import sys
import tempfile
import time
import unittest
from pathlib import Path

REPO = Path(__file__).resolve().parents[3]
OPS = REPO / ".claude" / "scripts" / "backlog-ops.py"

SKELETON = """# Backlog

## TODO

- (none)

## IN PROGRESS

- (none)

## DONE

- (none)
"""


def git(cwd, *args):
    return subprocess.run(["git", *args], cwd=cwd, check=True, capture_output=True,
                          text=True).stdout.strip()


class ParkTests(unittest.TestCase):

    def setUp(self):
        self.dir = Path(tempfile.mkdtemp(prefix="backlog-park-test-")).resolve()
        self.addCleanup(shutil.rmtree, self.dir, ignore_errors=True)
        git(self.dir, "init", "-q", "-b", "main")
        git(self.dir, "config", "user.email", "test@test.local")
        git(self.dir, "config", "user.name", "test")
        git(self.dir, "config", "core.autocrlf", "false")
        (self.dir / "README.md").write_text("repo\n", encoding="utf-8")
        (self.dir / "keep.txt").write_text("base\n", encoding="utf-8")
        git(self.dir, "add", "-A")
        git(self.dir, "commit", "-qm", "init")
        self.bl = self.dir / ".git" / "backlog"
        for d in ("planning", "todo", "in-progress", "done"):
            (self.bl / d).mkdir(parents=True)
        (self.bl / "BACKLOG.md").write_text(SKELETON, encoding="utf-8")
        self.procs = []
        self.addCleanup(self._kill_all)

    def _kill_all(self):
        for p in self.procs:
            if p.poll() is None:
                p.kill()
            p.wait()

    def agent(self):
        p = subprocess.Popen([sys.executable, "-c", "import time; time.sleep(300)"],
                             stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
        self.procs.append(p)
        time.sleep(0.3)
        return p

    def kill(self, p):
        p.kill()
        p.wait()
        time.sleep(0.2)

    def ops(self, *args, owner=None, expect_rc=0):
        env = {k: v for k, v in os.environ.items()
               if k not in ("BACKLOG_OWNER_PID", "BACKLOG_LOOP_TOKEN", "AGENT_MODE")}
        env["PYTHONUTF8"] = "1"
        if owner is not None:
            env["BACKLOG_OWNER_PID"] = str(owner.pid)
        proc = subprocess.run([sys.executable, str(OPS), *args], cwd=self.dir,
                              capture_output=True, text=True, env=env, encoding="utf-8")
        self.assertEqual(proc.returncode, expect_rc,
                         msg=f"args={args}\nstdout={proc.stdout}\nstderr={proc.stderr}")
        out = proc.stdout.strip()
        return json.loads(out) if out.startswith("{") else out

    def queue(self, *names):
        bullets = []
        for i, slug in enumerate(names, 1):
            name = f"{i:03d}-S-{slug}.md"
            (self.bl / "todo" / name).write_text(
                f"### [HIGH] {slug}\n\n**Tier:** S\n\nbody\n", encoding="utf-8")
            bullets.append(f"- [HIGH] [S] [{slug}](backlog/todo/{name})")
        text = (self.bl / "BACKLOG.md").read_text(encoding="utf-8")
        text = text.replace("## TODO\n\n- (none)\n", "## TODO\n\n" + "\n".join(bullets) + "\n", 1)
        (self.bl / "BACKLOG.md").write_text(text, encoding="utf-8")

    def todo_order(self):
        text = (self.bl / "BACKLOG.md").read_text(encoding="utf-8")
        section = text.split("## TODO", 1)[1].split("## IN PROGRESS", 1)[0]
        return [line.split("](backlog/todo/")[1][:3] for line in section.splitlines()
                if line.startswith("- [")]

    def status(self):
        return git(self.dir, "status", "--porcelain", "--untracked-files=all")

    # ------------------------------------------------------------------ tests
    def test_park_saves_partial_work_and_moves_task_to_tail(self):
        (self.dir / "keep.txt").write_text("dev edit\n", encoding="utf-8")    # pre-existing dirt
        self.queue("alpha", "beta")
        a = self.agent()
        self.ops("start", "001", owner=a)
        (self.dir / "README.md").write_text("repo\nhalf done\n", encoding="utf-8")
        (self.dir / "Feature").mkdir()
        (self.dir / "Feature" / "New.cs").write_text("class New {}\n", encoding="utf-8")
        (self.dir / "Feature.meta").write_text("guid: 1\n", encoding="utf-8")
        git(self.dir, "add", "-A")
        self.kill(a)                                     # the run died / budget spent

        out = self.ops("park", "001", "--reason", "REVIEW_BLOCKED x4")
        self.assertTrue(out["ok"], msg=out)
        self.assertTrue(out["work"]["saved"], msg=out)
        self.assertEqual(sorted(out["work"]["files"]), ["Feature.meta", "Feature/New.cs", "README.md"])
        # Task: tail of TODO, file in todo/, claim archived, note appended.
        self.assertEqual(self.todo_order(), ["002", "001"])
        task = self.bl / "todo" / "001-S-alpha.md"
        self.assertTrue(task.exists())
        self.assertIn("## Parked", task.read_text(encoding="utf-8"))
        self.assertIn("REVIEW_BLOCKED x4", task.read_text(encoding="utf-8"))
        self.assertFalse((self.bl / "runs" / "001.json").exists())
        self.assertTrue(any((self.bl / "runs" / "archive").glob("001-*-parked.json")))
        self.assertTrue(out["lint"]["ok"], msg=out)
        # Tree: the task's work is gone, the dev's pre-existing dirt is untouched.
        self.assertEqual(self.status(), "M  keep.txt")    # still staged by the task's `add -A`
        self.assertEqual((self.dir / "README.md").read_text(encoding="utf-8"), "repo\n")
        self.assertFalse((self.dir / "Feature").exists())
        self.assertEqual((self.dir / "keep.txt").read_text(encoding="utf-8"), "dev edit\n")
        # The saved commit holds exactly the task's work on top of HEAD.
        ref = "refs/backlog/parked/001"
        self.assertEqual(git(self.dir, "show", f"{ref}:README.md"), "repo\nhalf done")
        self.assertEqual(git(self.dir, "show", f"{ref}:Feature/New.cs"), "class New {}")
        self.assertEqual(git(self.dir, "show", f"{ref}:keep.txt"), "base")
        self.assertEqual(git(self.dir, "rev-parse", f"{ref}^1"), git(self.dir, "rev-parse", "HEAD"))

    def test_next_start_reports_parked_work_and_shipped_deletes_it(self):
        self.queue("alpha")
        a = self.agent()
        self.ops("start", "001", owner=a)
        (self.dir / "New.cs").write_text("wip\n", encoding="utf-8")
        self.kill(a)
        self.ops("park", "001")
        self.assertEqual(self.status(), "")

        b = self.agent()
        start = self.ops("start", "001", owner=b)
        self.assertEqual(start["parked_work"]["ref"], "refs/backlog/parked/001")
        self.assertEqual(start["parked_work"]["files"], ["A\tNew.cs"])
        git(self.dir, "checkout", "refs/backlog/parked/001", "--", "New.cs")   # restore
        git(self.dir, "commit", "-qm", "alpha", "-m", "Backlog-Task: 001")
        self.ops("done", "001", owner=b)
        shipped = self.ops("shipped", "001", "--commit", git(self.dir, "rev-parse", "--short", "HEAD"),
                           "--push", "skipped", owner=b)
        self.assertIn("refs/backlog/parked/001: deleted (task shipped)", shipped["actions"])
        self.assertEqual(subprocess.run(["git", "rev-parse", "--verify", "--quiet",
                                         "refs/backlog/parked/001"], cwd=self.dir).returncode, 1)

    def test_parking_twice_keeps_the_first_snapshot_reachable(self):
        self.queue("alpha")
        for content in ("first\n", "second\n"):
            a = self.agent()
            self.ops("start", "001", owner=a)
            (self.dir / "New.cs").write_text(content, encoding="utf-8")
            self.kill(a)
            self.ops("park", "001")
        ref = "refs/backlog/parked/001"
        self.assertEqual(git(self.dir, "show", f"{ref}:New.cs"), "second")
        self.assertEqual(git(self.dir, "show", f"{ref}^2:New.cs"), "first")

    def test_park_refuses_a_live_owner_and_changes_nothing(self):
        self.queue("alpha")
        a = self.agent()
        self.ops("start", "001", owner=a)
        (self.dir / "New.cs").write_text("wip\n", encoding="utf-8")
        out = self.ops("park", "001", owner=self.agent(), expect_rc=4)
        self.assertEqual(out["sentinel"], "TASK_BUSY")
        self.assertTrue((self.bl / "in-progress" / "001-S-alpha.md").exists())
        self.assertTrue((self.dir / "New.cs").exists())

    def test_keep_work_leaves_the_tree_alone(self):
        self.queue("alpha")
        a = self.agent()
        self.ops("start", "001", owner=a)
        (self.dir / "New.cs").write_text("wip\n", encoding="utf-8")
        self.kill(a)
        out = self.ops("park", "001", "--keep-work")
        self.assertFalse(out["work"]["saved"])
        self.assertTrue((self.dir / "New.cs").exists())
        self.assertTrue((self.bl / "todo" / "001-S-alpha.md").exists())

    def test_parking_a_todo_task_moves_its_bullet_to_the_tail(self):
        self.queue("alpha", "beta", "gamma")
        out = self.ops("park", "001", "--reason", "EDITOR_REQUIRED")
        self.assertIsNone(out["work"])
        self.assertEqual(self.todo_order(), ["002", "003", "001"])
        self.assertTrue(out["lint"]["ok"], msg=out)

    def test_park_restores_deleted_and_renamed_files(self):
        self.queue("alpha")
        a = self.agent()
        self.ops("start", "001", owner=a)
        git(self.dir, "mv", "keep.txt", "moved.txt")
        (self.dir / "README.md").unlink()
        self.kill(a)
        out = self.ops("park", "001")
        self.assertTrue(out["work"]["saved"], msg=out)
        self.assertEqual(self.status(), "")
        self.assertTrue((self.dir / "keep.txt").exists())
        self.assertTrue((self.dir / "README.md").exists())
        self.assertFalse((self.dir / "moved.txt").exists())


if __name__ == "__main__":
    unittest.main()
