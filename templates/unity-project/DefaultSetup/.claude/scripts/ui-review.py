#!/usr/bin/env python3
"""Serve/generate the UI review dashboard and freeze explicit human approvals."""

from __future__ import annotations

import argparse
import copy
import hmac
import hashlib
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
from html import escape
from http.server import BaseHTTPRequestHandler, HTTPServer
from pathlib import Path
from urllib.parse import parse_qs, unquote, urlparse

from project_profile import profile
from ui_spec_common import ROOT, apply_json_patch, canonical_json, load_spec, spec_hash




def _backlog_root() -> Path:
    """The backlog lives in the git COMMON dir (.git/backlog/), not the worktree —
    it is per-developer bookkeeping shared by every linked worktree of the clone.
    See .claude/scripts/backlog-ops.py for the full rationale."""
    try:
        out = subprocess.run(["git", "rev-parse", "--git-common-dir"], cwd=ROOT,
                             capture_output=True, text=True, check=True).stdout.strip()
        if out:
            return (ROOT / out).resolve() / "backlog"
    except Exception:
        pass
    return (ROOT / ".git").resolve() / "backlog"


BACKLOG_ROOT = _backlog_root()
PLANNING_DIR = BACKLOG_ROOT / "planning"
MOCKUPS_ROOT = ROOT / "TechSpec" / "Mockups"
DASHBOARD_PATH = MOCKUPS_ROOT / "_ui-review.html"
DASHBOARD_TEMPLATE = ROOT / ".claude" / "ui-kit" / "ui-review-dashboard.template.html"
VALIDATOR = ROOT / ".claude" / "scripts" / "ui-spec-validator.py"
RENDERER = ROOT / ".claude" / "scripts" / "ui-spec-render.py"
CLAUDE_GLOBAL_CONFIG = Path.home() / ".claude.json"
CLAUDE_LOCAL_SETTINGS = ROOT / ".claude" / "settings.local.json"
PENDING_RE = re.compile(r"groundTruth=PENDING-APPROVAL:([^\s`]+)")
HASH_RE = re.compile(r"^[0-9a-f]{64}$")

# One source of truth for the model/effort of every review session: the interactive deep link, the
# pinned local settings, and the headless regenerate agent. A second hardcoded constant would drift.
REVIEW_MODEL = "opus"
REVIEW_EFFORT = "xhigh"

REGEN_QUEUE = MOCKUPS_ROOT / "_regen-queue.jsonl"
# The only subtree a regenerate agent may write to. Enforced twice, deliberately: as a CLI
# permission rule (prevention) and as a git-diff check after the run (proof). See spawn_regen.
REGEN_SCOPE = "TechSpec/Mockups"


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
    """Trust this repo and pin review sessions to Opus/xhigh without bypassing permissions."""
    global_config = load_json_object(CLAUDE_GLOBAL_CONFIG)
    projects = global_config.setdefault("projects", {})
    project = projects.setdefault(str(ROOT.resolve()), {})
    project["hasTrustDialogAccepted"] = True
    atomic_pretty_json(CLAUDE_GLOBAL_CONFIG, global_config)

    local_settings = load_json_object(CLAUDE_LOCAL_SETTINGS)
    local_settings["model"] = REVIEW_MODEL
    local_settings["effortLevel"] = REVIEW_EFFORT
    atomic_pretty_json(CLAUDE_LOCAL_SETTINGS, local_settings)
    return {"trusted": True, "model": REVIEW_MODEL, "effort": REVIEW_EFFORT}


