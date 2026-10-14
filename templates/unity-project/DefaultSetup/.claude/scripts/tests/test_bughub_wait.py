"""Tests for bughub-wait.sh / bughub-wait.ps1 (chờ bug BugHub cho /fix-bug --watch) với server giả trên localhost.

Hai bản phải giữ cùng hợp đồng: exit 0 + đúng một dòng JSON khi có bug `retry`, exit 3 khi 404, poll tiếp khi
server lỗi, và chạm file `BUGHUB_WATCH_HEARTBEAT` mỗi lần poll (watchdog của `bughub-watch.py start`). Bản .sh
thêm: không có Python chạy được (python3 chỉ là stub) → exit 1 ngay, không poll tiếp im lặng.
Bản .ps1 chạy bằng pwsh/powershell nếu có, không thì skip.

Run from the repo root:
    python3 -m unittest discover -s .claude/scripts/tests -v
"""

import json
import os
import shutil
import subprocess
import tempfile
import threading
import unittest
from http.server import BaseHTTPRequestHandler, HTTPServer
from pathlib import Path

REPO = Path(__file__).resolve().parents[3]
SCRIPTS = REPO / ".claude" / "scripts"
PWSH = os.environ.get("PWSH") or shutil.which("pwsh") or shutil.which("powershell")
BASH = shutil.which("bash")


class FakeBugHub:
    """Trả lần lượt các (status, body) trong `replies`; hết danh sách thì lặp lại phần tử cuối."""

    def __init__(self, replies):
        self.replies, self.hits = list(replies), 0
        owner = self

        class Handler(BaseHTTPRequestHandler):
            def do_GET(self):
                status, body = owner.replies[min(owner.hits, len(owner.replies) - 1)]
                owner.hits += 1
                data = json.dumps(body).encode() if not isinstance(body, bytes) else body
                self.send_response(status)
                self.send_header("Content-Type", "application/json")
                self.send_header("Content-Length", str(len(data)))
                self.end_headers()
                self.wfile.write(data)

            def log_message(self, *_):
                pass

        self.server = HTTPServer(("127.0.0.1", 0), Handler)
        threading.Thread(target=self.server.serve_forever, daemon=True).start()
        self.endpoint = f"http://127.0.0.1:{self.server.server_port}"

    def close(self):
        self.server.shutdown()
        self.server.server_close()


class WaitContract:
    """Test dùng chung cho hai bản — lớp con đặt `command(code)`."""

    def setUp(self):
        self.tmp = Path(tempfile.mkdtemp(prefix="bughub-wait-"))
        self.addCleanup(shutil.rmtree, self.tmp, ignore_errors=True)
        self.beat = self.tmp / "beat"

    def run_wait(self, replies, code="T1", beat=True, timeout=60):
        server = FakeBugHub(replies)
        self.addCleanup(server.close)
        env = dict(os.environ, BUGHUB_ENDPOINT=server.endpoint, BUGHUB_POLL_INTERVAL="1")
        if beat:
            env["BUGHUB_WATCH_HEARTBEAT"] = self.beat.as_posix()
        else:
            env.pop("BUGHUB_WATCH_HEARTBEAT", None)
        proc = subprocess.run(self.command(code), capture_output=True, encoding="utf-8", env=env,
                              stdin=subprocess.DEVNULL, timeout=timeout)
        return proc, server

    def test_wakes_on_retry_and_touches_heartbeat(self):
        proc, server = self.run_wait([(200, {"new": 2, "retry": 0}), (500, {}), (200, {"new": 2, "retry": 1})])
        self.assertEqual(proc.returncode, 0, proc.stderr)
        self.assertEqual([json.loads(line) for line in proc.stdout.splitlines() if line.strip()],
                         [{"new": 2, "retry": 1}])
        self.assertGreaterEqual(server.hits, 3)
        self.assertTrue(self.beat.exists(), "bughub-wait phải chạm file heartbeat mỗi lần poll")

    def test_unknown_project_exits_3(self):
        proc, _ = self.run_wait([(404, {"error": "unknown_project"})])
        self.assertEqual(proc.returncode, 3, proc.stderr)

    def test_bad_code_exits_2(self):
        proc, _ = self.run_wait([(200, {"new": 0, "retry": 1})], code="a b")
        self.assertEqual(proc.returncode, 2)

    def test_works_without_heartbeat_env(self):
        proc, _ = self.run_wait([(200, {"new": 0, "retry": 1})], beat=False)
        self.assertEqual(proc.returncode, 0, proc.stderr)
        self.assertFalse(self.beat.exists())


@unittest.skipUnless(BASH, "cần bash")
class WaitShTests(WaitContract, unittest.TestCase):
    def command(self, code):
        return [BASH, str(SCRIPTS / "bughub-wait.sh"), code]

    def stub_python(self, body):
        """Đặt python3 / python / py giả lên đầu PATH — mô phỏng stub của Microsoft Store / Xcode CLT."""
        stub_dir = self.tmp / "stub-bin"
        stub_dir.mkdir()
        for name in ("python3", "python", "py"):
            path = stub_dir / name
            path.write_text("#!/bin/sh\n" + body, encoding="utf-8", newline="\n")
            path.chmod(0o755)
        old_path = os.environ["PATH"]
        os.environ["PATH"] = str(stub_dir) + os.pathsep + old_path
        self.addCleanup(os.environ.__setitem__, "PATH", old_path)

    def test_python_stub_exits_1_instead_of_polling_forever(self):
        # `command -v python3` thấy stub nhưng chạy thì lỗi: phải thoát 1 để model biết, không treo im lặng.
        self.stub_python('echo "Python was not found" >&2\nexit 49\n')
        proc, server = self.run_wait([(200, {"new": 0, "retry": 1})], timeout=20)
        self.assertEqual(proc.returncode, 1, proc.stderr)
        self.assertEqual(proc.stdout, "")
        self.assertEqual(server.hits, 0, "chưa có Python chạy được thì không poll")

    def test_python_breaking_mid_run_exits_1(self):
        # Qua được bước dò (import json, sys) nhưng lỗi khi parse: lỗi công cụ, không phải payload hỏng.
        self.stub_python('[ "$2" = "import json, sys" ] && exit 0\nexit 49\n')
        proc, server = self.run_wait([(200, {"new": 0, "retry": 1})], timeout=20)
        self.assertEqual(proc.returncode, 1, proc.stderr)
        self.assertEqual(server.hits, 1)


@unittest.skipUnless(PWSH, "cần PowerShell (pwsh) để chạy bughub-wait.ps1")
class WaitPs1Tests(WaitContract, unittest.TestCase):
    def command(self, code):
        return [PWSH, "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-File",
                str(SCRIPTS / "bughub-wait.ps1"), code]

    def test_script_keeps_utf8_bom(self):
        # PowerShell 5.1 đọc file không BOM theo ANSI → chuỗi tiếng Việt vỡ cú pháp.
        self.assertTrue((SCRIPTS / "bughub-wait.ps1").read_bytes().startswith(b"\xef\xbb\xbf"))


if __name__ == "__main__":
    unittest.main()
