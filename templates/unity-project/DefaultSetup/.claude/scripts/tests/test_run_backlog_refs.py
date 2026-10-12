"""Tests for run-backlog-refs.py and the run-backlog skill split it serves.

The run-backlog skill keeps four conditional sections in sibling files
(.claude/skills/run-backlog/ref-*.md); the loop inlines the ones that apply to an
iteration. A wrong choice silently drops a rule from the agent's context (a resume
without 1d, a workflow task without STEP 5.0), so the selection is pinned here, and
so is the wiring around it: the skill's table, stub headings and files; both loop
controllers' prompt splice; the fallback line the skill tells the agent to obey.

Run from the repo root:
    python3 -m unittest discover -s .claude/scripts/tests -p "test_run_backlog_refs.py" -v
"""

import importlib.util
import os
import re
import shutil
import subprocess
import sys
import tempfile
import unittest
from pathlib import Path

REPO = Path(__file__).resolve().parents[3]
SCRIPTS = REPO / ".claude" / "scripts"
HELPER = SCRIPTS / "run-backlog-refs.py"
LOOP_SH = SCRIPTS / "run-backlog-loop.sh"
LOOP_PS = SCRIPTS / "run-backlog-loop-core.ps1"
SKILL_DIR = REPO / ".claude" / "skills" / "run-backlog"

_spec = importlib.util.spec_from_file_location("run_backlog_refs", HELPER)
refs = importlib.util.module_from_spec(_spec)
_spec.loader.exec_module(refs)

ALL = ["ref-resume.md", "ref-requires.md", "ref-worktree.md", "ref-workflow.md"]
PLAIN_TASK = "### [P1] Do a thing\n\n**Tier:** M\n\nDescription.\n"
REQ_TASK = PLAIN_TASK + "**Requires:** unity-editor\n"
WF_TASK = PLAIN_TASK + "**Backed by workflow:** /new-ui\n**Workflow args:** Shop\n"
COMMENTED_TASK = PLAIN_TASK + "<!--\n**Requires:** unity-editor\n**Backed by workflow:** /new-ui\n-->\n"
FALLBACK_PREFIX = "AGENT_SKILL_REFS=unavailable"
AUDIT_LINE = "AGENT_SKILL_REFS=none (post-hoc audit"


def _tmpdir(test):
    path = Path(tempfile.mkdtemp(prefix="refs-test-"))
    test.addCleanup(shutil.rmtree, path, True)
    return path


class SelectTests(unittest.TestCase):
    def test_plain_todo_in_current_mode_needs_nothing(self):
        self.assertEqual(refs.select("current", "todo", PLAIN_TASK, False), [])

    def test_task_markers(self):
        self.assertEqual(refs.select("current", "todo", REQ_TASK, False), ["ref-requires.md"])
        self.assertEqual(refs.select("current", "todo", WF_TASK, False), ["ref-workflow.md"])
        self.assertEqual(refs.select("current", "todo", REQ_TASK + WF_TASK, False),
                         ["ref-requires.md", "ref-workflow.md"])

    def test_commented_markers_still_select(self):
        # Leaning to inclusion: a section the agent then skips costs a few KB; one it needs and
        # does not have drops rules from its context.
        self.assertEqual(refs.select("current", "todo", COMMENTED_TASK, False),
                         ["ref-requires.md", "ref-workflow.md"])

    def test_resume_and_recovery(self):
        self.assertEqual(refs.select("current", "in-progress", PLAIN_TASK, False), ["ref-resume.md"])
        self.assertEqual(refs.select("current", "todo", PLAIN_TASK, True), ["ref-resume.md"])
        self.assertEqual(refs.select("current", "in-progress", WF_TASK, True),
                         ["ref-resume.md", "ref-workflow.md"])

    def test_worktree_and_unknown_mode(self):
        self.assertEqual(refs.select("worktree", "todo", PLAIN_TASK, False), ["ref-worktree.md"])
        self.assertEqual(refs.select("", "todo", PLAIN_TASK, False), ["ref-worktree.md"])

    def test_unknown_or_ship_pending_state_selects_everything(self):
        for state in ("ship-pending", "", "busy", "empty", "weird"):
            self.assertEqual(refs.select("current", state, PLAIN_TASK, False), ALL, state)

    def test_unreadable_task_selects_both_marker_files(self):
        self.assertEqual(refs.select("current", "todo", None, False),
                         ["ref-requires.md", "ref-workflow.md"])

    def test_all_flag(self):
        self.assertEqual(refs.select("current", "todo", PLAIN_TASK, False, everything=True), ALL)


