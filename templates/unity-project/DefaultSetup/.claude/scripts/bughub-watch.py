#!/usr/bin/env python3
"""bughub-watch.py — nhánh riêng cho cửa sổ `/fix-bug --watch` (macOS / Linux / Windows).

Cửa sổ watch chạy trên một nhánh dành riêng cho bot (`bugHub.watch.branch`, vd `AutoFixBug`), không
phải nhánh dev đang làm. Trước mỗi bug, nhánh đó merge bản mới nhất của nhánh chính
(`bugHub.watch.baseBranch`, rỗng = `defaultBaseBranch`) để không sửa trên code cũ; sửa xong thì commit
+ push lên chính nhánh bot. Chỉ merge ngược vào nhánh chính khi project bật `bugHub.watch.mergeToBase`.
Chạy ngay trong checkout này hay trong một `git worktree` riêng do `bugHub.watch.worktree` quyết định
(mỗi project tự chọn trong `.claude/project-profile.json`).

Lệnh:
  start [--yes] [--no-launch] [-- <tham số claude>]
        Chuẩn bị thư mục + nhánh (tạo worktree / tạo nhánh từ nhánh chính nếu chưa có), chạy `sync`,
        rồi mở `claude "/fix-bug --watch"` ngay trong cửa sổ terminal này. Dev chạy lệnh này trong một
        pane riêng. `--yes`: không hỏi lại khi phải chuyển nhánh của checkout này (worktree=false).
        `--no-launch`: chỉ chuẩn bị, không mở claude. Tham số sau `--` chuyển nguyên cho claude
        (vd `-- --permission-mode bypassPermissions`).
  sync  Gọi bởi /fix-bug --watch TRƯỚC khi nhận mỗi bug: phải đứng trên nhánh bot, không có thay đổi
        chưa commit trên file đã track, rồi fetch + merge nhánh bot trên remote và nhánh chính vào.
  publish
        Gọi sau khi fix đã commit + push: mergeToBase=false → `DISABLED`; true → đưa HEAD lên nhánh chính
        (fast-forward, merge nhánh chính vào trước nếu nó đã chạy tiếp; không bao giờ force).
  config
        In config đã gộp + nhánh hiện tại (skill đọc ở preflight).

Dòng cuối stdout luôn là đúng một JSON `{"result": "...", ...}`; log cho người đọc ra stderr.
Exit: 0 xong (kể cả `DISABLED`) · 2 config / tham số sai · 3 sai trạng thái (sai nhánh, còn thay đổi
chưa commit, nhánh đang mở ở worktree khác) · 4 merge conflict (đã `merge --abort`, cây làm việc như cũ)
· 5 lệnh git mạng lỗi (fetch đã tự thử lại ~4 phút / push).
"""

from __future__ import annotations

import json
import os
import shutil
import subprocess
import sys
import time
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))
from project_profile import profile  # noqa: E402

EXIT_CONFIG = 2
EXIT_STATE = 3
EXIT_CONFLICT = 4
EXIT_NETWORK = 5

WATCH_PROMPT = "/fix-bug --watch"
# Giây chờ giữa các lần fetch lại (~4 phút tổng) trước khi báo FETCH_FAILED.
FETCH_RETRY_DELAYS = (15, 60, 180)


class Stop(Exception):
    """Dừng lệnh với một exit code + JSON kết quả."""

    def __init__(self, code: int, result: str, message: str, **extra):
        super().__init__(message)
        self.code = code
        self.payload = {"result": result, "message": message, **extra}


# --------------------------------------------------------------------------------------------------
# git
# --------------------------------------------------------------------------------------------------

def git(*args: str, cwd: Path | None = None, check: bool = True) -> subprocess.CompletedProcess:
    proc = subprocess.run(["git", *args], cwd=cwd, capture_output=True, text=True, encoding="utf-8", errors="replace")
    if check and proc.returncode != 0:
        raise Stop(EXIT_STATE, "GIT_FAILED", f"git {' '.join(args)}: {proc.stderr.strip() or proc.stdout.strip()}")
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
    """fetch origin; lỗi (mạng chập chờn lúc chạy đêm) thì thử lại theo FETCH_RETRY_DELAYS rồi mới bỏ."""
    for delay in (*FETCH_RETRY_DELAYS, None):
        proc = git("fetch", "--quiet", "--prune", "origin", cwd=cwd, check=False)
        if proc.returncode == 0:
            return
        if delay is None:
            raise Stop(EXIT_NETWORK, "FETCH_FAILED", f"git fetch origin lỗi: {proc.stderr.strip()}")
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


