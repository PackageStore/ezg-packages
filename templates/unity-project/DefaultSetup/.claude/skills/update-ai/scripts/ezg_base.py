#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""ezg_base.py — ownership + safe update of the EZG base agent system inside a game project.

Every file the EZG base ships (skills, commands, agents, rules, docs, scripts, CLAUDE.md, ...) is
OWNED BY THE BASE: a project must not edit it in place, because the next update would overwrite the
edit. A project's own instructions live in files the base never ships:

  CLAUDE_PROJECT.md                              the project's half of CLAUDE.md
  .claude/project/overrides/<kind>/<name>.md     per-skill/agent/rule/doc adjustments (override wins)
  .claude/project/ezg-pins.json                  items the project holds back from updates
  .claude/skills|commands|agents|rules/local-*   the project's own skills/commands/agents/rules
  .claude/docs/ArtStyle.md, QaProfile.md, ...    project-owned docs seeded once from a template

`.claude/ezg-base.lock.json` records, for every base file, the hash of the version installed. With
that baseline (or, for a project without one, the server's list of every hash ever published for a
path) an update can tell apart "the base moved" from "the project edited this file", and never
silently overwrites the second.

Hashes are sha256 over the content with CRLF folded to LF for text files (no NUL byte in the first
8000 bytes), so a Windows checkout with core.autocrlf does not look like an edit. The publish script
(scripts/ai-catalog-lib.mjs) and Feature Hub (EzgBaseLock.cs) compute exactly the same thing.

Commands (run from anywhere inside the project):
  status [--json]                  what an update would do — read-only
  apply  [--take-upstream P ...] [--install ID ...] [--no-new] [--json]
                                   apply every safe change; files edited locally are kept unless
                                   named with --take-upstream (after their intent moved to an override)
  diff PATH                        baseline→local (what the project changed) + baseline→upstream
  show PATH --which baseline|upstream|local
  pin ID --reason TEXT | unpin ID  hold an item back from updates
  lock-init [--from-dir DIR] [--force]
                                   create/refresh the lock (DIR = a template DefaultSetup/ folder)
  modified [--json] [--include-merge]
                                   base files edited in place, offline (lock only); --include-merge
                                   adds edited settings.json/.mcp.json (for plain copiers)
  guard                            PreToolUse hook: deny Edit/Write on base files
  session-context                  SessionStart hook: list the project's overrides

Common options: --root DIR (project root; default: nearest parent with .claude/),
                --source DIR|URL (catalog root; default: the EZG gateway, or $EZG_AI_SOURCE)

Exit codes: 0 ok · 1 error (message on stderr) · hooks always exit 0 (fail-open).
"""
import argparse
import datetime as _dt
import difflib
import hashlib
import io
import json
import os
import stat
import sys
import urllib.error
import urllib.request
import zipfile

# ----------------------------------------------------------------------------- constants

LOCK_REL = ".claude/ezg-base.lock.json"
PINS_REL = ".claude/project/ezg-pins.json"
OVERRIDES_REL = ".claude/project/overrides"
RECORD_REL = "ProjectSettings/EzgFeatureHub/install-record.json"

DEFAULT_GATEWAY = "https://upm-registry-worker.developer-a1f.workers.dev"
USER_AGENT = "ezg-update-ai/1"
HTTP_TIMEOUT = 60

# Config files a project legitimately edits (hooks, MCP servers, env): updated by a 3-way JSON merge,
# never overwritten, never blocked by the guard. The catalog marks them `ownership: "merge"`; this set
# is the fallback for a catalog published before that field existed.
MERGE_FALLBACK = {".claude/settings.json", ".mcp.json"}

# Project-owned paths. The base never ships them, an update never touches them, the guard allows them.
PROJECT_OWNED_EXACT = {
    "CLAUDE_PROJECT.md",
    ".claude/project-profile.json",
    ".claude/settings.local.json",
    ".claude/docs/ArtStyle.md",
    ".claude/docs/QaProfile.md",
    ".claude/ui-kit/ui-kit.json",
    ".claude/ui-kit/ui-kit.css",
    ".claude/ui-kit/kit-preview.html",
}
PROJECT_OWNED_PREFIX = (".claude/project/", ".claude/docs/ArtStyle/", ".claude/docs/qa/")
PROJECT_ITEM_DIRS = (".claude/skills/", ".claude/commands/", ".claude/agents/", ".claude/rules/")
PROJECT_ITEM_PREFIX = "local-"

# Category layout — mirrors KIND_MAP in scripts/ai-catalog-lib.mjs (used offline by lock-init --from-dir).
KIND_DIRS = [
    (".claude/skills/", "Skills", "dir"),
    (".claude/commands/", "Commands", "md"),
    (".claude/agents/", "Agents", "md"),
    (".claude/rules/", "Rules", "md"),
    (".claude/docs/", "Docs", "any"),
    (".claude/scripts/", "Scripts", "any"),
    (".claude/harness/", "Harness", "any"),
    (".claude/backlog-templates/", "Templates", "any"),
    (".claude/ui-kit/", "UiKit", "any"),
]
CONFIG_FILES = {".claude/settings.json": "Config/settings.json", ".mcp.json": "Config/.mcp.json",
                "CLAUDE.md": "Config/CLAUDE.md"}
JUNK_NAMES = {".DS_Store", "settings.local.json", "__pycache__", ".pytest_cache"}

EXEC_EXT = (".sh", ".command", ".py")

# Where a project puts its adjustment for a base file (shown by status + the guard).
OVERRIDE_KIND = {"Skills": "skills", "Commands": "commands", "Agents": "agents", "Rules": "rules",
                 "Docs": "docs", "Templates": "backlog-templates"}

# Post-update steps, keyed by what the update wrote.
UI_PIPELINE_HINTS = ("ui_spec_common.py", "ui-kit-sync.py", "ui-spec-", "ui-review", "ui-visual-diff",
                     "ui-build-report", "project_profile.py", ".claude/ui-kit/")


class EzgError(Exception):
    """A failure with a message meant for the developer (printed without a traceback)."""


# ----------------------------------------------------------------------------- hashing / paths

def nsha(data):
    """sha256 of `data`, CRLF folded to LF for text — identical in Node (publish) and C# (Feature Hub)."""
    if b"\0" not in data[:8000]:
        data = data.replace(b"\r\n", b"\n")
    return hashlib.sha256(data).hexdigest()


def posix(path):
    return path.replace("\\", "/")


