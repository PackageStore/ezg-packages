#!/usr/bin/env python3
"""make_vfx — CLI of the game-vfx skill. Run from the Unity project root (python3; `py` on Windows).

  python3 make_vfx.py list                        # recipes, elements (incl. the project's ramps), name groups
  python3 make_vfx.py config                      # resolved project config: VFX root, sorting layers, grounds, refs
  python3 make_vfx.py install [--showcase]        # copy the Unity kit (Editor builder + asmdef [+ showcase driver])
  python3 make_vfx.py shared                      # spark / mote sprites shared by every VFX
  python3 make_vfx.py make --recipe explode_burst --element fire --name explode_fire
                      [--subject hero] [--group hit] [--size 8.5] [--life 0.72] [--seed 11] [--frames 16] [--px 320]
                      [--out ROOT] [--layer FX]
  python3 make_vfx.py contact --frames <unity render dir> --out sheet.png [--fps 60] [--count 8]
  python3 make_vfx.py grid --root <render root> --names a,b,c --out preview.mp4 [--cols 4] [--fps 60]   (needs ffmpeg)
  python3 make_vfx.py compare --names fx_hit_explode_fire --kinds explode --out compare.png

`make` paints the sheet(s), writes <Base>.gamevfx.json (the spec GameVfxPrefabBuilder reads; it records the exact command)
next to them in <root>/<Base>/, writes review sheets over the game's darkest and brightest ground to
Temp/GameVfx/<Base>/ (git-ignored, throwaway) and prints a QA report.

Names follow the game-vfx standard (GameVFX_QuyChuan.md 8.2):
  Base     fx_[<subject>_]<group>_<name>               fx_hit_explode_fire, fx_hero_cast_fireball
  sheet    FX_TX_<Name>_<cols>x<rows>.png               FX_TX_ExplodeFire_4x4.png (sprites FX_TX_ExplodeFire_4x4_<i>)
  layer    FX_TX_<Name><Layer>_<cols>x<rows>.png        FX_TX_MeteorFireHead_4x2.png
  stream   FX_TX_<Name><Stream>.png                     FX_TX_BladeStormDagger.png
  prefab   <Base>.prefab (built in Unity by GameVfxPrefabBuilder)
Root folder: --out, else `vfxRoot` in .claude/project-profile.json, else DEFAULT_VFX_ROOT.
Project style (ArtStyle.md §6b, fenced block ```vfx-style```): default element, allowed elements, extra / overridden
element ramps, sorting layers, review grounds and the approved in-game FX used by `compare`.
"""
import argparse
import glob
import json
import math
import os
import re
import shutil
import sys
import zlib

import numpy as np
from PIL import Image, ImageDraw, ImageFont

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
from fxpaint import sheet  # noqa: E402
from vfx_recipes import PALETTES, RECIPES, SPRITES, palette, register_palette  # noqa: E402

DEFAULT_VFX_ROOT = "Assets/_Project/Visual/ArtAsset/Shared/VFX/Generated"
PREVIEW_ROOT = "Temp/GameVfx"                  # review images only: git-ignored, safe to lose
ART_STYLE = ".claude/docs/ArtStyle.md"
PROFILE = ".claude/project-profile.json"
DEFAULT_LAYERS = ("FX_Ground", "FX")             # game-vfx 7.3 example names: ground (below characters), main
DEFAULT_GROUNDS = ("#1c2027", "#c9ccd1")         # neutral darkest / brightest review grounds (game-vfx 5.5.1)
EDITOR_KIT_DIR = "Editor/GameVfxKit"                 # under the profile sourceRoot
# game-vfx 8.2 name groups (lowercase, fixed list: change the standard first, then this list)
GROUPS = ("hit", "proj", "muzzle", "slash", "cast", "skill", "aoe", "warn", "buff", "debuff", "heal", "shield",
          "status", "aura", "spawn", "death", "pickup", "env", "ui")
KINDS = ("impact", "muzzle", "explode", "cast", "slash", "projectile", "aura")
# Kieu tron cua lop (game-vfx 8.4): "ab" = lop nen alpha blend (glow ve san vao alpha, gan _mat_ab), "add" = lop cong sang
# dat TREN lop ab (gan _mat_add). Lop chinh cua recipe luon la "ab"; lop "add" khong bat buoc, chi khai khi lop ab chua du sang.
BLENDS = ("ab", "add")
ADD_ORDER_BIAS = 1                               # lop add khong khai sorting_order: ngay tren lop nen ab (order 10)


