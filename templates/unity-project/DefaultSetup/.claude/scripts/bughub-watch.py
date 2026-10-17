#!/usr/bin/env python3
"""bughub-watch.py — branch sync for the `/fix-bug --watch` window (macOS / Linux / Windows).

Two modes, chosen by `bugHub.watch.branch` in `.claude/project-profile.json`:

- **Follow the checked-out branch** (`branch` empty — default): whatever branch the watch runs on is the branch bugs
  are fixed and pushed on, right in that checkout. Before each bug it merges `origin/<branch>` so the fix is made on
  the latest code and the push is not rejected. If the dev switches the checkout's branch midway, the next bug
  follows the new branch.
- **Bot branch** (`branch` set, e.g. `AutoFixBug`): a dedicated branch for the bot. Before each bug it merges the
  latest main branch (`bugHub.watch.baseBranch`, empty = `defaultBaseBranch`); after the fix it pushes to the bot
  branch itself; it only lands on the main branch when `bugHub.watch.mergeToBase` is on. Whether it runs in this
  checkout or in a separate `git worktree` is decided by `bugHub.watch.worktree`.

A conflict while merging the main branch into the bot branch is NOT aborted: the script takes the main-branch version
for files that cannot be merged by hand (binary, serialized Unity assets, `.meta`, deleted on one side), leaves the
merge in progress with the remaining text files carrying (diff3-style) markers and returns `CONFLICT` — the skill
merges both sides, takes the main branch where it is forced to choose, then calls `resolve`.

The watch loop NEVER stops because of this script: transient states (network down, conflict, merge/rebase in
progress, wrong branch, git locked…) return `WAIT` + `reason`; the skill runs `pause` (30 seconds) and retries, with no
limit on attempts.

Commands:
  start [--yes] [--no-launch] [-- <claude args>]
        Prepare the directory (bot branch: create the worktree / switch branch; follow mode: keep the checkout as is),
        run `sync`, then launch `claude "/fix-bug --watch"` right in this terminal window and SUPERVISE it: claude
        exits with code ≠ 0 (crash, killed) → wait `BUGHUB_PAUSE_SECONDS` and relaunch, forever; exit code 0
        (`/exit`, Ctrl+C twice) or Ctrl+C while waiting to relaunch → stop. WATCHDOG: claude is alive but idle for
        more than `BUGHUB_WATCHDOG_MINUTES` minutes (default 20; 0 = off) — the session transcript has not grown and
        no loop command (`bughub-wait`, this script's commands) reported a heartbeat, i.e. a turn died on an API
        error with nobody to type on, or the model ended its turn without leaving anything waiting →
        stop claude (whole process tree) and launch a fresh session. Heartbeat: the file in env
        `BUGHUB_WATCH_HEARTBEAT` (a new file for each claude launch). `--yes`: do not ask again when this checkout's
        branch has to be switched. `--no-launch`: prepare only. Arguments after `--` are passed verbatim to claude
        (e.g. `-- --permission-mode bypassPermissions`).
  sync  BEFORE taking each bug: fetch + merge `origin/<branch>` (and the main branch in bot-branch mode).
        Bot branch: the directory belongs to the bot → before merging it cleans up like `clean` (`cleaned`) so junk
        written by Unity Editor (TMP font atlases, materials, plugin settings…) does not block merging the main
        branch. `OK` with `dirty` = tracked files with uncommitted edits (the bot must not touch them), `takenBase` =
        files for which the main-branch merge took the main-branch version | `CONFLICT` (main-branch merge in
        progress, `files` waiting for a hand merge) | `WAIT`.
  resolve [--prefer-base -- <file>...]
        (bot branch) Finish the in-progress main-branch merge after the skill merged by hand: `--prefer-base` for files
        the skill could not merge (non-overlapping parts keep both sides, overlapping hunks take the main branch — `git
        merge-file --theirs`); the other files must be marker-free → `git add` + merge commit → `MERGED`. Markers
        left → `CONFLICT` with `files`. No merge in progress → `NOTHING`.
  push  After a commit when `git_push` failed: merge `origin/<branch>` if the remote moved on, then push again
        (at most 3 attempts, never force).
  shelve --bug N --reason "<reason>" -- <file>...
        Bug released: put the unfinished fix (exactly the files the bot edited / created) into `git stash` so the
        directory is clean for the next bug without losing work — the dev gets it back with `git stash list` /
        `git stash apply`.
  publish
        (bot branch) Call after the fix is committed + pushed: mergeToBase=false (or follow mode) →
        `DISABLED`; true → move HEAD onto the main branch (fast-forward, merging the main branch first if it moved
        on — a conflict gives `CONFLICT` as in `sync`; never force).
  pause [--attempt K] [--seconds S]
        Sleep `--seconds` (default env `BUGHUB_PAUSE_SECONDS`, 30 seconds — same every time, no backoff, no
        attempt limit; `--attempt` is only logged) then print `RESUME` — the skill runs it in the background and
        ends its turn; the next turn retries the step that failed.
  config
        Print the merged config + current branch (read by the skill at preflight).
  editor status | open [--timeout S] | close [--wait S]
        The Unity Editor of this directory for bugs — compile-check is mandatory, so the bot does not fix without an
        Editor. `status`: {running, pid, owned}. `open`: no Editor open → open one (restart-unity --open-only) and
        record "opened by the bot" (owned); then wait until the Unity MCP plugin of that very process registers in
        the registry (Editor loaded + compiled) → `READY`. `close`: only closes an Editor the bot opened — the skill
        has already checked every `retry` bug, there are no unsaved changes, and it asked the Editor to quit via MCP;
        this command waits for the process to exit and kills it after `--wait` seconds (`CLOSED` / `KILLED`); an
        Editor the bot did not open → `NOT_OWNED`, left alone.
  notice check|mark --reason KEY -- N... | notice clear
        Prevents repeated `bughub_note` posts while the loop pauses because the Editor is unusable: `check` returns
        the bugs NOT yet notified with reason KEY, `mark` records them as notified, `clear` (Editor usable again)
        resets so the next failure notifies again.
  claim set --bug N | claim clear [--bug N] | claim get
        Remember the bug CURRENTLY HELD (claimed, not yet resolve / needs-info / block / release) so a later session
        can continue if this one dies midway (API error, crash, watchdog relaunch), plus a claim-time baseline for
        `discard`: HEAD and the paths differing from HEAD (the dev's unfinished changes — `discard` never touches
        them). `get` → `NONE` or `CLAIMED` with `commit` (the `Bug-Report: #N` commit made AFTER the claim, on HEAD)
        + `pushed`.
  discard --bug N [--auto] [-- <file>...]
        Someone pressed "Huỷ & hoàn tác" (cancel & revert) on the bug (or someone else re-claimed it) while the bot
        was fixing it: return the directory to its state at claim time. (1) Stash the files the bot edited / created
        (the list after `--`; `--auto` = add every path now differing from HEAD that did not differ at claim time — for
        when the previous session died and what it edited is unknown) into `git stash` "bughub #N cancelled" — the
        directory gets clean without losing anything; the dev's unfinished files at claim time are never stashed.
        (2) This claim's `Bug-Report: #N` commit NOT yet pushed: on top of HEAD → drop it (`git reset --keep`),
        otherwise `git revert` locally. Already pushed: the BugHub server reverted it on GitLab — the next `sync`
        pulls that in. (3) Delete `Temp/BugHub/N`. (4) Delete the bug's `claim` marker. → `DISCARDED` | `CLEAN`
        (nothing to clean up).
  clean [--bug N]
        AFTER each bug (finalized, committed + pushed) — reset the directory to HEAD before taking the next bug: every
        path differing from HEAD (tracked modified / deleted / staged + untracked not ignored) that is not in the
        bug's commit, mostly files Unity Editor writes on its own. Bot branch (directory dedicated to the bot): clean
        everything. Checkout shared with the dev (follow mode / one-shot mode): keep the paths that already differed
        from HEAD when bug N was claimed (the `claim set` baseline); without a baseline nothing is cleaned
        (`SKIPPED`). Nothing is lost: it is stashed into a stash commit kept at ref
        `refs/bughub/cleaned/<time>-<tag>` (not in `git stash list`; the newest `CLEAN_KEEP` are kept) —
        restore: `git stash apply <backup>`. → `CLEANED` (`count`, `backup`, `ref`, `left` = paths still differing
        from HEAD after cleaning) | `CLEAN` | `SKIPPED`; merge / rebase in progress → `WAIT` `BUSY`.

The last stdout line is always exactly one JSON `{"result": "...", ...}`; human-readable logs go to stderr.
Exit: 0 done (including `DISABLED`, `UP_TO_DATE`, `NOTHING`, `CLEAN`, `SKIPPED`) · 2 bad config / arguments / `start`
could not prepare the directory · 3 `WAIT` (transient — pause and retry; `reason`: DETACHED, WRONG_BRANCH, BUSY,
FETCH_FAILED, MERGE_CONFLICT, LOCAL_CHANGES, DIVERGED_DIRTY, PUSH_REJECTED, GIT_FAILED; `editor`: EDITOR_NOT_INSTALLED,
EDITOR_BUSY, EDITOR_LAUNCH_FAILED, EDITOR_EXITED, EDITOR_NOT_READY, EDITOR_STATUS_FAILED, EDITOR_KILL_FAILED) · 4
uncommitted changes block merging the main branch during `publish` (`LOCAL_CHANGES`, merge not started) · 5 push
rejected during `publish` · 6 `CONFLICT` (main-branch merge into the bot branch in progress, waiting for the skill to
merge by hand and then `resolve`).
"""

from __future__ import annotations

import json
import os
import re
import shutil
import signal
import subprocess
import sys
import tempfile
import time
from datetime import datetime, timezone
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))
from project_profile import profile  # noqa: E402

EXIT_CONFIG = 2
EXIT_WAIT = 3
EXIT_CONFLICT = 4
EXIT_NETWORK = 5
EXIT_MERGE = 6

