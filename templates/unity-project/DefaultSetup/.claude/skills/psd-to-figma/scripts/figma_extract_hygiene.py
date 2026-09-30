"""Hygiene facts (figma-hygiene S-1/S-2/S-3/S-7/S-8/S-9/V-3/V-4/V-5 inputs) from a REST node tree."""
import re

GENERIC_RE = re.compile(r"^(Frame|Group|Rectangle|Ellipse|Vector|Component|Instance)( \d+)?$")
SLICE_NAME_RE = re.compile(r"^slice_\d_\d$")
HYG_PREFIXES = ("Container-", "Container_", "Btn-", "Header-", "Row-", "Slot-", "Group-")
SHAPE_TYPES = ("FRAME", "RECTANGLE", "COMPONENT", "INSTANCE")
CONCENTRIC_TOL = 1.0
CONTAIN_TOL = 0.5


def _shape_kind(nd, slice_frame):
    if slice_frame:
        return "art"
    if nd.get("name", "").startswith("Mask-"):
        return "shape"
    paints = [p for p in (nd.get("fills") or []) + (nd.get("strokes") or [])
              if p.get("visible", True)]
    if any(p.get("type") == "IMAGE" for p in paints):
        return "art"
    return "shape" if paints else None


def _radii(nd):
    r = nd.get("rectangleCornerRadii")
    return list(r) if r else [nd.get("cornerRadius") or 0] * 4


def concentric_check(shapes):
    """V-5: an inner shape in an outer shape's corner has radius = outer - inset.

    shapes: pre-order list of {id, name, kind, box (x, y, w, h), radii [tl, tr, br, bl], masked}.
    The outer is the smallest earlier shape (ancestor or below-drawn sibling)
    whose box holds the inner. An "art" outer has no readable radius and skips.
    """
    out = []
    for i, c in enumerate(shapes):
        if c["kind"] != "shape" or c["masked"]:
            continue
        cx, cy, cw, chh = c["box"]
        outer = None
        for o in shapes[:i]:
            ox, oy, ow, oh = o["box"]
            if (ox - CONTAIN_TOL <= cx and oy - CONTAIN_TOL <= cy
                    and cx + cw <= ox + ow + CONTAIN_TOL and cy + chh <= oy + oh + CONTAIN_TOL
                    and (outer is None or ow * oh <= outer["box"][2] * outer["box"][3])):
                outer = o
        if outer is None or outer["kind"] != "shape":
            continue
        ox, oy, ow, oh = outer["box"]
        left, top = cx - ox, cy - oy
        right, bottom = ox + ow - cx - cw, oy + oh - cy - chh
        worst = None
        for k, (dx, dy) in enumerate(((left, top), (right, top), (right, bottom), (left, bottom))):
            ro = min(outer["radii"][k], min(ow, oh) / 2)
            d = (dx + dy) / 2
            if ro <= 0 or abs(dx - dy) > CONCENTRIC_TOL or d >= ro:
                continue
            expected = min(ro - d, min(cw, chh) / 2)
            actual = min(c["radii"][k], min(cw, chh) / 2)
            if abs(actual - expected) > CONCENTRIC_TOL and (
                    worst is None or abs(actual - expected) > abs(worst[0] - worst[1])):
                worst = (actual, expected)
        if worst:
            out.append({"id": c["id"], "name": c["name"], "outer": outer["name"],
                        "radius": round(worst[0], 2), "expected": round(worst[1], 2)})
    return out


def hygiene_walk(doc):
    ch = doc.get("children", [])
    fab = doc.get("absoluteBoundingBox", {})
    fx, fy = fab.get("x", 0), fab.get("y", 0)
    u_set, s_set = set(), set()
    shapes = []
    h = {
        "rootFrameChildren": len(ch), "rootLeaves": [], "rootContainers": [],
        "genericNames": [], "nonContainerGroupingFrames": [], "clipping": [],
        "underscoreNames": [], "spaceNames": [], "unstyledText": [],
        "gridStyle": bool((doc.get("styles") or {}).get("grid")),
        "screenNameUnderscore": "_" in doc.get("name", ""),
    }
    for c in ch:
        if c.get("type") != "FRAME":
            h["rootLeaves"].append(c.get("type", "") + ":" + c.get("name", ""))
        cn = c.get("name", "")
        if (cn.startswith("Container-") or cn.startswith("Container_")) \
                and c.get("absoluteBoundingBox"):
            cab = c["absoluteBoundingBox"]
            cs = c.get("constraints") or {}
            h["rootContainers"].append({
                "name": cn,
                "constraints": cs.get("horizontal", "MIN") + "/"
                               + cs.get("vertical", "MIN"),
                "x": cab["x"] - fx, "y": cab["y"] - fy,
                "w": cab["width"], "h": cab["height"],
            })

    def _hw(nd, pn, masked=False, hidden=False):
        nm = nd.get("name", "")
        if GENERIC_RE.match(nm):
            h["genericNames"].append({"id": nd["id"], "name": nm, "parent": pn})
        if "_" in nm and not SLICE_NAME_RE.match(nm):
            u_set.add(nm)
        if " " in nm:
            s_set.add(nm)
        ch2 = nd.get("children", [])
        slice_frame = bool(ch2) and all(c.get("name", "").startswith("slice_") for c in ch2)
        if (nd.get("type") == "FRAME" and len(ch2) >= 2 and not slice_frame
                and not any(nm.startswith(p) for p in HYG_PREFIXES)
                and nm != "Title" and "Scroll" not in nm):
            h["nonContainerGroupingFrames"].append({"id": nd["id"], "name": nm})
        if (nd.get("clipsContent") is True and not slice_frame and "Scroll" not in nm
                and not nm.startswith("Mask-")):
            h["clipping"].append(
                {"id": nd["id"], "name": nm, "type": nd.get("type", "")})
        hidden = hidden or nd.get("visible") is False
        if (nd.get("type") in SHAPE_TYPES and not hidden
                and not nd.get("rotation") and nd.get("absoluteBoundingBox")):
            kind = _shape_kind(nd, slice_frame)
            if kind:
                b = nd["absoluteBoundingBox"]
                shapes.append({"id": nd["id"], "name": nm, "kind": kind,
                               "box": (b["x"], b["y"], b["width"], b["height"]),
                               "radii": _radii(nd), "masked": masked})
        if nd.get("type") == "TEXT":
            overrides = nd.get("characterStyleOverrides")
            sid = (nd.get("styles") or {}).get("text")
            if not overrides and not sid:
                h["unstyledText"].append({"id": nd["id"], "name": nm})
        for c in nd.get("children", []):
            _hw(c, nm, masked or nm.startswith("Mask-"), hidden)

    for c in ch:
        _hw(c, doc.get("name", ""))
    h["underscoreNames"] = sorted(u_set)
    h["spaceNames"] = sorted(s_set)
    h["concentric"] = concentric_check(shapes)
    return h
