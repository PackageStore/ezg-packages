#!/usr/bin/env python3
"""Shared helpers for the spec-first UI mockup pipeline."""

from __future__ import annotations

import copy
import hashlib
import html
import json
import re
from pathlib import Path

# Every importer of this module lives in the same scripts/ directory, which is
# therefore already on sys.path — the same plain import ui-kit-sync.py uses.
from project_profile import profile


ROOT = Path(__file__).resolve().parents[2]
KIT_JSON = ROOT / ".claude" / "ui-kit" / "ui-kit.json"
KIT_CSS = ROOT / ".claude" / "ui-kit" / "ui-kit.css"
# Must resolve to the SAME directory ui-kit-sync.py extracted the kit from:
# kit_source_hash() below re-hashes those prefabs and compares against the
# `_meta.sourceHash` the extractor recorded. Reading the profile in one script
# and hardcoding the path in the other makes every validation in a project with
# a different layout report `kit_stale` forever, because this side would hash an
# empty directory.
TEMPLATES_DIR = ROOT / profile().ui_templates_root
PREFABS_ROOT = TEMPLATES_DIR.parent
SPEC_RE = re.compile(
    r"<script\s+[^>]*id=[\"']spec[\"'][^>]*>(.*?)</script>",
    re.IGNORECASE | re.DOTALL,
)
PATCH_ROOTS = {"containers", "elements", "wiring", "assumptions"}
PATCH_OPS = {"add", "remove", "replace"}

# Chuẩn canvas EZG: portrait 1080×2400 (khớp CanvasScaler của screen_template). Spec cũ vẽ ở
# 1080×1920 vẫn hợp lệ (đã duyệt thì không bắt vẽ lại) — chỉ draft mới phải theo design_resolution().
DEFAULT_DESIGN_RESOLUTION = [1080, 2400]
LEGACY_DESIGN_RESOLUTION = [1080, 1920]
_REFERENCE_RESOLUTION_RE = re.compile(
    r"m_ReferenceResolution:\s*\{x:\s*([\d.]+),\s*y:\s*([\d.]+)\}"
)


def _portrait_resolution(value) -> list[int] | None:
    """[w, h] số nguyên dương, dọc (w < h); không hợp lệ → None."""
    if not isinstance(value, (list, tuple)) or len(value) != 2:
        return None
    if not all(isinstance(v, (int, float)) and not isinstance(v, bool) for v in value):
        return None
    width, height = (int(round(v)) for v in value)
    if width <= 0 or height <= 0 or width >= height:
        return None
    return [width, height]


# Script pipeline phải tự theo designResolution của spec thì mới dùng được canvas khác 1080×1920. Project cập
# nhật lẻ ui_spec_common.py (Feature Hub cài từng file) mà ui-review / ui-spec-render / ... còn bản cũ ghi cứng
# 1080×1920 → PNG đóng băng bị cắt. Khi đó cả pipeline giữ hành vi cũ cho tới khi cập nhật đủ nhóm.
_RESOLUTION_AWARE_SCRIPTS = {
    "ui-review.py": "spec_resolution",
    "ui-spec-render.py": "spec_resolution",
    "ui-build-report.py": "spec_resolution",
    "ui-visual-diff.py": "supported_resolutions",
    "ui-kit-sync.py": "design_resolution",
}


def _pipeline_supports_design_resolution() -> bool:
    here = Path(__file__).resolve().parent
    for name, marker in _RESOLUTION_AWARE_SCRIPTS.items():
        path = here / name
        try:
            if path.exists() and marker not in path.read_text(encoding="utf-8", errors="ignore"):
                return False
        except OSError:
            return False
    return True


def design_resolution() -> list[int]:
    """Độ phân giải thiết kế của project: profile `designResolution` → CanvasScaler của
    screen_template.prefab → DEFAULT_DESIGN_RESOLUTION. Mọi script pipeline mockup đọc từ đây.
    Script pipeline còn bản cũ (cập nhật lẻ) → LEGACY 1080×1920."""
    if not _pipeline_supports_design_resolution():
        return list(LEGACY_DESIGN_RESOLUTION)
    configured = _portrait_resolution(profile().get("designResolution"))
    if configured:
        return configured
    try:
        candidates = sorted(TEMPLATES_DIR.rglob("screen_template.prefab"))
    except OSError:
        candidates = []
    for candidate in candidates:
        try:
            match = _REFERENCE_RESOLUTION_RE.search(candidate.read_text(encoding="utf-8", errors="ignore"))
        except OSError:
            continue
        detected = _portrait_resolution([float(match.group(1)), float(match.group(2))]) if match else None
        if detected:
            return detected
    return list(DEFAULT_DESIGN_RESOLUTION)


def supported_resolutions() -> list[list[int]]:
    """Mọi designResolution validator chấp nhận: của project + chuẩn EZG + legacy 1080×1920."""
    if not _pipeline_supports_design_resolution():
        return [list(LEGACY_DESIGN_RESOLUTION)]
    result = [design_resolution()]
    for value in (DEFAULT_DESIGN_RESOLUTION, LEGACY_DESIGN_RESOLUTION):
        if value not in result:
            result.append(list(value))
    return result


def spec_resolution(spec: dict) -> list[int]:
    """designResolution của một spec (đã validate); thiếu/hỏng → legacy 1080×1920 như trước."""
    value = _portrait_resolution(spec.get("designResolution")) if isinstance(spec, dict) else None
    return value or list(LEGACY_DESIGN_RESOLUTION)


class UISpecError(ValueError):
    pass


def canonical_json(value) -> str:
    return json.dumps(value, ensure_ascii=False, indent=2, sort_keys=True) + "\n"


def sha256_text(value: str) -> str:
    return hashlib.sha256(value.encode("utf-8")).hexdigest()


def spec_hash(spec: dict) -> str:
    return sha256_text(canonical_json(spec))


def kit_hash() -> str:
    if not KIT_JSON.exists() or not KIT_CSS.exists():
        raise UISpecError("UI kit missing; run python3 .claude/scripts/ui-kit-sync.py")
    return sha256_text(
        KIT_JSON.read_text(encoding="utf-8")
        + "\n"
        + KIT_CSS.read_text(encoding="utf-8")
    )


def kit_source_hash() -> str:
    # Phải băm ĐÚNG tập file mà ui-kit-sync.py source_hash() băm: quét đệ quy (rglob).
    # Thư viện template chia thư mục con (Button_Template/, Popup_Template/…); quét phẳng
    # thì hash luôn lệch `_meta.sourceHash` và mọi spec v1 bị báo kit_stale dù kit vừa sinh.
    digest = hashlib.sha256()
    for path in sorted(TEMPLATES_DIR.rglob("*.prefab")):
        digest.update(str(path.relative_to(ROOT)).encode("utf-8"))
        digest.update(b"\0")
        digest.update(path.read_bytes())
        meta = path.with_suffix(path.suffix + ".meta")
        if meta.exists():
            digest.update(meta.read_bytes())
        digest.update(b"\0")
    return digest.hexdigest()


