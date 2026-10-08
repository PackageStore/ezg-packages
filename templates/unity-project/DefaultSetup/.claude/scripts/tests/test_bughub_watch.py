"""End-to-end tests for bughub-watch.py (đồng bộ nhánh cho cửa sổ /fix-bug --watch).

Mỗi test dựng một bare repo làm `origin` + một clone `dev` mang bản copy của script và
project_profile.py (script đọc profile nằm cạnh nó), rồi chạy CLI thật qua subprocess — không
monkeypatch. Không đụng repo thật, không mạng.

Run from the repo root:
    python3 -m unittest discover -s .claude/scripts/tests -v
"""

import json
import os
import shutil
import subprocess
import sys
import tempfile
import unittest
from pathlib import Path

REPO = Path(__file__).resolve().parents[3]
SCRIPTS = REPO / ".claude" / "scripts"


def git(cwd, *args):
    return subprocess.run(["git", *args], cwd=cwd, check=True, capture_output=True, text=True).stdout.strip()


class WatchTestCase(unittest.TestCase):
    WATCH = {"branch": "AutoFixBug", "baseBranch": "develop", "mergeToBase": False, "worktree": False}

    def setUp(self):
        self.tmp = Path(tempfile.mkdtemp(prefix="bughub-watch-"))
        self.addCleanup(shutil.rmtree, self.tmp, ignore_errors=True)
        self.origin = self.tmp / "origin.git"
        git(self.tmp, "init", "-q", "--bare", str(self.origin))
        self.dev = self.tmp / "dev"
        git(self.tmp, "clone", "-q", str(self.origin), str(self.dev))
        self._identity(self.dev)
        git(self.dev, "checkout", "-q", "-b", "develop")
        scripts = self.dev / ".claude" / "scripts"
        scripts.mkdir(parents=True)
        for name in ("bughub-watch.py", "project_profile.py"):
            shutil.copy(SCRIPTS / name, scripts / name)
        self.write_profile(self.WATCH)
        (self.dev / "a.txt").write_text("a\n")
        git(self.dev, "add", "-A")
        git(self.dev, "commit", "-qm", "init")
        git(self.dev, "push", "-q", "-u", "origin", "develop")
        # Một clone thứ hai đóng vai dev khác đẩy lên develop.
        self.other = self.tmp / "other"
        git(self.tmp, "clone", "-q", "-b", "develop", str(self.origin), str(self.other))
        self._identity(self.other)

    @staticmethod
    def _identity(cwd):
        git(cwd, "config", "user.email", "t@example.com")
        git(cwd, "config", "user.name", "t")

    def write_profile(self, watch):
        profile = {"bugHub": {"projectCode": "T1", "watch": watch}}
        (self.dev / ".claude" / "project-profile.json").write_text(json.dumps(profile))

    def run_watch(self, cwd, *args):
        env = dict(os.environ, PATH=os.environ["PATH"])
        proc = subprocess.run([sys.executable, str(self.dev / ".claude" / "scripts" / "bughub-watch.py"), *args],
                              cwd=cwd, capture_output=True, encoding="utf-8", env=env, stdin=subprocess.DEVNULL)
        lines = [line for line in proc.stdout.splitlines() if line.strip()]
        return proc.returncode, json.loads(lines[-1]) if lines else {}

    def push_from_other(self, name, content):
        git(self.other, "pull", "-q")
        (self.other / name).write_text(content)
        git(self.other, "add", name)
        git(self.other, "commit", "-qm", f"other {name}")
        git(self.other, "push", "-q")

    def commit_local(self, name, content):
        (self.dev / name).write_text(content)
        git(self.dev, "add", name)
        git(self.dev, "commit", "-qm", f"local {name}")

    def commit_fix(self, cwd, name, content):
        (cwd / name).write_text(content)
        git(cwd, "add", name)
        git(cwd, "commit", "-qm", f"# Fix: {name}")
        git(cwd, "push", "-q", "-u", "origin", "HEAD")