WATCH_PROMPT = "/fix-bug --watch"
# Seconds between fetch retries within ONE command. Short on purpose: a longer outage returns WAIT so the
# loop pauses with backoff (the command runs in the foreground of the Bash tool, default timeout 2 minutes).
FETCH_RETRY_DELAYS = (5, 20)
PUSH_ATTEMPTS = 3
# Every pause / claude relaunch sleeps exactly this long (env BUGHUB_PAUSE_SECONDS) — no backoff, no limit.
PAUSE_SECONDS = 30
MERGE_STATE = "merge.json"
# Conflict marker line prefixes (merge.conflictStyle=diff3). A lone "=======" does not count — Markdown uses it as an underline.
CONFLICT_MARKERS = (b"<<<<<<<", b"|||||||", b">>>>>>>")
# Never let git block on a password prompt in an unattended loop.
GIT_ENV = dict(os.environ, GIT_TERMINAL_PROMPT="0")
# Wait for the Editor to be ready within ONE command: fits the Bash tool's 10-minute max timeout; longer → WAIT → pause → retry.
EDITOR_READY_TIMEOUT = 540
EDITOR_CLOSE_WAIT = 60
EDITOR_POLL_SECONDS = 5
# A Unity MCP registry entry older than this belongs to a dead Editor (= the MCP's default registryStalenessTimeoutMs).
REGISTRY_FRESH_SECONDS = 300
RESTART_UNITY_DIR = Path(__file__).resolve().parent.parent / "skills" / "restart-unity" / "scripts"
CLAIM_STATE = "claim.json"
# Claim-time baseline of "paths differing from HEAD": above this many paths it is not stored (discard --auto refuses, files must be listed).
SNAPSHOT_MAX = 2000
# Claim-time baseline for `clean` — a separate file that survives `claim clear` / `discard` (both delete CLAIM_STATE).
BASELINE_STATE = "baseline.json"
# `clean` backups: one ref under this prefix per run, keeping this many newest (older refs are deleted, objects left to `git gc`).
CLEAN_REF = "refs/bughub/cleaned"
CLEAN_KEEP = 30
# `start` sets this env for the claude it launches: every loop command touches that file so the watchdog knows the loop is alive.
HEARTBEAT_ENV = "BUGHUB_WATCH_HEARTBEAT"
# SESSION variables Claude Code sets for its shell. If `start` is called from inside a Claude session (an agent opening
# the watch window for the dev) and the child claude inherits them, the child treats itself as a sub-session of that
# session: it writes no transcript of its own (the watchdog goes blind — it thinks claude is idle while it is fixing a
# bug) and attaches to the parent session's messaging socket. Only dropped when really inside a Claude session
# (CLAUDECODE set); the user's config variables (CLAUDE_CONFIG_DIR…) are kept.
PARENT_SESSION_ENV = ("CLAUDECODE", "CLAUDE_PID", "CLAUDE_EFFORT", "CLAUDE_CODE_CHILD_SESSION",
                      "CLAUDE_CODE_SESSION_ID", "CLAUDE_CODE_SESSION_ATTENDED", "CLAUDE_CODE_ENTRYPOINT",
                      "CLAUDE_CODE_EXECPATH", "CLAUDE_CODE_MESSAGING_SOCKET", "CLAUDE_CODE_MESSAGING_TOKEN",
                      "CLAUDE_CODE_SSE_PORT")
# Claude idle longer than this (minutes, env BUGHUB_WATCHDOG_MINUTES; 0 = off) → `start`'s watchdog relaunches it.
# Must be longer than any normal stretch of work that writes no transcript (a long command / a long thinking turn).
WATCHDOG_MINUTES = 20
WATCHDOG_POLL_SECONDS = 15


class Stop(Exception):
    """Stop the command with an exit code + result JSON."""

    def __init__(self, code: int, result: str, message: str, **extra):
        super().__init__(message)
        self.code = code
        self.payload = {"result": result, "message": message, **extra}


def wait(reason: str, message: str, **extra) -> Stop:
    """Transient state — the skill pauses and retries; the loop does not stop."""
    return Stop(EXIT_WAIT, "WAIT", message, reason=reason, **extra)


def heartbeat() -> None:
    """Tell `start`'s watchdog the loop is alive (waiting / working). Not running under `start` → no-op."""
    path = os.environ.get(HEARTBEAT_ENV)
    if path:
        try:
            Path(path).touch()
        except OSError:
            pass


# --------------------------------------------------------------------------------------------------
# git
# --------------------------------------------------------------------------------------------------

def git(*args: str, cwd: Path | None = None, check: bool = True, stdin: str | None = None,
        literal: bool = False) -> subprocess.CompletedProcess:
    # literal: pathspecs are verbatim file names — Unity paths often contain `[`/`*` (e.g. "Azure[Sky] Lite") that would be read as globs.
    env = dict(GIT_ENV, GIT_LITERAL_PATHSPECS="1") if literal else GIT_ENV
    proc = subprocess.run(["git", *args], cwd=cwd, capture_output=True, text=True, encoding="utf-8",
                          errors="replace", env=env, input=stdin)
    if check and proc.returncode != 0:
        raise wait("GIT_FAILED", f"git {' '.join(args)}: {proc.stderr.strip() or proc.stdout.strip()}")
    return proc


def out(*args: str, cwd: Path | None = None) -> str:
    return git(*args, cwd=cwd).stdout.strip()


def ok(*args: str, cwd: Path | None = None) -> bool:
    return git(*args, cwd=cwd, check=False).returncode == 0


def ref_exists(ref: str, cwd: Path) -> bool:
    return ok("rev-parse", "--verify", "--quiet", f"{ref}^{{commit}}", cwd=cwd)


def is_ancestor(ancestor: str, ref: str, cwd: Path) -> bool:
    return ok("merge-base", "--is-ancestor", ancestor, ref, cwd=cwd)


def has_origin(cwd: Path) -> bool:
    return ok("remote", "get-url", "origin", cwd=cwd)


def current_branch(cwd: Path) -> str:
    proc = git("symbolic-ref", "--quiet", "--short", "HEAD", cwd=cwd, check=False)
    return proc.stdout.strip() if proc.returncode == 0 else ""


def fetch(cwd: Path) -> None:
    """fetch origin; on failure retry for a few seconds, then return WAIT (the loop pauses with backoff, never stops)."""
    for delay in (*FETCH_RETRY_DELAYS, None):
        proc = git("fetch", "--quiet", "--prune", "origin", cwd=cwd, check=False)
        if proc.returncode == 0:
            return
        if delay is None:
            raise wait("FETCH_FAILED", f"git fetch origin failed: {proc.stderr.strip()}")
        print(f"bughub-watch: fetch failed, retrying in {delay}s — {proc.stderr.strip()}", file=sys.stderr)
        time.sleep(delay)
        heartbeat()


def worktrees(cwd: Path) -> list[dict]:
    """`git worktree list --porcelain` → [{path, branch}]."""
    items, item = [], {}
    for line in out("worktree", "list", "--porcelain", cwd=cwd).splitlines() + [""]:
        if not line:
            if item:
                items.append(item)
            item = {}
        elif line.startswith("worktree "):
            item["path"] = line[len("worktree "):]
        elif line.startswith("branch refs/heads/"):
            item["branch"] = line[len("branch refs/heads/"):]
    return items


def checked_out_at(branch: str, cwd: Path) -> str | None:
    for item in worktrees(cwd):
        if item.get("branch") == branch:
            return item["path"]
    return None


def dirty_tracked(cwd: Path) -> list[str]:
    """Tracked files with uncommitted changes (untracked ignored — the skill's commit only takes the bug's files)."""
    # Not via out(): strip() eats the leading status column (" M a.txt"), shifting file names by 1 character.
    lines = git("status", "--porcelain", "--untracked-files=no", cwd=cwd).stdout.splitlines()
    return [line[3:] for line in lines if line.strip()]


def git_path_exists(name: str, cwd: Path) -> bool:
    path = Path(out("rev-parse", "--git-path", name, cwd=cwd))
    return (path if path.is_absolute() else cwd / path).exists()


def operation_in_progress(cwd: Path) -> str:
    """Name of the git operation in progress (merge / rebase / cherry-pick / revert) — empty if none."""
    for name, label in (("MERGE_HEAD", "merge"), ("rebase-merge", "rebase"), ("rebase-apply", "rebase"),
                        ("CHERRY_PICK_HEAD", "cherry-pick"), ("REVERT_HEAD", "revert")):
        if git_path_exists(name, cwd):
            return label
    return ""


def try_merge(ref: str, cwd: Path, ff_only: bool = False) -> dict | None:
    """Merge `ref` into HEAD. Done → None. Not done (conflict / blocked by uncommitted changes) → abort, return details."""
    args = ["merge", "--ff-only", ref] if ff_only else ["merge", "--no-edit", ref]
    proc = git(*args, cwd=cwd, check=False)
    if proc.returncode == 0:
        return None
    conflicted = out("diff", "--name-only", "--diff-filter=U", cwd=cwd).splitlines()
    if git_path_exists("MERGE_HEAD", cwd):
        git("merge", "--abort", cwd=cwd, check=False)
    detail = proc.stderr.strip() or proc.stdout.strip()
    # "would be overwritten by merge" lists the blocking files on tab-indented lines.
    blocking = [line.strip() for line in detail.splitlines() if line.startswith("\t")]
    return {"ref": ref, "detail": detail, "files": (conflicted or blocking)[:20],
            "kind": "conflict" if conflicted else "blocked"}


# --------------------------------------------------------------------------------------------------
# merging the main branch into the bot branch — conflict: merge both sides, take the main branch when forced to choose
# --------------------------------------------------------------------------------------------------

def unmerged(cwd: Path) -> dict[str, dict[int, str]]:
    """Conflicted path → {stage: blob} (1 = common ancestor, 2 = bot branch / HEAD, 3 = main branch being merged in)."""
    items: dict[str, dict[int, str]] = {}
    for entry in git("ls-files", "-u", "-z", cwd=cwd).stdout.split("\0"):
        if entry:
            meta, path = entry.split("\t", 1)
            _mode, sha, stage = meta.split()
            items.setdefault(path, {})[int(stage)] = sha
    return items


def blob(sha: str, cwd: Path) -> bytes:
    proc = subprocess.run(["git", "cat-file", "blob", sha], cwd=cwd, capture_output=True, env=GIT_ENV)
    if proc.returncode != 0:
        raise wait("GIT_FAILED", f"git cat-file blob {sha}: {proc.stderr.decode('utf-8', 'replace').strip()}")
    return proc.stdout


def hand_mergeable(path: str, stages: dict[int, str], cwd: Path) -> bool:
    """A text file edited on both sides — can be merged by hand. Binaries, serialized Unity assets (YAML fileID) and
    `.meta` cannot: mixing hunks from both sides easily yields dangling references / mismatched GUIDs, so the
    main-branch version is taken as is."""
    if 2 not in stages or 3 not in stages or path.endswith(".meta"):
        return False
    for stage in (2, 3):
        head = blob(stages[stage], cwd)[:8000]
        if b"\0" in head or (head.startswith(b"%YAML") and b"tag:unity3d.com" in head[:200]):
            return False
    return True


