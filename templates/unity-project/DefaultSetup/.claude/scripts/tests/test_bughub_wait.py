"""Tests for bughub-wait.sh / bughub-wait.ps1 (waiting for BugHub bugs for /fix-bug --watch) against a fake server on localhost.

Both versions must keep the same contract: exit 0 + exactly one JSON line when there is a `retry` bug, exit 3 on 404,
keep polling on server errors, and touch the `BUGHUB_WATCH_HEARTBEAT` file on every poll (the watchdog of
`bughub-watch.py start`). The .sh version also: no working Python (python3 is only a stub) → exit 1 right away,
instead of silently polling on. The .ps1 version runs with pwsh/powershell if available, otherwise it is skipped.

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
    """Returns the (status, body) pairs of `replies` in turn; once the list runs out, repeats the last one."""

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
    """Tests shared by both versions — subclasses define `command(code)`."""

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
        self.assertTrue(self.beat.exists(), "bughub-wait must touch the heartbeat file on every poll")

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


@unittest.skipUnless(BASH, "needs bash")
class WaitShTests(WaitContract, unittest.TestCase):
    def command(self, code):
        return [BASH, str(SCRIPTS / "bughub-wait.sh"), code]

    def stub_python(self, body):
        """Put fake python3 / python / py first on PATH — simulates the Microsoft Store / Xcode CLT stubs."""
        stub_dir = self.tmp / "stub-bin"
        stub_dir.mkdir()
        for name in ("python3", "python", "py"):
            path = stub_dir / name
            path.write_bytes(("#!/bin/sh\n" + body).encode("utf-8"))
            path.chmod(0o755)
        old_path = os.environ["PATH"]
        os.environ["PATH"] = str(stub_dir) + os.pathsep + old_path
        self.addCleanup(os.environ.__setitem__, "PATH", old_path)

    def test_python_stub_exits_1_instead_of_polling_forever(self):
        # `command -v python3` finds the stub but running it fails: must exit 1 so the model knows, not hang silently.
        self.stub_python('echo "Python was not found" >&2\nexit 49\n')
        proc, server = self.run_wait([(200, {"new": 0, "retry": 1})], timeout=20)
        self.assertEqual(proc.returncode, 1, proc.stderr)
        self.assertEqual(proc.stdout, "")
        self.assertEqual(server.hits, 0, "no polling until a working Python exists")

    def test_python_breaking_mid_run_exits_1(self):
        # Passes the probe (import json, sys) but fails while parsing: a tool error, not a bad payload.
        self.stub_python('[ "$2" = "import json, sys" ] && exit 0\nexit 49\n')
        proc, server = self.run_wait([(200, {"new": 0, "retry": 1})], timeout=20)
        self.assertEqual(proc.returncode, 1, proc.stderr)
        self.assertEqual(server.hits, 1)


@unittest.skipUnless(PWSH, "needs PowerShell (pwsh) to run bughub-wait.ps1")
class WaitPs1Tests(WaitContract, unittest.TestCase):
    def command(self, code):
        return [PWSH, "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-File",
                str(SCRIPTS / "bughub-wait.ps1"), code]

    def test_script_keeps_utf8_bom(self):
        # PowerShell 5.1 reads a file without BOM as ANSI → non-ASCII strings break the syntax.
        self.assertTrue((SCRIPTS / "bughub-wait.ps1").read_bytes().startswith(b"\xef\xbb\xbf"))


if __name__ == "__main__":
    unittest.main()