def blend_of(entry, where):
    b = entry.get("blend", "ab")
    if b not in BLENDS:
        sys.exit(f"{where}: blend '{b}' sai, chi nhan {', '.join(BLENDS)} (game-vfx 8.4)")
    return b
SNAKE = re.compile(r"^[a-z0-9]+(_[a-z0-9]+)*$")


# ============================================================================ project config
def load_profile():
    try:
        return json.load(open(PROFILE, encoding="utf-8"))
    except (OSError, ValueError):
        return {}


def vfx_root(args_out=None):
    return (args_out or load_profile().get("vfxRoot") or DEFAULT_VFX_ROOT).rstrip("/").replace("\\", "/")


def source_root():
    return (load_profile().get("sourceRoot") or "Assets/_Project").rstrip("/")


def parse_style():
    """The ```vfx-style``` block of ArtStyle.md §6b. Lines `key: value`; `#` starts a comment. Keys:
    default, allow, layers, grounds, `ramp <element>`, `ref <kind>`."""
    cfg = dict(default="", allow=[], layers=list(DEFAULT_LAYERS), grounds=list(DEFAULT_GROUNDS), ramps={},
               refs={}, source=None, errors=[])
    if not os.path.exists(ART_STYLE):
        return cfg
    text = open(ART_STYLE, encoding="utf-8").read()
    m = re.search(r"^```vfx-style[ \t]*\n(.*?)^```", text, re.M | re.S)
    if not m:
        return cfg
    cfg["source"] = ART_STYLE
    for raw in m.group(1).splitlines():
        # comments: "# …" lines and trailing "  # …" (a hash followed by a space; colours are "#rrggbb")
        line = "" if re.match(r"\s*#(\s|$)", raw) else re.sub(r"\s+#\s.*$", "", raw).strip()
        if not line or ":" not in line:
            continue
        key, val = (s.strip() for s in line.split(":", 1))
        try:
            if key == "default":
                cfg["default"] = val
            elif key == "allow":
                cfg["allow"] = [v.strip() for v in val.split(",") if v.strip()]
            elif key == "layers":
                parts = [v.strip() for v in val.split(",") if v.strip()]
                if len(parts) == 2:
                    cfg["layers"] = parts
                else:
                    raise ValueError("layers: <ground layer>, <main layer>")
            elif key == "grounds":
                cfg["grounds"] = re.findall(r"#[0-9a-fA-F]{6}", val) or list(DEFAULT_GROUNDS)
            elif key.startswith("ramp "):
                name = key[5:].strip()
                parts = [p.strip() for p in val.split("|")]
                stops = [(float(a), b) for a, b in re.findall(r"([0-9.]+)\s*:\s*(#[0-9a-fA-F]{6})", parts[0])]
                extra = {}
                for p in parts[1:]:
                    k, _, rest = p.partition(" ")
                    extra[k] = re.findall(r"#[0-9a-fA-F]{6}", rest)
                cfg["ramps"][name] = dict(stops=stops, spark=extra.get("spark"),
                                          soot=(extra.get("soot") or [None])[0],
                                          haze=[(0.0, extra["haze"][0]), (1.0, extra["haze"][1])]
                                          if len(extra.get("haze") or []) == 2 else None)
            elif key.startswith("ref "):
                kind = key[4:].strip()
                rm = re.match(r"(.+?)\s+(\d+)x(\d+)\s*@\s*(\d+)\s*,\s*(\d+)$", val)
                if not rm:
                    raise ValueError("ref <kind>: <sheet.png> <cols>x<rows> @<col>,<row>")
                cfg["refs"].setdefault(kind, []).append(
                    (rm.group(1).strip(), int(rm.group(2)), int(rm.group(3)), int(rm.group(4)), int(rm.group(5))))
        except (ValueError, IndexError) as e:
            cfg["errors"].append(f"{raw.strip()}  ->  {e}")
    for name, r in list(cfg["ramps"].items()):
        try:
            register_palette(name, r["stops"], spark=r["spark"], soot=r["soot"], haze=r["haze"])
        except ValueError as e:
            cfg["errors"].append(str(e))
            del cfg["ramps"][name]
    return cfg


# ============================================================================ helpers
def font(sz=18):
    for name in ("arialbd.ttf", "Arial Bold.ttf", "/System/Library/Fonts/Supplemental/Arial Bold.ttf",
                 "DejaVuSans-Bold.ttf", "/usr/share/fonts/truetype/dejavu/DejaVuSans-Bold.ttf"):
        try:
            return ImageFont.truetype(name, sz)
        except OSError:
            continue
    return ImageFont.load_default()


