#!/usr/bin/env python3
"""ui-kit-sync — extract the UI-kit contract from the template prefabs.

Reads `<uiTemplatesRoot>/*.prefab` (the profile key; Unity YAML, parsed with
regexes — same zero-dependency approach as backlog-preflight.py; PyYAML can't
read Unity's `!u!` tags) and writes to .claude/ui-kit/:

  ui-kit.json       real numbers per template: root size/anchors/pivot,
                    background color, text nodes (font size/color/sample),
                    direct children (name, size, position, active), nested
                    template references.
  ui-kit.css        one `.tpl-<Name>` class per template carrying the real
                    dimensions + colors, plus the shared wireframe base
                    (.stage 1080x1920, .tpl, .col/.row flex helpers) that
                    /ui-mockup drafts inline into each mockup HTML.
  kit-preview.html  gallery rendered from prefab hierarchy and linked assets,
                    including sliced sprites and layout-driven roots.

Prefab Variants (root = PrefabInstance with m_TransformParent {fileID: 0})
are resolved recursively against their base prefab (searched under
Assets/Resources/Prefabs/**), then root-targeted m_SizeDelta / text overrides
are applied on top.

The CSS contract intentionally remains a portable wireframe for mockup HTML.
The gallery is higher fidelity: it resolves sprite/font GUIDs, reads sprite
rects and borders from texture .meta files, and renders them from source assets
without creating captured copies.

Hand-written composition rules that no prefab can express (`ui-kit-usage.json`)
are merged in as each template's `usage` field.

Deterministic output (sorted names, no timestamps) → clean git diffs.
Run whenever the templates change:
    python3 .claude/scripts/ui-kit-sync.py
    python3 .claude/scripts/ui-kit-sync.py --check   # exit 1 if the kit is stale

Lifecycle (when to run, what to do when it drifts): .claude/skills/ui-kit/SKILL.md
"""

import hashlib
import copy
import json
import math
import re
import struct
import sys
from pathlib import Path

# Running as a script puts this directory on sys.path, so the plain import works.
from project_profile import profile

ROOT = Path(__file__).resolve().parents[2]
# Where the screen templates live differs per project, so it comes from the
# profile (`uiTemplatesRoot`). PREFABS_ROOT is its parent: the GUID scan below
# needs every prefab around the templates, not just the templates themselves.
TEMPLATES_REL = profile().ui_templates_root
TEMPLATES_DIR = ROOT / TEMPLATES_REL
PREFABS_ROOT = TEMPLATES_DIR.parent
OUT_DIR = ROOT / ".claude" / "ui-kit"
DESIGN_W, DESIGN_H = 1080, 1920
MAX_VARIANT_DEPTH = 5

# Hand-maintained composition contract, kept as DATA next to the kit rather than
# in this file. The extractor can read a prefab's own geometry but not how
# templates compose with each other, so a drafter reading ui-kit.json alone
# cannot tell that tab toggles must live inside the bottom bar (how DungeonGuide
# ended up with a hand-made tab row in Mid). Those rules are per-project — the
# template set differs — while this script is shared byte-identical across
# projects, so hardcoding them here would ship one game's rules to every other.
USAGE_FILE = OUT_DIR / "ui-kit-usage.json"


def load_usage(errors=None):
    """Per-project composition notes; missing file simply means none.

    `errors` collects why notes were dropped, so --check can say "your file is
    broken" instead of the misleading "the notes changed".
    """
    def broken(message):
        if errors is not None:
            errors.append(message)
        print(f"warning: {message}", file=sys.stderr)
        return {}

    try:
        raw = USAGE_FILE.read_text(encoding="utf-8")
    except FileNotFoundError:
        return {}
    except OSError as error:
        return broken(f"cannot read {USAGE_FILE.name}: {error}")
    try:
        payload = json.loads(raw)
    except json.JSONDecodeError as error:
        # Hand-edited file: a typo must not silently drop every note, and it must
        # not sink the kit either — the geometry half is still worth writing.
        return broken(f"{USAGE_FILE.name} is not valid JSON ({error}); "
                      "usage notes skipped")
    notes = payload.get("templates", payload)
    if not isinstance(notes, dict):
        return {}
    return {name: text for name, text in notes.items()
            if isinstance(text, str) and text.strip() and not name.startswith("_")}

DOC_RE = re.compile(r"^--- !u!(\d+) &(-?\d+)( stripped)?\s*$", re.M)
GUID_IN_META_RE = re.compile(r"^guid: ([0-9a-f]{32})", re.M)
MOD_ENTRY_RE = re.compile(
    r"- target: \{fileID: (-?\d+), guid: [0-9a-f]{32}, type: \d+\}\n"
    r"\s+propertyPath: (\S+)\n"
    r"\s+value: ([^\n]*)")
MOD_FULL_RE = re.compile(
    r"- target: \{fileID: (-?\d+), guid: ([0-9a-f]{32}), type: \d+\}\n"
    r"\s+propertyPath: ([^\n]+)\n"
    r"\s+value: ([^\n]*)\n"
    r"\s+objectReference: \{fileID: (-?\d+)(?:, guid: ([0-9a-f]{32}), type: \d+)?\}")

# Built-in UI script GUIDs (stable across Unity versions); used as a shortcut,
# with field-signature detection as the robust fallback.
GUID_IMAGE = "fe87c0e1cc204ed48ad3b37840f39efc"
GUID_TEXT = "5f7201a12d95ffc409449d95f23cf332"
GUID_TMP = "f4688fdb7df04437aeb418b961361dc5"


