#!/usr/bin/env python3
"""bughub-watch.py — đồng bộ nhánh cho cửa sổ `/fix-bug --watch` (macOS / Linux / Windows).

Hai chế độ, chọn bằng `bugHub.watch.branch` trong `.claude/project-profile.json`:

- **Theo nhánh đang mở** (`branch` rỗng — mặc định): chạy watch ở nhánh nào thì sửa bug + push lên đúng
  nhánh đó, ngay trong checkout đó. Trước mỗi bug merge `origin/<nhánh>` vào để sửa trên code mới nhất và
  push không bị từ chối. Dev đổi nhánh của checkout giữa chừng → bug sau đi theo nhánh mới.
- **Nhánh bot** (`branch` có giá trị, vd `AutoFixBug`): nhánh riêng cho bot. Trước mỗi bug merge bản mới
  nhất của nhánh chính (`bugHub.watch.baseBranch`, rỗng = `defaultBaseBranch`) vào; sửa xong push lên chính
  nhánh bot; chỉ đưa lên nhánh chính khi bật `bugHub.watch.mergeToBase`. Chạy ngay trong checkout này hay
  trong một `git worktree` riêng do `bugHub.watch.worktree` quyết định.

Loop watch KHÔNG BAO GIỜ dừng vì script này: trạng thái tạm thời (mất mạng, conflict, đang merge/rebase
dở, sai nhánh, git bị khoá…) trả `WAIT` + `reason`, skill chạy `pause` rồi thử lại.

Lệnh:
  start [--yes] [--no-launch] [-- <tham số claude>]
        Chuẩn bị thư mục (nhánh bot: tạo worktree / chuyển nhánh; theo nhánh: giữ nguyên checkout), chạy
        `sync`, rồi mở `claude "/fix-bug --watch"` ngay trong cửa sổ terminal này. `--yes`: không hỏi lại
        khi phải chuyển nhánh của checkout này. `--no-launch`: chỉ chuẩn bị. Tham số sau `--` chuyển
        nguyên cho claude (vd `-- --permission-mode bypassPermissions`).
  sync  TRƯỚC khi nhận mỗi bug: fetch + merge `origin/<nhánh>` (và nhánh chính ở chế độ nhánh bot).
        `OK` kèm `dirty` = file đã track đang sửa dở (bot không được đụng) | `WAIT`.
  push  Sau khi commit mà `git_push` lỗi: merge `origin/<nhánh>` nếu remote vừa chạy tiếp rồi push lại
        (tối đa 3 lần, không bao giờ force).
  shelve --bug N --reason "<lý do>" -- <file>...
        Bug bị release: cất phần sửa dở (đúng các file bot đã sửa / tạo) vào `git stash` để thư mục sạch
        cho bug sau mà không mất việc — dev lấy lại bằng `git stash list` / `git stash apply`.
  publish
        (nhánh bot) Gọi sau khi fix đã commit + push: mergeToBase=false (hoặc chế độ theo nhánh) →
        `DISABLED`; true → đưa HEAD lên nhánh chính (fast-forward, merge nhánh chính vào trước nếu nó đã
        chạy tiếp; không bao giờ force).
  pause [--attempt K] [--seconds S]
        Ngủ theo backoff (lần 1: 1', 2: 5', 3: 15', từ 4: 30') rồi in `RESUME` — skill chạy nền rồi kết
        thúc lượt, lượt sau thử lại bước vừa lỗi.
  config
        In config đã gộp + nhánh hiện tại (skill đọc ở preflight).
  editor status | open [--timeout S] | close [--wait S]
        Unity Editor của thư mục này cho bug — compile-check là bắt buộc nên bot không sửa khi chưa có Editor.
        `status`: {running, pid, owned}. `open`: Editor chưa mở thì mở (restart-unity --open-only) và ghi
        nhận "bot mở" (owned); rồi chờ tới khi plugin Unity MCP của đúng process đó đăng ký vào registry
        (Editor đã load + compile xong) → `READY`. `close`: chỉ tắt Editor do bot mở — skill đã kiểm hết bug
        `retry`, không có thay đổi chưa save, rồi xin Editor tự thoát qua MCP; lệnh này chờ process thoát,
        quá `--wait` giây thì kill (`CLOSED` / `KILLED`); Editor không do bot mở → `NOT_OWNED`, không đụng.
  notice check|mark --reason KEY -- N... | notice clear
        Chống gửi lặp `bughub_note` khi loop pause vì Editor chưa dùng được: `check` trả các bug CHƯA được
        nhắn với lý do KEY, `mark` ghi nhận đã nhắn, `clear` (Editor dùng được lại) xoá để lần lỗi sau nhắn tiếp.

Dòng cuối stdout luôn là đúng một JSON `{"result": "...", ...}`; log cho người đọc ra stderr.
Exit: 0 xong (kể cả `DISABLED`, `UP_TO_DATE`, `NOTHING`) · 2 config / tham số sai / `start` không chuẩn bị
được thư mục · 3 `WAIT` (tạm thời — pause rồi thử lại; `reason`: DETACHED, WRONG_BRANCH, BUSY, FETCH_FAILED,
MERGE_CONFLICT, LOCAL_CHANGES, DIVERGED_DIRTY, PUSH_REJECTED, GIT_FAILED; `editor`: EDITOR_NOT_INSTALLED, EDITOR_BUSY,
EDITOR_LAUNCH_FAILED, EDITOR_EXITED, EDITOR_NOT_READY, EDITOR_STATUS_FAILED, EDITOR_KILL_FAILED) · 4 merge conflict khi `publish`
(đã `merge --abort`) · 5 push khi `publish` bị từ chối.
"""

