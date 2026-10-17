# -*- coding: utf-8 -*-
"""
qaprofile.py — the PURE part (no device needed) of the per-project QA profile for the /auto-test-device skill.

The skill is shared by every repo, so all project-specific values live in `.claude/project-profile.json["qa"]`
(machine-readable) and `.claude/docs/QaProfile.md` (model-readable). This module provides:
  * QA_DEFAULTS + deep-merge (identical to the template's project_profile.DEFAULTS["qa"] — a test checks they match);
  * read/write of the `qa` block WITHOUT breaking the hand formatting + `$comment*` of project-profile.json;
  * auto-detection of the repo's conventions (Features enum, screen base class, cheat, PlayerData facade, CSV, tracking…);
  * feature liveness (dead patterns, forceLive/forceDead, observed screens, static references);
  * generation of QaProfile.md from QaProfile.template.md (writes only inside the auto regions, keeps dev text);
  * charters (`<FEATURE>-NN`), coverage, CSV reading as an oracle, parsing com.ezg.autotest report.json (0.1.1 + 0.2).
Standard library only.
"""
from __future__ import annotations

import copy
import csv
import datetime as dt
import glob
import importlib.util
import io
import json
import os
import re

QA_SCHEMA_VERSION = 1
PROFILE_REL = os.path.join(".claude", "project-profile.json")
PROJECT_PROFILE_PY_REL = os.path.join(".claude", "scripts", "project_profile.py")
TEMPLATE_REL = os.path.join(".claude", "docs", "QaProfile.template.md")

# Verbatim §1 contract — change it here and also change the template's project_profile.DEFAULTS["qa"] (a test checks they match).
QA_DEFAULTS = {
    "schemaVersion": QA_SCHEMA_VERSION,
    "status": "template",
    "conventions": {
        "screenBaseTypes": ["FeatureBaseController", "UIScreenBase", "ScreenBase", "PopupBase", "UIPanel"],
        "featuresEnum": "GameEnums+Features",
        "uiManagerType": "UIManager",
        "screenPrefabPrefix": "screen_",
        "cheatTypeName": "GameSystems",
        "cheatFlagMember": "isCheat",
        "cheatEnableMethod": "TryEnableCheat",
        "cheatDisableMethod": "DisableCheat",
        "cheatMethodPrefix": "Cheat_",
        "playerDataFacade": "PlayerDataManager",
        "timeProvider": "TimeManager",
        "trackingEventNameType": "TrackingEventName",
        "trackingLogPattern": "Tracking|Firebase|AppsFlyer",
        "csvConfigGlob": "Assets/_Project/Features/**/CsvConfig/*.csv",
    },
    "liveness": {
        "deadFeaturePatterns": ["_Retired$"],
        "forceLive": [],
        "forceDead": [],
        "observed": {},
    },
    "verified": {"cheats": {}, "statePaths": {}},
    "noise": [],
    "environments": {"fontScales": [1.3], "screenSizes": ["720x1280"], "darkMode": True, "animatorScale": 0},
    "paths": {
        "profileDoc": ".claude/docs/QaProfile.md",
        "chartersDir": ".claude/docs/qa/charters",
        "autotestSettings": "ProjectSettings/EZGAutoTestSettings.json",
        "autotestReports": "AutoTestReports",
        "probeOverride": "Assets/Resources/EzgQaProbeConfig.json",
    },
}

QA_COMMENT = [
    "QA profile for the /auto-test-device skill (qadevice.py). Missing keys fall back to the template defaults;",
    "`python3 .claude/skills/auto-test-device/scripts/qadevice.py profile show` prints the merged result with each key's source.",
    "conventions = the game's code conventions (auto-detected by `profile init`; fix by hand when detection is wrong).",
    "liveness.observed / verified.* are written by the tool on `stop` (screens seen open, cheats/state paths that worked).",
    "noise = rules that skip audit findings confirmed as noise. Human-readable version: .claude/docs/QaProfile.md.",
]

PROBE_OVERRIDE_KEYS = ("screenBaseTypes", "cheatTypeName", "cheatFlagMember", "cheatEnableMethod", "cheatDisableMethod")

# ───────────────────────────────────────────────────────────── utilities

def _read(path, default=""):
    try:
        with open(path, "r", encoding="utf-8", errors="replace") as f:
            return f.read()
    except OSError:
        return default


def _read_json(path, default=None):
    try:
        with open(path, "r", encoding="utf-8") as f:
            return json.load(f)
    except (OSError, ValueError):
        return default


def _write(path, text):
    os.makedirs(os.path.dirname(path) or ".", exist_ok=True)
    with open(path, "w", encoding="utf-8", newline="\n") as f:
        f.write(text)


def today():
    return dt.date.today().isoformat()


def deep_merge(base, over):
    """dict = per key (recursive), list = replaced whole, scalar = overwritten. Does not mutate the arguments."""
    if isinstance(base, dict) and isinstance(over, dict):
        out = copy.deepcopy(base)
        for k, v in over.items():
            out[k] = deep_merge(out[k], v) if k in out else copy.deepcopy(v)
        return out
    return copy.deepcopy(over)


def _rel(root, path):
    return os.path.relpath(path, root).replace(os.sep, "/")


# ───────────────────────────────────────────────────────────── read / write the qa block

def load_project_profile_module(root):
    """Import the repo's .claude/scripts/project_profile.py (if present) — source of the template's DEFAULTS["qa"]."""
    path = os.path.join(root, PROJECT_PROFILE_PY_REL)
    if not os.path.isfile(path):
        return None
    try:
        spec = importlib.util.spec_from_file_location("ezg_project_profile", path)
        mod = importlib.util.module_from_spec(spec)
        spec.loader.exec_module(mod)
        return mod
    except Exception:
        return None


def template_qa_defaults(root):
    mod = load_project_profile_module(root)
    d = getattr(mod, "DEFAULTS", None) if mod else None
    if isinstance(d, dict) and isinstance(d.get("qa"), dict):
        return deep_merge(QA_DEFAULTS, d["qa"])
    return copy.deepcopy(QA_DEFAULTS)


