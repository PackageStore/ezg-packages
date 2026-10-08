#!/usr/bin/env python3
"""backlog-ops — deterministic bookkeeping for the split-file backlog.

Owns every mechanical backlog transition so the model never hand-edits
BACKLOG.md or invents timestamps (prose-driven bookkeeping has already
corrupted the index once: leaked tool-call markup, dual-state task files,
forbidden DONE bullets). Sibling of backlog-preflight.py.

LOCATION — the backlog lives in `$(git rev-parse --git-common-dir)/backlog/`
(i.e. `.git/backlog/`), NOT in the worktree. Two reasons:
  * It is per-developer bookkeeping, not shared source. Keeping it in the tree
    made every dev branch carry its own BACKLOG.md + task files, so merging two
    devs into main conflicted on the index and collided on NNN numbering.
  * `.git/` is shared by every linked worktree of the same clone, so an agent
    running in a `git worktree` sees the SAME queue as the dev's main checkout
    with no symlink, no gitignore entry, and no copy step.
Nothing here is ever git-tracked: task transitions are plain filesystem moves
(a `git mv` inside `.git/` is meaningless and would hard-fail). The bullet
paths in BACKLOG.md stay written as `backlog/<state>/<file>.md`, resolved
relative to the git common dir.

Commands
  init                        Create the backlog root (dirs + BACKLOG.md skeleton).
  lint                        Directory<->index consistency check (read-only).
  pick                        Print the task run-backlog must work on (JSON).
  start   <NNN>               todo -> in-progress (move + index bullet move).
  done    <NNN>               in-progress -> done (move + bullet removal).
  demote  <NNN>               in-progress -> todo (abandon a blocked run; bullet
                              returns to the head of ## TODO).
  defer   <NNN>               Move a TODO bullet to the TAIL of ## TODO (file
                              stays in todo/). Used by run-backlog to skip a
                              task whose **Requires:** (e.g. unity-editor) is
                              not live, without dead-ending the loop.
  park    <NNN> [--reason R]  Give up on a task for this loop run without stopping
                              the loop (self-heal budget spent): IN PROGRESS/TODO
                              -> tail of ## TODO. Partial work is saved to
                              refs/backlog/parked/<NNN> and removed from the tree
                              (--keep-work leaves it); `start` reports it later,
                              `shipped` deletes the ref.
  promote <planning.md>...     planning -> todo (assign NNN, append bullets).
                              --check validates the batch without mutation.
                              Optional --priority HIGH|MEDIUM|LOW for all files.
                              Blocks when a task's **Depends on:** target is not
                              earlier in the batch nor in todo/in-progress/done,
                              and when a /new-ui task's groundTruth is still
                              PENDING-MOCKUP / PENDING-APPROVAL (mockup not yet
                              auto-approved — run /ui-mockup).
  timestamp                   Print UTC YYYYMMDDTHHmmssSSS (planning filenames).

Concurrency + resume (see "Concurrency & resume" below and run-backlog STEP 1):
  resume  <NNN>               Take over the claim of an IN PROGRESS / ship-pending
                              task whose previous run died, and print what that
                              run left behind (partial files, checkpoints).
  checkpoint <NNN> --step S   Journal a finished pipeline step (S in
                              implemented|compile|preflight|review|qa|smoke),
                              optional --result R --note N.
  checkpoint <NNN> --step commit --tree T
                              STEP 9e, right before `git commit`: record the staged
                              tree (`git write-tree`) on the ship-pending claim — how
                              a run that dies before `shipped` is later found to
                              have committed already (no trailer in the message).
  task-commit <NNN>           Read-only: the commit that carries a task (shipped
                              record, else its recorded tree), or null.
  shipped <NNN> --commit SHA  Close a DONE task's run once its commit exists
                              (--push pushed|no-remote|failed|skipped).
  lock acquire --pid P ...    Take the single loop lease (loop controllers only).
  lock release --token T      Give it back.
  lock status                 Who holds the lease / which tasks are claimed.
  lock break --yes [--task N] Manual override for a holder you know is dead.

Every mutating command re-runs lint afterwards and embeds the result.
--dry-run prints the plan without touching anything.

Exit codes: 0 = ok · 1 = lint errors / operation failure · 2 = pick found nothing
· 3 = backlog root missing (run `init`) · 4 = busy (another loop holds the lease,
or a live agent owns the task — change nothing) · 5 = resume conflict (the
partial work lives in another checkout/branch).
"""

import argparse
import json
import os
import re
import secrets
import shutil
import socket
import subprocess
import sys
import time
from contextlib import contextmanager, nullcontext
from datetime import datetime, timezone
from pathlib import Path

PRIORITIES = ("HIGH", "MEDIUM", "LOW")
TIERS = ("XS", "S", "M", "L")

# The regex accepts legacy tier-less bullets so lint can report a precise tier
# invariant error. New TODO / IN PROGRESS bullets must always carry [TIER].
BULLET_RE = re.compile(
    r"^- \[(?P<priority>HIGH|MEDIUM|LOW)\]"
    r"(?: \[(?P<tier>XS|S|M|L)\])? "
    r"\[(?P<title>.+?)\]\((?P<path>backlog/(?:todo|in-progress|done)/[^)]+)\)"
)
NONE_RE = re.compile(r"^- \(none\)\s*$")
NNN_RE = re.compile(r"^(\d{3,})-")
# Leaked tool-call markup is always a tag at line start; anchoring avoids
# false-positives on titles that merely contain angle brackets mid-line.
MARKUP_RE = re.compile(r"^\s*</?(?:antml:)?(?:content|invoke|parameter|function_calls)(?:[\s>/]|$)")
PLANNING_RE = re.compile(
    r"^(?P<ts>\d{8}T\d{6,9})(?:-(?P<idx>\d+))?-(?P<tier>XS|S|M|L)-(?P<slug>.+)\.md$"
)
QUEUE_RE = re.compile(
    r"^(?P<nnn>\d{3,})-(?P<tier>XS|S|M|L)-(?P<slug>.+)\.md$"
)
HEADING_RE = re.compile(r"^#{1,4} \[(?P<priority>HIGH|MEDIUM|LOW)\] (?P<title>.+?)\s*$")
BODY_TIER_RE = re.compile(r"^\*\*Tier:\*\*\s*(?P<tier>XS|S|M|L)\s*$", re.IGNORECASE)
DEPENDS_RE = re.compile(r"^\*\*Depends on:\*\*\s*(?P<deps>.+?)\s*$", re.IGNORECASE)
DEP_REF_RE = re.compile(r"`\s*([^`\s]+\.md)\s*`")
# Mockup-pipeline pending markers on any real line (usually **Workflow args:**
# for /new-ui tasks, or a dedicated **Mockup:** line for HYBRID tasks — the token
# is location-agnostic, ui-review.py flips it by whole-file replace). See /ui-mockup.
# PENDING-MOCKUP = no draft yet; PENDING-APPROVAL:<html> = draft not yet frozen to PNG.
PENDING_GROUNDTRUTH_RE = re.compile(r"groundTruth=(PENDING-MOCKUP|PENDING-APPROVAL:\S+)")
# ANY groundTruth token means the author made an explicit visual-source decision
# (.png approved / clone:<Prefab> / none / PENDING-*). Its total ABSENCE on a task
# that otherwise signals UI-screen work is the silent-skip hole this guards.
ANY_GROUNDTRUTH_RE = re.compile(r"groundTruth=\S+")
# A task referencing /new-ui builds or authors a UI prefab/screen — it must carry
# an explicit mockup decision (draft, clone, or none) before it can be promoted,
# so a new screen can never skip the draft+approval gate the way a /new-feature
# HYBRID once could.
NEWUI_SIGNAL_RE = re.compile(r"/new-ui\b")

STATE_DIRS = ("todo", "in-progress", "done")


def _git_out(*args):
    try:
        out = subprocess.run(["git", *args], capture_output=True, text=True,
                             check=True).stdout.strip()
        return out or None
    except Exception:
        return None


def repo_root() -> Path:
    out = _git_out("rev-parse", "--show-toplevel")
    if out:
        return Path(out)
    # git unavailable — walk up from this script (then cwd) looking for .git
    for base in (Path(__file__).resolve().parent, Path.cwd()):
        for p in (base, *base.parents):
            if (p / ".git").exists():
                return p
    return Path.cwd()


def git_common_dir() -> Path:
    """The shared .git directory. In a linked worktree `--git-dir` points at
    `.git/worktrees/<name>` (private) while `--git-common-dir` points at the
    ORIGINAL `.git` — that is precisely what makes one backlog visible from
    every worktree of the clone, so never substitute --git-dir here.

    Older git returns a path relative to the cwd, newer git an absolute one;
    resolve against the cwd so both shapes land on the same directory."""
    out = _git_out("rev-parse", "--git-common-dir")
    if out:
        return (Path.cwd() / out).resolve()
    return (repo_root() / ".git").resolve()


ROOT = repo_root()
# Bullets stay written as `backlog/<state>/<file>.md`; TASK_BASE is what they
# are resolved against, so a bullet's spelling never changed with the move.
TASK_BASE = git_common_dir()
BACKLOG_ROOT = TASK_BASE / "backlog"
BACKLOG = BACKLOG_ROOT / "BACKLOG.md"

BACKLOG_SKELETON = """# Backlog

Task index for the autonomous backlog system. Task bodies live as individual
files in `backlog/{todo,in-progress,done}/` next to this file; the agent reads
only this index plus the one task it picks, so tokens stay flat no matter how
many tasks accumulate.

This backlog is per-developer bookkeeping stored under the git common dir
(`.git/backlog/`). It is never committed and never merged, and every worktree
of this clone shares it. Templates live in `.claude/backlog-templates/`.

## Ordering Rules in TODO

- **FIFO (task order).** `promote` appends to the END of `## TODO`; `pick`
  always takes the HEAD. Position in this list = execution order.
- **Priority is a metadata tag only** — it does NOT reorder the queue.

## TODO

- (none)

## IN PROGRESS

- (none)

## DONE

- (none)

See `backlog/done/` — each completed task is a separate file with its summary.
"""


def init_backlog() -> list:
    """Create the backlog root. Idempotent — only reports what it actually made."""
    actions = []
    for d in ("planning", *STATE_DIRS):
        p = BACKLOG_ROOT / d
        if not p.is_dir():
            p.mkdir(parents=True, exist_ok=True)
            actions.append(f"created {d}/")
    if not BACKLOG.exists():
        BACKLOG.write_text(BACKLOG_SKELETON, encoding="utf-8")
        actions.append("created BACKLOG.md")
    return actions


# Commands that legitimately reach a machine whose backlog does not exist yet
# (a fresh clone / a brand-new dev): they bootstrap it. Everything else MUST
# fail loudly instead — a `pick` that silently auto-created an empty backlog
# would report `state: empty`, and the loop would write PAUSED and announce
# "backlog is empty" when the real fault is a misconfigured checkout.
AUTO_INIT_COMMANDS = ("init", "promote")


def script_path(name: str) -> str:
    """Path to a sibling script, spelled the way the caller can paste it back.

    The same toolchain lives under `.claude/scripts/` in one project layout and
    `.claude/scripts/` in another, so a hardcoded path is wrong in half the
    checkouts. Derive it from this file, relative to the cwd when that works
    (a worktree cwd with the script in the main checkout gives no relative
    path — fall back to absolute rather than printing a `../../..` chain)."""
    p = (Path(__file__).resolve().parent / name)
    try:
        return p.relative_to(Path.cwd().resolve()).as_posix()
    except ValueError:
        return p.as_posix()


def ensure_structure(command: str) -> list:
    if command in AUTO_INIT_COMMANDS:
        return init_backlog()
    missing = [str(p) for p in (BACKLOG, *(BACKLOG_ROOT / d for d in STATE_DIRS))
               if not p.exists()]
    if missing:
        # `fix` is the whole point of this branch: the reader is a model or a dev
        # who just hit exit 3, and both should be able to paste one line rather
        # than work out where the script lives in this layout.
        print(json.dumps({
            "ok": False,
            "error": "backlog not initialised",
            "backlog_root": str(BACKLOG_ROOT),
            "missing": missing,
            "fix": f"python3 {script_path('backlog-ops.py')} init",
            "hint": f"run `python3 {script_path('backlog-ops.py')} init` — or "
                    f"`bash {script_path('bootstrap.sh')}` to set up the link view "
                    "and the backlog together. The backlog lives in the git common "
                    "dir and is never committed, so a fresh clone starts without one",
        }, ensure_ascii=False, indent=2))
        raise SystemExit(3)
    return []


def fmt_bullet(priority, tier, title, path) -> str:
    """Build a BACKLOG.md bullet. The [Tier] bracket is emitted only when known
    (a tier-less source bullet stays tier-less rather than printing `[None]`)."""
    tier_part = f" [{tier}]" if tier else ""
    return f"- [{priority}]{tier_part} [{title}]({path})"


# --------------------------------------------------------------------------- index
class Index:
    """Parsed BACKLOG.md, preserving every line for lossless rewrites."""

    def __init__(self, text: str):
        self.lines = text.split("\n")
        self.sections = {}  # name -> (start_line_after_header, end_line_exclusive)
        current, start = None, None
        for i, line in enumerate(self.lines):
            if line.startswith("## "):
                if current is not None:
                    self.sections[current] = (start, i)
                current, start = line[3:].strip(), i + 1
        if current is not None:
            self.sections[current] = (start, len(self.lines))

    def bullets(self, section: str):
        rng = self.sections.get(section)
        if not rng:
            return []
        out = []
        for i in range(*rng):
            m = BULLET_RE.match(self.lines[i])
            if m:
                out.append((i, m))
        return out

    def replace_line(self, i: int, new: str):
        self.lines[i] = new

    def remove_line(self, i: int):
        del self.lines[i]
        self.sections = Index("\n".join(self.lines)).sections

    def insert_line(self, i: int, new: str):
        self.lines.insert(i, new)
        self.sections = Index("\n".join(self.lines)).sections

    def section_tail(self, section: str) -> int:
        """Line index right after the last non-blank line of a section."""
        rng = self.sections.get(section)
        if not rng:
            raise KeyError(f"section {section!r} not found")
        start, end = rng
        last = start - 1
        for i in range(start, end):
            if self.lines[i].strip():
                last = i
        return last + 1

    def text(self) -> str:
        out = "\n".join(self.lines)
        if not out.endswith("\n"):
            out += "\n"
        return out