class ConfigTests(WatchTestCase):
    def test_config_reports_branch_state(self):
        code, out = self.run_watch(self.dev, "config")
        self.assertEqual(code, 0)
        self.assertEqual((out["mode"], out["branch"], out["targetBranch"]), ("bot", "AutoFixBug", "AutoFixBug"))
        self.assertFalse(out["onWatchBranch"])

    def test_empty_branch_follows_current_branch(self):
        self.write_profile({})
        code, out = self.run_watch(self.dev, "config")
        self.assertEqual((code, out["mode"], out["targetBranch"]), (0, "follow", "develop"))
        self.assertTrue(out["onWatchBranch"])

    def test_branch_equal_to_base_is_rejected(self):
        self.write_profile({"branch": "develop", "baseBranch": "develop"})
        code, out = self.run_watch(self.dev, "config")
        self.assertEqual((code, out["result"]), (2, "BAD_CONFIG"))

    def test_sync_off_the_watch_branch_waits(self):
        code, out = self.run_watch(self.dev, "sync")
        self.assertEqual((code, out["result"], out["reason"]), (3, "WAIT", "WRONG_BRANCH"))

    def test_pause_needs_no_repo_and_validates_args(self):
        code, out = self.run_watch(self.tmp, "pause", "--seconds", "0", "--attempt", "4")
        self.assertEqual((code, out["result"], out["slept"]), (0, "RESUME", 0))
        code, out = self.run_watch(self.tmp, "pause", "--attempt")
        self.assertEqual((code, out["result"]), (2, "BAD_ARGS"))


class CurrentCheckoutTests(WatchTestCase):
    def test_switch_needs_confirmation(self):
        code, out = self.run_watch(self.dev, "start", "--no-launch")
        self.assertEqual((code, out["result"]), (2, "NOT_CONFIRMED"))
        self.assertEqual(git(self.dev, "branch", "--show-current"), "develop")

    def test_start_creates_branch_from_base_and_sync_merges_base(self):
        code, out = self.run_watch(self.dev, "start", "--no-launch", "--yes")
        self.assertEqual((code, out["result"]), (0, "READY"))
        self.assertEqual(git(self.dev, "branch", "--show-current"), "AutoFixBug")

        self.push_from_other("b.txt", "b\n")
        code, out = self.run_watch(self.dev, "sync")
        self.assertEqual((code, out["result"]), (0, "OK"))
        self.assertIn("origin/develop", out["merged"])
        self.assertTrue((self.dev / "b.txt").exists())

    def test_dirty_tree_is_reported_not_blocking(self):
        self.run_watch(self.dev, "start", "--no-launch", "--yes")
        (self.dev / "a.txt").write_text("changed\n")
        code, out = self.run_watch(self.dev, "sync")
        self.assertEqual((code, out["result"]), (0, "OK"))
        self.assertIn("a.txt", out["dirty"])

    def test_base_conflict_is_skipped_and_aborted_cleanly(self):
        self.run_watch(self.dev, "start", "--no-launch", "--yes")
        self.commit_fix(self.dev, "a.txt", "bot\n")
        self.push_from_other("a.txt", "dev\n")
        head = git(self.dev, "rev-parse", "HEAD")
        code, out = self.run_watch(self.dev, "sync")
        self.assertEqual((code, out["result"]), (0, "OK"))
        self.assertEqual([(s["ref"], s["files"]) for s in out["skipped"]], [("origin/develop", ["a.txt"])])
        self.assertEqual(git(self.dev, "rev-parse", "HEAD"), head)
        self.assertEqual(git(self.dev, "status", "--porcelain", "--untracked-files=no"), "")

    def test_publish_disabled_by_default(self):
        self.run_watch(self.dev, "start", "--no-launch", "--yes")
        code, out = self.run_watch(self.dev, "publish")
        self.assertEqual((code, out["result"]), (0, "DISABLED"))

    def test_publish_fast_forwards_then_merges_when_base_moved(self):
        self.write_profile({**self.WATCH, "mergeToBase": True})
        # Nhánh bot tạo từ origin/develop — config phải nằm trên nhánh chính mới có hiệu lực.
        git(self.dev, "commit", "-qam", "profile")
        git(self.dev, "push", "-q")
        self.run_watch(self.dev, "start", "--no-launch", "--yes")

        self.commit_fix(self.dev, "c.txt", "c\n")
        code, out = self.run_watch(self.dev, "publish")
        self.assertEqual((code, out["result"]), (0, "PUBLISHED"))
        self.assertEqual(git(self.origin, "rev-parse", "develop"), git(self.dev, "rev-parse", "HEAD"))

        self.push_from_other("o.txt", "o\n")
        self.commit_fix(self.dev, "d.txt", "d\n")
        code, out = self.run_watch(self.dev, "publish")
        self.assertEqual((code, out["result"]), (0, "PUBLISHED"))
        develop = git(self.origin, "rev-parse", "develop")
        self.assertEqual(develop, out["head"])
        self.assertEqual(git(self.origin, "rev-parse", "AutoFixBug"), develop)
        files = git(self.origin, "ls-tree", "--name-only", "develop").splitlines()
        self.assertTrue({"c.txt", "d.txt", "o.txt"} <= set(files))


