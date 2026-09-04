"""
visual_diff.py -- Region-based visual comparison of Figma renders vs PSD composites.

Produces per-region mean/max channel deltas plus diff PNGs.
"""
import argparse, json, sys, os
from pathlib import Path
import numpy as np
from PIL import Image

from pipeline_config import resolve

# ---------------------------------------------------------------------------
# Region definitions live in the data dir (see cfg tables.diffRegions), shaped
# { "<screen>": { "<label>": [x, y, w, h], ... }, ... }  --  frame-relative px.
# ---------------------------------------------------------------------------

# ---------------------------------------------------------------------------
# Helpers
# ---------------------------------------------------------------------------

def load_pair(screen: str, diff_dir: Path) -> tuple[np.ndarray, np.ndarray]:
    """Return (figma, psd) as float32 arrays in [0,1], shape (H,W,3)."""
    figma = Image.open(diff_dir / f"figma_{screen}.png").convert("RGB")
    psd   = Image.open(diff_dir / f"psd_{screen}.png").convert("RGB")
    assert figma.size == psd.size, f"Size mismatch: {figma.size} vs {psd.size}"
    return (
        np.asarray(figma, dtype=np.float32) / 255.0,
        np.asarray(psd,   dtype=np.float32) / 255.0,
    )


def region_stats(figma: np.ndarray, psd: np.ndarray,
                 x: int, y: int, w: int, h: int) -> dict:
    """Compute mean/max absolute channel delta over a region."""
    f_crop = figma[y:y+h, x:x+w]
    p_crop = psd[y:y+h, x:x+w]
    delta  = np.abs(f_crop - p_crop)
    return {
        "mean": round(float(np.mean(delta) * 255), 2),
        "max":  round(float(np.max(delta)  * 255), 1),
    }


def make_diff_png(figma: np.ndarray, psd: np.ndarray, path: Path) -> None:
    """Save a grayscale diff image where brightness = per-pixel delta."""
    delta = np.abs(figma - psd)
    gray  = np.mean(delta, axis=2)
    gray  = np.clip(gray * 10, 0, 1)  # 10x amplification
    img   = Image.fromarray((gray * 255).astype(np.uint8), mode="L")
    img.save(path)


# ---------------------------------------------------------------------------
# Main
# ---------------------------------------------------------------------------

def run(cfg) -> dict:
    diff_dir = cfg.path("diff")
    screen_regions = cfg.load("diffRegions")
    frame = cfg.settings["frame"]
    results = {}
    for screen, regions in screen_regions.items():
        f, p = load_pair(screen, diff_dir)
        report = {}
        for name, (x, y, w, h) in regions.items():
            report[name] = region_stats(f, p, x, y, w, h)
        report["_overall"] = region_stats(f, p, 0, 0, frame["w"], frame["h"])
        make_diff_png(f, p, diff_dir / f"diff_{screen}.png")
        results[screen] = report
    return results


def known_screen_keys(cfg, screen_regions) -> list:
    screens = cfg.load_optional("screens")
    if isinstance(screens, dict):
        return list(screens.keys())
    if isinstance(screens, list):
        return list(screens)
    return list(screen_regions.keys())


def screen_report(cfg, screen, regions, diff_dir, frame) -> dict:
    if not regions:
        return {"screen": screen, "regions": []}
    f, p = load_pair(screen, diff_dir)
    out = []
    for name, (x, y, w, h) in regions.items():
        out.append({"screen": screen, "region": name,
                    **region_stats(f, p, x, y, w, h)})
    out.append({"screen": screen, "region": "_overall",
                **region_stats(f, p, 0, 0, frame["w"], frame["h"])})
    make_diff_png(f, p, diff_dir / f"diff_{screen}.png")
    return {"screen": screen, "regions": out}


def run_screens(cfg, requested) -> list:
    diff_dir = cfg.path("diff")
    screen_regions = cfg.load("diffRegions")
    frame = cfg.settings["frame"]
    known = known_screen_keys(cfg, screen_regions)
    unknown = [k for k in requested if k not in known]
    if unknown:
        print(f"unknown screen key(s): {', '.join(unknown)}", file=sys.stderr)
        print(f"known keys: {', '.join(known)}", file=sys.stderr)
        sys.exit(2)
    return [screen_report(cfg, k, screen_regions.get(k, {}), diff_dir, frame)
            for k in requested]


if __name__ == "__main__":
    cfg, argv = resolve()
    parser = argparse.ArgumentParser(add_help=False)
    parser.add_argument("--screen", action="append")
    args = parser.parse_args(argv)
    MANIFEST = cfg.load("psd_manifest.json")
    if args.screen:
        print("\n".join(json.dumps(o) for o in run_screens(cfg, args.screen)))
    else:
        results = run(cfg)
        print(json.dumps(results, indent=2))
