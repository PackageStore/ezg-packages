#!/usr/bin/env python3
"""Shared helpers for the spec-first Unity UI mockup pipeline.

The UI kit here is DERIVED FROM THE UI CATALOG: ui-catalog/ui-tokens.json is
the UGUI SSOT (authored via UiCatalogConfig, baked by the editor exporter) and
ui-kit-sync.py bakes ui-catalog/ui-kit.{json,css} from it. Kit template names
ARE catalog token ids (e.g. "ui.currency.single"), so mockup specs and Unity
builds share one vocabulary. Design resolution is 1080x2400 (screen_template's
CanvasScaler reference resolution).
"""

from __future__ import annotations

import copy
import hashlib
import html
import json
import os
import re
from pathlib import Path


ROOT = Path(os.environ.get("UI_PIPELINE_ROOT", Path(__file__).resolve().parents[2])).resolve()
CATALOG_JSON = ROOT / "ui-catalog" / "ui-tokens.json"
KIT_JSON = ROOT / "ui-catalog" / "ui-kit.json"
KIT_CSS = ROOT / "ui-catalog" / "ui-kit.css"
DESIGN_RESOLUTION = [1080, 2400]
SPEC_RE = re.compile(
    r"<script\s+[^>]*id=[\"']spec[\"'][^>]*>(.*?)</script>",
    re.IGNORECASE | re.DOTALL,
)
CSS_SAFE_RE = re.compile(r"[^A-Za-z0-9_-]")
PATCH_ROOTS = {"containers", "elements", "wiring", "assumptions"}
PATCH_OPS = {"add", "remove", "replace"}


class UISpecError(ValueError):
    pass


def css_class(template: str) -> str:
    """Catalog token ids contain dots (ui.currency.single) — CSS class names
    cannot. Both the kit generator and the renderer must sanitize identically."""
    return CSS_SAFE_RE.sub("-", template)


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
    """Hash the catalog JSON plus every referenced prefab (+ .meta), sorted by
    token id. Must stay byte-identical to ui-kit-sync.py's source_hash()."""
    digest = hashlib.sha256()
    digest.update(CATALOG_JSON.read_bytes())
    digest.update(b"\0")
    tokens = json.loads(CATALOG_JSON.read_text(encoding="utf-8")).get("tokens", [])
    for token in sorted(tokens, key=lambda t: t.get("token", "")):
        asset = ROOT / token.get("assetPath", "")
        digest.update(token.get("token", "").encode("utf-8"))
        digest.update(b"\0")
        if asset.is_file():
            digest.update(asset.read_bytes())
            meta = asset.with_suffix(asset.suffix + ".meta")
            if meta.exists():
                digest.update(meta.read_bytes())
        else:
            digest.update(b"<missing>")
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
    """Apply the restricted patch dialect used by structured mockup choices."""
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


def validate_spec(
    spec: dict,
    *,
    mode: str = "draft",
    embedded: dict | None = None,
    require_lane: bool = False,
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

    lane = spec.get("mockupLane")
    if lane is not None and lane not in ("kit-composition", "custom"):
        errors.append(issue("mockup_lane", "$.mockupLane", "mockupLane must be kit-composition or custom"))
    elif strict and require_lane and lane is None:
        errors.append(issue("mockup_lane", "$.mockupLane", "new drafts must record their fast lane"))

    for key in ("screen", "feature"):
        if not isinstance(spec.get(key), str) or not spec[key].strip():
            errors.append(issue("required", f"$.{key}", f"{key} must be a non-empty string"))
    if spec.get("branch") not in ("Popup", "FullScreen"):
        errors.append(issue("branch", "$.branch", "branch must be Popup or FullScreen"))
    if spec.get("designResolution") != DESIGN_RESOLUTION:
        errors.append(issue("resolution", "$.designResolution",
                            f"designResolution must be {DESIGN_RESOLUTION}"))

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
    try:
        payload = json.loads(KIT_JSON.read_text(encoding="utf-8"))
        templates = set(payload.get("templates", {}))
        recorded_source_hash = payload.get("_meta", {}).get("sourceHash")
        current_source_hash = kit_source_hash()
        if recorded_source_hash != current_source_hash:
            target = errors if strict else warnings
            target.append(issue(
                "kit_stale", "$",
                "UI kit does not match current ui-catalog/prefab sources; run python3 .claude/scripts/ui-kit-sync.py",
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
            errors.append(issue("unknown_template", f"{base}.template", f"template {template!r} is not in ui-kit.json (kit templates are ui-catalog token ids)"))
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
            target.append(issue(
                "placeholder", f"{base}.text",
                f"unresolved [?] placeholder in {row.get('id', '?')!r}: {text_value!r} "
                f"(replace with a representative value + localize='dynamic', or remove the element)",
            ))

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
                "contentRoot must name the popup container_content or full_screen content container",
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
                if not row.get("baseChrome") and not descends_from(node_id, content_root):
                    errors.append(issue(
                        "containment", ids[node_id],
                        f"{node_id!r} must descend from contentRoot {content_root!r}; mark only template-owned chrome as baseChrome",
                    ))

    questions = spec.get("questions", [])
    if not isinstance(questions, list):
        errors.append(issue("questions", "$.questions", "questions must be an array"))
    else:
        # Options may stay as legacy strings (AI edit) or carry a deterministic JSON patch
        # that the review dashboard applies locally without launching an agent.
        for idx, q in enumerate(questions):
            if isinstance(q, str):
                continue
            if isinstance(q, dict) and isinstance(q.get("q"), str) and q.get("q").strip():
                opts = q.get("options", [])
                if not isinstance(opts, list):
                    errors.append(issue("questions", f"$.questions[{idx}].options", "question options must be an array"))
                    continue
                for option_idx, option in enumerate(opts):
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
                        if not isinstance(operation, dict) or operation.get("op") not in ("add", "remove", "replace"):
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
                            patched_result = validate_spec(
                                patched,
                                mode="draft",
                                require_lane=require_lane,
                                _validate_patches=False,
                            )
                            if patched_result["errors"]:
                                first = patched_result["errors"][0]
                                errors.append(issue(
                                    "questions",
                                    option_path + ".patch",
                                    f"structured option produces invalid UI: {first['path']}: {first['message']}",
                                ))
                continue
            errors.append(issue("questions", f"$.questions[{idx}]", "each question must be a string or an object {q, options?}"))
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
