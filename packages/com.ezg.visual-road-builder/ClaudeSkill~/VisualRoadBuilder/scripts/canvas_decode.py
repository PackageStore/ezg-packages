#!/usr/bin/env python3
"""Decode a RoadCanvasSave `.asset` and reproduce the tool's lattice classification offline.

Usage:
    canvas_decode.py <level_N_RoadCanvas.asset> [--layer road|road2|path|highway|hwdecor] [--grid]

Prints every lattice node that carries a mask, how the solver classifies it
(straight / junction), and which junction pairs sit 0.5 cell apart. Read-only.

This mirrors VisualRoadBuilderTool.EdgeModel.BuildMasks + MaskUtil.IsStraightLikeMask.
Keep it in sync when that C# changes — it exists so you can reason about a layout
without a running Editor, not as a second source of truth.

Edge model: every edge spans 1 lattice step (half a cell). `BuildMasks` credits both
endpoints — hops of +-1, not +-2. `AddMidpointJunctions` is gone (deleted when edges
became half-cell, SKILL.md §5).
"""
import argparse
import re
import struct
import sys

DIR_E, DIR_N, DIR_W, DIR_S = 1, 2, 4, 8
DIR_NAME = [(DIR_E, "E"), (DIR_N, "N"), (DIR_W, "W"), (DIR_S, "S")]


def mask_str(m):
    return "".join(ch for bit, ch in DIR_NAME if m & bit) or "-"


def count_bits(m):
    return bin(m).count("1")


def is_straight_like(m):
    d = count_bits(m)
    return d == 1 or m == (DIR_E | DIR_W) or m == (DIR_N | DIR_S)


def is_junction(m):
    return m != 0 and not is_straight_like(m)


