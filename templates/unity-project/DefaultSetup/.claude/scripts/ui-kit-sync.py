#!/usr/bin/env python3
"""ui-kit-sync — bake the visual UI-kit FROM THE UI CATALOG.

The UGUI SSOT of this repo is ui-catalog/ui-tokens.json (authored via
UiCatalogConfig, baked by the editor exporter — see CLAUDE.md "UI Token
Catalog"). This script derives the mockup kit from it: every catalog token
becomes one kit template (template name == token id, e.g. "ui.currency.single"),
and its geometry/colors are extracted from the token's real prefab at
`assetPath` (Unity YAML, parsed with regexes — same zero-dependency approach
as backlog-preflight.py; PyYAML can't read Unity's `!u!` tags).

Writes to ui-catalog/ (same home as the catalog it derives from):

  ui-kit.json       real numbers per token: root size/anchors/pivot,
                    background color, text nodes (font size/color/sample),
                    direct children, nested prefab references, plus catalog
                    metadata (prefab stem, category, controller,
                    contentContainer).
  ui-kit.css        one `.tpl-<css-safe token>` class per token carrying the
                    real dimensions + colors, plus the shared wireframe base
                    (.stage 1080x2400, .tpl, .col/.row flex helpers) that
                    mockup HTML inlines.
  kit-preview.html  gallery of every token composed from its prefab Images at
                    true proportions — open once
                    after each refresh to sanity-check the extraction.
  preview-assets/   generated browser-compatible PNGs for PSD and sliced
                    sprite-sheet sources (never screenshots).

Prefab Variants (root = PrefabInstance with m_TransformParent {fileID: 0})
are resolved recursively against their base prefab (searched under
Assets/_Project/**), then root-targeted m_SizeDelta / text overrides are
applied on top.

The JSON/CSS kit stays v0 wireframe-compatible. The gallery additionally
resolves prefab Images through their sprite GUIDs: PSDs and sprite-sheet
regions are converted losslessly to generated PNGs, and Unity Image type 1 is
rendered with CSS border-image (9-slice).

Deterministic output (sorted names, no timestamps) → clean git diffs.
Run whenever the catalog is re-exported or template prefabs change:
    python3 .claude/scripts/ui-kit-sync.py
"""

import hashlib
import html
import json
import os
import re
import shutil
import sys
from pathlib import Path
from urllib.parse import quote

ROOT = Path(__file__).resolve().parents[2]
CATALOG_JSON = ROOT / "ui-catalog" / "ui-tokens.json"
PREFABS_ROOT = ROOT / "Assets" / "_Project"
OUT_DIR = ROOT / "ui-catalog"
PREVIEW_ASSETS_DIR = OUT_DIR / "preview-assets"
DESIGN_W, DESIGN_H = 1080, 2400
MAX_VARIANT_DEPTH = 5

DOC_RE = re.compile(r"^--- !u!(\d+) &(-?\d+)( stripped)?\s*$", re.M)
GUID_IN_META_RE = re.compile(r"^guid: ([0-9a-f]{32})", re.M)
MOD_ENTRY_RE = re.compile(
    r"- target: \{fileID: (-?\d+), guid: [0-9a-f]{32}, type: \d+\}\n"
    r"\s+propertyPath: (\S+)\n"
    r"\s+value: ([^\n]*)\n"
    r"\s+objectReference: \{fileID: (-?\d+)"
    r"(?:, guid: ([0-9a-f]{32}), type: \d+)?\}")
CSS_SAFE_RE = re.compile(r"[^A-Za-z0-9_-]")

# Built-in UI script GUIDs (stable across Unity versions); used as a shortcut,
# with field-signature detection as the robust fallback.
GUID_IMAGE = "fe87c0e1cc204ed48ad3b37840f39efc"
GUID_TEXT = "5f7201a12d95ffc409449d95f23cf332"
GUID_TMP = "f4688fdb7df04437aeb418b961361dc5"


def css_class(name):
    return CSS_SAFE_RE.sub("-", name)


def load_tokens():
    payload = json.loads(CATALOG_JSON.read_text(encoding="utf-8"))
    return payload.get("tokens", [])