def scalar(body, name, default=None):
    m = re.search(rf"^\s*{re.escape(name)}: ([^\n]+)", body, re.M)
    return m.group(1).strip() if m else default


def object_ref(body, name):
    m = re.search(
        rf"^\s*{re.escape(name)}: \{{fileID: (-?\d+)(?:, guid: ([0-9a-f]{{32}}), type: \d+)?\}}",
        body, re.M)
    return {"fileId": m.group(1), "guid": m.group(2)} if m else None


def vec4_values(value):
    if not value:
        return [0, 0, 0, 0]
    fields = {key: num(raw) for key, raw in re.findall(r"([xyzw]): (-?[\d.eE+-]+)", value)}
    return [fields.get(key, 0) for key in "xyzw"]


def source_hash():
    """Hash template prefabs and their meta GUIDs without unrelated Resources churn."""
    digest = hashlib.sha256()
    for path in sorted(TEMPLATES_DIR.glob("*.prefab")):
        digest.update(str(path.relative_to(ROOT)).encode("utf-8"))
        digest.update(b"\0")
        digest.update(path.read_bytes())
        meta = path.with_suffix(path.suffix + ".meta")
        if meta.exists():
            digest.update(meta.read_bytes())
        digest.update(b"\0")
    return digest.hexdigest()


def num(s):
    v = float(s)
    v = round(v, 2)
    return int(v) if v == int(v) else v


def vec2(body, name):
    m = re.search(rf"^\s*{name}: \{{x: (-?[\d.eE+-]+), y: (-?[\d.eE+-]+)\}}", body, re.M)
    return [num(m.group(1)), num(m.group(2))] if m else None


def ref(body, name):
    m = re.search(rf"^\s*{name}: \{{fileID: (-?\d+)", body, re.M)
    return m.group(1) if m else None


def color(body, name="m_Color"):
    m = re.search(
        rf"^\s*{name}: \{{r: (-?[\d.eE+-]+), g: (-?[\d.eE+-]+), b: (-?[\d.eE+-]+), a: (-?[\d.eE+-]+)\}}",
        body, re.M)
    if not m:
        return None
    r, g, b, a = (float(m.group(i)) for i in range(1, 5))
    return [round(r, 4), round(g, 4), round(b, 4), round(a, 4)]


def css_rgba(c, fallback="rgba(255,255,255,0.08)"):
    if not c:
        return fallback
    r, g, b, a = c
    return f"rgba({round(r * 255)},{round(g * 255)},{round(b * 255)},{round(a, 3)})"


def parse_docs(text):
    """Split a Unity YAML file into (classid, fileid, stripped, body) docs."""
    marks = list(DOC_RE.finditer(text))
    out = []
    for i, m in enumerate(marks):
        end = marks[i + 1].start() if i + 1 < len(marks) else len(text)
        out.append((m.group(1), m.group(2), bool(m.group(3)), text[m.end():end]))
    return out


def parse_children(body):
    m = re.search(r"^\s*m_Children:\n((?:\s*- \{fileID: -?\d+\}\n)+)", body, re.M)
    if not m:
        return []
    return re.findall(r"fileID: (-?\d+)", m.group(1))


def classify_mono(body, guid):
    if guid == GUID_TMP or (re.search(r"^\s*m_fontSize:", body, re.M)
                            and re.search(r"^\s*m_text:", body, re.M)):
        return "tmp"
    if guid == GUID_TEXT or re.search(r"^\s*m_FontData:", body, re.M):
        return "text"
    if guid == GUID_IMAGE or (re.search(r"^\s*m_Sprite:", body, re.M)
                              and re.search(r"^\s*m_Color:", body, re.M)):
        return "image"
    return None


def text_sample(body, key):
    m = re.search(rf"^\s*{key}: (.*)$", body, re.M)
    if not m:
        return None
    s = m.group(1).strip().strip("'\"")
    return s[:40] if s else None


def guid_path_map():
    """guid → prefab Path for every prefab under Assets/Resources/Prefabs/**,
    so variant bases and nested PrefabInstances resolve to real files/names."""
    out = {}
    for meta in PREFABS_ROOT.rglob("*.prefab.meta"):
        m = GUID_IN_META_RE.search(meta.read_text(encoding="utf-8", errors="replace"))
        if m:
            out[m.group(1)] = meta.with_suffix("")  # strip .meta → .prefab path
    return out


def asset_guid_path_map():
    """GUID → asset path for sprite/font resolution in the HTML preview."""
    out = {}
    for meta in (ROOT / "Assets").rglob("*.meta"):
        try:
            head = meta.read_text(encoding="utf-8", errors="replace")[:256]
        except OSError:
            continue
        m = GUID_IN_META_RE.search(head)
        if m:
            out[m.group(1)] = meta.with_suffix("")
    return out


def png_size(path):
    try:
        with path.open("rb") as stream:
            header = stream.read(24)
        if header[:8] == b"\x89PNG\r\n\x1a\n" and header[12:16] == b"IHDR":
            return list(struct.unpack(">II", header[16:24]))
    except OSError:
        pass
    return None