def merge_in_progress(cwd: Path) -> bool:
    path = Path(out("rev-parse", "--git-path", "MERGE_HEAD", cwd=cwd))
    return (path if path.is_absolute() else cwd / path).exists()


def merge(ref: str, cwd: Path) -> None:
    """Merge `ref` vào HEAD; conflict / bị chặn → abort, raise MERGE_CONFLICT."""
    proc = git("merge", "--no-edit", ref, cwd=cwd, check=False)
    if proc.returncode == 0:
        return
    conflicted = out("diff", "--name-only", "--diff-filter=U", cwd=cwd).splitlines()
    git("merge", "--abort", cwd=cwd, check=False)
    detail = proc.stderr.strip() or proc.stdout.strip()
    raise Stop(EXIT_CONFLICT, "MERGE_CONFLICT", f"merge {ref} không tự xong được — dev resolve tay rồi chạy lại. {detail}",
               ref=ref, files=conflicted[:20])


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


def require_branch(cfg: dict) -> None:
    if not cfg["branch"]:
        raise Stop(EXIT_CONFIG, "NOT_CONFIGURED",
                   "Chưa khai bugHub.watch.branch trong .claude/project-profile.json — /fix-bug --watch sẽ commit lên nhánh đang mở")


def base_ref(cfg: dict, cwd: Path, remote: bool) -> str:
    base = cfg["baseBranch"]
    if remote and ref_exists(f"refs/remotes/origin/{base}", cwd):
        return f"origin/{base}"
    if ref_exists(f"refs/heads/{base}", cwd):
        return base
    raise Stop(EXIT_CONFIG, "BASE_UNKNOWN", f"không thấy nhánh chính '{base}' (cả local lẫn origin) — kiểm bugHub.watch.baseBranch")


# --------------------------------------------------------------------------------------------------
# sync / publish
# --------------------------------------------------------------------------------------------------

def check_ready(cfg: dict, cwd: Path) -> None:
    branch = current_branch(cwd)
    if branch != cfg["branch"]:
        raise Stop(EXIT_STATE, "WRONG_BRANCH",
                   f"đang ở '{branch or '(detached)'}', không phải nhánh watch '{cfg['branch']}' — mở cửa sổ bằng `python3 .claude/scripts/bughub-watch.py start`",
                   branch=branch)
    if merge_in_progress(cwd):
        raise Stop(EXIT_STATE, "DIRTY", "đang có merge dở trong thư mục watch — dev xử lý tay trước")
    dirty = dirty_tracked(cwd)
    if dirty:
        raise Stop(EXIT_STATE, "DIRTY",
                   "thư mục watch còn thay đổi chưa commit (thường là phần sửa dở của một bug đã release) — dev xem rồi commit / bỏ trước",
                   files=dirty[:20])


def do_sync(cfg: dict, cwd: Path) -> dict:
    require_branch(cfg)
    check_ready(cfg, cwd)
    remote = has_origin(cwd)
    if remote:
        fetch(cwd)
    merged = []
    own = f"origin/{cfg['branch']}"
    if remote and ref_exists(f"refs/remotes/{own}", cwd) and not is_ancestor(own, "HEAD", cwd):
        merge(own, cwd)
        merged.append(own)
    base = base_ref(cfg, cwd, remote)
    if not is_ancestor(base, "HEAD", cwd):
        merge(base, cwd)
        merged.append(base)
    return {"result": "OK", "branch": cfg["branch"], "base": base, "merged": merged,
            "head": out("rev-parse", "HEAD", cwd=cwd), "remote": remote}