class WorktreeTests(WatchTestCase):
    WATCH = {**WatchTestCase.WATCH, "worktree": True}

    def test_start_creates_and_reuses_sibling_worktree(self):
        code, out = self.run_watch(self.dev, "start", "--no-launch")
        self.assertEqual((code, out["result"]), (0, "READY"))
        path = Path(out["workdir"])
        self.assertEqual(path, (self.tmp / "dev-AutoFixBug").resolve())
        self.assertEqual(git(path, "branch", "--show-current"), "AutoFixBug")
        self.assertEqual(git(self.dev, "branch", "--show-current"), "develop")

        code, out = self.run_watch(self.dev, "start", "--no-launch")
        self.assertEqual((code, out["result"]), (0, "READY"))

        code, out = self.run_watch(path, "sync")
        self.assertEqual((code, out["result"]), (0, "OK"))

    def test_launch_runs_claude_in_workdir_with_extra_args(self):
        fake_bin = self.tmp / "bin"
        fake_bin.mkdir()
        claude = fake_bin / "claude"
        claude.write_text('#!/bin/sh\necho "{\\"cwd\\": \\"$(pwd)\\", \\"args\\": \\"$*\\"}"\n')
        claude.chmod(0o755)
        if os.name == "nt":
            self.skipTest("fake claude là shell script")
        env = dict(os.environ, PATH=f"{fake_bin}{os.pathsep}{os.environ['PATH']}")
        proc = subprocess.run([sys.executable, str(self.dev / ".claude" / "scripts" / "bughub-watch.py"),
                               "start", "--", "--permission-mode", "bypassPermissions"],
                              cwd=self.dev, capture_output=True, encoding="utf-8", env=env, stdin=subprocess.DEVNULL)
        last = json.loads(proc.stdout.splitlines()[-1])
        self.assertEqual(Path(last["cwd"]).resolve(), (self.tmp / "dev-AutoFixBug").resolve())
        self.assertEqual(last["args"], "--permission-mode bypassPermissions /fix-bug --watch")


