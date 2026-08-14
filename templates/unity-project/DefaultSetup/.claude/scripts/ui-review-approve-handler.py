#!/usr/bin/env python3
"""OS-level protocol handler for <gitConfigPrefix>-approve:// — runs ui-review.py approve directly
with NO AI agent in the loop (deterministic script execution only). Registered as the handler for
that URL scheme by setup-approve-handler-macos.sh / -windows.ps1 (the scheme is namespaced per
project so two games on this base don't fight over one machine-wide registration); invoked by the
OS with the raw URL as argv[1] when the dashboard's Approve button is clicked.

Bounded blast radius by design: this only ever calls `ui-review.py approve`, which itself refuses
anything whose specHash doesn't match the CURRENT on-disk spec (see approve_screen() in
ui-review.py) and restricts paths to under TechSpec/Mockups (see safe_html()). It cannot run
arbitrary commands or approve content that isn't already sitting unmodified in the repo.
"""

from __future__ import annotations

import json
import platform
import subprocess
import sys
from pathlib import Path
from urllib.parse import parse_qs, urlparse

# Running as a script puts this directory on sys.path, so the plain import works.
from project_profile import profile

ROOT = Path(__file__).resolve().parents[2]
REVIEW_SCRIPT = ROOT / ".claude" / "scripts" / "ui-review.py"
# Notification title — the project's own name, so a dev with two games open can
# tell which one just approved something.
NOTIFY_TITLE = f"{profile().project_name} Approve"


def notify(title: str, message: str) -> None:
    system = platform.system()
    try:
        if system == "Darwin":
            # AppleScript string literals are DOUBLE-quoted. Python's !r emits
            # single quotes, which osascript rejects outright (-2741 syntax
            # error) — so the notification silently never appeared. json.dumps
            # produces a correctly escaped double-quoted literal.
            script = f"display notification {json.dumps(message)} with title {json.dumps(title)}"
            subprocess.run(["osascript", "-e", script], check=False)
        elif system == "Windows":
            ps = (
                "Add-Type -AssemblyName System.Windows.Forms; "
                "$n = New-Object System.Windows.Forms.NotifyIcon; "
                "$n.Icon = [System.Drawing.SystemIcons]::Information; "
                "$n.Visible = $true; "
                f"$n.ShowBalloonTip(4000, {title!r}, {message!r}, 'Info')"
            )
            subprocess.run(["powershell", "-NoProfile", "-Command", ps], check=False)
        else:
            print(f"[{title}] {message}")
    except OSError:
        pass


def main() -> int:
    if len(sys.argv) < 2:
        notify(NOTIFY_TITLE, "Khong nhan duoc URL nao")
        return 1

    parsed = urlparse(sys.argv[1])
    params = parse_qs(parsed.query)
    items = params.get("item", [])

    if not items:
        notify(NOTIFY_TITLE, "URL khong co item nao de approve")
        return 1

    cmd = [sys.executable, str(REVIEW_SCRIPT), "approve"]
    for item in items:
        cmd += ["--item", item]

    result = subprocess.run(cmd, cwd=str(ROOT), capture_output=True, text=True)
    if result.returncode == 0:
        notify(NOTIFY_TITLE, f"Da approve {len(items)} man hinh")
        return 0

    tail = (result.stdout or result.stderr or "unknown error")[-300:]
    notify(f"{NOTIFY_TITLE} - THAT BAI", tail)
    return result.returncode


if __name__ == "__main__":
    raise SystemExit(main())