def source_hash(tokens):
    """Hash the catalog JSON plus every referenced prefab (+ .meta), sorted by
    token id. Must stay byte-identical to ui_spec_common.kit_source_hash()."""
    digest = hashlib.sha256()
    digest.update(CATALOG_JSON.read_bytes())
    digest.update(b"\0")
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
    if (re.search(r"^\s*m_Texture:", body, re.M)
            and re.search(r"^\s*m_UVRect:", body, re.M)):
        return "rawimage"
    return None


def text_sample(body, key):
    m = re.search(rf"^\s*{key}: (.*)$", body, re.M)
    if not m:
        return None
    s = m.group(1).strip().strip("'\"")
    return s[:40] if s else None


def guid_path_map():
    """guid → prefab Path for every prefab under Assets/_Project/**, so
    variant bases and nested PrefabInstances resolve to real files/names."""
    out = {}
    for meta in PREFABS_ROOT.rglob("*.prefab.meta"):
        m = GUID_IN_META_RE.search(meta.read_text(encoding="utf-8", errors="replace"))
        if m:
            out[m.group(1)] = meta.with_suffix("")  # strip .meta → .prefab path
    return out


def asset_guid_path_map():
    """guid -> source asset for browser-renderable sprite resolution."""
    out = {}
    for meta in (ROOT / "Assets").rglob("*.meta"):
        try:
            source = meta.read_text(encoding="utf-8", errors="replace")
        except OSError:
            continue
        m = GUID_IN_META_RE.search(source)
        if m:
            out[m.group(1)] = meta.with_suffix("")
    return out


def unity_vec4(body, name):
    m = re.search(
        rf"^\s*{name}: \{{x: (-?[\d.eE+-]+), y: (-?[\d.eE+-]+), "
        rf"z: (-?[\d.eE+-]+), w: (-?[\d.eE+-]+)\}}",
        body, re.M)
    return [num(m.group(i)) for i in range(1, 5)] if m else [0, 0, 0, 0]


def web_url(path):
    return quote(Path(os.path.relpath(path, OUT_DIR)).as_posix(), safe="/._-")


def sprite_info(guid, file_id, asset_paths, cache):
    """Resolve a Unity sprite reference to a browser PNG plus its 9-slice border.

    Unity rects use a bottom-left origin; Pillow uses top-left, so multi-sprite
    atlas regions are flipped on Y while being extracted.
    """
    key = (guid, str(file_id))
    if key in cache:
        return cache[key]
    source = asset_paths.get(guid)
    if not source or not source.is_file():
        cache[key] = None
        return None
    meta_path = source.with_suffix(source.suffix + ".meta")
    if not meta_path.is_file():
        cache[key] = None
        return None
    meta = meta_path.read_text(encoding="utf-8", errors="replace")
    mode_match = re.search(r"^\s*spriteMode: (\d+)", meta, re.M)
    sprite_mode = int(mode_match.group(1)) if mode_match else 1
    border = unity_vec4(meta, "spriteBorder")
    crop = None
    sprite_name = source.stem

    if sprite_mode == 2:
        blocks = re.findall(
            r"^    - serializedVersion: 2\n(.*?)(?=^    - serializedVersion: 2\n|^    outline:)",
            meta, re.M | re.S)
        wanted = str(file_id)
        selected = next((block for block in blocks
                         if re.search(rf"^\s*internalID: {re.escape(wanted)}\s*$", block, re.M)), None)
        if selected:
            name_match = re.search(r"^\s*name: (.+)$", selected, re.M)
            sprite_name = name_match.group(1).strip() if name_match else sprite_name
            rect_match = re.search(
                r"^\s*rect:\n\s*serializedVersion: \d+\n"
                r"\s*x: (-?[\d.]+)\n\s*y: (-?[\d.]+)\n"
                r"\s*width: ([\d.]+)\n\s*height: ([\d.]+)", selected, re.M)
            if rect_match:
                crop = [int(round(float(rect_match.group(i)))) for i in range(1, 5)]
            border = unity_vec4(selected, "border")

    direct_web = source.suffix.lower() in (".png", ".jpg", ".jpeg") and crop is None
    if direct_web:
        # Keep every browser dependency beside the generated gallery. Apart
        # from making it portable, this avoids file:// parent-path restrictions
        # in Safari Quick Look and some Chromium configurations.
        PREVIEW_ASSETS_DIR.mkdir(parents=True, exist_ok=True)
        output = PREVIEW_ASSETS_DIR / f"{guid}-{file_id}{source.suffix.lower()}"
        shutil.copyfile(source, output)
        result = {"url": web_url(output), "asset": source.relative_to(ROOT).as_posix(),
                  "sprite": sprite_name, "border": border}
        cache[key] = result
        return result

    # Browsers cannot decode PSD, and an atlas reference must expose only its
    # selected sprite. This is deterministic source extraction, not a capture.
    try:
        from PIL import Image
        with Image.open(source) as image:
            image.load()
            if crop:
                x, y, width, height = crop
                image = image.crop((x, image.height - y - height, x + width, image.height - y))
            image = image.convert("RGBA")
            PREVIEW_ASSETS_DIR.mkdir(parents=True, exist_ok=True)
            output = PREVIEW_ASSETS_DIR / f"{guid}-{file_id}.png"
            image.save(output, "PNG", optimize=True)
    except Exception:
        cache[key] = None
        return None
    result = {"url": web_url(output), "asset": source.relative_to(ROOT).as_posix(),
              "sprite": sprite_name, "border": border}
    cache[key] = result
    return result