def _provenance(defaults, user, path=""):
    """Map 'conventions.cheatTypeName' → dev|default for every leaf."""
    out = {}
    for k, v in defaults.items():
        p = (path + "." + k) if path else k
        if isinstance(v, dict) and k not in ("observed", "cheats", "statePaths"):
            out.update(_provenance(v, user.get(k) if isinstance(user, dict) and isinstance(user.get(k), dict) else {}, p))
        else:
            out[p] = "dev" if isinstance(user, dict) and k in user else "default"
    if isinstance(user, dict):
        for k in user:
            if k not in defaults:
                out[(path + "." + k) if path else k] = "dev"
    return out


def load_qa_profile(root, with_provenance=False):
    """Merged qa: QA_DEFAULTS ← the template's DEFAULTS["qa"] (if importable) ← the project's qa block."""
    prof = _read_json(os.path.join(root, PROFILE_REL), {}) or {}
    user = prof.get("qa") if isinstance(prof.get("qa"), dict) else {}
    defaults = template_qa_defaults(root)
    merged = deep_merge(defaults, user)
    if not with_provenance:
        return merged
    prov = _provenance(defaults, user)
    if "qa" not in prof:
        prov["_block"] = "missing"
    return merged, prov


def _top_level_key_span(text, key):
    """(start, end) of `"key": <value>` at depth 1 of the outermost JSON object; None if absent."""
    depth, i, n, in_str, esc = 0, 0, len(text), False, False
    want = json.dumps(key)
    while i < n:
        c = text[i]
        if in_str:
            if esc:
                esc = False
            elif c == "\\":
                esc = True
            elif c == '"':
                in_str = False
            i += 1
            continue
        if c == '"':
            if depth == 1 and text.startswith(want, i):
                j = i + len(want)
                while j < n and text[j] in " \t\r\n":
                    j += 1
                if j < n and text[j] == ":":
                    j += 1
                    while j < n and text[j] in " \t\r\n":
                        j += 1
                    _, end = json.JSONDecoder().raw_decode(text, j)
                    return i, end
            in_str = True
        elif c in "{[":
            depth += 1
        elif c in "}]":
            depth -= 1
        i += 1
    return None


def _indent_block(obj, indent=2, level=1):
    s = json.dumps(obj, ensure_ascii=False, indent=indent)
    pad = " " * (indent * level)
    lines = s.splitlines()
    return lines[0] + "\n" + "\n".join(pad + l for l in lines[1:]) if len(lines) > 1 else s


def save_qa_patch(root, patch, replace=False):
    """Write into the `qa` block of .claude/project-profile.json, keeping every other byte unchanged (hand format, $comment).
    replace=False: deep-merge onto the existing block; True: replace the whole block. Creates a minimal file if missing.
    Returns the qa block as written."""
    path = os.path.join(root, PROFILE_REL)
    text = _read(path, "")
    if not text.strip():
        text = "{\n}\n"
    data = json.loads(text)
    cur = data.get("qa") if isinstance(data.get("qa"), dict) else {}
    new = copy.deepcopy(patch) if replace else deep_merge(cur, patch)
    new.setdefault("schemaVersion", QA_SCHEMA_VERSION)
    block = '"qa": ' + _indent_block(new)
    span = _top_level_key_span(text, "qa")
    if span:
        text = text[: span[0]] + block + text[span[1]:]
    else:
        close = text.rstrip().rfind("}")
        head = text[:close].rstrip()
        comment = '"$comment_qa": ' + _indent_block(QA_COMMENT) if _top_level_key_span(text, "$comment_qa") is None else ""
        sep = "," if head.rstrip().endswith(("}", "]", '"', "e", "l", "0", "1", "2", "3", "4", "5", "6", "7", "8", "9")) and not head.rstrip().endswith("{") else ""
        add = ("\n\n  " + comment + ",\n  " if comment else "\n\n  ") + block + "\n"
        text = head + sep + add + text[close:]
    _write(path, text if text.endswith("\n") else text + "\n")
    return new


# ───────────────────────────────────────────────────────────── code scanning

SKIP_DIRS = {"Library", "Temp", "Logs", "obj", "UserSettings", ".git", "node_modules", "Plugins", "ThirdParty", "3rdParty", "Editor"}


def source_roots(root):
    """Game code folder: the profile's sourceRoot if set, else Assets/_Project, else Assets."""
    prof = _read_json(os.path.join(root, PROFILE_REL), {}) or {}
    cands = [prof.get("sourceRoot") or "", "Assets/_Project", "Assets"]
    for c in cands:
        if c and os.path.isdir(os.path.join(root, c)):
            return os.path.join(root, c)
    return os.path.join(root, "Assets")


def iter_cs(root, src=None):
    src = src or source_roots(root)
    for dirpath, dirnames, filenames in os.walk(src):
        dirnames[:] = [d for d in dirnames if d not in SKIP_DIRS and not d.startswith(".")]
        for fn in filenames:
            if fn.endswith(".cs"):
                yield os.path.join(dirpath, fn)


def _strip_cs(text):
    text = re.sub(r"/\*.*?\*/", "", text, flags=re.S)
    text = re.sub(r"//[^\n]*", "", text)
    return text


def _brace_body(text, open_idx):
    depth = 0
    for k in range(open_idx, len(text)):
        if text[k] == "{":
            depth += 1
        elif text[k] == "}":
            depth -= 1
            if depth == 0:
                return text[open_idx + 1:k]
    return text[open_idx + 1:]


def parse_enum(text, name):
    """Values of enum `name` in C# text (comments/attributes stripped). Returns [(name, number|None)]."""
    text = _strip_cs(text)
    m = re.search(r"\benum\s+%s\b[^{]*\{" % re.escape(name), text)
    if not m:
        return []
    body = _brace_body(text, m.end() - 1)
    body = re.sub(r"\[[^\]]*\]", "", body)
    out = []
    for part in body.split(","):
        part = part.strip()
        if not part:
            continue
        nm, _, val = part.partition("=")
        nm = nm.strip()
        if not re.match(r"^\w+$", nm):
            continue
        val = val.strip()
        num = None
        if re.match(r"^-?\d+$", val):
            num = int(val)
        out.append((nm, num))
    return out


