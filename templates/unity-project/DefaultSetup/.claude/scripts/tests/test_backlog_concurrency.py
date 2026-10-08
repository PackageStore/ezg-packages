"""Concurrency + resume tests for backlog-ops.py (loop lease, task claims,
resume journal, ship-pending fence).

Each test drives the REAL CLI in a fresh temp git repo, like
test_backlog_ops.py. Agent sessions are simulated with throw-away `sleep`
processes: BACKLOG_OWNER_PID names the "agent" that owns a call, killing the
process is a session dying (usage limit, crash, closed window). Loop
controllers are real `bash` processes running a stub run-backlog-loop.sh, so
the command-line + cwd scoping is exercised exactly as in production.

POSIX-only (the Windows process-table path needs a Windows box).

Run from the repo root:
    python3 -m unittest discover -s .claude/scripts/tests -v
"""

import json
import os
import shutil
import signal
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


def run_git(cwd, *args):
    return subprocess.run(["git", *args], cwd=cwd, check=True,
                          capture_output=True, text=True).stdout.strip()


@unittest.skipIf(os.name == "nt", "POSIX process table only")
class ConcurrencyTestCase(unittest.TestCase):

    def setUp(self):
        self.dir = Path(tempfile.mkdtemp(prefix="backlog-conc-test-")).resolve()
        self.addCleanup(shutil.rmtree, self.dir, ignore_errors=True)
        run_git(self.dir, "init", "-q", "-b", "main")
        run_git(self.dir, "config", "user.email", "test@test.local")
        run_git(self.dir, "config", "user.name", "test")
        (self.dir / "README.md").write_text("repo\n", encoding="utf-8")
        (self.dir / "keep.txt").write_text("base\n", encoding="utf-8")
        run_git(self.dir, "add", "-A")
        run_git(self.dir, "commit", "-qm", "init")
        self.bl = self.dir / ".git" / "backlog"
        for d in ("planning", "todo", "in-progress", "done"):
            (self.bl / d).mkdir(parents=True)
        (self.bl / "BACKLOG.md").write_text(SKELETON, encoding="utf-8")
        self.procs = []
        self.addCleanup(self._kill_all)

    # ------------------------------------------------------------------ helpers
    def _kill_all(self):
        for p in self.procs:
            if p.poll() is None:
                try:
                    os.killpg(p.pid, signal.SIGKILL)
                except OSError:
                    p.kill()
            p.wait()
            if p.stdout:
                p.stdout.close()

    def spawn(self, argv, cwd=None):
        p = subprocess.Popen(argv, cwd=cwd or self.dir, stdout=subprocess.DEVNULL,
                             stderr=subprocess.DEVNULL, start_new_session=True)
        self.procs.append(p)
        time.sleep(0.15)                 # let ps see it
        return p

    def agent(self):
        """A fake agent session — any live process can own a claim."""
        return self.spawn(["sleep", "300"])

    def claude_binary(self):
        """A symlink named `claude` to `sleep`: ps reports it exactly like the CLI.
        (A copied system binary is killed by macOS code signing; a symlink is not.)"""
        path = self.dir.parent / f"{self.dir.name}-bin" / "claude"
        if not path.exists():
            path.parent.mkdir(parents=True, exist_ok=True)
            path.symlink_to(shutil.which("sleep"))
            self.addCleanup(shutil.rmtree, path.parent, ignore_errors=True)
        return path

    def runner(self, with_agent: bool):
        """A task window of a pre-lease loop: bash running logs/backlog-loop/iter-*.run.sh.
        Without an agent it models a FAILED task's window parked at 'Press any key'."""
        logs = self.dir / "logs" / "backlog-loop"
        logs.mkdir(parents=True, exist_ok=True)
        script = logs / "iter-3-20260101-000000.run.sh"
        body = f"{self.claude_binary()} 300\n" if with_agent else "sleep 300\n"
        script.write_text("#!/usr/bin/env bash\n" + body, encoding="utf-8")
        return self.spawn(["bash", str(script)], cwd="/")

    def kill(self, p):
        os.killpg(p.pid, signal.SIGKILL)
        p.wait()

    def ops(self, *args, owner=None, token=None, expect_rc=0, cwd=None, env_extra=None):
        env = {k: v for k, v in os.environ.items()
               if k not in ("BACKLOG_OWNER_PID", "BACKLOG_LOOP_TOKEN", "AGENT_MODE")}
        env.update(env_extra or {})
        if owner is not None:
            env["BACKLOG_OWNER_PID"] = str(owner.pid)
        if token:
            env["BACKLOG_LOOP_TOKEN"] = token
        proc = subprocess.run([sys.executable, str(OPS), *args], cwd=cwd or self.dir,
                              capture_output=True, text=True, env=env)
        self.assertEqual(proc.returncode, expect_rc,
                         msg=f"args={args}\nstdout={proc.stdout}\nstderr={proc.stderr}")
        out = proc.stdout.strip()
        return json.loads(out) if out.startswith("{") else out

    def queue(self, *names):
        """Promote tasks straight into TODO (bypasses promote's planning gates)."""
        bullets = []
        for i, slug in enumerate(names, 1):
            name = f"{i:03d}-S-{slug}.md"
            (self.bl / "todo" / name).write_text(
                f"### [HIGH] {slug}\n\n**Tier:** S\n\nbody\n", encoding="utf-8")
            bullets.append(f"- [HIGH] [S] [{slug}](backlog/todo/{name})")
        text = (self.bl / "BACKLOG.md").read_text(encoding="utf-8")
        text = text.replace("## TODO\n\n- (none)\n", "## TODO\n\n" + "\n".join(bullets) + "\n", 1)
        (self.bl / "BACKLOG.md").write_text(text, encoding="utf-8")

    def run_record(self, nnn):
        return json.loads((self.bl / "runs" / f"{nnn}.json").read_text(encoding="utf-8"))

    def make_legacy_in_progress(self, slug="alpha"):
        """A task moved to IN PROGRESS by a pre-claim version (no run record)."""
        self.queue(slug)
        text = (self.bl / "BACKLOG.md").read_text(encoding="utf-8")
        text = text.replace(f"## TODO\n\n- [HIGH] [S] [{slug}](backlog/todo/001-S-{slug}.md)\n",
                            "## TODO\n\n- (none)\n")
        text = text.replace("## IN PROGRESS\n\n- (none)\n",
                            f"## IN PROGRESS\n\n- [HIGH] [S] [{slug}](backlog/in-progress/001-S-{slug}.md)\n")
        (self.bl / "BACKLOG.md").write_text(text, encoding="utf-8")
        (self.bl / "todo" / f"001-S-{slug}.md").rename(self.bl / "in-progress" / f"001-S-{slug}.md")

    def fake_controller(self, cwd=None):
        """A real `bash …/run-backlog-loop.sh` process, as a pre-lease loop runs."""
        root = Path(cwd or self.dir)
        script = root / ".claude" / "scripts" / "run-backlog-loop.sh"
        script.parent.mkdir(parents=True, exist_ok=True)
        script.write_text("#!/usr/bin/env bash\nsleep 300\n", encoding="utf-8")
        return self.spawn(["bash", ".claude/scripts/run-backlog-loop.sh", "--auto-model-by-tier"],
                          cwd=root)


