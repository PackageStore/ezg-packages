#!/usr/bin/env python3
"""Persist the figma_build.js return object and register nine-slice applications.

Consumes --data-dir / --project-root via pipeline_config; additional flags:
--in <file> (the build result JSON), --dry-run, --selftest.
"""
import argparse, json, os, shutil, subprocess, sys, tempfile
from collections import defaultdict
from pathlib import Path

from pipeline_config import resolve

SCRIPTS = Path(__file__).resolve().parent


def read_input(path):
    if path and path != "-":
        text = open(path, "r", encoding="utf-8").read()
    else:
        text = sys.stdin.read()
    data = json.loads(text)
    if isinstance(data, str):
        data = json.loads(data)
    return data


def stem_map(manifest):
    m = {}
    for l in manifest.get("layers", []):
        lk = f"{l['screen']}/{l['psdName']}@{l['x']},{l['y']}"
        if "asset" in l:
            m[lk] = l["asset"]
    return m


def write_node_ids(cfg, key, frame_id, layer_ids, dry_run):
    dest = cfg.path(f"node_ids_{key}.json")
    existing = {}
    if dest.is_file():
        existing = json.loads(dest.read_text(encoding="utf-8"))
    existing["frameId"] = frame_id
    layers = existing.get("layers", {})
    layers.update(layer_ids)
    existing["layers"] = layers
    n = len(layers)
    if dry_run:
        print(f"[dry-run] node_ids_{key}.json ({n} layers)")
    else:
        dest.write_text(json.dumps(existing, indent=2) + "\n", encoding="utf-8")
        print(f"wrote node_ids_{key}.json ({n} layers)")


def register_nine_slices(cfg, plan, ops_map, stems, dry_run):
    frame_name = plan["frameName"]
    by_stem = defaultdict(lambda: {"nodes": [], "border": None})
    for op in plan["ops"]:
        if op["op"] != "nineSlice":
            continue
        lk = op.get("layerKey")
        stem = stems.get(lk) if lk else None
        if not stem:
            print(f"warning: no stem for nineSlice {op['id']} (layerKey={lk})",
                  file=sys.stderr)
            continue
        node_id = ops_map.get(op["id"], "")
        by_stem[stem]["nodes"].append(f"{frame_name}/{op['name']} {node_id}")
        if by_stem[stem]["border"] is None:
            by_stem[stem]["border"] = op["border"]

    existing = cfg.load_optional("nine_slice.json", {})
    for stem, info in by_stem.items():
        nodes, replace = list(info["nodes"]), []
        prev = (existing.get(stem) or {}).get("applied") or {}
        if prev.get("node") and list(prev.get("border") or []) == list(info["border"]):
            nodes = [n.strip() for n in prev["node"].split(",")] + [n for n in nodes if n not in prev["node"]]
            replace = ["--replace"]
        applied = {"node": ", ".join(nodes), "border": info["border"]}
        if dry_run:
            print(f"[dry-run] nine-slice-applied --stem {stem}")
            continue
        fd, tmp = tempfile.mkstemp(suffix=".json")
        try:
            with os.fdopen(fd, "w", encoding="utf-8") as fh:
                json.dump(applied, fh)
            proc = subprocess.run(
                [sys.executable, str(SCRIPTS / "registry_add.py"),
                 "--data-dir", str(cfg.data_dir),
                 "--project-root", str(cfg.project_root),
                 "nine-slice-applied", "--stem", stem, "--entry", tmp] + replace,
                capture_output=True, text=True)
            if proc.returncode == 4:
                sys.stderr.write(proc.stderr)
                sys.exit(4)
            if proc.returncode != 0:
                sys.stderr.write(proc.stderr)
                sys.exit(proc.returncode)
            if proc.stdout.strip():
                print(proc.stdout.strip())
        finally:
            if os.path.exists(tmp):
                os.unlink(tmp)


def update_extract_config(cfg, frame_name, key, dry_run):
    from registry_add import locked, load_json, atomic_write
    ec_path = cfg.path("figma_extract_config.json")
    with locked(ec_path):
        ec = load_json(ec_path, {})
        frames = ec.get("frames", {})
        if frames.get(frame_name) == key:
            print(f"extract_config: {frame_name!r} already maps to {key!r}")
            return
        if dry_run:
            print(f"[dry-run] extract_config.frames += {frame_name!r}: {key!r}")
            return
        frames[frame_name] = key
        ec["frames"] = frames
        atomic_write(ec_path, ec)
        print(f"extract_config.frames += {frame_name!r}: {key!r}")


