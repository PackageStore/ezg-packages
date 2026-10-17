#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
qadevice.py — device driver for the /auto-test-device skill (EZG).

A CLI that lets an agent play the game like a real user, then log bugs:
  * Android (adb: real device / emulator, including auto-booting an AVD) — real touch input, logcat, perf, network, rotation.
  * iOS Simulator (xcrun simctl; touches via `idb` when available, otherwise via the probe).
  * Real iPhone (xcrun devicectl; screenshots/touches via the probe — needs a build with EZG QA Probe).
  * EZG QA Probe (optional, inside the build): UI tree with coordinates, visual audit (text overflow, missing glyphs,
    covered buttons…), read state / call cheat methods via reflection, Unity logs with stacks, bug context.
  * Logs bugs to BugHub with the Bug Logger contract (POST /v1/reports — identical to the in-game Bug Logger button).

Run:   python3 .claude/skills/auto-test-device/scripts/qadevice.py <command> [args]     (Windows: py …)
Session state: <git-common-dir>/auto-test-device/ (never committed). `qadevice.py -h` lists the commands.
Standard library only; Pillow (pip install pillow) enables thumbnails, the coordinate grid, frame diffs and image audits.
"""
from __future__ import annotations

import argparse
import base64
import contextlib
import datetime as dt
import fnmatch
import glob
import hashlib
import html
import io
import json
import os
import random
import re
import shutil
import signal
import string
import struct
import subprocess
import sys
import tempfile
import time
import unicodedata
import urllib.error
import urllib.parse
import urllib.request
import uuid

try:  # Pillow is optional — without it everything still runs, only image processing is lost.
    from PIL import Image, ImageChops, ImageDraw, ImageFont, ImageStat
except Exception:  # pragma: no cover - machine dependent
    Image = None

VERSION = "1.6.0"
PROBE_PORT = 7531
PROBE_PORT_SPAN = 5
PROBE_TRIGGER_FILE = "ezg_qa_probe.txt"
CHEAT_PENDING = ("", "waiting_init", "checking")
CHEAT_UNLOCK_RETRY_S = 30  # re-ask CheatGate at most once per 30 s (the server rate-limits)
DEFAULT_ENDPOINT = "https://bug-reporter.developer-a1f.workers.dev"
VIEW_LONG_EDGE = 1280
BUG_SHOT_LONG_EDGE = 1280
BUG_SHOT_MAX_BYTES = 3 * 1024 * 1024
MAX_LOG_BYTES = 1024 * 1024
MAX_SAVE_BYTES = 5 * 1024 * 1024
TITLE_MAX = 100
FIELD_MAX = 4000
EXTRA_MAX = 32
IS_WIN = os.name == "nt"
IS_MAC = sys.platform == "darwin"
SCRIPT_DIR = os.path.dirname(os.path.abspath(__file__))
SKILL_DIR = os.path.dirname(SCRIPT_DIR)
PROBE_PACKAGE = "com.ezg.qa-probe"
PROBE_MIN_VERSION = "1.2.1"  # package version this skill needs (kept in sync with EzgQaProbe.VERSION)
# Probe features newer than PROBE_MIN_VERSION: on a build with an older probe the command reports `unsupported`, not an error.
PROBE_FEATURES = {"pseudoloc": "1.3.0", "flags": "1.3.0", "screenBaseTypes": "1.3.0"}
AUTOTEST_PACKAGE = "com.ezg.autotest"
AUTOTEST_MIN_VERSION = "0.2.0"  # autotest version with the liveness filter + string report.json — older only warns (noisy)
CHEATGATE_PACKAGE = "com.ezg.cheat-gate"
BUGLOGGER_PACKAGE = "com.ezg.buglogger"
FEATUREHUB_PACKAGE = "com.ezg.featurehub"
FEATUREHUB_MIN_VERSION = "0.4.0"  # version with FeatureHubService/FeatureHubAiInstaller, which the ezg MCP snippets call
# Feature Hub items this skill depends on — `doctor` prints them so the model checks/updates them via the ezg MCP
# (check_project_updates → get_install_steps → unity_execute_code). Project values do NOT live here.
QA_HUB_ITEMS = {
    "upm": [PROBE_PACKAGE, AUTOTEST_PACKAGE, CHEATGATE_PACKAGE, BUGLOGGER_PACKAGE],
    "ai": ["Skills/auto-test-device", "Skills/bughub-setup", "Skills/auto-clear", "Skills/fix-bug",
           "Docs/QaProfile.template", "Scripts/project_profile.py", "Scripts/bootstrap.sh", "Scripts/bootstrap.ps1"],
}
INSTALL_RECORD_REL = os.path.join("ProjectSettings", "EzgFeatureHub", "install-record.json")
# MCP server the skill needs on the running machine (HTTP + Google OAuth @easygoing.vn). Missing → `claude mcp add --transport http --scope user <name> <url>`.
MCP_EZG_URL = "https://ezg-mcp.developer-a1f.workers.dev/mcp"
PROBE_OVERRIDE = os.path.join("Assets", "Resources", "EzgQaProbeConfig.json")
CAPTURE_HIDE_SECONDS = 3  # the floating Bug Logger button is hidden at most this long around one adb/simctl capture (the probe shows it again afterwards)
LEGACY_PROBE_MARK = "FILE DO TOOL QUẢN LÝ"  # marker in files v1.0/1.1 copied into Assets/EzgQaProbe/ (matched in file content) — now replaced by the package
REPORTER = "auto-test-device"

SEVERITIES = ("critical", "high", "medium", "low")
BUG_TYPES = ("crash", "logic", "visual", "ux", "perf", "gd")
STOP_SETTLE_SECONDS = 2.0  # `app restart|stop` default: stay on Home this many seconds (game gets OnApplicationPause → saves) before killing
OVERLAY_TYPES = ("APPLICATION_OVERLAY", "SYSTEM_ALERT", "SYSTEM_OVERLAY", "SYSTEM_ERROR", "PHONE")
OVERLAY_CACHE_SECONDS = 60  # other apps' overlay list: re-scanned at most once a minute (dumpsys input is slow)
VALUE_PRINT_MAX = 1500      # `call` / `state` print values up to this many chars (--full / --out for everything)
AD_CLOSE_WORDS = ("close", "skip", "dismiss", "dong", "bo qua")  # accents stripped, lower case (matched against ad UI labels)
AD_CLOSE_MARKS = ("x", "×", "✕", "✖", "╳")
# API-error retry (/auto-clear skill, StopFailure hook — the hook waits 30 s, then types this prompt). ASCII, one line, ≤ 200 chars.
API_RETRY_PROMPT = ("Continue /auto-test-device: the previous turn stopped on an API error - resume the exact step "
                    "in progress (QA session still open).")
RETRY_EVERY_S = 30       # `retry`: re-run the failing command every this many seconds
RETRY_MAX_WAIT_S = 540   # `retry`: one call keeps trying at most this many seconds (fits the Bash tool's 600 s timeout)
RETRY_PENDING_RC = 75    # `retry` timed out with the command still failing — call `retry` again, do not stop the session
RETRY_FATAL_RC = (2, 4, 130)  # bad syntax · BugHub not set up · Ctrl-C: retrying is pointless
EXPECT_TIMEOUT_S = 6.0     # `tap --expect`: wait at most this many seconds for the expectation
AUTO_AUDIT = True          # auto-audit when the screen changes after an action (global flag --no-auto-audit turns it off)
# `flags[]` (camelCase) of probe ≥1.3 — checks ported from com.ezg.autotest's UiAuditUtil: flag → (kind, severity, message).
# Unknown flag (probe newer than the skill) → kebab-case kind of the same name, severity medium. flagDetail{flag: detail} is appended to the message.
PROBE_FLAGS_13 = {
    "deadButton": ("dead-button", "medium", "button is clickable but onClick is empty and there is no touch handler (dead button?)"),
    "noRaycastTarget": ("no-raycast-target", "high", "interactive element has no Graphic with Raycast Target on — cannot be tapped"),
    "overlapButton": ("overlap-button", "medium", "two sibling buttons overlap >50% — taps hit the other button"),
    "missingSprite": ("sprite-missing-colored", "low", "Image has no sprite and shows a solid color block (missing art or intended?)"),
    "solidBackground": ("solid-background", "info", "sprite-less Image covers ≥50% of the screen (plain background/backdrop)"),
}
# Probe 1.2.1 flags already have their own branch in audit_elements (not mapped through PROBE_FLAGS_13).
PROBE_FLAG_ALIASES = {"overflow", "truncated", "missingGlyph", "noSprite", "missingMaterial", "outsideContainer", "interactable"}

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import qaprofile  # noqa: E402  — pure part of the QA profile (qa schema, convention detection, charter, coverage, CSV, autotest)

QA_DEFAULTS = qaprofile.QA_DEFAULTS

if hasattr(sys.stdout, "reconfigure"):
    try:
        sys.stdout.reconfigure(encoding="utf-8", errors="replace")
        sys.stderr.reconfigure(encoding="utf-8", errors="replace")
    except Exception:
        pass


# ───────────────────────────────────────────────────────────── common utilities

class QaError(Exception):
    """Error carrying a message for the agent (printed, then exit code 1)."""


class NotSupported(QaError):
    pass


def _dec(b):
    if b is None:
        return ""
    if isinstance(b, str):
        return b
    return b.decode("utf-8", "replace")


def run(cmd, timeout=60, binary=False, input_bytes=None, env=None, cwd=None):
    """Run a command, never raises: returns (returncode, stdout, stderr). Timeout → 124, command missing → 127."""
    try:
        p = subprocess.run(cmd, stdout=subprocess.PIPE, stderr=subprocess.PIPE, timeout=timeout,
                           input=input_bytes, env=env, cwd=cwd)
    except subprocess.TimeoutExpired as e:
        return 124, (e.stdout or b"") if binary else _dec(e.stdout), "timeout after %ss" % timeout
    except (FileNotFoundError, PermissionError, OSError) as e:
        return 127, b"" if binary else "", str(e)
    return p.returncode, p.stdout if binary else _dec(p.stdout), _dec(p.stderr)


def which(name):
    return shutil.which(name)


def now_iso():
    t = dt.datetime.now(dt.timezone.utc)
    return t.strftime("%Y-%m-%dT%H:%M:%S.") + "%03dZ" % (t.microsecond // 1000)


def local_stamp():
    return dt.datetime.now().strftime("%Y%m%d-%H%M%S")


def slug(text, n=32):
    text = strip_accents(text or "").lower()
    text = re.sub(r"[^a-z0-9]+", "-", text).strip("-")
    return text[:n] or "x"


def strip_accents(s):
    s = unicodedata.normalize("NFD", s or "")
    s = "".join(c for c in s if unicodedata.category(c) != "Mn")
    return s.replace("đ", "d").replace("Đ", "D")


def read_text(path, default=""):
    try:
        with open(path, "r", encoding="utf-8", errors="replace") as f:
            return f.read()
    except OSError:
        return default


def write_text(path, text):
    os.makedirs(os.path.dirname(path), exist_ok=True)
    with open(path, "w", encoding="utf-8") as f:
        f.write(text)


def read_json(path, default=None):
    try:
        with open(path, "r", encoding="utf-8") as f:
            return json.load(f)
    except (OSError, ValueError):
        return default


def write_json(path, data):
    os.makedirs(os.path.dirname(path), exist_ok=True)
    tmp = path + ".tmp"
    with open(tmp, "w", encoding="utf-8") as f:
        json.dump(data, f, ensure_ascii=False, indent=1)
    os.replace(tmp, path)


def append_jsonl(path, obj):
    os.makedirs(os.path.dirname(path), exist_ok=True)
    with open(path, "a", encoding="utf-8") as f:
        f.write(json.dumps(obj, ensure_ascii=False) + "\n")


def read_jsonl(path):
    out = []
    for line in read_text(path).splitlines():
        line = line.strip()
        if not line:
            continue
        try:
            out.append(json.loads(line))
        except ValueError:
            pass
    return out


def clip(s, n):
    s = s or ""
    return s if len(s) <= n else s[: max(0, n - 1)] + "…"


def pid_alive(pid):
    if not pid:
        return False
    if IS_WIN:
        rc, out, _ = run(["tasklist", "/FI", "PID eq %d" % pid], timeout=10)
        return str(pid) in out
    try:
        os.kill(pid, 0)
        return True
    except OSError:
        return False


def kill_pid(pid):
    if not pid:
        return
    try:
        if IS_WIN:
            run(["taskkill", "/F", "/T", "/PID", str(pid)], timeout=10)
        else:
            os.kill(pid, signal.SIGTERM)
    except OSError:
        pass


def spawn_background(cmd, out_path):
    """Start a background process detached from the current shell, stdout appended to out_path. Returns the pid."""
    os.makedirs(os.path.dirname(out_path), exist_ok=True)
    f = open(out_path, "ab")
    kw = {}
    if IS_WIN:
        kw["creationflags"] = 0x00000008 | 0x00000200  # DETACHED_PROCESS | CREATE_NEW_PROCESS_GROUP
    else:
        kw["start_new_session"] = True
    p = subprocess.Popen(cmd, stdout=f, stderr=subprocess.STDOUT, stdin=subprocess.DEVNULL, **kw)
    f.close()
    return p.pid


# ───────────────────────────────────────────────────────────── repo / project / state

def repo_root():
    rc, out, _ = run(["git", "rev-parse", "--show-toplevel"], timeout=10)
    if rc == 0 and out.strip():
        return os.path.abspath(out.strip())
    return os.path.abspath(os.getcwd())


def state_root():
    env = os.environ.get("QADEVICE_HOME")
    if env:
        os.makedirs(env, exist_ok=True)
        return env
    rc, out, _ = run(["git", "rev-parse", "--path-format=absolute", "--git-common-dir"], timeout=10)
    if rc != 0 or not out.strip():
        rc, out, _ = run(["git", "rev-parse", "--git-common-dir"], timeout=10)
        out = os.path.abspath(out.strip()) if rc == 0 and out.strip() else ""
    base = out.strip()
    if base:
        root = os.path.join(base, "auto-test-device")
    else:
        root = os.path.join(os.path.expanduser("~"), ".ezg-auto-test-device", slug(os.path.basename(repo_root())))
    os.makedirs(root, exist_ok=True)
    return root


def project_settings(root):
    text = read_text(os.path.join(root, "ProjectSettings", "ProjectSettings.asset"))
    info = {"ids": {}, "productName": "", "bundleVersion": "", "companyName": ""}
    m = re.search(r"applicationIdentifier:\s*\n((?:[ \t]{4,}\S[^\n]*\n)+)", text)
    if m:
        for line in m.group(1).splitlines():
            if ":" in line:
                k, v = line.strip().split(":", 1)
                info["ids"][k.strip()] = v.strip()
    for key in ("productName", "bundleVersion", "companyName"):
        mm = re.search(r"^\s*%s:\s*(.*)$" % key, text, re.M)
        if mm:
            info[key] = mm.group(1).strip()
    pv = read_text(os.path.join(root, "ProjectSettings", "ProjectVersion.txt"))
    mv = re.search(r"m_EditorVersion:\s*(\S+)", pv)
    info["unityVersion"] = mv.group(1) if mv else ""
    return info


def project_profile(root):
    return read_json(os.path.join(root, ".claude", "project-profile.json"), {}) or {}


def find_buglogger_settings(root):
    found = []
    assets = os.path.join(root, "Assets")
    for dirpath, dirnames, filenames in os.walk(assets):
        dirnames[:] = [d for d in dirnames if not d.startswith(".") and d not in ("StreamingAssets",)]
        if os.path.basename(dirpath) == "Resources" and "BugLoggerSettings.asset" in filenames:
            path = os.path.join(dirpath, "BugLoggerSettings.asset")
            text = read_text(path)
            code = re.search(r"^\s*projectCode:\s*(.*)$", text, re.M)
            ep = re.search(r"^\s*endpointOverride:\s*(.*)$", text, re.M)
            found.append({"path": os.path.relpath(path, root),
                          "projectCode": (code.group(1).strip() if code else "").strip("'\""),
                          "endpointOverride": (ep.group(1).strip() if ep else "").strip("'\"")})
    return found


def buglogger_package_present(root):
    if os.path.isfile(os.path.join(root, "Packages", "com.ezg.buglogger", "package.json")):
        return True
    return '"com.ezg.buglogger"' in read_text(os.path.join(root, "Packages", "manifest.json"))


# ───────────────────────────────────────────────────────────── tool lookup

def _exe(name):
    return name + (".exe" if IS_WIN else "")


def sdk_roots():
    roots = []
    for var in ("ANDROID_SDK_ROOT", "ANDROID_HOME"):
        if os.environ.get(var):
            roots.append(os.environ[var])
    home = os.path.expanduser("~")
    roots += [os.path.join(home, "Library", "Android", "sdk"), os.path.join(home, "Android", "Sdk")]
    if os.environ.get("LOCALAPPDATA"):
        roots.append(os.path.join(os.environ["LOCALAPPDATA"], "Android", "Sdk"))
    patterns = ["/Applications/Unity/Hub/Editor/*/PlaybackEngines/AndroidPlayer/SDK",
                os.path.join(home, "Unity", "Hub", "Editor", "*", "Editor", "Data", "PlaybackEngines", "AndroidPlayer", "SDK"),
                "C:/Program Files/Unity/Hub/Editor/*/Editor/Data/PlaybackEngines/AndroidPlayer/SDK"]
    for pat in patterns:
        roots += sorted(glob.glob(pat), reverse=True)
    return roots


def find_adb():
    if os.environ.get("ADB") and os.path.isfile(os.environ["ADB"]):
        return os.environ["ADB"]
    on_path = which("adb")
    if on_path:
        return on_path
    for root in sdk_roots():
        cand = os.path.join(root, "platform-tools", _exe("adb"))
        if os.path.isfile(cand):
            return cand
    return None


def find_emulator(adb):
    cands = []
    if adb:
        cands.append(os.path.join(os.path.dirname(os.path.dirname(adb)), "emulator", _exe("emulator")))
    for root in sdk_roots():
        cands.append(os.path.join(root, "emulator", _exe("emulator")))
    for c in cands:
        if os.path.isfile(c):
            return c
    return which("emulator")


def find_idb():
    return which("idb")


def has_xcrun():
    return IS_MAC and bool(which("xcrun"))


# ───────────────────────────────────────────────────────────── images

def ensure_pillow():
    """Pillow missing → `pip install --user pillow` once. Returns a status line ("" when already present)."""
    global Image, ImageChops, ImageDraw, ImageFont, ImageStat
    if Image is not None:
        return ""
    cmd = [sys.executable, "-m", "pip", "install", "--user", "--quiet", "--disable-pip-version-check", "pillow"]
    rc, _, err = run(cmd, timeout=300)
    if rc != 0 and "externally-managed" in (err or ""):
        rc, _, err = run(cmd + ["--break-system-packages"], timeout=300)  # writes to the user site only
    try:
        import importlib
        import site
        importlib.invalidate_caches()
        us = site.getusersitepackages()
        if us and us not in sys.path:
            sys.path.append(us)
        from PIL import Image as _im, ImageChops as _ic, ImageDraw as _id, ImageFont as _if, ImageStat as _is
        Image, ImageChops, ImageDraw, ImageFont, ImageStat = _im, _ic, _id, _if, _is
        return "Pillow: auto-installed"
    except Exception:
        return "Pillow: auto-install failed (%s) — still running, without thumbnails/coordinate grid/frame diff" % clip((err or "").strip(), 160)


def png_size(data):
    if not data or data[:8] != b"\x89PNG\r\n\x1a\n" or len(data) < 24:
        return None
    return struct.unpack(">II", data[16:24])


def image_size(path):
    if Image is not None:
        try:
            with Image.open(path) as im:
                return im.size
        except Exception:
            return None
    with open(path, "rb") as f:
        return png_size(f.read(32))


def _resample():
    return getattr(Image, "Resampling", Image).LANCZOS


def _font(size):
    for name in ("Arial.ttf", "Helvetica.ttc", "DejaVuSans.ttf", "arial.ttf", "/System/Library/Fonts/Supplemental/Arial.ttf",
                 "/System/Library/Fonts/Helvetica.ttc", "C:/Windows/Fonts/arial.ttf"):
        try:
            return ImageFont.truetype(name, size)
        except Exception:
            continue
    try:
        return ImageFont.load_default(size=size)
    except Exception:
        return ImageFont.load_default()


def make_view(full_path, view_path, long_edge=VIEW_LONG_EDGE):
    """Quick-view image (JPEG, long edge ≤ long_edge). Returns ((vw, vh), scale). No Pillow → the original image is used."""
    if Image is None:
        size = image_size(full_path)
        return size, 1.0
    with Image.open(full_path) as im:
        im = im.convert("RGB")
        w, h = im.size
        s = min(1.0, float(long_edge) / max(w, h))
        if s < 1.0:
            im = im.resize((max(1, round(w * s)), max(1, round(h * s))), _resample())
        im.save(view_path, "JPEG", quality=84)
        return im.size, s


def encode_bug_jpeg(src_path, long_edge=BUG_SHOT_LONG_EDGE, max_bytes=BUG_SHOT_MAX_BYTES):
    if Image is None:
        if IS_MAC and which("sips"):
            tmp = os.path.join(tempfile.mkdtemp(prefix="qadev"), "shot.jpg")
            rc, _, _ = run(["sips", "-s", "format", "jpeg", "-Z", str(long_edge), src_path, "--out", tmp], timeout=30)
            if rc == 0 and os.path.isfile(tmp):
                with open(tmp, "rb") as f:
                    data = f.read()
                return data if len(data) <= max_bytes else None
        return None
    with Image.open(src_path) as im:
        im = im.convert("RGB")
        w, h = im.size
        s = min(1.0, float(long_edge) / max(w, h))
        if s < 1.0:
            im = im.resize((max(1, round(w * s)), max(1, round(h * s))), _resample())
        for q in (82, 70, 55, 40):
            buf = io.BytesIO()
            im.save(buf, "JPEG", quality=q)
            if buf.tell() <= max_bytes:
                return buf.getvalue()
    return None


def analyze_image(path):
    """Plain black/white screen, magenta patches (broken shader). Returns a list of warnings."""
    if Image is None:
        return []
    warns = []
    with Image.open(path) as im:
        im = im.convert("RGB")
        w, h = im.size
        s = 200.0 / max(w, h)
        small = im.resize((max(1, int(w * s)), max(1, int(h * s))))
        stat = ImageStat.Stat(small.convert("L"))
        mean, std = stat.mean[0], stat.stddev[0]
        if mean < 8 and std < 5:
            warns.append("screen almost entirely BLACK (mean=%.1f) — hung / not rendering?" % mean)
        elif mean > 247 and std < 4:
            warns.append("screen almost plain WHITE (mean=%.1f) — missing render / stuck loading?" % mean)
        px = list(small.getdata())
        magenta = sum(1 for r, g, b in px if r > 235 and g < 40 and b > 235)
        ratio = magenta / float(len(px) or 1)
        if ratio > 0.004:
            warns.append("MAGENTA patch (%.1f%% of the image) — usually a broken shader/material" % (ratio * 100))
    return warns


def diff_ratio(a_path, b_path):
    """(ratio of changed pixels, bbox of the changed area in image-b coordinates) — None when they cannot be compared."""
    if Image is None or not (a_path and b_path and os.path.isfile(a_path) and os.path.isfile(b_path)):
        return None
    with Image.open(a_path) as a, Image.open(b_path) as b:
        if a.size != b.size:
            return None
        w, h = b.size
        s = 160.0 / max(w, h)
        size = (max(1, int(w * s)), max(1, int(h * s)))
        da = a.convert("L").resize(size)
        db = b.convert("L").resize(size)
        diff = ImageChops.difference(da, db).point(lambda v: 255 if v > 22 else 0)
        hist = diff.histogram()
        changed = hist[255] / float(size[0] * size[1])
        box = diff.getbbox()
        if box:
            box = tuple(int(round(v / s)) for v in box)
        return changed, box


def draw_grid(view_path, out_path):
    with Image.open(view_path) as im:
        im = im.convert("RGB")
        d = ImageDraw.Draw(im, "RGBA")
        w, h = im.size
        font = _font(max(11, w // 48))
        for i in range(1, 10):
            x = round(w * i / 10.0)
            d.line([(x, 0), (x, h)], fill=(255, 40, 40, 120), width=1)
            _label(d, (x + 2, 2), str(x), font)
            _label(d, (x + 2, h - 18), str(x), font)
        for i in range(1, 20):
            y = round(h * i / 20.0)
            d.line([(0, y), (w, y)], fill=(40, 120, 255, 120), width=1)
            _label(d, (2, y + 1), str(y), font)
            _label(d, (w - 42, y + 1), str(y), font)
        im.save(out_path, "JPEG", quality=84)


def _label(d, xy, text, font, fg=(255, 255, 255, 255), bg=(0, 0, 0, 170)):
    try:
        box = d.textbbox(xy, text, font=font)
    except Exception:
        box = (xy[0], xy[1], xy[0] + 7 * len(text), xy[1] + 12)
    d.rectangle([box[0] - 1, box[1] - 1, box[2] + 1, box[3] + 1], fill=bg)
    d.text(xy, text, fill=fg, font=font)


def draw_marks(view_path, out_path, marks):
    """marks: list (id, (x, y, w, h) view px, color) — draws boxes + id labels."""
    with Image.open(view_path) as im:
        im = im.convert("RGB")
        d = ImageDraw.Draw(im, "RGBA")
        font = _font(max(11, im.size[0] // 50))
        for mid, (x, y, w, h), color in marks:
            d.rectangle([x, y, x + w, y + h], outline=color + (230,), width=2)
            _label(d, (x + 2, y + 1), str(mid), font, bg=color + (200,))
        im.save(out_path, "JPEG", quality=84)


# ───────────────────────────────────────────────────────────── log analysis

LOGCAT_RE = re.compile(r"^(\d\d-\d\d\s+\d\d:\d\d:\d\d\.\d+)\s+(\d+)\s+(\d+)\s+([VDIWEFAS])\s+(.*?)\s*:\s?(.*)$")
EXC_RE = re.compile(r"\b[A-Z]\w*Exception\b|^Exception\b|\bException:")
PROBE_LINE_RE = re.compile(r"\[EzgQaProbe\] listening (\S+):(\d+) gate=(\w+)")


def log_signature(kind, first_line, frame=""):
    s = re.sub(r"0x[0-9a-fA-F]+", "0x#", first_line or "")
    s = re.sub(r"\d+", "#", s)
    s = re.sub(r"\s+", " ", s).strip()[:160]
    f = re.sub(r"\[0x[0-9a-fA-F]+\]|\d+", "#", frame or "").strip()[:120]
    return hashlib.sha1(("%s|%s|%s" % (kind, s, f)).encode("utf-8")).hexdigest()[:10]


def group_logcat(lines):
    """Merge consecutive logcat lines with the same (ts, pid, tid, level, tag) — that is how Android splits multi-line messages."""
    records = []
    cur = None
    for raw in lines:
        m = LOGCAT_RE.match(raw.rstrip("\r\n"))
        if not m:
            continue
        ts, pid, tid, lvl, tag, msg = m.groups()
        key = (ts, int(pid), int(tid), lvl, tag)
        if cur is not None and cur["key"] == key:
            cur["lines"].append(msg)
            continue
        cur = {"key": key, "ts": ts, "pid": int(pid), "tid": int(tid), "lvl": lvl, "tag": tag, "lines": [msg]}
        records.append(cur)
    return records


def _first_frame(lines):
    for l in lines[1:]:
        t = l.strip()
        if t and not t.startswith("UnityEngine.Debug") and not t.startswith("UnityEngine.Logger"):
            return t
    return ""


def classify_android(records, pkg, app_pids, ignore=()):
    """Turn logcat records into error events: Java/native crash, ANR, app died, LMK, Unity exception/error, missing glyph."""
    events = []
    probe = []
    pids = set(app_pids or [])
    ignore_re = [re.compile(p) for p in ignore if p]
    i = 0
    n = len(records)

    def mine(rec):
        return not pids or rec["pid"] in pids

    def ignored(text):
        return any(r.search(text) for r in ignore_re)

    while i < n:
        r = records[i]
        tag, lvl, first = r["tag"], r["lvl"], (r["lines"][0] if r["lines"] else "")
        text = "\n".join(r["lines"])
        if tag == "Unity" and mine(r):
            pm = PROBE_LINE_RE.search(text)
            if pm:
                probe.append({"host": pm.group(1), "port": int(pm.group(2)), "gate": pm.group(3)})
            elif lvl in ("E", "F", "A") and not ignored(text):
                kind = "exception" if EXC_RE.search(first) else "error"
                events.append({"kind": kind, "severity": "high" if kind == "exception" else "medium", "ts": r["ts"],
                               "first": first.strip(), "lines": r["lines"][:40],
                               "sig": log_signature(kind, first, _first_frame(r["lines"]))})
            elif lvl == "W" and ("font asset" in text and "was not found" in text) and not ignored(text):
                events.append({"kind": "missing-glyph", "severity": "medium", "ts": r["ts"], "first": first.strip(),
                               "lines": r["lines"][:6], "sig": log_signature("missing-glyph", first)})
        elif tag == "AndroidRuntime" and "FATAL EXCEPTION" in first:
            block = list(r["lines"])
            j = i + 1
            while j < n and records[j]["tag"] == "AndroidRuntime" and records[j]["pid"] == r["pid"] and len(block) < 80:
                block += records[j]["lines"]
                j += 1
            joined = "\n".join(block)
            if ("Process: %s" % pkg) in joined or r["pid"] in pids:
                exc = next((l for l in block if EXC_RE.search(l)), first)
                events.append({"kind": "crash-java", "severity": "critical", "ts": r["ts"], "first": exc.strip(),
                               "lines": block[:40], "sig": log_signature("crash-java", exc)})
            i = j
            continue
        elif tag in ("DEBUG", "crash_dump64", "crash_dump32", "tombstoned") and (">>> %s <<<" % pkg) in text:
            block = list(r["lines"])
            j = i + 1
            while j < n and records[j]["tag"] in ("DEBUG",) and len(block) < 60:
                block += records[j]["lines"]
                j += 1
            sigl = next((l for l in block if "signal" in l), first)
            events.append({"kind": "crash-native", "severity": "critical", "ts": r["ts"], "first": sigl.strip(),
                           "lines": block[:40], "sig": log_signature("crash-native", sigl)})
            i = j
            continue
        elif tag == "libc" and "Fatal signal" in first and (r["pid"] in pids or pkg in first):
            events.append({"kind": "crash-native", "severity": "critical", "ts": r["ts"], "first": first.strip(),
                           "lines": r["lines"][:10], "sig": log_signature("crash-native", first)})
        elif tag == "CRASH" and mine(r) and pids:
            events.append({"kind": "crash-native", "severity": "critical", "ts": r["ts"], "first": first.strip(),
                           "lines": r["lines"][:30], "sig": log_signature("crash-native", "unity-crash-handler")})
        elif tag == "ActivityManager":
            if ("ANR in %s" % pkg) in first:
                block = list(r["lines"])
                j = i + 1
                while j < n and records[j]["tag"] == "ActivityManager" and len(block) < 30:
                    block += records[j]["lines"]
                    j += 1
                events.append({"kind": "anr", "severity": "critical", "ts": r["ts"], "first": first.strip(),
                               "lines": block[:30], "sig": log_signature("anr", "ANR in " + pkg)})
                i = j
                continue
            if re.search(r"Process %s \(pid \d+\) has died" % re.escape(pkg), first):
                events.append({"kind": "app-died", "severity": "critical", "ts": r["ts"], "first": first.strip(),
                               "lines": r["lines"][:5], "sig": log_signature("app-died", "has died " + pkg)})
            elif ("Force stopping %s" % pkg) in first or ("stop %s due to" % pkg) in first:
                events.append({"kind": "expected-stop", "severity": "info", "ts": r["ts"], "first": first.strip(),
                               "lines": [], "sig": ""})
        elif tag in ("lowmemorykiller", "lmkd") and pkg in text:
            events.append({"kind": "lmk-kill", "severity": "high", "ts": r["ts"], "first": first.strip(),
                           "lines": r["lines"][:5], "sig": log_signature("lmk-kill", pkg)})
        i += 1
    return events, probe


IOS_CRASH_RE = re.compile(r"Terminating app|EXC_BAD_ACCESS|EXC_CRASH|SIGABRT|SIGSEGV|Fatal error|libc\+\+abi", re.I)


def classify_plain(lines, ignore=()):
    """iOS log (simctl log stream / devicectl --console): find exceptions/crashes by keyword, with a few following lines."""
    events = []
    ignore_re = [re.compile(p) for p in ignore if p]
    for idx, line in enumerate(lines):
        t = line.strip()
        if not t or any(r.search(t) for r in ignore_re):
            continue
        pm = PROBE_LINE_RE.search(t)
        if pm:
            continue
        if IOS_CRASH_RE.search(t):
            events.append({"kind": "crash-native", "severity": "critical", "ts": "", "first": t[:300],
                           "lines": [l.rstrip() for l in lines[idx: idx + 25]], "sig": log_signature("crash-native", t)})
        elif EXC_RE.search(t) and "UnityEngine.DebugLogHandler" not in t:
            events.append({"kind": "exception", "severity": "high", "ts": "", "first": t[:300],
                           "lines": [l.rstrip() for l in lines[idx: idx + 15]], "sig": log_signature("exception", t)})
    return events


def classify_probe_logs(entries, ignore=()):
    events = []
    ignore_re = [re.compile(p) for p in ignore if p]
    for e in entries:
        typ = e.get("type", "")
        msg = e.get("msg", "")
        if typ not in ("Exception", "Error", "Assert") or any(r.search(msg) for r in ignore_re):
            continue
        stack = (e.get("stack") or "").splitlines()
        kind = "exception" if typ == "Exception" or EXC_RE.search(msg) else "error"
        first = msg.splitlines()[0] if msg else typ
        events.append({"kind": kind, "severity": "high" if kind == "exception" else "medium", "ts": e.get("t", ""),
                       "first": first, "lines": msg.splitlines()[:10] + stack[:25],
                       "sig": log_signature(kind, first, stack[0] if stack else "")})
    return events


# ───────────────────────────────────────────────────────────── probe

# ───────────────────────────────────────────────────────────── other apps' overlays + native views (Android)

def parse_overlay_windows(window_dump, game_pkg=""):
    """`dumpsys window windows` → floating windows of THIRD-PARTY apps: [{token, package, uid, type}].
    Skips system windows (uid < 10000: systemui, ROM) and the game's own."""
    out = []
    for block in re.split(r"\n\s*Window #\d+ ", "\n" + (window_dump or ""))[1:]:
        head = re.match(r"Window\{(\w+) u\d+ ([^}]*)\}", block)
        ty = re.search(r"\bty=([A-Z_]+)", block)
        if not head or not ty or ty.group(1) not in OVERLAY_TYPES:
            continue
        pkg = re.search(r"\bpackage=(\S+)", block)
        uid = re.search(r"\bmOwnerUid=(\d+)", block)
        package = pkg.group(1) if pkg else head.group(2).split("/")[0].strip()
        uid_val = int(uid.group(1)) if uid else 0
        if (uid and uid_val % 100000 < 10000) or package in ("android", "com.android.systemui") or package == game_pkg:
            continue
        out.append({"token": head.group(1), "package": package, "uid": uid_val, "type": ty.group(1)})
    return out


def parse_input_regions(input_dump):
    """`dumpsys input` → {window token: {"rect": [x, y, w, h] device px, "visible": bool}} from the real touch region
    (touchableRegion, else frame). Takes the first entry of each token (the current window list)."""
    res = {}

    def flush(tok, text):
        if not tok or tok in res:
            return
        if "touchableRegion=<empty>" in text:
            res[tok] = {"rect": [0, 0, 0, 0], "visible": False}
            return
        r = (re.search(r"touchableRegion=\[(-?\d+),(-?\d+)\]\[(-?\d+),(-?\d+)\]", text)
             or re.search(r"\bframe=\[(-?\d+),(-?\d+)\]\[(-?\d+),(-?\d+)\]", text))
        if not r:
            return
        x1, y1, x2, y2 = (int(v) for v in r.groups())
        hidden = bool(re.search(r"NOT_VISIBLE|NOT_TOUCHABLE|\bvisible=false", text))
        res[tok] = {"rect": [x1, y1, x2 - x1, y2 - y1], "visible": not hidden}

    # Each window is an entry "N: name=<token> …" — with/without quotes depending on the Android version, on one line or continued on deeper-indented lines.
    tok, indent, buf = None, 0, []
    for line in (input_dump or "").splitlines():
        m = re.match(r"(\s*)\d+: name='?(?:Window\{)?(\w+) ", line)  # API 35+: name=abc Foo, — older versions quote it
        if m:
            flush(tok, "\n".join(buf))
            tok, indent, buf = m.group(2), len(m.group(1)), [line]
        elif tok and line.strip() and len(line) - len(line.lstrip()) > indent:
            buf.append(line)
        else:
            flush(tok, "\n".join(buf))
            tok, buf = None, []
    flush(tok, "\n".join(buf))
    return res


def overlay_rects(window_dump, input_dump, game_pkg=""):
    """Other apps' overlays currently receiving touches: [{package, type, rect:[x,y,w,h] device px}]."""
    regions = parse_input_regions(input_dump)
    out = []
    for w in parse_overlay_windows(window_dump, game_pkg):
        r = regions.get(w["token"])
        if r and r["visible"] and r["rect"][2] > 0 and r["rect"][3] > 0:
            out.append(dict(w, rect=r["rect"]))
    return out


def point_in_rects(x, y, items):
    """First item (with "rect" [x,y,w,h]) containing point (x, y); None if there is none."""
    for it in items or []:
        rx, ry, rw, rh = it["rect"]
        if rx <= x < rx + rw and ry <= y < ry + rh:
            return it
    return None


def uncovered_point(rect, covers, inset=0.2):
    """A point inside `rect` (minus an `inset` border — real buttons are usually smaller than their bounds) not under `covers`; None if fully covered."""
    x, y, w, h = rect
    for fy in (0.5, 0.35, 0.65, inset, 1 - inset):
        for fx in (0.5, 0.35, 0.65, inset, 1 - inset):
            px, py = int(x + w * fx), int(y + h * fy)
            if not point_in_rects(px, py, covers):
                return px, py
    return None


def parse_uia_nodes(xml):
    """`uiautomator dump` XML → [{text, desc, id, cls, pkg, clickable, enabled, rect}]."""
    nodes = []
    for m in re.finditer(r"<node\b([^>]*)>", xml or ""):
        attrs = dict(re.findall(r'([\w-]+)="([^"]*)"', m.group(1)))
        b = re.match(r"\[(-?\d+),(-?\d+)\]\[(-?\d+),(-?\d+)\]", attrs.get("bounds", ""))
        if not b:
            continue
        x1, y1, x2, y2 = (int(v) for v in b.groups())
        nodes.append({"text": html.unescape(attrs.get("text", "")), "desc": html.unescape(attrs.get("content-desc", "")),
                      "id": attrs.get("resource-id", ""), "cls": attrs.get("class", ""), "pkg": attrs.get("package", ""),
                      "clickable": attrs.get("clickable") == "true", "enabled": attrs.get("enabled", "true") == "true",
                      "rect": [x1, y1, x2 - x1, y2 - y1]})
    return nodes


