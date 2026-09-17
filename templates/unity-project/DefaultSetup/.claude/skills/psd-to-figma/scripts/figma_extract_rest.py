"""Fetch Figma node trees via REST and write figma_extract_<key>.json files.

Same output shape as the Plugin-based figma_extract.js + figma_extract_save.py.
"""
import argparse, json, os, sys, time
import urllib.error, urllib.request
from figma_extract_save import write_results
from figma_extract_hygiene import hygiene_walk
from pipeline_config import resolve

WEIGHT_MAP = {100: "Thin", 200: "ExtraLight", 300: "Light", 400: "Regular",
              500: "Medium", 600: "SemiBold", 700: "Bold", 800: "ExtraBold",
              900: "Black"}
SLICE_HOSTS = frozenset(("FRAME", "INSTANCE", "COMPONENT"))

def api_get(path, token):
    req = urllib.request.Request("https://api.figma.com" + path,
                                headers={"X-Figma-Token": token})
    for attempt in range(2):
        try:
            with urllib.request.urlopen(req) as resp:
                return json.loads(resp.read())
        except urllib.error.HTTPError as e:
            if e.code == 429 and attempt == 0:
                wait = int(e.headers.get("Retry-After", "30"))
                print(f"429 rate-limited; waiting {wait}s", file=sys.stderr)
                time.sleep(wait)
                continue
            raise

def has_visible_fill(node):
    return any(f.get("visible", True) for f in node.get("fills") or [])

def is_slice_frame(node):
    ch = node.get("children", [])
    return node.get("type") in SLICE_HOSTS and bool(ch) and all(c.get("name", "").startswith("slice_") for c in ch)

def derive_style(style, settings):
    fonts = settings.get("figma", {}).get("fonts", {})
    psn = style.get("fontPostScriptName")
    if psn:
        rmap = fonts.get("restStyleMap", {})
        if rmap.get(psn):
            return rmap[psn]
        parts = psn.rsplit("-", 1)
        if len(parts) == 2 and parts[1]:
            return parts[1]
    w = style.get("fontWeight")
    if w is not None:
        m = WEIGHT_MAP.get(int(w))
        if m:
            return m
    body = fonts.get("body", {}).get("style")
    if body:
        print(f"warning FONT_STYLE_GUESSED: psn={psn!r} weight={w!r}",
              file=sys.stderr)
        return body
    return "Regular"

def _tsid(node, smap):
    meta = smap.get((node.get("styles") or {}).get("text") or "")
    return f"S:{meta['key']}," if meta and meta.get("key") else ""

def _text_fields(node, smap, settings):
    if node.get("characterStyleOverrides"):
        return "MIXED", "MIXED", "MIXED"
    s = node.get("style", {})
    fn = {"family": s.get("fontFamily", ""), "style": derive_style(s, settings)}
    return fn, s.get("fontSize"), _tsid(node, smap)

def collect_clipped(node, leaf_name, out, smap, settings):
    if node.get("visible") is False:
        return
    if node.get("type") == "TEXT":
        fn, fs, tsid = _text_fields(node, smap, settings)
        out.append({"id": node["id"], "name": node["name"], "type": "TEXT",
                     "parentLeaf": leaf_name, "fontName": fn, "fontSize": fs,
                     "textStyleId": tsid})
        return
    for c in node.get("children", []):
        collect_clipped(c, leaf_name, out, smap, settings)

def walk(node, fbox, out, clip_out, cur_frame, config, smap, settings):
    if node.get("visible") is False:
        return
    ab = node.get("absoluteBoundingBox")
    if not ab:
        return
    nid = node["id"]
    if (cur_frame == config["rowFilter"]["screen"]
            and any(nid.startswith(p) for p in config["rowFilter"]["skipRows"])):
        return
    rec = {"id": nid, "name": node["name"], "type": node["type"],
           "x": ab["x"] - fbox["x"], "y": ab["y"] - fbox["y"],
           "w": ab["width"], "h": ab["height"]}
    leaf = is_slice_frame(node) or node["name"] in (config.get("clipLeafNames") or [])
    if node["type"] == "TEXT":
        rb = node.get("absoluteRenderBounds")
        if rb:
            rec.update(inkX=rb["x"] - fbox["x"], inkY=rb["y"] - fbox["y"],
                       inkW=rb["width"], inkH=rb["height"])
        sw = node.get("strokeWeight")
        rec["strokeWeight"] = sw if isinstance(sw, (int, float)) else 1
        rec["strokeAlign"] = node.get("strokeAlign", "OUTSIDE")
        strokes = node.get("strokes") or []
        rec["hasVisibleStroke"] = (bool(strokes)
                                   and any(s.get("visible", True) for s in strokes))
        rec["effects"] = [
            {k: e[k] for k in ("type", "visible", "radius", "offset") if k in e}
            for e in node.get("effects") or []]
        rec["fontName"], rec["fontSize"], rec["textStyleId"] = \
            _text_fields(node, smap, settings)
        out.append(rec)
        return
    if (node["type"] == "RECTANGLE" or leaf
            or (node["type"] == "FRAME" and has_visible_fill(node))):
        out.append(rec)
    if leaf:
        for c in node.get("children", []):
            collect_clipped(c, node["name"], clip_out, smap, settings)
        return
    for c in node.get("children", []):
        walk(c, fbox, out, clip_out, cur_frame, config, smap, settings)