def rgba(hex_colour):
    h = hex_colour.lstrip("#")
    return tuple(int(h[i:i + 2], 16) for i in (0, 2, 4)) + (255,)


def on_ground(img, ground):
    bg = Image.new("RGBA", img.size, rgba(ground))
    bg.alpha_composite(img.convert("RGBA"))
    return bg


def review_sheet(frames, cols, grounds, label):
    """The sheet over each review ground, stacked (darkest first): an effect must read on every ground (5.5.1)."""
    rows = [sheet([on_ground(f, g) for f in frames], cols).convert("RGB") for g in grounds]
    out = Image.new("RGB", (rows[0].width, sum(r.height for r in rows)))
    y = 0
    for r in rows:
        out.paste(r, (0, y))
        y += r.height
    ImageDraw.Draw(out).text((6, 4), label, fill=(255, 255, 255), font=font(16))
    return out


def grid_cols(n):
    """Columns so cols * rows == n exactly: the builder slices exactly `frames` sprites, blank tiles would flash."""
    for c in range(max(1, math.ceil(math.sqrt(n))), n + 1):
        if n % c == 0:
            return c
    return n


def pascal(*parts):
    return "".join(w[:1].upper() + w[1:] for p in parts for w in re.split(r"[_\s]+", p) if w)


def check_root():
    if not os.path.isdir("Assets"):
        sys.exit("make_vfx: run from the Unity project root (no Assets/ here).")


def output_layout(args, recipe):
    """(out_dir, base, texture stem). Base = fx_[<subject>_]<group>_<name> (game-vfx 8.2)."""
    group = (args.group or recipe["group"]).lower()
    if group not in GROUPS:
        sys.exit(f"make: --group {group} is not a game-vfx 8.2 group ({', '.join(GROUPS)})")
    for flag, value in (("--name", args.name), ("--subject", args.subject)):
        if value and not SNAKE.match(value):
            sys.exit(f"make: {flag} must be snake_case ASCII (a-z, 0-9, _), got {value}")
    name = args.name[3:] if args.name.startswith("fx_") else args.name
    base = "_".join(p for p in ("fx", args.subject, group, name) if p)
    out_dir = f"{vfx_root(args.out)}/{base}"
    return out_dir, base, "FX_TX_" + pascal(args.subject or "", name)


def find_spec(name, root):
    """Spec path of a VFX by base name, or a .gamevfx.json path as is."""
    if name.endswith(".gamevfx.json"):
        return name
    hits = glob.glob(os.path.join(root, "**", f"{name}.gamevfx.json"), recursive=True)
    if not hits:
        hits = glob.glob(os.path.join("Assets", "**", f"{name}.gamevfx.json"), recursive=True)
    if not hits:
        sys.exit(f"no spec {name}.gamevfx.json under {root} (or Assets/)")
    return hits[0]


# ============================================================================ QA
FAST_KINDS = ("impact", "muzzle", "explode", "slash")    # must read on frame 0


def qa(frames, loop, kind=""):
    """Automatic checks on the painted frames. Returns list of (code, message)."""
    A = [np.asarray(f, np.float32)[..., 3] / 255 for f in frames]
    issues = []
    border = max(max(a[:2].max(), a[-2:].max(), a[:, :2].max(), a[:, -2:].max()) for a in A)
    if border > 0.06:
        issues.append(("CLIPPED", f"alpha {border:.2f} on the cell border: the effect is cut by the frame "
                                  "(shrink radii / reach in the recipe)"))
    if not loop:
        if kind in FAST_KINDS and A[0].mean() < 0.002:
            issues.append(("SLOW_START", "frame 0 is almost empty: hits must read on the first frame"))
        if A[-1].max() > 0.15:
            issues.append(("POP_AT_END", f"last frame still has alpha {A[-1].max():.2f}: fade everything out by t=1"))
    diffs = [float(np.abs(A[i + 1] - A[i]).mean()) for i in range(len(A) - 1)]
    med = float(np.median(diffs)) if diffs else 0.0
    for i, d in enumerate(diffs):
        if d > max(3.5 * med, 0.012) and i > 0:
            issues.append(("JUMP", f"frame {i}->{i + 1} changes {d:.3f} (median {med:.3f}): add an in-between beat"))
    if loop:
        seam = float(np.abs(A[0] - A[-1]).mean())
        if seam > max(2.5 * med, 0.01):
            issues.append(("LOOP_SEAM", f"last->first change {seam:.3f} vs median {med:.3f}: loop is not seamless"))
    union = np.max(np.stack(A), 0) > 0.05
    if union.any():
        ys, xs = np.where(union)
        cover = max(xs.max() - xs.min(), ys.max() - ys.min()) / A[0].shape[0]
        if cover < 0.45:
            issues.append(("WASTED_SPACE", f"effect spans only {cover:.0%} of the frame: lower --px or enlarge shapes"))
    return issues