def embedded_spec(html_path: Path) -> dict:
    text = html_path.read_text(encoding="utf-8")
    match = SPEC_RE.search(text)
    if not match:
        raise UISpecError(f"{html_path}: missing <script id=\"spec\">")
    try:
        return json.loads(html.unescape(match.group(1)).strip())
    except json.JSONDecodeError as exc:
        raise UISpecError(f"{html_path}: invalid embedded spec JSON: {exc}") from exc


def sidecar_for(html_path: Path) -> Path:
    return html_path.with_suffix(".ui-spec.json")


def load_spec(path: Path, prefer_sidecar: bool = True) -> tuple[dict, Path, dict | None]:
    """Return (authoritative spec, source path, embedded spec when HTML input)."""
    path = path.resolve()
    if path.suffix.lower() == ".html":
        embedded = embedded_spec(path)
        sidecar = sidecar_for(path)
        if prefer_sidecar and sidecar.exists():
            try:
                candidate = json.loads(sidecar.read_text(encoding="utf-8"))
                # Only v1 sidecars are authoritative. Extracted v0 sidecars are
                # migration aids; legacy HTML remains the source to avoid drift.
                if candidate.get("specVersion", 0) >= 1:
                    return candidate, sidecar, embedded
            except json.JSONDecodeError as exc:
                raise UISpecError(f"{sidecar}: invalid JSON: {exc}") from exc
        return embedded, path, embedded
    try:
        return json.loads(path.read_text(encoding="utf-8")), path, None
    except json.JSONDecodeError as exc:
        raise UISpecError(f"{path}: invalid JSON: {exc}") from exc


def load_templates() -> set[str]:
    try:
        payload = json.loads(KIT_JSON.read_text(encoding="utf-8"))
    except FileNotFoundError as exc:
        raise UISpecError("UI kit missing; run python3 .claude/scripts/ui-kit-sync.py") from exc
    return set(payload.get("templates", {}))


def issue(code: str, path: str, message: str) -> dict:
    return {"code": code, "path": path, "message": message}


def _is_number(value) -> bool:
    return isinstance(value, (int, float)) and not isinstance(value, bool)


def _valid_vec(value, count: int, positive: bool = False) -> bool:
    if not isinstance(value, list) or len(value) != count:
        return False
    if not all(_is_number(v) for v in value):
        return False
    return not positive or all(v >= 0 for v in value)


def _pointer_parts(path: str) -> list[str]:
    if not isinstance(path, str) or not path.startswith("/"):
        raise ValueError("Patch path must be a JSON pointer")
    parts = [part.replace("~1", "/").replace("~0", "~") for part in path[1:].split("/")]
    if not parts or parts[0] not in PATCH_ROOTS:
        raise ValueError(f"Patch path must stay under: {', '.join(sorted(PATCH_ROOTS))}")
    return parts


def _list_index(value: str, items: list, *, allow_end: bool = False) -> int:
    size = len(items)
    if value == "-" and allow_end:
        return size
    if value.startswith("@"):
        node_id = value[1:]
        matches = [index for index, item in enumerate(items) if isinstance(item, dict) and item.get("id") == node_id]
        if len(matches) != 1:
            raise ValueError(f"Expected one node with id {node_id!r}, found {len(matches)}")
        return matches[0]
    if not value.isdigit():
        raise ValueError(f"Invalid list index: {value!r}")
    index = int(value)
    upper = size if allow_end else size - 1
    if index < 0 or index > upper:
        raise ValueError(f"List index out of range: {index}")
    return index


def apply_json_patch(document: dict, operations: list[dict]) -> dict:
    """Apply the restricted patch dialect used by deterministic review choices."""
    if not isinstance(operations, list) or len(operations) > 100:
        raise ValueError("Option patch must contain at most 100 operations")
    result = copy.deepcopy(document)
    for operation in operations:
        if not isinstance(operation, dict) or operation.get("op") not in PATCH_OPS:
            raise ValueError("Patch operations must use add, remove, or replace")
        op = operation["op"]
        parts = _pointer_parts(operation.get("path"))
        parent = result
        for part in parts[:-1]:
            if isinstance(parent, list):
                parent = parent[_list_index(part, parent)]
            elif isinstance(parent, dict) and part in parent:
                parent = parent[part]
            else:
                raise ValueError(f"Patch path does not exist: {operation['path']}")
        leaf = parts[-1]
        if isinstance(parent, list):
            index = _list_index(leaf, parent, allow_end=op == "add")
            if op == "add":
                if "value" not in operation:
                    raise ValueError("add requires value")
                parent.insert(index, copy.deepcopy(operation["value"]))
            elif op == "replace":
                if "value" not in operation:
                    raise ValueError("replace requires value")
                parent[index] = copy.deepcopy(operation["value"])
            else:
                parent.pop(index)
        elif isinstance(parent, dict):
            if op == "add":
                if "value" not in operation:
                    raise ValueError("add requires value")
                parent[leaf] = copy.deepcopy(operation["value"])
            elif op == "replace":
                if leaf not in parent or "value" not in operation:
                    raise ValueError("replace requires an existing path and value")
                parent[leaf] = copy.deepcopy(operation["value"])
            else:
                if leaf not in parent:
                    raise ValueError("remove requires an existing path")
                del parent[leaf]
        else:
            raise ValueError(f"Patch parent is not a container: {operation['path']}")
    return result


ANCHORS = {
    "top-left", "top-center", "top-right",
    "middle-left", "center", "middle-right",
    "bottom-left", "bottom-center", "bottom-right", "stretch",
}

# Containers are invisible layout only. A visible panel/card/frame must be an
# element mapping to a frame template (FrameTemplate / FrameTemplateInside /
# LayoutTemplate) anchored stretch — never CSS styling on the container. The
# /new-ui builder cannot turn a styled container into a real framed sprite; it
# emits a raw recoloured Image instead (the DungeonGuide "backgrounds bị đổi màu"
# + "không dùng FrameTemplate" defect). Popup outer frame comes from the base
# template chrome, not a redrawn container background.
CONTAINER_FRAME_KEYS = (
    "border", "borderTop", "borderBottom", "borderLeft", "borderRight", "boxShadow",
)
# Fixed-size chrome widgets: two instances at different sizes reads as uneven UI
# (the Dungeon "TimeLayoutTemplate kích thước không đồng đều" defect).
CHROME_TEMPLATES = {
    "TimeLayoutTemplate", "TimeLayoutTemplate_small", "ResourceHomeTemplate",
    "ResourceViewTemplate", "CurrencyPreview", "GameNotification",
}

