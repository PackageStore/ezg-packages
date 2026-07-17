#!/usr/bin/env python3
"""OS-level protocol handler for ezg-ui-approve:// — runs ui-review.py approve directly with
NO AI agent in the loop (deterministic script execution only). Registered as the handler for the
ezg-ui-approve:// URL scheme by setup-approve-handler-macos.sh / setup-approve-handler-windows.ps1;
invoked by the OS with the raw URL as argv[1] when the dashboard's Approve button is clicked.

Bounded blast radius by design: this only ever calls `ui-review.py approve`, which itself refuses
anything whose specHash doesn't match the CURRENT on-disk spec (see approve_screen() in
ui-review.py) and restricts paths to under TechSpec/Mockups (see safe_html()). It cannot run
arbitrary commands or approve content that isn't already sitting unmodified in the repo.
"""

from __future__ import annotations

import platform
import subprocess
import sys
from pathlib import Path
from urllib.parse import parse_qs, urlparse

ROOT = Path(__file__).resolve().parents[2]
REVIEW_SCRIPT = ROOT / ".claude" / "scripts" / "ui-review.py"


def notify(title: str, message: str) -> None:
    system = platform.system()
    try:
        if system == "Darwin":
            script = f"display notification {message!r} with title {title!r}"
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
        notify("EZG UI Approve", "Khong nhan duoc URL nao")
        return 1

    parsed = urlparse(sys.argv[1])
    params = parse_qs(parsed.query)
    items = params.get("item", [])

    if not items:
        notify("EZG UI Approve", "URL khong co item nao de approve")
        return 1

    cmd = [sys.executable, str(REVIEW_SCRIPT), "approve"]
    for item in items:
        cmd += ["--item", item]

    result = subprocess.run(cmd, cwd=str(ROOT), capture_output=True, text=True)
    if result.returncode == 0:
        notify("EZG UI Approve", f"Da approve {len(items)} man hinh")
        return 0

    tail = (result.stdout or result.stderr or "unknown error")[-300:]
    notify("EZG UI Approve - THAT BAI", tail)
    return result.returncode


if __name__ == "__main__":
    raise SystemExit(main())