# ============================================================================ commands
def cmd_list(_):
    style = parse_style()
    print("Recipes (group / kind / frames / life / size):")
    for k, r in RECIPES.items():
        print(f"  {k:15s} {r['group']:7s} {r['kind']:10s} {r['frames']:2d}f {r['life']:.2f}s size {r['size']:<5} {r['desc']}")
    project = [n for n in style["ramps"]]
    print("Elements: " + ", ".join(n for n in PALETTES if n not in project)
          + (f"  | project ramps ({ART_STYLE}): {', '.join(project)}" if project else ""))
    if style["allow"]:
        print("Allowed by the project: " + ", ".join(style["allow"]))
    print("Groups (game-vfx 8.2): " + ", ".join(GROUPS))


def cmd_config(_):
    style = parse_style()
    print(json.dumps(dict(
        vfxRoot=vfx_root(), sharedDir=vfx_root() + "/_Shared", editorKit=f"{source_root()}/{EDITOR_KIT_DIR}",
        styleSource=style["source"] or f"(no ```vfx-style``` block in {ART_STYLE}: defaults)",
        defaultElement=style["default"] or "(per recipe)", allow=style["allow"] or "(all)",
        sortingLayers=dict(ground=style["layers"][0], main=style["layers"][1]), grounds=style["grounds"],
        projectRamps=list(style["ramps"]), references={k: [r[0] for r in v] for k, v in style["refs"].items()},
        errors=style["errors"]), indent=2))


def kit_files(showcase):
    """skill unity/ file -> project destination."""
    editor = f"{source_root()}/{EDITOR_KIT_DIR}"
    kit = {"GameVfxPrefabBuilder.cs": f"{editor}/GameVfxPrefabBuilder.cs",
           "Ezg.GameVfx.Editor.asmdef": f"{editor}/Ezg.GameVfx.Editor.asmdef"}
    if showcase:
        kit["GameVfxShowcaseLoop.cs"] = f"{vfx_root()}/_Kit/GameVfxShowcaseLoop.cs"
    return kit


def cmd_install(args):
    check_root()
    lock = open("Packages/packages-lock.json", encoding="utf-8").read() if os.path.exists("Packages/packages-lock.json") else ""
    if '"com.unity.2d.sprite"' not in lock:
        print("WARNING   package com.unity.2d.sprite is not in Packages/packages-lock.json: the builder's slicing API "
              "(UnityEditor.U2D.Sprites) will not compile. Add it with Package Manager first.")
    for src_name, dst in kit_files(args.showcase).items():
        data = open(os.path.join(HERE, "..", "unity", src_name), "rb").read()
        if os.path.exists(dst):
            if open(dst, "rb").read() == data:
                print(f"ok        {dst}")
                continue
            if args.no_overwrite:
                print(f"DIFFERS   {dst} (kept; rerun without --no-overwrite to update)")
                continue
            open(dst, "wb").write(data)
            print(f"updated   {dst}")
            continue
        os.makedirs(os.path.dirname(dst), exist_ok=True)
        open(dst, "wb").write(data)
        print(f"installed {dst}")
    print("-> Assets/Refresh in Unity, wait for compile, check errors (compile-check).")


def cmd_shared(args):
    check_root()
    shared = vfx_root() + "/_Shared"
    os.makedirs(shared, exist_ok=True)
    for name, fn in SPRITES.items():
        path = f"{shared}/FX_TX_{name}.png"
        if os.path.exists(path) and not args.force:
            print(f"exists    {path}")
            continue
        fn().save(path)
        print(f"wrote     {path}")


