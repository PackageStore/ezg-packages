#!/usr/bin/env python3
"""Fold registry sidecars into the live registries."""
import argparse, json, os, sys, tempfile
from pathlib import Path
from pipeline_config import resolve
from registry_add import (
    Conflict, apply_nine_slice, atomic_write, deep_copy, locked, merge)

def _load(p):
    return json.loads(p.read_text("utf-8")) if p.exists() else {}

def _contained(t, f):
    if isinstance(f, dict) and isinstance(t, dict):
        return all(k in t and _contained(t[k], v) for k, v in f.items())
    return t == f

def _fold_flat(w, src):
    ch, cf = [], []
    for k, v in src.items():
        cur = w.get(k)
        if cur is None:
            w[k] = v; ch.append("+" + k)
        elif isinstance(cur, dict) and isinstance(v, dict):
            m = deep_copy(cur)
            try:
                sub = merge(m, v, False)
                if sub: w[k] = m; ch.append(k + "(" + ",".join(sub) + ")")
            except Conflict as c:
                cf.append(Conflict(f"{k}.{c.path}", c.existing, c.incoming))
        elif cur != v:
            cf.append(Conflict(k, cur, v))
    return ch, cf

def _fold_ns(w, src):
    ch, cf = [], []
    for stem, applied in src.items():
        try:
            tok = apply_nine_slice(w, stem, applied, False)
            if tok: ch.append(tok)
        except Conflict as c:
            cf.append(Conflict(f"{stem}.applied.{c.path}", c.existing, c.incoming))
    return ch, cf

def _process(label, dd, tgt, prefix, fold, do_apply):
    tp = dd / tgt
    scs = sorted(dd.glob(f"{prefix}.*.json"))
    if not scs: return True
    print(f"{label}:")
    w = deep_copy(_load(tp))
    bad = []
    for sp in scs:
        sd = _load(sp)
        ch, cf = fold(w, sd)
        if ch: print(f"  {sp.name}: {', '.join(ch)}")
        elif not cf: print(f"  {sp.name}: (already contained)")
        for c in cf:
            print(f"  {sp.name}: CONFLICT {c.path}: "
                  f"{json.dumps(c.existing)} != {json.dumps(c.incoming)}")
            bad.append(c)
    if bad: return False
    if not do_apply: return True
    is_ns = prefix == "nine_slice_applied"
    with locked(tp):
        wk = deep_copy(_load(tp))
        for sp in scs: fold(wk, _load(sp))
        atomic_write(tp, wk)
    final = _load(tp)
    for sp in scs:
        sd = _load(sp)
        if is_ns:
            ok = all(_contained(final.get(s,{}).get("applied",{}), a) for s,a in sd.items())
        else:
            ok = _contained(final, sd)
        assert ok, f"{sp.name} not fully contained after write"
        sp.unlink(); print(f"  deleted {sp.name}")
    return True

def main():
    if "--self-test" in sys.argv[1:]: return _self_test()
    cfg, argv = resolve()
    ap = argparse.ArgumentParser(prog="registry_consolidate")
    ap.add_argument("--apply", action="store_true")
    args = ap.parse_args(argv)
    dd = cfg.data_dir
    ok = _process("component_ids", dd, "component_ids.json",
                   "component_ids", _fold_flat, args.apply)
    ok &= _process("style_ids", dd, "style_ids.json",
                    "style_ids", _fold_flat, args.apply)
    ok &= _process("nine_slice_applied", dd, "nine_slice.json",
                    "nine_slice_applied", _fold_ns, args.apply)
    if not ok: sys.exit(4)
    if args.apply:
        cp = dd / "psd2figma.json"
        with locked(cp):
            d = _load(cp); d["styleIdFiles"] = ["style_ids.json"]; atomic_write(cp, d)
        print('styleIdFiles -> ["style_ids.json"]')
        for p in sorted(dd.iterdir()):
            if p.suffix in (".lock", ".bak"): p.unlink(); print(f"cleaned: {p.name}")

def _self_test():
    import shutil, subprocess
    root = tempfile.mkdtemp(prefix="regcons_")
    try:
        def w(n, o): json.dump(o, open(os.path.join(root, n), "w"))
        w("psd2figma.json", {"styleIdFiles": ["style_ids.json", "style_ids.x.json"],
                             "paths": {"projectRoot": root, "psdDir": root}})
        w("component_ids.json", {"A": {"id": "1:1"}})
        w("component_ids.x.json", {"B": {"id": "2:2"}})
        w("style_ids.json", {"textStyles": {"T1": {"id": "3:3"}}})
        w("style_ids.x.json", {"textStyles": {"T2": {"id": "4:4"}}})
        w("nine_slice.json", {"s": {"size": [10, 10]}})
        w("nine_slice_applied.x.json", {"s": {"node": "5:5"}})
        run = lambda *a: subprocess.run(
            [sys.executable, __file__, "--data-dir", root, *a],
            capture_output=True, text=True)
        r = run(); assert r.returncode == 0, f"dry run: {r.stderr}"
        assert Path(root, "component_ids.x.json").exists()
        r = run("--apply"); assert r.returncode == 0, f"apply: {r.stderr}"
        assert not Path(root, "component_ids.x.json").exists()
        ci = json.load(open(os.path.join(root, "component_ids.json")))
        assert "B" in ci
        si = json.load(open(os.path.join(root, "style_ids.json")))
        assert "T2" in si["textStyles"]
        ns = json.load(open(os.path.join(root, "nine_slice.json")))
        assert ns["s"]["applied"]["node"] == "5:5"
        cfg = json.load(open(os.path.join(root, "psd2figma.json")))
        assert cfg["styleIdFiles"] == ["style_ids.json"]
        w("component_ids.json", {"X": {"id": "1:1"}})
        w("component_ids.c.json", {"X": {"id": "9:9"}})
        r = run(); assert r.returncode == 4, f"conflict exit: {r.returncode}"
        print("self-test: PASS")
        return 0
    finally:
        shutil.rmtree(root, ignore_errors=True)

if __name__ == "__main__":
    sys.exit(main() or 0)