def selftest():
    box = lambda x, y, w, h: {"x": x, "y": y, "width": w, "height": h}
    doc = {"id": "0:1", "name": "F", "type": "FRAME", "absoluteBoundingBox": box(10, 20, 100, 200), "children": [
        {"id": "0:2", "name": "Bg", "type": "RECTANGLE", "absoluteBoundingBox": box(10, 20, 100, 200)},
        {"id": "0:3", "name": "T", "type": "TEXT", "absoluteBoundingBox": box(15, 25, 50, 20),
         "absoluteRenderBounds": box(16, 26, 48, 18), "styles": {"text": "1:1"},
         "style": {"fontFamily": "Fam", "fontPostScriptName": "Fam-SemiBold", "fontSize": 20}},
        {"id": "0:4", "name": "P", "type": "FRAME", "absoluteBoundingBox": box(0, 0, 9, 9),
         "children": [{"id": "0:5", "name": "slice_0_0", "type": "RECTANGLE", "absoluteBoundingBox": box(0, 0, 9, 9)}]}]}
    out, clipped = [], []
    for c in doc["children"]:
        walk(c, doc["absoluteBoundingBox"], out, clipped, "F", {"rowFilter": {"screen": "", "skipRows": []}}, {"1:1": {"key": "abc"}}, {})
    assert [n["name"] for n in out] == ["Bg", "T", "P"], out
    t = out[1]
    assert (t["x"], t["y"], t["inkX"], t["textStyleId"], t["fontName"]["style"]) == (5, 5, 6, "S:abc,", "SemiBold"), t
    assert derive_style({"fontWeight": 700}, {}) == "Bold" and hygiene_walk(doc)["rootFrameChildren"] == 3
    print("figma_extract_rest self-test OK")


def main():
    if "--selftest" in sys.argv[1:]:
        return selftest()
    cfg, argv = resolve()
    ap = argparse.ArgumentParser(prog="figma_extract_rest.py")
    ap.add_argument("--keys")
    ap.add_argument("--dry-run", action="store_true")
    args = ap.parse_args(argv)
    token = os.environ.get("FIGMA_TOKEN", "")
    if not token:
        print("FIGMA_TOKEN is not set", file=sys.stderr)
        sys.exit(2)
    ec = cfg.load("figma_extract_config.json")
    file_key = cfg.settings["figma"]["fileKey"]
    all_frames = ec["frames"]
    want = set(args.keys.split(",")) if args.keys else set(all_frames.values())
    frames = {fn: k for fn, k in all_frames.items() if k in want}
    if want - set(frames.values()):
        raise SystemExit(f"unknown key(s): {', '.join(sorted(want - set(frames.values())))}")
    file_data = api_get(f"/v1/files/{file_key}?depth=2", token)
    smap = file_data.get("styles", {})
    pages = file_data.get("document", {}).get("children", [])
    page = next((pg for pg in pages if pg["name"] == ec["pageName"]), None)
    if not page:
        raise SystemExit(f"page not found: {ec['pageName']}")
    fid_map = {ch["name"]: ch["id"] for ch in page.get("children", []) if ch["name"] in frames}
    missing_frames = [fn for fn in frames if fn not in fid_map]
    if missing_frames:
        known = [c["name"] for c in page.get("children", [])]
        raise SystemExit(f"frame(s) not found on page {ec['pageName']}: "
                         f"{', '.join(missing_frames)} (have: {', '.join(known)})")
    ids_param = ",".join(fid_map[fn] for fn in frames)
    nodes_resp = api_get(
        f"/v1/files/{file_key}/nodes?ids={ids_param}&geometry=paths", token)
    result = {}
    for fname, key in frames.items():
        fid = fid_map[fname]
        doc = nodes_resp["nodes"][fid]["document"]
        fab = doc["absoluteBoundingBox"]
        nodes, clipped = [], []
        for c in doc.get("children", []):
            walk(c, fab, nodes, clipped, fname, ec, smap, cfg.settings)
        result[key] = {"frameId": fid, "frameName": fname, "frameX": 0,
                        "frameY": 0, "frameW": fab["width"],
                        "frameH": fab["height"], "nodes": nodes,
                        "clippedText": clipped,
                        "hygiene": hygiene_walk(doc)}
    write_results(result, cfg, args.dry_run)

if __name__ == "__main__":
    main()