def _enclosing_type(text, idx):
    """Name of the types (class/struct) enclosing position idx + namespace → reflection-style 'Outer+Inner'."""
    text = text[:idx]
    outers = []
    depth = 0
    # walk the braces up to idx to know which declarations we are inside
    stack = []
    for m in re.finditer(r"\b(class|struct|namespace)\s+([\w.]+)[^{;]*\{|\{|\}", text):
        tok = m.group(0)
        if tok == "{":
            stack.append(None)
        elif tok == "}":
            if stack:
                stack.pop()
        else:
            stack.append((m.group(1), m.group(2)))
    for kind, nm in [s for s in stack if s]:
        if kind in ("class", "struct"):
            outers.append(nm)
    return "+".join(outers)


def find_features_enum(root, src=None):
    """(type name 'Outer+Features', file path, [(name, number)]) or (None, None, [])."""
    best = None
    for p in iter_cs(root, src):
        t = _read(p)
        if "enum Features" not in t:
            continue
        vals = parse_enum(t, "Features")
        if len(vals) < 3:
            continue
        m = re.search(r"\benum\s+Features\b", _strip_cs(t))
        outer = _enclosing_type(_strip_cs(t), m.start()) if m else ""
        name = (outer + "+Features") if outer else "Features"
        if not best or len(vals) > len(best[2]):
            best = (name, p, vals)
    return best or (None, None, [])


SCREEN_NAME_RE = re.compile(r"Screen|Popup|Panel|Page|Window|Dialog", re.I)
# SCREEN lifecycle markers (generic Show()/Hide()/Close() do not count — any child component has them).
SCREEN_MARKERS = (r"\bOnShow\w*\b", r"\bOnHide\w*\b", r"\bOnBack\w*\b", r"\bUIManager\b", r"\bisShow\w*\b|\bIsShow\w*\b",
                  r"\bFeatures\.\w+", r"\bOnOpen\w*\b|\bOnClose\w*\b", r"\bCanvasGroup\b")


def detect_screen_bases(root, src=None, known=()):
    """UI screen base classes: ≥3 subclasses, not a subclass of another candidate, and (name in the known list | name contains
    Screen/Popup/Panel/Page/Window/Dialog | class body has ≥2 screen lifecycle markers: OnShow/OnHide/OnBack/UIManager/isShow/Features./CanvasGroup).
    Returns (list of names, {name: subclass count})."""
    parents, counts, bodies = {}, {}, {}
    for p in iter_cs(root, src):
        t = _strip_cs(_read(p))
        for m in re.finditer(r"\b(?:abstract\s+|partial\s+|sealed\s+|public\s+|internal\s+)*class\s+(\w+)\s*(?:<[^>]*>)?\s*(?::\s*([\w.]+))?", t):
            child, base = m.group(1), (m.group(2) or "").split(".")[-1]
            if base:
                parents[child] = base
                counts[base] = counts.get(base, 0) + 1
            if child not in bodies:
                ob = t.find("{", m.end())
                bodies[child] = _brace_body(t, ob) if ob >= 0 else " "
    known = set(known)
    cands = set(k for k in known if counts.get(k))
    for base, n in counts.items():
        if n < 3 or base in ("MonoBehaviour", "Component", "Behaviour", "ScriptableObject", "Object"):
            continue
        body = bodies.get(base, "")
        if not body:  # Unity/package class (EditorWindow, MonoBehaviour subclasses…) — not a game screen
            continue
        marks = sum(1 for rg in SCREEN_MARKERS if re.search(rg, body))
        if SCREEN_NAME_RE.search(base) or (marks >= 2 and re.search(r"Controller|View$", base)):
            cands.add(base)
    roots = []
    for c in sorted(cands):
        anc, cur, hop = False, parents.get(c), 0
        while cur and hop < 12:
            if cur in cands:
                anc = True
                break
            cur, hop = parents.get(cur), hop + 1
        if not anc:
            roots.append(c)
    roots.sort(key=lambda b: (0 if b in known else 1, -(counts.get(b, 0)), b))
    return roots[:8], {b: counts.get(b, 0) for b in roots}


def detect_cheat(root, src=None):
    """Type holding the cheat flag (public static bool isCheat) + enable method (asks CheatGate, takes Action<bool>) / disable method."""
    out = {"cheatTypeName": "", "cheatFlagMember": "", "cheatEnableMethod": "", "cheatDisableMethod": "", "file": "",
           "namespace": ""}
    for p in iter_cs(root, src):
        t = _read(p)
        m = re.search(r"public\s+static\s+bool\s+(isCheat|IsCheat|cheatEnabled|CheatEnabled)\b", t)
        if not m:
            continue
        cls = None
        for cm in re.finditer(r"\bclass\s+(\w+)", _strip_cs(t)):
            if cm.start() < m.start():
                cls = cm.group(1)
        if not cls:
            continue
        ns = re.search(r"^\s*namespace\s+([\w.]+)", t, re.M)
        out.update({"cheatTypeName": cls, "cheatFlagMember": m.group(1), "file": p, "namespace": ns.group(1) if ns else ""})
        if re.search(r"public\s+static\s+void\s+TryEnableCheat\s*\(\s*(System\.)?Action\s*<\s*bool\s*>", t):
            out["cheatEnableMethod"] = "TryEnableCheat"
        if re.search(r"public\s+static\s+void\s+DisableCheat\s*\(", t):
            out["cheatDisableMethod"] = "DisableCheat"
        return out
    return out


def detect_cheat_prefix(root, src=None):
    counts = {}
    for p in iter_cs(root, src):
        t = _read(p)
        for m in re.finditer(r"\bstatic\s+[\w<>\[\],.]+\s+((?:Cheat|Debug|Dev|Hack)_)\w+\s*\(", t):
            counts[m.group(1)] = counts.get(m.group(1), 0) + 1
    if not counts:
        return "", 0
    best = max(counts.items(), key=lambda kv: kv[1])
    return best


def find_class(root, names, src=None):
    """First class name from `names` declared in the game code → (name, file) | ("", "")."""
    pats = [(n, re.compile(r"\b(?:class|struct)\s+%s\b" % re.escape(n))) for n in names]
    for p in iter_cs(root, src):
        t = _read(p)
        for n, rg in pats:
            if rg.search(t):
                return n, p
    return "", ""