def take_base(path: str, stages: dict[int, str], cwd: Path) -> bool:
    """Resolve the conflict with the main-branch version: present (stage 3) → take it; deleted on the main branch →
    delete; only the bot branch has it (the main branch never had this path) → keep. Returns True when the
    main-branch version overrides the bot's change."""
    if 3 in stages:
        git("checkout", "--theirs", "--", path, cwd=cwd)
        git("add", "-A", "--", path, cwd=cwd)
        return True
    if 1 in stages:
        git("rm", "-q", "-f", "--", path, cwd=cwd)
        return True
    git("checkout", "--ours", "--", path, cwd=cwd)
    git("add", "-A", "--", path, cwd=cwd)
    return False


def prefer_base(path: str, stages: dict[int, str], cwd: Path) -> None:
    """Re-merge the file from its 3 versions: non-overlapping parts keep both sides, overlapping hunks take the main branch."""
    if not hand_mergeable(path, stages, cwd):
        take_base(path, stages, cwd)
        return
    with tempfile.TemporaryDirectory(prefix="bughub-merge-") as tmp:
        files = []
        for stage, name in ((2, "bot"), (1, "common"), (3, "base")):
            file = Path(tmp) / name
            file.write_bytes(blob(stages[stage], cwd) if stage in stages else b"")
            files.append(str(file))
        proc = subprocess.run(["git", "merge-file", "-p", "--theirs", *files], cwd=cwd, capture_output=True, env=GIT_ENV)
    if proc.returncode != 0:
        print(f"bughub-watch: merge-file {path} failed — taking the main-branch version as is", file=sys.stderr)
        take_base(path, stages, cwd)
        return
    (cwd / path).write_bytes(proc.stdout)
    git("add", "-A", "--", path, cwd=cwd)


def has_markers(path: Path) -> bool:
    try:
        data = path.read_bytes()
    except OSError:
        return False  # the skill deleted the file → `git add -A` records the deletion
    return any(line.startswith(CONFLICT_MARKERS) for line in data.splitlines())


def bot_merge(cwd: Path) -> dict | None:
    """The main-branch merge this script started that is still in progress (MERGE_HEAD matches the state). Stale state
    (merge finished / dev aborted) → deleted."""
    path = state_file(cwd, MERGE_STATE)
    state = read_json(path)
    if not state:
        return None
    if git_path_exists("MERGE_HEAD", cwd) and out("rev-parse", "MERGE_HEAD", cwd=cwd) == state.get("theirs"):
        return state
    path.unlink(missing_ok=True)
    return None


def commit_merge(state: dict, cwd: Path) -> dict:
    git("commit", "--no-edit", "-q", cwd=cwd)
    state_file(cwd, MERGE_STATE).unlink(missing_ok=True)
    return {"ref": state["ref"], "op": state.get("op"), "head": out("rev-parse", "HEAD", cwd=cwd),
            "resolved": state.get("resolved", []), "preferBase": state.get("preferBase", []),
            "takenBase": state.get("takenBase", [])}


def conflict(state: dict, files: list[str]) -> Stop:
    return Stop(EXIT_MERGE, "CONFLICT",
                f"merge of {state['ref']} into the bot branch still has conflicts in {len(files)} file(s) — merge by hand (fix-bug section 3.6), then run `resolve`",
                ref=state["ref"], op=state.get("op"), files=files, takenBase=state.get("takenBase", []))


def resume_bot_merge(cwd: Path) -> dict | None:
    """The bot's main-branch merge left in progress by an earlier turn: conflicted files left → CONFLICT; none → commit it."""
    state = bot_merge(cwd)
    if not state:
        return None
    left = sorted(unmerged(cwd))
    if left:
        raise conflict(state, left)
    return commit_merge(state, cwd)


def merge_base(ref: str, cwd: Path, op: str) -> dict:
    """Merge the main branch `ref` into the bot branch. Done (including when the main-branch version was taken for
    files that cannot be merged by hand) → {"ref", "takenBase", …}. Uncommitted changes block the merge (merge not
    started) → {"blocked": {...}}. Text files still conflicted → leave the merge in progress, write the state, raise
    CONFLICT."""
    proc = git("-c", "merge.conflictStyle=diff3", "merge", "--no-edit", ref, cwd=cwd, check=False)
    if proc.returncode == 0:
        return {"ref": ref, "takenBase": []}
    detail = proc.stderr.strip() or proc.stdout.strip()
    if not git_path_exists("MERGE_HEAD", cwd):
        # "would be overwritten by merge" lists the blocking files on tab-indented lines.
        blocking = [line.strip() for line in detail.splitlines() if line.startswith("\t")]
        return {"blocked": {"ref": ref, "detail": detail, "files": blocking[:20], "kind": "blocked"}}
    taken = [path for path, stages in unmerged(cwd).items()
             if not hand_mergeable(path, stages, cwd) and take_base(path, stages, cwd)]
    state = {"ref": ref, "theirs": out("rev-parse", "MERGE_HEAD", cwd=cwd), "op": op, "takenBase": sorted(taken),
             "resolved": [], "preferBase": [], "startedAt": datetime.now(timezone.utc).isoformat()}
    left = sorted(unmerged(cwd))
    if not left:
        return commit_merge(state, cwd)
    write_json(state_file(cwd, MERGE_STATE), state)
    print(f"bughub-watch: merge {ref}: conflict in {len(left)} text file(s) — waiting for a hand merge", file=sys.stderr)
    raise conflict(state, left)


def merge_base_or_stop(ref: str, cwd: Path, op: str) -> dict:
    outcome = merge_base(ref, cwd, op)
    if "blocked" in outcome:
        blocked = outcome["blocked"]
        raise Stop(EXIT_CONFLICT, "LOCAL_CHANGES", f"uncommitted changes block merging {ref} — {blocked['detail']}",
                   ref=ref, files=blocked["files"])
    return outcome


# --------------------------------------------------------------------------------------------------
# config
# --------------------------------------------------------------------------------------------------

def repo_root(cwd: Path) -> Path:
    return Path(out("rev-parse", "--show-toplevel", cwd=cwd)).resolve()


def main_checkout(cwd: Path) -> Path:
    """The clone's main checkout directory (first worktree in the list)."""
    return Path(worktrees(cwd)[0]["path"]).resolve()


def load_config(cwd: Path) -> dict:
    p = profile()
    watch = p.bug_hub_watch
    branch = (watch.get("branch") or "").strip()
    base = (watch.get("baseBranch") or "").strip() or p.default_base_branch
    cfg = {
        "projectCode": p.bug_hub_project_code,
        "mode": "bot" if branch else "follow",
        "branch": branch,
        "baseBranch": base,
        "mergeToBase": bool(watch.get("mergeToBase")),
        "worktree": bool(watch.get("worktree")),
        "worktreePath": "",
    }
    if branch:
        for name, value in (("branch", branch), ("baseBranch", base)):
            if not ok("check-ref-format", "--branch", value, cwd=cwd):
                raise Stop(EXIT_CONFIG, "BAD_CONFIG", f"bugHub.watch.{name} is not a valid branch name: '{value}'")
        if branch == base:
            raise Stop(EXIT_CONFIG, "BAD_CONFIG", "bugHub.watch.branch must differ from the main branch — the bot never commits straight to the main branch")
        if cfg["worktree"]:
            custom = (watch.get("worktreePath") or "").strip()
            main = main_checkout(cwd)
            path = Path(custom).expanduser() if custom else main.parent / f"{main.name}-{branch.replace('/', '-')}"
            if not path.is_absolute():
                path = main / path
            cfg["worktreePath"] = str(path.resolve())
    return cfg


def base_ref(cfg: dict, cwd: Path, remote: bool) -> str:
    base = cfg["baseBranch"]
    if remote and ref_exists(f"refs/remotes/origin/{base}", cwd):
        return f"origin/{base}"
    if ref_exists(f"refs/heads/{base}", cwd):
        return base
    raise Stop(EXIT_CONFIG, "BASE_UNKNOWN", f"main branch '{base}' not found (neither local nor on origin) — check bugHub.watch.baseBranch")


# --------------------------------------------------------------------------------------------------
# sync / push / shelve / publish
# --------------------------------------------------------------------------------------------------

def check_ready(cfg: dict, cwd: Path, allow_bot_merge: bool = False) -> str:
    """The branch the bot will commit to; a state it cannot work in yet (detached, wrong branch, merge in progress) → WAIT.
    `allow_bot_merge`: a main-branch merge started by this script and still in progress does not count as busy
    (sync / publish carry it on)."""
    branch = current_branch(cwd)
    if not branch:
        raise wait("DETACHED", "HEAD is detached — waiting for a branch to be checked out")
    if cfg["mode"] == "bot" and branch != cfg["branch"]:
        raise wait("WRONG_BRANCH",
                   f"on '{branch}', not the bot branch '{cfg['branch']}' — open the window with `python3 .claude/scripts/bughub-watch.py start`",
                   branch=branch)
    busy = operation_in_progress(cwd)
    if busy and not (allow_bot_merge and busy == "merge" and bot_merge(cwd)):
        raise wait("BUSY", f"{busy} in progress in {cwd} — waiting for the dev to finish it", branch=branch)
    return branch


def integrate_own(branch: str, cwd: Path) -> str | None:
    """Bring `origin/<branch>` into HEAD so the push can go through. Returns the merged ref (None if not needed); cannot → WAIT."""
    own = f"origin/{branch}"
    if not ref_exists(f"refs/remotes/{own}", cwd) or is_ancestor(own, "HEAD", cwd):
        return None
    fast_forward = is_ancestor("HEAD", own, cwd)
    if not fast_forward and dirty_tracked(cwd):
        # A real merge that conflicts could have `merge --abort` damage the dev's uncommitted changes — wait for the dev to commit/push.
        raise wait("DIVERGED_DIRTY", f"'{branch}' has unpushed local commits while {own} has moved on, and the directory "
                   "has uncommitted changes — waiting for the dev to push / commit, then retrying", ref=own)
    failed = try_merge(own, cwd, ff_only=fast_forward)
    if failed:
        reason = "MERGE_CONFLICT" if failed["kind"] == "conflict" else "LOCAL_CHANGES"
        raise wait(reason, f"could not merge {own} into '{branch}' (the push would be rejected) — waiting for the dev. {failed['detail']}",
                   ref=own, files=failed["files"])
    return own


