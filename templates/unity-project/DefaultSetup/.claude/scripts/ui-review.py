#!/usr/bin/env python3
"""Serve/generate the UI review dashboard and freeze explicit human approvals."""

from __future__ import annotations

import argparse
import copy
import hashlib
import hmac
import json
import os
import re
import secrets
import shutil
import signal
import struct
import subprocess
import sys
import tempfile
import time
import webbrowser
from datetime import datetime, timezone
from http.server import BaseHTTPRequestHandler, HTTPServer
from pathlib import Path
from urllib.parse import parse_qs, unquote, urlparse

from ui_spec_common import ROOT, apply_json_patch, canonical_json, load_spec, spec_hash


MOCKUPS_ROOT = ROOT / "TechSpec" / "Mockups"
DASHBOARD_PATH = MOCKUPS_ROOT / "_ui-review.html"
REGEN_QUEUE = MOCKUPS_ROOT / "_regen-queue.jsonl"
DEFAULT_PORT = 4176
DASHBOARD_TEMPLATE = ROOT / "ui-catalog" / "ui-review-dashboard.template.html"
VALIDATOR = ROOT / ".claude" / "scripts" / "ui-spec-validator.py"
RENDERER = ROOT / ".claude" / "scripts" / "ui-spec-render.py"
CLAUDE_GLOBAL_CONFIG = Path.home() / ".claude.json"
PENDING_RE = re.compile(r"groundTruth=PENDING-APPROVAL:([^\s`]+)")
HASH_RE = re.compile(r"^[0-9a-f]{64}$")
DESIGN_W, DESIGN_H = 1080, 2400
# Regenerate agent tuning — user-chosen: at least Sonnet, effort xhigh (NEVER haiku). stream-json
# so mid-run progress is observable (default text output only prints the FINAL result → empty log).
REGEN_MODEL = "claude-sonnet-5"
REGEN_MODEL_LABEL = "Sonnet 5"
REGEN_EFFORT = "xhigh"
REGEN_STALE_SECONDS = 15 * 60  # a running status older than this with a dead PID is treated as crashed


def utc_now() -> str:
    return datetime.now(timezone.utc).isoformat()


def atomic_json(path: Path, payload: dict) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    with tempfile.NamedTemporaryFile("w", encoding="utf-8", dir=path.parent, delete=False) as handle:
        handle.write(canonical_json(payload))
        temp_path = Path(handle.name)
    os.replace(temp_path, path)


def atomic_pretty_json(path: Path, payload: dict) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    with tempfile.NamedTemporaryFile("w", encoding="utf-8", dir=path.parent, delete=False) as handle:
        handle.write(json.dumps(payload, ensure_ascii=False, indent=2) + "\n")
        temp_path = Path(handle.name)
    os.replace(temp_path, path)


def atomic_bytes(path: Path, payload: bytes) -> None:
    with tempfile.NamedTemporaryFile("wb", dir=path.parent, delete=False) as handle:
        handle.write(payload)
        temp_path = Path(handle.name)
    os.replace(temp_path, path)


def load_json_object(path: Path) -> dict:
    if not path.exists():
        return {}
    payload = json.loads(path.read_text(encoding="utf-8"))
    if not isinstance(payload, dict):
        raise RuntimeError(f"Expected JSON object: {path}")
    return payload


def ensure_claude_launch_config() -> dict:
    """Trust this repo so the dashboard's claude-cli:// deep links open without
    a trust dialog. Deliberately does NOT pin model/effort (the session default
    — user-chosen — applies) and never enables bypassPermissions."""
    global_config = load_json_object(CLAUDE_GLOBAL_CONFIG)
    projects = global_config.setdefault("projects", {})
    project = projects.setdefault(str(ROOT.resolve()), {})
    project["hasTrustDialogAccepted"] = True
    atomic_pretty_json(CLAUDE_GLOBAL_CONFIG, global_config)
    return {"trusted": True, "model": "Session default", "effort": "Session default"}