def find_root(start):
    """Nearest directory at or above `start` that holds a `.claude/` folder."""
    current = os.path.abspath(start)
    while True:
        if os.path.isdir(os.path.join(current, ".claude")):
            return current
        parent = os.path.dirname(current)
        if parent == current:
            return None
        current = parent


def is_project_owned(rel):
    if rel in PROJECT_OWNED_EXACT or rel.startswith(PROJECT_OWNED_PREFIX):
        return True
    for base in PROJECT_ITEM_DIRS:
        if rel.startswith(base) and rel[len(base):].startswith(PROJECT_ITEM_PREFIX):
            return True
    return False


def item_id_for(rel):
    """Catalog id of the item a project-relative path belongs to ("Skills/ui-kit"), or None."""
    if rel in CONFIG_FILES:
        return CONFIG_FILES[rel]
    for prefix, category, mode in KIND_DIRS:
        if not rel.startswith(prefix):
            continue
        rest = rel[len(prefix):]
        head = rest.split("/", 1)[0]
        is_dir = "/" in rest
        if mode == "dir":
            return "%s/%s" % (category, head) if is_dir else None
        if mode == "md":
            return "%s/%s" % (category, head[:-3]) if (not is_dir and head.endswith(".md")) else None
        name = head[:-3] if (not is_dir and head.lower().endswith(".md")) else head
        return "%s/%s" % (category, name)
    return None


def install_path_for(rel):
    """installPath of the item `rel` belongs to (dir or file), mirroring the catalog."""
    if rel in CONFIG_FILES:
        return rel
    for prefix, _category, mode in KIND_DIRS:
        if rel.startswith(prefix):
            rest = rel[len(prefix):]
            head = rest.split("/", 1)[0]
            return prefix + head
    return rel


def override_path_for(rel, item_id):
    """Where a project writes its adjustment for base file `rel` (None = no markdown override possible)."""
    if rel == "CLAUDE.md":
        return "CLAUDE_PROJECT.md"
    if rel == ".claude/ui-kit/ui-kit-usage.json":
        return ".claude/project/ui-kit-usage.json"
    category, _, name = (item_id or "").partition("/")
    kind = OVERRIDE_KIND.get(category)
    if not kind:
        return None  # scripts / config: behaviour lives in code — fix it upstream (or pin the item)
    return "%s/%s/%s.md" % (OVERRIDES_REL, kind, name)


def now_utc():
    return _dt.datetime.now(_dt.timezone.utc).replace(microsecond=0).isoformat().replace("+00:00", "Z")


def today():
    return _dt.date.today().isoformat()


def read_json_file(path, default=None):
    try:
        with open(path, encoding="utf-8") as handle:
            return json.load(handle)
    except (OSError, ValueError):
        return default


def dump_json(data):
    return (json.dumps(data, indent=2, ensure_ascii=False) + "\n").encode("utf-8")


# ----------------------------------------------------------------------------- remote catalog

class Remote:
    """The AI catalog: index.json + per-item zips + history.json + content-addressed blobs.

    `source` is the catalog root — the gateway URL ".../template/ai" or, for tests and offline
    mirrors, a local directory with the same layout (index.json, history.json, blobs/<sha>,
    files/<Category>/<file>.zip).
    """

    def __init__(self, source=None):
        gateway = (os.environ.get("EZG_GATEWAY_URL") or DEFAULT_GATEWAY).rstrip("/")
        self.gateway = gateway
        source = source or os.environ.get("EZG_AI_SOURCE") or gateway + "/template/ai"
        self.is_local = not source.startswith(("http://", "https://"))
        self.base = source.rstrip("/") if not self.is_local else os.path.abspath(source)
        self._index = None
        self._history = None
        self._blobs = {}
        self._items = {}
        self._token = None
        self._refreshed = False

    # -- transport

    def _cred_file(self):
        return os.environ.get("EZG_CRED_FILE") or os.path.join(os.path.expanduser("~"), ".ezg", "credentials.json")

    def _load_token(self):
        if self._token is None:
            self._token = os.environ.get("EZG_TOKEN") or (read_json_file(self._cred_file(), {}) or {}).get("access_token") or ""
        return self._token

    def _refresh_token(self):
        """One silent refresh (same call as ~/.ezg/refresh-session.sh), then retry."""
        if self._refreshed or os.environ.get("EZG_TOKEN"):
            return False
        self._refreshed = True
        token = self._load_token()
        if not token:
            return False
        req = urllib.request.Request(self.gateway + "/auth/refresh", data=b"{}", method="POST", headers={
            "Authorization": "Bearer " + token, "User-Agent": USER_AGENT, "Content-Type": "application/json"})
        try:
            with urllib.request.urlopen(req, timeout=HTTP_TIMEOUT) as res:
                body = res.read()
            fresh = json.loads(body.decode("utf-8")).get("access_token")
        except (OSError, ValueError, urllib.error.URLError):
            return False
        if not fresh:
            return False
        try:
            with open(self._cred_file(), "wb") as handle:
                handle.write(body)
            os.chmod(self._cred_file(), 0o600)
        except OSError:
            pass
        self._token = fresh
        return True

    def _fetch(self, rel_or_url, allow_missing=False):
        if self.is_local:
            path = rel_or_url if os.path.isabs(rel_or_url) else os.path.join(self.base, rel_or_url)
            try:
                with open(path, "rb") as handle:
                    return handle.read()
            except OSError:
                if allow_missing:
                    return None
                raise EzgError("Không đọc được %s" % path)
        url = rel_or_url if rel_or_url.startswith(("http://", "https://")) else self.base + "/" + rel_or_url
        for attempt in (0, 1):
            headers = {"User-Agent": USER_AGENT}
            if url.startswith(self.gateway) and self._load_token():
                headers["Authorization"] = "Bearer " + self._load_token()
            try:
                with urllib.request.urlopen(urllib.request.Request(url, headers=headers), timeout=HTTP_TIMEOUT) as res:
                    return res.read()
            except urllib.error.HTTPError as err:
                if err.code == 404 and allow_missing:
                    return None
                if err.code in (401, 403) and attempt == 0 and self._refresh_token():
                    continue
                if err.code in (401, 403):
                    raise EzgError(
                        "Gateway EZG từ chối (%d) — phiên đăng nhập hết hạn hoặc chưa đăng nhập. Đăng nhập lại "
                        "bằng MCP `ezg` → login_instructions (hoặc chạy lại ~/.ezg/refresh-session.sh), rồi chạy lại."
                        % err.code)
                raise EzgError("Tải %s lỗi HTTP %d" % (url, err.code))
            except (urllib.error.URLError, OSError) as err:
                raise EzgError("Không kết nối được %s (%s) — kiểm tra mạng rồi chạy lại." % (url, err))
        raise EzgError("Tải %s thất bại" % url)

    # -- catalog

    def index(self):
        if self._index is None:
            try:
                self._index = json.loads(self._fetch("index.json").decode("utf-8"))
            except ValueError as err:
                raise EzgError("index.json của catalog AI không đọc được: %s" % err)
        return self._index

    def history(self):
        """{path: [nsha, ...]} — every hash ever published for a path ({} on an older server)."""
        if self._history is None:
            raw = self._fetch("history.json", allow_missing=True)
            try:
                self._history = (json.loads(raw.decode("utf-8")).get("files") or {}) if raw else {}
            except ValueError:
                self._history = {}
        return self._history

    def blob(self, digest):
        if not digest:
            return None
        if digest not in self._blobs:
            self._blobs[digest] = self._fetch("blobs/" + digest, allow_missing=True)
        return self._blobs[digest]

    def item_files(self, item):
        """{path: bytes} of one catalog item, from its zip (sha256 verified)."""
        if item["id"] in self._items:
            return self._items[item["id"]]
        if self.is_local:
            raw = self._fetch(os.path.join("files", item.get("category", ""), item.get("fileName") or ""))
        else:
            raw = self._fetch(item["url"])
        if item.get("sha256") and hashlib.sha256(raw).hexdigest() != item["sha256"].lower():
            raise EzgError("SHA-256 của %s không khớp catalog — dừng để an toàn." % item["id"])
        files = {}
        install = item.get("installPath", "").rstrip("/")
        with zipfile.ZipFile(io.BytesIO(raw)) as archive:
            for info in archive.infolist():
                name = posix(info.filename)
                if name.endswith("/"):
                    continue
                if not (name == install or name.startswith(install + "/")) or ".." in name.split("/"):
                    continue  # same zip-slip rule as the Unity installer
                files[name] = archive.read(info)
        self._items[item["id"]] = files
        return files

    def upstream_hashes(self, item):
        hashes = item.get("fileHashes")
        if hashes:
            return dict(hashes)
        return {path: nsha(data) for path, data in self.item_files(item).items()}  # older catalog