class FollowBranchTests(WatchTestCase):
    """branch rỗng: chạy watch ở nhánh nào thì sửa + push lên nhánh đó."""
    WATCH = {}

    def test_start_keeps_checkout_on_current_branch(self):
        code, out = self.run_watch(self.dev, "start", "--no-launch")
        self.assertEqual((code, out["result"], out["mode"], out["branch"]), (0, "READY", "follow", "develop"))
        self.assertEqual(Path(out["workdir"]), self.dev.resolve())
        self.assertEqual(git(self.dev, "branch", "--show-current"), "develop")

    def test_sync_fast_forwards_own_remote_around_unrelated_wip(self):
        (self.dev / "a.txt").write_text("wip\n")
        self.push_from_other("b.txt", "b\n")
        code, out = self.run_watch(self.dev, "sync")
        self.assertEqual((code, out["result"], out["merged"]), (0, "OK", ["origin/develop"]))
        self.assertEqual(out["dirty"], ["a.txt"])
        self.assertTrue((self.dev / "b.txt").exists())
        self.assertEqual((self.dev / "a.txt").read_text(), "wip\n")

    def test_follows_branch_switch(self):
        git(self.dev, "checkout", "-q", "-b", "feature")
        code, out = self.run_watch(self.dev, "sync")
        self.assertEqual((code, out["branch"]), (0, "feature"))

    def test_wip_on_incoming_file_waits(self):
        self.push_from_other("a.txt", "dev\n")
        (self.dev / "a.txt").write_text("wip\n")
        code, out = self.run_watch(self.dev, "sync")
        self.assertEqual((code, out["result"], out["reason"]), (3, "WAIT", "LOCAL_CHANGES"))
        self.assertEqual((self.dev / "a.txt").read_text(), "wip\n")

    def test_diverged_with_wip_waits_without_merging(self):
        self.commit_local("c.txt", "c\n")
        self.push_from_other("b.txt", "b\n")
        (self.dev / "a.txt").write_text("wip\n")
        head = git(self.dev, "rev-parse", "HEAD")
        code, out = self.run_watch(self.dev, "sync")
        self.assertEqual((code, out["reason"]), (3, "DIVERGED_DIRTY"))
        self.assertEqual(git(self.dev, "rev-parse", "HEAD"), head)

    def test_diverged_conflict_waits_and_aborts_cleanly(self):
        self.commit_local("a.txt", "mine\n")
        self.push_from_other("a.txt", "theirs\n")
        head = git(self.dev, "rev-parse", "HEAD")
        code, out = self.run_watch(self.dev, "sync")
        self.assertEqual((code, out["reason"], out["files"]), (3, "MERGE_CONFLICT", ["a.txt"]))
        self.assertEqual(git(self.dev, "rev-parse", "HEAD"), head)
        self.assertEqual(git(self.dev, "status", "--porcelain", "--untracked-files=no"), "")

    def test_detached_head_waits(self):
        git(self.dev, "checkout", "-q", "--detach")
        code, out = self.run_watch(self.dev, "sync")
        self.assertEqual((code, out["reason"]), (3, "DETACHED"))

    def test_push_merges_remote_that_moved_then_pushes(self):
        self.commit_local("c.txt", "c\n")
        self.push_from_other("b.txt", "b\n")
        code, out = self.run_watch(self.dev, "push")
        self.assertEqual((code, out["result"], out["merged"]), (0, "PUSHED", ["origin/develop"]))
        self.assertEqual(git(self.origin, "rev-parse", "develop"), git(self.dev, "rev-parse", "HEAD"))
        code, out = self.run_watch(self.dev, "push")
        self.assertEqual((code, out["result"]), (0, "UP_TO_DATE"))

    def test_push_sets_upstream_for_new_branch(self):
        git(self.dev, "checkout", "-q", "-b", "feature")
        self.commit_local("c.txt", "c\n")
        code, out = self.run_watch(self.dev, "push")
        self.assertEqual((code, out["result"]), (0, "PUSHED"))
        self.assertEqual(git(self.dev, "rev-parse", "--abbrev-ref", "feature@{upstream}"), "origin/feature")

    def test_shelve_stashes_only_the_bug_files(self):
        (self.dev / "a.txt").write_text("bot half fix\n")
        (self.dev / "new.cs").write_text("class X {}\n")
        (self.dev / "wip.txt").write_text("dev untracked\n")
        code, out = self.run_watch(self.dev, "shelve", "--bug", "7", "--reason", "compile lỗi", "--", "a.txt", "new.cs", "gone.txt")
        self.assertEqual((code, out["result"], sorted(out["files"])), (0, "SHELVED", ["a.txt", "new.cs"]))
        self.assertEqual((self.dev / "a.txt").read_text(), "a\n")
        self.assertFalse((self.dev / "new.cs").exists())
        self.assertTrue((self.dev / "wip.txt").exists())
        self.assertIn("bughub #7 released: compile lỗi", git(self.dev, "stash", "list"))
        code, out = self.run_watch(self.dev, "shelve", "--bug", "7", "--", "a.txt")
        self.assertEqual((code, out["result"]), (0, "NOTHING"))

    def test_shelve_rejects_paths_outside_repo(self):
        code, out = self.run_watch(self.dev, "shelve", "--bug", "7", "--", "../x.txt")
        self.assertEqual((code, out["result"]), (2, "BAD_ARGS"))

    def test_publish_is_disabled(self):
        code, out = self.run_watch(self.dev, "publish")
        self.assertEqual((code, out["result"]), (0, "DISABLED"))


if __name__ == "__main__":
    unittest.main()