# A scroll region is a CONTAINER property, not an element: ScrollViewTemplate /
# ScrollLoopTemplate own their own Viewport/Content subtree, and spec elements
# cannot nest children, so placing one as an element strands the scrolled body
# outside it. Marking the container instead tells /new-ui to instantiate the
# template and drop the container's children into Viewport/Content. Without this
# field a scrolling area is invisible in the spec and the builder hand-adds a
# raw ScrollRect (the StageOverview / task 064 defect).
SCROLL_TEMPLATES = {"ScrollViewTemplate", "ScrollLoopTemplate"}
SCROLL_MODES = (False, "vertical", "horizontal", "loop")

# A tab bar is a CONTAINER property too. FeatureTemplate ships FullScreen/Bot/
# TabBottomTemplate (Image + ToggleGroup + HorizontalLayoutGroup +
# UI_TabExtensions) already holding the toggle row, but inactive — a feature with
# tabs activates it and parents its TabToggle* instances to it, then wires
# UI_TabExtensions (_toggleList ↔ _objectList). Spec elements hold no children,
# so a TabBottomTemplate element cannot host the toggles: they end up in a
# hand-made row inside Mid while the real bar stays dead and the controller
# hand-wires Toggle.onValueChanged (the DungeonGuide defect).
TAB_BAR_TEMPLATES = {"TabBottomTemplate"}
TAB_TOGGLE_TEMPLATES = {"TabToggleIconTemplate", "TabToggleTextTemplate"}

# A titled info block is a CONTAINER property, same reasoning as scroll/tabBar: the
# block is a FrameTemplateInside instance whose ButtonTitleTemplate pill straddles the
# frame's top edge, and both must WRAP the block's body — which spec elements cannot do.
# Drawn by hand it degrades into a styled container plus a loose label, and the /new-ui
# builder then ships a flat wall of text with no framing (the DungeonGuide storyboard
# defect). Precedent: StageOverview.prefab, DungeonGuide.prefab.
SECTION_FRAME_TEMPLATES = {"FrameTemplate", "FrameTemplateInside", "LayoutTemplate"}
SECTION_TITLE_TEMPLATES = {"ButtonTitleTemplate"}
# The pill hangs 30px above the frame's top edge, so the list stacking the sections needs
# at least that much clearance or each title lands on the block above it, and the frame
# itself must reserve the other 30px plus breathing room before its body starts.
SECTION_MIN_PARENT_GAP = 40
SECTION_PILL_RESERVE = 50


def _is_visible_background(value) -> bool:
    """A container background reads as a visible panel unless it is fully transparent."""
    if value is None:
        return False
    if not isinstance(value, str):
        return True
    text = value.strip().lower()
    if text in ("", "transparent", "none"):
        return False
    if text.startswith("rgba(") and text.endswith(")"):
        parts = [part.strip() for part in text[5:-1].split(",")]
        if len(parts) == 4:
            try:
                return float(parts[3]) != 0
            except ValueError:
                return True
    return True


def _container_frame_offenders(style: dict) -> list[str]:
    """Return the visible-framing style keys a container must not carry."""
    offenders = [
        key for key in CONTAINER_FRAME_KEYS
        if key in style and style[key] not in (None, "", "none")
    ]
    if "background" in style and _is_visible_background(style["background"]):
        offenders.append("background")
    return sorted(offenders)


# --- Polish gate (PolishRules §9) ------------------------------------------------------------
# Chỉ những luật ĐO ĐƯỢC trên spec: cỡ chữ, touch target, số CTA chính, cặp nút, lưới khoảng cách,
# chỗ cho bản dịch dài. Ngưỡng đọc từ profile `polish` (mặc định = PolishRules trên canvas 1080 ngang).
# Mức "block" chặn draft/approve khi specGate = block; build chỉ cảnh báo (spec đã duyệt).
POLISH_DEFAULTS = {
    "specGate": "block",
    "minFontSize": 32,
    "hardMinFontSize": 30,
    "minTouchTarget": 128,
    "ctaHeight": [152, 176],
    "gridStep": 8,
}
POLISH_GATES = ("block", "warn", "off")
# Vai trò nút (element.role). ACTION_ROLES là nhóm "cặp nút hành động" phải nằm ngang.
BUTTON_ROLES = {"primary", "secondary", "ads", "cancel", "close", "back", "nav", "tab", "item", "locked"}
ACTION_ROLES = {"primary", "secondary", "cancel", "ads"}
# Màn chỉ dev thấy (cheat/admin/debug) — người chơi không thấy, không chấm polish (như Phase D D0).
DEV_ONLY_RE = re.compile(r"cheat|admin|debug", re.IGNORECASE)
# Ước lượng bề ngang chữ: ~0.55 em mỗi ký tự, chừa 30% cho bản dịch dài (PolishRules §9.2).
TEXT_EM_PER_CHAR = 0.55
TEXT_LOCALIZE_ROOM = 1.3
TEXT_LINE_HEIGHT = 1.25


def _polish_settings() -> dict:
    """Ngưỡng polish đã merge với profile; giá trị hỏng rơi về mặc định thay vì làm sập validator."""
    try:
        raw = profile().get("polish") or {}
    except Exception:  # noqa: BLE001 — profile hỏng đã được project_profile báo to ở chỗ khác
        raw = {}
    if isinstance(raw, str) and raw in POLISH_GATES:
        raw = {"specGate": raw}  # viết tắt "polish": "off" | "warn" | "block"
    settings = dict(POLISH_DEFAULTS)
    for key, default in POLISH_DEFAULTS.items():
        value = raw.get(key, default) if isinstance(raw, dict) else default
        if key == "specGate":
            settings[key] = value if value in POLISH_GATES else default
        elif key == "ctaHeight":
            ok = isinstance(value, list) and len(value) == 2 and all(_is_number(v) and v > 0 for v in value)
            settings[key] = list(value) if ok and value[0] <= value[1] else list(default)
        else:
            settings[key] = value if _is_number(value) and value > 0 else default
    return settings


def _is_interactive(row: dict) -> bool:
    """Element là thứ người chơi bấm: khai `interactive`/`role`, hoặc template họ button/toggle."""
    if row.get("interactive") is False:
        return False
    role = row.get("role")
    if row.get("interactive") is True or (isinstance(role, str) and role in BUTTON_ROLES):
        return True
    template = row.get("template")
    if not isinstance(template, str) or template in SECTION_TITLE_TEMPLATES:
        return False
    name = template.lower()
    if name.startswith("text") or "cheat" in name:
        return False  # nhãn chữ kiểu text_button_CTA không phải vùng bấm; nút cheat là chrome dev-only
    return "button" in name or "btn" in name or "toggle" in name


