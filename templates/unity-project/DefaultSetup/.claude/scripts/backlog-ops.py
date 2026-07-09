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
  promote <planning.md>...    planning -> todo (assign NNN, append bullets).
                              Optional --priority HIGH|MEDIUM|LOW for all files.
  timestamp                   Print UTC YYYYMMDDTHHmmssSSS (planning filenames).

Every mutating command re-runs lint afterwards and embeds the result.
--dry-run prints the plan without touching anything.

Exit codes: 0 = ok · 1 = lint errors / operation failure · 2 = pick found nothing.
"""

import argparse
import json
import re
import subprocess
import sys
from datetime import datetime, timezone
from pathlib import Path

PRIORITIES = ("HIGH", "MEDIUM", "LOW")
TIERS = ("XS", "S", "M", "L")

BULLET_RE = re.compile(
    r"^- \[(?P<priority>HIGH|MEDIUM|LOW)\] \[(?P<tier>XS|S|M|L)\] "
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
HEADING_RE = re.compile(r"^#{1,4} \[(?P<priority>HIGH|MEDIUM|LOW)\] (?P<title>.+?)\s*$")

STATE_DIRS = ("todo", "in-progress", "done")


def repo_root() -> Path:
    try:
        out = subprocess.run(
            ["git", "rev-parse", "--show-toplevel"],
            capture_output=True, text=True, check=True,
        ).stdout.strip()
        return Path(out)
    except Exception:
        cur = Path.cwd()
        for p in (cur, *cur.parents):
            if (p / "BACKLOG.md").exists():
                return p
        return cur


ROOT = repo_root()
BACKLOG = ROOT / "BACKLOG.md"


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
                    f"(need '- [PRIORITY] [TIER] [Title](backlog/{folder}/NNN-slug.md)'): "
                    f"{line.strip()[:80]!r}"
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
            linked[folder].add(Path(path).name)

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
            f"cannot extract NNN from {arg!r} (pass the 3+-digit task number or the NNN-slug.md filename)"
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
    new_bullet = f"- [{m.group('priority')}] [{m.group('tier')}] [{m.group('title')}]({dst_rel})"
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
        BACKLOG.write_text(idx.text(), encoding="utf-8")
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
        BACKLOG.write_text(idx.text(), encoding="utf-8")
    actions.append(f"BACKLOG.md: IN PROGRESS bullet {nnn} removed")
    return {"ok": True, "nnn": nnn, "path": dst_rel, "title": m.group("title"),
            "actions": actions, "lint": None if dry_run else run_lint(),
            "note": "write the completion summary into the done file yourself "
                    "(content is model work; only the transition is scripted)"}


def run_demote(arg: str, dry_run: bool) -> dict:
    """Abandon a blocked run: in-progress -> todo, bullet back to the HEAD of
    ## TODO (it was the queue head when picked, so it stays next in line)."""
    nnn, idx, i, m, src, dst_rel = prepare_transition("IN PROGRESS", "todo", arg)
    new_bullet = f"- [{m.group('priority')}] [{m.group('tier')}] [{m.group('title')}]({dst_rel})"
    idx.remove_line(i)
    if not idx.bullets("IN PROGRESS"):
        idx.insert_line(idx.section_tail("IN PROGRESS"), "- (none)")
    todo_bullets = idx.bullets("TODO")
    at = todo_bullets[0][0] if todo_bullets else idx.section_tail("TODO")
    idx.insert_line(at, new_bullet)

    actions = [safe_git_mv(str(src.relative_to(ROOT)), dst_rel, dry_run)]
    if not dry_run:
        BACKLOG.write_text(idx.text(), encoding="utf-8")
    actions.append(f"BACKLOG.md: IN PROGRESS bullet {nnn} -> head of TODO")
    return {"ok": True, "nnn": nnn, "path": dst_rel, "title": m.group("title"),
            "actions": actions, "lint": None if dry_run else run_lint()}


# --------------------------------------------------------------------------- promote
def parse_planning(path: Path) -> dict:
    m = PLANNING_RE.match(path.name)
    if not m:
        raise SystemExit(
            f"{path.name}: filename does not match "
            "'<timestamp>[-<index>]-<TIER>-<slug>.md'"
        )
    priority, title = "MEDIUM", m.group("slug").replace("-", " ")
    for line in path.read_text(encoding="utf-8").split("\n"):
        h = HEADING_RE.match(line)
        if h:
            priority, title = h.group("priority"), h.group("title")
            break
    return {"tier": m.group("tier"), "slug": m.group("slug"),
            "priority": priority, "title": title,
            "sort_key": (m.group("ts"), int(m.group("idx") or 0), path.name)}


def run_promote(paths, priority_override, dry_run: bool) -> dict:
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

    if priority_override:
        for t in tasks:
            t["priority"] = priority_override

    nnn = max_nnn()
    actions, bullets, moved = [], [], []
    for t in tasks:
        nnn += 1
        dst_name = f"{nnn:03d}-{t['slug']}.md"
        if (ROOT / "backlog" / "todo" / dst_name).exists():
            dst_name = f"{nnn:03d}-{t['slug']}-2.md"
        dst_rel = f"backlog/todo/{dst_name}"
        try:
            actions.append(git("mv", str(t["src"].relative_to(ROOT)), dst_rel, dry_run=dry_run))
        except subprocess.CalledProcessError as e:
            actions.append(f"FAILED git mv {t['src'].name}: {e.stderr.strip()}")
            continue
        bullets.append(f"- [{t['priority']}] [{t['tier']}] [{t['title']}]({dst_rel})")
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
        BACKLOG.write_text(idx.text(), encoding="utf-8")
        actions.append(f"BACKLOG.md: appended {len(bullets)} bullet(s) to ## TODO")

    return {"ok": all(not a.startswith("FAILED") for a in actions),
            "moved": moved, "actions": actions,
            "lint": None if dry_run else run_lint()}


# --------------------------------------------------------------------------- main
def main():
    ap = argparse.ArgumentParser(prog="backlog-ops.py", description=__doc__,
                                 formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("command", choices=["lint", "pick", "start", "done", "demote", "promote", "timestamp"])
    ap.add_argument("args", nargs="*")
    ap.add_argument("--priority", choices=PRIORITIES)
    ap.add_argument("--dry-run", action="store_true")
    ns = ap.parse_args()

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
    elif ns.command == "promote":
        if not ns.args:
            ap.error("promote takes one or more backlog/planning/*.md paths")
        result = run_promote(ns.args, ns.priority, ns.dry_run)

    print(json.dumps(result, ensure_ascii=False, indent=2))
    if ns.command == "pick":
        return 2 if result.get("state") == "empty" else 0
    lint = result if ns.command == "lint" else result.get("lint")
    ok = result.get("ok", True) and (lint is None or lint["ok"])
    return 0 if ok else 1


if __name__ == "__main__":
    sys.exit(main())