def texture_metadata(path, file_id, cache):
    """Resolve a Unity Sprite reference to its source texture rect and border."""
    key = (str(path), str(file_id))
    if key in cache:
        return copy.deepcopy(cache[key])
    meta_path = path.with_suffix(path.suffix + ".meta")
    if not path.is_file() or not meta_path.is_file():
        cache[key] = None
        return None
    text = meta_path.read_text(encoding="utf-8", errors="replace")
    size = png_size(path)
    if not size:
        cache[key] = None
        return None

    ppu = num(scalar(text, "spritePixelsToUnits", "100"))
    border_match = re.search(r"^\s*spriteBorder: \{([^}]+)\}", text, re.M)
    border = vec4_values(border_match.group(1) if border_match else None)
    rect = [0, 0, size[0], size[1]]
    sprite_name = path.stem
    if str(file_id) != "21300000":
        blocks = re.finditer(
            r"(?ms)^    - serializedVersion: 2\n(.*?)(?=^    - serializedVersion: 2\n|^    outline:)",
            text)
        for match in blocks:
            body = match.group(1)
            internal = re.search(r"^\s+internalID: (-?\d+)", body, re.M)
            if not internal or internal.group(1) != str(file_id):
                continue
            sprite_name = scalar(body, "name", sprite_name)
            rect_match = re.search(
                r"(?ms)^\s+rect:\n\s+serializedVersion: \d+\n"
                r"\s+x: (-?[\d.]+)\n\s+y: (-?[\d.]+)\n"
                r"\s+width: ([\d.]+)\n\s+height: ([\d.]+)", body)
            if rect_match:
                rect = [num(rect_match.group(i)) for i in range(1, 5)]
            sub_border = re.search(r"^\s+border: \{([^}]+)\}", body, re.M)
            if sub_border:
                border = vec4_values(sub_border.group(1))
            break

    relative = path.relative_to(ROOT).as_posix()
    result = {
        "path": "../../" + relative,
        "textureSize": size,
        "rect": rect,
        "border": border,
        "pixelsPerUnit": ppu,
        "name": sprite_name,
    }
    cache[key] = result
    return copy.deepcopy(result)


def extract(prefab: Path, guid_paths: dict):
    """Parse one prefab. Returns a template record, a variant marker
    {"variant": guid, "mods": [...]}, or None (no UI root)."""
    text = prefab.read_text(encoding="utf-8", errors="replace")
    gos, rts, stripped_rts, monos, instances = {}, {}, {}, [], {}

    for classid, fid, stripped, body in parse_docs(text):
        if classid == "1":
            name = re.search(r"^\s*m_Name: (.*)$", body, re.M)
            active = re.search(r"^\s*m_IsActive: (\d)", body, re.M)
            gos[fid] = {"name": (name.group(1).strip() if name else fid),
                        "active": (active.group(1) == "1") if active else True}
        elif classid == "224":
            if stripped:
                stripped_rts[fid] = {"instance": ref(body, "m_PrefabInstance")}
                continue
            rts[fid] = {
                "go": ref(body, "m_GameObject"),
                "father": ref(body, "m_Father"),
                "children": parse_children(body),
                "anchorMin": vec2(body, "m_AnchorMin"),
                "anchorMax": vec2(body, "m_AnchorMax"),
                "size": vec2(body, "m_SizeDelta"),
                "pos": vec2(body, "m_AnchoredPosition"),
                "pivot": vec2(body, "m_Pivot"),
            }
        elif classid == "114":
            gm = re.search(r"m_Script: \{fileID: -?\d+, guid: ([0-9a-f]{32})", body)
            monos.append({"go": ref(body, "m_GameObject"),
                          "guid": gm.group(1) if gm else "", "body": body})
        elif classid == "1001":
            sm = re.search(r"m_SourcePrefab: \{fileID: -?\d+, guid: ([0-9a-f]{32})", body)
            instances[fid] = {"source": sm.group(1) if sm else "",
                              "parent": ref(body, "m_TransformParent"),
                              "mods": MOD_ENTRY_RE.findall(body)}

    root_id = next((fid for fid, rt in rts.items() if rt["father"] in (None, "0")), None)
    if root_id is None:
        # Prefab Variant: the root is a PrefabInstance parented to fileID 0.
        for inst in instances.values():
            if inst["parent"] == "0" and inst["source"]:
                return {"variant": inst["source"], "mods": inst["mods"]}
        return None
    root = rts[root_id]

    def go_name(go_id):
        return gos.get(go_id, {}).get("name", "?")

    def comps_of(go_id, kind):
        return [mb["body"] for mb in monos
                if mb["go"] == go_id and classify_mono(mb["body"], mb["guid"]) == kind]

    def axis_size(rt, axis, full):
        """CSS-usable pixel size: fixed anchors → sizeDelta; stretch → design
        size + sizeDelta (sizeDelta is the summed margins when stretched)."""
        a_min, a_max = rt["anchorMin"], rt["anchorMax"]
        sd = rt["size"] or [0, 0]
        if a_min and a_max and a_min[axis] != a_max[axis]:
            return max(0, num(full * (a_max[axis] - a_min[axis]) + sd[axis])), True
        return sd[axis], False

    w, stretch_w = axis_size(root, 0, DESIGN_W)
    h, stretch_h = axis_size(root, 1, DESIGN_H)

    bg = None
    for body in comps_of(root["go"], "image"):
        bg = color(body)
        break
    if not bg or bg[3] < 0.05:
        # Root image is often a transparent raycast target — the visible skin
        # lives on a direct child (e.g. ButtonImg-on). Borrow its color.
        for cid in root["children"]:
            rt = rts.get(cid)
            if not rt:
                continue
            child_bg = next((color(b) for b in comps_of(rt["go"], "image")), None)
            if child_bg and child_bg[3] >= 0.05:
                bg = child_bg
                break

    texts = []
    for mb in monos:
        kind = classify_mono(mb["body"], mb["guid"])
        if kind == "text":
            fs = re.search(r"^\s*m_FontSize: (\d+)", mb["body"], re.M)
            texts.append({"node": go_name(mb["go"]),
                          "fontSize": int(fs.group(1)) if fs else None,
                          "color": color(mb["body"]),
                          "sample": text_sample(mb["body"], "m_Text")})
        elif kind == "tmp":
            fs = re.search(r"^\s*m_fontSize: (-?[\d.]+)", mb["body"], re.M)
            texts.append({"node": go_name(mb["go"]),
                          "fontSize": num(fs.group(1)) if fs else None,
                          "color": color(mb["body"], "m_fontColor"),
                          "sample": text_sample(mb["body"], "m_text")})
    texts.sort(key=lambda t: (t["node"], str(t["sample"])))

    children = []
    for cid in root["children"]:
        if cid in stripped_rts:
            inst = instances.get(stripped_rts[cid]["instance"] or "", {})
            mods = {p: v for _, p, v in inst.get("mods", [])}
            src_path = guid_paths.get(inst.get("source", ""))
            src = src_path.stem if src_path else inst.get("source", "?")[:8]
            entry = {"name": mods.get("m_Name", src), "nested": src}
            if "m_SizeDelta.x" in mods and "m_SizeDelta.y" in mods:
                entry["size"] = [num(mods["m_SizeDelta.x"]), num(mods["m_SizeDelta.y"])]
            children.append(entry)
        elif cid in rts:
            rt = rts[cid]
            go = gos.get(rt["go"], {})
            children.append({"name": go.get("name", "?"),
                             "size": rt["size"], "pos": rt["pos"],
                             "active": go.get("active", True)})

    nested = sorted({guid_paths[i["source"]].stem for i in instances.values()
                     if i["source"] in guid_paths})

    rec = {"size": [w, h],
           "background": css_rgba(bg, None) if bg else None,
           "texts": texts[:6],
           "children": children,
           "nested": nested,
           "_rootRT": root_id}
    if stretch_w or stretch_h:
        rec["stretch"] = True
    if bg:
        rec["backgroundRGBA"] = bg
    if root["pivot"] and root["pivot"] != [0.5, 0.5]:
        rec["pivot"] = root["pivot"]
    return rec


