#!/usr/bin/env python3
"""Generate a build plan (op list) for one or more screens from the manifest."""
import argparse, json, re, sys
from collections import OrderedDict, defaultdict
from pathlib import Path
from pipeline_config import resolve

_lk = lambda l: f"{l['screen']}/{l['psdName']}@{l['x']},{l['y']}"
_pascal = lambda n: "".join(w[:1].upper() + (w[1:].lower() if w.isupper() else w[1:]) for w in re.split(r"[\s_]+", n) if w)
def _load_style_ids(cfg):
    m = {}
    for name in cfg.settings.get("styleIdFiles", []):
        p = cfg.path(name)
        if p.is_file():
            m.update(json.loads(p.read_text(encoding="utf-8")).get("textStyles", {}))
    return m
def _resolve_cid(reg, ci):
    if reg in ci and ci[reg].get("componentId"): return ci[reg]["componentId"]
    if "/" in reg:
        s, v = reg.split("/", 1)
        return ci.get(s, {}).get("variants", {}).get(v, {}).get("id")
    return ci.get(reg, {}).get("componentId")
def _dedup(name, used):
    n, r = 2, name
    while r in used: r = f"{name}-{n}"; n += 1
    used.add(r); return r
def _desc(g, gio, gpar, glv):
    r = list(glv.get(g, []))
    for og in gio:
        if gpar.get(og) == g: r.extend(_desc(og, gio, gpar, glv))
    return r

def _bbox(layers):
    x0 = min(l["x"] for l in layers); y0 = min(l["y"] for l in layers)
    return (x0, y0, max(l["x"]+l["w"] for l in layers)-x0,
            max(l["y"]+l["h"] for l in layers)-y0)

def _registered_stems(cp):
    return {st for cl in cp.get("clusters", {}).values() if cl.get("registered")
            for st in cl.get("stems", [])}

def _sizes_by_stem(manifest):
    d = defaultdict(set)
    for l in manifest["layers"]:
        if l["role"] not in ("skip", "text"):
            d[l.get("asset", l["psdName"].lower().replace(" ", "_"))].add((l["w"], l["h"]))
    return d

def _norm_border(border, sliceable):
    b = ([border["left"], border["top"], border["right"], border["bottom"]]
         if isinstance(border, dict) else list(border))
    if not sliceable.get("x", False): b[0] = b[2] = 0
    if not sliceable.get("y", False): b[1] = b[3] = 0
    return b