from __future__ import annotations

import json
import os
import re
import shutil
import subprocess
import sys
import time
from datetime import datetime, timezone
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))
from project_profile import profile  # noqa: E402

EXIT_CONFIG = 2
EXIT_WAIT = 3
EXIT_CONFLICT = 4
EXIT_NETWORK = 5

WATCH_PROMPT = "/fix-bug --watch"
# Giây chờ giữa các lần fetch lại trong MỘT lệnh. Ngắn có chủ đích: mất mạng lâu hơn thì trả WAIT để
# loop pause theo backoff (lệnh chạy foreground trong Bash tool, timeout mặc định 2 phút).
FETCH_RETRY_DELAYS = (5, 20)
PUSH_ATTEMPTS = 3
PAUSE_STEPS = (60, 300, 900, 1800)
# Không bao giờ để git đứng chờ nhập mật khẩu trong loop không người trông.
GIT_ENV = dict(os.environ, GIT_TERMINAL_PROMPT="0")
# Chờ Editor sẵn sàng trong MỘT lệnh: vừa timeout tối đa 10 phút của Bash tool; lâu hơn thì WAIT → pause → thử lại.
EDITOR_READY_TIMEOUT = 540
EDITOR_CLOSE_WAIT = 60
EDITOR_POLL_SECONDS = 5
# Entry registry của Unity MCP cũ hơn mức này là của Editor đã chết (= registryStalenessTimeoutMs mặc định của MCP).
REGISTRY_FRESH_SECONDS = 300
RESTART_UNITY_DIR = Path(__file__).resolve().parent.parent / "skills" / "restart-unity" / "scripts"


class Stop(Exception):
    """Dừng lệnh với một exit code + JSON kết quả."""

    def __init__(self, code: int, result: str, message: str, **extra):
        super().__init__(message)
        self.code = code
        self.payload = {"result": result, "message": message, **extra}


def wait(reason: str, message: str, **extra) -> Stop:
    """Trạng thái tạm thời — skill pause rồi thử lại, không dừng loop."""
    return Stop(EXIT_WAIT, "WAIT", message, reason=reason, **extra)


# --------------------------------------------------------------------------------------------------
# git
# --------------------------------------------------------------------------------------------------

def git(*args: str, cwd: Path | None = None, check: bool = True) -> subprocess.CompletedProcess:
    proc = subprocess.run(["git", *args], cwd=cwd, capture_output=True, text=True, encoding="utf-8",
                          errors="replace", env=GIT_ENV)
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
    """fetch origin; lỗi thì thử lại vài giây rồi trả WAIT (loop pause theo backoff, không dừng)."""
    for delay in (*FETCH_RETRY_DELAYS, None):
        proc = git("fetch", "--quiet", "--prune", "origin", cwd=cwd, check=False)
        if proc.returncode == 0:
            return
        if delay is None:
            raise wait("FETCH_FAILED", f"git fetch origin lỗi: {proc.stderr.strip()}")
        print(f"bughub-watch: fetch lỗi, thử lại sau {delay}s — {proc.stderr.strip()}", file=sys.stderr)
        time.sleep(delay)


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
    """File đã track còn thay đổi chưa commit (untracked bỏ qua — commit của skill chỉ lấy file của bug)."""
    # Không qua out(): strip() ăn mất cột trạng thái đầu dòng (" M a.txt") nên tên file lệch 1 ký tự.
    lines = git("status", "--porcelain", "--untracked-files=no", cwd=cwd).stdout.splitlines()
    return [line[3:] for line in lines if line.strip()]


def git_path_exists(name: str, cwd: Path) -> bool:
    path = Path(out("rev-parse", "--git-path", name, cwd=cwd))
    return (path if path.is_absolute() else cwd / path).exists()