def cmd_make(args):
    check_root()
    style = parse_style()
    for e in style["errors"]:
        print(f"WARNING   {ART_STYLE} vfx-style: {e}")
    if args.recipe not in RECIPES:
        sys.exit(f"unknown recipe {args.recipe}; see `list`")
    if args.element not in PALETTES:
        sys.exit(f"unknown element {args.element}; see `list` (project ramps live in {ART_STYLE} §6b)")
    if style["allow"] and args.element not in style["allow"]:
        sys.exit(f"element {args.element} is not allowed by {ART_STYLE} §6b (allow: {', '.join(style['allow'])}); "
                 "ask the dev before widening the list")
    r = RECIPES[args.recipe]
    n = args.frames or r["frames"]
    cols = r["cols"] if n == r["frames"] else grid_cols(n)
    px = args.px or r["px"]
    seed = args.seed if args.seed is not None else r.get("seed", zlib.crc32(args.recipe.encode()) % 9000 + 100)
    size = args.size or r["size"]
    life = args.life or r["life"]
    scale = size / r["size"]
    loop = bool(r.get("loop"))
    ground_layer, main_layer = style["layers"]
    main_layer = args.layer or main_layer
    grounds = style["grounds"]

    def layer_of(value):
        return ground_layer if value == "ground" else main_layer

    out_dir, name, stem = output_layout(args, r)
    before = set(os.listdir(out_dir)) if os.path.isdir(out_dir) else set()
    frames = r["fn"](palette(args.element), seed, n, px)
    rows = (len(frames) + cols - 1) // cols
    sheet_name = f"{stem}_{cols}x{rows}.png"
    os.makedirs(out_dir, exist_ok=True)
    sheet(frames, cols).save(os.path.join(out_dir, sheet_name))

    def scaled(e):
        e = dict(e)
        for k in ("radius",):
            e[k] = e.get(k, 0) * scale
        for k in ("speed", "size", "rise"):
            if k in e:
                e[k] = [v * scale for v in e[k]]
        return e
    extras = [scaled(e) for e in r.get("extras", [])]
    for e in extras:
        e["blend"] = blend_of(e, f"extra {e.get('type', '?')}")
    issues = qa(frames, loop, r["kind"])
    pdir = os.path.join(PREVIEW_ROOT, name)
    os.makedirs(pdir, exist_ok=True)

    # extra flipbook layers (multi-sheet recipes): own sheet, timing, motion and birth sub-emitter trails
    flipbooks, layer_reports = [], []
    for k, L in enumerate(r.get("layers", [])):
        lframes = L["fn"](palette(args.element), seed + 101 * (k + 1), L["frames"], L["px"])
        lrows = (len(lframes) + L["cols"] - 1) // L["cols"]
        lsheet = f"{stem}{pascal(L['name'])}_{L['cols']}x{lrows}.png"
        sheet(lframes, L["cols"]).save(os.path.join(out_dir, lsheet))
        lissues = qa(lframes, bool(L.get("loop")), L.get("kind", ""))
        lblend = blend_of(L, f"layer {L['name']}")
        flipbooks.append(dict(
            name=L["name"], role=L.get("role", "glow"), secondary=bool(L.get("secondary")), sheet=lsheet, blend=lblend,
            cols=L["cols"], rows=lrows, frames=len(lframes), life=L["life"], cycle=L.get("cycle", 0.0),
            size=L["size"] * scale, delay=L.get("delay", 0.0), position=[v * scale for v in L.get("position", [0, 0])],
            velocity=[v * scale for v in L.get("velocity", [0, 0])], randomRotation=bool(L.get("random_rotation")),
            lengthScale=L.get("lengthScale", 0.0), velocityScale=L.get("velocityScale", 0.0),
            fade=list(L.get("fade", [0, 0])), grow=list(L.get("grow", [1, 1])),
            sortingLayer=layer_of(L.get("sorting_layer", "")),
            sortingOrder=L.get("sorting_order", 10 + (ADD_ORDER_BIAS if lblend == "add" else 0)),
            trail=[dict(scaled(e), blend=blend_of(e, f"trail of {L['name']}")) for e in L.get("trail", [])], trailBack=L.get("trailBack", 0.0) * scale,
            qa=[f"{c}: {m}" for c, m in lissues]))
        review_sheet(lframes, L["cols"], grounds, f"{name} / {L['name']}").save(
            os.path.join(pdir, f"{name}_{L['name']}.sheet.png"))
        layer_reports.append((L["name"], lsheet, lissues))
        issues += [(c, f"[{L['name']}] {m}") for c, m in lissues]
    # stream layers: a painted single sprite (thrown daggers…) emitted in a loop, stretched along its velocity
    streams = []
    for S in r.get("streams", []):
        ssprite = f"{stem}{pascal(S['name'])}.png"
        simg = S["fn"](palette(args.element))
        simg.save(os.path.join(out_dir, ssprite))
        review_sheet([simg.resize((simg.width * 3, simg.height), Image.LANCZOS)], 1, grounds, S["name"]).save(
            os.path.join(pdir, f"{name}_{S['name']}.sheet.png"))      # previewed stretched 3x like in game
        streams.append(dict(
            name=S["name"], role=S.get("role", "debris"), blend=blend_of(S, f"stream {S['name']}"), sprite=ssprite,
            rate=S["rate"], life=S["life"],
            speed=[v * scale for v in S["speed"]], size=[v * scale for v in S["size"]], radius=S["radius"] * scale,
            arcSpeed=S["arcSpeed"], orbital=S["orbital"], drag=S["drag"], lengthScale=S["lengthScale"],
            squash=S["squash"], fade=S["fade"], sortingOrder=S["sortingOrder"]))
        layer_reports.append((S["name"], ssprite, []))
    group = (args.group or r["group"]).lower()
    spec = dict(
        name=name, subject=args.subject or "", group=group, role=r.get("role", "impact"), recipe=args.recipe,
        element=args.element, kind=r["kind"], seed=seed, blend="ab",   # lop chinh = lop nen ab (game-vfx 8.4)
        sheet=sheet_name, cols=cols, rows=rows, frames=len(frames),
        life=life, size=size, loop=loop, delay=r.get("delay", 0.0),
        randomRotation=bool(r.get("random_rotation")), alignLocal=bool(r.get("align_local")),
        offsetX=r.get("offset", [0, 0])[0], offsetY=r.get("offset", [0, 0])[1],
        sortingLayer=layer_of(r.get("sorting_layer", "")), sortingOrder=10,
        sparkColors=["#ffffff"] + PALETTES[args.element]["spark"], sharedDir=vfx_root() + "/_Shared",
        sparks=[e for e in extras if e["type"] == "sparks"], motes=[e for e in extras if e["type"] == "motes"],
        flipbooks=flipbooks,
        spin=r.get("spin", 0.0), squash=r.get("squash", 1.0), intro=r.get("intro", 0.0), streams=streams,
        reviewGrounds=list(grounds),
        qa=[f"{c}: {m}" for c, m in issues],
        command=(f"python3 .claude/skills/game-vfx/scripts/make_vfx.py make --recipe {args.recipe} "
                 f"--element {args.element} --name {args.name}"
                 + (f" --subject {args.subject}" if args.subject else "") + (f" --group {args.group}" if args.group else "")
                 + (f" --out {args.out}" if args.out else "") + (f" --layer {args.layer}" if args.layer else "")
                 + f" --seed {seed} --size {size} --life {life} --frames {len(frames)} --px {px}"),
        tool="Pillow (game-vfx) - no AI image model",
    )
    spec_path = f"{out_dir}/{name}.gamevfx.json"
    json.dump(spec, open(spec_path, "w", encoding="utf-8"), indent=2)

    review_sheet(frames, cols, grounds, f"{name}  ({args.recipe} x {args.element})").save(
        os.path.join(pdir, f"{name}.sheet.png"))
    print(f"sheet  {out_dir}/{sheet_name}  ({cols}x{rows}, {len(frames)} frames @ {px}px, seed {seed})")
    print(f"spec   {spec_path}")
    print(f"review {pdir}/{name}.sheet.png  (grounds {', '.join(grounds)})")
    print(f"layers sorting: main {spec['sortingLayer']}" + (f", ground {ground_layer}" if any(
        fb["sortingLayer"] == ground_layer for fb in flipbooks) or spec["sortingLayer"] == ground_layer else ""))
    for lname, lsheet, _ in layer_reports:
        print(f"layer  {lname:8s} {out_dir}/{lsheet}  review {pdir}/{name}_{lname}.sheet.png")
    print("QA     " + ("pass" if not issues else ""))
    for c, m in issues:
        print(f"  - {c}: {m}")
    written = {sheet_name, os.path.basename(spec_path)} | {f for _, f, _ in layer_reports}
    report_folder(out_dir, before & written)


