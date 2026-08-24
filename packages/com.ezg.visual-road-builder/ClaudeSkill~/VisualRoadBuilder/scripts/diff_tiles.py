#!/usr/bin/env python3
"""Compare two tile CSVs as multisets and report the exact delta.

Usage:
    diff_tiles.py <expected.csv> <actual.csv>

`expected` = the hand-fixed prefab (ground truth, via prefab_tiles.py).
`actual`   = what the tool's solver produces (via solver_dump.cs).

Supports both the old 4-column format (`name,x,z,yaw`) and the new 5-column format
(`layer,name,x,z,yaw`). Old-format rows are assigned layer "Road".

MISSING = in the prefab but the tool does not build it.
EXTRA   = the tool builds it but the prefab does not have it.
Exit code 0 only when both are empty — that is the contract for "the tool now matches the demo".
"""
import argparse
import sys
from collections import Counter


def load(path):
    c = Counter()
    for line in open(path, encoding="utf-8"):
        line = line.strip()
        if not line:
            continue
        parts = line.split(",")
        if len(parts) == 5:
            layer, name, x, z, yaw = parts
        elif len(parts) == 4:
            layer = "Road"
            name, x, z, yaw = parts
        else:
            continue
        try:
            yaw = int(yaw) % 360
        except ValueError:
            pass  # non-Y rotation marker from prefab_tiles.py — compare verbatim
        c[(layer, name, round(float(x), 3), round(float(z), 3), yaw)] += 1
    return c


def show(label, c):
    print(f"\n### {label}: {sum(c.values())}")
    for (layer, name, x, z, yaw), n in sorted(c.items(), key=lambda kv: (kv[0][0], kv[0][3], kv[0][2], kv[0][1])):
        print(f"   {n}x [{layer}] {name:<22} local=({x:g}, {z:g}) yaw={yaw}")


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("expected")
    ap.add_argument("actual")
    a = ap.parse_args()

    want, got = load(a.expected), load(a.actual)
    missing, extra = want - got, got - want
    print(f"expected={sum(want.values())}  actual={sum(got.values())}")
    show("MISSING (in prefab, tool does not build)", missing)
    show("EXTRA (tool builds, not in prefab)", extra)

    if not missing and not extra:
        print("\nMATCH — solver output is identical to the demonstrated prefab.")
        return 0
    print(f"\nMISMATCH — {sum(missing.values())} missing, {sum(extra.values())} extra.")
    return 1


if __name__ == "__main__":
    sys.exit(main())