def parse_full_mods(body):
    return [
        {
            "target": target,
            "targetGuid": target_guid,
            "property": prop.strip(),
            "value": value.strip(),
            "refFileId": ref_file,
            "refGuid": ref_guid,
        }
        for target, target_guid, prop, value, ref_file, ref_guid in MOD_FULL_RE.findall(body)
    ]


def visual_rect(body):
    rotation = re.search(
        r"^\s*m_LocalRotation: \{x: (-?[\d.eE+-]+), y: (-?[\d.eE+-]+), "
        r"z: (-?[\d.eE+-]+), w: (-?[\d.eE+-]+)\}", body, re.M)
    angle = 0
    if rotation:
        z, w = float(rotation.group(3)), float(rotation.group(4))
        angle = round(math.degrees(2 * math.atan2(z, w)), 3)
    scale_match = re.search(
        r"^\s*m_LocalScale: \{x: (-?[\d.eE+-]+), y: (-?[\d.eE+-]+), z: (-?[\d.eE+-]+)\}",
        body, re.M)
    scale = [num(scale_match.group(1)), num(scale_match.group(2))] if scale_match else [1, 1]
    return {
        "anchorMin": vec2(body, "m_AnchorMin") or [0.5, 0.5],
        "anchorMax": vec2(body, "m_AnchorMax") or [0.5, 0.5],
        "size": vec2(body, "m_SizeDelta") or [0, 0],
        "pos": vec2(body, "m_AnchoredPosition") or [0, 0],
        "pivot": vec2(body, "m_Pivot") or [0.5, 0.5],
        "scale": scale,
        "rotation": angle,
    }


def visual_image(body):
    sprite = object_ref(body, "m_Sprite")
    texture = object_ref(body, "m_Texture")
    return {
        "spriteRef": sprite or texture,
        "color": color(body) or [1, 1, 1, 1],
        "type": int(scalar(body, "m_Type", "0")),
        "preserveAspect": scalar(body, "m_PreserveAspect", "0") == "1",
        "fillCenter": scalar(body, "m_FillCenter", "1") == "1",
        "fillMethod": int(scalar(body, "m_FillMethod", "4")),
        "fillAmount": num(scalar(body, "m_FillAmount", "1")),
        "fillOrigin": int(scalar(body, "m_FillOrigin", "0")),
        "fillClockwise": scalar(body, "m_FillClockwise", "1") == "1",
        "ppuMultiplier": num(scalar(body, "m_PixelsPerUnitMultiplier", "1")),
    }