def report_folder(out_dir, overwritten):
    """Say what was overwritten in place (same .meta = same GUID) and what in the folder no longer belongs to the
    spec (an older grid size, a renamed layer, hand-made art): delete it once nothing references its GUID."""
    kit = set()
    for path in glob.glob(os.path.join(out_dir, "*.gamevfx.json")):
        s = json.load(open(path, encoding="utf-8"))
        kit |= {os.path.basename(path), s["sheet"], f"{s['name']}.prefab"}
        kit |= {fb["sheet"] for fb in s.get("flipbooks", [])} | {st["sprite"] for st in s.get("streams", [])}
    for f in sorted(overwritten):
        print(f"kept   {out_dir}/{f}  (overwritten in place, same .meta = same GUID)")
    folder_meta = out_dir + ".meta"
    folder_guid = next((ln.split()[1] for ln in open(folder_meta) if ln.startswith("guid:")), "?") \
        if os.path.exists(folder_meta) else "?"
    for f in sorted(f for f in os.listdir(out_dir) if not f.endswith(".meta") and f not in kit):
        if f.endswith(".spriteatlasv2") or f.endswith(".spriteatlas"):
            packs_folder = f"fileID: 102900000, guid: {folder_guid}" in open(os.path.join(out_dir, f)).read()
            print(f"atlas  {out_dir}/{f}  " + ("(packs this folder: the new sprites join it; keep it)" if packs_folder
                                                else "(lists files, not the folder: add the new sheets or drop the old ones from it)"))
        else:
            print(f"old    {out_dir}/{f}  (not in any spec here: delete once nothing references its GUID)")