def parse_asset(path):
    txt = open(path, encoding="utf-8").read()

    def scalar(key, cast=int, default=0):
        # [ \t]* not \s* — \s swallows newlines and would grab the NEXT key's value.
        m = re.search(rf"^[ \t]*{key}:[ \t]*(\S+)[ \t]*$", txt, re.M)
        return cast(m.group(1)) if m else default

    def hexlist(key):
        m = re.search(rf"^[ \t]*{key}:[ \t]*(\S*)[ \t]*$", txt, re.M)
        if not m or not m.group(1):
            return []
        raw = bytes.fromhex(m.group(1))
        return list(struct.unpack("<%di" % (len(raw) // 4), raw))

    oc = re.search(r"originCell:\s*\{x:\s*(-?\d+),\s*y:\s*(-?\d+)\}", txt)
    esv = scalar("edgeSpanVersion", default=0)
    return {
        "width": scalar("width", default=50),
        "height": scalar("height", default=50),
        "edgeSpanVersion": esv,
        "originCell": (int(oc.group(1)), int(oc.group(2))) if oc else (0, 0),
        "road": hexlist("edges"),
        "road2": hexlist("road2Edges"),
        "path": hexlist("pathEdges"),
        "highway": hexlist("highwayEdges"),
        "hwdecor": hexlist("hwDecorEdges"),
        "stations": hexlist("stations"),
        "parkings": hexlist("parkings"),
    }


def decode_edge(eid):
    return (eid >> 1) & 0xFFF, eid >> 13, eid & 1  # x2, y2, orient


def split_edge_span(edges):
    """Convert span-2 edges (edgeSpanVersion 0) into pairs of span-1 edges."""
    result = []
    for eid in edges:
        x2, y2, orient = decode_edge(eid)
        result.append((y2 << 13) | (x2 << 1) | orient)
        nx2 = x2 + 1 if orient == 0 else x2
        ny2 = y2 + 1 if orient == 1 else y2
        result.append((ny2 << 13) | (nx2 << 1) | orient)
    return result


def build_masks(edges, lw, lh):
    """Mirror of BuildMasks — each edge spans 1 lattice step (half a cell)."""
    masks = [0] * (lw * lh)
    for x2, y2, o in edges:
        if o == 0:
            if x2 + 1 >= lw or y2 >= lh:
                continue
            masks[y2 * lw + x2] |= DIR_E
            masks[y2 * lw + x2 + 1] |= DIR_W
        else:
            if x2 >= lw or y2 + 1 >= lh:
                continue
            masks[y2 * lw + x2] |= DIR_N
            masks[(y2 + 1) * lw + x2] |= DIR_S
    return masks


def half_cell_pairs(masks, lw, lh):
    """Junction nodes with another junction exactly 1 lattice step away in an OPEN dir."""
    out = []
    for y in range(lh):
        for x in range(lw):
            m = masks[y * lw + x]
            if not is_junction(m):
                continue
            for bit, dx, dy in ((DIR_E, 1, 0), (DIR_W, -1, 0), (DIR_N, 0, 1), (DIR_S, 0, -1)):
                nx, ny = x + dx, y + dy
                if not (m & bit) or not (0 <= nx < lw and 0 <= ny < lh):
                    continue
                if is_junction(masks[ny * lw + nx]):
                    out.append(((x, y), (nx, ny), mask_str(bit)))
    return out


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("asset")
    ap.add_argument("--layer", default="road",
                    choices=("road", "road2", "path", "highway", "hwdecor"))
    ap.add_argument("--grid", action="store_true", help="also print an ASCII lattice map")
    a = ap.parse_args()

    d = parse_asset(a.asset)
    lw, lh = (d["width"] - 1) * 2 + 1, (d["height"] - 1) * 2 + 1
    ox, oy = d["originCell"]
    esv = d["edgeSpanVersion"]

    raw_edges = d[a.layer]
    # edgeSpanVersion 0: edges span 2 lattice steps — split to span-1 before decoding.
    # edgeSpanVersion 1: edges already span 1 step. Path edges are always span-1.
    need_split = esv < 1 and a.layer not in ("path",)
    if need_split:
        raw_edges = split_edge_span(raw_edges)

    edges = [decode_edge(e) for e in raw_edges]

    print(f"grid {d['width']}x{d['height']}  lattice {lw}x{lh}  originCell=({ox},{oy})")
    print(f"edgeSpanVersion={esv}{'  (split to span-1)' if need_split else ''}")
    print(f"local = canvas + originCell  ->  local_x = canvas_x{ox:+d}, local_z = canvas_y{oy:+d}")
    print(f"layer '{a.layer}': {len(edges)} edges (after split)" if need_split
          else f"layer '{a.layer}': {len(edges)} edges")

    # Print counts of other layers for context
    layers_summary = []
    for lname in ("road", "road2", "path", "highway", "hwdecor"):
        n = len(d[lname])
        if n > 0 and lname != a.layer:
            layers_summary.append(f"{lname}={n}")
    if d["stations"]:
        layers_summary.append(f"stations={len(d['stations'])}")
    if d["parkings"]:
        layers_summary.append(f"parkings={len(d['parkings'])}")
    if layers_summary:
        print(f"other layers: {', '.join(layers_summary)}")
    print()

    print("--- edges (half-cell x2/y2, orient 0=E/W 1=N/S) ---")
    for x2, y2, o in sorted(edges, key=lambda t: (t[2], t[1], t[0])):
        span = f"({x2},{y2})->({x2+1},{y2})" if o == 0 else f"({x2},{y2})->({x2},{y2+1})"
        print(f"  x2={x2:<3} y2={y2:<3} orient={o}  canvas {span}  = cells "
              f"({x2/2}, {y2/2})->({(x2+1)/2 if o==0 else x2/2}, {y2/2 if o==0 else (y2+1)/2})")

    masks = build_masks(edges, lw, lh)

    print("\n--- nodes ---")
    for i, m in enumerate(masks):
        if not m:
            continue
        x, y = i % lw, i // lw
        kind = "JUNCTION" if is_junction(m) else "straight"
        print(f"  x2={x:<3} y2={y:<3} canvas=({x/2}, {y/2})  local=({x/2+ox}, {y/2+oy})  "
              f"mask={mask_str(m):<4} {kind}")

    pairs = half_cell_pairs(masks, lw, lh)
    print(f"\n--- junction pairs 0.5 cell apart: {len(pairs)} (each must yield that arm) ---")
    for (ax, ay), (bx, by), dirn in pairs:
        print(f"  ({ax/2}, {ay/2}) --{dirn}--> ({bx/2}, {by/2})")

    if a.grid:
        print("\n--- lattice (J=junction, s=straight, .=empty) ---")
        for y in range(lh - 1, -1, -1):
            row = "".join(
                "J" if is_junction(masks[y * lw + x]) else "s" if masks[y * lw + x] else "."
                for x in range(lw))
            print(f"  y2={y:<3} {row}")


if __name__ == "__main__":
    sys.exit(main())