def sha256_file(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as handle:
        for chunk in iter(lambda: handle.read(1024 * 1024), b""):
            digest.update(chunk)
    return digest.hexdigest()


def relative(path: Path) -> str:
    return path.resolve().relative_to(ROOT.resolve()).as_posix()


def safe_html(value: str) -> Path:
    path = (ROOT / value).resolve()
    path.relative_to(MOCKUPS_ROOT.resolve())
    if path.suffix.lower() != ".html":
        raise ValueError("Expected a mockup HTML path")
    return path


def approval_path(html_path: Path) -> Path:
    return html_path.with_suffix(".ui-approval.json")


def current_hash(html_path: Path) -> str:
    spec, _, _ = load_spec(html_path)
    return spec_hash(spec)


def matching_approval(html_path: Path) -> bool:
    evidence, png = approval_path(html_path), html_path.with_suffix(".png")
    if not evidence.exists() or not png.exists():
        return False
    try:
        payload = json.loads(evidence.read_text(encoding="utf-8"))
        return payload.get("specHash") == current_hash(html_path) and payload.get("htmlSha256") == sha256_file(html_path) and payload.get("pngSha256") == sha256_file(png)
    except (OSError, ValueError, json.JSONDecodeError):
        return False


def pending_task_htmls() -> set[str]:
    found, planning = set(), ROOT / "backlog" / "planning"
    if planning.exists():
        for task in planning.glob("*.md"):
            found.update(match.group(1) for match in PENDING_RE.finditer(task.read_text(encoding="utf-8")))
    return found


def normalize_questions(raw) -> list[dict]:
    """Return browser-safe questions. Structured option patches stay server-side; the
    dashboard only needs the label and whether the choice can be applied instantly."""
    out = []
    if not isinstance(raw, list):
        return out
    for item in raw:
        if isinstance(item, str) and item.strip():
            out.append({"q": item.strip(), "options": []})
        elif isinstance(item, dict) and isinstance(item.get("q"), str) and item["q"].strip():
            opts = []
            for option in item.get("options", []):
                if isinstance(option, str):
                    opts.append({"label": option, "instant": False})
                elif isinstance(option, dict) and isinstance(option.get("label"), str):
                    opts.append({
                        "label": option["label"],
                        "instant": isinstance(option.get("patch"), list),
                    })
            out.append({"q": item["q"].strip(), "options": opts})
    return out


def screen_record(spec: dict, html: Path, html_rel: str) -> dict:
    """Dashboard row for one screen. Carries the drafter's open `questions[]` (normalized to
    {q, options[]}) + `assumptions[]` so the review card can show what still needs a human
    decision — approving is otherwise blind to them — plus any in-flight `regen` snapshot."""
    return {
        "feature": spec.get("feature", html.parent.name),
        "screen": spec.get("screen", html.stem),
        "lane": spec.get("mockupLane", "custom"),
        "strict": isinstance(spec.get("specVersion"), int) and spec.get("specVersion") >= 1,
        "html": html_rel,
        "preview": html.relative_to(MOCKUPS_ROOT).as_posix(),
        "specHash": spec_hash(spec),
        "questions": normalize_questions(spec.get("questions", [])),
        "assumptions": [a for a in spec.get("assumptions", []) if isinstance(a, str)],
        "regen": public_regen(html_rel),
        # deterministic approve-blocker signal so the card can warn BEFORE the user clicks Approve:
        # a literal [?] in any element text is rejected by approve-mode validation.
        "placeholders": sum(
            1 for e in spec.get("elements", [])
            if isinstance(e, dict) and isinstance(e.get("text"), str) and "[?]" in e["text"]
        ),
    }


def discover_screens() -> list[dict]:
    task_pending, screens, seen = pending_task_htmls(), [], set()
    for spec_path in sorted(MOCKUPS_ROOT.rglob("*.ui-spec.json")):
        html = spec_path.with_name(spec_path.name.removesuffix(".ui-spec.json") + ".html")
        if not html.exists():
            continue
        html_rel = relative(html)
        if matching_approval(html) and html_rel not in task_pending:
            continue
        try:
            spec, _, _ = load_spec(spec_path)
        except (OSError, ValueError, json.JSONDecodeError):
            # A parallel planning session may still be replacing this pair. Keep the review
            # queue available and discover it on the next refresh once the spec is complete.
            continue
        screens.append(screen_record(spec, html, html_rel))
        seen.add(html_rel)
    for html_rel in sorted(task_pending - seen):
        try:
            html = safe_html(html_rel)
            spec, _, _ = load_spec(html)
        except (OSError, ValueError, json.JSONDecodeError):
            continue
        screens.append(screen_record(spec, html, html_rel))
    return screens


def render_dashboard_html(config: dict) -> tuple[str, list[dict]]:
    """Build the dashboard HTML string (no file write). `config` is injected as the
    page's `CONFIG` object — the serve path adds `serve`/`token`/`port` so the page
    talks to the local HTTP server via fetch() instead of the fragile URL schemes."""
    screens = discover_screens()
    template = DASHBOARD_TEMPLATE.read_text(encoding="utf-8")
    source = (
        template
        .replace("__PROJECT__", json.dumps(str(ROOT), ensure_ascii=False))
        .replace("__SCREENS__", json.dumps(screens, ensure_ascii=False).replace("</", "<\\/"))
        .replace("__CONFIG__", json.dumps(config, ensure_ascii=False))
    )
    return source, screens


def generate_dashboard(open_browser: bool = False) -> list[dict]:
    source, screens = render_dashboard_html(ensure_claude_launch_config())
    DASHBOARD_PATH.parent.mkdir(parents=True, exist_ok=True)
    DASHBOARD_PATH.write_text(source, encoding="utf-8")
    if open_browser:
        webbrowser.open(DASHBOARD_PATH.resolve().as_uri())
    return screens


def run_json(command: list[str]) -> dict:
    result = subprocess.run(command, cwd=ROOT, text=True, capture_output=True)
    try:
        payload = json.loads(result.stdout)
    except json.JSONDecodeError as exc:
        raise RuntimeError(result.stderr.strip() or result.stdout.strip() or str(exc)) from exc
    if result.returncode or not payload.get("ok"):
        raise RuntimeError("; ".join(e.get("message", str(e)) for e in payload.get("errors", [])) or "UI validation failed")
    return payload


def apply_decisions(html_rel: str, expected_hash: str, decisions: list[dict]) -> dict:
    """Apply structured human choices without an AI regenerate round."""
    html = safe_html(html_rel)
    if not html.exists() or not HASH_RE.fullmatch(expected_hash):
        raise ValueError("Invalid decision item")
    spec, source, _ = load_spec(html)
    if source.suffix != ".json" or spec.get("specVersion") != 1:
        raise ValueError("Instant choices require an authoritative v1 .ui-spec.json")
    current = spec_hash(spec)
    if current != expected_hash:
        raise ValueError(f"Hash changed: expected {expected_hash}, current {current}")
    if not isinstance(decisions, list) or not decisions or len(decisions) > 100:
        raise ValueError("Expected 1-100 structured decisions")

    questions = spec.get("questions", [])
    selected: dict[int, int] = {}
    for decision in decisions:
        if not isinstance(decision, dict):
            raise ValueError("Each decision must be an object")
        qi, oi = decision.get("questionIndex"), decision.get("optionIndex")
        if not isinstance(qi, int) or isinstance(qi, bool) or not isinstance(oi, int) or isinstance(oi, bool):
            raise ValueError("Decision indexes must be integers")
        if qi in selected:
            raise ValueError(f"Question {qi} was selected more than once")
        if qi < 0 or qi >= len(questions) or not isinstance(questions[qi], dict):
            raise ValueError(f"Question index out of range: {qi}")
        options = questions[qi].get("options", [])
        if oi < 0 or oi >= len(options) or not isinstance(options[oi], dict):
            raise ValueError(f"Option {qi}:{oi} is not an instant choice")
        if not isinstance(options[oi].get("patch"), list):
            raise ValueError(f"Option {qi}:{oi} has no deterministic patch")
        selected[qi] = oi

    updated = copy.deepcopy(spec)
    for qi, oi in sorted(selected.items()):
        updated = apply_json_patch(updated, questions[qi]["options"][oi]["patch"])
    updated_questions = updated.get("questions", [])
    for qi in sorted(selected, reverse=True):
        updated_questions.pop(qi)

    old_source = source.read_bytes()
    old_html = html.read_bytes()
    try:
        atomic_json(source, updated)
        run_json([sys.executable, str(RENDERER), str(source), "--output", str(html)])
        validation = run_json([sys.executable, str(VALIDATOR), str(source), "--mode", "draft"])
    except Exception:
        atomic_bytes(source, old_source)
        atomic_bytes(html, old_html)
        raise
    return {
        "ok": True,
        "html": html_rel,
        "specHash": validation["specHash"],
        "applied": len(selected),
        "screens": discover_screens(),
    }


def find_chrome() -> str:
    for value in (shutil.which("google-chrome"), shutil.which("chromium"), "/Applications/Google Chrome.app/Contents/MacOS/Google Chrome", r"C:\Program Files\Google\Chrome\Application\chrome.exe"):
        if value and Path(value).exists():
            return str(value)
    raise RuntimeError("Chrome/Chromium not found; capture the sibling PNG manually and use --existing-png")


def png_size(path: Path) -> tuple[int, int]:
    with path.open("rb") as handle:
        if handle.read(8) != b"\x89PNG\r\n\x1a\n" or struct.unpack(">I", handle.read(4))[0] < 8 or handle.read(4) != b"IHDR":
            raise RuntimeError("Invalid PNG")
        return struct.unpack(">II", handle.read(8))


def update_tasks(html_rel: str, png_rel: str) -> list[Path]:
    changed, planning = [], ROOT / "backlog" / "planning"
    if planning.exists():
        needle, replacement = f"groundTruth=PENDING-APPROVAL:{html_rel}", f"groundTruth={png_rel}"
        for task in planning.glob("*.md"):
            text = task.read_text(encoding="utf-8")
            if needle in text:
                task.write_text(text.replace(needle, replacement), encoding="utf-8")
                changed.append(task)
    return changed


def approve_screen(html_rel: str, expected_hash: str, existing_png: bool = False) -> dict:
    html = safe_html(html_rel)
    if not html.exists() or not HASH_RE.fullmatch(expected_hash):
        raise ValueError("Invalid approval item")
    validation = run_json([sys.executable, str(VALIDATOR), str(html), "--mode", "approve"])
    spec_path = html.with_suffix(".ui-spec.json")
    if spec_path.exists():
        run_json([sys.executable, str(RENDERER), str(spec_path), "--output", str(html), "--check"])
    before_hash, before_html = current_hash(html), sha256_file(html)
    if before_hash != expected_hash or validation.get("specHash") != expected_hash:
        raise RuntimeError(f"Hash changed: expected {expected_hash}, current {before_hash}")
    png = html.with_suffix(".png")
    if not existing_png:
        result = subprocess.run([find_chrome(), "--headless=new", "--disable-gpu", "--hide-scrollbars", "--force-device-scale-factor=1", f"--window-size={DESIGN_W},{DESIGN_H}", f"--screenshot={png}", html.resolve().as_uri()], cwd=ROOT, text=True, capture_output=True)
        if result.returncode or not png.exists():
            raise RuntimeError(result.stderr.strip() or "Chrome screenshot export failed")
    if not png.exists() or png_size(png) != (DESIGN_W, DESIGN_H):
        raise RuntimeError(f"Approved PNG must exist at {DESIGN_W}x{DESIGN_H}")
    if current_hash(html) != before_hash or sha256_file(html) != before_html:
        raise RuntimeError("Spec or HTML changed during approval")
    png_rel, evidence = relative(png), approval_path(html)
    tasks = update_tasks(html_rel, png_rel)
    atomic_json(evidence, {"approvalVersion": 1, "screen": html.stem, "specHash": before_hash, "htmlSha256": before_html, "approvedAt": utc_now(), "html": html_rel, "png": png_rel, "pngSha256": sha256_file(png)})
    stage = [html, png, evidence, *tasks] + ([spec_path] if spec_path.exists() else [])
    staged = subprocess.run(["git", "add", "--", *map(str, stage)], cwd=ROOT, text=True, capture_output=True)
    if staged.returncode:
        evidence.unlink(missing_ok=True)
        for task in tasks:
            task.write_text(task.read_text(encoding="utf-8").replace(f"groundTruth={png_rel}", f"groundTruth=PENDING-APPROVAL:{html_rel}"), encoding="utf-8")
        raise RuntimeError(staged.stderr.strip() or "git add failed; approval rolled back")
    return {"html": html_rel, "png": png_rel, "specHash": before_hash}


def approve_items(items: list[dict]) -> dict:
    """Approve a bounded batch; every item remains independently hash-guarded."""
    if not isinstance(items, list) or not items or len(items) > 100:
        raise ValueError("Expected 1-100 approval items")
    approved, errors = [], []
    for item in items:
        if not isinstance(item, dict):
            errors.append({"item": item, "error": "Approval item must be an object"})
            continue
        try:
            html_rel, expected_hash = item.get("html"), item.get("specHash")
            if not isinstance(html_rel, str) or not isinstance(expected_hash, str):
                raise ValueError("Approval item requires html and specHash strings")
            approved.append(approve_screen(html_rel, expected_hash))
        except (OSError, ValueError, RuntimeError) as exc:
            errors.append({"item": item, "error": str(exc)})
    return {"ok": not errors, "approved": approved, "errors": errors, "screens": discover_screens()}


# ---------------------------------------------------------------------------
# Local review server — reliable replacement for the file:// + custom-URL-scheme
# bridge (ezg-ui-approve:// silently no-op'd; claude-cli:// raced the Terminal
# paste before `claude` finished init). The dashboard, when served over http,
# calls these endpoints with fetch() — no OS scheme, no Gatekeeper, no paste race.
# Loopback-only (127.0.0.1) + a per-run token guard on every mutating POST.
# ---------------------------------------------------------------------------

CONTENT_TYPES = {
    ".html": "text/html; charset=utf-8", ".css": "text/css; charset=utf-8",
    ".js": "application/javascript; charset=utf-8", ".json": "application/json; charset=utf-8",
    ".png": "image/png", ".jpg": "image/jpeg", ".jpeg": "image/jpeg",
    ".webp": "image/webp", ".svg": "image/svg+xml", ".gif": "image/gif",
}


def content_type(path: Path) -> str:
    return CONTENT_TYPES.get(path.suffix.lower(), "application/octet-stream")


def find_claude() -> str | None:
    return shutil.which("claude")


def spec_rel_for(html_rel: str) -> str:
    return html_rel[:-5] + ".ui-spec.json" if html_rel.endswith(".html") else html_rel


def regen_status_path(html_rel: str) -> Path:
    return MOCKUPS_ROOT / f"_regen-{Path(html_rel).stem}.status.json"


def regen_log_path(html_rel: str) -> Path:
    return MOCKUPS_ROOT / f"_regen-{Path(html_rel).stem}.log"


def regen_prompt(html_rel: str, expected_hash: str, text: str) -> str:
    """Lean, self-contained prompt for a headless `claude -p` run — deliberately does NOT ask the
    agent to re-read ui-mockup.md every time (a wasted turn); the 3 rules it needs are inlined."""
    spec_rel = spec_rel_for(html_rel)
    return (
        f"Bạn đang chỉnh MỘT UI mockup trong project {ROOT}. Màn: {html_rel}; spec nguồn (source of truth): {spec_rel}; "
        f"expected specHash: {expected_hash}. "
        f"BƯỚC 1 — xác minh specHash hiện tại của spec còn khớp expected; nếu lệch thì DỪNG và in lý do, không sửa gì. "
        f"BƯỚC 2 — áp dụng yêu cầu của human bằng cách CHỈ sửa file {spec_rel} (TUYỆT ĐỐI không sửa tay HTML). "
        f"Yêu cầu của human: {json.dumps(text, ensure_ascii=False)}. "
        f"Quy tắc: chỉ dùng template có trong ui-catalog/ui-kit.json; mọi text phải có localize (#key / dynamic / none). "
        f"QUAN TRỌNG — sau khi áp dụng, XÓA khỏi mảng `questions[]` mọi câu hỏi mà human vừa trả lời (nếu đã trả lời hết thì đặt `questions: []`). "
        f"KHÔNG để lại text literal `[?]` trong bất kỳ element nào — literal `[?]` sẽ CHẶN approval (mode approve coi là lỗi placeholder). "
        f"Với quyết định kiểu 'bind từ config / placeholder động', THAY `[?]` bằng một GIÁ TRỊ MẪU đại diện + `localize:\"dynamic\"` "
        f"(ví dụ '1.200', '$4.99', '×2') — giá trị thật sẽ bind lúc runtime; đừng bịa con số 'chốt', chỉ là mẫu hiển thị. "
        f"Chỉ được giữ `[?]` + câu hỏi khi human YÊU CẦU RÕ hoãn quyết định đó. "
        f"BƯỚC 3 — render lại HTML: python3 .claude/scripts/ui-spec-render.py {spec_rel} --output {html_rel}; "
        f"sau đó validate: python3 .claude/scripts/ui-spec-validator.py {spec_rel} --mode draft. "
        f"KHÔNG generate dashboard, KHÔNG export PNG, TUYỆT ĐỐI KHÔNG approve màn nào. Xong thì in tóm tắt ngắn."
    )


def queue_regen(html_rel: str, expected_hash: str, text: str) -> dict:
    """Durably record a regenerate request (survives even if the auto-run is off/fails)."""
    html = safe_html(html_rel)
    if not html.exists():
        raise ValueError("Mockup HTML does not exist")
    if not HASH_RE.fullmatch(expected_hash):
        raise ValueError("Invalid specHash")
    current = current_hash(html)
    if current != expected_hash:
        raise ValueError(f"Hash changed: expected {expected_hash}, current {current}")
    if not isinstance(text, str) or not text.strip() or len(text) > 2000:
        raise ValueError("Regenerate text must contain 1-2000 characters")
    record = {"html": html_rel, "specHash": expected_hash, "text": text.strip(), "ts": utc_now(), "done": False}
    REGEN_QUEUE.parent.mkdir(parents=True, exist_ok=True)
    with REGEN_QUEUE.open("a", encoding="utf-8") as handle:
        handle.write(json.dumps(record, ensure_ascii=False) + "\n")
    return record


def spawn_regen(html_rel: str, expected_hash: str, text: str) -> dict:
    """Headless `claude -p` to apply the edit — Sonnet + effort xhigh (user-chosen, never haiku) and
    `--output-format stream-json` so the log fills with live events (the default text format prints
    ONLY the final result → the log stays empty mid-run, which is why progress used to be invisible).
    Detached so it outlives the request; a per-screen status file tracks PID + start time so the
    dashboard can show elapsed/phase and detect a crash instead of polling blindly."""
    claude = find_claude()
    if not claude:
        return {"auto": False, "reason": "claude CLI not on PATH — request queued only"}
    log = regen_log_path(html_rel)
    handle = log.open("w", encoding="utf-8")
    try:
        proc = subprocess.Popen(
            [
                claude, "-p", regen_prompt(html_rel, expected_hash, text),
                "--model", REGEN_MODEL, "--effort", REGEN_EFFORT,
                "--output-format", "stream-json", "--verbose",
                "--permission-mode", "acceptEdits",
                "--allowedTools", "Read,Edit,Glob,Grep,Bash(python3 .claude/scripts/ui-spec-render.py *),Bash(python3 .claude/scripts/ui-spec-validator.py *)",
            ],
            cwd=ROOT, stdin=subprocess.DEVNULL, stdout=handle, stderr=subprocess.STDOUT, start_new_session=True,
        )
    finally:
        handle.close()
    atomic_pretty_json(regen_status_path(html_rel), {
        "html": html_rel, "pid": proc.pid, "startedAt": utc_now(), "startedMono": time.time(),
        "expectedHash": expected_hash, "log": relative(log), "state": "running",
        "model": REGEN_MODEL_LABEL, "effort": REGEN_EFFORT,
    })
    return {"auto": True, "log": relative(log), "pid": proc.pid, "model": REGEN_MODEL_LABEL, "effort": REGEN_EFFORT}


def pid_alive(pid: int) -> bool:
    if not pid:
        return False
    try:
        os.kill(pid, 0)
    except ProcessLookupError:
        return False
    except PermissionError:
        return True
    return True


def _humanize_stream_line(line: str) -> str | None:
    """Turn one stream-json event into a short human phrase, or None if it carries nothing useful."""
    try:
        ev = json.loads(line)
    except (ValueError, json.JSONDecodeError):
        return None
    etype = ev.get("type")
    if etype == "assistant":
        for block in ev.get("message", {}).get("content", []):
            if block.get("type") == "text" and block.get("text", "").strip():
                return " ".join(block["text"].split())[:160]
            if block.get("type") == "tool_use":
                name = block.get("name", "tool")
                cmd = (block.get("input", {}) or {}).get("command", "")
                return f"🔧 {name}: {' '.join(str(cmd).split())[:80]}" if cmd else f"🔧 {name}"
    if etype == "result":
        return "✓ agent hoàn tất" if not ev.get("is_error") else "✗ agent báo lỗi"
    return None


def regen_progress(html_rel: str) -> dict:
    """Derive a coarse phase + latest readable line from the stream-json log tail."""
    log = regen_log_path(html_rel)
    if not log.exists():
        return {"phase": "Đang khởi động agent…", "lastLine": ""}
    try:
        tail = log.read_text(encoding="utf-8", errors="replace")[-8000:]
    except OSError:
        return {"phase": "", "lastLine": ""}
    phase = "Đang phân tích yêu cầu…"
    if "Edit" in tail or "str_replace" in tail or ".ui-spec.json" in tail:
        phase = "Đang sửa .ui-spec.json…"
    if "ui-spec-render" in tail:
        phase = "Đang render HTML…"
    if "ui-spec-validator" in tail:
        phase = "Đang validate spec…"
    last = ""
    for line in reversed(tail.splitlines()):
        readable = _humanize_stream_line(line.strip())
        if readable:
            last = readable
            break
    return {"phase": phase, "lastLine": last}


def mark_regen_terminal(html_rel: str, state: str) -> None:
    """Flip the status file + the matching queue record to a terminal state so re-scans are accurate
    and the poll stops deterministically instead of counting down a fixed timeout."""
    status_path = regen_status_path(html_rel)
    status = load_json_object(status_path)
    if status:
        status["state"] = state
        status["endedAt"] = utc_now()
        atomic_pretty_json(status_path, status)
    if not REGEN_QUEUE.exists():
        return
    lines, changed = [], False
    for raw in REGEN_QUEUE.read_text(encoding="utf-8").splitlines():
        raw = raw.strip()
        if not raw:
            continue
        try:
            rec = json.loads(raw)
        except (ValueError, json.JSONDecodeError):
            lines.append(raw)
            continue
        if rec.get("html") == html_rel and not rec.get("done"):
            rec["done"], rec["state"] = True, state
            changed = True
        lines.append(json.dumps(rec, ensure_ascii=False))
    if changed:
        REGEN_QUEUE.write_text("\n".join(lines) + "\n", encoding="utf-8")


def evaluate_regen(html_rel: str) -> dict:
    """Current state of an in-flight regenerate for one screen (drives the dashboard status panel)."""
    status = load_json_object(regen_status_path(html_rel))
    if not status:
        return {"state": "idle"}
    if status.get("state") in ("done", "error", "cancelled"):
        return {"state": status["state"]}
    started = status.get("startedMono")
    elapsed = int(time.time() - started) if isinstance(started, (int, float)) else 0
    expected, pid = status.get("expectedHash"), status.get("pid")
    try:
        current = current_hash(safe_html(html_rel))
    except (OSError, ValueError, json.JSONDecodeError):
        current = None  # spec mid-rewrite → treat as still running
    if current and current != expected:
        mark_regen_terminal(html_rel, "done")
        return {"state": "done", "newHash": current, "elapsed": elapsed}
    alive = pid_alive(pid)
    progress = regen_progress(html_rel)
    if not alive:
        # process gone but spec unchanged → it failed (or verified hash mismatch and bailed)
        mark_regen_terminal(html_rel, "error")
        return {"state": "error", "elapsed": elapsed, **progress}
    return {"state": "running", "elapsed": elapsed, "pid": pid, **progress,
            "model": status.get("model"), "effort": status.get("effort")}


def cancel_regen(html_rel: str) -> dict:
    status = load_json_object(regen_status_path(html_rel))
    pid = status.get("pid")
    if pid and pid_alive(pid):
        try:
            os.killpg(os.getpgid(pid), signal.SIGTERM)
        except (ProcessLookupError, PermissionError, OSError):
            try:
                os.kill(pid, signal.SIGTERM)
            except (ProcessLookupError, PermissionError):
                pass
    mark_regen_terminal(html_rel, "cancelled")
    return {"ok": True, "state": "cancelled"}


def public_regen(html_rel: str) -> dict | None:
    """Lightweight regen snapshot embedded in each screen row so a page reload can resume tracking a
    run that is still in flight (returns None when nothing is actively regenerating)."""
    status = load_json_object(regen_status_path(html_rel))
    if not status or status.get("state") != "running":
        return None
    if not pid_alive(status.get("pid")):
        return None
    started = status.get("startedMono")
    return {
        "state": "running",
        "elapsed": int(time.time() - started) if isinstance(started, (int, float)) else 0,
        "model": status.get("model"), "effort": status.get("effort"),
    }


class ReviewHandler(BaseHTTPRequestHandler):
    server_version = "UiReview/1"

    def log_message(self, *_args):  # keep the server quiet
        return

    def _reply(self, code: int, body, ctype: str = "application/json; charset=utf-8") -> None:
        data = body if isinstance(body, (bytes, bytearray)) else json.dumps(body, ensure_ascii=False).encode("utf-8")
        self.send_response(code)
        self.send_header("Content-Type", ctype)
        self.send_header("Content-Length", str(len(data)))
        self.send_header("Cache-Control", "no-store")
        self.end_headers()
        if self.command != "HEAD":
            self.wfile.write(data)

    def _dashboard(self) -> None:
        config = {**self.server.base_config, "serve": True, "token": self.server.token, "port": self.server.server_address[1]}
        html, _ = render_dashboard_html(config)
        self._reply(200, html.encode("utf-8"), "text/html; charset=utf-8")

    def _authorized(self) -> bool:
        supplied = self.headers.get("X-Review-Token", "")
        return bool(self.server.token) and hmac.compare_digest(supplied, self.server.token)

    def do_GET(self) -> None:
        path = unquote(urlparse(self.path).path)
        if path == "/api/screens":
            if not self._authorized():
                self._reply(403, {"ok": False, "error": "bad or missing review token"})
                return
            self._reply(200, {"ok": True, "screens": discover_screens()})
            return
        if path == "/api/regen-status":
            if not self._authorized():
                self._reply(403, {"ok": False, "error": "bad or missing review token"})
                return
            html_rel = (parse_qs(urlparse(self.path).query).get("html") or [""])[0]
            try:
                safe_html(html_rel)
            except (ValueError, OSError):
                self._reply(400, {"ok": False, "error": "bad html param"})
                return
            self._reply(200, {"ok": True, **evaluate_regen(html_rel)})
            return
        if path in ("/", "/index.html", f"/{relative(DASHBOARD_PATH)}"):
            self._dashboard()
            return
        target = (ROOT / path.lstrip("/")).resolve()
        allowed_roots = (MOCKUPS_ROOT.resolve(), (ROOT / "ui-catalog").resolve())
        if not any(target == allowed or allowed in target.parents for allowed in allowed_roots):
            self._reply(403, {"ok": False, "error": "forbidden"})
            return
        if not target.is_file():
            self._reply(404, {"ok": False, "error": "not found"})
            return
        self._reply(200, target.read_bytes(), content_type(target))

    def do_POST(self) -> None:
        path = urlparse(self.path).path
        if not self._authorized():
            self._reply(403, {"ok": False, "error": "bad or missing review token"})
            return
        try:
            length = int(self.headers.get("Content-Length", 0) or 0)
            if length < 0 or length > 256 * 1024:
                raise ValueError("request body is too large")
            payload = json.loads(self.rfile.read(length) or b"{}")
            if not isinstance(payload, dict):
                raise ValueError("expected a JSON object")
        except (ValueError, json.JSONDecodeError):
            self._reply(400, {"ok": False, "error": "invalid JSON body"})
            return
        if path == "/api/approve":
            try:
                self._reply(200, approve_items(payload.get("items")))
            except ValueError as exc:
                self._reply(400, {"ok": False, "error": str(exc)})
            return
        if path == "/api/apply-decisions":
            try:
                self._reply(200, apply_decisions(
                    payload["html"], payload["specHash"], payload.get("decisions"),
                ))
            except (KeyError, OSError, ValueError, RuntimeError) as exc:
                self._reply(400, {"ok": False, "error": str(exc)})
            return
        if path == "/api/regenerate":
            try:
                record = queue_regen(payload["html"], payload["specHash"], payload.get("text", ""))
            except (KeyError, ValueError, TypeError) as exc:
                self._reply(400, {"ok": False, "error": str(exc)})
                return
            auto = spawn_regen(record["html"], record["specHash"], record["text"]) if self.server.auto_regen else {"auto": False}
            self._reply(200, {"ok": True, "queued": True, **auto})
            return
        if path == "/api/regen-cancel":
            html_rel = payload.get("html", "")
            try:
                safe_html(html_rel)
            except (ValueError, OSError):
                self._reply(400, {"ok": False, "error": "bad html param"})
                return
            self._reply(200, {"ok": True, **cancel_regen(html_rel)})
            return
        self._reply(404, {"ok": False, "error": "unknown endpoint"})


def create_review_server(port: int, token: str, base_config: dict, auto_regen: bool) -> HTTPServer:
    if not token:
        raise ValueError("Review server requires a token")
    httpd = HTTPServer(("127.0.0.1", port), ReviewHandler)
    httpd.token = token
    httpd.base_config = base_config
    httpd.auto_regen = auto_regen
    return httpd


def serve(port: int = DEFAULT_PORT, open_browser: bool = True, auto_regen: bool = True) -> int:
    token, config = secrets.token_hex(16), ensure_claude_launch_config()
    active_regen = bool(auto_regen and find_claude())
    if active_regen:
        # In serve mode the regenerate agent is pinned (Sonnet + xhigh); reflect it on the chips.
        config = {**config, "model": REGEN_MODEL_LABEL, "effort": REGEN_EFFORT}
    try:
        httpd = create_review_server(port, token, config, active_regen)
    except OSError:
        httpd = create_review_server(0, token, config, active_regen)
    actual = httpd.server_address[1]
    url = f"http://127.0.0.1:{actual}/{relative(DASHBOARD_PATH)}"
    print(json.dumps({"ok": True, "url": url, "port": actual, "autoRegen": httpd.auto_regen}, ensure_ascii=False, indent=2), flush=True)
    if open_browser:
        webbrowser.open(url)
    try:
        httpd.serve_forever()
    except KeyboardInterrupt:
        pass
    finally:
        httpd.server_close()
    return 0


def parse_item(value: str) -> tuple[str, str]:
    try:
        html, digest = value.rsplit("=", 1)
    except ValueError as exc:
        raise ValueError("Approval item must be HTML=SPEC_HASH") from exc
    return html, digest


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    sub = parser.add_subparsers(dest="command", required=True)
    gen = sub.add_parser("generate"); gen.add_argument("--open", action="store_true")
    sub.add_parser("list")
    approve = sub.add_parser("approve"); approve.add_argument("--item", action="append", required=True); approve.add_argument("--existing-png", action="store_true")
    srv = sub.add_parser("serve")
    srv.add_argument("--port", type=int, default=DEFAULT_PORT)
    srv.add_argument("--no-open", action="store_true")
    srv.add_argument("--no-auto-regen", action="store_true")
    args = parser.parse_args()
    if args.command == "serve":
        return serve(port=args.port, open_browser=not args.no_open, auto_regen=not args.no_auto_regen)
    if args.command == "generate":
        screens = generate_dashboard(args.open)
        print(json.dumps({"ok": True, "dashboard": relative(DASHBOARD_PATH), "pending": len(screens)}, ensure_ascii=False, indent=2)); return 0
    if args.command == "list":
        print(json.dumps({"ok": True, "screens": discover_screens()}, ensure_ascii=False, indent=2)); return 0
    approved, errors = [], []
    for value in args.item:
        try:
            approved.append(approve_screen(*parse_item(value), existing_png=args.existing_png))
        except (OSError, ValueError, RuntimeError) as exc:
            errors.append({"item": value, "error": str(exc)})
    generate_dashboard()
    print(json.dumps({"ok": not errors, "approved": approved, "errors": errors}, ensure_ascii=False, indent=2))
    return 0 if not errors else 1


if __name__ == "__main__":
    raise SystemExit(main())