def detect_tracking_class(root, src=None):
    """Class named *Tracking*/*Analytics* holding ≥5 string constants (event names) — for projects without TrackingEventName."""
    best = ("", "", 0)
    for p in iter_cs(root, src):
        if not re.search(r"Track|Analytic", os.path.basename(p), re.I):
            continue
        t = _strip_cs(_read(p))
        for m in re.finditer(r"\bclass\s+(\w*(?:Track|Analytic)\w*)", t):
            body = _brace_body(t, t.find("{", m.end())) if t.find("{", m.end()) >= 0 else ""
            c = len(re.findall(r"\bconst\s+string\s+\w+\s*=", body))
            if c >= 5 and c > best[2]:
                best = (m.group(1), p, c)
    return best[0], best[1]


def detect_player_data_facade(root, src=None):
    n, p = find_class(root, ["PlayerDataManager"], src)
    if n:
        return n, p
    best = ("", "", 0)
    for p in iter_cs(root, src):
        t = _read(p)
        c = len(re.findall(r"GetModule<", t))
        if c >= 3:
            m = re.search(r"\bclass\s+(\w+)", _strip_cs(t))
            if m and c > best[2]:
                best = (m.group(1), p, c)
    return best[0], best[1]


def detect_csv_glob(root, src=None):
    src = src or source_roots(root)
    rel_src = _rel(root, src)
    csv_dirs = []
    for dirpath, dirnames, filenames in os.walk(src):
        dirnames[:] = [d for d in dirnames if d not in SKIP_DIRS and not d.startswith(".")]
        if os.path.basename(dirpath) == "CsvConfig":
            return "%s/**/CsvConfig/*.csv" % rel_src, dirpath
        n = sum(1 for f in filenames if f.lower().endswith(".csv"))
        if n >= 3:
            csv_dirs.append((n, dirpath))
    if not csv_dirs:
        return "", ""
    csv_dirs.sort(reverse=True)
    tops = (os.path.normpath(src), os.path.normpath(root))
    common = os.path.commonpath([d for _, d in csv_dirs]) if len(csv_dirs) > 1 else csv_dirs[0][1]
    if len(csv_dirs) == 1 or os.path.normpath(common) in tops or len(_rel(root, common)) < len("Assets"):
        # a single folder, or CSVs scattered around (localize, config…) → parent of the folder with the most CSVs (e.g. Assets/_Project/Csv)
        best = csv_dirs[0][1]
        parent = os.path.dirname(best)
        common = parent if os.path.normpath(parent) not in tops else best
    return "%s/**/*.csv" % _rel(root, common), common


def detect_prefab_prefix(root, src=None):
    src = src or source_roots(root)
    names = [os.path.basename(p)[:-7] for p in glob.glob(os.path.join(src, "**", "Resources", "*.prefab"), recursive=True)]
    if not names:
        names = [os.path.basename(p)[:-7] for p in glob.glob(os.path.join(src, "**", "*.prefab"), recursive=True)]
    for prefix in ("screen_", "Screen", "popup_", "Popup", "ui_", "UI_"):
        if sum(1 for n in names if n.startswith(prefix)) >= 3:
            return prefix
    return ""


def detect_conventions(root):
    """Scan the repo → {key: {"value", "source": auto|default, "evidence"}} for every conventions key + liveness patterns."""
    src = source_roots(root)
    d = QA_DEFAULTS["conventions"]
    res = {}

    def put(key, value, evidence=""):
        if value in ("", None, []):
            res[key] = {"value": d[key], "source": "default", "evidence": evidence or "not detected — using default"}
        else:
            res[key] = {"value": value, "source": "auto", "evidence": evidence}

    enum_name, enum_file, vals = find_features_enum(root, src)
    put("featuresEnum", enum_name, "%s (%d values)" % (_rel(root, enum_file), len(vals)) if enum_file else "")
    bases, counts = detect_screen_bases(root, src, known=d["screenBaseTypes"])
    put("screenBaseTypes", bases, ", ".join("%s×%d" % (b, counts.get(b, 0)) for b in bases))
    n, p = find_class(root, ["UIManager", "UIController", "ScreenManager"], src)
    put("uiManagerType", n, _rel(root, p) if p else "")
    put("screenPrefabPrefix", detect_prefab_prefix(root, src), "from prefab names in Resources")
    ch = detect_cheat(root, src)
    put("cheatTypeName", ch["cheatTypeName"], _rel(root, ch["file"]) if ch["file"] else "")
    put("cheatFlagMember", ch["cheatFlagMember"], "")
    if ch["file"]:
        res["cheatEnableMethod"] = {"value": ch["cheatEnableMethod"], "source": "auto",
                                    "evidence": "" if ch["cheatEnableMethod"] else "no TryEnableCheat(Action<bool>) — cheat is enabled the old/manual way"}
        res["cheatDisableMethod"] = {"value": ch["cheatDisableMethod"], "source": "auto",
                                     "evidence": "" if ch["cheatDisableMethod"] else "no DisableCheat"}
    else:
        put("cheatEnableMethod", "", "")
        put("cheatDisableMethod", "", "")
    pre, cnt = detect_cheat_prefix(root, src)
    put("cheatMethodPrefix", pre, "%d static methods" % cnt if cnt else "")
    n, p = detect_player_data_facade(root, src)
    put("playerDataFacade", n, _rel(root, p) if p else "")
    n, p = find_class(root, ["TimeManager", "TimeService", "GameTime"], src)
    put("timeProvider", n, _rel(root, p) if p else "")
    n, p = find_class(root, ["TrackingEventName", "TrackingEvents", "AnalyticsEventName", "EventTrackingName"], src)
    if not n:
        n, p = detect_tracking_class(root, src)
    put("trackingEventNameType", n, _rel(root, p) if p else "")
    res["trackingLogPattern"] = {"value": d["trackingLogPattern"], "source": "default", "evidence": "default tracking log pattern"}
    g, gd = detect_csv_glob(root, src)
    put("csvConfigGlob", g, _rel(root, gd) if gd else "")
    # liveness patterns: "dead" suffixes seen in the enum
    pats = list(QA_DEFAULTS["liveness"]["deadFeaturePatterns"])
    names = [v[0] for v in vals]
    for suf in ("_Retired", "_Old", "_Legacy", "_Deprecated", "_Unused"):
        if any(nm.endswith(suf) for nm in names) and ("%s$" % suf) not in pats:
            pats.append("%s$" % suf)
    res["_deadFeaturePatterns"] = {"value": pats, "source": "auto" if len(pats) > 1 else "default",
                                   "evidence": "%d/%d enum values match" % (sum(1 for nm in names if any(re.search(pt, nm) for pt in pats)), len(names))}
    res["_features"] = {"value": names, "source": "auto", "evidence": enum_name or ""}
    res["_sourceRoot"] = {"value": _rel(root, src), "source": "auto", "evidence": ""}
    return res