# ----------------------------------------------------------------------------- project state

class Project:
    def __init__(self, root):
        self.root = os.path.abspath(root)
        self._lock = None
        self._pins = None

    def abs(self, rel):
        return os.path.join(self.root, *rel.split("/"))

    def exists(self, rel):
        return os.path.exists(self.abs(rel))

    def read(self, rel):
        try:
            with open(self.abs(rel), "rb") as handle:
                return handle.read()
        except OSError:
            return None

    def local_hash(self, rel):
        data = self.read(rel)
        return nsha(data) if data is not None else None

    def write(self, rel, data):
        path = self.abs(rel)
        os.makedirs(os.path.dirname(path), exist_ok=True)
        was_exec = os.path.exists(path) and os.access(path, os.X_OK)
        tmp = path + ".ezg-tmp"
        with open(tmp, "wb") as handle:
            handle.write(data)
        os.replace(tmp, path)
        if was_exec or rel.endswith(EXEC_EXT):
            mode = os.stat(path).st_mode
            os.chmod(path, mode | stat.S_IXUSR | stat.S_IXGRP | stat.S_IXOTH)

    def delete(self, rel):
        path = self.abs(rel)
        try:
            os.remove(path)
        except OSError:
            return
        stop = self.abs(".claude")
        current = os.path.dirname(path)
        while current.startswith(stop) and current != stop:
            try:
                os.rmdir(current)
            except OSError:
                break
            current = os.path.dirname(current)

    def walk(self, rel_dir):
        """Every file under a project-relative directory (junk skipped), as project-relative paths."""
        base = self.abs(rel_dir)
        out = []
        if not os.path.isdir(base):
            return out
        for current, dirs, files in os.walk(base):
            dirs[:] = [d for d in dirs if d not in JUNK_NAMES]
            for name in files:
                if name in JUNK_NAMES or name.endswith(".pyc") or name.startswith("._") or name.endswith(".ezg-tmp"):
                    continue
                out.append(posix(os.path.relpath(os.path.join(current, name), self.root)))
        return sorted(out)

    # -- lock

    def lock(self):
        if self._lock is None:
            data = read_json_file(self.abs(LOCK_REL))
            if not isinstance(data, dict):
                data = None
            self._lock = data
        return self._lock

    def has_lock(self):
        return self.lock() is not None

    def lock_or_empty(self):
        return self.lock() or {"items": {}, "files": {}}

    def save_lock(self, lock):
        current = self.lock()
        if current and current.get("items") == lock.get("items") and current.get("files") == lock.get("files"):
            return  # nothing moved — no timestamp-only diff to commit
        lock = dict(lock)
        lock["schemaVersion"] = 1
        lock["$comment"] = (
            "Baseline của các file thuộc base EZG — /update-ai và Feature Hub ghi file này, đừng sửa tay. "
            "Commit cùng repo. Phần riêng của project: CLAUDE_PROJECT.md, .claude/project/ (xem rule base-ownership).")
        lock["updatedAtUtc"] = now_utc()
        lock["items"] = dict(sorted((lock.get("items") or {}).items()))
        lock["files"] = dict(sorted((lock.get("files") or {}).items()))
        ordered = {k: lock[k] for k in ("$comment", "schemaVersion", "updatedAtUtc", "items", "files")}
        self.write(LOCK_REL, dump_json(ordered))
        self._lock = ordered

    # -- pins

    def pins(self):
        if self._pins is None:
            data = read_json_file(self.abs(PINS_REL), {}) or {}
            self._pins = data.get("pins") or {}
        return self._pins

    def save_pins(self, pins):
        data = {"$comment": "Item base EZG mà project giữ lại, /update-ai bỏ qua. Mỗi pin cần lý do.",
                "pins": dict(sorted(pins.items()))}
        self.write(PINS_REL, dump_json(data))
        self._pins = data["pins"]

    # -- Feature Hub install record (so the Unity window and MCP check_project_updates agree)

    def update_install_record(self, entries, removed_ids=()):
        path = self.abs(RECORD_REL)
        record = read_json_file(path, None)
        if not isinstance(record, dict):
            record = {"unityPackages": [], "aiItems": []}
        before = len(record.get("aiItems") or [])
        items = [i for i in (record.get("aiItems") or []) if i.get("id") not in removed_ids]
        changed = len(items) != before
        by_id = {i.get("id"): i for i in items}
        for entry in entries:
            existing = by_id.get(entry["id"])
            if existing is None:
                items.append(entry)
                by_id[entry["id"]] = entry
                changed = True
            elif existing.get("sha256") != entry["sha256"] or existing.get("files") != entry["files"]:
                existing.update(entry)
                changed = True
        if not changed:
            return  # same versions as recorded — keep installedAtUtc, no diff
        record["aiItems"] = items
        record.setdefault("unityPackages", [])
        os.makedirs(os.path.dirname(path), exist_ok=True)
        with open(path, "w", encoding="utf-8") as handle:
            handle.write(json.dumps(record, indent=2, ensure_ascii=False))