class ClaimTests(ConcurrencyTestCase):

    def test_second_session_cannot_take_a_live_claim(self):
        self.queue("alpha")
        a, b = self.agent(), self.agent()
        self.ops("start", "001", owner=a)
        # Session B sees the task as busy — and changes nothing.
        out = self.ops("pick", owner=b, expect_rc=4)
        self.assertEqual((out["state"], out["reason"], out["sentinel"]),
                         ("busy", "task-claimed", "TASK_BUSY"))
        self.ops("resume", "001", owner=b, expect_rc=4)
        self.ops("checkpoint", "001", "--step", "implemented", owner=b, expect_rc=4)
        self.ops("done", "001", owner=b, expect_rc=4)
        self.ops("demote", "001", owner=b, expect_rc=4)
        self.assertTrue((self.bl / "in-progress" / "001-S-alpha.md").exists())
        self.assertEqual(len(self.run_record("001")["attempts"]), 1)
        # The owner itself still sees and resumes its own task.
        mine = self.ops("pick", owner=a)
        self.assertEqual((mine["state"], mine["claim"]["status"]), ("in-progress", "mine"))
        self.assertEqual(self.ops("resume", "001", owner=a)["actions"], ["runs/001.json: already mine"])

    def test_two_sessions_racing_start_only_one_wins(self):
        self.queue("alpha")
        a, b = self.agent(), self.agent()
        self.ops("start", "001", owner=a)
        # B's pick raced A's start and still names 001 from TODO: its start is busy.
        out = self.ops("start", "001", owner=b, expect_rc=4)
        self.assertEqual((out["reason"], out["sentinel"]), ("task-claimed", "TASK_BUSY"))
        # A re-running start on its own task is told to resume instead.
        self.ops("start", "001", owner=a, expect_rc=1)

    def test_dead_session_is_resumed_with_its_partial_work(self):
        # Dirt that exists BEFORE the task starts is not the task's work.
        (self.dir / "keep.txt").write_text("dev edit\n", encoding="utf-8")
        self.queue("alpha")
        a = self.agent()
        self.ops("start", "001", owner=a)
        (self.dir / "feature.cs").write_text("half done\n", encoding="utf-8")
        (self.dir / "README.md").write_text("repo\nchanged by task\n", encoding="utf-8")
        self.ops("checkpoint", "001", "--step", "compile", "--result", "pass", owner=a)
        self.kill(a)                                  # usage limit / crash

        b = self.agent()
        pick = self.ops("pick", owner=b)
        self.assertEqual((pick["state"], pick["resume"], pick["claim"]["status"]),
                         ("in-progress", True, "stale"))
        res = self.ops("resume", "001", owner=b)
        self.assertEqual(res["attempt"], 2)
        self.assertEqual(res["attempts"][0]["end"], "stale")
        partial = {p["path"]: p["note"] for p in res["partial_work"]}
        self.assertEqual(partial, {"feature.cs": "new since start",
                                   "README.md": "new since start"})
        self.assertEqual(res["preexisting_dirty_unchanged"], ["keep.txt"])
        self.assertEqual(res["last_checkpoint"]["step"], "compile")
        self.assertIn("CONTINUE", res["guidance"])
        # B now owns it; a third session is locked out.
        self.ops("pick", owner=self.agent(), expect_rc=4)

    def test_preexisting_dirt_edited_by_the_task_counts_as_partial_work(self):
        (self.dir / "keep.txt").write_text("dev edit\n", encoding="utf-8")
        self.queue("alpha")
        a = self.agent()
        self.ops("start", "001", owner=a)
        (self.dir / "keep.txt").write_text("dev edit\ntask edit\n", encoding="utf-8")
        self.kill(a)
        res = self.ops("resume", "001", owner=self.agent())
        self.assertEqual(res["partial_work"], [{"path": "keep.txt", "xy": " M",
                                                "note": "changed since start"}])
        self.assertEqual(res["preexisting_dirty_unchanged"], [])

    def test_resume_after_implemented_checkpoint_reruns_gates(self):
        self.queue("alpha")
        a = self.agent()
        self.ops("start", "001", owner=a)
        (self.dir / "feature.cs").write_text("done\n", encoding="utf-8")
        self.ops("checkpoint", "001", "--step", "implemented", owner=a)
        self.ops("checkpoint", "001", "--step", "qa", "--result", "pass", owner=a)
        self.kill(a)
        res = self.ops("resume", "001", owner=self.agent())
        self.assertIn("re-run EVERY gate from STEP 5b", res["guidance"])
        self.assertIn("play mode", res["guidance"])   # died between qa and smoke

    def test_resume_from_another_branch_is_a_conflict_and_changes_nothing(self):
        self.queue("alpha")
        a = self.agent()
        self.ops("start", "001", owner=a)
        self.kill(a)
        run_git(self.dir, "checkout", "-q", "-b", "other")
        out = self.ops("resume", "001", owner=self.agent(), expect_rc=5)
        self.assertEqual((out["state"], out["sentinel"]), ("conflict", "RESUME_CONFLICT"))
        self.assertIn("branch", out["mismatch"])
        self.assertEqual(len(self.run_record("001")["attempts"]), 1)

    def test_bad_checkpoint_step_is_rejected(self):
        self.queue("alpha")
        a = self.agent()
        self.ops("start", "001", owner=a)
        self.ops("checkpoint", "001", "--step", "implement", owner=a, expect_rc=1)

    def test_demote_archives_the_claim_and_restart_is_fresh(self):
        self.queue("alpha")
        a = self.agent()
        self.ops("start", "001", owner=a)
        self.ops("demote", "001", owner=a)
        self.assertFalse((self.bl / "runs" / "001.json").exists())
        self.assertTrue(any((self.bl / "runs" / "archive").glob("001-*-demoted.json")))
        self.ops("start", "001", owner=a)
        self.assertEqual(len(self.run_record("001")["attempts"]), 1)

    def test_break_task_claim_keeps_the_journal(self):
        self.queue("alpha")
        a = self.agent()
        self.ops("start", "001", owner=a)
        self.ops("checkpoint", "001", "--step", "implemented", owner=a)
        self.ops("lock", "break", "--task", "001", expect_rc=1)          # needs --yes
        self.ops("lock", "break", "--task", "001", "--yes")
        res = self.ops("resume", "001", owner=self.agent())            # a is still ALIVE
        self.assertEqual(res["attempt"], 2)
        self.assertEqual(res["last_checkpoint"]["step"], "implemented")


