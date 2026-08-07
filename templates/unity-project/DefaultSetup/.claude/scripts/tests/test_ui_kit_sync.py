"""Tests for ui-kit-sync.py's staleness contract (`--check`) and usage notes.

The kit is a generated artifact with no watcher: nothing regenerates it when a
screen-template prefab changes, and a stale kit degrades quietly (the UI suite
skips instead of failing). `--check` is what bootstrap, the ui-kit skill and any
gate ask, so its states are a contract worth pinning.

Each test builds a throwaway tree — the script derives its root from its own
location, so copying it into a temp `.claude/scripts/` makes that tree the
"project". Hash-correct fixtures are never needed: every state under test is
reachable without one, and `fresh` is exercised end-to-end by the real repo's
kit in test_fresh_state_on_the_real_repo.

Run from the repo root:
    python3 -m unittest discover -s .claude/scripts/tests -v
"""

import json
import shutil
import subprocess
import sys
import tempfile
import unittest
from pathlib import Path

REPO = Path(__file__).resolve().parents[3]
SCRIPT_SRC = REPO / ".claude" / "scripts" / "ui-kit-sync.py"
SCRIPT_DEPS = ["project_profile.py"]
DEFAULT_TEMPLATES = "Assets/Resources/Prefabs/Templates"


class UiKitCheckTests(unittest.TestCase):
    def setUp(self):
        self.dir = Path(tempfile.mkdtemp(prefix="ui-kit-test-"))
        self.addCleanup(shutil.rmtree, self.dir, ignore_errors=True)
        scripts = self.dir / ".claude" / "scripts"
        scripts.mkdir(parents=True)
        self.script = scripts / "ui-kit-sync.py"
        shutil.copy(SCRIPT_SRC, self.script)
        for dep in SCRIPT_DEPS:
            shutil.copy(SCRIPT_SRC.parent / dep, scripts / dep)
        self.kit_dir = self.dir / ".claude" / "ui-kit"
        self.kit_dir.mkdir(parents=True)

    def write(self, rel, content):
        path = self.dir / rel
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_text(content, encoding="utf-8")
        return path

    def check(self):
        proc = subprocess.run([sys.executable, str(self.script), "--check"],
                              cwd=self.dir, capture_output=True, text=True)
        return json.loads(proc.stdout), proc.returncode

    def test_no_templates_is_not_a_failure(self):
        # A toolchain checkout without UI prefabs is a legitimate state; making
        # it an error would train everyone to ignore the exit code.
        result, code = self.check()
        self.assertEqual(result["state"], "no-templates")
        self.assertEqual(code, 0)

    def test_missing_kit_fails(self):
        self.write(f"{DEFAULT_TEMPLATES}/PanelTemplate.prefab", "x: 1\n")
        result, code = self.check()
        self.assertEqual(result["state"], "missing")
        self.assertEqual(code, 1)

    def test_prefab_change_makes_the_kit_stale(self):
        self.write(f"{DEFAULT_TEMPLATES}/PanelTemplate.prefab", "x: 1\n")
        self.write(".claude/ui-kit/ui-kit.json", json.dumps(
            {"_meta": {"sourceHash": "hash-of-an-older-prefab"}, "templates": {}}))
        result, code = self.check()
        self.assertEqual(result["state"], "stale")
        self.assertIn("prefabs changed", result["detail"])
        self.assertEqual(code, 1)

    def test_broken_usage_file_is_reported_as_itself(self):
        # Distinguished from `stale` on purpose: regenerating would silently drop
        # every note instead of fixing anything, so the message has to say so.
        self.write(f"{DEFAULT_TEMPLATES}/PanelTemplate.prefab", "x: 1\n")
        self.write(".claude/ui-kit/ui-kit.json", json.dumps(
            {"_meta": {"sourceHash": "x"}, "templates": {}}))
        self.write(".claude/ui-kit/ui-kit-usage.json", "{ not json")
        result, code = self.check()
        self.assertEqual(result["state"], "usage-invalid")
        self.assertEqual(code, 1)


class UiKitRealRepoTests(unittest.TestCase):
    """The committed kit of this repo must be current — that is the whole point."""

    def test_fresh_state_on_the_real_repo(self):
        if not (REPO / DEFAULT_TEMPLATES).is_dir():
            self.skipTest("no screen templates in this checkout")
        proc = subprocess.run([sys.executable, str(SCRIPT_SRC), "--check"],
                              cwd=REPO, capture_output=True, text=True)
        result = json.loads(proc.stdout)
        self.assertEqual(result["state"], "fresh",
                         msg=f"{result['detail']} — run {result['regenerate']}")
        self.assertEqual(proc.returncode, 0)


if __name__ == "__main__":
    unittest.main()