def operation_in_progress(cwd: Path) -> str:
    """Tên thao tác git đang dở (merge / rebase / cherry-pick / revert) — rỗng nếu không có."""
    for name, label in (("MERGE_HEAD", "merge"), ("rebase-merge", "rebase"), ("rebase-apply", "rebase"),
                        ("CHERRY_PICK_HEAD", "cherry-pick"), ("REVERT_HEAD", "revert")):
        if git_path_exists(name, cwd):
            return label
    return ""


def try_merge(ref: str, cwd: Path, ff_only: bool = False) -> dict | None:
    """Merge `ref` vào HEAD. Xong → None. Không xong (conflict / bị thay đổi dở chặn) → abort, trả chi tiết."""
    args = ["merge", "--ff-only", ref] if ff_only else ["merge", "--no-edit", ref]
    proc = git(*args, cwd=cwd, check=False)
    if proc.returncode == 0:
        return None
    conflicted = out("diff", "--name-only", "--diff-filter=U", cwd=cwd).splitlines()
    if git_path_exists("MERGE_HEAD", cwd):
        git("merge", "--abort", cwd=cwd, check=False)
    detail = proc.stderr.strip() or proc.stdout.strip()
    # "would be overwritten by merge" liệt kê file chặn ở các dòng thụt bằng tab.
    blocking = [line.strip() for line in detail.splitlines() if line.startswith("\t")]
    return {"ref": ref, "detail": detail, "files": (conflicted or blocking)[:20],
            "kind": "conflict" if conflicted else "blocked"}


def merge(ref: str, cwd: Path) -> None:
    """Merge `ref` vào HEAD; không tự xong được → abort, raise MERGE_CONFLICT (dùng cho publish)."""
    failed = try_merge(ref, cwd)
    if failed:
        raise Stop(EXIT_CONFLICT, "MERGE_CONFLICT", f"merge {ref} không tự xong được — dev resolve tay. {failed['detail']}",
                   ref=ref, files=failed["files"])


# --------------------------------------------------------------------------------------------------
# config
# --------------------------------------------------------------------------------------------------

def repo_root(cwd: Path) -> Path:
    return Path(out("rev-parse", "--show-toplevel", cwd=cwd)).resolve()


def main_checkout(cwd: Path) -> Path:
    """Thư mục checkout chính của clone (worktree đầu tiên trong danh sách)."""
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
                raise Stop(EXIT_CONFIG, "BAD_CONFIG", f"bugHub.watch.{name} không phải tên nhánh hợp lệ: '{value}'")
        if branch == base:
            raise Stop(EXIT_CONFIG, "BAD_CONFIG", "bugHub.watch.branch phải khác nhánh chính — bot không commit thẳng lên nhánh chính")
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
    raise Stop(EXIT_CONFIG, "BASE_UNKNOWN", f"không thấy nhánh chính '{base}' (cả local lẫn origin) — kiểm bugHub.watch.baseBranch")


# --------------------------------------------------------------------------------------------------
# sync / push / shelve / publish
# --------------------------------------------------------------------------------------------------

def check_ready(cfg: dict, cwd: Path) -> str:
    """Nhánh bot sẽ commit lên; trạng thái chưa làm được (detached, sai nhánh, đang merge dở) → WAIT."""
    branch = current_branch(cwd)
    if not branch:
        raise wait("DETACHED", "HEAD đang detached — chờ checkout về một nhánh")
    if cfg["mode"] == "bot" and branch != cfg["branch"]:
        raise wait("WRONG_BRANCH",
                   f"đang ở '{branch}', không phải nhánh bot '{cfg['branch']}' — mở cửa sổ bằng `python3 .claude/scripts/bughub-watch.py start`",
                   branch=branch)
    busy = operation_in_progress(cwd)
    if busy:
        raise wait("BUSY", f"đang có {busy} dở trong {cwd} — chờ dev xử lý xong", branch=branch)
    return branch


def integrate_own(branch: str, cwd: Path) -> str | None:
    """Đưa `origin/<branch>` vào HEAD để push được. Trả ref đã merge (None nếu không cần); không được → WAIT."""
    own = f"origin/{branch}"
    if not ref_exists(f"refs/remotes/{own}", cwd) or is_ancestor(own, "HEAD", cwd):
        return None
    fast_forward = is_ancestor("HEAD", own, cwd)
    if not fast_forward and dirty_tracked(cwd):
        # Merge thật mà conflict thì `merge --abort` có thể làm hỏng thay đổi dở của dev — chờ dev commit/push.
        raise wait("DIVERGED_DIRTY", f"'{branch}' có commit local chưa push trong khi {own} đã chạy tiếp, và thư mục "
                   "còn thay đổi dở — chờ dev push / commit rồi thử lại", ref=own)
    failed = try_merge(own, cwd, ff_only=fast_forward)
    if failed:
        reason = "MERGE_CONFLICT" if failed["kind"] == "conflict" else "LOCAL_CHANGES"
        raise wait(reason, f"chưa merge được {own} vào '{branch}' (push sẽ bị từ chối) — chờ dev xử lý. {failed['detail']}",
                   ref=own, files=failed["files"])
    return own


