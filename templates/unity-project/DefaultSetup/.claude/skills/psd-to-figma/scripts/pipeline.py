#!/usr/bin/env python3
"""Idempotent stage runner for the local psd2figma pipeline.

    pipeline.py --data-dir <d> --project-root <r> run [--stages a,b] [--screen KEY] [--force]
    pipeline.py --data-dir <d> --project-root <r> status [--screen KEY]

`run` executes the stale stages in order (manifest, export, icons, borders,
gate), copying each stage's outputs to <data>/.pipeline/before/<stage>/ first
and printing a one-line content diff after. A stage runs when any input digest
differs from its <data>/.pipeline/<stage>.stamp, or under --force. Exit codes
propagate; a collision (exit 3) stops the chain and the summary names the stem.
Upload and Figma stages need the MCP and are never run here. Input digests are
sha256 over file bytes for JSON, (name, size, mtime_ns) for PSDs and PNG dirs;
a mtime-only bump on a JSON input is intentionally ignored.
"""

import argparse
import hashlib
import json
import shutil
import subprocess
import sys
from pathlib import Path

from pipeline_config import resolve

SCRIPTS = Path(__file__).parent
STAGES = ["manifest", "export", "icons", "borders", "gate"]
DEPS = {"manifest": [], "export": ["manifest"], "icons": [],
        "borders": ["export"], "gate": ["manifest"]}
SCRIPT = {"manifest": "psd_manifest.py", "export": "psd_export_pngs.py",
          "icons": "psd_export_icons.py", "borders": "nine_slice_detect.py",
          "gate": "verify_figma_vs_psd.py"}


def _sha(b):
    return hashlib.sha256(b).hexdigest()


def _json_atom(p):
    return "json:" + (_sha(p.read_bytes()) if p.is_file() else "absent")


def _psd_atom(p):
    if not p.exists():
        return f"psd:{p.name}:absent"
    st = p.stat()
    return f"psd:{p.name}:{st.st_size}:{st.st_mtime_ns}"


def _dir_atom(d):
    if not d.is_dir():
        return "dir:absent"
    rows = []
    for f in sorted(d.rglob("*")):
        if f.is_file():
            st = f.stat()
            rows.append(f"{f.relative_to(d)}:{st.st_size}:{st.st_mtime_ns}")
    return "dir:" + "|".join(rows)


def _icon_psds(cfg):
    icons = cfg.settings.get("icons", {})
    srcs = icons.get("sources") or ([icons] if "subdir" in icons else [])
    out = []
    for src in srcs:
        d = cfg.psd_dir.joinpath(*src["subdir"])
        if d.is_dir():
            out += sorted(d.rglob("*.psd"))
    return out


def stage_inputs(cfg, stage, screen_args):
    dd = cfg.data_dir
    tables = cfg.settings.get("tables", {})

    def data(name):
        return dd / tables.get(name, name)

    screens = cfg.load("screens")
    screen_psds = [cfg.psd_dir / v["psd"] for v in screens.values()]
    if stage == "manifest":
        atoms = [_json_atom(data("screens")), _json_atom(data("nodeNames")),
                 _json_atom(dd / "psd2figma.json")]
        atoms += [_psd_atom(p) for p in screen_psds]
    elif stage == "export":
        atoms = [_json_atom(dd / "psd_manifest.json")]
        atoms += [_psd_atom(p) for p in screen_psds]
    elif stage == "icons":
        atoms = [_json_atom(dd / "psd2figma.json")]
        atoms += [_psd_atom(p) for p in _icon_psds(cfg)]
    elif stage == "borders":
        plates = cfg.settings.get("export", {}).get("plates", [])
        atoms = [_dir_atom(dd / "assets"),
                 "plates:" + json.dumps(plates, sort_keys=True)]
    elif stage == "gate":
        atoms = [_json_atom(dd / f"figma_extract_{k}.json") for k in screens]
        atoms += [_json_atom(dd / "psd_manifest.json"),
                  _json_atom(dd / "accepted_debt.json"),
                  _json_atom(dd / "text_styles.json"),
                  "screen:" + json.dumps(sorted(screen_args))]
    return _sha("\n".join(atoms).encode())


def stage_outputs(cfg, stage):
    dd = cfg.data_dir
    return {
        "manifest": [dd / "psd_manifest.json"],
        "export": [dd / "assets", dd / "assets_index.json"],
        "icons": [dd / "icons", dd / "icons_index.json"],
        "borders": [dd / "nine_slice.json"],
        "gate": [dd / "verify_report.md", dd / "verify_report.json"],
    }[stage]


def stage_cmd(cfg, stage, screen_args):
    cmd = [sys.executable, str(SCRIPTS / SCRIPT[stage]),
           "--data-dir", str(cfg.data_dir),
           "--project-root", str(cfg.project_root)]
    if stage == "gate":
        cmd.append("--json")
        for s in screen_args:
            cmd += ["--screen", s]
    return cmd


def stamp_path(cfg, stage):
    return cfg.data_dir / ".pipeline" / f"{stage}.stamp"


def read_stamp(cfg, stage):
    p = stamp_path(cfg, stage)
    return json.loads(p.read_text()) if p.is_file() else None


def write_stamp(cfg, stage, digest, exit_code):
    p = stamp_path(cfg, stage)
    p.parent.mkdir(parents=True, exist_ok=True)
    p.write_text(json.dumps({"digest": digest, "exit": exit_code}))


