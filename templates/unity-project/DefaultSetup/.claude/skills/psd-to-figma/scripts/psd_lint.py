#!/usr/bin/env python3
"""PSD lint — fail early on what the gate cannot repair."""
import argparse, json, math, re, sys
from psd_tools import PSDImage
from pipeline_config import resolve

DEFAULT_NAME = re.compile(r"^(Rectangle|Ellipse|Layer|Group|Shape) ?\d*$")
COPY_SUFFIX = re.compile(r" copy( \d+)?$")
BAKED_NAME = re.compile(r"(?i)(demo|grid|dont_use)")
COVERAGE_THR = 0.40
ERROR_RULES = {"L-1", "L-2", "L-3", "L-8"}

def _rec(layer, pbbox, sibs, scr):
    bb = layer.bbox; x, y, w, h = bb[0], bb[1], bb[2]-bb[0], bb[3]-bb[1]
    try: fill_o = layer._record.filler if hasattr(layer._record, "filler") else 255
    except Exception: fill_o = 255
    efx = ([{"type": type(e).__name__, "enabled": getattr(e, "enabled", True)}
            for e in layer.effects] if hasattr(layer, "effects") and layer.effects else [])
    font, xform = None, None
    if layer.kind == "type":
        try:
            rd, ed = layer.resource_dict, layer.engine_dict; fi = 0
            for run in ed["StyleRun"]["RunArray"]:
                if "Font" in run["StyleSheet"]["StyleSheetData"]:
                    fi = int(run["StyleSheet"]["StyleSheetData"]["Font"])
            fs = rd.get("FontSet", [])
            if fi < len(fs):
                raw = fs[fi]["Name"]; font = raw.value if hasattr(raw, "value") else str(raw)
        except Exception: pass
        xform = layer.transform
    return {"name": layer.name, "kind": layer.kind, "bbox": (x,y,w,h),
            "visible": layer.visible, "parent_bbox": pbbox, "siblings": sibs,
            "effects": efx, "fill_opacity": fill_o, "font": font,
            "transform": xform, "screen": scr}

def _finding(rule, screen, layer, detail): return {"rule": rule, "screen": screen, "layer": layer, "detail": detail}

def ck_L1(r):
    if DEFAULT_NAME.match(r["name"]) or COPY_SUFFIX.search(r["name"]):
        return _finding("L-1", r["screen"], r["name"], "default or copy name")

def ck_L2(sibs):
    if not sibs: return None
    groups = sum(1 for s in sibs if s["kind"] in ("group", "artboard"))
    leaves = sum(1 for s in sibs if s["kind"] not in ("group", "artboard") and s["visible"])
    if groups == 0 and leaves > 8:
        return _finding("L-2", sibs[0]["screen"], "(root)", f"{leaves} visible leaves, 0 groups")

def ck_L3(r):
    if BAKED_NAME.search(r["name"]):
        return _finding("L-3", r["screen"], r["name"], "name contains baked-art keyword")
    if r["kind"] != "pixel" or not r["visible"]: return None
    _, _, pw, ph = r["parent_bbox"]; _, _, lw, lh = r["bbox"]
    if pw <= 0 or ph <= 0 or lw <= 0 or lh <= 0: return None
    cov = (lw * lh) / (pw * ph)
    noth = sum(1 for s in r["siblings"]
               if s is not r and s["visible"] and s["kind"] not in ("group", "artboard"))
    if cov >= COVERAGE_THR and noth >= 3:
        return _finding("L-3", r["screen"], r["name"],
                        f"pixel covers {cov:.1%} of parent, {noth} other visible leaves")

def ck_L4(r):
    if r["kind"] != "type" or r["transform"] is None: return None
    t = r["transform"]; deg = abs(math.degrees(math.atan2(float(t[1]), float(t[0]))))
    if deg > 0.1: return _finding("L-4", r["screen"], r["name"], f"rotation {deg:.2f} deg")

def ck_L5(r):
    for e in r["effects"]:
        if not e.get("enabled", True):
            return _finding("L-5", r["screen"], r["name"], f"disabled effect: {e['type']}")

def ck_L6(r, cond_real):
    if r["kind"] != "type" or not r["font"]: return None
    if "Condensed" in r["font"] and not cond_real:
        return _finding("L-6", r["screen"], r["name"],
                        f"font {r['font']} with condensedIsRealFamily=false")

def ck_L7(r):
    if r["kind"] not in ("type","pixel","shape","smartobject") or not r["visible"]: return None
    if isinstance(r["fill_opacity"], int) and r["fill_opacity"] < 255:
        return _finding("L-7", r["screen"], r["name"], f"fill_opacity={r['fill_opacity']}")

def ck_L8(r, all_recs):
    if r["visible"]: return None
    stem = re.sub(r"[ _]?\d+$", "", r["name"])
    if not stem: return None
    bx, by, bw, bh = r["bbox"]
    if bw <= 0 or bh <= 0: return None
    for s in all_recs:
        if s is r or not s["visible"] or s["screen"] != r["screen"]: continue
        if re.sub(r"[ _]?\d+$", "", s["name"]) != stem: continue
        sx, sy, sw, sh = s["bbox"]
        if bx < sx + sw and bx + bw > sx and by < sy + sh and by + bh > sy:
            return _finding("L-8", r["screen"], r["name"],
                            f"hidden, overlaps visible sibling {s['name']!r}")