class ResolveTests(unittest.TestCase):
    def test_pick_paths_resolve_against_the_git_common_dir(self):
        base = Path(tempfile.gettempdir()).resolve()  # absolute on every OS (a drive on Windows)
        root = base / "repo" / ".git" / "backlog"
        self.assertEqual(refs.resolve_task_file("backlog/todo/001-M-x.md", str(root)),
                         root / "todo" / "001-M-x.md")
        self.assertEqual(refs.resolve_task_file("todo/001-M-x.md", str(root)),
                         root / "todo" / "001-M-x.md")
        absolute = base / "elsewhere" / "x.md"
        self.assertEqual(refs.resolve_task_file(str(absolute), str(root)), absolute)
        self.assertIsNone(refs.resolve_task_file("", str(root)))
        self.assertIsNone(refs.resolve_task_file("backlog/todo/x.md", ""))


class CliTests(unittest.TestCase):
    def setUp(self):
        self.tmp = _tmpdir(self)
        self.backlog = self.tmp / ".git" / "backlog"
        (self.backlog / "todo").mkdir(parents=True)
        (self.backlog / "todo" / "007-M-wf.md").write_text(WF_TASK, encoding="utf-8")
        self.out = self.tmp / "logs" / "refs.md"

    def run_helper(self, *extra, skill_dir=SKILL_DIR):
        cmd = [sys.executable, str(HELPER), "--out", str(self.out), "--backlog-root", str(self.backlog),
               "--skill-dir", str(skill_dir)] + list(extra)
        return subprocess.run(cmd, capture_output=True, text=True)

    def test_block_inlines_the_selected_files_verbatim(self):
        r = self.run_helper("--mode", "current", "--state", "todo",
                            "--task-path", "backlog/todo/007-M-wf.md", "--task-nnn", "007")
        self.assertEqual(r.returncode, 0, r.stdout + r.stderr)
        self.assertEqual(r.stdout.strip(), "refs: ref-workflow.md")
        block = self.out.read_text(encoding="utf-8")
        self.assertTrue(block.startswith("AGENT_SKILL_REFS=ref-workflow.md\n"))
        self.assertIn("AGENT_SKILL_REFS_FOR=task 007 (todo), mode current, recovery no", block)
        body = (SKILL_DIR / "ref-workflow.md").read_text(encoding="utf-8").rstrip("\n")
        self.assertIn("===== BEGIN .claude/skills/run-backlog/ref-workflow.md =====\n" + body
                      + "\n===== END ref-workflow.md =====", block)
        self.assertNotIn("ref-resume.md =====", block)

    def test_none_selected_still_writes_the_marker_line(self):
        (self.backlog / "todo" / "008-S-plain.md").write_text(PLAIN_TASK, encoding="utf-8")
        r = self.run_helper("--mode", "current", "--state", "todo", "--task-path", "backlog/todo/008-S-plain.md")
        self.assertEqual(r.returncode, 0, r.stdout + r.stderr)
        self.assertEqual(r.stdout.strip(), "refs: none")
        self.assertTrue(self.out.read_text(encoding="utf-8").startswith("AGENT_SKILL_REFS=none\n"))

    def test_missing_ref_file_fails_without_a_block(self):
        self.out.parent.mkdir(parents=True)
        self.out.write_text("stale", encoding="utf-8")
        r = self.run_helper("--mode", "current", "--all", skill_dir=self.tmp / "nowhere")
        self.assertEqual(r.returncode, 1)
        self.assertFalse(self.out.exists(), "a failed run must not leave a block behind")

    def test_pre_split_skill_fails_even_when_nothing_would_be_selected(self):
        # New loop + old (unsplit) skill: announcing "none" would point at sections that do not
        # exist as files; failing makes the controller print the read-them-all fallback.
        old = self.tmp / "old-skill"
        old.mkdir()
        (old / "SKILL.md").write_text("# old skill, everything inline\n", encoding="utf-8")
        (self.backlog / "todo" / "008-S-plain.md").write_text(PLAIN_TASK, encoding="utf-8")
        r = self.run_helper("--mode", "current", "--state", "todo",
                            "--task-path", "backlog/todo/008-S-plain.md", skill_dir=old)
        self.assertEqual(r.returncode, 1, r.stdout)
        self.assertFalse(self.out.exists())

    def test_missing_task_file_leans_to_inclusion(self):
        r = self.run_helper("--mode", "current", "--state", "todo", "--task-path", "backlog/todo/999-M-gone.md")
        self.assertEqual(r.returncode, 0, r.stdout + r.stderr)
        self.assertEqual(r.stdout.strip(), "refs: ref-requires.md ref-workflow.md")

    def test_non_utf8_task_file_leans_to_inclusion(self):
        (self.backlog / "todo" / "009-M-utf16.md").write_text(PLAIN_TASK, encoding="utf-16")
        r = self.run_helper("--mode", "current", "--state", "todo", "--task-path", "backlog/todo/009-M-utf16.md")
        self.assertEqual(r.returncode, 0, r.stdout + r.stderr)
        self.assertEqual(r.stdout.strip(), "refs: ref-requires.md ref-workflow.md")