def visual_text(body, kind):
    if kind == "text":
        font_size = scalar(body, "m_FontSize")
        alignment = int(scalar(body, "m_Alignment", "4"))
        sample = text_sample(body, "m_Text") or ""
        font_ref = object_ref(body, "m_Font")
        best_fit = scalar(body, "m_BestFit", "0") == "1"
        min_size = scalar(body, "m_MinSize", "3")
        max_size = scalar(body, "m_MaxSize", font_size or "40")
        font_style = int(scalar(body, "m_FontStyle", "0"))
    else:
        font_size = scalar(body, "m_fontSize")
        alignment = int(scalar(body, "m_textAlignment", "514"))
        sample = text_sample(body, "m_text") or ""
        font_ref = object_ref(body, "m_fontAsset")
        best_fit = scalar(body, "m_enableAutoSizing", "0") == "1"
        min_size = scalar(body, "m_fontSizeMin", "3")
        max_size = scalar(body, "m_fontSizeMax", font_size or "40")
        font_style = int(scalar(body, "m_fontStyle", "0"))
    return {
        "value": sample,
        "fontSize": num(font_size) if font_size else 30,
        "color": color(body, "m_fontColor") if kind == "tmp" else color(body),
        "alignment": alignment,
        "bestFit": best_fit,
        "minSize": num(min_size),
        "maxSize": num(max_size),
        "bold": font_style in (1, 3),
        "italic": font_style in (2, 3),
        "fontRef": font_ref,
    }


def iter_visual_nodes(root):
    yield root
    for child in root.get("children", []):
        yield from iter_visual_nodes(child)


def set_pair_component(pair, component, raw):
    index = 0 if component == "x" else 1
    pair[index] = num(raw)


def set_color_component(values, component, raw):
    index = {"r": 0, "g": 1, "b": 2, "a": 3}[component]
    values[index] = num(raw)


def apply_visual_mods(root, mods):
    for mod in mods:
        prop, value, target = mod["property"], mod["value"], mod["target"]
        for node in iter_visual_nodes(root):
            if node.get("_asset") != mod["targetGuid"]:
                continue
            if target == node.get("_go"):
                if prop == "m_Name":
                    node["name"] = value
                elif prop == "m_IsActive":
                    node["active"] = value == "1"
            if target == node.get("_rt"):
                pairs = {
                    "m_SizeDelta": "size",
                    "m_AnchoredPosition": "pos",
                    "m_AnchorMin": "anchorMin",
                    "m_AnchorMax": "anchorMax",
                    "m_Pivot": "pivot",
                    "m_LocalScale": "scale",
                }
                for unity_name, key in pairs.items():
                    if prop in (unity_name + ".x", unity_name + ".y"):
                        set_pair_component(node["rect"][key], prop[-1], value)
                if prop == "m_LocalEulerAnglesHint.z":
                    node["rect"]["rotation"] = num(value)
            if node.get("image") and target == node.get("_image"):
                if prop == "m_Sprite" and mod["refFileId"] != "0":
                    node["image"]["spriteRef"] = {
                        "fileId": mod["refFileId"], "guid": mod["refGuid"]}
                elif prop.startswith("m_Color."):
                    set_color_component(node["image"]["color"], prop[-1], value)
                elif prop == "m_FillAmount":
                    node["image"]["fillAmount"] = num(value)
            if node.get("text") and target == node.get("_text"):
                if prop in ("m_Text", "m_text"):
                    node["text"]["value"] = value.strip("'\"")
                elif prop in ("m_FontData.m_FontSize", "m_fontSize"):
                    node["text"]["fontSize"] = num(value)
                elif prop in ("m_FontData.m_MaxSize", "m_fontSizeMax"):
                    node["text"]["maxSize"] = num(value)
                elif prop in ("m_FontData.m_MinSize", "m_fontSizeMin"):
                    node["text"]["minSize"] = num(value)
                elif prop.startswith("m_Color.") or prop.startswith("m_fontColor."):
                    set_color_component(node["text"]["color"], prop[-1], value)
    return root