# ----------------------------------------------------------------------------- JSON 3-way merge

_MISSING = object()


def _merge_list(base, local, upstream):
    base_list = base if isinstance(base, list) else []
    removed = [x for x in base_list if x not in upstream]
    added = [x for x in upstream if x not in base_list and x not in local]
    return [x for x in local if x not in removed] + added


def merge3(base, local, upstream, path, conflicts):
    """Merge `upstream` into `local` given their common ancestor `base` (_MISSING when unknown).

    The project wins every real conflict (its value is kept and the key is reported), so a merge
    never loses something the project wrote; additions on either side are kept; lists merge like
    sets (local order kept, upstream additions appended, items upstream removed dropped).
    """
    if local == upstream:
        return local
    if base is not _MISSING and local == base:
        return upstream
    if base is not _MISSING and upstream == base:
        return local
    if isinstance(local, dict) and isinstance(upstream, dict):
        base_dict = base if isinstance(base, dict) else None
        out = {}
        for key in list(local.keys()) + [k for k in upstream.keys() if k not in local]:
            b = base_dict.get(key, _MISSING) if base_dict is not None else _MISSING
            l = local.get(key, _MISSING)
            u = upstream.get(key, _MISSING)
            sub = path + "." + key if path else key
            if l is _MISSING:
                if base_dict is not None and b is not _MISSING:
                    if u != b:
                        conflicts.append(sub + " (project đã xoá, base đã đổi)")
                    continue  # the project deleted it — respected
                out[key] = u  # new upstream key
                continue
            if u is _MISSING:
                if base_dict is not None and b is not _MISSING:
                    if l == b:
                        continue  # removed upstream, untouched locally
                    conflicts.append(sub + " (base đã xoá, project đã đổi)")
                out[key] = l  # project's own key (or kept despite upstream removal)
                continue
            out[key] = merge3(b, l, u, sub, conflicts)
        return out
    if isinstance(local, list) and isinstance(upstream, list):
        return _merge_list(base, local, upstream)
    conflicts.append(path or "(gốc)")
    return local


def merge_json_bytes(base_bytes, local_bytes, upstream_bytes):
    """Returns (merged_bytes, conflicts) or raises ValueError if local/upstream is not JSON."""
    local = json.loads(local_bytes.decode("utf-8-sig"))
    upstream = json.loads(upstream_bytes.decode("utf-8-sig"))
    base = _MISSING
    if base_bytes is not None:
        try:
            base = json.loads(base_bytes.decode("utf-8-sig"))
        except ValueError:
            base = _MISSING
    conflicts = []
    merged = merge3(base, local, upstream, "", conflicts)
    return dump_json(merged), conflicts


# ----------------------------------------------------------------------------- planning

# File actions. Safe ones are applied by a plain `apply`; the rest wait for a decision.
SAFE_ACTIONS = {"add", "update", "remove", "merge"}
STATE_LABEL = {
    "ok": "đã mới nhất",
    "add": "thêm file mới của base",
    "update": "cập nhật (project chưa sửa)",
    "remove": "base đã bỏ file này (project chưa sửa) → xoá",
    "merge": "gộp JSON (giữ phần project thêm)",
    "local-only": "project sửa tại chỗ, base không đổi → chuyển phần sửa sang override",
    "conflict": "XUNG ĐỘT: project sửa tại chỗ VÀ base cũng đổi",
    "removed-modified": "base đã bỏ file nhưng project có sửa → giữ, cần quyết",
    "extra": "file lạ trong thư mục của base (không thuộc base) → nên chuyển sang local-*",
}


def classify_file(up, loc, base, known):
    """State of one base file. up/loc/base: hashes or None; known: hashes ever published for it."""
    if up is None:
        if loc is None:
            return None
        if loc == base or loc in known:
            return "remove"
        return "removed-modified"
    if loc is None:
        return "add"
    if loc == up:
        return "ok"
    if loc == base or loc in known:
        return "update"
    if base is not None and base == up:
        return "local-only"
    return "conflict"


def infer_baseline(remote, rel, local_bytes, candidates):
    """No lock: guess which published version the project started from — the historical version
    textually closest to the local file. Returns (hash, ratio) or (None, 0)."""
    if local_bytes is None or b"\0" in local_bytes[:8000]:
        return None, 0.0
    local_lines = local_bytes.decode("utf-8", "replace").replace("\r\n", "\n").splitlines()
    best, best_ratio = None, 0.0
    for digest in candidates:
        data = remote.blob(digest)
        if data is None:
            continue
        lines = data.decode("utf-8", "replace").splitlines()
        ratio = difflib.SequenceMatcher(None, lines, local_lines, autojunk=False).ratio()
        if ratio > best_ratio:
            best, best_ratio = digest, ratio
    return best, best_ratio