class GitStateTests(ConcurrencyTestCase):

    def test_partial_work_covers_renames_deletes_spaces_and_unicode(self):
        (self.dir / "old name.txt").write_text("x\n", encoding="utf-8")
        (self.dir / "gone.txt").write_text("y\n", encoding="utf-8")
        run_git(self.dir, "add", "-A")
        run_git(self.dir, "commit", "-qm", "more")
        self.queue("alpha")
        a = self.agent()
        self.ops("start", "001", owner=a)
        run_git(self.dir, "mv", "old name.txt", "new name.txt")
        (self.dir / "gone.txt").unlink()
        (self.dir / "sub dir").mkdir()
        (self.dir / "sub dir" / "t\u00e0i li\u1ec7u.cs").write_text("z\n", encoding="utf-8")
        self.kill(a)
        res = self.ops("resume", "001", owner=self.agent())
        paths = {p["path"]: p["xy"] for p in res["partial_work"]}
        self.assertEqual(paths.get("new name.txt", "")[0], "R")
        self.assertEqual(paths.get("old name.txt"), "D ")
        self.assertEqual(paths.get("gone.txt"), " D")
        self.assertIn("sub dir/t\u00e0i li\u1ec7u.cs", paths)

    def test_repo_state_reports_a_stale_index_lock_and_an_open_merge(self):
        self.queue("alpha")
        (self.dir / ".git" / "index.lock").write_text("", encoding="utf-8")
        head = run_git(self.dir, "rev-parse", "HEAD")
        (self.dir / ".git" / "MERGE_HEAD").write_text(head + "\n", encoding="utf-8")
        st = self.ops("pick", owner=self.agent())["repo_state"]
        self.assertIn("index_lock", st)
        self.assertIn("git_running", st["index_lock"])
        self.assertIn("merge_in_progress", st)

    def test_commits_landing_after_start_are_listed(self):
        self.queue("alpha")
        a = self.agent()
        self.ops("start", "001", owner=a)
        (self.dir / "README.md").write_text("dev commit\n", encoding="utf-8")
        run_git(self.dir, "commit", "-qam", "dev work meanwhile")
        self.kill(a)
        res = self.ops("resume", "001", owner=self.agent())
        self.assertEqual(len(res["commits_since_start"]), 1)
        self.assertTrue(res["commits_since_start"][0].endswith("dev work meanwhile"))