def resolve_visual(prefab, guid_paths, path_guids, asset_paths, cache, depth=0):
    key = str(prefab)
    if key in cache:
        return copy.deepcopy(cache[key])
    if depth > MAX_VARIANT_DEPTH + 4:
        raise ValueError("visual prefab nesting too deep")

    text = prefab.read_text(encoding="utf-8", errors="replace")
    prefab_guid = path_guids.get(str(prefab), "")
    gos, rts, stripped_rts, monos, instances = {}, {}, {}, [], {}
    for classid, fid, stripped, body in parse_docs(text):
        if classid == "1":
            gos[fid] = {
                "name": scalar(body, "m_Name", fid),
                "active": scalar(body, "m_IsActive", "1") == "1",
            }
        elif classid == "224":
            if stripped:
                stripped_rts[fid] = ref(body, "m_PrefabInstance")
            else:
                rts[fid] = {
                    "go": ref(body, "m_GameObject"),
                    "father": ref(body, "m_Father"),
                    "children": parse_children(body),
                    "body": body,
                }
        elif classid == "114" and not stripped:
            script = re.search(r"m_Script: \{fileID: -?\d+, guid: ([0-9a-f]{32})", body)
            guid = script.group(1) if script else ""
            monos.append({"id": fid, "go": ref(body, "m_GameObject"), "guid": guid, "body": body})
        elif classid == "1001":
            source = re.search(r"m_SourcePrefab: \{fileID: -?\d+, guid: ([0-9a-f]{32})", body)
            instances[fid] = {
                "source": source.group(1) if source else "",
                "parent": ref(body, "m_TransformParent"),
                "mods": parse_full_mods(body),
            }

    root_id = next((fid for fid, rt in rts.items() if rt["father"] in (None, "0")), None)
    if root_id is None:
        variant = next((item for item in instances.values()
                        if item["parent"] == "0" and item["source"]), None)
        if not variant or variant["source"] not in guid_paths:
            return None
        tree = resolve_visual(
            guid_paths[variant["source"]], guid_paths, path_guids,
            asset_paths, cache, depth + 1)
        if tree:
            apply_visual_mods(tree, variant["mods"])
            cache[key] = copy.deepcopy(tree)
        return tree

    def node_for(rt_id):
        rt = rts[rt_id]
        go_id = rt["go"]
        go = gos.get(go_id, {"name": go_id, "active": True})
        components = [item for item in monos if item["go"] == go_id]
        node = {
            "_asset": prefab_guid,
            "_go": go_id,
            "_rt": rt_id,
            "name": go["name"],
            "active": go["active"],
            "rect": visual_rect(rt["body"]),
            "children": [],
        }
        image_component = next(
            (item for item in components if classify_mono(item["body"], item["guid"]) == "image"), None)
        if image_component:
            node["_image"] = image_component["id"]
            node["image"] = visual_image(image_component["body"])
        text_component = next(
            (item for item in components
             if classify_mono(item["body"], item["guid"]) in ("text", "tmp")), None)
        if text_component:
            kind = classify_mono(text_component["body"], text_component["guid"])
            node["_text"] = text_component["id"]
            node["text"] = visual_text(text_component["body"], kind)
            effect = next((item for item in components if "m_EffectColor:" in item["body"]), None)
            if effect:
                distance_match = re.search(r"^\s*m_EffectDistance: \{([^}]+)\}", effect["body"], re.M)
                node["text"]["effect"] = {
                    "color": color(effect["body"], "m_EffectColor"),
                    "distance": vec4_values(distance_match.group(1) if distance_match else None)[:2],
                }
        layout_component = next(
            (item for item in components if "m_ChildControlWidth:" in item["body"]), None)
        if layout_component:
            body = layout_component["body"]
            node["layout"] = {
                "direction": "horizontal",
                "padding": [
                    int(scalar(body, "m_Left", "0")),
                    int(scalar(body, "m_Right", "0")),
                    int(scalar(body, "m_Top", "0")),
                    int(scalar(body, "m_Bottom", "0")),
                ],
                "alignment": int(scalar(body, "m_ChildAlignment", "0")),
                "spacing": num(scalar(body, "m_Spacing", "0")),
                "controlWidth": scalar(body, "m_ChildControlWidth", "1") == "1",
                "controlHeight": scalar(body, "m_ChildControlHeight", "1") == "1",
                "expandWidth": scalar(body, "m_ChildForceExpandWidth", "0") == "1",
                "expandHeight": scalar(body, "m_ChildForceExpandHeight", "0") == "1",
                "reverse": scalar(body, "m_ReverseArrangement", "0") == "1",
            }
        layout_element = next(
            (item for item in components
             if "m_PreferredWidth:" in item["body"] and "m_IgnoreLayout:" in item["body"]), None)
        if layout_element:
            body = layout_element["body"]
            node["layoutElement"] = {
                "ignore": scalar(body, "m_IgnoreLayout", "0") == "1",
                "minWidth": num(scalar(body, "m_MinWidth", "-1")),
                "minHeight": num(scalar(body, "m_MinHeight", "-1")),
                "preferredWidth": num(scalar(body, "m_PreferredWidth", "-1")),
                "preferredHeight": num(scalar(body, "m_PreferredHeight", "-1")),
                "flexibleWidth": num(scalar(body, "m_FlexibleWidth", "-1")),
                "flexibleHeight": num(scalar(body, "m_FlexibleHeight", "-1")),
            }
        for child_id in rt["children"]:
            if child_id in rts:
                node["children"].append(node_for(child_id))
            elif child_id in stripped_rts:
                instance = instances.get(stripped_rts[child_id])
                if not instance or instance["source"] not in guid_paths:
                    continue
                child = resolve_visual(
                    guid_paths[instance["source"]], guid_paths, path_guids,
                    asset_paths, cache, depth + 1)
                if child:
                    apply_visual_mods(child, instance["mods"])
                    node["children"].append(child)
        return node

    tree = node_for(root_id)
    cache[key] = copy.deepcopy(tree)
    return tree


def finalize_visual(root, asset_paths, sprite_cache):
    if not root:
        return None
    for node in iter_visual_nodes(root):
        image = node.get("image")
        if image:
            sprite_ref = image.pop("spriteRef", None)
            guid = sprite_ref.get("guid") if sprite_ref else None
            path = asset_paths.get(guid) if guid else None
            image["sprite"] = texture_metadata(path, sprite_ref["fileId"], sprite_cache) if path else None
        text = node.get("text")
        if text:
            font_ref = text.pop("fontRef", None)
            font_path = asset_paths.get(font_ref.get("guid")) if font_ref else None
            text["font"] = ("../../" + font_path.relative_to(ROOT).as_posix()) if font_path else None
        for key in list(node):
            if key.startswith("_"):
                del node[key]
    return root


