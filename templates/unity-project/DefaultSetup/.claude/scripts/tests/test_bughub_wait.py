"""Tests for bughub-wait.sh / bughub-wait.ps1 (chờ bug BugHub cho /fix-bug --watch) với server giả trên localhost.

Hai bản phải giữ cùng hợp đồng: exit 0 + đúng một dòng JSON khi có bug `retry`, exit 3 khi 404, poll tiếp khi
server lỗi, và chạm file `BUGHUB_WATCH_HEARTBEAT` mỗi lần poll (watchdog của `bughub-watch.py start`).
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
