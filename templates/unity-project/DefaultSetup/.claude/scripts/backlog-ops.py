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
  promote <planning.md>...     planning -> todo (assign NNN, append bullets).
                              --check validates the batch without mutation.
                              Optional --priority HIGH|MEDIUM|LOW for all files.
                              Blocks when a task's **Depends on:** target is not
                              earlier in the batch nor in todo/in-progress/done,
                              and when a /new-ui task's groundTruth is still
                              PENDING-MOCKUP / PENDING-APPROVAL (mockup not yet
                              auto-approved — run /ui-mockup).
  timestamp                   Print UTC YYYYMMDDTHHmmssSSS (planning filenames).

Every mutating command re-runs lint afterwards and embeds the result.
--dry-run prints the plan without touching anything.

Exit codes: 0 = ok · 1 = lint errors / operation failure · 2 = pick found nothing
· 3 = backlog root missing (run `init`).
"""

import argparse
import json
import os
import re
import shutil
import subprocess
import sys
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

    # W — single in-progress task, trailing newline
    if len(linked["in-progress"]) > 1:
        warnings.append(f"{len(linked['in-progress'])} tasks IN PROGRESS (pipeline is single-task)")
    if not raw.endswith("\n"):
        warnings.append("BACKLOG.md does not end with a newline")

    return {"ok": not errors, "errors": errors, "warnings": warnings}


# --------------------------------------------------------------------------- pick
def run_pick() -> dict:
    idx = load_index()
    for section, state in (("IN PROGRESS", "in-progress"), ("TODO", "todo")):
        bullets = idx.bullets(section)
        if bullets:
            _, m = bullets[0]
            name = Path(m.group("path")).name
            nnn = NNN_RE.match(name)
            return {
                "state": state,
                "resume": state == "in-progress",
                "nnn": nnn.group(1) if nnn else None,
                "tier": m.group("tier"),
                "priority": m.group("priority"),
                "title": m.group("title"),
                "path": m.group("path"),
            }
    return {"state": "empty"}


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


def run_start(arg: str, dry_run: bool) -> dict:
    nnn, idx, i, m, src, dst_rel = prepare_transition("TODO", "in-progress", arg)
    new_bullet = fmt_bullet(m.group("priority"), m.group("tier"), m.group("title"), dst_rel)
    idx.remove_line(i)
    ip_rng = idx.sections["IN PROGRESS"]
    for j in range(*ip_rng):
        if NONE_RE.match(idx.lines[j]):
            idx.replace_line(j, new_bullet)
            break
    else:
        idx.insert_line(idx.section_tail("IN PROGRESS"), new_bullet)

    actions = [move_file(src, dst_rel, dry_run)]
    if not dry_run:
        write_backlog(idx.text())
    actions.append(f"BACKLOG.md: TODO bullet {nnn} -> IN PROGRESS")
    return {"ok": True, "nnn": nnn, "path": dst_rel, "tier": m.group("tier"),
            "priority": m.group("priority"), "title": m.group("title"),
            "actions": actions, "lint": None if dry_run else run_lint()}


def run_done(arg: str, dry_run: bool) -> dict:
    nnn, idx, i, m, src, dst_rel = prepare_transition("IN PROGRESS", "done", arg)
    idx.remove_line(i)
    if not idx.bullets("IN PROGRESS"):
        idx.insert_line(idx.section_tail("IN PROGRESS"), "- (none)")

    actions = [move_file(src, dst_rel, dry_run)]
    if not dry_run:
        write_backlog(idx.text())
    actions.append(f"BACKLOG.md: IN PROGRESS bullet {nnn} removed")
    return {"ok": True, "nnn": nnn, "path": dst_rel, "title": m.group("title"),
            "actions": actions, "lint": None if dry_run else run_lint(),
            "note": "write the completion summary into the done file yourself "
                    "(content is model work; only the transition is scripted)"}


def run_demote(arg: str, dry_run: bool) -> dict:
    """Abandon a blocked run: in-progress -> todo, bullet back to the HEAD of
    ## TODO (it was the queue head when picked, so it stays next in line)."""
    nnn, idx, i, m, src, dst_rel = prepare_transition("IN PROGRESS", "todo", arg)
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
    actions.append(f"BACKLOG.md: IN PROGRESS bullet {nnn} -> head of TODO")
    return {"ok": True, "nnn": nnn, "path": dst_rel, "title": m.group("title"),
            "actions": actions, "lint": None if dry_run else run_lint()}


def run_defer(arg: str, dry_run: bool) -> dict:
    """Skip a TODO task without abandoning it: move its bullet to the TAIL of
    ## TODO so `pick` returns the next task. The file stays in backlog/todo/.
    Used by run-backlog when a task's **Requires:** (e.g. unity-editor) is not
    live in the current environment — deferring beats dead-ending the loop."""
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
def main():
    ap = argparse.ArgumentParser(prog="backlog-ops.py", description=__doc__,
                                 formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("command", choices=["init", "lint", "pick", "start", "done", "demote", "defer", "promote", "timestamp"])
    ap.add_argument("args", nargs="*")
    ap.add_argument("--priority", choices=PRIORITIES)
    ap.add_argument("--dry-run", action="store_true")
    ap.add_argument("--check", action="store_true",
                    help="validate promote inputs and blockers without mutation")
    # Allow natural command syntax such as `promote --check file.md`; plain
    # parse_args cannot intermix an option after the command with nargs="*".
    ns = ap.parse_intermixed_args()
    if ns.check and ns.command != "promote":
        ap.error("--check is only valid with promote")

    if ns.command == "timestamp":
        now = datetime.now(timezone.utc)  # single instant — two now() calls can tear across a second boundary
        print(now.strftime("%Y%m%dT%H%M%S") + f"{now.microsecond // 1000:03d}")
        return 0

    init_actions = ensure_structure(ns.command)

    if ns.command == "init":
        result = {"ok": True, "backlog_root": str(BACKLOG_ROOT),
                  "actions": init_actions or ["already initialised"],
                  "lint": run_lint()}
    elif ns.command == "lint":
        result = run_lint()
    elif ns.command == "pick":
        result = run_pick()
    elif ns.command == "start":
        if len(ns.args) != 1:
            ap.error("start takes exactly one <NNN> (or task path)")
        result = run_start(ns.args[0], ns.dry_run)
    elif ns.command == "done":
        if len(ns.args) != 1:
            ap.error("done takes exactly one <NNN> (or task path)")
        result = run_done(ns.args[0], ns.dry_run)
    elif ns.command == "demote":
        if len(ns.args) != 1:
            ap.error("demote takes exactly one <NNN> (or task path)")
        result = run_demote(ns.args[0], ns.dry_run)
    elif ns.command == "defer":
        if len(ns.args) != 1:
            ap.error("defer takes exactly one <NNN> (or task path)")
        result = run_defer(ns.args[0], ns.dry_run)
    elif ns.command == "promote":
        if not ns.args:
            ap.error("promote takes one or more backlog/planning/*.md paths")
        result = run_promote(ns.args, ns.priority, ns.dry_run, ns.check)

    print(json.dumps(result, ensure_ascii=False, indent=2))
    if ns.command == "pick":
        return 2 if result.get("state") == "empty" else 0
    lint = result if ns.command == "lint" else result.get("lint")
    ok = result.get("ok", True) and (lint is None or lint["ok"])
    return 0 if ok else 1


if __name__ == "__main__":
    sys.exit(main())