def resolve(prefab: Path, guid_paths: dict, cache: dict, depth=0):
    """extract() + recursive variant resolution with root-targeted overrides."""
    key = str(prefab)
    if key in cache:
        return cache[key]
    if depth > MAX_VARIANT_DEPTH:
        raise ValueError("variant chain too deep")
    rec = extract(prefab, guid_paths)
    if rec and "variant" in rec:
        base_path = guid_paths.get(rec["variant"])
        if base_path is None or not base_path.exists():
            raise ValueError(f"variant of unknown base guid {rec['variant'][:8]}…")
        base = resolve(base_path, guid_paths, cache, depth + 1)
        if base is None:
            raise ValueError(f"variant base {base_path.stem} has no UI root")
        merged = json.loads(json.dumps({k: v for k, v in base.items()}))
        merged["variantOf"] = base_path.stem
        root_rt = str(base.get("_rootRT", ""))
        size = list(merged["size"])
        sample = fontsize = None
        for target, prop, value in rec["mods"]:
            if target == root_rt and prop == "m_SizeDelta.x":
                size[0] = num(value)
            elif target == root_rt and prop == "m_SizeDelta.y":
                size[1] = num(value)
            elif prop in ("m_Text", "m_text") and sample is None and value.strip():
                sample = value.strip().strip("'\"")[:40]
            elif prop in ("m_FontData.m_FontSize", "m_fontSize") and fontsize is None:
                fontsize = num(value)
        merged["size"] = size
        if merged["texts"] and (sample or fontsize):
            if sample:
                merged["texts"][0]["sample"] = sample
            if fontsize:
                merged["texts"][0]["fontSize"] = fontsize
        rec = merged
    cache[key] = rec
    return rec


def build_css(kit):
    lines = [
        "/* generated by .claude/scripts/ui-kit-sync.py — do not hand-edit.",
        "   v0 wireframe: real geometry/colors, no sprites. Inline this whole file",
        "   into each mockup HTML (self-contained contract, survives moves). */",
        "*{box-sizing:border-box;margin:0;padding:0}",
        "body{background:#0d0d16;font-family:-apple-system,'Segoe UI',Roboto,Arial,sans-serif}",
        f".stage{{position:relative;width:{DESIGN_W}px;height:{DESIGN_H}px;overflow:hidden;"
        "background:#151527;color:#fff}",
        ".col{display:flex;flex-direction:column}",
        ".row{display:flex;flex-direction:row}",
        ".abs{position:absolute}",
        ".dim{position:absolute;inset:0;background:rgba(0,0,0,.6)}",
        ".tpl{position:relative;display:flex;align-items:center;justify-content:center;"
        "flex:none;border:2px dashed rgba(255,255,255,.3);color:#fff;text-align:center;"
        "font-size:var(--font,32px);overflow:hidden}",
        ".tpl:empty::after{content:attr(data-tpl);opacity:.7;font-size:22px;padding:4px}",
    ]
    for name, rec in kit.items():
        w, h = rec["size"]
        if w == 0 and h == 0:
            # Screen-root templates (FeatureTemplate/PackageTemplate) are sized
            # by UIManager at runtime — in a mockup the .stage plays that role.
            props = [f"width:{DESIGN_W}px", f"height:{DESIGN_H}px"]
        else:
            # A single zero axis = layout-driven (LayoutGroup sizes it at
            # runtime) — let content size it in the mockup instead of 0px.
            props = [(f"width:{w}px" if w else "width:auto;min-width:60px"),
                     (f"height:{h}px" if h else "height:auto;min-height:60px")]
        props.append(f"background:{rec['background'] or 'rgba(255,255,255,0.08)'}")
        fs = next((t["fontSize"] for t in rec["texts"] if t["fontSize"]), None)
        if fs:
            props.append(f"--font:{fs}px")
        tc = next((t["color"] for t in rec["texts"] if t["color"]), None)
        bga = rec.get("backgroundRGBA")
        if tc:
            props.append(f"color:{css_rgba(tc)}")
        elif bga and bga[3] >= 0.5 and (bga[0] + bga[1] + bga[2]) / 3 > 0.7:
            props.append("color:#222")  # readable label on a bright wireframe fill
        lines.append(f".tpl-{name}{{{';'.join(props)}}}")
    return "\n".join(lines) + "\n"


def build_preview(kit):
    cards = []
    for name, rec in kit.items():
        w, h = rec["size"]
        variant = f" · variant of {rec['variantOf']}" if rec.get("variantOf") else ""
        layout_driven = " · layout-driven" if not w or not h else ""
        cards.append(
            f'<article class="card" data-template="{name}" data-name="{name.lower()}">'
            f'<button class="preview" type="button" aria-label="Inspect {name}">'
            '<span class="checker"></span><span class="prefab-host"></span>'
            '<span class="inspect" aria-hidden="true">Inspect</span></button>'
            f'<div class="meta"><div><b>{name}</b><span>{w}×{h}{layout_driven}{variant}</span></div>'
            f'<code>{len(rec["visual"]["children"]) if rec.get("visual") else 0} root children</code>'
            '</div></article>')
    payload = json.dumps(kit, ensure_ascii=False, separators=(",", ":")).replace("</", "<\\/")
    template = (OUT_DIR / "kit-preview.template.html").read_text(encoding="utf-8")
    return (template.replace("__COUNT__", str(len(kit)))
            .replace("__CARDS__", "".join(cards))
            .replace("__KIT__", payload))