def selftest():
    d = tempfile.mkdtemp(prefix="figma_build_save_test_")
    try:
        settings = {"paths": {"projectRoot": d, "psdDir": "."}, "styleIdFiles": []}
        (Path(d) / "psd2figma.json").write_text(json.dumps(settings))
        manifest = {"screens": {"s": {}}, "layers": [
            {"screen": "s", "psdName": "bg", "node": "Bg", "role": "art",
             "asset": "bg_stem", "x": 10, "y": 20, "w": 100, "h": 50}],
            "collisions": {"nodeStyles": [], "stemSizes": []}}
        (Path(d) / "psd_manifest.json").write_text(json.dumps(manifest))
        plan = {"key": "s", "frameName": "S-Frame",
                "frame": {"x": 0, "y": 0, "w": 200, "h": 200},
                "pageName": "P", "replace": True, "ops": [
                    {"op": "nineSlice", "id": "o1", "name": "Bg", "parent": None,
                     "x": 10, "y": 20, "w": 100, "h": 50, "hash": "abc",
                     "srcW": 80, "srcH": 40, "border": [10, 10, 10, 10],
                     "layerKey": "s/bg@10,20"}],
                "warnings": []}
        (Path(d) / "build_plan_s.json").write_text(json.dumps(plan))
        ns = {"bg_stem": {"size": [80, 40], "border": {"left": 10, "top": 10,
              "right": 10, "bottom": 10}, "sliceable": {"x": True, "y": True}}}
        (Path(d) / "nine_slice.json").write_text(json.dumps(ns))
        ec = {"pageName": "P", "frames": {}}
        (Path(d) / "figma_extract_config.json").write_text(json.dumps(ec))
        result = {"key": "s", "frameId": "99:1", "replacedId": None,
                  "ops": {"o1": "99:2"}, "layerIds": {"s/bg@10,20": "99:2"},
                  "textDeltas": {}, "warnings": []}
        rp = Path(d) / "result.json"
        rp.write_text(json.dumps(result))
        proc = subprocess.run(
            [sys.executable, str(Path(__file__).resolve()),
             "--data-dir", d, "--project-root", d, "--in", str(rp)],
            capture_output=True, text=True)
        assert proc.returncode == 0, f"exit {proc.returncode}: {proc.stderr}"
        nids = json.loads((Path(d) / "node_ids_s.json").read_text())
        assert nids["frameId"] == "99:1"
        assert nids["layers"]["s/bg@10,20"] == "99:2"
        ns2 = json.loads((Path(d) / "nine_slice.json").read_text())
        assert "applied" in ns2["bg_stem"], ns2["bg_stem"]
        assert ns2["bg_stem"]["applied"]["border"] == [10, 10, 10, 10]
        ec2 = json.loads((Path(d) / "figma_extract_config.json").read_text())
        assert ec2["frames"]["S-Frame"] == "s"
        rp.write_text(json.dumps(json.dumps(result)))
        proc2 = subprocess.run(
            [sys.executable, str(Path(__file__).resolve()),
             "--data-dir", d, "--project-root", d, "--in", str(rp)],
            capture_output=True, text=True)
        assert proc2.returncode == 0, f"double-encoded exit {proc2.returncode}"
        print("figma_build_save selftest OK")
    finally:
        shutil.rmtree(d, ignore_errors=True)


def main():
    if "--selftest" in sys.argv[1:]:
        return selftest()
    cfg, argv = resolve()
    parser = argparse.ArgumentParser(prog="figma_build_save.py")
    parser.add_argument("--in", dest="inp", required=True)
    parser.add_argument("--dry-run", action="store_true")
    args = parser.parse_args(argv)

    result = read_input(args.inp)
    key = result["key"]
    plan = cfg.load(f"build_plan_{key}.json")
    manifest = cfg.load("psd_manifest.json")
    stems = stem_map(manifest)

    write_node_ids(cfg, key, result["frameId"], result.get("layerIds", {}),
                   args.dry_run)
    register_nine_slices(cfg, plan, result.get("ops", {}), stems, args.dry_run)
    update_extract_config(cfg, plan["frameName"], key, args.dry_run)


if __name__ == "__main__":
    main()
