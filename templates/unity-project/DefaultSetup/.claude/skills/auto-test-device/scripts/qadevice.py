#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
qadevice.py — driver thiết bị cho skill /auto-test-device (EZG).

Một CLI cho agent chơi game như người dùng thật rồi log bug:
  * Android (adb: máy thật / emulator, kể cả tự boot AVD) — input chạm thật, logcat, perf, mạng, xoay màn.
  * iOS Simulator (xcrun simctl; chạm qua `idb` nếu có, không thì qua probe).
  * iPhone thật (xcrun devicectl; chụp/chạm qua probe — cần build có EZG QA Probe).
  * EZG QA Probe (tuỳ chọn, nằm trong build): cây UI kèm toạ độ, audit visual (tràn chữ, thiếu glyph, nút bị
    che…), đọc state / gọi hàm cheat qua reflection, log Unity kèm stack, context cho bug.
  * Log bug lên BugHub đúng contract của Bug Logger (POST /v1/reports — giống hệt nút Bug Logger trong game).

Chạy:  python3 .claude/skills/auto-test-device/scripts/qadevice.py <lệnh> [tham số]     (Windows: py …)
Trạng thái phiên: <git-common-dir>/auto-test-device/ (không bao giờ commit). `qadevice.py -h` để xem lệnh.
Chỉ dùng thư viện chuẩn; Pillow (pip install pillow) bật thu nhỏ ảnh, lưới toạ độ, so khung hình, audit ảnh.
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

try:  # Pillow là tuỳ chọn — thiếu thì vẫn chạy, chỉ mất phần xử lý ảnh.
    from PIL import Image, ImageChops, ImageDraw, ImageFont, ImageStat
except Exception:  # pragma: no cover - phụ thuộc máy
    Image = None

VERSION = "1.5.0"
PROBE_PORT = 7531
PROBE_PORT_SPAN = 5
PROBE_TRIGGER_FILE = "ezg_qa_probe.txt"
CHEAT_PENDING = ("", "waiting_init", "checking")
CHEAT_UNLOCK_RETRY_S = 30  # hỏi lại CheatGate tối đa 1 lần/30 s (server giới hạn tần suất)
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
PROBE_MIN_VERSION = "1.2.1"  # bản package skill này cần (đồng bộ với EzgQaProbe.VERSION)
# Tính năng probe mới hơn PROBE_MIN_VERSION: build có probe cũ thì lệnh báo `unsupported`, không lỗi.
PROBE_FEATURES = {"pseudoloc": "1.3.0", "flags": "1.3.0", "screenBaseTypes": "1.3.0"}
AUTOTEST_PACKAGE = "com.ezg.autotest"
AUTOTEST_MIN_VERSION = "0.2.0"  # bản autotest có liveness filter + report.json chuỗi — thấp hơn chỉ cảnh báo (nhiễu)
PROBE_OVERRIDE = os.path.join("Assets", "Resources", "EzgQaProbeConfig.json")
CAPTURE_HIDE_SECONDS = 3  # nút nổi Bug Logger ẩn tối đa chừng này quanh một lần adb/simctl chụp (probe tự hiện lại khi hết)
LEGACY_PROBE_MARK = "FILE DO TOOL QUẢN LÝ"  # bản v1.0/1.1 chép vào Assets/EzgQaProbe/ — nay thay bằng package
REPORTER = "auto-test-device"

SEVERITIES = ("critical", "high", "medium", "low")
BUG_TYPES = ("crash", "logic", "visual", "ux", "perf", "gd")
STOP_SETTLE_SECONDS = 2.0  # `app restart|stop` mặc định: ở Home chừng này giây (game kịp OnApplicationPause → lưu) rồi mới tắt
OVERLAY_TYPES = ("APPLICATION_OVERLAY", "SYSTEM_ALERT", "SYSTEM_OVERLAY", "SYSTEM_ERROR", "PHONE")
OVERLAY_CACHE_SECONDS = 60  # danh sách overlay app khác: dò lại tối đa 1 lần/phút (dumpsys input chậm)
VALUE_PRINT_MAX = 1500      # `call` / `state` in giá trị tối đa chừng này ký tự (--full / --out để lấy trọn)
AD_CLOSE_WORDS = ("close", "skip", "dismiss", "dong", "bo qua")  # đã bỏ dấu, chữ thường
AD_CLOSE_MARKS = ("x", "×", "✕", "✖", "╳")
# Thử lại lỗi API (skill /auto-clear, hook StopFailure — hook tự chờ 30 s rồi gõ prompt này). ASCII, một dòng, ≤ 200 ký tự.
API_RETRY_PROMPT = ("Continue /auto-test-device: the previous turn stopped on an API error - resume the exact step "
                    "in progress (QA session still open).")
RETRY_EVERY_S = 30       # `retry`: chạy lại lệnh lỗi mỗi chừng này giây
RETRY_MAX_WAIT_S = 540   # `retry`: một lần gọi thử tối đa chừng này giây (vừa timeout 600 s của Bash tool)
RETRY_PENDING_RC = 75    # `retry` hết giờ mà lệnh vẫn lỗi — gọi lại `retry`, không dừng phiên
RETRY_FATAL_RC = (2, 4, 130)  # sai cú pháp · BugHub chưa setup · Ctrl-C: thử lại vô ích
EXPECT_TIMEOUT_S = 6.0     # `tap --expect`: chờ kỳ vọng xuất hiện tối đa chừng này giây
AUTO_AUDIT = True          # tự audit khi màn đổi sau thao tác (flag toàn cục --no-auto-audit tắt)
# Cờ `flags[]` (camelCase) của probe ≥1.3 — check port từ UiAuditUtil của com.ezg.autotest: cờ → (kind, mức, thông điệp).
# Cờ lạ (probe mới hơn skill) → kind kebab-case cùng tên, mức medium. flagDetail{cờ: chi tiết} được nối vào thông điệp.
PROBE_FLAGS_13 = {
    "deadButton": ("dead-button", "medium", "nút bấm được nhưng onClick rỗng và không có handler chạm (nút chết?)"),
    "noRaycastTarget": ("no-raycast-target", "high", "phần tử tương tác không có Graphic nào bật Raycast Target — không bấm được"),
    "overlapButton": ("overlap-button", "medium", "hai nút cùng cha chồng lên nhau >50% — bấm trúng nút khác"),
    "missingSprite": ("sprite-missing-colored", "low", "Image không có sprite, đang hiện khối màu (thiếu art hay cố ý?)"),
    "solidBackground": ("solid-background", "info", "Image không sprite phủ ≥50% màn (nền trơn/backdrop)"),
}
# Cờ probe 1.2.1 đã có nhánh xử lý riêng trong audit_elements (không map qua PROBE_FLAGS_13).
PROBE_FLAG_ALIASES = {"overflow", "truncated", "missingGlyph", "noSprite", "missingMaterial", "outsideContainer", "interactable"}

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import qaprofile  # noqa: E402  — phần thuần của hồ sơ QA (schema qa, tự dò quy ước, charter, coverage, CSV, autotest)

QA_DEFAULTS = qaprofile.QA_DEFAULTS

if hasattr(sys.stdout, "reconfigure"):
    try:
        sys.stdout.reconfigure(encoding="utf-8", errors="replace")
        sys.stderr.reconfigure(encoding="utf-8", errors="replace")
    except Exception:
        pass


# ───────────────────────────────────────────────────────────── tiện ích chung

class QaError(Exception):
    """Lỗi có thông điệp cho agent đọc (in ra rồi thoát mã 1)."""


class NotSupported(QaError):
    pass


def _dec(b):
    if b is None:
        return ""
    if isinstance(b, str):
        return b
    return b.decode("utf-8", "replace")


def run(cmd, timeout=60, binary=False, input_bytes=None, env=None, cwd=None):
    """Chạy lệnh, không bao giờ ném: trả (returncode, stdout, stderr). Timeout → 124, không có lệnh → 127."""
    try:
        p = subprocess.run(cmd, stdout=subprocess.PIPE, stderr=subprocess.PIPE, timeout=timeout,
                           input=input_bytes, env=env, cwd=cwd)
    except subprocess.TimeoutExpired as e:
        return 124, (e.stdout or b"") if binary else _dec(e.stdout), "timeout sau %ss" % timeout
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
    """Chạy tiến trình nền tách khỏi shell hiện tại, stdout ghi nối vào out_path. Trả pid."""
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


# ───────────────────────────────────────────────────────────── tìm công cụ

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


# ───────────────────────────────────────────────────────────── ảnh

def ensure_pillow():
    """Thiếu Pillow → tự `pip install --user pillow` một lần. Trả dòng báo ("" khi đã có sẵn)."""
    global Image, ImageChops, ImageDraw, ImageFont, ImageStat
    if Image is not None:
        return ""
    cmd = [sys.executable, "-m", "pip", "install", "--user", "--quiet", "--disable-pip-version-check", "pillow"]
    rc, _, err = run(cmd, timeout=300)
    if rc != 0 and "externally-managed" in (err or ""):
        rc, _, err = run(cmd + ["--break-system-packages"], timeout=300)  # chỉ ghi vào user site
    try:
        import importlib
        import site
        importlib.invalidate_caches()
        us = site.getusersitepackages()
        if us and us not in sys.path:
            sys.path.append(us)
        from PIL import Image as _im, ImageChops as _ic, ImageDraw as _id, ImageFont as _if, ImageStat as _is
        Image, ImageChops, ImageDraw, ImageFont, ImageStat = _im, _ic, _id, _if, _is
        return "Pillow: đã tự cài"
    except Exception:
        return "Pillow: không tự cài được (%s) — vẫn chạy, thiếu ảnh thu nhỏ/lưới toạ độ/so khung" % clip((err or "").strip(), 160)


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
    """Ảnh xem nhanh (JPEG, cạnh dài ≤ long_edge). Trả ((vw, vh), scale). Không Pillow → dùng nguyên ảnh."""
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
    """Màn đen/trắng trơn, mảng hồng magenta (shader lỗi). Trả list cảnh báo."""
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
            warns.append("màn hình gần như ĐEN hoàn toàn (mean=%.1f) — treo/không render?" % mean)
        elif mean > 247 and std < 4:
            warns.append("màn hình gần như TRẮNG trơn (mean=%.1f) — thiếu render/loading kẹt?" % mean)
        px = list(small.getdata())
        magenta = sum(1 for r, g, b in px if r > 235 and g < 40 and b > 235)
        ratio = magenta / float(len(px) or 1)
        if ratio > 0.004:
            warns.append("có mảng màu HỒNG magenta (%.1f%% ảnh) — thường là shader/material lỗi" % (ratio * 100))
    return warns