def build_plan(project, remote):
    index = remote.index()
    history = remote.history()
    lock = project.lock_or_empty()
    lock_files = lock.get("files") or {}
    lock_items = lock.get("items") or {}
    pins = project.pins()

    items = []
    catalog_ids = set()
    for item in index.get("items") or []:
        iid = item["id"]
        catalog_ids.add(iid)
        install = item.get("installPath", "")
        ownership = item.get("ownership") or ("merge" if install in MERGE_FALLBACK else "base")
        entry = {"id": iid, "name": item.get("name"), "category": item.get("category"),
                 "installPath": install, "isDirectory": bool(item.get("isDirectory")),
                 "ownership": ownership, "source": item.get("source"), "sha256": item.get("sha256"),
                 "requires": item.get("requires") or [], "files": []}
        items.append(entry)

        if iid in pins:
            entry["state"] = "pinned"
            entry["pinReason"] = (pins[iid] or {}).get("reason", "")
            continue

        up_hashes = remote.upstream_hashes(item)
        locked = {p: v for p, v in lock_files.items() if v.get("item") == iid}
        present = [p for p in up_hashes if project.exists(p)] + [p for p in locked if project.exists(p)]
        if not present and not project.exists(install):
            if iid in lock_items:
                entry["state"] = "removed-by-project"  # was installed, the project deleted it
            else:
                entry["state"] = "new"
                entry["recommended"] = item.get("source") == "defaultsetup"
            continue

        paths = set(up_hashes) | set(locked)
        local_extra = []
        if entry["isDirectory"]:
            for rel in project.walk(install):
                if rel not in paths:
                    if rel in history:
                        paths.add(rel)  # a base file this version no longer ships
                    else:
                        local_extra.append(rel)
        for rel in sorted(paths):
            up = up_hashes.get(rel)
            loc = project.local_hash(rel)
            base = (locked.get(rel) or {}).get("sha256")
            known = set(history.get(rel) or [])
            inferred = False
            # Not for merge-owned JSON: re-formatting alone moves line similarity, so a guess could pick
            # the new upstream and skip the merge; an unknown base merges 2-way instead (keeps both sides).
            if (base is None and ownership != "merge" and loc is not None and up is not None and loc != up
                    and loc not in known and known):
                guess, ratio = infer_baseline(remote, rel, project.read(rel), [h for h in known if h != loc])
                if guess and ratio >= 0.5:  # below that the file is a rewrite, not an edit of a version
                    base, inferred = guess, True
            state = classify_file(up, loc, base, known)
            if state is None:
                continue
            if ownership == "merge" and state in ("conflict", "local-only"):
                state = "ok" if (base is not None and base == up) else "merge"
            entry["files"].append({"path": rel, "state": state, "upstream": up, "local": loc,
                                   "baseline": base or (loc if loc in known else None),
                                   "baselineInferred": inferred,
                                   "override": override_path_for(rel, iid) if state in ("local-only", "conflict", "removed-modified") else None})
        for rel in local_extra:
            entry["files"].append({"path": rel, "state": "extra", "upstream": None,
                                   "local": project.local_hash(rel), "baseline": None, "override": None})
        states = {f["state"] for f in entry["files"]}
        entry["state"] = ("conflict" if states & {"conflict", "removed-modified"} else
                          "local-only" if "local-only" in states else
                          "update" if states & SAFE_ACTIONS else "ok")

    orphans = sorted(i for i in lock_items if i not in catalog_ids)
    return {"items": items, "orphans": orphans, "hasLock": project.has_lock(),
            "historyAvailable": bool(history), "pins": pins}


def summarize(plan):
    counts = {}
    for entry in plan["items"]:
        if entry["state"] in ("new",):
            key = "new-recommended" if entry.get("recommended") else "new-optional"
            counts[key] = counts.get(key, 0) + 1
            continue
        if entry["state"] in ("pinned", "removed-by-project"):
            counts[entry["state"]] = counts.get(entry["state"], 0) + 1
            continue
        for f in entry["files"]:
            if f["state"] != "ok":
                counts[f["state"]] = counts.get(f["state"], 0) + 1
    return counts


def print_plan(plan, out=sys.stdout):
    counts = summarize(plan)
    w = out.write
    w("=== EZG base — trạng thái so với Feature Hub ===\n")
    if not plan["hasLock"]:
        w("(project chưa có %s — lần đầu: so với lịch sử phiên bản trên server%s)\n"
          % (LOCK_REL, "" if plan["historyAvailable"] else "; server chưa có history.json nên file khác bản mới sẽ bị coi là đã sửa"))
    if not counts:
        w("Mọi thứ đã mới nhất.\n")
    order = ["update", "add", "remove", "merge", "new-recommended", "local-only", "conflict",
             "removed-modified", "extra", "new-optional", "removed-by-project", "pinned"]
    titles = {"new-recommended": "item mới của bộ mặc định (sẽ cài)", "new-optional": "item tuỳ chọn chưa cài (chỉ liệt kê)",
              "removed-by-project": "item project đã gỡ (không cài lại)", "pinned": "item đang pin (bỏ qua)"}
    for key in order:
        if key in counts:
            w("  %-18s %4d  %s\n" % (key, counts[key], titles.get(key, STATE_LABEL.get(key, ""))))
    for entry in plan["items"]:
        if entry["state"] == "new":
            continue
        if entry["state"] == "pinned":
            w("\n[pinned] %s — %s\n" % (entry["id"], entry.get("pinReason") or "(không ghi lý do)"))
            continue
        if entry["state"] == "removed-by-project":
            w("\n[đã gỡ] %s — có trong lock nhưng không còn trên đĩa; pin nó nếu cố ý bỏ\n" % entry["id"])
            continue
        changed = [f for f in entry["files"] if f["state"] != "ok"]
        if not changed:
            continue
        w("\n%s (%s)\n" % (entry["id"], entry["ownership"]))
        for f in changed:
            line = "  %-16s %s" % (f["state"], f["path"])
            if f.get("override"):
                line += "  → override: %s" % f["override"]
            w(line + "\n")
        for req in entry.get("requires") or []:
            w("  cần package: %s\n" % req)
    new_items = [e for e in plan["items"] if e["state"] == "new"]
    if new_items:
        w("\nItem chưa cài:\n")
        for entry in new_items:
            w("  %s %s\n" % ("+" if entry.get("recommended") else "·", entry["id"]))
    if plan["orphans"]:
        w("\nItem trong lock nhưng catalog không còn (giữ nguyên, không xoá): %s\n" % ", ".join(plan["orphans"]))


# ----------------------------------------------------------------------------- applying