def copy_before(cfg, stage):
    dest = cfg.data_dir / ".pipeline" / "before" / stage
    if dest.exists():
        shutil.rmtree(dest)
    dest.mkdir(parents=True)
    for out in stage_outputs(cfg, stage):
        if out.is_dir():
            shutil.copytree(out, dest / out.name)
        elif out.is_file():
            shutil.copy2(out, dest / out.name)
    return dest


def _collect(paths, base_of):
    files = {}
    for out in paths:
        if out.is_dir():
            for p in out.rglob("*"):
                if p.is_file():
                    files[str(p.relative_to(base_of(out)))] = p.read_bytes()
        elif out.is_file():
            files[out.name] = out.read_bytes()
    return files


def summarize(cfg, stage, before_dir):
    outs = stage_outputs(cfg, stage)
    after = _collect(outs, lambda o: o.parent)
    before = _collect([before_dir / o.name for o in outs], lambda o: before_dir)
    png_add = png_chg = png_del = 0
    changed_json = {}
    for k in set(after) | set(before):
        a, b = after.get(k), before.get(k)
        if a == b:
            continue
        if k.lower().endswith(".png"):
            png_del += b is not None and a is None
            png_add += b is None and a is not None
            png_chg += a is not None and b is not None
        else:
            changed_json[Path(k).name] = (b, a)
    parts = []
    if stage in ("export", "icons"):
        seg = f"+{png_add} png, {png_chg} changed"
        if png_del:
            seg += f", -{png_del}"
        parts.append(seg)
        if any(n.endswith("index.json") for n in changed_json):
            parts.append("index changed")
    if stage == "manifest":
        parts.append(_json_count(changed_json.get("psd_manifest.json"),
                                 "layers", "layer"))
    if stage == "borders":
        parts.append(_json_count(changed_json.get("nine_slice.json"),
                                 None, "plate"))
    if stage == "gate":
        parts.append("report changed" if "verify_report.md" in changed_json
                     else "report unchanged")
    return ", ".join(parts) if parts else "no change"


def _json_count(pair, list_key, noun):
    if pair is None:
        return f"0 {noun}s changed"
    b, a = pair
    try:
        bd = json.loads(b) if b else {}
        ad = json.loads(a) if a else {}
    except ValueError:
        return "changed"
    if list_key:
        bl, al = bd.get(list_key, []), ad.get(list_key, [])
        n = abs(len(bl) - len(al)) + sum(1 for x, y in zip(bl, al) if x != y)
    else:
        n = sum(1 for k in set(bd) | set(ad) if bd.get(k) != ad.get(k))
    return f"{n} {noun}{'' if n == 1 else 's'} changed"


def collision_stems(text):
    stems = []
    for line in text.splitlines():
        if "STEM COLLISION" in line:
            stem = line.split("STEM COLLISION", 1)[1].strip().split(":", 1)[0].strip()
            if stem and stem not in stems:
                stems.append(stem)
    return stems


def cmd_status(cfg, screen_args):
    own = {s: (read_stamp(cfg, s) or {}).get("digest") != stage_inputs(cfg, s, screen_args)
           for s in STAGES}
    stale = {}
    for s in STAGES:
        stale[s] = own[s] or any(stale[d] for d in DEPS[s])
    for s in STAGES:
        reason = "inputs changed" if own[s] else (
            "upstream stale" if stale[s] else "up to date")
        print(f"{s:9s} {'stale' if stale[s] else 'fresh':5s}  ({reason})")
    return 0


def cmd_run(cfg, screen_args, requested, force):
    worst = 0
    for stage in STAGES:
        if stage not in requested:
            continue
        digest = stage_inputs(cfg, stage, screen_args)
        stamp = read_stamp(cfg, stage)
        if not force and stamp is not None and stamp.get("digest") == digest:
            print(f"{stage}: skip (up to date)")
            worst = max(worst, stamp.get("exit", 0))
            continue
        before = copy_before(cfg, stage)
        proc = subprocess.run(stage_cmd(cfg, stage, screen_args),
                              capture_output=True, text=True)
        rc = proc.returncode
        if rc == 3:
            stems = collision_stems(proc.stdout + proc.stderr)
            print(f"{stage}: STOPPED exit 3 — stem collision: "
                  f"{', '.join(stems) or '(unnamed)'}")
            return 3
        print(f"{stage}: {summarize(cfg, stage, before)} (exit {rc})")
        if rc in (0, 1):
            write_stamp(cfg, stage, digest, rc)
        worst = max(worst, rc)
    return worst


def main():
    cfg, argv = resolve()
    ap = argparse.ArgumentParser(prog="pipeline")
    sub = ap.add_subparsers(dest="cmd", required=True)
    pr = sub.add_parser("run")
    pr.add_argument("--stages")
    pr.add_argument("--screen", action="append")
    pr.add_argument("--force", action="store_true")
    ps = sub.add_parser("status")
    ps.add_argument("--screen", action="append")
    args = ap.parse_args(argv)

    screen_args = args.screen or []
    if args.cmd == "status":
        sys.exit(cmd_status(cfg, screen_args))

    if args.stages:
        requested = [s.strip() for s in args.stages.split(",") if s.strip()]
        unknown = [s for s in requested if s not in STAGES]
        if unknown:
            ap.error(f"unknown stage(s): {', '.join(unknown)}; "
                     f"choose from {', '.join(STAGES)}")
        requested = set(requested)
    else:
        requested = set(STAGES)
    sys.exit(cmd_run(cfg, screen_args, requested, args.force))


if __name__ == "__main__":
    main()