def do_sync(cfg: dict, cwd: Path) -> dict:
    branch = check_ready(cfg, cwd)
    remote = has_origin(cwd)
    merged, skipped = [], []
    if remote:
        fetch(cwd)
        own = integrate_own(branch, cwd)
        if own:
            merged.append(own)
    if cfg["mode"] == "bot":
        base = base_ref(cfg, cwd, remote)
        if not is_ancestor(base, "HEAD", cwd):
            failed = try_merge(base, cwd)
            if failed:
                # Nhánh bot vẫn push được — sửa trên code cũ hơn nhánh chính, không chặn loop.
                print(f"bughub-watch: bỏ qua merge {base} — {failed['detail']}", file=sys.stderr)
                skipped.append(failed)
            else:
                merged.append(base)
    ahead = 0
    if remote and ref_exists(f"refs/remotes/origin/{branch}", cwd):
        ahead = int(out("rev-list", "--count", f"origin/{branch}..HEAD", cwd=cwd))
    return {"result": "OK", "mode": cfg["mode"], "branch": branch, "merged": merged, "skipped": skipped,
            "dirty": dirty_tracked(cwd)[:50], "ahead": ahead, "head": out("rev-parse", "HEAD", cwd=cwd), "remote": remote}


def do_push(cfg: dict, cwd: Path) -> dict:
    branch = check_ready(cfg, cwd)
    if not has_origin(cwd):
        raise Stop(EXIT_CONFIG, "NO_ORIGIN", "repo chưa có remote origin")
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
        print(f"bughub-watch: push lần {attempt} lỗi — {last}", file=sys.stderr)
    raise wait("PUSH_REJECTED", f"push '{branch}' lỗi sau {PUSH_ATTEMPTS} lần (không force): {last}", branch=branch)


def parse_shelve_args(argv: list[str]) -> tuple[str, str, list[str]]:
    if "--" not in argv:
        raise Stop(EXIT_CONFIG, "BAD_ARGS", "shelve cần `-- <file>...`")
    head, files = argv[:argv.index("--")], argv[argv.index("--") + 1:]
    opts = {}
    it = iter(head)
    for arg in it:
        if arg not in ("--bug", "--reason"):
            raise Stop(EXIT_CONFIG, "BAD_ARGS", f"tham số lạ: {arg}")
        opts[arg] = next(it, "")
    bug, reason = opts.get("--bug", ""), " ".join(opts.get("--reason", "").split())
    if not re.fullmatch(r"[1-9][0-9]*", bug):
        raise Stop(EXIT_CONFIG, "BAD_ARGS", "--bug phải là số bug nguyên dương")
    if not files:
        raise Stop(EXIT_CONFIG, "BAD_ARGS", "shelve cần ít nhất một file")
    return bug, reason[:120], files


def do_shelve(cwd: Path, argv: list[str]) -> dict:
    bug, reason, files = parse_shelve_args(argv)
    root = repo_root(cwd)
    paths = []
    for name in files:
        path = (root / name).resolve()
        if root not in path.parents or ".git" in path.relative_to(root).parts:
            raise Stop(EXIT_CONFIG, "BAD_ARGS", f"file nằm ngoài repo: {name}")
        paths.append(path.relative_to(root).as_posix())
    # Chỉ cất path thật sự đổi (đã track hoặc mới tạo) — pathspec không khớp gì làm stash báo lỗi.
    status = git("status", "--porcelain", "-z", "--untracked-files=all", "--", *paths, cwd=root).stdout.split("\0")
    changed, skip_next = [], False
    for entry in status:
        if skip_next or not entry:
            skip_next = False
            continue
        changed.append(entry[3:])
        skip_next = entry[0] in "RC"  # rename/copy: entry kế là path cũ
    if not changed:
        return {"result": "NOTHING", "bug": int(bug), "message": "không còn thay đổi nào để cất"}
    message = f"bughub #{bug} released" + (f": {reason}" if reason else "")
    git("stash", "push", "--include-untracked", "-m", message, "--", *changed, cwd=root)
    return {"result": "SHELVED", "bug": int(bug), "stash": out("rev-parse", "stash@{0}", cwd=root),
            "files": changed, "message": f"đã cất vào git stash \"{message}\" — lấy lại: git stash apply <stash>"}


