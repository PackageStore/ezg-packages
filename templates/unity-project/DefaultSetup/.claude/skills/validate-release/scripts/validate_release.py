#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
validate-release — checklist release readiness cho Unity mobile project, hoàn toàn deterministic.

Chỉ đọc file trên đĩa (YAML/JSON/XML/CSV/C#), KHÔNG cần Unity Editor, KHÔNG cần MCP, KHÔNG cần
package ngoài stdlib. Chạy được offline, trong CI, hoặc trong pre-push hook.

Output là MỘT CHECKLIST: mỗi mục một dòng, có tick cả mục đạt lẫn mục hỏng — để nhìn phát biết
còn thiếu gì, thay vì phải đọc một bức tường chữ.

    python3 validate_release.py                 # checklist đầy đủ
    python3 validate_release.py --fails-only    # chỉ mục chưa đạt
    python3 validate_release.py -v              # kèm cách sửa
    python3 validate_release.py --format json   # cho CI
    python3 validate_release.py --only ads,build

Exit code: 0 = không mục hỏng · 1 = có mục hỏng (hoặc có cảnh báo khi --strict) · 2 = lỗi chạy script.

Tri thức per-project nằm ở config, không viết cứng trong code:
  - default:  <skill>/config/default-rules.json
  - override: <project>/.claude/validate-release.json   (tuỳ chọn, deep-merge lên default)
"""

from __future__ import annotations

import argparse
import fnmatch
import json
import os
import plistlib
import re
import subprocess
import sys
from pathlib import Path

# Trạng thái một mục checklist. "skip" = không áp dụng cho project này (không tính là hỏng).
STATUSES = ("ok", "blocker", "warn", "info", "skip")
FAIL_STATUSES = ("blocker", "warn")

ICONS_UNICODE = {"ok": "✓", "blocker": "✗", "warn": "!", "info": "·", "skip": "–"}
ICONS_ASCII = {"ok": "+", "blocker": "X", "warn": "!", "info": ".", "skip": "-"}

SKILL_DIR = Path(__file__).resolve().parent.parent
DEFAULT_CONFIG = SKILL_DIR / "config" / "default-rules.json"

LABEL_WIDTH = 38


# --------------------------------------------------------------------------------------
# Unity YAML — reader tối giản, đủ cho các file *.asset của ProjectSettings và SDK
# --------------------------------------------------------------------------------------
def _indent_of(line: str) -> int:
    return len(line) - len(line.lstrip(" "))


def yaml_block(text: str, key: str) -> str:
    """Trả về phần thân (các dòng thụt sâu hơn) nằm dưới `key:`."""
    lines = text.splitlines()
    for i, line in enumerate(lines):
        if line.strip() == f"{key}:":
            base = _indent_of(line)
            out = []
            for nxt in lines[i + 1:]:
                if not nxt.strip():
                    continue
                if _indent_of(nxt) <= base:
                    break
                out.append(nxt)
            return "\n".join(out)
    return ""


def yaml_scalar(text: str, key: str):
    """Giá trị vô hướng đầu tiên của `key:`. Trả None nếu không có hoặc rỗng."""
    m = re.search(rf"^[ \t]*{re.escape(key)}:[ \t]*(.*)$", text, re.M)
    if not m:
        return None
    return m.group(1).strip() or None


def yaml_sub(text: str, parent: str, key: str):
    blk = yaml_block(text, parent)
    return yaml_scalar(blk, key) if blk else None


def yaml_list(text: str, key: str) -> list:
    """Unity ghi phần tử sequence THỤT NGANG BẰNG key (`  appIds:` rồi `  - 2453…`), nên không
    dùng lại yaml_block được — nó cắt theo 'thụt sâu hơn' và sẽ trả về rỗng."""
    lines = text.splitlines()
    for i, line in enumerate(lines):
        if line.strip() != f"{key}:":
            continue
        base = _indent_of(line)
        out = []
        for nxt in lines[i + 1:]:
            if not nxt.strip():
                continue
            stripped, indent = nxt.strip(), _indent_of(nxt)
            if stripped.startswith("- ") and indent >= base:
                out.append(stripped[2:].strip())
            elif indent <= base:
                break
        return out
    return []


def unquote(val):
    if val is None:
        return None
    val = val.strip()
    if len(val) >= 2 and val[0] == val[-1] and val[0] in "'\"":
        val = val[1:-1]
    return val or None


def csharp_class_body(text: str, class_name: str) -> str:
    """Cắt thân của `class <name>` trong file C# bằng cách đếm ngoặc nhọn."""
    m = re.search(rf"\bclass\s+{re.escape(class_name)}\b", text)
    if not m:
        return ""
    start = text.find("{", m.end())
    if start < 0:
        return ""
    depth = 0
    for i in range(start, len(text)):
        if text[i] == "{":
            depth += 1
        elif text[i] == "}":
            depth -= 1
            if depth == 0:
                return text[start:i]
    return text[start:]


def short(value, limit: int = 58) -> str:
    value = str(value).replace("\n", " ").strip()
    return value if len(value) <= limit else value[: limit - 1] + "…"


# --------------------------------------------------------------------------------------
# Config
# --------------------------------------------------------------------------------------
def deep_merge(base: dict, over: dict) -> dict:
    out = dict(base)
    for k, v in over.items():
        out[k] = deep_merge(out[k], v) if isinstance(v, dict) and isinstance(out.get(k), dict) else v
    return out


def load_config(root: Path, explicit: Path | None) -> dict:
    cfg = json.loads(DEFAULT_CONFIG.read_text(encoding="utf-8"))
    candidate = explicit or (root / ".claude" / "validate-release.json")
    if candidate.is_file():
        cfg = deep_merge(cfg, json.loads(candidate.read_text(encoding="utf-8")))
    return cfg


def detect_root(explicit: str | None) -> Path:
    if explicit:
        return Path(explicit).resolve()
    for seed in (Path.cwd().resolve(), Path(__file__).resolve()):
        for p in [seed, *seed.parents]:
            if (p / "Assets").is_dir() and (p / "ProjectSettings").is_dir():
                return p
    return Path.cwd().resolve()


def detect_source_root(root: Path, cfg: dict) -> Path:
    profile = root / ".claude" / "project-profile.json"
    if profile.is_file():
        try:
            hint = json.loads(profile.read_text(encoding="utf-8")).get("sourceRoot")
            if hint and (root / hint).is_dir():
                return root / hint
        except (ValueError, OSError):
            pass
    for cand in cfg.get("sourceRootCandidates", []):
        if (root / cand).is_dir():
            return root / cand
    return root / "Assets"


# --------------------------------------------------------------------------------------
# Context
# --------------------------------------------------------------------------------------
class Ctx:
    def __init__(self, root: Path, cfg: dict):
        self.root = root
        self.cfg = cfg
        self.source_root = detect_source_root(root, cfg)
        self.items: list[dict] = []
        self._text: dict[Path, str] = {}
        self._git: set[str] | None = None
        self.scan: dict = {}
        self.platforms: set[str] = set()

    # -- io -----------------------------------------------------------------
    def read(self, rel) -> str | None:
        path = rel if isinstance(rel, Path) else self.root / rel
        if path not in self._text:
            try:
                self._text[path] = path.read_text(encoding="utf-8", errors="replace")
            except OSError:
                self._text[path] = None
        return self._text[path]

    def rel(self, path: Path) -> str:
        try:
            return str(path.relative_to(self.root))
        except ValueError:
            return str(path)

    def is_excluded(self, path: Path) -> bool:
        """Thư mục rác. `excludeTopLevel` chỉ tính ở tầng ngay dưới root — nếu tính ở mọi tầng thì
        một tên phổ biến như 'Build' sẽ giết luôn Assets/_Project/Core/Build/Editor/AutoBuild.cs."""
        scan = self.cfg.get("scan", {})
        try:
            parts = path.relative_to(self.root).parts
        except ValueError:
            return False
        if parts and parts[0] in set(scan.get("excludeTopLevel", [])):
            return True
        return bool(set(parts) & set(scan.get("excludeAnywhere", [])))

    def glob(self, patterns, base: Path | None = None) -> list[Path]:
        base = base or self.root
        hits: list[Path] = []
        for pat in (patterns if isinstance(patterns, (list, tuple)) else [patterns]):
            hits.extend(p for p in base.glob(pat) if not self.is_excluded(p))
        return sorted(set(hits))

    def git_files(self) -> set[str]:
        if self._git is None:
            try:
                out = subprocess.run(["git", "-C", str(self.root), "ls-files"],
                                     capture_output=True, text=True, timeout=30, check=False)
                self._git = set(out.stdout.splitlines()) if out.returncode == 0 else set()
            except (OSError, subprocess.SubprocessError):
                self._git = set()
        return self._git

    # -- checklist ----------------------------------------------------------
    def item(self, check_id, label, status, note="", file=None, line=None, fix=None):
        """Ghi MỘT dòng checklist. Mọi check phải gọi hàm này cả khi đạt — không có dòng 'ok'
        thì user không biết thứ đó đã được kiểm hay bị bỏ qua."""
        assert status in STATUSES, status
        if any(fnmatch.fnmatch(check_id, pat) for pat in self.cfg.get("ignore", [])):
            return
        if status != "ok":
            status = self.cfg.get("severity", {}).get(check_id, status)
            if status == "off":
                return
        self.items.append({
            "id": check_id, "label": label, "status": status,
            "note": note, "file": file, "line": line, "fix": fix,
        })

    def ok(self, check_id, label, note="", file=None):
        self.item(check_id, label, "ok", note, file=file)

    def skip(self, check_id, label, note=""):
        self.item(check_id, label, "skip", note)


# --------------------------------------------------------------------------------------
# Một lần duyệt source — mọi check dựa trên code đều ăn kết quả ở đây
# --------------------------------------------------------------------------------------
def scan_sources(ctx: Ctx) -> None:
    cfg = ctx.cfg
    skip_editor = cfg.get("scan", {}).get("excludeEditorCode", True)

    re_webhook = re.compile(cfg["secrets"]["webhookPattern"])
    re_flag = re.compile(cfg["debugResidue"]["flagPattern"])
    re_remote_lit = re.compile(cfg["remote"]["literalPattern"])
    re_remote_sym = re.compile(cfg["remote"]["symbolPattern"])
    re_active_ads = re.compile(cfg["ads"]["activeImplPattern"])
    re_bundle_lit = re.compile(r'"(com\.[a-z0-9_]+(?:\.[a-z0-9_]+){2,})"')
    re_play_link = re.compile(r'play\.google\.com/store/apps/details\?id=([A-Za-z0-9_.]+)')
    re_appstore_link = re.compile(r'apps\.apple\.com/(?:[a-z]{2}/)?app/(?:[^/]+/)?id(\d+)')
    re_const_str = re.compile(r'\b(\w+)\s*=\s*"([^"]*)"\s*;')

    markers = cfg["remote"]["scopeMarkers"]
    out = {
        "webhooks": [], "debug_flags": [], "remote_keys": set(), "bundle_literals": {},
        "play_links": {}, "appstore_links": {}, "consts": {}, "active_ads_impl": None,
        "uses_firebase": False, "cs_count": 0,
    }

    def at(text, pos):
        return text[:pos].count("\n") + 1

    for path in sorted(ctx.source_root.rglob("*.cs")):
        if ctx.is_excluded(path) or (skip_editor and "Editor" in path.parts):
            continue
        text = ctx.read(path)
        if text is None:
            continue
        out["cs_count"] += 1
        relp = ctx.rel(path)

        if "using Firebase" in text:
            out["uses_firebase"] = True
        for m in re_webhook.finditer(text):
            out["webhooks"].append((relp, at(text, m.start()), m.group(0)[:60]))
        for m in re_flag.finditer(text):
            out["debug_flags"].append((relp, at(text, m.start()), m.group(1)))
        if any(mk in text for mk in markers):
            out["remote_keys"].update(re_remote_lit.findall(text))
            out["remote_keys"].update(re_remote_sym.findall(text))
        for m in re_bundle_lit.finditer(text):
            out["bundle_literals"].setdefault(m.group(1), []).append((relp, at(text, m.start())))
        for m in re_play_link.finditer(text):
            out["play_links"].setdefault(m.group(1), []).append((relp, at(text, m.start())))
        for m in re_appstore_link.finditer(text):
            out["appstore_links"].setdefault(m.group(1), []).append((relp, at(text, m.start())))
        if out["active_ads_impl"] is None:
            m = re_active_ads.search(text)
            if m:
                out["active_ads_impl"] = m.group(1)
        for m in re_const_str.finditer(text):
            out["consts"].setdefault(m.group(1), (m.group(2), relp, at(text, m.start())))

    ctx.scan = out


# --------------------------------------------------------------------------------------
# Helpers dùng chung cho check
# --------------------------------------------------------------------------------------
PS_FILE = "ProjectSettings/ProjectSettings.asset"


def _ps(ctx: Ctx) -> str:
    return ctx.read(PS_FILE) or ""


def _bundle_ids(ctx: Ctx) -> dict:
    text = _ps(ctx)
    return {"Android": yaml_sub(text, "applicationIdentifier", "Android"),
            "iOS": yaml_sub(text, "applicationIdentifier", "iPhone")}


def detect_platforms(ctx: Ctx) -> set[str]:
    configured = ctx.cfg.get("platforms", "auto")
    if isinstance(configured, list):
        return set(configured)
    platforms = set()
    for script in ctx.glob(ctx.cfg["build"]["scriptGlobs"]):
        text = ctx.read(script) or ""
        if "BuildTarget.Android" in text:
            platforms.add("Android")
        if "BuildTarget.iOS" in text:
            platforms.add("iOS")
    if not platforms:
        if (ctx.root / "Assets" / "Plugins" / "Android").is_dir():
            platforms.add("Android")
        if (ctx.root / "Assets" / "Plugins" / "iOS").is_dir():
            platforms.add("iOS")
    return platforms or {"Android"}


# --------------------------------------------------------------------------------------
# Checks — mỗi hàm phát ra checklist item, PASS lẫn FAIL
# --------------------------------------------------------------------------------------
def check_identity(ctx: Ctx) -> None:
    text = _ps(ctx)
    ids = _bundle_ids(ctx)
    placeholders = [p.lower() for p in ctx.cfg["identity"]["bundleIdPlaceholders"]]

    for platform in sorted(ctx.platforms):
        label = f"Bundle id {platform}"
        value = ids.get(platform)
        if not value:
            ctx.item("identity.bundle-id", label, "blocker",
                     "chưa có entry trong applicationIdentifier", file=PS_FILE,
                     fix="Player Settings > Other Settings > Identification > Override Default Bundle Identifier.")
        elif value.lower() in placeholders:
            ctx.item("identity.bundle-id", label, "blocker", f"còn là placeholder: {value}", file=PS_FILE)
        else:
            ctx.ok("identity.bundle-id", label, value)

    product = yaml_scalar(text, "productName")
    if product in ctx.cfg["identity"]["productNamePlaceholders"]:
        ctx.item("identity.product-name", "Product name", "warn", f"còn là placeholder: {product}", file=PS_FILE)
    else:
        ctx.ok("identity.product-name", "Product name", product or "")

    label = "Keystore Android"
    if "Android" not in ctx.platforms:
        ctx.skip("identity.keystore", label, "Android không trong scope")
    elif yaml_scalar(text, "androidUseCustomKeystore") != "1":
        ctx.item("identity.keystore", label, "warn", "chưa bật custom keystore", file=PS_FILE,
                 fix="Không có keystore riêng thì bản release ký bằng debug key, Play Console từ chối.")
    else:
        raw = unquote(yaml_scalar(text, "AndroidKeystoreName")) or ""
        resolved = raw.split(":", 1)[1].strip() if raw.startswith("{inproject}:") else raw
        path = (ctx.root / resolved) if resolved and not os.path.isabs(resolved) else Path(resolved or "")
        if not resolved:
            ctx.item("identity.keystore", label, "blocker", "bật custom keystore nhưng không khai đường dẫn", file=PS_FILE)
        elif not path.is_file():
            ctx.item("identity.keystore", label, "blocker", f"không tìm thấy file: {raw}", file=PS_FILE)
        else:
            ctx.ok("identity.keystore", label, resolved)

    label = "Đường dẫn máy khác trong settings"
    settings_files = ctx.glob(["ProjectSettings/*.asset", "ProjectSettings/*.json",
                               "Assets/**/FacebookSettings.asset", "Assets/**/AppLovinSettings.asset"])
    re_foreign = re.compile(r"[A-Za-z]:\\\\?[\w\\ .-]+")
    foreign = None
    for path in settings_files:
        content = ctx.read(path) or ""
        m = re_foreign.search(content)
        if m:
            foreign = (ctx.rel(path), content[: m.start()].count("\n") + 1, m.group(0))
            break
    if foreign:
        ctx.item("identity.path-foreign", label, "warn", short(foreign[2]),
                 file=foreign[0], line=foreign[1],
                 fix="Trỏ lại đường dẫn tương đối — máy/CI khác không có path này.")
    else:
        ctx.ok("identity.path-foreign", label)

    label = "iOS signing"
    if "iOS" not in ctx.platforms:
        ctx.skip("identity.ios-signing", label, "iOS không trong scope")
    elif yaml_scalar(text, "appleDeveloperTeamID") or yaml_scalar(text, "appleEnableAutomaticSigning") == "1":
        ctx.ok("identity.ios-signing", label)
    else:
        ctx.item("identity.ios-signing", label, "warn", "không có Team ID và cũng không bật automatic signing", file=PS_FILE)

    android_code = yaml_scalar(text, "AndroidBundleVersionCode")
    ios_code = yaml_sub(text, "buildNumber", "iPhone")
    version = yaml_scalar(text, "bundleVersion")
    if len(ctx.platforms) > 1 and android_code and ios_code and android_code != ios_code:
        ctx.item("identity.version-code", "Version code", "info",
                 f"v{version} · Android={android_code}, iOS={ios_code}", file=PS_FILE)
    else:
        ctx.ok("identity.version-code", "Version code", f"v{version} · {android_code or ios_code}")


def check_firebase(ctx: Ctx) -> None:
    if not ctx.scan.get("uses_firebase"):
        ctx.skip("firebase.config", "Firebase config", "source không dùng Firebase")
        return
    ids = _bundle_ids(ctx)

    if "Android" in ctx.platforms:
        found = [p for p in ctx.glob(["Assets/**/google-services.json"]) if "desktop" not in p.name]
        if not found:
            ctx.item("firebase.android-config", "google-services.json", "blocker", "không tìm thấy",
                     fix="Tải từ Firebase Console > Project settings > Android app.")
        else:
            ctx.ok("firebase.android-config", "google-services.json", ctx.rel(found[0]))
            try:
                data = json.loads(ctx.read(found[0]) or "{}")
                packages = [c.get("client_info", {}).get("android_client_info", {}).get("package_name")
                            for c in data.get("client", [])]
                packages = [p for p in packages if p]
                if ids["Android"] and packages and ids["Android"] not in packages:
                    ctx.item("firebase.android-package-match", "google-services.json khớp bundle id", "blocker",
                             f"file = {', '.join(packages)}", file=ctx.rel(found[0]),
                             fix="Lệch thì Firebase không init → mất Analytics/Crashlytics/Remote Config.")
                else:
                    ctx.ok("firebase.android-package-match", "google-services.json khớp bundle id")
            except ValueError:
                ctx.item("firebase.android-config", "google-services.json", "blocker",
                         "file không parse được", file=ctx.rel(found[0]))

    if "iOS" in ctx.platforms:
        found = ctx.glob(["Assets/**/GoogleService-Info.plist"])
        if not found:
            ctx.item("firebase.ios-config", "GoogleService-Info.plist", "blocker", "không tìm thấy",
                     fix="Tải từ Firebase Console > Project settings > iOS app, bỏ vào Assets/.")
        else:
            ctx.ok("firebase.ios-config", "GoogleService-Info.plist", ctx.rel(found[0]))
            try:
                with open(found[0], "rb") as fh:
                    bundle = plistlib.load(fh).get("BUNDLE_ID")
                if ids["iOS"] and bundle and bundle != ids["iOS"]:
                    ctx.item("firebase.ios-bundle-match", "GoogleService-Info.plist khớp bundle id", "blocker",
                             f"plist = {bundle}", file=ctx.rel(found[0]))
                else:
                    ctx.ok("firebase.ios-bundle-match", "GoogleService-Info.plist khớp bundle id")
            except (OSError, ValueError):
                pass


def check_ads(ctx: Ctx) -> None:
    cfg = ctx.cfg["ads"]
    applovin = ctx.glob(["Assets/**/AppLovinSettings.asset"])
    gma = ctx.glob(["Assets/**/GoogleMobileAdsSettings.asset"])

    max_ids: dict = {}
    if applovin:
        text = ctx.read(applovin[0]) or ""
        key = yaml_scalar(text, "sdkKey")
        if key:
            ctx.ok("ads.applovin-key", "AppLovin SDK key", short(key, 20) + "…")
        else:
            ctx.item("ads.applovin-key", "AppLovin SDK key", "blocker", "rỗng", file=ctx.rel(applovin[0]))
        max_ids = {"Android": yaml_scalar(text, "adMobAndroidAppId"),
                   "iOS": yaml_scalar(text, "adMobIosAppId") or yaml_scalar(text, "adMobIOSAppId")}
    else:
        ctx.skip("ads.applovin-key", "AppLovin SDK key", "không có AppLovinSettings")

    gma_ids: dict = {}
    if gma:
        text = ctx.read(gma[0]) or ""
        gma_ids = {"Android": yaml_scalar(text, "adMobAndroidAppId"),
                   "iOS": yaml_scalar(text, "adMobIOSAppId") or yaml_scalar(text, "adMobIosAppId")}
        for platform in sorted(ctx.platforms):
            label = f"AdMob App ID {platform}"
            value = gma_ids.get(platform)
            if not value:
                ctx.item("ads.admob-appid", label, "blocker", "trống",
                         file=ctx.rel(gma[0]), fix="Thiếu App ID thì app crash ngay khi init Mobile Ads SDK.")
            elif value in cfg["testAppIds"]:
                ctx.item("ads.admob-appid", label, "blocker", f"đang là ID TEST của Google: {value}", file=ctx.rel(gma[0]))
            else:
                ctx.ok("ads.admob-appid", label, value)
    else:
        ctx.skip("ads.admob-appid", "AdMob App ID", "không có GoogleMobileAdsSettings")

    label = "AdMob App ID khớp giữa 2 SDK"
    pairs = [(p, max_ids.get(p), gma_ids.get(p)) for p in sorted(ctx.platforms)]
    pairs = [(p, a, g) for p, a, g in pairs if a and g]
    if not pairs:
        ctx.skip("ads.admob-appid-match", label, "chỉ có một SDK khai App ID")
    else:
        bad = [(p, a, g) for p, a, g in pairs if a != g]
        if bad:
            p, a, g = bad[0]
            ctx.item("ads.admob-appid-match", label, "blocker",
                     f"{p}: AppLovin={a} · GoogleMobileAds={g}",
                     fix="AppLovin ghi đè App ID lúc build → giá trị sai sẽ vào manifest.")
        else:
            ctx.ok("ads.admob-appid-match", label)

    label = "Consent flow + Privacy Policy URL"
    consent_path = ctx.root / "ProjectSettings" / "AppLovinInternalSettings.json"
    if not consent_path.is_file():
        ctx.skip("ads.consent", label, "không có AppLovinInternalSettings.json")
    else:
        try:
            consent = json.loads(ctx.read(consent_path) or "{}")
            if not consent.get("consentFlowEnabled"):
                ctx.item("ads.consent", label, "warn", "consent flow đang tắt",
                         file="ProjectSettings/AppLovinInternalSettings.json",
                         fix="GDPR/UMP bắt buộc ở EU — xác nhận là chủ đích.")
            elif not consent.get("consentFlowPrivacyPolicyUrl"):
                ctx.item("ads.consent", label, "blocker", "bật consent nhưng thiếu Privacy Policy URL",
                         file="ProjectSettings/AppLovinInternalSettings.json")
            else:
                ctx.ok("ads.consent", label, short(consent["consentFlowPrivacyPolicyUrl"], 40))
        except ValueError:
            ctx.item("ads.consent", label, "warn", "file không parse được")

    impl = ctx.scan.get("active_ads_impl")
    unit_files = ctx.glob(cfg["adUnitFileGlobs"], base=ctx.source_root)
    label = "Ad unit của mạng đang chạy"
    if not impl or not unit_files:
        ctx.skip("ads.ad-unit", label, "không xác định được mediation đang chạy")
    else:
        network = re.sub(r"(Ads?vertising|AdNetwork|Ads)$", "", impl) or impl
        body = csharp_class_body(ctx.read(unit_files[0]) or "", network)
        if not body:
            ctx.skip("ads.ad-unit", label, f"không tìm thấy class {network}")
        else:
            empty = []
            for unit in cfg["criticalUnitNames"]:
                m = re.search(rf'\b{unit}\w*Id\s*=\s*"(.*?)"', body)
                if m and not m.group(1):
                    empty.append(unit)
            if empty:
                ctx.item("ads.ad-unit", label, "blocker",
                         f"{network}: rỗng ở {', '.join(empty)}", file=ctx.rel(unit_files[0]))
            else:
                ctx.ok("ads.ad-unit", label, f"{network} — đủ {', '.join(cfg['criticalUnitNames'])}")
        ctx.item("ads.active-network", "Mediation đang chạy", "info", impl, file=ctx.rel(unit_files[0]))


def check_tracking(ctx: Ctx) -> None:
    consts = ctx.scan.get("consts", {})

    for name, label, platform in (("AppsFlyerId", "AppsFlyer dev key", None),
                                  ("IOSAppId", "Apple App ID (AppsFlyer)", "iOS")):
        if platform and platform not in ctx.platforms:
            continue
        entry = consts.get(name)
        if entry is None:
            ctx.skip("tracking.appsflyer-key", label, f"không thấy hằng {name}")
            continue
        value, relp, line = entry
        if not value or value.lower() in ("todo", "changeme", "xxx"):
            ctx.item("tracking.appsflyer-key", label, "blocker", "rỗng/placeholder", file=relp, line=line)
        else:
            ctx.ok("tracking.appsflyer-key", label, value, file=relp)

    fb_settings = ctx.glob(["Assets/**/FacebookSettings.asset"])
    manifests = [p for p in ctx.glob(["Assets/Plugins/Android/**/AndroidManifest.xml"])
                 if "com.facebook.sdk.ApplicationId" in (ctx.read(p) or "")]
    labels = {"tracking.facebook-appid": "Facebook App ID khớp manifest",
              "tracking.facebook-provider": "Facebook ContentProvider authority",
              "tracking.facebook-token": "Facebook Client Token khớp manifest"}
    if not (fb_settings and manifests):
        for cid, label in labels.items():
            ctx.skip(cid, label, "không có Facebook SDK")
        return

    text = ctx.read(fb_settings[0]) or ""
    app_ids, tokens = yaml_list(text, "appIds"), yaml_list(text, "clientTokens")
    mtext, mrel = ctx.read(manifests[0]) or "", ctx.rel(manifests[0])

    checks = [
        ("tracking.facebook-appid", app_ids,
         re.search(r'android:name="com\.facebook\.sdk\.ApplicationId"\s+android:value="fb?([0-9]+)"', mtext), None),
        ("tracking.facebook-provider", app_ids,
         re.search(r'android:authorities="com\.facebook\.app\.FacebookContentProvider([0-9]+)"', mtext),
         "Authority sai làm app crash lúc khởi động trên một số máy."),
        ("tracking.facebook-token", tokens,
         re.search(r'android:name="com\.facebook\.sdk\.ClientToken"\s+android:value="([^"]+)"', mtext), None),
    ]
    for cid, values, match, fix in checks:
        label = labels[cid]
        if not values or not match:
            ctx.skip(cid, label, "không đọc được giá trị để so")
        elif match.group(1) != values[0]:
            ctx.item(cid, label, "blocker",
                     f"settings={values[0]} · manifest={match.group(1)}", file=mrel, fix=fix)
        else:
            ctx.ok(cid, label, values[0])


def check_flags(ctx: Ctx) -> None:
    flags = ctx.scan.get("debug_flags", [])
    label = "Cờ debug/sandbox đã tắt"
    if not flags:
        ctx.ok("flags.debug-const", label)
        return
    for relp, line, name in flags:
        ctx.item("flags.debug-const", f"Cờ {name}", "blocker", "đang = true",
                 file=relp, line=line, fix="Đổi về false trước khi build release.")


def check_remote_config(ctx: Ctx) -> None:
    files = ctx.glob(ctx.cfg["remote"]["defaultFileGlobs"], base=ctx.source_root)
    labels = {"remote.default-file": "File remote-config default",
              "remote.missing-default": "Remote key đều có default",
              "remote.unused-default": "Key mồ côi trong file default"}
    if not files:
        for cid, label in labels.items():
            ctx.skip(cid, label, "không có file remote-config default")
        return

    used = set(ctx.scan.get("remote_keys", set()))
    per_file: dict[Path, set[str]] = {}
    for path in files:
        try:
            data = json.loads(ctx.read(path) or "{}")
        except ValueError:
            ctx.item("remote.default-file", labels["remote.default-file"], "warn",
                     "JSON hỏng", file=ctx.rel(path))
            continue
        params = data.get("parameters", data)
        per_file[path] = set(params.keys()) if isinstance(params, dict) else set()

    # File mà KHÔNG key nào được code đọc gần như chắc chắn là export của project khác còn sót.
    stale = [p for p, keys in per_file.items() if keys and not (keys & used)]
    live = {p: k for p, k in per_file.items() if p not in stale}
    for path in stale:
        ctx.item("remote.default-file", "File default của project khác", "warn",
                 f"{len(per_file[path])} key, không key nào được đọc", file=ctx.rel(path),
                 fix="Xoá đi để khỏi nạp nhầm.")
    if not stale:
        names = ", ".join(ctx.rel(p) for p in live) if len(live) == 1 else f"{len(live)} file"
        ctx.ok("remote.default-file", labels["remote.default-file"], short(names, 46))

    defaults = set().union(*live.values()) if live else set()
    missing = sorted(used - defaults)
    if missing:
        ctx.item("remote.missing-default", labels["remote.missing-default"], "warn",
                 f"thiếu {len(missing)}: " + short(", ".join(missing), 40),
                 fix="Thiếu default thì lần chạy đầu (chưa fetch xong / offline) rơi về giá trị C#.")
    else:
        ctx.ok("remote.missing-default", labels["remote.missing-default"], f"{len(used)} key")

    # Gộp theo file: 15 key mồ côi của một file là MỘT vấn đề, không phải 15 vấn đề.
    orphan_total = 0
    for path, keys in sorted(live.items()):
        orphans = sorted(keys - used)
        orphan_total += len(orphans)
        if not orphans:
            continue
        ratio = len(orphans) / len(keys)
        ctx.item("remote.unused-default", labels["remote.unused-default"],
                 "warn" if ratio > 0.6 else "info",
                 f"{len(orphans)}/{len(keys)} key không thấy code nào đọc"
                 + (" — quá nửa, kiểm tra xem có phải file của project khác không" if ratio > 0.6 else ""),
                 file=ctx.rel(path))
    if not orphan_total:
        ctx.ok("remote.unused-default", labels["remote.unused-default"], "không có")


def _build_scenes(ctx: Ctx) -> tuple[list[str], Path | None]:
    for path in ctx.glob(ctx.cfg["build"]["scriptGlobs"], base=ctx.source_root):
        text = ctx.read(path) or ""
        scenes = re.findall(r'"([^"]+\.unity)"', text)
        if scenes:
            seen, ordered = set(), []
            for s in scenes:
                if s not in seen:
                    seen.add(s)
                    ordered.append(s)
            return ordered, path
    return [], None


def _editor_build_scenes(ctx: Ctx) -> list[str]:
    text = ctx.read("ProjectSettings/EditorBuildSettings.asset") or ""
    return [m.group(2) for m in re.finditer(r"- enabled:\s*(\d+)\s*\n\s*path:\s*(\S+)", text) if m.group(1) == "1"]


def check_build(ctx: Ctx) -> None:
    text = _ps(ctx)
    scenes, script = _build_scenes(ctx)
    script_rel = ctx.rel(script) if script else None

    labels = {"build.scene-exists": "Scene trong build list tồn tại",
              "build.development-option": "Development Build đã tắt",
              "build.editor-scene": "Scene dev không lọt vào build"}
    if not script:
        for cid, label in labels.items():
            ctx.skip(cid, label, "không tìm thấy build script")
        ctx.item("build.scene-source", "Nguồn scene list", "info", "Editor Build Settings (không có build script)")
    else:
        missing = [s for s in scenes if not (ctx.root / s).is_file()]
        if missing:
            ctx.item("build.scene-exists", labels["build.scene-exists"], "blocker",
                     "không có trên đĩa: " + short(", ".join(missing), 40), file=script_rel)
        else:
            ctx.ok("build.scene-exists", labels["build.scene-exists"], f"{len(scenes)} scene")

        code_only = re.sub(r"//.*?$|/\*.*?\*/", "", ctx.read(script) or "", flags=re.M | re.S)
        if "BuildOptions.Development" in code_only or "development = true" in code_only:
            ctx.item("build.development-option", labels["build.development-option"], "blocker",
                     "build script bật Development Build", file=script_rel)
        else:
            ctx.ok("build.development-option", labels["build.development-option"])

        markers = ctx.cfg["build"]["editorOnlySceneMarkers"]
        suspects = [Path(s).stem for s in scenes if any(mk.lower() in Path(s).stem.lower() for mk in markers)]
        if suspects:
            ctx.item("build.editor-scene", labels["build.editor-scene"], "warn",
                     "nghi là scene dev: " + ", ".join(suspects), file=script_rel)
        else:
            ctx.ok("build.editor-scene", labels["build.editor-scene"])

        # Build script là nguồn sự thật khi build bằng CI — lệch với Editor KHÔNG phải lỗi.
        editor_scenes = _editor_build_scenes(ctx)
        note = "build script (nguồn sự thật khi build CI)"
        if editor_scenes and set(editor_scenes) != set(scenes):
            note += f" — khác Editor Build Settings ({len(editor_scenes)} scene)"
        ctx.item("build.scene-source", "Nguồn scene list", "info", note, file=script_rel)

    if "Android" in ctx.platforms:
        label = "IL2CPP (Android)"
        if not ctx.cfg["android"]["requireIl2cpp"]:
            ctx.skip("build.il2cpp", label, "rule tắt trong config")
        elif yaml_sub(text, "scriptingBackend", "Android") == "1":
            ctx.ok("build.il2cpp", label)
        else:
            ctx.item("build.il2cpp", label, "blocker", "đang dùng Mono — không xuất được ARM64", file=PS_FILE)

        label = "ARM64 (Android)"
        arch = yaml_scalar(text, "AndroidTargetArchitectures")
        if not ctx.cfg["android"]["requireArm64"]:
            ctx.skip("build.arm64", label, "rule tắt trong config")
        elif arch and int(arch) & 2:
            ctx.ok("build.arm64", label)
        else:
            ctx.item("build.arm64", label, "blocker", f"AndroidTargetArchitectures = {arch}", file=PS_FILE)

        label = "Target SDK (Android)"
        target_sdk = yaml_scalar(text, "AndroidTargetSdkVersion")
        min_sdk = ctx.cfg["android"]["minTargetSdk"]
        if target_sdk == "0":
            ctx.item("build.target-sdk", label, "info", "Automatic — phụ thuộc máy build", file=PS_FILE)
        elif target_sdk and int(target_sdk) < min_sdk:
            ctx.item("build.target-sdk", label, "blocker",
                     f"{target_sdk} < mức Play yêu cầu ({min_sdk})", file=PS_FILE)
        else:
            ctx.ok("build.target-sdk", label, target_sdk or "")

    stripping = yaml_sub(text, "managedStrippingLevel", "Android") or "0"
    strip_engine = yaml_scalar(text, "stripEngineCode") or "0"
    label = "link.xml khi bật stripping"
    if stripping == "0" and strip_engine != "1":
        ctx.skip("build.link-xml", label, "không bật stripping")
    elif ctx.glob(["Assets/**/link.xml"]):
        ctx.ok("build.link-xml", label)
    else:
        ctx.item("build.link-xml", label, "blocker",
                 "không có link.xml — reflection (save module, CSV config) sẽ bị strip")
    ctx.item("build.stripping-profile", "Mức stripping", "info",
             f"managed={stripping}, engineCode={strip_engine}", file=PS_FILE)

    label = "Unity splash screen đã tắt"
    if yaml_scalar(text, "m_ShowUnitySplashScreen") == "1":
        ctx.item("build.unity-splash", label, "warn", "vẫn đang bật", file=PS_FILE)
    else:
        ctx.ok("build.unity-splash", label)


def check_debug_residue(ctx: Ctx) -> None:
    scenes = _build_scenes(ctx)[0] or _editor_build_scenes(ctx)
    markers = ctx.cfg["debugResidue"]["sceneMarkers"]
    label = "Debug console ngoài scene ship"
    hits = []
    for scene in scenes:
        path = ctx.root / scene
        if not path.is_file():
            continue
        text = ctx.read(path) or ""
        found = [m for m in markers if m in text]
        # "DebugConsole" là con của "IngameDebugConsole" — báo cả hai là báo trùng một object.
        found = [m for m in found if not any(m != o and m in o for o in found)]
        hits.extend((Path(scene).name, m, scene) for m in found)
    if not hits:
        ctx.ok("debug.console-in-scene", label)
    else:
        for scene_name, marker, scene in hits:
            ctx.item("debug.console-in-scene", label, "warn", f"{marker} trong {scene_name}",
                     file=scene, fix="Xác nhận là chủ đích (cheat build) hay quên gỡ.")


def check_secrets(ctx: Ctx) -> None:
    hooks = ctx.scan.get("webhooks", [])
    label = "Webhook hardcode trong source"
    if not hooks:
        ctx.ok("secrets.webhook", label)
    else:
        for relp, line, sample in hooks:
            ctx.item("secrets.webhook", label, "warn", short(sample, 44), file=relp, line=line,
                     fix="Ai có APK cũng đọc được — cân nhắc đẩy qua remote config.")

    label = "File credential trong git"
    tracked = ctx.git_files()
    if not tracked:
        ctx.skip("secrets.credential-tracked", label, "không đọc được git index")
        return
    hits = sorted({f for pat in ctx.cfg["secrets"]["credentialFileGlobs"]
                   for f in tracked if fnmatch.fnmatch(f, pat)})
    if not hits:
        ctx.ok("secrets.credential-tracked", label)
    else:
        ctx.item("secrets.credential-tracked", label, "warn",
                 f"{len(hits)} file: " + short(", ".join(hits), 44), file=hits[0],
                 fix="Nhiều team cố ý commit keystore vào repo nội bộ — xác nhận rồi đưa vào ignore.")


def check_localization(ctx: Ctx) -> None:
    label = "Localization đủ key"
    cfg = ctx.cfg["localization"]
    sep, ref_name = cfg["separator"], cfg["referenceLocale"]
    # Glob bắt cả thư mục CODE tên na ná (Core/Localize/Localization). Thư mục dữ liệu thật là
    # thư mục có sẵn locale gốc bên trong — lọc theo dấu hiệu đó thay vì lấy bừa cái đầu tiên.
    roots = [p for p in ctx.glob(cfg["rootGlobs"], base=ctx.source_root)
             if p.is_dir() and (p / ref_name).is_dir()]
    if not roots:
        ctx.skip("l10n.keys", label, f"không thấy thư mục localization nào có locale gốc '{ref_name}'")
        return
    root = max(roots, key=lambda p: len([d for d in p.iterdir() if d.is_dir()]))
    locales = sorted(p for p in root.iterdir() if p.is_dir())
    ref = root / ref_name

    def keys_of(path: Path) -> set[str]:
        out = set()
        for i, line in enumerate((ctx.read(path) or "").splitlines()):
            if line.strip() and not (i == 0 and line.startswith("key")):
                out.add(line.split(sep, 1)[0].strip())
        return out

    ref_files = {p.name: keys_of(p) for p in sorted(ref.glob("*.csv"))}
    missing_files, missing_keys = [], []
    for locale in locales:
        if locale == ref:
            continue
        for fname, ref_keys in ref_files.items():
            target = locale / fname
            if not target.is_file():
                missing_files.append(f"{locale.name}/{fname}")
            else:
                gap = ref_keys - keys_of(target)
                if gap:
                    missing_keys.append(f"{locale.name}/{fname} thiếu {len(gap)}")

    if missing_files:
        ctx.item("l10n.files", "Localization đủ file", "warn",
                 f"{len(missing_files)} file thiếu: " + short(", ".join(missing_files), 40))
    else:
        ctx.ok("l10n.files", "Localization đủ file", f"{len(locales)} ngôn ngữ × {len(ref_files)} file")

    if missing_keys:
        ctx.item("l10n.keys", label, "warn", short(", ".join(missing_keys), 50))
    else:
        ctx.ok("l10n.keys", label, f"gốc '{ref_name}' · {sum(len(k) for k in ref_files.values())} key")


def check_hygiene(ctx: Ctx) -> None:
    ids = _bundle_ids(ctx)
    own = {v for v in ids.values() if v}
    known = tuple(ctx.cfg["hygiene"]["knownBundlePrefixes"])

    label = "Package id lạ trong source"
    strays = {lit: hits for lit, hits in ctx.scan.get("bundle_literals", {}).items()
              if lit not in own and not lit.startswith(known)}
    if not strays:
        ctx.ok("hygiene.stale-package-ref", label)
    else:
        for literal, hits in sorted(strays.items()):
            relp, line = hits[0]
            ctx.item("hygiene.stale-package-ref", label, "warn", literal, file=relp, line=line,
                     fix="Thường là hằng số sót lại từ project trước — deep link / rate / share trỏ sai app.")

    label = "Link store trỏ đúng app"
    bad_links = []
    for pkg, hits in sorted(ctx.scan.get("play_links", {}).items()):
        if ids["Android"] and pkg != ids["Android"]:
            bad_links.append((f"Play: {pkg}", hits[0]))
    ios_app_id = (ctx.scan.get("consts", {}).get("IOSAppId") or (None,))[0]
    if ios_app_id:
        for app_id, hits in sorted(ctx.scan.get("appstore_links", {}).items()):
            if app_id != ios_app_id:
                bad_links.append((f"App Store: id{app_id}", hits[0]))
    if not bad_links:
        ctx.ok("hygiene.store-link", label)
    else:
        for note, (relp, line) in bad_links:
            ctx.item("hygiene.store-link", label, "warn", note, file=relp, line=line)

    label = "File backup trong Assets/"
    backups = [p for p in ctx.glob(ctx.cfg["hygiene"]["backupGlobs"]) if not p.name.endswith(".meta")]
    if len(backups) >= ctx.cfg["hygiene"]["backupThreshold"]:
        ctx.item("hygiene.backup-clutter", label, "info",
                 f"{len(backups)} file (vẫn bị import vào build)",
                 file=ctx.rel(backups[0]), fix="Xoá hoặc chuyển ra ngoài Assets/.")
    else:
        ctx.ok("hygiene.backup-clutter", label)


CHECKS = {
    "identity": (check_identity, "Identity & ký build"),
    "firebase": (check_firebase, "Firebase"),
    "ads": (check_ads, "Ads / mediation"),
    "tracking": (check_tracking, "Tracking & attribution"),
    "flags": (check_flags, "Cờ debug/sandbox"),
    "remote": (check_remote_config, "Remote Config"),
    "build": (check_build, "Build settings"),
    "debug": (check_debug_residue, "Debug residue"),
    "secrets": (check_secrets, "Secrets"),
    "l10n": (check_localization, "Localization"),
    "hygiene": (check_hygiene, "Hygiene"),
}


# --------------------------------------------------------------------------------------
# Render
# --------------------------------------------------------------------------------------
def render(ctx: Ctx, items: list[dict], icons: dict, verbose: bool) -> str:
    counts = {s: sum(1 for i in items if i["status"] == s) for s in STATUSES}
    head = " · ".join(f"{counts[s]} {icons[s]}" for s in STATUSES if counts[s])
    lines = [
        f"VALIDATE RELEASE · {ctx.root.name} · {', '.join(sorted(ctx.platforms))}",
        f"{head}",
        "",
    ]
    if not items:
        return "\n".join(lines + ["(không có mục nào để hiển thị)"])

    for group, (_, title) in CHECKS.items():
        group_items = [i for i in items if i["id"].split(".")[0] == group]
        if not group_items:
            continue
        lines.append(title.upper())
        for it in group_items:
            label = it["label"]
            pad = " " * max(1, LABEL_WIDTH - len(label))
            lines.append(f"  {icons[it['status']]} {label}{pad}{it['note']}".rstrip())
            if verbose:
                if it["file"]:
                    loc = it["file"] + (f":{it['line']}" if it["line"] else "")
                    lines.append(f"      {loc}")
                if it["fix"] and it["status"] in FAIL_STATUSES:
                    lines.append(f"      → {it['fix']}")
        lines.append("")

    failing = [i for i in items if i["status"] in FAIL_STATUSES]
    if failing and not verbose:
        lines.append(f"Chạy lại với -v để xem file và cách sửa của {len(failing)} mục chưa đạt.")
    return "\n".join(lines).rstrip() + "\n"


def main(argv=None) -> int:
    ap = argparse.ArgumentParser(description="Checklist release readiness cho Unity project (deterministic).")
    ap.add_argument("--project-root", help="Thư mục gốc project (mặc định: tự dò).")
    ap.add_argument("--config", type=Path, help="File config override (mặc định: <root>/.claude/validate-release.json).")
    ap.add_argument("--format", choices=("text", "json"), default="text")
    ap.add_argument("--only", help="Chỉ chạy các nhóm này, phân tách bằng dấu phẩy.")
    ap.add_argument("--fails-only", action="store_true", help="Chỉ hiện mục chưa đạt.")
    ap.add_argument("-v", "--verbose", action="store_true", help="Kèm đường dẫn file và cách sửa.")
    ap.add_argument("--ascii", action="store_true", help="Dùng ký tự ASCII thay icon unicode.")
    ap.add_argument("--strict", action="store_true", help="Cảnh báo cũng làm exit code khác 0.")
    ap.add_argument("--list-checks", action="store_true", help="In danh sách nhóm check rồi thoát.")
    args = ap.parse_args(argv)

    if args.list_checks:
        for group, (_, title) in CHECKS.items():
            print(f"{group:10s} {title}")
        return 0

    root = detect_root(args.project_root)
    if not (root / "ProjectSettings").is_dir():
        print(f"Không tìm thấy Unity project ở {root}", file=sys.stderr)
        return 2

    ctx = Ctx(root, load_config(root, args.config))
    ctx.platforms = detect_platforms(ctx)
    scan_sources(ctx)

    for group in ([g.strip() for g in args.only.split(",")] if args.only else list(CHECKS)):
        entry = CHECKS.get(group)
        if entry is None:
            print(f"Nhóm check không tồn tại: {group}", file=sys.stderr)
            return 2
        entry[0](ctx)

    items = [i for i in ctx.items if not args.fails_only or i["status"] in FAIL_STATUSES]

    if args.format == "json":
        print(json.dumps({
            "root": str(root),
            "sourceRoot": ctx.rel(ctx.source_root),
            "platforms": sorted(ctx.platforms),
            "counts": {s: sum(1 for i in items if i["status"] == s) for s in STATUSES},
            "items": items,
        }, ensure_ascii=False, indent=2))
    else:
        print(render(ctx, items, ICONS_ASCII if args.ascii else ICONS_UNICODE, args.verbose))

    statuses = {i["status"] for i in ctx.items}
    if "blocker" in statuses or (args.strict and "warn" in statuses):
        return 1
    return 0


if __name__ == "__main__":
    try:
        sys.exit(main())
    except KeyboardInterrupt:
        sys.exit(130)