def lint_screen(psd, screen_key, skip_names, cond_real, skip_ab):
    findings, records = [], []
    checks = [ck_L1, ck_L3, ck_L4, ck_L5]
    def _chk(rec):
        for fn in checks:
            f = fn(rec)
            if f: findings.append(f)
        f = ck_L6(rec, cond_real)
        if f: findings.append(f)
        f = ck_L7(rec)
        if f: findings.append(f)
    def walk(children, pbbox):
        cl = list(children)
        sr = [_rec(c, pbbox, [], screen_key) for c in cl]
        for s in sr: s["siblings"] = sr
        for i, child in enumerate(cl):
            records.append(sr[i]); _chk(sr[i])
            if hasattr(child, "__iter__") and child.kind in ("group", "artboard"):
                b = child.bbox; walk(child, (b[0], b[1], b[2]-b[0], b[3]-b[1]))
    roots = list(psd)
    if roots and roots[-1].kind == "artboard" and all(not r.visible for r in roots[:-1]): roots = [roots[-1]]
    if len(roots) == 1 and roots[0].kind == "artboard":
        ab = roots[0]; eff = list(ab)
        rb = (ab.bbox[0], ab.bbox[1], ab.bbox[2]-ab.bbox[0], ab.bbox[3]-ab.bbox[1])
    else: eff, rb = roots, (0, 0, psd.width, psd.height)
    walk(eff, rb)
    l2 = ck_L2([r for r in records if r["parent_bbox"] == rb])
    if l2: findings.append(l2)
    for r in records:
        f = ck_L8(r, records)
        if f: findings.append(f)
    for f in findings:
        fk = f"{screen_key}/{f['layer']}"
        if fk in skip_names or f["layer"] in skip_names: f["skipped_by_import"] = True
    return findings

def _selftest():
    ok = fail = 0
    def chk(rid, rec, fn, *a):
        nonlocal ok, fail
        r = fn(rec, *a) if a else fn(rec)
        if r and r["rule"] == rid: print(f"  PASS {rid}"); ok += 1
        else: print(f"  FAIL {rid}: got {r}"); fail += 1
    def chk2(rid, r):
        nonlocal ok, fail
        if r and r["rule"] == rid: print(f"  PASS {rid}"); ok += 1
        else: print(f"  FAIL {rid}: got {r}"); fail += 1
    B = {"screen":"t","kind":"pixel","bbox":(0,0,100,100),"visible":True,
         "parent_bbox":(0,0,200,200),"siblings":[],"effects":[],
         "fill_opacity":255,"font":None,"transform":None}
    chk("L-1", {**B, "name": "Rectangle 3"}, ck_L1)
    chk("L-1", {**B, "name": "icon copy 2"}, ck_L1)
    sibs = [{**B, "name": f"l{i}"} for i in range(10)]
    chk2("L-2", ck_L2(sibs))
    chk("L-3", {**B, "name": "demo_panel", "siblings": sibs[:5]}, ck_L3)
    chk("L-4", {**B, "name":"T","kind":"type","transform":(.9,.1,-.1,.9,0,0)}, ck_L4)
    chk("L-5", {**B, "name":"S","effects":[{"type":"DropShadow","enabled":False}]}, ck_L5)
    chk("L-6", {**B, "name":"P","kind":"type","font":"FredokaCondensed-SB"}, ck_L6, False)
    chk("L-7", {**B, "name": "O", "fill_opacity": 200}, ck_L7)
    h = {**B, "name":"icon_star","visible":False,"bbox":(10,10,50,50)}
    v = {**B, "name":"icon_star","visible":True,"bbox":(20,20,60,60)}
    chk2("L-8", ck_L8(h, [h, v]))
    print(f"\n{ok} passed, {fail} failed"); sys.exit(1 if fail else 0)

def main():
    if "--selftest" in sys.argv[1:]: _selftest(); return
    cfg, argv = resolve()
    pa = argparse.ArgumentParser()
    pa.add_argument("--screen", action="append", default=[])
    pa.add_argument("--json", action="store_true")
    args = pa.parse_args(argv)
    screens = cfg.load("screens")
    nd = cfg.load("nodeNames")
    skip = set(nd.get("skipNames", []))
    skip_ab = nd.get("skipArtboard", "")
    cond_real = cfg.settings.get("figma", {}).get("condensedIsRealFamily", True)
    keys = args.screen or list(screens.keys())
    all_f, counts = {}, {}
    for key in keys:
        scfg = screens.get(key)
        if not scfg: print(f"unknown screen: {key}", file=sys.stderr); continue
        pp = cfg.psd_dir / scfg["psd"]
        if not pp.exists(): print(f"PSD missing: {pp}", file=sys.stderr); continue
        findings = lint_screen(PSDImage.open(str(pp)), key, skip, cond_real, skip_ab)
        if findings: all_f[key] = findings
        for f in findings: counts[f["rule"]] = counts.get(f["rule"], 0) + 1
    lines = []
    for scr, fs in sorted(all_f.items()):
        for f in fs:
            tag = "ERROR" if f["rule"] in ERROR_RULES else "WARN"
            sn = " (skipped by import)" if f.get("skipped_by_import") else ""
            lines.append((0 if tag == "ERROR" else 1,
                          f"[{tag}] {f['rule']} {scr}/{f['layer']}: {f['detail']}{sn}"))
    for _, ln in sorted(lines): print(ln)
    if counts: print(f"\nCounts: {json.dumps(counts, sort_keys=True)}")
    if args.json:
        op = cfg.path("lint_report.json")
        op.write_text(json.dumps({"screens": dict(all_f), "counts": counts},
                      indent=2, ensure_ascii=False), encoding="utf-8")
        print(f"Wrote {op}")
    sys.exit(1 if any(f["rule"] in ERROR_RULES for fs in all_f.values() for f in fs) else 0)

if __name__ == "__main__":
    main()