# ───────────────────────────────────────────────────────────── liveness

def feature_names(root, qa):
    """Values of the Features enum named by qa.conventions.featuresEnum (last segment after '+'/'.')."""
    want = (qa.get("conventions") or {}).get("featuresEnum") or "Features"
    enum_short = want.split("+")[-1].split(".")[-1]
    for p in iter_cs(root):
        t = _read(p)
        if ("enum %s" % enum_short) in t:
            vals = parse_enum(t, enum_short)
            if len(vals) >= 3:
                return [v[0] for v in vals], p
    return [], ""


def _screen_token(name):
    """'screen_offline_earning(Clone)' / 'ScreenOfflineEarningController' → 'offlineearning' for comparing with the enum."""
    s = re.sub(r"\(Clone\)$", "", name or "").strip()
    s = re.sub(r"^(screen_|popup_|ui_)", "", s, flags=re.I)
    s = re.sub(r"^Screen", "", s)
    s = re.sub(r"(Controller|View|Popup|Screen)$", "", s)
    return re.sub(r"[^a-z0-9]", "", s.lower())


def match_feature(screen_name, names):
    """Screen GameObject name (from the probe) → best-matching Feature enum name, or None."""
    tok = _screen_token(screen_name)
    if not tok:
        return None
    by_tok = {}
    for n in names:
        by_tok.setdefault(re.sub(r"[^a-z0-9]", "", n.lower()), n)
    if tok in by_tok:
        return by_tok[tok]
    # 'offlineearnings' vs 'offlineearning' — accept a trailing s/plural, or a long prefix match
    for t2, n in by_tok.items():
        if len(tok) >= 5 and (t2.rstrip("s") == tok.rstrip("s") or (tok.startswith(t2) and len(t2) >= 6) or (t2.startswith(tok) and len(tok) >= 6)):
            return n
    return None


def static_refs(root, names, enum_file=""):
    """Number of code files (excluding the enum file and the bundle/AssetRefs maps) that reference `Features.<Name>`."""
    counts = {n: 0 for n in names}
    rg = re.compile(r"\bFeatures\.(\w+)\b")
    skip = re.compile(r"AssetRefs\.cs$|AssetBundleName\.cs$|\.Generated\.cs$")
    for p in iter_cs(root):
        if p == enum_file or skip.search(p):
            continue
        t = _read(p)
        if "Features." not in t:
            continue
        seen = set(m.group(1) for m in rg.finditer(t))
        for n in seen:
            if n in counts:
                counts[n] += 1
    return counts


def classify_features(root, qa, names=None, enum_file=None, refs=True):
    """→ {name: {"state": live|dead|unreferenced|referenced, "why": ...}}.
    dead  = matches deadFeaturePatterns or forceDead;  live = forceLive or observed;
    the rest by static references: referenced (some code uses it) / unreferenced (no code reference found — may open via a prefab)."""
    lv = qa.get("liveness") or {}
    if names is None:
        names, enum_file = feature_names(root, qa)
    pats = [re.compile(p) for p in lv.get("deadFeaturePatterns") or []]
    force_live, force_dead = set(lv.get("forceLive") or []), set(lv.get("forceDead") or [])
    observed = lv.get("observed") or {}
    counts = static_refs(root, names, enum_file or "") if refs else {}
    out = {}
    for n in names:
        if n.lower() == "none":
            continue
        if n in force_dead:
            out[n] = {"state": "dead", "why": "forceDead"}
        elif any(p.search(n) for p in pats):
            out[n] = {"state": "dead", "why": "matches a dead pattern"}
        elif n in force_live:
            out[n] = {"state": "live", "why": "forceLive"}
        elif n in observed:
            out[n] = {"state": "live", "why": "seen open %s" % observed[n]}
        elif refs:
            c = counts.get(n, 0)
            out[n] = {"state": "referenced" if c else "unreferenced", "why": "referenced by %d file(s)" % c if c else "no file references Features.%s" % n}
        else:
            out[n] = {"state": "referenced", "why": ""}
    return out


# ───────────────────────────────────────────────────────────── QaProfile.md

# `<Tên game>` is a placeholder token that qadevice.py replaces with the product name — keep it verbatim.
MINIMAL_QA_DOC = """# QA Profile — <Tên game>

**Status:** template
**Updated:** <YYYY-MM-DD> · **Reviewer:** <dev>

(Minimal frame generated by the tool because the repo has no `.claude/docs/QaProfile.template.md` yet — update the
template's AI kit to get the full §0–§11 frame; text outside the auto regions below is kept on refresh.)

## 2. Code conventions
<!-- auto:conventions:start -->
<!-- auto:conventions:end -->

## 3. Screens / features: live – dead – observed
<!-- auto:features:start -->
<!-- auto:features:end -->

## 5. Verified cheats & state paths
<!-- auto:verified:start -->
<!-- auto:verified:end -->

## 7. Confirmed noise rules
<!-- auto:noise:start -->
<!-- auto:noise:end -->

## 8. Environments & devices
<!-- auto:environments:start -->
<!-- auto:environments:end -->

## 10. Paths
<!-- auto:paths:start -->
<!-- auto:paths:end -->

## 11. Session history
| Date | Feature | Device | Bug | Coverage | Notes |
|---|---|---|---|---|---|
"""

