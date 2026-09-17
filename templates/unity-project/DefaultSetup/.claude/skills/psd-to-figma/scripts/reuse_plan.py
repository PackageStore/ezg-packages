"""Compute reuse clusters, variant sets and composites from manifest + digests.

Reads psd_manifest.json, layer_digests.json, component_ids.json and
nine_slice.json; writes components_plan.json.
"""
import json, sys
from collections import OrderedDict, defaultdict
from pathlib import Path
from pipeline_config import resolve

REPEAT_MIN = 3
COMPOSITE_OFFSET_TOL = 2


def _lk(l):
    return f"{l['screen']}/{l['psdName']}@{l['x']},{l['y']}"


def _registry_assets(registry):
    out = defaultdict(list)
    for sn, sd in registry.items():
        if not isinstance(sd, dict):
            continue
        if "asset" in sd:
            out[sd["asset"]].append((sn, tuple(sd.get("size") or ())))
        for vn, vd in sd.get("variants", {}).items():
            if isinstance(vd, dict) and "asset" in vd:
                out[vd["asset"]].append((f"{sn}/{vn}", tuple(vd.get("size") or ())))
    return out


def _pick_registered(cands, w, h):
    return next((n for n, sz in cands if sz == (w, h)), cands[0][0])


def _build_clusters(layers, digests):
    by_sha, by_soid, k2l = defaultdict(list), defaultdict(list), {}
    for l in layers:
        if l["role"] != "art":
            continue
        k = _lk(l)
        if k not in digests:
            continue
        k2l[k] = l
        by_sha[digests[k]["sha1"]].append(k)
        soid = l.get("smartObjectId")
        if soid:
            by_soid[soid].append(k)
    cid_n, k2c, clusters = 0, {}, {}
    for sha, keys in by_sha.items():
        cid = f"c{cid_n}"; cid_n += 1
        d = digests[keys[0]]
        clusters[cid] = {
            "sha1": sha, "w": d["w"], "h": d["h"], "count": len(keys),
            "stems": sorted({digests[k]["stem"] for k in keys}),
        }
        for k in keys:
            k2c[k] = cid
    for soid, keys in by_soid.items():
        cids = sorted({k2c[k] for k in keys if k in k2c})
        if len(cids) <= 1:
            continue
        keep = cids[0]
        for merge in cids[1:]:
            m = clusters.pop(merge); c = clusters[keep]
            c["count"] += m["count"]
            c["stems"] = sorted(set(c["stems"]) | set(m["stems"]))
            if (m["w"], m["h"]) != (c["w"], c["h"]):
                c.setdefault("sizes", [[c["w"], c["h"]]]).append([m["w"], m["h"]])
            for k in [k for k, v in k2c.items() if v == merge]:
                k2c[k] = keep
    return clusters, k2c, k2l


def _variant_sets(clusters):
    by_pfx = defaultdict(set)
    for cid, c in clusters.items():
        for s in c["stems"]:
            p = s.rsplit("_", 1)
            if len(p) == 2:
                by_pfx[p[0]].add(cid)
    seen, out = set(), []
    for pfx, cids in by_pfx.items():
        cids = sorted(cids)
        if len(cids) < 2:
            continue
        if len({(clusters[c]["w"], clusters[c]["h"]) for c in cids}) != 1:
            continue
        if len({clusters[c]["sha1"] for c in cids}) < 2:
            continue
        key = tuple(cids)
        if key in seen:
            continue
        seen.add(key)
        out.append({"name": pfx, "members": cids, "reason": "sameSizeDifferentPixels"})
    return out


