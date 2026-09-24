#!/usr/bin/env python3
"""Stop hook: no "done" after a Figma bridge change without a passing visual check.

Registered in the figma-to-unity SKILL.md frontmatter, so it is active from the skill's first use
until the session ends. It blocks once (Claude Code sets stop_hook_active on the retry) when:

- Packages/manifest.json takes com.ezg.figma-bridge from a local `file:` path, and
- a .cs file of that package changed during this session, and
- no visual check report is newer than that change, or the newest report failed.

A registry bridge cannot be edited in place, so it never blocks. Any error lets the stop through:
a broken gate must not trap a session. Standard library only.
"""
import glob
import json
import os
import sys
import time

PACKAGE = "com.ezg.figma-bridge"
SESSION_FALLBACK_SECONDS = 12 * 3600
ROUNDTRIP = "python3 .claude/skills/figma-to-unity/scripts/roundtrip.py <screen prefab> --import offline"


def local_bridge_folder(project):
    manifest = os.path.join(project, "Packages", "manifest.json")
    if not os.path.isfile(manifest):
        return None
    with open(manifest, encoding="utf-8") as handle:
        source = json.load(handle).get("dependencies", {}).get(PACKAGE, "")
    if not source.startswith("file:"):
        return None
    folder = os.path.normpath(os.path.join(project, "Packages", source[len("file:"):]))
    return folder if os.path.isdir(folder) else None


def newest_source(folder):
    newest, newest_path = 0.0, None
    for directory, subdirectories, files in os.walk(folder):
        subdirectories[:] = [d for d in subdirectories if not d.startswith(".")]
        for name in files:
            if name.endswith(".cs"):
                path = os.path.join(directory, name)
                modified = os.path.getmtime(path)
                if modified > newest:
                    newest, newest_path = modified, path
    return newest, newest_path


def session_start(transcript_path):
    try:
        stat = os.stat(transcript_path)
        return getattr(stat, "st_birthtime", stat.st_ctime)
    except (OSError, TypeError):
        return time.time() - SESSION_FALLBACK_SECONDS


def block(reason):
    print(json.dumps({"decision": "block", "reason": reason}))
    return 0


def main():
    try:
        event = json.loads(sys.stdin.read() or "{}")
    except ValueError:
        event = {}
    if event.get("stop_hook_active"):
        return 0

    project = os.environ.get("CLAUDE_PROJECT_DIR") or event.get("cwd") or os.getcwd()
    bridge = local_bridge_folder(project)
    if bridge is None:
        return 0
    changed_at, changed_file = newest_source(bridge)
    if changed_file is None or changed_at < session_start(event.get("transcript_path")):
        return 0

    reports = glob.glob(os.path.join(project, "Library", "FigmaVisualCheck", "*", "report.json"))
    newest_report = max(reports, key=os.path.getmtime) if reports else None
    changed = os.path.relpath(changed_file, bridge)
    if newest_report is None or os.path.getmtime(newest_report) < changed_at:
        return block(f"The Figma bridge source changed in this session ({changed}) and no visual check ran "
                     f"after it. Re-import and score the affected screen: {ROUNDTRIP} (use --import online when "
                     "server renders changed). Pass: 0.90 per container, 0.85 for text-only containers. If the "
                     "check cannot run, tell the user why instead of reporting the fix as done.")

    with open(newest_report, encoding="utf-8") as handle:
        report = json.load(handle)
    if not report.get("passed"):
        low = [c for c in report.get("containers", []) if c.get("score", 0) < c.get("passScore", 0)]
        worst = min(low, key=lambda c: c["score"] - c["passScore"]) if low else None
        detail = f"; worst {worst['score']:.3f} at {worst['path']}" if worst else ""
        return block(f"The last visual check of {report.get('prefab')} failed ({len(low)} container(s) below pass"
                     f"{detail}). Crops: {os.path.join(os.path.dirname(newest_report), 'low')}. Fix and re-run "
                     f"{ROUNDTRIP}, or tell the user why it stays failed.")
    return 0


if __name__ == "__main__":
    try:
        sys.exit(main())
    except Exception as error:  # the gate must never trap a session
        print(f"[check_gate] skipped: {error}", file=sys.stderr)
        sys.exit(0)