AUTO_RE = re.compile(r"(<!--\s*auto:(\w+):start\s*-->)(.*?)(<!--\s*auto:\2:end\s*-->)", re.S)

# Label of the date line. "Cập nhật" is the old (Vietnamese) label — still accepted so existing QaProfile.md files keep
# working; set_status keeps whichever label the doc already uses.
UPDATED_LINE_RE = re.compile(r"^\*\*(Updated|Cập nhật):\*\*\s*[^·\n]*", re.M)


def fill_auto_sections(doc, sections):
    """Replace the content between <!-- auto:<name>:start --> … <!-- auto:<name>:end -->; everything outside is kept."""
    def rep(m):
        name = m.group(2)
        if name not in sections:
            return m.group(0)
        return "%s\n%s\n%s" % (m.group(1), sections[name].rstrip("\n"), m.group(4))
    return AUTO_RE.sub(rep, doc)


def set_status(doc, status, date=None):
    doc = re.sub(r"^\*\*Status:\*\*\s*\S+", "**Status:** %s" % status, doc, count=1, flags=re.M)
    if date:
        doc = UPDATED_LINE_RE.sub(lambda m: "**%s:** %s " % (m.group(1), date), doc, count=1)
    return doc


def doc_status(doc):
    m = re.search(r"^\*\*Status:\*\*\s*(\S+)", doc or "", re.M)
    return m.group(1) if m else ""


def render_sections(root, qa, det, classes, game_name=""):
    """Tables for the auto regions of QaProfile.md."""
    conv = qa.get("conventions") or {}
    rows = ["| Key | Value | Source | Evidence |", "|---|---|---|---|"]
    for k in QA_DEFAULTS["conventions"]:
        v = conv.get(k)
        info = det.get(k, {})
        rows.append("| `%s` | `%s` | %s | %s |" % (k, json.dumps(v, ensure_ascii=False) if isinstance(v, list) else (v or "—"),
                                               info.get("source", "?"), info.get("evidence", "")))
    conv_md = "\n".join(rows)
    live = sorted(n for n, c in classes.items() if c["state"] in ("live", "referenced"))
    unref = sorted(n for n, c in classes.items() if c["state"] == "unreferenced")
    dead = sorted(n for n, c in classes.items() if c["state"] == "dead")
    lv = qa.get("liveness") or {}
    feat_md = "\n".join([
        "- Enum: `%s` — %d values · dead patterns: %s" % (conv.get("featuresEnum"), len(classes) + 1, ", ".join("`%s`" % p for p in lv.get("deadFeaturePatterns") or [])),
        "- **Live (referenced in code / observed): %d** — %s" % (len(live), ", ".join(live) or "—"),
        "- **No code reference found (may open via prefab / serialized enum — confirm by observation): %d** — %s" % (len(unref), ", ".join(unref) or "—"),
        "- **Dead (skipped in tests): %d** — %s" % (len(dead), ", ".join(dead) or "—"),
        "- Observed open at runtime: %s" % (", ".join("%s (%s)" % kv for kv in sorted((lv.get("observed") or {}).items())) or "no sessions yet"),
    ])
    ver = qa.get("verified") or {}
    cheats = ver.get("cheats") or {}
    paths = ver.get("statePaths") or {}
    ver_md = "\n".join(["| Kind | Name / path | Last OK |", "|---|---|---|"] +
                       ["| cheat | `%s` | %s |" % kv for kv in sorted(cheats.items())] +
                       ["| state | `%s` → `%s` | %s |" % (k, v.get("path", ""), v.get("lastOk", "")) for k, v in sorted(paths.items())]
                       or ["| — | none yet | |"])
    noise = qa.get("noise") or []
    noise_md = "\n".join(["| Flag | Match | Reason | By / date |", "|---|---|---|---|"] +
                         ["| `%s` | `%s` | %s | %s %s |" % (r.get("flag"), r.get("match"), r.get("reason", ""), r.get("addedBy", ""), r.get("date", "")) for r in noise]
                         or ["| — | no rules yet | | |"])
    env = qa.get("environments") or {}
    env_md = "\n".join([
        "- System font scales: %s" % ", ".join(str(x) for x in env.get("fontScales") or []),
        "- Test screen sizes: %s" % ", ".join(env.get("screenSizes") or []) if env.get("screenSizes") else "- Test screen sizes: (none)",
        "- Dark mode: %s · animator scale: %s" % ("yes" if env.get("darkMode") else "no", env.get("animatorScale")),
    ])
    paths_md = "\n".join("- `%s`: `%s`" % kv for kv in sorted((qa.get("paths") or {}).items()))
    return {"conventions": conv_md, "features": feat_md, "verified": ver_md, "noise": noise_md, "environments": env_md,
            "paths": paths_md}


# ───────────────────────────────────────────────────────────── charter

# Case id in a charter table: `P0-01` (priority-number) — full reference `<charter-file-name>/<P0-01>` (e.g. offline-earning/P0-01).
CASE_ID_RE = re.compile(r"\|\s*((?:P[0-5]|[A-Z][A-Z0-9]{1,9})-(\d{2,3}))\s*\|")
FULL_CASE_RE = re.compile(r"^(?:([\w.-]+)/)?((?:P[0-5]|[A-Z][A-Z0-9]{1,9})-\d{2,3})$", re.I)


def charter_dir(root, qa):
    return os.path.join(root, (qa.get("paths") or {}).get("chartersDir") or QA_DEFAULTS["paths"]["chartersDir"])


def charter_slug(feature):
    s = re.sub(r"[^a-z0-9]+", "-", _strip_accents(feature or "").lower()).strip("-")
    return s[:40] or "charter"


def _strip_accents(s):
    import unicodedata
    s = unicodedata.normalize("NFD", s or "")
    s = "".join(c for c in s if unicodedata.category(c) != "Mn")
    return s.replace("đ", "d").replace("Đ", "D")


def split_case(ref, default_charter=""):
    """'offline-earning/P0-01' | 'P0-01' → (charter, 'P0-01'), or None if malformed."""
    m = FULL_CASE_RE.match((ref or "").strip())
    if not m:
        return None
    return (m.group(1) or default_charter or ""), m.group(2).upper()