def generate(cfg, key, out_dir):
    manifest = cfg.load("psd_manifest.json")
    cp = cfg.load("components_plan.json")
    ih = cfg.load_optional("image_hashes.json", {})
    ci = cfg.load_optional("component_ids.json", {})
    ts = cfg.load_optional("text_styles.json", {"styles": {}})
    sid_map = _load_style_ids(cfg)
    screens = cfg.load_optional("screens.json", {})
    ecfg = cfg.load_optional("figma_extract_config.json", {})
    nn = cfg.load_optional("node_names.json", {})
    ns = cfg.load_optional("nine_slice.json", {})
    nmap = cfg.load_optional("name_map.json", {}).get("styles", {})
    s = cfg.settings
    sbs = _sizes_by_stem(manifest)
    reg_stems = _registered_stems(cp)
    clip_set = set(ecfg.get("clipLeafNames", []))
    fw, fh = s.get("frame",{}).get("w",1080), s.get("frame",{}).get("h",2400)
    grid = s.get("figma",{}).get("gridStyleId")
    pname = ecfg.get("pageName", "Screens")
    fname = next((fn for fn, fk in ecfg.get("frames",{}).items() if fk == key), key)
    is_rows = screens.get(key,{}).get("walkMode") == "rows"
    layers = sorted((l for l in manifest["layers"] if l["screen"]==key and l["role"] in ("art","text")),
                    key=lambda l: l.get("zOrder", 0))
    if not layers: sys.stderr.write(f"build_plan_gen: no layers for {key!r}\n"); return
    ops, warns, oid, styles = [], [], [0], ts.get("styles", {})
    nid = lambda: (oid.__setitem__(0, oid[0]+1), f"o{oid[0]}")[1]

    def emit(layer, pid, fx, fy):
        lk, role, node = _lk(layer), layer["role"], layer["node"]
        rx, ry, w, h = layer["x"]-fx, layer["y"]-fy, layer["w"], layer["h"]
        opa = layer.get("opacity", 1.0)
        if role == "text":
            sn = layer.get("type",{}).get("style","Unknown"); sn = nmap.get(sn, sn); sid = sid_map.get(sn)
            if sid is None: warns.append({"code":"NO_STYLE","layerKey":lk,"style":sn})
            recipe = OrderedDict((k, styles[sn][k]) for k in ("fontSize","fill","strokeWeight",
                "strokeAlign","strokes","effects") if sn in styles and k in styles[sn])
            t = layer.get("type",{})
            top = OrderedDict([("op","text"),("id",nid()),("name",node),("parent",pid),
                ("x",rx),("y",ry),("w",w),("h",h),("chars",t.get("content","")),
                ("styleId",sid),("recipe",recipe),
                ("ink",{"x":layer["x"],"y":layer["y"],"w":w,"h":h}),("layerKey",lk)])
            if opa != 1.0: top["opacity"] = opa
            if node in clip_set:
                cid = nid()
                ops.append(OrderedDict([("op","container"),("id",cid),("name",node),
                    ("parent",pid),("clip",True),("x",rx),("y",ry),("w",w),("h",h),
                    ("layerKey",lk)]))
                top["name"],top["parent"],top["x"],top["y"] = "Text-Content",cid,0,0
            ops.append(top); return
        inst = cp.get("instances",{}).get(lk)
        if inst:
            cl = cp["clusters"].get(inst["cluster"],{})
            reg = cl.get("registered")
            if reg:
                cmp = _resolve_cid(reg, ci)
                if cmp:
                    o = OrderedDict([("op","instance"),("id",nid()),("name",node),
                        ("parent",pid),("componentId",cmp),
                        ("x",rx),("y",ry),("w",w),("h",h),("layerKey",lk)])
                    if opa != 1.0: o["opacity"] = opa
                    ops.append(o); return
        stem = layer.get("asset", layer["psdName"].lower().replace(" ","_"))
        hv = ih.get(stem)
        if hv is None: warns.append({"code":"NO_HASH","layerKey":lk,"stem":stem})
        nse = ns.get(stem)
        if nse and stem not in reg_stems:
            sl = nse.get("sliceable", {})
            if sl.get("x") or sl.get("y"):
                if len(sbs.get(stem, set())) >= 2 or "applied" in nse:
                    border = _norm_border(
                        nse["applied"]["border"] if "applied" in nse else nse["border"], sl)
                    srcW, srcH = nse["size"]
                    if border[0]+border[2] < w and border[1]+border[3] < h:
                        o = OrderedDict([("op","nineSlice"),("id",nid()),("name",node),
                            ("parent",pid),("x",rx),("y",ry),("w",w),("h",h),("hash",hv),
                            ("srcW",srcW),("srcH",srcH),("border",border),
                            ("axes",{"x":sl.get("x",False),"y":sl.get("y",False)}),
                            ("opacity",opa),("layerKey",lk)])
                        ops.append(o); return
                    warns.append({"code":"SLICE_TOO_SMALL","layerKey":lk,"stem":stem})
        o = OrderedDict([("op","rect"),("id",nid()),("name",node),("parent",pid),
            ("x",rx),("y",ry),("w",w),("h",h),("hash",hv),("opacity",opa),("layerKey",lk)])
        ops.append(o)

    if is_rows:
        for l in layers:
            if not l.get("isRowChild"): emit(l, None, 0, 0)
        rc = [l for l in layers if l.get("isRowChild")]
        if rc:
            bx,by,bw,bh = _bbox(rc); cid = nid()
            ops.append(OrderedDict([("op","container"),("id",cid),("name","Container-Row-1"),
                ("parent",None),("x",bx),("y",by),("w",bw),("h",bh),
                ("layerKey",f"{key}/Row-1@{bx},{by}")]))
            for l in rc: emit(l, cid, bx, by)
            warns.append({"code":"ROWS_REPEAT","rowPitch":nn.get("rowPitch"),
                          "layerKey":f"{key}/Row-1@{bx},{by}"})
    else:
        gbbox, gpar, glv = {}, {}, {}
        for l in layers:
            gp = tuple(l.get("groupPath", []))
            for i in range(len(gp)):
                gpar.setdefault(gp[:i+1], gp[:i] or None)
                glv.setdefault(gp[:i+1], []).append(l)
        for g, ls in glv.items(): gbbox[g] = _bbox(ls)
        gids, used = {}, set()
        def ensure(g):
            if not g or g in gids: return gids.get(g)
            pp = gpar.get(g); pid = ensure(pp)
            fx, fy = (gbbox[pp][0], gbbox[pp][1]) if pp else (0, 0)
            bx, by, bw, bh = gbbox[g]; cid = nid(); gids[g] = cid
            cn = _dedup(f"Container-{_pascal(g[-1])}", used)
            ops.append(OrderedDict([("op","container"),("id",cid),("name",cn),("parent",pid),
                ("x",bx-fx),("y",by-fy),("w",bw),("h",bh),("layerKey",f"{key}/{'/'.join(g)}")]))
            return cid
        for l in layers:
            gp = tuple(l.get("groupPath", []))
            pid = ensure(gp) if gp else None
            fx, fy = (gbbox[gp][0], gbbox[gp][1]) if gp else (0, 0)
            emit(l, pid, fx, fy)

    result = OrderedDict([("key",key),("frameName",fname),
        ("frame",{"x":None,"y":None,"w":fw,"h":fh}),
        ("gridStyleId",grid),("pageName",pname),("ops",ops),("warnings",warns)])
    dest = (Path(out_dir) if out_dir else cfg.data_dir) / f"build_plan_{key}.json"
    dest.parent.mkdir(parents=True, exist_ok=True)
    dest.write_text(json.dumps(result, indent=2, ensure_ascii=False)+"\n", encoding="utf-8")
    ct = {k: sum(1 for o in ops if o["op"] == k) for k in sorted({o["op"] for o in ops})}
    ns_stems = sorted({o.get("name","") for o in ops if o["op"]=="nineSlice"})
    ns_msg = f"; nineSlice ops: {ct.get('nineSlice',0)} (stems: {', '.join(ns_stems)})" if ns_stems else ""
    print(f"{key}: {len(ops)} ops ({' '.join(f'{k}={v}' for k,v in sorted(ct.items()))}), {len(warns)} warnings{ns_msg}")
    for w in warns: print(f"  warn: {w['code']} {w.get('layerKey','')}")

def main():
    if "--selftest" in sys.argv:
        from build_plan_gen_selftest import selftest
        return selftest()
    cfg, rem = resolve()
    p = argparse.ArgumentParser(prog="build_plan_gen.py")
    p.add_argument("--keys", required=True); p.add_argument("--out-dir")
    a = p.parse_args(rem)
    for k in filter(None, (k.strip() for k in a.keys.split(","))): generate(cfg, k, a.out_dir)

if __name__ == "__main__":
    main()
