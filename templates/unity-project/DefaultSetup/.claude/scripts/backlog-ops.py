#!/usr/bin/env python3
"""backlog-ops — deterministic bookkeeping for the split-file backlog.

Owns every mechanical backlog transition so the model never hand-edits
BACKLOG.md or invents timestamps (prose-driven bookkeeping has already
corrupted the index once: leaked tool-call markup, dual-state task files,
forbidden DONE bullets). Sibling of backlog-preflight.py.

Commands
  lint                        Directory<->index consistency check (read-only).
  pick                        Print the task run-backlog must work on (JSON).
  start   <NNN>               todo -> in-progress (git mv + index bullet move).
  done    <NNN>               in-progress -> done (git mv + bullet removal).
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
                              approved via /ui-mockup), or clone:<Prefab> does
                              not resolve through ui-catalog or to an
                              unambiguous prefab under Assets/.
  timestamp                   Print UTC YYYYMMDDTHHmmssSSS (planning filenames).

Every mutating command re-runs lint afterwards and embeds the result.
--dry-run prints the plan without touching anything.

Exit codes: 0 = ok · 1 = lint errors / operation failure · 2 = pick found nothing.
"""

import argparse
import json
import os
import re
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
# Mockup-pipeline pending markers inside **Workflow args:** (see /ui-mockup).
# PENDING-MOCKUP = no draft yet; PENDING-APPROVAL:<html> = draft awaits human OK.
PENDING_GROUNDTRUTH_RE = re.compile(r"groundTruth=(PENDING-MOCKUP|PENDING-APPROVAL:\S+)")
CLONE_GROUNDTRUTH_RE = re.compile(r"groundTruth=clone:([^\s`]+)")

STATE_DIRS = ("todo", "in-progress", "done")


def repo_root() -> Path:
    script_root = Path(__file__).resolve().parents[2]
    try:
        out = subprocess.run(
            ["git", "rev-parse", "--show-toplevel"],
            cwd=script_root, capture_output=True, text=True, check=True,
        ).stdout.strip()
        if out and (Path(out) / "BACKLOG.md").exists():
            return Path(out)
    except Exception:
        pass
    # git unavailable — walk up from this script (then cwd) looking for BACKLOG.md
    for base in (script_root, Path.cwd()):
        for p in (base, *base.parents):
            if (p / "BACKLOG.md").exists():
                return p
    return Path.cwd()


ROOT = repo_root()
BACKLOG = ROOT / "BACKLOG.md"


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
        p = ROOT / "backlog" / d
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


def git(*args, dry_run=False):
    if dry_run:
        return f"DRY-RUN: git {' '.join(args)}"
    subprocess.run(["git", *args], cwd=ROOT, check=True, capture_output=True, text=True)
    return f"git {' '.join(args)}"


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
            if not (ROOT / path).exists():
                errors.append(f"BACKLOG.md:{i + 1}: bullet target does not exist: {path}")
            else:
                task_path = ROOT / path
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
    planning_dir = ROOT / "backlog" / "planning"
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


def safe_git_mv(src_rel: str, dst_rel: str, dry_run: bool) -> str:
    try:
        return git("mv", src_rel, dst_rel, dry_run=dry_run)
    except subprocess.CalledProcessError as e:
        raise SystemExit(f"git mv {src_rel} -> {dst_rel} failed: {(e.stderr or '').strip()}")


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
    src = ROOT / m.group("path")
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

    actions = [safe_git_mv(str(src.relative_to(ROOT)), dst_rel, dry_run)]
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

    actions = [safe_git_mv(str(src.relative_to(ROOT)), dst_rel, dry_run)]
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

    actions = [safe_git_mv(str(src.relative_to(ROOT)), dst_rel, dry_run)]
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
    clone_mockup = None
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
                depends = [tok.strip().strip("`") for tok in d.group("deps").split(",")]
                depends = [t for t in depends if t and t.lower() not in ("none", "n/a", "-")]
        if pending_mockup is None:
            g = PENDING_GROUNDTRUTH_RE.search(stripped)
            if g:
                pending_mockup = g.group(1)
        if clone_mockup is None:
            clone = CLONE_GROUNDTRUTH_RE.search(stripped)
            if clone:
                clone_mockup = clone.group(1)
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
            "pending_mockup": pending_mockup, "clone_mockup": clone_mockup,
            "sort_key": (m.group("ts"), int(m.group("idx") or 0), path.name)}


