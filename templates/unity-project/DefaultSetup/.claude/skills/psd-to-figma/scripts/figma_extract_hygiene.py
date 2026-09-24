"""Hygiene facts (figma-hygiene S-1/S-2/S-3/S-7/S-8/S-9/V-3/V-4 inputs) from a REST node tree."""
import re

GENERIC_RE = re.compile(r"^(Frame|Group|Rectangle|Ellipse|Vector|Component|Instance)( \d+)?$")
SLICE_NAME_RE = re.compile(r"^slice_\d_\d$")
HYG_PREFIXES = ("Container-", "Container_", "Btn-", "Header-", "Row-", "Slot-", "Group-")


def hygiene_walk(doc):
    ch = doc.get("children", [])
    fab = doc.get("absoluteBoundingBox", {})
    fx, fy = fab.get("x", 0), fab.get("y", 0)
    u_set, s_set = set(), set()
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

    def _hw(nd, pn):
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
        if nd.get("type") == "TEXT":
            overrides = nd.get("characterStyleOverrides")
            sid = (nd.get("styles") or {}).get("text")
            if not overrides and not sid:
                h["unstyledText"].append({"id": nd["id"], "name": nm})
        for c in nd.get("children", []):
            _hw(c, nm)

    for c in ch:
        _hw(c, doc.get("name", ""))
    h["underscoreNames"] = sorted(u_set)
    h["spaceNames"] = sorted(s_set)
    return h