def do_sync(cfg: dict, cwd: Path) -> dict:
    branch = check_ready(cfg, cwd, allow_bot_merge=True)
    merged, skipped, taken = [], [], []
    resumed = resume_bot_merge(cwd)
    if resumed:
        merged.append(resumed["ref"])
        taken += resumed["takenBase"]
    cleaned = None
    if cfg["mode"] == "bot" and not operation_in_progress(cwd):
        # The bot branch's directory holds nobody's unfinished work: uncommitted changes here are only Editor junk /
        # leftovers of an earlier session — left in place they block the main-branch merge ("would be overwritten")
        # and the bot keeps fixing on stale code.
        cleaned = clean_tree(cwd, set(), "bughub sync cleanup", "sync")
        if cleaned["backup"]:
            print(f"bughub-watch: cleaned {cleaned['count']} uncommitted path(s) before merging — backup {cleaned['backup'][:10]}",
                  file=sys.stderr)
    remote = has_origin(cwd)
    if remote:
        fetch(cwd)
        own = integrate_own(branch, cwd)
        if own:
            merged.append(own)
    if cfg["mode"] == "bot":
        base = base_ref(cfg, cwd, remote)
        if not is_ancestor(base, "HEAD", cwd):
            outcome = merge_base(base, cwd, "sync")
            if "blocked" in outcome:
                # Uncommitted changes block the merge — the bot branch can still push (fixing on code older than the main branch); the loop is not blocked.
                print(f"bughub-watch: skipping merge of {base} — {outcome['blocked']['detail']}", file=sys.stderr)
                skipped.append(outcome["blocked"])
            else:
                merged.append(base)
                taken += outcome["takenBase"]
    ahead = 0
    if remote and ref_exists(f"refs/remotes/origin/{branch}", cwd):
        ahead = int(out("rev-list", "--count", f"origin/{branch}..HEAD", cwd=cwd))
    return {"result": "OK", "mode": cfg["mode"], "branch": branch, "merged": merged, "skipped": skipped,
            "takenBase": taken, "cleaned": cleaned, "dirty": dirty_tracked(cwd)[:50], "ahead": ahead,
            "head": out("rev-parse", "HEAD", cwd=cwd), "remote": remote}


def do_push(cfg: dict, cwd: Path) -> dict:
    branch = check_ready(cfg, cwd)
    if not has_origin(cwd):
        raise Stop(EXIT_CONFIG, "NO_ORIGIN", "repo has no origin remote")
    upstream = ok("rev-parse", "--abbrev-ref", "--symbolic-full-name", f"{branch}@{{upstream}}", cwd=cwd)
    last = ""
    for attempt in range(1, PUSH_ATTEMPTS + 1):
        fetch(cwd)
        own = f"origin/{branch}"
        if ref_exists(f"refs/remotes/{own}", cwd) and is_ancestor("HEAD", own, cwd):
            return {"result": "UP_TO_DATE", "branch": branch, "head": out("rev-parse", "HEAD", cwd=cwd)}
        merged = integrate_own(branch, cwd)
        args = ["push", "--quiet", *([] if upstream else ["-u"]), "origin", f"refs/heads/{branch}:refs/heads/{branch}"]
        proc = git(*args, cwd=cwd, check=False)
        if proc.returncode == 0:
            return {"result": "PUSHED", "branch": branch, "merged": [merged] if merged else [],
                    "head": out("rev-parse", "HEAD", cwd=cwd)}
        last = proc.stderr.strip()
        print(f"bughub-watch: push attempt {attempt} failed — {last}", file=sys.stderr)
    raise wait("PUSH_REJECTED", f"push of '{branch}' failed after {PUSH_ATTEMPTS} attempts (no force): {last}", branch=branch)


def parse_shelve_args(argv: list[str]) -> tuple[str, str, list[str]]:
    if "--" not in argv:
        raise Stop(EXIT_CONFIG, "BAD_ARGS", "shelve needs `-- <file>...`")
    head, files = argv[:argv.index("--")], argv[argv.index("--") + 1:]
    opts = {}
    it = iter(head)
    for arg in it:
        if arg not in ("--bug", "--reason"):
            raise Stop(EXIT_CONFIG, "BAD_ARGS", f"unknown argument: {arg}")
        opts[arg] = next(it, "")
    bug, reason = opts.get("--bug", ""), " ".join(opts.get("--reason", "").split())
    if not re.fullmatch(r"[1-9][0-9]*", bug):
        raise Stop(EXIT_CONFIG, "BAD_ARGS", "--bug must be a positive integer bug number")
    if not files:
        raise Stop(EXIT_CONFIG, "BAD_ARGS", "shelve needs at least one file")
    return bug, reason[:120], files


def do_shelve(cwd: Path, argv: list[str]) -> dict:
    bug, reason, files = parse_shelve_args(argv)
    root = repo_root(cwd)
    paths = []
    for name in files:
        path = (root / name).resolve()
        if root not in path.parents or ".git" in path.relative_to(root).parts:
            raise Stop(EXIT_CONFIG, "BAD_ARGS", f"file outside the repo: {name}")
        paths.append(path.relative_to(root).as_posix())
    # Only stash paths that really changed (tracked or newly created) — a pathspec matching nothing makes stash fail.
    status = git("status", "--porcelain", "-z", "--untracked-files=all", "--", *paths, cwd=root).stdout.split("\0")
    changed, skip_next = [], False
    for entry in status:
        if skip_next or not entry:
            skip_next = False
            continue
        changed.append(entry[3:])
        skip_next = entry[0] in "RC"  # rename/copy: the next entry is the old path
    if not changed:
        return {"result": "NOTHING", "bug": int(bug), "message": "no changes left to stash"}
    message = f"bughub #{bug} released" + (f": {reason}" if reason else "")
    git("stash", "push", "--include-untracked", "-m", message, "--", *changed, cwd=root)
    return {"result": "SHELVED", "bug": int(bug), "stash": out("rev-parse", "stash@{0}", cwd=root),
            "files": changed, "message": f"stashed into git stash \"{message}\" — restore: git stash apply <stash>"}


def do_resolve(cwd: Path, argv: list[str]) -> dict:
    prefer: list[str] = []
    if argv:
        if argv[:2] != ["--prefer-base", "--"] or len(argv) < 3:
            raise Stop(EXIT_CONFIG, "BAD_ARGS", "resolve [--prefer-base -- <file>...]")
        prefer = [name.removeprefix("./") for name in argv[2:]]
    state = bot_merge(cwd)
    if not state:
        return {"result": "NOTHING", "message": "no main-branch merge of the bot in progress"}
    pending = unmerged(cwd)
    unknown = [name for name in prefer if name not in pending]
    if unknown:
        raise Stop(EXIT_CONFIG, "BAD_ARGS", f"not a conflicted file: {', '.join(unknown)}", files=sorted(pending))
    for name in prefer:
        prefer_base(name, pending.pop(name), cwd)
    state["preferBase"] = sorted(set(state.get("preferBase", []) + prefer))
    left, resolved = [], []
    for path, stages in pending.items():
        if not hand_mergeable(path, stages, cwd):
            if take_base(path, stages, cwd):
                state["takenBase"] = sorted(set(state.get("takenBase", []) + [path]))
        elif has_markers(cwd / path):
            left.append(path)
        else:
            git("add", "-A", "--", path, cwd=cwd)
            resolved.append(path)
    state["resolved"] = sorted(set(state.get("resolved", []) + resolved))
    if left:
        write_json(state_file(cwd, MERGE_STATE), state)
        raise conflict(state, sorted(left))
    return {"result": "MERGED", **commit_merge(state, cwd)}


def do_publish(cfg: dict, cwd: Path) -> dict:
    if cfg["mode"] != "bot" or not cfg["mergeToBase"]:
        return {"result": "DISABLED", "branch": cfg["branch"] or current_branch(cwd),
                "message": "the fix is already on the target branch" if cfg["mode"] != "bot" else
                           "mergeToBase=false — the fix stays on the bot branch only, the dev merges it"}
    check_ready(cfg, cwd, allow_bot_merge=True)
    resume_bot_merge(cwd)
    base = cfg["baseBranch"]
    if not has_origin(cwd):
        holder = checked_out_at(base, cwd)
        if holder:
            raise Stop(EXIT_WAIT, "BASE_CHECKED_OUT",
                       f"no remote and '{base}' is checked out at {holder} — that branch cannot be moved safely, the dev merges by hand")
        if not ref_exists(f"refs/heads/{base}", cwd):
            raise Stop(EXIT_CONFIG, "BASE_UNKNOWN", f"main branch '{base}' not found")
        if not is_ancestor(base, "HEAD", cwd):
            merge_base_or_stop(base, cwd, "publish")
        old = out("rev-parse", base, cwd=cwd)
        git("update-ref", f"refs/heads/{base}", "HEAD", old, cwd=cwd)
        return {"result": "PUBLISHED", "base": base, "head": out("rev-parse", "HEAD", cwd=cwd), "remote": False}

    for attempt in (1, 2):
        fetch(cwd)
        remote_base = f"origin/{base}"
        if not ref_exists(f"refs/remotes/{remote_base}", cwd):
            raise Stop(EXIT_CONFIG, "BASE_UNKNOWN", f"origin has no branch '{base}'")
        if not is_ancestor(remote_base, "HEAD", cwd):
            merge_base_or_stop(remote_base, cwd, "publish")
        own = f"origin/{cfg['branch']}"
        if not (ref_exists(f"refs/remotes/{own}", cwd) and is_ancestor("HEAD", own, cwd)):
            # The main-branch merge (this one or an earlier `resolve`) only exists locally — the bot branch must go up first.
            push = git("push", "--quiet", "origin", f"HEAD:refs/heads/{cfg['branch']}", cwd=cwd, check=False)
            if push.returncode != 0:
                raise Stop(EXIT_NETWORK, "PUSH_FAILED", f"pushing the bot branch failed: {push.stderr.strip()}")
        push = git("push", "--quiet", "origin", f"HEAD:refs/heads/{base}", cwd=cwd, check=False)
        if push.returncode == 0:
            return {"result": "PUBLISHED", "base": base, "head": out("rev-parse", "HEAD", cwd=cwd), "remote": True}
        if attempt == 2:
            raise Stop(EXIT_NETWORK, "PUSH_FAILED", f"push to '{base}' rejected (no force): {push.stderr.strip()}")
        print(f"bughub-watch: '{base}' just moved on at origin, merging again and retrying", file=sys.stderr)
    raise AssertionError("unreachable")