class SkillWiringTests(unittest.TestCase):
    """SKILL.md's table, its stub headings and the ref files must agree."""

    STUBS = {  # stub heading prefix in SKILL.md -> the file holding the section
        "### 1c. ": "ref-resume.md",
        "### 1d. ": "ref-resume.md",
        "### 1f. ": "ref-resume.md",
        "### 1b. ": "ref-requires.md",
        "### 2a–2b. ": "ref-worktree.md",
        "### 5.0 — ": "ref-workflow.md",
    }
    SECTIONS = {  # section heading -> the file that must hold it exactly once
        "### 1c. Ship recovery": "ref-resume.md",
        "### 1d. Resume protocol": "ref-resume.md",
        "### 1f. Recovery iteration": "ref-resume.md",
        "### 1b. Requires gate": "ref-requires.md",
        "### 2a. Resolve the base branch": "ref-worktree.md",
        "### 2b. Sync the worktree branch": "ref-worktree.md",
        "### 5.0 — Workflow-backed shortcut": "ref-workflow.md",
    }

    @classmethod
    def setUpClass(cls):
        cls.core = (SKILL_DIR / "SKILL.md").read_text(encoding="utf-8")

    def test_every_ref_file_exists_and_is_in_the_table(self):
        self.assertEqual(list(refs.REFS), ALL)
        for name in ALL:
            self.assertTrue((SKILL_DIR / name).is_file(), name)
            self.assertIn("| [`%s`](%s) |" % (name, name), self.core, name)

    def test_sections_live_in_exactly_one_ref_file(self):
        for heading, name in self.SECTIONS.items():
            for other in ALL:
                body = (SKILL_DIR / other).read_text(encoding="utf-8")
                count = len(re.findall(r"^" + re.escape(heading), body, re.M))
                self.assertEqual(count, 1 if other == name else 0, (heading, other))
            self.assertNotRegex(self.core, r"(?m)^" + re.escape(heading) + r"(?!.*→ \[`ref-)", heading)

    def test_each_stub_points_at_the_right_file(self):
        for prefix, name in self.STUBS.items():
            lines = [l for l in self.core.splitlines() if l.startswith(prefix)]
            self.assertEqual(len(lines), 1, prefix)
            self.assertTrue(lines[0].endswith("→ [`%s`](%s)" % (name, name)), (prefix, lines[0]))
        self.assertIn("\n### 1e. ", self.core)  # every iteration needs the checkpointed exit

    def test_hand_run_and_fallback_rules_are_present(self):
        self.assertIn("No `AGENT_SKILL_REFS=` line in your prompt", self.core)
        self.assertIn("or it says `unavailable` → Read all four files now, before STEP 1", self.core)
        self.assertIn("AGENT_SKILL_REFS_FOR", self.core)

    def test_gate_integrity_rule_is_in_the_core(self):
        # It used to reach every iteration through 1f; after the split it must live in SKILL.md itself.
        self.assertIn("**Gate integrity — every iteration, every gate.**", self.core)
        self.assertIn("Never weaken a gate to get past it", self.core)