class ShipPendingTests(ConcurrencyTestCase):

    def test_done_but_uncommitted_is_shipped_before_the_next_task(self):
        self.queue("alpha", "beta")
        a = self.agent()
        self.ops("start", "001", owner=a)
        self.ops("done", "001", owner=a)
        self.kill(a)                                  # died between STEP 8 and STEP 9
        b = self.agent()
        pick = self.ops("pick", owner=b)
        self.assertEqual((pick["state"], pick["nnn"], pick["path"]),
                         ("ship-pending", "001", "backlog/done/001-S-alpha.md"))
        res = self.ops("resume", "001", owner=b)
        self.assertEqual(res["phase"], "done")
        lint = self.ops("lint")
        self.assertTrue(any("never recorded" in w for w in lint["warnings"]))
        self.ops("shipped", "001", "--commit", "abc1234", "--push", "pushed", owner=b)
        self.assertFalse((self.bl / "runs" / "001.json").exists())
        nxt = self.ops("pick", owner=b)
        self.assertEqual((nxt["state"], nxt["nnn"]), ("todo", "002"))

    def test_ship_pending_owned_by_a_live_session_is_busy(self):
        self.queue("alpha")
        a = self.agent()
        self.ops("start", "001", owner=a)
        self.ops("done", "001", owner=a)
        self.ops("pick", owner=self.agent(), expect_rc=4)

    def test_shipped_validates_its_arguments(self):
        self.queue("alpha")
        a = self.agent()
        self.ops("start", "001", owner=a)
        self.ops("shipped", "001", "--commit", "abc", owner=a, expect_rc=1)   # not done yet
        self.ops("done", "001", owner=a)
        self.ops("shipped", "001", owner=a, expect_rc=1)                      # no --commit
        self.ops("shipped", "001", "--commit", "abc", "--push", "maybe", owner=a, expect_rc=1)

    def test_pre_claim_task_finished_by_old_skill_gets_no_fence(self):
        # The old skill never calls `shipped` nor writes the trailer: a fence would
        # make the next run re-commit whatever happens to be dirty.
        self.make_legacy_in_progress()
        a = self.agent()
        self.assertEqual(self.ops("pick", owner=a)["claim"]["status"], "none")
        self.ops("done", "001", owner=a)
        self.assertFalse((self.bl / "runs" / "001.json").exists())
        self.kill(a)
        self.assertEqual(self.ops("pick", owner=self.agent(), expect_rc=2)["state"], "empty")

    def test_pre_claim_task_resumed_by_new_skill_is_fenced(self):
        self.make_legacy_in_progress()
        a = self.agent()
        res = self.ops("resume", "001", owner=a)
        self.assertTrue(res["legacy"])
        self.assertEqual(res["partial_work"], [])          # clean tree
        self.ops("done", "001", owner=a)
        self.kill(a)
        self.assertEqual(self.ops("pick", owner=self.agent())["state"], "ship-pending")

    def test_ship_recovery_sees_the_trailer_commit_and_does_not_recommit(self):
        self.queue("alpha")
        a = self.agent()
        self.ops("start", "001", owner=a)
        (self.dir / "feature.cs").write_text("done\n", encoding="utf-8")
        self.ops("done", "001", owner=a)
        run_git(self.dir, "add", "-A")
        run_git(self.dir, "commit", "-qm", "feat: alpha", "-m", "Backlog-Task: 001")
        self.kill(a)                              # died between commit and `shipped`
        (self.dir / "unity-auto-dirt.asset").write_text("x\n", encoding="utf-8")
        b = self.agent()
        res = self.ops("resume", "001", owner=b)
        self.assertEqual(len(res["task_commits"]), 1)
        self.assertTrue(res["task_commits"][0].endswith("feat: alpha"))
        self.assertIn("do NOT commit again", res["guidance"])
        self.ops("shipped", "001", "--commit", res["task_commits"][0].split()[0], owner=b)
        # shipped twice is harmless (record closed)
        self.assertIn("nothing to close", self.ops("shipped", "001", "--commit", "x", owner=b)["note"])

    def test_ship_recovery_without_a_task_commit_says_commit_now(self):
        self.queue("alpha")
        a = self.agent()
        self.ops("start", "001", owner=a)
        self.ops("done", "001", owner=a)
        self.kill(a)
        res = self.ops("resume", "001", owner=self.agent())
        self.assertEqual(res["task_commits"], [])
        self.assertIn("run STEP 9 now", res["guidance"])

    def test_ship_recovery_finds_the_commit_by_its_recorded_tree(self):
        # STEP 9e keeps the message trailer-free and records the staged tree instead.
        self.queue("alpha")
        a = self.agent()
        self.ops("start", "001", owner=a)
        (self.dir / "feature.cs").write_text("done\n", encoding="utf-8")
        self.ops("done", "001", owner=a)
        run_git(self.dir, "add", "--", "feature.cs")
        tree = run_git(self.dir, "write-tree")
        cp = self.ops("checkpoint", "001", "--step", "commit", "--tree", tree, owner=a)
        self.assertEqual(cp["commit_tree"]["tree"], tree)
        run_git(self.dir, "commit", "-qm", "+ Feat: add alpha")
        self.assertNotIn("Backlog-Task", run_git(self.dir, "log", "-1", "--format=%B"))
        self.kill(a)                              # died between commit and `shipped`
        (self.dir / "unity-auto-dirt.asset").write_text("x\n", encoding="utf-8")
        b = self.agent()
        res = self.ops("resume", "001", owner=b)
        self.assertEqual(len(res["task_commits"]), 1)
        self.assertTrue(res["task_commits"][0].endswith("+ Feat: add alpha"))
        self.assertIn("do NOT commit again", res["guidance"])
        sha = res["task_commits"][0].split()[0]
        found = self.ops("task-commit", "001")
        self.assertEqual((found["commit"], found["source"]), (sha, "commit-tree"))
        self.ops("shipped", "001", "--commit", sha, "--push", "no-remote", owner=b)
        found = self.ops("task-commit", "001")          # now from the archived record
        self.assertEqual((found["commit"], found["source"]), (sha, "shipped"))

    def test_commit_checkpoint_needs_a_done_task_and_a_full_tree_sha(self):
        self.queue("alpha")
        a = self.agent()
        self.ops("start", "001", owner=a)
        tree = run_git(self.dir, "write-tree")
        self.ops("checkpoint", "001", "--step", "commit", "--tree", tree, owner=a, expect_rc=1)
        self.ops("done", "001", owner=a)
        self.ops("checkpoint", "001", "--step", "commit", owner=a, expect_rc=1)
        self.ops("checkpoint", "001", "--step", "commit", "--tree", tree[:7], owner=a, expect_rc=1)
        self.ops("checkpoint", "001", "--step", "nonsense", owner=a, expect_rc=1)
        self.ops("checkpoint", "001", "--step", "commit", "--tree", tree, owner=a)
        # Only the owning session may record it.
        self.ops("checkpoint", "001", "--step", "commit", "--tree", tree, owner=self.agent(),
                 expect_rc=4)

    def test_a_tree_that_predates_the_task_is_never_its_commit(self):
        self.queue("alpha")
        a = self.agent()
        self.ops("start", "001", owner=a)
        self.ops("done", "001", owner=a)
        old_tree = run_git(self.dir, "write-tree")        # == the init commit's tree
        self.ops("checkpoint", "001", "--step", "commit", "--tree", old_tree, owner=a)
        self.kill(a)
        res = self.ops("resume", "001", owner=self.agent())
        self.assertEqual(res["task_commits"], [])           # HEAD never moved
        (self.dir / "other.txt").write_text("dev\n", encoding="utf-8")
        run_git(self.dir, "add", "-A")
        run_git(self.dir, "commit", "-qm", "dev work")
        self.assertIsNone(self.ops("task-commit", "001")["commit"])

    def test_task_commit_of_an_already_satisfied_task_is_null(self):
        self.queue("alpha")
        a = self.agent()
        self.ops("start", "001", owner=a)
        self.ops("done", "001", owner=a)
        head = run_git(self.dir, "rev-parse", "--short", "HEAD")
        self.ops("shipped", "001", "--commit", head, "--push", "skipped",
                 "--note", "already-satisfied", owner=a)
        found = self.ops("task-commit", "001")
        self.assertEqual((found["commit"], found["source"]), (None, "no-commit"))
        unknown = self.ops("task-commit", "002")
        self.assertEqual((unknown["commit"], unknown["source"]), (None, None))   # not "no-commit"

    def commit_task(self, owner, nnn="001", subject="+ Feat: add alpha"):
        (self.dir / f"feature-{nnn}.cs").write_text(f"{nnn}\n", encoding="utf-8")
        self.ops("done", nnn, owner=owner)
        run_git(self.dir, "add", "--", f"feature-{nnn}.cs")
        self.ops("checkpoint", nnn, "--step", "commit", "--tree", run_git(self.dir, "write-tree"),
                 owner=owner)
        run_git(self.dir, "commit", "-qm", subject)
        return run_git(self.dir, "rev-parse", "--short", "HEAD")

    def test_task_started_on_an_unborn_branch_still_finds_its_commit(self):
        # A project fresh from `git init` has no commit: `start` records no base head.
        run_git(self.dir, "checkout", "-q", "--orphan", "fresh")
        self.queue("alpha")
        a = self.agent()
        self.ops("start", "001", owner=a)
        self.assertIsNone(self.run_record("001")["base"].get("head"))
        sha = self.commit_task(a)
        self.kill(a)
        res = self.ops("resume", "001", owner=self.agent())
        self.assertEqual([c.split()[0] for c in res["task_commits"]], [sha])
        self.assertIn("do NOT commit again", res["guidance"])
        self.assertEqual(self.ops("task-commit", "001")["commit"], sha)

    def test_a_later_empty_commit_with_the_same_tree_is_not_the_task_commit(self):
        self.queue("alpha")
        a = self.agent()
        self.ops("start", "001", owner=a)
        sha = self.commit_task(a)
        run_git(self.dir, "commit", "-q", "--allow-empty", "-m", "build flags")
        self.kill(a)
        res = self.ops("resume", "001", owner=self.agent())
        self.assertEqual([c.split()[0] for c in res["task_commits"]], [sha])
        self.assertEqual(self.ops("task-commit", "001")["commit"], sha)

    def test_only_an_already_satisfied_close_counts_as_no_commit(self):
        self.queue("alpha", "beta")
        a = self.agent()
        self.ops("start", "001", owner=a)
        sha = self.commit_task(a)
        # A real commit recorded with `--push skipped` (no remote) is still a commit.
        self.ops("shipped", "001", "--commit", sha, "--push", "skipped", owner=a)
        self.assertEqual(self.ops("task-commit", "001")["commit"], sha)
        # `--push skipped` naming the task's own base head (no note) = already satisfied.
        self.ops("start", "002", owner=a)
        self.ops("done", "002", owner=a)
        head = run_git(self.dir, "rev-parse", "--short", "HEAD")
        self.ops("shipped", "002", "--commit", head, "--push", "skipped", owner=a)
        self.assertEqual(self.ops("task-commit", "002")["source"], "no-commit")