def load_index() -> Index:
    return Index(BACKLOG.read_text(encoding="utf-8"))


def write_backlog(text: str):
    """Atomic BACKLOG.md replace (tmp file + os.replace) — a crash mid-write
    must never leave a truncated index. A leftover BACKLOG.md.tmp after a
    crash is harmless (visible in git status, never read by anything)."""
    tmp = BACKLOG.with_name(BACKLOG.name + ".tmp")
    tmp.write_text(text, encoding="utf-8")
    os.replace(tmp, BACKLOG)


def state_files():
    """basename -> list of state dirs that contain it."""
    seen = {}
    for d in STATE_DIRS:
        p = BACKLOG_ROOT / d
        if not p.is_dir():
            continue
        for f in sorted(p.glob("*.md")):
            seen.setdefault(f.name, []).append(d)
    return seen


def read_body_tier(path: Path):
    """Read the first real **Tier:** field, ignoring HTML template comments."""
    in_comment = False
    for line in path.read_text(encoding="utf-8").split("\n"):
        stripped = line.strip()
        if in_comment:
            if "-->" in stripped:
                in_comment = False
            continue
        if stripped.startswith("<!--"):
            if "-->" not in stripped:
                in_comment = True
            continue
        match = BODY_TIER_RE.match(stripped)
        if match:
            return match.group("tier").upper()
    return None


def max_nnn() -> int:
    best = 0
    for name in state_files():
        m = NNN_RE.match(name)
        if m:
            best = max(best, int(m.group(1)))
    return best


def move_file(src: Path, dst_rel: str, dry_run: bool) -> str:
    """Move a task file between states. Plain filesystem move, never `git mv`:
    the backlog lives inside the git common dir, so nothing here is tracked and
    `git mv` would hard-fail with 'not under version control'."""
    if dry_run:
        return f"DRY-RUN: mv {src.name} -> {dst_rel}"
    dst = TASK_BASE / dst_rel
    dst.parent.mkdir(parents=True, exist_ok=True)
    shutil.move(str(src), str(dst))
    return f"mv {src.name} -> {dst_rel}"


# --------------------------------------------------------------------------- concurrency & resume
# Two holes this section closes:
#
# 1. Two consumers of ONE queue. Every loop controller and every hand-run
#    /run-backlog on this clone reads the same index, and `pick` used to be
#    lock-free and always return the IN PROGRESS head — so a second consumer
#    silently "resumed" the task the first was still working on (seen in
#    practice: two agents, one Editor, one git index, each `git add -A`
#    sweeping up the other's half-done files).
# 2. Blind resume. A run that died mid-task (usage limit, crash, watchdog kill)
#    left partial code in the tree and no record of how far it got; and a death
#    between `done` and the commit let the NEXT task's `git add -A` sweep the
#    finished code into the wrong commit.
#
#   runs/loop.json   the loop LEASE: at most one loop controller per clone. The
#                    holder is the controller process; its iterations prove
#                    membership with the lease token (env BACKLOG_LOOP_TOKEN).
#   runs/<NNN>.json  the task CLAIM + journal: written by `start`, taken over by
#                    `resume`, closed by `shipped` (or `demote`). The owner is
#                    the agent CLI process (claude/codex/gemini) doing the work,
#                    found by walking up the process tree — so a claim dies
#                    exactly when that session dies (usage limit, crash, closed
#                    window, reboot) and never needs a heartbeat.
#   runs/.mutex      an OS file lock (flock / msvcrt.locking) around every
#                    read-modify-write of BACKLOG.md and runs/*.json. The kernel
#                    drops it when its holder exits or dies, so it can neither go
#                    stale nor be stolen from a live holder.
#
# A process is identified by pid + start time, so a recycled PID never looks
# alive. Liveness is never guessed optimistically: one that cannot be read
# counts as LIVE — refuse, and say how to break it (`lock break`), rather than
# run two agents on one checkout. The host name is recorded but never compared:
# the backlog lives in this clone's .git, and macOS rewrites the host name on
# network changes, which would turn every live holder into "unknown".

EXIT_BUSY = 4
EXIT_CONFLICT = 5
RUNS_DIR = BACKLOG_ROOT / "runs"
ARCHIVE_DIR = RUNS_DIR / "archive"
LEASE_FILE = RUNS_DIR / "loop.json"
MUTEX_FILE = RUNS_DIR / ".mutex"
LOOP_TOKEN_ENV = "BACKLOG_LOOP_TOKEN"
OWNER_PID_ENV = "BACKLOG_OWNER_PID"
IS_WINDOWS = os.name == "nt"
HOST = socket.gethostname()
AGENT_NAMES = ("claude", "codex", "gemini")
# npm launches run the agent through node: `node …/@anthropic-ai/claude-code/cli.js`,
# `…/@google/gemini-cli/…`, `…/@openai/codex/bin/codex.js` — the agent name (or its
# package name) appears as a path segment.
AGENT_ARG_RE = re.compile(
    r"(?:^|[\\/\s@])(?:claude(?:-code)?|codex|gemini(?:-cli)?)(?:[\\/.\s]|$)", re.IGNORECASE)
# A loop controller / per-task runner is the INTERPRETER PROCESS executing the
# script: argv0 is the shell / powershell / cmd, then (flags and) the script path.
# Anchoring on argv0 keeps a command line that merely mentions the script — a
# `grep`, an editor, a `zsh -c '… bash run-backlog-loop.sh …'` Bash-tool wrapper
# that already finished launching it — from counting as a loop.
_SHELL0 = r"^(?:\"?[^\"\s]*[\\/])?(?:ba|z|da)?sh(?:\.exe)?\"?\s+(?:-\w+\s+)*(?:\"[^\"]*|'[^']*|[^\s;&|\"']*)"
_PS0 = r"^\"?(?:[^\"]*[\\/])?(?:powershell|pwsh)(?:\.exe)?\"?\s+(?:.*?\s)?-File\s+\"?[^\"]*?"
_CMD0 = r"^\"?(?:[^\"]*[\\/])?cmd(?:\.exe)?\"?\s+/[ck]\s+.*?"
CONTROLLER_RE = re.compile(
    r"(?:" + _SHELL0 + r"run-backlog-loop\.sh\b)"
    r"|(?:" + _PS0 + r"run-backlog-loop[\w-]*\.ps1\b)"
    r"|(?:" + _CMD0 + r"run-backlog-loop\.bat\b)",
    re.IGNORECASE)
RUNNER_RE = re.compile(
    r"(?:" + _SHELL0 + r"iter-\d+-\d{8}-\d{6}\.run\.sh\b)"
    r"|(?:" + _PS0 + r"iter-[a-z]+-\d{8}-\d{6}-\d{3}\.log\.run\.ps1\b)",
    re.IGNORECASE)
CHECKPOINT_STEPS = ("implemented", "compile", "preflight", "review", "qa", "smoke")
# STEP 9e, on the ship-pending claim: the staged tree the commit will carry (ship fence).
COMMIT_STEP = "commit"
PUSH_STATES = ("pushed", "no-remote", "failed", "skipped")
START_TOLERANCE_S = 2        # ps/CIM report start time to the second
MUTEX_WAIT_S = 20.0          # override: env BACKLOG_MUTEX_WAIT_S
BUSY_TOKEN = {
    "loop-active": "LOOP_BUSY",
    "legacy-loop": "LOOP_BUSY",
    "task-claimed": "TASK_BUSY",
    "unclaimed-task-foreign-loop": "TASK_BUSY",
}


def now_iso() -> str:
    return datetime.now(timezone.utc).strftime("%Y-%m-%dT%H:%M:%SZ")


def read_json(path: Path):
    try:
        return json.loads(path.read_text(encoding="utf-8"))
    except (OSError, ValueError):
        return None


