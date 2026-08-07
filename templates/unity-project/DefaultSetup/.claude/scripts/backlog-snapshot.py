#!/usr/bin/env python3
"""One-call staged-diff snapshot for /run-backlog quality gates.

Collapses the STEP 6a/6b/7d command cluster

    git diff --staged --name-only
    git diff --staged
    backlog-preflight.py -Pretty
    git add -A                       # preflight-fix round
    backlog-preflight.py -Pretty
    git diff --staged --name-only
    git diff --staged

into a single tool call, and keeps the full diff OUT of the model's context.

Why this exists
---------------
Every Bash call re-reads the whole conversation context from cache. Measured on
the 061 run: 103 Bash calls at ~103K average context = ~10.6M cache-read tokens
for the shell alone -- ~36% of that task's total tokens. The cost scales with the
NUMBER of calls, not the size of each command's output. So the fix is fewer
calls, not shorter output.

This wrapper is deterministic: it does not rely on the model remembering to batch
anything. STEP 6a/6b/7d each collapse from 4-9 Bash calls to 1.

The full diff is written to a file and referenced by path, never echoed to
stdout. A 200KB diff pasted into stdout would be re-read by every subsequent
tool call for the rest of the task; on disk it costs nothing until something
actually opens it. Reviewers that need the whole diff read `diff_path`; the
orchestrator reads the summary.

Preflight is invoked as a subprocess rather than imported so that the
.py/.ps1 twin discipline in backlog-preflight.* stays intact -- rules live in
exactly one place and this file never needs to change when they do. It runs
under sys.executable, so it inherits the working interpreter (relevant on
Windows, where `python3` is a Microsoft Store stub -- see PROJECT_NOTES sec 1).

Usage:
    py .claude/scripts/backlog-snapshot.py                    # compact JSON
    py .claude/scripts/backlog-snapshot.py --pretty
    py .claude/scripts/backlog-snapshot.py --stage            # git add -A first
    py .claude/scripts/backlog-snapshot.py --label review-after
    py .claude/scripts/backlog-snapshot.py --no-preflight     # diff capture only

Exit codes:
    0  snapshot ok
    2  no staged changes  -> STEP 6a NO_CHANGES
    3  preflight summary.has_blocking_definite = true
    1  internal error (git/preflight failed)
"""

import argparse
import json
import os
import subprocess
import sys

SCRIPT_DIR = os.path.dirname(os.path.abspath(__file__))
REPO_ROOT = os.path.dirname(os.path.dirname(SCRIPT_DIR))
TMP_DIR = os.path.join(REPO_ROOT, ".claude", "tmp", "backlog")
PREFLIGHT = os.path.join(SCRIPT_DIR, "backlog-preflight.py")


def git_bytes(args, check=True):
    """Run git in REPO_ROOT and return raw stdout bytes.

    Deliberately NOT text=True: that decodes with the locale codec, which on a
    stock Windows box is cp1252 and dies on the first non-cp1252 byte in a diff
    (UnicodeDecodeError inside subprocess's reader thread -> stdout silently
    becomes None). Same failure family as the PS 5.1 ANSI trap in PROJECT_NOTES
    sec 1. Callers decode explicitly, or write the bytes straight through.
    """
    proc = subprocess.run(
        ["git"] + args, cwd=REPO_ROOT,
        stdout=subprocess.PIPE, stderr=subprocess.PIPE,
    )
    if check and proc.returncode != 0:
        raise RuntimeError(
            "git {} failed ({}): {}".format(
                " ".join(args), proc.returncode,
                proc.stderr.decode("utf-8", "replace").strip(),
            )
        )
    return proc.stdout


def git(args, check=True):
    """git_bytes decoded as UTF-8. For metadata (names, stat) -- not the diff."""
    return git_bytes(args, check).decode("utf-8", "replace")


def run_preflight():
    """Invoke the preflight twin and parse its JSON verdict."""
    proc = subprocess.run(
        [sys.executable, PREFLIGHT],
        cwd=REPO_ROOT,
        stdout=subprocess.PIPE, stderr=subprocess.PIPE,
        encoding="utf-8", errors="replace",
        env=dict(os.environ, PYTHONUTF8="1"),
    )
    if proc.returncode != 0:
        raise RuntimeError(
            "preflight failed ({}): {}".format(
                proc.returncode, (proc.stderr or proc.stdout).strip()
            )
        )
    try:
        return json.loads(proc.stdout)
    except json.JSONDecodeError as exc:
        raise RuntimeError(
            "preflight returned non-JSON: {}".format(exc)
        ) from exc


def main():
    parser = argparse.ArgumentParser(
        description="One-call staged-diff snapshot + preflight for /run-backlog."
    )
    parser.add_argument(
        "--stage", action="store_true",
        help="run `git add -A` before snapshotting (preflight-fix rounds)",
    )
    parser.add_argument(
        "--label", default="staged",
        help="basename for the diff file, e.g. review-before / review-after",
    )
    parser.add_argument(
        "--no-preflight", action="store_true",
        help="capture the diff only; skip the preflight verdict",
    )
    parser.add_argument("--pretty", action="store_true", help="indent the JSON")
    args = parser.parse_args()

    try:
        if args.stage:
            git(["add", "-A"])

        names = [f for f in git(["diff", "--staged", "--name-only"]).splitlines() if f]

        if not names:
            out = {"file_count": 0, "files": [], "no_changes": True}
            print(json.dumps(out, indent=2 if args.pretty else None))
            return 2

        os.makedirs(TMP_DIR, exist_ok=True)
        diff_path = os.path.join(TMP_DIR, args.label + ".diff")
        # Written as raw bytes: a diff can carry any encoding the repo contains,
        # and this file is a review artifact -- round-tripping it through a lossy
        # decode would hand reviewers mangled text.
        diff_bytes = git_bytes(["diff", "--staged"])
        with open(diff_path, "wb") as fh:
            fh.write(diff_bytes)

        result = {
            "file_count": len(names),
            "files": names,
            "stat": git(["diff", "--staged", "--stat"]).strip(),
            # Path, not content: reviewers Read this; the orchestrator does not
            # need the bytes in context.
            "diff_path": os.path.relpath(diff_path, REPO_ROOT).replace("\\", "/"),
            "diff_bytes": len(diff_bytes),
        }

        exit_code = 0
        if not args.no_preflight:
            pf = run_preflight()
            result["preflight"] = pf
            if pf.get("summary", {}).get("has_blocking_definite"):
                exit_code = 3

        print(json.dumps(result, indent=2 if args.pretty else None, ensure_ascii=False))
        return exit_code

    except (RuntimeError, OSError) as exc:
        print(json.dumps({"error": str(exc)}), file=sys.stderr)
        return 1


if __name__ == "__main__":
    sys.exit(main())