class LoopLeaseTests(ConcurrencyTestCase):

    def acquire(self, controller, expect_rc=0):
        return self.ops("lock", "acquire", "--pid", str(controller.pid), "--mode", "current",
                        "--work-dir", str(self.dir), "--branch", "main", expect_rc=expect_rc)

    def test_one_loop_per_clone(self):
        self.queue("alpha")
        c1 = self.spawn(["sleep", "300"])
        lease = self.acquire(c1)
        token = lease["token"]
        self.assertEqual(len(token), 32)
        # A second controller is refused while the first lives.
        c2 = self.spawn(["sleep", "300"])
        out = self.acquire(c2, expect_rc=4)
        self.assertEqual((out["reason"], out["sentinel"]), ("loop-active", "LOOP_BUSY"))
        self.assertNotIn("token", out["lease"])                 # never leak the token
        # A hand-run /run-backlog without the token is refused; the loop's own iteration is not.
        self.assertEqual(self.ops("pick", owner=self.agent(), expect_rc=4)["reason"], "loop-active")
        self.assertEqual(self.ops("pick", owner=self.agent(), token=token)["state"], "todo")
        self.ops("start", "001", owner=self.agent(), expect_rc=4)
        self.ops("defer", "001", owner=self.agent(), expect_rc=4)
        # Re-acquire by the same controller is idempotent.
        self.assertTrue(self.acquire(c1)["reacquired"])
        # Wrong token releases nothing; the right one frees the clone.
        self.assertFalse(self.ops("lock", "release", "--token", "nope")["released"])
        self.assertTrue(self.ops("lock", "release", "--token", token)["released"])
        self.acquire(c2)

    def test_a_dead_controller_lease_is_replaced(self):
        c1 = self.spawn(["sleep", "300"])
        self.acquire(c1)
        self.kill(c1)                                            # kill -9: no trap ran
        self.assertEqual(self.ops("pick", owner=self.agent(), expect_rc=2)["state"], "empty")
        c2 = self.spawn(["sleep", "300"])
        out = self.acquire(c2)
        self.assertIn("replaced_stale", out["lease"])

    def test_lease_is_refused_while_an_orphaned_task_session_is_alive(self):
        self.queue("alpha")
        orphan = self.agent()                     # task window of a killed controller
        self.ops("start", "001", owner=orphan)
        out = self.acquire(self.spawn(["sleep", "300"]), expect_rc=4)
        self.assertEqual(out["reason"], "task-claimed")
        self.kill(orphan)
        self.acquire(self.spawn(["sleep", "300"]))

    def test_lock_status_reports_busy_and_free(self):
        self.assertEqual(self.ops("lock", "status")["state"], "free")
        c1 = self.spawn(["sleep", "300"])
        self.acquire(c1)
        st = self.ops("lock", "status", expect_rc=4)
        self.assertEqual((st["state"], st["lease"]["liveness"]), ("busy", "live"))
        self.ops("lock", "break", expect_rc=1)                    # needs --yes
        self.ops("lock", "break", "--yes")
        self.assertEqual(self.ops("lock", "status")["state"], "free")

    def test_pre_lease_loop_of_this_clone_blocks_hand_runs(self):
        self.queue("alpha")
        self.fake_controller()
        out = self.ops("pick", owner=self.agent(), expect_rc=4)
        self.assertEqual((out["reason"], out["sentinel"]), ("legacy-loop", "LOOP_BUSY"))
        # …and a new controller will not start next to it either.
        self.assertEqual(self.acquire(self.spawn(["sleep", "300"]), expect_rc=4)["reason"],
                         "legacy-loop")

    def test_a_loop_of_another_clone_is_ignored(self):
        self.queue("alpha")
        other = Path(tempfile.mkdtemp(prefix="backlog-other-clone-")).resolve()
        self.addCleanup(shutil.rmtree, other, ignore_errors=True)
        self.fake_controller(cwd=other)
        self.assertEqual(self.ops("pick", owner=self.agent())["state"], "todo")
        self.acquire(self.spawn(["sleep", "300"]))

    def test_mention_of_the_script_is_not_a_loop(self):
        self.queue("alpha")
        self.spawn(["bash", "-c", "grep -q run-backlog-loop.sh /dev/null; sleep 300"])
        self.assertEqual(self.ops("pick", owner=self.agent())["state"], "todo")