def do_publish(cfg: dict, cwd: Path) -> dict:
    require_branch(cfg)
    if not cfg["mergeToBase"]:
        return {"result": "DISABLED", "branch": cfg["branch"],
                "message": "mergeToBase=false — fix chỉ nằm trên nhánh watch, dev tự merge"}
    check_ready(cfg, cwd)
    base = cfg["baseBranch"]
    if not has_origin(cwd):
        holder = checked_out_at(base, cwd)
        if holder:
            raise Stop(EXIT_STATE, "BASE_CHECKED_OUT",
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
                raise Stop(EXIT_NETWORK, "PUSH_FAILED", f"push nhánh watch lỗi: {push.stderr.strip()}")
        push = git("push", "--quiet", "origin", f"HEAD:refs/heads/{base}", cwd=cwd, check=False)
        if push.returncode == 0:
            return {"result": "PUBLISHED", "base": base, "head": out("rev-parse", "HEAD", cwd=cwd), "remote": True}
        if attempt == 2:
            raise Stop(EXIT_NETWORK, "PUSH_FAILED", f"push lên '{base}' bị từ chối (không force): {push.stderr.strip()}")
        print(f"bughub-watch: '{base}' vừa chạy tiếp trên origin, merge lại rồi thử lần nữa", file=sys.stderr)
    raise AssertionError("unreachable")


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
    remote = has_origin(cwd)
    if remote:
        fetch(cwd)
    branch = cfg["branch"]

    if cfg["worktree"]:
        path = Path(cfg["worktreePath"])
        if path.exists():
            if not (path / ".git").exists():
                raise Stop(EXIT_STATE, "WORKTREE_PATH_TAKEN", f"{path} đã tồn tại nhưng không phải worktree — đổi bugHub.watch.worktreePath")
            print(f"bughub-watch: dùng lại worktree {path}", file=sys.stderr)
            return path
        holder = checked_out_at(branch, cwd)
        if holder:
            raise Stop(EXIT_STATE, "BRANCH_CHECKED_OUT", f"nhánh {branch} đang mở ở {holder} — đóng/đổi nhánh ở đó trước", path=holder)
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
        raise Stop(EXIT_STATE, "BRANCH_CHECKED_OUT", f"nhánh {branch} đang mở ở {holder} — chạy watch ở đó, hoặc bật bugHub.watch.worktree", path=holder)
    dirty = dirty_tracked(root)
    if dirty:
        raise Stop(EXIT_STATE, "DIRTY", f"checkout {root} đang có thay đổi chưa commit — commit/stash trước khi chuyển sang {branch}", files=dirty[:20])
    if not confirm(f"Checkout {root} sẽ chuyển từ '{here or '(detached)'}' sang nhánh bot '{branch}' và dành cho cửa sổ watch "
                   f"(Unity Editor mở thư mục này sẽ import lại). Tiếp tục?", assume_yes):
        raise Stop(EXIT_STATE, "NOT_CONFIRMED", f"chưa chuyển nhánh — chạy lại với --yes, hoặc bật bugHub.watch.worktree để dùng thư mục riêng")
    ensure_branch(cfg, root, remote)
    git("checkout", "--quiet", branch, cwd=root)
    return root


def set_title(text: str) -> None:
    if sys.stdout.isatty() and os.name != "nt":
        sys.stdout.write(f"\033]0;{text}\007")
        sys.stdout.flush()


def do_start(cfg: dict, cwd: Path, argv: list[str]) -> dict:
    require_branch(cfg)
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
    synced = do_sync(cfg, workdir)
    result = {**synced, "result": "READY", "workdir": str(workdir), "mergeToBase": cfg["mergeToBase"]}
    print(f"bughub-watch: {cfg['projectCode']} · nhánh {cfg['branch']} (nhánh chính {cfg['baseBranch']}, "
          f"merge vào nhánh chính: {'có' if cfg['mergeToBase'] else 'không'}) · {workdir}", file=sys.stderr)
    if not launch:
        return result

    print(json.dumps(result, ensure_ascii=False))
    sys.stdout.flush()
    set_title(f"BugHub watch · {cfg['projectCode']} · {cfg['branch']}")
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
        if not ok("rev-parse", "--git-dir", cwd=cwd):
            raise Stop(EXIT_CONFIG, "NOT_A_REPO", "không đứng trong một repo git")
        cfg = load_config(cwd)
        if command == "config":
            result = {"result": "OK", **cfg, "currentBranch": current_branch(cwd),
                      "onWatchBranch": bool(cfg["branch"]) and current_branch(cwd) == cfg["branch"]}
        elif command == "sync":
            result = do_sync(cfg, repo_root(cwd))
        elif command == "publish":
            result = do_publish(cfg, repo_root(cwd))
        elif command == "start":
            result = do_start(cfg, cwd, rest)
        else:
            raise Stop(EXIT_CONFIG, "BAD_ARGS", f"lệnh lạ: {command} (start | sync | publish | config)")
    except Stop as stop:
        print(f"bughub-watch: {stop.payload['result']} — {stop.payload['message']}", file=sys.stderr)
        print(json.dumps(stop.payload, ensure_ascii=False))
        return stop.code
    print(json.dumps(result, ensure_ascii=False))
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