def do_publish(cfg: dict, cwd: Path) -> dict:
    if cfg["mode"] != "bot" or not cfg["mergeToBase"]:
        return {"result": "DISABLED", "branch": cfg["branch"] or current_branch(cwd),
                "message": "fix đã nằm trên nhánh đích" if cfg["mode"] != "bot" else
                           "mergeToBase=false — fix chỉ nằm trên nhánh bot, dev tự merge"}
    check_ready(cfg, cwd)
    base = cfg["baseBranch"]
    if not has_origin(cwd):
        holder = checked_out_at(base, cwd)
        if holder:
            raise Stop(EXIT_WAIT, "BASE_CHECKED_OUT",
                       f"không có remote và '{base}' đang mở ở {holder} — không dời được nhánh đó an toàn, dev merge tay")
        if not ref_exists(f"refs/heads/{base}", cwd):
            raise Stop(EXIT_CONFIG, "BASE_UNKNOWN", f"không thấy nhánh chính '{base}'")
        if not is_ancestor(base, "HEAD", cwd):
            merge(base, cwd)
        old = out("rev-parse", base, cwd=cwd)
        git("update-ref", f"refs/heads/{base}", "HEAD", old, cwd=cwd)
        return {"result": "PUBLISHED", "base": base, "head": out("rev-parse", "HEAD", cwd=cwd), "remote": False}

    for attempt in (1, 2):
        fetch(cwd)
        remote_base = f"origin/{base}"
        if not ref_exists(f"refs/remotes/{remote_base}", cwd):
            raise Stop(EXIT_CONFIG, "BASE_UNKNOWN", f"origin không có nhánh '{base}'")
        if not is_ancestor(remote_base, "HEAD", cwd):
            merge(remote_base, cwd)
            push = git("push", "--quiet", "origin", f"HEAD:refs/heads/{cfg['branch']}", cwd=cwd, check=False)
            if push.returncode != 0:
                raise Stop(EXIT_NETWORK, "PUSH_FAILED", f"push nhánh bot lỗi: {push.stderr.strip()}")
        push = git("push", "--quiet", "origin", f"HEAD:refs/heads/{base}", cwd=cwd, check=False)
        if push.returncode == 0:
            return {"result": "PUBLISHED", "base": base, "head": out("rev-parse", "HEAD", cwd=cwd), "remote": True}
        if attempt == 2:
            raise Stop(EXIT_NETWORK, "PUSH_FAILED", f"push lên '{base}' bị từ chối (không force): {push.stderr.strip()}")
        print(f"bughub-watch: '{base}' vừa chạy tiếp trên origin, merge lại rồi thử lần nữa", file=sys.stderr)
    raise AssertionError("unreachable")


def do_pause(argv: list[str]) -> dict:
    opts = dict(zip(argv[::2], argv[1::2]))
    if len(argv) % 2 or any(key not in ("--attempt", "--seconds") for key in opts):
        raise Stop(EXIT_CONFIG, "BAD_ARGS", "pause [--attempt K] [--seconds S]")
    try:
        attempt = max(1, int(opts.get("--attempt", "1")))
        seconds = int(opts["--seconds"]) if "--seconds" in opts else PAUSE_STEPS[min(attempt, len(PAUSE_STEPS)) - 1]
    except ValueError:
        raise Stop(EXIT_CONFIG, "BAD_ARGS", "--attempt / --seconds phải là số nguyên") from None
    seconds = max(0, seconds)
    print(f"bughub-watch: nghỉ {seconds}s (lần lỗi thứ {attempt}) rồi thử lại", file=sys.stderr)
    time.sleep(seconds)
    return {"result": "RESUME", "attempt": attempt, "slept": seconds}


# --------------------------------------------------------------------------------------------------
# editor / notice
# --------------------------------------------------------------------------------------------------

def state_file(cwd: Path, name: str) -> Path:
    """File trạng thái của watch trong git dir của worktree này (không bao giờ bị commit, sống qua /clear)."""
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
    """Chạy restart-unity (sh / ps1) với đúng một cờ, trả dòng status cuối của nó."""
    if os.name == "nt":
        ps_flag = {"--status": "-Status", "--open-only": "-OpenOnly", "--no-launch": "-NoLaunch"}[flag]
        cmd = ["powershell", "-NoProfile", "-ExecutionPolicy", "Bypass", "-File",
               str(RESTART_UNITY_DIR / "restart-unity.ps1"), ps_flag, "-Project", str(root)]
    else:
        cmd = ["bash", str(RESTART_UNITY_DIR / "restart-unity.sh"), flag, "--project", str(root)]
    try:
        proc = subprocess.run(cmd, capture_output=True, text=True, encoding="utf-8", errors="replace", timeout=120)
    except (OSError, subprocess.TimeoutExpired) as err:
        return f"ERROR không chạy được restart-unity: {err}"
    lines = [line.strip() for line in proc.stdout.splitlines() if line.strip()]
    return lines[-1] if lines else f"ERROR restart-unity không in gì (exit {proc.returncode}): {proc.stderr.strip()[:200]}"


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
    """Editor GUI của project này + có phải bot mở không. Marker của Editor đã tắt / bị thay thì xoá."""
    line = restart_unity("--status", root)
    match = re.fullmatch(r"RUNNING gui=(\S+) pids=\[([^\]]*)\]", line)
    if line == "NOT_RUNNING":
        gui, pids = None, []
    elif match:
        gui = int(match.group(1)) if match.group(1).isdigit() else None
        pids = [int(x) for x in match.group(2).split() if x.isdigit()]
    else:
        raise wait("EDITOR_STATUS_FAILED", f"không đọc được trạng thái Unity Editor: {line}")
    marker_path = state_file(root, "editor.json")
    marker = read_json(marker_path)
    owned = bool(marker and gui and marker.get("pid") == gui)
    if marker and not owned:
        marker_path.unlink(missing_ok=True)
    return {"running": bool(pids), "pid": gui, "pids": pids, "owned": owned}