def apply_plan(project, remote, plan, take_upstream=(), install_ids=(), install_new=True):
    take_upstream = set(take_upstream)
    install_ids = set(install_ids)
    claude_dir = project.abs(".claude")
    tops_before = set(os.listdir(claude_dir)) if os.path.isdir(claude_dir) else set()
    index_by_id = {i["id"]: i for i in remote.index().get("items") or []}
    lock = project.lock_or_empty()
    lock_items = dict(lock.get("items") or {})
    lock_files = dict(lock.get("files") or {})
    written, deleted, kept, merged_conflicts, record_entries = [], [], [], {}, []

    unknown = take_upstream - {f["path"] for e in plan["items"] for f in e["files"]}
    if unknown:
        raise EzgError("--take-upstream: không phải file base trong plan: %s" % ", ".join(sorted(unknown)))
    missing_ids = install_ids - set(index_by_id)
    if missing_ids:
        raise EzgError("--install: catalog không có item %s" % ", ".join(sorted(missing_ids)))

    for entry in plan["items"]:
        iid = entry["id"]
        item = index_by_id[iid]
        if entry["state"] == "pinned":
            continue
        if entry["state"] in ("new", "removed-by-project"):
            wanted = iid in install_ids or (entry["state"] == "new" and install_new and entry.get("recommended"))
            if not wanted:
                continue
            files = remote.item_files(item)
            for rel, data in sorted(files.items()):
                project.write(rel, data)
                written.append(rel)
                lock_files[rel] = {"item": iid, "sha256": nsha(data)}
            lock_items[iid] = _lock_item(item, entry)
            record_entries.append(_record_entry(item, sorted(files)))
            continue

        files_up = None
        for f in entry["files"]:
            rel, state = f["path"], f["state"]
            action = state if state in SAFE_ACTIONS else None
            if rel in take_upstream and state in ("local-only", "conflict", "removed-modified", "merge"):
                action = "remove" if f["upstream"] is None else "update"
            if action is None:
                if state in ("local-only", "conflict", "removed-modified"):
                    kept.append(rel)
                    if f["upstream"] is not None:
                        # Acknowledge: the baseline becomes this upstream, so the next run reports the
                        # file as "project edited" (local-only) instead of a fresh conflict every time.
                        lock_files[rel] = {"item": iid, "sha256": f["upstream"]}
                elif state == "ok" and f["upstream"] is not None:
                    lock_files[rel] = {"item": iid, "sha256": f["upstream"]}
                continue
            if action == "remove":
                project.delete(rel)
                deleted.append(rel)
                lock_files.pop(rel, None)
                continue
            if files_up is None:
                files_up = remote.item_files(item)
            data = files_up.get(rel)
            if data is None:
                raise EzgError("Zip của %s thiếu %s — catalog lệch, publish lại rồi chạy tiếp." % (iid, rel))
            if action == "merge":
                base_bytes = remote.blob(f.get("baseline")) if f.get("baseline") else None
                try:
                    data, conflicts = merge_json_bytes(base_bytes, project.read(rel) or b"{}", data)
                except ValueError:
                    kept.append(rel)
                    merged_conflicts[rel] = ["file local không phải JSON hợp lệ — giữ nguyên"]
                    continue
                if conflicts:
                    merged_conflicts[rel] = conflicts
            lock_files[rel] = {"item": iid, "sha256": f["upstream"]}
            if data == project.read(rel):
                continue  # merge kept the file byte-identical — nothing to write or report
            project.write(rel, data)
            written.append(rel)
        for f in entry["files"]:
            if f["state"] == "extra":
                kept.append(f["path"])
        lock_items[iid] = _lock_item(item, entry)
        installed = [r for r in (project.walk(entry["installPath"]) if entry["isDirectory"] else [entry["installPath"]])
                     if project.exists(r)]
        record_entries.append(_record_entry(item, sorted(installed)))

    project.save_lock({"items": lock_items, "files": lock_files})
    project.update_install_record(record_entries)
    return {"written": written, "deleted": deleted, "kept": sorted(set(kept)),
            "mergeConflicts": merged_conflicts,
            "postSteps": post_steps(written + deleted, set(os.listdir(claude_dir)) - tops_before),
            "lock": LOCK_REL, "record": RECORD_REL}


def _lock_item(item, entry):
    return {"sha256": item.get("sha256") or "", "installPath": item.get("installPath"),
            "isDirectory": bool(item.get("isDirectory")), "ownership": entry["ownership"]}


def _record_entry(item, files):
    return {"id": item["id"], "name": item.get("name"), "category": item.get("category"),
            "sha256": item.get("sha256") or "", "installPath": item.get("installPath"),
            "files": files, "installedAtUtc": now_utc()}


def post_steps(paths, new_top_dirs=()):
    steps = []
    if new_top_dirs:
        steps.append("bootstrap")  # a new top-level .claude/ folder needs its .agents/ link
    if any(h in p for p in paths for h in UI_PIPELINE_HINTS):
        steps.append("ui-kit-sync")
    if any(p.startswith(".claude/scripts/") or p.startswith(".claude/skills/run-backlog/") for p in paths):
        steps.append("unittest")
    if any(p in (".claude/settings.json", ".mcp.json") for p in paths):
        steps.append("restart-session")
    return steps


# ----------------------------------------------------------------------------- lock maintenance

def iter_template_files(template_root):
    """(project-relative path, bytes) for every base file in a DefaultSetup/ folder."""
    for current, dirs, files in os.walk(template_root):
        dirs[:] = [d for d in dirs if d not in JUNK_NAMES and d != ".agents"]
        for name in files:
            if name in JUNK_NAMES or name.endswith(".pyc") or name.startswith("._"):
                continue
            rel = posix(os.path.relpath(os.path.join(current, name), template_root))
            if rel.startswith("ProjectSettings/") or is_project_owned(rel) or rel == LOCK_REL:
                continue
            if item_id_for(rel) is None:
                continue
            with open(os.path.join(current, name), "rb") as handle:
                yield rel, handle.read()


def lock_from_dir(project, template_root):
    """Refresh the lock from a local template: a file whose content equals the template's gets the
    template hash as baseline; a file that differs keeps whatever baseline it had."""
    lock = project.lock_or_empty()
    lock_items = dict(lock.get("items") or {})
    lock_files = dict(lock.get("files") or {})
    count = 0
    for rel, data in iter_template_files(template_root):
        iid = item_id_for(rel)
        digest = nsha(data)
        if project.local_hash(rel) != digest:
            continue
        lock_files[rel] = {"item": iid, "sha256": digest}
        install = install_path_for(rel)
        lock_items.setdefault(iid, {"sha256": "", "installPath": install, "isDirectory": install != rel,
                                    "ownership": "merge" if rel in MERGE_FALLBACK else "base"})
        count += 1
    project.save_lock({"items": lock_items, "files": lock_files})
    return count


def lock_from_catalog(project, remote):
    """First lock for a project with none: baseline = the local hash wherever it matches a published
    version; a file edited in place gets no baseline (status will ask about it)."""
    history = remote.history()
    lock_items, lock_files = {}, {}
    for item in remote.index().get("items") or []:
        up_hashes = remote.upstream_hashes(item)
        matched = False
        for rel, up in up_hashes.items():
            loc = project.local_hash(rel)
            if loc is None:
                continue
            if loc == up or loc in set(history.get(rel) or []):
                lock_files[rel] = {"item": item["id"], "sha256": loc}
                matched = True
        if matched:
            install = item.get("installPath", "")
            lock_items[item["id"]] = {"sha256": "", "installPath": install, "isDirectory": bool(item.get("isDirectory")),
                                      "ownership": item.get("ownership") or ("merge" if install in MERGE_FALLBACK else "base")}
    project.save_lock({"items": lock_items, "files": lock_files})
    return len(lock_files)