def pause_seconds() -> int:
    try:
        return max(0, int(os.environ.get("BUGHUB_PAUSE_SECONDS", PAUSE_SECONDS)))
    except ValueError:
        return PAUSE_SECONDS


def do_pause(argv: list[str]) -> dict:
    opts = dict(zip(argv[::2], argv[1::2]))
    if len(argv) % 2 or any(key not in ("--attempt", "--seconds") for key in opts):
        raise Stop(EXIT_CONFIG, "BAD_ARGS", "pause [--attempt K] [--seconds S]")
    try:
        attempt = max(1, int(opts.get("--attempt", "1")))
        seconds = int(opts["--seconds"]) if "--seconds" in opts else pause_seconds()
    except ValueError:
        raise Stop(EXIT_CONFIG, "BAD_ARGS", "--attempt / --seconds must be integers") from None
    seconds = max(0, seconds)
    print(f"bughub-watch: sleeping {seconds}s (failure #{attempt}), then retrying", file=sys.stderr)
    time.sleep(seconds)
    heartbeat()
    return {"result": "RESUME", "attempt": attempt, "slept": seconds}


# --------------------------------------------------------------------------------------------------
# editor / notice
# --------------------------------------------------------------------------------------------------

def state_file(cwd: Path, name: str) -> Path:
    """Watch state file in this worktree's git dir (never committed, survives /clear)."""
    return Path(out("rev-parse", "--absolute-git-dir", cwd=cwd)) / "bughub-watch" / name


def read_json(path: Path) -> dict | None:
    try:
        data = json.loads(path.read_text(encoding="utf-8"))
    except (OSError, ValueError):
        return None
    return data if isinstance(data, dict) else None


def write_json(path: Path, data: dict) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    tmp = path.with_suffix(".tmp")
    tmp.write_text(json.dumps(data, ensure_ascii=False), encoding="utf-8")
    os.replace(tmp, path)


def restart_unity(flag: str, root: Path) -> str:
    """Run restart-unity (sh / ps1) with exactly one flag, return its last status line."""
    if os.name == "nt":
        ps_flag = {"--status": "-Status", "--open-only": "-OpenOnly", "--no-launch": "-NoLaunch"}[flag]
        cmd = ["powershell", "-NoProfile", "-ExecutionPolicy", "Bypass", "-File",
               str(RESTART_UNITY_DIR / "restart-unity.ps1"), ps_flag, "-Project", str(root)]
    else:
        cmd = ["bash", str(RESTART_UNITY_DIR / "restart-unity.sh"), flag, "--project", str(root)]
    try:
        proc = subprocess.run(cmd, capture_output=True, text=True, encoding="utf-8", errors="replace", timeout=120)
    except (OSError, subprocess.TimeoutExpired) as err:
        return f"ERROR could not run restart-unity: {err}"
    lines = [line.strip() for line in proc.stdout.splitlines() if line.strip()]
    return lines[-1] if lines else f"ERROR restart-unity printed nothing (exit {proc.returncode}): {proc.stderr.strip()[:200]}"


def pid_alive(pid: int) -> bool:
    if os.name == "nt":
        proc = subprocess.run(["tasklist", "/FI", f"PID eq {pid}", "/NH"], capture_output=True, text=True,
                              encoding="utf-8", errors="replace")
        return str(pid) in proc.stdout
    try:
        os.kill(pid, 0)
    except ProcessLookupError:
        return False
    except PermissionError:
        return True
    return True


def editor_status(root: Path) -> dict:
    """This project's GUI Editor + whether the bot opened it. A marker of an Editor that has exited / been replaced is deleted."""
    line = restart_unity("--status", root)
    match = re.fullmatch(r"RUNNING gui=(\S+) pids=\[([^\]]*)\]", line)
    if line == "NOT_RUNNING":
        gui, pids = None, []
    elif match:
        gui = int(match.group(1)) if match.group(1).isdigit() else None
        pids = [int(x) for x in match.group(2).split() if x.isdigit()]
    else:
        raise wait("EDITOR_STATUS_FAILED", f"could not read the Unity Editor status: {line}")
    marker_path = state_file(root, "editor.json")
    marker = read_json(marker_path)
    owned = bool(marker and gui and marker.get("pid") == gui)
    if marker and not owned:
        marker_path.unlink(missing_ok=True)
    return {"running": bool(pids), "pid": gui, "pids": pids, "owned": owned}


def registry_path() -> Path:
    """Instance registry of the Unity MCP plugin (the same path the MCP server reads)."""
    override = os.environ.get("UNITY_INSTANCE_REGISTRY")
    if override:
        return Path(override)
    base = Path(os.environ.get("LOCALAPPDATA") or Path.home() / "AppData" / "Local") if os.name == "nt" \
        else Path.home() / ".local" / "share"
    return base / "UnityMCP" / "instances.json"


def parse_utc(value: object) -> datetime | None:
    """'2026-10-09T02:38:06.8293400Z' (C# writes 7 fractional digits — fromisoformat rejects that)."""
    match = re.fullmatch(r"(\d{4}-\d\d-\d\dT\d\d:\d\d:\d\d)(?:\.(\d+))?(?:Z|\+00:00)?", str(value or ""))
    if not match:
        return None
    stamp = datetime.strptime(match.group(1), "%Y-%m-%dT%H:%M:%S").replace(tzinfo=timezone.utc)
    return stamp.replace(microsecond=int((match.group(2) or "0")[:6].ljust(6, "0")))


def same_path(a: object, b: Path) -> bool:
    return bool(a) and os.path.normcase(os.path.realpath(str(a))) == os.path.normcase(os.path.realpath(str(b)))


def registry_entry(root: Path, pid: int) -> dict | None:
    """Live entry of exactly this Editor process — meaning the MCP plugin is running (Editor loaded + compiled)."""
    try:
        data = json.loads(registry_path().read_text(encoding="utf-8-sig"))
    except (OSError, ValueError):
        return None
    now = datetime.now(timezone.utc)
    for entry in data if isinstance(data, list) else []:
        if not isinstance(entry, dict) or entry.get("processId") != pid or not same_path(entry.get("projectPath"), root):
            continue
        seen = parse_utc(entry.get("lastSeen") or entry.get("registeredAt"))
        if seen and (now - seen).total_seconds() <= REGISTRY_FRESH_SECONDS:
            return entry
    return None


def editor_open(root: Path, timeout: int) -> dict:
    status = editor_status(root)
    pid, opened = status["pid"], False
    if not pid:
        line = restart_unity("--open-only", root)
        match = re.fullmatch(r"(OPENED|ALREADY_RUNNING) pid=(\d+)", line)
        if not match:
            detail = line.removeprefix("ERROR").strip()
            reason = "EDITOR_NOT_INSTALLED" if "VERSION_NOT_INSTALLED" in line else \
                "EDITOR_BUSY" if detail.startswith("BUSY") else "EDITOR_LAUNCH_FAILED"
            raise wait(reason, f"could not open Unity Editor: {detail}")
        pid, opened = int(match.group(2)), match.group(1) == "OPENED"
        if opened:
            write_json(state_file(root, "editor.json"),
                       {"pid": pid, "projectPath": str(root), "openedAt": datetime.now(timezone.utc).isoformat()})
            print(f"bughub-watch: opened Unity Editor pid {pid} — waiting for it to load (up to {timeout}s)", file=sys.stderr)
    owned = opened or status["owned"]
    started, last_log = time.monotonic(), time.monotonic()
    while True:
        entry = registry_entry(root, pid)
        if entry:
            return {"result": "READY", "pid": pid, "opened": opened, "owned": owned, "port": entry.get("port"),
                    "waited": int(time.monotonic() - started)}
        if not pid_alive(pid):
            if owned:
                state_file(root, "editor.json").unlink(missing_ok=True)
            raise wait("EDITOR_EXITED", f"Unity Editor pid {pid} exited before becoming ready (crash, license prompt, or closed by hand)",
                       pid=pid)
        if time.monotonic() - started >= timeout:
            raise wait("EDITOR_NOT_READY",
                       f"Unity Editor pid {pid} not ready after {timeout // 60} min — a long import/compile, stuck on a dialog "
                       "(license, version upgrade…) that needs someone to click, or the project lacks the Unity MCP plugin", pid=pid, owned=owned)
        if time.monotonic() - last_log >= 60:
            print(f"bughub-watch: Editor pid {pid} still loading ({int(time.monotonic() - started)}s)…", file=sys.stderr)
            last_log = time.monotonic()
        time.sleep(EDITOR_POLL_SECONDS)
        heartbeat()


def editor_close(root: Path, wait_s: int) -> dict:
    marker = read_json(state_file(root, "editor.json"))
    status = editor_status(root)
    if not status["owned"]:
        if marker and not status["running"]:
            # The Editor the bot opened already quit (the MCP quit command finished before close was called) — its marker was cleaned up.
            return {"result": "CLOSED", "pid": marker.get("pid")}
        return {"result": "NOT_OWNED", "running": status["running"], "pid": status["pid"],
                "message": "Editor not opened by the bot (or already closed) — left alone"}
    pid = status["pid"]
    deadline = time.monotonic() + max(0, wait_s)
    while pid_alive(pid) and time.monotonic() < deadline:
        time.sleep(2)
        heartbeat()
    graceful = not pid_alive(pid)
    if not graceful or restart_unity("--status", root) != "NOT_RUNNING":
        # The Editor did not quit by itself (or orphaned import workers remain) → kill the project's whole process group.
        line = restart_unity("--no-launch", root)
        if line != "KILLED":
            raise wait("EDITOR_KILL_FAILED", f"could not close Unity Editor pid {pid}: {line}", pid=pid)
    state_file(root, "editor.json").unlink(missing_ok=True)
    return {"result": "CLOSED" if graceful else "KILLED", "pid": pid}


def int_option(argv: list[str], name: str, default: int) -> int:
    if not argv:
        return default
    if len(argv) != 2 or argv[0] != name or not re.fullmatch(r"[0-9]+", argv[1]):
        raise Stop(EXIT_CONFIG, "BAD_ARGS", f"unknown arguments: {' '.join(argv)} (only {name} <seconds> is accepted)")
    return int(argv[1])


def do_editor(cwd: Path, argv: list[str]) -> dict:
    sub, rest = (argv[0], argv[1:]) if argv else ("", [])
    root = repo_root(cwd)
    if sub == "status" and not rest:
        return {"result": "OK", **editor_status(root)}
    if sub == "open":
        return editor_open(root, int_option(rest, "--timeout", EDITOR_READY_TIMEOUT))
    if sub == "close":
        return editor_close(root, int_option(rest, "--wait", EDITOR_CLOSE_WAIT))
    raise Stop(EXIT_CONFIG, "BAD_ARGS", "editor status | open [--timeout S] | close [--wait S]")