def registry_path() -> Path:
    """Registry instance của plugin Unity MCP (cùng đường dẫn MCP server đọc)."""
    override = os.environ.get("UNITY_INSTANCE_REGISTRY")
    if override:
        return Path(override)
    base = Path(os.environ.get("LOCALAPPDATA") or Path.home() / "AppData" / "Local") if os.name == "nt" \
        else Path.home() / ".local" / "share"
    return base / "UnityMCP" / "instances.json"


def parse_utc(value: object) -> datetime | None:
    """'2026-10-09T02:38:06.8293400Z' (C# ghi 7 chữ số thập phân — fromisoformat không nhận)."""
    match = re.fullmatch(r"(\d{4}-\d\d-\d\dT\d\d:\d\d:\d\d)(?:\.(\d+))?(?:Z|\+00:00)?", str(value or ""))
    if not match:
        return None
    stamp = datetime.strptime(match.group(1), "%Y-%m-%dT%H:%M:%S").replace(tzinfo=timezone.utc)
    return stamp.replace(microsecond=int((match.group(2) or "0")[:6].ljust(6, "0")))


def same_path(a: object, b: Path) -> bool:
    return bool(a) and os.path.normcase(os.path.realpath(str(a))) == os.path.normcase(os.path.realpath(str(b)))


def registry_entry(root: Path, pid: int) -> dict | None:
    """Entry còn sống của đúng process Editor này — có nghĩa plugin MCP đã chạy (Editor load + compile xong)."""
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
            raise wait(reason, f"không mở được Unity Editor: {detail}")
        pid, opened = int(match.group(2)), match.group(1) == "OPENED"
        if opened:
            write_json(state_file(root, "editor.json"),
                       {"pid": pid, "projectPath": str(root), "openedAt": datetime.now(timezone.utc).isoformat()})
            print(f"bughub-watch: đã mở Unity Editor pid {pid} — chờ load xong (tối đa {timeout}s)", file=sys.stderr)
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
            raise wait("EDITOR_EXITED", f"Unity Editor pid {pid} đã thoát trước khi sẵn sàng (crash, hỏi license, hoặc bị tắt tay)",
                       pid=pid)
        if time.monotonic() - started >= timeout:
            raise wait("EDITOR_NOT_READY",
                       f"Unity Editor pid {pid} chưa sẵn sàng sau {timeout // 60} phút — đang import/compile lâu, kẹt một dialog "
                       "(license, nâng version…) cần người bấm, hoặc project thiếu plugin Unity MCP", pid=pid, owned=owned)
        if time.monotonic() - last_log >= 60:
            print(f"bughub-watch: Editor pid {pid} vẫn đang load ({int(time.monotonic() - started)}s)…", file=sys.stderr)
            last_log = time.monotonic()
        time.sleep(EDITOR_POLL_SECONDS)


def editor_close(root: Path, wait_s: int) -> dict:
    marker = read_json(state_file(root, "editor.json"))
    status = editor_status(root)
    if not status["owned"]:
        if marker and not status["running"]:
            # Editor bot mở đã tự thoát (lệnh thoát qua MCP chạy xong trước khi gọi close) — marker đã được dọn.
            return {"result": "CLOSED", "pid": marker.get("pid")}
        return {"result": "NOT_OWNED", "running": status["running"], "pid": status["pid"],
                "message": "Editor không do bot mở (hoặc đã tắt) — để nguyên"}
    pid = status["pid"]
    deadline = time.monotonic() + max(0, wait_s)
    while pid_alive(pid) and time.monotonic() < deadline:
        time.sleep(2)
    graceful = not pid_alive(pid)
    if not graceful or restart_unity("--status", root) != "NOT_RUNNING":
        # Editor không tự thoát (hoặc còn import worker mồ côi) → kill cả cụm của project.
        line = restart_unity("--no-launch", root)
        if line != "KILLED":
            raise wait("EDITOR_KILL_FAILED", f"không tắt được Unity Editor pid {pid}: {line}", pid=pid)
    state_file(root, "editor.json").unlink(missing_ok=True)
    return {"result": "CLOSED" if graceful else "KILLED", "pid": pid}