def cmd_contact(args):
    fs = sorted(glob.glob(os.path.join(args.frames, "f_*.png")))
    if not fs:
        sys.exit("no frames")
    k = args.count
    idx = [round(1 + (len(fs) - 2) * j / max(k - 1, 1)) for j in range(k)]
    T = 220
    out = Image.new("RGB", (T * k, T), (12, 12, 14))
    d = ImageDraw.Draw(out)
    for j, i in enumerate(idx):
        out.paste(Image.open(fs[i]).convert("RGB").resize((T, T), Image.LANCZOS), (j * T, 0))
        d.text((j * T + 4, 3), f"{i / args.fps:.2f}s", fill=(255, 255, 255), font=font(14))
    out.save(args.out)
    print("wrote", args.out)


def cmd_grid(args):
    """Loop every rendered effect in its own cell -> mp4 (and gif when --out ends with .gif). Needs ffmpeg."""
    if not shutil.which("ffmpeg"):
        sys.exit("grid: ffmpeg not found on PATH (optional step: use `contact` instead)")
    names = args.names.split(",")
    seqs = {n: [Image.open(p).convert("RGB") for p in sorted(glob.glob(os.path.join(args.root, n, "f_*.png")))] for n in names}
    seqs = {n: s for n, s in seqs.items() if s}
    if not seqs:
        sys.exit("grid: no rendered frames under --root for --names")
    C = next(iter(seqs.values()))[0].width
    cols = min(args.cols, len(seqs))
    rows = (len(seqs) + cols - 1) // cols
    hdr = 34
    tmp = os.path.join(os.path.dirname(os.path.abspath(args.out)), "_grid_frames")
    os.makedirs(tmp, exist_ok=True)
    total = int(args.seconds * args.fps)
    pause = int(0.4 * args.fps)
    for i in range(total):
        canvas = Image.new("RGB", (C * cols, (C + hdr) * rows), (14, 14, 16))
        d = ImageDraw.Draw(canvas)
        for k, (n, fr) in enumerate(seqs.items()):
            cyc = len(fr) + pause
            j = i % cyc
            x, y = (k % cols) * C, (k // cols) * (C + hdr)
            canvas.paste(fr[min(j, len(fr) - 1)], (x, y + hdr))
            d.text((x + 10, y + 6), n, fill=(255, 236, 190), font=font(20))
        canvas.save(os.path.join(tmp, f"g_{i:04d}.png"))
    pat = os.path.join(tmp, "g_%04d.png")
    if args.out.endswith(".gif"):
        vf = "fps=30,scale=1200:-1:flags=lanczos,split[s0][s1];[s0]palettegen=max_colors=220[p];[s1][p]paletteuse=dither=sierra2_4a"
        os.system(f'ffmpeg -y -loglevel error -framerate {args.fps} -i "{pat}" -vf "{vf}" -loop 0 "{args.out}"')
    else:
        os.system(f'ffmpeg -y -loglevel error -framerate {args.fps} -i "{pat}" -c:v libx264 -pix_fmt yuv420p -crf 16 "{args.out}"')
    shutil.rmtree(tmp, ignore_errors=True)
    print("wrote", args.out)


def cmd_compare(args):
    """Top row: the project's approved in-game FX for the kinds (ArtStyle.md §6b `ref` lines); bottom row: a mid
    frame of each new sheet. Both on the darkest review ground."""
    style = parse_style()
    ground = style["grounds"][0]
    C = 230

    def cell(path, cols, rows, c, r):
        im = Image.open(path).convert("RGBA")
        w, h = im.width // cols, im.height // rows
        fr = im.crop((c * w, r * h, (c + 1) * w, (r + 1) * h))
        s = max(w, h)
        sq = Image.new("RGBA", (s, s), (0, 0, 0, 0))
        sq.paste(fr, ((s - w) // 2, (s - h) // 2))
        return sq
    refs, missing = [], []
    for kind in args.kinds.split(","):
        for p, c, r, cc, rr in style["refs"].get(kind.strip(), []):
            if os.path.exists(p):
                refs.append((os.path.basename(p).split(".")[0], cell(p, c, r, cc, rr)))
            else:
                missing.append(p)
    mine = []
    for n in args.names.split(","):
        spec_path = find_spec(n.strip(), vfx_root(args.out_root))
        spec = json.load(open(spec_path, encoding="utf-8"))
        idx = spec["frames"] // 3
        mine.append((spec["name"], cell(os.path.join(os.path.dirname(spec_path), spec["sheet"]), spec["cols"], spec["rows"],
                                        idx % spec["cols"], idx // spec["cols"])))
    width = max(len(refs), len(mine), 1) * C + 170
    out = Image.new("RGB", (width, 2 * (C + 28)), (14, 14, 16))
    d = ImageDraw.Draw(out)
    for row, (label, items) in enumerate((("In-game FX", refs), ("New VFX", mine))):
        y = row * (C + 28)
        d.text((8, y + C // 2), label, fill=(255, 236, 190), font=font(18))
        if not items:
            d.text((178, y + C // 2), "(no `ref` for this kind in ArtStyle.md §6b)", fill=(200, 120, 120), font=font(16))
        for k, (n, im) in enumerate(items):
            t = Image.new("RGBA", (C, C), rgba(ground))
            t.alpha_composite(im.resize((C, C), Image.LANCZOS))
            out.paste(t.convert("RGB"), (170 + k * C, y + 28))
            d.text((170 + k * C + 6, y + 5), n, fill=(220, 220, 220), font=font(16))
    out.save(args.out)
    print("wrote", args.out)
    for p in missing:
        print(f"missing ref {p} (ArtStyle.md §6b points at a file that is gone)")
    if not refs:
        print("no reference FX declared for these kinds: the style gate is the dev's review of the review sheets "
              "(say so in the report)")


def main():
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    sub = ap.add_subparsers(dest="cmd", required=True)
    sub.add_parser("list")
    sub.add_parser("config")
    p = sub.add_parser("install")
    p.add_argument("--no-overwrite", action="store_true")
    p.add_argument("--showcase", action="store_true", help="also install the runtime showcase driver (GameVfxShowcaseLoop)")
    p = sub.add_parser("shared")
    p.add_argument("--force", action="store_true")
    p = sub.add_parser("make")
    p.add_argument("--recipe", required=True)
    p.add_argument("--element", required=True)
    p.add_argument("--name", required=True, help="snake_case effect name, e.g. explode_fire -> fx_<group>_explode_fire")
    p.add_argument("--subject", help="snake_case owner (game-vfx 8.2 'prefab of an object'): fx_<subject>_<group>_<name>")
    p.add_argument("--group", help="game-vfx 8.2 group; default: the recipe's")
    p.add_argument("--out", help="root folder (default: vfxRoot of .claude/project-profile.json)")
    p.add_argument("--size", type=float)
    p.add_argument("--life", type=float)
    p.add_argument("--seed", type=int)
    p.add_argument("--frames", type=int)
    p.add_argument("--px", type=int)
    p.add_argument("--layer", help="main sorting layer (default: ArtStyle.md §6b `layers`, else FX)")
    p = sub.add_parser("contact")
    p.add_argument("--frames", required=True)
    p.add_argument("--out", required=True)
    p.add_argument("--fps", type=float, default=60)
    p.add_argument("--count", type=int, default=8)
    p = sub.add_parser("grid")
    p.add_argument("--root", required=True)
    p.add_argument("--names", required=True)
    p.add_argument("--out", required=True)
    p.add_argument("--cols", type=int, default=4)
    p.add_argument("--fps", type=float, default=60)
    p.add_argument("--seconds", type=float, default=3.0)
    p = sub.add_parser("compare")
    p.add_argument("--names", required=True, help="prefab bases (fx_hit_explode_fire) or .gamevfx.json paths")
    p.add_argument("--kinds", required=True, help=f"kinds whose references to show: {', '.join(KINDS)}")
    p.add_argument("--out", required=True)
    p.add_argument("--out-root", help="root the specs live under (default: vfxRoot)")
    args = ap.parse_args()
    {"list": cmd_list, "config": cmd_config, "install": cmd_install, "shared": cmd_shared, "make": cmd_make,
     "contact": cmd_contact, "grid": cmd_grid, "compare": cmd_compare}[args.cmd](args)


if __name__ == "__main__":
    main()