def modified_files(project, include_merge=False):
    """Base files whose content differs from the lock baseline (offline). Merge-owned files (a project
    legitimately edits them) only with include_merge — what a plain copier (builder, agents-update)
    must not overwrite either."""
    lock = project.lock()
    if not lock:
        return []
    merge_items = {i for i, v in (lock.get("items") or {}).items() if v.get("ownership") == "merge"}
    out = []
    for rel, meta in sorted((lock.get("files") or {}).items()):
        if not include_merge and (meta.get("item") in merge_items or rel in MERGE_FALLBACK):
            continue
        loc = project.local_hash(rel)
        if loc is not None and loc != meta.get("sha256"):
            out.append(rel)
    return out


# ----------------------------------------------------------------------------- hooks

def guard_decision(root, file_path, cwd=None):
    """Reason string when an edit of `file_path` must be denied, else None."""
    if not file_path:
        return None
    project = Project(root)
    lock = project.lock()
    if not lock:
        return None
    target = file_path if os.path.isabs(file_path) else os.path.join(cwd or root, file_path)
    real_root = os.path.realpath(root)
    real = os.path.realpath(target)
    if not os.path.exists(real):
        # A new file: resolve the deepest existing parent (follows the .agents/ link view).
        parent, tail = os.path.dirname(target), [os.path.basename(target)]
        while parent and not os.path.exists(parent) and os.path.dirname(parent) != parent:
            tail.insert(0, os.path.basename(parent))
            parent = os.path.dirname(parent)
        real = os.path.join(os.path.realpath(parent), *tail)
    rel = posix(os.path.relpath(real, real_root))
    if rel.startswith("../") or rel == "..":
        return None
    if rel.startswith(".agents/"):
        rel = ".claude/" + rel[len(".agents/"):]
    if rel == LOCK_REL:
        return ("%s do /update-ai và Feature Hub quản lý — đừng sửa tay. Muốn cập nhật base: chạy /update-ai." % LOCK_REL)
    if is_project_owned(rel):
        return None
    items = lock.get("items") or {}
    meta = (lock.get("files") or {}).get(rel)
    if meta:
        if (items.get(meta.get("item")) or {}).get("ownership") == "merge" or rel in MERGE_FALLBACK:
            return None
        return _deny_message(rel, meta.get("item"))
    for iid, info in items.items():
        install = info.get("installPath") or ""
        if info.get("isDirectory") and info.get("ownership") != "merge" and rel.startswith(install + "/"):
            return _deny_message(rel, iid, new_file=True)
    return None


def _deny_message(rel, item_id, new_file=False):
    override = override_path_for(rel, item_id)
    lines = ["`%s` thuộc base EZG (item %s) — project KHÔNG sửa%s file base: lần cập nhật sau sẽ ghi đè."
             % (rel, item_id, " / thêm file vào thư mục" if new_file else "")]
    if override:
        lines.append("Ghi phần riêng của project vào `%s` (tạo nếu chưa có; override thắng bản base khi mâu thuẫn), "
                     "rồi tiếp tục task." % override)
    else:
        lines.append("File này là code/config dùng chung, không override bằng markdown được: báo lỗi/đề xuất cho người "
                     "bảo trì base (sửa ở monorepo ezg-packages rồi /publish-ai-features), hoặc pin item bằng "
                     "`/update-ai` nếu project buộc phải giữ bản vá riêng.")
    lines.append("Skill/command/agent/rule riêng của project: đặt tên `local-<tên>`. Quy ước đầy đủ: rule base-ownership.")
    lines.append("Đang bảo trì base thật sự: sửa trong monorepo ezg-packages, hoặc mở Claude với EZG_BASE_EDIT=1.")
    return "\n".join(lines)


def cmd_guard(_args):
    try:
        if os.environ.get("EZG_BASE_EDIT") == "1":
            return 0
        raw = sys.stdin.read()
        event = json.loads(raw) if raw.strip() else {}
        tool_input = event.get("tool_input") or {}
        file_path = tool_input.get("file_path") or tool_input.get("notebook_path")
        start = os.environ.get("CLAUDE_PROJECT_DIR") or event.get("cwd") or os.getcwd()
        root = find_root(start)
        if not root or not file_path:
            return 0
        reason = guard_decision(root, file_path, cwd=event.get("cwd") or start)
        if reason:
            sys.stdout.write(json.dumps({"hookSpecificOutput": {
                "hookEventName": "PreToolUse", "permissionDecision": "deny",
                "permissionDecisionReason": reason}}, ensure_ascii=False))
    except Exception:  # noqa: BLE001 — a guard must never break the session
        return 0
    return 0


def session_context_text(root):
    project = Project(root)
    lines = []
    overrides = project.walk(OVERRIDES_REL)
    if overrides:
        lines.append("[EZG base] Project có override cho base — trước khi làm theo skill/agent/rule/doc dưới đây, "
                     "đọc file override của nó; override thắng bản base khi mâu thuẫn:")
        for rel in overrides:
            short = rel[len(OVERRIDES_REL) + 1:]
            lines.append("- %s → %s" % (short[:-3] if short.endswith(".md") else short, rel))
    pins = project.pins()
    if pins:
        lines.append("[EZG base] Item đang pin (không cập nhật): %s" % ", ".join(sorted(pins)))
    edited = modified_files(project)
    if edited:
        lines.append("[EZG base] %d file base đang bị sửa tại chỗ (vd %s) — chạy /update-ai để chuyển phần sửa sang override."
                     % (len(edited), edited[0]))
    return "\n".join(lines)


def cmd_session_context(_args):
    try:
        root = find_root(os.environ.get("CLAUDE_PROJECT_DIR") or os.getcwd())
        if root:
            text = session_context_text(root)
            if text:
                sys.stdout.write(text + "\n")
    except Exception:  # noqa: BLE001
        return 0
    return 0


# ----------------------------------------------------------------------------- diff / show

def _resolve(project, remote, rel):
    plan = build_plan(project, remote)
    for entry in plan["items"]:
        for f in entry["files"]:
            if f["path"] == rel:
                return entry, f
    raise EzgError("%s không phải file base cần xử lý (status không liệt kê nó)." % rel)