def _composites(layers, k2c):
    groups = [l for l in layers if l["role"] == "group" and "groupPath" in l]
    sigs = defaultdict(list)
    for g in groups:
        sc, gn = g["screen"], g["psdName"]
        ch = [l for l in layers
              if l["screen"] == sc and l["role"] == "art"
              and l.get("groupPath") and l["groupPath"][-1] == gn]
        if len(ch) < 2:
            continue
        ch.sort(key=lambda c: c.get("asset", ""))
        stems = [c.get("asset", c["psdName"]) for c in ch]
        gx, gy = g["x"], g["y"]
        offs = [(s, c["x"] - gx, c["y"] - gy) for s, c in zip(stems, ch)]
        sigs["+".join(stems)].append({"gk": _lk(g), "offs": offs})
    out = []
    for sig, ents in sigs.items():
        if len(ents) < REPEAT_MIN:
            continue
        ref = ents[0]["offs"]
        compat = [ents[0]]
        for e in ents[1:]:
            if len(e["offs"]) == len(ref) and all(
                abs(a[1]-b[1]) <= COMPOSITE_OFFSET_TOL
                and abs(a[2]-b[2]) <= COMPOSITE_OFFSET_TOL
                for a, b in zip(ref, e["offs"])):
                compat.append(e)
        if len(compat) < REPEAT_MIN:
            continue
        out.append({
            "signature": sig, "count": len(compat),
            "members": [{"stem": s, "dx": dx, "dy": dy} for s, dx, dy in ref],
            "occurrences": [e["gk"] for e in compat],
        })
    return out


def main(cfg):
    manifest = cfg.load("psd_manifest.json")
    digests = dict(cfg.load("layer_digests.json"))
    registry = cfg.load_optional("component_ids.json", {})
    layers = manifest["layers"]
    clusters, k2c, k2l = _build_clusters(layers, digests)
    ra = _registry_assets(registry)
    for c in clusters.values():
        for s in c["stems"]:
            if s in ra:
                c["registered"] = _pick_registered(ra[s], c["w"], c["h"]); break
    vs = _variant_sets(clusters)
    comps = _composites(layers, k2c)
    instances = {}
    for k, cid in k2c.items():
        r = "smartObject" if k2l[k].get("smartObjectId") else "sha1"
        instances[k] = {"cluster": cid, "reason": r}
    unpromoted = sorted(c for c, d in clusters.items()
                        if d["count"] >= REPEAT_MIN and "registered" not in d)
    result = OrderedDict([("instances", instances), ("clusters", clusters),
                          ("variantSets", vs), ("composites", comps),
                          ("unpromoted", unpromoted)])
    out = cfg.path("components_plan.json")
    out.write_text(json.dumps(result, indent=2) + "\n", encoding="utf-8")
    print(f"clusters {len(clusters)}, unpromoted {len(unpromoted)}, "
          f"composites {len(comps)}")


def _selftest():
    import tempfile, shutil
    tmp = Path(tempfile.mkdtemp())
    try:
        layers = [{"screen": "s1", "psdName": chr(65+i), "role": "art",
                    "asset": "icon_a", "x": i*50, "y": 0, "w": 32, "h": 32,
                    "opacity": 1.0, "groupPath": [], "zOrder": i} for i in range(3)]
        manifest = {"screens": {"s1": {"psd": "s1.psd", "psdW": 100, "psdH": 100,
                     "frame": {"w": 100, "h": 100}, "dx": 0, "dy": 0}},
                     "layers": layers, "collisions": {}}
        digests = {f"s1/{chr(65+i)}@{i*50},0": {"sha1": "aaa", "w": 32, "h": 32,
                    "stem": "icon_a"} for i in range(3)}
        for name, data in [("psd_manifest.json", manifest),
                           ("layer_digests.json", digests),
                           ("component_ids.json", {"TS": {"asset": "icon_a", "variants": {}}}),
                           ("nine_slice.json", {}), ("psd2figma.json", {})]:
            (tmp / name).write_text(json.dumps(data))
        C = type("C", (), {
            "path": lambda s, *p: tmp.joinpath(*p),
            "load": lambda s, n: json.loads((tmp / n).read_text()),
            "load_optional": lambda s, n, d=None:
                json.loads((tmp / n).read_text()) if (tmp / n).is_file() else d})()
        main(C)
        r = json.loads((tmp / "components_plan.json").read_text())
        assert len(r["clusters"]) == 1
        cid = list(r["clusters"])[0]
        assert r["clusters"][cid]["count"] == 3
        assert r["clusters"][cid]["registered"] == "TS"
        assert len(r["instances"]) == 3
        assert len(r["unpromoted"]) == 0
        print("reuse_plan self-test OK")
    finally:
        shutil.rmtree(tmp)


if __name__ == "__main__":
    _selftest() if "--selftest" in sys.argv else main(resolve()[0])