def check():
    """Report whether the committed kit still matches the prefabs + usage notes.

    Exists so staleness is detectable without regenerating: bootstrap, the skill
    and any gate can ask, and only a human decides to rewrite generated files.
    `sourceHash` covers the prefabs alone, so an edited usage note would slip
    past it — the recorded notes are therefore compared directly.
    """
    kit_file = OUT_DIR / "ui-kit.json"
    def report(state, detail, code):
        print(json.dumps({"ok": code == 0, "state": state, "detail": detail,
                          "kit": str(kit_file.relative_to(ROOT)),
                          "regenerate": "python3 .claude/scripts/ui-kit-sync.py"},
                         ensure_ascii=False, indent=2))
        return code

    if not TEMPLATES_DIR.is_dir():
        # A toolchain checkout with no UI prefabs yet is not a failure state.
        return report("no-templates", f"{TEMPLATES_REL} does not exist", 0)

    # Before anything else: a broken usage file has to be reported as itself,
    # because the obvious response to any other state is "regenerate" — which
    # here would quietly produce a kit stripped of every composition rule.
    usage_errors = []
    usage = load_usage(usage_errors)
    if usage_errors:
        return report("usage-invalid", "; ".join(usage_errors), 1)

    try:
        payload = json.loads(kit_file.read_text(encoding="utf-8"))
    except FileNotFoundError:
        return report("missing", "kit has never been generated", 1)
    except (OSError, json.JSONDecodeError) as error:
        return report("unreadable", str(error), 1)

    recorded = payload.get("_meta", {}).get("sourceHash")
    if recorded != source_hash():
        return report("stale", "prefabs changed since the kit was generated", 1)

    templates = payload.get("templates", {})
    # Only templates that actually exist are compared: a note naming a template
    # this project does not have is a bad entry, not staleness — regenerating
    # would never clear it. Those surface as `_meta.usageUnknown` instead.
    drifted = sorted(name for name, rec in templates.items()
                     if rec.get("usage") != usage.get(name))
    if drifted:
        return report("stale", f"usage notes changed: {', '.join(drifted)}", 1)
    return report("fresh", f"{len(templates)} template(s) in sync", 0)


def main():
    if any(a in ("--check", "-check") for a in sys.argv[1:]):
        return check()
    if not TEMPLATES_DIR.is_dir():
        print(f"templates dir not found: {TEMPLATES_DIR}", file=sys.stderr)
        return 1
    guid_paths = guid_path_map()
    asset_paths = asset_guid_path_map()
    path_guids = {str(path): guid for guid, path in asset_paths.items()}
    usage = load_usage()
    kit, skipped, cache, visual_cache, sprite_cache = {}, [], {}, {}, {}
    for prefab in sorted(TEMPLATES_DIR.glob("*.prefab")):
        try:
            rec = resolve(prefab, guid_paths, cache)
        except Exception as e:  # one broken prefab must not sink the whole kit
            skipped.append(f"{prefab.stem}: {e}")
            continue
        if rec is None:
            skipped.append(f"{prefab.stem}: no RectTransform root (not a UI prefab)")
            continue
        public_rec = {k: v for k, v in rec.items() if not k.startswith("_")}
        if prefab.stem in usage:
            public_rec["usage"] = usage[prefab.stem]
        try:
            visual = resolve_visual(
                prefab, guid_paths, path_guids, asset_paths, visual_cache)
            public_rec["visual"] = finalize_visual(visual, asset_paths, sprite_cache)
        except Exception as error:
            public_rec["visual"] = None
            skipped.append(f"{prefab.stem} visual: {error}")
        kit[prefab.stem] = public_rec

    OUT_DIR.mkdir(parents=True, exist_ok=True)
    # A note whose template no longer exists (renamed, deleted, or a typo) would
    # otherwise vanish silently — the very drift this kit keeps having.
    usage_unknown = sorted(set(usage) - set(kit))
    payload = {
        "_meta": {
            "source": TEMPLATES_REL,
            "designResolution": [DESIGN_W, DESIGN_H],
            "fidelity": "v0-wireframe",
            "previewFidelity": "v1-prefab-assets",
            "sourceHash": source_hash(),
            "count": len(kit),
            "skipped": skipped,
            "regenerate": "python3 .claude/scripts/ui-kit-sync.py",
            # Absent when empty, so the common case keeps the same shape.
            **({"usageUnknown": usage_unknown} if usage_unknown else {}),
        },
        # Keep the public mockup contract compact/backward-compatible. The
        # richer visual tree is embedded only in kit-preview.html.
        "templates": {
            name: {key: value for key, value in rec.items() if key != "visual"}
            for name, rec in kit.items()
        },
    }
    (OUT_DIR / "ui-kit.json").write_text(
        json.dumps(payload, ensure_ascii=False, indent=2, sort_keys=True) + "\n",
        encoding="utf-8")
    (OUT_DIR / "ui-kit.css").write_text(build_css(kit), encoding="utf-8")
    (OUT_DIR / "kit-preview.html").write_text(build_preview(kit), encoding="utf-8")
    print(json.dumps({"ok": True, "templates": len(kit), "skipped": skipped,
                      "usageUnknown": usage_unknown,
                      "out": str(OUT_DIR.relative_to(ROOT))}, ensure_ascii=False, indent=2))
    return 0


if __name__ == "__main__":
    sys.exit(main())