def full_case(charter, case):
    return ("%s/%s" % (charter, case)) if charter else case


def list_charters(root, qa):
    d = charter_dir(root, qa)
    out = []
    for p in sorted(glob.glob(os.path.join(d, "*.md"))):
        if os.path.basename(p).lower() == "readme.md":
            continue
        t = _read(p)
        ids = [m.group(1) for m in CASE_ID_RE.finditer(t)]
        title = re.search(r"^#\s+(.+)$", t, re.M)
        out.append({"file": p, "id": os.path.splitext(os.path.basename(p))[0], "title": title.group(1).strip() if title else "",
                    "cases": ids, "status": doc_status(t)})
    return out


def charter_cases(root, qa, charter_id=None):
    """{'<charter>/<P0-01>': {"charter", "case", "priority", "title"}} from every charter (or one charter by file name)."""
    out = {}
    for ch in list_charters(root, qa):
        if charter_id and ch["id"] != charter_id:
            continue
        for line in _read(ch["file"]).splitlines():
            m = CASE_ID_RE.search(line)
            if not m:
                continue
            cid = m.group(1).upper()
            cells = [c.strip() for c in line.strip().strip("|").split("|")]
            pri = cid.split("-")[0] if cid.startswith("P") else next((c for c in cells if re.match(r"^P[0-5]$", c)), "")
            title = next((c for c in cells if c and c != m.group(1) and not re.match(r"^P[0-5]$", c)), "")
            out[full_case(ch["id"], cid)] = {"charter": ch["id"], "case": cid, "priority": pri, "title": title}
    return out


def new_charter_text(template, feature, qa, sources=""):
    # Placeholders below are literal tokens of references/charter.template.md — keep them in sync with that file.
    t = template.replace("<Tính năng>", feature).replace("<tính năng>", feature).replace("<YYYY-MM-DD>", today())
    if sources:
        # "<GDD/TechSpec/file code/CSV đã đọc>" = token of the old (Vietnamese) template — still replaced, in case of an old copy.
        for token in ("<GDD/TechSpec/code files/CSVs read>", "<GDD/TechSpec/file code/CSV đã đọc>"):
            t = t.replace(token, sources)
    return t


# ───────────────────────────────────────────────────────────── coverage

def coverage_summary(cases_run, planned, classes, observed_now):
    """cases_run: [{case, result}], planned: charter_cases(), classes: classify_features(), observed_now: {feature: date}."""
    results = {}
    for c in cases_run:
        if c.get("case"):
            results[c["case"]] = c.get("result") or results.get(c["case"]) or ""
    by_charter = {}
    for cid, info in planned.items():
        b = by_charter.setdefault(info["charter"], {"planned": 0, "run": 0, "pass": 0, "fail": 0, "blocked": 0, "skip": 0, "not_run": []})
        b["planned"] += 1
        r = results.get(cid)
        if r:
            b["run"] += 1
            b[r if r in ("pass", "fail", "blocked", "skip") else "run"] = b.get(r, 0) + (1 if r in ("pass", "fail", "blocked", "skip") else 0)
        else:
            b["not_run"].append(cid)
    adhoc = sorted(c for c in results if c not in planned)
    live = sorted(n for n, c in classes.items() if c["state"] in ("live", "referenced"))
    unvisited = [n for n in live if n not in observed_now]
    return {"charters": by_charter, "adhoc": adhoc, "results": results, "live": live, "visited": sorted(observed_now),
            "unvisited": unvisited}


def coverage_markdown(cov, title=""):
    out = ["## Coverage%s" % (" — " + title if title else ""), ""]
    if cov["charters"]:
        out += ["| Charter | Planned | Run | Pass | Fail | Blocked | Skip | Not run |", "|---|---|---|---|---|---|---|---|"]
        for ch, b in sorted(cov["charters"].items()):
            out.append("| %s | %d | %d | %d | %d | %d | %d | %s |" % (ch, b["planned"], b["run"], b["pass"], b["fail"], b["blocked"], b["skip"],
                                                               ", ".join(b["not_run"]) or "—"))
    else:
        out.append("- No charters in the repo yet (`charter new <feature>`).")
    if cov["adhoc"]:
        out.append("- Cases recorded outside any charter: %s" % ", ".join(cov["adhoc"]))
    out.append("")
    out.append("- Live screens: %d · opened this session: %d · **not opened: %d** — %s" % (
        len(cov["live"]), len(cov["visited"]), len(cov["unvisited"]), ", ".join(cov["unvisited"][:40]) + ("…" if len(cov["unvisited"]) > 40 else "")))
    return "\n".join(out) + "\n"


# ───────────────────────────────────────────────────────────── CSV oracle

def find_csv(root, qa, name):
    pat = (qa.get("conventions") or {}).get("csvConfigGlob") or QA_DEFAULTS["conventions"]["csvConfigGlob"]
    files = glob.glob(os.path.join(root, pat), recursive=True)
    want = name.lower()
    if want.endswith(".csv"):
        want = want[:-4]
    exact = [f for f in files if os.path.splitext(os.path.basename(f))[0].lower() == want]
    if exact:
        return exact[0], files
    part = [f for f in files if want in os.path.basename(f).lower()]
    return (part[0] if len(part) == 1 else None), (part or files)


def read_csv_rows(path):
    text = _read(path)
    if text.startswith("\ufeff"):
        text = text[1:]
    rows = list(csv.reader(io.StringIO(text)))
    rows = [r for r in rows if any(c.strip() for c in r)]
    if not rows:
        return [], []
    header = [h.strip() for h in rows[0]]
    out = []
    for r in rows[1:]:
        if header and r and r[0].strip().startswith("#"):
            continue
        out.append({header[i] if i < len(header) else "col%d" % i: (r[i].strip() if i < len(r) else "") for i in range(max(len(header), len(r)))})
    return header, out