def sha256_file(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as handle:
        for chunk in iter(lambda: handle.read(1024 * 1024), b""):
            digest.update(chunk)
    return digest.hexdigest()


def sha256_html(path: Path) -> str:
    """Newline-insensitive hash for text artifacts: git autocrlf rewrites LF→CRLF on Windows
    checkouts, which must not void approval evidence frozen on another OS."""
    return hashlib.sha256(path.read_bytes().replace(b"\r\n", b"\n")).hexdigest()


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
        # htmlSha256: accept the normalized hash (current freeze format) or the raw-byte hash
        # (legacy evidence frozen from CRLF bytes before normalization existed).
        html_ok = payload.get("htmlSha256") in (sha256_html(html_path), sha256_file(html_path))
        return payload.get("specHash") == current_hash(html_path) and html_ok and payload.get("pngSha256") == sha256_file(png)
    except (OSError, ValueError, json.JSONDecodeError):
        return False


def pending_task_htmls() -> set[str]:
    found, planning = set(), PLANNING_DIR
    if planning.exists():
        for task in planning.glob("*.md"):
            found.update(match.group(1) for match in PENDING_RE.finditer(task.read_text(encoding="utf-8")))
    return found


def normalize_questions(raw) -> list[dict]:
    """Return browser-safe questions; deterministic patches stay server-side."""
    out = []
    if not isinstance(raw, list):
        return out
    for item in raw:
        if isinstance(item, str) and item.strip():
            out.append({"q": item.strip(), "options": []})
        elif isinstance(item, dict) and isinstance(item.get("q"), str) and item["q"].strip():
            options = []
            for option in item.get("options", []):
                if isinstance(option, str):
                    options.append({"label": option, "instant": False})
                elif isinstance(option, dict) and isinstance(option.get("label"), str):
                    options.append({"label": option["label"], "instant": isinstance(option.get("patch"), list)})
            out.append({"q": item["q"].strip(), "options": options})
    return out


def screen_record(spec: dict, html: Path, html_rel: str) -> dict:
    """Dashboard row for one screen.

    Carries the drafter's `questions[]`/`assumptions[]` and a count of literal `[?]` placeholders.
    validate_spec already turns all three into hard errors under --mode approve, so approval was
    never actually blind — but the human only met them as a terse validator error AFTER clicking
    Approve. Surfacing them here moves that same signal in front of the click.
    """
    return {
        "feature": spec.get("feature", html.parent.name),
        "screen": spec.get("screen", html.stem),
        "strict": isinstance(spec.get("specVersion"), int) and spec.get("specVersion") >= 1,
        "html": html_rel,
        "preview": html.relative_to(MOCKUPS_ROOT).as_posix(),
        "specHash": spec_hash(spec),
        "questions": normalize_questions(spec.get("questions", [])),
        "assumptions": [a for a in spec.get("assumptions", []) if isinstance(a, str)],
        "placeholders": sum(
            1 for e in spec.get("elements", [])
            if isinstance(e, dict) and isinstance(e.get("text"), str) and "[?]" in e["text"]
        ),
        "regen": public_regen(html_rel),
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
            # A parallel drafter may still be replacing this pair. Keep the queue usable and
            # discover the screen on the next refresh once its authoritative spec is complete.
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
    """Render dashboard in memory so a serve-session token is never written to disk."""
    screens = discover_screens()
    template = DASHBOARD_TEMPLATE.read_text(encoding="utf-8")
    # Per-project, not hardcoded: the approve:// scheme is registered machine-wide,
    # so two projects on the same base must not claim the same one (see
    # setup-approve-handler-macos.sh). Both come from .claude/project-profile.json.
    source = (
        template
        .replace("__PROJECT_TITLE__", escape(profile().project_name))
        .replace("__APPROVE_SCHEME__", json.dumps(f"{profile().git_config_prefix}-approve"))
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
        raise RuntimeError(_failure_detail(payload))
    return payload


def _failure_detail(payload: dict) -> str:
    """Best readable cause from a validator/renderer payload.

    The renderer reports a single `error` holding the validator's own JSON, so reading only
    `errors[]` collapses every distinct failure into "UI validation failed" — which tells the human
    nothing and hides actionable causes like a stale UI kit.
    """
    detail = "; ".join(e.get("message", str(e)) for e in payload.get("errors", []))
    nested = payload.get("error")
    if not detail and isinstance(nested, str):
        try:
            inner = json.loads(nested)
            detail = "; ".join(e.get("message", str(e)) for e in inner.get("errors", []))
        except (ValueError, json.JSONDecodeError):
            detail = nested
    return detail or "UI validation failed"


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
        question_index, option_index = decision.get("questionIndex"), decision.get("optionIndex")
        if (not isinstance(question_index, int) or isinstance(question_index, bool)
                or not isinstance(option_index, int) or isinstance(option_index, bool)):
            raise ValueError("Decision indexes must be integers")
        if question_index in selected:
            raise ValueError(f"Question {question_index} was selected more than once")
        if question_index < 0 or question_index >= len(questions) or not isinstance(questions[question_index], dict):
            raise ValueError(f"Question index out of range: {question_index}")
        options = questions[question_index].get("options", [])
        if option_index < 0 or option_index >= len(options) or not isinstance(options[option_index], dict):
            raise ValueError(f"Option {question_index}:{option_index} is not an instant choice")
        if not isinstance(options[option_index].get("patch"), list):
            raise ValueError(f"Option {question_index}:{option_index} has no deterministic patch")
        selected[question_index] = option_index

    updated = copy.deepcopy(spec)
    for question_index, option_index in sorted(selected.items()):
        updated = apply_json_patch(updated, questions[question_index]["options"][option_index]["patch"])
    for question_index in sorted(selected, reverse=True):
        updated["questions"].pop(question_index)

    old_source, old_html = source.read_bytes(), html.read_bytes()
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
    changed, planning = [], PLANNING_DIR
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
    before_hash, before_html = current_hash(html), sha256_html(html)
    if before_hash != expected_hash or validation.get("specHash") != expected_hash:
        raise RuntimeError(f"Hash changed: expected {expected_hash}, current {before_hash}")
    png = html.with_suffix(".png")
    if not existing_png:
        result = subprocess.run([find_chrome(), "--headless=new", "--disable-gpu", "--hide-scrollbars", "--force-device-scale-factor=1", "--window-size=1080,1920", f"--screenshot={png}", html.resolve().as_uri()], cwd=ROOT, text=True, capture_output=True)
        if result.returncode or not png.exists():
            raise RuntimeError(result.stderr.strip() or "Chrome screenshot export failed")
    if not png.exists() or png_size(png) != (1080, 1920):
        raise RuntimeError("Approved PNG must exist at 1080x1920")
    if current_hash(html) != before_hash or sha256_html(html) != before_html:
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


def auto_approve(task_files: list[str] | None = None) -> dict:
    """Default-approve every pending draft that survives approve-mode validation.

    Approval is the DEFAULT state of a mockup: after drafting, planning (or a manual
    /ui-mockup run) calls this to freeze each draft to its 1080×1920 PNG contract and flip
    the task groundTruth — no human round. The only screens left pending are the ones the
    validator refuses (unresolved questions[] / literal [?] placeholders — the
    forbidden-to-invent group) or ones whose PNG export failed; those are reported so the
    dev can settle them in the review dashboard (`serve`).
    """
    screens = discover_screens()
    if task_files:
        wanted = set()
        for task_file in task_files:
            # Accept the canonical `backlog/planning/<file>.md` spelling (resolved
            # against the git common dir), an absolute path, or a bare filename.
            candidate = Path(task_file)
            if candidate.is_absolute():
                path = candidate.resolve()
            else:
                for base in (BACKLOG_ROOT.parent / task_file, ROOT / task_file,
                             PLANNING_DIR / candidate.name):
                    if base.exists():
                        path = base.resolve()
                        break
                else:
                    raise SystemExit(f"task file not found: {task_file} (looked in {PLANNING_DIR})")
            wanted.update(match.group(1) for match in PENDING_RE.finditer(path.read_text(encoding="utf-8")))
        screens = [s for s in screens if s["html"] in wanted]
    approved, pending = [], []
    for screen in screens:
        if screen["questions"] or screen["placeholders"]:
            pending.append({
                "html": screen["html"],
                "reason": f"{len(screen['questions'])} câu hỏi chưa trả lời, {screen['placeholders']} placeholder [?] — "
                          f"cần dev quyết qua dashboard (`ui-review.py serve`) rồi chạy auto-approve lại",
            })
            continue
        try:
            approved.append(approve_screen(screen["html"], screen["specHash"]))
        except (OSError, ValueError, RuntimeError) as exc:
            # Self-heal a stale render: the sidecar spec is authoritative and the renderer is
            # deterministic, so re-rendering and retrying once is safe. This happens when a spec
            # edit landed but its render step was interrupted (the DungeonConfirmPopup case).
            if "differs from" in str(exc) or "re-render" in str(exc):
                try:
                    html = safe_html(screen["html"])
                    spec_path = html.with_suffix(".ui-spec.json")
                    run_json([sys.executable, str(RENDERER), str(spec_path), "--output", str(html)])
                    approved.append(approve_screen(screen["html"], current_hash(html)))
                    continue
                except (OSError, ValueError, RuntimeError) as retry_exc:
                    exc = retry_exc
            pending.append({"html": screen["html"], "reason": str(exc)})
    return {"ok": not pending, "approved": approved, "pending": pending}


GALLERY_PATH = MOCKUPS_ROOT / "_approved-gallery.html"

GALLERY_CSS = """*,*::before,*::after{box-sizing:border-box}
:root{color-scheme:dark light;--bg:#111115;--surface:rgba(30,28,22,.97);--border:rgba(255,255,255,.12);
--text:#f0ede6;--text-muted:#b4b0a8;--accent:#f59e0b;--good:#6ee7b7}
@media(prefers-color-scheme:light){:root{--bg:#faf8f4;--surface:#fff;--border:rgba(0,0,0,.09);
--text:#1c1a16;--text-muted:#6b6660;--accent:#b45309;--good:#059669}}
body{margin:0;padding:32px 28px 56px;background:var(--bg);color:var(--text);
font:15px/1.5 -apple-system,BlinkMacSystemFont,"Segoe UI",Roboto,sans-serif}
h1{margin:0 0 4px;font-size:22px;letter-spacing:-.01em}
p.sub{margin:0 0 28px;color:var(--text-muted);font-size:13px}
.grid{display:grid;gap:24px;grid-template-columns:repeat(auto-fill,minmax(260px,1fr))}
article{background:var(--surface);border:1px solid var(--border);border-radius:16px;overflow:hidden;
display:flex;flex-direction:column}
a.shot{display:block;background:#000;line-height:0}
a.shot img{width:100%;height:auto;display:block}
.meta{padding:14px 16px 16px;display:flex;flex-direction:column;gap:8px}
.meta small{color:var(--text-muted);font-size:11px;text-transform:uppercase;letter-spacing:.06em}
.meta b{font-size:15px;word-break:break-word}
.badge{align-self:flex-start;color:var(--good);font-size:11px;font-weight:600;letter-spacing:.04em}
.links{display:flex;gap:14px;font-size:12px}
.links a{color:var(--accent);text-decoration:none}
.links a:hover{text-decoration:underline}"""


def write_gallery(approved: list[dict]) -> Path:
    """One index page for a multi-screen run, so `--open` costs a single tab instead of N.

    Opening every approved screen is right for one screen and unusable for ten; the gallery
    keeps the frozen PNG contract in front of the dev and links through to the interactive
    HTML per screen. Generated session state like `_ui-review.html` — never committed.
    """
    cards = []
    for item in approved:
        html_rel, png_rel = item["html"], item["png"]
        png_src = escape(relative_to_mockups(png_rel), quote=True)
        html_src = escape(relative_to_mockups(html_rel), quote=True)
        screen = escape(Path(html_rel).stem)
        feature = escape(Path(html_rel).parent.name)
        cards.append(
            f'<article><a class="shot" href="{png_src}" target="_blank" rel="noreferrer">'
            f'<img src="{png_src}" alt="{screen}" loading="lazy"></a>'
            f'<div class="meta"><small>{feature}</small><b>{screen}</b>'
            f'<span class="badge">✓ ĐÃ DUYỆT</span>'
            f'<span class="links"><a href="{html_src}" target="_blank" rel="noreferrer">HTML tương tác</a>'
            f'<a href="{png_src}" target="_blank" rel="noreferrer">PNG 1080×1920</a></span></div></article>'
        )
    body = (
        f"<h1>Mockup vừa duyệt — {len(approved)} màn hình</h1>"
        f'<p class="sub">PNG là bản contract đã đóng băng mà <code>/new-ui</code> dựng theo. '
        f"Bấm ảnh để xem full size, hoặc mở HTML để bật nhãn template.</p>"
        f'<div class="grid">{"".join(cards)}</div>'
    )
    GALLERY_PATH.write_text(
        f'<!doctype html><html lang="vi"><head><meta charset="utf-8">'
        f'<meta name="viewport" content="width=device-width,initial-scale=1">'
        f"<title>Mockup đã duyệt ({len(approved)})</title><style>{GALLERY_CSS}</style></head>"
        f"<body>{body}</body></html>",
        encoding="utf-8",
    )
    return GALLERY_PATH


def relative_to_mockups(repo_rel: str) -> str:
    """Path usable as an href from the gallery, which lives at the Mockups root."""
    return (ROOT / repo_rel).resolve().relative_to(MOCKUPS_ROOT.resolve()).as_posix()


def open_approved(approved: list[dict]) -> str | None:
    """Show the work: one tab for one screen, one gallery tab for many. Returns what opened."""
    targets = [ROOT / item["html"] for item in approved]
    existing = [path for path in targets if path.exists()]
    if not existing:
        return None
    target = existing[0] if len(existing) == 1 else write_gallery(approved)
    webbrowser.open(target.resolve().as_uri())
    return relative(target)


def approve_items(items: list[dict]) -> dict:
    """Approve a bounded batch. Each item remains independently hash-guarded."""
    if not isinstance(items, list) or not items or len(items) > 100:
        raise ValueError("Expected 1-100 approval items")
    approved, errors = [], []
    for item in items:
        if not isinstance(item, dict):
            errors.append({"item": item, "error": "Approval item must be an object"})
            continue
        html_rel, expected_hash = item.get("html"), item.get("specHash")
        try:
            if not isinstance(html_rel, str) or not isinstance(expected_hash, str):
                raise ValueError("Approval item requires html and specHash strings")
            approved.append(approve_screen(html_rel, expected_hash))
        except (OSError, ValueError, RuntimeError) as exc:
            errors.append({"item": item, "error": str(exc)})
    return {"ok": not errors, "approved": approved, "errors": errors, "screens": discover_screens()}


# ---------------------------------------------------------------------------
# Regenerate: apply a human's change request to one spec via a headless agent.
# ---------------------------------------------------------------------------

def find_claude() -> str | None:
    return shutil.which("claude")


def spec_rel_for(html_rel: str) -> str:
    return html_rel[:-5] + ".ui-spec.json" if html_rel.endswith(".html") else html_rel


def regen_status_path(html_rel: str) -> Path:
    return MOCKUPS_ROOT / f"_regen-{Path(html_rel).stem}.status.json"


def regen_log_path(html_rel: str) -> Path:
    return MOCKUPS_ROOT / f"_regen-{Path(html_rel).stem}.log"


def git_dirty() -> set[str]:
    """Repo-relative paths git currently reports as modified (staged or not)."""
    result = subprocess.run(["git", "status", "--porcelain"], cwd=ROOT, text=True, capture_output=True)
    if result.returncode:
        return set()
    paths = set()
    for line in result.stdout.splitlines():
        if len(line) < 4:
            continue
        entry = line[3:].strip()
        if " -> " in entry:  # rename: only the destination matters
            entry = entry.split(" -> ", 1)[1]
        paths.add(entry.strip('"'))
    return paths


def regen_out_of_scope(status: dict) -> list[str]:
    """Files the agent dirtied outside REGEN_SCOPE, versus the pre-spawn snapshot.

    The CLI permission rule in spawn_regen is the actual enforcement; this is the proof. It holds
    even if that flag regresses, is dropped, or a future CLI changes how the rule parses — a silent
    fail-open there shows up here as a blocked run instead of an unnoticed repo-wide write.
    """
    before = set(status.get("dirtyBefore") or [])
    return sorted(p for p in (git_dirty() - before) if not p.startswith(REGEN_SCOPE + "/"))


def html_rendered(html_rel: str) -> tuple[bool, str]:
    """True when the HTML on disk is exactly what the current spec renders to.

    A run must not be called 'done' on a spec-hash change alone. The dashboard previews the HTML
    while the hash (and the approval) track the spec, so a spec edit whose render step failed would
    show the human a stale image labelled fresh — the exact drift this pipeline exists to prevent.
    """
    html = safe_html(html_rel)
    spec_path = html.with_suffix(".ui-spec.json")
    if not spec_path.exists():
        return True, ""  # legacy embedded-spec screen: no sidecar to cross-check
    try:
        run_json([sys.executable, str(RENDERER), str(spec_path), "--output", str(html), "--check"])
        return True, ""
    except (OSError, RuntimeError) as exc:
        return False, str(exc)[:240]


def regen_prompt(html_rel: str, expected_hash: str, text: str) -> str:
    """Lean, self-contained prompt for a headless `claude -p` run — deliberately does NOT ask the
    agent to re-read ui-mockup.md every time (a wasted turn); the rules it needs are inlined.

    The spec-hash guard is enforced SERVER-SIDE only (queue_regen at queue time + spawn_regen right
    before Popen). The agent is deliberately NOT asked to verify the hash: the allowlist contains no
    hash-computing command, and demanding an unverifiable check is exactly what stranded earlier runs
    (the agent burned every Bash attempt on permission_denials, then quit without editing the spec).
    For the same reason the prompt spells out the exact tool contract — a headless run has no human
    to approve an off-allowlist call, so every attempt outside it is a wasted denied turn."""
    spec_rel = spec_rel_for(html_rel)
    return (
        f"Bạn đang chỉnh MỘT UI mockup trong project {ROOT}. Màn: {html_rel}; spec nguồn (source of truth): {spec_rel}. "
        f"specHash của spec ({expected_hash}) đã được server xác minh khớp NGAY TRƯỚC khi spawn bạn — KHÔNG cần và KHÔNG THỂ tự kiểm lại hash, đừng thử. "
        f"CÔNG CỤ ĐƯỢC PHÉP (mọi call ngoài danh sách này bị DENY tự động trong headless — đừng thử): "
        f"tool Read/Glob/Grep (đọc không giới hạn, dùng tool Grep thay vì Bash grep); "
        f"tool Edit CHỈ trong TechSpec/Mockups/** (tool Write KHÔNG có — sửa spec bằng Edit); "
        f"Bash CHỈ với đúng 2 lệnh, chạy từ repo root với path tương đối, KHÔNG cd/&&/pipe/python -c: "
        f"`python3 .claude/scripts/ui-spec-render.py ...` và `python3 .claude/scripts/ui-spec-validator.py ...`. "
        f"BƯỚC 1 — Read {spec_rel} để nắm spec hiện tại. "
        f"BƯỚC 2 — áp dụng yêu cầu của human bằng cách CHỈ sửa file {spec_rel} qua tool Edit (TUYỆT ĐỐI không sửa tay HTML). "
        f"Yêu cầu của human: {json.dumps(text, ensure_ascii=False)}. "
        f"Quy tắc: chỉ dùng template có trong .claude/ui-kit/ui-kit.json; mọi text phải có localize (#key / dynamic / none); "
        f"giữ nguyên designResolution 1080×1920. "
        f"QUAN TRỌNG — sau khi áp dụng, XÓA khỏi mảng `questions[]` mọi câu hỏi mà human vừa trả lời (nếu đã trả lời hết thì đặt `questions: []`). "
        f"KHÔNG để lại text literal `[?]` trong bất kỳ element nào — literal `[?]` sẽ CHẶN approval (validator ở mode approve coi là lỗi placeholder). "
        f"Với quyết định kiểu 'bind từ config / placeholder động', THAY `[?]` bằng một GIÁ TRỊ MẪU đại diện + `localize:\"dynamic\"` "
        f"(ví dụ '1.200', '$4.99', '×2') — giá trị thật sẽ bind lúc runtime; đừng bịa con số 'chốt', chỉ là mẫu hiển thị. "
        f"Chỉ được giữ `[?]` + câu hỏi khi human YÊU CẦU RÕ hoãn quyết định đó. "
        f"BƯỚC 3 — render lại HTML: python3 .claude/scripts/ui-spec-render.py {spec_rel} --output {html_rel}; "
        f"sau đó validate: python3 .claude/scripts/ui-spec-validator.py {spec_rel} --mode draft. "
        f"Render/validate lỗi → sửa lại spec rồi lặp BƯỚC 3; nếu vẫn bế tắc thì in RÕ nguyên nhân trước khi kết thúc — đừng im lặng bỏ cuộc. "
        f"KHÔNG generate dashboard, KHÔNG export PNG, TUYỆT ĐỐI KHÔNG approve màn nào. Xong thì in tóm tắt ngắn."
    )


def queue_regen(html_rel: str, expected_hash: str, text: str) -> dict:
    """Durably record a regenerate request (survives even if the auto-run is off or fails)."""
    html = safe_html(html_rel)
    if not html.exists():
        raise ValueError("Mockup HTML does not exist")
    if not HASH_RE.fullmatch(expected_hash):
        raise ValueError("Invalid specHash")
    current = current_hash(html)
    if current != expected_hash:
        raise ValueError(f"Hash changed: expected {expected_hash}, current {current}")
    if not isinstance(text, str) or not text.strip() or len(text) > 4000:
        raise ValueError("Regenerate text must contain 1-4000 characters")
    record = {"html": html_rel, "specHash": expected_hash, "text": text.strip(), "ts": utc_now(), "done": False}
    REGEN_QUEUE.parent.mkdir(parents=True, exist_ok=True)
    with REGEN_QUEUE.open("a", encoding="utf-8") as handle:
        handle.write(json.dumps(record, ensure_ascii=False) + "\n")
    return record


def spawn_regen(html_rel: str, expected_hash: str, text: str) -> dict:
    """Headless `claude -p` to apply one change request, detached so it outlives the HTTP request.

    Two flag choices here are load-bearing and were settled by testing the CLI, not by reading docs:

    * `--permission-mode default`, never `acceptEdits`. Permission-mode is evaluated BEFORE allow
      rules, so acceptEdits auto-approves an edit before --allowedTools is ever consulted — the
      allowlist becomes decorative and the agent can write anywhere. Passing `default` explicitly
      also overrides a machine-level `"defaultMode": "bypassPermissions"` in ~/.claude/settings.json,
      which otherwise disables tool scoping entirely for every spawned run.
    * `Edit(TechSpec/Mockups/**)` — path scoping DOES enforce (verified: an out-of-scope Edit comes
      back in permission_denials and the file is untouched). Read stays unscoped so the agent can
      ground its decisions in the GDD/TechSpec; it just cannot write there.

    stream-json keeps the log filling live; the default text format prints only the final result,
    which would leave progress invisible mid-run.
    """
    claude = find_claude()
    if not claude:
        return {"auto": False, "reason": "claude CLI not on PATH — request queued only"}
    # Server-side hash guard, re-checked at spawn time (queue_regen checked at queue time; a request
    # replayed later from the durable queue must not run against a spec that drifted in between).
    # This is the ONLY hash check — the agent's allowlist has no hash command, so the prompt
    # deliberately does not ask it to verify (see regen_prompt docstring).
    try:
        current = current_hash(safe_html(html_rel))
    except (OSError, ValueError, json.JSONDecodeError) as exc:
        return {"auto": False, "reason": f"cannot hash spec before spawn: {exc} — request queued only"}
    if current != expected_hash:
        return {"auto": False, "reason": f"hash drift before spawn: expected {expected_hash}, current {current} — not spawned"}
    log = regen_log_path(html_rel)
    handle = log.open("w", encoding="utf-8")
    try:
        proc = subprocess.Popen(
            [
                claude, "-p", regen_prompt(html_rel, expected_hash, text),
                "--model", REVIEW_MODEL, "--effort", REVIEW_EFFORT,
                "--output-format", "stream-json", "--verbose",
                "--permission-mode", "default",
                "--allowedTools",
                "Read,Glob,Grep,"
                f"Edit({REGEN_SCOPE}/**),"
                "Bash(python3 .claude/scripts/ui-spec-render.py *),"
                "Bash(python3 .claude/scripts/ui-spec-validator.py *)",
            ],
            cwd=ROOT, stdin=subprocess.DEVNULL, stdout=handle, stderr=subprocess.STDOUT, start_new_session=True,
        )
    finally:
        handle.close()
    atomic_pretty_json(regen_status_path(html_rel), {
        "html": html_rel, "pid": proc.pid, "startedAt": utc_now(), "startedMono": time.time(),
        "expectedHash": expected_hash, "log": relative(log), "state": "running",
        "model": REVIEW_MODEL, "effort": REVIEW_EFFORT, "dirtyBefore": sorted(git_dirty()),
    })
    return {"auto": True, "log": relative(log), "pid": proc.pid, "model": REVIEW_MODEL, "effort": REVIEW_EFFORT}


def pid_alive(pid) -> bool:
    if not pid:
        return False
    try:
        os.kill(pid, 0)
    except PermissionError:
        return True
    except OSError:
        # ProcessLookupError (POSIX) and Windows WinError 87 ("parameter is incorrect",
        # raised for a stale/recycled pid) both mean the process is gone.
        return False
    return True


def _humanize_stream_line(line: str) -> str | None:
    """Turn one stream-json event into a short human phrase, or None if it carries nothing useful."""
    try:
        event = json.loads(line)
    except (ValueError, json.JSONDecodeError):
        return None
    etype = event.get("type")
    if etype == "assistant":
        for block in event.get("message", {}).get("content", []):
            if block.get("type") == "text" and block.get("text", "").strip():
                return " ".join(block["text"].split())[:160]
            if block.get("type") == "tool_use":
                name = block.get("name", "tool")
                command = (block.get("input", {}) or {}).get("command", "")
                return f"🔧 {name}: {' '.join(str(command).split())[:80]}" if command else f"🔧 {name}"
    if etype == "result":
        return "✓ agent hoàn tất" if not event.get("is_error") else "✗ agent báo lỗi"
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


def mark_regen_terminal(html_rel: str, state: str, out_of_scope: list[str] | None = None) -> None:
    """Flip the status file + matching queue record to a terminal state so re-scans are accurate and
    the dashboard poll stops deterministically instead of counting down a fixed timeout."""
    status_path = regen_status_path(html_rel)
    status = load_json_object(status_path)
    if status:
        status["state"] = state
        status["endedAt"] = utc_now()
        if out_of_scope:
            status["outOfScope"] = out_of_scope
        atomic_pretty_json(status_path, status)
    if not REGEN_QUEUE.exists():
        return
    lines, changed = [], False
    for raw in REGEN_QUEUE.read_text(encoding="utf-8").splitlines():
        raw = raw.strip()
        if not raw:
            continue
        try:
            record = json.loads(raw)
        except (ValueError, json.JSONDecodeError):
            lines.append(raw)
            continue
        if record.get("html") == html_rel and not record.get("done"):
            record["done"], record["state"] = True, state
            changed = True
        lines.append(json.dumps(record, ensure_ascii=False))
    if changed:
        REGEN_QUEUE.write_text("\n".join(lines) + "\n", encoding="utf-8")


def evaluate_regen(html_rel: str) -> dict:
    """Current state of an in-flight regenerate for one screen (drives the dashboard panel)."""
    status = load_json_object(regen_status_path(html_rel))
    if not status:
        return {"state": "idle"}
    if status.get("state") in ("done", "error", "cancelled", "blocked"):
        return {"state": status["state"], "outOfScope": status.get("outOfScope", [])}
    started = status.get("startedMono")
    elapsed = int(time.time() - started) if isinstance(started, (int, float)) else 0
    expected, pid = status.get("expectedHash"), status.get("pid")
    try:
        current = current_hash(safe_html(html_rel))
    except (OSError, ValueError, json.JSONDecodeError):
        current = None  # spec mid-rewrite → treat as still running
    alive = pid_alive(pid)
    if current and current != expected:
        stray = regen_out_of_scope(status)
        if stray:
            mark_regen_terminal(html_rel, "blocked", stray)
            return {"state": "blocked", "newHash": current, "elapsed": elapsed, "outOfScope": stray}
        rendered, detail = html_rendered(html_rel)
        if rendered:
            mark_regen_terminal(html_rel, "done")
            return {"state": "done", "newHash": current, "elapsed": elapsed, "outOfScope": []}
        if alive:
            # Spec updated, render still pending. The agent edits the spec first and re-renders
            # seconds later — a poll landing inside that window must NOT mark the run terminal
            # (this exact race mislabelled a fully successful run as error). Terminal verdicts
            # only once the process is gone.
            return {"state": "running", "elapsed": elapsed, "pid": pid, **regen_progress(html_rel),
                    "model": status.get("model"), "effort": status.get("effort")}
        mark_regen_terminal(html_rel, "error")
        return {"state": "error", "elapsed": elapsed, "outOfScope": [],
                "lastLine": f"spec đã đổi nhưng HTML chưa render lại được: {detail}"}
    if not alive:
        # process gone but spec unchanged → it failed (or verified a hash mismatch and bailed)
        stray = regen_out_of_scope(status)
        state = "blocked" if stray else "error"
        mark_regen_terminal(html_rel, state, stray)
        return {"state": state, "elapsed": elapsed, "outOfScope": stray, **regen_progress(html_rel)}
    return {"state": "running", "elapsed": elapsed, "pid": pid, **regen_progress(html_rel),
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
    """Lightweight snapshot embedded in each screen row so a page reload can resume tracking a run
    that is still in flight (None when nothing is actively regenerating)."""
    status = load_json_object(regen_status_path(html_rel))
    if not status or status.get("state") != "running" or not pid_alive(status.get("pid")):
        return None
    started = status.get("startedMono")
    return {
        "state": "running",
        "elapsed": int(time.time() - started) if isinstance(started, (int, float)) else 0,
        "model": status.get("model"), "effort": status.get("effort"),
    }


CONTENT_TYPES = {
    ".html": "text/html; charset=utf-8", ".css": "text/css; charset=utf-8",
    ".js": "application/javascript; charset=utf-8", ".json": "application/json; charset=utf-8",
    ".png": "image/png", ".jpg": "image/jpeg", ".jpeg": "image/jpeg",
    ".webp": "image/webp", ".svg": "image/svg+xml", ".gif": "image/gif",
}


class ReviewRequestHandler(BaseHTTPRequestHandler):
    """Token-guarded loopback API with a narrowly scoped mockup file server."""

    server_version = "AgentUiReview/2"

    def log_message(self, *_args) -> None:
        return

    def send_payload(self, status: int, payload, content_type: str = "application/json; charset=utf-8") -> None:
        body = payload if isinstance(payload, (bytes, bytearray)) else json.dumps(payload, ensure_ascii=False).encode("utf-8")
        self.send_response(status)
        self.send_header("Content-Type", content_type)
        self.send_header("Content-Length", str(len(body)))
        self.send_header("Cache-Control", "no-store")
        self.end_headers()
        if self.command != "HEAD":
            self.wfile.write(body)

    def authorized(self) -> bool:
        supplied = self.headers.get("X-UI-Review-Token", "")
        return bool(self.server.api_token) and hmac.compare_digest(supplied, self.server.api_token)

    def dashboard(self) -> None:
        config = {**self.server.base_config, "serve": True, "token": self.server.api_token}
        source, _ = render_dashboard_html(config)
        self.send_payload(200, source.encode("utf-8"), "text/html; charset=utf-8")

    def do_GET(self) -> None:
        parsed = urlparse(self.path)
        path = unquote(parsed.path)
        if path in ("/api/screens", "/api/regen-status"):
            if not self.authorized():
                self.send_payload(403, {"ok": False, "error": "Invalid review token"})
                return
            if path == "/api/screens":
                self.send_payload(200, {"ok": True, "screens": discover_screens()})
                return
            html_rel = (parse_qs(parsed.query).get("html") or [""])[0]
            try:
                safe_html(html_rel)
                self.send_payload(200, {"ok": True, **evaluate_regen(html_rel)})
            except (OSError, ValueError) as exc:
                self.send_payload(400, {"ok": False, "error": str(exc)})
            return
        if path in ("/", "/_ui-review.html"):
            self.dashboard()
            return
        try:
            target = (MOCKUPS_ROOT / path.lstrip("/")).resolve()
            target.relative_to(MOCKUPS_ROOT.resolve())
        except (OSError, ValueError):
            self.send_payload(403, {"ok": False, "error": "Forbidden"})
            return
        if not target.is_file():
            self.send_payload(404, {"ok": False, "error": "Not found"})
            return
        self.send_payload(200, target.read_bytes(), CONTENT_TYPES.get(target.suffix.lower(), "application/octet-stream"))

    def do_POST(self) -> None:
        path = urlparse(self.path).path
        if path not in ("/api/approve", "/api/refresh", "/api/apply-decisions", "/api/regenerate", "/api/regen-cancel"):
            self.send_payload(404, {"ok": False, "error": "Unknown endpoint"})
            return
        if not self.authorized():
            self.send_payload(403, {"ok": False, "error": "Invalid review token"})
            return
        try:
            length = int(self.headers.get("Content-Length", "0"))
            if length < 0 or length > 256 * 1024:
                raise ValueError("Request body is too large")
            payload = json.loads(self.rfile.read(length) or b"{}")
            if not isinstance(payload, dict):
                raise ValueError("Expected a JSON object")
            if path == "/api/regenerate":
                record = queue_regen(payload.get("html"), payload.get("specHash"), payload.get("text", ""))
                spawned = spawn_regen(record["html"], record["specHash"], record["text"]) if self.server.auto_regen else {"auto": False}
                self.send_payload(200, {"ok": True, "queued": True, **spawned})
                return
            if path == "/api/regen-cancel":
                html_rel = payload.get("html", "")
                safe_html(html_rel)
                self.send_payload(200, {"ok": True, **cancel_regen(html_rel)})
                return
            if path == "/api/apply-decisions":
                result = apply_decisions(payload["html"], payload["specHash"], payload.get("decisions"))
            elif path == "/api/approve":
                result = approve_items(payload.get("items"))
            else:
                result = {"ok": True, "screens": discover_screens()}
            self.send_payload(200, result)
        except (KeyError, OSError, ValueError, TypeError, json.JSONDecodeError, RuntimeError) as exc:
            self.send_payload(400, {"ok": False, "error": str(exc)})


def create_review_server(port: int, api_token: str, auto_regen: bool = True, base_config: dict | None = None) -> HTTPServer:
    if not api_token:
        raise ValueError("Review server requires an API token")
    server = HTTPServer(("127.0.0.1", port), ReviewRequestHandler)
    server.api_token = api_token
    server.auto_regen = auto_regen
    server.base_config = base_config or {"trusted": True, "model": REVIEW_MODEL, "effort": REVIEW_EFFORT}
    return server


def serve_dashboard(port: int = 8765, open_browser: bool = True, auto_regen: bool = True) -> None:
    token = secrets.token_urlsafe(32)
    active_regen = bool(auto_regen and find_claude())
    config = ensure_claude_launch_config()
    try:
        server = create_review_server(port, token, active_regen, config)
    except OSError:
        server = create_review_server(0, token, active_regen, config)
    actual_port = server.server_address[1]
    url = f"http://127.0.0.1:{actual_port}/"
    print(json.dumps({"ok": True, "dashboard": url, "pending": len(discover_screens()), "autoRegen": active_regen}, ensure_ascii=False), flush=True)
    if open_browser:
        webbrowser.open(url)
    try:
        server.serve_forever()
    except KeyboardInterrupt:
        pass
    finally:
        server.server_close()


def parse_item(value: str) -> tuple[str, str]:
    try:
        html, digest = value.rsplit("=", 1)
    except ValueError as exc:
        raise ValueError("Approval item must be HTML=SPEC_HASH") from exc
    return html, digest


def main() -> int:
    # Windows consoles default to cp1252, which cannot encode the Vietnamese report text.
    if hasattr(sys.stdout, "reconfigure"):
        sys.stdout.reconfigure(encoding="utf-8")
    parser = argparse.ArgumentParser(description=__doc__)
    sub = parser.add_subparsers(dest="command", required=True)
    gen = sub.add_parser("generate"); gen.add_argument("--open", action="store_true")
    serve = sub.add_parser("serve"); serve.add_argument("--port", type=int, default=8765); serve.add_argument("--no-open", action="store_true"); serve.add_argument("--no-auto-regen", action="store_true", help="queue regenerate requests without spawning the headless agent")
    sub.add_parser("list")
    approve = sub.add_parser("approve"); approve.add_argument("--item", action="append", required=True); approve.add_argument("--existing-png", action="store_true")
    auto = sub.add_parser("auto-approve", help="freeze every validating pending draft to its PNG contract (default-approve)")
    auto.add_argument("--task", action="append", help="limit to screens referenced by these planning task files")
    auto.add_argument("--open", action="store_true", help="open each freshly approved screen's HTML in the default browser (interactive /ui-mockup runs; leave off in planning/headless loops)")
    args = parser.parse_args()
    if args.command == "generate":
        screens = generate_dashboard(args.open)
        print(json.dumps({"ok": True, "dashboard": relative(DASHBOARD_PATH), "pending": len(screens)}, ensure_ascii=False, indent=2)); return 0
    if args.command == "serve":
        serve_dashboard(args.port, open_browser=not args.no_open, auto_regen=not args.no_auto_regen); return 0
    if args.command == "list":
        print(json.dumps({"ok": True, "screens": discover_screens()}, ensure_ascii=False, indent=2)); return 0
    if args.command == "auto-approve":
        result = auto_approve(args.task)
        if args.open and result.get("approved"):
            result["opened"] = open_approved(result["approved"])
        print(json.dumps(result, ensure_ascii=False, indent=2))
        return 0 if result["ok"] else 1
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