def _ad_close_label(node):
    """Label matching an ad close/skip button ("" if no match)."""
    for raw in (node["desc"], node["text"], node["id"].split("/")[-1]):
        val = (raw or "").strip()
        if not val:
            continue
        if val.lower() in AD_CLOSE_MARKS:
            return val
        norm = " " + " ".join(re.findall(r"[a-z0-9]+", strip_accents(val).lower())) + " "
        if "privacy" in norm or "info" in norm:
            continue
        if any((" %s " % w) in norm or norm.strip().startswith(w) for w in AD_CLOSE_WORDS):
            return val
    return ""


def pick_close_candidates(nodes):
    """Ad close/skip buttons, best first: clickable > small (X button) > higher up."""
    out = []
    for n in nodes:
        if not n["enabled"] or n["rect"][2] <= 0 or n["rect"][3] <= 0:
            continue
        label = _ad_close_label(n)
        if label:
            out.append(dict(n, label=label))
    out.sort(key=lambda n: (not n["clickable"], n["rect"][2] * n["rect"][3], n["rect"][1]))
    return out


def find_duplicate(rows, code, title, log_sig="", screen="", btype=""):
    """A bug already logged on this machine that duplicates the one about to be sent → (index row, reason); none → (None, "").
    Duplicate when: same log signature · title ≥ 75% similar · same screen + same type + title ≥ 40% similar."""
    code = (code or "").upper()
    for b in rows or []:
        if (b.get("code") or "").upper() != code:
            continue
        if log_sig and b.get("logSig") == log_sig:
            return b, "same log signature %s" % log_sig
        sim = similar(b.get("title", ""), title)
        if sim >= 0.75:
            return b, "title %d%% similar" % round(sim * 100)
        if screen and btype and b.get("screen") == screen and b.get("type") == btype and sim >= 0.4:
            return b, "same screen %s, same type %s, title %d%% similar" % (clip(screen, 60), btype, round(sim * 100))
    return None, ""


def format_value(prefix, value, limit=VALUE_PRINT_MAX, full=False):
    """Output line of `call`/`state`; too long → cut, and SAY it was cut (truncated JSON does not parse). "[cut " is a marker the skill doc quotes."""
    s = json.dumps(value, ensure_ascii=False)
    if full or len(s) <= limit:
        return "%s → %s" % (prefix, s)
    return "%s → %s… [cut %d/%d chars — add --full or --out <file>]" % (prefix, s[:limit], limit, len(s))


class ProbeError(QaError):
    def __init__(self, msg, status=0, body=None):
        QaError.__init__(self, msg)
        self.status = status
        self.body = body or {}


class Probe:
    """HTTP client for EZG QA Probe (runs inside the game). Every request carries the X-QA-Token header."""

    def __init__(self, base, token, timeout=15, reconnect=None):
        self.base = base.rstrip("/")
        self.token = token
        self.timeout = timeout
        self.reconnect = reconnect  # () -> bool: rebuilds the route (adb forward) when the connection is refused
        self._opener = urllib.request.build_opener(urllib.request.ProxyHandler({}))

    def request(self, method, endpoint, params=None, data=None, timeout=None, raw=False, _retry=True):
        url = self.base + endpoint
        if params:
            url += "?" + urllib.parse.urlencode(params, doseq=True)
        body = urllib.parse.urlencode(data, doseq=True).encode("utf-8") if data is not None else None
        req = urllib.request.Request(url, data=body, method=method, headers={"X-QA-Token": self.token})
        if body is not None:
            req.add_header("Content-Type", "application/x-www-form-urlencoded; charset=utf-8")
        try:
            with self._opener.open(req, timeout=timeout or self.timeout) as r:
                payload, status = r.read(), r.status
        except urllib.error.HTTPError as e:
            payload, status = e.read(), e.code
        except Exception as e:  # socket/timeout
            # an adb server restart (Unity/Android Studio build) drops every forward → rebuild it and try once more.
            if _retry and self.reconnect and "refused" in str(e).lower() and self.reconnect():
                return self.request(method, endpoint, params, data, timeout, raw, _retry=False)
            raise ProbeError("probe not responding (%s)" % e, 0)
        if raw and status < 400:
            return payload
        try:
            j = json.loads(payload.decode("utf-8"))
        except ValueError:
            j = {"raw": _dec(payload[:300])}
        if status >= 400:
            raise ProbeError("probe %s: %s %s" % (status, j.get("error", ""), j.get("message", "")), status, j)
        return j

    def get(self, endpoint, timeout=None, **params):
        return self.request("GET", endpoint, params=params or None, timeout=timeout)

    def post(self, endpoint, timeout=None, **data):
        return self.request("POST", endpoint, data=data, timeout=timeout)


# ───────────────────────────────────────────────────────────── devices

class Device(object):
    kind = "?"
    platform = "?"          # Unity RuntimePlatform: Android / IPhonePlayer
    native_input = True

    def __init__(self, ident):
        self.id = ident
        self.probe = None

    def describe(self):
        return self.id

    # Actions — subclasses override them; unsupported by default.
    def screenshot_png(self):
        raise NotSupported("this device cannot take screenshots by itself — needs the probe")

    def tap(self, x, y):
        raise NotSupported("this device has no native input — needs the probe")

    def swipe(self, x1, y1, x2, y2, ms):
        raise NotSupported("this device has no native input — needs the probe")

    def longpress(self, x, y, ms):
        self.swipe(x, y, x, y, ms)

    def drag(self, x1, y1, x2, y2, ms, hold_ms):
        self.swipe(x1, y1, x2, y2, ms + hold_ms)

    def text(self, s):
        raise NotSupported("text input is not supported on this device — use --probe")

    def key(self, name):
        raise NotSupported("key '%s' is not supported on %s" % (name, self.kind))

    def network(self, mode):
        raise NotSupported("toggling the network is not supported on %s" % self.kind)

    def rotate(self, mode):
        raise NotSupported("screen rotation is not supported on %s" % self.kind)

    def perf(self, pkg):
        return {}

    def background(self, pkg, secs):
        raise NotSupported("sending the app to background is not supported on %s" % self.kind)

    def clear_data(self, pkg):
        raise NotSupported("clearing app data is not supported on %s" % self.kind)

    def stop_like_player(self, pkg, settle):
        """Close like a player swiping it out of recents: go Home first (game gets OnApplicationPause → saves), then kill.
        Device without Home → NotSupported, the caller falls back to a hard kill."""
        self.key("home")
        time.sleep(max(0.5, settle))
        self.stop(pkg)

    def overlays(self, pkg=""):
        """Floating windows of other apps over the screen (only Android can detect them)."""
        return []


