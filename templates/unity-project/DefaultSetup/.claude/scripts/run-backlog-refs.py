#!/usr/bin/env python3
"""Pick the run-backlog skill's conditional sections for one loop iteration.

The run-backlog skill keeps four sections that most iterations never need in sibling
files (`.claude/skills/run-backlog/ref-*.md`, table "Conditional sections" in its
SKILL.md). The loop controllers (run-backlog-loop.sh, run-backlog-loop-core.ps1) call
this before every iteration and inline what applies into the prompt, so the agent pays
for no section it does not need and never has to remember to read one it does.

The choice is deterministic and leans to inclusion — whatever cannot be established
selects the section anyway:
  - unknown pick state                           -> every file
  - `ship-pending` (after shipping, the agent    -> every file
    picks again, and that task is unknown here)
  - mode other than `current`                    -> the worktree file
  - unreadable task file                         -> both task-marker files
Markers are matched anywhere in the task file, HTML comments included: SKILL.md STEP 1
may ignore a commented one, but inlining a section the agent then skips costs a few KB,
while leaving out one it acts on would drop its rules from the agent's context.
A skill folder without all four ref files (a pre-split SKILL.md) is an error, so the
controller falls back to "read them all" instead of announcing sections that do not exist.

Usage:
  python3 .claude/scripts/run-backlog-refs.py --out <file> --mode current|worktree
      [--state todo|in-progress|ship-pending] [--task-path backlog/todo/NNN-T-slug.md]
      [--task-nnn NNN] [--backlog-root <dir>] [--skill-dir <dir>] [--recovery] [--all]

Writes the prompt block (UTF-8) to --out and prints `refs: <files>` (ASCII) on stdout.
Exit 0 = block written. Non-zero = no usable block (1: runtime error, --out removed; 2: bad
arguments): the controller then tells the agent to read every ref file itself, which is
exactly what a hand-run does.
"""
import argparse
import sys
from pathlib import Path

# Order = the order the sections appear in SKILL.md.
REFS = ("ref-resume.md", "ref-requires.md", "ref-worktree.md", "ref-workflow.md")
REQUIRES_MARK = "**Requires:**"
WORKFLOW_MARK = "**Backed by workflow:**"
DEFAULT_SKILL_DIR = Path(__file__).resolve().parent.parent / "skills" / "run-backlog"

INTRO = (
    "The run-backlog skill keeps four conditional sections in sibling files (SKILL.md, "
    "\"Conditional sections\"). The loop chose the ones that apply to this iteration; the "
    "full text of each follows (none when the list says none) and is already in your context, so do "
    "NOT Read them again. A file left "
    "out was judged not to apply: if its condition comes up anyway (SKILL.md says when), Read "
    "it before acting on it."
)


def task_markers(text):
    """(requires, workflow): a marker anywhere in the file counts (see the module docstring)."""
    return REQUIRES_MARK in text, WORKFLOW_MARK in text


def resolve_task_file(task_path, backlog_root):
    """`pick` reports paths as `backlog/<state>/<file>.md`, relative to the git common dir."""
    if not task_path:
        return None
    p = Path(task_path)
    if p.is_absolute():
        return p
    if not backlog_root:
        return None
    root = Path(backlog_root)
    if p.parts and p.parts[0] == "backlog":
        return root.parent / p
    return root / p


def select(mode, state, task_text, recovery, everything=False):
    """Return the ref files that apply, in SKILL.md order."""
    if everything or state not in ("todo", "in-progress"):
        return list(REFS)
    chosen = set()
    if state == "in-progress" or recovery:
        chosen.add("ref-resume.md")
    if mode != "current":
        chosen.add("ref-worktree.md")
    if task_text is None:
        chosen.update(("ref-requires.md", "ref-workflow.md"))
    else:
        requires, workflow = task_markers(task_text)
        if requires:
            chosen.add("ref-requires.md")
        if workflow:
            chosen.add("ref-workflow.md")
    return [r for r in REFS if r in chosen]


def render(selected, skill_dir, describe):
    parts = [
        "AGENT_SKILL_REFS=" + (",".join(selected) if selected else "none"),
        "AGENT_SKILL_REFS_FOR=" + describe,
        INTRO,
    ]
    for name in selected:
        text = (skill_dir / name).read_text(encoding="utf-8").rstrip("\n")
        parts.append("")
        parts.append("===== BEGIN .claude/skills/run-backlog/%s =====" % name)
        parts.append(text)
        parts.append("===== END %s =====" % name)
    return "\n".join(parts) + "\n"


def main(argv=None):
    ap = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    ap.add_argument("--out", required=True)
    ap.add_argument("--mode", default="")
    ap.add_argument("--state", default="")
    ap.add_argument("--task-path", default="")
    ap.add_argument("--task-nnn", default="")
    ap.add_argument("--backlog-root", default="")
    ap.add_argument("--skill-dir", default="")
    ap.add_argument("--recovery", action="store_true")
    ap.add_argument("--all", action="store_true")
    args = ap.parse_args(argv)

    out = Path(args.out)
    try:
        out.unlink()
    except OSError:
        pass
    try:
        mode = args.mode.strip().lower()
        state = args.state.strip().lower()
        skill_dir = Path(args.skill_dir) if args.skill_dir else DEFAULT_SKILL_DIR
        missing = [r for r in REFS if not (skill_dir / r).is_file()]
        if missing:
            raise RuntimeError("no %s in %s (pre-split run-backlog skill?)" % (", ".join(missing), skill_dir))
        task_text = None
        task_file = resolve_task_file(args.task_path.strip(), args.backlog_root.strip())
        if task_file is not None:
            try:
                task_text = task_file.read_text(encoding="utf-8")
            except (OSError, ValueError):  # missing, unreadable, or not UTF-8
                task_text = None
        selected = select(mode, state, task_text, args.recovery, args.all)
        describe = "task %s (%s), mode %s, recovery %s" % (
            args.task_nnn.strip() or "?", state or "unknown", mode or "unknown",
            "yes" if args.recovery else "no")
        block = render(selected, skill_dir, describe)
        out.parent.mkdir(parents=True, exist_ok=True)
        with open(out, "w", encoding="utf-8", newline="\n") as fh:
            fh.write(block)
    except Exception as exc:  # any failure -> no block; the controller falls back to "read them all"
        try:
            out.unlink()
        except OSError:
            pass
        print("refs: error: %s" % exc)
        return 1
    print("refs: " + (" ".join(selected) if selected else "none"))
    return 0


if __name__ == "__main__":
    sys.exit(main())