class MutexTests(ConcurrencyTestCase):

    HOLD = ("import fcntl, os, sys, time; fd = os.open(sys.argv[1], os.O_RDWR | os.O_CREAT);"
            "fcntl.flock(fd, fcntl.LOCK_EX); print('held', flush=True); time.sleep(300)")

    def hold_mutex(self):
        runs = self.bl / "runs"
        runs.mkdir(exist_ok=True)
        p = subprocess.Popen([sys.executable, "-c", self.HOLD, str(runs / ".mutex")],
                             stdout=subprocess.PIPE, start_new_session=True, text=True)
        self.procs.append(p)
        self.assertEqual(p.stdout.readline().strip(), "held")
        return p

    def test_leftover_mutex_file_never_blocks(self):
        self.queue("alpha")
        (self.bl / "runs").mkdir(exist_ok=True)
        (self.bl / "runs" / ".mutex").write_text('{"stale": true}', encoding="utf-8")
        self.ops("start", "001", owner=self.agent())

    def test_live_holder_blocks_and_its_death_releases_at_once(self):
        self.queue("alpha")
        holder = self.hold_mutex()
        a = self.agent()
        proc = subprocess.run([sys.executable, str(OPS), "start", "001"], cwd=self.dir,
                              capture_output=True, text=True,
                              env={**os.environ, "BACKLOG_OWNER_PID": str(a.pid),
                                   "BACKLOG_MUTEX_WAIT_S": "0.5"})
        self.assertEqual(proc.returncode, 1)
        self.assertIn("mutex busy", proc.stderr)
        self.assertTrue((self.bl / "todo" / "001-S-alpha.md").exists())
        self.kill(holder)                         # kill -9: the kernel drops the lock
        self.ops("start", "001", owner=a)