def int_option(argv: list[str], name: str, default: int) -> int:
    if not argv:
        return default
    if len(argv) != 2 or argv[0] != name or not re.fullmatch(r"[0-9]+", argv[1]):
        raise Stop(EXIT_CONFIG, "BAD_ARGS", f"tham số lạ: {' '.join(argv)} (chỉ nhận {name} <giây>)")
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
        raise Stop(EXIT_CONFIG, "BAD_ARGS", "--reason phải là mã IN_HOA (vd EDITOR_NOT_READY)")
    if not numbers or any(not re.fullmatch(r"[1-9][0-9]*", n) for n in numbers):
        raise Stop(EXIT_CONFIG, "BAD_ARGS", "sau -- là số bug nguyên dương")
    reason, bugs = head[1], [int(n) for n in numbers]
    state = read_json(path) or {}
    posted = [int(n) for n in state.get("posted", []) if isinstance(n, int)] if state.get("reason") == reason else []
    if sub == "check":
        return {"result": "OK", "reason": reason, "post": [n for n in bugs if n not in posted]}
    write_json(path, {"reason": reason, "posted": sorted(set(posted + bugs))})
    return {"result": "OK", "reason": reason, "marked": bugs}


# --------------------------------------------------------------------------------------------------
# start
# --------------------------------------------------------------------------------------------------

def ensure_branch(cfg: dict, cwd: Path, remote: bool) -> None:
    branch = cfg["branch"]
    if ref_exists(f"refs/heads/{branch}", cwd):
        return
    if remote and ref_exists(f"refs/remotes/origin/{branch}", cwd):
        git("branch", "--track", branch, f"origin/{branch}", cwd=cwd)
        print(f"bughub-watch: tạo nhánh {branch} theo origin/{branch}", file=sys.stderr)
        return
    start_point = base_ref(cfg, cwd, remote)
    git("branch", "--no-track", branch, start_point, cwd=cwd)
    print(f"bughub-watch: tạo nhánh {branch} từ {start_point} (push lần đầu khi sửa xong bug đầu tiên)", file=sys.stderr)


def confirm(question: str, assume_yes: bool) -> bool:
    if assume_yes:
        return True
    if not sys.stdin.isatty():
        return False
    try:
        return input(f"{question} [y/N] ").strip().lower() in ("y", "yes")
    except EOFError:
        return False


def prepare_workdir(cfg: dict, cwd: Path, assume_yes: bool) -> Path:
    """Thư mục chạy cửa sổ watch. Theo nhánh: chính checkout này. Nhánh bot: worktree riêng / chuyển nhánh."""
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
                raise Stop(EXIT_CONFIG, "WORKTREE_PATH_TAKEN", f"{path} đã tồn tại nhưng không phải worktree — đổi bugHub.watch.worktreePath")
            print(f"bughub-watch: dùng lại worktree {path}", file=sys.stderr)
            return path
        holder = checked_out_at(branch, cwd)
        if holder:
            raise Stop(EXIT_CONFIG, "BRANCH_CHECKED_OUT", f"nhánh {branch} đang mở ở {holder} — đóng/đổi nhánh ở đó trước", path=holder)
        ensure_branch(cfg, cwd, remote)
        git("worktree", "add", str(path), branch, cwd=cwd)
        print(f"bughub-watch: tạo worktree {path} trên {branch}. Đây là một project Unity riêng — muốn có "
              "compile-check thì mở Unity Editor cho đúng thư mục này (lần đầu import Library lâu).", file=sys.stderr)
        return path

    root = repo_root(cwd)
    here = current_branch(root)
    if here == branch:
        return root
    holder = checked_out_at(branch, cwd)
    if holder:
        raise Stop(EXIT_CONFIG, "BRANCH_CHECKED_OUT", f"nhánh {branch} đang mở ở {holder} — chạy watch ở đó, hoặc bật bugHub.watch.worktree", path=holder)
    dirty = dirty_tracked(root)
    if dirty:
        raise Stop(EXIT_CONFIG, "DIRTY", f"checkout {root} đang có thay đổi chưa commit — commit/stash trước khi chuyển sang {branch}", files=dirty[:20])
    if not confirm(f"Checkout {root} sẽ chuyển từ '{here or '(detached)'}' sang nhánh bot '{branch}' và dành cho cửa sổ watch "
                   f"(Unity Editor mở thư mục này sẽ import lại). Tiếp tục?", assume_yes):
        raise Stop(EXIT_CONFIG, "NOT_CONFIRMED", "chưa chuyển nhánh — chạy lại với --yes, hoặc bật bugHub.watch.worktree để dùng thư mục riêng")
    ensure_branch(cfg, root, remote)
    git("checkout", "--quiet", branch, cwd=root)
    return root