class ControllerContractTests(unittest.TestCase):
    """Both loop controllers must speak the protocol SKILL.md describes."""

    def test_both_controllers_call_the_helper_and_fall_back(self):
        for path in (LOOP_SH, LOOP_PS):
            text = path.read_text(encoding="utf-8")
            self.assertIn("run-backlog-refs.py", text, path.name)
            self.assertIn(FALLBACK_PREFIX, text, path.name)
            self.assertIn(AUDIT_LINE, text, path.name)
        ps = LOOP_PS.read_text(encoding="utf-8")
        self.assertIn("(Get-SkillRefsBlock -Pick $pick -IsRecovery $isRecovery -OutFile $refsFile)", ps)

    def test_bash_prompt_holds_each_placeholder_once(self):
        src = LOOP_SH.read_text(encoding="utf-8")
        heredoc = re.search(r"^read -r -d '' PROMPT <<EOF\n(.*?)^EOF$", src, re.S | re.M).group(1)
        for placeholder in ("__RECOVERY__", "__SKILL_REFS__", "__ITER_DEADLINE__"):
            self.assertEqual(heredoc.count(placeholder), 1, placeholder)

    def test_powershell_controller_stays_ascii(self):
        data = LOOP_PS.read_bytes()
        self.assertFalse(any(b > 127 for b in data), "Windows PowerShell 5.1 reads BOM-less .ps1 as ANSI")

    @unittest.skipIf(os.name == "nt" or shutil.which("bash") is None, "needs a POSIX bash")
    def test_bash_splices_keep_the_prompt_and_inline_the_block(self):
        tmp = _tmpdir(self)
        src = LOOP_SH.read_text(encoding="utf-8")
        fn = re.search(r"^skill_refs_block\(\) \{.*?^\}\n", src, re.S | re.M).group(0)
        cases = re.findall(r"^ *case \"\$ITER_PROMPT\" in\n.*?^ *esac\n", src, re.S | re.M)
        recovery = [c for c in cases if "__RECOVERY__" in c]
        refs_case = [c for c in cases if "__SKILL_REFS__" in c]
        self.assertEqual((len(recovery), len(refs_case)), (1, 1), cases)
        backlog = tmp / ".git" / "backlog"
        (backlog / "todo").mkdir(parents=True)
        (backlog / "todo" / "007-M-wf.md").write_text(WF_TASK, encoding="utf-8")
        harness = tmp / "harness.sh"
        harness.write_text(
            "set -u\n" + fn + "\n"
            'MODE="$5"; BACKLOG_ROOT="$1"; WORK_DIR="$2"; REFS_HELPER="$3"\n'
            'PICK_STATE=todo; pick_path=backlog/todo/007-M-wf.md; TASK_NNN=007; IS_RECOVERY=0; base="$4"\n'
            "ITER_PROMPT='head & tail \\1 $x\n__RECOVERY__\n__SKILL_REFS__\nStart now.'\n"
            "recovery_text='\nRECOVERY & co \\1 * ? [x] $y'\n"
            + recovery[0] + 'refs_text="$(skill_refs_block "$base.refs.md")"\n' + refs_case[0]
            + 'printf "%s" "$ITER_PROMPT"\n', encoding="utf-8")
        root = SKILL_DIR.parents[2]  # the repo root that holds .claude/skills/run-backlog
        args = ["bash", str(harness), str(backlog), str(root)]
        ok = subprocess.run(args + [str(HELPER), str(tmp / "it"), "worktree"], capture_output=True, text=True)
        self.assertEqual(ok.returncode, 0, ok.stderr)
        head = "head & tail \\1 $x\n\nRECOVERY & co \\1 * ? [x] $y\nAGENT_SKILL_REFS=ref-worktree.md,ref-workflow.md\n"
        self.assertTrue(ok.stdout.startswith(head), ok.stdout[:300])
        self.assertTrue(ok.stdout.endswith("===== END ref-workflow.md =====\nStart now."), ok.stdout[-200:])
        worktree = (SKILL_DIR / "ref-worktree.md").read_text(encoding="utf-8").rstrip("\n")
        self.assertIn("&&", worktree)  # the splice must carry '&' through untouched
        self.assertIn(worktree, ok.stdout)
        self.assertNotIn("__SKILL_REFS__", ok.stdout)
        self.assertNotIn("__RECOVERY__", ok.stdout)
        bad = subprocess.run(args + [str(tmp / "missing.py"), str(tmp / "it2"), "current"], capture_output=True, text=True)
        self.assertEqual(bad.returncode, 0, bad.stderr)
        self.assertIn("\n" + FALLBACK_PREFIX, bad.stdout)
        self.assertTrue(bad.stdout.endswith("\nStart now."))

    def test_powershell_function_builds_the_block(self):
        shell = shutil.which("pwsh") or (shutil.which("powershell") if os.name == "nt" else None)
        if not shell:
            self.skipTest("no PowerShell on this machine")
        tmp = _tmpdir(self)
        backlog = tmp / ".git" / "backlog"
        (backlog / "todo").mkdir(parents=True)
        (backlog / "todo" / "007-M-wf.md").write_text(WF_TASK, encoding="utf-8")
        script = tmp / "t.ps1"
        script.write_text(
            "param([string]$Core, [string]$Root, [string]$Backlog, [string]$Helper, [string]$Out)\n"
            "$ast = [System.Management.Automation.Language.Parser]::ParseFile($Core, [ref]$null, [ref]$null)\n"
            "$fn = $ast.FindAll({ param($n) $n -is [System.Management.Automation.Language.FunctionDefinitionAst] "
            "-and $n.Name -eq 'Get-SkillRefsBlock' }, $true)[0]\n"
            "Invoke-Expression $fn.Extent.Text\n"
            "function Write-Log { param($m, $c) }\n"
            "$Mode = 'Current'; $BacklogRoot = $Backlog; $WorkDir = $Root; $RefsScript = $Helper\n"
            "$pythonName = '" + sys.executable.replace("'", "''") + "'\n"
            "$pick = @{ Json = [pscustomobject]@{ state = 'todo'; path = 'backlog/todo/007-M-wf.md'; nnn = '007' } }\n"
            "$b = Get-SkillRefsBlock -Pick $pick -IsRecovery $false -OutFile $Out\n"
            "[IO.File]::WriteAllText($Out + '.got', $b, (New-Object System.Text.UTF8Encoding $false))\n"
            "$RefsScript = Join-Path $Root 'missing.py'\n"
            "$b2 = Get-SkillRefsBlock -Pick $null -IsRecovery $false -OutFile ($Out + '.2')\n"
            "[IO.File]::WriteAllText($Out + '.fallback', $b2, (New-Object System.Text.UTF8Encoding $false))\n",
            encoding="utf-8")
        out = tmp / "refs.md"
        r = subprocess.run([shell, "-NoProfile", "-ExecutionPolicy", "Bypass", "-File", str(script),
                            "-Core", str(LOOP_PS), "-Root", str(SKILL_DIR.parents[2]), "-Backlog", str(backlog),
                            "-Helper", str(HELPER), "-Out", str(out)], capture_output=True, text=True)
        self.assertEqual(r.returncode, 0, r.stdout + r.stderr)
        got = Path(str(out) + ".got").read_text(encoding="utf-8")
        self.assertTrue(got.startswith("AGENT_SKILL_REFS=ref-workflow.md"), got[:120])
        self.assertIn((SKILL_DIR / "ref-workflow.md").read_text(encoding="utf-8").rstrip("\n"), got.replace("\r\n", "\n"))
        self.assertTrue(Path(str(out) + ".fallback").read_text(encoding="utf-8").startswith(FALLBACK_PREFIX))


if __name__ == "__main__":
    unittest.main()