def do_notice(cwd: Path, argv: list[str]) -> dict:
    path = state_file(repo_root(cwd), "notices.json")
    sub = argv[0] if argv else ""
    if sub == "clear" and len(argv) == 1:
        path.unlink(missing_ok=True)
        return {"result": "OK", "cleared": True}
    if sub not in ("check", "mark") or "--" not in argv:
        raise Stop(EXIT_CONFIG, "BAD_ARGS", "notice check|mark --reason KEY -- N... | notice clear")
    head, numbers = argv[1:argv.index("--")], argv[argv.index("--") + 1:]
    if len(head) != 2 or head[0] != "--reason" or not re.fullmatch(r"[A-Z][A-Z0-9_]{0,63}", head[1]):
        raise Stop(EXIT_CONFIG, "BAD_ARGS", "--reason must be an UPPER_CASE code (e.g. EDITOR_NOT_READY)")
    if not numbers or any(not re.fullmatch(r"[1-9][0-9]*", n) for n in numbers):
        raise Stop(EXIT_CONFIG, "BAD_ARGS", "-- must be followed by positive integer bug numbers")
    reason, bugs = head[1], [int(n) for n in numbers]
    state = read_json(path) or {}
    posted = [int(n) for n in state.get("posted", []) if isinstance(n, int)] if state.get("reason") == reason else []
    if sub == "check":
        return {"result": "OK", "reason": reason, "post": [n for n in bugs if n not in posted]}
    write_json(path, {"reason": reason, "posted": sorted(set(posted + bugs))})
    return {"result": "OK", "reason": reason, "marked": bugs}


def bug_option(argv: list[str], required: bool) -> int | None:
    if not argv and not required:
        return None
    if len(argv) != 2 or argv[0] != "--bug" or not re.fullmatch(r"[1-9][0-9]*", argv[1]):
        raise Stop(EXIT_CONFIG, "BAD_ARGS", "claim set --bug N | claim clear [--bug N] | claim get")
    return int(argv[1])


def bug_commit(bug: int, since: str, cwd: Path) -> str | None:
    """The commit fixing bug #N (line `Bug-Report: #N`) made since the claim, on HEAD — None if there is none yet.
    Filtered by time so a commit of an earlier fix (bug reassigned by QA) is not mistaken for it."""
    proc = git("log", "-n", "1", "--format=%H", f"--since={since}", "-E", f"--grep=^Bug-Report: #{bug}$", "HEAD",
               cwd=cwd, check=False)
    sha = proc.stdout.strip()
    return sha if proc.returncode == 0 and sha else None


def do_claim(cwd: Path, argv: list[str]) -> dict:
    sub, rest = (argv[0], argv[1:]) if argv else ("", [])
    path = state_file(cwd, CLAIM_STATE)
    if sub == "set":
        bug = bug_option(rest, required=True)
        changed = changed_paths(cwd)
        state = {"bug": bug, "claimedAt": datetime.now(timezone.utc).replace(microsecond=0).isoformat(),
                 "branch": current_branch(cwd), "head": out("rev-parse", "HEAD", cwd=cwd),
                 "dirty": changed if len(changed) <= SNAPSHOT_MAX else None}
        write_json(path, state)
        write_json(state_file(cwd, BASELINE_STATE), state)
        return {"result": "OK", "bug": bug}
    state = read_json(path)
    if sub == "clear":
        bug = bug_option(rest, required=False)
        cleared = bool(state) and (bug is None or state.get("bug") == bug)
        if cleared:
            path.unlink(missing_ok=True)
        return {"result": "OK", "cleared": cleared}
    if sub == "get" and not rest:
        if not state or not isinstance(state.get("bug"), int):
            return {"result": "NONE"}
        bug, since = state["bug"], str(state.get("claimedAt") or "")
        commit = bug_commit(bug, since, cwd) if since else None
        branch = current_branch(cwd)
        own = f"refs/remotes/origin/{branch}"
        pushed = bool(commit and branch and ref_exists(own, cwd) and is_ancestor(commit, own, cwd))
        return {"result": "CLAIMED", "bug": bug, "claimedAt": since, "branch": branch, "commit": commit, "pushed": pushed}
    raise Stop(EXIT_CONFIG, "BAD_ARGS", "claim set --bug N | claim clear [--bug N] | claim get")


def changed_paths(cwd: Path, paths: list[str] | None = None) -> list[str]:
    """Paths differing from HEAD: tracked modified / deleted / staged and untracked not ignored (rename: both names)."""
    args = ["status", "--porcelain", "-z", "--untracked-files=all"]
    if paths is not None:
        if not paths:
            return []
        args += ["--", *paths]
    entries = git(*args, cwd=cwd).stdout.split("\0")
    changed, rename_source = [], False
    for entry in entries:
        if rename_source:
            changed.append(entry)  # rename/copy: the next entry is the old path
            rename_source = False
            continue
        if not entry:
            continue
        changed.append(entry[3:])
        rename_source = entry[0] in "RC"
    return sorted(set(changed))


def parse_discard_args(argv: list[str]) -> tuple[int, bool, list[str]]:
    head, files = (argv[:argv.index("--")], argv[argv.index("--") + 1:]) if "--" in argv else (argv, [])
    bug, auto, it = None, False, iter(head)
    for arg in it:
        if arg == "--bug":
            value = next(it, "")
            if not re.fullmatch(r"[1-9][0-9]*", value):
                raise Stop(EXIT_CONFIG, "BAD_ARGS", "--bug must be a positive integer bug number")
            bug = int(value)
        elif arg == "--auto":
            auto = True
        else:
            raise Stop(EXIT_CONFIG, "BAD_ARGS", f"unknown argument: {arg} — discard --bug N [--auto] [-- <file>...]")
    if bug is None:
        raise Stop(EXIT_CONFIG, "BAD_ARGS", "discard needs --bug N")
    return bug, auto, files


def do_discard(cwd: Path, argv: list[str]) -> dict:
    bug, auto, files = parse_discard_args(argv)
    root = repo_root(cwd)
    busy = operation_in_progress(root)
    if busy:
        raise wait("BUSY", f"{busy} in progress in {root} — finish it, then discard again")
    path = state_file(root, CLAIM_STATE)
    state = read_json(path) or {}
    state = state if state.get("bug") == bug else {}
    since = str(state.get("claimedAt") or "")
    before = state.get("dirty")
    if auto and not isinstance(before, list):
        raise Stop(EXIT_CONFIG, "BAD_ARGS", "no claim-time baseline (claim set) — list the files after `--` instead of --auto")
    protected = set(before or [])

    # 1. Working tree: only the bot's files, never files the dev had in progress at claim time.
    wanted = []
    for name in files:
        target = (root / name).resolve()
        if root not in target.parents or ".git" in target.relative_to(root).parts:
            raise Stop(EXIT_CONFIG, "BAD_ARGS", f"file outside the repo: {name}")
        wanted.append(target.relative_to(root).as_posix())
    if auto:
        wanted += [name for name in changed_paths(root) if name not in protected]
    kept = sorted({name for name in (changed_paths(root) if auto else wanted) if name in protected})
    stashed = changed_paths(root, sorted({name for name in wanted if name not in protected}))
    stash = None
    if stashed:
        git("stash", "push", "--include-untracked", "-m", f"bughub #{bug} cancelled", "--", *stashed, cwd=root)
        stash = out("rev-parse", "stash@{0}", cwd=root)

    # 2. This claim's `Bug-Report: #N` commit: pushed → the server already reverted it; not pushed → drop / revert locally.
    branch = current_branch(root)
    upstream = f"refs/remotes/origin/{branch}" if branch and ref_exists(f"refs/remotes/origin/{branch}", root) else None
    scope = [f"--since={since}", "HEAD"] if since else [f"{upstream}..HEAD"] if upstream else []  # no baseline: only unpushed commits
    shas = git("log", "--no-merges", "--format=%H", "-E", f"--grep=^Bug-Report: #{bug}$", *scope, cwd=root).stdout.split() if scope else []
    if shas:
        # A "This reverts commit <sha>" commit already exists (an earlier discard, or the dev reverted by hand) → leave it alone.
        undone = set(re.findall(r"This reverts commit ([0-9a-f]{40})", git("log", "--format=%B", *scope, cwd=root).stdout))
        shas = [sha for sha in shas if sha not in undone]
    pushed = [sha for sha in shas if upstream and is_ancestor(sha, upstream, root)]
    local = [sha for sha in shas if sha not in pushed]
    dropped, reverted = [], []
    if local:
        top = out("rev-list", "--first-parent", "-n", str(len(local)), "HEAD", cwd=root).split()
        if top == local and git("reset", "--keep", f"{local[-1]}^", cwd=root, check=False).returncode == 0:
            dropped = local
        else:
            for sha in local:  # newest first
                proc = git("revert", "--no-edit", sha, cwd=root, check=False)
                if proc.returncode != 0:
                    git("revert", "--abort", cwd=root, check=False)
                    raise wait("REVERT_FAILED", f"could not revert local commit {sha[:10]}: {proc.stderr.strip()} — the dev must handle it by hand",
                               stash=stash, reverted=reverted)
                reverted.append(sha)

    # 3. The bug's downloaded files, 4. marker.
    shutil.rmtree(root / "Temp" / "BugHub" / str(bug), ignore_errors=True)
    if state:
        path.unlink(missing_ok=True)
    result = "DISCARDED" if stashed or dropped or reverted else "CLEAN"
    message = []
    if stash:
        message.append(f"stashed {len(stashed)} file(s) into git stash \"bughub #{bug} cancelled\" (restore: git stash apply {stash[:10]})")
    if dropped:
        message.append(f"dropped {len(dropped)} unpushed commit(s)")
    if reverted:
        message.append(f"reverted {len(reverted)} unpushed commit(s) locally (they go out with the next push)")
    if pushed:
        message.append(f"{len(pushed)} commit(s) already pushed — the BugHub server reverts them on GitLab, the next sync pulls that in")
    return {"result": result, "bug": bug, "branch": branch, "stash": stash, "stashed": stashed, "kept": kept,
            "droppedCommits": dropped, "revertedLocally": reverted, "pushedCommits": pushed,
            "message": "; ".join(message) or "nothing to clean up"}