def _content(project, remote, entry, f, which):
    if which == "local":
        return project.read(f["path"])
    if which == "upstream":
        if f["upstream"] is None:
            return None
        item = next(i for i in remote.index()["items"] if i["id"] == entry["id"])
        return remote.item_files(item).get(f["path"])
    return remote.blob(f.get("baseline")) if f.get("baseline") else None


def _text(data):
    return [] if data is None else data.decode("utf-8", "replace").replace("\r\n", "\n").splitlines(True)


def cmd_diff(args, project, remote):
    entry, f = _resolve(project, remote, posix(args.path))
    base = _content(project, remote, entry, f, "baseline")
    local = _content(project, remote, entry, f, "local")
    upstream = _content(project, remote, entry, f, "upstream")
    out = sys.stdout
    out.write("# %s — %s (item %s)\n" % (f["path"], f["state"], entry["id"]))
    if f.get("override"):
        out.write("# override: %s\n" % f["override"])
    if base is None:
        out.write("\n## Không tìm được bản gốc (baseline) — diff 2 chiều upstream → local\n")
        out.writelines(difflib.unified_diff(_text(upstream), _text(local), "upstream", "local"))
        return 0
    if f.get("baselineInferred"):
        out.write("# (chưa có lock — baseline là bản base từng publish gần với file local nhất)\n")
    out.write("\n## Project đã đổi gì (baseline → local)\n")
    out.writelines(difflib.unified_diff(_text(base), _text(local), "baseline", "local"))
    out.write("\n## Base đã đổi gì (baseline → upstream)\n")
    out.writelines(difflib.unified_diff(_text(base), _text(upstream), "baseline", "upstream"))
    return 0


def cmd_show(args, project, remote):
    entry, f = _resolve(project, remote, posix(args.path))
    data = _content(project, remote, entry, f, args.which)
    if data is None:
        raise EzgError("Không có bản %s của %s." % (args.which, f["path"]))
    sys.stdout.write(data.decode("utf-8", "replace"))
    return 0


# ----------------------------------------------------------------------------- CLI

def _project(args):
    root = args.root or find_root(os.getcwd())
    if not root or not os.path.isdir(os.path.join(root, ".claude")):
        raise EzgError("Không tìm thấy project (thư mục có .claude/) — chạy trong project game hoặc truyền --root.")
    return Project(root)


def main(argv=None):
    parser = argparse.ArgumentParser(prog="ezg_base.py", description=__doc__.split("\n\n")[0])
    parser.add_argument("--root")
    parser.add_argument("--source")
    sub = parser.add_subparsers(dest="cmd", required=True)
    p = sub.add_parser("status")
    p.add_argument("--json", action="store_true")
    p = sub.add_parser("apply")
    p.add_argument("--take-upstream", nargs="*", default=[])
    p.add_argument("--install", nargs="*", default=[])
    p.add_argument("--no-new", action="store_true")
    p.add_argument("--json", action="store_true")
    p = sub.add_parser("diff")
    p.add_argument("path")
    p = sub.add_parser("show")
    p.add_argument("path")
    p.add_argument("--which", choices=("baseline", "upstream", "local"), required=True)
    p = sub.add_parser("pin")
    p.add_argument("id")
    p.add_argument("--reason", required=True)
    p = sub.add_parser("unpin")
    p.add_argument("id")
    p = sub.add_parser("lock-init")
    p.add_argument("--from-dir")
    p.add_argument("--force", action="store_true")
    p = sub.add_parser("modified")
    p.add_argument("--json", action="store_true")
    p.add_argument("--include-merge", action="store_true")
    sub.add_parser("guard")
    sub.add_parser("session-context")
    args = parser.parse_args(argv)

    if args.cmd == "guard":
        return cmd_guard(args)
    if args.cmd == "session-context":
        return cmd_session_context(args)

    try:
        project = _project(args)
        if args.cmd == "modified":
            files = modified_files(project, include_merge=args.include_merge)
            sys.stdout.write(json.dumps(files) + "\n" if args.json else "".join(f + "\n" for f in files))
            return 0
        if args.cmd == "pin":
            pins = dict(project.pins())
            pins[args.id] = {"reason": args.reason, "since": today()}
            project.save_pins(pins)
            print("Đã pin %s — /update-ai sẽ bỏ qua item này." % args.id)
            return 0
        if args.cmd == "unpin":
            pins = dict(project.pins())
            if pins.pop(args.id, None) is None:
                raise EzgError("%s không có trong %s" % (args.id, PINS_REL))
            project.save_pins(pins)
            print("Đã bỏ pin %s." % args.id)
            return 0
        if args.cmd == "lock-init":
            if args.from_dir:
                count = lock_from_dir(project, os.path.abspath(args.from_dir))
                print("Lock cập nhật từ %s: %d file khớp template." % (args.from_dir, count))
                return 0
            if project.has_lock() and not args.force:
                print("Đã có %s — giữ nguyên (thêm --force để tạo lại)." % LOCK_REL)
                return 0
            count = lock_from_catalog(project, Remote(args.source))
            print("Đã tạo %s: %d file khớp một phiên bản đã publish." % (LOCK_REL, count))
            return 0

        remote = Remote(args.source)
        if args.cmd == "status":
            plan = build_plan(project, remote)
            if args.json:
                plan = dict(plan, summary=summarize(plan))
                sys.stdout.write(json.dumps(plan, ensure_ascii=False, indent=1) + "\n")
            else:
                print_plan(plan)
            return 0
        if args.cmd == "apply":
            plan = build_plan(project, remote)
            result = apply_plan(project, remote, plan, take_upstream=[posix(p) for p in args.take_upstream],
                                install_ids=args.install, install_new=not args.no_new)
            if args.json:
                sys.stdout.write(json.dumps(result, ensure_ascii=False, indent=1) + "\n")
            else:
                print("Ghi %d file, xoá %d file, giữ nguyên %d file sửa local." % (
                    len(result["written"]), len(result["deleted"]), len(result["kept"])))
                for rel, conflicts in result["mergeConflicts"].items():
                    print("  gộp %s — giữ giá trị của project ở: %s" % (rel, "; ".join(conflicts)))
                for rel in result["kept"]:
                    print("  giữ  %s" % rel)
                if result["postSteps"]:
                    print("Bước tiếp: %s" % ", ".join(result["postSteps"]))
            return 0
        if args.cmd == "diff":
            return cmd_diff(args, project, remote)
        if args.cmd == "show":
            return cmd_show(args, project, remote)
    except EzgError as err:
        sys.stderr.write("LỖI: %s\n" % err)
        return 1
    return 0


if __name__ == "__main__":
    sys.exit(main())