def set_title(text: str) -> None:
    if sys.stdout.isatty() and os.name != "nt":
        sys.stdout.write(f"\033]0;{text}\007")
        sys.stdout.flush()


def do_start(cfg: dict, cwd: Path, argv: list[str]) -> dict:
    if not cfg["projectCode"]:
        raise Stop(EXIT_CONFIG, "NOT_CONFIGURED", "bugHub.projectCode rỗng — chạy /bughub-setup trước")
    assume_yes = "--yes" in argv
    launch = "--no-launch" not in argv
    extra = argv[argv.index("--") + 1:] if "--" in argv else []
    unknown = [a for a in (argv[:argv.index("--")] if "--" in argv else argv) if a not in ("--yes", "--no-launch")]
    if unknown:
        raise Stop(EXIT_CONFIG, "BAD_ARGS", f"tham số lạ: {' '.join(unknown)}")
    if launch and not shutil.which("claude"):
        raise Stop(EXIT_CONFIG, "NO_CLAUDE", "không thấy lệnh `claude` trong PATH")

    workdir = prepare_workdir(cfg, cwd, assume_yes)
    try:
        synced = do_sync(cfg, workdir)
    except Stop as stop:
        if stop.code != EXIT_WAIT:
            raise
        # Loop tự pause + sync lại trước mỗi bug — chưa đồng bộ được lúc mở cửa sổ không phải lý do để không mở.
        print(f"bughub-watch: chưa sync được ({stop.payload['reason']}) — loop sẽ thử lại: {stop.payload['message']}", file=sys.stderr)
        synced = {**stop.payload, "branch": stop.payload.get("branch") or current_branch(workdir)}
    branch = synced.get("branch") or cfg["branch"]
    result = {**synced, "result": "READY", "mode": cfg["mode"], "workdir": str(workdir), "mergeToBase": cfg["mergeToBase"]}
    target = f"nhánh bot {branch} (nhánh chính {cfg['baseBranch']}, merge vào nhánh chính: {'có' if cfg['mergeToBase'] else 'không'})" \
        if cfg["mode"] == "bot" else f"theo nhánh đang mở: {branch or '(detached)'}"
    print(f"bughub-watch: {cfg['projectCode']} · {target} · {workdir}", file=sys.stderr)
    if not launch:
        return result

    print(json.dumps(result, ensure_ascii=False))
    sys.stdout.flush()
    set_title(f"BugHub watch · {cfg['projectCode']} · {branch}")
    os.chdir(workdir)
    cmd = ["claude", *extra, WATCH_PROMPT]
    if os.name == "nt":
        sys.exit(subprocess.call(cmd))
    os.execvp(cmd[0], cmd)
    raise AssertionError("unreachable")


# --------------------------------------------------------------------------------------------------

def main(argv: list[str]) -> int:
    # Windows: stdio bị pipe (Bash tool, subprocess) mặc định là cp1252 strict → in tiếng Việt là crash, mất JSON.
    for stream in (sys.stdout, sys.stderr):
        if hasattr(stream, "reconfigure"):
            stream.reconfigure(encoding="utf-8", errors="replace")
    command = argv[0] if argv and not argv[0].startswith("-") else "start"
    rest = argv[1:] if argv and not argv[0].startswith("-") else argv
    cwd = Path.cwd()
    try:
        if command == "pause":
            # Không cần repo / config — chính lúc config hỏng skill cũng phải pause được.
            result = do_pause(rest)
        else:
            if not ok("rev-parse", "--git-dir", cwd=cwd):
                raise Stop(EXIT_CONFIG, "NOT_A_REPO", "không đứng trong một repo git")
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
            elif command == "publish":
                result = do_publish(cfg, repo_root(cwd))
            elif command == "start":
                result = do_start(cfg, cwd, rest)
            elif command == "editor":
                result = do_editor(cwd, rest)
            elif command == "notice":
                result = do_notice(cwd, rest)
            else:
                raise Stop(EXIT_CONFIG, "BAD_ARGS",
                           f"lệnh lạ: {command} (start | sync | push | shelve | publish | pause | config | editor | notice)")
    except Stop as stop:
        print(f"bughub-watch: {stop.payload['result']} — {stop.payload['message']}", file=sys.stderr)
        print(json.dumps(stop.payload, ensure_ascii=False))
        return stop.code
    print(json.dumps(result, ensure_ascii=False))
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
