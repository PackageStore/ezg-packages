"""Golden tests for backlog-preflight.py (run end-to-end via subprocess).

The script derives REPO_ROOT from its own file location (parent of parent of
.claude/scripts/), NOT from cwd — so each test copies the script into a fresh
temp git repo and stages crafted files there. This exercises the real staged-
diff path with the exact shipped regexes.

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
SCRIPT_SRC = REPO / ".claude" / "scripts" / "backlog-preflight.py"
# Modules the script imports at load time. They have to land beside it in the
# temp repo or it dies on import before any rule runs. Deliberately NOT copying
# a project-profile.json with them: the temp repo has none, which is exactly the
# case these goldens pin — profile absent means project_profile.DEFAULTS, i.e.
# the rule set this suite was written against.
SCRIPT_DEPS = ["project_profile.py"]


def run_git(cwd, *args):
    subprocess.run(["git", *args], cwd=cwd, check=True,
                   capture_output=True, text=True)


class PreflightTestCase(unittest.TestCase):
    """Fresh temp repo per test, with the preflight script copied in."""

    def setUp(self):
        self.dir = Path(tempfile.mkdtemp(prefix="preflight-test-"))
        self.addCleanup(shutil.rmtree, self.dir, ignore_errors=True)
        scripts = self.dir / ".claude" / "scripts"
        scripts.mkdir(parents=True)
        self.script = scripts / "backlog-preflight.py"
        shutil.copy(SCRIPT_SRC, self.script)
        for dep in SCRIPT_DEPS:
            shutil.copy(SCRIPT_SRC.parent / dep, scripts / dep)
        run_git(self.dir, "init", "-q")
        run_git(self.dir, "config", "user.email", "test@test.local")
        run_git(self.dir, "config", "user.name", "test")
        run_git(self.dir, "add", "-A")
        run_git(self.dir, "commit", "-qm", "init")

    def stage(self, rel, content):
        p = self.dir / rel
        p.parent.mkdir(parents=True, exist_ok=True)
        p.write_text(content, encoding="utf-8")
        run_git(self.dir, "add", rel)

    def run_preflight(self):
        proc = subprocess.run(
            [sys.executable, str(self.script)],
            cwd=self.dir, capture_output=True, text=True)
        self.assertEqual(proc.returncode, 0, msg=proc.stdout + proc.stderr)
        return json.loads(proc.stdout)

    def findings(self, result, rule):
        return [f for f in result["findings"] if f["rule"] == rule]


class CredentialRuleTests(PreflightTestCase):
    """Regression tests for the credential identifier rule: the pattern is
    compiled with a scoped (?-i:) group so it must stay UPPER_SNAKE-only even
    though every other rule is case-insensitive (audit finding #23)."""

    def test_lowercase_identifiers_do_not_match(self):
        self.stage("Assets/Scripts/GameFlow.cs", "\n".join([
            "public class GameFlow {",
            "    void Go() {",
            "        var player_token = GetValue();",
            '        string session_key = "s";',
            '        var t = json["access_token"];',
            "        ResetPassword(reset_password);",
            "    }",
            "}",
        ]) + "\n")
        r = self.run_preflight()
        self.assertEqual(self.findings(r, "credential"), [])
        self.assertFalse(r["sensitive"]["value"])
        self.assertFalse(r["summary"]["has_blocking_definite"])

    def test_upper_snake_identifier_is_contextual_and_sensitive(self):
        self.stage("Assets/Scripts/GameFlow.cs", "\n".join([
            "public class GameFlow {",
            '    private const string SUPABASE_SERVICE_KEY = "abc";',
            "}",
        ]) + "\n")
        r = self.run_preflight()
        cred = self.findings(r, "credential")
        self.assertEqual(len(cred), 1)
        self.assertEqual(cred[0]["severity"], "critical")
        # contextual, not definite: legit SCREAMING_CONST names match the same
        # shape, so this must route to the security-auditor, never auto-fix.
        self.assertEqual(cred[0]["confidence"], "contextual")
        self.assertFalse(r["summary"]["has_blocking_definite"])
        # ... but it must still trip the security-auditor spawn signal.
        self.assertTrue(r["sensitive"]["value"])
        self.assertIn("credential-pattern",
                      [x["type"] for x in r["sensitive"]["reasons"]])

    def test_actual_secret_value_stays_definite(self):
        self.stage("Assets/Scripts/GameFlow.cs", "\n".join([
            "public class GameFlow {",
            '    private string _jwt = "eyJhbGciOiJIUzI1NiJ9.payload";',
            "}",
        ]) + "\n")
        r = self.run_preflight()
        cred = self.findings(r, "credential")
        self.assertEqual(len(cred), 1)
        self.assertEqual(cred[0]["confidence"], "definite")
        self.assertTrue(r["summary"]["has_blocking_definite"])
        self.assertTrue(r["sensitive"]["value"])

    def test_sk_prefix_requires_word_boundary_and_lowercase(self):
        # regression: the unanchored secret-prefix pattern used to match INSIDE
        # ordinary identifiers such as task_still and TASK_BASE (mid-word, and
        # case-insensitively) and flagged them as critical-definite blocks
        self.stage("Assets/Scripts/GameFlow.cs", "\n".join([
            "public class GameFlow {",
            "    private int task_still_in_progress = 1;",
            '    private const string TASK_BASE = "x";',
            "    void KeyJson() { var keyJwt = 1; }",
            "}",
        ]) + "\n")
        r = self.run_preflight()
        self.assertEqual(self.findings(r, "credential"), [])

        run_git(self.dir, "reset", "-q")
        self.stage("Assets/Scripts/Pay.cs", "\n".join([
            "public class Pay {",
            '    private string _k = "sk_live_abc123";',
            "}",
        ]) + "\n")
        r = self.run_preflight()
        cred = self.findings(r, "credential")
        self.assertEqual(len(cred), 1)
        self.assertEqual(cred[0]["confidence"], "definite")


class CoreRuleTests(PreflightTestCase):

    def test_datetime_now_is_definite_critical(self):
        self.stage("Assets/Scripts/Clock.cs", "\n".join([
            "public class Clock {",
            "    void Tick() { var t = DateTime.Now; }",
            "}",
        ]) + "\n")
        r = self.run_preflight()
        tm = self.findings(r, "time-manager")
        self.assertEqual(len(tm), 1)
        self.assertEqual(tm[0]["confidence"], "definite")
        self.assertTrue(r["summary"]["has_blocking_definite"])

    def test_leading_comment_lines_are_skipped(self):
        self.stage("Assets/Scripts/Clock.cs", "\n".join([
            "public class Clock {",
            "    // DateTime.Now is banned here, use TimeManager",
            "    void Tick() { }",
            "}",
        ]) + "\n")
        r = self.run_preflight()
        self.assertEqual(self.findings(r, "time-manager"), [])

    def test_sensitive_filename_pattern_sets_sensitive(self):
        self.stage("Assets/_Game/PurchaseManager.cs",
                   "public class PurchaseManager { }\n")
        r = self.run_preflight()
        self.assertTrue(r["sensitive"]["value"])
        self.assertIn("file-pattern",
                      [x["type"] for x in r["sensitive"]["reasons"]])

    def test_missing_using_detected_for_new_generic_use(self):
        self.stage("Assets/Scripts/Bag.cs", "\n".join([
            "public class Bag {",
            "    private List<int> _items = new List<int>();",
            "}",
        ]) + "\n")
        r = self.run_preflight()
        missing = self.findings(r, "missing-using")
        self.assertEqual(len(missing), 1)
        self.assertIn("System.Collections.Generic", missing[0]["suggestion"])

    def test_missing_using_still_flags_real_ui_button_field(self):
        self.stage("Assets/Scripts/Panel.cs", "\n".join([
            "public class Panel {",
            "    private Button _confirm;",
            "}",
        ]) + "\n")
        r = self.run_preflight()
        missing = self.findings(r, "missing-using")
        self.assertEqual(len(missing), 1)
        self.assertIn("UnityEngine.UI", missing[0]["suggestion"])

    def test_odin_button_attribute_is_not_a_ui_button(self):
        # The [CHEAT] guardrail mandates Odin [Button] on cheat controllers; that attribute is
        # Sirenix.OdinInspector.Button, so it must not demand a UnityEngine.UI using.
        self.stage("Assets/Scripts/Cheats.cs", "\n".join([
            "using Sirenix.OdinInspector;",
            "public class Cheats {",
            '    [Button("Add Torch")]',
            "    public void Cheat_AddTorch() { }",
            "}",
        ]) + "\n")
        r = self.run_preflight()
        self.assertEqual(self.findings(r, "missing-using"), [])


class UiKitStalenessTests(PreflightTestCase):
    """The kit is generated from the screen-template prefabs and has no watcher.

    Staging a template edit without the regenerated kit is how this repo ended up
    with a kit describing 46 templates against a folder of 48 — invisible, because
    a stale kit makes the UI suite skip rather than fail. The default templates
    root applies here: the temp repo has no profile (see SCRIPT_DEPS).
    """

    TEMPLATE = "Assets/Resources/Prefabs/Templates/PanelTemplate.prefab"
    KIT = ".claude/ui-kit/ui-kit.json"

    def commit_kit(self):
        """A kit already tracked in HEAD — the state the rule guards."""
        self.stage(self.KIT, '{"_meta": {"count": 1}}\n')
        run_git(self.dir, "commit", "-qm", "kit")

    def test_template_prefab_without_regenerated_kit_is_flagged(self):
        self.commit_kit()
        self.stage(self.TEMPLATE, "RectTransform: {m_SizeDelta: {x: 100, y: 100}}\n")
        r = self.run_preflight()
        found = self.findings(r, "ui-kit-stale")
        self.assertEqual(len(found), 1)
        self.assertEqual(found[0]["severity"], "major")
        self.assertEqual(found[0]["confidence"], "definite")
        self.assertIn("PanelTemplate.prefab", found[0]["evidence"])

    def test_regenerated_kit_in_the_same_commit_clears_it(self):
        self.commit_kit()
        self.stage(self.TEMPLATE, "RectTransform: {m_SizeDelta: {x: 100, y: 100}}\n")
        self.stage(self.KIT, '{"_meta": {"count": 2}}\n')
        self.assertEqual(self.findings(self.run_preflight(), "ui-kit-stale"), [])

    def test_untracked_kit_stays_silent(self):
        # A project that gitignores the generated half rebuilds it at bootstrap;
        # flagging every prefab commit there would be pure noise.
        self.stage(self.TEMPLATE, "RectTransform: {m_SizeDelta: {x: 100, y: 100}}\n")
        self.assertEqual(self.findings(self.run_preflight(), "ui-kit-stale"), [])

    def test_prefab_outside_the_templates_root_is_ignored(self):
        self.commit_kit()
        self.stage("Assets/_Game/2.BUS/Features/Shop/Resources/Shop.prefab", "x: 1\n")
        self.assertEqual(self.findings(self.run_preflight(), "ui-kit-stale"), [])


if __name__ == "__main__":
    unittest.main()