def stash_away(root: Path, paths: list[str], label: str) -> str | None:
    """Stash `paths` (tracked modified / deleted / staged + untracked) into a stash commit and reset those paths to
    HEAD → the stash commit sha; None when git finds nothing to stash (e.g. only stat differences). The list goes
    through stdin: a few thousand paths still stay under the Windows command-line limit."""
    before = git("rev-parse", "-q", "--verify", "refs/stash", cwd=root, check=False).stdout.strip()
    git("stash", "push", "--include-untracked", "-m", label, "--pathspec-from-file=-", "--pathspec-file-nul",
        cwd=root, stdin="\0".join(paths), literal=True)
    after = git("rev-parse", "-q", "--verify", "refs/stash", cwd=root, check=False).stdout.strip()
    return after if after and after != before else None


def prune_clean_refs(root: Path) -> None:
    refs = git("for-each-ref", "--sort=-refname", "--format=%(refname)", CLEAN_REF, cwd=root).stdout.split()
    for ref in refs[CLEAN_KEEP:]:
        git("update-ref", "-d", ref, cwd=root, check=False)


def clean_tree(root: Path, protected: set[str], label: str, tag: str) -> dict:
    """Reset every path differing from HEAD (except `protected`) to HEAD. The backup is kept at ref
    `CLEAN_REF/<time>-<tag>` and removed from `git stash list` (one per bug — left in the stash list, the dev could
    not find their own stashes)."""
    changed = changed_paths(root)
    targets = [name for name in changed if name not in protected]
    kept = [name for name in changed if name in protected]
    sha = ref = None
    if targets:
        sha = stash_away(root, targets, label)
        if sha:
            ref = f"{CLEAN_REF}/{datetime.now(timezone.utc).strftime('%Y%m%dT%H%M%S%fZ')}-{tag}"
            git("update-ref", ref, sha, cwd=root)
            if git("rev-parse", "-q", "--verify", "refs/stash", cwd=root, check=False).stdout.strip() == sha:
                git("stash", "drop", "-q", cwd=root, check=False)
            prune_clean_refs(root)
    left = [name for name in changed_paths(root) if name not in protected] if targets else []
    return {"count": len(targets) if sha else 0, "files": targets[:50], "kept": kept[:50], "backup": sha, "ref": ref,
            "left": left[:50]}


def clean_scope(cfg: dict, root: Path, bug: int | None) -> set[str] | None:
    """Paths `clean` must not touch. Bot branch (directory dedicated to the bot) → none. Checkout shared with the dev →
    the paths that already differed from HEAD when bug `bug` was claimed (the `claim set` baseline); no baseline →
    None (no way to tell which changes are the dev's unfinished work)."""
    if cfg["mode"] == "bot" and current_branch(root) == cfg["branch"]:
        return set()
    base = read_json(state_file(root, BASELINE_STATE)) or {}
    if bug is not None and base.get("bug") == bug and isinstance(base.get("dirty"), list):
        return set(base["dirty"])
    return None


def do_clean(cfg: dict, cwd: Path, argv: list[str]) -> dict:
    if argv and (len(argv) != 2 or argv[0] != "--bug" or not re.fullmatch(r"[1-9][0-9]*", argv[1])):
        raise Stop(EXIT_CONFIG, "BAD_ARGS", "clean [--bug N]")
    bug = int(argv[1]) if argv else None
    root = repo_root(cwd)
    busy = operation_in_progress(root)
    if busy:
        raise wait("BUSY", f"{busy} in progress in {root} — finish it, then clean again")
    protected = clean_scope(cfg, root, bug)
    if protected is None:
        return {"result": "SKIPPED", "bug": bug,
                "message": "checkout shared with the dev and no claim-time baseline for this bug (claim set) — the dev's unfinished "
                           "work cannot be told apart, so nothing is cleaned"}
    done = clean_tree(root, protected, f"bughub #{bug} cleanup" if bug else "bughub cleanup", f"bug{bug}" if bug else "manual")
    if not done["backup"]:
        return {"result": "CLEAN", "bug": bug, **done,
                "message": "no changes outside the commit" + (f" (kept {len(done['kept'])} path(s) of the dev's unfinished work)" if done["kept"] else "")}
    message = f"cleaned {done['count']} uncommitted path(s), backup {done['backup'][:10]} (restore: git stash apply {done['backup'][:10]})"
    if done["kept"]:
        message += f"; kept {len(done['kept'])} path(s) the dev had in progress since the claim"
    if done["left"]:
        message += f"; {len(done['left'])} path(s) still differ from HEAD after cleaning (usually line-ending differences)"
    return {"result": "CLEANED", "bug": bug, **done, "message": message}


# --------------------------------------------------------------------------------------------------
# start
# --------------------------------------------------------------------------------------------------

def ensure_branch(cfg: dict, cwd: Path, remote: bool) -> None:
    branch = cfg["branch"]
    if ref_exists(f"refs/heads/{branch}", cwd):
        return
    if remote and ref_exists(f"refs/remotes/origin/{branch}", cwd):
        git("branch", "--track", branch, f"origin/{branch}", cwd=cwd)
        print(f"bughub-watch: created branch {branch} tracking origin/{branch}", file=sys.stderr)
        return
    start_point = base_ref(cfg, cwd, remote)
    git("branch", "--no-track", branch, start_point, cwd=cwd)
    print(f"bughub-watch: created branch {branch} from {start_point} (first push once the first bug is fixed)", file=sys.stderr)


def confirm(question: str, assume_yes: bool) -> bool:
    if assume_yes:
        return True
    if not sys.stdin.isatty():
        return False
    # The question goes to stderr: stdout is reserved for the result JSON line. Windows treats NUL as a tty, so this
    # is reached even with empty stdin — printed to stdout, the question would stick to the start of the JSON line.
    print(f"{question} [y/N] ", end="", file=sys.stderr, flush=True)
    try:
        return input().strip().lower() in ("y", "yes")
    except EOFError:
        print(file=sys.stderr)
        return False


def prepare_workdir(cfg: dict, cwd: Path, assume_yes: bool) -> Path:
    """Directory the watch window runs in. Follow mode: this checkout itself. Bot branch: a dedicated worktree / branch switch."""
    if cfg["mode"] == "follow":
        return repo_root(cwd)
    remote = has_origin(cwd)
    if remote:
        fetch(cwd)
    branch = cfg["branch"]

    if cfg["worktree"]:
        path = Path(cfg["worktreePath"])
        if path.exists():
            if not (path / ".git").exists():
                raise Stop(EXIT_CONFIG, "WORKTREE_PATH_TAKEN", f"{path} exists but is not a worktree — change bugHub.watch.worktreePath")
            print(f"bughub-watch: reusing worktree {path}", file=sys.stderr)
            return path
        holder = checked_out_at(branch, cwd)
        if holder:
            raise Stop(EXIT_CONFIG, "BRANCH_CHECKED_OUT", f"branch {branch} is checked out at {holder} — close it / switch branches there first", path=holder)
        ensure_branch(cfg, cwd, remote)
        git("worktree", "add", str(path), branch, cwd=cwd)
        print(f"bughub-watch: created worktree {path} on {branch}. It is a separate Unity project — for "
              "compile-check, open Unity Editor on exactly this directory (the first Library import takes a while).", file=sys.stderr)
        return path

    root = repo_root(cwd)
    here = current_branch(root)
    if here == branch:
        return root
    holder = checked_out_at(branch, cwd)
    if holder:
        raise Stop(EXIT_CONFIG, "BRANCH_CHECKED_OUT", f"branch {branch} is checked out at {holder} — run the watch there, or enable bugHub.watch.worktree", path=holder)
    dirty = dirty_tracked(root)
    if dirty:
        raise Stop(EXIT_CONFIG, "DIRTY", f"checkout {root} has uncommitted changes — commit/stash them before switching to {branch}", files=dirty[:20])
    if not confirm(f"Checkout {root} will switch from '{here or '(detached)'}' to the bot branch '{branch}' and be dedicated to the watch window "
                   f"(a Unity Editor open on this directory will reimport). Continue?", assume_yes):
        raise Stop(EXIT_CONFIG, "NOT_CONFIRMED", "branch not switched — rerun with --yes, or enable bugHub.watch.worktree to use a separate directory")
    ensure_branch(cfg, root, remote)
    git("checkout", "--quiet", branch, cwd=root)
    return root


def set_title(text: str) -> None:
    if sys.stdout.isatty() and os.name != "nt":
        sys.stdout.write(f"\033]0;{text}\007")
        sys.stdout.flush()


def do_start(cfg: dict, cwd: Path, argv: list[str]) -> dict:
    if not cfg["projectCode"]:
        raise Stop(EXIT_CONFIG, "NOT_CONFIGURED", "bugHub.projectCode is empty — run /bughub-setup first")
    assume_yes = "--yes" in argv
    launch = "--no-launch" not in argv
    extra = argv[argv.index("--") + 1:] if "--" in argv else []
    unknown = [a for a in (argv[:argv.index("--")] if "--" in argv else argv) if a not in ("--yes", "--no-launch")]
    if unknown:
        raise Stop(EXIT_CONFIG, "BAD_ARGS", f"unknown arguments: {' '.join(unknown)}")
    # Real path (e.g. `claude.cmd` of an npm install on Windows): CreateProcess does not append .cmd by itself,
    # so Popen(["claude"]) raises FileNotFoundError even though `which` finds the command.
    claude = shutil.which("claude") if launch else None
    if launch and not claude:
        raise Stop(EXIT_CONFIG, "NO_CLAUDE", "`claude` command not found in PATH")

    workdir = prepare_workdir(cfg, cwd, assume_yes)
    try:
        synced = do_sync(cfg, workdir)
    except Stop as stop:
        if stop.code not in (EXIT_WAIT, EXIT_MERGE):
            raise
        # The loop pauses + re-syncs before every bug by itself — failing to sync when opening the window is no reason not to open it.
        reason = stop.payload.get("reason") or stop.payload["result"]
        print(f"bughub-watch: could not sync yet ({reason}) — the loop will retry: {stop.payload['message']}", file=sys.stderr)
        synced = {**stop.payload, "branch": stop.payload.get("branch") or current_branch(workdir)}
    branch = synced.get("branch") or cfg["branch"]
    result = {**synced, "result": "READY", "mode": cfg["mode"], "workdir": str(workdir), "mergeToBase": cfg["mergeToBase"]}
    target = f"bot branch {branch} (main branch {cfg['baseBranch']}, merge into main branch: {'yes' if cfg['mergeToBase'] else 'no'})" \
        if cfg["mode"] == "bot" else f"following the checked-out branch: {branch or '(detached)'}"
    print(f"bughub-watch: {cfg['projectCode']} · {target} · {workdir}", file=sys.stderr)
    if not launch:
        return result

    print(json.dumps(result, ensure_ascii=False))
    sys.stdout.flush()
    set_title(f"BugHub watch · {cfg['projectCode']} · {branch}")
    os.chdir(workdir)
    sys.exit(supervise([claude, *extra, WATCH_PROMPT], workdir))