class Android(Device):
    kind = "android"
    platform = "Android"

    def __init__(self, serial, adb=None):
        Device.__init__(self, serial)
        self.adb_path = adb or find_adb()
        if not self.adb_path:
            raise QaError("adb not found (install Android SDK platform-tools or the Unity Android module)")

    def adb(self, *args, **kw):
        return run([self.adb_path, "-s", self.id] + list(args), **kw)

    def sh(self, cmd, timeout=30):
        return self.adb("shell", cmd, timeout=timeout)

    def getprop(self, name):
        return self.sh("getprop %s" % name, timeout=10)[1].strip()

    def describe(self):
        return "%s %s (Android %s)" % (self.getprop("ro.product.manufacturer"), self.getprop("ro.product.model"),
                                       self.getprop("ro.build.version.release"))

    def info(self):
        rc, out, _ = self.sh("cat /proc/meminfo | head -1; wm size; wm density; settings get secure android_id", timeout=10)
        mem = re.search(r"MemTotal:\s+(\d+)", out)
        size = re.findall(r"(?:Physical|Override) size:\s*(\d+)x(\d+)", out)
        dens = re.findall(r"(?:Physical|Override) density:\s*(\d+)", out)
        aid = out.strip().splitlines()[-1].strip() if out.strip() else ""
        return {"model": "%s %s" % (self.getprop("ro.product.manufacturer"), self.getprop("ro.product.model")),
                "os": "Android OS %s / API-%s" % (self.getprop("ro.build.version.release"), self.getprop("ro.build.version.sdk")),
                "ramMb": int(mem.group(1)) // 1024 if mem else 0,
                "display": [int(size[-1][0]), int(size[-1][1])] if size else None,
                "density": int(dens[-1]) if dens else 0,
                "deviceId": aid if re.match(r"^[0-9a-f]{8,}$", aid) else self.id,
                "emulator": self.id.startswith("emulator-") or self.getprop("ro.kernel.qemu") == "1"}

    def screenshot_png(self):
        rc, data, err = self.adb("exec-out", "screencap", "-p", timeout=25, binary=True)
        if rc == 0 and png_size(data):
            return data
        tmp = "/sdcard/qadevice_shot.png"
        self.sh("screencap -p %s" % tmp, timeout=25)
        local = os.path.join(tempfile.mkdtemp(prefix="qadev"), "s.png")
        self.adb("pull", tmp, local, timeout=25)
        self.sh("rm -f %s" % tmp)
        try:
            with open(local, "rb") as f:
                data = f.read()
        except OSError:
            data = b""
        if not png_size(data):
            raise QaError("screenshot failed (%s) — device locked / disconnected?" % (err or "screencap failed").strip())
        return data

    def tap(self, x, y):
        self._input("tap %d %d" % (x, y))

    def tap_many(self, x, y, times, interval):
        """Repeated taps in ONE adb command (as fast as a player mashing). At least 70 ms apart: Unity merges taps
        within one frame into one, so closer taps reach the game only once."""
        cmd = ("; sleep %.2f; " % max(interval, 0.07)).join("input tap %d %d" % (x, y) for _ in range(times))
        rc, out, err = self.sh(cmd, timeout=60)
        if rc != 0:
            raise QaError("adb input failed: %s" % (err or out).strip())

    def swipe(self, x1, y1, x2, y2, ms):
        self._input("swipe %d %d %d %d %d" % (x1, y1, x2, y2, ms))

    def drag(self, x1, y1, x2, y2, ms, hold_ms):
        if hold_ms <= 0:
            return self.swipe(x1, y1, x2, y2, ms)
        # Hold, then drag (drag-and-drop items, drawing paths): motionevent exists since Android 10-11.
        rc, out, err = self.sh("input motionevent DOWN %d %d" % (x1, y1))
        if rc != 0 or "Error" in out or "Unknown" in out:
            return self.swipe(x1, y1, x2, y2, ms + hold_ms)
        time.sleep(hold_ms / 1000.0)
        steps = max(4, ms // 40)
        script = ";".join("input motionevent MOVE %d %d" % (round(x1 + (x2 - x1) * k / float(steps)),
                                                          round(y1 + (y2 - y1) * k / float(steps)))
                          for k in range(1, steps + 1))
        self.sh(script, timeout=30)
        self.sh("input motionevent UP %d %d" % (x2, y2))

    def text(self, s):
        if any(ord(c) > 126 for c in s):
            raise NotSupported("adb `input text` only types ASCII — use `text --probe` (build with the probe) for Vietnamese/emoji")
        esc = s.replace("%", "\\%").replace(" ", "%s")
        esc = re.sub(r"([()<>|;&*\\~\"'`$])", r"\\\1", esc)
        self._input("text %s" % esc)

    KEYS = {"back": "KEYCODE_BACK", "home": "KEYCODE_HOME", "menu": "KEYCODE_MENU", "enter": "KEYCODE_ENTER",
            "app_switch": "KEYCODE_APP_SWITCH", "recent": "KEYCODE_APP_SWITCH", "power": "KEYCODE_POWER",
            "volume_up": "KEYCODE_VOLUME_UP", "volume_down": "KEYCODE_VOLUME_DOWN", "mute": "KEYCODE_VOLUME_MUTE",
            "wakeup": "KEYCODE_WAKEUP", "del": "KEYCODE_DEL", "tab": "KEYCODE_TAB", "escape": "KEYCODE_ESCAPE"}

    def key(self, name):
        code = self.KEYS.get(name.lower(), name if name.upper().startswith("KEYCODE_") else None)
        if not code:
            raise QaError("unknown key: %s (available: %s)" % (name, ", ".join(sorted(self.KEYS))))
        self._input("keyevent %s" % code)

    def _input(self, args):
        rc, out, err = self.sh("input %s" % args, timeout=30)
        if rc != 0:
            raise QaError("adb input failed: %s" % (err or out).strip())

    def wake(self):
        self.sh("input keyevent KEYCODE_WAKEUP; wm dismiss-keyguard", timeout=10)

    def installed(self, pkg):
        rc, out, _ = self.sh("pm path %s" % pkg, timeout=15)
        return "package:" in out

    def install(self, path):
        rc, out, err = self.adb("install", "-r", "-d", "-g", path, timeout=600)
        if rc != 0 and not (out + err).strip():  # the adb server was just restarted (e.g. Unity building Android) → retry
            time.sleep(2)
            self.adb("wait-for-device", timeout=60)
            rc, out, err = self.adb("install", "-r", "-d", "-g", path, timeout=600)
        if rc != 0 or "Success" not in out:
            raise QaError("APK install failed: %s" % (out + err).strip()[-400:])

    def app_version(self, pkg):
        rc, out, _ = self.sh("dumpsys package %s | grep -E 'versionName|versionCode' | head -2" % pkg, timeout=15)
        vn = re.search(r"versionName=(\S+)", out)
        vc = re.search(r"versionCode=(\d+)", out)
        return (vn.group(1) if vn else ""), (vc.group(1) if vc else "")

    def launcher(self, pkg):
        rc, out, _ = self.sh("cmd package resolve-activity --brief -c android.intent.category.LAUNCHER %s" % pkg, timeout=15)
        comps = [l.strip() for l in out.splitlines() if "/" in l and pkg in l]
        if comps:
            return comps[-1]
        rc, out, _ = self.sh("dumpsys package %s" % pkg, timeout=20)
        m = re.search(r"android\.intent\.action\.MAIN:\s*\n\s*\S+\s+(%s/\S+)" % re.escape(pkg), out)
        return m.group(1) if m else None

    def launch(self, pkg, token=None, port=None):
        comp = self.launcher(pkg)
        extra = ""
        if token:
            extra = " --es ezg_qa_probe %s --ei ezg_qa_port %d" % (token, port or PROBE_PORT)
        if comp:
            rc, out, err = self.sh("am start -W -n %s%s" % (comp, extra), timeout=90)
        else:
            rc, out, err = self.sh("monkey -p %s -c android.intent.category.LAUNCHER 1" % pkg, timeout=60)
        total = re.search(r"TotalTime:\s*(\d+)", out)
        if rc != 0 or "Error" in out:
            raise QaError("app launch failed: %s" % (out + err).strip()[-300:])
        return int(total.group(1)) if total else None

    def stop(self, pkg):
        self.sh("am force-stop %s" % pkg)

    def pidof(self, pkg):
        rc, out, _ = self.sh("pidof %s" % pkg, timeout=10)
        return [int(p) for p in out.split() if p.isdigit()]

    def foreground(self):
        rc, out, _ = self.sh("dumpsys window | grep -E 'mCurrentFocus|mFocusedApp'", timeout=15)
        m = re.search(r"mCurrentFocus=Window\{\S+ \S+ ([^/\s}]+)", out) or re.search(r"ActivityRecord\{\S+ \S+ ([^/\s}]+)", out)
        return m.group(1) if m else ""

    def overlays(self, pkg=""):
        """Bubbles / floating windows of THIRD-PARTY apps (recorders, chat heads, screen recorders…) — an adb tap in that area
        hits that app, not the game. Joins window types (dumpsys window) with the real touch regions (dumpsys input)."""
        rc1, wd, _ = self.sh("dumpsys window windows", timeout=30)
        rc2, idump, _ = self.sh("dumpsys input", timeout=30)
        return overlay_rects(wd, idump, pkg)

    def uia_dump(self):
        """Native view tree (uiautomator) — shows buttons of ad / system Activities that the Unity probe cannot see."""
        path = "/sdcard/ezg_qa_uia.xml"
        for _ in range(3):
            rc, out, _ = self.sh("uiautomator dump %s >/dev/null 2>&1; cat %s; rm -f %s" % (path, path, path), timeout=45)
            if "<hierarchy" in out:
                return out
            time.sleep(1.0)
        raise QaError("uiautomator dump could not get the view tree (screen animating continuously?) — look at the shot and tap by hand")

    def clear_data(self, pkg):
        rc, out, err = self.sh("pm clear %s" % pkg, timeout=60)
        if "Success" not in out:
            raise QaError("pm clear failed: %s" % (out + err).strip())

    def background(self, pkg, secs, token=None, port=None):
        """Send the app to background, then reopen it. token: sent with the new intent (Unity setIntent in onNewIntent) so the
        running probe switches to this session's token — the only way on Android 11+ (the app cannot read files adb writes)."""
        self.key("home")
        time.sleep(max(0.5, secs))
        comp = self.launcher(pkg)
        if comp:
            extra = (" --es ezg_qa_probe %s --ei ezg_qa_port %d" % (token, port or PROBE_PORT)) if token else ""
            self.sh("am start -n %s%s" % (comp, extra), timeout=30)
        else:
            self.sh("monkey -p %s -c android.intent.category.LAUNCHER 1" % pkg, timeout=30)

    def network(self, mode):
        mode = mode.lower()
        if mode in ("off", "airplane"):
            rc, out, err = self.sh("cmd connectivity airplane-mode enable", timeout=20)
            if rc != 0 or "Exception" in out + err:
                self.sh("svc wifi disable; svc data disable", timeout=20)
        elif mode == "on":
            self.sh("cmd connectivity airplane-mode disable", timeout=20)
            self.sh("svc wifi enable; svc data enable", timeout=20)
        elif mode == "wifi-off":
            self.sh("svc wifi disable", timeout=20)
        elif mode == "data-off":
            self.sh("svc data disable", timeout=20)
        time.sleep(1.5)
        rc, out, _ = self.sh("settings get global airplane_mode_on; dumpsys connectivity | grep -m1 'Active default network'", timeout=20)
        lines = out.strip().splitlines()
        plane = "ON" if lines and lines[0].strip() == "1" else "OFF"
        net = lines[1].split(":", 1)[-1].strip() if len(lines) > 1 else "?"
        return "airplane mode %s · default network: %s" % (plane, "none" if net in ("none", "") else net)

    def rotate(self, mode):
        mode = mode.lower()
        if mode == "auto":
            self.sh("settings put system accelerometer_rotation 1")
            return "auto"
        rot = {"portrait": 0, "landscape": 1, "reverse-portrait": 2, "reverse-landscape": 3}.get(mode)
        if rot is None:
            raise QaError("rotate: portrait|landscape|reverse-portrait|reverse-landscape|auto")
        self.sh("settings put system accelerometer_rotation 0; settings put system user_rotation %d" % rot)
        return mode

    def perf(self, pkg):
        res = {}
        pids = self.pidof(pkg)
        rc, out, _ = self.sh("dumpsys meminfo %s" % pkg, timeout=30)
        m = re.search(r"TOTAL PSS:\s+(\d+)", out) or re.search(r"^\s*TOTAL\s+(\d+)", out, re.M)
        if m:
            res["pss_mb"] = round(int(m.group(1)) / 1024.0, 1)
        if pids:
            rc, out, _ = self.sh("top -b -n 1 -p %d" % pids[0], timeout=15)
            lines = [l for l in out.splitlines() if l.strip()]
            hdr = next((l for l in lines if "PID" in l and "CPU" in l), None)
            row = next((l for l in lines if l.strip().startswith(str(pids[0]))), None)
            if hdr and row:
                cols = hdr.split()
                idx = next((k for k, c in enumerate(cols) if "CPU" in c), None)
                if idx is not None and cols[idx].startswith("S["):  # toybox merges the S and %CPU columns in the header
                    idx += 1
                vals = row.split()
                if idx is not None and idx < len(vals):
                    try:
                        res["cpu_pct"] = float(vals[idx].rstrip("%"))
                    except ValueError:
                        pass
        rc, out, _ = self.sh("dumpsys battery | grep -E 'temperature|level'", timeout=10)
        t = re.search(r"temperature:\s*(\d+)", out)
        if t:
            res["battery_c"] = int(t.group(1)) / 10.0
        lv = re.search(r"level:\s*(\d+)", out)
        if lv:
            res["battery_pct"] = int(lv.group(1))
        return res

    def write_trigger(self, pkg, content):
        d = "/sdcard/Android/data/%s/files" % pkg
        self.sh("mkdir -p %s && echo '%s' > %s/%s" % (d, content, d, PROBE_TRIGGER_FILE), timeout=15)

    def remove_trigger(self, pkg):
        self.sh("rm -f /sdcard/Android/data/%s/files/%s" % (pkg, PROBE_TRIGGER_FILE), timeout=15)

    def forward(self, device_port):
        rc, out, err = self.adb("forward", "tcp:0", "tcp:%d" % device_port, timeout=15)
        out = out.strip()
        return int(out) if out.isdigit() else None

    def reforward(self, local_port, device_port):
        """Rebuild the same forward (host port unchanged so the session's base URL stays valid)."""
        rc, _, _ = self.adb("forward", "tcp:%d" % local_port, "tcp:%d" % device_port, timeout=15)
        return rc == 0

    def probe_client(self, base, token, device_port):
        local = int(base.rsplit(":", 1)[-1])
        return Probe(base, token, reconnect=lambda: self.reforward(local, device_port))

    def unforward(self, local_port):
        if local_port:
            self.adb("forward", "--remove", "tcp:%d" % local_port, timeout=10)

    def unforward_probe(self, base_port=PROBE_PORT):
        """Remove every forward of this device to the probe port (including leftovers from earlier sessions)."""
        rc, out, _ = run([self.adb_path, "forward", "--list"], timeout=10)
        remotes = set("tcp:%d" % p for p in range(base_port, base_port + PROBE_PORT_SPAN))
        for line in out.splitlines():
            parts = line.split()
            if len(parts) == 3 and parts[0] == self.id and parts[2] in remotes:
                self.adb("forward", "--remove", parts[1], timeout=10)

    def log_cmd(self):
        return [self.adb_path, "-s", self.id, "logcat", "-v", "threadtime", "-T", "1"]

    def stay_awake(self, on, restore=None):
        rc, out, _ = self.sh("settings get global stay_on_while_plugged_in", timeout=10)
        prev = out.strip()
        if restore is not None:
            val = restore if restore.isdigit() else "0"
            self.sh("settings put global stay_on_while_plugged_in %s" % val, timeout=10)
        else:
            self.sh("svc power stayon %s" % ("true" if on else "false"), timeout=10)
        return prev

    # ── environment (env matrix / aspect) — each function returns the PREVIOUS value so close_session can revert it ──
    def _setting(self, ns, key, value=None):
        rc, out, _ = self.sh("settings get %s %s" % (ns, key), timeout=10)
        prev = out.strip()
        if value is not None:
            rc, out, err = self.sh("settings put %s %s %s" % (ns, key, value), timeout=10)
            if rc != 0 or "Exception" in out + err:
                raise NotSupported("settings put %s %s failed: %s" % (ns, key, (err or out).strip()[:120]))
        return prev if prev not in ("null", "") else None

    def font_scale(self, value):
        """value: number (1.0 = default, 1.3 = Large) or None to read. Returns the previous value."""
        return self._setting("system", "font_scale", None if value is None else ("%.2f" % float(value)).rstrip("0").rstrip("."))

    def dark_mode(self, on):
        rc, out, _ = self.sh("cmd uimode night", timeout=10)
        prev = "yes" if "yes" in out.lower() else "no"
        rc, out, err = self.sh("cmd uimode night %s" % ("yes" if on else "no"), timeout=15)
        if rc != 0 or "Error" in out + err or "unknown command" in (out + err).lower():
            raise NotSupported("device does not support `cmd uimode night` (Android < 10)")
        return prev

    def animator_scale(self, value):
        prev = self._setting("global", "animator_duration_scale")
        for key in ("animator_duration_scale", "transition_animation_scale", "window_animation_scale"):
            self._setting("global", key, str(value))
        return prev

    def always_finish(self, on):
        return self._setting("global", "always_finish_activities", "1" if on else "0")

    def trim_memory(self, pkg, level="RUNNING_CRITICAL"):
        rc, out, err = self.sh("am send-trim-memory %s %s" % (pkg, level), timeout=15)
        if rc != 0 or "Error" in out + err or "Unknown" in out + err:
            raise NotSupported("am send-trim-memory failed: %s" % (err or out).strip()[:120])
        return level

    def wm_size(self, spec):
        """spec 'WxH' | 'reset'. Returns the previous 'Override size' (None = not overridden)."""
        rc, out, _ = self.sh("wm size", timeout=10)
        m = re.search(r"Override size:\s*(\d+x\d+)", out)
        prev = m.group(1) if m else None
        rc, out, err = self.sh("wm size %s" % spec, timeout=15)
        if rc != 0 or "Error" in out + err or "Exception" in out + err:
            raise NotSupported("wm size %s failed: %s" % (spec, (err or out).strip()[:120]))
        return prev

    def wm_density(self, spec):
        rc, out, _ = self.sh("wm density", timeout=10)
        m = re.search(r"Override density:\s*(\d+)", out)
        prev = m.group(1) if m else None
        self.sh("wm density %s" % spec, timeout=15)
        return prev

    def set_clock(self, delta_s):
        """Shift the device clock (emulator/rooted device): turn auto_time off, then `date -s`. delta_s=None → turn auto_time back on."""
        if delta_s is None:
            self._setting("global", "auto_time", "1")
            self.sh("su 0 cmd alarm set-time $(( $(date +%s) * 1000 )) 2>/dev/null; true", timeout=10)
            return "auto"
        self._setting("global", "auto_time", "0")
        rc, out, _ = self.sh("date +%s", timeout=10)
        try:
            now = int(out.strip())
        except ValueError:
            raise NotSupported("could not read the device time")
        target = now + int(delta_s)
        for cmd in ("su 0 date -s @%d" % target, "date -s @%d" % target,
                    "su 0 date %s" % dt.datetime.fromtimestamp(target).strftime("%m%d%H%M%Y.%S"),
                    "date %s" % dt.datetime.fromtimestamp(target).strftime("%m%d%H%M%Y.%S")):
            rc, out, err = self.sh(cmd, timeout=10)
            if rc == 0 and "denied" not in (out + err).lower() and "not found" not in (out + err).lower() and "cannot" not in (out + err).lower():
                return "%+ds" % int(delta_s)
        raise NotSupported("could not change the device time (needs an emulator or a rooted device) — use the game's time-skip cheat instead")

    def set_locale(self, tag):
        """Change the system language: emulator/root (persist.sys.locale + framework restart ~20 s). Returns the previous locale."""
        prev = self.getprop("persist.sys.locale") or self.getprop("ro.product.locale") or ""
        rc, out, err = self.sh("su 0 setprop persist.sys.locale %s || setprop persist.sys.locale %s" % (tag, tag), timeout=10)
        if rc != 0 or "denied" in (out + err).lower():
            raise NotSupported("could not change the locale (needs emulator/root) — change it by hand in the device Settings")
        self.sh("su 0 sh -c 'stop; start' || (stop; start)", timeout=20)
        time.sleep(15)
        self.adb("wait-for-device", timeout=60)
        return prev

    def build_info_from_apk(self, pkg):
        """Read StreamingAssets/BugLogger/buglogger_build.json from the APK (commit/branch baked in at build time)."""
        rc, out, _ = self.sh("pm path %s" % pkg, timeout=15)
        paths = [l.split(":", 1)[1].strip() for l in out.splitlines() if l.startswith("package:")]
        base = next((p for p in paths if p.endswith("base.apk")), paths[0] if paths else None)
        if not base:
            return {}
        rc, out, _ = self.sh("unzip -p %s assets/BugLogger/buglogger_build.json 2>/dev/null" % base, timeout=20)
        try:
            return json.loads(out) if out.strip().startswith("{") else {}
        except ValueError:
            return {}


class IosSim(Device):
    kind = "ios-sim"
    platform = "IPhonePlayer"

    def __init__(self, udid, name=""):
        Device.__init__(self, udid)
        self.name = name
        self.idb = find_idb()
        self.native_input = bool(self.idb)
        self._density = None

    def simctl(self, *args, **kw):
        return run(["xcrun", "simctl"] + list(args), **kw)

    def describe(self):
        return "Simulator %s (%s)" % (self.name or self.id, self.id[:8])

    def info(self):
        rc, out, _ = self.simctl("list", "devices", "--json", timeout=20)
        model, osv = self.name, "iOS"
        try:
            for rt, devs in json.loads(out).get("devices", {}).items():
                for d in devs:
                    if d.get("udid") == self.id:
                        model = d.get("name", model)
                        m = re.search(r"iOS-(\d+)-(\d+)", rt)
                        if m:
                            osv = "iOS %s.%s" % m.groups()
        except ValueError:
            pass
        return {"model": "Simulator " + model, "os": osv, "ramMb": 0, "display": None, "density": 0,
                "deviceId": self.id, "emulator": True}

    def screenshot_png(self):
        tmp = os.path.join(tempfile.mkdtemp(prefix="qadev"), "s.png")
        rc, out, err = self.simctl("io", self.id, "screenshot", "--type=png", tmp, timeout=30)
        try:
            with open(tmp, "rb") as f:
                return f.read()
        except OSError:
            raise QaError("simctl screenshot failed: %s" % err.strip())

    def density(self):
        if self._density is None:
            self._density = 3.0
            if self.idb:
                rc, out, _ = run([self.idb, "describe", "--udid", self.id, "--json"], timeout=20)
                m = re.search(r'"density"\s*:\s*([\d.]+)', out)
                if m:
                    self._density = float(m.group(1))
        return self._density

    def _idb(self, *args):
        if not self.idb:
            raise NotSupported("Simulator needs `idb` for taps/swipes (brew install facebook/fb/idb-companion && pip3 install fb-idb) or a build with the probe")
        rc, out, err = run([self.idb] + list(args) + ["--udid", self.id], timeout=30)
        if rc != 0:
            raise QaError("idb failed: %s" % (err or out).strip())

    def tap(self, x, y):
        s = self.density()
        self._idb("ui", "tap", str(round(x / s)), str(round(y / s)))

    def swipe(self, x1, y1, x2, y2, ms):
        s = self.density()
        self._idb("ui", "swipe", str(round(x1 / s)), str(round(y1 / s)), str(round(x2 / s)), str(round(y2 / s)),
                  "--duration", "%.2f" % (ms / 1000.0))

    def longpress(self, x, y, ms):
        s = self.density()
        self._idb("ui", "tap", str(round(x / s)), str(round(y / s)), "--duration", "%.2f" % (ms / 1000.0))

    def text(self, s):
        self._idb("ui", "text", s)

    def key(self, name):
        name = name.lower()
        if name == "home":
            return self._idb("ui", "button", "HOME")
        if name == "lock":
            return self._idb("ui", "button", "LOCK")
        raise NotSupported("iOS has no '%s' key (no Back) — tap the in-game close/back button" % name)

    def installed(self, bundle):
        return self.simctl("get_app_container", self.id, bundle, timeout=20)[0] == 0

    def install(self, path):
        rc, out, err = self.simctl("install", self.id, path, timeout=600)
        if rc != 0:
            raise QaError("simctl install failed: %s" % err.strip())

    def _appinfo(self, bundle):
        return self.simctl("appinfo", self.id, bundle, timeout=20)[1]

    def app_version(self, bundle):
        out = self._appinfo(bundle)
        vn = re.search(r'CFBundleShortVersionString\s*=\s*"?([^";\n]+)', out)
        vc = re.search(r'CFBundleVersion\s*=\s*"?([^";\n]+)', out)
        return (vn.group(1).strip() if vn else ""), (vc.group(1).strip() if vc else "")

    def exe_name(self, bundle):
        m = re.search(r'CFBundleExecutable\s*=\s*"?([^";\n]+)', self._appinfo(bundle))
        return m.group(1).strip() if m else ""

    def launch(self, bundle, token=None, port=None):
        env = dict(os.environ)
        args = ["xcrun", "simctl", "launch", "--terminate-running-process", self.id, bundle]
        if token:
            env["SIMCTL_CHILD_EZG_QA_PROBE"] = token
            env["SIMCTL_CHILD_EZG_QA_PORT"] = str(port or PROBE_PORT)
            args += ["-ezgQaProbe", token, "-ezgQaPort", str(port or PROBE_PORT)]
        t0 = time.time()
        rc, out, err = run(args, timeout=90, env=env)
        if rc != 0:
            raise QaError("simctl launch failed: %s" % err.strip())
        return int((time.time() - t0) * 1000)

    def stop(self, bundle):
        self.simctl("terminate", self.id, bundle, timeout=20)

    def pidof(self, bundle):
        rc, out, _ = self.simctl("spawn", self.id, "launchctl", "list", timeout=20)
        pids = []
        for line in out.splitlines():
            if ("UIKitApplication:%s" % bundle) in line:
                p = line.split()[0]
                if p.isdigit():
                    pids.append(int(p))
        return pids

    def foreground(self):
        return ""

    def background(self, bundle, secs):
        self.simctl("launch", self.id, "com.apple.Preferences", timeout=30)
        time.sleep(max(0.5, secs))
        self.simctl("launch", self.id, bundle, timeout=30)

    def stop_like_player(self, bundle, settle):
        # No idb needed: opening Settings pushes the game to background (applicationWillResignActive → saves), then terminate.
        self.simctl("launch", self.id, "com.apple.Preferences", timeout=30)
        time.sleep(max(0.5, settle))
        self.stop(bundle)

    def data_dir(self, bundle):
        rc, out, _ = self.simctl("get_app_container", self.id, bundle, "data", timeout=20)
        return os.path.join(out.strip(), "Documents") if rc == 0 and out.strip() else None

    def write_trigger(self, bundle, content):
        d = self.data_dir(bundle)
        if d:
            write_text(os.path.join(d, PROBE_TRIGGER_FILE), content + "\n")

    def remove_trigger(self, bundle):
        d = self.data_dir(bundle)
        if d and os.path.isfile(os.path.join(d, PROBE_TRIGGER_FILE)):
            os.remove(os.path.join(d, PROBE_TRIGGER_FILE))

    def log_cmd(self, bundle):
        exe = self.exe_name(bundle)
        if not exe:
            return None
        return ["xcrun", "simctl", "spawn", self.id, "log", "stream", "--style", "compact", "--level", "debug",
                "--predicate", 'process == "%s"' % exe]

    # ── environment: the Simulator can only change dark/light appearance + text size (Dynamic Type) ──
    IOS_CONTENT_SIZE = {1.0: "medium", 1.15: "large", 1.3: "extra-large", 1.5: "extra-extra-large", 1.7: "extra-extra-extra-large",
                        2.0: "accessibility-medium"}

    def dark_mode(self, on):
        rc, out, _ = self.simctl("ui", self.id, "appearance", timeout=10)
        prev = out.strip() or "light"
        rc, out, err = self.simctl("ui", self.id, "appearance", "dark" if on else "light", timeout=10)
        if rc != 0:
            raise NotSupported("simctl ui appearance failed: %s" % (err or out).strip()[:120])
        return prev

    def font_scale(self, value):
        rc, out, _ = self.simctl("ui", self.id, "content_size", timeout=10)
        prev = out.strip() or "medium"
        if value is None:
            return prev
        v = float(value)
        name = min(self.IOS_CONTENT_SIZE.items(), key=lambda kv: abs(kv[0] - v))[1] if v != 1.0 else "medium"
        rc, out, err = self.simctl("ui", self.id, "content_size", name, timeout=10)
        if rc != 0:
            raise NotSupported("simctl ui content_size failed (needs Xcode ≥ 15): %s" % (err or out).strip()[:120])
        return prev


class IosDevice(Device):
    kind = "ios-device"
    platform = "IPhonePlayer"
    native_input = False

    def __init__(self, ident, name="", host=""):
        Device.__init__(self, ident)
        self.name = name
        self.host = host

    def devicectl(self, *args, **kw):
        tmp = os.path.join(tempfile.mkdtemp(prefix="qadev"), "o.json")
        rc, out, err = run(["xcrun", "devicectl"] + list(args) + ["--json-output", tmp], **kw)
        return rc, read_json(tmp, {}) or {}, (err or out)

    def describe(self):
        return "iPhone %s (%s)" % (self.name or self.id, self.id[:8])

    def info(self):
        return {"model": self.name or "iOS device", "os": "iOS", "ramMb": 0, "display": None, "density": 0,
                "deviceId": self.id, "emulator": False}

    def _apps(self, bundle):
        rc, j, err = self.devicectl("device", "info", "apps", "--device", self.id, "--bundle-id", bundle, timeout=60)
        return (j.get("result") or {}).get("apps") or []

    def installed(self, bundle):
        return bool(self._apps(bundle))

    def install(self, path):
        rc, j, err = self.devicectl("device", "install", "app", "--device", self.id, path, timeout=900)
        if rc != 0:
            raise QaError("devicectl install failed: %s" % err.strip()[-300:])

    def app_version(self, bundle):
        apps = self._apps(bundle)
        if apps:
            return apps[0].get("version", ""), apps[0].get("bundleVersion", "")
        return "", ""

    def launch_cmd(self, bundle, token=None, port=None, console=True):
        cmd = ["xcrun", "devicectl", "device", "process", "launch", "--device", self.id, "--terminate-existing"]
        if console:
            cmd.append("--console")
        if token:
            cmd += ["--environment-variables", json.dumps({"EZG_QA_PROBE": token, "EZG_QA_PORT": str(port or PROBE_PORT)})]
        cmd.append(bundle)
        if token:
            cmd += ["-ezgQaProbe", token, "-ezgQaPort", str(port or PROBE_PORT)]
        return cmd

    def launch(self, bundle, token=None, port=None):
        rc, out, err = run(self.launch_cmd(bundle, token, port, console=False), timeout=120)
        if rc != 0:
            raise QaError("devicectl launch failed: %s" % (err or out).strip()[-300:])
        return None

    def pidof(self, bundle):
        rc, j, err = self.devicectl("device", "info", "processes", "--device", self.id, timeout=60)
        procs = (j.get("result") or {}).get("runningProcesses") or []
        apps = self._apps(bundle)
        url = (apps[0].get("url") if apps else "") or ""
        return [p.get("processIdentifier") for p in procs
                if url and (p.get("executable") or "").startswith(url.rstrip("/"))]

    def stop(self, bundle):
        for pid in self.pidof(bundle):
            self.devicectl("device", "process", "terminate", "--device", self.id, "--pid", str(pid), timeout=60)

    def foreground(self):
        return ""


class UnityEditorTarget(Device):
    """Unity Editor in Play mode — everything goes through the probe (trigger file in the Editor's persistentDataPath)."""
    kind = "editor"
    platform = "Editor"
    native_input = False

    def __init__(self, ident="editor"):
        Device.__init__(self, ident)

    def describe(self):
        return "Unity Editor (Play mode)"

    def info(self):
        return {"model": "Unity Editor", "os": sys.platform, "ramMb": 0, "display": None, "density": 0,
                "deviceId": "editor", "emulator": True}

    def data_dir(self, _pkg=None):
        ps = project_settings(repo_root())
        company, product = ps.get("companyName") or "DefaultCompany", ps.get("productName") or "Game"
        if IS_MAC:
            return os.path.join(os.path.expanduser("~"), "Library", "Application Support", company, product)
        if IS_WIN:
            return os.path.join(os.path.expanduser("~"), "AppData", "LocalLow", company, product)
        return os.path.join(os.path.expanduser("~"), ".config", "unity3d", company, product)

    def write_trigger(self, pkg, content):
        write_text(os.path.join(self.data_dir(), PROBE_TRIGGER_FILE), content + "\n")

    def remove_trigger(self, pkg):
        path = os.path.join(self.data_dir(), PROBE_TRIGGER_FILE)
        if os.path.isfile(path):
            os.remove(path)

    def installed(self, pkg):
        return True

    def app_version(self, pkg):
        return project_settings(repo_root()).get("bundleVersion", ""), "editor"

    def launch(self, pkg, token=None, port=None):
        return None

    def stop(self, pkg):
        pass

    def pidof(self, pkg):
        return []

    def foreground(self):
        return ""


def list_android(adb):
    if not adb:
        return []
    rc, out, _ = run([adb, "devices", "-l"], timeout=20)
    devs = []
    for line in out.splitlines()[1:]:
        parts = line.split()
        if len(parts) < 2:
            continue
        props = dict(p.split(":", 1) for p in parts[2:] if ":" in p)
        devs.append({"id": parts[0], "kind": "android", "state": parts[1], "model": props.get("model", ""),
                     "emulator": parts[0].startswith("emulator-")})
    return devs


def avd_of(adb, serial):
    rc, out, _ = run([adb, "-s", serial, "emu", "avd", "name"], timeout=10)
    return out.strip().splitlines()[0].strip() if rc == 0 and out.strip() else ""


def list_avds(adb):
    emu = find_emulator(adb)
    if not emu:
        return []
    rc, out, _ = run([emu, "-list-avds"], timeout=20)
    return [l.strip() for l in out.splitlines() if l.strip() and not l.startswith("INFO")]


def list_sims():
    if not has_xcrun():
        return []
    rc, out, _ = run(["xcrun", "simctl", "list", "devices", "--json"], timeout=30)
    sims = []
    try:
        for rt, devs in json.loads(out).get("devices", {}).items():
            m = re.search(r"(iOS|iPadOS)-(\d+)-(\d+)", rt)
            if not m:
                continue
            for d in devs:
                if d.get("isAvailable", True):
                    sims.append({"id": d["udid"], "kind": "ios-sim", "state": d.get("state", ""), "model": d.get("name", ""),
                                 "os": "%s %s.%s" % m.groups()})
    except ValueError:
        pass
    return sims


def list_ios_devices():
    if not has_xcrun():
        return []
    tmp = os.path.join(tempfile.mkdtemp(prefix="qadev"), "d.json")
    run(["xcrun", "devicectl", "list", "devices", "--json-output", tmp], timeout=40)
    j = read_json(tmp, {}) or {}
    devs = []
    for d in (j.get("result") or {}).get("devices") or []:
        cp = d.get("connectionProperties") or {}
        dp = d.get("deviceProperties") or {}
        hp = d.get("hardwareProperties") or {}
        hosts = cp.get("potentialHostnames") or []
        devs.append({"id": d.get("identifier", ""), "udid": hp.get("udid", ""), "kind": "ios-device",
                     "state": cp.get("tunnelState", "") or d.get("visibilityClass", ""), "model": dp.get("name", ""),
                     "os": "iOS %s" % dp.get("osVersionNumber", ""), "host": cp.get("tunnelIPAddress") or (hosts[0] if hosts else ""),
                     "pairing": cp.get("pairingState", "")})
    return devs


def boot_avd(adb, name, headless):
    emu = find_emulator(adb)
    if not emu:
        raise QaError("Android SDK emulator binary not found, cannot boot AVD %s" % name)
    before = set(d["id"] for d in list_android(adb))
    cmd = [emu, "-avd", name, "-no-boot-anim", "-no-snapshot-save"]
    if headless:
        cmd += ["-no-window", "-no-audio"]
    spawn_background(cmd, os.path.join(state_root(), "emulator-%s.log" % slug(name)))
    t0 = time.time()
    serial = None
    while time.time() - t0 < 240:
        for d in list_android(adb):
            if d["id"] not in before and d["emulator"] and avd_of(adb, d["id"]) == name:
                serial = d["id"]
                break
        if serial:
            break
        time.sleep(2)
    if not serial:
        raise QaError("emulator %s did not come up after 240s" % name)
    while time.time() - t0 < 300:
        rc, out, _ = run([adb, "-s", serial, "shell", "getprop", "sys.boot_completed"], timeout=10)
        if out.strip() == "1":
            return serial
        time.sleep(2)
    raise QaError("emulator %s had not finished booting after 300s" % name)


def resolve_device(spec, headless=True):
    """`<device id>` → Device. Accepts: adb serial, AVD name (auto-boot), Simulator UDID/name (auto-boot), iPhone id/name, auto."""
    spec = (spec or "auto").strip()
    if spec.lower() in ("editor", "unity-editor"):
        return UnityEditorTarget()
    adb = find_adb()
    androids = list_android(adb)
    low = spec.lower()
    for d in androids:
        if d["id"] == spec:
            if d["state"] != "device":
                raise QaError("device %s is in state '%s' (unauthorized = tap Allow USB debugging on the device)" % (spec, d["state"]))
            return Android(spec, adb)
    if adb:
        name = spec[4:] if low.startswith("avd:") else spec
        for d in androids:
            if d["emulator"] and d["state"] == "device" and avd_of(adb, d["id"]) == name:
                return Android(d["id"], adb)
        if name in list_avds(adb):
            return Android(boot_avd(adb, name, headless), adb)
    sims = list_sims()
    for s in sims:
        if s["id"] == spec or s["model"].lower() == low:
            if s["state"] != "Booted":
                run(["xcrun", "simctl", "boot", s["id"]], timeout=120)
                run(["xcrun", "simctl", "bootstatus", s["id"], "-b"], timeout=300)
            return IosSim(s["id"], s["model"])
    for d in list_ios_devices():
        if spec in (d["id"], d["udid"]) or d["model"].lower() == low:
            return IosDevice(d["id"], d["model"], d["host"])
    if low in ("auto", "android", "ios", ""):
        cands = []
        if low in ("auto", "android", ""):
            cands += [("android", d) for d in androids if d["state"] == "device"]
        if low in ("auto", "ios", ""):
            cands += [("ios-sim", s) for s in sims if s["state"] == "Booted"]
            cands += [("ios-device", d) for d in list_ios_devices() if d["state"] == "connected"]
        if len(cands) == 1:
            k, d = cands[0]
            if k == "android":
                return Android(d["id"], adb)
            if k == "ios-sim":
                return IosSim(d["id"], d["model"])
            return IosDevice(d["id"], d["model"], d.get("host", ""))
        if not cands:
            avds = list_avds(adb) if adb and low in ("auto", "android", "") else []
            if len(avds) == 1:
                print("No device connected — auto-booting AVD %s" % avds[0])
                return Android(boot_avd(adb, avds[0], headless), adb)
            raise QaError("no device connected. Plug in an Android device (USB debugging on) / boot an emulator / Simulator, "
                          "or pass an AVD name to auto-boot it%s. `devices` lists them." % (
                              (" (available: %s)" % ", ".join(avds)) if avds else ""))
        raise QaError("%d devices found — specify the id: %s" % (len(cands), ", ".join(d["id"] for _, d in cands)))
    for d in androids:
        if d["model"].lower().replace("_", " ") == low.replace("_", " ") and d["state"] == "device":
            return Android(d["id"], adb)
    raise QaError("device '%s' not found. Run `devices` to see valid ids." % spec)


# ───────────────────────────────────────────────────────────── session

class Session(object):
    def __init__(self, path, data):
        self.dir = path
        self.d = data

    @property
    def id(self):
        return self.d["id"]

    def path(self, *parts):
        return os.path.join(self.dir, *parts)

    def save(self):
        write_json(self.path("session.json"), self.d)

    @staticmethod
    def load(sid=None):
        root = state_root()
        sid = sid or read_text(os.path.join(root, "current")).strip()
        if not sid:
            raise QaError("no session yet — run `start <device> --feature \"...\"` first")
        path = os.path.join(root, "sessions", sid)
        data = read_json(os.path.join(path, "session.json"))
        if not data:
            raise QaError("could not read session %s" % sid)
        return Session(path, data)

    @staticmethod
    def create(device, pkg, feature):
        root = state_root()
        sid = "%s-%s" % (local_stamp(), slug(device.id, 18))
        path = os.path.join(root, "sessions", sid)
        os.makedirs(os.path.join(path, "shots"), exist_ok=True)
        data = {"id": sid, "created": now_iso(), "feature": feature or "", "package": pkg,
                "device": {"id": device.id, "kind": device.kind, "platform": device.platform},
                "probe": {}, "view": {}, "log": {"offset": 0, "pid": 0}, "shot": 0, "pids": [],
                "expect_exit": 0, "changed": {}, "mark": 0, "ui": {}, "tool": VERSION}
        if isinstance(device, IosSim):
            data["device"]["name"] = device.name
        if isinstance(device, IosDevice):
            data["device"]["name"] = device.name
            data["device"]["host"] = device.host
        s = Session(path, data)
        s.save()
        write_text(os.path.join(root, "current"), sid)
        return s

    def device(self):
        dv = self.d["device"]
        if dv["kind"] == "android":
            dev = Android(dv["id"])
        elif dv["kind"] == "editor":
            dev = UnityEditorTarget()
        elif dv["kind"] == "ios-sim":
            dev = IosSim(dv["id"], dv.get("name", ""))
        else:
            dev = IosDevice(dv["id"], dv.get("name", ""), dv.get("host", ""))
        pr = self.d.get("probe") or {}
        if pr.get("ok") and pr.get("base"):
            dev.probe = dev.probe_client(pr["base"], pr["token"], pr.get("port") or PROBE_PORT) if isinstance(dev, Android) \
                else Probe(pr["base"], pr["token"])
        return dev

    def step(self, action, desc="", **params):
        steps = read_jsonl(self.path("steps.jsonl"))
        n = (steps[-1]["n"] + 1) if steps else 1
        rec = {"n": n, "t": now_iso(), "action": action, "desc": desc}
        rec.update({k: v for k, v in params.items() if v not in (None, "", [])})
        append_jsonl(self.path("steps.jsonl"), rec)
        return rec

    def steps_since_mark(self):
        steps = read_jsonl(self.path("steps.jsonl"))
        mark = self.d.get("mark", 0)
        return [s for s in steps if s["n"] > mark and s["action"] not in ("mark", "note", "shot", "check")]


def need_probe(dev, what):
    if not dev.probe:
        raise QaError("%s needs EZG QA Probe in the build (start reported 'probe: none'). The project needs package %s (`start` adds it "
                      "to Packages/manifest.json), then rebuild; until then test black-box with screenshots + coordinates." % (what, PROBE_PACKAGE))
    return dev.probe


# ───────────────────────────────────────────────────────────── background log

def log_start(sess, dev):
    lg = sess.d.setdefault("log", {})
    if lg.get("pid") and pid_alive(lg["pid"]):
        return
    path = sess.path("device.log")
    cmd = None
    if isinstance(dev, Android):
        cmd = dev.log_cmd()
    elif isinstance(dev, IosSim):
        cmd = dev.log_cmd(sess.d["package"])
    if cmd:
        lg["pid"] = spawn_background(cmd, path)
        lg["file"] = path
        lg.setdefault("offset", 0)
        sess.save()


def log_stop(sess):
    lg = sess.d.get("log") or {}
    kill_pid(lg.get("pid"))
    lg["pid"] = 0
    kill_pid((sess.d.get("console") or {}).get("pid"))
    sess.save()


def refresh_pids(sess, dev):
    try:
        cur = dev.pidof(sess.d["package"])
    except Exception:
        cur = []
    pids = list(sess.d.get("pids") or [])
    for p in cur:
        if p and p not in pids:
            pids.append(p)
    sess.d["pids"] = pids[-6:]
    return cur


def check_logs(sess, dev, defer=False):
    """Read the log since the last check → new events (signature not seen yet). Updates issues.json. Returns (new, repeat count).
    defer=True: keep new errors for the next print instead of returning them (used while waiting for the probe, so no warning is lost)."""
    pkg = sess.d["package"]
    if sess.d["device"]["kind"] in ("android", "ios-sim") and not pid_alive((sess.d.get("log") or {}).get("pid")):
        log_start(sess, dev)
    alive = refresh_pids(sess, dev)
    ignore = list((project_profile(repo_root()).get("autoTestDevice") or {}).get("ignoreLogPatterns") or [])
    ignore += sess.d.get("ignore", [])
    events = []
    lg = sess.d.setdefault("log", {})
    path = lg.get("file")
    if path and os.path.isfile(path):
        with open(path, "rb") as f:
            f.seek(lg.get("offset", 0))
            chunk = f.read()
        end = chunk.rfind(b"\n")
        if end >= 0:
            lines = _dec(chunk[: end + 1]).splitlines()
            lg["offset"] = lg.get("offset", 0) + end + 1
            if sess.d["device"]["kind"] == "android":
                evs, probe_lines = classify_android(group_logcat(lines), pkg, sess.d.get("pids"), ignore)
                for pl in probe_lines:
                    sess.d.setdefault("probe", {})["announced"] = pl
                events += evs
            else:
                events += classify_plain(lines, ignore)
    if dev.probe and sess.d["device"]["kind"] != "android":
        try:
            j = dev.probe.get("/logs", since=sess.d.get("probe_log_cursor", 0), timeout=8)
            sess.d["probe_log_cursor"] = j.get("cursor", 0)
            events += classify_probe_logs(j.get("entries", []), ignore)
        except QaError:
            pass
    # Skip app deaths caused by our own stop/restart.
    expected = any(e["kind"] == "expected-stop" for e in events)
    out = []
    for e in events:
        if e["kind"] == "expected-stop":
            continue
        if e["kind"] == "app-died" and (expected or sess.d.get("expect_exit", 0) > 0):
            sess.d["expect_exit"] = max(0, sess.d.get("expect_exit", 0) - 1)
            continue
        out.append(e)
    issues = read_json(sess.path("issues.json"), {}) or {}
    fresh, repeats = [], 0
    for e in out:
        it = issues.get(e["sig"])
        if it:
            it["count"] += 1
            it["last"] = now_iso()
            repeats += 1
            continue
        e2 = dict(e)
        e2.update({"count": 1, "firstSeen": now_iso(), "last": now_iso(), "step": _last_step_n(sess), "bug": None})
        issues[e["sig"]] = e2
        fresh.append(e2)
    if out:
        write_json(sess.path("issues.json"), issues)
    if not alive and sess.d.get("app_running") and sess.d.get("expect_exit", 0) <= 0 and sess.d["device"]["kind"] not in ("ios-device", "editor"):
        gone = {"kind": "app-gone", "severity": "critical", "first": "Tiến trình game không còn chạy (crash/thoát ngoài ý muốn?)",
                "lines": [], "sig": log_signature("app-gone", pkg), "count": 1, "firstSeen": now_iso(), "last": now_iso(),
                "step": _last_step_n(sess), "bug": None}
        if gone["sig"] not in issues:
            issues[gone["sig"]] = gone
            write_json(sess.path("issues.json"), issues)
            fresh.append(gone)
        sess.d["app_running"] = False
    elif alive:
        sess.d["app_running"] = True
    pending = sess.d.pop("pending_issues", [])
    if defer:
        sess.d["pending_issues"] = pending + [e["sig"] for e in fresh]
        sess.save()
        return [], repeats
    if pending:
        fresh = [issues[sg] for sg in pending if sg in issues] + fresh
    sess.save()
    return fresh, repeats


def _last_step_n(sess):
    steps = read_jsonl(sess.path("steps.jsonl"))
    return steps[-1]["n"] if steps else 0


def print_issues(fresh, repeats, verbose_lines=6):
    if not fresh and not repeats:
        print("log: no new errors")
        return
    if fresh:
        print("⚠ LOG: %d NEW errors" % len(fresh))
        for e in fresh:
            print("  [%s] %s sig=%s · %s" % (e["severity"], e["kind"], e["sig"], clip(e["first"], 220)))
            for l in e.get("lines", [])[1:verbose_lines]:
                print("      " + clip(l.strip(), 200))
    if repeats:
        print("log: +%d repeats of known errors (see `logs --all`)" % repeats)


# ───────────────────────────────────────────────────────────── capture + observe

@contextlib.contextmanager
def bug_logger_hidden(dev):
    """Hide the in-game Bug Logger UI (floating report button, toast — probe /capturehidden) around an adb/simctl capture, so the
    view and the BugHub screenshot do not show the button. No probe / game without Bug Logger / old probe → capture as usual."""
    pr = getattr(dev, "probe", None)
    hidden = False
    if pr is not None and getattr(pr, "capture_hidden", True):
        try:
            hidden = bool(pr.get("/capturehidden", timeout=5, sec=CAPTURE_HIDE_SECONDS).get("supported"))
            if not hidden:
                pr.capture_hidden = False  # the game has no recent-enough Bug Logger — stop asking on every capture
        except ProbeError as e:
            if e.status == 404:
                pr.capture_hidden = False  # probe < 1.2.1 has no such endpoint
    try:
        yield
    finally:
        if hidden:
            try:
                pr.get("/capturehidden", timeout=5, sec=0)
            except QaError:
                pass  # the probe shows the button again after CAPTURE_HIDE_SECONDS


def grab_png(sess, dev):
    try:
        with bug_logger_hidden(dev):
            return dev.screenshot_png()
    except NotSupported:
        pr = need_probe(dev, "Taking screenshots on this device")
        data = pr.request("GET", "/shot", params={"fmt": "png"}, timeout=30, raw=True)
        return data


def observe(sess, dev, label="", grid=False, marks=False, logs=True, compare=True, after_action=False):
    """Capture → save the original PNG + a JPEG view; diff with the previous shot; image audit; check new logs. Prints a summary for the agent."""
    data = grab_png(sess, dev)
    sess.d["shot"] = sess.d.get("shot", 0) + 1
    n = sess.d["shot"]
    base = "%03d%s" % (n, ("-" + slug(label, 28)) if label else "")
    full = sess.path("shots", base + ".png")
    os.makedirs(os.path.dirname(full), exist_ok=True)
    with open(full, "wb") as f:
        f.write(data)
    size = png_size(data) or image_size(full)
    view = sess.path("shots", base + ".view.jpg") if Image is not None else full
    (vw, vh), scale = make_view(full, view)
    prev = (sess.d.get("view") or {}).get("path")
    sess.d["view"] = {"path": view, "full": full, "w": vw, "h": vh, "shot_w": size[0], "shot_h": size[1],
                      "scale": scale, "n": n}
    shown = view
    if grid and Image is not None:
        shown = sess.path("shots", base + ".grid.jpg")
        draw_grid(view, shown)
    if marks:
        shown = draw_ui_marks(sess, dev, view, base) or shown
    sess.save()
    line = "shot #%03d %s  (%dx%d, 1 view px = %.2f device px)" % (n, shown, vw, vh, 1.0 / scale if scale else 1)
    if compare and prev:
        dr = diff_ratio(prev, view)
        if dr is not None:
            ch, box = dr
            line += "\n  Δ vs previous shot: %.1f%%" % (ch * 100) + (" region %s" % (box,) if box and ch > 0.001 else "")
            if ch < 0.002 and after_action:
                line += "  ← ALMOST UNCHANGED (no response to the action? screen hung?)"
    print(line)
    for w in analyze_image(view):
        print("  ⚠ SCREENSHOT: " + w)
    if logs:
        fresh, rep = check_logs(sess, dev)
        print_issues(fresh, rep)
        fg = ""
        try:
            fg = dev.foreground()
        except Exception:
            pass
        if fg and fg != sess.d["package"] and sess.d["device"]["kind"] == "android":
            print("  ⚠ foreground app: %s (not the game — ad/browser/system?)" % fg)
    return sess.d["view"]


def to_device_xy(sess, x, y, space="view"):
    """Agent-supplied coordinates (view image / % / device px) → device px."""
    v = sess.d.get("view") or {}
    sw, sh = v.get("shot_w"), v.get("shot_h")
    if not sw:
        raise QaError("no screenshot yet — take a `shot` first to get coordinates")

    def conv(val, total_shot, total_view):
        s = str(val).strip()
        if s.endswith("%"):
            return float(s[:-1]) / 100.0 * total_shot
        f = float(s)
        if space == "device":
            return f
        return f * total_shot / float(total_view)

    dx = conv(x, sw, v["w"])
    dy = conv(y, sh, v["h"])
    return int(round(min(max(dx, 0), sw - 1))), int(round(min(max(dy, 0), sh - 1)))


def device_to_view(sess, x, y):
    v = sess.d["view"]
    return int(round(x * v["w"] / float(v["shot_w"]))), int(round(y * v["h"] / float(v["shot_h"])))


def unity_scale(sess):
    """device px → Unity px (Screen) using the size the probe reports."""
    pr = sess.d.get("probe") or {}
    v = sess.d.get("view") or {}
    uw, uh = pr.get("screen_w"), pr.get("screen_h")
    if not (uw and uh and v.get("shot_w")):
        return 1.0, 1.0
    return uw / float(v["shot_w"]), uh / float(v["shot_h"])


# ───────────────────────────────────────────────────────────── UI via the probe

def probe_ui(sess, dev, include_all=False):
    pr = need_probe(dev, "Reading the UI tree")
    j = pr.get("/ui", all=1 if include_all else 0, timeout=20)
    sess.d.setdefault("probe", {}).update({"screen_w": j.get("screen", {}).get("w"), "screen_h": j.get("screen", {}).get("h")})
    sess.d["screens"] = {"scene": j.get("scene"), "stack": list(j.get("screens") or []), "at": now_iso()}
    sx, sy = unity_scale(sess)
    els = j.get("elements", [])
    for e in els:
        x, y, w, h = e.get("rect", [0, 0, 0, 0])
        # Unity px (top-left origin) → device px
        e["dev"] = [x / sx, y / sy, w / sx, h / sy]
    for o in j.get("world", []):
        x, y = o.get("pos", [0, 0])
        o["dev"] = [x / sx, y / sy]
    sess.d["ui"] = {"at": now_iso(), "elements": [{"id": e["id"], "path": e.get("path"), "dev": e["dev"],
                                                    "text": e.get("text", ""), "label": e.get("label", "")} for e in els]}
    sess.save()
    return j


def _view_rect(sess, dev_rect):
    x, y, w, h = dev_rect
    v = sess.d["view"]
    k = v["w"] / float(v["shot_w"])
    return [int(round(x * k)), int(round(y * k)), max(1, int(round(w * k))), max(1, int(round(h * k)))]


def draw_ui_marks(sess, dev, view, base, findings=None, ui=None):
    if Image is None or not dev.probe:
        return None
    j = ui
    if j is None:
        try:
            j = probe_ui(sess, dev)
        except QaError:
            return None
    flagged = set(f["id"] for f in (findings or []))
    marks = []
    for e in j.get("elements", []):
        if findings is not None and e["id"] not in flagged:
            continue
        if findings is None and (e.get("occluded") or (not e.get("interactable") and e.get("kind") not in ("ScrollRect", "Clickable"))):
            continue
        color = (220, 40, 40) if (e.get("blockedBy") or e["id"] in flagged) else (30, 170, 60)
        marks.append((e["id"], _view_rect(sess, e["dev"]), color))
    out = sess.path("shots", base + (".audit.jpg" if findings is not None else ".marks.jpg"))
    draw_marks(view, out, marks)
    return out


KEY_RE = re.compile(r"^[a-z][a-z0-9]*(?:_[a-z0-9]+){1,}$")
UKEY_RE = re.compile(r"^[A-Z][A-Z0-9]*(?:_[A-Z0-9]+){1,}$")
HASHKEY_RE = re.compile(r"^#[A-Za-z_][\w.]*$")  # localization key leaking as #shop_title
PLACEHOLDER_RE = re.compile(r"^(new text|text|button|label|textmeshpro|text \(tmp\)|lorem.*|placeholder|todo|tbd|xxx+|\?\?+|-|abc|test)$", re.I)
BAD_VALUE_RE = re.compile(r"\b(NaN|Infinity|-Infinity|null|undefined)\b|System\.\w+|UnityEngine\.\w+|\{\d\}|%[sd]\b|<color=[^>]*>[^<]*$")


def audit_text(text):
    """Heuristic for wrongly displayed text: leaked localization key, placeholder, NaN/null/{0}, .NET type names."""
    t = (text or "").strip()
    if not t:
        return None
    plain = re.sub(r"<[^>]+>", "", t).strip()
    if BAD_VALUE_RE.search(t):
        return ("high", "text contains a broken value / unreplaced format (%s)" % clip(plain, 60))
    if PLACEHOLDER_RE.match(plain):
        return ("medium", "placeholder text not replaced: \"%s\"" % clip(plain, 40))
    if len(plain) >= 5 and " " not in plain and (KEY_RE.match(plain) or UKEY_RE.match(plain) or HASHKEY_RE.match(plain)):
        return ("medium", "looks like a raw localization KEY instead of translated text: \"%s\"" % clip(plain, 50))
    return None


def _inter(a, b):
    x1, y1 = max(a[0], b[0]), max(a[1], b[1])
    x2, y2 = min(a[0] + a[2], b[0] + b[2]), min(a[1] + a[3], b[1] + b[3])
    return max(0, x2 - x1) * max(0, y2 - y1)


def _is_ancestor(p, q):
    return bool(p and q) and (q.startswith(p + "/") or p.startswith(q + "/"))


def noise_match(rule, finding, screens=()):
    """Does a qa.noise rule match the finding? flag by name (or '*'); match = regex on the path, or 'screen:<name>'."""
    flag = rule.get("flag") or "*"
    if flag not in ("*", finding.get("kind")):
        return False
    m = rule.get("match") or ""
    if not m:
        return True
    if m.startswith("screen:"):
        want = m[len("screen:"):].strip().lower()
        return any(want and want in (s or "").lower() for s in screens)
    try:
        return bool(re.search(m, finding.get("path") or "", re.I)) or bool(re.search(m, finding.get("text") or "", re.I))
    except re.error:
        return False


def apply_noise(findings, noise, screens=()):
    """Mark findings that match a noise rule: adds "suppressed": "<reason>" (kept in the file, not printed by default)."""
    for f in findings:
        for r in noise or []:
            if noise_match(r, f, screens):
                f["suppressed"] = r.get("reason") or r.get("match") or r.get("flag") or "noise"
                break
    return findings


def _flag_kind(flag):
    """Probe ≥1.3 flag (kebab-case or camelCase) → kebab-case finding name."""
    s = re.sub(r"(?<=[a-z0-9])(?=[A-Z])", "-", flag or "").lower()
    return re.sub(r"[^a-z0-9]+", "-", s).strip("-")


def audit_elements(ui, noise=None):
    """Visual audit on probe data → findings [{id, severity, kind, msg, path, text[, detail][, suppressed]}].
    noise: the project's qa.noise — matching findings get `suppressed` (still returned so the report can count them)."""
    els = ui.get("elements", [])
    scr = ui.get("screen", {})
    sw, sh = scr.get("w", 0), scr.get("h", 0)
    safe = scr.get("safe")  # [x, y, w, h] top-left origin
    dpi = scr.get("dpi") or 0
    out = []

    def add(e, sev, kind, msg, detail=None):
        f = {"id": e["id"], "severity": sev, "kind": kind, "msg": msg, "path": e.get("path", ""), "text": e.get("text", "")}
        if detail:
            f["detail"] = detail
        out.append(f)

    texts = []
    for e in els:
        flags = e.get("flags", [])
        r = e.get("rect", [0, 0, 0, 0])
        # New probe ≥1.3 flags (checks ported from com.ezg.autotest's UiAuditUtil): one finding per flag.
        details = e.get("flagDetail") or {}
        for fl in flags:
            if fl in PROBE_FLAG_ALIASES:
                continue
            kind, sev, msg = PROBE_FLAGS_13.get(fl) or (_flag_kind(fl), "medium", "probe reported flag %s" % fl)
            det = details.get(fl) or ""
            add(e, sev, kind, (msg + (" — " + det if det else "")), det or None)
        if "overflow" in flags or "truncated" in flags:
            add(e, "medium", "text-overflow", "text overflows / is cut off its box: \"%s\"" % clip(e.get("text", ""), 50))
        if "missingGlyph" in flags:
            add(e, "high", "missing-glyph", "font is missing glyphs (shown as boxes): %s" % e.get("missing", ""))
        if "noSprite" in flags:
            add(e, "medium", "missing-sprite", "Image has no sprite but shows a white box (missing art?)")
        if "missingMaterial" in flags:
            add(e, "high", "missing-material", "Graphic lost its material/shader")
        if "outsideContainer" in flags:
            add(e, "medium", "text-outside", "text \"%s\" is drawn outside its background/button '%s'" % (clip(e.get("text", ""), 40), e.get("container", "?")))
        a = audit_text(e.get("text"))
        if a:
            add(e, a[0], "text-content", a[1])
        if e.get("text") and r[2] > 0 and r[3] > 0:
            texts.append(e)
        if e.get("interactable"):
            if sw and sh:
                outside = r[2] * r[3] - _inter(r, [0, 0, sw, sh])
                if r[2] * r[3] and outside / float(r[2] * r[3]) > 0.15:
                    add(e, "medium", "offscreen", "button is partly off-screen")
                elif safe and safe[2] and safe[3] and (safe[2] < sw or safe[3] < sh):
                    out_safe = r[2] * r[3] - _inter(r, safe)
                    if r[2] * r[3] and out_safe / float(r[2] * r[3]) > 0.25:
                        add(e, "medium", "unsafe-area", "button intrudes into the notch/system bar area (outside the safe area)")
            big = sw and sh and r[2] * r[3] >= 0.5 * sw * sh  # background button / full-screen touch area: being covered by content is normal
            if e.get("blockedBy") and not e.get("occluded") and not big:
                add(e, "medium", "blocked", "button cannot be tapped — covered by %s (small/invisible overlay or same screen; confirm on the shot)" % e["blockedBy"])
            if dpi and min(r[2], r[3]) < 6.0 / 25.4 * dpi:
                add(e, "low", "tiny-target", "small touch target %dx%d px (<6mm) — hard to tap" % (r[2], r[3]))
    for i in range(len(texts)):
        for k in range(i + 1, len(texts)):
            a, b = texts[i], texts[k]
            # only compare texts on the same screen layer (a popup over the HUD is not text overlap), never parent/child
            if a.get("layer") != b.get("layer") or _is_ancestor(a.get("path"), b.get("path")):
                continue
            ra, rb = a.get("ink") or a["rect"], b.get("ink") or b["rect"]
            inter = _inter(ra, rb)
            small = min(ra[2] * ra[3], rb[2] * rb[3])
            if small and inter / float(small) > (0.15 if a.get("ink") and b.get("ink") else 0.3):
                add(a, "medium", "text-overlap", "text overlaps another text [%d] \"%s\"" % (b["id"], clip(b.get("text", ""), 30)))
    order = {s: i for i, s in enumerate(("critical", "high", "medium", "low", "info"))}
    out.sort(key=lambda f: order.get(f["severity"], 9))
    return apply_noise(out, noise, ui.get("screens") or []) if noise else out


def find_element(sess, dev, ref=None, text=None, name=None):
    j = probe_ui(sess, dev)
    els = j.get("elements", [])
    if ref is not None:
        old = next((e for e in (sess.d.get("ui_prev") or {}).get("elements", []) if e["id"] == ref), None)
        hit = next((e for e in els if old and e.get("path") == old.get("path")), None) or next((e for e in els if e["id"] == ref), None)
        if not hit:
            raise QaError("element @%d not found (UI changed?) — call `ui` again" % ref)
        return hit
    cands = []
    for e in sorted(els, key=lambda x: 1 if x.get("occluded") else 0):
        hay = [e.get("text", ""), e.get("label", "")]
        if text is not None:
            tl = text.lower()
            vals = [h.lower() for h in hay if h]
            if any(v == tl for v in vals):
                cands.append((0 if e.get("interactable") else 2, e))
            elif any(tl in v for v in vals):
                cands.append((1 if e.get("interactable") else 3, e))
        elif name is not None:
            nm = name.lower()
            p = (e.get("path") or "").lower()
            if (e.get("name") or "").lower() == nm or p.endswith("/" + nm):
                cands.append((0 if e.get("interactable") else 2, e))
            elif nm in p:
                cands.append((1 if e.get("interactable") else 3, e))
    if not cands:
        raise QaError("element %s not found on the current screen" % ("text=%r" % text if text is not None else "name=%r" % name))
    cands.sort(key=lambda c: c[0])
    return cands[0][1]


# ───────────────────────────────────────────────────────────── per-project QA profile (qa in project-profile.json)

_QA_CACHE = {}


def qa_profile(root=None, refresh=False):
    """Merged `qa` block (QA_DEFAULTS ← template ← project). Cached per process."""
    root = root or repo_root()
    if refresh or root not in _QA_CACHE:
        _QA_CACHE[root] = qaprofile.load_qa_profile(root)
    return _QA_CACHE[root]


def qa_path(root, key):
    return os.path.join(root, (qa_profile(root).get("paths") or {}).get(key) or QA_DEFAULTS["paths"][key])


def probe_supports(sess, feature):
    """Whether the probe in the build has `feature` (PROBE_FEATURES) — based on the saved /ping version."""
    v = (sess.d.get("probe") or {}).get("version") or ""
    need = PROBE_FEATURES.get(feature)
    return bool(need) and _is_semver(v) and _ver(v) >= _ver(need)


def unsupported_probe(sess, feature):
    v = (sess.d.get("probe") or {}).get("version") or "?"
    return "unsupported (probe v%s < %s — rebuild with %s ≥ %s to get `%s`)" % (v, PROBE_FEATURES.get(feature), PROBE_PACKAGE,
                                                                          PROBE_FEATURES.get(feature), feature)


def screens_key(j):
    return "%s|%s" % (j.get("scene") or "", ">".join(j.get("screens") or []))


def record_observed(sess, j, names=None):
    """Open screens (GameObject names from the probe) → recorded in sess.d["observed"] under the matching Feature name (or the raw name)."""
    obs = sess.d.setdefault("observed", {})
    if names is None:
        names = sess.d.get("feature_names")
        if names is None:
            names, _ = qaprofile.feature_names(repo_root(), qa_profile())
            sess.d["feature_names"] = names
    for s in j.get("screens") or []:
        key = qaprofile.match_feature(s, names or []) or re.sub(r"\(Clone\)$", "", s)
        obs.setdefault(key, qaprofile.today())
    return obs


def run_audit(sess, dev, label="audit", view=None, show_all=False, include_suppressed=False, quiet=False, tag=None):
    """Capture (if needed) + full /ui + audit_elements with the project's noise rules. Writes audit-<tag>.json. Returns (findings, j)."""
    v = view or observe(sess, dev, label, logs=True)
    j = probe_ui(sess, dev, include_all=True)
    record_observed(sess, j)
    findings = audit_elements(j, noise=qa_profile().get("noise"))
    shown = [f for f in findings if (show_all or f["severity"] != "info") and (include_suppressed or not f.get("suppressed"))]
    sup = sum(1 for f in findings if f.get("suppressed"))
    if not quiet:
        print("AUDIT screen %s: %d findings (%d info hidden — `--all` to show%s)" % (
            " > ".join(j.get("screens", [])) or j.get("scene"), len(shown), sum(1 for f in findings if f["severity"] == "info" and not f.get("suppressed")),
            (" · %d hidden by the project's noise rules — `--include-suppressed`" % sup) if sup and not include_suppressed else ""))
        for f in shown:
            print("  [%s] [%d] %s — %s (%s)%s" % (f["severity"], f["id"], f["kind"], f["msg"], clip(f["path"], 70),
                                               (" ⟂ noise: %s" % f["suppressed"]) if f.get("suppressed") else ""))
    if shown and Image is not None and not quiet:
        out = draw_ui_marks(sess, dev, v["path"], "%03d" % v["n"], findings=shown, ui=j)
        if out:
            print("annotated shot: %s" % out)
    write_json(sess.path("audit-%s.json" % (tag or "%03d" % v["n"])), {"screens": j.get("screens"), "scene": j.get("scene"),
                                                                     "shot": v["n"], "findings": findings})
    sess.save()
    return findings, j


def maybe_auto_audit(sess, dev, label=""):
    """After an action: screen changed (compares the /ui screen stack) → automatic audit, writes audit-auto-<n>.json + a step. No probe → skipped."""
    if not AUTO_AUDIT or not dev.probe or not (sess.d.get("probe") or {}).get("ok"):
        return None
    try:
        j = probe_ui(sess, dev, include_all=False)
    except QaError:
        return None
    key = screens_key(j)
    record_observed(sess, j)
    if key == sess.d.get("screens_key"):
        sess.save()
        return None
    sess.d["screens_key"] = key
    n = sess.d.get("auto_audit_n", 0) + 1
    sess.d["auto_audit_n"] = n
    try:
        findings, j2 = run_audit(sess, dev, view=sess.d.get("view"), quiet=True, tag="auto-%03d" % n)
    except QaError as e:
        sess.save()
        print("  auto-audit skipped: %s" % e)
        return None
    real = [f for f in findings if f["severity"] != "info" and not f.get("suppressed")]
    sup = sum(1 for f in findings if f.get("suppressed"))
    sess.step("auto-audit", "", screens=j2.get("screens"), findings=len(real), suppressed=sup or None, file="audit-auto-%03d.json" % n)
    worst = real[0] if real else None
    print("  ⚑ auto audit #%d: %d findings (%d suppressed) · screens %s%s · audit-auto-%03d.json" % (
        n, len(real), sup, " > ".join(j2.get("screens") or []) or j2.get("scene"),
        (" · worst [%s] %s" % (worst["severity"], worst["kind"])) if worst else "", n))
    return findings


# ───────────────────────────────────────────────────────────── BugHub

def bughub_endpoint(root):
    env = os.environ.get("BUGHUB_ENDPOINT", "").strip()
    if env:
        return env.rstrip("/")
    for s in find_buglogger_settings(root):
        if s["endpointOverride"].startswith("https://"):
            return s["endpointOverride"].rstrip("/")
    wait = read_text(os.path.join(root, ".claude", "scripts", "bughub-wait.sh"))
    m = re.search(r'^DEFAULT_ENDPOINT="?([^"\n]+)"?', wait, re.M)
    return (m.group(1) if m else DEFAULT_ENDPOINT).rstrip("/")


def http_json(url, timeout=20):
    opener = urllib.request.build_opener()
    try:
        with opener.open(urllib.request.Request(url, headers={"User-Agent": "qadevice/" + VERSION}), timeout=timeout) as r:
            return r.status, json.loads(r.read().decode("utf-8"))
    except urllib.error.HTTPError as e:
        try:
            return e.code, json.loads(e.read().decode("utf-8"))
        except ValueError:
            return e.code, {}
    except Exception as e:
        return 0, {"error": str(e)}


def bughub_status(root, code=None):
    prof = project_profile(root)
    pcode = ((prof.get("bugHub") or {}).get("projectCode") or "").strip()
    assets = find_buglogger_settings(root)
    acodes = sorted(set(a["projectCode"] for a in assets if a["projectCode"]))
    code = (code or pcode or (acodes[0] if len(acodes) == 1 else "")).strip()
    endpoint = bughub_endpoint(root)
    st = {"code": code, "profileCode": pcode, "assets": assets, "package": buglogger_package_present(root),
          "endpoint": endpoint, "problems": [], "form": None}
    if not code:
        st["problems"].append("no BugHub project code (bugHub.projectCode in .claude/project-profile.json)")
    elif not re.match(r"^[A-Za-z0-9_-]+$", code):
        st["problems"].append("invalid project code: %s" % code)
    if not st["package"]:
        st["problems"].append("project has no com.ezg.buglogger package")
    if not assets:
        st["problems"].append("Resources/BugLoggerSettings.asset not found")
    elif code and not any(a["projectCode"].lower() == code.lower() for a in assets):
        st["problems"].append("BugLoggerSettings.asset has code %s ≠ %s" % (",".join(acodes) or "(empty)", code))
    if code and re.match(r"^[A-Za-z0-9_-]+$", code):
        status, form = http_json("%s/v1/projects/%s/form" % (endpoint, code))
        st["http"] = status
        if status == 200:
            st["form"] = form
            if not form.get("intakeEnabled", False):
                st["problems"].append("server: intake of %s is OFF (ask a BugHub admin to enable it)" % code)
            write_json(os.path.join(state_root(), "bughub-form-%s.json" % code.upper()), form)
        elif status == 404:
            st["problems"].append("server: project %s is not registered / intake is off (404)" % code)
        else:
            st["problems"].append("BugHub server not responding (%s %s)" % (status, form.get("error", "")))
    st["ok"] = not st["problems"]
    return st


def pick_category(form, btype):
    cats = (form or {}).get("categories") or ["Visual", "Logic", "GD"]
    want = {"visual": "visual", "ux": "visual", "gd": "gd"}.get(btype, "logic")
    for c in cats:
        if c.lower() == want:
            return c
    return cats[0] if cats else ""


def pick_tags(form, btype, severity, names=()):
    tags = (form or {}).get("tags") or []
    wanted = list(names)
    wanted.append({"visual": "Visual", "ux": "Improvement", "perf": "Performance"}.get(btype, "Logic"))
    wanted.append({"critical": "High", "high": "High", "medium": "Medium", "low": "Low"}.get(severity, ""))
    ids = []
    for w in wanted:
        for t in tags:
            if w and t.get("name", "").lower() == w.lower() and t["id"] not in ids:
                ids.append(t["id"])
    return ids[: ((form or {}).get("limits") or {}).get("maxTags", 4)]


def _name_key(s):
    return " ".join(strip_accents(s or "").lower().split())


def pick_people(form, names):
    """Discord ids of the people named in --mention: exact name (accents / case ignored), Discord id, or a part
    of a name that matches exactly ONE person by whole words. A part shared by several people ("Anh", a family
    name) is an error listing them — never a ping to all of them."""
    people = (form or {}).get("people") or []
    ids = []
    for n in names:
        want = _name_key(n)
        if not want:
            continue
        hits = [p for p in people if p.get("id") == n.strip() or _name_key(p.get("name")) == want]
        if not hits:
            words = want.split()
            hits = [p for p in people if all(w in _name_key(p.get("name")).split() for w in words)]
        if not hits:
            raise QaError("--mention \"%s\": no such person in BugHub (people: %s)" % (
                n, ", ".join(p.get("name", "?") for p in people) or "none"))
        if len(hits) > 1:
            raise QaError("--mention \"%s\" matches %d people (%s) — give the full name of the one person who must act" % (
                n, len(hits), ", ".join(p.get("name", "?") for p in hits)))
        if hits[0]["id"] not in ids:
            ids.append(hits[0]["id"])
    return ids[:5]


TITLE_SHORT_MAX = 70  # = limits.titleMax of the BugHub form: the Discord thread name is <= 100 characters
LEADING_TAGS_RE = re.compile(r"^(?:\s*\[[^\]\n]{0,40}\]\s*[-–—:|·]?\s*)+")
TITLE_FEATURE_RE = re.compile(r"^([^:\n]{1,40}?)\s*:\s+\S")


def tidy_title(title, limit=TITLE_SHORT_MAX):
    """One line, no leading [tags] (version / platform / map are attached by the tool), at most `limit` chars —
    a longer title is an error: the agent rewrites it instead of having it cut."""
    t = " ".join((title or "").split())
    t = LEADING_TAGS_RE.sub("", t).strip() or t
    if len(t) > limit:
        raise QaError("title has %d characters > %d — write '<Feature>: <what is wrong>' in Vietnamese "
                      "(e.g. 'Shop: bấm nhanh gói cash trừ gem 2 lần'); numbers / details go in --actual" % (len(t), limit))
    return t


def bug_feature(explicit, title, session_feature, screen):
    """featureKey of a bug: --feature, else the 'Feature:' prefix of the title, else the session's feature without
    its '(...)' part (a charter-wide description is not a feature)."""
    if explicit:
        return explicit.strip()
    m = TITLE_FEATURE_RE.match(title or "")
    if m and len(m.group(1).split()) <= 4:
        return m.group(1).strip()
    f = re.sub(r"\s*[(\[][^)\]]*[)\]]", "", session_feature or "").strip()
    return f or screen


def norm_title(t):
    t = strip_accents(t or "").lower()
    return " ".join(re.findall(r"[a-z0-9]+", t))


def similar(a, b):
    ta, tb = set(norm_title(a).split()), set(norm_title(b).split())
    if not ta or not tb:
        return 0.0
    return len(ta & tb) / float(len(ta | tb))


def format_steps(steps, precondition=""):
    lines = []
    if precondition:
        lines.append("Điều kiện: %s" % precondition)
    k = 0
    for s in steps:
        desc = s.get("desc") or default_step_desc(s)
        if not desc:
            continue
        k += 1
        lines.append("%d. %s" % (k, desc))
    return "\n".join(lines)


def default_step_desc(s):
    a = s.get("action")
    if a == "tap":
        return "Chạm %s" % (("\"%s\"" % s["el"]) if s.get("el") else "toạ độ (%s,%s)" % tuple(s.get("xy", ("?", "?"))))
    if a == "swipe":
        return "Vuốt từ (%s,%s) tới (%s,%s)" % tuple(s.get("xy", ("?",) * 4))
    if a == "longpress":
        return "Giữ tay tại (%s,%s)" % tuple(s.get("xy", ("?", "?")))
    if a == "drag":
        return "Kéo từ (%s,%s) tới (%s,%s)" % tuple(s.get("xy", ("?",) * 4))
    if a == "text":
        return "Nhập \"%s\"" % s.get("text", "")
    if a == "key":
        return "Bấm phím %s" % s.get("key", "")
    if a == "wait":
        return "Chờ %ss" % s.get("secs", "")
    if a == "launch":
        return "Mở game"
    if a == "restart":
        return "Tắt hẳn game rồi mở lại"
    if a == "background":
        return "Đưa game xuống nền %ss rồi mở lại" % s.get("secs", "")
    if a == "network":
        return "Mạng: %s" % s.get("mode", "")
    if a == "rotate":
        return "Xoay màn: %s" % s.get("mode", "")
    if a == "call":
        return "(cheat) %s" % s.get("method", "")
    return ""


def build_report(fields, app, device, player_id="", extra=None):
    """report.json schema 1 — identical to BugReportBuilder.WriteReport of com.ezg.buglogger (BugHub contract §3.4)."""
    def t(v, n):
        v = (v or "").strip()
        return v if len(v) <= n else v[:n]

    ex = {}
    for k, v in (extra or {}).items():
        if len(ex) >= EXTRA_MAX:
            break
        if k and v not in (None, ""):
            ex[str(k)[:64]] = str(v)[:256]
    return {
        "schema": 1,
        "id": str(uuid.uuid4()),
        "kind": "bug",
        "title": t(fields.get("title"), TITLE_MAX),
        "featureKey": t(fields.get("featureKey"), 200),
        "category": t(fields.get("category"), 200),
        "reproduceRate": t(fields.get("reproduceRate"), 16),
        "actual": t(fields.get("actual"), FIELD_MAX),
        "expected": t(fields.get("expected"), FIELD_MAX),
        "steps": t(fields.get("steps"), FIELD_MAX),
        "description": t(fields.get("description"), FIELD_MAX),
        "tagIds": list(fields.get("tagIds") or [])[:4],
        "mentionIds": list(fields.get("mentionIds") or [])[:5],
        "createdAtUtc": now_iso(),
        "app": {k: t(app.get(k), 200) for k in ("version", "build", "commitSha", "branch", "platform", "unityVersion")},
        "device": {"id": t(device.get("id"), 200), "model": t(device.get("model"), 200), "os": t(device.get("os"), 200),
                   "ramMb": int(device.get("ramMb") or 0), "screen": t(device.get("screen"), 32)},
        "player": {"id": t(player_id, 200)},
        "extra": ex,
    }


def multipart(parts):
    boundary = "----qadevice" + uuid.uuid4().hex
    buf = io.BytesIO()
    for name, filename, ctype, data in parts:
        buf.write(("--%s\r\n" % boundary).encode())
        buf.write(('Content-Disposition: form-data; name="%s"; filename="%s"\r\n' % (name, filename)).encode())
        buf.write(("Content-Type: %s\r\n\r\n" % ctype).encode())
        buf.write(data)
        buf.write(b"\r\n")
    buf.write(("--%s--\r\n" % boundary).encode())
    return boundary, buf.getvalue()


class _NoRedirect(urllib.request.HTTPRedirectHandler):
    def redirect_request(self, *a, **k):
        return None


def post_report(endpoint, code, report, shot=None, log=None, save=None, timeout=30):
    parts = [("report", "report.json", "application/json", json.dumps(report, ensure_ascii=False).encode("utf-8"))]
    if shot:
        parts.append(("screenshot", "screenshot.jpg", "image/jpeg", shot))
    if log:
        parts.append(("log", "log.txt", "text/plain", log))
    if save:
        parts.append(("save", "save.json", "application/json", save))
    boundary, body = multipart(parts)
    req = urllib.request.Request(endpoint.rstrip("/") + "/v1/reports", data=body, method="POST", headers={
        "Content-Type": "multipart/form-data; boundary=%s" % boundary, "X-BugHub-Project": code,
        "User-Agent": "qadevice/" + VERSION})
    opener = urllib.request.build_opener(_NoRedirect())
    try:
        with opener.open(req, timeout=timeout) as r:
            status, payload = r.status, r.read()
    except urllib.error.HTTPError as e:
        status, payload = e.code, e.read()
    except Exception as e:
        return 0, {"error": "network: %s" % e}
    try:
        j = json.loads(payload.decode("utf-8")) if payload else {}
    except ValueError:
        j = {"raw": _dec(payload[:300])}
    return status, j


def collect_log_excerpt(sess, max_bytes=MAX_LOG_BYTES, sig=None, probe_entries=None):
    lines = []
    issues = read_json(sess.path("issues.json"), {}) or {}
    if sig and sig in issues:
        lines.append("=== Lỗi log liên quan (sig %s, x%d) ===" % (sig, issues[sig]["count"]))
        lines += issues[sig].get("lines", [])
        lines.append("")
    path = (sess.d.get("log") or {}).get("file")
    if path and os.path.isfile(path):
        pids = set(str(p) for p in sess.d.get("pids") or [])
        pkg = sess.d["package"]
        with open(path, "rb") as f:
            f.seek(0, 2)
            size = f.tell()
            f.seek(max(0, size - 6 * 1024 * 1024))
            tail = _dec(f.read()).splitlines()
        keep = []
        for l in tail:
            m = LOGCAT_RE.match(l)
            if m:
                if (m.group(2) in pids) or pkg in l or m.group(5) in ("AndroidRuntime", "DEBUG", "ActivityManager") and pkg in l:
                    keep.append(l)
            elif sess.d["device"]["kind"] != "android":
                keep.append(l)
        lines.append("=== Log của app (cuối phiên) ===")
        lines += keep[-1500:]
    if probe_entries:
        lines.append("=== Log Unity (EZG QA Probe) ===")
        for e in probe_entries[-400:]:
            lines.append("[%s] %s: %s" % (e.get("t", ""), e.get("type", ""), e.get("msg", "")))
            if e.get("stack"):
                lines += ["    " + l for l in e["stack"].splitlines()[:20]]
    text = "\n".join(lines).encode("utf-8")
    if len(text) > max_bytes:
        text = text[-max_bytes:]
        nl = text.find(b"\n")
        text = text[nl + 1:] if 0 <= nl < 4096 else text
    return text


# ───────────────────────────────────────────────────────────── commands

def cmd_devices(a):
    adb = find_adb()
    rows = []
    for d in list_android(adb):
        name = avd_of(adb, d["id"]) if d["emulator"] and d["state"] == "device" else ""
        rows.append((d["id"], "android", d["state"], d["model"] + (" (AVD %s)" % name if name else "")))
    running_avds = set(r[3].split("AVD ")[-1].rstrip(")") for r in rows if "AVD" in r[3])
    for n in list_avds(adb):
        if n not in running_avds:
            rows.append((n, "android-avd", "off (will auto-boot)", ""))
    sims = list_sims()
    booted = [s for s in sims if s["state"] == "Booted"]
    for s in booted + [s for s in sims if s["state"] != "Booted" and "iPhone" in s["model"]][:6]:
        rows.append((s["id"], "ios-sim", s["state"], "%s · %s" % (s["model"], s["os"])))
    for d in list_ios_devices():
        rows.append((d["id"], "ios-device", d["state"], "%s · %s" % (d["model"], d["os"])))
    if a.json:
        print(json.dumps([dict(zip(("id", "kind", "state", "model"), r)) for r in rows], ensure_ascii=False, indent=1))
        return
    if not rows:
        print("No devices/AVDs/Simulators.")
    for r in rows:
        print("%-40s %-11s %-18s %s" % r)


def cmd_bughub_check(a):
    st = bughub_status(repo_root(), a.code)
    if a.json:
        print(json.dumps(st, ensure_ascii=False, indent=1))
    else:
        print("BugHub: %s · code %s · endpoint %s" % ("OK" if st["ok"] else "NOT READY", st["code"] or "?", st["endpoint"]))
        for p in st["problems"]:
            print("  ✗ " + p)
        if st.get("form"):
            f = st["form"]
            print("  categories: %s" % ", ".join(f.get("categories", [])))
            print("  tags: %s" % ", ".join(t["name"] for t in f.get("tags", [])))
            print("  people: %s" % ", ".join(p["name"] for p in f.get("people", [])))
        if not st["ok"]:
            print("→ run the /bughub-setup skill now, in this session (ask the dev for the Discord forum ID/project code only if the project is not registered), then retry.")
    if not st["ok"]:
        sys.exit(4)


def find_artifact(root, platform):
    pats = ["*.apk"] if platform == "Android" else ["*.app", "*.ipa"]
    best = None
    skip = {"Library", "Temp", "Logs", "Packages", ".git", "Assets", "obj", "UserSettings", "node_modules"}
    for dirpath, dirnames, filenames in os.walk(root):
        dirnames[:] = [d for d in dirnames if d not in skip and not d.startswith(".") and not d.endswith(".app")]
        rel = os.path.relpath(dirpath, root)
        if rel.count(os.sep) > 4:
            dirnames[:] = []
        for pat in pats:
            for fn in list(filenames) + list(dirnames):
                if fnmatch.fnmatch(fn, pat):
                    p = os.path.join(dirpath, fn)
                    mt = os.path.getmtime(p)
                    if not best or mt > best[0]:
                        best = (mt, p)
    return best[1] if best else None


def ensure_probe_in_project(root):
    """Project lacks the probe package / has an older one than the skill needs → add/bump the manifest line; remove the old copy in Assets.
    Returns status lines (empty when nothing changed). The note texts are quoted by the skill doc — keep them verbatim."""
    notes = []
    info = find_probe_in_project(root)
    ver = info["package"]
    if ver is None or (_is_semver(ver) and _ver(ver) < _ver(PROBE_MIN_VERSION)):
        set_manifest_dependency(root, PROBE_PACKAGE, PROBE_MIN_VERSION)
        notes.append("%s %s %s in Packages/manifest.json — commit the manifest (+ the packages-lock.json Unity generates); with a new build "
                     "the next run can test in depth" % ("bumped" if ver else "added", PROBE_PACKAGE, PROBE_MIN_VERSION))
        if not info["registry"]:
            notes.append("⚠ manifest has no EZG scoped registry (scope com.ezg) — Unity cannot download %s; install the registry via "
                         "Feature Hub" % PROBE_PACKAGE)
    if info["legacy"]:
        notes.extend(remove_legacy_probe(root, info["legacy"]))
    return notes


def auto_clear_script():
    """Script of the /auto-clear skill next to this skill (macOS/Linux: .sh, Windows: .ps1); otherwise the copy that the hook
    on this machine runs (~/.claude/auto-clear/). None = the machine/project has no /auto-clear."""
    name = "auto-clear.ps1" if IS_WIN else "auto-clear.sh"
    for p in (os.path.join(os.path.dirname(SKILL_DIR), "auto-clear", "scripts", name),
              os.path.join(os.path.expanduser("~"), ".claude", "auto-clear", name)):
        if os.path.isfile(p):
            return p
    return None


def auto_clear_cmd(script, *args):
    if script.endswith(".ps1"):
        return ["powershell", "-NoProfile", "-ExecutionPolicy", "Bypass", "-File", script] + list(args)
    return ["bash", script] + list(args)


def parse_retry_result(rc, out, err):
    """Output of `auto-clear retry-arm|retry-off` → (state, result line).
    state: armed · disarmed · not_armed · not_installed · unsupported · error."""
    for line in (out or "").splitlines():
        s = line.strip()
        for key, state in (("RETRY_ARMED", "armed"), ("RETRY_DISARMED", "disarmed"), ("RETRY_NOT_ARMED", "not_armed"),
                           ("NOT_INSTALLED", "not_installed"), ("UNSUPPORTED", "unsupported"), ("INVALID_", "error")):
            if s.startswith(key):
                return state, s
    tail = [l.strip() for l in ((err or "") + "\n" + (out or "")).splitlines() if l.strip()]
    return "error", tail[-1] if tail else "auto-clear exited with code %s" % rc


def api_retry(root, action):
    """Arm (`arm`) / disarm (`off`) the API-ERROR RETRY for the Claude Code pane running the skill: when a turn dies on an API error
    (org disabled Claude access, rate limit, overloaded, 5xx, lost session…) the model is no longer running to retry — the
    /auto-clear StopFailure hook waits 30 s, then types API_RETRY_PROMPT into that exact pane, unlimited times, until the
    API answers or `stop`. Hook not registered on the machine → auto `install` (idempotent, backs up settings, the hook does
    nothing while the pane flag is off), then arm again. Returns (state, line); state is `missing` when there is no /auto-clear script."""
    script = auto_clear_script()
    if not script:
        return "missing", "project has no /auto-clear skill (install it from Feature Hub)"
    if action != "arm":
        return parse_retry_result(*run(auto_clear_cmd(script, "retry-off"), timeout=30, cwd=root))
    arm = auto_clear_cmd(script, "retry-arm", "--max", "0", "--prompt", API_RETRY_PROMPT)
    st, line = parse_retry_result(*run(arm, timeout=30, cwd=root))
    if st == "not_installed":
        rc, out, err = run(auto_clear_cmd(script, "install"), timeout=60, cwd=root)
        st, line = parse_retry_result(*run(arm, timeout=30, cwd=root))
        if st == "armed":
            line = "auto-installed the /auto-clear hook on this machine · " + line  # describe_api_retry matches "auto-installed"
        elif rc != 0:
            line = "`auto-clear install` failed: %s" % ((err or out).strip() or "code %s" % rc)
    return st, line


def describe_api_retry(ar):
    """One line for the `start` output + report. The skill doc matches "API-error retry: ON …" / "OFF — …" — keep the words ON/OFF verbatim."""
    if not ar:
        return "off (--no-api-retry)"
    st, line = ar.get("state"), ar.get("line", "")
    if st == "armed":
        return ("ON — a turn killed by an API error (org disabled Claude access, rate limit, overloaded, 5xx…) is auto-resumed "
                "after %ds, no limit; `stop` disarms it%s" % (RETRY_EVERY_S, " (hook auto-installed on this machine)" if "auto-installed" in line else ""))
    if st == "disarmed":
        return "disarmed (session stopped)"
    if st == "not_installed":
        return "OFF — the StopFailure hook could not be installed on this machine (%s) → the dev runs `/auto-clear install`" % line
    if st == "unsupported":
        return "OFF — %s (works only in iTerm2 on macOS / a Windows console) — API errors must be resumed by hand" % line
    return "OFF — %s" % line


def cmd_start(a):
    root = repo_root()
    # BugHub is mandatory — check it before touching the device.
    bh = bughub_status(root, a.code)
    if not bh["ok"] and not a.skip_bughub:
        print("BugHub: NOT READY · code %s · endpoint %s" % (bh["code"] or "?", bh["endpoint"]))
        for pr_ in bh["problems"]:
            print("  ✗ " + pr_)
        print("→ run the /bughub-setup skill NOW in this session (Skill tool; ask the dev for the Discord forum ID/project code only if the project is not registered), "
              "then call `start` again.")
        sys.exit(4)
    notes = [n for n in (ensure_pillow(),) if n]
    # Project QA profile: no qa block → auto-detect conventions (profile init) so ui/state/cheat/coverage use the right ones.
    qa_line = ""
    try:
        qa0, prov0 = qaprofile.load_qa_profile(root, with_provenance=True)
        if prov0.get("_block") == "missing" and not a.no_profile_init:
            _, det0, _ = profile_init(root)
            qa0 = qa_profile(root, refresh=True)
            notes.append("no QA profile → ran `profile init` (status draft): detected conventions written to %s + QaProfile.md — the dev reviews them, then sets `reviewed`" % qaprofile.PROFILE_REL)
        lv0 = qa0.get("liveness") or {}
        qa_line = "status %s · enum %s · %d screens observed · %d noise rules%s" % (
            qa0.get("status"), (qa0.get("conventions") or {}).get("featuresEnum"), len(lv0.get("observed") or {}), len(qa0.get("noise") or []),
            " · doc: %s" % os.path.relpath(qa_path(root, "profileDoc"), root) if os.path.isfile(qa_path(root, "profileDoc")) else " · NO QaProfile.md")
    except Exception as e:  # a broken profile must not block the test session
        qa_line = "error reading the QA profile: %s" % e
    if a.probe != "off" and not a.no_probe_install:
        try:
            notes.extend(ensure_probe_in_project(root))
        except (OSError, QaError) as e:
            notes.append("could not add the probe package to the project: %s" % e)
    close_previous_session()
    dev = resolve_device(a.device, headless=not a.window)
    ps = project_settings(root)
    pkg = a.package or ps["ids"].get("Android" if dev.platform == "Android" else "iPhone") or ps["ids"].get("Standalone")
    if not pkg:
        raise QaError("could not read the bundle id from ProjectSettings — pass --package")
    if isinstance(dev, Android):
        dev.wake()
    if a.install:
        dev.install(a.install)
    if not dev.installed(pkg):
        art = find_artifact(root, dev.platform)
        if art and not a.no_install:
            print("App %s is not installed on the device — installing %s" % (pkg, art))
            dev.install(art)
        if not dev.installed(pkg):
            raise QaError("app %s is not installed on %s and no build was found in the repo — install it by hand or pass --install <apk/app>" % (pkg, dev.id))
    sess = Session.create(dev, pkg, a.feature)
    info = dev.info()
    vn, vc = dev.app_version(pkg)
    sess.d["device"].update({"model": info["model"], "os": info["os"], "ramMb": info["ramMb"], "deviceId": info["deviceId"],
                             "emulator": info["emulator"], "density": info.get("density", 0)})
    sess.d["app"] = {"version": vn, "build": vc, "platform": dev.platform, "unityVersion": ps.get("unityVersion", ""),
                     "productName": ps.get("productName", "")}
    if isinstance(dev, Android):
        bi = dev.build_info_from_apk(pkg)
        if bi:
            sess.d["app"].update({"commitSha": bi.get("commitSha", ""), "branch": bi.get("branch", ""),
                                  "builtAtUtc": bi.get("builtAtUtc", "")})
        sess.d["changed"]["stayon"] = dev.stay_awake(True)
    sess.d["bughub"] = {"ok": bh["ok"], "code": bh["code"], "endpoint": bh["endpoint"], "problems": bh["problems"]}
    if not a.no_api_retry:
        st, line = api_retry(root, "arm")
        sess.d["api_retry"] = {"state": st, "line": line}
    sess.save()
    log_start(sess, dev)
    if a.fresh:
        dev.stop(pkg)
        dev.clear_data(pkg)
        sess.step("fresh", "Xoá dữ liệu game (người chơi mới)")
    token = "".join(random.choice(string.ascii_letters + string.digits) for _ in range(24)) if a.probe != "off" else None
    port = a.port
    if token:
        sess.d["probe"] = {"token": token, "port": port, "ok": False}
        if isinstance(dev, Android):
            dev.unforward_probe(port)
            dev.remove_trigger(pkg)  # Android uses the intent; a file written by old versions only causes an "Access denied" warning
        else:
            try:
                dev.write_trigger(pkg, "%s:%d" % (token, port))
            except Exception:
                pass
    running = bool(dev.pidof(pkg))
    launch_ms = None
    if a.attach and running:
        try:
            if isinstance(dev, Android):
                dev.background(pkg, 1, token, port)  # the new intent carries this session's token
            elif hasattr(dev, "background"):
                dev.background(pkg, 1)  # reloads the trigger file when the app regains focus
        except NotSupported:
            pass
        sess.step("attach", "Tiếp tục phiên chơi đang mở của game")
    else:
        if running:
            sess.d["expect_exit"] = sess.d.get("expect_exit", 0) + 1
            dev.stop(pkg)
            time.sleep(1.0)
        if isinstance(dev, IosDevice):
            cmd = dev.launch_cmd(pkg, token, port, console=True)
            cpid = spawn_background(cmd, sess.path("device.log"))
            time.sleep(4)
            if not pid_alive(cpid):  # old Xcode has no --console → launch normally, logs only via the probe
                dev.launch(pkg, token, port)
                cpid = 0
            sess.d["console"] = {"pid": cpid}
            sess.d["log"] = {"file": sess.path("device.log"), "offset": 0, "pid": 0}
        else:
            launch_ms = dev.launch(pkg, token, port)
        sess.step("launch", "Mở game (khởi động nguội)")
    sess.d["launch_ms"] = launch_ms
    sess.d["app_running"] = True
    sess.save()
    probe_msg = "off (--probe off)"
    if token:
        probe_msg = connect_probe(sess, dev, wait=a.probe_wait if a.probe == "auto" else max(a.probe_wait, 60))
    if isinstance(dev, UnityEditorTarget) and not (sess.d.get("probe") or {}).get("ok"):
        print("SESSION %s (Editor) — probe not connected yet: enter Play mode (Unity MCP unity_play_mode), then run `probe-connect`" % sess.id)
        print("API-error retry: %s" % describe_api_retry(sess.d.get("api_retry")))
        return
    # wait for the game to render, then capture
    time.sleep(a.settle)
    print("SESSION %s" % sess.id)
    print("device: %s · %s · %s" % (dev.id, info["model"], info["os"]))
    print("app: %s v%s (%s)%s%s" % (pkg, vn or "?", vc or "?",
                                  (" · commit %s@%s" % (sess.d["app"].get("commitSha", "")[:9], sess.d["app"].get("branch", ""))) if sess.d["app"].get("commitSha") not in (None, "", "unknown") else "",
                                  (" · cold start %d ms" % launch_ms) if launch_ms else ""))
    print("probe: %s" % probe_msg)
    if a.probe == "require" and not (sess.d.get("probe") or {}).get("ok"):
        raise QaError("--probe require but the probe could not be connected (see the probe line above)")
    print("bughub: %s" % ("OK (%s)" % bh["code"] if bh["ok"] else "NOT READY — " + "; ".join(bh["problems"])))
    print("QA profile: %s" % qa_line)
    print("API-error retry: %s" % describe_api_retry(sess.d.get("api_retry")))
    for n in notes:
        print("note: " + n)
    print("session folder: %s" % sess.dir)
    hint = (sess.d.get("probe") or {}).get("next")
    if hint:
        print("→ " + hint)
    observe(sess, dev, "start")
    for o in get_overlays(sess, dev, refresh=True):
        print("⚠ other-app overlay: %s — the tool blocks adb taps in this area (they would hit that app). Game button underneath → "
              "--inject; ad button underneath → turn off/move the overlay on the device before testing" % overlay_line(sess, o))


def wait_cheat_gate(cli, j, wait):
    """Build with com.ezg.cheat-gate: wait until the probe has asked CheatGate (game Initialize at splash + 1 request), then return a fresh /ping."""
    t0 = time.time()
    while j.get("gate") != "open" and (j.get("cheatGate") or {}).get("present") and \
            (j.get("cheatGate") or {}).get("state") in CHEAT_PENDING and time.time() - t0 < wait:
        time.sleep(1.0)
        try:
            j = cli.get("/ping", timeout=5)
        except ProbeError:
            break
    return j


def describe_probe(j, code=None, model=""):
    """/ping → (one-line status, next step or ""). The status texts are markers quoted by the skill doc — keep them verbatim."""
    cg = j.get("cheatGate") or {}
    state, dev_id, proj = cg.get("state", ""), cg.get("deviceId", ""), cg.get("project") or code or "<project code>"
    cheat = j.get("cheat")
    cheat_txt = "" if cheat is None else (" · game cheats %s" % ("ON" if cheat else "off"))
    if j.get("gate") == "open":
        if cg.get("present") and state == "unlocked":
            return "gate open — cheat-gate: device %s is in test list %s%s" % (dev_id, proj, cheat_txt), ""
        return "gate open (%s)%s" % (j.get("gateReason", ""), cheat_txt), ""
    if cg.get("present"):
        if state == "refused":
            return ("GATE CLOSED — device %s is not in the test list of %s" % (dev_id, proj),
                    "add the device: ezg MCP test_device_add(project=\"%s\", device_id=\"%s\", label=\"<device holder> - %s\") "
                    "then `probe-connect --unlock` (the server picks it up after ~1-2 min, the command waits)" % (proj, dev_id, model or "test device"))
        if state == "offline":
            return "GATE CLOSED — device has no network, CheatGate needs the network on first launch", "turn the device network on, then `probe-connect --unlock`"
        if state == "not_configured":
            return ("GATE CLOSED — game has not called CheatGate.Initialize(project code) / empty code",
                    "the dev puts the BugHub code into GameConstant.CheatGateProject (or wherever the game calls CheatGate.Initialize), then rebuilds — this session tests black-box")
        if state == "off":
            return "GATE CLOSED — autoCheat=false in EzgQaProbeConfig", "tap the version 10 times on the device (device in the list), then `probe-connect`"
        return "GATE CLOSED — cheat-gate not finished yet (%s)" % (state or "?"), "`probe-connect --unlock` to wait longer"
    return ("GATE CLOSED (%s)" % j.get("gateReason", ""),
            "turn cheats on the way the game allows (e.g. tap the version in Settings 10 times, or add the device to the game's cheat list), then `probe-connect`")


def connect_probe(sess, dev, wait=25, cheat_wait=40):
    pr = sess.d["probe"]
    token, port = pr["token"], pr["port"]
    t0 = time.time()
    tried_ports = [port + k for k in range(PROBE_PORT_SPAN)]
    last_err = ""
    while time.time() - t0 < wait:
        # the log line "[EzgQaProbe] listening host:port gate=…" gives the real port (Android)
        check_logs(sess, dev, defer=True)
        ann = (sess.d.get("probe") or {}).get("announced")
        ports = [ann["port"]] if ann else tried_ports
        for p in ports:
            base = None
            if isinstance(dev, Android):
                local = dev.forward(p)
                if not local:
                    continue
                base = "http://127.0.0.1:%d" % local
            elif isinstance(dev, (IosSim, UnityEditorTarget)):
                base = "http://127.0.0.1:%d" % p
            else:
                host = dev.host
                if not host:
                    return "none (iPhone network address unknown — needs a CoreDevice/USB connection)"
                base = "http://%s:%d" % ("[%s]" % host if ":" in host else host, p)
            try:
                cli = Probe(base, token, timeout=4)
                j = cli.get("/ping", timeout=4)
                j = wait_cheat_gate(cli, j, cheat_wait)
                pr.update({"ok": True, "base": base, "port": p, "version": j.get("version"), "gate": j.get("gate"),
                           "gateReason": j.get("gateReason", ""), "cheatGate": j.get("cheatGate") or {}, "cheat": j.get("cheat"),
                           "screen_w": j.get("screen", {}).get("w"), "screen_h": j.get("screen", {}).get("h"),
                           "bugLogger": j.get("bugLogger"), "local": base.rsplit(":", 1)[-1]})
                status, hint = describe_probe(j, sess.d.get("bughub", {}).get("code"), sess.d["device"].get("model", ""))
                pr["next"] = hint
                sess.save()
                dev.probe = dev.probe_client(base, token, p) if isinstance(dev, Android) else Probe(base, token)
                old = " · old probe in the build (v%s < %s): rebuild for all features" % (j.get("version"), PROBE_MIN_VERSION) \
                    if _is_semver(j.get("version")) and _ver(j.get("version")) < _ver(PROBE_MIN_VERSION) else ""
                return "OK v%s @ %s · Unity %sx%s · %s%s" % (j.get("version"), base, pr["screen_w"], pr["screen_h"], status, old)
            except ProbeError as e:
                last_err = str(e)
                if isinstance(dev, Android):
                    dev.unforward(int(base.rsplit(":", 1)[-1]))
        if not ann and time.time() - t0 > min(wait, 12) and not _probe_in_log(sess):
            break
        time.sleep(1.5)
    sess.d["probe"]["ok"] = False
    sess.save()
    return "none (build has no EZG QA Probe or the app is not open) — black-box test: screenshots + coordinates%s" % (
        (" · last error: %s" % last_err) if last_err and "not responding" not in last_err else "")


def cmd_probe_connect(a):
    sess = Session.load(a.session)
    dev = sess.device()
    pr = sess.d.get("probe") or {}
    if not pr.get("token"):
        raise QaError("session was opened with --probe off")
    if isinstance(dev, Android) and pr.get("local"):
        dev.unforward(int(pr["local"]))
    if a.retrigger:
        if isinstance(dev, Android):
            dev.background(sess.d["package"], 1, pr["token"], pr.get("port", PROBE_PORT))
        elif hasattr(dev, "write_trigger"):
            dev.write_trigger(sess.d["package"], "%s:%d" % (pr["token"], pr.get("port", PROBE_PORT)))
    print("probe: %s" % connect_probe(sess, dev, wait=a.wait))
    pr = sess.d.get("probe") or {}
    if a.unlock and pr.get("ok") and pr.get("gate") != "open":
        print("probe: %s" % unlock_probe(sess, dev, a.unlock_wait))
        pr = sess.d.get("probe") or {}
    if pr.get("next") and pr.get("gate") != "open":
        print("→ " + pr["next"])
    if pr.get("ok") and not sess.d.get("view"):
        observe(sess, dev, "probe")


def unlock_probe(sess, dev, wait):
    """Re-ask CheatGate (e.g. right after test_device_add) and wait for the server to accept — at most `wait` seconds, re-asking every 30 s."""
    pr = sess.d["probe"]
    cli = dev.probe
    t0, last, j = time.time(), 0.0, {}
    while time.time() - t0 < wait:
        try:
            if time.time() - last >= CHEAT_UNLOCK_RETRY_S:
                last = time.time()
                cli.post("/unlock", timeout=10)
            j = wait_cheat_gate(cli, cli.get("/ping", timeout=5), 20)
        except ProbeError as e:
            return "could not re-ask: %s" % e
        st = (j.get("cheatGate") or {}).get("state")
        if j.get("gate") == "open" or not (j.get("cheatGate") or {}).get("present") or st in ("not_configured", "off"):
            break
        time.sleep(3)
    status, hint = describe_probe(j, sess.d.get("bughub", {}).get("code"), sess.d["device"].get("model", ""))
    pr.update({"gate": j.get("gate"), "gateReason": j.get("gateReason", ""), "cheatGate": j.get("cheatGate") or {},
               "cheat": j.get("cheat"), "next": hint})
    sess.save()
    return status


def _probe_in_log(sess):
    return bool((sess.d.get("probe") or {}).get("announced"))


def act_point(a, sess, dev):
    """Device-px coordinates for tap/longpress: @id, --text, --name, --world or x y."""
    el = None
    if a.target and a.target[0].startswith("@"):
        sess.d["ui_prev"] = sess.d.get("ui")
        el = find_element(sess, dev, ref=int(a.target[0][1:]))
    elif getattr(a, "text", None) is not None:
        el = find_element(sess, dev, text=a.text)
    elif getattr(a, "name", None) is not None:
        el = find_element(sess, dev, name=a.name)
    elif getattr(a, "world", None) is not None:
        pr = need_probe(dev, "Tapping a 3D object by name")
        j = pr.get("/find", q=a.world, timeout=15)
        objs = [o for o in j.get("world", []) if o.get("onScreen")]
        if not objs:
            raise QaError("object '%s' not on screen (find returned %d)" % (a.world, len(j.get("world", []))))
        sx, sy = unity_scale(sess)
        o = objs[0]
        return int(o["pos"][0] / sx), int(o["pos"][1] / sy), o.get("path", a.world)
    if el:
        if el.get("occluded") or el.get("blockedBy"):
            print("⚠ element is covered by %s — the tap will hit the layer above (close the popup/overlay first to tap it)" % (
                "another screen/popup" if el.get("occluded") else clip(el["blockedBy"], 80)))
        x, y, w, h = el["dev"]
        cx, cy = el.get("tap", [None, None])
        if cx is not None:
            sx, sy = unity_scale(sess)
            return int(cx / sx), int(cy / sy), el.get("label") or el.get("text") or el.get("path")
        return int(x + w / 2), int(y + h / 2), el.get("label") or el.get("text") or el.get("path")
    if not a.target or len(a.target) < 2:
        raise QaError("need X Y coordinates (on the latest view image, or 50%% 80%%) or @id / --text / --name / --world")
    x, y = to_device_xy(sess, a.target[0], a.target[1], a.space)
    return x, y, ""


def get_overlays(sess, dev, refresh=False):
    """Other apps' overlays (cached in the session, re-scanned at most once per OVERLAY_CACHE_SECONDS)."""
    if not isinstance(dev, Android):
        return []
    cache = sess.d.get("overlays") or {}
    if refresh or time.time() - cache.get("t", 0) > OVERLAY_CACHE_SECONDS:
        try:
            items = dev.overlays(sess.d.get("package", ""))
        except QaError:
            items = cache.get("items", [])
        cache = {"t": time.time(), "items": items}
        sess.d["overlays"] = cache
        sess.save()
    return cache.get("items", [])


def overlay_line(sess, o):  # "covers view" is part of a marker quoted by the skill doc — keep it verbatim
    x, y, w, h = o["rect"]
    if not (sess.d.get("view") or {}).get("shot_w"):
        return "%s (%s) covers device px [%d,%d %dx%d]" % (o["package"], o["type"], x, y, w, h)
    vx, vy = device_to_view(sess, x, y)
    vx2, vy2 = device_to_view(sess, x + w, y + h)
    return "%s (%s) covers view (%d,%d)-(%d,%d) = device px [%d,%d %dx%d]" % (o["package"], o["type"], vx, vy, vx2, vy2, x, y, w, h)


def guard_overlay(a, sess, dev, points):
    """Block adb taps that land on another app's overlay — they hit that app (once started a floating mic app's recording by mistake)."""
    if getattr(a, "inject", False) or not dev.native_input or getattr(a, "allow_overlay", False):
        return
    ovs = get_overlays(sess, dev)
    for x, y in points:
        o = point_in_rects(x, y, ovs)
        if o:
            raise QaError("touch point (%d,%d) device px is under another app's overlay: %s — an adb tap would hit that app, not "
                          "the game. Fix: tap the game UI via the probe (--inject), or turn off/move that overlay on the device, "
                          "or --allow-overlay if you mean to tap it" % (x, y, overlay_line(sess, o)))


def kill_app(dev, pkg, hard=False, settle=STOP_SETTLE_SECONDS):
    """Close the game. Default like a player (Home → wait → kill: the game gets OnApplicationPause/saves); hard = kill outright
    like a crash / the OS killing the app — the game does NOT get to save. Returns "player" | "hard" | "hard-fallback" (device without Home)."""
    if hard:
        dev.stop(pkg)
        return "hard"
    try:
        dev.stop_like_player(pkg, settle)
        return "player"
    except NotSupported:
        dev.stop(pkg)
        return "hard-fallback"


KILL_DESC = {"player": "Về màn hình chính, vuốt tắt game khỏi đa nhiệm",
             "hard": "Game bị tắt đột ngột (giả lập crash / hệ điều hành giết app)",
             "hard-fallback": "Tắt hẳn game"}
KILL_NOTE = {"player": "Home → wait %ss → kill, like a player swiping it away",
             "hard": "hard kill — like a crash / the OS killing the app, the game cannot save on pause",
             "hard-fallback": "device has no Home key → hard kill; save bugs must be re-confirmed on Android/Simulator"}


def inject(sess, dev, path, **params):
    pr = need_probe(dev, "Input via the probe")
    sx, sy = unity_scale(sess)
    conv = {}
    for k, v in params.items():
        if k in ("x", "x1", "x2"):
            conv[k] = round(v * sx, 1)
        elif k in ("y", "y1", "y2"):
            conv[k] = round(v * sy, 1)
        else:
            conv[k] = v
    return pr.post(path, timeout=20, **conv)


def after_action(a, sess, dev, label):
    if getattr(a, "no_shot", False):
        fresh, rep = check_logs(sess, dev)
        print_issues(fresh, rep)
        maybe_auto_audit(sess, dev, label)
        return
    time.sleep(max(0.0, getattr(a, "wait", 0.8)))
    observe(sess, dev, label, grid=getattr(a, "grid", False), marks=getattr(a, "marks", False), after_action=True)
    maybe_auto_audit(sess, dev, label)


def parse_expect(spec):
    """'text:Nhận' | '@12' | 'screen:Shop' | 'Nhận' (default text) → (kind, value)."""
    s = (spec or "").strip()
    if s.startswith("@") and s[1:].isdigit():
        return "id", int(s[1:])
    for k in ("text", "screen", "name"):
        if s.lower().startswith(k + ":"):
            return k, s[len(k) + 1:].strip()
    return "text", s


def expect_met(j, kind, value):
    els = j.get("elements") or []
    v = str(value).lower()
    if kind == "screen":
        return any(v in (s or "").lower() for s in j.get("screens") or []) or v in (j.get("scene") or "").lower()
    if kind == "id":
        return any(e.get("id") == value for e in els)
    if kind == "name":
        return any(v == (e.get("name") or "").lower() or (e.get("path") or "").lower().endswith("/" + v) for e in els)
    return any(v in ((e.get("text") or "") + " " + (e.get("label") or "")).lower() for e in els)


def expect_pre_met(a, dev):
    """Is the `--expect` expectation already met BEFORE the action? (called before tap/key) — None without a probe / when it cannot be checked."""
    spec = getattr(a, "expect", None)
    if not spec:
        return None
    try:
        kind, value = parse_expect(spec)
        return expect_met(need_probe(dev, "--expect").get("/ui", all=1, timeout=15), kind, value)
    except Exception:
        return None


def check_expect(a, sess, dev, spec, pre_met=None):
    """`--expect`: wait for the expectation (probe) until the timeout; not seen → record an expectation-failed finding (NO bug is created).
    `pre_met=True` (already met before the action, e.g. `screen:` of a screen that was already open) → still passes but warns: it proves no response."""
    kind, value = parse_expect(spec)
    pr = need_probe(dev, "Checking the expectation after an action (--expect)")
    timeout = max(0.5, float(getattr(a, "timeout", None) or EXPECT_TIMEOUT_S))
    t0 = time.time()
    last = None
    while True:
        try:
            last = pr.get("/ui", all=1, timeout=15)
        except QaError as e:
            last = {"error": str(e)}
        if expect_met(last, kind, value):
            warn = " — ⚠ already true BEFORE the action, so it proves no response; use a text/@id that only exists on the new screen" if pre_met else ""
            print("  ✓ expectation %s:%s seen after %.1fs%s" % (kind, value, time.time() - t0, warn))
            sess.step("check", "Kỳ vọng %s:%s — đạt%s" % (kind, value, " (đã đúng trước thao tác)" if pre_met else ""), result="pass")
            return True
        if time.time() - t0 >= timeout:
            break
        time.sleep(0.5)
    scr = " > ".join(last.get("screens") or []) if isinstance(last, dict) else "?"
    print("  ✗ EXPECTATION FAILED: %s:%s did not appear after %.0fs (current screen: %s) — confirm on the shot, then decide whether to log a bug" % (
        kind, value, timeout, scr or last.get("scene") if isinstance(last, dict) else "?"))
    sess.step("check", "Kỳ vọng %s:%s — KHÔNG đạt (màn: %s)" % (kind, value, scr), result="fail", kind="expectation-failed")
    sess.d.setdefault("expect_failed", []).append({"expect": spec, "screens": last.get("screens") if isinstance(last, dict) else None,
                                                   "step": _last_step_n(sess), "t": now_iso()})
    sess.save()
    return False


def cmd_tap(a):
    sess = Session.load(a.session)
    dev = sess.device()
    pre_met = expect_pre_met(a, dev)
    x, y, el = act_point(a, sess, dev)
    guard_overlay(a, sess, dev, [(x, y)])
    if a.times > 1 and not (a.inject or not dev.native_input) and isinstance(dev, Android):
        dev.tap_many(x, y, a.times, a.interval)
    else:
        for _ in range(max(1, a.times)):
            if a.inject or not dev.native_input:
                r = inject(sess, dev, "/tap", x=x, y=y)
                el = el or r.get("hit", "")
            else:
                dev.tap(x, y)
            if a.times > 1:
                time.sleep(a.interval)
    vx, vy = device_to_view(sess, x, y)
    sess.step("tap", a.desc or "", xy=[vx, vy], dev=[x, y], el=el, times=a.times if a.times > 1 else None)
    print("tap%s (%d,%d) view → (%d,%d) device%s%s" % ("×%d" % a.times if a.times > 1 else "", vx, vy, x, y,
                                               (" · " + el) if el else "", (" · \"%s\"" % a.desc) if a.desc else ""))
    after_action(a, sess, dev, a.label or "tap")
    if getattr(a, "expect", None):
        check_expect(a, sess, dev, a.expect, pre_met=pre_met)


def cmd_longpress(a):
    sess = Session.load(a.session)
    dev = sess.device()
    x, y, el = act_point(a, sess, dev)
    guard_overlay(a, sess, dev, [(x, y)])
    if a.inject or not dev.native_input:
        inject(sess, dev, "/tap", x=x, y=y, hold=a.ms)
    else:
        dev.longpress(x, y, a.ms)
    vx, vy = device_to_view(sess, x, y)
    sess.step("longpress", a.desc or "", xy=[vx, vy], el=el, ms=a.ms)
    print("longpress (%d,%d) %dms%s" % (vx, vy, a.ms, (" · " + el) if el else ""))
    after_action(a, sess, dev, a.label or "longpress")


def cmd_swipe(a, action="swipe"):
    sess = Session.load(a.session)
    dev = sess.device()
    if a.dir:
        v = sess.d["view"]
        cx, cy = v["w"] / 2.0, v["h"] / 2.0
        dist = (a.dist or 40) / 100.0
        d = {"up": (0, -1), "down": (0, 1), "left": (-1, 0), "right": (1, 0)}[a.dir]
        pts = [cx - d[0] * v["w"] * dist / 2, cy - d[1] * v["h"] * dist / 2, cx + d[0] * v["w"] * dist / 2, cy + d[1] * v["h"] * dist / 2]
        x1, y1 = to_device_xy(sess, pts[0], pts[1], "view")
        x2, y2 = to_device_xy(sess, pts[2], pts[3], "view")
    else:
        if len(a.coords) != 4:
            raise QaError("need X1 Y1 X2 Y2 or --dir up|down|left|right")
        x1, y1 = to_device_xy(sess, a.coords[0], a.coords[1], a.space)
        x2, y2 = to_device_xy(sess, a.coords[2], a.coords[3], a.space)
    hold = getattr(a, "hold", 0)
    guard_overlay(a, sess, dev, [(x1, y1)])  # the touch-down point decides which window receives the whole gesture
    if a.inject or not dev.native_input:
        inject(sess, dev, "/drag", x1=x1, y1=y1, x2=x2, y2=y2, ms=a.ms, hold=hold)
    elif action == "drag":
        dev.drag(x1, y1, x2, y2, a.ms, hold)
    else:
        dev.swipe(x1, y1, x2, y2, a.ms)
    v1, v2 = device_to_view(sess, x1, y1), device_to_view(sess, x2, y2)
    sess.step(action, a.desc or "", xy=[v1[0], v1[1], v2[0], v2[1]], ms=a.ms)
    print("%s (%d,%d)→(%d,%d) %dms%s" % (action, v1[0], v1[1], v2[0], v2[1], a.ms, (" · \"%s\"" % a.desc) if a.desc else ""))
    after_action(a, sess, dev, a.label or action)


def cmd_drag(a):
    cmd_swipe(a, "drag")


def cmd_text(a):
    sess = Session.load(a.session)
    dev = sess.device()
    if a.probe or not dev.native_input or any(ord(c) > 126 for c in a.value):
        pr = need_probe(dev, "Unicode text input")
        r = pr.post("/text", text=a.value, path=a.path or "", submit=1 if a.submit else 0)
        print("text (probe) → %s" % r.get("target", "?"))
    else:
        dev.text(a.value)
        if a.submit:
            dev.key("enter")
        print("text \"%s\"" % a.value)
    sess.step("text", a.desc or "", text=a.value)
    after_action(a, sess, dev, a.label or "text")


def cmd_key(a):
    sess = Session.load(a.session)
    dev = sess.device()
    dev.key(a.name)
    sess.step("key", a.desc or ("Bấm nút Back (Android)" if a.name.lower() == "back" else ""), key=a.name)
    print("key %s" % a.name)
    after_action(a, sess, dev, a.label or ("key-" + a.name))


def cmd_shot(a):
    sess = Session.load(a.session)
    dev = sess.device()
    observe(sess, dev, a.label or "", grid=a.grid, marks=a.marks, logs=not a.no_logs)


def cmd_wait(a):
    sess = Session.load(a.session)
    dev = sess.device()
    if a.settle:
        t0 = time.time()
        prev = None
        while time.time() - t0 < a.secs:
            v = observe(sess, dev, "settle", logs=False, compare=False)
            if prev:
                dr = diff_ratio(prev, v["path"])
                if dr and dr[0] < a.threshold:
                    break
            prev = v["path"]
            time.sleep(0.4)
        print("waited %.1fs for the screen to settle" % (time.time() - t0))
        fresh, rep = check_logs(sess, dev)
        print_issues(fresh, rep)
        return
    time.sleep(a.secs)
    sess.step("wait", a.desc or "", secs=a.secs)
    if a.no_shot:
        fresh, rep = check_logs(sess, dev)
        print_issues(fresh, rep)
    else:
        observe(sess, dev, a.label or "wait")
    maybe_auto_audit(sess, dev, "wait")


def cmd_burst(a):
    sess = Session.load(a.session)
    dev = sess.device()
    paths = []
    for k in range(a.count):
        v = observe(sess, dev, "burst%d" % (k + 1), logs=(k == a.count - 1), compare=k > 0)
        paths.append(v["path"])
        time.sleep(a.interval)
    if Image is not None and paths:
        # join into one strip to view at once
        ims = [Image.open(p) for p in paths]
        h = max(i.size[1] for i in ims)
        s = 420.0 / h
        ims = [i.resize((max(1, int(i.size[0] * s)), int(i.size[1] * s))) for i in ims]
        strip = Image.new("RGB", (sum(i.size[0] for i in ims) + 6 * (len(ims) - 1), ims[0].size[1]), (40, 40, 40))
        x = 0
        for i in ims:
            strip.paste(i, (x, 0))
            x += i.size[0] + 6
        out = sess.path("shots", "%03d-burst-strip.jpg" % sess.d["shot"])
        strip.save(out, "JPEG", quality=82)
        print("%d-frame strip: %s" % (len(ims), out))


def cmd_app(a):
    sess = Session.load(a.session)
    dev = sess.device()
    pkg = sess.d["package"]
    op = a.op
    if op == "restart":
        sess.d["expect_exit"] = sess.d.get("expect_exit", 0) + 1
        sess.save()
        mode = kill_app(dev, pkg, hard=a.hard, settle=a.settle)
        time.sleep(1.0)
        relaunch(sess, dev)
        sess.step("restart", a.desc or (KILL_DESC[mode] + ", rồi mở lại game"), mode=mode)
        print("restart done (%s → relaunched)" % (KILL_NOTE[mode] % a.settle if mode == "player" else KILL_NOTE[mode]))
    elif op == "stop":
        sess.d["expect_exit"] = sess.d.get("expect_exit", 0) + 1
        sess.save()
        mode = kill_app(dev, pkg, hard=a.hard, settle=a.settle)
        sess.d["app_running"] = False
        sess.step("stop", a.desc or KILL_DESC[mode], mode=mode)
        print("app stopped (%s)" % (KILL_NOTE[mode] % a.settle if mode == "player" else KILL_NOTE[mode]))
        return
    elif op == "launch":
        relaunch(sess, dev)
        sess.step("launch", a.desc or "")
        print("app launched")
    elif op == "background":
        dev.background(pkg, a.secs)
        sess.step("background", a.desc or "", secs=a.secs)
        print("sent to background for %ss, then reopened" % a.secs)
    elif op == "clear":
        sess.d["expect_exit"] = sess.d.get("expect_exit", 0) + 1
        sess.save()
        dev.stop(pkg)
        dev.clear_data(pkg)
        if (sess.d.get("probe") or {}).get("token"):
            dev.write_trigger(pkg, "%s:%d" % (sess.d["probe"]["token"], sess.d["probe"]["port"]))
        relaunch(sess, dev)
        sess.step("fresh", a.desc or "Xoá dữ liệu game rồi mở lại (người chơi mới)")
        print("data cleared + relaunched")
    elif op == "info":
        vn, vc = dev.app_version(pkg)
        print("%s v%s (%s) · pid %s · foreground=%s" % (pkg, vn, vc, dev.pidof(pkg), dev.foreground() or "?"))
        return
    elif op == "install":
        if not a.path:
            raise QaError("app install <apk/app/ipa path>")
        dev.install(a.path)
        print("installed %s" % a.path)
        return
    time.sleep(a.wait)
    observe(sess, dev, op)
    maybe_auto_audit(sess, dev, op)


def relaunch(sess, dev):
    pkg = sess.d["package"]
    pr = sess.d.get("probe") or {}
    token, port = pr.get("token"), pr.get("port", PROBE_PORT)
    if isinstance(dev, IosDevice):
        kill_pid((sess.d.get("console") or {}).get("pid"))
        sess.d["console"] = {"pid": spawn_background(dev.launch_cmd(pkg, token, port, True), sess.path("device.log"))}
    else:
        sess.d["launch_ms"] = dev.launch(pkg, token, port)
    sess.d["app_running"] = True
    sess.save()
    if token and pr.get("ok"):
        if isinstance(dev, Android) and pr.get("local"):
            dev.unforward(int(pr["local"]))
        msg = connect_probe(sess, dev, wait=30)
        print("probe: %s" % msg)


def cmd_net(a):
    sess = Session.load(a.session)
    dev = sess.device()
    st = dev.network(a.mode)
    sess.d["changed"]["network"] = a.mode != "on"
    sess.save()
    sess.step("network", a.desc or "", mode=a.mode)
    print("network → %s · %s" % (a.mode, st))
    after_action(a, sess, dev, "net-" + a.mode)


def cmd_rotate(a):
    sess = Session.load(a.session)
    dev = sess.device()
    dev.rotate(a.mode)
    sess.d["changed"]["rotation"] = a.mode != "auto"
    sess.save()
    sess.step("rotate", a.desc or "", mode=a.mode)
    print("rotate → %s" % a.mode)
    after_action(a, sess, dev, "rotate-" + a.mode)


def cmd_overlays(a):
    sess = Session.load(a.session)
    dev = sess.device()
    if not isinstance(dev, Android):
        print("only Android can detect other apps' overlays")
        return
    ovs = get_overlays(sess, dev, refresh=True)
    if not ovs:
        print("no other app's overlay is receiving touches")
    for o in ovs:
        print("⚠ " + overlay_line(sess, o))


def cmd_ad_close(a):
    """Native ad/page (its own Activity — the Unity probe cannot see it): find the close/skip button via uiautomator and tap it
    like a player. Button fully covered by another app's overlay → say so instead of tapping that app."""
    sess = Session.load(a.session)
    dev = sess.device()
    if not isinstance(dev, Android):
        raise NotSupported("ad-close is Android only — iOS: look at the shot and tap the X button (or wait for the ad to close itself)")
    deadline = time.time() + max(0.0, a.timeout)
    while True:
        cands = pick_close_candidates(parse_uia_nodes(dev.uia_dump()))
        if cands or a.list or time.time() >= deadline:
            break
        time.sleep(2.0)
    fg = dev.foreground()
    if not cands:
        print("no close/skip button found (foreground=%s) after %ss — ad still counting down? `wait` more and run it again, "
              "or `key back`" % (fg or "?", a.timeout))
        return
    ovs = get_overlays(sess, dev, refresh=True)
    for c in cands[:5]:
        x, y, w, h = c["rect"]
        vx, vy = device_to_view(sess, x + w // 2, y + h // 2)
        o = point_in_rects(x + w // 2, y + h // 2, ovs)
        print("  candidate \"%s\" view (%d,%d) %dx%d device px%s%s" % (c["label"], vx, vy, w, h,
                                                             "" if c["clickable"] else " · not clickable",
                                                             (" · COVERED by " + o["package"]) if o else ""))
    if a.list:
        return
    c = cands[0]
    if a.allow_overlay:
        pt = (c["rect"][0] + c["rect"][2] // 2, c["rect"][1] + c["rect"][3] // 2)
    else:
        pt = uncovered_point(c["rect"], ovs)
    if pt is None:
        o = point_in_rects(c["rect"][0] + c["rect"][2] // 2, c["rect"][1] + c["rect"][3] // 2, ovs) or (ovs[0] if ovs else {})
        raise QaError("ad close button \"%s\" is fully covered by another app's overlay: %s — adb cannot tap through it; turn off/move that overlay "
                      "on the device, then run `ad-close` again. Test cases that depend on ads are `blocked` (environment), "
                      "NOT a game bug" % (c["label"], overlay_line(sess, o) if o else "?"))
    dev.tap(pt[0], pt[1])
    vx, vy = device_to_view(sess, pt[0], pt[1])
    sess.step("tap", a.desc or "Bấm nút đóng quảng cáo (%s)" % c["label"], xy=[vx, vy], dev=list(pt), el=c["label"])
    print("ad-close: tapped \"%s\" view (%d,%d) · previous foreground=%s" % (c["label"], vx, vy, fg or "?"))
    after_action(a, sess, dev, a.label or "ad-close")


def cmd_logs(a):
    sess = Session.load(a.session)
    dev = sess.device()
    if a.tail or a.grep:
        path = (sess.d.get("log") or {}).get("file")
        lines = read_text(path).splitlines() if path else []
        pids = set(str(p) for p in sess.d.get("pids") or [])
        if not a.raw and sess.d["device"]["kind"] == "android":
            lines = [l for l in lines if (LOGCAT_RE.match(l) and LOGCAT_RE.match(l).group(2) in pids) or sess.d["package"] in l]
        if a.grep:
            rg = re.compile(a.grep, re.I)
            lines = [l for l in lines if rg.search(l)]
        for l in lines[-(a.tail or 80):]:
            print(clip(l, 400))
        return
    fresh, rep = check_logs(sess, dev)
    print_issues(fresh, rep, verbose_lines=12)
    if a.all:
        issues = read_json(sess.path("issues.json"), {}) or {}
        print("— whole session: %d error signatures" % len(issues))
        for sig, it in sorted(issues.items(), key=lambda kv: SEVERITIES.index(kv[1]["severity"]) if kv[1]["severity"] in SEVERITIES else 9):
            print("  [%s] %s ×%d sig=%s step#%s%s · %s" % (it["severity"], it["kind"], it["count"], sig, it.get("step"),
                                                         (" → bug #%s" % it["bug"]) if it.get("bug") else "", clip(it["first"], 160)))
    if a.ignore:
        sess.d.setdefault("ignore", []).append(a.ignore)
        sess.save()
        print("ignored from now on (this session): /%s/" % a.ignore)


def cmd_perf(a):
    sess = Session.load(a.session)
    dev = sess.device()
    res = dev.perf(sess.d["package"])
    if dev.probe:
        try:
            j = dev.probe.get("/perf", timeout=8)
            res.update({"fps": j.get("fps"), "worst_ms": j.get("worstMs"), "mono_mb": j.get("monoMb"), "unity_mb": j.get("allocatedMb"),
                        "objects": j.get("objects")})
        except QaError:
            pass
    res = {k: v for k, v in res.items() if v is not None}
    res["t"] = now_iso()
    res["step"] = _last_step_n(sess)
    hist = read_jsonl(sess.path("perf.jsonl"))
    append_jsonl(sess.path("perf.jsonl"), res)
    line = " · ".join("%s=%s" % (k, v) for k, v in res.items() if k not in ("t", "step"))
    print("perf: %s" % (line or "(could not read)"))
    if hist and "pss_mb" in res and "pss_mb" in hist[0]:
        grow = res["pss_mb"] - hist[0]["pss_mb"]
        print("  RAM vs the session's first sample: %+.1f MB%s" % (grow, "  ⚠ sharp increase — suspected memory leak" if grow > 150 else ""))
    if res.get("fps") is not None and res["fps"] < 25:
        print("  ⚠ low FPS: %.1f" % res["fps"])
    if res.get("battery_c") and res["battery_c"] >= 43:
        print("  ⚠ device hot %.1f°C" % res["battery_c"])


def cmd_ui(a):
    sess = Session.load(a.session)
    dev = sess.device()
    j = probe_ui(sess, dev, include_all=a.all)
    if a.json:
        print(json.dumps(j, ensure_ascii=False, indent=1))
        return
    if not sess.d.get("view"):
        observe(sess, dev, "ui", logs=False)
    els = [e for e in j.get("elements", []) if a.all or not e.get("occluded")]
    if a.find:
        fl = a.find.lower()
        els = [e for e in els if fl in (e.get("text", "") + " " + e.get("label", "") + " " + e.get("path", "")).lower()]
    print("UI · scene=%s · screens: %s · %d elements (coordinates = view px of shot #%s)" % (
        j.get("scene"), " > ".join(j.get("screens", [])) or "?", len(els), sess.d["view"].get("n")))
    for e in els:
        vx, vy, vw, vh = _view_rect(sess, e["dev"])
        state = ""
        if e.get("interactable") is False and e.get("kind") in ("Button", "Toggle", "InputField", "Dropdown"):
            state = " ✗disabled"
        if e.get("blockedBy"):
            state += " ✗covered:" + clip(e["blockedBy"], 40)  # "✗covered:" is quoted by the skill doc — keep it
        flags = [f for f in e.get("flags", []) if f not in ("interactable",)]
        txt = e.get("label") or e.get("text") or ""
        print(" [%d] %-9s %-28s %-26s c=(%d,%d) %dx%d%s%s" % (e["id"], e.get("kind", "")[:9], clip(e.get("name", ""), 28),
                                                           ("\"%s\"" % clip(txt, 22)) if txt else "", vx + vw // 2, vy + vh // 2, vw, vh,
                                                           state, (" ⚑" + ",".join(flags)) if flags else ""))
    world = j.get("world", [])
    if world:
        print("World (tappable 3D/2D objects, nearest to the camera first):")
        for o in world[:25]:
            vx, vy = device_to_view(sess, *o["dev"])
            print("  · %-40s c=(%d,%d)%s" % (clip(o.get("path", ""), 40), vx, vy, "" if o.get("onScreen") else " (off-screen)"))
    if a.marks and Image is not None:
        out = draw_ui_marks(sess, dev, sess.d["view"]["path"], "%03d-ui" % sess.d["view"]["n"])
        if out:
            print("numbered shot: %s" % out)


def cmd_audit(a):
    sess = Session.load(a.session)
    dev = sess.device()
    v = observe(sess, dev, "audit", logs=True)
    if not dev.probe:
        print("deep audit (text overflow/missing glyphs/covered buttons…) needs the probe — only the image + log checks above ran. Inspect the shot by eye with the visual checklist.")
        return
    findings, j = run_audit(sess, dev, view=v, show_all=a.all, include_suppressed=a.include_suppressed)
    if not probe_supports(sess, "flags"):
        print("  (probe v%s has no 1.3 flags[] — the checks ported from UiAuditUtil are missing; rebuild to get them)" % ((sess.d.get("probe") or {}).get("version") or "?"))
    sess.d["screens_key"] = screens_key(j)
    sess.save()


def cmd_state(a):
    sess = Session.load(a.session)
    dev = sess.device()
    pr = need_probe(dev, "Reading game state")
    dump = {}
    for p in a.paths:
        try:
            j = pr.get("/get", path=p, timeout=10)
            dump[p] = j.get("value")
            if not a.out:
                print(format_value(p, j.get("value"), full=a.full).replace(" → ", " = ", 1))
            sess.d.setdefault("verified_state", {})[p] = qaprofile.today()  # flushed into qa.verified.statePaths at stop
        except ProbeError as e:
            print("%s → error: %s" % (p, e))
    sess.save()
    if a.out and dump:
        write_text(os.path.abspath(a.out), json.dumps(dump if len(a.paths) > 1 else next(iter(dump.values())),
                                                      ensure_ascii=False, indent=2))
        print("wrote %d values to %s" % (len(dump), os.path.abspath(a.out)))


def cmd_call(a):
    sess = Session.load(a.session)
    dev = sess.device()
    pr = need_probe(dev, "Calling a game method")
    j = pr.post("/call", method=a.method, args=a.args)
    sess.step("call", a.desc or "", method="%s(%s)" % (a.method, ", ".join(a.args)))
    head = "%s(%s)" % (a.method, ", ".join(a.args))
    prefix = (qa_profile().get("conventions") or {}).get("cheatMethodPrefix") or "Cheat_"
    if a.method.split(".")[-1].startswith(prefix):
        sess.d.setdefault("verified_cheats", {})[a.method] = qaprofile.today()  # flushed into qa.verified.cheats at stop
        sess.save()
    if a.out:
        write_text(os.path.abspath(a.out), json.dumps(j.get("value"), ensure_ascii=False, indent=2))
        print("%s → result written to %s" % (head, os.path.abspath(a.out)))
    else:
        print(format_value(head, j.get("value"), full=a.full))
    if not a.no_shot:
        time.sleep(a.wait)
        observe(sess, dev, "call")
    maybe_auto_audit(sess, dev, "call")


def cmd_types(a):
    sess = Session.load(a.session)
    dev = sess.device()
    pr = need_probe(dev, "Looking up game types/methods")
    j = pr.get("/types", q=a.query, timeout=20)
    for t in j.get("types", []):
        print("%s" % t.get("type"))
        for m in t.get("members", [])[:40]:
            print("    %s" % m)


def cmd_timescale(a):
    sess = Session.load(a.session)
    dev = sess.device()
    pr = need_probe(dev, "Changing timeScale")
    j = pr.post("/timescale", value=a.value)
    sess.step("note", "timeScale %s → %s (tăng tốc chờ, không phải thao tác người chơi)" % (j.get("previous"), a.value))
    print("timeScale %s → %s" % (j.get("previous"), j.get("value")))


def cmd_cheat(a):
    sess = Session.load(a.session)
    dev = sess.device()
    pr = need_probe(dev, "Toggling the game's cheats")
    params = {} if a.mode == "status" else {"on": 1 if a.mode == "on" else 0}
    j = pr.get("/cheat", **params)
    cg = j.get("cheatGate") or {}
    print("game cheats: %s%s · probe gate %s%s" % (
        {True: "ON", False: "off"}.get(j.get("cheat"), "?"), (" (via %s)" % j["via"]) if j.get("via") else "", j.get("gate"),
        (" · cheat-gate %s %s" % (cg.get("state"), cg.get("deviceId"))) if cg.get("present") else ""))
    if a.mode != "status":
        sess.step("cheat", "(chuẩn bị) %s cheat của game" % ("Bật" if a.mode == "on" else "Tắt"))
        if j.get("gate") != "open":
            print("⚠ the probe gate closed after turning cheats off (the gate follows the game's cheat flag) — `cheat on` can no longer be called; turn cheats back on on the device, then `probe-connect`")


def cmd_mark(a):
    sess = Session.load(a.session)
    rec = sess.step("mark", a.name or "")
    sess.d["mark"] = rec["n"]
    sess.d.setdefault("cases", []).append({"name": a.name or "", "step": rec["n"], "t": rec["t"]})
    sess.save()
    print("mark #%d %s — bug repro steps start from here" % (rec["n"], a.name or ""))


def cmd_note(a):
    sess = Session.load(a.session)
    text = a.text or ""
    if a.case:
        # Case with an id in the charter (<FEATURE>-NN): separate bookkeeping → readable by coverage/charter diff.
        if not a.result:
            raise QaError("note --case needs --result pass|fail|blocked|skip")
        sc = qaprofile.split_case(a.case, sess.d.get("charter") or "")
        if not sc:
            raise QaError("--case is <charter-file-name>/<P0-01> (e.g. offline-earning/P0-01); just P0-01 once the session has a charter (`charter new`/`charter diff --charter`)")
        charter, cid = sc
        if not charter:
            raise QaError("--case %s has no charter name — write <charter>/%s (see `charter list`)" % (a.case, cid))
        case = qaprofile.full_case(charter, cid)
        rec = sess.step("note", "[%s] %s: %s" % (case, a.result, text) if text else "[%s] %s" % (case, a.result), case=case, result=a.result,
                        bug=a.bug)
        append_jsonl(sess.path("cases.jsonl"), {"case": case, "charter": charter, "id": cid, "result": a.result, "bug": a.bug,
                                                "text": text, "step": rec["n"], "t": rec["t"], "mark": sess.d.get("mark", 0)})
        sess.d["charter"] = charter
        sess.save()
        print("case %s → %s%s" % (case, a.result, (" (bug #%s)" % a.bug) if a.bug else ""))
        return
    if not text:
        raise QaError("note needs text (or --case <ID> --result …)")
    sess.step("note", text)
    if a.result:
        cases = sess.d.setdefault("cases", [])
        if cases:
            cases[-1]["result"] = a.result
            cases[-1]["note"] = text
        sess.save()
    print("note saved")


def cmd_steps(a):
    sess = Session.load(a.session)
    steps = sess.steps_since_mark() if not a.all else [s for s in read_jsonl(sess.path("steps.jsonl"))]
    print(format_steps(steps) or "(no steps since the last mark)")


def cmd_bug(a):
    root = repo_root()
    sess = Session.load(a.session)
    dev = sess.device()
    bh = sess.d.get("bughub") or {}
    code = a.code or bh.get("code")
    endpoint = a.endpoint or bh.get("endpoint") or bughub_endpoint(root)
    if not code:
        raise QaError("no BugHub code — run /bughub-setup")
    form = read_json(os.path.join(state_root(), "bughub-form-%s.json" % code.upper()), {}) or {}
    a.title = tidy_title(a.title, min(int(((form.get("limits") or {}).get("titleMax")) or TITLE_SHORT_MAX), TITLE_SHORT_MAX))
    idx_path = os.path.join(state_root(), "bugs-index.jsonl")
    if a.steps and a.steps != "auto":
        steps = (("Điều kiện: %s\n" % a.precondition) if a.precondition else "") + a.steps.replace("\\n", "\n")
    else:
        auto = sess.steps_since_mark()
        it = (read_json(sess.path("issues.json"), {}) or {}).get(a.log_sig or "")
        if it and it.get("step"):  # log error: stop at the step that triggered it
            auto = [st for st in auto if st["n"] <= it["step"]] or auto
        steps = format_steps(auto, a.precondition or "")
    app = dict(sess.d.get("app") or {})
    ctx = {}
    if dev.probe:
        try:
            ctx = dev.probe.get("/context", save=1 if a.save else 0, timeout=20)
        except QaError:
            ctx = {}
    screen = ctx.get("screen") or ""
    # local de-dup (every session of the project): log signature · title · screen + type. The error text is a skill-doc marker — keep it.
    if not a.force:
        dup, why = find_duplicate(read_jsonl(idx_path), code, a.title, a.log_sig or "", screen, a.type)
        if dup:
            raise QaError("duplicate of logged bug #%s \"%s\" (session %s; %s) — skip it, or --force if it really is different" % (
                dup.get("number") or "?", dup.get("title"), dup.get("session"), why))
    app.setdefault("commitSha", "")
    if ctx.get("commitSha") and ctx["commitSha"] != "unknown":
        app["commitSha"], app["branch"] = ctx["commitSha"], ctx.get("branch", app.get("branch", ""))
        if ctx.get("build"):
            app["build"] = ctx["build"]
    app = {"version": app.get("version", ""), "build": app.get("build", ""), "commitSha": app.get("commitSha") or "unknown",
           "branch": app.get("branch") or "unknown", "platform": app.get("platform", dev.platform),
           "unityVersion": ctx.get("unityVersion") or app.get("unityVersion", "")}
    v = sess.d.get("view") or {}
    dv = sess.d["device"]
    devinfo = {"id": dv.get("deviceId", dv["id"]), "model": dv.get("model", ""), "os": dv.get("os", ""),
               "ramMb": dv.get("ramMb", 0), "screen": "%sx%s" % (v.get("shot_w", "?"), v.get("shot_h", "?"))}
    shot_path = None
    if a.shot and a.shot != "none":
        shot_path = v.get("full") if a.shot == "last" else a.shot
        if a.shot != "last" and not os.path.isabs(shot_path):
            shot_path = sess.path("shots", shot_path) if os.path.isfile(sess.path("shots", shot_path)) else shot_path
    shot = encode_bug_jpeg(shot_path) if shot_path and os.path.isfile(shot_path) else None
    btype = a.type
    sev = a.severity
    desc_lines = ["[auto-test-device] Bug do agent QA tự động phát hiện khi chơi thử trên thiết bị.",
                  "Mức độ: %s · Loại: %s · Tính năng test: %s" % (sev, btype, sess.d.get("feature") or "?"),
                  "Thiết bị: %s · %s · build %s (%s)" % (dv.get("model"), dv.get("os"), app["version"], app["build"])]
    if a.evidence:
        desc_lines.append("Bằng chứng: %s" % a.evidence)
    if a.log_sig:
        it = (read_json(sess.path("issues.json"), {}) or {}).get(a.log_sig)
        if it:
            desc_lines.append("Log (%s ×%d): %s" % (it["kind"], it["count"], clip(it["first"], 300)))
            desc_lines += ["  " + clip(l.strip(), 200) for l in it.get("lines", [])[1:8]]
    if a.description:
        desc_lines.append(a.description)
    desc_lines.append("Phiên QA: %s" % sess.id)
    fields = {"title": a.title, "featureKey": bug_feature(a.feature, a.title, sess.d.get("feature"), ctx.get("screen", "")),
              "category": a.category or pick_category(form, btype), "reproduceRate": a.rate,
              "actual": a.actual, "expected": a.expected or "", "steps": steps, "description": "\n".join(desc_lines),
              "tagIds": pick_tags(form, btype, sev, a.tag or []), "mentionIds": pick_people(form, a.mention or [])}
    extra = {"reporter": REPORTER, "qaSession": sess.id, "severity": sev, "bugType": btype,
             "deviceSerial": dv["id"], "probe": "yes" if dev.probe else "no", "logSig": a.log_sig or "",
             "scene": ctx.get("scene", ""), "screen": screen[:256], "screens": ",".join(ctx.get("screens", []) or [])[:256],
             "toolVersion": VERSION, "repoHead": _git_head(root)}
    for k, val in (ctx.get("extra") or {}).items():
        extra.setdefault("game." + k, val)
    for kv in a.extra or []:
        if "=" in kv:
            k, val = kv.split("=", 1)
            extra[k.strip()] = val.strip()
    report = build_report(fields, app, devinfo, ctx.get("playerId", ""), extra)
    probe_logs = None
    if dev.probe and sess.d["device"]["kind"] != "android":
        try:
            probe_logs = dev.probe.get("/logs", since=0, timeout=10).get("entries")
        except QaError:
            probe_logs = None
    log = collect_log_excerpt(sess, sig=a.log_sig, probe_entries=probe_logs)
    save = ctx.get("save").encode("utf-8") if isinstance(ctx.get("save"), str) and ctx.get("save") else None
    if save and len(save) > MAX_SAVE_BYTES:
        save = None
    bug_dir = sess.path("bugs", report["id"])
    os.makedirs(bug_dir, exist_ok=True)
    write_json(os.path.join(bug_dir, "report.json"), report)
    if shot:
        with open(os.path.join(bug_dir, "screenshot.jpg"), "wb") as f:
            f.write(shot)
    with open(os.path.join(bug_dir, "log.txt"), "wb") as f:
        f.write(log)
    if not report["title"] or not report["actual"]:
        raise QaError("bug needs --title and --actual")
    if a.dry_run:
        print("DRY-RUN — not sent. Report: %s" % bug_dir)
        print(json.dumps({k: report[k] for k in ("title", "category", "featureKey", "reproduceRate", "tagIds")}, ensure_ascii=False))
        print("steps:\n" + report["steps"])
        return
    if a.via == "ingame":
        pr = need_probe(dev, "Logging a bug via the in-game Bug Logger")
        j = pr.post("/bug", timeout=30, title=report["title"], actual=report["actual"], expected=report["expected"],
                    steps=report["steps"], description=report["description"], featureKey=report["featureKey"],
                    category=report["category"], reproduceRate=report["reproduceRate"], tagIds=report["tagIds"],
                    mentionIds=report["mentionIds"], shot_b64=base64.b64encode(shot).decode() if shot else "")
        status, resp = 202, {"id": j.get("id"), "via": "ingame"}
    else:
        status, resp = post_report(endpoint, code, report, shot, log, save)
    ok = status in (200, 201, 202)
    rec = {"t": now_iso(), "session": sess.id, "code": code, "title": report["title"], "severity": sev, "type": btype,
           "status": status, "number": resp.get("number"), "url": resp.get("url"), "duplicate": resp.get("duplicate"),
           "reportId": report["id"], "dir": bug_dir, "logSig": a.log_sig, "screen": screen, "step": _last_step_n(sess)}
    if ok:
        append_jsonl(sess.path("bugs.jsonl"), rec)
        append_jsonl(idx_path, rec)
        if a.log_sig:
            issues = read_json(sess.path("issues.json"), {}) or {}
            if a.log_sig in issues:
                issues[a.log_sig]["bug"] = resp.get("number") or "sent"
                write_json(sess.path("issues.json"), issues)
        if resp.get("duplicate"):
            print("BugHub: DUPLICATE of existing report #%s %s" % (resp.get("number"), resp.get("url", "")))
        elif resp.get("number"):
            print("BUG #%s logged → %s" % (resp["number"], resp.get("url", "")))
        else:
            print("BugHub accepted the report (%s) id=%s — the issue number comes once the server has processed it" % (status, report["id"]))
    else:
        append_jsonl(sess.path("bugs-failed.jsonl"), rec)
        raise QaError("BugHub rejected (%s): %s — report kept in %s, resend with `bug-resend %s`" % (
            status, resp.get("error") or resp, bug_dir, report["id"]))


def cmd_bug_resend(a):
    sess = Session.load(a.session)
    bh = sess.d.get("bughub") or {}
    d = sess.path("bugs", a.report_id)
    report = read_json(os.path.join(d, "report.json"))
    if not report:
        raise QaError("report %s not found" % a.report_id)

    def rb(name):
        p = os.path.join(d, name)
        if os.path.isfile(p):
            with open(p, "rb") as f:
                return f.read()
        return None

    status, resp = post_report(bh.get("endpoint") or DEFAULT_ENDPOINT, bh.get("code"), report, rb("screenshot.jpg"), rb("log.txt"))
    print("%s %s" % (status, json.dumps(resp, ensure_ascii=False)))
    if status in (200, 201, 202):
        ex = report.get("extra") or {}
        rec = {"t": now_iso(), "session": sess.id, "code": bh.get("code"), "title": report["title"], "status": status,
               "number": resp.get("number"), "url": resp.get("url"), "reportId": report["id"], "dir": d,
               "type": ex.get("bugType"), "logSig": ex.get("logSig") or None, "screen": ex.get("screen", "")}
        append_jsonl(sess.path("bugs.jsonl"), rec)
        append_jsonl(os.path.join(state_root(), "bugs-index.jsonl"), rec)


def _git_head(root):
    rc, out, _ = run(["git", "-C", root, "rev-parse", "--short", "HEAD"], timeout=10)
    return out.strip() if rc == 0 else ""


def cmd_report(a):
    sess = Session.load(a.session)
    md = build_session_report(sess)
    path = sess.path("report.md")
    write_text(path, md)
    print(md if a.print else "report: %s" % path)


def probe_report_line(pr):
    if not pr.get("ok"):
        return "no (black-box)"
    cg = pr.get("cheatGate") or {}
    if pr.get("gate") != "open":
        return "yes, but the gate is closed (%s) — black-box" % (pr.get("gateReason") or "?")
    return "yes (v%s%s)" % (pr.get("version"), (" · cheat-gate device %s" % cg.get("deviceId")) if cg.get("state") == "unlocked" else "")


def build_session_report(sess):
    d = sess.d
    steps = read_jsonl(sess.path("steps.jsonl"))
    bugs = read_jsonl(sess.path("bugs.jsonl"))
    failed = read_jsonl(sess.path("bugs-failed.jsonl"))
    issues = read_json(sess.path("issues.json"), {}) or {}
    perf = read_jsonl(sess.path("perf.jsonl"))
    cases = d.get("cases", [])
    actions = [s for s in steps if s["action"] not in ("mark", "note")]
    dv, app = d["device"], d.get("app", {})
    out = ["# Auto test device — %s" % (d.get("feature") or "(unnamed)"), "",
           "| | |", "|---|---|",
           "| Session | `%s` |" % d["id"],
           "| Device | %s · %s (`%s`) |" % (dv.get("model"), dv.get("os"), dv["id"]),
           "| Build | %s v%s (%s)%s |" % (d["package"], app.get("version"), app.get("build"),
                                       (" · %s@%s" % (app.get("commitSha", "")[:9], app.get("branch"))) if app.get("commitSha") else ""),
           "| Probe | %s |" % probe_report_line(d.get("probe") or {}),
           "| API-error retry | %s |" % describe_api_retry(d.get("api_retry")),
           "| Actions | %d · shots %d · test cases %d |" % (len(actions), d.get("shot", 0), len(cases)),
           "| Cold start | %s |" % (("%d ms" % d["launch_ms"]) if d.get("launch_ms") else "?"), ""]
    out.append("## Bugs logged (%d)" % len(bugs))
    for b in bugs:
        out.append("- %s **%s** [%s/%s]%s" % (("#%s" % b["number"]) if b.get("number") else "(number pending)", b["title"],
                                             b.get("severity"), b.get("type"), (" — " + b["url"]) if b.get("url") else ""))
    if failed:
        out.append("- ⚠ %d reports failed to send (see bugs-failed.jsonl, `bug-resend`)" % len(failed))
    out.append("")
    out.append("## Test cases")
    for c in cases:
        out.append("- %s — %s%s" % (c.get("name") or "?", c.get("result", "no result recorded"), (": " + c["note"]) if c.get("note") else ""))
    out.append("")
    unreported = [it for it in issues.values() if not it.get("bug")]
    out.append("## Log errors (%d signatures, %d without a bug)" % (len(issues), len(unreported)))
    for it in sorted(issues.values(), key=lambda x: SEVERITIES.index(x["severity"]) if x["severity"] in SEVERITIES else 9):
        out.append("- [%s] %s ×%d%s — `%s`" % (it["severity"], it["kind"], it["count"], (" → #%s" % it["bug"]) if it.get("bug") else "",
                                             clip(it["first"], 150)))
    if perf:
        out.append("")
        out.append("## Performance")
        keys = sorted(set(k for p in perf for k in p if k not in ("t", "step")))
        for k in keys:
            vals = [p[k] for p in perf if isinstance(p.get(k), (int, float))]
            if vals:
                out.append("- %s: min %s · max %s · last %s" % (k, min(vals), max(vals), vals[-1]))
    out += report_extra_sections(sess)
    out.append("")
    out.append("Session folder: `%s`" % sess.dir)
    return "\n".join(out) + "\n"


def report_extra_sections(sess):
    """Report sections added in 1.5: charter cases + coverage, auto audit/noise, failed expectations, environment matrix,
    selfcheck, autotest-import, learned QA profile."""
    d = sess.d
    out = []
    cases = read_jsonl(sess.path("cases.jsonl"))
    if cases:
        out += ["", "## Charter cases (%d)" % len(cases)]
        for c in cases:
            out.append("- `%s` — **%s**%s%s" % (c["case"], c["result"], (" → bug #%s" % c["bug"]) if c.get("bug") else "", (": " + c["text"]) if c.get("text") else ""))
    cov = read_text(sess.path("coverage.md"))
    if cov:
        out += [""] + cov.rstrip("\n").splitlines()
    audits = sorted(glob.glob(sess.path("audit-*.json")))
    if audits:
        tot, sup, auto = 0, 0, 0
        kinds = {}
        for p in audits:
            j = read_json(p, {}) or {}
            fs = j.get("findings") if isinstance(j, dict) else j
            auto += 1 if "auto-" in os.path.basename(p) else 0
            for f in fs or []:
                if f.get("severity") == "info":
                    continue
                if f.get("suppressed"):
                    sup += 1
                    continue
                tot += 1
                kinds[f["kind"]] = kinds.get(f["kind"], 0) + 1
        out += ["", "## Visual audit: %d runs (%d automatic on screen change) · %d findings · %d hidden by noise rules" % (len(audits), auto, tot, sup)]
        for k, n in sorted(kinds.items(), key=lambda kv: -kv[1])[:12]:
            out.append("- %s ×%d" % (k, n))
    ef = d.get("expect_failed") or []
    if ef:
        out += ["", "## Failed expectations (%d)" % len(ef)]
        for e in ef:
            out.append("- step #%s: `%s` (screens: %s)" % (e.get("step"), e.get("expect"), " > ".join(e.get("screens") or []) or "?"))
    em = read_text(sess.path("env-matrix.md"))
    if em:
        out += [""] + em.rstrip("\n").splitlines()
    sc = d.get("selfcheck")
    if sc:
        out += ["", "## Tool selfcheck: %s (%d checks%s)" % ("OK" if not sc.get("fail") else "%d FAIL" % sc["fail"], sc.get("n", 0), ", session results are less reliable" if sc.get("fail") else "")]
    at = d.get("autotest")
    if at:
        out += ["", "## Imported Editor autotest: %s (%s) · %s · screens OK %d · screens failed %d" % (
            at.get("runId"), at.get("version"), " ".join("%s %d" % kv for kv in (at.get("bySeverity") or {}).items()),
            len(at.get("coveredScreens") or []), len(at.get("failedScreens") or []))]
    obs = d.get("observed") or {}
    if obs or d.get("verified_cheats") or d.get("verified_state"):
        out += ["", "## Learned QA profile (written to project-profile.json at stop)",
                "- Screens opened: %d — %s" % (len(obs), ", ".join(sorted(obs))),
                "- Cheats called OK: %s" % (", ".join(sorted(d.get("verified_cheats") or {})) or "—"),
                "- State paths read OK: %s" % (", ".join(sorted(d.get("verified_state") or {})) or "—")]
    mig = read_text(sess.path("migration.md"))
    if mig:
        out += [""] + mig.rstrip("\n").splitlines()
    return out


def cmd_stop(a):
    sess = Session.load(a.session)
    ar = sess.d.get("api_retry") or {}
    if ar.get("state") == "armed":
        # Disarm before writing the report: API errors after this session must no longer type the auto-test prompt into the pane.
        st, line = api_retry(repo_root(), "off")
        sess.d["api_retry"] = {"state": "disarmed" if st in ("disarmed", "not_armed") else st, "line": line}
        if st not in ("disarmed", "not_armed"):
            print("API-error retry: could not disarm (%s) — type `/auto-clear off`" % line)
    close_session(sess)
    print("session %s stopped · report: %s" % (sess.id, sess.path("report.md")))


def cmd_api_retry(a):
    """Re-arm / disarm / show the API-error retry of the current session (e.g. the dev ran `/auto-clear off`, or start --no-api-retry)."""
    sess = Session.load(a.session)
    if a.action != "status":
        st, line = api_retry(repo_root(), "arm" if a.action == "on" else "off")
        if a.action == "off" and st in ("disarmed", "not_armed"):
            st = "disarmed"
        sess.d["api_retry"] = {"state": st, "line": line}
        sess.save()
    print("API-error retry: %s" % describe_api_retry(sess.d.get("api_retry")))


def cmd_retry(a):
    """Re-run a qadevice command every `--every` seconds until it passes: transient failures (cable unplugged/offline, adb hung,
    flaky network or BugHub, lost probe) must not stop the test session. The child command runs in its own process, output goes
    straight through. Still failing after `--max-wait` → exit RETRY_PENDING_RC so the agent calls this exact `retry` again."""
    argv = list(a.argv or [])
    if argv and argv[0] == "--":
        argv = argv[1:]
    if not argv:
        raise QaError("retry needs a qadevice command, e.g. `retry -- shot` · `retry -- bug-resend <id>` · `retry -- start auto --feature …`")
    if argv[0] == "retry":
        raise QaError("retry nested in retry — drop one level")
    cmd = [sys.executable, os.path.abspath(__file__)] + (["--session", a.session] if a.session else []) + argv
    every = max(a.every, 1.0)
    deadline = time.time() + max(a.max_wait, 0)
    n = 0
    while True:
        n += 1
        sys.stdout.flush()
        rc = subprocess.call(cmd)
        if rc == 0:
            if n > 1:
                print("retry: passed on attempt %d" % n)
            return
        if rc in RETRY_FATAL_RC:
            raise QaError("retry: command exited with code %d — needs a human (syntax / BugHub not set up), retrying is pointless" % rc)
        if time.time() + every > deadline:
            print("retry: still failing after %d attempts (code %d)" % (n, rc))
            print("→ do not stop the session: run this exact `retry` again (every %ds, until it passes)" % int(every))
            sys.exit(RETRY_PENDING_RC)
        print("retry: attempt %d failed (code %d) — retrying in %ds" % (n, rc, int(every)))
        sys.stdout.flush()
        time.sleep(every)


def close_previous_session():
    """`start` while the previous session was never stopped (agent gave up): close it cleanly — restore device settings, stop the background log, write the report."""
    try:
        prev = Session.load(None)
    except QaError:
        return
    if prev.d.get("ended"):
        return
    try:
        close_session(prev, quiet=True)
        print("closed the previous unstopped session: %s" % prev.id)
    except Exception as e:  # old device unplugged / broken session — do not block the new one
        log_stop(prev)
        prev.d["ended"] = now_iso()
        prev.save()
        print("closing the previous session %s was incomplete: %s" % (prev.id, e))


def close_session(sess, quiet=False):
    dev = sess.device()
    fresh, rep = check_logs(sess, dev)
    if not quiet:
        print_issues(fresh, rep)
    ch = sess.d.get("changed") or {}
    try:
        if ch.get("network"):
            dev.network("on")
        if ch.get("rotation"):
            dev.rotate("auto")
        if isinstance(dev, Android) and "stayon" in ch:
            dev.stay_awake(False, restore=str(ch.get("stayon") or "0"))
        if dev.probe and (sess.d.get("probe") or {}).get("ok"):
            try:
                dev.probe.post("/timescale", value=1, timeout=5)
            except QaError:
                pass
            try:
                dev.probe.post("/stop", timeout=5)  # the probe stops listening; the next session (even --attach) uses a new token
            except QaError:
                pass
        for n in revert_env(sess, dev):
            print("environment cleanup: %s" % n)
        if sess.d.get("pseudoloc") and dev.probe:
            try:
                dev.probe.get("/pseudoloc", on=0, timeout=5)
            except QaError:
                pass
        if hasattr(dev, "remove_trigger"):
            dev.remove_trigger(sess.d["package"])
        if isinstance(dev, Android):
            dev.unforward_probe((sess.d.get("probe") or {}).get("port") or PROBE_PORT)
    except QaError as e:
        print("cleanup: %s" % e)
    log_stop(sess)
    sess.d["ended"] = now_iso()
    sess.save()
    try:
        flush_profile(sess, quiet=quiet)
    except Exception as e:  # a profile write error must not lose the report
        print("QA profile: could not write (%s)" % e)
    write_text(sess.path("report.md"), build_session_report(sess))


def _is_semver(v):
    return bool(re.match(r"^\d+\.\d+\.\d+", v or ""))


def _ver(v):
    return tuple(int(x) if x.isdigit() else 0 for x in re.split(r"[.\-]", v or "0"))


def read_manifest(root):
    return read_json(os.path.join(root, "Packages", "manifest.json"), {}) or {}


def find_probe_in_project(root):
    """Probe in the project: package (version declared in the manifest), old copy in Assets, EZG registry."""
    man = read_manifest(root)
    info = {"package": (man.get("dependencies") or {}).get(PROBE_PACKAGE), "legacy": None, "legacyVersion": None,
            "registry": any("com.ezg" in (r.get("scopes") or []) for r in (man.get("scopedRegistries") or []))}
    assets = os.path.join(root, "Assets")
    for dirpath, dirnames, filenames in os.walk(assets):
        dirnames[:] = [d for d in dirnames if not d.startswith(".")]
        if "EzgQaProbe.cs" in filenames:
            text = read_text(os.path.join(dirpath, "EzgQaProbe.cs"))
            if LEGACY_PROBE_MARK in text:
                m = re.search(r'VERSION\s*=\s*"([^"]+)"', text)
                info["legacy"] = os.path.relpath(dirpath, root)
                info["legacyVersion"] = m.group(1) if m else "?"
                break
    return info


def set_manifest_dependency(root, name, version):
    """Add/update one dependency, keeping the order of the other lines (a new line is inserted alphabetically)."""
    path = os.path.join(root, "Packages", "manifest.json")
    man = read_json(path, None)
    if not isinstance(man, dict):
        raise QaError("could not read %s" % path)
    eol = "\n" if read_text(path).endswith("\n") else ""  # Unity writes the manifest without a trailing newline — keep it that way
    deps = man.get("dependencies") or {}
    if name in deps:
        deps[name] = version
    else:
        items = list(deps.items())
        idx = next((i for i, (k, _) in enumerate(items) if k > name), len(items))
        items.insert(idx, (name, version))
        deps = dict(items)
    man["dependencies"] = deps
    with open(path, "w", encoding="utf-8", newline="\n") as f:
        f.write(json.dumps(man, ensure_ascii=False, indent=2) + eol)


def remove_legacy_probe(root, rel_dir):
    """Remove the old copy (v1.0/1.1) — the package replaces it. Custom non-default settings are kept in the override file."""
    notes = []
    d = os.path.join(root, rel_dir)
    cfg = read_json(os.path.join(d, "Resources", "EzgQaProbeConfig.json"), {}) or {}
    det = detect_cheat_config(root)
    custom = {k: cfg[k] for k in ("releaseGate", "cheatOn", "cheatOff") if cfg.get(k) and cfg[k] != det.get(k)}
    if cfg.get("autoCheat") is False:
        custom["autoCheat"] = False
    if custom:
        write_probe_override(root, custom)
        notes.append("kept the custom probe settings (%s) in %s" % (", ".join(sorted(custom)), PROBE_OVERRIDE))
    shutil.rmtree(d, ignore_errors=True)
    if os.path.isfile(d.rstrip(os.sep) + ".meta"):
        os.remove(d.rstrip(os.sep) + ".meta")
    notes.append("removed the old copied probe %s/ — replaced by package %s" % (rel_dir, PROBE_PACKAGE))
    return notes


def write_probe_override(root, values):
    path = os.path.join(root, PROBE_OVERRIDE)
    cur = read_json(path, {}) or {}
    cur.update(values)
    os.makedirs(os.path.dirname(path), exist_ok=True)
    write_json(path, cur)


def effective_probe_config(root):
    """Probe config used at runtime: GameSystems auto-detection (like ProbeAutoConfig in the package) + the override file if any."""
    det = detect_cheat_config(root)
    ov = read_json(os.path.join(root, PROBE_OVERRIDE), {}) or {}
    out = {"override": bool(ov)}
    for k in ("releaseGate", "cheatOn", "cheatOff"):
        v = ov.get(k) or det[k]
        out[k] = "" if v == "none" else v
    return out


def detect_cheat_config(root):
    """EZG template GameSystems: cheat flag (releaseGate), method enabling cheats via CheatGate (cheatOn), method disabling them (cheatOff)."""
    out = {"releaseGate": "", "cheatOn": "", "cheatOff": ""}
    for path in glob.glob(os.path.join(root, "Assets", "**", "GameSystems.cs"), recursive=True):
        text = read_text(path)
        if not re.search(r"public\s+static\s+bool\s+isCheat\b", text):
            continue
        ns = re.search(r"^\s*namespace\s+([\w.]+)", text, re.M)
        prefix = ("%s.GameSystems" % ns.group(1)) if ns else "GameSystems"
        out["releaseGate"] = prefix + ".isCheat"
        # Only the CheatGate variant (takes Action<bool>); the old TryEnableCheat() asks Remote Config and can turn cheats OFF.
        if re.search(r"public\s+static\s+void\s+TryEnableCheat\s*\(\s*(System\.)?Action\s*<\s*bool\s*>", text):
            out["cheatOn"] = prefix + ".TryEnableCheat"
        if re.search(r"public\s+static\s+void\s+DisableCheat\s*\(", text):
            out["cheatOff"] = prefix + ".DisableCheat"
        return out
    return out


def detect_gate(root):
    """Gate that opens the probe in release builds: the EZG template cheat flag if present (GameSystems.isCheat)."""
    return detect_cheat_config(root)["releaseGate"]


def cheat_gate_info(root):
    """com.ezg.cheat-gate in the project → (version | None, project code in GameConstant.CheatGateProject | None)."""
    man = read_json(os.path.join(root, "Packages", "manifest.json"), {}) or {}
    ver = (man.get("dependencies") or {}).get("com.ezg.cheat-gate")
    code = None
    if ver:
        for path in glob.glob(os.path.join(root, "Assets", "**", "GameConstant.cs"), recursive=True):
            m = re.search(r'CheatGateProject\s*=\s*"([^"]*)"', read_text(path))
            if m:
                code = m.group(1)
                break
    return ver, code


def cmd_probe_install(a):
    root = repo_root()
    notes = ensure_probe_in_project(root)
    if a.gate:
        write_probe_override(root, {"releaseGate": a.gate})
        notes.append("releaseGate=%s override written to %s" % (a.gate, PROBE_OVERRIDE))
    info = find_probe_in_project(root)
    for line in notes or ["package %s %s already in the manifest — unchanged" % (PROBE_PACKAGE, info["package"])]:
        print(line)
    cfg = effective_probe_config(root)
    cg_ver, cg_code = cheat_gate_info(root)
    print("config: releaseGate=%s · cheatOn=%s · cheatOff=%s" % (cfg["releaseGate"] or "-", cfg["cheatOn"] or "(sets releaseGate)",
                                                               cfg["cheatOff"] or "(sets releaseGate)"))
    print("release gate: %s" % (
        ("com.ezg.cheat-gate %s (code %s) — devices in the test list open automatically, no tapping needed" % (cg_ver, cg_code or "?")) if cg_ver
        else ("flag %s must be true (turn cheats on on the device)" % cfg["releaseGate"]) if cfg["releaseGate"]
        else "none — release builds are always closed, only development builds open"))


# ───────────────────────────────────────────────────────────── QA profile: profile init | sync | show

def _game_name(root):
    ps = project_settings(root)
    prof = project_profile(root)
    return ps.get("productName") or prof.get("displayName") or prof.get("projectName") or os.path.basename(root)


def refresh_profile_doc(root, qa=None, det=None, create=False, status=None):
    """Fill the <!-- auto:… --> regions of .claude/docs/QaProfile.md from qa + detection results; create it from the template if missing (create).
    Dev text outside the auto regions is kept. Returns the path or None."""
    qa = qa or qa_profile(root, refresh=True)
    doc_path = qa_path(root, "profileDoc")
    tpl_path = os.path.join(root, qaprofile.TEMPLATE_REL)
    doc = read_text(doc_path, "")
    if not doc:
        if not create:
            return None
        doc = read_text(tpl_path, "")
        if not doc:  # repo has not received Docs/QaProfile.template from Feature Hub → minimal skeleton; a later template update still keeps the dev text
            doc = qaprofile.MINIMAL_QA_DOC
            print("  ⚠ %s missing — using the minimal skeleton; update the template's AI set (agents-update) for the full one" % qaprofile.TEMPLATE_REL)
        doc = doc.replace("<Tên game>", _game_name(root))  # placeholder token of the template — keep it verbatim
    det = det or {}
    names, enum_file = qaprofile.feature_names(root, qa)
    classes = qaprofile.classify_features(root, qa, names, enum_file)
    sections = qaprofile.render_sections(root, qa, det, classes)
    doc = qaprofile.fill_auto_sections(doc, sections)
    cur = qaprofile.doc_status(doc)
    new_status = status or ("draft" if cur in ("", "template") else cur)
    doc = qaprofile.set_status(doc, new_status, qaprofile.today())
    write_text(doc_path, doc)
    return doc_path


def profile_init(root, force=False, dry_run=False):
    """Auto-detect conventions → write the qa block (only missing keys, unless --force) + QaProfile.md + charters/README. Returns (patch, det, lines)."""
    det = qaprofile.detect_conventions(root)
    prof = project_profile(root)
    existing = prof.get("qa") if isinstance(prof.get("qa"), dict) else {}
    ex_conv = existing.get("conventions") or {}
    conv = {}
    lines = []
    for k in QA_DEFAULTS["conventions"]:
        info = det.get(k) or {"value": QA_DEFAULTS["conventions"][k], "source": "default", "evidence": ""}
        if k in ex_conv and not force:
            lines.append("  %-22s %-40s dev      (kept: %s)" % (k, clip(json.dumps(ex_conv[k], ensure_ascii=False), 40), info.get("evidence", "")))
            continue
        if info["source"] == "auto":
            conv[k] = info["value"]
        lines.append("  %-22s %-40s %-8s %s" % (k, clip(json.dumps(info["value"], ensure_ascii=False), 40), info["source"], info.get("evidence", "")))
    patch = {"schemaVersion": qaprofile.QA_SCHEMA_VERSION}
    if conv:
        patch["conventions"] = conv
    st = existing.get("status")
    patch["status"] = st if (st in ("draft", "reviewed") and not force) else "draft"
    lv = existing.get("liveness") or {}
    if "deadFeaturePatterns" not in lv or force:
        patch["liveness"] = {"deadFeaturePatterns": det["_deadFeaturePatterns"]["value"]}
    feats = det.get("_features", {}).get("value") or []
    dead = [n for n in feats if any(re.search(p, n) for p in det["_deadFeaturePatterns"]["value"])]
    lines.append("  features: %d enum values · %d match dead patterns %s · code source: %s" % (
        len(feats), len(dead), det["_deadFeaturePatterns"]["value"], det.get("_sourceRoot", {}).get("value", "")))
    if dry_run:
        return patch, det, lines
    qa_new = qaprofile.save_qa_patch(root, patch)
    _QA_CACHE.pop(root, None)
    qa = qa_profile(root, refresh=True)
    doc = refresh_profile_doc(root, qa, det, create=True, status=None if qaprofile.doc_status(read_text(qa_path(root, "profileDoc"))) in ("draft", "reviewed") else "draft")
    cdir = qa_path(root, "chartersDir")
    readme = os.path.join(cdir, "README.md")
    if not os.path.isfile(readme):
        write_text(readme, CHARTERS_README)
        lines.append("  created %s" % os.path.relpath(readme, root))
    lines.append("  wrote qa (status=%s) → %s" % (qa_new.get("status"), qaprofile.PROFILE_REL))
    if doc:
        lines.append("  QaProfile.md → %s (Status: %s — the dev reviews it, then sets `reviewed`)" % (os.path.relpath(doc, root), qaprofile.doc_status(read_text(doc))))
    return patch, det, lines


CHARTERS_README = """# Project QA charters

Each `<feature>.md` file is a **test charter** of the `/auto-test-device` skill: oracles (expected values with their source) and
a list of cases with **ids** of the form `<FEATURE>-NN` (e.g. `SHOP-03`). The agent records each case's result with
`qadevice.py note --case SHOP-03 --result pass|fail|blocked|skip`, `coverage` compares cases run / not run,
`charter diff` lists the cases still missing in the session.

- New charter: `python3 .claude/skills/auto-test-device/scripts/qadevice.py charter new "<feature name>"`.
- Write only **test cases + oracles**, never per-session results (those live in `.git/auto-test-device/sessions/`).
- Commit with the feature; devs edit freely (add cases, update oracles when the GDD changes). Keep existing case ids — add new numbers.
"""


def profile_sync(root, dry_run=False):
    """Push the conventions down to the two build-side files: EzgQaProbeConfig.json (5 probe keys) + EZGAutoTestSettings.json (liveness block).
    Merge: other keys are kept. Returns the list of change lines."""
    qa = qa_profile(root, refresh=True)
    conv = qa.get("conventions") or {}
    changes = []
    # probe override
    p_path = qa_path(root, "probeOverride")
    cur = read_json(p_path, {}) or {}
    new = dict(cur)
    for k in qaprofile.PROBE_OVERRIDE_KEYS:
        v = conv.get(k)
        if v in ("", None, []):
            if k in new:
                del new[k]
                changes.append("probe: removed %s" % k)
        elif new.get(k) != v:
            changes.append("probe: %s %s → %s" % (k, json.dumps(cur.get(k), ensure_ascii=False) if k in cur else "(none)", json.dumps(v, ensure_ascii=False)))
            new[k] = v
    if new != cur and not dry_run:
        os.makedirs(os.path.dirname(p_path), exist_ok=True)
        with open(p_path, "w", encoding="utf-8", newline="\n") as f:
            f.write(json.dumps(new, ensure_ascii=False, indent=2) + "\n")
    # autotest settings
    a_path = qa_path(root, "autotestSettings")
    cur_a = read_json(a_path, {}) or {}
    lv = qa.get("liveness") or {}
    want = {"enabled": (cur_a.get("liveness") or {}).get("enabled", True),
            "deadFeaturePatterns": list(lv.get("deadFeaturePatterns") or []),
            "forceLive": list(lv.get("forceLive") or []), "forceDead": list(lv.get("forceDead") or []),
            "observedLive": sorted((lv.get("observed") or {}).keys())}
    if (cur_a.get("liveness") or {}) != want:
        for k, v in want.items():
            if (cur_a.get("liveness") or {}).get(k) != v:
                changes.append("autotest: liveness.%s → %s" % (k, clip(json.dumps(v, ensure_ascii=False), 80)))
        if not dry_run:
            new_a = dict(cur_a)
            new_a["liveness"] = want
            os.makedirs(os.path.dirname(a_path), exist_ok=True)
            with open(a_path, "w", encoding="utf-8", newline="\n") as f:
                f.write(json.dumps(new_a, ensure_ascii=False, indent=2) + "\n")
    return changes


def cmd_profile(a):
    root = repo_root()
    if a.op == "init":
        patch, det, lines = profile_init(root, force=a.force, dry_run=a.dry_run)
        print("profile init%s — detected conventions (source: auto = detected from code · dev = declared in the profile · default = template default):" % (
            " (dry-run, nothing written)" if a.dry_run else ""))
        for l in lines:
            print(l)
        if a.dry_run:
            print("patch that would be written to qa: %s" % json.dumps(patch, ensure_ascii=False))
        else:
            print("→ `profile sync` to push it to EzgQaProbeConfig.json + EZGAutoTestSettings.json; `doctor` to check.")
        return
    if a.op == "sync":
        changes = profile_sync(root, dry_run=a.dry_run)
        for c in changes or ["nothing changed — both build-side files already match the profile"]:
            print(("  " if changes else "") + c)
        if changes and not a.dry_run:
            print("wrote %s + %s (commit them with the repo; Unity creates the .meta on import)" % (
                os.path.relpath(qa_path(root, "probeOverride"), root), os.path.relpath(qa_path(root, "autotestSettings"), root)))
        return
    qa, prov = qaprofile.load_qa_profile(root, with_provenance=True)
    if a.json:
        print(json.dumps({"qa": qa, "provenance": prov}, ensure_ascii=False, indent=1))
        return
    print("qa (schema %s · status %s%s):" % (qa.get("schemaVersion"), qa.get("status"), " · NO qa block — run `profile init`" if prov.get("_block") == "missing" else ""))
    for k, v in (qa.get("conventions") or {}).items():
        print("  conventions.%-22s %-44s %s" % (k, clip(json.dumps(v, ensure_ascii=False), 44), prov.get("conventions." + k, "?")))
    lv = qa.get("liveness") or {}
    print("  liveness: dead patterns %s · forceLive %d · forceDead %d · observed %d" % (lv.get("deadFeaturePatterns"), len(lv.get("forceLive") or []),
                                                                             len(lv.get("forceDead") or []), len(lv.get("observed") or {})))
    ver = qa.get("verified") or {}
    print("  verified: cheat %d · state path %d · noise %d rules" % (len(ver.get("cheats") or {}), len(ver.get("statePaths") or {}), len(qa.get("noise") or [])))
    print("  environments: %s" % json.dumps(qa.get("environments"), ensure_ascii=False))
    for k, v in (qa.get("paths") or {}).items():
        print("  paths.%-16s %s%s" % (k, v, "" if os.path.exists(os.path.join(root, v)) else "  (missing)"))


# ───────────────────────────────────────────────────────────── doctor (extended) + selfcheck

def autotest_info(root):
    """(com.ezg.autotest version | None, source 'manifest'|'embedded'|'')."""
    man = read_manifest(root)
    ver = (man.get("dependencies") or {}).get(AUTOTEST_PACKAGE)
    if ver:
        emb = read_json(os.path.join(root, "Packages", AUTOTEST_PACKAGE, "package.json"), {}) or {}
        return (emb.get("version") or ver), ("embedded" if emb else "manifest")
    emb = read_json(os.path.join(root, "Packages", AUTOTEST_PACKAGE, "package.json"), {}) or {}
    return (emb.get("version"), "embedded") if emb else (None, "")


def doctor_qa(root, strict=False):
    """QA-profile part of `doctor`: returns (printed lines, required problems, warnings)."""
    lines, problems, warns = [], [], []
    qa, prov = qaprofile.load_qa_profile(root, with_provenance=True)
    conv = qa.get("conventions") or {}
    if prov.get("_block") == "missing":
        problems.append("no qa block in %s — run `profile init`" % qaprofile.PROFILE_REL)
        lines.append("qa:        MISSING (every convention is the template default) → `profile init`")
    else:
        lines.append("qa:        schema %s · status %s · %d keys declared by the dev" % (qa.get("schemaVersion"), qa.get("status"),
                                                                         sum(1 for k, v in prov.items() if v == "dev" and k.startswith("conventions."))))
        if qa.get("status") == "template":
            warns.append("qa.status is still `template` — `profile init` to auto-detect")
    classes = set()
    for p in qaprofile.iter_cs(root):
        t = read_text(p)
        for m in re.finditer(r"\b(?:class|struct|enum)\s+(\w+)", t):
            classes.add(m.group(1))
    names, enum_file = qaprofile.feature_names(root, qa)
    if names:
        cls = qaprofile.classify_features(root, qa, names, enum_file)
        dead = sum(1 for c in cls.values() if c["state"] == "dead")
        unref = sum(1 for c in cls.values() if c["state"] == "unreferenced")
        lines.append("features:  %s — %d values · %d dead · %d not referenced in code · %d observed (%s)" % (
            conv.get("featuresEnum"), len(names), dead, unref, len((qa.get("liveness") or {}).get("observed") or {}), os.path.relpath(enum_file, root)))
    else:
        problems.append("enum %s not found in code — fix qa.conventions.featuresEnum" % conv.get("featuresEnum"))
        lines.append("features:  enum %s NOT FOUND" % conv.get("featuresEnum"))
    bases = [b for b in conv.get("screenBaseTypes") or [] if b in classes]
    if bases:
        lines.append("screens:   base classes found in code: %s" % ", ".join(bases))
    else:
        problems.append("none of the screen base classes in qa.conventions.screenBaseTypes exists in code")
        lines.append("screens:   NO base class exists (%s)" % conv.get("screenBaseTypes"))
    for k in ("uiManagerType", "cheatTypeName", "playerDataFacade", "timeProvider", "trackingEventNameType"):
        v = conv.get(k)
        if v and v not in classes:
            warns.append("%s=%s not found in code" % (k, v))
    csv_files = glob.glob(os.path.join(root, conv.get("csvConfigGlob") or ""), recursive=True) if conv.get("csvConfigGlob") else []
    lines.append("csv:       %d files match %s" % (len(csv_files), conv.get("csvConfigGlob")))
    if not csv_files:
        warns.append("csvConfigGlob matches no file — the `csv` oracle is unavailable")
    # build-side files in sync?
    ov = read_json(qa_path(root, "probeOverride"), {}) or {}
    drift = [k for k in qaprofile.PROBE_OVERRIDE_KEYS if conv.get(k) not in ("", None, []) and ov.get(k) != conv.get(k)]
    at = (read_json(qa_path(root, "autotestSettings"), {}) or {}).get("liveness") or {}
    lv = qa.get("liveness") or {}
    at_drift = at.get("deadFeaturePatterns") != list(lv.get("deadFeaturePatterns") or []) or sorted(at.get("observedLive") or []) != sorted((lv.get("observed") or {}).keys())
    lines.append("sync:      probe override %s · autotest liveness %s" % ("in sync" if not drift else "DRIFT (%s)" % ", ".join(drift), "in sync" if not at_drift else "DRIFT"))
    if drift or at_drift:
        warns.append("build-side files drift from the profile — run `profile sync`")
    av, asrc = autotest_info(root)
    if av:
        old = _is_semver(av) and _ver(av) < _ver(AUTOTEST_MIN_VERSION)
        lines.append("autotest:  %s %s (%s)%s" % (AUTOTEST_PACKAGE, av, asrc, " ⚠ < %s: dead features not filtered, noisy report" % AUTOTEST_MIN_VERSION if old else ""))
        if old:
            warns.append("com.ezg.autotest %s < %s" % (av, AUTOTEST_MIN_VERSION))
        rep = qaprofile.latest_report(root, qa)
        lines.append("           latest report: %s" % (os.path.relpath(rep, root) if rep else "none yet — run Ezg > Auto test system, then `autotest-import`"))
    else:
        lines.append("autotest:  no %s — autotest-based Δ-skip unavailable" % AUTOTEST_PACKAGE)
    doc = qa_path(root, "profileDoc")
    st = qaprofile.doc_status(read_text(doc, ""))
    lines.append("docs:      %s %s · charters: %s" % (os.path.relpath(doc, root), ("Status " + st) if st else "MISSING", (
        "%d files" % len(qaprofile.list_charters(root, qa))) if os.path.isdir(qa_path(root, "chartersDir")) else "NO folder"))
    if not st:
        problems.append("missing %s — `profile init`" % os.path.relpath(doc, root))
    if not os.path.isdir(qa_path(root, "chartersDir")):
        problems.append("no charter folder %s — `profile init`" % os.path.relpath(qa_path(root, "chartersDir"), root))
    return lines, problems, warns


def ensure_autotest_in_project(root):
    """Project lacks com.ezg.autotest (Editor-only, not in the build) or has an older one than the skill needs → add/bump the manifest line
    (only when the manifest already has the EZG scoped registry — without it Unity cannot download it, so only report). Returns status lines."""
    notes = []
    info = find_probe_in_project(root)
    ver, src = autotest_info(root)
    if src == "embedded":
        return notes  # embedded copy in Packages/ — leave it alone
    if ver is None or (_is_semver(ver) and _ver(ver) < _ver(AUTOTEST_MIN_VERSION)):
        if not info["registry"]:
            notes.append("⚠ %s not added: manifest has no EZG scoped registry — install the registry via Feature Hub first" % AUTOTEST_PACKAGE)
            return notes
        set_manifest_dependency(root, AUTOTEST_PACKAGE, AUTOTEST_MIN_VERSION)
        notes.append("%s %s %s in Packages/manifest.json (Editor-only — not in the build) — commit the manifest (+ packages-lock.json)" % (
            "bumped" if ver else "added", AUTOTEST_PACKAGE, AUTOTEST_MIN_VERSION))
    return notes


def api_retry_hook_state(root):
    """/auto-clear StopFailure hook on this machine: installed · not_installed · unsupported · missing (no skill) · error."""
    script = auto_clear_script()
    if not script:
        return "missing", "project has no /auto-clear skill (install it from Feature Hub — AI item Skills/auto-clear)"
    rc, out, err = run(auto_clear_cmd(script, "status"), timeout=30, cwd=root)
    for line in (out or "").splitlines():
        t = line.strip()
        if t.startswith("HOOK installed"):
            return "installed", t
        if t.startswith("HOOK not-installed") or t.startswith("HOOK broken"):
            return "not_installed", t
        if t.startswith("UNSUPPORTED"):
            return "unsupported", t
    tail = [l.strip() for l in ((err or "") + "\n" + (out or "")).splitlines() if l.strip()]
    return "error", tail[-1] if tail else "auto-clear status exited with code %s" % rc


def doctor_report(root, strict=False, probe_hook=True):
    """The whole `doctor` result as a dict: machine tools, packages, BugHub, QA profile, API-error retry hook, and `actions` —
    the remaining to-dos in order, with who does them (`tool` = qadevice itself in `doctor --fix`/`start`; `model` = the agent calls
    MCP/skills; `dev` = a human). No network access besides BugHub /form."""
    adb = find_adb()
    rep = {"version": VERSION, "python": sys.version.split()[0], "platform": sys.platform,
           "tools": {"adb": adb or "", "emulator": find_emulator(adb) or "", "xcrun": has_xcrun(), "idb": find_idb() or "",
                     "pillow": Image is not None},
           "problems": [], "warns": [], "actions": []}
    ps = project_settings(root)
    rep["project"] = {"productName": ps.get("productName"), "ids": ps["ids"], "unityVersion": ps.get("unityVersion"),
                      "root": root}
    man = read_manifest(root)
    deps = man.get("dependencies") or {}
    info = find_probe_in_project(root)
    av, asrc = autotest_info(root)
    cg_ver, cg_code = cheat_gate_info(root)
    fh_ver = deps.get(FEATUREHUB_PACKAGE)
    rep["packages"] = {
        PROBE_PACKAGE: {"version": info["package"], "min": PROBE_MIN_VERSION, "legacy": info["legacy"], "legacyVersion": info["legacyVersion"]},
        AUTOTEST_PACKAGE: {"version": av, "min": AUTOTEST_MIN_VERSION, "source": asrc},
        CHEATGATE_PACKAGE: {"version": cg_ver, "code": cg_code},
        BUGLOGGER_PACKAGE: {"present": buglogger_package_present(root)},
        FEATUREHUB_PACKAGE: {"version": fh_ver, "min": FEATUREHUB_MIN_VERSION},
        "registry": info["registry"],
    }
    rep["probeConfig"] = effective_probe_config(root)
    rep["bughub"] = bughub_status(root)
    qa_lines, qa_problems, qa_warns = doctor_qa(root, strict=strict)
    rep["qaLines"] = qa_lines
    qa, prov = qaprofile.load_qa_profile(root, with_provenance=True)
    rep["qa"] = {"block": prov.get("_block") != "missing", "status": qa.get("status"),
                 "doc": os.path.relpath(qa_path(root, "profileDoc"), root),
                 "docStatus": qaprofile.doc_status(read_text(qa_path(root, "profileDoc"), "")),
                 "chartersDir": os.path.relpath(qa_path(root, "chartersDir"), root),
                 "charters": len(qaprofile.list_charters(root, qa)) if os.path.isdir(qa_path(root, "chartersDir")) else 0}
    if probe_hook:
        hs, hl = api_retry_hook_state(root)
    else:
        hs, hl = "skipped", ""
    rep["apiRetryHook"] = {"state": hs, "line": hl}
    rep["featureHub"] = {"manifest": os.path.join("Packages", "manifest.json"), "installRecord": INSTALL_RECORD_REL,
                         "installRecordExists": os.path.isfile(os.path.join(root, INSTALL_RECORD_REL)), "items": QA_HUB_ITEMS}
    rep["mcp"] = {"ezg": {"url": MCP_EZG_URL, "why": "Feature Hub (check_project_updates/get_install_steps), test device list (test_device_*)"},
                  "bughub": {"url": rep["bughub"]["endpoint"].rstrip("/") + "/mcp", "why": "open bugs (bughub_list), /bughub-setup, permissions (bughub_whoami)"},
                  "unity": {"url": "", "why": "installs Feature Hub items (unity_execute_code), compile-check; without it → UPM goes straight into the manifest, AI items are installed later"}}
    problems, warns, actions = rep["problems"], rep["warns"], rep["actions"]

    def act(aid, who, how, why):
        actions.append({"id": aid, "who": who, "how": how, "why": why})

    # --- machine tools
    if not adb:
        warns.append("no adb — Android devices/AVDs unavailable (install Android platform-tools or the Unity Android module)")
    if Image is None:
        warns.append("no Pillow — `start` installs it; thumbnails/grid/frame diffs need it")
    # --- Feature Hub: always checked (idempotent) — done by the model
    act("feature-hub-update", "model",
        "MCP ezg check_project_updates(manifest_json=<%s>, install_record_json=<%s>) → every QA_HUB_ITEMS item that is update_available/not installed → "
        "get_install_steps → unity_execute_code (this project's Editor, featurehub ≥ %s); no Editor → UPM: `doctor --fix` adds them to the manifest, "
        "AI items go into the report" % (rep["featureHub"]["manifest"], INSTALL_RECORD_REL, FEATUREHUB_MIN_VERSION),
        "skill/probe/autotest/bughub-setup must be the latest versions before testing")
    if fh_ver and _is_semver(fh_ver) and _ver(fh_ver) < _ver(FEATUREHUB_MIN_VERSION):
        warns.append("%s %s < %s — the ezg MCP install snippets need a newer version (upgrade it in Package Manager)" % (FEATUREHUB_PACKAGE, fh_ver, FEATUREHUB_MIN_VERSION))
    if not fh_ver:
        warns.append("no %s in the manifest — installing Feature Hub items through the Editor will not work; add the package via Package Manager (EZG registry)" % FEATUREHUB_PACKAGE)
    if not info["registry"]:
        warns.append("manifest has no EZG scoped registry (scope com.ezg) — Unity cannot download com.ezg.*; install the registry via Feature Hub")
    # --- package
    pv = info["package"]
    if pv is None or (_is_semver(pv) and _ver(pv) < _ver(PROBE_MIN_VERSION)):
        problems.append("%s %s" % (PROBE_PACKAGE, "not in the manifest" if pv is None else "%s < %s" % (pv, PROBE_MIN_VERSION)))
        act("probe-package", "tool", "`doctor --fix` or `start` adds/bumps it in Packages/manifest.json", "deep testing (ui/state/call/cheat) needs the probe in the build")
    if info["legacy"]:
        warns.append("old copied probe still at %s (v%s) — `start`/`probe-install` will remove it" % (info["legacy"], info["legacyVersion"]))
    if av is None or (_is_semver(av) and _ver(av) < _ver(AUTOTEST_MIN_VERSION)):
        warns.append("%s %s" % (AUTOTEST_PACKAGE, "missing — autotest-based Δ-skip unavailable" if av is None else "%s < %s: dead features not filtered" % (av, AUTOTEST_MIN_VERSION)))
        act("autotest-package", "tool", "`doctor --fix` adds/bumps it (Editor-only); or install it via Feature Hub", "autotest-import needs report schema 0.2")
    if not cg_ver:
        warns.append("no %s — release builds open the probe only when the game has cheats on (releaseGate)" % CHEATGATE_PACKAGE)
        act("cheat-gate", "dev", "install %s via Feature Hub + call CheatGate.Initialize at boot (template: GameSystems.InitCheatGate)" % CHEATGATE_PACKAGE,
            "test devices in the list get cheats + the probe on release builds automatically")
    elif not cg_code:
        warns.append("%s present but no project code found (GameConstant.CheatGateProject) — the probe does not know which code to ask the server for" % CHEATGATE_PACKAGE)
    # --- BugHub
    if not rep["bughub"]["ok"]:
        warns.append("BugHub not ready (`start` will exit with code 4): " + "; ".join(rep["bughub"]["problems"]))
        act("bughub-setup", "model", "run the /bughub-setup skill now, in this session (Skill tool) — ask the dev for the Discord forum ID/project code ONLY if the project is not registered; then call `start` again",
            "`start` exits with code 4 while BugHub is not ready")
    # --- QA profile
    problems += qa_problems
    warns += qa_warns
    if any("profile init" in p for p in qa_problems) or rep["qa"]["status"] == "template":
        act("profile-init", "tool", "`doctor --fix` or `start` runs `profile init` (auto-detects conventions, QaProfile.md draft)", "ui/state/cheat/coverage need the right conventions")
    if any("profile sync" in w for w in qa_warns):
        act("profile-sync", "tool", "`doctor --fix` or `profile sync`", "probe/autotest read the same conventions as the profile")
    # --- API-error retry hook
    if hs == "not_installed":
        act("api-retry-hook", "tool", "`start` runs `auto-clear install` (idempotent), then retry-arm", "a turn killed by an API error is resumed automatically after 30 s")
    elif hs == "missing":
        warns.append(hl)
        act("api-retry-hook", "model", "install AI item Skills/auto-clear via Feature Hub (feature-hub-update step)", "without the skill, API errors must be resumed by hand")
    elif hs == "unsupported":
        warns.append("API-error retry: " + hl)
    # --- MCP: Python cannot see Claude Code's MCPs — the model checks them with ToolSearch; this is the install command when one is missing
    act("mcp-check", "model", "ToolSearch `ezg` + `bughub`; for each missing server → print for the dev: `claude mcp add --transport http --scope user <name> <url>` "
        "then /mcp → Authenticate (Google @easygoing.vn), reopen the session. Never run that command yourself", "every Feature Hub / test device / BugHub step goes through MCP")
    rep["ok"] = not problems
    return rep


def doctor_fix(root, rep, dry_run=False):
    """The `doctor --fix` part: what the tool can do itself, idempotent — profile init/sync, probe/autotest packages. Returns status lines."""
    done = []
    ids = {a["id"] for a in rep["actions"]}
    if "profile-init" in ids:
        if dry_run:
            done.append("(dry-run) profile init")
        else:
            _, _, lines = profile_init(root)
            done.append("profile init: %d convention lines → %s + QaProfile.md (draft)" % (len(lines), qaprofile.PROFILE_REL))
            qa_profile(root, refresh=True)
    if "probe-package" in ids:
        done.extend(["(dry-run) " + PROBE_PACKAGE] if dry_run else ensure_probe_in_project(root))
    if "autotest-package" in ids:
        done.extend(["(dry-run) " + AUTOTEST_PACKAGE] if dry_run else ensure_autotest_in_project(root))
    if "profile-sync" in ids or "profile-init" in ids:
        ch = profile_sync(root, dry_run=dry_run)
        done.extend(("(dry-run) " if dry_run else "") + c for c in ch)
    return done


def print_doctor(rep):
    t = rep["tools"]
    print("qadevice %s · python %s · %s" % (rep["version"], rep["python"], rep["platform"]))
    print("adb:       %s" % (t["adb"] or "MISSING — install Android platform-tools or the Unity Android module"))
    print("emulator:  %s" % (t["emulator"] or "missing (only needed to auto-boot an AVD)"))
    print("xcrun:     %s" % ("yes" if t["xcrun"] else "no (macOS only — iOS Simulator/iPhone)"))
    print("idb:       %s" % (t["idb"] or "no — the Simulator taps via the probe (brew install facebook/fb/idb-companion && pip3 install fb-idb)"))
    print("Pillow:    %s" % ("yes" if t["pillow"] else "NO — `start` installs it (`%s -m pip install --user pillow`)" % ("py" if IS_WIN else "python3")))
    pj = rep["project"]
    print("project:   %s · %s · Unity %s" % (pj.get("productName"), pj["ids"], pj.get("unityVersion")))
    pk = rep["packages"]
    pr = pk[PROBE_PACKAGE]
    if pr["version"]:
        old = _is_semver(pr["version"]) and _ver(pr["version"]) < _ver(PROBE_MIN_VERSION)
        print("probe:     package %s %s%s" % (PROBE_PACKAGE, pr["version"], " ⚠ < %s" % PROBE_MIN_VERSION if old else ""))
        print("           features by version: %s" % " · ".join("%s ≥ %s" % kv for kv in sorted(PROBE_FEATURES.items())))
    else:
        print("probe:     no package %s in Packages/manifest.json (`start`/`doctor --fix` adds it)" % PROBE_PACKAGE)
    if pr["legacy"]:
        print("           ⚠ old copy still at %s (v%s) — `start`/`probe-install` will remove it" % (pr["legacy"], pr["legacyVersion"]))
    if not pk["registry"]:
        print("           ⚠ manifest has no EZG scoped registry (scope com.ezg) — install the registry via Feature Hub first")
    cfg = rep["probeConfig"]
    print("config:    releaseGate=%s · cheatOn=%s · cheatOff=%s%s" % (
        cfg["releaseGate"] or "-", cfg["cheatOn"] or "-", cfg["cheatOff"] or "-", " (override in %s)" % PROBE_OVERRIDE if cfg["override"] else ""))
    cg = pk[CHEATGATE_PACKAGE]
    print("cheat:     %s" % (("%s %s · code %s — the probe opens the gate for devices in the test list" % (CHEATGATE_PACKAGE, cg["version"], cg["code"] or "?"))
                             if cg["version"] else "no %s — the release gate follows the game's cheat flag (releaseGate)" % CHEATGATE_PACKAGE))
    fh = pk[FEATUREHUB_PACKAGE]
    print("featurehub: %s · buglogger %s · install-record %s" % (
        ("%s %s" % (FEATUREHUB_PACKAGE, fh["version"])) if fh["version"] else "NO %s" % FEATUREHUB_PACKAGE,
        "yes" if pk[BUGLOGGER_PACKAGE]["present"] else "NO", "yes" if rep["featureHub"]["installRecordExists"] else "none"))
    bh = rep["bughub"]
    print("bughub:    %s · code %s · %s" % ("OK" if bh["ok"] else "NOT READY", bh["code"] or "?", bh["endpoint"]))
    for p_ in bh["problems"]:
        print("           ✗ " + p_)
    for l in rep["qaLines"]:
        print(l)
    hk = rep["apiRetryHook"]
    print("api-retry: %s%s" % (hk["state"], " — " + hk["line"] if hk["line"] else ""))
    print("state:     %s" % state_root())
    for w in rep["warns"]:
        print("  ⚠ " + w)
    for p_ in rep["problems"]:
        print("  ✗ " + p_)
    if rep["actions"]:
        print("remaining tasks (in order · who does it):")
        for a_ in rep["actions"]:
            print("  → [%s] %s: %s" % (a_["who"], a_["id"], a_["how"]))


def cmd_doctor(a):
    root = repo_root()
    rep = doctor_report(root, strict=a.strict)
    fixed = []
    if a.fix:
        fixed = doctor_fix(root, rep, dry_run=a.dry_run)
        rep = doctor_report(root, strict=a.strict, probe_hook=False)  # re-read after fixing
        rep["fixed"] = fixed
    if a.json:
        print(json.dumps(rep, ensure_ascii=False, indent=1, default=str))
    else:
        print_doctor(rep)
        if fixed:
            print("doctor --fix did:")
            for f in fixed:
                print("  " + f)
    if a.strict and rep["problems"]:
        print("doctor --strict: %d required items not met" % len(rep["problems"]))
        sys.exit(1)


# ───────────────────────────────────────────────────────────── plan: split "the whole game" into charters per system

# Default risk order by template bucket (money/IAP first, then gameplay, meta, social, onboarding, system last) —
# unknown buckets go after Gameplay. Features matching HIGH_RISK_RE jump to the top of their group.
RISK_BUCKET_ORDER = ["Monetization", "Gameplay", "Events", "Meta", "Social", "Onboarding", "System"]
HIGH_RISK_RE = re.compile(r"shop|store|iap|purchase|pack|offer|pass|gacha|boost|vip|reward|save|account|offline", re.I)
FEATURE_MARKERS = ("Scripts", "Resources", "CsvConfig", "Visuals", "Prefabs")


def features_root(root):
    """Features folder: the profile's `featuresRoot` if set, else <sourceRoot>/Features, else Assets/_Project/Features."""
    prof = project_profile(root)
    cands = [prof.get("featuresRoot") or "", os.path.join(os.path.relpath(qaprofile.source_roots(root), root), "Features"),
             os.path.join("Assets", "_Project", "Features")]
    for c in cands:
        if c and os.path.isdir(os.path.join(root, c)):
            return os.path.join(root, c)
    return ""


def _is_feature_dir(path):
    try:
        names = set(os.listdir(path))
    except OSError:
        return False
    return any(m in names for m in FEATURE_MARKERS) or any(n.endswith(".cs") for n in names)


def feature_buckets(root):
    """→ {bucket: [feature folder]} from <Features>/<Bucket>/<Feature>/ (template layout). Old layout (feature directly
    under Features/, or an odd folder such as `_Gameplay`) → feature under a bucket named after itself / "(root)". Skips `_Shared`."""
    froot = features_root(root)
    out = {}
    if not froot:
        return out
    for b in sorted(os.listdir(froot)):
        bp = os.path.join(froot, b)
        if not os.path.isdir(bp) or b.startswith(".") or b.lower() in ("_shared", "shared"):
            continue
        subs = [d for d in sorted(os.listdir(bp)) if os.path.isdir(os.path.join(bp, d)) and not d.startswith(".")]
        feats = [d for d in subs if _is_feature_dir(os.path.join(bp, d)) and d.lower() not in ("_shared", "shared")]
        if feats and (len(feats) >= 2 or not _is_feature_dir(bp)):
            out[b] = feats  # real bucket (≥2 child features, or not a feature itself)
        else:
            out.setdefault(b.lstrip("_") or b, []).append(b)  # it is a feature itself (old layout / single-feature bucket)
    return out


def screen_folder_index(root, names, buckets):
    """Screen → feature folder by where code REFERENCES the enum (`Features.<Name>`): scans .cs under Features/, each name takes the
    folder referencing it most, skipping `_Shared` (UIManager/EventName touch every screen). Used for screens matching no folder name
    (HomeScreen → Meta/HomeScene, Tut1Step1… → Onboarding/Tutorial, StaffDetail → Meta/Employees…)."""
    froot = features_root(root)
    if not froot or not names:
        return {}
    folder_of = {}
    for b, fs in buckets.items():
        for f in fs:
            folder_of[(b, f)] = f
    want = set(names)
    counts = {}
    pat = re.compile(r"\bFeatures\.(\w+)\b")
    for path in qaprofile.iter_cs(root, froot):
        rel = os.path.relpath(path, froot).split(os.sep)
        if not rel or rel[0].lower() in ("_shared", "shared"):
            continue
        key = (rel[0], rel[1]) if len(rel) > 2 and (rel[0], rel[1]) in folder_of else None
        if key is None:
            # old layout / single-feature bucket: the first folder is the feature
            key = next(((b, f) for (b, f) in folder_of if f == rel[0]), None)
        if key is None:
            continue
        for m in pat.finditer(read_text(path)):
            n = m.group(1)
            if n in want:
                counts.setdefault(n, {}).setdefault(key, 0)
                counts[n][key] += 1
    return {n: max(ks.items(), key=lambda kv: kv[1])[0] for n, ks in counts.items()}


def _bucket_rank(bucket):
    b = bucket.lstrip("_").lower()
    for i, name in enumerate(RISK_BUCKET_ORDER):
        if b == name.lower():
            return i
    return RISK_BUCKET_ORDER.index("Gameplay") + 0.5


def _norm(s):
    return re.sub(r"[^a-z0-9]", "", (s or "").lower())


def match_folder(name, folders, ref=None):
    """Screen enum name → feature folder: exact name (ignoring accents/spaces) → singular/plural → folder whose code references it (`ref`
    = (bucket, feature) from screen_folder_index) → match_feature heuristic (prefix, Popup/Screen suffix dropped).
    Exact matches win, so 'RewardPopup' does not land in 'RewardAds'."""
    by = {}
    for f in folders:
        by.setdefault(_norm(f), f)
    n = _norm(name)
    if n in by:
        return by[n]
    for k, f in by.items():
        if k.rstrip("s") == n.rstrip("s"):
            return f
    if ref and ref[1] in folders:
        return ref[1]
    return qaprofile.match_feature(name, folders)


def group_plan(classes, buckets, charters=(), ref_index=None):
    """Map screen enums (classify_features) to feature folders → groups by bucket/feature, sorted by risk.
    Match order: folder name (exact/plural) → where code references the enum (`ref_index`, from screen_folder_index) → prefix
    heuristic. → list of dicts {bucket, feature, screens:[{name,state,why}], charter, risk}. `dead` screens dropped; no match → 'Khác'. The data values 'Khác' (= other) and risk 'cao'/'thường' (= high/normal) are kept verbatim."""
    folder_names = [f for fs in buckets.values() for f in fs]
    folder_bucket = {f: b for b, fs in buckets.items() for f in fs}
    ref_index = ref_index or {}
    have = {c["id"]: c for c in charters}
    groups = {}
    for b, fs in buckets.items():
        for f in fs:
            groups[(b, f)] = {"bucket": b, "feature": f, "screens": [], "charter": None}
    for name, c in classes.items():
        if c["state"] == "dead":
            continue
        f = match_folder(name, folder_names, ref_index.get(name))
        key = (folder_bucket[f], f) if f else ("Khác", "Khác")
        groups.setdefault(key, {"bucket": key[0], "feature": key[1], "screens": [], "charter": None})
        groups[key]["screens"].append({"name": name, "state": c["state"], "why": c.get("why", "")})
    out = []
    for (b, f), g in groups.items():
        slug = qaprofile.charter_slug(f)
        g["charter"] = have[slug]["id"] if slug in have else None
        g["risk"] = "cao" if HIGH_RISK_RE.search(f) or any(HIGH_RISK_RE.search(s["name"]) for s in g["screens"]) else "thường"
        g["slug"] = slug
        out.append(g)
    out.sort(key=lambda g: (g["bucket"] == "Khác", _bucket_rank(g["bucket"]), g["risk"] != "cao",
                            -sum(1 for s in g["screens"] if s["state"] == "live"), g["feature"].lower()))
    return out


def cmd_plan(a):
    """`plan` — test plan for "the whole game": one charter per system/feature, sorted by risk; used when the feature is
    the whole game (or empty). Read-only; writes plan.md into the session if one is open."""
    root = repo_root()
    qa = qa_profile(root)
    names, enum_file = qaprofile.feature_names(root, qa)
    classes = qaprofile.classify_features(root, qa, names, enum_file) if names else {}
    buckets = feature_buckets(root)
    charters = qaprofile.list_charters(root, qa) if os.path.isdir(qa_path(root, "chartersDir")) else []
    groups = group_plan(classes, buckets, charters, screen_folder_index(root, list(classes), buckets))
    if a.only_screens:
        groups = [g for g in groups if g["screens"]]
    if a.json:
        print(json.dumps({"enumFile": os.path.relpath(enum_file, root) if enum_file else "", "groups": groups}, ensure_ascii=False, indent=1))
        return
    lines = ["plan — %d groups · %d live/referenced screens · %d dead screens skipped · enum %s" % (
        len(groups), sum(len(g["screens"]) for g in groups), sum(1 for c in classes.values() if c["state"] == "dead"),
        os.path.relpath(enum_file, root) if enum_file else "NOT FOUND")]
    for i, g in enumerate(groups, 1):
        scr = ", ".join("%s%s" % (s["name"], {"live": "", "referenced": "", "unreferenced": "?"}.get(s["state"], "")) for s in g["screens"]) or "(no screens — test through gameplay/flows)"
        lines.append("%2d. [%s] %s/%s · risk %s · charter %s" % (i, g["bucket"], g["bucket"], g["feature"], g["risk"],
                                                                 g["charter"] or "NONE — `charter new \"%s\"`" % g["feature"]))
        lines.append("    screens: %s" % scr)
    lines.append("`?` = the enum exists but no code references it (may open via a prefab) — confirm quickly on the device, do not log a bug if it cannot be reached.")
    lines.append("Work through them from 1 in the SAME session: `charter new` → STEP 1–5 → next group; `coverage` + `stop` once at the end.")
    text = "\n".join(lines)
    print(text)
    try:
        sess = Session.load(a.session)
        if not sess.d.get("ended"):
            write_text(sess.path("plan.md"), "# Plan — whole game\n\n" + text + "\n")
            print("→ %s" % sess.path("plan.md"))
    except QaError:
        pass


SAFE_TAP_WORDS = ("settings", "cài đặt", "cai dat", "setting", "close", "đóng", "dong", "back", "quay lại", "x", "✕", "×", "info", "?")  # matched against on-screen labels — keep the Vietnamese words


def cmd_selfcheck(a):
    """Self-check the tools on the connected device (open session): probe answers, /ui has elements, tap --text hits and goes back, shot not black, logs readable."""
    sess = Session.load(a.session)
    dev = sess.device()
    results = []

    def rec(name, ok, note=""):
        results.append({"check": name, "status": "pass" if ok is True else ("skip" if ok is None else "fail"), "note": note})
        print("  %s %-10s %s" % ({"pass": "✓", "fail": "✗", "skip": "–"}[results[-1]["status"]], name, note))

    print("selfcheck of session %s on %s:" % (sess.id, dev.id))
    pr = dev.probe
    if pr:
        try:
            j = pr.get("/ping", timeout=6)
            rec("probe", True, "v%s gate=%s cheat=%s" % (j.get("version"), j.get("gate"), j.get("cheat")))
        except QaError as e:
            rec("probe", False, str(e))
            pr = None
    else:
        rec("probe", None, "no probe — black-box (ui/tap --text/state/call unavailable)")
    try:
        v = observe(sess, dev, "selfcheck", logs=False, compare=False)
        warns = analyze_image(v["path"])
        rec("shot", not warns, "%dx%d %s" % (v["shot_w"], v["shot_h"], "; ".join(warns) if warns else "has content"))
    except QaError as e:
        rec("shot", False, str(e))
    try:
        fresh, rep = check_logs(sess, dev)
        lg = sess.d.get("log") or {}
        has = bool(lg.get("file") and os.path.isfile(lg["file"]) and os.path.getsize(lg["file"]) > 0) or bool(pr and sess.d["device"]["kind"] != "android")
        rec("logs", has, "%s · %d new errors" % (os.path.basename(lg.get("file") or "") or "via probe", len(fresh)))
    except QaError as e:
        rec("logs", False, str(e))
    if pr:
        try:
            j = probe_ui(sess, dev, include_all=False)
            els = j.get("elements") or []
            rec("ui", len(els) > 0, "%d elements · screens %s" % (len(els), " > ".join(j.get("screens") or []) or j.get("scene")))
            cand = None
            for e in els:
                lbl = ((e.get("label") or e.get("text") or "")).strip().lower()
                if e.get("interactable") and not e.get("occluded") and not e.get("blockedBy") and lbl in SAFE_TAP_WORDS:
                    cand = e
                    break
            if cand and not a.no_tap:
                key0 = screens_key(j)
                x, y, w, h = cand["dev"]
                cx, cy = int(x + w / 2), int(y + h / 2)
                if dev.native_input:
                    dev.tap(cx, cy)
                else:
                    inject(sess, dev, "/tap", x=cx, y=cy)
                time.sleep(1.2)
                j2 = probe_ui(sess, dev, include_all=False)
                changed = screens_key(j2) != key0 or len(j2.get("elements") or []) != len(els)
                rec("tap", changed, "tapped \"%s\" → %s" % (cand.get("label") or cand.get("text"), "UI changed" if changed else "UI did NOT change (input not reaching the game?)"))
                try:
                    dev.key("back")
                    time.sleep(0.8)
                except (QaError, NotSupported):
                    pass
                sess.step("note", "(selfcheck) bấm \"%s\" rồi Back" % (cand.get("label") or cand.get("text")))
            else:
                rec("tap", None, "no safe button (%s) on this screen to try — use `tap --text` in the first step of a case to check" % "/".join(SAFE_TAP_WORDS[:4]))
        except QaError as e:
            rec("ui", False, str(e))
    write_json(sess.path("selfcheck.json"), {"t": now_iso(), "results": results})
    sess.d["selfcheck"] = {"t": now_iso(), "fail": sum(1 for r in results if r["status"] == "fail"), "n": len(results)}
    sess.save()
    bad = [r for r in results if r["status"] == "fail"]
    if bad:
        print("selfcheck: %d checks FAIL — fix the tools before testing (session results would not be trustworthy)" % len(bad))
        sys.exit(1)
    print("selfcheck: OK")


# ───────────────────────────────────────────────────────────── coverage · charter

def cmd_coverage(a):
    root = repo_root()
    sess = Session.load(a.session)
    qa = qa_profile(root)
    cases_run = read_jsonl(sess.path("cases.jsonl"))
    planned = qaprofile.charter_cases(root, qa, a.charter)
    names, enum_file = qaprofile.feature_names(root, qa)
    classes = qaprofile.classify_features(root, qa, names, enum_file)
    cov = qaprofile.coverage_summary(cases_run, planned, classes, sess.d.get("observed") or {})
    at = sess.d.get("autotest") or {}
    if at.get("coveredScreens"):
        cov["unvisited_autotest_ok"] = [n for n in cov["unvisited"] if n in at["coveredScreens"]]
    md = qaprofile.coverage_markdown(cov, sess.d.get("feature") or "")
    if cov.get("unvisited_autotest_ok"):
        md += "- Not opened yet, but already opened OK by the Editor autotest (low priority): %s\n" % ", ".join(cov["unvisited_autotest_ok"])
    write_text(sess.path("coverage.md"), md)
    print(md if not a.quiet else "coverage: %s" % sess.path("coverage.md"))


def cmd_charter(a):
    root = repo_root()
    qa = qa_profile(root)
    cdir = qa_path(root, "chartersDir")
    if a.op == "list":
        chs = qaprofile.list_charters(root, qa)
        if not chs:
            print("no charter in %s yet — `charter new \"<feature>\"`" % os.path.relpath(cdir, root))
        for ch in chs:
            print("%-28s %-6s %3d cases  %s" % (ch["id"], ch["status"] or "-", len(ch["cases"]), ch["title"]))
        return
    if a.op == "new":
        if not a.feature:
            raise QaError("charter new \"<feature name>\" [--sources \"GDD/..., file.cs\"]")
        tpl = read_text(os.path.join(SKILL_DIR, "references", "charter.template.md"))
        if not tpl:
            raise QaError("references/charter.template.md is missing from the skill")
        cid = a.id or qaprofile.charter_slug(a.feature)
        path = os.path.join(cdir, cid + ".md")
        if os.path.isfile(path) and not a.force:
            raise QaError("%s already exists — edit that file, or --force to overwrite it" % os.path.relpath(path, root))
        write_text(path, qaprofile.new_charter_text(tpl, a.feature, qa, a.sources or ""))
        if not os.path.isfile(os.path.join(cdir, "README.md")):
            write_text(os.path.join(cdir, "README.md"), CHARTERS_README)
        try:
            sess = Session.load(a.session)
            if not sess.d.get("ended"):
                sess.d["charter"] = cid
                sess.save()
                print("session %s now uses charter %s — `note --case %s/P0-01 --result …` (or just P0-01)" % (sess.id, cid, cid))
        except QaError:
            pass
        print("new charter: %s (case ids P0-01, P1-01… — referenced as %s/P0-01) — fill in oracles + cases, then commit it with the feature" % (os.path.relpath(path, root), cid))
        return
    # diff
    sess = Session.load(a.session)
    if a.charter:
        sess.d["charter"] = a.charter
        sess.save()
    planned = qaprofile.charter_cases(root, qa, a.charter or sess.d.get("charter") or None)
    run = {}
    for c in read_jsonl(sess.path("cases.jsonl")):
        run[c["case"]] = c.get("result")
    missing = [cid for cid in planned if cid not in run]
    extra = [cid for cid in run if cid not in planned]
    by_pri = {}
    for cid in missing:
        by_pri.setdefault(planned[cid].get("priority") or "P?", []).append(cid)
    print("charter diff — session %s: %d cases planned · %d run · %d NOT run" % (sess.id, len(planned), len([c for c in planned if c in run]), len(missing)))
    for pri in sorted(by_pri):
        print("  %s not run: %s" % (pri, ", ".join("%s %s" % (c, clip(planned[c].get("title", ""), 40)) for c in by_pri[pri])))
    if extra:
        print("  cases logged outside the charter: %s" % ", ".join(extra))
    fails = [c for c, r in run.items() if r == "fail"]
    if fails:
        print("  FAIL cases: %s" % ", ".join(fails))


# ───────────────────────────────────────────────────────────── environment: aspect · env · env matrix

ASPECT_PRESETS = {"tall": "720x1600", "tablet": "1600x2560", "short": "720x1280", "square": "1080x1080"}
ENV_NAMES = ("font-scale", "dark", "animator", "kill-activities", "trim-memory", "clock", "locale")


def _remember(sess, key, prev):
    ch = sess.d.setdefault("changed", {}).setdefault("env", {})
    if key not in ch:
        ch[key] = prev
    sess.save()


def apply_env(sess, dev, name, value):
    """Apply one environment setting on the device; remembers the previous value so close_session can restore it. Returns a description (kept in Vietnamese: env_matrix writes it into the repro steps); NotSupported if the device cannot do it."""
    pkg = sess.d["package"]
    name = name.lower()
    if name == "font-scale":
        if not hasattr(dev, "font_scale"):
            raise NotSupported("changing the system text size is not supported on %s" % dev.kind)
        v = None if str(value).lower() in ("reset", "default", "1", "1.0") else float(value)
        prev = dev.font_scale(1.0 if v is None else v)
        _remember(sess, "font-scale", prev)
        return "cỡ chữ hệ thống %s (trước: %s)" % ("mặc định" if v is None else v, prev or "mặc định")
    if name == "dark":
        if not hasattr(dev, "dark_mode"):
            raise NotSupported("dark mode is not supported on %s" % dev.kind)
        on = str(value).lower() in ("on", "1", "yes", "true", "dark")
        prev = dev.dark_mode(on)
        _remember(sess, "dark", prev)
        return "dark mode %s (trước: %s)" % ("BẬT" if on else "tắt", prev)
    if name == "animator":
        if not isinstance(dev, Android):
            raise NotSupported("animator scale is Android only")
        try:
            scale = ("%g" % float(value))
        except ValueError:
            raise QaError("animator 0 | 0.5 | 1")
        prev = dev.animator_scale(scale)
        _remember(sess, "animator", prev)
        return "animator/transition/window scale = %s (trước: %s)" % (scale, prev or "1")
    if name == "kill-activities":
        if not isinstance(dev, Android):
            raise NotSupported("always_finish_activities is Android only")
        on = str(value).lower() in ("on", "1", "yes", "true")
        prev = dev.always_finish(on)
        _remember(sess, "kill-activities", prev)
        return "Don't keep activities %s (trước: %s) — nền rồi quay lại sẽ tạo lại Activity" % ("BẬT" if on else "tắt", prev or "0")
    if name == "trim-memory":
        if not isinstance(dev, Android):
            raise NotSupported("send-trim-memory is Android only")
        lvl = (value or "RUNNING_CRITICAL").upper()
        dev.trim_memory(pkg, lvl)
        return "đã gửi trim-memory %s cho %s (game nhận OnLowMemory/onTrimMemory)" % (lvl, pkg)
    if name == "clock":
        if not isinstance(dev, Android):
            raise NotSupported("changing the device time is only supported on an Android emulator/root — use the game's time-skip cheat")
        if str(value).lower() in ("reset", "auto"):
            dev.set_clock(None)
            _remember(sess, "clock", "auto")
            return "đồng hồ máy về tự động"
        m = re.match(r"^([+-]?\d+)([smhd])$", str(value))
        if not m:
            raise QaError("clock +1d | +3h | -30m | +90s | reset")
        secs = int(m.group(1)) * {"s": 1, "m": 60, "h": 3600, "d": 86400}[m.group(2)]
        _remember(sess, "clock", "auto")  # remember FIRST: set_clock turns auto_time off even if changing the time fails → stop still turns it back on
        prev = dev.set_clock(secs)
        return "đồng hồ máy dời %s (auto_time tắt tới khi stop)" % prev
    if name == "locale":
        if not isinstance(dev, Android):
            raise NotSupported("changing the locale is only supported on an Android emulator/root")
        if str(value).lower() == "reset":
            prev = ((sess.d.get("changed") or {}).get("env") or {}).get("locale")
            if not prev:
                raise QaError("the locale was not changed in this session")
            dev.set_locale(prev)
            return "locale về %s" % prev
        prev = dev.set_locale(value)
        _remember(sess, "locale", prev)
        return "locale %s (trước: %s) — framework đã khởi động lại, mở lại game" % (value, prev or "?")
    raise QaError("env <%s> <value> — or `env matrix`" % "|".join(ENV_NAMES))


def revert_env(sess, dev):
    """Revert every environment + aspect change of the session (called at stop)."""
    ch = sess.d.get("changed") or {}
    env = ch.get("env") or {}
    notes = []
    for key, prev in list(env.items()):
        try:
            if key == "font-scale" and hasattr(dev, "font_scale"):
                dev.font_scale(float(prev) if prev and re.match(r"^[\d.]+$", str(prev)) else 1.0)
            elif key == "dark" and hasattr(dev, "dark_mode"):
                dev.dark_mode(str(prev).lower() in ("yes", "dark"))
            elif key == "animator" and isinstance(dev, Android):
                dev.animator_scale(prev if prev else 1)
            elif key == "kill-activities" and isinstance(dev, Android):
                dev.always_finish(str(prev) == "1")
            elif key == "clock" and isinstance(dev, Android):
                dev.set_clock(None)
            elif key == "locale" and isinstance(dev, Android) and prev:
                dev.set_locale(prev)
        except (QaError, NotSupported) as e:
            notes.append("%s: %s" % (key, e))
    if ch.get("wm_size") and isinstance(dev, Android):
        try:
            dev.wm_size("reset")
            if ch.get("wm_density"):
                dev.wm_density("reset")
        except (QaError, NotSupported) as e:
            notes.append("wm size: %s" % e)
    ch["env"] = {}
    ch["wm_size"] = False
    sess.save()
    return notes


def cmd_aspect(a):
    sess = Session.load(a.session)
    dev = sess.device()
    spec = ASPECT_PRESETS.get(a.spec.lower(), a.spec)
    if not isinstance(dev, Android):
        raise NotSupported("changing the screen aspect is Android only (wm size). iOS: pick another Simulator (`devices`), e.g. iPhone SE / iPad, then `start` again")
    if spec.lower() == "reset":
        dev.wm_size("reset")
        if (sess.d.get("changed") or {}).get("wm_density"):
            dev.wm_density("reset")
        sess.d["changed"]["wm_size"] = False
        sess.step("note", "(môi trường) tỉ lệ màn về mặc định")
        desc = "screen back to its real size"
    else:
        if not re.match(r"^\d+x\d+$", spec):
            raise QaError("aspect WxH | %s | reset" % "|".join(sorted(ASPECT_PRESETS)))
        dev.wm_size(spec)
        sess.d.setdefault("changed", {})["wm_size"] = True
        if a.density:
            dev.wm_density(str(a.density))
            sess.d["changed"]["wm_density"] = True
        sess.step("note", "(môi trường) giả lập màn %s%s" % (spec, " dpi %s" % a.density if a.density else ""))
        desc = "simulated screen %s%s" % (spec, " dpi %s" % a.density if a.density else "")
    sess.save()
    print("aspect: %s — the game picks up the new configuration after 1-2 s (may need `app restart` if the game reads Screen only at launch)" % desc)
    time.sleep(a.wait)
    observe(sess, dev, "aspect-" + slug(spec, 12))
    sess.d["screens_key"] = None  # force auto-audit next time / audit now with --audit
    if a.audit and dev.probe:
        run_audit(sess, dev, view=sess.d.get("view"), tag="aspect-%s" % slug(spec, 12))


def cmd_env(a):
    sess = Session.load(a.session)
    dev = sess.device()
    if a.name.lower() == "matrix":
        return env_matrix(a, sess, dev)
    if a.value is None:
        raise QaError("env %s <value>" % a.name)
    try:
        desc = apply_env(sess, dev, a.name, a.value)
    except NotSupported as e:
        print("env %s: unsupported — %s" % (a.name, e))
        return
    sess.step("note", "(môi trường) " + desc)
    print("env: " + desc)
    time.sleep(a.wait)
    if a.name.lower() != "locale":
        observe(sess, dev, "env-" + slug(a.name, 14))
        if a.audit and dev.probe:
            run_audit(sess, dev, view=sess.d.get("view"), tag="env-%s" % slug(a.name + "-" + str(a.value), 20))


def env_matrix(a, sess, dev):
    """Environment matrix from qa.environments: apply → capture → audit → revert, step by step; summarized in env-matrix.md."""
    env = qa_profile().get("environments") or {}
    steps = []
    for fs in env.get("fontScales") or []:
        steps.append(("font-scale", str(fs)))
    for sz in env.get("screenSizes") or []:
        steps.append(("aspect", sz))
    if env.get("darkMode"):
        steps.append(("dark", "on"))
    if env.get("animatorScale") is not None and isinstance(dev, Android):
        steps.append(("animator", str(env.get("animatorScale"))))
    if a.quick:
        seen, q = set(), []
        for s in steps:
            if s[0] not in seen:
                seen.add(s[0])
                q.append(s)
        steps = q
    if not steps:
        raise QaError("qa.environments is empty — declare fontScales/screenSizes/darkMode in project-profile.json")
    rows = []
    print("env matrix: %d steps on %s" % (len(steps), dev.id))
    for name, value in steps:
        label = "%s=%s" % (name, value)
        try:
            if name == "aspect":
                if not isinstance(dev, Android):
                    raise NotSupported("wm size is Android only")
                dev.wm_size(value)
                sess.d.setdefault("changed", {})["wm_size"] = True
                desc = "màn %s" % value  # Vietnamese: goes into the repro steps below
            else:
                desc = apply_env(sess, dev, name, value)
        except (NotSupported, QaError) as e:
            rows.append({"step": label, "status": "unsupported", "note": str(e)})
            print("  – %-22s unsupported: %s" % (label, e))
            continue
        time.sleep(max(1.5, a.wait))
        v = observe(sess, dev, "env-" + slug(label, 18), logs=True)
        n_f, worst = 0, ""
        if dev.probe:
            try:
                findings, j = run_audit(sess, dev, view=v, quiet=True, tag="env-%s" % slug(label, 18))
                real = [f for f in findings if f["severity"] != "info" and not f.get("suppressed")]
                n_f = len(real)
                worst = "[%s] %s" % (real[0]["severity"], real[0]["kind"]) if real else ""
            except QaError as e:
                worst = "audit error: %s" % e
        img_w = analyze_image(v["path"])
        rows.append({"step": label, "status": "ok", "findings": n_f, "worst": worst, "shot": v["n"], "image": "; ".join(img_w)})
        print("  ✓ %-22s %s → %d findings %s %s" % (label, desc, n_f, worst, ("· IMAGE: " + "; ".join(img_w)) if img_w else ""))
        sess.step("env", "(môi trường) %s: %d phát hiện" % (desc, n_f), env=label, findings=n_f, shot=v["n"])
        # revert this step right away so the next one starts clean
        try:
            if name == "aspect":
                dev.wm_size("reset")
                sess.d["changed"]["wm_size"] = False
            elif name == "font-scale":
                apply_env(sess, dev, "font-scale", "reset")
            elif name == "dark":
                apply_env(sess, dev, "dark", "off")
            elif name == "animator":
                apply_env(sess, dev, "animator", "1")
        except (NotSupported, QaError):
            pass
        time.sleep(1.0)
    md = ["## Environment matrix — %s" % (sess.d.get("feature") or ""), "", "| Step | Result | Findings | Worst | Shot |", "|---|---|---|---|---|"]
    for r in rows:
        md.append("| %s | %s | %s | %s | %s |" % (r["step"], r["status"], r.get("findings", ""), r.get("worst") or r.get("note", ""), ("#%03d" % r["shot"]) if r.get("shot") else ""))
    write_text(sess.path("env-matrix.md"), "\n".join(md) + "\n")
    sess.d["env_matrix"] = rows
    sess.d["screens_key"] = None
    sess.save()
    print("env-matrix.md: %s" % sess.path("env-matrix.md"))


# ───────────────────────────────────────────────────────────── oracle: csv · pseudoloc · track

def cmd_csv(a):
    root = repo_root()
    qa = qa_profile(root)
    if a.list or not a.name:
        pat = (qa.get("conventions") or {}).get("csvConfigGlob")
        files = sorted(glob.glob(os.path.join(root, pat), recursive=True))
        print("%d CSV files match %s:" % (len(files), pat))
        for f in files:
            print("  %-34s %s" % (os.path.splitext(os.path.basename(f))[0], os.path.relpath(f, root)))
        return
    path, cands = qaprofile.find_csv(root, qa, a.name)
    if not path:
        raise QaError("CSV '%s' not found%s — `csv --list`" % (a.name, (" (close matches: %s)" % ", ".join(os.path.basename(c) for c in cands[:6])) if cands else ""))
    header, rows = qaprofile.read_csv_rows(path)
    rows = qaprofile.filter_rows(rows, a.where)
    cols = a.col or header
    if a.json:
        print(json.dumps([{c: r.get(c, "") for c in cols} for r in rows], ensure_ascii=False, indent=1))
        return
    print("%s · %d rows%s · columns: %s" % (os.path.relpath(path, root), len(rows), " (filter %s)" % " ".join(a.where) if a.where else "", ", ".join(header)))
    widths = {c: min(28, max(len(c), *(len(str(r.get(c, ""))) for r in rows[:a.limit])) if rows else len(c)) for c in cols}
    print("  " + " | ".join(c.ljust(widths[c]) for c in cols))
    for r in rows[:a.limit]:
        print("  " + " | ".join(clip(str(r.get(c, "")), widths[c]).ljust(widths[c]) for c in cols))
    if len(rows) > a.limit:
        print("  … %d more rows (--limit N)" % (len(rows) - a.limit))


def cmd_pseudoloc(a):
    sess = Session.load(a.session)
    dev = sess.device()
    pr = need_probe(dev, "Pseudo-localization")
    if not probe_supports(sess, "pseudoloc"):
        print("pseudoloc: " + unsupported_probe(sess, "pseudoloc"))
        print("  alternative: switch the game language to the longest one (e.g. German/Russian) in Settings, then `audit` each screen")
        return
    on = a.mode_onoff == "on"
    j = pr.get("/pseudoloc", on=1 if on else 0, mode=a.mode, timeout=20)
    sess.d["pseudoloc"] = on
    sess.step("note", "(môi trường) pseudo-loc %s (%s) — %s chữ" % ("BẬT" if on else "tắt", a.mode, j.get("count")))
    print("pseudoloc %s · mode %s · %s text elements changed" % ("ON" if j.get("on") else "off", j.get("mode", a.mode), j.get("count")))
    time.sleep(a.wait)
    v = observe(sess, dev, "pseudoloc-" + ("on" if on else "off"))
    if on:
        run_audit(sess, dev, view=v, tag="pseudoloc-%03d" % v["n"])
        print("→ walk the charter's screens with pseudo-loc on: auto-audit catches overflowing/cut text; `pseudoloc off` when done")


def _lines_since(sess, mark_name=None):
    """App log lines since a point (mark name / 'last' / None = whole session)."""
    lg = sess.d.get("log") or {}
    path = lg.get("file")
    lines = read_text(path).splitlines() if path else []
    if sess.d["device"]["kind"] == "android":
        pids = set(str(p) for p in sess.d.get("pids") or [])
        lines = [l for l in lines if (LOGCAT_RE.match(l) and LOGCAT_RE.match(l).group(2) in pids) or sess.d["package"] in l]
    if mark_name:
        steps = read_jsonl(sess.path("steps.jsonl"))
        marks = [s for s in steps if s["action"] == "mark" and (mark_name == "last" or (s.get("desc") or "").lower().startswith(mark_name.lower()))]
        if marks:
            t = marks[-1]["t"]  # ISO UTC; logcat has no year/UTC → cut by line position instead of time
            # use the line count recorded at the mark if any, otherwise the last 60%
            cut = marks[-1].get("log_lines")
            lines = lines[cut:] if isinstance(cut, int) else lines[int(len(lines) * 0.4):]
    return lines


def cmd_track(a):
    sess = Session.load(a.session)
    dev = sess.device()
    pat = (qa_profile().get("conventions") or {}).get("trackingLogPattern") or "Tracking"
    rg = re.compile(a.pattern or pat, re.I)
    lines = [l for l in _lines_since(sess, a.since) if rg.search(l)]
    if dev.probe and sess.d["device"]["kind"] != "android":
        try:
            ents = dev.probe.get("/logs", since=0, timeout=10).get("entries") or []
            lines += [e.get("msg", "") for e in ents if rg.search(e.get("msg", ""))]
        except QaError:
            pass
    print("tracking: %d matching lines /%s/%s" % (len(lines), a.pattern or pat, (" since mark %s" % a.since) if a.since else ""))
    for l in lines[-a.tail:]:
        m = LOGCAT_RE.match(l)
        print("  " + clip(m.group(6) if m else l, 220))
    if a.expect:
        hit = [l for l in lines if re.search(a.expect, l, re.I)]
        if hit:
            print("  ✓ event /%s/ present (%d times)" % (a.expect, len(hit)))
            sess.step("check", "Tracking: có event %s" % a.expect, result="pass")
        else:
            print("  ✗ MISSING event /%s/ — recheck the tracking code of the flow just played" % a.expect)
            sess.step("check", "Tracking: THIẾU event %s" % a.expect, result="fail", kind="tracking-missing")
            sys.exit(1)


# ───────────────────────────────────────────────────────────── autotest-import · build update

def cmd_autotest_import(a):
    root = repo_root()
    qa = qa_profile(root)
    path = a.path or qaprofile.latest_report(root, qa)
    if not path or not os.path.isfile(path):
        raise QaError("no com.ezg.autotest report.json (%s) — run Ezg > Auto test system in the Editor first, or pass a path" % qa_path(root, "autotestReports"))
    rep = qaprofile.parse_autotest_report(path)
    md = qaprofile.autotest_markdown(rep)
    if rep["version"] == "0.1.1":
        md += "\n> ⚠ autotest report < %s: dead features not filtered, MissingReference not scoped to what is loaded — Major/Critical counts of the static/smoke suites are noisy; use only the list of screens opened OK/FAILED and the Blockers/Criticals with exceptions.\n" % AUTOTEST_MIN_VERSION
    try:
        sess = Session.load(a.session)
        if sess.d.get("ended"):
            sess = None
    except QaError:
        sess = None
    if sess:
        write_text(sess.path("autotest-summary.md"), md)
        sess.d["autotest"] = {"runId": rep["runId"], "path": path, "version": rep["version"], "bySeverity": rep["bySeverity"],
                              "coveredScreens": rep["coveredScreens"], "failedScreens": rep["failedScreens"]}
        sess.step("note", "(autotest-import) %s: %s · màn OK %d · màn lỗi %d" % (
            rep["runId"], " ".join("%s%d" % (k[0], v) for k, v in rep["bySeverity"].items()), len(rep["coveredScreens"]), len(rep["failedScreens"])))
        sess.save()
    print(md if not a.quiet else "autotest-import: %s" % (sess.path("autotest-summary.md") if sess else path))
    if a.json:
        print(json.dumps({k: v for k, v in rep.items() if k not in ("issues", "cases")}, ensure_ascii=False, indent=1))


def cmd_build_update(a):
    """Install a new build over the one under test (keeping data) → check save migration: state before/after per qa.verified.statePaths."""
    root = repo_root()
    sess = Session.load(a.session)
    dev = sess.device()
    pkg = sess.d["package"]
    path = a.path
    if path in ("latest", "auto"):
        path = find_artifact(root, dev.platform)
        if not path:
            raise QaError("no build found in the repo — pass an apk/app path")
    if not os.path.exists(path):
        raise QaError("file %s does not exist" % path)
    qa = qa_profile(root)
    paths = [v.get("path") for v in ((qa.get("verified") or {}).get("statePaths") or {}).values() if v.get("path")] + list(a.state or [])
    before = {"version": dev.app_version(pkg), "state": {}}

    def snapshot():
        out = {}
        if dev.probe and (sess.d.get("probe") or {}).get("ok"):
            for p in paths:
                try:
                    out[p] = dev.probe.get("/get", path=p, timeout=10).get("value")
                except QaError as e:
                    out[p] = "<error: %s>" % e
        return out
    before["state"] = snapshot()
    print("build update: %s v%s → installing %s%s" % (pkg, before["version"][0], os.path.basename(path), " (keeping data)" if not a.wipe else " (WIPING data)"))
    sess.d["expect_exit"] = sess.d.get("expect_exit", 0) + 1
    sess.save()
    dev.stop(pkg)
    dev.install(path)
    if a.wipe:
        dev.clear_data(pkg)
    relaunch(sess, dev)
    time.sleep(a.settle)
    after = {"version": dev.app_version(pkg), "state": snapshot()}
    diff = []
    for p in paths:
        b, c = before["state"].get(p), after["state"].get(p)
        if b != c:
            diff.append((p, b, c))
    md = ["## Build update — save migration", "",
          "- Before: v%s (%s) · After: v%s (%s) · file `%s`" % (before["version"][0], before["version"][1], after["version"][0], after["version"][1], path),
          "- Compared state: %d paths · %d differ" % (len(paths), len(diff)), ""]
    if diff:
        md += ["| Path | Before | After |", "|---|---|---|"]
        for p, b, c in diff:
            md.append("| `%s` | %s | %s |" % (p, clip(json.dumps(b, ensure_ascii=False), 60), clip(json.dumps(c, ensure_ascii=False), 60)))
    elif not paths:
        md.append("- (no qa.verified.statePaths yet — `state <path>` a few important values before `build update`, or --state <path>)")
    write_text(sess.path("migration.md"), "\n".join(md) + "\n")
    sess.step("build-update", "Cập nhật game lên bản mới%s rồi mở lại" % (" (xoá data)" if a.wipe else " giữ nguyên dữ liệu"),
              before=before["version"], after=after["version"], changed=len(diff))
    sess.d["app"].update({"version": after["version"][0], "build": after["version"][1]})
    sess.save()
    print("\n".join(md))
    observe(sess, dev, "build-update")
    if diff:
        print("⚠ %d state values changed after the update — see migration.md; lost currency/progress = save-migration bug (critical)" % len(diff))


# ───────────────────────────────────────────────────────────── flush learned knowledge into the profile

def flush_profile(sess, quiet=False):
    """At stop: observed screens → qa.liveness.observed; cheats/state called OK → qa.verified. Refreshes the auto regions of QaProfile.md."""
    root = repo_root()
    qa = qa_profile(root, refresh=True)
    names, _ = qaprofile.feature_names(root, qa)
    name_set = set(names)
    obs = {k: v for k, v in (sess.d.get("observed") or {}).items() if k in name_set}
    cheats = sess.d.get("verified_cheats") or {}
    states = {}
    for p, d in (sess.d.get("verified_state") or {}).items():
        alias = ".".join(p.split(".")[-2:])
        states[alias] = {"path": p, "lastOk": d}
    patch = {}
    if obs:
        patch["liveness"] = {"observed": obs}
    if cheats or states:
        patch["verified"] = {}
        if cheats:
            patch["verified"]["cheats"] = cheats
        if states:
            patch["verified"]["statePaths"] = states
    if not patch:
        return None
    if not os.path.isfile(os.path.join(root, qaprofile.PROFILE_REL)):
        return None
    qaprofile.save_qa_patch(root, patch)
    _QA_CACHE.pop(root, None)
    doc = refresh_profile_doc(root, qa_profile(root, refresh=True))
    line = "QA profile: +%d observed screens · +%d cheat · +%d state path → %s%s" % (
        len(obs), len(cheats), len(states), qaprofile.PROFILE_REL, (" + %s" % os.path.relpath(doc, root)) if doc else "")
    if not quiet:
        print(line)
        print("  → `profile sync` so the Editor autotest gets observedLive; the dev reviews QaProfile.md (Status → reviewed); commit with the repo")
    return line


# ───────────────────────────────────────────────────────────── argparse

def build_parser():
    p = argparse.ArgumentParser(prog="qadevice", description="Device driver for /auto-test-device (EZG).")
    p.add_argument("--session", help="session id (default: the current session)")
    p.add_argument("--no-auto-audit", action="store_true", help="do not auto-audit when the screen changes after an action (default: on, needs the probe)")
    sub = p.add_subparsers(dest="cmd")

    def act(sp, coords=True):
        sp.add_argument("--as", dest="desc", help="describe the action as a player would (goes into the bug repro steps, in Vietnamese)")
        sp.add_argument("--wait", type=float, default=0.8, help="wait before capturing after the action (s)")
        sp.add_argument("--no-shot", action="store_true", help="no capture after the action")
        sp.add_argument("--grid", action="store_true", help="post-action shot with a coordinate grid")
        sp.add_argument("--marks", action="store_true", help="post-action shot with numbered UI elements (probe)")
        sp.add_argument("--label", help="label for the shot file name")
        if coords:
            sp.add_argument("--space", choices=("view", "device"), default="view", help="coordinates in view image px (default) or device px")
            sp.add_argument("--inject", action="store_true", help="tap via the probe instead of system input")
            sp.add_argument("--allow-overlay", action="store_true",
                            help="allow adb taps on another app's overlay area (blocked by default — the tap would hit that app)")

    sp = sub.add_parser("doctor", help="check machine tools, packages, BugHub, QA profile, API-error retry hook + the remaining to-do list "
                                        "(--json for the model; --fix does what the tool can; --strict: rc 1 when a required item is missing)")
    sp.add_argument("--strict", action="store_true")
    sp.add_argument("--json", action="store_true", help="print JSON (tools/packages/bughub/qa/apiRetryHook/featureHub/mcp/actions)")
    sp.add_argument("--fix", action="store_true", help="do what the tool can: profile init/sync, add the probe/autotest packages to the manifest")
    sp.add_argument("--dry-run", action="store_true", help="--fix: print only, write nothing")
    sp.set_defaults(fn=cmd_doctor)

    sp = sub.add_parser("plan", help="test plan for the WHOLE game: map live screens to features/buckets, sort by risk, one charter per group (writes plan.md into the session if one is open)")
    sp.add_argument("--json", action="store_true")
    sp.add_argument("--only-screens", action="store_true", help="drop groups without screens")
    sp.set_defaults(fn=cmd_plan)

    sp = sub.add_parser("profile", help="project QA profile (.claude/project-profile.json[qa] + QaProfile.md): init | sync | show")
    sp.add_argument("op", choices=("init", "sync", "show"))
    sp.add_argument("--force", action="store_true", help="init: also overwrite keys the dev declared")
    sp.add_argument("--dry-run", action="store_true", help="init/sync: print only, write nothing")
    sp.add_argument("--json", action="store_true", help="show: print JSON with the source of each key")
    sp.set_defaults(fn=cmd_profile)

    sp = sub.add_parser("selfcheck", help="self-check the tools on the connected device: probe, /ui, tap --text, shot, logs (writes selfcheck.json; rc 1 on failure)")
    sp.add_argument("--no-tap", action="store_true", help="do not try tapping a safe button")
    sp.set_defaults(fn=cmd_selfcheck)

    sp = sub.add_parser("coverage", help="planned/run/pass/fail cases per charter + live screens not opened yet → coverage.md")
    sp.add_argument("--charter", help="only one charter (file name without extension)")
    sp.add_argument("--quiet", action="store_true")
    sp.set_defaults(fn=cmd_coverage)

    sp = sub.add_parser("charter", help="charters in the repo (.claude/docs/qa/charters): list | new \"<feature>\" | diff")
    sp.add_argument("op", choices=("list", "new", "diff"))
    sp.add_argument("feature", nargs="?", help="new: feature name")
    sp.add_argument("--id", help="new: charter file name (default: slug of the feature)")
    sp.add_argument("--sources", help="new: sources read (GDD/TechSpec/files)")
    sp.add_argument("--charter", help="diff: charter name (default: the session's charter)")
    sp.add_argument("--force", action="store_true")
    sp.set_defaults(fn=cmd_charter)

    sp = sub.add_parser("aspect", help="(Android) simulate a screen size: WxH | tall | tablet | short | square | reset")
    sp.add_argument("spec")
    sp.add_argument("--density", type=int, help="wm density to go with it")
    sp.add_argument("--wait", type=float, default=2.0)
    sp.add_argument("--audit", action="store_true", help="audit right after the change")
    sp.set_defaults(fn=cmd_aspect)

    sp = sub.add_parser("env", help="device environment: font-scale 1.3|reset · dark on|off · animator 0|0.5|1 · kill-activities on|off · "
                                    "trim-memory [LEVEL] · clock +1d|reset · locale vi-VN — or `env matrix [--quick]`")
    sp.add_argument("name")
    sp.add_argument("value", nargs="?")
    sp.add_argument("--wait", type=float, default=2.0)
    sp.add_argument("--audit", action="store_true", help="audit right after the change")
    sp.add_argument("--quick", action="store_true", help="matrix: one step per kind")
    sp.set_defaults(fn=cmd_env)

    sp = sub.add_parser("csv", help="oracle: read a config CSV by name (qa.conventions.csvConfigGlob): csv CoinPack --where id=2 --col price")
    sp.add_argument("name", nargs="?")
    sp.add_argument("--where", action="append", help="col=val | col~regex | col>=3 (repeatable)")
    sp.add_argument("--col", action="append", help="print only this column (repeatable)")
    sp.add_argument("--limit", type=int, default=40)
    sp.add_argument("--json", action="store_true")
    sp.add_argument("--list", action="store_true", help="list the CSV files")
    sp.set_defaults(fn=cmd_csv)

    sp = sub.add_parser("pseudoloc", help="(probe ≥1.3) pseudo-localization: on | off [--mode expand|accent|both]")
    sp.add_argument("mode_onoff", choices=("on", "off"))
    sp.add_argument("--mode", choices=("expand", "accent", "both"), default="both")
    sp.add_argument("--wait", type=float, default=1.0)
    sp.set_defaults(fn=cmd_pseudoloc)

    sp = sub.add_parser("track", help="tracking events in the log (qa.conventions.trackingLogPattern): track [--since MARK] [--expect REGEX]")
    sp.add_argument("--since", help="mark name (or `last`)")
    sp.add_argument("--expect", help="regex of a required event; missing → rc 1")
    sp.add_argument("--pattern", help="filter regex instead of trackingLogPattern")
    sp.add_argument("--tail", type=int, default=40)
    sp.set_defaults(fn=cmd_track)

    sp = sub.add_parser("autotest-import", help="read the latest com.ezg.autotest report.json → autotest-summary.md + the screens that did / did not open (Δ-skip)")
    sp.add_argument("path", nargs="?")
    sp.add_argument("--quiet", action="store_true")
    sp.add_argument("--json", action="store_true")
    sp.set_defaults(fn=cmd_autotest_import)

    sp = sub.add_parser("build", help="build update <apk|app|latest>: install over the current build keeping data, then compare state (save migration)")
    sp.add_argument("op", choices=("update",))
    sp.add_argument("path", nargs="?", default="latest")
    sp.add_argument("--keep-data", action="store_true", help="(default) keep the game data")
    sp.add_argument("--wipe", action="store_true", help="wipe the data after installing (new player on the new build)")
    sp.add_argument("--state", action="append", help="extra state path to compare (besides qa.verified.statePaths)")
    sp.add_argument("--settle", type=float, default=6.0)
    sp.set_defaults(fn=cmd_build_update)
    sp = sub.add_parser("devices", help="list devices/AVDs/Simulators")
    sp.add_argument("--json", action="store_true")
    sp.set_defaults(fn=cmd_devices)
    sp = sub.add_parser("bughub-check", help="check that BugHub is set up for the project")
    sp.add_argument("--code")
    sp.add_argument("--json", action="store_true")
    sp.set_defaults(fn=cmd_bughub_check)

    sp = sub.add_parser("start", help="open a test session: pick the device, open the game, capture logs, connect the probe, take the first shot")
    sp.add_argument("device", nargs="?", default="auto", help="adb serial / AVD name / Simulator UDID or name / iPhone id / auto")
    sp.add_argument("--feature", default="", help="feature under test")
    sp.add_argument("--package", help="bundle id (default: read from ProjectSettings)")
    sp.add_argument("--install", help="install an APK/.app/.ipa before testing")
    sp.add_argument("--no-install", action="store_true", help="do not auto-install a build found in the repo")
    sp.add_argument("--fresh", action="store_true", help="wipe the game data first (new player — LOSES the progress on the device)")
    sp.add_argument("--attach", action="store_true", help="do not restart a game that is already running")
    sp.add_argument("--probe", choices=("auto", "off", "require"), default="auto")
    sp.add_argument("--probe-wait", type=float, default=25)
    sp.add_argument("--port", type=int, default=PROBE_PORT)
    sp.add_argument("--settle", type=float, default=3.0, help="wait after opening the game before capturing (s)")
    sp.add_argument("--window", action="store_true", help="boot the AVD with a window (default: headless)")
    sp.add_argument("--code", help="BugHub code (default: from project-profile)")
    sp.add_argument("--no-probe-install", action="store_true", help="do not auto-install/update the probe in the project")
    sp.add_argument("--no-api-retry", action="store_true",
                    help="do not arm the API-error retry (default: /auto-clear retry-arm --max 0 — a turn killed by an API error is resumed after 30 s)")
    sp.add_argument("--no-profile-init", action="store_true", help="do not auto-run `profile init` when the project has no qa block")
    sp.add_argument("--skip-bughub", action="store_true", help=argparse.SUPPRESS)
    sp.set_defaults(fn=cmd_start)

    sp = sub.add_parser("retry", help="re-run a qadevice command every 30 s until it passes: retry [--every S] [--max-wait S] -- <command…>")
    sp.add_argument("--every", type=float, default=RETRY_EVERY_S, help="seconds between attempts")
    sp.add_argument("--max-wait", type=float, default=RETRY_MAX_WAIT_S,
                    help="keep trying at most this many seconds per call (on timeout: exit %d, call again)" % RETRY_PENDING_RC)
    sp.add_argument("argv", nargs=argparse.REMAINDER, help="the qadevice command to re-run, after `--`")
    sp.set_defaults(fn=cmd_retry)

    sp = sub.add_parser("api-retry", help="the session's API-error retry (/auto-clear StopFailure hook): on | off | status")
    sp.add_argument("action", choices=("on", "off", "status"), nargs="?", default="status")
    sp.set_defaults(fn=cmd_api_retry)

    sp = sub.add_parser("probe-connect", help="reconnect the probe (after turning cheats/the gate on, entering Editor Play mode, an app restart)")
    sp.add_argument("--wait", type=float, default=30)
    sp.add_argument("--retrigger", action="store_true", help="rewrite the trigger file + background/reopen the app so the probe reads it")
    sp.add_argument("--unlock", action="store_true", help="re-ask CheatGate (device just added with test_device_add) and wait for the gate to open")
    sp.add_argument("--unlock-wait", type=float, default=180, help="wait at most N seconds for the server to accept the new device")
    sp.set_defaults(fn=cmd_probe_connect)

    sp = sub.add_parser("cheat", help="(probe) turn the game's cheat mode on/off: on | off | status")
    sp.add_argument("mode", choices=("on", "off", "status"), nargs="?", default="status")
    sp.set_defaults(fn=cmd_cheat)

    sp = sub.add_parser("shot", help="screenshot + frame diff + log check")
    sp.add_argument("--grid", action="store_true")
    sp.add_argument("--marks", action="store_true")
    sp.add_argument("--label")
    sp.add_argument("--no-logs", action="store_true")
    sp.set_defaults(fn=cmd_shot)

    sp = sub.add_parser("tap", help="tap: X Y (view image) | 50%% 80%% | @id | --text | --name | --world")
    sp.add_argument("target", nargs="*")
    sp.add_argument("--text")
    sp.add_argument("--name")
    sp.add_argument("--world")
    sp.add_argument("--times", type=int, default=1, help="tap N times in a row (spam/double-tap tests)")
    sp.add_argument("--interval", type=float, default=0.08, help="seconds between taps (at least 0.07 on Android)")
    sp.add_argument("--expect", help="(probe) expectation after the tap: text:<text> | @id | screen:<screen name> | name:<name> (default text) — not met → records expectation-failed")
    sp.add_argument("--timeout", type=float, default=EXPECT_TIMEOUT_S, help="max wait for the expectation (s)")
    act(sp)
    sp.set_defaults(fn=cmd_tap)

    sp = sub.add_parser("longpress", help="press and hold")
    sp.add_argument("target", nargs="*")
    sp.add_argument("--text")
    sp.add_argument("--name")
    sp.add_argument("--world")
    sp.add_argument("--ms", type=int, default=1200)
    act(sp)
    sp.set_defaults(fn=cmd_longpress)

    for name, fn in (("swipe", cmd_swipe), ("drag", cmd_drag)):
        sp = sub.add_parser(name, help="%s X1 Y1 X2 Y2 | --dir up/down/left/right" % name)
        sp.add_argument("coords", nargs="*")
        sp.add_argument("--dir", choices=("up", "down", "left", "right"))
        sp.add_argument("--dist", type=float, help="%% of the screen for --dir (default 40)")
        sp.add_argument("--ms", type=int, default=350 if name == "swipe" else 900)
        if name == "drag":
            sp.add_argument("--hold", type=int, default=350, help="hold before dragging (ms)")
        act(sp)
        sp.set_defaults(fn=fn)

    sp = sub.add_parser("text", help="type text into the focused field")
    sp.add_argument("value")
    sp.add_argument("--probe", action="store_true", help="set the text via the probe (Unicode)")
    sp.add_argument("--path", help="(probe) path of the input field")
    sp.add_argument("--submit", action="store_true")
    act(sp, coords=False)
    sp.set_defaults(fn=cmd_text)

    sp = sub.add_parser("key", help="system key: back home enter app_switch volume_up …")
    sp.add_argument("name")
    act(sp, coords=False)
    sp.set_defaults(fn=cmd_key)

    sp = sub.add_parser("wait", help="wait N seconds, then capture (--settle: wait until the screen stops changing)")
    sp.add_argument("secs", type=float)
    sp.add_argument("--settle", action="store_true")
    sp.add_argument("--threshold", type=float, default=0.01)
    sp.add_argument("--as", dest="desc")
    sp.add_argument("--no-shot", action="store_true")
    sp.add_argument("--label")
    sp.set_defaults(fn=cmd_wait)

    sp = sub.add_parser("burst", help="capture N consecutive frames (inspect animation/flicker)")
    sp.add_argument("count", type=int, nargs="?", default=5)
    sp.add_argument("--interval", type=float, default=0.15)
    sp.set_defaults(fn=cmd_burst)

    sp = sub.add_parser("app", help="restart|stop|launch|background|clear|info|install")
    sp.add_argument("op", choices=("restart", "stop", "launch", "background", "clear", "info", "install"))
    sp.add_argument("path", nargs="?")
    sp.add_argument("--secs", type=float, default=5)
    sp.add_argument("--wait", type=float, default=4)
    sp.add_argument("--hard", action="store_true",
                    help="restart/stop: hard kill like a crash / the OS killing the app (default: Home → wait → kill, like a player)")
    sp.add_argument("--settle", type=float, default=STOP_SETTLE_SECONDS, help="restart/stop: seconds on Home before killing")
    sp.add_argument("--as", dest="desc")
    sp.set_defaults(fn=cmd_app)

    sp = sub.add_parser("overlays", help="(Android) list other apps' overlays over the screen (adb taps there hit that app)")
    sp.set_defaults(fn=cmd_overlays)

    sp = sub.add_parser("ad-close", help="(Android) find + tap the close/skip button of a native ad; reports when an overlay covers it")
    sp.add_argument("--timeout", type=float, default=30, help="wait at most N seconds for the close button to appear (ad countdown)")
    sp.add_argument("--list", action="store_true", help="only list the candidate buttons, do not tap")
    sp.add_argument("--allow-overlay", action="store_true", help="tap even when the button center is under another app's overlay")
    sp.add_argument("--wait", type=float, default=1.5, help="wait after tapping before capturing (s)")
    sp.add_argument("--no-shot", action="store_true")
    sp.add_argument("--label")
    sp.add_argument("--as", dest="desc")
    sp.set_defaults(fn=cmd_ad_close)

    sp = sub.add_parser("net", help="network: off|on|wifi-off|data-off")
    sp.add_argument("mode", choices=("off", "on", "wifi-off", "data-off"))
    act(sp, coords=False)
    sp.set_defaults(fn=cmd_net)

    sp = sub.add_parser("rotate", help="portrait|landscape|reverse-portrait|reverse-landscape|auto")
    sp.add_argument("mode")
    act(sp, coords=False)
    sp.set_defaults(fn=cmd_rotate)

    sp = sub.add_parser("logs", help="new errors in the log (--all: whole session; --tail N; --grep REGEX; --ignore REGEX)")
    sp.add_argument("--all", action="store_true")
    sp.add_argument("--tail", type=int)
    sp.add_argument("--grep")
    sp.add_argument("--raw", action="store_true", help="--tail/--grep without filtering by app")
    sp.add_argument("--ignore", help="ignore errors matching the regex for this session (confirmed SDK noise)")
    sp.set_defaults(fn=cmd_logs)

    sp = sub.add_parser("perf", help="RAM/CPU/temperature/FPS (FPS needs the probe)")
    sp.set_defaults(fn=cmd_perf)

    sp = sub.add_parser("ui", help="(probe) UI tree with coordinates, states, issue flags")
    sp.add_argument("--all", action="store_true", help="include non-interactive texts/images")
    sp.add_argument("--find")
    sp.add_argument("--marks", action="store_true", help="draw boxes + numbers on the shot")
    sp.add_argument("--json", action="store_true")
    sp.set_defaults(fn=cmd_ui)

    sp = sub.add_parser("audit", help="capture + visual audit of the current screen (deep with the probe; applies the qa.noise rules)")
    sp.add_argument("--all", action="store_true")
    sp.add_argument("--include-suppressed", action="store_true", help="also print findings hidden by the project's noise rules")
    sp.set_defaults(fn=cmd_audit)

    sp = sub.add_parser("state", help="(probe) read a static value: Namespace.Type.Member.Member…")
    sp.add_argument("paths", nargs="+")
    sp.add_argument("--full", action="store_true", help="print the whole value (default: cut at %d chars)" % VALUE_PRINT_MAX)
    sp.add_argument("--out", help="write the value (JSON) to a file instead of printing")
    sp.set_defaults(fn=cmd_state)

    sp = sub.add_parser("call", help="(probe) call a static/instance method: Type.Method arg1 arg2 (setup/cheat)")
    sp.add_argument("method")
    sp.add_argument("args", nargs="*")
    sp.add_argument("--as", dest="desc")
    sp.add_argument("--no-shot", action="store_true")
    sp.add_argument("--wait", type=float, default=0.8)
    sp.add_argument("--full", action="store_true", help="print the whole result (default: cut at %d chars)" % VALUE_PRINT_MAX)
    sp.add_argument("--out", help="write the result (JSON) to a file instead of printing — for long lists")
    sp.set_defaults(fn=cmd_call)

    sp = sub.add_parser("types", help="(probe) find types + static members by keyword (e.g. Cheat, PlayerData)")
    sp.add_argument("query")
    sp.set_defaults(fn=cmd_types)

    sp = sub.add_parser("timescale", help="(probe) change Time.timeScale to fast-forward waits")
    sp.add_argument("value", type=float)
    sp.set_defaults(fn=cmd_timescale)

    sp = sub.add_parser("mark", help="start a new test case (bug repro steps start here)")
    sp.add_argument("name", nargs="?")
    sp.set_defaults(fn=cmd_mark)

    sp = sub.add_parser("note", help="note / test case result; --case <charter>/<P0-01> --result … records a charter case (read by coverage)")
    sp.add_argument("text", nargs="?")
    sp.add_argument("--case", help="case id in the charter, e.g. offline-earning/P0-01")
    sp.add_argument("--result", choices=("pass", "fail", "blocked", "skip"))
    sp.add_argument("--bug", type=int, help="related BugHub bug number (failed case)")
    sp.set_defaults(fn=cmd_note)

    sp = sub.add_parser("steps", help="show the repro steps since the last mark")
    sp.add_argument("--all", action="store_true")
    sp.set_defaults(fn=cmd_steps)

    sp = sub.add_parser("bug", help="log a bug to BugHub (Bug Logger contract)")
    sp.add_argument("--title", required=True)
    sp.add_argument("--actual", required=True)
    sp.add_argument("--expected", default="")
    sp.add_argument("--steps", default="auto", help="'auto' = generated from the steps since the last mark")
    sp.add_argument("--precondition", help="precondition (level, currency, state…)")
    sp.add_argument("--type", choices=BUG_TYPES, default="logic")
    sp.add_argument("--severity", choices=SEVERITIES, default="medium")
    sp.add_argument("--rate", default="1/1", help="repro rate, e.g. 3/3")
    sp.add_argument("--feature", help="featureKey (default: from the title's 'Feature:' prefix / session feature / current screen)")
    sp.add_argument("--category")
    sp.add_argument("--tag", action="append", help="extra forum tag name")
    sp.add_argument("--mention", action="append", help="name of a person to tag")
    sp.add_argument("--description")
    sp.add_argument("--evidence", help="evidence description (which shot, measured values…)")
    sp.add_argument("--shot", default="last", help="last | none | image path")
    sp.add_argument("--log-sig", help="signature of the related log error (from `logs`)")
    sp.add_argument("--save", action="store_true", help="(probe) attach a snapshot of the game save")
    sp.add_argument("--extra", action="append", help="k=v added to extra")
    sp.add_argument("--via", choices=("http", "ingame"), default="http")
    sp.add_argument("--code")
    sp.add_argument("--endpoint")
    sp.add_argument("--force", action="store_true", help="skip the local duplicate check (log signature / title / screen+type)")
    sp.add_argument("--dry-run", action="store_true")
    sp.set_defaults(fn=cmd_bug)

    sp = sub.add_parser("bug-resend", help="resend a saved report")
    sp.add_argument("report_id")
    sp.set_defaults(fn=cmd_bug_resend)

    sp = sub.add_parser("report", help="write the session's report.md")
    sp.add_argument("--print", action="store_true")
    sp.set_defaults(fn=cmd_report)

    sp = sub.add_parser("stop", help="end the session: restore network/rotation, stop logs, remove the probe trigger, write the report")
    sp.set_defaults(fn=cmd_stop)

    sp = sub.add_parser("probe-install", help="add/bump the com.ezg.qa-probe package in the manifest, remove the old copy")
    sp.add_argument("--gate", help="override the release gate: none | Namespace.Type.member (static bool)")
    sp.set_defaults(fn=cmd_probe_install)
    return p


def main(argv=None):
    global AUTO_AUDIT
    p = build_parser()
    a = p.parse_args(argv)
    if not getattr(a, "fn", None):
        p.print_help()
        return 2
    if getattr(a, "no_auto_audit", False):
        AUTO_AUDIT = False
    try:
        a.fn(a)
        return 0
    except QaError as e:
        print("ERROR: %s" % e)  # "ERROR:" prefix is quoted by the skill doc — keep it
        return 1
    except KeyboardInterrupt:
        return 130


if __name__ == "__main__":
    sys.exit(main())