def move_with_git(src: Path, dst_rel: str, dry_run: bool) -> str:
    """git mv, falling back to filesystem-move + git add when the source is
    untracked — planning drafts are often written but never git-added, and a
    plain `git mv` hard-fails on them ('not under version control'), which
    would dead-end an entire batch promote."""
    try:
        return git("mv", str(src.relative_to(ROOT)), dst_rel, dry_run=dry_run)
    except subprocess.CalledProcessError as e:
        stderr = e.stderr or ""
        if "not under version control" not in stderr:
            raise
        if dry_run:
            return f"DRY-RUN: mv {src.name} -> {dst_rel} + git add (untracked source)"
        src.rename(ROOT / dst_rel)
        git("add", dst_rel)
        return f"mv {src.name} -> {dst_rel} + git add (untracked source)"


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
    """A /new-ui task promoted while its groundTruth is still PENDING-* will
    execute without an approved visual reference — the exact failure mode the
    mockup pipeline exists to prevent. Clone fast lanes must also resolve to a
    real catalog token/prefab. Warnings are preflight blockers."""
    warns = []
    catalog_path = ROOT / "ui-catalog" / "ui-tokens.json"
    clone_targets = set()
    try:
        tokens = json.loads(catalog_path.read_text(encoding="utf-8")).get("tokens", [])
        for token in tokens:
            if not isinstance(token, dict):
                continue
            for key in ("token", "prefab", "resourcesPath", "assetPath"):
                value = token.get(key)
                if isinstance(value, str) and value:
                    clone_targets.add(value.lower())
                    if key == "assetPath":
                        clone_targets.add(Path(value).stem.lower())
    except (OSError, ValueError, json.JSONDecodeError):
        # A fresh project may not have exported a UI catalog yet. Keep clone
        # tasks usable when the requested prefab can be resolved unambiguously
        # from Assets; mockup/kit-composition tasks still require the catalog.
        assets = ROOT / "Assets"
        if assets.is_dir():
            prefabs = list(assets.rglob("*.prefab"))
            stem_counts = {}
            for prefab in prefabs:
                stem = prefab.stem.lower()
                stem_counts[stem] = stem_counts.get(stem, 0) + 1
                clone_targets.add(prefab.relative_to(ROOT).as_posix().lower())
            clone_targets.update(stem for stem, count in stem_counts.items() if count == 1)
    for t in tasks:
        marker = t.get("pending_mockup")
        if marker:
            warns.append(
                f"{t['src'].name}: groundTruth still {marker} — approve a mockup via "
                f"/ui-mockup before this task executes, or set "
                f"groundTruth=clone:<ExistingPrefab> if the screen only clones an "
                f"existing layout"
            )
            continue
        clone = t.get("clone_mockup")
        if clone and not clone_targets:
            warns.append(
                f"{t['src'].name}: cannot validate groundTruth=clone:{clone} because "
                f"ui-catalog/ui-tokens.json is missing or invalid and no matching "
                f"prefab was found under Assets"
            )
        elif clone and clone.lower() not in clone_targets:
            warns.append(
                f"{t['src'].name}: groundTruth=clone:{clone} does not resolve to a catalog "
                f"token/prefab — choose a real ui-catalog entry or use the mockup lane"
            )
    return warns


def run_promote(paths, priority_override, dry_run: bool, check_only=False) -> dict:
    tasks = []
    for p in paths:
        path = Path(p)
        if not path.is_absolute():
            path = ROOT / p
        if not path.exists():
            raise SystemExit(f"planning file not found: {p}")
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
            "tasks": [str(t["src"].relative_to(ROOT)) for t in tasks],
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
        if (ROOT / "backlog" / "todo" / dst_name).exists():
            dst_name = f"{nnn:03d}-{t['tier']}-{t['slug']}-2.md"
        dst_rel = f"backlog/todo/{dst_name}"
        try:
            actions.append(move_with_git(t["src"], dst_rel, dry_run))
        except subprocess.CalledProcessError as e:
            actions.append(f"FAILED git mv {t['src'].name}: {e.stderr.strip()}")
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
    ap.add_argument("command", choices=["lint", "pick", "start", "done", "demote", "defer", "promote", "timestamp"])
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

    if ns.command == "lint":
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