def extract(prefab: Path, guid_paths: dict, asset_paths: dict, sprite_cache: dict):
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
            monos.append({"id": fid, "go": ref(body, "m_GameObject"),
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
        # lives on a direct child. Borrow its color.
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
            mods = {p: v for _, p, v, _, _ in inst.get("mods", [])}
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

    # Recreate the prefab's local Image composition. This intentionally uses
    # serialized prefab data only: no editor camera, screenshot or thumbnail.
    display_w = w or DESIGN_W
    display_h = h or DESIGN_H
    layout = {}

    def walk_layout(rt_id, left, top, width, height, parent_active=True):
        rt = rts[rt_id]
        go = gos.get(rt["go"], {})
        active = parent_active and go.get("active", True)
        layout[rt_id] = [left, top, width, height, active]
        for child_id in rt["children"]:
            child = rts.get(child_id)
            if not child:
                continue
            amin = child["anchorMin"] or [0.5, 0.5]
            amax = child["anchorMax"] or amin
            sd = child["size"] or [0, 0]
            pos = child["pos"] or [0, 0]
            pivot = child["pivot"] or [0.5, 0.5]
            child_w = max(0, width * (amax[0] - amin[0]) + sd[0])
            child_h = max(0, height * (amax[1] - amin[1]) + sd[1])
            anchor_x = width * (amin[0] + amax[0]) / 2
            anchor_y = height * (amin[1] + amax[1]) / 2
            child_left = left + anchor_x + pos[0] - pivot[0] * child_w
            child_top = top + height - anchor_y - pos[1] - (1 - pivot[1]) * child_h
            walk_layout(child_id, child_left, child_top, child_w, child_h, active)

    walk_layout(root_id, 0, 0, display_w, display_h)
    go_to_rt = {rt["go"]: rt_id for rt_id, rt in rts.items()}
    images = []
    for mb in monos:
        kind = classify_mono(mb["body"], mb["guid"])
        if kind not in ("image", "rawimage"):
            continue
        rt_id = go_to_rt.get(mb["go"])
        rect = layout.get(rt_id)
        if not rect or rect[2] <= 0 or rect[3] <= 0:
            continue
        reference_name = "m_Sprite" if kind == "image" else "m_Texture"
        sprite_match = re.search(
            rf"^\s*{reference_name}: \{{fileID: (-?\d+), guid: ([0-9a-f]{{32}})",
            mb["body"], re.M)
        if not sprite_match:
            continue
        sprite = sprite_info(sprite_match.group(2), sprite_match.group(1),
                             asset_paths, sprite_cache)
        if not sprite:
            continue
        type_match = re.search(r"^\s*m_Type: (\d+)", mb["body"], re.M)
        preserve_match = re.search(r"^\s*m_PreserveAspect: (\d+)", mb["body"], re.M)
        center_match = re.search(r"^\s*m_FillCenter: (\d+)", mb["body"], re.M)
        ppu_match = re.search(r"^\s*m_PixelsPerUnitMultiplier: (-?[\d.]+)", mb["body"], re.M)
        active_chain = []
        cursor = rt_id
        while cursor in rts:
            cursor_rt = rts[cursor]
            cursor_go = cursor_rt["go"]
            active_chain.append([cursor_go, gos.get(cursor_go, {}).get("active", True)])
            cursor = cursor_rt["father"]
        images.append({
            "node": go_name(mb["go"]),
            "rect": [round(v, 2) for v in rect[:4]],
            "sprite": sprite,
            "imageType": int(type_match.group(1)) if type_match and kind == "image" else 0,
            "preserveAspect": bool(int(preserve_match.group(1))) if preserve_match else False,
            "fillCenter": bool(int(center_match.group(1))) if center_match else True,
            "pixelsPerUnitMultiplier": float(ppu_match.group(1)) if ppu_match else 1,
            "color": color(mb["body"]),
            "active": rect[4],
            "enabled": not bool(re.search(r"^\s*m_Enabled: 0\s*$", mb["body"], re.M)),
            "_component": mb["id"],
            "_rt": rt_id,
            "_go": mb["go"],
            "_activeChain": active_chain,
        })

    rec = {"size": [w, h],
           "background": css_rgba(bg, None) if bg else None,
           "texts": texts[:6],
           "children": children,
           "images": images,
           "nested": nested,
           "_rootRT": root_id,
           "_rootRTData": root,
           "_layout": layout,
           "_nestedInstances": [
               {"source": inst["source"], "parent": inst["parent"], "mods": inst["mods"]}
               for inst in instances.values() if inst["source"] and inst["parent"] not in (None, "0")
           ]}
    if stretch_w or stretch_h:
        rec["stretch"] = True
    if bg:
        rec["backgroundRGBA"] = bg
    if root["pivot"] and root["pivot"] != [0.5, 0.5]:
        rec["pivot"] = root["pivot"]
    return rec


def resolve(prefab: Path, guid_paths: dict, asset_paths: dict, sprite_cache: dict,
            cache: dict, depth=0):
    """extract() + recursive variant resolution with root-targeted overrides."""
    key = str(prefab)
    if key in cache:
        return cache[key]
    if depth > MAX_VARIANT_DEPTH:
        raise ValueError("variant chain too deep")
    rec = extract(prefab, guid_paths, asset_paths, sprite_cache)
    if rec and "variant" in rec:
        base_path = guid_paths.get(rec["variant"])
        if base_path is None or not base_path.exists():
            raise ValueError(f"variant of unknown base guid {rec['variant'][:8]}…")
        base = resolve(base_path, guid_paths, asset_paths, sprite_cache, cache, depth + 1)
        if base is None:
            raise ValueError(f"variant base {base_path.stem} has no UI root")
        merged = json.loads(json.dumps({k: v for k, v in base.items()}))
        merged["variantOf"] = base_path.stem
        root_rt = str(base.get("_rootRT", ""))
        size = list(merged["size"])
        sample = fontsize = None
        active_overrides = {}
        unmatched_sprite_overrides = []
        override_props = {}
        old_w, old_h = merged["size"]
        for target, prop, value, object_file, object_guid in rec["mods"]:
            override_props.setdefault(target, {})[prop] = value
            if target == root_rt and prop == "m_SizeDelta.x":
                size[0] = num(value)
            elif target == root_rt and prop == "m_SizeDelta.y":
                size[1] = num(value)
            elif prop in ("m_Text", "m_text") and sample is None and value.strip():
                sample = value.strip().strip("'\"")[:40]
            elif prop in ("m_FontData.m_FontSize", "m_fontSize") and fontsize is None:
                fontsize = num(value)
            if prop == "m_IsActive":
                active_overrides[target] = value == "1"
            matched_image = False
            for image in merged.get("images", []):
                if target != image.get("_component"):
                    continue
                matched_image = True
                if prop in ("m_Sprite", "m_Texture"):
                    replacement = (sprite_info(object_guid, object_file, asset_paths, sprite_cache)
                                   if object_guid and object_file != "0" else None)
                    image["sprite"] = replacement
                    if replacement is None:
                        image["enabled"] = False
                elif prop == "m_Type":
                    image["imageType"] = int(value)
                elif prop == "m_PreserveAspect":
                    image["preserveAspect"] = value == "1"
                elif prop == "m_FillCenter":
                    image["fillCenter"] = value == "1"
                elif prop == "m_Enabled":
                    image["enabled"] = value == "1"
                elif prop == "m_PixelsPerUnitMultiplier":
                    image["pixelsPerUnitMultiplier"] = float(value)
                elif prop.startswith("m_Color."):
                    channel = {"r": 0, "g": 1, "b": 2, "a": 3}.get(prop.rsplit(".", 1)[-1])
                    if channel is not None:
                        image["color"] = list(image.get("color") or [1, 1, 1, 1])
                        image["color"][channel] = float(value)
            if (prop in ("m_Sprite", "m_Texture") and not matched_image
                    and object_guid and object_file != "0"):
                replacement = sprite_info(object_guid, object_file, asset_paths, sprite_cache)
                if replacement:
                    unmatched_sprite_overrides.append((target, replacement))
        merged["size"] = size
        old_display_w, old_display_h = old_w or DESIGN_W, old_h or DESIGN_H
        new_display_w, new_display_h = size[0] or DESIGN_W, size[1] or DESIGN_H
        scale_x = new_display_w / old_display_w if old_display_w else 1
        scale_y = new_display_h / old_display_h if old_display_h else 1
        for image in merged.get("images", []):
            if scale_x != 1 or scale_y != 1:
                x, y, width, height = image["rect"]
                image["rect"] = [round(x * scale_x, 2), round(y * scale_y, 2),
                                 round(width * scale_x, 2), round(height * scale_y, 2)]
            if active_overrides:
                chain = image.get("_activeChain", [])
                for state in chain:
                    if state[0] in active_overrides:
                        state[1] = active_overrides[state[0]]
                image["active"] = all(state[1] for state in chain)
        # Unity assigns virtual fileIDs to objects added by an intermediate
        # prefab variant. Those IDs do not occur in the source YAML of that
        # intermediate prefab. Button variants follow a stable convention:
        # the final unmatched sprite is the face/btn graphic and earlier ones
        # are centered icon overlays. Preserve those virtual IDs on the layers
        # so the next variant in the chain can override them again.
        if unmatched_sprite_overrides and "/Button_Template/" in prefab.as_posix():
            root_width, root_height = new_display_w, new_display_h
            icon_overrides = unmatched_sprite_overrides
            if len(unmatched_sprite_overrides) >= 2:
                background_target, background_sprite = unmatched_sprite_overrides[-1]
                candidates = [image for image in merged.get("images", [])
                              if image.get("node", "").lower() in ("btn", "background", "bg")]
                if candidates:
                    background = max(candidates, key=lambda image: image["rect"][2] * image["rect"][3])
                    background["sprite"] = background_sprite
                    background["_component"] = background_target
                    props = override_props.get(background_target, {})
                    if "m_Type" in props:
                        background["imageType"] = int(props["m_Type"])
                    icon_overrides = unmatched_sprite_overrides[:-1]
            for icon_target, icon_sprite in icon_overrides:
                # button_template_icon defines a 120px icon in a 160px root;
                # common text+icon buttons use roughly 68% of their height.
                icon_ratio = 0.75 if abs(root_width - root_height) < 1 else 0.68
                side = min(root_width, root_height) * icon_ratio
                props = override_props.get(icon_target, {})
                merged["images"].append({
                    "node": "icon",
                    "rect": [round((root_width - side) / 2, 2),
                             round((root_height - side) / 2, 2),
                             round(side, 2), round(side, 2)],
                    "sprite": icon_sprite,
                    "imageType": int(props.get("m_Type", 0)),
                    "preserveAspect": True,
                    "fillCenter": True,
                    "pixelsPerUnitMultiplier": float(props.get("m_PixelsPerUnitMultiplier", 1)),
                    "color": [1, 1, 1, 1],
                    "active": True,
                    "enabled": True,
                    "_component": icon_target,
                    "_rt": root_rt,
                    "_go": "",
                    "_activeChain": [],
                })
        if merged["texts"] and (sample or fontsize):
            if sample:
                merged["texts"][0]["sample"] = sample
            if fontsize:
                merged["texts"][0]["fontSize"] = fontsize
        rec = merged
    elif rec:
        # Flatten nested prefab Image layers into the owning prefab. Unity
        # serializes nested prefab transforms as PrefabInstance overrides;
        # compose those transforms here so the browser preview follows the
        # same hierarchy instead of showing an empty placeholder.
        nested_images = []
        for nested_instance in rec.get("_nestedInstances", []):
            source_path = guid_paths.get(nested_instance["source"])
            parent_rect = rec.get("_layout", {}).get(nested_instance["parent"])
            if not source_path or not source_path.exists() or not parent_rect or not parent_rect[4]:
                continue
            source = resolve(source_path, guid_paths, asset_paths, sprite_cache,
                             cache, depth + 1)
            if not source:
                continue
            root_rt = str(source.get("_rootRT", ""))
            root_data = json.loads(json.dumps(source.get("_rootRTData", {})))
            mods = {prop: value for target, prop, value, _, _ in nested_instance["mods"]
                    if target == root_rt}
            size_delta = list(root_data.get("size") or source.get("size") or [0, 0])
            position = list(root_data.get("pos") or [0, 0])
            anchor_min = list(root_data.get("anchorMin") or [0.5, 0.5])
            anchor_max = list(root_data.get("anchorMax") or anchor_min)
            pivot = list(root_data.get("pivot") or [0.5, 0.5])
            for axis, key in enumerate(("x", "y")):
                if f"m_SizeDelta.{key}" in mods:
                    size_delta[axis] = num(mods[f"m_SizeDelta.{key}"])
                if f"m_AnchoredPosition.{key}" in mods:
                    position[axis] = num(mods[f"m_AnchoredPosition.{key}"])
            parent_left, parent_top, parent_w, parent_h = parent_rect[:4]
            width = max(0, parent_w * (anchor_max[0] - anchor_min[0]) + size_delta[0])
            height = max(0, parent_h * (anchor_max[1] - anchor_min[1]) + size_delta[1])
            anchor_x = parent_w * (anchor_min[0] + anchor_max[0]) / 2
            anchor_y = parent_h * (anchor_min[1] + anchor_max[1]) / 2
            left = parent_left + anchor_x + position[0] - pivot[0] * width
            top = parent_top + parent_h - anchor_y - position[1] - (1 - pivot[1]) * height
            source_w = source["size"][0] or DESIGN_W
            source_h = source["size"][1] or DESIGN_H
            scale_x = width / source_w if source_w else 1
            scale_y = height / source_h if source_h else 1
            for image in source.get("images", []):
                layer = json.loads(json.dumps(image))
                x, y, image_w, image_h = layer["rect"]
                layer["rect"] = [round(left + x * scale_x, 2),
                                 round(top + y * scale_y, 2),
                                 round(image_w * scale_x, 2),
                                 round(image_h * scale_y, 2)]
                layer["node"] = f"{source_path.stem}/{layer['node']}"
                nested_images.append(layer)
        rec["images"].extend(nested_images)
    cache[key] = rec
    return rec


def build_css(kit):
    lines = [
        "/* generated by .claude/scripts/ui-kit-sync.py — do not hand-edit.",
        "   Derived from ui-catalog/ui-tokens.json (template name == token id).",
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
            # Screen-root tokens (screen.* / popup shells) are sized by
            # UIManager at runtime — in a mockup the .stage plays that role.
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
        lines.append(f".tpl-{css_class(name)}{{{';'.join(props)}}}")
    return "\n".join(lines) + "\n"


def image_layer_html(layer):
    left, top, width, height = layer["rect"]
    url = html.escape(layer["sprite"]["url"], quote=True)
    rgba = layer.get("color") or [1, 1, 1, 1]
    styles = ["position:absolute", f"left:{left}px", f"top:{top}px",
              f"width:{width}px", f"height:{height}px", "pointer-events:none"]
    if rgba[3] < 1:
        styles.append(f"opacity:{rgba[3]}")
    border = layer["sprite"].get("border") or [0, 0, 0, 0]
    if layer["imageType"] == 1 and any(border):
        # Unity border = left,bottom,right,top; CSS = top,right,bottom,left.
        source_slices = [border[3], border[2], border[1], border[0]]
        multiplier = layer.get("pixelsPerUnitMultiplier", 1)
        render_widths = [value * multiplier for value in source_slices]
        render_widths[0] = min(render_widths[0], height / 2)
        render_widths[2] = min(render_widths[2], height / 2)
        render_widths[1] = min(render_widths[1], width / 2)
        render_widths[3] = min(render_widths[3], width / 2)
        values = " ".join(f"{v}px" for v in render_widths)
        slices = " ".join(str(v) for v in source_slices)
        fill = " fill" if layer.get("fillCenter", True) else ""
        styles.extend([
            # url() is single-quoted: this whole string lands in a double-quoted
            # HTML style="…" attribute, so a double-quoted url() would close the
            # attribute early and silently drop the sprite (web_url percent-
            # encodes the path, so it never contains a single quote). border-color
            # is a transparent fallback so a missing sprite degrades to nothing
            # instead of Chrome's solid currentColor frame.
            "border-style:solid", "border-color:transparent",
            f"border-width:{values}",
            f"border-image-source:url('{url}')",
            f"border-image-slice:{slices}{fill}",
            f"border-image-width:{values}", "border-image-repeat:stretch",
        ])
    else:
        styles.extend([f"background-image:url('{url}')", "background-position:center",
                       "background-repeat:repeat" if layer["imageType"] == 2
                       else "background-repeat:no-repeat"])
        if layer["imageType"] != 2:
            styles.append("background-size:contain" if layer["preserveAspect"]
                          else "background-size:100% 100%")
    node = html.escape(layer["node"], quote=True)
    asset = html.escape(layer["sprite"]["asset"], quote=True)
    return f'<div class="image-layer" title="{node} — {asset}" style="{";".join(styles)}"></div>'


def visible_images(rec):
    return [image for image in rec.get("images", [])
            if image.get("active", True) and image.get("enabled", True)
            and image.get("sprite")
            and (image.get("color") or [1, 1, 1, 1])[3] > 0.001]


def build_preview(kit):
    cells = []
    # Reusable ui.* templates are the primary purpose of this gallery; keep
    # full feature screens after them so opening the file lands on useful kits.
    for name, rec in sorted(kit.items(), key=lambda item: (item[0].startswith("screen."), item[0])):
        w, h = rec["size"]
        display_w = w or DESIGN_W
        display_h = h or DESIGN_H
        scale = min(0.42, 300 / max(display_w, 1), 300 / max(display_h, 1))
        sw, sh = round(display_w * scale), round(display_h * scale)
        fs = next((t["fontSize"] for t in rec["texts"] if t["fontSize"]), "—")
        extra = f" · variant of {rec['variantOf']}" if rec.get("variantOf") else ""
        nested = f" · nested: {', '.join(rec['nested'])}" if rec["nested"] else ""
        cat = f" · {rec['category']}" if rec.get("category") else ""
        preview_images = visible_images(rec)
        layers = "".join(image_layer_html(layer) for layer in preview_images)
        image_note = f" · {len(preview_images)} prefab images"
        dimensions = f"{w}×{h}" if w and h else f"runtime {display_w}×{display_h}"
        cells.append(
            f'<div class="cell"><div class="box" style="width:{sw}px;height:{sh}px">'
            f'<div class="tpl prefab-composite tpl-{css_class(name)}" data-tpl="{name}" '
            f'style="width:{display_w}px;height:{display_h}px;transform:scale({scale});'
            f'transform-origin:top left">{layers}</div></div>'
            f'<p><b>{name}</b><br>{dimensions} · font {fs}{image_note}{cat}{extra}{nested}</p></div>')
    image_count = sum(len(visible_images(rec)) for rec in kit.values())
    sliced_count = sum(1 for rec in kit.values() for layer in visible_images(rec)
                       if layer["imageType"] == 1 and any(layer["sprite"].get("border") or []))
    return (
        "<!doctype html><meta charset='utf-8'><title>Unity visual UI-kit (from ui-catalog)</title>\n"
        "<link rel='stylesheet' href='ui-kit.css'>\n"
        "<style>body{padding:24px;color:#eee}h1{margin-bottom:4px}"
        ".hint{opacity:.7;margin-bottom:20px}"
        ".grid{display:flex;flex-wrap:wrap;gap:20px;align-items:flex-start}"
        ".cell{width:320px}.box{overflow:hidden;border:1px solid #333;background:#151527}"
        ".prefab-composite{background-image:none!important}"
        ".cell p{font-size:13px;line-height:1.5;margin-top:6px;opacity:.9}</style>\n"
        "<h1>Visual UI-kit — prefab sprites (derived from ui-catalog)</h1>\n"
        f"<p class='hint'>{len(kit)} tokens · {image_count} Image layers · "
        f"{sliced_count} rendered with 9-slice · no screenshots · "
        "regenerate: <code>python3 .claude/scripts/ui-kit-sync.py</code></p>\n"
        f"<div class='grid'>{''.join(cells)}</div>\n")


def main():
    if not CATALOG_JSON.is_file():
        print(f"catalog not found: {CATALOG_JSON}", file=sys.stderr)
        return 1
    tokens = load_tokens()
    guid_paths = guid_path_map()
    asset_paths = asset_guid_path_map()
    if PREVIEW_ASSETS_DIR.exists():
        shutil.rmtree(PREVIEW_ASSETS_DIR)
    kit, skipped, cache, sprite_cache = {}, [], {}, {}
    for token in sorted(tokens, key=lambda t: t.get("token", "")):
        name = token.get("token")
        asset = ROOT / token.get("assetPath", "")
        if not name:
            continue
        if not asset.is_file():
            skipped.append(f"{name}: assetPath missing ({token.get('assetPath')})")
            continue
        try:
            rec = resolve(asset, guid_paths, asset_paths, sprite_cache, cache)
        except Exception as e:  # one broken prefab must not sink the whole kit
            skipped.append(f"{name}: {e}")
            continue
        if rec is None:
            skipped.append(f"{name}: no RectTransform root (not a UI prefab)")
            continue
        rec = {k: v for k, v in rec.items() if not k.startswith("_")}
        rec["prefab"] = token.get("prefab")
        if token.get("category"):
            rec["category"] = token["category"]
        if token.get("controller"):
            rec["controller"] = token["controller"]
        layout = token.get("layout") or {}
        if layout.get("contentContainer"):
            rec["contentContainer"] = layout["contentContainer"]
        kit[name] = rec

    OUT_DIR.mkdir(parents=True, exist_ok=True)
    # Image layers are gallery implementation details. Keep ui-kit.json's
    # established v0 schema stable for ui-spec/mockup consumers.
    public_kit = {
        name: {key: value for key, value in rec.items() if key != "images"}
        for name, rec in kit.items()
    }
    payload = {
        "_meta": {
            "source": "ui-catalog/ui-tokens.json",
            "designResolution": [DESIGN_W, DESIGN_H],
            "fidelity": "v0-wireframe",
            "sourceHash": source_hash(tokens),
            "count": len(kit),
            "skipped": skipped,
            "regenerate": "python3 .claude/scripts/ui-kit-sync.py",
        },
        "templates": public_kit,
    }
    (OUT_DIR / "ui-kit.json").write_text(
        json.dumps(payload, ensure_ascii=False, indent=2, sort_keys=True) + "\n",
        encoding="utf-8")
    (OUT_DIR / "ui-kit.css").write_text(build_css(kit), encoding="utf-8")
    (OUT_DIR / "kit-preview.html").write_text(build_preview(kit), encoding="utf-8")
    print(json.dumps({"ok": True, "templates": len(kit), "skipped": skipped,
                      "out": str(OUT_DIR.relative_to(ROOT))}, ensure_ascii=False, indent=2))
    return 0


if __name__ == "__main__":
    sys.exit(main())
