"""Tests for the Windows twin of auto-clear (.claude/skills/auto-clear/scripts/auto-clear.ps1).

Chạy script THẬT bằng PowerShell (`pwsh`, hoặc `powershell` trên Windows) với HOME tạm. Cây process được giả
lập trên MỌI nền tảng (kể cả Windows — không thì test bám vào process claude thật đang chạy test): hàm
`Get-CimInstance` / `Get-Process` định nghĩa trước khi gọi script (hàm thắng cmdlet cùng tên) cho cha của shell
test là `claude.exe` pid 777. Phần gõ phím (AttachConsole + WriteConsoleInput) vì vậy dừng ở attach pid 777 —
test chỉ kiểm tới bước gọi nó. Không có PowerShell → skip.

Run from the repo root:
    python3 -m unittest discover -s .claude/scripts/tests -v
"""

import base64
import json
import os
import re
import shutil
import subprocess
import tempfile
import unittest
from pathlib import Path

REPO = Path(__file__).resolve().parents[3]
SCRIPT = REPO / ".claude" / "skills" / "auto-clear" / "scripts" / "auto-clear.ps1"
PWSH = os.environ.get("PWSH") or shutil.which("pwsh") or shutil.which("powershell")
CLAUDE_PID = 777

# Cây process giả: shell test ($PID) → bash.exe 4242 → claude.exe 777. Process 777 còn sống, mọi pid khác đã chết.
MOCKS = r"""
function Get-CimInstance {
    [CmdletBinding()] param([Parameter(Position = 0)] $ClassName, $Filter)
    $id = [int]($Filter -replace '\D', '')
    if ($id -eq $PID) { return [pscustomobject]@{ ProcessId = $PID; Name = 'pwsh.exe'; ParentProcessId = 4242; CommandLine = 'pwsh' } }
    if ($id -eq 4242) { return [pscustomobject]@{ ProcessId = 4242; Name = 'bash.exe'; ParentProcessId = 777; CommandLine = 'bash' } }
    if ($id -eq 777) { return [pscustomobject]@{ ProcessId = 777; Name = 'claude.exe'; ParentProcessId = 1; CommandLine = 'claude' } }
    return $null
}
function Get-Process {
    [CmdletBinding()] param([int]$Id)
    if ($Id -eq 777) { return [pscustomobject]@{ Id = 777; ProcessName = 'claude' } }
    if ($Id -eq 888) { return [pscustomobject]@{ Id = 888; ProcessName = 'notepad' } }
    return $null
}
"""