class DetectionTests(ConcurrencyTestCase):

    def test_npm_launched_agents_are_recognised(self):
        import importlib.util
        spec = importlib.util.spec_from_file_location("backlog_ops_mod", OPS)
        mod = importlib.util.module_from_spec(spec)
        spec.loader.exec_module(mod)
        for args in (r"C:\Program Files\nodejs\node.exe C:\Users\u\AppData\Roaming\npm\node_modules\@anthropic-ai\claude-code\cli.js -p",
                     "node /usr/local/lib/node_modules/@google/gemini-cli/dist/index.js",
                     "node /opt/homebrew/lib/node_modules/@openai/codex/bin/codex.js exec",
                     "node /Users/u/.local/bin/claude --verbose"):
            self.assertTrue(mod.is_agent({"name": "node", "args": args}), args)
        self.assertFalse(mod.is_agent({"name": "node", "args": "node server.js --port 3000"}))

    def test_loop_command_lines_are_matched_on_the_interpreter_only(self):
        import importlib.util
        spec = importlib.util.spec_from_file_location("backlog_ops_mod", OPS)
        mod = importlib.util.module_from_spec(spec)
        spec.loader.exec_module(mod)
        controllers = {
            "bash .claude/scripts/run-backlog-loop.sh --auto-model-by-tier": True,
            "/bin/bash ./run-backlog-loop.sh": True,
            'bash "/Users/x/My Proj/.claude/scripts/run-backlog-loop.sh"': True,
            r"C:\WINDOWS\System32\WindowsPowerShell\v1.0\powershell.exe -NoExit -ExecutionPolicy Bypass -File D:\P\.claude\scripts\run-backlog-loop-claude.ps1": True,
            r'"C:\Program Files\PowerShell\7\pwsh.exe" -File "D:\My P\.claude\scripts\run-backlog-loop-core.ps1" -Mode Current': True,
            r'C:\WINDOWS\system32\cmd.exe /c ""D:\P\.claude\scripts\run-backlog-loop.bat" "': True,
            # mentions are not loops
            "/bin/zsh -c source x.sh && { bash .claude/scripts/run-backlog-loop.sh; }": False,
            "bash -c grep -q run-backlog-loop.sh /dev/null": False,
            "sed -n 1,60p .claude/scripts/run-backlog-loop.sh": False,
            r"notepad D:\P\.claude\scripts\run-backlog-loop-core.ps1": False,
            r"powershell -Command Get-Content .claude\scripts\run-backlog-loop-core.ps1": False,
        }
        for args, want in controllers.items():
            self.assertEqual(mod.is_controller({"args": args}), want, args)
        self.assertTrue(mod.is_runner({"args": "bash /r/logs/backlog-loop/iter-6-20260928-233440.run.sh"}))
        self.assertTrue(mod.is_runner({"args": r'"C:\W\powershell.exe" -NoProfile -File D:\P\logs\backlog-loop\iter-claude-20260928-233440-001.log.run.ps1'}))
        self.assertFalse(mod.is_runner({"args": "cat logs/backlog-loop/iter-6-20260928-233440.run.sh"}))

    def test_failed_task_window_does_not_block_the_relaunch(self):
        self.queue("alpha")
        self.runner(with_agent=False)             # parked at "Press any key", agent gone
        self.assertEqual(self.ops("pick", owner=self.agent())["state"], "todo")
        self.assertEqual(self.ops("lock", "status")["state"], "free")

    def test_a_live_task_window_of_an_old_loop_is_a_foreign_worker(self):
        self.make_legacy_in_progress()
        self.runner(with_agent=True)
        out = self.ops("pick", owner=self.agent(), expect_rc=4)
        self.assertIn(out["reason"], ("legacy-loop", "unclaimed-task-foreign-loop"))

    def test_sibling_clone_with_a_prefix_name_is_not_this_clone(self):
        self.queue("alpha")
        sibling = Path(str(self.dir) + "2")
        (sibling / ".claude" / "scripts").mkdir(parents=True)
        self.addCleanup(shutil.rmtree, sibling, ignore_errors=True)
        script = sibling / ".claude" / "scripts" / "run-backlog-loop.sh"
        script.write_text("#!/usr/bin/env bash\nsleep 300\n", encoding="utf-8")
        self.spawn(["bash", str(script)], cwd="/")
        self.assertEqual(self.ops("pick", owner=self.agent())["state"], "todo")

    def test_host_name_is_recorded_but_never_compared(self):
        c1 = self.spawn(["sleep", "300"])
        self.ops("lock", "acquire", "--pid", str(c1.pid))
        lease_path = self.bl / "runs" / "loop.json"
        lease = json.loads(lease_path.read_text(encoding="utf-8"))
        lease["holder"]["host"] = "renamed-by-dhcp.local"
        lease_path.write_text(json.dumps(lease), encoding="utf-8")
        st = self.ops("lock", "status", expect_rc=4)
        self.assertEqual(st["lease"]["liveness"], "live")
        self.assertTrue(self.ops("lock", "acquire", "--pid", str(c1.pid))["reacquired"])


if __name__ == "__main__":
    unittest.main()