def _polish_issues(spec, containers, elements, container_map, element_map, declared_parent, ids, kit_records):
    """Danh sách (level, issue) cho các luật polish đo được trên spec. level = block | warn."""
    settings = _polish_settings()
    if settings["specGate"] == "off":
        return []
    name = f"{spec.get('screen', '')} {spec.get('feature', '')}"
    if DEV_ONLY_RE.search(name):
        return []
    found: list[tuple[str, dict]] = []

    def path_of(node_id, suffix=""):
        return ids.get(node_id, "$") + suffix

    # Node nằm trong list lặp (scroll / grid): mỗi ô có nút riêng là bình thường, không tính CTA màn.
    repeated = {
        row["id"] for row in containers
        if isinstance(row, dict) and isinstance(row.get("id"), str)
        and (row.get("scroll") or row.get("type") == "grid")
    }

    def in_repeated(node_id) -> bool:
        seen, current = set(), node_id
        while current in declared_parent and current not in seen:
            seen.add(current)
            current = declared_parent[current]
            if current in repeated:
                return True
        return False

    def is_template_chrome(node_id, row) -> bool:
        """Chrome của base template (baseChrome) hoặc nằm trong CheatMenu — template quyết cỡ, không chấm."""
        if row.get("baseChrome") or "cheat" in str(row.get("template", "")).lower():
            return True
        seen, current = set(), node_id
        while current in declared_parent and current not in seen:
            seen.add(current)
            current = declared_parent[current]
            if "cheat" in str(current).lower():
                return True
        return False

    def native_record(row):
        record = kit_records.get(row.get("template")) if isinstance(kit_records, dict) else None
        return record if isinstance(record, dict) else {}

    def effective_size(row):
        if row.get("anchor") == "stretch":
            return None  # kích thước theo cha — không đo được trên spec
        if _valid_vec(row.get("size"), 2, positive=True):
            # Một trục = 0 nghĩa là layout group quyết cỡ (quy ước của kit) — không đo được trên spec.
            return row["size"] if row["size"][0] > 0 and row["size"][1] > 0 else None
        parent = container_map.get(row.get("parent")) or {}
        if parent.get("type") == "grid" and _valid_vec(parent.get("cellSize"), 2, positive=True):
            return parent["cellSize"]
        native = native_record(row).get("size")
        if _valid_vec(native, 2, positive=True) and native[0] > 0 and native[1] > 0:
            return native
        return None

    def effective_font(row):
        if _is_number(row.get("fontSize")):
            return row["fontSize"], True
        for text in native_record(row).get("texts") or []:
            if isinstance(text, dict) and _is_number(text.get("fontSize")) and text["fontSize"] > 0:
                return text["fontSize"], False
        return None, False

    hard_min, soft_min = settings["hardMinFontSize"], settings["minFontSize"]
    min_touch = settings["minTouchTarget"]
    cta_low, cta_high = settings["ctaHeight"]
    grid = settings["gridStep"]
    primaries = []
    tab_bars = {c for c, row in container_map.items() if row.get("tabBar") is True}

    for node_id, row in element_map.items():
        if is_template_chrome(node_id, row):
            continue
        font, explicit = effective_font(row)
        if font is not None and explicit and font < hard_min:
            found.append(("block", issue(
                "polish_font_size", path_of(node_id, ".fontSize"),
                f"fontSize {font} is below the hard floor {hard_min}px (PolishRules §9.1: min 32, never below 30 "
                "on the 1080-wide canvas) — enlarge it or drop the text",
            )))
        elif font is not None and explicit and font < soft_min:
            found.append(("warn", issue(
                "polish_font_size_soft", path_of(node_id, ".fontSize"),
                f"fontSize {font} is under the {soft_min}px minimum (PolishRules §9.1); keep it only for a "
                "secondary tag/badge and record why in assumptions[]",
            )))

        role = row.get("role")
        if role is not None and not (isinstance(role, str) and role in BUTTON_ROLES):
            found.append(("warn", issue(
                "polish_role_unknown", path_of(node_id, ".role"),
                f"unknown role {role!r}; use one of {sorted(BUTTON_ROLES)}",
            )))

        interactive = _is_interactive(row)
        size = effective_size(row)
        if interactive and size is not None and min(size) < min_touch:
            hit = row.get("hitArea")
            if not (_valid_vec(hit, 2, positive=True) and min(hit) >= min_touch):
                found.append(("block", issue(
                    "polish_touch_target", path_of(node_id, ".size"),
                    f"touch target {size[0]}x{size[1]} is under {min_touch}px (PolishRules §9.1 ≈ 44pt) — enlarge "
                    f"it, or keep the visual and declare \"hitArea\": [w,h] >= {min_touch} for an invisible "
                    "raycast pad",
                )))

        if role == "primary" and not in_repeated(node_id):
            primaries.append(node_id)
            if size is not None and not (cta_low <= size[1] <= cta_high):
                found.append(("warn", issue(
                    "polish_cta_height", path_of(node_id, ".size"),
                    f"primary CTA height {size[1]} is outside {cta_low}–{cta_high}px (PolishRules §9.1)",
                )))
        if (interactive and role is None and not in_repeated(node_id)
                and declared_parent.get(node_id) not in tab_bars):
            found.append(("warn", issue(
                "polish_button_role", path_of(node_id, ".role"),
                "interactive element has no role — declare \"role\" (primary / secondary / ads / cancel / close / "
                "back / nav / tab / item / locked) so the one-primary-CTA and button-pair rules can check it",
            )))

        text = row.get("text")
        if (isinstance(text, str) and text.strip() and not text.startswith("#") and font
                and size is not None and size[0] > 0):
            needed = len(text.strip()) * font * TEXT_EM_PER_CHAR * TEXT_LOCALIZE_ROOM
            lines = 1 if interactive else max(1, int(size[1] // (font * TEXT_LINE_HEIGHT)))
            if needed > size[0] * lines:
                found.append(("warn", issue(
                    "polish_text_room", path_of(node_id, ".text"),
                    f"{text.strip()!r} at {font}px needs ~{round(needed)}px with 30% localisation room but the box "
                    f"gives {round(size[0] * lines)}px ({lines} line(s)); button labels stay on one line "
                    "(PolishRules §9.2) — widen the box, shorten the copy, or set an auto-size minimum",
                )))

    if len(primaries) > 1:
        found.append(("block", issue(
            "polish_multiple_primary", "$.elements",
            f"{len(primaries)} primary CTAs on one screen ({', '.join(primaries)}); PolishRules §9.3 allows at most "
            "one — demote the others to secondary/ads",
        )))

    for container_id, row in container_map.items():
        children = row.get("children") if isinstance(row.get("children"), list) else []
        roles = {
            child: element_map[child].get("role") for child in children
            if isinstance(child, str) and child in element_map and isinstance(element_map[child].get("role"), str)
        }
        actions = [child for child, role in roles.items() if role in ACTION_ROLES]
        has_primary = "primary" in roles.values()
        if (row.get("type") == "col" and not in_repeated(container_id) and container_id not in repeated
                and has_primary and len(actions) >= 2):
            found.append(("block", issue(
                "polish_button_pair_stacked", path_of(container_id, ".type"),
                f"action buttons {', '.join(actions)} (a primary with its secondary / cancel / ads partner) are "
                "stacked vertically; PolishRules §9.3 puts the pair side by side at one height (secondary left, "
                "primary right) — put them in a row container. A vertical menu of secondary buttons is fine",
            )))
        if row.get("type") == "row" and has_primary and len(actions) >= 2:
            heights = {
                element_map[child]["size"][1] for child in actions
                if _valid_vec(element_map[child].get("size"), 2, positive=True) and element_map[child]["size"][1] > 0
            }
            if len(heights) > 1:
                found.append(("warn", issue(
                    "polish_button_pair_height", path_of(container_id, ".children"),
                    f"action pair {', '.join(actions)} uses different heights {sorted(heights)}; PolishRules §9.3 "
                    "keeps a pair at one height",
                )))
        values = []
        if _is_number(row.get("gap")):
            values.append(("gap", row["gap"]))
        for key in ("padding", "spacing"):
            if isinstance(row.get(key), list):
                values.extend((key, v) for v in row[key] if _is_number(v))
        off_grid = sorted({f"{key}={v}" for key, v in values if v and v % grid})
        if off_grid:
            found.append(("warn", issue(
                "polish_grid", path_of(container_id),
                f"spacing off the {grid}px grid: {', '.join(off_grid)} (PolishRules §9.1 scale 8/16/24/32/48)",
            )))
    return found


def validate_spec(
    spec: dict,
    *,
    mode: str = "draft",
    embedded: dict | None = None,
    _validate_patches: bool = True,
) -> dict:
    errors, warnings = [], []
    version = spec.get("specVersion", 0) if isinstance(spec, dict) else 0
    strict = isinstance(version, int) and version >= 1

    if not isinstance(spec, dict):
        errors.append(issue("invalid_root", "$", "spec must be a JSON object"))
        return {"ok": False, "errors": errors, "warnings": warnings, "specVersion": 0}

    if not strict:
        warnings.append(issue(
            "legacy_spec", "$.specVersion",
            "legacy embedded spec: compatible read-only fallback; new drafts must use specVersion 1",
        ))
    elif version != 1:
        errors.append(issue("unsupported_version", "$.specVersion", f"unsupported specVersion {version}"))

    for key in ("screen", "feature"):
        if not isinstance(spec.get(key), str) or not spec[key].strip():
            errors.append(issue("required", f"$.{key}", f"{key} must be a non-empty string"))
    if spec.get("branch") not in ("Popup", "FullScreen"):
        errors.append(issue("branch", "$.branch", "branch must be Popup or FullScreen"))
    supported = supported_resolutions()
    design = supported[0]
    if spec.get("designResolution") not in supported:
        errors.append(issue(
            "resolution", "$.designResolution",
            f"designResolution must be the project design resolution {design} "
            f"(accepted: {', '.join(str(value) for value in supported)})",
        ))
    elif strict and spec.get("designResolution") != design:
        warnings.append(issue(
            "resolution_legacy", "$.designResolution",
            f"designResolution {spec.get('designResolution')} is not the project design resolution {design}; "
            "an approved spec stays valid, but a new or re-generated draft must use the project value",
        ))

    containers = spec.get("containers", [])
    elements = spec.get("elements", [])
    if not isinstance(containers, list):
        errors.append(issue("type", "$.containers", "containers must be an array"))
        containers = []
    if not isinstance(elements, list):
        errors.append(issue("type", "$.elements", "elements must be an array"))
        elements = []
    if strict:
        for key in ("assumptions", "questions"):
            if not isinstance(spec.get(key), list):
                errors.append(issue("required", f"$.{key}", f"{key} must be an array"))

    ids: dict[str, str] = {}
    container_map, element_map = {}, {}
    for kind, rows in (("containers", containers), ("elements", elements)):
        for index, row in enumerate(rows):
            base = f"$.{kind}[{index}]"
            if not isinstance(row, dict):
                errors.append(issue("type", base, f"{kind[:-1]} must be an object"))
                continue
            node_id = row.get("id")
            if not isinstance(node_id, str) or not node_id.strip():
                errors.append(issue("required", f"{base}.id", "id must be a non-empty string"))
                continue
            if node_id in ids:
                errors.append(issue("duplicate_id", f"{base}.id", f"duplicate id {node_id!r}"))
            ids[node_id] = base
            (container_map if kind == "containers" else element_map)[node_id] = row

    templates = set()
    kit_records: dict = {}
    try:
        payload = json.loads(KIT_JSON.read_text(encoding="utf-8"))
        templates = set(payload.get("templates", {}))
        kit_records = payload.get("templates", {}) if isinstance(payload.get("templates"), dict) else {}
        recorded_source_hash = payload.get("_meta", {}).get("sourceHash")
        current_source_hash = kit_source_hash()
        if recorded_source_hash != current_source_hash:
            target = errors if strict else warnings
            target.append(issue(
                "kit_stale", "$",
                "UI kit does not match current prefab sources; run python3 .claude/scripts/ui-kit-sync.py",
            ))
    except UISpecError as exc:
        errors.append(issue("kit_missing", "$", str(exc)))
    except FileNotFoundError:
        errors.append(issue("kit_missing", "$", "UI kit missing; run python3 .claude/scripts/ui-kit-sync.py"))

    declared_parent: dict[str, str] = {}
    for index, row in enumerate(containers):
        if not isinstance(row, dict) or not isinstance(row.get("id"), str):
            continue
        base = f"$.containers[{index}]"
        if row.get("type") not in ("row", "col", "grid", "absolute"):
            errors.append(issue("container_type", f"{base}.type", "type must be row, col, grid or absolute"))
        if "size" in row and not _valid_vec(row["size"], 2, positive=True):
            errors.append(issue("size", f"{base}.size", "size must be [width,height] with non-negative numbers"))
        if "gap" in row and (not _is_number(row["gap"]) or row["gap"] < 0):
            errors.append(issue("gap", f"{base}.gap", "gap must be a non-negative number"))
        if "padding" in row and not _valid_vec(row["padding"], 4, positive=True):
            errors.append(issue("padding", f"{base}.padding", "padding must be [left,right,top,bottom]"))
        if "pos" in row and not _valid_vec(row["pos"], 2):
            errors.append(issue("position", f"{base}.pos", "pos must be [x,y]"))
        if "anchor" in row and row["anchor"] not in ANCHORS:
            errors.append(issue("anchor", f"{base}.anchor", "unsupported anchor preset"))
        if row.get("anchor") == "stretch" and not _valid_vec(row.get("offsets"), 4, positive=True):
            errors.append(issue("offsets", f"{base}.offsets", "stretch anchor requires [left,right,top,bottom] offsets"))
        if "childAlignment" in row and row["childAlignment"] not in ANCHORS - {"stretch"}:
            errors.append(issue("child_alignment", f"{base}.childAlignment", "unsupported childAlignment"))
        if row.get("type") == "grid":
            if not _valid_vec(row.get("cellSize"), 2, positive=True):
                errors.append(issue("grid_cell", f"{base}.cellSize", "grid requires non-negative cellSize [width,height]"))
            if not isinstance(row.get("columns"), int) or row["columns"] < 1:
                errors.append(issue("grid_columns", f"{base}.columns", "grid requires integer columns >= 1"))
            if "spacing" in row and not _valid_vec(row["spacing"], 2, positive=True):
                errors.append(issue("grid_spacing", f"{base}.spacing", "grid spacing must be [x,y]"))
        if "scroll" in row:
            scroll = row["scroll"]
            if scroll not in SCROLL_MODES:
                errors.append(issue(
                    "scroll_value", f"{base}.scroll",
                    "scroll must be false, 'vertical', 'horizontal' or 'loop'",
                ))
            elif scroll:
                if row.get("type") == "absolute":
                    errors.append(issue(
                        "scroll_container_type", f"{base}.type",
                        "a scrolling container must be row, col or grid — an absolute container has no flow "
                        "extent for the builder to size Viewport/Content from",
                    ))
                if strict and "size" not in row and row.get("anchor") != "stretch":
                    errors.append(issue(
                        "scroll_size", f"{base}.size",
                        "a scrolling container must declare size (or anchor 'stretch' with offsets) — that "
                        "rect becomes the ScrollViewTemplate Viewport the content scrolls inside",
                    ))
        if "tabBar" in row:
            tab_bar = row["tabBar"]
            if not isinstance(tab_bar, bool):
                errors.append(issue("tabbar_value", f"{base}.tabBar", "tabBar must be true or false"))
            elif tab_bar:
                if row.get("type") != "row":
                    errors.append(issue(
                        "tabbar_container_type", f"{base}.type",
                        "a tabBar container must be type 'row' - it maps to TabBottomTemplate, whose "
                        "HorizontalLayoutGroup lays the toggles out left to right",
                    ))
                children = row.get("children")
                for child in children if isinstance(children, list) else []:
                    child_row = element_map.get(child)
                    if child_row is None or child_row.get("template") not in TAB_TOGGLE_TEMPLATES:
                        errors.append(issue(
                            "tabbar_children", f"{base}.children",
                            f"tabBar child {child!r} must be a TabToggleIconTemplate/TabToggleTextTemplate "
                            "element — TabBottomTemplate hosts only the toggles; each tab's page body lives "
                            "in the content root (FullScreen/Mid)",
                        ))
        if "section" in row:
            section = row["section"]
            if not isinstance(section, dict):
                errors.append(issue(
                    "section_value", f"{base}.section",
                    "section must be an object {title, localize} — it maps the container to a "
                    "FrameTemplateInside block titled by a ButtonTitleTemplate pill",
                ))
            else:
                title = section.get("title")
                localize = section.get("localize")
                if not isinstance(title, str) or not title.strip():
                    errors.append(issue(
                        "section_value", f"{base}.section.title",
                        "section.title must be a non-empty string — it is the text painted in the pill",
                    ))
                if localize != "none" and not (
                    isinstance(localize, str) and localize.startswith("#") and len(localize) > 1
                ):
                    errors.append(issue(
                        "section_localize", f"{base}.section.localize",
                        "section.localize must be '#key' or 'none' — the pill's TitleText is a STATIC "
                        "label owned by LocalizesUI, never 'dynamic'",
                    ))
                if row.get("type") == "absolute":
                    errors.append(issue(
                        "section_container_type", f"{base}.type",
                        "a section container must be row, col or grid — the frame sizes itself from the "
                        "flow extent (VerticalLayoutGroup + ContentSizeFitter); an absolute container "
                        "has none",
                    ))
                padding = row.get("padding")
                if _valid_vec(padding, 4, positive=True) and padding[2] < SECTION_PILL_RESERVE:
                    warnings.append(issue(
                        "section_padding_top", f"{base}.padding",
                        f"padding.top {padding[2]} leaves less than {SECTION_PILL_RESERVE} for the title "
                        "pill; the pill sinks 30px into the frame, so the body starts under it",
                    ))
                children = row.get("children")
                for child in children if isinstance(children, list) else []:
                    template = (element_map.get(child) or {}).get("template")
                    if template in SECTION_TITLE_TEMPLATES:
                        errors.append(issue(
                            "section_title_as_element", f"{base}.children",
                            f"{child!r} draws a {template} inside a section container that already owns "
                            "its title pill — the flag builds it, so this ships two titles",
                        ))
                    elif template in SECTION_FRAME_TEMPLATES:
                        errors.append(issue(
                            "section_frame_as_element", f"{base}.children",
                            f"{child!r} draws a {template} inside a section container that already owns "
                            "its frame — the flag builds it, so this ships a frame inside a frame",
                        ))
        style = row.get("style")
        if strict and isinstance(style, dict):
            offenders = _container_frame_offenders(style)
            if offenders:
                errors.append(issue(
                    "container_style", f"{base}.style",
                    f"container declares visible framing ({', '.join(offenders)}); a panel/card/frame must be an "
                    "element using FrameTemplate/FrameTemplateInside/LayoutTemplate anchored stretch, not container "
                    "styling — the builder recolours a raw Image otherwise",
                ))
        children = row.get("children", [])
        if not isinstance(children, list):
            errors.append(issue("children", f"{base}.children", "children must be an array of ids"))
            continue
        for child in children:
            if child not in ids:
                errors.append(issue("unknown_child", f"{base}.children", f"unknown child id {child!r}"))
                continue
            if child in declared_parent and declared_parent[child] != row["id"]:
                errors.append(issue("multiple_parents", f"{base}.children", f"{child!r} appears under multiple containers"))
            declared_parent[child] = row["id"]

    for index, row in enumerate(elements):
        if not isinstance(row, dict) or not isinstance(row.get("id"), str):
            continue
        base = f"$.elements[{index}]"
        template = row.get("template")
        if not isinstance(template, str) or not template:
            errors.append(issue("required", f"{base}.template", "template is required"))
        elif templates and template not in templates:
            errors.append(issue("unknown_template", f"{base}.template", f"template {template!r} is not in ui-kit.json"))
        elif template in SCROLL_TEMPLATES:
            errors.append(issue(
                "scroll_as_element", f"{base}.template",
                f"{template} cannot be an element — elements hold no children, so the scrolled body would land "
                "outside its Viewport/Content. Mark the container that scrolls with "
                "\"scroll\": \"vertical\" | \"horizontal\" | \"loop\" instead",
            ))
        parent = row.get("parent")
        if parent not in container_map:
            errors.append(issue("parent", f"{base}.parent", f"parent {parent!r} is not a container"))
        elif row["id"] in declared_parent and declared_parent[row["id"]] != parent:
            errors.append(issue("parent_mismatch", f"{base}.parent", f"parent conflicts with containers[].children ({declared_parent[row['id']]!r})"))
        if "size" in row and not _valid_vec(row["size"], 2, positive=True):
            errors.append(issue("size", f"{base}.size", "size must be [width,height] with non-negative numbers"))
        if "pos" in row and not _valid_vec(row["pos"], 2):
            errors.append(issue("position", f"{base}.pos", "pos must be [x,y]"))
        if "anchor" in row and row["anchor"] not in ANCHORS:
            errors.append(issue("anchor", f"{base}.anchor", "unsupported anchor preset"))
        if row.get("anchor") == "stretch" and not _valid_vec(row.get("offsets"), 4, positive=True):
            errors.append(issue("offsets", f"{base}.offsets", "stretch anchor requires [left,right,top,bottom] offsets"))
        if "fontSize" in row and (not _is_number(row["fontSize"]) or row["fontSize"] <= 0):
            errors.append(issue("font_size", f"{base}.fontSize", "fontSize must be positive"))
        if "color" in row:
            value = row["color"]
            valid_color = isinstance(value, str) and bool(value.strip())
            if isinstance(value, list) and len(value) in (3, 4):
                valid_color = all(_is_number(v) and 0 <= v <= 1 for v in value)
            if not valid_color:
                errors.append(issue("color", f"{base}.color", "color must be CSS text or normalized RGB/RGBA array"))

        text_value = row.get("text")
        if isinstance(text_value, str) and text_value:
            localize = row.get("localize")
            if localize not in ("dynamic", "none") and not (
                isinstance(localize, str) and localize.startswith("#") and len(localize) > 1
            ):
                target = errors if strict else warnings
                target.append(issue(
                    "localize", f"{base}.localize",
                    "text elements require localize='#key', 'dynamic', or 'none'",
                ))
        if isinstance(text_value, str) and "[?]" in text_value:
            target = errors if mode in ("approve", "build") else warnings
            target.append(issue("placeholder", f"{base}.text", "unresolved [?] placeholder"))

    tab_bar_ids = {
        row["id"] for row in containers
        if isinstance(row, dict) and isinstance(row.get("id"), str) and row.get("tabBar") is True
    }
    tab_toggles = [
        row for row in elements
        if isinstance(row, dict) and row.get("template") in TAB_TOGGLE_TEMPLATES
    ]
    # FeatureTemplate ships Bot/TabBottomTemplate inactive, so a bar drawn without toggles is a
    # brown strip the build never renders - and the mockup showing it is exactly what makes a
    # builder activate it for nothing (BattleResultDungeon).
    if not tab_toggles:
        for row in elements:
            if not isinstance(row, dict) or row.get("template") not in TAB_BAR_TEMPLATES:
                continue
            warnings.append(issue(
                "tabbar_empty_chrome", ids.get(row.get("id"), "$.elements") + ".template",
                "TabBottomTemplate is drawn with no tab toggles; the shipped bar is inactive, so this screen "
                "renders no bar at runtime. Drop it and leave ButtonBack alone in the Bot chrome",
            ))
    if tab_toggles:
        for row in elements:
            if not isinstance(row, dict) or row.get("template") not in TAB_BAR_TEMPLATES:
                continue
            errors.append(issue(
                "tabbar_as_element", ids.get(row.get("id"), "$.elements") + ".template",
                "TabBottomTemplate cannot be an element on a screen that has tab toggles — elements hold no "
                "children, so the toggles land outside the bar. Mark the row container holding them with "
                "\"tabBar\": true instead",
            ))
        for row in tab_toggles:
            parent = row.get("parent")
            if parent in tab_bar_ids:
                continue
            errors.append(issue(
                "tabs_outside_bottom_bar", ids.get(row.get("id"), "$.elements") + ".parent",
                f"tab toggle {row.get('id')!r} sits in {parent!r}, which is not a \"tabBar\": true container — "
                "navigation tabs belong inside FullScreen/Bot's TabBottomTemplate (precedent: Equipment.prefab, "
                "Shop.prefab). A hand-made tab row in Mid leaves the shipped bar inactive and forces the "
                "controller to hand-wire Toggle listeners instead of UI_TabExtensions",
            ))

    section_ids = {
        row["id"] for row in containers
        if isinstance(row, dict) and isinstance(row.get("id"), str) and isinstance(row.get("section"), dict)
    }
    # A loose pill is how the pattern degrades: the title renders but the body it belongs to
    # keeps no frame, so the builder ships a bare label above unframed text.
    for row in elements:
        if not isinstance(row, dict) or row.get("template") not in SECTION_TITLE_TEMPLATES:
            continue
        if declared_parent.get(row.get("id")) in section_ids:
            continue
        warnings.append(issue(
            "section_title_without_frame", ids.get(row.get("id"), "$.elements") + ".parent",
            f"{row.get('id')!r} is a loose ButtonTitleTemplate; a titled info block is a container "
            "flag — put \"section\": {\"title\", \"localize\"} on the container it heads so the "
            "builder frames the body too (StageOverview.prefab). A standalone header pill is fine — "
            "record it in assumptions[]",
        ))
    # The pill hangs above the frame, so a tight parent gap stacks each title onto the block above.
    for node_id in section_ids:
        parent_row = container_map.get(declared_parent.get(node_id))
        if parent_row is None:
            continue
        gap = parent_row.get("gap")
        if _is_number(gap) and gap < SECTION_MIN_PARENT_GAP:
            warnings.append(issue(
                "section_parent_gap", ids[declared_parent[node_id]] + ".gap",
                f"gap {gap} is below {SECTION_MIN_PARENT_GAP} while stacking section {node_id!r}; the "
                "title pill hangs 30px above its frame and will overlap the block above it",
            ))

    chrome_sizes: dict[str, set] = {}
    for row in elements:
        if not isinstance(row, dict):
            continue
        template = row.get("template")
        size = row.get("size")
        if template in CHROME_TEMPLATES and _valid_vec(size, 2, positive=True):
            chrome_sizes.setdefault(template, set()).add((size[0], size[1]))
    for template, sizes in chrome_sizes.items():
        if len(sizes) > 1:
            warnings.append(issue(
                "inconsistent_chrome_size", "$.elements",
                f"template {template!r} is placed at differing sizes {sorted(sizes)}; identical chrome widgets "
                "(timers, chips, badges) should share one size — reuse the template's native size",
            ))

    if strict:
        for level, entry in _polish_issues(
            spec, containers, elements, container_map, element_map, declared_parent, ids, kit_records,
        ):
            gate = _polish_settings()["specGate"]
            # build = spec đã duyệt, đang dựng: hợp đồng đã đóng băng, lỗi polish chỉ cảnh báo để
            # không làm gãy task đang chạy. draft/approve mới là chỗ chặn.
            blocking = level == "block" and gate == "block" and mode != "build"
            (errors if blocking else warnings).append(entry)

    for node_id, row in {**container_map, **element_map}.items():
        parent = row.get("parent")
        if parent is not None and parent not in container_map:
            errors.append(issue("parent", ids[node_id] + ".parent", f"parent {parent!r} is not a container"))
        if parent is not None and node_id in declared_parent and declared_parent[node_id] != parent:
            errors.append(issue("parent_mismatch", ids[node_id] + ".parent", "parent conflicts with containers[].children"))
        if strict and parent is not None and node_id not in declared_parent:
            errors.append(issue(
                "unlisted_child", ids[node_id] + ".parent",
                f"{node_id!r} declares parent {parent!r} but is absent from that container's children",
            ))

    if strict:
        content_root = spec.get("contentRoot")
        if content_root not in container_map:
            errors.append(issue(
                "content_root", "$.contentRoot",
                "contentRoot must name the Popup/content or FullScreen/Mid container",
            ))
        else:
            def descends_from(node_id: str, ancestor: str) -> bool:
                seen = set()
                current = node_id
                while current in declared_parent and current not in seen:
                    seen.add(current)
                    current = declared_parent[current]
                    if current == ancestor:
                        return True
                return node_id == ancestor

            for node_id, row in element_map.items():
                # Tab toggles are feature content that legitimately lives in the Bot chrome
                # (inside the shipped TabBottomTemplate), so a tabBar parent exempts them
                # instead of forcing a misleading baseChrome flag.
                if declared_parent.get(node_id) in tab_bar_ids:
                    continue
                if not row.get("baseChrome") and not descends_from(node_id, content_root):
                    errors.append(issue(
                        "containment", ids[node_id],
                        f"{node_id!r} must descend from contentRoot {content_root!r}; mark only template-owned chrome as baseChrome",
                    ))

            if spec.get("branch") == "FullScreen":
                has_fullscreen_chrome = any(
                    row.get("baseChrome") and not descends_from(node_id, content_root)
                    for node_id, row in element_map.items()
                )
                if not has_fullscreen_chrome:
                    warnings.append(issue(
                        "fullscreen_missing_chrome", "$.branch",
                        f"branch='FullScreen' has no baseChrome element outside contentRoot {content_root!r} — "
                        "mirror FeatureTemplate.prefab's FullScreen/Top (ResourceViewTemplate Gold/Gem bar) and "
                        "FullScreen/Bot (a ButtonIcon instance named ButtonBack; the TabBottomTemplate bar only "
                        "when the screen has tabs), or record the deviation in assumptions[]",
                    ))

            if tab_bar_ids and all(descends_from(node_id, content_root) for node_id in tab_bar_ids):
                warnings.append(issue(
                    "tabbar_in_content", "$.containers",
                    "every tabBar container sits inside contentRoot; primary navigation tabs belong to the "
                    "FullScreen/Bot chrome (the shipped TabBottomTemplate). A content-level filter row like "
                    "Equipment's EquipmentFilter is fine — record it in assumptions[]",
                ))

    questions = spec.get("questions", [])
    if not isinstance(questions, list):
        errors.append(issue("questions", "$.questions", "questions must be an array"))
    else:
        # Structured options carry a restricted deterministic patch. Legacy string options still
        # route through AI regenerate for changes that cannot be expressed safely as data.
        for idx, question in enumerate(questions):
            if isinstance(question, str):
                continue
            if not isinstance(question, dict) or not isinstance(question.get("q"), str) or not question["q"].strip():
                errors.append(issue("questions", f"$.questions[{idx}]", "each question must be a string or an object {q, options?}"))
                continue
            options = question.get("options", [])
            if not isinstance(options, list):
                errors.append(issue("questions", f"$.questions[{idx}].options", "question options must be an array"))
                continue
            for option_idx, option in enumerate(options):
                option_path = f"$.questions[{idx}].options[{option_idx}]"
                if isinstance(option, str):
                    continue
                if not isinstance(option, dict) or not isinstance(option.get("label"), str) or not option["label"].strip():
                    errors.append(issue("questions", option_path, "option must be a string or {label, patch}"))
                    continue
                patch = option.get("patch")
                if not isinstance(patch, list):
                    errors.append(issue("questions", option_path + ".patch", "structured option patch must be an array"))
                    continue
                option_valid = True
                if len(patch) > 100:
                    errors.append(issue("questions", option_path + ".patch", "structured option patch may contain at most 100 operations"))
                    option_valid = False
                for patch_idx, operation in enumerate(patch):
                    op_path = f"{option_path}.patch[{patch_idx}]"
                    if not isinstance(operation, dict) or operation.get("op") not in PATCH_OPS:
                        errors.append(issue("questions", op_path, "patch op must be add, remove, or replace"))
                        option_valid = False
                        continue
                    path = operation.get("path")
                    if not isinstance(path, str) or not re.match(r"^/(containers|elements|wiring|assumptions)(/|$)", path):
                        errors.append(issue("questions", op_path + ".path", "patch path must target a mutable UI field"))
                        option_valid = False
                    if operation["op"] in ("add", "replace") and "value" not in operation:
                        errors.append(issue("questions", op_path + ".value", f"{operation['op']} requires value"))
                        option_valid = False
                if option_valid and _validate_patches:
                    try:
                        patched = apply_json_patch(spec, patch)
                    except ValueError as exc:
                        errors.append(issue("questions", option_path + ".patch", f"structured option cannot apply: {exc}"))
                    else:
                        result = validate_spec(patched, mode="draft", _validate_patches=False)
                        if result["errors"]:
                            first = result["errors"][0]
                            errors.append(issue("questions", option_path + ".patch", f"structured option produces invalid UI: {first['path']}: {first['message']}"))
        if questions:
            target = errors if mode in ("approve", "build") else warnings
            target.append(issue("unresolved_questions", "$.questions", f"{len(questions)} unresolved question(s)"))

    if strict and embedded is not None and canonical_json(spec) != canonical_json(embedded):
        errors.append(issue(
            "sidecar_drift", "$",
            "HTML embedded spec differs from the authoritative .ui-spec.json sidecar; re-render HTML",
        ))

    # Grandfathered embedded specs predate the v1 schema and were previously
    # approvable without machine validation. Keep only load/top-level contract
    # failures blocking; surface all detailed defects as migration warnings.
    if not strict:
        fatal_codes = {"invalid_root", "required", "branch", "resolution", "type", "kit_missing"}
        legacy_details = [entry for entry in errors if entry["code"] not in fatal_codes]
        errors = [entry for entry in errors if entry["code"] in fatal_codes]
        warnings.extend(legacy_details)

    return {
        "ok": not errors,
        "errors": errors,
        "warnings": warnings,
        "specVersion": version,
        "specHash": spec_hash(spec),
        "kitHash": kit_hash() if KIT_JSON.exists() and KIT_CSS.exists() else None,
    }


if __name__ == "__main__":
    # `python3 .claude/scripts/ui_spec_common.py design-resolution` → [1080, 2400]: kích thước pin Game view /
    # chụp kiểm khi chưa có spec (có spec thì dùng designResolution của chính spec đó).
    import sys

    if sys.argv[1:] == ["design-resolution"]:
        print(json.dumps(design_resolution()))
    else:
        sys.exit("usage: ui_spec_common.py design-resolution")