def write_json(path: Path, data):
    """Atomic replace, like write_backlog. Windows refuses os.replace onto a file
    another process has open for the instant it reads it — retry briefly."""
    path.parent.mkdir(parents=True, exist_ok=True)
    tmp = path.with_name(f".{path.name}.{os.getpid()}.tmp")
    tmp.write_text(json.dumps(data, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    for attempt in range(20):
        try:
            os.replace(tmp, path)
            return
        except PermissionError:
            if attempt == 19:
                raise
            time.sleep(0.05)


class ProbeError(Exception):
    """The process table could not be read: liveness is UNKNOWN, never 'dead'."""


def _base_name(cmd: str) -> str:
    name = re.split(r"[\\/]", cmd.strip().strip('"'))[-1].lower()
    return name[:-4] if name.endswith(".exe") else name


def _posix_process_table() -> dict:
    # TZ=UTC: lstart is rendered in local time, so a timezone change while a
    # holder runs would otherwise make it look like a different (dead) process.
    env = {**os.environ, "LC_ALL": "C", "TZ": "UTC"}
    try:
        meta = subprocess.run(["ps", "-Ao", "pid=,ppid=,lstart=,comm="], capture_output=True,
                              text=True, env=env, timeout=30, errors="replace")
        args = subprocess.run(["ps", "-Ao", "pid=,args="], capture_output=True,
                              text=True, env=env, timeout=30, errors="replace")
    except (OSError, subprocess.SubprocessError) as e:
        raise ProbeError(f"ps unavailable: {e}")
    if meta.returncode != 0 or not meta.stdout.strip():
        raise ProbeError(f"ps failed: {meta.stderr.strip()[:200]}")
    table = {}
    for line in meta.stdout.splitlines():
        parts = line.split(None, 7)
        if len(parts) < 7:
            continue
        try:
            pid, ppid = int(parts[0]), int(parts[1])
            start = int(datetime.strptime(" ".join(parts[2:7]), "%a %b %d %H:%M:%S %Y")
                        .replace(tzinfo=timezone.utc).timestamp())
        except ValueError:
            continue
        table[pid] = {"pid": pid, "ppid": ppid, "start": start,
                      "name": _base_name(parts[7] if len(parts) > 7 else ""), "args": ""}
    for line in args.stdout.splitlines():
        parts = line.strip().split(None, 1)
        if parts and parts[0].isdigit() and int(parts[0]) in table:
            table[int(parts[0])]["args"] = parts[1] if len(parts) > 1 else ""
    return table


_WIN_PS = (
    "$ErrorActionPreference='Stop';[Console]::OutputEncoding=[Text.Encoding]::UTF8;"
    "@(Get-CimInstance Win32_Process | ForEach-Object {"
    " $d=$_.CreationDate; $s=0; if ($d) { $s=([DateTimeOffset]$d).ToUnixTimeSeconds() };"
    " [pscustomobject]@{p=[int64]$_.ProcessId; pp=[int64]$_.ParentProcessId;"
    " n=[string]$_.Name; a=[string]$_.CommandLine; s=[int64]$s} }) | ConvertTo-Json -Compress"
)


def _win_process_table() -> dict:
    last = "no PowerShell found"
    for exe in ("powershell.exe", "pwsh.exe"):
        try:
            r = subprocess.run([exe, "-NoProfile", "-NonInteractive", "-Command", _WIN_PS],
                               capture_output=True, text=True, encoding="utf-8",
                               errors="replace", timeout=90)
        except (OSError, subprocess.SubprocessError) as e:
            last = str(e)
            continue
        if r.returncode != 0 or not r.stdout.strip():
            last = r.stderr.strip()[:200] or f"{exe} exited {r.returncode}"
            continue
        try:
            rows = json.loads(r.stdout)
        except ValueError as e:
            last = f"unparsable CIM output: {e}"
            continue
        if isinstance(rows, dict):
            rows = [rows]
        return {int(row["p"]): {"pid": int(row["p"]), "ppid": int(row["pp"]),
                                "start": int(row["s"]), "name": _base_name(row.get("n") or ""),
                                "args": row.get("a") or ""} for row in rows}
    try:
        return _win_toolhelp_table()
    except Exception as e:                               # noqa: BLE001 — any ctypes failure
        raise ProbeError(f"Windows process table unavailable: {last}; toolhelp: {e}")


def _win_toolhelp_table() -> dict:
    """Fallback without PowerShell: pid/ppid/exe from a toolhelp snapshot and the
    creation time from GetProcessTimes. No command lines, so loop-process scans
    see nothing — claims (the part that matters) still work."""
    import ctypes
    from ctypes import wintypes
    k32 = ctypes.WinDLL("kernel32", use_last_error=True)

    class PROCESSENTRY32W(ctypes.Structure):
        _fields_ = [("dwSize", wintypes.DWORD), ("cntUsage", wintypes.DWORD),
                    ("th32ProcessID", wintypes.DWORD), ("th32DefaultHeapID", ctypes.c_size_t),
                    ("th32ModuleID", wintypes.DWORD), ("cntThreads", wintypes.DWORD),
                    ("th32ParentProcessID", wintypes.DWORD), ("pcPriClassBase", ctypes.c_long),
                    ("dwFlags", wintypes.DWORD), ("szExeFile", ctypes.c_wchar * 260)]

    k32.CreateToolhelp32Snapshot.restype = wintypes.HANDLE
    k32.CreateToolhelp32Snapshot.argtypes = [wintypes.DWORD, wintypes.DWORD]
    k32.Process32FirstW.argtypes = [wintypes.HANDLE, ctypes.POINTER(PROCESSENTRY32W)]
    k32.Process32NextW.argtypes = [wintypes.HANDLE, ctypes.POINTER(PROCESSENTRY32W)]
    k32.OpenProcess.restype = wintypes.HANDLE
    k32.OpenProcess.argtypes = [wintypes.DWORD, wintypes.BOOL, wintypes.DWORD]
    k32.GetProcessTimes.argtypes = [wintypes.HANDLE] + [ctypes.POINTER(wintypes.FILETIME)] * 4
    k32.CloseHandle.argtypes = [wintypes.HANDLE]
    snap = k32.CreateToolhelp32Snapshot(0x2, 0)          # TH32CS_SNAPPROCESS
    if not snap or snap == wintypes.HANDLE(-1).value:
        raise OSError(ctypes.get_last_error(), "CreateToolhelp32Snapshot failed")
    rows = []
    try:
        entry = PROCESSENTRY32W()
        entry.dwSize = ctypes.sizeof(entry)
        ok = k32.Process32FirstW(snap, ctypes.byref(entry))
        while ok:
            rows.append((int(entry.th32ProcessID), int(entry.th32ParentProcessID), entry.szExeFile))
            ok = k32.Process32NextW(snap, ctypes.byref(entry))
    finally:
        k32.CloseHandle(snap)
    table = {}
    for pid, ppid, exe in rows:
        start = 0
        h = k32.OpenProcess(0x1000, False, pid)            # PROCESS_QUERY_LIMITED_INFORMATION
        if h:
            try:
                times = [wintypes.FILETIME() for _ in range(4)]
                if k32.GetProcessTimes(h, *[ctypes.byref(t) for t in times]):
                    ft = (times[0].dwHighDateTime << 32) | times[0].dwLowDateTime
                    start = int(ft / 10_000_000 - 11_644_473_600)   # FILETIME -> unix seconds
            finally:
                k32.CloseHandle(h)
        table[pid] = {"pid": pid, "ppid": ppid, "start": start, "name": _base_name(exe), "args": ""}
    return table


_PROC_TABLE = None


def process_table() -> dict:
    """pid -> {pid, ppid, start (epoch s), name, args}; read once per invocation."""
    global _PROC_TABLE
    if _PROC_TABLE is None:
        _PROC_TABLE = _win_process_table() if IS_WINDOWS else _posix_process_table()
    return _PROC_TABLE


def ident_of(pid):
    p = process_table().get(int(pid))
    if not p:
        return None
    return {"pid": p["pid"], "start": p["start"], "host": HOST, "name": p["name"]}


def liveness(ident) -> str:
    """'live' | 'dead' | 'unknown' — unknown must be treated like live."""
    if not isinstance(ident, dict) or "pid" not in ident:
        return "dead"
    try:
        p = process_table().get(int(ident["pid"]))
    except ProbeError:
        return "unknown"
    if not p:
        return "dead"
    if ident.get("start") is None:
        return "unknown"
    return "live" if abs(int(p["start"]) - int(ident["start"])) <= START_TOLERANCE_S else "dead"


def same_process(a, b) -> bool:
    return (isinstance(a, dict) and isinstance(b, dict) and "pid" in a and "pid" in b
            and int(a["pid"]) == int(b["pid"])
            and a.get("start") is not None and b.get("start") is not None
            and abs(int(a["start"]) - int(b["start"])) <= START_TOLERANCE_S)


def ancestor_chain(pid) -> list:
    table = process_table()
    chain, seen = [], {pid}
    cur = table.get(pid)
    while cur and len(chain) < 64:
        parent = table.get(cur["ppid"])
        if not parent or parent["pid"] in seen:
            break
        chain.append(parent)
        seen.add(parent["pid"])
        cur = parent
    return chain


def is_agent(p) -> bool:
    if p["name"] in AGENT_NAMES:
        return True
    if p["name"] in ("node", "bun", "deno"):
        # Windows toolhelp-less fallbacks can lose argv; an argv-less node that is
        # an ancestor of a backlog-ops call is the agent CLI in every real setup.
        return (not p["args"] and IS_WINDOWS) or bool(AGENT_ARG_RE.search(p["args"]))
    return False


def is_controller(p) -> bool:
    return bool(CONTROLLER_RE.search(p["args"]))


def is_runner(p) -> bool:
    return bool(RUNNER_RE.search(p["args"]))


def proc_summary(p) -> dict:
    return {"pid": p["pid"], "name": p["name"], "started": datetime.fromtimestamp(
        p["start"], timezone.utc).strftime("%Y-%m-%dT%H:%M:%SZ"), "cmd": p["args"][:240]}


class Caller:
    """Who is running this backlog-ops call: the agent session that owns claims
    (nearest claude/codex/gemini ancestor), whether it runs inside a loop, and
    the loop token it carries. `probe=True` evaluates the queue on someone
    else's behalf (the controller's pre-spawn check): nothing is 'mine'."""

    def __init__(self, probe: bool = False):
        self.token = os.environ.get(LOOP_TOKEN_ENV, "").strip() or None
        self.agent, self.agent_source = None, "not-found"
        self.chain, self.probe_error = [], None
        try:
            table = process_table()
            self.chain = ancestor_chain(os.getpid())
            override = os.environ.get(OWNER_PID_ENV, "").strip()
            if probe:
                self.agent_source = "probe"
            elif override.isdigit() and int(override) in table:
                self.agent, self.agent_source = ident_of(int(override)), "env"
            else:
                for p in self.chain:
                    if is_agent(p):
                        self.agent, self.agent_source = ident_of(p["pid"]), "ancestor"
                        break
        except ProbeError as e:
            self.probe_error = str(e)
        self.chain_pids = {p["pid"] for p in self.chain} | {os.getpid()}
        self.in_loop = any(is_controller(p) or is_runner(p) for p in self.chain)

    def describe(self) -> dict:
        return {"agent": self.agent, "agent_source": self.agent_source,
                "in_loop": self.in_loop, "loop_token": bool(self.token),
                "probe_error": self.probe_error}


def _descendants(table: dict, pid: int) -> list:
    children = {}
    for p in table.values():
        children.setdefault(p["ppid"], []).append(p)
    out, stack, seen = [], list(children.get(pid, [])), {pid}
    while stack:
        p = stack.pop()
        if p["pid"] in seen:
            continue
        seen.add(p["pid"])
        out.append(p)
        stack.extend(children.get(p["pid"], []))
    return out


def _is_working(table: dict, p, workers_only: bool) -> bool:
    """A loop process only counts while it is doing something. A task runner
    counts while an agent runs under it — a FAILED task's window keeps its bash
    alive at "Press any key" with the agent long gone, and must not block the
    relaunch that resumes it. A controller counts while an agent runs under it
    (inline mode, or the Windows task window it spawned); outside workers_only
    also while it has any child at all (the macOS controller polls its task
    window with `sleep`), but not when it sits child-less at the final
    "Press Enter" prompt."""
    below = _descendants(table, p["pid"])
    if any(is_agent(q) for q in below):
        return True
    return is_controller(p) and not workers_only and bool(below)


def self_and_ancestors(pid) -> set:
    try:
        return {int(pid)} | {p["pid"] for p in ancestor_chain(int(pid))}
    except (ProbeError, ValueError, TypeError):
        return set()


_CLONE_PATHS = None


def _norm_path(path: str) -> str:
    out = os.path.normpath(str(path)).replace("\\", "/").rstrip("/")
    return out.lower() if IS_WINDOWS else out


def clone_paths() -> list:
    """Every checkout of THIS clone (main + linked worktrees): the only places a
    loop consuming this backlog can run from."""
    global _CLONE_PATHS
    if _CLONE_PATHS is None:
        paths = {_norm_path(TASK_BASE.parent), _norm_path(ROOT)}
        listing = _git_out("worktree", "list", "--porcelain") or ""
        for line in listing.splitlines():
            if line.startswith("worktree "):
                paths.add(_norm_path(Path(line[len("worktree "):]).resolve()))
        _CLONE_PATHS = sorted(paths)
    return _CLONE_PATHS


def _cwd_of(pid: int):
    if IS_WINDOWS:
        return None                      # not readable without PEB access; argv carries the path
    link = Path(f"/proc/{pid}/cwd")
    if link.exists():
        try:
            return os.readlink(link)
        except OSError:
            return None
    try:
        r = subprocess.run(["lsof", "-a", "-d", "cwd", "-p", str(pid), "-Fn"],
                           capture_output=True, text=True, timeout=20, errors="replace")
    except (OSError, subprocess.SubprocessError):
        return None
    for line in r.stdout.splitlines():
        if line.startswith("n"):
            return line[1:]
    return None


_WIN_SCRIPT_DIR_RE = re.compile(
    r"([a-z]:/[^\"]*?)/(?:run-backlog-loop[\w-]*\.(?:ps1|bat)|iter-[\w-]+\.log\.run\.ps1)\b")


def _under(path: str, roots) -> bool:
    return any(path == r or path.startswith(r + "/") for r in roots)


def belongs_to_clone(p) -> bool:
    """A loop process serves this backlog when its command line names one of this
    clone's checkouts (as a whole path segment — `/x/Game` is not `/x/Game2`), or
    its cwd lies inside one. Loops of OTHER clones and projects on the same
    machine have their own backlog and must not count. On Windows the cwd of
    another process is unreadable: a loop launched by a relative path is then
    assumed to be ours (refusing is recoverable, a double run is not)."""
    roots = clone_paths()
    args = _norm_path(p["args"]) if IS_WINDOWS else p["args"]
    for r in roots:
        if re.search(re.escape(r) + r"(?:[/\\\s\"']|$)", args):
            return True
    cwd = _cwd_of(p["pid"])
    if cwd:
        return _under(_norm_path(cwd), roots)
    if IS_WINDOWS:
        m = _WIN_SCRIPT_DIR_RE.search(args)
        return True if not m else _under(m.group(1), roots)
    return False


def foreign_loop_processes(caller: Caller, workers_only: bool = False, exclude=()) -> list:
    """Live loop controllers / task runners of THIS clone that are not this
    caller's own ancestors — the only trace a loop started by a pre-lease copy
    of the scripts leaves. `workers_only` keeps just the ones executing a task
    right now (a runner window, or a controller with an inline agent child): a
    window-mode controller is never an ancestor of its own task window, so it
    must not count as 'someone else working on this task'. Idle leftovers (a
    failed task's window, a finished controller's prompt) never count — see
    _is_working. Unreadable table -> [] (the lease and the claims still hold)."""
    try:
        table = process_table()
    except ProbeError:
        return []
    skip = set(caller.chain_pids) | set(exclude)
    out = []
    for p in table.values():
        if p["pid"] in skip:
            continue
        if not (is_runner(p) or is_controller(p)):
            continue
        if _is_working(table, p, workers_only) and belongs_to_clone(p):
            out.append(proc_summary(p))
    return out


@contextmanager
def backlog_mutex():
    """Exclusive OS file lock around every read-modify-write (flock on POSIX,
    msvcrt.locking on Windows). The kernel releases it the moment its holder
    exits or dies, so there is no stale-holder detection to get wrong. The
    lock file is permanent and empty; never delete it."""
    RUNS_DIR.mkdir(parents=True, exist_ok=True)
    try:
        wait = float(os.environ.get("BACKLOG_MUTEX_WAIT_S", MUTEX_WAIT_S))
    except ValueError:
        wait = MUTEX_WAIT_S
    fd = os.open(str(MUTEX_FILE), os.O_RDWR | os.O_CREAT, 0o644)
    deadline = time.monotonic() + wait
    locked = False
    try:
        while True:
            try:
                if IS_WINDOWS:
                    import msvcrt
                    os.lseek(fd, 0, os.SEEK_SET)
                    msvcrt.locking(fd, msvcrt.LK_NBLCK, 1)
                else:
                    import fcntl
                    fcntl.flock(fd, fcntl.LOCK_EX | fcntl.LOCK_NB)
                locked = True
                break
            except OSError:
                if time.monotonic() > deadline:
                    raise SystemExit(
                        f"backlog mutex busy for {wait:.0f}s ({MUTEX_FILE}) — another "
                        "backlog-ops call is still running; retry when it finishes.")
                time.sleep(0.05)
        yield
    finally:
        if locked:
            try:
                if IS_WINDOWS:
                    import msvcrt
                    os.lseek(fd, 0, os.SEEK_SET)
                    msvcrt.locking(fd, msvcrt.LK_UNLCK, 1)
                else:
                    import fcntl
                    fcntl.flock(fd, fcntl.LOCK_UN)
            except OSError:
                pass
        os.close(fd)


# --------------------------------------------------------------------------- git state (for resume)
def _git_cp(*args, stdin=None):
    try:
        return subprocess.run(["git", *args], cwd=str(ROOT), capture_output=True,
                              input=stdin, timeout=300)
    except (OSError, subprocess.SubprocessError):
        return None


def dirty_snapshot():
    """{repo-relative path: content hash | 'deleted'} for every staged,
    unstaged or untracked change — None when git cannot answer. Hashing is
    raw bytes (--no-filters): an LFS clean filter per file would be slow and
    the hash only has to be self-consistent."""
    r = _git_cp("status", "--porcelain=v1", "-z", "--untracked-files=all", "--ignore-submodules=dirty")
    if r is None or r.returncode != 0:
        return None
    entries = r.stdout.decode("utf-8", "surrogateescape").split("\0")
    codes, i = {}, 0
    while i < len(entries):
        entry = entries[i]
        i += 1
        if len(entry) < 4:
            continue
        xy, path = entry[:2], entry[3:]
        if xy[0] in "RC" and i < len(entries):
            if xy[0] == "R":
                codes[entries[i]] = "D "
            i += 1
        codes[path] = xy
    files = [p for p in codes if (ROOT / p).is_file()]
    hashes = {}
    if files:
        h = _git_cp("hash-object", "--no-filters", "--stdin-paths",
                    stdin="\n".join(files).encode("utf-8", "surrogateescape"))
        if h is not None and h.returncode == 0:
            hashes = dict(zip(files, h.stdout.decode().split()))
    snap = {}
    for p, xy in codes.items():
        snap[p] = {"xy": xy, "hash": hashes.get(p) or ("deleted" if not (ROOT / p).exists() else "unhashed")}
    return snap


def repo_state() -> dict:
    """Leftovers a killed git command leaves that would make the next one fail."""
    state = {}
    for name, key in (("index.lock", "index_lock"), ("MERGE_HEAD", "merge_in_progress"),
                      ("rebase-merge", "rebase_in_progress"), ("rebase-apply", "rebase_in_progress"),
                      ("CHERRY_PICK_HEAD", "cherry_pick_in_progress")):
        out = _git_out("rev-parse", "--git-path", name)
        if not out:
            continue
        p = Path(out) if Path(out).is_absolute() else Path.cwd() / out
        if p.exists():
            entry = {"path": str(p)}
            if key == "index_lock":
                try:
                    entry["age_s"] = int(time.time() - p.stat().st_mtime)
                    entry["git_running"] = any(q["name"] == "git" for q in process_table().values())
                except (OSError, ProbeError):
                    entry["git_running"] = None
            state[key] = entry
    return state


# --------------------------------------------------------------------------- claims
def run_path(nnn: str) -> Path:
    return RUNS_DIR / f"{nnn}.json"


def read_run(nnn: str):
    rec = read_json(run_path(nnn))
    return None if not rec or rec.get("closed") else rec


def run_records() -> list:
    if not RUNS_DIR.is_dir():
        return []
    out = []
    for p in sorted(RUNS_DIR.glob("*.json")):
        if p.name == LEASE_FILE.name:
            continue
        rec = read_json(p)
        if isinstance(rec, dict) and rec.get("nnn") and not rec.get("closed"):
            out.append(rec)
    return out


def archive_run(nnn: str, rec: dict, end: str):
    """Close a run. The live record is rewritten FIRST (with `closed`, and
    `shipped` when set), so even an unlink that fails — Windows refuses while a
    concurrent reader has the file open — leaves a record no one re-ships."""
    rec["closed"] = {"end": end, "at": now_iso()}
    write_json(run_path(nnn), rec)
    stamp = datetime.now(timezone.utc).strftime("%Y%m%dT%H%M%S")
    write_json(ARCHIVE_DIR / f"{nnn}-{stamp}-{end}.json", rec)
    for attempt in range(20):
        try:
            run_path(nnn).unlink()
            return
        except FileNotFoundError:
            return
        except OSError:
            time.sleep(0.05)


def attempt_entry(n: int, caller: Caller) -> dict:
    return {"n": n, "process": caller.agent, "agent_source": caller.agent_source,
            "loop": bool(caller.token) or caller.in_loop, "started_at": now_iso()}


def owner_entry(caller: Caller) -> dict:
    return {"process": caller.agent, "agent_source": caller.agent_source,
            "loop_token": caller.token, "claimed_at": now_iso()}


def new_run_record(nnn, task_name, meta, caller: Caller, legacy=False) -> dict:
    return {
        "version": 1, "nnn": nnn, "task": task_name,
        "title": meta.get("title"), "tier": meta.get("tier"), "priority": meta.get("priority"),
        "phase": "in-progress", "created_at": now_iso(), "legacy": legacy,
        "base": {"head": _git_out("rev-parse", "HEAD"),
                 "branch": _git_out("rev-parse", "--abbrev-ref", "HEAD"),
                 "work_dir": str(ROOT), "mode": (os.environ.get("AGENT_MODE") or "current").lower()},
        # None = unknown (the task started before claims existed): every dirty
        # path is then reported as possibly-this-task's, never silently dropped.
        "baseline_dirty": None if legacy else dirty_snapshot(),
        "owner": owner_entry(caller),
        "attempts": [attempt_entry(1, caller)],
        "checkpoints": [], "shipped": None,
    }


def claim_status(rec, caller: Caller) -> str:
    """none | mine | live | stale. A claim whose owner cannot be probed is live."""
    if not rec:
        return "none"
    proc = (rec.get("owner") or {}).get("process")
    if not proc:
        return "stale"
    if caller.agent and same_process(proc, caller.agent):
        return "mine"
    return "stale" if liveness(proc) == "dead" else "live"


def take_over(rec: dict, caller: Caller, end: str = "stale"):
    if rec.get("attempts"):
        last = rec["attempts"][-1]
        last.setdefault("ended_at", now_iso())
        last.setdefault("end", end)
    rec.setdefault("attempts", []).append(attempt_entry(len(rec.get("attempts", [])) + 1, caller))
    rec["owner"] = owner_entry(caller)


def claim_summary(rec, status: str) -> dict:
    if not rec:
        return {"status": status, "attempts": 0, "last_checkpoint": None}
    cps = rec.get("checkpoints") or []
    return {"status": status, "attempts": len(rec.get("attempts") or []),
            "last_checkpoint": cps[-1] if cps else None,
            "owner": (rec.get("owner") or {}).get("process")}


def busy(reason: str, **extra) -> dict:
    hint = {
        "loop-active": "another loop controller holds the lease — let it finish or stop "
                       "it; `lock status` shows it, `lock break --yes` only if it is dead",
        "legacy-loop": "a loop started by an older copy of the scripts (no lease) is "
                       "running — stop it before starting another consumer",
        "task-claimed": "a live agent session owns this task — let it finish, or close "
                        "that session; `lock break --yes --task NNN` only if it is dead",
        "unclaimed-task-foreign-loop": "the task predates claims and another loop is live "
                                       "— it is probably working on it",
    }[reason]
    return {"ok": False, "state": "busy", "reason": reason, "sentinel": BUSY_TOKEN[reason],
            "hint": hint, **extra}


def loop_gate(caller: Caller):
    """None when the caller may consume the queue, else a busy payload."""
    lease = read_json(LEASE_FILE)
    if lease:
        state = liveness(lease.get("holder"))
        if state != "dead":
            if caller.token and caller.token == lease.get("token"):
                return None
            return busy("loop-active", lease=lease, holder_liveness=state)
    if not caller.in_loop:
        foreign = foreign_loop_processes(caller)
        if foreign:
            return busy("legacy-loop", processes=foreign)
    return None


def enforce_owner(nnn: str, rec, caller: Caller, action: str):
    """Only the owning session may journal/close a claim. A dead owner is taken
    over implicitly (the dead cannot object); a live foreign one refuses."""
    status = claim_status(rec, caller)
    if status == "live":
        return busy("task-claimed", nnn=nnn, action=action,
                    claim=claim_summary(rec, status), caller=caller.describe())
    if status == "stale":
        take_over(rec, caller, end="stale")
        rec["attempts"][-1]["implicit"] = action
    return None


TREE_SHA_RE = re.compile(r"^(?:[0-9a-f]{40}|[0-9a-f]{64})$")


def task_commits_for(rec: dict, head=None) -> list:
    """`<short-sha> <subject>` of every commit that carries this task, oldest first.

    The commit message stays trailer-free (push-in-session §3.4), so the commit is
    found by content: STEP 9e records the staged tree (`git write-tree`) with
    `checkpoint --step commit --tree` right before `git commit`, and a commit made
    from that index has exactly that tree. Only commits that landed after the
    task started are searched — an identical older tree cannot be this task's. A
    task that started on an unborn branch (fresh `git init`, no base head) has no
    older commits at all, so the recent history is searched. An empty commit made
    later (same tree as its parent, e.g. a build-flag commit) is never the task's.
    A `Backlog-Task: <NNN>` trailer is still honoured, for a run committed by a
    skill version that wrote one."""
    nnn = rec.get("nnn")
    base_head = (rec.get("base") or {}).get("head")
    head = head if head is not None else _git_out("rev-parse", "HEAD")
    if not head:
        return []
    moved = bool(base_head and base_head != head)
    rng = [f"{base_head}..HEAD"] if moved else ["-n", "200", "HEAD"]
    found = []
    trees = {t.get("tree") for t in (rec.get("commit_trees") or []) if t.get("tree")}
    if trees and (moved or not base_head):
        log = _git_out("log", "--reverse", "--format=%T|%P|%h %s", *rng)
        for line in (log.splitlines() if log else []):
            tree, parents, rest = (line.split("|", 2) + ["", ""])[:3]
            if tree not in trees:
                continue
            first_parent = parents.split()[0] if parents.split() else ""
            if first_parent and _git_out("rev-parse", f"{first_parent}^{{tree}}") == tree:
                continue                        # empty commit: same tree as its parent
            found.append(rest)
    tlog = _git_out("log", "--format=%h %s", f"--grep=^Backlog-Task: {nnn}$", *rng)
    for line in (tlog.splitlines() if tlog else []):
        if line not in found:
            found.append(line)
    return found


def resume_context(rec: dict) -> dict:
    """What the dead attempt left behind, split into work that is (probably) this
    task's and dirt that already existed when the task started."""
    base = rec.get("base") or {}
    head = _git_out("rev-parse", "HEAD")
    now = dirty_snapshot() or {}
    baseline = rec.get("baseline_dirty")
    partial, untouched, reverted = [], [], []
    for path, cur in sorted(now.items()):
        was = (baseline or {}).get(path)
        if baseline is not None and was and was.get("hash") == cur.get("hash"):
            untouched.append(path)
        else:
            partial.append({"path": path, "xy": cur.get("xy"),
                            "note": "unverified (no baseline)" if baseline is None else
                                    ("changed since start" if was else "new since start")})
    if baseline:
        reverted = sorted(p for p in baseline if p not in now)
    commits = []
    if base.get("head") and head and base["head"] != head:
        log = _git_out("log", "--format=%h %s", "-n", "30", f"{base['head']}..HEAD")
        commits = log.splitlines() if log else []
    # Commits that carry THIS task: the only reliable answer to "did the dead run
    # already commit?" (STEP 1c). See task_commits_for().
    task_commits = task_commits_for(rec, head)
    unpushed = None
    if _git_out("rev-parse", "--abbrev-ref", "--symbolic-full-name", "@{u}"):
        count = _git_out("rev-list", "--count", "@{u}..HEAD")
        unpushed = int(count) if count and count.isdigit() else None
    cps = rec.get("checkpoints") or []
    steps = {c.get("step") for c in cps}
    if not partial and not cps:
        guidance = ("Nothing survived from the previous attempt(s): implement from STEP 4 "
                    "as a fresh start (the claim and journal are already in place).")
    elif "implemented" not in steps:
        guidance = ("Implementation was in progress when the previous attempt died. Read "
                    "the partial diff first and CONTINUE it — keep what is correct, finish "
                    "what is missing; never revert it or restart from scratch.")
    else:
        guidance = (f"Implementation had finished (last checkpoint: {cps[-1].get('step')}). "
                    "Re-check the diff against the completion criteria, then re-run EVERY "
                    "gate from STEP 5b — a dead attempt's verdicts are not trusted.")
    if "qa" in steps and "smoke" not in steps:
        guidance += (" The previous attempt may have died inside the runtime smoke gate: "
                     "make sure the Editor is out of play mode and that player data was "
                     "restored before running it again.")
    if rec.get("phase") == "done":
        guidance = ("DONE but never recorded as shipped (STEP 1c). " + (
            f"Already committed as {task_commits[0].split()[0]} — push if `unpushed` > 0, then "
            "record `shipped` with that sha; do NOT commit again." if task_commits else
            "No commit carries this task yet: run STEP 9 now (record the staged tree with "
            f"`checkpoint {rec.get('nnn')} --step commit --tree <sha>` right before the commit), "
            "then `shipped`."))
    return {"partial_work": partial, "preexisting_dirty_unchanged": untouched,
            "baseline_dirty_now_clean": reverted, "head": head, "base_head": base.get("head"),
            "commits_since_start": commits, "task_commits": task_commits,
            "unpushed_commits": unpushed, "checkpoints": cps,
            "last_checkpoint": cps[-1] if cps else None, "guidance": guidance}


# --------------------------------------------------------------------------- parked work
# A task the loop gives up on (self-heal budget spent) is PARKED: moved to the tail
# of TODO so the loop carries on with the next one. Its half-done code cannot stay
# in the tree — the next task's `git add -A` would sweep it into the wrong commit,
# and code that does not compile would block every task after it. So `park`
# snapshots the partial work into a commit on top of HEAD, kept at
# refs/backlog/parked/<NNN> (never a branch, never pushed), and returns those
# paths to HEAD. The next `start` of the task reports the ref so the work can be
# restored; `shipped` deletes it.
PARK_REF_PREFIX = "refs/backlog/parked/"


def park_ref(nnn: str) -> str:
    return f"{PARK_REF_PREFIX}{nnn}"


def _git_ok(*args, stdin=None, env=None):
    """Run git in ROOT; return stdout (str) or raise SystemExit with git's error."""
    try:
        r = subprocess.run(["git", *args], cwd=str(ROOT), capture_output=True, input=stdin,
                           timeout=300, env=env)
    except (OSError, subprocess.SubprocessError) as e:
        raise SystemExit(f"git {args[0]} failed: {e}")
    if r.returncode != 0:
        raise SystemExit(f"git {' '.join(args[:2])} failed: "
                         f"{r.stderr.decode('utf-8', 'replace').strip()[:400]}")
    return r.stdout.decode("utf-8", "surrogateescape").strip()


def _nul(paths) -> bytes:
    return "\0".join(paths).encode("utf-8", "surrogateescape")


def _paths_in_head(paths) -> set:
    if not paths:
        return set()
    out = _git_ok("cat-file", "--batch-check=%(objecttype)",
                  stdin="".join(f"HEAD:{p}\n" for p in paths).encode("utf-8", "surrogateescape"))
    lines = out.split("\n") if out else []
    return {p for p, line in zip(paths, lines) if not line.endswith("missing")}


def parked_work(nnn: str):
    """What an earlier `park` saved for this task, or None."""
    sha = _git_out("rev-parse", "--verify", "--quiet", park_ref(nnn))
    if not sha:
        return None
    files = (_git_out("diff", "--name-status", "--no-renames", f"{sha}^1", sha) or "").splitlines()
    return {"ref": park_ref(nnn), "commit": sha[:12], "files": files,
            "hint": f"partial work of an earlier parked attempt. Inspect it with "
                    f"`git diff {sha[:12]}^1 {sha[:12]}`; restore what still fits with "
                    f"`git checkout {park_ref(nnn)} -- <path>` (it was cut from an older HEAD — "
                    f"re-check each file against the current code before keeping it)"}


def park_partial_work(nnn: str, paths, reason: str, dry_run: bool) -> dict:
    """Snapshot `paths` (this task's partial work) into refs/backlog/parked/<NNN>,
    then return them to HEAD: tracked files are restored, new files deleted."""
    paths = sorted(set(paths))
    if not paths:
        return {"saved": False, "files": [], "note": "no partial work to save"}
    head = _git_ok("rev-parse", "HEAD")
    in_head = _paths_in_head(paths)
    present = [p for p in paths if os.path.lexists(ROOT / p)]
    gone = [p for p in paths if p not in present]
    restore, remove = [p for p in paths if p in in_head], [p for p in paths if p not in in_head]
    plan = {"ref": park_ref(nnn), "files": paths, "restored_to_head": restore,
            "deleted_new_files": [p for p in remove if p in present]}
    if dry_run:
        return {"saved": False, "dry_run": True, **plan}

    tmp_index = RUNS_DIR / f".park-{nnn}.index"
    env = {**os.environ, "GIT_INDEX_FILE": str(tmp_index), "GIT_LITERAL_PATHSPECS": "1"}
    try:
        _git_ok("read-tree", head, env=env)
        if present:
            _git_ok("add", "-A", "-f", "--pathspec-from-file=-", "--pathspec-file-nul",
                    stdin=_nul(present), env=env)
        if gone:
            _git_ok("rm", "--cached", "-q", "--ignore-unmatch", "--pathspec-from-file=-",
                    "--pathspec-file-nul", stdin=_nul(gone), env=env)
        tree = _git_ok("write-tree", env=env)
    finally:
        try:
            tmp_index.unlink()
        except OSError:
            pass
    parents = ["-p", head]
    previous = _git_out("rev-parse", "--verify", "--quiet", park_ref(nnn))
    if previous:
        parents += ["-p", previous]          # an earlier park stays reachable
    ident = [] if _git_out("config", "user.email") else \
        ["-c", "user.name=backlog-ops", "-c", "user.email=backlog-ops@localhost"]
    commit = _git_ok(*ident, "commit-tree", tree, *parents, "-m",
                     f"backlog park {nnn}: {reason or 'self-heal budget spent'}")
    _git_ok("update-ref", "-m", f"backlog park {nnn}", park_ref(nnn), commit)

    lit = {**os.environ, "GIT_LITERAL_PATHSPECS": "1"}
    if restore:
        _git_ok("restore", "--source=HEAD", "--staged", "--worktree", "--pathspec-from-file=-",
                "--pathspec-file-nul", stdin=_nul(restore), env=lit)
    if remove:
        _git_ok("rm", "--cached", "-q", "--ignore-unmatch", "--pathspec-from-file=-",
                "--pathspec-file-nul", stdin=_nul(remove), env=lit)
        root = ROOT.resolve()
        for p in remove:
            f = ROOT / p
            try:
                if f.is_dir() and not f.is_symlink():
                    shutil.rmtree(f)
                elif os.path.lexists(f):
                    f.unlink()
            except OSError:
                continue
            d = f.parent
            while d.resolve() != root and root in d.resolve().parents:
                try:
                    d.rmdir()                # only while empty
                except OSError:
                    break
                d = d.parent
    return {"saved": True, "commit": commit[:12], **plan}


# --------------------------------------------------------------------------- lint
def run_lint() -> dict:
    errors, warnings = [], []
    if not BACKLOG.exists():
        return {"ok": False, "errors": ["BACKLOG.md not found"], "warnings": []}

    raw = BACKLOG.read_text(encoding="utf-8")
    idx = Index(raw)

    # E1 — required sections
    for sec in ("TODO", "IN PROGRESS", "DONE"):
        if sec not in idx.sections:
            errors.append(f"missing section: ## {sec}")

    # E2 — leaked tool-call markup anywhere in the index
    for i, line in enumerate(raw.split("\n"), 1):
        if MARKUP_RE.search(line):
            errors.append(f"BACKLOG.md:{i}: leaked tool-call markup: {line.strip()[:60]!r}")

    # E3/E4 — bullets under TODO / IN PROGRESS well-formed and pointing at real files
    # E4b — active task tier invariant: filename == body == bullet. Historical
    # DONE files are intentionally immutable/grandfathered; new active filenames
    # carry the tier as NNN-TIER-slug.md and preserve it through start/done.
    linked = {"todo": set(), "in-progress": set()}
    for sec, folder in (("TODO", "todo"), ("IN PROGRESS", "in-progress")):
        rng = idx.sections.get(sec)
        if not rng:
            continue
        for i in range(*rng):
            line = idx.lines[i]
            # catch `-[HIGH]`-style typos too, but not `---` rules
            if not line.startswith("-") or line.startswith("---"):
                continue
            if NONE_RE.match(line):
                continue
            m = BULLET_RE.match(line)
            if not m:
                errors.append(
                    f"BACKLOG.md:{i + 1}: malformed bullet under ## {sec} "
                    f"(need '- [PRIORITY] [Tier] [Title](backlog/{folder}/NNN-TIER-slug.md)', "
                    f"the [Tier] bracket is optional): {line.strip()[:80]!r}"
                )
                continue
            path = m.group("path")
            if not path.startswith(f"backlog/{folder}/"):
                errors.append(
                    f"BACKLOG.md:{i + 1}: bullet under ## {sec} links outside backlog/{folder}/: {path}"
                )
                continue
            if not (TASK_BASE / path).exists():
                errors.append(f"BACKLOG.md:{i + 1}: bullet target does not exist: {path}")
            else:
                task_path = TASK_BASE / path
                queue_name = QUEUE_RE.match(task_path.name)
                filename_tier = queue_name.group("tier") if queue_name else None
                body_tier = read_body_tier(task_path)
                bullet_tier = m.group("tier")
                if not queue_name:
                    errors.append(
                        f"{path}: active filename must be NNN-TIER-slug.md for tier invariant"
                    )
                if not body_tier:
                    errors.append(f"{path}: missing **Tier:** XS|S|M|L body field")
                if not bullet_tier:
                    errors.append(f"BACKLOG.md:{i + 1}: active bullet missing [Tier]")
                present = [t for t in (filename_tier, body_tier, bullet_tier) if t]
                if len(set(present)) > 1:
                    errors.append(
                        f"tier mismatch for {path}: filename={filename_tier or 'missing'}, "
                        f"body={body_tier or 'missing'}, bullet={bullet_tier or 'missing'}"
                    )
            linked[folder].add(Path(path).name)

    # E4c — draft invariant before promotion: planning filename tier == body tier.
    planning_dir = BACKLOG_ROOT / "planning"
    if planning_dir.is_dir():
        for task_path in sorted(planning_dir.glob("*.md")):
            filename = PLANNING_RE.match(task_path.name)
            if not filename:
                errors.append(
                    f"backlog/planning/{task_path.name}: filename does not match "
                    "<timestamp>[-<index>]-<TIER>-<slug>.md"
                )
                continue
            body_tier = read_body_tier(task_path)
            filename_tier = filename.group("tier")
            if not body_tier:
                errors.append(f"backlog/planning/{task_path.name}: missing **Tier:** body field")
            elif body_tier != filename_tier:
                errors.append(
                    f"tier mismatch for backlog/planning/{task_path.name}: "
                    f"filename={filename_tier}, body={body_tier}"
                )

    # E5 — orphan queue files with no index bullet
    files = state_files()
    for folder in ("todo", "in-progress"):
        for name, dirs in files.items():
            if folder in dirs and name not in linked[folder]:
                errors.append(f"backlog/{folder}/{name}: no bullet in BACKLOG.md ## "
                              f"{'TODO' if folder == 'todo' else 'IN PROGRESS'}")

    # E6 — dual-state files
    for name, dirs in files.items():
        if len(dirs) > 1:
            errors.append(f"dual-state task: {name} exists in {', '.join(dirs)}")

    # E7 — DONE bullets are forbidden (backlog/done/ is the source of truth)
    rng = idx.sections.get("DONE")
    if rng:
        for i in range(*rng):
            if BULLET_RE.match(idx.lines[i]):
                errors.append(
                    f"BACKLOG.md:{i + 1}: DONE bullet forbidden (backlog/done/ is the source "
                    f"of truth, see run-backlog STEP 8): {idx.lines[i].strip()[:80]!r}"
                )

    # E8 — duplicate NNN across the queue (todo + in-progress); done-only dupes = warning
    nnn_map = {}
    for name, dirs in files.items():
        m = NNN_RE.match(name)
        if not m:
            # planning-named specs closed in place into done/ are expected
            # (_REVALIDATION-PLAYBOOK closure flow) — only flag them elsewhere
            if not (dirs == ["done"] and PLANNING_RE.match(name)):
                warnings.append(f"non-NNN filename in backlog state dir: {name} ({', '.join(dirs)})")
            continue
        nnn_map.setdefault(m.group(1), []).append((name, dirs))
    for nnn, entries in nnn_map.items():
        if len(entries) < 2:
            continue
        queue_hit = any(d in ("todo", "in-progress") for _, dirs in entries for d in dirs)
        msg = f"duplicate NNN {nnn}: " + "; ".join(
            f"{n} ({', '.join(dirs)})" for n, dirs in entries
        )
        (errors if queue_hit else warnings).append(msg)

    # W — run records (claims) that no longer match a task where they should be
    for rec in run_records():
        task, phase = str(rec.get("task")), rec.get("phase")
        if phase == "in-progress" and not (BACKLOG_ROOT / "in-progress" / task).exists() \
                and not (BACKLOG_ROOT / "todo" / task).exists():
            warnings.append(f"runs/{rec['nnn']}.json: claim for {task} which is not in "
                            "in-progress/ (stale record — `demote`/`lock break` history)")
        elif phase == "done" and not rec.get("shipped"):
            where = "done/" if (BACKLOG_ROOT / "done" / task).exists() else "nowhere"
            warnings.append(f"runs/{rec['nnn']}.json: {task} is DONE ({where}) but its commit "
                            f"was never recorded — the next `pick` returns it as ship-pending")

    # W — single in-progress task, trailing newline
    if len(linked["in-progress"]) > 1:
        warnings.append(f"{len(linked['in-progress'])} tasks IN PROGRESS (pipeline is single-task)")
    if not raw.endswith("\n"):
        warnings.append("BACKLOG.md does not end with a newline")

    return {"ok": not errors, "errors": errors, "warnings": warnings}


# --------------------------------------------------------------------------- pick
def ship_pending_records() -> list:
    """DONE tasks whose commit was never recorded (`shipped`): the run died
    between STEP 8 and STEP 9, so the finished code is still uncommitted —
    left alone, the next task's `git add -A` would sweep it into its commit."""
    return [r for r in run_records()
            if r.get("phase") == "done" and not r.get("shipped")
            and (BACKLOG_ROOT / "done" / str(r.get("task"))).exists()]


def run_pick(caller: Caller) -> dict:
    gate = loop_gate(caller)
    if gate:
        return gate
    extra = {"repo_state": repo_state()}
    for rec in ship_pending_records():
        status = claim_status(rec, caller)
        if status == "live":
            return busy("task-claimed", nnn=rec["nnn"], claim=claim_summary(rec, status))
        return {"state": "ship-pending", "resume": True, "nnn": rec["nnn"],
                "tier": rec.get("tier"), "priority": rec.get("priority"),
                "title": rec.get("title"), "path": f"backlog/done/{rec['task']}",
                "claim": claim_summary(rec, status), **extra}
    idx = load_index()
    for section, state in (("IN PROGRESS", "in-progress"), ("TODO", "todo")):
        bullets = idx.bullets(section)
        if bullets:
            _, m = bullets[0]
            name = Path(m.group("path")).name
            nnn = NNN_RE.match(name)
            result = {
                "state": state,
                "resume": state == "in-progress",
                "nnn": nnn.group(1) if nnn else None,
                "tier": m.group("tier"),
                "priority": m.group("priority"),
                "title": m.group("title"),
                "path": m.group("path"),
                **extra,
            }
            if state == "in-progress" and result["nnn"]:
                rec = read_run(result["nnn"])
                status = claim_status(rec, caller)
                if status == "live":
                    return busy("task-claimed", nnn=result["nnn"], claim=claim_summary(rec, status))
                if status == "none":
                    # Started before claims existed: a live loop from an older
                    # script copy may be working on it right now.
                    foreign = foreign_loop_processes(caller, workers_only=True)
                    if foreign:
                        return busy("unclaimed-task-foreign-loop", nnn=result["nnn"],
                                    processes=foreign)
                result["claim"] = claim_summary(rec, status)
            return result
    return {"state": "empty", **extra}


# --------------------------------------------------------------------------- start / done
def find_bullet(idx: Index, section: str, nnn: str):
    for i, m in idx.bullets(section):
        name = Path(m.group("path")).name
        if name.startswith(f"{nnn}-"):
            return i, m
    return None, None


def norm_nnn(arg: str) -> str:
    name = Path(arg).name
    if re.fullmatch(r"\d{3,}", name):
        return name
    m = NNN_RE.match(name)
    if not m:
        raise SystemExit(
            f"cannot extract NNN from {arg!r} (pass the 3+-digit task number or the NNN-TIER-slug.md filename)"
        )
    return m.group(1)


def prepare_transition(from_section: str, to_dir: str, arg: str):
    """Validate everything and build the mutated index in memory BEFORE any
    filesystem change — a crash mid-transition must never leave the dual-state
    corruption this script exists to prevent."""
    nnn = norm_nnn(arg)
    idx = load_index()
    for sec in ("TODO", "IN PROGRESS"):
        if sec not in idx.sections:
            raise SystemExit(f"BACKLOG.md has no ## {sec} section — fix the index first (run lint)")
    i, m = find_bullet(idx, from_section, nnn)
    if m is None:
        raise SystemExit(f"no ## {from_section} bullet for task {nnn} in BACKLOG.md")
    src = TASK_BASE / m.group("path")
    if not src.exists():
        raise SystemExit(f"task file missing: {m.group('path')}")
    dst_rel = f"backlog/{to_dir}/{src.name}"
    return nnn, idx, i, m, src, dst_rel


def require_agent(caller: Caller, action: str):
    """A claim without a live owner fences nothing — refuse to create one."""
    if caller.agent is None:
        why = f"process table unreadable: {caller.probe_error}" if caller.probe_error else \
            "no claude/codex/gemini process above this call"
        raise SystemExit(
            f"{action}: cannot identify the agent session that would own the claim ({why}). "
            f"Run it from inside the agent session, or set {OWNER_PID_ENV}=<agent pid>. "
            "manual intervention required")


def run_start(arg: str, dry_run: bool, caller: Caller) -> dict:
    gate = loop_gate(caller)
    if gate:
        return gate
    nnn = norm_nnn(arg)
    _, ip = find_bullet(load_index(), "IN PROGRESS", nnn)
    if ip is not None:
        # Lost a race: another session started it between our pick and start.
        rec = read_run(nnn)
        status = claim_status(rec, caller)
        if status in ("live", "none"):
            return busy("task-claimed", nnn=nnn, claim=claim_summary(rec, status))
        raise SystemExit(f"task {nnn} is already IN PROGRESS ({status} claim) — use `resume {nnn}`")
    require_agent(caller, "start")
    nnn, idx, i, m, src, dst_rel = prepare_transition("TODO", "in-progress", arg)
    # A record for a TODO task is a leftover (crash between claim and move, or a
    # pre-demote copy): overwrite it unless its owner is somehow still alive.
    stale = read_run(nnn)
    if claim_status(stale, caller) == "live":
        return busy("task-claimed", nnn=nnn, claim=claim_summary(stale, "live"))
    new_bullet = fmt_bullet(m.group("priority"), m.group("tier"), m.group("title"), dst_rel)
    idx.remove_line(i)
    ip_rng = idx.sections["IN PROGRESS"]
    for j in range(*ip_rng):
        if NONE_RE.match(idx.lines[j]):
            idx.replace_line(j, new_bullet)
            break
    else:
        idx.insert_line(idx.section_tail("IN PROGRESS"), new_bullet)

    meta = {"title": m.group("title"), "tier": m.group("tier"), "priority": m.group("priority")}
    rec = new_run_record(nnn, src.name, meta, caller)
    actions = []
    if not dry_run:
        # Claim BEFORE the move: a crash in between leaves a TODO task with a
        # dead claim (harmless, overwritten above), never an unclaimed IN PROGRESS.
        write_json(run_path(nnn), rec)
        actions.append(f"runs/{nnn}.json: claimed by {caller.agent_source} "
                       f"{(caller.agent or {}).get('name', '?')} pid {(caller.agent or {}).get('pid', '?')}")
    actions.append(move_file(src, dst_rel, dry_run))
    if not dry_run:
        write_backlog(idx.text())
    actions.append(f"BACKLOG.md: TODO bullet {nnn} -> IN PROGRESS")
    result = {"ok": True, "nnn": nnn, "path": dst_rel, "tier": m.group("tier"),
              "priority": m.group("priority"), "title": m.group("title"),
              "actions": actions, "lint": None if dry_run else run_lint()}
    saved = parked_work(nnn)
    if saved:
        result["parked_work"] = saved
    return result


def run_resume(arg: str, dry_run: bool, caller: Caller) -> dict:
    """Take over an IN PROGRESS (or ship-pending DONE) task whose previous
    attempt died, and report what it left behind. Idempotent for the owner."""
    gate = loop_gate(caller)
    if gate:
        return gate
    nnn = norm_nnn(arg)
    idx = load_index()
    _, m = find_bullet(idx, "IN PROGRESS", nnn)
    rec = read_run(nnn)
    if m is not None:
        phase, task_rel = "in-progress", m.group("path")
    elif rec and rec.get("phase") == "done" and (BACKLOG_ROOT / "done" / str(rec.get("task"))).exists():
        phase, task_rel = "done", f"backlog/done/{rec['task']}"
    else:
        raise SystemExit(f"task {nnn} is neither IN PROGRESS nor ship-pending — `pick` decides "
                         "what to work on; a TODO task is started with `start`")

    status = claim_status(rec, caller)
    if status == "live":
        return busy("task-claimed", nnn=nnn, claim=claim_summary(rec, status),
                    caller=caller.describe())
    if status == "none":
        foreign = foreign_loop_processes(caller, workers_only=True)
        if foreign:
            return busy("unclaimed-task-foreign-loop", nnn=nnn, processes=foreign)
    require_agent(caller, "resume")

    # The partial work lives where the task started. Resuming from another
    # checkout or branch would re-implement blind and orphan that work.
    if rec:
        base = rec.get("base") or {}
        mismatch = {}
        if base.get("work_dir") and Path(base["work_dir"]).resolve() != ROOT.resolve():
            mismatch["work_dir"] = {"task_started_in": base["work_dir"], "now": str(ROOT)}
        branch = _git_out("rev-parse", "--abbrev-ref", "HEAD")
        if base.get("branch") and branch and base["branch"] != branch:
            mismatch["branch"] = {"task_started_on": base["branch"], "now": branch}
        if mismatch:
            hint = (f"ship it from the checkout/branch it started in (same --mode): `resume {nnn}` "
                    f"there runs the ship recovery; if its commit already exists, "
                    f"`shipped {nnn} --commit <sha>` from anywhere closes it"
                    if phase == "done" else
                    "resume it from the checkout/branch it started in (same --mode), "
                    f"or abandon it there with `demote {nnn}`")
            return {"ok": False, "state": "conflict", "sentinel": "RESUME_CONFLICT", "nnn": nnn,
                    "phase": phase, "mismatch": mismatch, "hint": hint}

    actions = []
    if rec is None:
        meta = {"title": m.group("title") if m else None, "tier": m.group("tier") if m else None,
                "priority": m.group("priority") if m else None}
        rec = new_run_record(nnn, Path(task_rel).name, meta, caller, legacy=True)
        rec["phase"] = phase
        actions.append(f"runs/{nnn}.json: created (task predates claims — no baseline)")
    elif status == "stale":
        take_over(rec, caller, end="stale")
        actions.append(f"runs/{nnn}.json: took over from a dead attempt "
                       f"(attempt {len(rec['attempts'])})")
    else:
        actions.append(f"runs/{nnn}.json: already mine")
    if phase == "in-progress" and rec.get("phase") != "in-progress":
        rec["phase"] = "in-progress"            # crash between `done`'s claim write and move
    if not dry_run:
        write_json(run_path(nnn), rec)
    ctx = resume_context(rec)
    return {"ok": True, "nnn": nnn, "phase": rec["phase"], "path": task_rel,
            "attempt": len(rec.get("attempts") or []), "attempts": rec.get("attempts"),
            "legacy": rec.get("legacy", False), "repo_state": repo_state(),
            "actions": actions, **ctx}


def run_checkpoint(arg: str, step, result, note, caller: Caller, tree=None) -> dict:
    nnn = norm_nnn(arg)
    if step == COMMIT_STEP:
        return run_commit_checkpoint(nnn, tree, note, caller)
    if step not in CHECKPOINT_STEPS:
        raise SystemExit(f"--step must be one of {', '.join(CHECKPOINT_STEPS + (COMMIT_STEP,))} "
                         f"(got {step!r})")
    rec = read_run(nnn)
    if not rec or rec.get("phase") != "in-progress":
        raise SystemExit(f"task {nnn} has no IN PROGRESS claim — `start` or `resume` it first")
    refused = enforce_owner(nnn, rec, caller, "checkpoint")
    if refused:
        return refused
    entry = {"step": step, "result": result or "ok", "note": note or "",
             "at": now_iso(), "attempt": len(rec.get("attempts") or [])}
    rec.setdefault("checkpoints", []).append(entry)
    write_json(run_path(nnn), rec)
    return {"ok": True, "nnn": nnn, "checkpoint": entry}


def run_commit_checkpoint(nnn: str, tree, note, caller: Caller) -> dict:
    """STEP 9e, right before `git commit`: record the tree the commit will carry.
    This is the ship fence's memory of the commit (task_commits_for) — the
    message itself stays trailer-free. Recorded on the ship-pending claim, so it
    is only valid between `done` and `shipped`."""
    tree = (tree or "").strip().lower()
    if not TREE_SHA_RE.match(tree):
        raise SystemExit("--step commit needs --tree <full sha from `git write-tree`> "
                         f"(got {tree or 'nothing'!r})")
    rec = read_run(nnn)
    if rec is None:
        if (BACKLOG_ROOT / "done").is_dir() and any((BACKLOG_ROOT / "done").glob(f"{nnn}-*.md")):
            return {"ok": True, "nnn": nnn, "note": "no run record (task predates claims) — nothing to record"}
        raise SystemExit(f"task {nnn} has no run record and is not in done/")
    if rec.get("phase") != "done":
        raise SystemExit(f"task {nnn} is {rec.get('phase')!r}, not done — run `done {nnn}` "
                         "first; the commit checkpoint belongs to STEP 9")
    refused = enforce_owner(nnn, rec, caller, "checkpoint")
    if refused:
        return refused
    entry = {"tree": tree, "note": note or "", "at": now_iso(),
             "attempt": len(rec.get("attempts") or [])}
    rec.setdefault("commit_trees", []).append(entry)
    write_json(run_path(nnn), rec)
    return {"ok": True, "nnn": nnn, "commit_tree": entry}


def run_task_commit(arg: str) -> dict:
    """Read-only: the commit that carries task NNN — from the run record's
    `shipped` entry (live or archived), else by its recorded tree / trailer.
    The loop controller uses it to decide whether a post-hoc audit has a
    commit to read."""
    nnn = norm_nnn(arg)
    records = []
    live = read_run(nnn)
    if live:
        records.append(live)
    if ARCHIVE_DIR.is_dir():
        for p in sorted(ARCHIVE_DIR.glob(f"{nnn}-*.json"), reverse=True):
            rec = read_json(p)
            if rec:
                records.append(rec)
    for rec in records:
        shipped = rec.get("shipped") or {}
        if not shipped:
            continue
        if closed_without_commit(rec):
            return {"ok": True, "nnn": nnn, "commit": None, "source": "no-commit"}
        sha = (shipped.get("commit") or "").strip()
        if sha:
            return {"ok": True, "nnn": nnn, "commit": sha, "source": "shipped"}
    for rec in records:
        found = task_commits_for(rec)
        if found:
            return {"ok": True, "nnn": nnn, "commit": found[0].split()[0], "source": "commit-tree"}
    # Unknown: no shipped record and no recorded commit found. Callers must not read
    # this as "the task made no commit" — only source "no-commit" means that.
    return {"ok": True, "nnn": nnn, "commit": None, "source": None}


def closed_without_commit(rec: dict) -> bool:
    """A run closed as "already satisfied" (STEP 6a, exit 2): `shipped --commit` then
    names the HEAD the criteria were checked against, not a commit of this task.
    Recognised by its note, or by a `skipped` push whose sha is the task's base."""
    shipped = rec.get("shipped") or {}
    if (shipped.get("note") or "").strip().lower() == "already-satisfied":
        return True
    if shipped.get("push") != "skipped":
        return False
    base_head = (rec.get("base") or {}).get("head")
    sha = (shipped.get("commit") or "").strip()
    return bool(base_head and sha and
                _git_out("rev-parse", "--verify", "--quiet", f"{sha}^{{commit}}") == base_head)


def run_done(arg: str, dry_run: bool, caller: Caller) -> dict:
    nnn, idx, i, m, src, dst_rel = prepare_transition("IN PROGRESS", "done", arg)
    rec = read_run(nnn)
    refused = enforce_owner(nnn, rec, caller, "done")
    if refused:
        return refused
    # A task started by a pre-claim version is being finished by a pre-claim
    # skill that will never call `shipped` nor record its commit tree:
    # fencing it would make the next run re-commit whatever is dirty. No fence.
    if rec is not None:
        rec["phase"], rec["done_at"] = "done", now_iso()
    idx.remove_line(i)
    if not idx.bullets("IN PROGRESS"):
        idx.insert_line(idx.section_tail("IN PROGRESS"), "- (none)")

    actions = []
    if not dry_run and rec is not None:
        write_json(run_path(nnn), rec)
        actions.append(f"runs/{nnn}.json: ship pending until `shipped {nnn}`")
    actions.append(move_file(src, dst_rel, dry_run))
    if not dry_run:
        write_backlog(idx.text())
    actions.append(f"BACKLOG.md: IN PROGRESS bullet {nnn} removed")
    return {"ok": True, "nnn": nnn, "path": dst_rel, "title": m.group("title"),
            "actions": actions, "lint": None if dry_run else run_lint(),
            "note": "write the completion summary into the done file yourself "
                    "(content is model work; only the transition is scripted), then "
                    f"record the staged tree (`checkpoint {nnn} --step commit --tree <git write-tree>`) "
                    "right before the commit (no trailer), push, and record it with "
                    f"`shipped {nnn} --commit <sha> --push <state>`"}


def run_shipped(arg: str, commit, push, note, caller: Caller) -> dict:
    nnn = norm_nnn(arg)
    if push and push not in PUSH_STATES:
        raise SystemExit(f"--push must be one of {', '.join(PUSH_STATES)} (got {push!r})")
    if not commit:
        raise SystemExit("--commit <sha> is required (the commit that carries the task)")
    rec = read_run(nnn)
    if rec is None:
        if (BACKLOG_ROOT / "done").is_dir() and any((BACKLOG_ROOT / "done").glob(f"{nnn}-*.md")):
            return {"ok": True, "nnn": nnn, "note": "no run record (task predates claims) — nothing to close"}
        raise SystemExit(f"task {nnn} has no run record and is not in done/")
    if rec.get("phase") != "done":
        raise SystemExit(f"task {nnn} is {rec.get('phase')!r}, not done — run `done {nnn}` first")
    refused = enforce_owner(nnn, rec, caller, "shipped")
    if refused:
        return refused
    rec["shipped"] = {"commit": commit, "push": push or "pushed", "note": note or "", "at": now_iso()}
    write_json(run_path(nnn), rec)            # never re-shipped, even if archiving fails
    archive_run(nnn, rec, "shipped")
    actions = [f"runs/{nnn}.json: closed (archived)"]
    if _git_out("rev-parse", "--verify", "--quiet", park_ref(nnn)):
        _git_out("update-ref", "-d", park_ref(nnn))
        actions.append(f"{park_ref(nnn)}: deleted (task shipped)")
    return {"ok": True, "nnn": nnn, "shipped": rec["shipped"], "actions": actions}


def run_demote(arg: str, dry_run: bool, caller: Caller) -> dict:
    """Abandon a blocked run: in-progress -> todo, bullet back to the HEAD of
    ## TODO (it was the queue head when picked, so it stays next in line)."""
    nnn, idx, i, m, src, dst_rel = prepare_transition("IN PROGRESS", "todo", arg)
    rec = read_run(nnn)
    refused = enforce_owner(nnn, rec, caller, "demote")
    if refused:
        return refused
    new_bullet = fmt_bullet(m.group("priority"), m.group("tier"), m.group("title"), dst_rel)
    idx.remove_line(i)
    if not idx.bullets("IN PROGRESS"):
        idx.insert_line(idx.section_tail("IN PROGRESS"), "- (none)")
    todo_bullets = idx.bullets("TODO")
    at = todo_bullets[0][0] if todo_bullets else idx.section_tail("TODO")
    idx.insert_line(at, new_bullet)

    actions = [move_file(src, dst_rel, dry_run)]
    if not dry_run:
        write_backlog(idx.text())
        if rec is not None:
            archive_run(nnn, rec, "demoted")
            actions.append(f"runs/{nnn}.json: closed (demoted)")
    actions.append(f"BACKLOG.md: IN PROGRESS bullet {nnn} -> head of TODO")
    return {"ok": True, "nnn": nnn, "path": dst_rel, "title": m.group("title"),
            "actions": actions, "lint": None if dry_run else run_lint()}


def run_defer(arg: str, dry_run: bool, caller: Caller) -> dict:
    """Skip a TODO task without abandoning it: move its bullet to the TAIL of
    ## TODO so `pick` returns the next task. The file stays in backlog/todo/.
    Used by run-backlog when a task's **Requires:** (e.g. unity-editor) is not
    live in the current environment — deferring beats dead-ending the loop."""
    gate = loop_gate(caller)
    if gate:
        return gate
    nnn = norm_nnn(arg)
    idx = load_index()
    i, m = find_bullet(idx, "TODO", nnn)
    if m is None:
        raise SystemExit(f"no ## TODO bullet for task {nnn} in BACKLOG.md")
    bullets = idx.bullets("TODO")
    if len(bullets) < 2:
        return {"ok": True, "nnn": nnn, "title": m.group("title"),
                "actions": [], "note": "only bullet in TODO — defer is a no-op "
                "(nothing to run ahead of it)", "lint": run_lint()}
    line = idx.lines[i]
    idx.remove_line(i)
    idx.insert_line(idx.section_tail("TODO"), line)
    if not dry_run:
        write_backlog(idx.text())
    return {"ok": True, "nnn": nnn, "title": m.group("title"),
            "actions": [f"BACKLOG.md: TODO bullet {nnn} -> tail of TODO"],
            "lint": None if dry_run else run_lint()}


def run_park(arg: str, reason, keep_work: bool, dry_run: bool, caller: Caller) -> dict:
    """Give up on a task for the rest of this loop run WITHOUT stopping the loop:
    IN PROGRESS (or TODO) -> tail of ## TODO. For an IN PROGRESS task the partial
    work is first saved to refs/backlog/parked/<NNN> and taken out of the tree
    (see park_partial_work), so the next task starts clean; --keep-work skips that
    (e.g. RESUME_CONFLICT: the work belongs to another checkout/branch)."""
    nnn = norm_nnn(arg)
    idx = load_index()
    i_ip, m_ip = find_bullet(idx, "IN PROGRESS", nnn)
    i_td, m_td = find_bullet(idx, "TODO", nnn)
    if m_ip is None and m_td is None:
        raise SystemExit(f"task {nnn} is neither IN PROGRESS nor TODO — nothing to park")
    rec = read_run(nnn)
    if m_ip is not None:
        refused = enforce_owner(nnn, rec, caller, "park")
        if refused:
            return refused
    reason = (reason or "self-heal budget spent").strip()
    actions, work = [], None

    if m_ip is not None:
        nnn, idx, i, m, src, dst_rel = prepare_transition("IN PROGRESS", "todo", arg)
        if keep_work:
            work = {"saved": False, "note": "--keep-work: partial work left in the tree"}
        elif rec is None or rec.get("baseline_dirty") is None:
            work = {"saved": False, "note": "task predates claims (no baseline): its partial "
                                            "work cannot be told apart from other dirt — left "
                                            "in the tree"}
        else:
            ctx = resume_context(rec)
            work = park_partial_work(nnn, [p["path"] for p in ctx["partial_work"]],
                                     reason, dry_run)
        line = fmt_bullet(m.group("priority"), m.group("tier"), m.group("title"), dst_rel)
        idx.remove_line(i)
        if not idx.bullets("IN PROGRESS"):
            idx.insert_line(idx.section_tail("IN PROGRESS"), "- (none)")
        actions.append(move_file(src, dst_rel, dry_run))
        title = m.group("title")
    else:
        line, dst_rel, title = idx.lines[i_td], m_td.group("path"), m_td.group("title")
        idx.remove_line(i_td)
    rng = idx.sections["TODO"]
    for j in range(*rng):
        if NONE_RE.match(idx.lines[j]):
            idx.remove_line(j)
            break
    idx.insert_line(idx.section_tail("TODO"), line)
    actions.append(f"BACKLOG.md: bullet {nnn} -> tail of TODO (parked)")

    if not dry_run:
        write_backlog(idx.text())
        note = [f"\n\n## Parked — {now_iso()}\n", f"- Reason: {reason}\n"]
        if work and work.get("saved"):
            note.append(f"- Partial work saved at `{work['ref']}` (commit {work['commit']}) and "
                        "removed from the tree; `start` reports it as `parked_work`.\n")
        elif work:
            note.append(f"- Partial work: {work.get('note')}\n")
        try:
            with open(TASK_BASE / dst_rel, "a", encoding="utf-8") as fh:
                fh.write("".join(note))
        except OSError:
            pass
        if rec is not None and m_ip is not None:
            rec["parked"] = {"reason": reason, "at": now_iso(), "work": work}
            archive_run(nnn, rec, "parked")
            actions.append(f"runs/{nnn}.json: closed (parked)")
    return {"ok": True, "nnn": nnn, "path": dst_rel, "title": title, "reason": reason,
            "work": work, "actions": actions, "lint": None if dry_run else run_lint()}


# --------------------------------------------------------------------------- lock (loop lease)
def lease_view(lease):
    if not lease:
        return None
    view = {k: v for k, v in lease.items() if k != "token"}
    view["liveness"] = liveness(lease.get("holder"))
    return view


def run_lock_acquire(ns, caller: Caller) -> dict:
    """One loop controller per clone. Refuses while another controller holds a
    live lease, while any task is claimed by a live session (an orphaned task
    window from a killed controller is still working), or while a loop from a
    pre-lease script copy runs. Re-acquiring as the same process is idempotent."""
    pid = ns.pid or os.getppid()
    try:
        holder = ident_of(pid)
    except ProbeError as e:
        raise SystemExit(f"cannot identify the controller process {pid}: {e}")
    if not holder:
        raise SystemExit(f"controller process {pid} is not running")
    prev = read_json(LEASE_FILE)
    if prev:
        if same_process(prev.get("holder"), holder):
            return {"ok": True, "token": prev.get("token"), "lease": lease_view(prev), "reacquired": True}
        state = liveness(prev.get("holder"))
        if state != "dead":
            return busy("loop-active", lease=lease_view(prev), holder_liveness=state)
    probe = Caller(probe=True)
    live = [{**claim_summary(r, "live"), "nnn": r["nnn"], "phase": r.get("phase")}
            for r in run_records() if claim_status(r, probe) == "live"]
    if live:
        return busy("task-claimed", claims=live)
    foreign = foreign_loop_processes(caller, exclude=self_and_ancestors(pid))
    if foreign:
        return busy("legacy-loop", processes=foreign)
    lease = {"version": 1, "token": secrets.token_hex(16), "holder": holder,
             "mode": (ns.mode or "current").lower(), "work_dir": ns.work_dir or str(ROOT),
             "branch": ns.branch or _git_out("rev-parse", "--abbrev-ref", "HEAD"),
             "log_dir": ns.log_dir, "acquired_at": now_iso()}
    if prev:
        lease["replaced_stale"] = {k: v for k, v in prev.items() if k != "token"}
    write_json(LEASE_FILE, lease)
    return {"ok": True, "token": lease["token"], "lease": lease_view(lease)}


def run_lock_release(token) -> dict:
    lease = read_json(LEASE_FILE)
    if not token:
        raise SystemExit("lock release needs --token (the one `lock acquire` printed)")
    if lease and lease.get("token") == token:
        try:
            LEASE_FILE.unlink()
        except OSError:
            pass
        return {"ok": True, "released": True}
    return {"ok": True, "released": False,
            "note": "lease not held by this token (already released, broken, or replaced)"}


def run_lock_status(caller: Caller) -> dict:
    lease = read_json(LEASE_FILE)
    view = lease_view(lease)
    claims = []
    for r in run_records():
        status = claim_status(r, caller)
        claims.append({"nnn": r["nnn"], "task": r.get("task"), "phase": r.get("phase"),
                       **claim_summary(r, status)})
    holder_pid = ((lease or {}).get("holder") or {}).get("pid")
    live_lease = bool(view and view["liveness"] != "dead")
    # The live holder (and the shell that launched it) is the lease, not a legacy loop.
    foreign = foreign_loop_processes(
        caller, exclude=self_and_ancestors(holder_pid) if live_lease and holder_pid else ())
    busy_now = bool(live_lease or any(c["status"] == "live" for c in claims) or foreign)
    return {"ok": not busy_now, "state": "busy" if busy_now else "free", "lease": view,
            "claims": claims, "ship_pending": [r["nnn"] for r in ship_pending_records()],
            "legacy_loop_processes": foreign, "caller": caller.describe()}


def run_lock_break(task, yes: bool) -> dict:
    """Manual override — for a holder the operator KNOWS is dead but that cannot
    be proven dead (other host, unreadable process table). Breaking a claim keeps
    its journal and baseline, so the next `resume` still sees the partial work."""
    if not yes:
        raise SystemExit("lock break is a manual override: re-run with --yes once you are sure "
                         "the holder is not running")
    if task:
        nnn = norm_nnn(task)
        rec = read_run(nnn)
        if not rec:
            return {"ok": True, "broken": False, "note": f"task {nnn} has no claim"}
        old = (rec.get("owner") or {}).get("process")
        if rec.get("attempts"):
            rec["attempts"][-1].setdefault("ended_at", now_iso())
            rec["attempts"][-1].setdefault("end", "broken")
        rec["owner"] = {"process": None, "agent_source": "broken", "loop_token": None,
                        "claimed_at": now_iso()}
        write_json(run_path(nnn), rec)
        return {"ok": True, "broken": True, "nnn": nnn, "previous_owner": old}
    lease = read_json(LEASE_FILE)
    if not lease:
        return {"ok": True, "broken": False, "note": "no loop lease"}
    try:
        LEASE_FILE.unlink()
    except OSError:
        pass
    return {"ok": True, "broken": True, "previous_lease": lease_view(lease)}


# --------------------------------------------------------------------------- promote
def parse_planning(path: Path) -> dict:
    m = PLANNING_RE.match(path.name)
    if not m:
        raise SystemExit(
            f"{path.name}: filename does not match "
            "'<timestamp>[-<index>]-<TIER>-<slug>.md'"
        )
    priority, title, depends = "MEDIUM", m.group("slug").replace("-", " "), []
    body_tier = None
    pending_mockup = None
    has_groundtruth = False
    ui_signal = False
    heading_found = False
    in_comment = False
    for line in path.read_text(encoding="utf-8").split("\n"):
        # Skip HTML-comment blocks — templates carry example field spellings in
        # comments, and a leftover comment must never be parsed as a real field.
        stripped = line.strip()
        if in_comment:
            if "-->" in stripped:
                in_comment = False
            continue
        if stripped.startswith("<!--"):
            if "-->" not in stripped:
                in_comment = True
            continue
        if not heading_found:
            h = HEADING_RE.match(line)
            if h:
                priority, title = h.group("priority"), h.group("title")
                heading_found = True
        if body_tier is None:
            tier_match = BODY_TIER_RE.match(stripped)
            if tier_match:
                body_tier = tier_match.group("tier").upper()
        if not depends:
            d = DEPENDS_RE.match(stripped)
            if d:
                # Backticked filenames are the convention and a dependency line often
                # carries prose around them ("`-12-.md` (một chiều — `-12-` là **chủ**)").
                # Comma-splitting swallows that commentary into the token, and the dep then
                # matches nothing — a silently wrong promote gate.
                depends = DEP_REF_RE.findall(d.group("deps"))
                if not depends:
                    depends = [tok.strip().strip("`") for tok in d.group("deps").split(",")]
                depends = [t for t in depends if t and t.lower() not in ("none", "n/a", "-")]
        if pending_mockup is None:
            g = PENDING_GROUNDTRUTH_RE.search(stripped)
            if g:
                pending_mockup = g.group(1)
        if not has_groundtruth and ANY_GROUNDTRUTH_RE.search(stripped):
            has_groundtruth = True
        if not ui_signal and NEWUI_SIGNAL_RE.search(stripped):
            ui_signal = True
    filename_tier = m.group("tier")
    tier_errors = []
    if body_tier is None:
        tier_errors.append(f"{path.name}: missing **Tier:** body field")
    elif body_tier != filename_tier:
        tier_errors.append(
            f"{path.name}: tier mismatch filename={filename_tier}, body={body_tier}"
        )
    return {"tier": filename_tier, "body_tier": body_tier,
            "tier_errors": tier_errors, "slug": m.group("slug"),
            "priority": priority, "title": title, "depends_on": depends,
            "pending_mockup": pending_mockup,
            "has_groundtruth": has_groundtruth, "ui_signal": ui_signal,
            "sort_key": (m.group("ts"), int(m.group("idx") or 0), path.name)}


def resolve_planning_arg(arg: str) -> Path:
    """Accept every spelling a caller may reasonably pass for a planning file:
    an absolute path, the canonical `backlog/planning/<name>.md` (relative to
    the git common dir), the same string relative to the worktree root (which
    resolves when the optional discoverability junction exists), or a bare
    filename. Returning the canonical location for a non-existent input keeps
    the caller's error message pointing at the real backlog."""
    path = Path(arg)
    if path.is_absolute():
        return path
    for candidate in (TASK_BASE / arg, ROOT / arg, BACKLOG_ROOT / "planning" / path.name):
        if candidate.exists():
            return candidate
    return TASK_BASE / arg


def dependency_warnings(tasks) -> list:
    """Cross-check each task's **Depends on:** targets. A dependency is
    satisfied when it is EARLIER in this same promote batch (sort order) or
    already lives in todo/in-progress/done — matched by filename stem, NNN,
    or SLUG (promote renames planning files to NNN-TIER-<slug>.md, so a dep written
    as a planning filename must still match its already-promoted twin).
    Unsatisfied deps become preflight blockers so a partial promote cannot
    silently queue a dependent before its upstream (run-backlog executes
    strictly in queue order)."""
    known, known_slugs = set(), set()
    for name, dirs in state_files().items():
        stem = name[:-3] if name.endswith(".md") else name
        known.add(stem)
        m = NNN_RE.match(name)
        if m:
            known.add(m.group(1))
            slug = stem[len(m.group(1)) + 1:]          # NNN-[TIER-]<slug>
            queue_match = QUEUE_RE.match(name)
            if queue_match:
                slug = queue_match.group("slug")
            known_slugs.add(slug)
            if slug.endswith("-2"):                     # promote dedup suffix
                known_slugs.add(slug[:-2])
    warns, batch_before = [], set()
    for t in tasks:
        for dep in t.get("depends_on", []):
            d = dep[:-3] if dep.endswith(".md") else dep
            if d in known or d in batch_before:
                continue
            pm = PLANNING_RE.match(d + ".md")            # planning-form dep -> match by slug
            if pm and pm.group("slug") in known_slugs:
                continue
            warns.append(
                f"{t['src'].name}: depends on {dep!r} which is neither earlier in "
                f"this batch nor in todo/in-progress/done — promoting it now can "
                f"make run-backlog execute it before its dependency"
            )
        stem = t["src"].name[:-3]
        batch_before.add(stem)
    return warns


def mockup_warnings(tasks) -> list:
    """Two silent-skip holes in the mockup gate, both promotion blockers:

    1. A task whose groundTruth is still PENDING-* would execute without an
       approved visual reference — the failure the mockup pipeline prevents.
    2. A task that signals UI-screen work (references /new-ui) but carries NO
       groundTruth token at all. This was the /new-feature-HYBRID hole: the
       mockup gate keyed only off a `groundTruth=` token, which such tasks
       never emitted, so a brand-new screen skipped draft+approval entirely.
       Requiring an explicit decision (draft / clone / none) closes it."""
    warns = []
    for t in tasks:
        marker = t.get("pending_mockup")
        if marker:
            warns.append(
                f"{t['src'].name}: groundTruth still {marker} — run /ui-mockup "
                f"(drafts + auto-approves to a frozen PNG) before this task executes, "
                f"or set groundTruth=clone:<ExistingPrefab> if the screen only clones "
                f"an existing layout"
            )
        elif t.get("ui_signal") and not t.get("has_groundtruth"):
            warns.append(
                f"{t['src'].name}: references /new-ui (builds/authors a UI screen) but "
                f"carries no mockup groundTruth — the task would skip the draft+approval "
                f"gate. Draft one via /ui-mockup by adding a line "
                f"`**Mockup:** groundTruth=PENDING-MOCKUP (screen=<Feature>/<Screen>)`, or "
                f"declare no mockup is needed with groundTruth=clone:<ExistingPrefab> "
                f"(clones an existing layout) or groundTruth=none (only wires an existing screen)"
            )
    return warns


def run_promote(paths, priority_override, dry_run: bool, check_only=False) -> dict:
    tasks = []
    for p in paths:
        path = resolve_planning_arg(p)
        if not path.exists():
            raise SystemExit(f"planning file not found: {p} (looked in {BACKLOG_ROOT / 'planning'})")
        if path.parent.name != "planning":
            raise SystemExit(f"not a backlog/planning/ file: {p}")
        info = parse_planning(path)
        info["src"] = path
        tasks.append(info)
    tasks.sort(key=lambda t: t["sort_key"])  # task order, not argv order

    dep_warnings = dependency_warnings(tasks)
    mock_warnings = mockup_warnings(tasks)
    tier_errors = [error for task in tasks for error in task.get("tier_errors", [])]

    # Promotion blockers are evaluated before the first git/filesystem mutation.
    # `--check` exposes this read-only preflight to /add-to-backlog; the normal
    # promote path enforces the same result so callers cannot accidentally bypass it.
    blockers = tier_errors + dep_warnings + mock_warnings
    if check_only or blockers:
        return {
            "ok": not blockers,
            "check_only": True,
            "tasks": [f"backlog/planning/{t['src'].name}" for t in tasks],
            "tier_errors": tier_errors,
            "dependency_warnings": dep_warnings,
            "mockup_warnings": mock_warnings,
            "moved": [],
            "actions": [],
            "lint": run_lint(),
        }

    if priority_override:
        for t in tasks:
            t["priority"] = priority_override

    nnn = max_nnn()
    actions, bullets, moved = [], [], []
    for t in tasks:
        nnn += 1
        dst_name = f"{nnn:03d}-{t['tier']}-{t['slug']}.md"
        if (BACKLOG_ROOT / "todo" / dst_name).exists():
            dst_name = f"{nnn:03d}-{t['tier']}-{t['slug']}-2.md"
        dst_rel = f"backlog/todo/{dst_name}"
        try:
            actions.append(move_file(t["src"], dst_rel, dry_run))
        except OSError as e:
            actions.append(f"FAILED mv {t['src'].name}: {e}")
            continue
        bullets.append(fmt_bullet(t["priority"], t["tier"], t["title"], dst_rel))
        moved.append({"nnn": f"{nnn:03d}", "path": dst_rel, "tier": t["tier"],
                      "priority": t["priority"], "title": t["title"]})

    if bullets and not dry_run:
        idx = load_index()
        at = idx.section_tail("TODO")
        for j, b in enumerate(bullets):
            idx.insert_line(at + j, b)
        for i, line in enumerate(idx.lines):  # drop `- (none)` placeholder if present
            rng = idx.sections.get("TODO")
            if rng and rng[0] <= i < rng[1] and NONE_RE.match(line):
                idx.remove_line(i)
                break
        write_backlog(idx.text())
        actions.append(f"BACKLOG.md: appended {len(bullets)} bullet(s) to ## TODO")

    return {"ok": all(not a.startswith("FAILED") for a in actions),
            "moved": moved, "actions": actions,
            "tier_errors": [], "dependency_warnings": [],
            "mockup_warnings": [],
            "lint": None if dry_run else run_lint()}


# --------------------------------------------------------------------------- main
MUTATING = ("start", "done", "demote", "defer", "park", "promote", "resume", "checkpoint", "shipped")
LOCK_SUBCOMMANDS = ("acquire", "release", "status", "break")


def main():
    ap = argparse.ArgumentParser(prog="backlog-ops.py", description=__doc__,
                                 formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("command", choices=["init", "lint", "pick", "start", "done", "demote", "defer",
                                        "park", "promote", "timestamp", "resume", "checkpoint",
                                        "shipped", "lock", "task-commit"])
    ap.add_argument("args", nargs="*")
    ap.add_argument("--priority", choices=PRIORITIES)
    ap.add_argument("--dry-run", action="store_true")
    ap.add_argument("--check", action="store_true",
                    help="validate promote inputs and blockers without mutation")
    ap.add_argument("--probe", action="store_true",
                    help="pick: evaluate on another process's behalf (loop controller pre-check)")
    ap.add_argument("--step", help="checkpoint: " + "|".join(CHECKPOINT_STEPS + (COMMIT_STEP,)))
    ap.add_argument("--tree", help="checkpoint --step commit: the staged tree (`git write-tree`)")
    ap.add_argument("--result", help="checkpoint: pass|warn|skipped|ok (free text)")
    ap.add_argument("--note", help="checkpoint/shipped: one-line note")
    ap.add_argument("--reason", help="park: why the task is parked (one line)")
    ap.add_argument("--keep-work", action="store_true",
                    help="park: leave the partial work in the tree instead of saving + removing it")
    ap.add_argument("--commit", help="shipped: sha of the commit that carries the task")
    ap.add_argument("--push", help="shipped: " + "|".join(PUSH_STATES))
    ap.add_argument("--pid", type=int, help="lock acquire: the controller process id")
    ap.add_argument("--mode", help="lock acquire: current|worktree")
    ap.add_argument("--work-dir", help="lock acquire: the loop's working directory")
    ap.add_argument("--branch", help="lock acquire: the loop's work branch")
    ap.add_argument("--log-dir", help="lock acquire: where the loop writes iteration logs")
    ap.add_argument("--token", help="lock release: the token `lock acquire` printed")
    ap.add_argument("--task", help="lock break: break this task's claim instead of the lease")
    ap.add_argument("--yes", action="store_true", help="lock break: confirm the manual override")
    # Allow natural command syntax such as `promote --check file.md`; plain
    # parse_args cannot intermix an option after the command with nargs="*".
    ns = ap.parse_intermixed_args()
    if ns.check and ns.command != "promote":
        ap.error("--check is only valid with promote")
    if ns.probe and ns.command != "pick":
        ap.error("--probe is only valid with pick")

    if ns.command == "timestamp":
        now = datetime.now(timezone.utc)  # single instant — two now() calls can tear across a second boundary
        print(now.strftime("%Y%m%dT%H%M%S") + f"{now.microsecond // 1000:03d}")
        return 0

    def one_task():
        if len(ns.args) != 1:
            ap.error(f"{ns.command} takes exactly one <NNN> (or task path)")
        return ns.args[0]

    sub = None
    if ns.command == "lock":
        if len(ns.args) != 1 or ns.args[0] not in LOCK_SUBCOMMANDS:
            ap.error(f"lock takes one of: {', '.join(LOCK_SUBCOMMANDS)}")
        sub = ns.args[0]

    init_actions = ensure_structure(ns.command)
    guarded = ns.command in MUTATING or (ns.command == "lock" and sub != "status")

    with (backlog_mutex() if guarded and not ns.dry_run else nullcontext()):
        caller = Caller(probe=ns.probe) if ns.command in (
            "pick", "start", "done", "demote", "defer", "park", "resume", "checkpoint", "shipped",
            "lock") else None
        if ns.command == "init":
            RUNS_DIR.mkdir(parents=True, exist_ok=True)
            result = {"ok": True, "backlog_root": str(BACKLOG_ROOT),
                      "actions": init_actions or ["already initialised"],
                      "lint": run_lint()}
        elif ns.command == "lint":
            result = run_lint()
        elif ns.command == "pick":
            result = run_pick(caller)
        elif ns.command == "start":
            result = run_start(one_task(), ns.dry_run, caller)
        elif ns.command == "done":
            result = run_done(one_task(), ns.dry_run, caller)
        elif ns.command == "demote":
            result = run_demote(one_task(), ns.dry_run, caller)
        elif ns.command == "defer":
            result = run_defer(one_task(), ns.dry_run, caller)
        elif ns.command == "park":
            result = run_park(one_task(), ns.reason, ns.keep_work, ns.dry_run, caller)
        elif ns.command == "resume":
            result = run_resume(one_task(), ns.dry_run, caller)
        elif ns.command == "checkpoint":
            result = run_checkpoint(one_task(), ns.step, ns.result, ns.note, caller, ns.tree)
        elif ns.command == "shipped":
            result = run_shipped(one_task(), ns.commit, ns.push, ns.note, caller)
        elif ns.command == "task-commit":
            result = run_task_commit(one_task())
        elif ns.command == "lock":
            if sub == "acquire":
                result = run_lock_acquire(ns, caller)
            elif sub == "release":
                result = run_lock_release(ns.token)
            elif sub == "status":
                result = run_lock_status(caller)
            else:
                result = run_lock_break(ns.task, ns.yes)
        elif ns.command == "promote":
            if not ns.args:
                ap.error("promote takes one or more backlog/planning/*.md paths")
            result = run_promote(ns.args, ns.priority, ns.dry_run, ns.check)

    print(json.dumps(result, ensure_ascii=False, indent=2))
    if result.get("state") == "busy":
        return EXIT_BUSY
    if result.get("state") == "conflict":
        return EXIT_CONFLICT
    if ns.command == "pick":
        return 2 if result.get("state") == "empty" else 0
    if ns.command == "lock":
        return 0 if result.get("ok", True) else 1
    lint = result if ns.command == "lint" else result.get("lint")
    ok = result.get("ok", True) and (lint is None or lint["ok"])
    return 0 if ok else 1


if __name__ == "__main__":
    sys.exit(main())