def diff_ratio(a_path, b_path):
    """(tỉ lệ pixel đổi, bbox vùng đổi theo toạ độ ảnh b) — None nếu không so được."""
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
    """marks: list (id, (x, y, w, h) view px, màu) — vẽ khung + nhãn id."""
    with Image.open(view_path) as im:
        im = im.convert("RGB")
        d = ImageDraw.Draw(im, "RGBA")
        font = _font(max(11, im.size[0] // 50))
        for mid, (x, y, w, h), color in marks:
            d.rectangle([x, y, x + w, y + h], outline=color + (230,), width=2)
            _label(d, (x + 2, y + 1), str(mid), font, bg=color + (200,))
        im.save(out_path, "JPEG", quality=84)


# ───────────────────────────────────────────────────────────── phân tích log

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
    """Gộp các dòng logcat liên tiếp cùng (ts, pid, tid, level, tag) — Android tách message nhiều dòng như vậy."""
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
    """Biến record logcat thành event lỗi: crash Java/native, ANR, app chết, LMK, exception/error Unity, thiếu glyph."""
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
    """Log iOS (simctl log stream / devicectl --console): dò exception/crash theo từ khoá, lấy kèm vài dòng sau."""
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

# ───────────────────────────────────────────────────────────── overlay app khác + view native (Android)

def parse_overlay_windows(window_dump, game_pkg=""):
    """`dumpsys window windows` → cửa sổ nổi của app BÊN THỨ BA: [{token, package, uid, type}].
    Bỏ cửa sổ hệ thống (uid < 10000: systemui, ROM) và của chính game."""
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
    """`dumpsys input` → {token cửa sổ: {"rect": [x, y, w, h] px máy, "visible": bool}} theo vùng chạm thật
    (touchableRegion, không có thì frame). Lấy dòng đầu của mỗi token (danh sách cửa sổ hiện tại)."""
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

    # Mỗi cửa sổ là một mục "N: name=<token> …" — tuỳ bản Android có/không nháy, một dòng hoặc kéo sang dòng thụt sâu hơn.
    tok, indent, buf = None, 0, []
    for line in (input_dump or "").splitlines():
        m = re.match(r"(\s*)\d+: name='?(?:Window\{)?(\w+) ", line)  # API 35+: name=abc Foo, — bản cũ có nháy
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
    """Overlay app khác ĐANG nhận chạm: [{package, type, rect:[x,y,w,h] px máy}]."""
    regions = parse_input_regions(input_dump)
    out = []
    for w in parse_overlay_windows(window_dump, game_pkg):
        r = regions.get(w["token"])
        if r and r["visible"] and r["rect"][2] > 0 and r["rect"][3] > 0:
            out.append(dict(w, rect=r["rect"]))
    return out


def point_in_rects(x, y, items):
    """Phần tử đầu tiên (có "rect" [x,y,w,h]) chứa điểm (x, y); None nếu không có."""
    for it in items or []:
        rx, ry, rw, rh = it["rect"]
        if rx <= x < rx + rw and ry <= y < ry + rh:
            return it
    return None


def uncovered_point(rect, covers, inset=0.2):
    """Điểm trong `rect` (bỏ viền `inset` — nút thật thường nhỏ hơn bounds) không nằm dưới `covers`; None nếu bị che kín."""
    x, y, w, h = rect
    for fy in (0.5, 0.35, 0.65, inset, 1 - inset):
        for fx in (0.5, 0.35, 0.65, inset, 1 - inset):
            px, py = int(x + w * fx), int(y + h * fy)
            if not point_in_rects(px, py, covers):
                return px, py
    return None


def parse_uia_nodes(xml):
    """XML của `uiautomator dump` → [{text, desc, id, cls, pkg, clickable, enabled, rect}]."""
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
    """Nhãn khớp nút đóng/skip quảng cáo ("" nếu không khớp)."""
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
    """Nút đóng/skip quảng cáo, tốt nhất trước: bấm được > nhỏ (nút X) > nằm cao."""
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
    """Bug đã log trong máy trùng bug sắp gửi → (dòng index, lý do); không trùng → (None, "").
    Trùng khi: cùng chữ ký log · tiêu đề giống ≥ 75% · cùng màn + cùng loại + tiêu đề giống ≥ 40%."""
    code = (code or "").upper()
    for b in rows or []:
        if (b.get("code") or "").upper() != code:
            continue
        if log_sig and b.get("logSig") == log_sig:
            return b, "cùng chữ ký log %s" % log_sig
        sim = similar(b.get("title", ""), title)
        if sim >= 0.75:
            return b, "tiêu đề giống %d%%" % round(sim * 100)
        if screen and btype and b.get("screen") == screen and b.get("type") == btype and sim >= 0.4:
            return b, "cùng màn %s, cùng loại %s, tiêu đề giống %d%%" % (clip(screen, 60), btype, round(sim * 100))
    return None, ""


def format_value(prefix, value, limit=VALUE_PRINT_MAX, full=False):
    """Dòng in kết quả `call`/`state`; quá dài thì cắt và NÓI RÕ là đã cắt (JSON cụt không parse được)."""
    s = json.dumps(value, ensure_ascii=False)
    if full or len(s) <= limit:
        return "%s → %s" % (prefix, s)
    return "%s → %s… [đã cắt %d/%d ký tự — thêm --full hoặc --out <file>]" % (prefix, s[:limit], limit, len(s))


class ProbeError(QaError):
    def __init__(self, msg, status=0, body=None):
        QaError.__init__(self, msg)
        self.status = status
        self.body = body or {}


class Probe:
    """Client HTTP của EZG QA Probe (chạy trong game). Mọi request mang header X-QA-Token."""

    def __init__(self, base, token, timeout=15, reconnect=None):
        self.base = base.rstrip("/")
        self.token = token
        self.timeout = timeout
        self.reconnect = reconnect  # () -> bool: dựng lại đường vào (adb forward) khi bị từ chối kết nối
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
            # adb server khởi động lại (Unity/Android Studio build) làm mất mọi forward → dựng lại rồi thử một lần nữa.
            if _retry and self.reconnect and "refused" in str(e).lower() and self.reconnect():
                return self.request(method, endpoint, params, data, timeout, raw, _retry=False)
            raise ProbeError("probe không trả lời (%s)" % e, 0)
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


# ───────────────────────────────────────────────────────────── thiết bị

class Device(object):
    kind = "?"
    platform = "?"          # RuntimePlatform của Unity: Android / IPhonePlayer
    native_input = True

    def __init__(self, ident):
        self.id = ident
        self.probe = None

    def describe(self):
        return self.id

    # Các thao tác — lớp con override; mặc định không hỗ trợ.
    def screenshot_png(self):
        raise NotSupported("thiết bị này không tự chụp được — cần probe")

    def tap(self, x, y):
        raise NotSupported("thiết bị này không có input gốc — cần probe")

    def swipe(self, x1, y1, x2, y2, ms):
        raise NotSupported("thiết bị này không có input gốc — cần probe")

    def longpress(self, x, y, ms):
        self.swipe(x, y, x, y, ms)

    def drag(self, x1, y1, x2, y2, ms, hold_ms):
        self.swipe(x1, y1, x2, y2, ms + hold_ms)

    def text(self, s):
        raise NotSupported("nhập chữ không hỗ trợ trên thiết bị này — dùng --probe")

    def key(self, name):
        raise NotSupported("phím '%s' không hỗ trợ trên %s" % (name, self.kind))

    def network(self, mode):
        raise NotSupported("bật/tắt mạng không hỗ trợ trên %s" % self.kind)

    def rotate(self, mode):
        raise NotSupported("xoay màn không hỗ trợ trên %s" % self.kind)

    def perf(self, pkg):
        return {}

    def background(self, pkg, secs):
        raise NotSupported("đưa app xuống nền không hỗ trợ trên %s" % self.kind)

    def clear_data(self, pkg):
        raise NotSupported("xoá dữ liệu app không hỗ trợ trên %s" % self.kind)

    def stop_like_player(self, pkg, settle):
        """Tắt như người chơi vuốt khỏi đa nhiệm: về Home trước (game nhận OnApplicationPause → lưu) rồi mới tắt.
        Thiết bị không có Home → NotSupported, caller rơi về kill thẳng."""
        self.key("home")
        time.sleep(max(0.5, settle))
        self.stop(pkg)

    def overlays(self, pkg=""):
        """Cửa sổ nổi của app khác đè lên màn (chỉ Android dò được)."""
        return []


class Android(Device):
    kind = "android"
    platform = "Android"

    def __init__(self, serial, adb=None):
        Device.__init__(self, serial)
        self.adb_path = adb or find_adb()
        if not self.adb_path:
            raise QaError("không tìm thấy adb (cài Android SDK platform-tools hoặc Unity Android module)")

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
            raise QaError("không chụp được màn hình (%s) — máy khoá màn/mất kết nối?" % (err or "screencap lỗi").strip())
        return data

    def tap(self, x, y):
        self._input("tap %d %d" % (x, y))

    def tap_many(self, x, y, times, interval):
        """Chạm liên tiếp trong MỘT lệnh adb (nhanh như người bấm dồn). Cách nhau ≥ 70 ms: Unity gộp các chạm
        trong cùng một frame thành một, bấm sát hơn thì game chỉ thấy một lần."""
        cmd = ("; sleep %.2f; " % max(interval, 0.07)).join("input tap %d %d" % (x, y) for _ in range(times))
        rc, out, err = self.sh(cmd, timeout=60)
        if rc != 0:
            raise QaError("adb input lỗi: %s" % (err or out).strip())

    def swipe(self, x1, y1, x2, y2, ms):
        self._input("swipe %d %d %d %d %d" % (x1, y1, x2, y2, ms))

    def drag(self, x1, y1, x2, y2, ms, hold_ms):
        if hold_ms <= 0:
            return self.swipe(x1, y1, x2, y2, ms)
        # Giữ tay rồi mới kéo (kéo-thả item, kéo đường): motionevent có từ Android 10-11.
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
            raise NotSupported("adb `input text` chỉ gõ được ASCII — dùng `text --probe` (build có probe) cho tiếng Việt/emoji")
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
            raise QaError("phím không biết: %s (có: %s)" % (name, ", ".join(sorted(self.KEYS))))
        self._input("keyevent %s" % code)

    def _input(self, args):
        rc, out, err = self.sh("input %s" % args, timeout=30)
        if rc != 0:
            raise QaError("adb input lỗi: %s" % (err or out).strip())

    def wake(self):
        self.sh("input keyevent KEYCODE_WAKEUP; wm dismiss-keyguard", timeout=10)

    def installed(self, pkg):
        rc, out, _ = self.sh("pm path %s" % pkg, timeout=15)
        return "package:" in out

    def install(self, path):
        rc, out, err = self.adb("install", "-r", "-d", "-g", path, timeout=600)
        if rc != 0 and not (out + err).strip():  # adb server vừa bị khởi động lại (vd Unity đang build Android) → thử lại
            time.sleep(2)
            self.adb("wait-for-device", timeout=60)
            rc, out, err = self.adb("install", "-r", "-d", "-g", path, timeout=600)
        if rc != 0 or "Success" not in out:
            raise QaError("cài APK lỗi: %s" % (out + err).strip()[-400:])

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
            raise QaError("mở app lỗi: %s" % (out + err).strip()[-300:])
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
        """Bong bóng / cửa sổ nổi của app BÊN THỨ BA (ghi âm, chat head, ghi màn hình…) — adb chạm vào vùng đó là
        trúng app kia chứ không phải game. Ghép loại cửa sổ (dumpsys window) với vùng chạm thật (dumpsys input)."""
        rc1, wd, _ = self.sh("dumpsys window windows", timeout=30)
        rc2, idump, _ = self.sh("dumpsys input", timeout=30)
        return overlay_rects(wd, idump, pkg)

    def uia_dump(self):
        """Cây view native (uiautomator) — để thấy nút của Activity quảng cáo / hệ thống mà probe Unity không thấy."""
        path = "/sdcard/ezg_qa_uia.xml"
        for _ in range(3):
            rc, out, _ = self.sh("uiautomator dump %s >/dev/null 2>&1; cat %s; rm -f %s" % (path, path, path), timeout=45)
            if "<hierarchy" in out:
                return out
            time.sleep(1.0)
        raise QaError("uiautomator dump không lấy được cây view (màn đang chạy animation liên tục?) — nhìn ảnh rồi tap tay")

    def clear_data(self, pkg):
        rc, out, err = self.sh("pm clear %s" % pkg, timeout=60)
        if "Success" not in out:
            raise QaError("pm clear lỗi: %s" % (out + err).strip())

    def background(self, pkg, secs, token=None, port=None):
        """Đưa app xuống nền rồi mở lại. token: gửi kèm intent mới (Unity setIntent trong onNewIntent) để probe đang
        chạy đổi sang token của phiên này — đường duy nhất trên Android 11+ (app không đọc được file do adb ghi)."""
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
        plane = "BẬT" if lines and lines[0].strip() == "1" else "TẮT"
        net = lines[1].split(":", 1)[-1].strip() if len(lines) > 1 else "?"
        return "chế độ máy bay %s · mạng mặc định: %s" % (plane, "không có" if net in ("none", "") else net)

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
                if idx is not None and cols[idx].startswith("S["):  # toybox gộp cột S và %CPU trong header
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
        """Dựng lại đúng forward cũ (cổng máy tính giữ nguyên để base URL của phiên còn đúng)."""
        rc, _, _ = self.adb("forward", "tcp:%d" % local_port, "tcp:%d" % device_port, timeout=15)
        return rc == 0

    def probe_client(self, base, token, device_port):
        local = int(base.rsplit(":", 1)[-1])
        return Probe(base, token, reconnect=lambda: self.reforward(local, device_port))

    def unforward(self, local_port):
        if local_port:
            self.adb("forward", "--remove", "tcp:%d" % local_port, timeout=10)

    def unforward_probe(self, base_port=PROBE_PORT):
        """Gỡ mọi forward của máy này trỏ vào cổng probe (kể cả forward sót từ phiên trước)."""
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

    # ── môi trường (ma trận env / aspect) — mỗi hàm trả giá trị TRƯỚC để close_session hoàn nguyên ──
    def _setting(self, ns, key, value=None):
        rc, out, _ = self.sh("settings get %s %s" % (ns, key), timeout=10)
        prev = out.strip()
        if value is not None:
            rc, out, err = self.sh("settings put %s %s %s" % (ns, key, value), timeout=10)
            if rc != 0 or "Exception" in out + err:
                raise NotSupported("settings put %s %s lỗi: %s" % (ns, key, (err or out).strip()[:120]))
        return prev if prev not in ("null", "") else None

    def font_scale(self, value):
        """value: số (1.0 = mặc định, 1.3 = Lớn) hoặc None để đọc. Trả giá trị trước."""
        return self._setting("system", "font_scale", None if value is None else ("%.2f" % float(value)).rstrip("0").rstrip("."))

    def dark_mode(self, on):
        rc, out, _ = self.sh("cmd uimode night", timeout=10)
        prev = "yes" if "yes" in out.lower() else "no"
        rc, out, err = self.sh("cmd uimode night %s" % ("yes" if on else "no"), timeout=15)
        if rc != 0 or "Error" in out + err or "unknown command" in (out + err).lower():
            raise NotSupported("máy không hỗ trợ `cmd uimode night` (Android < 10)")
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
            raise NotSupported("am send-trim-memory lỗi: %s" % (err or out).strip()[:120])
        return level

    def wm_size(self, spec):
        """spec 'WxH' | 'reset'. Trả 'Override size' trước (None = chưa override)."""
        rc, out, _ = self.sh("wm size", timeout=10)
        m = re.search(r"Override size:\s*(\d+x\d+)", out)
        prev = m.group(1) if m else None
        rc, out, err = self.sh("wm size %s" % spec, timeout=15)
        if rc != 0 or "Error" in out + err or "Exception" in out + err:
            raise NotSupported("wm size %s lỗi: %s" % (spec, (err or out).strip()[:120]))
        return prev

    def wm_density(self, spec):
        rc, out, _ = self.sh("wm density", timeout=10)
        m = re.search(r"Override density:\s*(\d+)", out)
        prev = m.group(1) if m else None
        self.sh("wm density %s" % spec, timeout=15)
        return prev

    def set_clock(self, delta_s):
        """Dời đồng hồ máy (emulator/máy root): tắt auto_time rồi `date -s`. delta_s=None → bật lại auto_time."""
        if delta_s is None:
            self._setting("global", "auto_time", "1")
            self.sh("su 0 cmd alarm set-time $(( $(date +%s) * 1000 )) 2>/dev/null; true", timeout=10)
            return "auto"
        self._setting("global", "auto_time", "0")
        rc, out, _ = self.sh("date +%s", timeout=10)
        try:
            now = int(out.strip())
        except ValueError:
            raise NotSupported("không đọc được giờ máy")
        target = now + int(delta_s)
        for cmd in ("su 0 date -s @%d" % target, "date -s @%d" % target,
                    "su 0 date %s" % dt.datetime.fromtimestamp(target).strftime("%m%d%H%M%Y.%S"),
                    "date %s" % dt.datetime.fromtimestamp(target).strftime("%m%d%H%M%Y.%S")):
            rc, out, err = self.sh(cmd, timeout=10)
            if rc == 0 and "denied" not in (out + err).lower() and "not found" not in (out + err).lower() and "cannot" not in (out + err).lower():
                return "%+ds" % int(delta_s)
        raise NotSupported("không đổi được giờ máy (cần emulator hoặc máy root) — thay bằng cheat tua thời gian của game")

    def set_locale(self, tag):
        """Đổi ngôn ngữ hệ thống: emulator/root (persist.sys.locale + khởi động lại framework ~20 s). Trả locale trước."""
        prev = self.getprop("persist.sys.locale") or self.getprop("ro.product.locale") or ""
        rc, out, err = self.sh("su 0 setprop persist.sys.locale %s || setprop persist.sys.locale %s" % (tag, tag), timeout=10)
        if rc != 0 or "denied" in (out + err).lower():
            raise NotSupported("không đổi được locale (cần emulator/root) — đổi tay trong Settings máy")
        self.sh("su 0 sh -c 'stop; start' || (stop; start)", timeout=20)
        time.sleep(15)
        self.adb("wait-for-device", timeout=60)
        return prev

    def build_info_from_apk(self, pkg):
        """Đọc StreamingAssets/BugLogger/buglogger_build.json trong APK (commit/branch bake lúc build)."""
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
            raise QaError("simctl screenshot lỗi: %s" % err.strip())

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
            raise NotSupported("Simulator cần `idb` cho chạm/vuốt (brew install facebook/fb/idb-companion && pip3 install fb-idb) hoặc build có probe")
        rc, out, err = run([self.idb] + list(args) + ["--udid", self.id], timeout=30)
        if rc != 0:
            raise QaError("idb lỗi: %s" % (err or out).strip())

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
        raise NotSupported("iOS không có phím '%s' (không có Back) — bấm nút đóng/back trong game" % name)

    def installed(self, bundle):
        return self.simctl("get_app_container", self.id, bundle, timeout=20)[0] == 0

    def install(self, path):
        rc, out, err = self.simctl("install", self.id, path, timeout=600)
        if rc != 0:
            raise QaError("simctl install lỗi: %s" % err.strip())

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
            raise QaError("simctl launch lỗi: %s" % err.strip())
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
        # Không cần idb: mở Settings đẩy game xuống nền (applicationWillResignActive → lưu) rồi mới terminate.
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

    # ── môi trường: Simulator chỉ đổi được giao diện tối/sáng + cỡ chữ (Dynamic Type) ──
    IOS_CONTENT_SIZE = {1.0: "medium", 1.15: "large", 1.3: "extra-large", 1.5: "extra-extra-large", 1.7: "extra-extra-extra-large",
                        2.0: "accessibility-medium"}

    def dark_mode(self, on):
        rc, out, _ = self.simctl("ui", self.id, "appearance", timeout=10)
        prev = out.strip() or "light"
        rc, out, err = self.simctl("ui", self.id, "appearance", "dark" if on else "light", timeout=10)
        if rc != 0:
            raise NotSupported("simctl ui appearance lỗi: %s" % (err or out).strip()[:120])
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
            raise NotSupported("simctl ui content_size lỗi (cần Xcode ≥ 15): %s" % (err or out).strip()[:120])
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
            raise QaError("devicectl install lỗi: %s" % err.strip()[-300:])

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
            raise QaError("devicectl launch lỗi: %s" % (err or out).strip()[-300:])
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
    """Unity Editor đang Play mode — mọi thứ qua probe (trigger file trong persistentDataPath của Editor)."""
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
        raise QaError("không tìm thấy binary emulator của Android SDK để boot AVD %s" % name)
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
        raise QaError("emulator %s không lên sau 240s" % name)
    while time.time() - t0 < 300:
        rc, out, _ = run([adb, "-s", serial, "shell", "getprop", "sys.boot_completed"], timeout=10)
        if out.strip() == "1":
            return serial
        time.sleep(2)
    raise QaError("emulator %s chưa boot xong sau 300s" % name)


def resolve_device(spec, headless=True):
    """`<device id>` → Device. Nhận: serial adb, tên AVD (tự boot), UDID/tên Simulator (tự boot), id/tên iPhone, auto."""
    spec = (spec or "auto").strip()
    if spec.lower() in ("editor", "unity-editor"):
        return UnityEditorTarget()
    adb = find_adb()
    androids = list_android(adb)
    low = spec.lower()
    for d in androids:
        if d["id"] == spec:
            if d["state"] != "device":
                raise QaError("thiết bị %s đang ở trạng thái '%s' (unauthorized = bấm Allow USB debugging trên máy)" % (spec, d["state"]))
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
                print("Không có máy nào đang cắm — tự boot AVD %s" % avds[0])
                return Android(boot_avd(adb, avds[0], headless), adb)
            raise QaError("không có thiết bị nào đang kết nối. Cắm máy Android (bật USB debugging) / boot emulator / Simulator, "
                          "hoặc truyền tên AVD để tự boot%s. `devices` để xem danh sách." % (
                              (" (có: %s)" % ", ".join(avds)) if avds else ""))
        raise QaError("có %d thiết bị — chỉ rõ id: %s" % (len(cands), ", ".join(d["id"] for _, d in cands)))
    for d in androids:
        if d["model"].lower().replace("_", " ") == low.replace("_", " ") and d["state"] == "device":
            return Android(d["id"], adb)
    raise QaError("không tìm thấy thiết bị '%s'. Chạy `devices` để xem id hợp lệ." % spec)


# ───────────────────────────────────────────────────────────── phiên

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
            raise QaError("chưa có phiên — chạy `start <device> --feature \"...\"` trước")
        path = os.path.join(root, "sessions", sid)
        data = read_json(os.path.join(path, "session.json"))
        if not data:
            raise QaError("không đọc được phiên %s" % sid)
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
        raise QaError("%s cần EZG QA Probe trong build (start báo 'probe: không có'). Project cần package %s (`start` tự thêm "
                      "vào Packages/manifest.json) rồi build lại; tạm thời test hộp đen bằng ảnh + toạ độ." % (what, PROBE_PACKAGE))
    return dev.probe


# ───────────────────────────────────────────────────────────── log nền

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
    """Đọc log mới từ lần kiểm trước → event mới (chưa thấy chữ ký). Cập nhật issues.json. Trả (mới, số lặp).
    defer=True: không trả lỗi mới mà để dành cho lần in kế tiếp (dùng khi đang chờ probe, không muốn mất cảnh báo)."""
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
    # App chết do chính mình stop/restart thì bỏ qua.
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
        print("log: không có lỗi mới")
        return
    if fresh:
        print("⚠ LOG: %d lỗi MỚI" % len(fresh))
        for e in fresh:
            print("  [%s] %s sig=%s · %s" % (e["severity"], e["kind"], e["sig"], clip(e["first"], 220)))
            for l in e.get("lines", [])[1:verbose_lines]:
                print("      " + clip(l.strip(), 200))
    if repeats:
        print("log: +%d lần lặp của lỗi đã ghi (xem `logs --all`)" % repeats)


# ───────────────────────────────────────────────────────────── chụp + quan sát

@contextlib.contextmanager
def bug_logger_hidden(dev):
    """Ẩn UI Bug Logger trong game (nút nổi báo lỗi, toast — probe /capturehidden) quanh lúc adb/simctl chụp, để ảnh xem
    và ảnh gửi BugHub không dính nút. Không có probe / game không có Bug Logger / probe cũ → chụp như thường."""
    pr = getattr(dev, "probe", None)
    hidden = False
    if pr is not None and getattr(pr, "capture_hidden", True):
        try:
            hidden = bool(pr.get("/capturehidden", timeout=5, sec=CAPTURE_HIDE_SECONDS).get("supported"))
            if not hidden:
                pr.capture_hidden = False  # game không có Bug Logger đủ mới — khỏi hỏi lại mỗi lần chụp
        except ProbeError as e:
            if e.status == 404:
                pr.capture_hidden = False  # probe < 1.2.1 chưa có endpoint
    try:
        yield
    finally:
        if hidden:
            try:
                pr.get("/capturehidden", timeout=5, sec=0)
            except QaError:
                pass  # probe tự hiện lại nút khi hết CAPTURE_HIDE_SECONDS


def grab_png(sess, dev):
    try:
        with bug_logger_hidden(dev):
            return dev.screenshot_png()
    except NotSupported:
        pr = need_probe(dev, "Chụp màn hình trên thiết bị này")
        data = pr.request("GET", "/shot", params={"fmt": "png"}, timeout=30, raw=True)
        return data


def observe(sess, dev, label="", grid=False, marks=False, logs=True, compare=True, after_action=False):
    """Chụp → lưu PNG gốc + ảnh xem JPEG; so với ảnh trước; audit ảnh; kiểm log mới. In tóm tắt cho agent."""
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
    line = "shot #%03d %s  (%dx%d, 1 px ảnh = %.2f px máy)" % (n, shown, vw, vh, 1.0 / scale if scale else 1)
    if compare and prev:
        dr = diff_ratio(prev, view)
        if dr is not None:
            ch, box = dr
            line += "\n  Δ so với ảnh trước: %.1f%%" % (ch * 100) + (" vùng %s" % (box,) if box and ch > 0.001 else "")
            if ch < 0.002 and after_action:
                line += "  ← GẦN NHƯ KHÔNG ĐỔI (thao tác không có phản hồi? màn treo?)"
    print(line)
    for w in analyze_image(view):
        print("  ⚠ ẢNH: " + w)
    if logs:
        fresh, rep = check_logs(sess, dev)
        print_issues(fresh, rep)
        fg = ""
        try:
            fg = dev.foreground()
        except Exception:
            pass
        if fg and fg != sess.d["package"] and sess.d["device"]["kind"] == "android":
            print("  ⚠ app đang ở foreground: %s (không phải game — quảng cáo/trình duyệt/hệ thống?)" % fg)
    return sess.d["view"]


def to_device_xy(sess, x, y, space="view"):
    """Toạ độ agent đưa (ảnh xem / % / px máy) → px máy."""
    v = sess.d.get("view") or {}
    sw, sh = v.get("shot_w"), v.get("shot_h")
    if not sw:
        raise QaError("chưa có ảnh nào — chụp `shot` trước để biết toạ độ")

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
    """px máy → px Unity (Screen) theo kích thước probe báo."""
    pr = sess.d.get("probe") or {}
    v = sess.d.get("view") or {}
    uw, uh = pr.get("screen_w"), pr.get("screen_h")
    if not (uw and uh and v.get("shot_w")):
        return 1.0, 1.0
    return uw / float(v["shot_w"]), uh / float(v["shot_h"])


# ───────────────────────────────────────────────────────────── UI qua probe

def probe_ui(sess, dev, include_all=False):
    pr = need_probe(dev, "Đọc cây UI")
    j = pr.get("/ui", all=1 if include_all else 0, timeout=20)
    sess.d.setdefault("probe", {}).update({"screen_w": j.get("screen", {}).get("w"), "screen_h": j.get("screen", {}).get("h")})
    sess.d["screens"] = {"scene": j.get("scene"), "stack": list(j.get("screens") or []), "at": now_iso()}
    sx, sy = unity_scale(sess)
    els = j.get("elements", [])
    for e in els:
        x, y, w, h = e.get("rect", [0, 0, 0, 0])
        # Unity px (gốc trên-trái) → px máy
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
HASHKEY_RE = re.compile(r"^#[A-Za-z_][\w.]*$")  # key localize lộ dạng #shop_title
PLACEHOLDER_RE = re.compile(r"^(new text|text|button|label|textmeshpro|text \(tmp\)|lorem.*|placeholder|todo|tbd|xxx+|\?\?+|-|abc|test)$", re.I)
BAD_VALUE_RE = re.compile(r"\b(NaN|Infinity|-Infinity|null|undefined)\b|System\.\w+|UnityEngine\.\w+|\{\d\}|%[sd]\b|<color=[^>]*>[^<]*$")


def audit_text(text):
    """Heuristic chữ hiển thị sai: key localize lộ ra, placeholder, NaN/null/{0}, tên type .NET."""
    t = (text or "").strip()
    if not t:
        return None
    plain = re.sub(r"<[^>]+>", "", t).strip()
    if BAD_VALUE_RE.search(t):
        return ("high", "chữ chứa giá trị lỗi/format chưa thay (%s)" % clip(plain, 60))
    if PLACEHOLDER_RE.match(plain):
        return ("medium", "chữ placeholder chưa thay: \"%s\"" % clip(plain, 40))
    if len(plain) >= 5 and " " not in plain and (KEY_RE.match(plain) or UKEY_RE.match(plain) or HASHKEY_RE.match(plain)):
        return ("medium", "có vẻ lộ KEY localize thay vì chữ đã dịch: \"%s\"" % clip(plain, 50))
    return None


def _inter(a, b):
    x1, y1 = max(a[0], b[0]), max(a[1], b[1])
    x2, y2 = min(a[0] + a[2], b[0] + b[2]), min(a[1] + a[3], b[1] + b[3])
    return max(0, x2 - x1) * max(0, y2 - y1)


def _is_ancestor(p, q):
    return bool(p and q) and (q.startswith(p + "/") or p.startswith(q + "/"))


def noise_match(rule, finding, screens=()):
    """Luật nhiễu qa.noise khớp finding? flag bằng tên (hoặc '*'); match = regex trên path hoặc 'screen:<tên>'."""
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
    """Đánh dấu finding khớp luật nhiễu: thêm "suppressed": "<lý do>" (giữ trong file, không in mặc định)."""
    for f in findings:
        for r in noise or []:
            if noise_match(r, f, screens):
                f["suppressed"] = r.get("reason") or r.get("match") or r.get("flag") or "noise"
                break
    return findings


def _flag_kind(flag):
    """Cờ của probe ≥1.3 (kebab-case hoặc camelCase) → tên finding kebab-case."""
    s = re.sub(r"(?<=[a-z0-9])(?=[A-Z])", "-", flag or "").lower()
    return re.sub(r"[^a-z0-9]+", "-", s).strip("-")


def audit_elements(ui, noise=None):
    """Audit visual trên dữ liệu probe → findings [{id, severity, kind, msg, path, text[, detail][, suppressed]}].
    noise: qa.noise của project — finding khớp được gắn `suppressed` (vẫn trả về để report đếm)."""
    els = ui.get("elements", [])
    scr = ui.get("screen", {})
    sw, sh = scr.get("w", 0), scr.get("h", 0)
    safe = scr.get("safe")  # [x, y, w, h] gốc trên-trái
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
        # Cờ mới của probe ≥1.3 (check port từ UiAuditUtil của com.ezg.autotest): mỗi cờ = một finding.
        details = e.get("flagDetail") or {}
        for fl in flags:
            if fl in PROBE_FLAG_ALIASES:
                continue
            kind, sev, msg = PROBE_FLAGS_13.get(fl) or (_flag_kind(fl), "medium", "probe báo cờ %s" % fl)
            det = details.get(fl) or ""
            add(e, sev, kind, (msg + (" — " + det if det else "")), det or None)
        if "overflow" in flags or "truncated" in flags:
            add(e, "medium", "text-overflow", "chữ bị tràn/cắt khỏi khung: \"%s\"" % clip(e.get("text", ""), 50))
        if "missingGlyph" in flags:
            add(e, "high", "missing-glyph", "font thiếu ký tự (hiện ô vuông): %s" % e.get("missing", ""))
        if "noSprite" in flags:
            add(e, "medium", "missing-sprite", "Image không có sprite nhưng đang hiện ô màu trắng (thiếu art?)")
        if "missingMaterial" in flags:
            add(e, "high", "missing-material", "Graphic mất material/shader")
        if "outsideContainer" in flags:
            add(e, "medium", "text-outside", "chữ \"%s\" vẽ tràn ra ngoài nền/nút '%s'" % (clip(e.get("text", ""), 40), e.get("container", "?")))
        a = audit_text(e.get("text"))
        if a:
            add(e, a[0], "text-content", a[1])
        if e.get("text") and r[2] > 0 and r[3] > 0:
            texts.append(e)
        if e.get("interactable"):
            if sw and sh:
                outside = r[2] * r[3] - _inter(r, [0, 0, sw, sh])
                if r[2] * r[3] and outside / float(r[2] * r[3]) > 0.15:
                    add(e, "medium", "offscreen", "nút nằm một phần ngoài màn hình")
                elif safe and safe[2] and safe[3] and (safe[2] < sw or safe[3] < sh):
                    out_safe = r[2] * r[3] - _inter(r, safe)
                    if r[2] * r[3] and out_safe / float(r[2] * r[3]) > 0.25:
                        add(e, "medium", "unsafe-area", "nút lấn vào vùng tai thỏ/thanh hệ thống (ngoài safe area)")
            big = sw and sh and r[2] * r[3] >= 0.5 * sw * sh  # nút nền/vùng chạm toàn màn: bị nội dung đè là bình thường
            if e.get("blockedBy") and not e.get("occluded") and not big:
                add(e, "medium", "blocked", "nút không bấm được — bị %s đè lên (overlay nhỏ/vô hình hoặc cùng màn; xem ảnh xác nhận)" % e["blockedBy"])
            if dpi and min(r[2], r[3]) < 6.0 / 25.4 * dpi:
                add(e, "low", "tiny-target", "vùng chạm nhỏ %dx%d px (<6mm) — khó bấm" % (r[2], r[3]))
    for i in range(len(texts)):
        for k in range(i + 1, len(texts)):
            a, b = texts[i], texts[k]
            # chỉ so chữ cùng một lớp màn (popup đè HUD không phải chồng chữ) và không cha-con
            if a.get("layer") != b.get("layer") or _is_ancestor(a.get("path"), b.get("path")):
                continue
            ra, rb = a.get("ink") or a["rect"], b.get("ink") or b["rect"]
            inter = _inter(ra, rb)
            small = min(ra[2] * ra[3], rb[2] * rb[3])
            if small and inter / float(small) > (0.15 if a.get("ink") and b.get("ink") else 0.3):
                add(a, "medium", "text-overlap", "chữ chồng lên chữ khác [%d] \"%s\"" % (b["id"], clip(b.get("text", ""), 30)))
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
            raise QaError("không thấy phần tử @%d (UI đã đổi?) — gọi lại `ui`" % ref)
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
        raise QaError("không tìm thấy phần tử %s trên màn hiện tại" % ("text=%r" % text if text is not None else "name=%r" % name))
    cands.sort(key=lambda c: c[0])
    return cands[0][1]


# ───────────────────────────────────────────────────────────── hồ sơ QA theo project (qa trong project-profile.json)

_QA_CACHE = {}


def qa_profile(root=None, refresh=False):
    """Block `qa` đã merge (QA_DEFAULTS ← template ← project). Cache theo process."""
    root = root or repo_root()
    if refresh or root not in _QA_CACHE:
        _QA_CACHE[root] = qaprofile.load_qa_profile(root)
    return _QA_CACHE[root]


def qa_path(root, key):
    return os.path.join(root, (qa_profile(root).get("paths") or {}).get(key) or QA_DEFAULTS["paths"][key])


def probe_supports(sess, feature):
    """Probe trong build có tính năng `feature` (PROBE_FEATURES) không — theo version /ping đã lưu."""
    v = (sess.d.get("probe") or {}).get("version") or ""
    need = PROBE_FEATURES.get(feature)
    return bool(need) and _is_semver(v) and _ver(v) >= _ver(need)


def unsupported_probe(sess, feature):
    v = (sess.d.get("probe") or {}).get("version") or "?"
    return "unsupported (probe v%s < %s — build lại với %s ≥ %s để có `%s`)" % (v, PROBE_FEATURES.get(feature), PROBE_PACKAGE,
                                                                          PROBE_FEATURES.get(feature), feature)


def screens_key(j):
    return "%s|%s" % (j.get("scene") or "", ">".join(j.get("screens") or []))


def record_observed(sess, j, names=None):
    """Màn đang mở (tên GameObject từ probe) → ghi vào sess.d["observed"] theo tên Feature khớp (hoặc tên thô)."""
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
    """Chụp (nếu chưa) + /ui đầy đủ + audit_elements với luật nhiễu của project. Ghi audit-<tag>.json. Trả (findings, j)."""
    v = view or observe(sess, dev, label, logs=True)
    j = probe_ui(sess, dev, include_all=True)
    record_observed(sess, j)
    findings = audit_elements(j, noise=qa_profile().get("noise"))
    shown = [f for f in findings if (show_all or f["severity"] != "info") and (include_suppressed or not f.get("suppressed"))]
    sup = sum(1 for f in findings if f.get("suppressed"))
    if not quiet:
        print("AUDIT màn %s: %d phát hiện (%d info ẩn — `--all` để xem%s)" % (
            " > ".join(j.get("screens", [])) or j.get("scene"), len(shown), sum(1 for f in findings if f["severity"] == "info" and not f.get("suppressed")),
            (" · %d bị luật nhiễu của project ẩn — `--include-suppressed`" % sup) if sup and not include_suppressed else ""))
        for f in shown:
            print("  [%s] [%d] %s — %s (%s)%s" % (f["severity"], f["id"], f["kind"], f["msg"], clip(f["path"], 70),
                                               (" ⟂ nhiễu: %s" % f["suppressed"]) if f.get("suppressed") else ""))
    if shown and Image is not None and not quiet:
        out = draw_ui_marks(sess, dev, v["path"], "%03d" % v["n"], findings=shown, ui=j)
        if out:
            print("ảnh khoanh lỗi: %s" % out)
    write_json(sess.path("audit-%s.json" % (tag or "%03d" % v["n"])), {"screens": j.get("screens"), "scene": j.get("scene"),
                                                                     "shot": v["n"], "findings": findings})
    sess.save()
    return findings, j


def maybe_auto_audit(sess, dev, label=""):
    """Sau thao tác: màn đổi (so stack màn từ /ui) → audit tự động, ghi audit-auto-<n>.json + một bước. Không có probe → bỏ qua."""
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
        print("  auto-audit bỏ qua: %s" % e)
        return None
    real = [f for f in findings if f["severity"] != "info" and not f.get("suppressed")]
    sup = sum(1 for f in findings if f.get("suppressed"))
    sess.step("auto-audit", "", screens=j2.get("screens"), findings=len(real), suppressed=sup or None, file="audit-auto-%03d.json" % n)
    worst = real[0] if real else None
    print("  ⚑ audit tự động #%d: %d phát hiện (%d suppressed) · màn %s%s · audit-auto-%03d.json" % (
        n, len(real), sup, " > ".join(j2.get("screens") or []) or j2.get("scene"),
        (" · nặng nhất [%s] %s" % (worst["severity"], worst["kind"])) if worst else "", n))
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
        st["problems"].append("chưa có mã project BugHub (bugHub.projectCode trong .claude/project-profile.json)")
    elif not re.match(r"^[A-Za-z0-9_-]+$", code):
        st["problems"].append("mã project không hợp lệ: %s" % code)
    if not st["package"]:
        st["problems"].append("project chưa có package com.ezg.buglogger")
    if not assets:
        st["problems"].append("không thấy Resources/BugLoggerSettings.asset")
    elif code and not any(a["projectCode"].lower() == code.lower() for a in assets):
        st["problems"].append("BugLoggerSettings.asset mang mã %s ≠ %s" % (",".join(acodes) or "(rỗng)", code))
    if code and re.match(r"^[A-Za-z0-9_-]+$", code):
        status, form = http_json("%s/v1/projects/%s/form" % (endpoint, code))
        st["http"] = status
        if status == 200:
            st["form"] = form
            if not form.get("intakeEnabled", False):
                st["problems"].append("server: intake của %s đang TẮT (nhờ admin BugHub bật)" % code)
            write_json(os.path.join(state_root(), "bughub-form-%s.json" % code.upper()), form)
        elif status == 404:
            st["problems"].append("server: project %s chưa đăng ký / đang tắt intake (404)" % code)
        else:
            st["problems"].append("server BugHub không trả lời (%s %s)" % (status, form.get("error", "")))
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


def pick_people(form, names):
    people = (form or {}).get("people") or []
    ids = []
    for n in names:
        nl = strip_accents(n).lower()
        for p in people:
            if nl and (nl in strip_accents(p.get("name", "")).lower() or p.get("id") == n) and p["id"] not in ids:
                ids.append(p["id"])
    return ids[:5]


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
    """report.json schema 1 — y hệt BugReportBuilder.WriteReport của com.ezg.buglogger (BugHub contract §3.4)."""
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


# ───────────────────────────────────────────────────────────── lệnh

def cmd_doctor(a):
    adb = find_adb()
    print("qadevice %s · python %s · %s" % (VERSION, sys.version.split()[0], sys.platform))
    print("adb:       %s" % (adb or "KHÔNG CÓ — cài Android platform-tools hoặc Unity Android module"))
    print("emulator:  %s" % (find_emulator(adb) or "không có (chỉ cần khi muốn tự boot AVD)"))
    print("xcrun:     %s" % ("có" if has_xcrun() else "không (chỉ macOS — iOS Simulator/iPhone)"))
    print("idb:       %s" % (find_idb() or "không — Simulator sẽ chạm qua probe (brew install facebook/fb/idb-companion && pip3 install fb-idb)"))
    print("Pillow:    %s" % ("có" if Image is not None else "KHÔNG — chạy `%s -m pip install --user pillow` để có ảnh xem nhỏ, lưới toạ độ, so khung hình" % ("py" if IS_WIN else "python3")))
    root = repo_root()
    ps = project_settings(root)
    print("project:   %s · %s · Unity %s" % (ps.get("productName"), ps["ids"], ps.get("unityVersion")))
    info = find_probe_in_project(root)
    print("probe:     %s" % (("package %s %s" % (PROBE_PACKAGE, info["package"])) if info["package"]
                             else "chưa có package %s trong Packages/manifest.json (`start` tự thêm)" % PROBE_PACKAGE))
    if info["legacy"]:
        print("           ⚠ còn bản chép cũ %s (v%s) — `start`/`probe-install` sẽ gỡ" % (info["legacy"], info["legacyVersion"]))
    if not info["registry"]:
        print("           ⚠ manifest không có scoped registry EZG (scope com.ezg) — cài registry qua Feature Hub trước")
    cfg = effective_probe_config(root)
    print("config:    releaseGate=%s · cheatOn=%s · cheatOff=%s%s" % (
        cfg["releaseGate"] or "-", cfg["cheatOn"] or "-", cfg["cheatOff"] or "-", " (có ghi đè %s)" % PROBE_OVERRIDE if cfg["override"] else ""))
    cg_ver, cg_code = cheat_gate_info(root)
    print("cheat:     %s" % (("com.ezg.cheat-gate %s · mã %s — probe tự mở cổng cho máy có trong danh sách test" % (cg_ver, cg_code or "?"))
                             if cg_ver else "không có com.ezg.cheat-gate — cổng release theo cờ cheat của game (releaseGate)"))
    print("state:     %s" % state_root())


def cmd_devices(a):
    adb = find_adb()
    rows = []
    for d in list_android(adb):
        name = avd_of(adb, d["id"]) if d["emulator"] and d["state"] == "device" else ""
        rows.append((d["id"], "android", d["state"], d["model"] + (" (AVD %s)" % name if name else "")))
    running_avds = set(r[3].split("AVD ")[-1].rstrip(")") for r in rows if "AVD" in r[3])
    for n in list_avds(adb):
        if n not in running_avds:
            rows.append((n, "android-avd", "tắt (sẽ tự boot)", ""))
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
        print("Không có thiết bị/AVD/Simulator nào.")
    for r in rows:
        print("%-40s %-11s %-18s %s" % r)


def cmd_bughub_check(a):
    st = bughub_status(repo_root(), a.code)
    if a.json:
        print(json.dumps(st, ensure_ascii=False, indent=1))
    else:
        print("BugHub: %s · mã %s · endpoint %s" % ("OK" if st["ok"] else "CHƯA SẴN SÀNG", st["code"] or "?", st["endpoint"]))
        for p in st["problems"]:
            print("  ✗ " + p)
        if st.get("form"):
            f = st["form"]
            print("  categories: %s" % ", ".join(f.get("categories", [])))
            print("  tags: %s" % ", ".join(t["name"] for t in f.get("tags", [])))
            print("  people: %s" % ", ".join(p["name"] for p in f.get("people", [])))
        if not st["ok"]:
            print("→ chạy /bughub-setup cho project này rồi thử lại.")
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
    """Project chưa có package probe / bản cũ hơn skill cần → thêm/nâng dòng trong manifest; gỡ bản chép cũ trong Assets.
    Trả danh sách dòng báo (rỗng khi không đổi gì)."""
    notes = []
    info = find_probe_in_project(root)
    ver = info["package"]
    if ver is None or (_is_semver(ver) and _ver(ver) < _ver(PROBE_MIN_VERSION)):
        set_manifest_dependency(root, PROBE_PACKAGE, PROBE_MIN_VERSION)
        notes.append("%s %s %s vào Packages/manifest.json — commit manifest (+ packages-lock.json Unity sinh) và build mới thì "
                     "lần sau test sâu được" % ("nâng" if ver else "đã thêm", PROBE_PACKAGE, PROBE_MIN_VERSION))
        if not info["registry"]:
            notes.append("⚠ manifest chưa có scoped registry EZG (scope com.ezg) — Unity không tải được %s; cài registry qua "
                         "Feature Hub" % PROBE_PACKAGE)
    if info["legacy"]:
        notes.extend(remove_legacy_probe(root, info["legacy"]))
    return notes


def auto_clear_script():
    """Script của skill /auto-clear cạnh skill này (macOS/Linux: .sh, Windows: .ps1); không có thì bản copy mà hook
    của máy đang chạy (~/.claude/auto-clear/). None = máy/project chưa có /auto-clear."""
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
    """Output của `auto-clear retry-arm|retry-off` → (state, dòng kết quả).
    state: armed · disarmed · not_armed · not_installed · unsupported · error."""
    for line in (out or "").splitlines():
        s = line.strip()
        for key, state in (("RETRY_ARMED", "armed"), ("RETRY_DISARMED", "disarmed"), ("RETRY_NOT_ARMED", "not_armed"),
                           ("NOT_INSTALLED", "not_installed"), ("UNSUPPORTED", "unsupported"), ("INVALID_", "error")):
            if s.startswith(key):
                return state, s
    tail = [l.strip() for l in ((err or "") + "\n" + (out or "")).splitlines() if l.strip()]
    return "error", tail[-1] if tail else "auto-clear thoát mã %s" % rc


def api_retry(root, action):
    """Bật (`arm`) / gỡ (`off`) THỬ LẠI LỖI API cho pane Claude Code đang chạy skill: lượt trả lời chết vì lỗi API
    (org tắt quyền Claude, rate limit, overloaded, 5xx, mất phiên…) thì model không còn chạy để tự thử lại — hook
    StopFailure của /auto-clear chờ 30 s rồi gõ API_RETRY_PROMPT vào đúng pane, không giới hạn số lần, tới khi
    API trả lời được hoặc `stop`. Máy chưa đăng ký hook → tự `install` (idempotent, có backup settings, hook không làm
    gì khi pane không bật cờ) rồi bật lại. Trả (state, dòng); state thêm `missing` khi không có script /auto-clear."""
    script = auto_clear_script()
    if not script:
        return "missing", "project chưa có skill /auto-clear (cài từ Feature Hub)"
    if action != "arm":
        return parse_retry_result(*run(auto_clear_cmd(script, "retry-off"), timeout=30, cwd=root))
    arm = auto_clear_cmd(script, "retry-arm", "--max", "0", "--prompt", API_RETRY_PROMPT)
    st, line = parse_retry_result(*run(arm, timeout=30, cwd=root))
    if st == "not_installed":
        rc, out, err = run(auto_clear_cmd(script, "install"), timeout=60, cwd=root)
        st, line = parse_retry_result(*run(arm, timeout=30, cwd=root))
        if st == "armed":
            line = "đã tự cài hook /auto-clear trên máy · " + line
        elif rc != 0:
            line = "`auto-clear install` lỗi: %s" % ((err or out).strip() or "mã %s" % rc)
    return st, line


def describe_api_retry(ar):
    """Một dòng cho output `start` + report."""
    if not ar:
        return "tắt (--no-api-retry)"
    st, line = ar.get("state"), ar.get("line", "")
    if st == "armed":
        return ("BẬT — lượt trả lời chết vì lỗi API (org tắt quyền Claude, rate limit, overloaded, 5xx…) → tự gõ tiếp "
                "sau %ds, không giới hạn; `stop` gỡ%s" % (RETRY_EVERY_S, " (đã tự cài hook trên máy)" if "đã tự cài" in line else ""))
    if st == "disarmed":
        return "đã gỡ (phiên đã stop)"
    if st == "not_installed":
        return "TẮT — hook StopFailure chưa cài được trên máy (%s) → dev chạy `/auto-clear install`" % line
    if st == "unsupported":
        return "TẮT — %s (chỉ chạy trong iTerm2 trên macOS / console Windows) — lỗi API phải gõ tiếp tay" % line
    return "TẮT — %s" % line


def cmd_start(a):
    root = repo_root()
    # BugHub là điều kiện bắt buộc — kiểm trước khi đụng vào máy.
    bh = bughub_status(root, a.code)
    if not bh["ok"] and not a.skip_bughub:
        print("BugHub: CHƯA SẴN SÀNG · mã %s · endpoint %s" % (bh["code"] or "?", bh["endpoint"]))
        for pr_ in bh["problems"]:
            print("  ✗ " + pr_)
        print("→ chạy /bughub-setup cho project này rồi thử lại.")
        sys.exit(4)
    notes = [n for n in (ensure_pillow(),) if n]
    # Hồ sơ QA của project: chưa có block qa → tự dò quy ước (profile init) để lệnh ui/state/cheat/coverage có quy ước đúng.
    qa_line = ""
    try:
        qa0, prov0 = qaprofile.load_qa_profile(root, with_provenance=True)
        if prov0.get("_block") == "missing" and not a.no_profile_init:
            _, det0, _ = profile_init(root)
            qa0 = qa_profile(root, refresh=True)
            notes.append("chưa có hồ sơ QA → đã `profile init` (status draft): quy ước tự dò ghi vào %s + QaProfile.md — dev đọc lại rồi đổi `reviewed`" % qaprofile.PROFILE_REL)
        lv0 = qa0.get("liveness") or {}
        qa_line = "status %s · enum %s · %d màn đã quan sát · %d luật nhiễu%s" % (
            qa0.get("status"), (qa0.get("conventions") or {}).get("featuresEnum"), len(lv0.get("observed") or {}), len(qa0.get("noise") or []),
            " · doc: %s" % os.path.relpath(qa_path(root, "profileDoc"), root) if os.path.isfile(qa_path(root, "profileDoc")) else " · CHƯA CÓ QaProfile.md")
    except Exception as e:  # hồ sơ hỏng không được chặn phiên test
        qa_line = "lỗi đọc hồ sơ QA: %s" % e
    if a.probe != "off" and not a.no_probe_install:
        try:
            notes.extend(ensure_probe_in_project(root))
        except (OSError, QaError) as e:
            notes.append("không thêm được package probe vào project: %s" % e)
    close_previous_session()
    dev = resolve_device(a.device, headless=not a.window)
    ps = project_settings(root)
    pkg = a.package or ps["ids"].get("Android" if dev.platform == "Android" else "iPhone") or ps["ids"].get("Standalone")
    if not pkg:
        raise QaError("không đọc được bundle id từ ProjectSettings — truyền --package")
    if isinstance(dev, Android):
        dev.wake()
    if a.install:
        dev.install(a.install)
    if not dev.installed(pkg):
        art = find_artifact(root, dev.platform)
        if art and not a.no_install:
            print("App %s chưa cài trên máy — cài %s" % (pkg, art))
            dev.install(art)
        if not dev.installed(pkg):
            raise QaError("app %s chưa cài trên %s và không tìm thấy bản build trong repo — cài tay hoặc truyền --install <apk/app>" % (pkg, dev.id))
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
            dev.remove_trigger(pkg)  # Android dùng intent; file do bản cũ ghi chỉ gây cảnh báo "Access denied"
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
                dev.background(pkg, 1, token, port)  # intent mới mang token phiên này
            elif hasattr(dev, "background"):
                dev.background(pkg, 1)  # nạp lại file trigger khi app lấy lại focus
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
            if not pid_alive(cpid):  # Xcode cũ chưa có --console → mở thường, log chỉ còn qua probe
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
    probe_msg = "tắt (--probe off)"
    if token:
        probe_msg = connect_probe(sess, dev, wait=a.probe_wait if a.probe == "auto" else max(a.probe_wait, 60))
    if isinstance(dev, UnityEditorTarget) and not (sess.d.get("probe") or {}).get("ok"):
        print("PHIÊN %s (Editor) — chưa nối được probe: vào Play mode (Unity MCP unity_play_mode) rồi chạy `probe-connect`" % sess.id)
        print("thử lại lỗi API: %s" % describe_api_retry(sess.d.get("api_retry")))
        return
    # chờ game lên hình rồi chụp
    time.sleep(a.settle)
    print("PHIÊN %s" % sess.id)
    print("thiết bị: %s · %s · %s" % (dev.id, info["model"], info["os"]))
    print("app: %s v%s (%s)%s%s" % (pkg, vn or "?", vc or "?",
                                  (" · commit %s@%s" % (sess.d["app"].get("commitSha", "")[:9], sess.d["app"].get("branch", ""))) if sess.d["app"].get("commitSha") not in (None, "", "unknown") else "",
                                  (" · mở nguội %d ms" % launch_ms) if launch_ms else ""))
    print("probe: %s" % probe_msg)
    if a.probe == "require" and not (sess.d.get("probe") or {}).get("ok"):
        raise QaError("--probe require nhưng không nối được probe (xem dòng probe ở trên)")
    print("bughub: %s" % ("OK (%s)" % bh["code"] if bh["ok"] else "CHƯA SẴN SÀNG — " + "; ".join(bh["problems"])))
    print("hồ sơ QA: %s" % qa_line)
    print("thử lại lỗi API: %s" % describe_api_retry(sess.d.get("api_retry")))
    for n in notes:
        print("ghi chú: " + n)
    print("thư mục phiên: %s" % sess.dir)
    hint = (sess.d.get("probe") or {}).get("next")
    if hint:
        print("→ " + hint)
    observe(sess, dev, "start")
    for o in get_overlays(sess, dev, refresh=True):
        print("⚠ overlay app khác: %s — tool chặn chạm adb vào vùng này (sẽ trúng app kia). Nút game nằm dưới → "
              "--inject; nút quảng cáo nằm dưới → tắt/dời overlay trên máy trước khi test" % overlay_line(sess, o))


def wait_cheat_gate(cli, j, wait):
    """Build có com.ezg.cheat-gate: chờ probe hỏi xong CheatGate (game Initialize ở splash + 1 request) rồi trả /ping mới."""
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
    """/ping → (trạng thái một dòng, việc cần làm tiếp hoặc "")."""
    cg = j.get("cheatGate") or {}
    state, dev_id, proj = cg.get("state", ""), cg.get("deviceId", ""), cg.get("project") or code or "<mã project>"
    cheat = j.get("cheat")
    cheat_txt = "" if cheat is None else (" · cheat game %s" % ("BẬT" if cheat else "tắt"))
    if j.get("gate") == "open":
        if cg.get("present") and state == "unlocked":
            return "cổng mở — cheat-gate: máy %s có trong danh sách test %s%s" % (dev_id, proj, cheat_txt), ""
        return "cổng mở (%s)%s" % (j.get("gateReason", ""), cheat_txt), ""
    if cg.get("present"):
        if state == "refused":
            return ("CỔNG ĐÓNG — máy %s chưa có trong danh sách máy test của %s" % (dev_id, proj),
                    "thêm máy: ezg MCP test_device_add(project=\"%s\", device_id=\"%s\", label=\"<người cầm máy> - %s\") "
                    "rồi `probe-connect --unlock` (server nhận sau ~1-2 phút, lệnh tự chờ)" % (proj, dev_id, model or "máy test"))
        if state == "offline":
            return "CỔNG ĐÓNG — máy không có mạng, CheatGate cần mạng ở lần mở đầu", "bật mạng trên máy rồi `probe-connect --unlock`"
        if state == "not_configured":
            return ("CỔNG ĐÓNG — game chưa CheatGate.Initialize(mã project) / mã trống",
                    "dev đặt mã BugHub vào GameConstant.CheatGateProject (hoặc chỗ game gọi CheatGate.Initialize) rồi build lại — phiên này test hộp đen")
        if state == "off":
            return "CỔNG ĐÓNG — autoCheat=false trong EzgQaProbeConfig", "tap version 10 lần trên máy (máy trong danh sách) rồi `probe-connect`"
        return "CỔNG ĐÓNG — cheat-gate chưa xong (%s)" % (state or "?"), "`probe-connect --unlock` để chờ thêm"
    return ("CỔNG ĐÓNG (%s)" % j.get("gateReason", ""),
            "bật cheat trên máy theo cách của game (vd bấm version trong Settings 10 lần, hoặc thêm máy vào danh sách cheat của game) rồi `probe-connect`")


def connect_probe(sess, dev, wait=25, cheat_wait=40):
    pr = sess.d["probe"]
    token, port = pr["token"], pr["port"]
    t0 = time.time()
    tried_ports = [port + k for k in range(PROBE_PORT_SPAN)]
    last_err = ""
    while time.time() - t0 < wait:
        # dòng log "[EzgQaProbe] listening host:port gate=…" cho biết cổng thật (Android)
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
                    return "không có (không biết địa chỉ mạng của iPhone — cần kết nối CoreDevice/USB)"
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
                old = " · probe trong build cũ (v%s < %s): build mới để có đủ tính năng" % (j.get("version"), PROBE_MIN_VERSION) \
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
    return "không có (build không chứa EZG QA Probe hoặc chưa mở) — test hộp đen: ảnh + toạ độ%s" % (
        (" · lỗi cuối: %s" % last_err) if last_err and "không trả lời" not in last_err else "")


def cmd_probe_connect(a):
    sess = Session.load(a.session)
    dev = sess.device()
    pr = sess.d.get("probe") or {}
    if not pr.get("token"):
        raise QaError("phiên mở với --probe off")
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
    """Hỏi lại CheatGate (vd vừa test_device_add) và chờ server nhận — tối đa `wait` giây, hỏi lại mỗi 30 s."""
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
            return "không hỏi lại được: %s" % e
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
    """Lấy toạ độ px máy cho lệnh tap/longpress: @id, --text, --name, --world hoặc x y."""
    el = None
    if a.target and a.target[0].startswith("@"):
        sess.d["ui_prev"] = sess.d.get("ui")
        el = find_element(sess, dev, ref=int(a.target[0][1:]))
    elif getattr(a, "text", None) is not None:
        el = find_element(sess, dev, text=a.text)
    elif getattr(a, "name", None) is not None:
        el = find_element(sess, dev, name=a.name)
    elif getattr(a, "world", None) is not None:
        pr = need_probe(dev, "Chạm object 3D theo tên")
        j = pr.get("/find", q=a.world, timeout=15)
        objs = [o for o in j.get("world", []) if o.get("onScreen")]
        if not objs:
            raise QaError("không thấy object '%s' trên màn (find trả %d)" % (a.world, len(j.get("world", []))))
        sx, sy = unity_scale(sess)
        o = objs[0]
        return int(o["pos"][0] / sx), int(o["pos"][1] / sy), o.get("path", a.world)
    if el:
        if el.get("occluded") or el.get("blockedBy"):
            print("⚠ phần tử đang bị %s che — chạm sẽ trúng lớp phía trên (đóng popup/overlay trước nếu muốn bấm nó)" % (
                "màn/popup khác" if el.get("occluded") else clip(el["blockedBy"], 80)))
        x, y, w, h = el["dev"]
        cx, cy = el.get("tap", [None, None])
        if cx is not None:
            sx, sy = unity_scale(sess)
            return int(cx / sx), int(cy / sy), el.get("label") or el.get("text") or el.get("path")
        return int(x + w / 2), int(y + h / 2), el.get("label") or el.get("text") or el.get("path")
    if not a.target or len(a.target) < 2:
        raise QaError("cần toạ độ X Y (theo ảnh xem gần nhất, hoặc 50%% 80%%) hoặc @id / --text / --name / --world")
    x, y = to_device_xy(sess, a.target[0], a.target[1], a.space)
    return x, y, ""


def get_overlays(sess, dev, refresh=False):
    """Overlay app khác (cache trong phiên, dò lại tối đa 1 lần/OVERLAY_CACHE_SECONDS)."""
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


def overlay_line(sess, o):
    x, y, w, h = o["rect"]
    if not (sess.d.get("view") or {}).get("shot_w"):
        return "%s (%s) che px máy [%d,%d %dx%d]" % (o["package"], o["type"], x, y, w, h)
    vx, vy = device_to_view(sess, x, y)
    vx2, vy2 = device_to_view(sess, x + w, y + h)
    return "%s (%s) che vùng ảnh (%d,%d)-(%d,%d) = px máy [%d,%d %dx%d]" % (o["package"], o["type"], vx, vy, vx2, vy2, x, y, w, h)


def guard_overlay(a, sess, dev, points):
    """Chặn chạm adb rơi vào overlay app khác — chạm đó trúng app kia (từng bật nhầm ghi âm của app mic nổi)."""
    if getattr(a, "inject", False) or not dev.native_input or getattr(a, "allow_overlay", False):
        return
    ovs = get_overlays(sess, dev)
    for x, y in points:
        o = point_in_rects(x, y, ovs)
        if o:
            raise QaError("điểm chạm (%d,%d) px máy nằm dưới overlay của app khác: %s — adb chạm sẽ trúng app đó, không "
                          "phải game. Cách xử lý: chạm UI game qua probe (--inject), hoặc tắt/dời overlay đó trên máy, "
                          "hoặc --allow-overlay nếu cố ý chạm vào nó" % (x, y, overlay_line(sess, o)))


def kill_app(dev, pkg, hard=False, settle=STOP_SETTLE_SECONDS):
    """Tắt game. Mặc định như người chơi (Home → chờ → tắt: game kịp OnApplicationPause/lưu); hard = kill thẳng
    như crash / OS giết app — game KHÔNG kịp lưu. Trả "player" | "hard" | "hard-fallback" (máy không có Home)."""
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
KILL_NOTE = {"player": "Home → chờ %ss → tắt, như người chơi vuốt tắt",
             "hard": "kill thẳng — như crash / OS giết app, game không kịp lưu lúc pause",
             "hard-fallback": "máy không có phím Home → kill thẳng; bug lưu trữ phải xác nhận lại trên Android/Simulator"}


def inject(sess, dev, path, **params):
    pr = need_probe(dev, "Input qua probe")
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
    """'text:Nhận' | '@12' | 'screen:Shop' | 'Nhận' (mặc định text) → (kiểu, giá trị)."""
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
    """Kỳ vọng `--expect` đã đúng TRƯỚC khi thao tác chưa? (gọi trước tap/key) — None khi không có probe/không kiểm được."""
    spec = getattr(a, "expect", None)
    if not spec:
        return None
    try:
        kind, value = parse_expect(spec)
        return expect_met(need_probe(dev, "--expect").get("/ui", all=1, timeout=15), kind, value)
    except Exception:
        return None


def check_expect(a, sess, dev, spec, pre_met=None):
    """`--expect`: chờ kỳ vọng xuất hiện (probe) tới timeout; không thấy → ghi finding expectation-failed (KHÔNG tự tạo bug).
    `pre_met=True` (kỳ vọng đã đúng trước thao tác, vd `screen:` của màn đang mở sẵn) → vẫn pass nhưng cảnh báo: không chứng minh phản hồi."""
    kind, value = parse_expect(spec)
    pr = need_probe(dev, "Kiểm kỳ vọng sau thao tác (--expect)")
    timeout = max(0.5, float(getattr(a, "timeout", None) or EXPECT_TIMEOUT_S))
    t0 = time.time()
    last = None
    while True:
        try:
            last = pr.get("/ui", all=1, timeout=15)
        except QaError as e:
            last = {"error": str(e)}
        if expect_met(last, kind, value):
            warn = " — ⚠ đã đúng TRƯỚC khi thao tác nên không chứng minh được phản hồi; dùng text/@id chỉ có ở màn mới" if pre_met else ""
            print("  ✓ kỳ vọng %s:%s đã thấy sau %.1fs%s" % (kind, value, time.time() - t0, warn))
            sess.step("check", "Kỳ vọng %s:%s — đạt%s" % (kind, value, " (đã đúng trước thao tác)" if pre_met else ""), result="pass")
            return True
        if time.time() - t0 >= timeout:
            break
        time.sleep(0.5)
    scr = " > ".join(last.get("screens") or []) if isinstance(last, dict) else "?"
    print("  ✗ KỲ VỌNG KHÔNG ĐẠT: %s:%s không xuất hiện sau %.0fs (màn hiện tại: %s) — xác nhận bằng ảnh rồi quyết định log bug" % (
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
    print("tap%s (%d,%d) ảnh → (%d,%d) máy%s%s" % ("×%d" % a.times if a.times > 1 else "", vx, vy, x, y,
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
            raise QaError("cần X1 Y1 X2 Y2 hoặc --dir up|down|left|right")
        x1, y1 = to_device_xy(sess, a.coords[0], a.coords[1], a.space)
        x2, y2 = to_device_xy(sess, a.coords[2], a.coords[3], a.space)
    hold = getattr(a, "hold", 0)
    guard_overlay(a, sess, dev, [(x1, y1)])  # điểm đặt tay quyết định cửa sổ nhận cả cử chỉ
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
        pr = need_probe(dev, "Nhập chữ Unicode")
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
        print("đã chờ ổn định %.1fs" % (time.time() - t0))
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
        # ghép thành một dải để xem một lần
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
        print("dải %d khung: %s" % (len(ims), out))


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
        print("restart xong (%s → mở lại)" % (KILL_NOTE[mode] % a.settle if mode == "player" else KILL_NOTE[mode]))
    elif op == "stop":
        sess.d["expect_exit"] = sess.d.get("expect_exit", 0) + 1
        sess.save()
        mode = kill_app(dev, pkg, hard=a.hard, settle=a.settle)
        sess.d["app_running"] = False
        sess.step("stop", a.desc or KILL_DESC[mode], mode=mode)
        print("đã tắt app (%s)" % (KILL_NOTE[mode] % a.settle if mode == "player" else KILL_NOTE[mode]))
        return
    elif op == "launch":
        relaunch(sess, dev)
        sess.step("launch", a.desc or "")
        print("đã mở app")
    elif op == "background":
        dev.background(pkg, a.secs)
        sess.step("background", a.desc or "", secs=a.secs)
        print("đã đưa xuống nền %ss rồi mở lại" % a.secs)
    elif op == "clear":
        sess.d["expect_exit"] = sess.d.get("expect_exit", 0) + 1
        sess.save()
        dev.stop(pkg)
        dev.clear_data(pkg)
        if (sess.d.get("probe") or {}).get("token"):
            dev.write_trigger(pkg, "%s:%d" % (sess.d["probe"]["token"], sess.d["probe"]["port"]))
        relaunch(sess, dev)
        sess.step("fresh", a.desc or "Xoá dữ liệu game rồi mở lại (người chơi mới)")
        print("đã xoá dữ liệu + mở lại")
    elif op == "info":
        vn, vc = dev.app_version(pkg)
        print("%s v%s (%s) · pid %s · foreground=%s" % (pkg, vn, vc, dev.pidof(pkg), dev.foreground() or "?"))
        return
    elif op == "install":
        if not a.path:
            raise QaError("app install <đường dẫn apk/app/ipa>")
        dev.install(a.path)
        print("đã cài %s" % a.path)
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
    print("mạng → %s · %s" % (a.mode, st))
    after_action(a, sess, dev, "net-" + a.mode)


def cmd_rotate(a):
    sess = Session.load(a.session)
    dev = sess.device()
    dev.rotate(a.mode)
    sess.d["changed"]["rotation"] = a.mode != "auto"
    sess.save()
    sess.step("rotate", a.desc or "", mode=a.mode)
    print("xoay → %s" % a.mode)
    after_action(a, sess, dev, "rotate-" + a.mode)


def cmd_overlays(a):
    sess = Session.load(a.session)
    dev = sess.device()
    if not isinstance(dev, Android):
        print("chỉ Android dò được overlay app khác")
        return
    ovs = get_overlays(sess, dev, refresh=True)
    if not ovs:
        print("không có overlay app khác đang nhận chạm")
    for o in ovs:
        print("⚠ " + overlay_line(sess, o))


def cmd_ad_close(a):
    """Quảng cáo/trang native (Activity riêng — probe Unity không thấy): tìm nút đóng/skip qua uiautomator rồi bấm
    như người chơi. Nút bị overlay app khác che kín → báo rõ thay vì bấm trúng app kia."""
    sess = Session.load(a.session)
    dev = sess.device()
    if not isinstance(dev, Android):
        raise NotSupported("ad-close chỉ có trên Android — iOS: nhìn ảnh rồi tap nút X (hoặc chờ quảng cáo tự đóng)")
    deadline = time.time() + max(0.0, a.timeout)
    while True:
        cands = pick_close_candidates(parse_uia_nodes(dev.uia_dump()))
        if cands or a.list or time.time() >= deadline:
            break
        time.sleep(2.0)
    fg = dev.foreground()
    if not cands:
        print("không thấy nút đóng/skip (foreground=%s) sau %ss — quảng cáo còn đếm ngược? `wait` thêm rồi chạy lại, "
              "hoặc `key back`" % (fg or "?", a.timeout))
        return
    ovs = get_overlays(sess, dev, refresh=True)
    for c in cands[:5]:
        x, y, w, h = c["rect"]
        vx, vy = device_to_view(sess, x + w // 2, y + h // 2)
        o = point_in_rects(x + w // 2, y + h // 2, ovs)
        print("  ứng viên \"%s\" ảnh (%d,%d) %dx%d px máy%s%s" % (c["label"], vx, vy, w, h,
                                                             "" if c["clickable"] else " · không clickable",
                                                             (" · BỊ CHE bởi " + o["package"]) if o else ""))
    if a.list:
        return
    c = cands[0]
    if a.allow_overlay:
        pt = (c["rect"][0] + c["rect"][2] // 2, c["rect"][1] + c["rect"][3] // 2)
    else:
        pt = uncovered_point(c["rect"], ovs)
    if pt is None:
        o = point_in_rects(c["rect"][0] + c["rect"][2] // 2, c["rect"][1] + c["rect"][3] // 2, ovs) or (ovs[0] if ovs else {})
        raise QaError("nút đóng quảng cáo \"%s\" bị overlay app khác che kín: %s — adb không bấm xuyên được; tắt/dời overlay "
                      "đó trên máy rồi chạy lại `ad-close`. Ca test phụ thuộc quảng cáo ghi `blocked` (môi trường), "
                      "KHÔNG phải bug game" % (c["label"], overlay_line(sess, o) if o else "?"))
    dev.tap(pt[0], pt[1])
    vx, vy = device_to_view(sess, pt[0], pt[1])
    sess.step("tap", a.desc or "Bấm nút đóng quảng cáo (%s)" % c["label"], xy=[vx, vy], dev=list(pt), el=c["label"])
    print("ad-close: bấm \"%s\" (%d,%d) ảnh · foreground trước đó=%s" % (c["label"], vx, vy, fg or "?"))
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
        print("— toàn phiên: %d chữ ký lỗi" % len(issues))
        for sig, it in sorted(issues.items(), key=lambda kv: SEVERITIES.index(kv[1]["severity"]) if kv[1]["severity"] in SEVERITIES else 9):
            print("  [%s] %s ×%d sig=%s step#%s%s · %s" % (it["severity"], it["kind"], it["count"], sig, it.get("step"),
                                                         (" → bug #%s" % it["bug"]) if it.get("bug") else "", clip(it["first"], 160)))
    if a.ignore:
        sess.d.setdefault("ignore", []).append(a.ignore)
        sess.save()
        print("bỏ qua từ giờ (phiên này): /%s/" % a.ignore)


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
    print("perf: %s" % (line or "(không đọc được)"))
    if hist and "pss_mb" in res and "pss_mb" in hist[0]:
        grow = res["pss_mb"] - hist[0]["pss_mb"]
        print("  RAM so với mẫu đầu phiên: %+.1f MB%s" % (grow, "  ⚠ tăng mạnh — nghi rò bộ nhớ" if grow > 150 else ""))
    if res.get("fps") is not None and res["fps"] < 25:
        print("  ⚠ FPS thấp: %.1f" % res["fps"])
    if res.get("battery_c") and res["battery_c"] >= 43:
        print("  ⚠ máy nóng %.1f°C" % res["battery_c"])


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
    print("UI · scene=%s · màn: %s · %d phần tử (toạ độ = px ảnh xem #%s)" % (
        j.get("scene"), " > ".join(j.get("screens", [])) or "?", len(els), sess.d["view"].get("n")))
    for e in els:
        vx, vy, vw, vh = _view_rect(sess, e["dev"])
        state = ""
        if e.get("interactable") is False and e.get("kind") in ("Button", "Toggle", "InputField", "Dropdown"):
            state = " ✗disabled"
        if e.get("blockedBy"):
            state += " ✗che:" + clip(e["blockedBy"], 40)
        flags = [f for f in e.get("flags", []) if f not in ("interactable",)]
        txt = e.get("label") or e.get("text") or ""
        print(" [%d] %-9s %-28s %-26s c=(%d,%d) %dx%d%s%s" % (e["id"], e.get("kind", "")[:9], clip(e.get("name", ""), 28),
                                                           ("\"%s\"" % clip(txt, 22)) if txt else "", vx + vw // 2, vy + vh // 2, vw, vh,
                                                           state, (" ⚑" + ",".join(flags)) if flags else ""))
    world = j.get("world", [])
    if world:
        print("World (object 3D/2D chạm được, gần camera trước):")
        for o in world[:25]:
            vx, vy = device_to_view(sess, *o["dev"])
            print("  · %-40s c=(%d,%d)%s" % (clip(o.get("path", ""), 40), vx, vy, "" if o.get("onScreen") else " (ngoài màn)"))
    if a.marks and Image is not None:
        out = draw_ui_marks(sess, dev, sess.d["view"]["path"], "%03d-ui" % sess.d["view"]["n"])
        if out:
            print("ảnh đánh số: %s" % out)


def cmd_audit(a):
    sess = Session.load(a.session)
    dev = sess.device()
    v = observe(sess, dev, "audit", logs=True)
    if not dev.probe:
        print("audit sâu (tràn chữ/thiếu glyph/nút bị che…) cần probe — chỉ có kiểm ảnh + log ở trên. Soi ảnh bằng mắt theo checklist visual.")
        return
    findings, j = run_audit(sess, dev, view=v, show_all=a.all, include_suppressed=a.include_suppressed)
    if not probe_supports(sess, "flags"):
        print("  (probe v%s chưa có flags[] của 1.3 — thiếu các check port từ UiAuditUtil; build mới để đủ)" % ((sess.d.get("probe") or {}).get("version") or "?"))
    sess.d["screens_key"] = screens_key(j)
    sess.save()


def cmd_state(a):
    sess = Session.load(a.session)
    dev = sess.device()
    pr = need_probe(dev, "Đọc state game")
    dump = {}
    for p in a.paths:
        try:
            j = pr.get("/get", path=p, timeout=10)
            dump[p] = j.get("value")
            if not a.out:
                print(format_value(p, j.get("value"), full=a.full).replace(" → ", " = ", 1))
            sess.d.setdefault("verified_state", {})[p] = qaprofile.today()  # flush vào qa.verified.statePaths lúc stop
        except ProbeError as e:
            print("%s → lỗi: %s" % (p, e))
    sess.save()
    if a.out and dump:
        write_text(os.path.abspath(a.out), json.dumps(dump if len(a.paths) > 1 else next(iter(dump.values())),
                                                      ensure_ascii=False, indent=2))
        print("đã ghi %d giá trị vào %s" % (len(dump), os.path.abspath(a.out)))


def cmd_call(a):
    sess = Session.load(a.session)
    dev = sess.device()
    pr = need_probe(dev, "Gọi hàm trong game")
    j = pr.post("/call", method=a.method, args=a.args)
    sess.step("call", a.desc or "", method="%s(%s)" % (a.method, ", ".join(a.args)))
    head = "%s(%s)" % (a.method, ", ".join(a.args))
    prefix = (qa_profile().get("conventions") or {}).get("cheatMethodPrefix") or "Cheat_"
    if a.method.split(".")[-1].startswith(prefix):
        sess.d.setdefault("verified_cheats", {})[a.method] = qaprofile.today()  # flush vào qa.verified.cheats lúc stop
        sess.save()
    if a.out:
        write_text(os.path.abspath(a.out), json.dumps(j.get("value"), ensure_ascii=False, indent=2))
        print("%s → đã ghi kết quả vào %s" % (head, os.path.abspath(a.out)))
    else:
        print(format_value(head, j.get("value"), full=a.full))
    if not a.no_shot:
        time.sleep(a.wait)
        observe(sess, dev, "call")
    maybe_auto_audit(sess, dev, "call")


def cmd_types(a):
    sess = Session.load(a.session)
    dev = sess.device()
    pr = need_probe(dev, "Tra type/hàm trong game")
    j = pr.get("/types", q=a.query, timeout=20)
    for t in j.get("types", []):
        print("%s" % t.get("type"))
        for m in t.get("members", [])[:40]:
            print("    %s" % m)


def cmd_timescale(a):
    sess = Session.load(a.session)
    dev = sess.device()
    pr = need_probe(dev, "Đổi timeScale")
    j = pr.post("/timescale", value=a.value)
    sess.step("note", "timeScale %s → %s (tăng tốc chờ, không phải thao tác người chơi)" % (j.get("previous"), a.value))
    print("timeScale %s → %s" % (j.get("previous"), j.get("value")))


def cmd_cheat(a):
    sess = Session.load(a.session)
    dev = sess.device()
    pr = need_probe(dev, "Bật/tắt cheat của game")
    params = {} if a.mode == "status" else {"on": 1 if a.mode == "on" else 0}
    j = pr.get("/cheat", **params)
    cg = j.get("cheatGate") or {}
    print("cheat game: %s%s · cổng probe %s%s" % (
        {True: "BẬT", False: "tắt"}.get(j.get("cheat"), "?"), (" (qua %s)" % j["via"]) if j.get("via") else "", j.get("gate"),
        (" · cheat-gate %s %s" % (cg.get("state"), cg.get("deviceId"))) if cg.get("present") else ""))
    if a.mode != "status":
        sess.step("cheat", "(chuẩn bị) %s cheat của game" % ("Bật" if a.mode == "on" else "Tắt"))
        if j.get("gate") != "open":
            print("⚠ cổng probe đóng sau khi tắt cheat (cổng theo cờ cheat của game) — `cheat on` không gọi được nữa; bật lại trên máy rồi `probe-connect`")


def cmd_mark(a):
    sess = Session.load(a.session)
    rec = sess.step("mark", a.name or "")
    sess.d["mark"] = rec["n"]
    sess.d.setdefault("cases", []).append({"name": a.name or "", "step": rec["n"], "t": rec["t"]})
    sess.save()
    print("mark #%d %s — bước tái hiện của bug sẽ tính từ đây" % (rec["n"], a.name or ""))


def cmd_note(a):
    sess = Session.load(a.session)
    text = a.text or ""
    if a.case:
        # Ca có mã trong charter (<FEATURE>-NN): bookkeeping riêng → coverage/charter diff đọc được.
        if not a.result:
            raise QaError("note --case cần --result pass|fail|blocked|skip")
        sc = qaprofile.split_case(a.case, sess.d.get("charter") or "")
        if not sc:
            raise QaError("--case dạng <tên-file-charter>/<P0-01> (vd offline-earning/P0-01); chỉ P0-01 khi phiên đã gắn charter (`charter new`/`charter diff --charter`)")
        charter, cid = sc
        if not charter:
            raise QaError("--case %s thiếu tên charter — ghi <charter>/%s (xem `charter list`)" % (a.case, cid))
        case = qaprofile.full_case(charter, cid)
        rec = sess.step("note", "[%s] %s: %s" % (case, a.result, text) if text else "[%s] %s" % (case, a.result), case=case, result=a.result,
                        bug=a.bug)
        append_jsonl(sess.path("cases.jsonl"), {"case": case, "charter": charter, "id": cid, "result": a.result, "bug": a.bug,
                                                "text": text, "step": rec["n"], "t": rec["t"], "mark": sess.d.get("mark", 0)})
        sess.d["charter"] = charter
        sess.save()
        print("ca %s → %s%s" % (case, a.result, (" (bug #%s)" % a.bug) if a.bug else ""))
        return
    if not text:
        raise QaError("note cần nội dung (hoặc --case <MÃ> --result …)")
    sess.step("note", text)
    if a.result:
        cases = sess.d.setdefault("cases", [])
        if cases:
            cases[-1]["result"] = a.result
            cases[-1]["note"] = text
        sess.save()
    print("đã ghi chú")


def cmd_steps(a):
    sess = Session.load(a.session)
    steps = sess.steps_since_mark() if not a.all else [s for s in read_jsonl(sess.path("steps.jsonl"))]
    print(format_steps(steps) or "(chưa có bước nào từ mark gần nhất)")


def cmd_bug(a):
    root = repo_root()
    sess = Session.load(a.session)
    dev = sess.device()
    bh = sess.d.get("bughub") or {}
    code = a.code or bh.get("code")
    endpoint = a.endpoint or bh.get("endpoint") or bughub_endpoint(root)
    if not code:
        raise QaError("không có mã BugHub — chạy /bughub-setup")
    form = read_json(os.path.join(state_root(), "bughub-form-%s.json" % code.upper()), {}) or {}
    idx_path = os.path.join(state_root(), "bugs-index.jsonl")
    if a.steps and a.steps != "auto":
        steps = (("Điều kiện: %s\n" % a.precondition) if a.precondition else "") + a.steps.replace("\\n", "\n")
    else:
        auto = sess.steps_since_mark()
        it = (read_json(sess.path("issues.json"), {}) or {}).get(a.log_sig or "")
        if it and it.get("step"):  # lỗi log: dừng ở bước làm phát sinh lỗi
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
    # chống trùng trong máy (mọi phiên của project): chữ ký log · tiêu đề · màn + loại
    if not a.force:
        dup, why = find_duplicate(read_jsonl(idx_path), code, a.title, a.log_sig or "", screen, a.type)
        if dup:
            raise QaError("trùng bug đã log #%s \"%s\" (phiên %s; %s) — bỏ qua, hoặc --force nếu thật sự khác" % (
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
    fields = {"title": a.title, "featureKey": a.feature or sess.d.get("feature") or ctx.get("screen", ""),
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
        raise QaError("bug cần --title và --actual")
    if a.dry_run:
        print("DRY-RUN — chưa gửi. Report: %s" % bug_dir)
        print(json.dumps({k: report[k] for k in ("title", "category", "featureKey", "reproduceRate", "tagIds")}, ensure_ascii=False))
        print("steps:\n" + report["steps"])
        return
    if a.via == "ingame":
        pr = need_probe(dev, "Log bug qua Bug Logger trong game")
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
            print("BugHub: TRÙNG report đã có #%s %s" % (resp.get("number"), resp.get("url", "")))
        elif resp.get("number"):
            print("BUG #%s đã log → %s" % (resp["number"], resp.get("url", "")))
        else:
            print("BugHub đã nhận report (%s) id=%s — số issue sẽ có khi server xử lý xong" % (status, report["id"]))
    else:
        append_jsonl(sess.path("bugs-failed.jsonl"), rec)
        raise QaError("BugHub từ chối (%s): %s — report giữ ở %s, gửi lại bằng `bug-resend %s`" % (
            status, resp.get("error") or resp, bug_dir, report["id"]))


def cmd_bug_resend(a):
    sess = Session.load(a.session)
    bh = sess.d.get("bughub") or {}
    d = sess.path("bugs", a.report_id)
    report = read_json(os.path.join(d, "report.json"))
    if not report:
        raise QaError("không thấy report %s" % a.report_id)

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
        return "không (hộp đen)"
    cg = pr.get("cheatGate") or {}
    if pr.get("gate") != "open":
        return "có nhưng cổng đóng (%s) — hộp đen" % (pr.get("gateReason") or "?")
    return "có (v%s%s)" % (pr.get("version"), (" · cheat-gate máy %s" % cg.get("deviceId")) if cg.get("state") == "unlocked" else "")


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
    out = ["# Auto test device — %s" % (d.get("feature") or "(không tên)"), "",
           "| | |", "|---|---|",
           "| Phiên | `%s` |" % d["id"],
           "| Thiết bị | %s · %s (`%s`) |" % (dv.get("model"), dv.get("os"), dv["id"]),
           "| Build | %s v%s (%s)%s |" % (d["package"], app.get("version"), app.get("build"),
                                       (" · %s@%s" % (app.get("commitSha", "")[:9], app.get("branch"))) if app.get("commitSha") else ""),
           "| Probe | %s |" % probe_report_line(d.get("probe") or {}),
           "| Thử lại lỗi API | %s |" % describe_api_retry(d.get("api_retry")),
           "| Thao tác | %d · ảnh %d · ca test %d |" % (len(actions), d.get("shot", 0), len(cases)),
           "| Khởi động nguội | %s |" % (("%d ms" % d["launch_ms"]) if d.get("launch_ms") else "?"), ""]
    out.append("## Bug đã log (%d)" % len(bugs))
    for b in bugs:
        out.append("- %s **%s** [%s/%s]%s" % (("#%s" % b["number"]) if b.get("number") else "(chờ số)", b["title"],
                                             b.get("severity"), b.get("type"), (" — " + b["url"]) if b.get("url") else ""))
    if failed:
        out.append("- ⚠ %d report gửi lỗi (xem bugs-failed.jsonl, `bug-resend`)" % len(failed))
    out.append("")
    out.append("## Ca test")
    for c in cases:
        out.append("- %s — %s%s" % (c.get("name") or "?", c.get("result", "chưa ghi kết quả"), (": " + c["note"]) if c.get("note") else ""))
    out.append("")
    unreported = [it for it in issues.values() if not it.get("bug")]
    out.append("## Lỗi trong log (%d chữ ký, %d chưa log bug)" % (len(issues), len(unreported)))
    for it in sorted(issues.values(), key=lambda x: SEVERITIES.index(x["severity"]) if x["severity"] in SEVERITIES else 9):
        out.append("- [%s] %s ×%d%s — `%s`" % (it["severity"], it["kind"], it["count"], (" → #%s" % it["bug"]) if it.get("bug") else "",
                                             clip(it["first"], 150)))
    if perf:
        out.append("")
        out.append("## Hiệu năng")
        keys = sorted(set(k for p in perf for k in p if k not in ("t", "step")))
        for k in keys:
            vals = [p[k] for p in perf if isinstance(p.get(k), (int, float))]
            if vals:
                out.append("- %s: min %s · max %s · cuối %s" % (k, min(vals), max(vals), vals[-1]))
    out += report_extra_sections(sess)
    out.append("")
    out.append("Thư mục phiên: `%s`" % sess.dir)
    return "\n".join(out) + "\n"


def report_extra_sections(sess):
    """Mục mới của report 1.5: ca theo charter + coverage, audit tự động/nhiễu, kỳ vọng không đạt, ma trận môi trường,
    selfcheck, autotest-import, hồ sơ QA học được."""
    d = sess.d
    out = []
    cases = read_jsonl(sess.path("cases.jsonl"))
    if cases:
        out += ["", "## Ca theo charter (%d)" % len(cases)]
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
        out += ["", "## Audit visual: %d lần (%d tự động khi đổi màn) · %d phát hiện · %d ẩn theo luật nhiễu" % (len(audits), auto, tot, sup)]
        for k, n in sorted(kinds.items(), key=lambda kv: -kv[1])[:12]:
            out.append("- %s ×%d" % (k, n))
    ef = d.get("expect_failed") or []
    if ef:
        out += ["", "## Kỳ vọng không đạt (%d)" % len(ef)]
        for e in ef:
            out.append("- bước #%s: `%s` (màn: %s)" % (e.get("step"), e.get("expect"), " > ".join(e.get("screens") or []) or "?"))
    em = read_text(sess.path("env-matrix.md"))
    if em:
        out += [""] + em.rstrip("\n").splitlines()
    sc = d.get("selfcheck")
    if sc:
        out += ["", "## Selfcheck công cụ: %s (%d mục%s)" % ("OK" if not sc.get("fail") else "%d FAIL" % sc["fail"], sc.get("n", 0), ", kết quả phiên kém tin cậy" if sc.get("fail") else "")]
    at = d.get("autotest")
    if at:
        out += ["", "## Autotest Editor đã nhập: %s (%s) · %s · màn OK %d · màn lỗi %d" % (
            at.get("runId"), at.get("version"), " ".join("%s %d" % kv for kv in (at.get("bySeverity") or {}).items()),
            len(at.get("coveredScreens") or []), len(at.get("failedScreens") or []))]
    obs = d.get("observed") or {}
    if obs or d.get("verified_cheats") or d.get("verified_state"):
        out += ["", "## Hồ sơ QA học được (đã ghi vào project-profile.json lúc stop)",
                "- Màn đã mở: %d — %s" % (len(obs), ", ".join(sorted(obs))),
                "- Cheat đã gọi OK: %s" % (", ".join(sorted(d.get("verified_cheats") or {})) or "—"),
                "- State path đã đọc OK: %s" % (", ".join(sorted(d.get("verified_state") or {})) or "—")]
    mig = read_text(sess.path("migration.md"))
    if mig:
        out += [""] + mig.rstrip("\n").splitlines()
    return out


def cmd_stop(a):
    sess = Session.load(a.session)
    ar = sess.d.get("api_retry") or {}
    if ar.get("state") == "armed":
        # Gỡ trước khi viết report: lỗi API sau phiên này không được gõ prompt auto-test vào pane nữa.
        st, line = api_retry(repo_root(), "off")
        sess.d["api_retry"] = {"state": "disarmed" if st in ("disarmed", "not_armed") else st, "line": line}
        if st not in ("disarmed", "not_armed"):
            print("thử lại lỗi API: gỡ không được (%s) — gõ `/auto-clear off`" % line)
    close_session(sess)
    print("đã dừng phiên %s · report: %s" % (sess.id, sess.path("report.md")))


def cmd_api_retry(a):
    """Bật lại / gỡ / xem thử lại lỗi API của phiên hiện tại (vd dev đã `/auto-clear off`, hoặc start --no-api-retry)."""
    sess = Session.load(a.session)
    if a.action != "status":
        st, line = api_retry(repo_root(), "arm" if a.action == "on" else "off")
        if a.action == "off" and st in ("disarmed", "not_armed"):
            st = "disarmed"
        sess.d["api_retry"] = {"state": st, "line": line}
        sess.save()
    print("thử lại lỗi API: %s" % describe_api_retry(sess.d.get("api_retry")))


def cmd_retry(a):
    """Chạy lại một lệnh qadevice mỗi `--every` giây tới khi qua: lỗi tạm thời (máy rút cáp/offline, adb treo, mạng
    hay BugHub chập chờn, probe mất) không được làm dừng phiên test. Lệnh con chạy trong process riêng, output in
    thẳng ra. Hết `--max-wait` mà vẫn lỗi → thoát RETRY_PENDING_RC để agent gọi lại đúng lệnh `retry` này."""
    argv = list(a.argv or [])
    if argv and argv[0] == "--":
        argv = argv[1:]
    if not argv:
        raise QaError("retry cần lệnh qadevice, vd `retry -- shot` · `retry -- bug-resend <id>` · `retry -- start auto --feature …`")
    if argv[0] == "retry":
        raise QaError("retry lồng retry — bỏ một lớp")
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
                print("retry: qua ở lần %d" % n)
            return
        if rc in RETRY_FATAL_RC:
            raise QaError("retry: lệnh thoát mã %d — lỗi cần người xử lý (cú pháp / BugHub chưa setup), thử lại vô ích" % rc)
        if time.time() + every > deadline:
            print("retry: vẫn lỗi sau %d lần (mã %d)" % (n, rc))
            print("→ không dừng phiên: chạy lại đúng lệnh `retry` này (mỗi %ds một lần, tới khi qua)" % int(every))
            sys.exit(RETRY_PENDING_RC)
        print("retry: lần %d lỗi (mã %d) — thử lại sau %ds" % (n, rc, int(every)))
        sys.stdout.flush()
        time.sleep(every)


def close_previous_session():
    """`start` khi phiên trước chưa `stop` (agent bỏ dở): đóng gọn nó — trả cài đặt máy, tắt log nền, ghi report."""
    try:
        prev = Session.load(None)
    except QaError:
        return
    if prev.d.get("ended"):
        return
    try:
        close_session(prev, quiet=True)
        print("đã đóng phiên trước chưa stop: %s" % prev.id)
    except Exception as e:  # máy cũ đã rút / phiên hỏng — không chặn phiên mới
        log_stop(prev)
        prev.d["ended"] = now_iso()
        prev.save()
        print("đóng phiên trước %s không trọn: %s" % (prev.id, e))


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
                dev.probe.post("/stop", timeout=5)  # probe thôi nghe; phiên sau (kể cả --attach) dùng token mới
            except QaError:
                pass
        for n in revert_env(sess, dev):
            print("dọn dẹp môi trường: %s" % n)
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
        print("dọn dẹp: %s" % e)
    log_stop(sess)
    sess.d["ended"] = now_iso()
    sess.save()
    try:
        flush_profile(sess, quiet=quiet)
    except Exception as e:  # không để lỗi ghi hồ sơ làm mất report
        print("hồ sơ QA: không ghi được (%s)" % e)
    write_text(sess.path("report.md"), build_session_report(sess))


def _is_semver(v):
    return bool(re.match(r"^\d+\.\d+\.\d+", v or ""))


def _ver(v):
    return tuple(int(x) if x.isdigit() else 0 for x in re.split(r"[.\-]", v or "0"))


def read_manifest(root):
    return read_json(os.path.join(root, "Packages", "manifest.json"), {}) or {}


def find_probe_in_project(root):
    """Probe trong project: package (phiên bản/khai báo trong manifest), bản chép cũ trong Assets, registry EZG."""
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
    """Thêm/sửa một dependency, giữ nguyên thứ tự các dòng khác (dòng mới chèn theo thứ tự chữ cái)."""
    path = os.path.join(root, "Packages", "manifest.json")
    man = read_json(path, None)
    if not isinstance(man, dict):
        raise QaError("không đọc được %s" % path)
    eol = "\n" if read_text(path).endswith("\n") else ""  # Unity ghi manifest không có newline cuối — giữ nguyên
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
    """Gỡ bản chép cũ (v1.0/1.1) — package thay thế. Giữ cấu hình tự đặt khác mặc định bằng file ghi đè."""
    notes = []
    d = os.path.join(root, rel_dir)
    cfg = read_json(os.path.join(d, "Resources", "EzgQaProbeConfig.json"), {}) or {}
    det = detect_cheat_config(root)
    custom = {k: cfg[k] for k in ("releaseGate", "cheatOn", "cheatOff") if cfg.get(k) and cfg[k] != det.get(k)}
    if cfg.get("autoCheat") is False:
        custom["autoCheat"] = False
    if custom:
        write_probe_override(root, custom)
        notes.append("giữ cấu hình probe tự đặt (%s) ở %s" % (", ".join(sorted(custom)), PROBE_OVERRIDE))
    shutil.rmtree(d, ignore_errors=True)
    if os.path.isfile(d.rstrip(os.sep) + ".meta"):
        os.remove(d.rstrip(os.sep) + ".meta")
    notes.append("đã gỡ bản probe chép cũ %s/ — thay bằng package %s" % (rel_dir, PROBE_PACKAGE))
    return notes


def write_probe_override(root, values):
    path = os.path.join(root, PROBE_OVERRIDE)
    cur = read_json(path, {}) or {}
    cur.update(values)
    os.makedirs(os.path.dirname(path), exist_ok=True)
    write_json(path, cur)


def effective_probe_config(root):
    """Config probe sẽ dùng khi chạy: tự dò GameSystems (như ProbeAutoConfig trong package) + file ghi đè nếu có."""
    det = detect_cheat_config(root)
    ov = read_json(os.path.join(root, PROBE_OVERRIDE), {}) or {}
    out = {"override": bool(ov)}
    for k in ("releaseGate", "cheatOn", "cheatOff"):
        v = ov.get(k) or det[k]
        out[k] = "" if v == "none" else v
    return out


def detect_cheat_config(root):
    """GameSystems của template EZG: cờ cheat (releaseGate), hàm bật cheat qua CheatGate (cheatOn), hàm tắt (cheatOff)."""
    out = {"releaseGate": "", "cheatOn": "", "cheatOff": ""}
    for path in glob.glob(os.path.join(root, "Assets", "**", "GameSystems.cs"), recursive=True):
        text = read_text(path)
        if not re.search(r"public\s+static\s+bool\s+isCheat\b", text):
            continue
        ns = re.search(r"^\s*namespace\s+([\w.]+)", text, re.M)
        prefix = ("%s.GameSystems" % ns.group(1)) if ns else "GameSystems"
        out["releaseGate"] = prefix + ".isCheat"
        # Chỉ bản hỏi CheatGate (nhận Action<bool>); TryEnableCheat() kiểu cũ hỏi Remote Config và có thể TẮT cheat.
        if re.search(r"public\s+static\s+void\s+TryEnableCheat\s*\(\s*(System\.)?Action\s*<\s*bool\s*>", text):
            out["cheatOn"] = prefix + ".TryEnableCheat"
        if re.search(r"public\s+static\s+void\s+DisableCheat\s*\(", text):
            out["cheatOff"] = prefix + ".DisableCheat"
        return out
    return out


def detect_gate(root):
    """Cổng mở probe ở build release: cờ cheat của template EZG nếu có (GameSystems.isCheat)."""
    return detect_cheat_config(root)["releaseGate"]


def cheat_gate_info(root):
    """com.ezg.cheat-gate trong project → (phiên bản | None, mã project trong GameConstant.CheatGateProject | None)."""
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
        notes.append("ghi đè releaseGate=%s ở %s" % (a.gate, PROBE_OVERRIDE))
    info = find_probe_in_project(root)
    for line in notes or ["package %s %s đã có trong manifest — không đổi" % (PROBE_PACKAGE, info["package"])]:
        print(line)
    cfg = effective_probe_config(root)
    cg_ver, cg_code = cheat_gate_info(root)
    print("config: releaseGate=%s · cheatOn=%s · cheatOff=%s" % (cfg["releaseGate"] or "-", cfg["cheatOn"] or "(gán releaseGate)",
                                                               cfg["cheatOff"] or "(gán releaseGate)"))
    print("cổng release: %s" % (
        ("com.ezg.cheat-gate %s (mã %s) — máy có trong danh sách test tự mở, không cần tap" % (cg_ver, cg_code or "?")) if cg_ver
        else ("cờ %s phải true (bật cheat trên máy)" % cfg["releaseGate"]) if cfg["releaseGate"]
        else "không có — build release luôn đóng, chỉ development build mở"))


# ───────────────────────────────────────────────────────────── hồ sơ QA: profile init | sync | show

def _game_name(root):
    ps = project_settings(root)
    prof = project_profile(root)
    return ps.get("productName") or prof.get("displayName") or prof.get("projectName") or os.path.basename(root)


def refresh_profile_doc(root, qa=None, det=None, create=False, status=None):
    """Điền các vùng <!-- auto:… --> của .claude/docs/QaProfile.md từ qa + kết quả dò; tạo từ template nếu thiếu (create).
    Chữ dev ngoài vùng auto giữ nguyên. Trả đường dẫn hoặc None."""
    qa = qa or qa_profile(root, refresh=True)
    doc_path = qa_path(root, "profileDoc")
    tpl_path = os.path.join(root, qaprofile.TEMPLATE_REL)
    doc = read_text(doc_path, "")
    if not doc:
        if not create:
            return None
        doc = read_text(tpl_path, "")
        if not doc:  # repo chưa nhận Docs/QaProfile.template từ Feature Hub → khung tối thiểu, cập nhật template sau vẫn giữ chữ dev
            doc = qaprofile.MINIMAL_QA_DOC
            print("  ⚠ thiếu %s — dùng khung tối thiểu; cập nhật bộ AI của template (agents-update) để có khung đầy đủ" % qaprofile.TEMPLATE_REL)
        doc = doc.replace("<Tên game>", _game_name(root))
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
    """Tự dò quy ước → ghi block qa (chỉ key chưa có, trừ --force) + QaProfile.md + charters/README. Trả (patch, det, lines)."""
    det = qaprofile.detect_conventions(root)
    prof = project_profile(root)
    existing = prof.get("qa") if isinstance(prof.get("qa"), dict) else {}
    ex_conv = existing.get("conventions") or {}
    conv = {}
    lines = []
    for k in QA_DEFAULTS["conventions"]:
        info = det.get(k) or {"value": QA_DEFAULTS["conventions"][k], "source": "default", "evidence": ""}
        if k in ex_conv and not force:
            lines.append("  %-22s %-40s dev      (giữ: %s)" % (k, clip(json.dumps(ex_conv[k], ensure_ascii=False), 40), info.get("evidence", "")))
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
    lines.append("  features: %d giá trị enum · %d khớp mẫu chết %s · nguồn code: %s" % (
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
        lines.append("  tạo %s" % os.path.relpath(readme, root))
    lines.append("  ghi qa (status=%s) → %s" % (qa_new.get("status"), qaprofile.PROFILE_REL))
    if doc:
        lines.append("  QaProfile.md → %s (Status: %s — dev đọc lại rồi đổi `reviewed`)" % (os.path.relpath(doc, root), qaprofile.doc_status(read_text(doc))))
    return patch, det, lines


CHARTERS_README = """# Charter QA của project

Mỗi file `<feature>.md` là một **test charter** của skill `/auto-test-device`: oracle (giá trị kỳ vọng kèm nguồn) và
danh sách ca có **mã** dạng `<FEATURE>-NN` (vd `SHOP-03`). Agent ghi kết quả từng ca bằng
`qadevice.py note --case SHOP-03 --result pass|fail|blocked|skip`, `coverage` đối chiếu ca đã chạy / chưa chạy,
`charter diff` liệt kê ca còn thiếu trong phiên.

- Tạo charter mới: `python3 .claude/skills/auto-test-device/scripts/qadevice.py charter new "<tên feature>"`.
- Chỉ ghi **ca test + oracle**, không ghi kết quả từng phiên (kết quả nằm trong `.git/auto-test-device/sessions/`).
- Commit cùng feature; dev sửa thoải mái (thêm ca, sửa oracle khi GDD đổi). Giữ nguyên mã ca đã có — thêm số mới.
"""


def profile_sync(root, dry_run=False):
    """Đẩy quy ước xuống hai file build-side: EzgQaProbeConfig.json (5 key probe) + EZGAutoTestSettings.json (block liveness).
    Merge: giữ nguyên key khác. Trả danh sách dòng thay đổi."""
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
                changes.append("probe: bỏ %s" % k)
        elif new.get(k) != v:
            changes.append("probe: %s %s → %s" % (k, json.dumps(cur.get(k), ensure_ascii=False) if k in cur else "(chưa có)", json.dumps(v, ensure_ascii=False)))
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
        print("profile init%s — quy ước tự dò (nguồn: auto = dò từ code · dev = đã khai trong profile · default = mặc định template):" % (
            " (dry-run, không ghi)" if a.dry_run else ""))
        for l in lines:
            print(l)
        if a.dry_run:
            print("patch sẽ ghi vào qa: %s" % json.dumps(patch, ensure_ascii=False))
        else:
            print("→ `profile sync` để đẩy xuống EzgQaProbeConfig.json + EZGAutoTestSettings.json; `doctor` để kiểm.")
        return
    if a.op == "sync":
        changes = profile_sync(root, dry_run=a.dry_run)
        for c in changes or ["không có gì đổi — hai file build-side đã khớp profile"]:
            print(("  " if changes else "") + c)
        if changes and not a.dry_run:
            print("đã ghi %s + %s (commit cùng repo; Unity sinh .meta khi import)" % (
                os.path.relpath(qa_path(root, "probeOverride"), root), os.path.relpath(qa_path(root, "autotestSettings"), root)))
        return
    qa, prov = qaprofile.load_qa_profile(root, with_provenance=True)
    if a.json:
        print(json.dumps({"qa": qa, "provenance": prov}, ensure_ascii=False, indent=1))
        return
    print("qa (schema %s · status %s%s):" % (qa.get("schemaVersion"), qa.get("status"), " · CHƯA CÓ block qa — chạy `profile init`" if prov.get("_block") == "missing" else ""))
    for k, v in (qa.get("conventions") or {}).items():
        print("  conventions.%-22s %-44s %s" % (k, clip(json.dumps(v, ensure_ascii=False), 44), prov.get("conventions." + k, "?")))
    lv = qa.get("liveness") or {}
    print("  liveness: mẫu chết %s · forceLive %d · forceDead %d · quan sát %d" % (lv.get("deadFeaturePatterns"), len(lv.get("forceLive") or []),
                                                                             len(lv.get("forceDead") or []), len(lv.get("observed") or {})))
    ver = qa.get("verified") or {}
    print("  verified: cheat %d · state path %d · noise %d luật" % (len(ver.get("cheats") or {}), len(ver.get("statePaths") or {}), len(qa.get("noise") or [])))
    print("  environments: %s" % json.dumps(qa.get("environments"), ensure_ascii=False))
    for k, v in (qa.get("paths") or {}).items():
        print("  paths.%-16s %s%s" % (k, v, "" if os.path.exists(os.path.join(root, v)) else "  (chưa có)"))


# ───────────────────────────────────────────────────────────── doctor (mở rộng) + selfcheck

def autotest_info(root):
    """(phiên bản com.ezg.autotest | None, nguồn 'manifest'|'embedded'|'')."""
    man = read_manifest(root)
    ver = (man.get("dependencies") or {}).get(AUTOTEST_PACKAGE)
    if ver:
        emb = read_json(os.path.join(root, "Packages", AUTOTEST_PACKAGE, "package.json"), {}) or {}
        return (emb.get("version") or ver), ("embedded" if emb else "manifest")
    emb = read_json(os.path.join(root, "Packages", AUTOTEST_PACKAGE, "package.json"), {}) or {}
    return (emb.get("version"), "embedded") if emb else (None, "")


def doctor_qa(root, strict=False):
    """Phần hồ sơ QA của `doctor`: trả (dòng in, vấn đề bắt buộc, cảnh báo)."""
    lines, problems, warns = [], [], []
    qa, prov = qaprofile.load_qa_profile(root, with_provenance=True)
    conv = qa.get("conventions") or {}
    if prov.get("_block") == "missing":
        problems.append("chưa có block qa trong %s — chạy `profile init`" % qaprofile.PROFILE_REL)
        lines.append("qa:        CHƯA CÓ (mọi quy ước đang là mặc định template) → `profile init`")
    else:
        lines.append("qa:        schema %s · status %s · %d key dev khai" % (qa.get("schemaVersion"), qa.get("status"),
                                                                         sum(1 for k, v in prov.items() if v == "dev" and k.startswith("conventions."))))
        if qa.get("status") == "template":
            warns.append("qa.status còn `template` — `profile init` để tự dò")
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
        lines.append("features:  %s — %d giá trị · %d chết · %d không thấy code gọi · %d đã quan sát (%s)" % (
            conv.get("featuresEnum"), len(names), dead, unref, len((qa.get("liveness") or {}).get("observed") or {}), os.path.relpath(enum_file, root)))
    else:
        problems.append("không thấy enum %s trong code — sửa qa.conventions.featuresEnum" % conv.get("featuresEnum"))
        lines.append("features:  KHÔNG THẤY enum %s" % conv.get("featuresEnum"))
    bases = [b for b in conv.get("screenBaseTypes") or [] if b in classes]
    if bases:
        lines.append("screens:   lớp cơ sở có trong code: %s" % ", ".join(bases))
    else:
        problems.append("không lớp cơ sở màn nào trong qa.conventions.screenBaseTypes tồn tại trong code")
        lines.append("screens:   KHÔNG lớp cơ sở nào tồn tại (%s)" % conv.get("screenBaseTypes"))
    for k in ("uiManagerType", "cheatTypeName", "playerDataFacade", "timeProvider", "trackingEventNameType"):
        v = conv.get(k)
        if v and v not in classes:
            warns.append("%s=%s không thấy trong code" % (k, v))
    csv_files = glob.glob(os.path.join(root, conv.get("csvConfigGlob") or ""), recursive=True) if conv.get("csvConfigGlob") else []
    lines.append("csv:       %d file khớp %s" % (len(csv_files), conv.get("csvConfigGlob")))
    if not csv_files:
        warns.append("csvConfigGlob không khớp file nào — oracle `csv` không dùng được")
    # build-side files đồng bộ?
    ov = read_json(qa_path(root, "probeOverride"), {}) or {}
    drift = [k for k in qaprofile.PROBE_OVERRIDE_KEYS if conv.get(k) not in ("", None, []) and ov.get(k) != conv.get(k)]
    at = (read_json(qa_path(root, "autotestSettings"), {}) or {}).get("liveness") or {}
    lv = qa.get("liveness") or {}
    at_drift = at.get("deadFeaturePatterns") != list(lv.get("deadFeaturePatterns") or []) or sorted(at.get("observedLive") or []) != sorted((lv.get("observed") or {}).keys())
    lines.append("sync:      probe override %s · autotest liveness %s" % ("khớp" if not drift else "LỆCH (%s)" % ", ".join(drift), "khớp" if not at_drift else "LỆCH"))
    if drift or at_drift:
        warns.append("file build-side lệch profile — chạy `profile sync`")
    av, asrc = autotest_info(root)
    if av:
        old = _is_semver(av) and _ver(av) < _ver(AUTOTEST_MIN_VERSION)
        lines.append("autotest:  %s %s (%s)%s" % (AUTOTEST_PACKAGE, av, asrc, " ⚠ < %s: chưa lọc feature chết, báo cáo nhiễu" % AUTOTEST_MIN_VERSION if old else ""))
        if old:
            warns.append("com.ezg.autotest %s < %s" % (av, AUTOTEST_MIN_VERSION))
        rep = qaprofile.latest_report(root, qa)
        lines.append("           report mới nhất: %s" % (os.path.relpath(rep, root) if rep else "chưa có — chạy Ezg > Auto test system rồi `autotest-import`"))
    else:
        lines.append("autotest:  không có %s — Δ-skip theo autotest không dùng được" % AUTOTEST_PACKAGE)
    doc = qa_path(root, "profileDoc")
    st = qaprofile.doc_status(read_text(doc, ""))
    lines.append("docs:      %s %s · charters: %s" % (os.path.relpath(doc, root), ("Status " + st) if st else "CHƯA CÓ", (
        "%d file" % len(qaprofile.list_charters(root, qa))) if os.path.isdir(qa_path(root, "chartersDir")) else "CHƯA CÓ thư mục"))
    if not st:
        problems.append("chưa có %s — `profile init`" % os.path.relpath(doc, root))
    if not os.path.isdir(qa_path(root, "chartersDir")):
        problems.append("chưa có thư mục charter %s — `profile init`" % os.path.relpath(qa_path(root, "chartersDir"), root))
    return lines, problems, warns


def cmd_doctor(a):
    adb = find_adb()
    print("qadevice %s · python %s · %s" % (VERSION, sys.version.split()[0], sys.platform))
    print("adb:       %s" % (adb or "KHÔNG CÓ — cài Android platform-tools hoặc Unity Android module"))
    print("emulator:  %s" % (find_emulator(adb) or "không có (chỉ cần khi muốn tự boot AVD)"))
    print("xcrun:     %s" % ("có" if has_xcrun() else "không (chỉ macOS — iOS Simulator/iPhone)"))
    print("idb:       %s" % (find_idb() or "không — Simulator sẽ chạm qua probe (brew install facebook/fb/idb-companion && pip3 install fb-idb)"))
    print("Pillow:    %s" % ("có" if Image is not None else "KHÔNG — chạy `%s -m pip install --user pillow` để có ảnh xem nhỏ, lưới toạ độ, so khung hình" % ("py" if IS_WIN else "python3")))
    root = repo_root()
    ps = project_settings(root)
    print("project:   %s · %s · Unity %s" % (ps.get("productName"), ps["ids"], ps.get("unityVersion")))
    info = find_probe_in_project(root)
    problems, warns = [], []
    if info["package"]:
        old = _is_semver(info["package"]) and _ver(info["package"]) < _ver(PROBE_MIN_VERSION)
        print("probe:     package %s %s%s" % (PROBE_PACKAGE, info["package"], " ⚠ < %s" % PROBE_MIN_VERSION if old else ""))
        if old:
            warns.append("probe %s < %s" % (info["package"], PROBE_MIN_VERSION))
        print("           tính năng theo bản: %s" % " · ".join("%s ≥ %s" % kv for kv in sorted(PROBE_FEATURES.items())))
    else:
        print("probe:     chưa có package %s trong Packages/manifest.json (`start` tự thêm)" % PROBE_PACKAGE)
        problems.append("chưa có %s trong manifest" % PROBE_PACKAGE)
    if info["legacy"]:
        print("           ⚠ còn bản chép cũ %s (v%s) — `start`/`probe-install` sẽ gỡ" % (info["legacy"], info["legacyVersion"]))
    if not info["registry"]:
        print("           ⚠ manifest không có scoped registry EZG (scope com.ezg) — cài registry qua Feature Hub trước")
        warns.append("thiếu scoped registry com.ezg")
    cfg = effective_probe_config(root)
    print("config:    releaseGate=%s · cheatOn=%s · cheatOff=%s%s" % (
        cfg["releaseGate"] or "-", cfg["cheatOn"] or "-", cfg["cheatOff"] or "-", " (có ghi đè %s)" % PROBE_OVERRIDE if cfg["override"] else ""))
    cg_ver, cg_code = cheat_gate_info(root)
    print("cheat:     %s" % (("com.ezg.cheat-gate %s · mã %s — probe tự mở cổng cho máy có trong danh sách test" % (cg_ver, cg_code or "?"))
                             if cg_ver else "không có com.ezg.cheat-gate — cổng release theo cờ cheat của game (releaseGate)"))
    if not cg_ver:
        warns.append("không có com.ezg.cheat-gate — build release chỉ mở probe khi game bật cheat")
    lines, p2, w2 = doctor_qa(root, strict=a.strict)
    for l in lines:
        print(l)
    problems += p2
    warns += w2
    print("state:     %s" % state_root())
    for w in warns:
        print("  ⚠ " + w)
    for p in problems:
        print("  ✗ " + p)
    if a.strict and problems:
        print("doctor --strict: %d mục bắt buộc chưa đạt" % len(problems))
        sys.exit(1)


SAFE_TAP_WORDS = ("settings", "cài đặt", "cai dat", "setting", "close", "đóng", "dong", "back", "quay lại", "x", "✕", "×", "info", "?")


def cmd_selfcheck(a):
    """Tự kiểm công cụ trên máy đang nối (phiên đang mở): probe trả lời, /ui có element, tap --text trúng rồi quay lại, shot không đen, logs đọc được."""
    sess = Session.load(a.session)
    dev = sess.device()
    results = []

    def rec(name, ok, note=""):
        results.append({"check": name, "status": "pass" if ok is True else ("skip" if ok is None else "fail"), "note": note})
        print("  %s %-10s %s" % ({"pass": "✓", "fail": "✗", "skip": "–"}[results[-1]["status"]], name, note))

    print("selfcheck phiên %s trên %s:" % (sess.id, dev.id))
    pr = dev.probe
    if pr:
        try:
            j = pr.get("/ping", timeout=6)
            rec("probe", True, "v%s gate=%s cheat=%s" % (j.get("version"), j.get("gate"), j.get("cheat")))
        except QaError as e:
            rec("probe", False, str(e))
            pr = None
    else:
        rec("probe", None, "không có probe — hộp đen (ui/tap --text/state/call không dùng được)")
    try:
        v = observe(sess, dev, "selfcheck", logs=False, compare=False)
        warns = analyze_image(v["path"])
        rec("shot", not warns, "%dx%d %s" % (v["shot_w"], v["shot_h"], "; ".join(warns) if warns else "có nội dung"))
    except QaError as e:
        rec("shot", False, str(e))
    try:
        fresh, rep = check_logs(sess, dev)
        lg = sess.d.get("log") or {}
        has = bool(lg.get("file") and os.path.isfile(lg["file"]) and os.path.getsize(lg["file"]) > 0) or bool(pr and sess.d["device"]["kind"] != "android")
        rec("logs", has, "%s · %d lỗi mới" % (os.path.basename(lg.get("file") or "") or "qua probe", len(fresh)))
    except QaError as e:
        rec("logs", False, str(e))
    if pr:
        try:
            j = probe_ui(sess, dev, include_all=False)
            els = j.get("elements") or []
            rec("ui", len(els) > 0, "%d phần tử · màn %s" % (len(els), " > ".join(j.get("screens") or []) or j.get("scene")))
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
                rec("tap", changed, "bấm \"%s\" → %s" % (cand.get("label") or cand.get("text"), "UI đổi" if changed else "UI KHÔNG đổi (input không tới game?)"))
                try:
                    dev.key("back")
                    time.sleep(0.8)
                except (QaError, NotSupported):
                    pass
                sess.step("note", "(selfcheck) bấm \"%s\" rồi Back" % (cand.get("label") or cand.get("text")))
            else:
                rec("tap", None, "không có nút an toàn (%s) trên màn này để thử — dùng `tap --text` ở bước đầu của ca để kiểm" % "/".join(SAFE_TAP_WORDS[:4]))
        except QaError as e:
            rec("ui", False, str(e))
    write_json(sess.path("selfcheck.json"), {"t": now_iso(), "results": results})
    sess.d["selfcheck"] = {"t": now_iso(), "fail": sum(1 for r in results if r["status"] == "fail"), "n": len(results)}
    sess.save()
    bad = [r for r in results if r["status"] == "fail"]
    if bad:
        print("selfcheck: %d mục FAIL — sửa công cụ trước khi test (kết quả phiên sẽ không tin được)" % len(bad))
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
        md += "- Trong số chưa mở, autotest Editor đã mở OK (ưu tiên thấp): %s\n" % ", ".join(cov["unvisited_autotest_ok"])
    write_text(sess.path("coverage.md"), md)
    print(md if not a.quiet else "coverage: %s" % sess.path("coverage.md"))


def cmd_charter(a):
    root = repo_root()
    qa = qa_profile(root)
    cdir = qa_path(root, "chartersDir")
    if a.op == "list":
        chs = qaprofile.list_charters(root, qa)
        if not chs:
            print("chưa có charter nào trong %s — `charter new \"<feature>\"`" % os.path.relpath(cdir, root))
        for ch in chs:
            print("%-28s %-6s %3d ca  %s" % (ch["id"], ch["status"] or "-", len(ch["cases"]), ch["title"]))
        return
    if a.op == "new":
        if not a.feature:
            raise QaError("charter new \"<tên feature>\" [--sources \"GDD/..., file.cs\"]")
        tpl = read_text(os.path.join(SKILL_DIR, "references", "charter.template.md"))
        if not tpl:
            raise QaError("thiếu references/charter.template.md trong skill")
        cid = a.id or qaprofile.charter_slug(a.feature)
        path = os.path.join(cdir, cid + ".md")
        if os.path.isfile(path) and not a.force:
            raise QaError("đã có %s — sửa file đó, hoặc --force để ghi đè" % os.path.relpath(path, root))
        write_text(path, qaprofile.new_charter_text(tpl, a.feature, qa, a.sources or ""))
        if not os.path.isfile(os.path.join(cdir, "README.md")):
            write_text(os.path.join(cdir, "README.md"), CHARTERS_README)
        try:
            sess = Session.load(a.session)
            if not sess.d.get("ended"):
                sess.d["charter"] = cid
                sess.save()
                print("phiên %s gắn charter %s — `note --case %s/P0-01 --result …` (hoặc chỉ P0-01)" % (sess.id, cid, cid))
        except QaError:
            pass
        print("charter mới: %s (mã ca P0-01, P1-01… — tham chiếu %s/P0-01) — điền oracle + ca rồi commit cùng feature" % (os.path.relpath(path, root), cid))
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
    print("charter diff — phiên %s: %d ca planned · %d đã chạy · %d CHƯA chạy" % (sess.id, len(planned), len([c for c in planned if c in run]), len(missing)))
    for pri in sorted(by_pri):
        print("  %s chưa chạy: %s" % (pri, ", ".join("%s %s" % (c, clip(planned[c].get("title", ""), 40)) for c in by_pri[pri])))
    if extra:
        print("  ca ghi ngoài charter: %s" % ", ".join(extra))
    fails = [c for c, r in run.items() if r == "fail"]
    if fails:
        print("  ca FAIL: %s" % ", ".join(fails))


# ───────────────────────────────────────────────────────────── môi trường: aspect · env · env matrix

ASPECT_PRESETS = {"tall": "720x1600", "tablet": "1600x2560", "short": "720x1280", "square": "1080x1080"}
ENV_NAMES = ("font-scale", "dark", "animator", "kill-activities", "trim-memory", "clock", "locale")


def _remember(sess, key, prev):
    ch = sess.d.setdefault("changed", {}).setdefault("env", {})
    if key not in ch:
        ch[key] = prev
    sess.save()


def apply_env(sess, dev, name, value):
    """Áp một biến môi trường trên máy; nhớ giá trị trước để close_session trả. Trả mô tả; NotSupported nếu máy không làm được."""
    pkg = sess.d["package"]
    name = name.lower()
    if name == "font-scale":
        if not hasattr(dev, "font_scale"):
            raise NotSupported("đổi cỡ chữ hệ thống không hỗ trợ trên %s" % dev.kind)
        v = None if str(value).lower() in ("reset", "default", "1", "1.0") else float(value)
        prev = dev.font_scale(1.0 if v is None else v)
        _remember(sess, "font-scale", prev)
        return "cỡ chữ hệ thống %s (trước: %s)" % ("mặc định" if v is None else v, prev or "mặc định")
    if name == "dark":
        if not hasattr(dev, "dark_mode"):
            raise NotSupported("dark mode không hỗ trợ trên %s" % dev.kind)
        on = str(value).lower() in ("on", "1", "yes", "true", "dark")
        prev = dev.dark_mode(on)
        _remember(sess, "dark", prev)
        return "dark mode %s (trước: %s)" % ("BẬT" if on else "tắt", prev)
    if name == "animator":
        if not isinstance(dev, Android):
            raise NotSupported("animator scale chỉ có trên Android")
        try:
            scale = ("%g" % float(value))
        except ValueError:
            raise QaError("animator 0 | 0.5 | 1")
        prev = dev.animator_scale(scale)
        _remember(sess, "animator", prev)
        return "animator/transition/window scale = %s (trước: %s)" % (scale, prev or "1")
    if name == "kill-activities":
        if not isinstance(dev, Android):
            raise NotSupported("always_finish_activities chỉ có trên Android")
        on = str(value).lower() in ("on", "1", "yes", "true")
        prev = dev.always_finish(on)
        _remember(sess, "kill-activities", prev)
        return "Don't keep activities %s (trước: %s) — nền rồi quay lại sẽ tạo lại Activity" % ("BẬT" if on else "tắt", prev or "0")
    if name == "trim-memory":
        if not isinstance(dev, Android):
            raise NotSupported("send-trim-memory chỉ có trên Android")
        lvl = (value or "RUNNING_CRITICAL").upper()
        dev.trim_memory(pkg, lvl)
        return "đã gửi trim-memory %s cho %s (game nhận OnLowMemory/onTrimMemory)" % (lvl, pkg)
    if name == "clock":
        if not isinstance(dev, Android):
            raise NotSupported("đổi giờ máy chỉ hỗ trợ Android emulator/root — dùng cheat tua thời gian của game")
        if str(value).lower() in ("reset", "auto"):
            dev.set_clock(None)
            _remember(sess, "clock", "auto")
            return "đồng hồ máy về tự động"
        m = re.match(r"^([+-]?\d+)([smhd])$", str(value))
        if not m:
            raise QaError("clock +1d | +3h | -30m | +90s | reset")
        secs = int(m.group(1)) * {"s": 1, "m": 60, "h": 3600, "d": 86400}[m.group(2)]
        _remember(sess, "clock", "auto")  # nhớ TRƯỚC: set_clock đã tắt auto_time dù đổi giờ có thể thất bại → stop vẫn bật lại
        prev = dev.set_clock(secs)
        return "đồng hồ máy dời %s (auto_time tắt tới khi stop)" % prev
    if name == "locale":
        if not isinstance(dev, Android):
            raise NotSupported("đổi locale chỉ hỗ trợ Android emulator/root")
        if str(value).lower() == "reset":
            prev = ((sess.d.get("changed") or {}).get("env") or {}).get("locale")
            if not prev:
                raise QaError("chưa đổi locale trong phiên này")
            dev.set_locale(prev)
            return "locale về %s" % prev
        prev = dev.set_locale(value)
        _remember(sess, "locale", prev)
        return "locale %s (trước: %s) — framework đã khởi động lại, mở lại game" % (value, prev or "?")
    raise QaError("env <%s> <giá trị> — hoặc `env matrix`" % "|".join(ENV_NAMES))


def revert_env(sess, dev):
    """Trả mọi thay đổi môi trường + aspect của phiên (gọi lúc stop)."""
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
        raise NotSupported("đổi tỉ lệ màn chỉ có trên Android (wm size). iOS: chọn Simulator khác (`devices`), vd iPhone SE / iPad, rồi `start` lại")
    if spec.lower() == "reset":
        dev.wm_size("reset")
        if (sess.d.get("changed") or {}).get("wm_density"):
            dev.wm_density("reset")
        sess.d["changed"]["wm_size"] = False
        sess.step("note", "(môi trường) tỉ lệ màn về mặc định")
        desc = "màn về kích thước thật"
    else:
        if not re.match(r"^\d+x\d+$", spec):
            raise QaError("aspect WxH | %s | reset" % "|".join(sorted(ASPECT_PRESETS)))
        dev.wm_size(spec)
        sess.d.setdefault("changed", {})["wm_size"] = True
        if a.density:
            dev.wm_density(str(a.density))
            sess.d["changed"]["wm_density"] = True
        sess.step("note", "(môi trường) giả lập màn %s%s" % (spec, " dpi %s" % a.density if a.density else ""))
        desc = "màn giả lập %s%s" % (spec, " dpi %s" % a.density if a.density else "")
    sess.save()
    print("aspect: %s — game nhận cấu hình mới sau 1-2 s (có thể cần `app restart` nếu game chỉ đọc Screen lúc mở)" % desc)
    time.sleep(a.wait)
    observe(sess, dev, "aspect-" + slug(spec, 12))
    sess.d["screens_key"] = None  # ép auto-audit ở lần kế / audit ngay nếu --audit
    if a.audit and dev.probe:
        run_audit(sess, dev, view=sess.d.get("view"), tag="aspect-%s" % slug(spec, 12))


def cmd_env(a):
    sess = Session.load(a.session)
    dev = sess.device()
    if a.name.lower() == "matrix":
        return env_matrix(a, sess, dev)
    if a.value is None:
        raise QaError("env %s <giá trị>" % a.name)
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
    """Ma trận môi trường từ qa.environments: áp → chụp → audit → hoàn nguyên từng bước; tổng hợp env-matrix.md."""
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
        raise QaError("qa.environments trống — khai fontScales/screenSizes/darkMode trong project-profile.json")
    rows = []
    print("env matrix: %d bước trên %s" % (len(steps), dev.id))
    for name, value in steps:
        label = "%s=%s" % (name, value)
        try:
            if name == "aspect":
                if not isinstance(dev, Android):
                    raise NotSupported("wm size chỉ có trên Android")
                dev.wm_size(value)
                sess.d.setdefault("changed", {})["wm_size"] = True
                desc = "màn %s" % value
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
                worst = "audit lỗi: %s" % e
        img_w = analyze_image(v["path"])
        rows.append({"step": label, "status": "ok", "findings": n_f, "worst": worst, "shot": v["n"], "image": "; ".join(img_w)})
        print("  ✓ %-22s %s → %d phát hiện %s %s" % (label, desc, n_f, worst, ("· ẢNH: " + "; ".join(img_w)) if img_w else ""))
        sess.step("env", "(môi trường) %s: %d phát hiện" % (desc, n_f), env=label, findings=n_f, shot=v["n"])
        # hoàn nguyên bước này ngay để bước sau sạch
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
    md = ["## Ma trận môi trường — %s" % (sess.d.get("feature") or ""), "", "| Bước | Kết quả | Phát hiện | Nặng nhất | Ảnh |", "|---|---|---|---|---|"]
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
        print("%d file CSV khớp %s:" % (len(files), pat))
        for f in files:
            print("  %-34s %s" % (os.path.splitext(os.path.basename(f))[0], os.path.relpath(f, root)))
        return
    path, cands = qaprofile.find_csv(root, qa, a.name)
    if not path:
        raise QaError("không thấy CSV '%s'%s — `csv --list`" % (a.name, (" (gần giống: %s)" % ", ".join(os.path.basename(c) for c in cands[:6])) if cands else ""))
    header, rows = qaprofile.read_csv_rows(path)
    rows = qaprofile.filter_rows(rows, a.where)
    cols = a.col or header
    if a.json:
        print(json.dumps([{c: r.get(c, "") for c in cols} for r in rows], ensure_ascii=False, indent=1))
        return
    print("%s · %d dòng%s · cột: %s" % (os.path.relpath(path, root), len(rows), " (lọc %s)" % " ".join(a.where) if a.where else "", ", ".join(header)))
    widths = {c: min(28, max(len(c), *(len(str(r.get(c, ""))) for r in rows[:a.limit])) if rows else len(c)) for c in cols}
    print("  " + " | ".join(c.ljust(widths[c]) for c in cols))
    for r in rows[:a.limit]:
        print("  " + " | ".join(clip(str(r.get(c, "")), widths[c]).ljust(widths[c]) for c in cols))
    if len(rows) > a.limit:
        print("  … %d dòng nữa (--limit N)" % (len(rows) - a.limit))


def cmd_pseudoloc(a):
    sess = Session.load(a.session)
    dev = sess.device()
    pr = need_probe(dev, "Pseudo-localization")
    if not probe_supports(sess, "pseudoloc"):
        print("pseudoloc: " + unsupported_probe(sess, "pseudoloc"))
        print("  thay thế: đổi ngôn ngữ game sang bản dài nhất (vd Đức/Nga) trong Settings rồi `audit` từng màn")
        return
    on = a.mode_onoff == "on"
    j = pr.get("/pseudoloc", on=1 if on else 0, mode=a.mode, timeout=20)
    sess.d["pseudoloc"] = on
    sess.step("note", "(môi trường) pseudo-loc %s (%s) — %s chữ" % ("BẬT" if on else "tắt", a.mode, j.get("count")))
    print("pseudoloc %s · mode %s · %s phần tử chữ đổi" % ("BẬT" if j.get("on") else "tắt", j.get("mode", a.mode), j.get("count")))
    time.sleep(a.wait)
    v = observe(sess, dev, "pseudoloc-" + ("on" if on else "off"))
    if on:
        run_audit(sess, dev, view=v, tag="pseudoloc-%03d" % v["n"])
        print("→ đi qua các màn của charter với pseudo-loc đang bật: auto-audit bắt tràn/cắt chữ; `pseudoloc off` khi xong")


def _lines_since(sess, mark_name=None):
    """Dòng log của app từ mốc (tên mark / 'last' / None = cả phiên)."""
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
            t = marks[-1]["t"]  # ISO UTC; logcat không có năm/UTC → cắt theo vị trí dòng thay vì giờ
            # dùng số dòng tại thời điểm mark nếu đã ghi, không thì lấy 60% cuối
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
    print("tracking: %d dòng khớp /%s/%s" % (len(lines), a.pattern or pat, (" từ mark %s" % a.since) if a.since else ""))
    for l in lines[-a.tail:]:
        m = LOGCAT_RE.match(l)
        print("  " + clip(m.group(6) if m else l, 220))
    if a.expect:
        hit = [l for l in lines if re.search(a.expect, l, re.I)]
        if hit:
            print("  ✓ có event /%s/ (%d lần)" % (a.expect, len(hit)))
            sess.step("check", "Tracking: có event %s" % a.expect, result="pass")
        else:
            print("  ✗ THIẾU event /%s/ — kiểm tra lại code tracking của luồng vừa chơi" % a.expect)
            sess.step("check", "Tracking: THIẾU event %s" % a.expect, result="fail", kind="tracking-missing")
            sys.exit(1)


# ───────────────────────────────────────────────────────────── autotest-import · build update

def cmd_autotest_import(a):
    root = repo_root()
    qa = qa_profile(root)
    path = a.path or qaprofile.latest_report(root, qa)
    if not path or not os.path.isfile(path):
        raise QaError("không có report.json của com.ezg.autotest (%s) — chạy Ezg > Auto test system trong Editor trước, hoặc truyền đường dẫn" % qa_path(root, "autotestReports"))
    rep = qaprofile.parse_autotest_report(path)
    md = qaprofile.autotest_markdown(rep)
    if rep["version"] == "0.1.1":
        md += "\n> ⚠ report của autotest < %s: chưa lọc feature chết, MissingReference chưa theo phạm vi load — số Major/Critical ở suite static/smoke có nhiễu; chỉ dùng danh sách màn mở OK/LỖI và Blocker/Critical có exception.\n" % AUTOTEST_MIN_VERSION
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
    """Cài build mới đè bản đang test (giữ data) → kiểm save migration: state trước/sau theo qa.verified.statePaths."""
    root = repo_root()
    sess = Session.load(a.session)
    dev = sess.device()
    pkg = sess.d["package"]
    path = a.path
    if path in ("latest", "auto"):
        path = find_artifact(root, dev.platform)
        if not path:
            raise QaError("không thấy bản build trong repo — truyền đường dẫn apk/app")
    if not os.path.exists(path):
        raise QaError("không có file %s" % path)
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
                    out[p] = "<lỗi: %s>" % e
        return out
    before["state"] = snapshot()
    print("build update: %s v%s → cài %s%s" % (pkg, before["version"][0], os.path.basename(path), " (giữ data)" if not a.wipe else " (XOÁ data)"))
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
    md = ["## Cập nhật build — save migration", "",
          "- Trước: v%s (%s) · Sau: v%s (%s) · file `%s`" % (before["version"][0], before["version"][1], after["version"][0], after["version"][1], path),
          "- State so sánh: %d đường dẫn · %d khác" % (len(paths), len(diff)), ""]
    if diff:
        md += ["| Đường dẫn | Trước | Sau |", "|---|---|---|"]
        for p, b, c in diff:
            md.append("| `%s` | %s | %s |" % (p, clip(json.dumps(b, ensure_ascii=False), 60), clip(json.dumps(c, ensure_ascii=False), 60)))
    elif not paths:
        md.append("- (chưa có qa.verified.statePaths — `state <path>` vài giá trị quan trọng trước khi `build update`, hoặc --state <path>)")
    write_text(sess.path("migration.md"), "\n".join(md) + "\n")
    sess.step("build-update", "Cập nhật game lên bản mới%s rồi mở lại" % (" (xoá data)" if a.wipe else " giữ nguyên dữ liệu"),
              before=before["version"], after=after["version"], changed=len(diff))
    sess.d["app"].update({"version": after["version"][0], "build": after["version"][1]})
    sess.save()
    print("\n".join(md))
    observe(sess, dev, "build-update")
    if diff:
        print("⚠ %d state đổi sau khi cập nhật — xem migration.md; mất tiền/tiến độ = bug save migration (critical)" % len(diff))


# ───────────────────────────────────────────────────────────── flush tri thức về profile

def flush_profile(sess, quiet=False):
    """Lúc stop: màn đã quan sát → qa.liveness.observed; cheat/state đã gọi OK → qa.verified. Làm mới vùng auto của QaProfile.md."""
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
    line = "hồ sơ QA: +%d màn quan sát · +%d cheat · +%d state path → %s%s" % (
        len(obs), len(cheats), len(states), qaprofile.PROFILE_REL, (" + %s" % os.path.relpath(doc, root)) if doc else "")
    if not quiet:
        print(line)
        print("  → `profile sync` để autotest Editor nhận observedLive; dev duyệt QaProfile.md (Status → reviewed); commit cùng repo")
    return line


# ───────────────────────────────────────────────────────────── argparse

def build_parser():
    p = argparse.ArgumentParser(prog="qadevice", description="Driver thiết bị cho /auto-test-device (EZG).")
    p.add_argument("--session", help="id phiên (mặc định: phiên hiện tại)")
    p.add_argument("--no-auto-audit", action="store_true", help="không tự audit khi màn đổi sau thao tác (mặc định: có, cần probe)")
    sub = p.add_subparsers(dest="cmd")

    def act(sp, coords=True):
        sp.add_argument("--as", dest="desc", help="mô tả hành động như người chơi (ghi vào bước tái hiện bug)")
        sp.add_argument("--wait", type=float, default=0.8, help="chờ trước khi chụp sau thao tác (s)")
        sp.add_argument("--no-shot", action="store_true", help="không chụp sau thao tác")
        sp.add_argument("--grid", action="store_true", help="ảnh sau thao tác kèm lưới toạ độ")
        sp.add_argument("--marks", action="store_true", help="ảnh sau thao tác đánh số phần tử UI (probe)")
        sp.add_argument("--label", help="nhãn tên file ảnh")
        if coords:
            sp.add_argument("--space", choices=("view", "device"), default="view", help="toạ độ theo ảnh xem (mặc định) hay px máy")
            sp.add_argument("--inject", action="store_true", help="chạm qua probe thay vì input hệ thống")
            sp.add_argument("--allow-overlay", action="store_true",
                            help="cho chạm adb vào vùng overlay của app khác (mặc định chặn — chạm sẽ trúng app kia)")

    sp = sub.add_parser("doctor", help="kiểm công cụ trên máy + hồ sơ QA của project (--strict: rc 1 khi thiếu mục bắt buộc)")
    sp.add_argument("--strict", action="store_true")
    sp.set_defaults(fn=cmd_doctor)

    sp = sub.add_parser("profile", help="hồ sơ QA của project (.claude/project-profile.json[qa] + QaProfile.md): init | sync | show")
    sp.add_argument("op", choices=("init", "sync", "show"))
    sp.add_argument("--force", action="store_true", help="init: ghi đè cả key dev đã khai")
    sp.add_argument("--dry-run", action="store_true", help="init/sync: chỉ in, không ghi")
    sp.add_argument("--json", action="store_true", help="show: in JSON kèm nguồn từng key")
    sp.set_defaults(fn=cmd_profile)

    sp = sub.add_parser("selfcheck", help="tự kiểm công cụ trên máy đang nối: probe, /ui, tap --text, shot, logs (ghi selfcheck.json; rc 1 nếu fail)")
    sp.add_argument("--no-tap", action="store_true", help="không thử bấm nút an toàn")
    sp.set_defaults(fn=cmd_selfcheck)

    sp = sub.add_parser("coverage", help="ca planned/run/pass/fail theo charter + màn sống chưa mở → coverage.md")
    sp.add_argument("--charter", help="chỉ một charter (tên file không đuôi)")
    sp.add_argument("--quiet", action="store_true")
    sp.set_defaults(fn=cmd_coverage)

    sp = sub.add_parser("charter", help="charter trong repo (.claude/docs/qa/charters): list | new \"<feature>\" | diff")
    sp.add_argument("op", choices=("list", "new", "diff"))
    sp.add_argument("feature", nargs="?", help="new: tên feature")
    sp.add_argument("--id", help="new: tên file charter (mặc định slug của feature)")
    sp.add_argument("--sources", help="new: nguồn đã đọc (GDD/TechSpec/file)")
    sp.add_argument("--charter", help="diff: tên charter (mặc định charter đang gắn với phiên)")
    sp.add_argument("--force", action="store_true")
    sp.set_defaults(fn=cmd_charter)

    sp = sub.add_parser("aspect", help="(Android) giả lập kích thước màn: WxH | tall | tablet | short | square | reset")
    sp.add_argument("spec")
    sp.add_argument("--density", type=int, help="wm density kèm theo")
    sp.add_argument("--wait", type=float, default=2.0)
    sp.add_argument("--audit", action="store_true", help="audit ngay sau khi đổi")
    sp.set_defaults(fn=cmd_aspect)

    sp = sub.add_parser("env", help="môi trường máy: font-scale 1.3|reset · dark on|off · animator 0|0.5|1 · kill-activities on|off · "
                                    "trim-memory [LEVEL] · clock +1d|reset · locale vi-VN — hoặc `env matrix [--quick]`")
    sp.add_argument("name")
    sp.add_argument("value", nargs="?")
    sp.add_argument("--wait", type=float, default=2.0)
    sp.add_argument("--audit", action="store_true", help="audit ngay sau khi đổi")
    sp.add_argument("--quick", action="store_true", help="matrix: mỗi loại một bước")
    sp.set_defaults(fn=cmd_env)

    sp = sub.add_parser("csv", help="oracle: đọc CSV cấu hình theo tên (qa.conventions.csvConfigGlob): csv CoinPack --where id=2 --col price")
    sp.add_argument("name", nargs="?")
    sp.add_argument("--where", action="append", help="col=val | col~regex | col>=3 (lặp được)")
    sp.add_argument("--col", action="append", help="chỉ in cột này (lặp được)")
    sp.add_argument("--limit", type=int, default=40)
    sp.add_argument("--json", action="store_true")
    sp.add_argument("--list", action="store_true", help="liệt kê file CSV")
    sp.set_defaults(fn=cmd_csv)

    sp = sub.add_parser("pseudoloc", help="(probe ≥1.3) pseudo-localization: on | off [--mode expand|accent|both]")
    sp.add_argument("mode_onoff", choices=("on", "off"))
    sp.add_argument("--mode", choices=("expand", "accent", "both"), default="both")
    sp.add_argument("--wait", type=float, default=1.0)
    sp.set_defaults(fn=cmd_pseudoloc)

    sp = sub.add_parser("track", help="event tracking trong log (qa.conventions.trackingLogPattern): track [--since MARK] [--expect REGEX]")
    sp.add_argument("--since", help="tên mark (hoặc `last`)")
    sp.add_argument("--expect", help="regex event phải có; thiếu → rc 1")
    sp.add_argument("--pattern", help="regex lọc thay cho trackingLogPattern")
    sp.add_argument("--tail", type=int, default=40)
    sp.set_defaults(fn=cmd_track)

    sp = sub.add_parser("autotest-import", help="đọc report.json mới nhất của com.ezg.autotest → autotest-summary.md + danh sách màn đã/không mở được (Δ-skip)")
    sp.add_argument("path", nargs="?")
    sp.add_argument("--quiet", action="store_true")
    sp.add_argument("--json", action="store_true")
    sp.set_defaults(fn=cmd_autotest_import)

    sp = sub.add_parser("build", help="build update <apk|app|latest>: cài đè giữ data rồi so state (save migration)")
    sp.add_argument("op", choices=("update",))
    sp.add_argument("path", nargs="?", default="latest")
    sp.add_argument("--keep-data", action="store_true", help="(mặc định) giữ dữ liệu game")
    sp.add_argument("--wipe", action="store_true", help="xoá dữ liệu sau khi cài (người chơi mới trên bản mới)")
    sp.add_argument("--state", action="append", help="thêm đường dẫn state để so (ngoài qa.verified.statePaths)")
    sp.add_argument("--settle", type=float, default=6.0)
    sp.set_defaults(fn=cmd_build_update)
    sp = sub.add_parser("devices", help="liệt kê thiết bị/AVD/Simulator")
    sp.add_argument("--json", action="store_true")
    sp.set_defaults(fn=cmd_devices)
    sp = sub.add_parser("bughub-check", help="kiểm BugHub đã setup cho project")
    sp.add_argument("--code")
    sp.add_argument("--json", action="store_true")
    sp.set_defaults(fn=cmd_bughub_check)

    sp = sub.add_parser("start", help="mở phiên test: chọn máy, mở game, bắt log, nối probe, chụp ảnh đầu")
    sp.add_argument("device", nargs="?", default="auto", help="serial adb / tên AVD / UDID hoặc tên Simulator / id iPhone / auto")
    sp.add_argument("--feature", default="", help="tính năng đang test")
    sp.add_argument("--package", help="bundle id (mặc định đọc ProjectSettings)")
    sp.add_argument("--install", help="cài APK/.app/.ipa trước khi test")
    sp.add_argument("--no-install", action="store_true", help="không tự cài bản build tìm thấy trong repo")
    sp.add_argument("--fresh", action="store_true", help="xoá dữ liệu game trước (người chơi mới — MẤT tiến trình trên máy)")
    sp.add_argument("--attach", action="store_true", help="không khởi động lại game đang chạy")
    sp.add_argument("--probe", choices=("auto", "off", "require"), default="auto")
    sp.add_argument("--probe-wait", type=float, default=25)
    sp.add_argument("--port", type=int, default=PROBE_PORT)
    sp.add_argument("--settle", type=float, default=3.0, help="chờ sau khi mở game rồi mới chụp (s)")
    sp.add_argument("--window", action="store_true", help="boot AVD có cửa sổ (mặc định headless)")
    sp.add_argument("--code", help="mã BugHub (mặc định từ project-profile)")
    sp.add_argument("--no-probe-install", action="store_true", help="không tự cài/cập nhật probe vào project")
    sp.add_argument("--no-api-retry", action="store_true",
                    help="không bật thử lại lỗi API (mặc định: /auto-clear retry-arm --max 0 — lượt chết vì lỗi API tự gõ tiếp sau 30 s)")
    sp.add_argument("--no-profile-init", action="store_true", help="không tự `profile init` khi project chưa có block qa")
    sp.add_argument("--skip-bughub", action="store_true", help=argparse.SUPPRESS)
    sp.set_defaults(fn=cmd_start)

    sp = sub.add_parser("retry", help="chạy lại một lệnh qadevice mỗi 30 s tới khi qua: retry [--every S] [--max-wait S] -- <lệnh…>")
    sp.add_argument("--every", type=float, default=RETRY_EVERY_S, help="giây giữa hai lần thử")
    sp.add_argument("--max-wait", type=float, default=RETRY_MAX_WAIT_S,
                    help="thử tối đa chừng này giây mỗi lần gọi (hết giờ: thoát %d, gọi lại)" % RETRY_PENDING_RC)
    sp.add_argument("argv", nargs=argparse.REMAINDER, help="lệnh qadevice cần chạy lại, sau `--`")
    sp.set_defaults(fn=cmd_retry)

    sp = sub.add_parser("api-retry", help="thử lại lỗi API của phiên (hook StopFailure /auto-clear): on | off | status")
    sp.add_argument("action", choices=("on", "off", "status"), nargs="?", default="status")
    sp.set_defaults(fn=cmd_api_retry)

    sp = sub.add_parser("probe-connect", help="nối lại probe (sau khi bật cheat/cổng, vào Play mode Editor, app restart)")
    sp.add_argument("--wait", type=float, default=30)
    sp.add_argument("--retrigger", action="store_true", help="ghi lại file trigger + đưa app nền/mở lại để probe đọc")
    sp.add_argument("--unlock", action="store_true", help="hỏi lại CheatGate (vừa thêm máy bằng test_device_add) và chờ cổng mở")
    sp.add_argument("--unlock-wait", type=float, default=180, help="chờ server nhận máy mới tối đa N giây")
    sp.set_defaults(fn=cmd_probe_connect)

    sp = sub.add_parser("cheat", help="(probe) bật/tắt chế độ cheat của game: on | off | status")
    sp.add_argument("mode", choices=("on", "off", "status"), nargs="?", default="status")
    sp.set_defaults(fn=cmd_cheat)

    sp = sub.add_parser("shot", help="chụp màn hình + so khung + kiểm log")
    sp.add_argument("--grid", action="store_true")
    sp.add_argument("--marks", action="store_true")
    sp.add_argument("--label")
    sp.add_argument("--no-logs", action="store_true")
    sp.set_defaults(fn=cmd_shot)

    sp = sub.add_parser("tap", help="chạm: X Y (ảnh xem) | 50%% 80%% | @id | --text | --name | --world")
    sp.add_argument("target", nargs="*")
    sp.add_argument("--text")
    sp.add_argument("--name")
    sp.add_argument("--world")
    sp.add_argument("--times", type=int, default=1, help="chạm liên tiếp N lần (test spam/double tap)")
    sp.add_argument("--interval", type=float, default=0.08, help="giây giữa các lần chạm (tối thiểu 0.07 trên Android)")
    sp.add_argument("--expect", help="(probe) kỳ vọng sau chạm: text:<chữ> | @id | screen:<tên màn> | name:<tên> (mặc định text) — không đạt → ghi expectation-failed")
    sp.add_argument("--timeout", type=float, default=EXPECT_TIMEOUT_S, help="chờ kỳ vọng tối đa (s)")
    act(sp)
    sp.set_defaults(fn=cmd_tap)

    sp = sub.add_parser("longpress", help="giữ tay")
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
        sp.add_argument("--dist", type=float, help="%% màn hình cho --dir (mặc định 40)")
        sp.add_argument("--ms", type=int, default=350 if name == "swipe" else 900)
        if name == "drag":
            sp.add_argument("--hold", type=int, default=350, help="giữ trước khi kéo (ms)")
        act(sp)
        sp.set_defaults(fn=fn)

    sp = sub.add_parser("text", help="nhập chữ vào ô đang focus")
    sp.add_argument("value")
    sp.add_argument("--probe", action="store_true", help="đặt chữ qua probe (Unicode)")
    sp.add_argument("--path", help="(probe) đường dẫn ô nhập")
    sp.add_argument("--submit", action="store_true")
    act(sp, coords=False)
    sp.set_defaults(fn=cmd_text)

    sp = sub.add_parser("key", help="phím hệ thống: back home enter app_switch volume_up …")
    sp.add_argument("name")
    act(sp, coords=False)
    sp.set_defaults(fn=cmd_key)

    sp = sub.add_parser("wait", help="chờ N giây rồi chụp (--settle: chờ tới khi màn đứng yên)")
    sp.add_argument("secs", type=float)
    sp.add_argument("--settle", action="store_true")
    sp.add_argument("--threshold", type=float, default=0.01)
    sp.add_argument("--as", dest="desc")
    sp.add_argument("--no-shot", action="store_true")
    sp.add_argument("--label")
    sp.set_defaults(fn=cmd_wait)

    sp = sub.add_parser("burst", help="chụp N khung liên tiếp (soi animation/nhấp nháy)")
    sp.add_argument("count", type=int, nargs="?", default=5)
    sp.add_argument("--interval", type=float, default=0.15)
    sp.set_defaults(fn=cmd_burst)

    sp = sub.add_parser("app", help="restart|stop|launch|background|clear|info|install")
    sp.add_argument("op", choices=("restart", "stop", "launch", "background", "clear", "info", "install"))
    sp.add_argument("path", nargs="?")
    sp.add_argument("--secs", type=float, default=5)
    sp.add_argument("--wait", type=float, default=4)
    sp.add_argument("--hard", action="store_true",
                    help="restart/stop: kill thẳng như crash/OS giết app (mặc định: Home → chờ → tắt như người chơi)")
    sp.add_argument("--settle", type=float, default=STOP_SETTLE_SECONDS, help="restart/stop: giây ở Home trước khi tắt")
    sp.add_argument("--as", dest="desc")
    sp.set_defaults(fn=cmd_app)

    sp = sub.add_parser("overlays", help="(Android) liệt kê overlay của app khác đang đè lên màn (chạm adb vào đó trúng app kia)")
    sp.set_defaults(fn=cmd_overlays)

    sp = sub.add_parser("ad-close", help="(Android) tìm + bấm nút đóng/skip của quảng cáo native; báo khi bị overlay che")
    sp.add_argument("--timeout", type=float, default=30, help="chờ nút đóng xuất hiện tối đa N giây (quảng cáo đếm ngược)")
    sp.add_argument("--list", action="store_true", help="chỉ liệt kê nút ứng viên, không bấm")
    sp.add_argument("--allow-overlay", action="store_true", help="bấm cả khi tâm nút nằm dưới overlay app khác")
    sp.add_argument("--wait", type=float, default=1.5, help="chờ sau khi bấm rồi mới chụp (s)")
    sp.add_argument("--no-shot", action="store_true")
    sp.add_argument("--label")
    sp.add_argument("--as", dest="desc")
    sp.set_defaults(fn=cmd_ad_close)

    sp = sub.add_parser("net", help="mạng: off|on|wifi-off|data-off")
    sp.add_argument("mode", choices=("off", "on", "wifi-off", "data-off"))
    act(sp, coords=False)
    sp.set_defaults(fn=cmd_net)

    sp = sub.add_parser("rotate", help="portrait|landscape|reverse-portrait|reverse-landscape|auto")
    sp.add_argument("mode")
    act(sp, coords=False)
    sp.set_defaults(fn=cmd_rotate)

    sp = sub.add_parser("logs", help="lỗi mới trong log (--all: cả phiên; --tail N; --grep REGEX; --ignore REGEX)")
    sp.add_argument("--all", action="store_true")
    sp.add_argument("--tail", type=int)
    sp.add_argument("--grep")
    sp.add_argument("--raw", action="store_true", help="--tail/--grep không lọc theo app")
    sp.add_argument("--ignore", help="bỏ qua lỗi khớp regex trong phiên này (noise SDK đã xác nhận)")
    sp.set_defaults(fn=cmd_logs)

    sp = sub.add_parser("perf", help="RAM/CPU/nhiệt/FPS (FPS cần probe)")
    sp.set_defaults(fn=cmd_perf)

    sp = sub.add_parser("ui", help="(probe) cây UI kèm toạ độ, trạng thái, cờ lỗi")
    sp.add_argument("--all", action="store_true", help="kể cả text/ảnh không tương tác")
    sp.add_argument("--find")
    sp.add_argument("--marks", action="store_true", help="vẽ khung + số lên ảnh")
    sp.add_argument("--json", action="store_true")
    sp.set_defaults(fn=cmd_ui)

    sp = sub.add_parser("audit", help="chụp + audit visual màn hiện tại (sâu nếu có probe; áp luật nhiễu qa.noise)")
    sp.add_argument("--all", action="store_true")
    sp.add_argument("--include-suppressed", action="store_true", help="in cả finding bị luật nhiễu của project ẩn")
    sp.set_defaults(fn=cmd_audit)

    sp = sub.add_parser("state", help="(probe) đọc giá trị static: Namespace.Type.Member.Member…")
    sp.add_argument("paths", nargs="+")
    sp.add_argument("--full", action="store_true", help="in trọn giá trị (mặc định cắt ở %d ký tự)" % VALUE_PRINT_MAX)
    sp.add_argument("--out", help="ghi giá trị (JSON) ra file thay vì in")
    sp.set_defaults(fn=cmd_state)

    sp = sub.add_parser("call", help="(probe) gọi hàm static/instance: Type.Method arg1 arg2 (setup/cheat)")
    sp.add_argument("method")
    sp.add_argument("args", nargs="*")
    sp.add_argument("--as", dest="desc")
    sp.add_argument("--no-shot", action="store_true")
    sp.add_argument("--wait", type=float, default=0.8)
    sp.add_argument("--full", action="store_true", help="in trọn kết quả (mặc định cắt ở %d ký tự)" % VALUE_PRINT_MAX)
    sp.add_argument("--out", help="ghi kết quả (JSON) ra file thay vì in — cho danh sách dài")
    sp.set_defaults(fn=cmd_call)

    sp = sub.add_parser("types", help="(probe) tìm type + member static theo từ khoá (vd Cheat, PlayerData)")
    sp.add_argument("query")
    sp.set_defaults(fn=cmd_types)

    sp = sub.add_parser("timescale", help="(probe) đổi Time.timeScale để tua thời gian chờ")
    sp.add_argument("value", type=float)
    sp.set_defaults(fn=cmd_timescale)

    sp = sub.add_parser("mark", help="bắt đầu ca test mới (bước tái hiện bug tính từ đây)")
    sp.add_argument("name", nargs="?")
    sp.set_defaults(fn=cmd_mark)

    sp = sub.add_parser("note", help="ghi chú / kết quả ca test; --case <charter>/<P0-01> --result … ghi ca theo charter (coverage đọc)")
    sp.add_argument("text", nargs="?")
    sp.add_argument("--case", help="mã ca trong charter, vd offline-earning/P0-01")
    sp.add_argument("--result", choices=("pass", "fail", "blocked", "skip"))
    sp.add_argument("--bug", type=int, help="số bug BugHub liên quan (ca fail)")
    sp.set_defaults(fn=cmd_note)

    sp = sub.add_parser("steps", help="xem bước tái hiện từ mark gần nhất")
    sp.add_argument("--all", action="store_true")
    sp.set_defaults(fn=cmd_steps)

    sp = sub.add_parser("bug", help="log bug lên BugHub (contract Bug Logger)")
    sp.add_argument("--title", required=True)
    sp.add_argument("--actual", required=True)
    sp.add_argument("--expected", default="")
    sp.add_argument("--steps", default="auto", help="'auto' = sinh từ các bước từ mark gần nhất")
    sp.add_argument("--precondition", help="điều kiện trước (level, tiền, trạng thái…)")
    sp.add_argument("--type", choices=BUG_TYPES, default="logic")
    sp.add_argument("--severity", choices=SEVERITIES, default="medium")
    sp.add_argument("--rate", default="1/1", help="tỉ lệ tái hiện, vd 3/3")
    sp.add_argument("--feature", help="featureKey (mặc định: màn hiện tại/tính năng phiên)")
    sp.add_argument("--category")
    sp.add_argument("--tag", action="append", help="tên forum tag thêm")
    sp.add_argument("--mention", action="append", help="tên người cần tag")
    sp.add_argument("--description")
    sp.add_argument("--evidence", help="mô tả bằng chứng (ảnh số mấy, giá trị đo…)")
    sp.add_argument("--shot", default="last", help="last | none | đường dẫn ảnh")
    sp.add_argument("--log-sig", help="chữ ký lỗi log liên quan (từ `logs`)")
    sp.add_argument("--save", action="store_true", help="(probe) đính kèm save snapshot của game")
    sp.add_argument("--extra", action="append", help="k=v thêm vào extra")
    sp.add_argument("--via", choices=("http", "ingame"), default="http")
    sp.add_argument("--code")
    sp.add_argument("--endpoint")
    sp.add_argument("--force", action="store_true", help="bỏ qua kiểm trùng trong máy (chữ ký log / tiêu đề / màn+loại)")
    sp.add_argument("--dry-run", action="store_true")
    sp.set_defaults(fn=cmd_bug)

    sp = sub.add_parser("bug-resend", help="gửi lại report đã lưu")
    sp.add_argument("report_id")
    sp.set_defaults(fn=cmd_bug_resend)

    sp = sub.add_parser("report", help="viết report.md của phiên")
    sp.add_argument("--print", action="store_true")
    sp.set_defaults(fn=cmd_report)

    sp = sub.add_parser("stop", help="kết thúc phiên: trả mạng/xoay, tắt log, xoá trigger probe, viết report")
    sp.set_defaults(fn=cmd_stop)

    sp = sub.add_parser("probe-install", help="thêm/nâng package com.ezg.qa-probe trong manifest, gỡ bản chép cũ")
    sp.add_argument("--gate", help="ghi đè cổng release: none | Namespace.Type.member (bool static)")
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
        print("LỖI: %s" % e)
        return 1
    except KeyboardInterrupt:
        return 130


if __name__ == "__main__":
    sys.exit(main())