def filter_rows(rows, where):
    """where: ['col=val', 'col~regex', 'col>=3']"""
    def ok(row):
        for w in where or []:
            m = re.match(r"^(\w+)\s*(>=|<=|!=|=|~|>|<)\s*(.*)$", w)
            if not m:
                continue
            col, op, val = m.groups()
            cur = str(row.get(col, ""))
            if op == "=":
                if cur.lower() != val.lower():
                    return False
            elif op == "!=":
                if cur.lower() == val.lower():
                    return False
            elif op == "~":
                if not re.search(val, cur, re.I):
                    return False
            else:
                try:
                    a, b = float(cur), float(val)
                except ValueError:
                    return False
                if not {">": a > b, "<": a < b, ">=": a >= b, "<=": a <= b}[op]:
                    return False
        return True
    return [r for r in rows if ok(r)]


# ───────────────────────────────────────────────────────────── com.ezg.autotest report.json

SEVERITY_INT = {0: "Info", 1: "Minor", 2: "Major", 3: "Critical", 4: "Blocker"}
STATUS_INT = {0: "NotRun", 1: "Running", 2: "Passed", 3: "Warning", 4: "Failed", 5: "Error", 6: "Skipped", 7: "Cancelled"}
SEV_ORDER = ("Blocker", "Critical", "Major", "Minor", "Info")


def _sev(v):
    if isinstance(v, bool):
        return "Info"
    if isinstance(v, int):
        return SEVERITY_INT.get(v, str(v))
    return str(v or "Info").capitalize()


def _status(v):
    if isinstance(v, int) and not isinstance(v, bool):
        return STATUS_INT.get(v, str(v))
    return str(v or "")


def screen_of_case(case):
    cid = str(case.get("caseId") or "")
    m = re.match(r"^screen-(\w+)$", cid)
    if m:
        return m.group(1)
    m = re.search(r"Mở màn\s+(\w+)", str(case.get("name") or ""))  # case name as written by com.ezg.autotest (Vietnamese)
    return m.group(1) if m else ""


def latest_report(root, qa):
    d = os.path.join(root, (qa.get("paths") or {}).get("autotestReports") or "AutoTestReports")
    cands = sorted(glob.glob(os.path.join(d, "*", "report.json")))
    return cands[-1] if cands else None


def parse_autotest_report(path):
    """report.json 0.1.1 (numeric enums) or 0.2 (strings) → flat structure: issues[], cases[], summary."""
    d = _read_json(path, None)
    if not isinstance(d, dict):
        raise ValueError("cannot read %s" % path)
    issues, cases = [], []
    for s in d.get("suites") or []:
        sid = s.get("suiteId") or s.get("id") or ""
        for c in s.get("cases") or []:
            scr = c.get("screen") or screen_of_case(c)
            cases.append({"suite": sid, "caseId": c.get("caseId", ""), "name": c.get("name", ""), "status": _status(c.get("status")),
                          "screen": scr, "issues": len(c.get("issues") or [])})
            for i in c.get("issues") or []:
                issues.append({"suite": sid, "case": c.get("caseId", ""), "group": i.get("category") or i.get("group") or "",
                               "severity": _sev(i.get("severity")), "location": i.get("location", ""), "objectPath": i.get("objectPath", ""),
                               "message": i.get("title") or i.get("message", ""), "screen": i.get("screen") or scr})
    for i in d.get("issues") or []:  # format 0.2 may keep issues flat at the root
        issues.append({"suite": i.get("suite", ""), "case": i.get("case", ""), "group": i.get("group") or i.get("category", ""),
                       "severity": _sev(i.get("severity")), "location": i.get("location", ""), "objectPath": i.get("objectPath", ""),
                       "message": i.get("message", ""), "screen": i.get("screen", "")})
    by_sev = {}
    by_suite = {}
    for i in issues:
        by_sev[i["severity"]] = by_sev.get(i["severity"], 0) + 1
        by_suite.setdefault(i["suite"], {}).setdefault(i["severity"], 0)
        by_suite[i["suite"]][i["severity"]] += 1
    covered = sorted(set(c["screen"] for c in cases if c["screen"] and c["status"] in ("Passed", "Warning")))
    failed_screens = sorted(set(c["screen"] for c in cases if c["screen"] and c["status"] in ("Failed", "Error")))
    return {"path": path, "runId": d.get("runId", ""), "version": d.get("version") or ("0.2" if any(isinstance((s.get("status")), str) for s in d.get("suites") or []) else "0.1.1"),
            "startedAt": d.get("startedAt", ""), "env": d.get("env") or {}, "summary": d.get("summary") or {},
            "issues": issues, "cases": cases, "bySeverity": by_sev, "bySuite": by_suite,
            "coveredScreens": covered, "failedScreens": failed_screens}


def autotest_markdown(rep, max_rows=40):
    out = ["## Autotest import — %s" % (rep.get("runId") or os.path.basename(os.path.dirname(rep["path"]))),
           "", "- File: `%s` · format %s · %s" % (rep["path"], rep["version"], rep.get("startedAt", "")),
           "- By severity: " + " · ".join("%s %d" % (s, rep["bySeverity"].get(s, 0)) for s in SEV_ORDER if rep["bySeverity"].get(s)),
           "- By suite: " + " · ".join("%s(%s)" % (s, ",".join("%s%d" % (k[0], v) for k, v in sorted(m.items()))) for s, m in sorted(rep["bySuite"].items())),
           "- Screens autotest opened OK (Δ-skip the open-screen smoke): %d — %s" % (len(rep["coveredScreens"]), ", ".join(rep["coveredScreens"]) or "—"),
           "- Screens autotest FAILED to open (confirm on device first): %d — %s" % (len(rep["failedScreens"]), ", ".join(rep["failedScreens"]) or "—"), ""]
    hot = [i for i in rep["issues"] if i["severity"] in ("Blocker", "Critical")]
    if hot:
        out += ["### Blocker/Critical (%d)" % len(hot), "", "| Suite | Group | Screen | Location | Message |", "|---|---|---|---|---|"]
        for i in hot[:max_rows]:
            out.append("| %s | %s | %s | %s | %s |" % (i["suite"], i["group"], i["screen"], (i["objectPath"] or i["location"])[-60:],
                                                   i["message"][:90].replace("|", "/")))
        out.append("")
    return "\n".join(out) + "\n"