def watchdog_limit() -> float:
    """Max idle seconds before the watchdog relaunches claude (0 = watchdog off)."""
    try:
        minutes = float(os.environ.get("BUGHUB_WATCHDOG_MINUTES", WATCHDOG_MINUTES))
    except ValueError:
        minutes = WATCHDOG_MINUTES
    return max(0.0, minutes) * 60


def watch_log(workdir: Path, message: str) -> None:
    """Supervisor log (`.git/bughub-watch/watch.log`) — printing to the terminal while claude runs would break its TUI."""
    try:
        path = state_file(workdir, "watch.log")
        path.parent.mkdir(parents=True, exist_ok=True)
        with path.open("a", encoding="utf-8") as f:
            f.write(f"{datetime.now().strftime('%Y-%m-%d %H:%M:%S')} {message}\n")
    except (OSError, Stop):
        pass


def claude_projects_dir() -> Path:
    base = os.environ.get("CLAUDE_CONFIG_DIR")
    return (Path(base).expanduser() if base else Path.home() / ".claude") / "projects"


def transcript_files(folder: Path) -> list[Path]:
    return [*folder.glob("*.jsonl"), *folder.glob("*/subagents/*.jsonl")]


def transcript_cwd(file: Path) -> str | None:
    try:
        with file.open(encoding="utf-8", errors="replace") as f:
            for _, line in zip(range(40), f):
                try:
                    cwd = json.loads(line).get("cwd")
                except (ValueError, AttributeError):
                    continue
                if cwd:
                    return str(cwd)
    except OSError:
        pass
    return None


def find_transcript_dir(workdir: Path, since: float) -> Path | None:
    """Claude Code's transcript directory for this working directory (~/.claude/projects/<cwd, odd characters become ->)."""
    root = claude_projects_dir()
    try:
        folders = [d for d in root.iterdir() if d.is_dir()]
    except OSError:
        return None
    want = re.sub(r"[^A-Za-z0-9]", "-", str(workdir)).lower()
    for folder in folders:
        if folder.name.lower() == want:
            return folder
    # The folder name is an internal Claude Code convention (may change / be truncated) → match on the `cwd` recorded in fresh transcripts.
    for folder in folders:
        for file in folder.glob("*.jsonl"):
            try:
                fresh = file.stat().st_mtime >= since
            except OSError:
                continue
            if fresh and same_path(transcript_cwd(file), workdir):
                return folder
    return None


def last_activity(started: float, beat: Path, folder: Path) -> float:
    """Latest sign of loop activity: the transcript grew (a turn is running) or a heartbeat (waiting / long command)."""
    stamps = [started]
    for file in [beat, *transcript_files(folder)]:
        try:
            stamps.append(file.stat().st_mtime)
        except OSError:
            pass
    return max(stamps)


def descendants(pid: int) -> list[int]:
    """Descendant PIDs of `pid` (POSIX) — to stop claude's background tasks too, not just the main process."""
    proc = subprocess.run(["ps", "-A", "-o", "pid=,ppid="], capture_output=True, text=True)
    children: dict[int, list[int]] = {}
    for line in proc.stdout.splitlines():
        parts = line.split()
        if len(parts) == 2 and parts[0].isdigit() and parts[1].isdigit():
            children.setdefault(int(parts[1]), []).append(int(parts[0]))
    found, todo = [], [pid]
    while todo:
        for child in children.get(todo.pop(), []):
            if child not in found:
                found.append(child)
                todo.append(child)
    return found


def stop_claude(proc: subprocess.Popen) -> None:
    """Stop claude together with its whole process tree (MCP servers, background shells waiting for bugs)."""
    if os.name == "nt":
        subprocess.run(["taskkill", "/T", "/F", "/PID", str(proc.pid)], capture_output=True)
    else:
        children = descendants(proc.pid)
        proc.terminate()
        try:
            proc.wait(timeout=15)
        except subprocess.TimeoutExpired:
            proc.kill()
        for child in children:
            try:
                os.kill(child, signal.SIGTERM)
            except OSError:
                pass
    try:
        proc.wait(timeout=30)
    except subprocess.TimeoutExpired:
        pass
    if os.name != "nt" and sys.stdin.isatty():
        subprocess.run(["stty", "sane"], check=False)  # claude killed midway may leave the terminal in raw mode


def wait_claude(proc: subprocess.Popen, workdir: Path, beat: Path, limit: float) -> tuple[int, bool]:
    """Wait for claude to exit → (exit code, False). Watchdog: idle longer than `limit` seconds → stop claude → (code, True)."""
    if limit <= 0:
        return proc.wait(), False
    started = time.time()
    poll = max(0.5, min(WATCHDOG_POLL_SECONDS, limit / 4))
    folder, warned = None, False
    while True:
        try:
            return proc.wait(timeout=poll), False
        except subprocess.TimeoutExpired:
            pass
        folder = folder or find_transcript_dir(workdir, started)
        if folder is None:
            # Without a readable transcript "working" cannot be told from "idle" — never stop it blindly.
            if not warned and time.time() - started >= limit:
                watch_log(workdir, f"watchdog: no claude transcript found in {claude_projects_dir()} — watchdog disabled for this run")
                warned = True
            continue
        idle = time.time() - last_activity(started, beat, folder)
        if idle >= limit:
            watch_log(workdir, f"watchdog: claude pid {proc.pid} idle for {int(idle)}s (limit {int(limit)}s) — stopping it to relaunch")
            stop_claude(proc)
            return (proc.returncode if proc.returncode is not None else -1), True


def supervise(cmd: list[str], workdir: Path) -> int:
    """Run claude and relaunch it whenever it dies (code ≠ 0: crash, killed) or stays idle too long (watchdog) — the
    watch loop does not die with the process. Exit code 0 (`/exit`, Ctrl+C twice) or Ctrl+C while waiting to
    relaunch = the dev stopped it → return that code."""
    interrupted: list[bool] = []

    def on_sigint(_sig, _frame):
        interrupted.append(True)

    limit = watchdog_limit()
    beat_dir = state_file(workdir, "heartbeat").parent
    beat_dir.mkdir(parents=True, exist_ok=True)
    for old in beat_dir.glob("heartbeat-*"):
        old.unlink(missing_ok=True)
    launch = 0
    while True:
        launch += 1
        # A separate heartbeat file per launch: background processes left over from the previous launch cannot fake "alive" for this one.
        beat = beat_dir / f"heartbeat-{os.getpid()}-{launch}"
        beat.touch()
        env = dict(os.environ, **{HEARTBEAT_ENV: beat.as_posix()})
        if "CLAUDECODE" in env:
            for key in PARENT_SESSION_ENV:
                env.pop(key, None)
        # A Python handler is not inherited by the child across exec (SIG_IGN would be) → claude still receives Ctrl+C
        # normally, while the script is not killed by KeyboardInterrupt in the middle of the child's run.
        previous = signal.signal(signal.SIGINT, on_sigint)
        try:
            proc = subprocess.Popen(cmd, env=env)
            code, stalled = wait_claude(proc, workdir, beat, limit)
        finally:
            signal.signal(signal.SIGINT, previous)
            beat.unlink(missing_ok=True)
        if not stalled and (code == 0 or interrupted):
            return code
        delay = pause_seconds()
        reason = f"idle for more than {limit / 60:g} min (watchdog)" if stalled else f"exited (exit {code})"
        watch_log(workdir, f"claude {reason} — relaunching in {delay}s")
        print(f"bughub-watch: claude {reason} — relaunching in {delay}s, Ctrl+C to stop for good", file=sys.stderr)
        try:
            time.sleep(delay)
        except KeyboardInterrupt:
            return code


# --------------------------------------------------------------------------------------------------

def main(argv: list[str]) -> int:
    # Windows: piped stdio (Bash tool, subprocess) defaults to strict cp1252 → printing non-ASCII text (Vietnamese
    # file names, stash reasons…) crashes and the JSON is lost.
    for stream in (sys.stdout, sys.stderr):
        if hasattr(stream, "reconfigure"):
            stream.reconfigure(encoding="utf-8", errors="replace")
    command = argv[0] if argv and not argv[0].startswith("-") else "start"
    rest = argv[1:] if argv and not argv[0].startswith("-") else argv
    cwd = Path.cwd()
    heartbeat()
    try:
        if command == "pause":
            # Needs no repo / config — the skill must be able to pause precisely when the config is broken.
            result = do_pause(rest)
        else:
            if not ok("rev-parse", "--git-dir", cwd=cwd):
                raise Stop(EXIT_CONFIG, "NOT_A_REPO", "not inside a git repo")
            cfg = load_config(cwd)
            if command == "config":
                here = current_branch(cwd)
                on_branch = here == cfg["branch"] if cfg["mode"] == "bot" else bool(here)
                result = {"result": "OK", **cfg, "currentBranch": here, "onWatchBranch": on_branch,
                          "targetBranch": cfg["branch"] or here}
            elif command == "sync":
                result = do_sync(cfg, repo_root(cwd))
            elif command == "push":
                result = do_push(cfg, repo_root(cwd))
            elif command == "shelve":
                result = do_shelve(cwd, rest)
            elif command == "resolve":
                result = do_resolve(repo_root(cwd), rest)
            elif command == "publish":
                result = do_publish(cfg, repo_root(cwd))
            elif command == "start":
                result = do_start(cfg, cwd, rest)
            elif command == "editor":
                result = do_editor(cwd, rest)
            elif command == "notice":
                result = do_notice(cwd, rest)
            elif command == "claim":
                result = do_claim(repo_root(cwd), rest)
            elif command == "discard":
                result = do_discard(cwd, rest)
            elif command == "clean":
                result = do_clean(cfg, cwd, rest)
            else:
                raise Stop(EXIT_CONFIG, "BAD_ARGS",
                           f"unknown command: {command} (start | sync | push | shelve | resolve | publish | pause | config | editor | notice | claim | discard | clean)")
    except Stop as stop:
        print(f"bughub-watch: {stop.payload['result']} — {stop.payload['message']}", file=sys.stderr)
        print(json.dumps(stop.payload, ensure_ascii=False))
        return stop.code
    print(json.dumps(result, ensure_ascii=False))
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