@unittest.skipUnless(PWSH, "cần PowerShell (pwsh) để chạy auto-clear.ps1")
class AutoClearPs1Tests(unittest.TestCase):
    def setUp(self):
        self.home = Path(tempfile.mkdtemp(prefix="auto-clear-ps1-"))
        self.addCleanup(shutil.rmtree, self.home, ignore_errors=True)
        self.claude = self.home / ".claude"
        self.state = self.claude / "auto-clear"
        self.settings = self.claude / "settings.json"
        self.claude.mkdir()

    def env(self, **extra):
        return dict(os.environ, HOME=str(self.home), USERPROFILE=str(self.home), AUTO_CLEAR_DELAY="0",
                    AUTO_CLEAR_THEN_DELAY="0", AUTO_CLEAR_RETRY_DELAY="0", **extra)

    def wrapper(self, *args):
        """File .ps1 gọi script thật (kèm mock cây process) — stdin của process đi thẳng vào script."""
        quoted = " ".join("'" + a.replace("'", "''") + "'" for a in args)
        wrapper = self.home / "run.ps1"
        wrapper.write_text(MOCKS + f"\n& '{SCRIPT}' {quoted}\nexit $LASTEXITCODE\n",
                           encoding="utf-8")
        return wrapper

    def run_ps(self, *args, stdin="", env=None):
        proc = subprocess.run([PWSH, "-NoProfile", "-NonInteractive", "-File", str(self.wrapper(*args))],
                              input=stdin, capture_output=True, encoding="utf-8", env=self.env(**(env or {})),
                              cwd=self.home)
        return proc.returncode, proc.stdout + proc.stderr

    def run_hook(self, sub, payload):
        """Chạy hook như Claude Code: JSON qua stdin của process."""
        text = payload if isinstance(payload, str) else json.dumps(payload)
        return self.run_ps(sub, stdin=text)

    def log(self):
        path = self.state / "auto-clear.log"
        return path.read_text(encoding="utf-8") if path.exists() else ""

    def retry_flag(self, pid=CLAUDE_PID):
        path = self.state / f"pid-{pid}.retry"
        if not path.exists():
            return None
        return dict(line.split("=", 1) for line in path.read_text(encoding="utf-8").splitlines() if "=" in line)

    def install(self):
        code, out = self.run_ps("install")
        self.assertEqual(code, 0, out)
        self.assertIn("INSTALLED Stop + StopFailure", out)

    # --- install ---------------------------------------------------------------------------------

    def test_script_is_pure_ascii(self):
        # PowerShell 5.1 đọc .ps1 không BOM theo ANSI: một ký tự ngoài ASCII là vỡ chuỗi / vỡ cú pháp.
        SCRIPT.read_bytes().decode("ascii")

    def test_install_keeps_other_hooks_and_is_idempotent(self):
        original = {
            "model": "opus",
            "permissions": {"allow": [], "deny": ["Bash(rm -rf *)"]},
            "hooks": {
                "Stop": [{"hooks": [{"type": "command", "command": "echo khác — giữ nguyên"}]}],
                "PreToolUse": [{"matcher": "Bash", "hooks": [{"type": "command", "command": "echo pre"}]}],
            },
        }
        self.settings.write_text(json.dumps(original, ensure_ascii=False), encoding="utf-8")
        self.install()
        self.install()
        data = json.loads(self.settings.read_text(encoding="utf-8"))
        self.assertEqual(data["model"], "opus")
        self.assertEqual(data["permissions"], {"allow": [], "deny": ["Bash(rm -rf *)"]})
        self.assertEqual(data["hooks"]["PreToolUse"], original["hooks"]["PreToolUse"])
        stop = [h["command"] for g in data["hooks"]["Stop"] for h in g["hooks"]]
        fail = [h for g in data["hooks"]["StopFailure"] for h in g["hooks"]]
        self.assertEqual(stop[0], "echo khác — giữ nguyên")
        self.assertEqual(sum("auto-clear" in c for c in stop), 1)
        self.assertEqual(len(fail), 1)
        self.assertTrue(fail[0]["command"].endswith('auto-clear.ps1" failhook'))
        self.assertEqual((fail[0]["async"], fail[0]["timeout"]), (True, 120))
        self.assertTrue((self.state / "auto-clear.ps1").exists())
        self.assertTrue((self.claude / "settings.json.bak-auto-clear").exists())

    def test_script_has_version_marker_matching_bash_twin(self):
        marker = re.compile(r"^# auto-clear-version: (\d+)\s*$", re.M)
        ps1 = marker.search(SCRIPT.read_text(encoding="ascii"))
        sh = marker.search(SCRIPT.with_suffix(".sh").read_text(encoding="utf-8"))
        self.assertTrue(ps1 and sh, "cả hai bản phải có dòng auto-clear-version")
        self.assertEqual(ps1.group(1), sh.group(1), "auto-clear.ps1 và auto-clear.sh phải cùng version")

    def test_install_and_arm_never_downgrade_a_newer_hook_copy(self):
        # Project khác trên cùng máy đã cài bản mới hơn: install / retry-arm của bản này không được chép đè.
        self.install()
        copy = self.state / "auto-clear.ps1"
        newer = re.sub(r"(?m)^# auto-clear-version: \d+", "# auto-clear-version: 99", SCRIPT.read_text(encoding="ascii"))
        copy.write_text(newer, encoding="ascii", newline="")
        code, out = self.run_ps("install")
        self.assertEqual(code, 0, out)
        self.assertIn("KEPT_NEWER", out)
        code, out = self.run_ps("retry-arm", "--max", "0")
        self.assertEqual(code, 0, out)
        self.assertEqual(copy.read_text(encoding="ascii", newline=""), newer)
        self.assertIn("keep hook copy v99", self.log())

    def test_install_and_arm_replace_an_older_hook_copy(self):
        # Bản copy cũ không có dòng version (= v0, failhook không làm gì) → arm chép đè bằng bản này.
        self.install()
        copy = self.state / "auto-clear.ps1"
        copy.write_text("# auto-clear cũ\n'failhook' { exit 0 }\n", encoding="utf-8")
        code, out = self.run_ps("retry-arm", "--max", "0")
        self.assertEqual(code, 0, out)
        self.assertEqual(copy.read_bytes(), SCRIPT.read_bytes())

    def test_arm_repairs_prompt_rewritten_by_git_bash(self):
        # Git Bash đổi "/fix-bug --watch" thành "C:/Program Files/Git/fix-bug --watch" trước khi powershell nhận.
        self.install()
        git_bash = {"MSYSTEM": "MINGW64", "EXEPATH": r"C:\Program Files\Git\bin"}
        code, out = self.run_ps("retry-arm", "--prompt", "C:/Program Files/Git/fix-bug --watch", env=git_bash)
        self.assertEqual(code, 0, out)
        prompt = base64.b64decode(self.retry_flag()["prompt_b64"]).decode("utf-8")
        self.assertEqual(prompt, "/fix-bug --watch")
        code, out = self.run_ps("arm", "--then", "C:/Program Files/Git/fix-bug --watch", env=git_bash)
        self.assertEqual(code, 0, out)
        self.assertIn("then=/fix-bug --watch", out)
        # Không có MSYSTEM (không chạy từ Git Bash) → giữ nguyên chữ.
        code, out = self.run_ps("arm", "--then", "C:/Program Files/Git/x", env={"MSYSTEM": ""})
        self.assertIn("then=C:/Program Files/Git/x", out)

    def test_uninstall_removes_both_hooks(self):
        self.install()
        code, out = self.run_ps("uninstall")
        self.assertEqual(code, 0, out)
        data = json.loads(self.settings.read_text(encoding="utf-8"))
        commands = [h["command"] for event in data["hooks"].values() for g in event for h in g["hooks"]]
        self.assertFalse([c for c in commands if "auto-clear" in c])

    # --- retry-arm -------------------------------------------------------------------------------

    def test_retry_arm_needs_failhook(self):
        code, out = self.run_ps("retry-arm", "--max", "0")
        self.assertEqual(code, 3, out)
        self.assertIn("NOT_INSTALLED", out)
        self.assertIsNone(self.retry_flag())

    def test_retry_arm_validates_before_side_effects(self):
        self.install()
        code, out = self.run_ps("retry-arm", "--max", "x")
        self.assertEqual(code, 1, out)
        self.assertIn("INVALID_MAX", out)
        code, out = self.run_ps("retry-arm", "--prompt", "a" * 201)
        self.assertEqual(code, 1, out)
        self.assertIn("INVALID_THEN", out)
        self.assertIsNone(self.retry_flag())

    def test_retry_arm_writes_flag_and_drops_stale_ones(self):
        self.install()
        self.state.mkdir(exist_ok=True)
        for name in ("pid-999.retry", "pid-999.flag", "pid-888.retry"):  # 999 đã chết, 888 không phải claude
            (self.state / name).write_text("pid=x\n")
        code, out = self.run_ps("retry-arm", "--max", "0", "--prompt", "Continue /fix-bug --watch")
        self.assertEqual(code, 0, out)
        self.assertIn(f"RETRY_ARMED pid={CLAUDE_PID} delay=0s max=unlimited", out)
        flag = self.retry_flag()
        self.assertEqual((flag["count"], flag["max"]), ("0", "0"))
        self.assertFalse((self.state / "pid-999.retry").exists())
        self.assertFalse((self.state / "pid-999.flag").exists())
        self.assertFalse((self.state / "pid-888.retry").exists())

        code, out = self.run_ps("status")
        self.assertIn("FAILHOOK installed", out)
        self.assertIn(f"RETRY_ARMED pid={CLAUDE_PID} count=0/unlimited", out)

    # --- failhook / Stop hook --------------------------------------------------------------------

    def test_failhook_counts_and_reaches_the_typing_step(self):
        self.install()
        self.run_ps("retry-arm", "--max", "0", "--prompt", "Continue /fix-bug --watch")
        code, _ = self.run_hook("failhook", {"hook_event_name": "StopFailure", "error": "rate_limit"})
        self.assertEqual(code, 0)
        self.assertEqual(self.retry_flag()["count"], "1")
        log = self.log()
        self.assertIn(f"failhook pid={CLAUDE_PID} error=rate_limit try 1/unlimited", log)
        if os.name != "nt":
            # Không có console Windows để attach: phải tới đúng bước gõ phím rồi mới hỏng, và hook vẫn exit 0.
            self.assertIn("failhook error:", log)

    def test_failhook_gives_up_after_max_and_stop_hook_resets(self):
        self.install()
        self.run_ps("retry-arm", "--max", "1")
        self.run_hook("failhook", {"error": "overloaded"})
        self.run_hook("failhook", {"error": "overloaded"})
        self.assertIn("failhook give-up", self.log())
        self.assertEqual(self.retry_flag()["count"], "1")
        code, _ = self.run_hook("hook", {"hook_event_name": "Stop"})
        self.assertEqual(code, 0)
        self.assertEqual(self.retry_flag()["count"], "0")
        self.assertIn(f"retry-reset pid={CLAUDE_PID}", self.log())

    def test_failhook_without_flag_does_nothing(self):
        self.install()
        code, _ = self.run_hook("failhook", {"error": "server_error"})
        self.assertEqual(code, 0)
        self.assertNotIn("failhook", self.log())

    def test_failhook_survives_garbage_stdin(self):
        self.install()
        self.run_ps("retry-arm", "--max", "0")
        code, _ = self.run_hook("failhook", "not json")
        self.assertEqual(code, 0)
        self.assertIn("error=? try 1/unlimited", self.log())

    # --- off / retry-off / arm --------------------------------------------------------------------

    def test_off_drops_both_flags_and_retry_off_only_retry(self):
        self.install()
        self.run_ps("retry-arm", "--max", "0")
        code, out = self.run_ps("retry-off")
        self.assertIn(f"RETRY_DISARMED pid={CLAUDE_PID}", out)
        self.assertIsNone(self.retry_flag())

        self.run_ps("retry-arm", "--max", "0")
        code, out = self.run_ps("arm", "--then", "/fix-bug --watch")
        self.assertEqual(code, 0, out)
        self.assertIn(f"ARMED pid={CLAUDE_PID}", out)
        self.assertIn("then=/fix-bug --watch", out)
        code, out = self.run_ps("off")
        self.assertIn("RETRY_DISARMED", out)
        self.assertIn("DISARMED", out)
        self.assertIsNone(self.retry_flag())
        self.assertFalse((self.state / f"pid-{CLAUDE_PID}.flag").exists())

    def test_unknown_subcommand_prints_usage(self):
        code, out = self.run_ps("nope")
        self.assertEqual(code, 1)
        self.assertIn("retry-arm", out)

    def test_inject_source_compiles(self):
        # C# P/Invoke phải biên dịch được (Add-Type) — lỗi ở đây chỉ lộ ra trên Windows lúc hook chạy thật.
        command = (f"$src = (Get-Content -Raw '{SCRIPT}');"
                   "$start = $src.IndexOf(\"@'\") + 3; $end = $src.IndexOf(\"'@\");"
                   "Add-Type -TypeDefinition $src.Substring($start, $end - $start);"
                   "[AutoClear.ConsoleInject].GetMethod('Send').Name")
        proc = subprocess.run([PWSH, "-NoProfile", "-NonInteractive", "-Command", command],
                              capture_output=True, encoding="utf-8")
        self.assertEqual(proc.stdout.strip(), "Send", proc.stderr)


if __name__ == "__main__":
    unittest.main()
