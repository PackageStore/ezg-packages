#!/usr/bin/env python3
"""Extract the baked road tiles from a level prefab as a normalized CSV.

Usage:
    prefab_tiles.py <level_N.prefab> [--parent RoadParent] [--out tiles.csv]

Output lines: `layer,prefabName,localX,localZ,yaw`  — the same shape solver_dump.cs emits, so
diff_tiles.py can compare the two directly.

Extracts tiles from all sub-groups under the `--parent` node (default `RoadParent`):
  Roads    -> layer "Road"
  Road2    -> layer "Road2"
  Path     -> layer "Path"
  Highways -> layer "Highway"

Tiles may be either native GameObjects (baked via SaveAsPrefabAsset — name = `{prefab}_{cx}_{cy}`,
position+rotation on their Transform) or PrefabInstances (source GUID → prefab basename, overrides
for position/rotation). The script handles both.

ASSUMPTION: road part prefabs have an identity base rotation, so the serialized quaternion is a
pure Y rotation and yaw = 2*atan2(qy, qw). Apply() writes `Euler(0,yaw,0) * prefab.localRotation`,
so a part whose source prefab is tilted (e.g. a Blender import with X=90) would break that — the
script flags any such instance on stderr instead of emitting a wrong yaw.
"""
import argparse
import math
import os
import re
import subprocess
import sys

REPO = subprocess.run(["git", "rev-parse", "--show-toplevel"], capture_output=True, text=True,
                      cwd=os.path.dirname(os.path.abspath(__file__))).stdout.strip()

GROUP_TO_LAYER = {
    "Roads": "Road",
    "Road2": "Road2",
    "Path": "Path",
    "Highways": "Highway",
}


def guid_index():
    """guid -> prefab basename, from one pass over every *.prefab.meta in Assets."""
    r = subprocess.run(
        ["grep", "-r", "--include=*.prefab.meta", "-m1", "-H", "^guid:", os.path.join(REPO, "Assets")],
        capture_output=True, text=True)
    idx = {}
    for line in r.stdout.splitlines():
        path, _, rest = line.partition(":guid:")
        if not rest:
            continue
        idx[rest.strip()] = os.path.basename(path)[: -len(".prefab.meta")]
    return idx


def parse_blocks(txt):
    blocks = re.split(r"^--- !u!(\d+) &(\d+)[^\n]*$", txt, flags=re.M)
    return [(blocks[k], blocks[k + 1], blocks[k + 2]) for k in range(1, len(blocks), 3)]


def find_transform_of_go(parsed, go_name):
    """Return fileID of the Transform of a GameObject named `go_name`."""
    for cls, oid, body in parsed:
        if cls != "1" or not re.search(rf"^\s*m_Name:\s*{re.escape(go_name)}\s*$", body, re.M):
            continue
        comp = re.search(r"m_Component:\s*\n\s*- component: \{fileID: (\d+)\}", body)
        if comp:
            return comp.group(1)
    return None


def get_transform_children(parsed, parent_tid):
    """Return list of child Transform fileIDs from the parent Transform's m_Children."""
    for cls, oid, body in parsed:
        if cls != "4" or oid != parent_tid:
            continue
        cm = re.search(r"m_Children:(.*?)(?=\n\s*m_)", body, re.DOTALL)
        if not cm:
            return []
        return re.findall(r"fileID:\s*(\d+)", cm.group(1))
    return []


def get_child_groups(parsed, parent_tid):
    """For each Transform child of parent_tid, find its GO name. Return {name: child_tid}."""
    child_tids = get_transform_children(parsed, parent_tid)
    groups = {}
    for child_tid in child_tids:
        for cls, oid, body in parsed:
            if cls != "4" or oid != child_tid:
                continue
            go_match = re.search(r"m_GameObject:\s*\{fileID:\s*(\d+)\}", body)
            if not go_match:
                break
            go_id = go_match.group(1)
            for cls2, oid2, body2 in parsed:
                if oid2 == go_id and cls2 == "1":
                    name_match = re.search(r"m_Name:\s*(.+)", body2)
                    if name_match:
                        groups[name_match.group(1).strip()] = child_tid
                    break
            break
    return groups


def strip_coords_suffix(name):
    """Remove the trailing `_cx_cy` coordinate suffix from baked tile names.
    E.g. 'Road_1x1_side_12_11.75' -> 'Road_1x1_side'."""
    # Walk backwards, stripping numeric suffixes separated by underscores
    # The format is: prefabName_canvasX_canvasY
    parts = name.rsplit("_", 2)
    if len(parts) == 3:
        try:
            float(parts[1])
            float(parts[2])
            return parts[0]
        except ValueError:
            pass
    # Try stripping just one (in case name already has underscores matching the pattern)
    parts = name.rsplit("_", 1)
    if len(parts) == 2:
        try:
            float(parts[1])
            return parts[0]
        except ValueError:
            pass
    return name


def extract_native_tiles(parsed, group_tid, layer, guid_idx):
    """Extract tiles as native GameObjects under a group Transform."""
    rows = []
    skipped = 0
    child_tids = get_transform_children(parsed, group_tid)
    for child_tid in child_tids:
        for cls, oid, body in parsed:
            if cls != "4" or oid != child_tid:
                continue
            go_match = re.search(r"m_GameObject:\s*\{fileID:\s*(\d+)\}", body)
            pos_match = re.search(
                r"m_LocalPosition:\s*\{x:\s*([^,]+),\s*y:\s*([^,]+),\s*z:\s*([^}]+)\}", body)
            rot_match = re.search(
                r"m_LocalRotation:\s*\{x:\s*([^,]+),\s*y:\s*([^,]+),\s*z:\s*([^,]+),\s*w:\s*([^}]+)\}",
                body)
            if not go_match or not pos_match or not rot_match:
                break

            go_id = go_match.group(1)
            name = None
            for cls2, oid2, body2 in parsed:
                if oid2 == go_id and cls2 == "1":
                    nm = re.search(r"m_Name:\s*(.+)", body2)
                    if nm:
                        name = nm.group(1).strip()
                    break
            if not name:
                break

            prefab_name = strip_coords_suffix(name)
            x = float(pos_match.group(1))
            z = float(pos_match.group(3))
            qx = float(rot_match.group(1))
            qy = float(rot_match.group(2))
            qz = float(rot_match.group(3))
            qw = float(rot_match.group(4))

            if abs(qx) > 1e-3 or abs(qz) > 1e-3:
                skipped += 1
                print(f"warn: non-Y rotation on {prefab_name} "
                      f"q=({qx},{qy},{qz},{qw}) — yaw unreliable, row emitted as q(...)",
                      file=sys.stderr)
                yaw = f"q({qx},{qy},{qz},{qw})"
            else:
                yaw = round(math.degrees(2 * math.atan2(qy, qw))) % 360

            rows.append((layer, prefab_name, x, z, yaw))
            break
    return rows, skipped


def extract_prefab_instances(txt, parent_tid, layer, guid_idx):
    """Extract tiles as PrefabInstances parented to a specific Transform."""
    rows = []
    skipped = 0
    for block in re.split(r"^--- !u!1001 &\d+\s*$", txt, flags=re.M)[1:]:
        block = re.split(r"^--- !u!", block, flags=re.M)[0]
        parent = re.search(r"m_TransformParent: \{fileID: (\d+)\}", block)
        if parent_tid is not None and (not parent or parent.group(1) != parent_tid):
            continue
        src = re.search(r"m_SourcePrefab: \{fileID: 100100000, guid: ([0-9a-f]+)", block)
        if not src:
            continue
        vals = {m.group(1): float(m.group(2)) for m in re.finditer(
            r"propertyPath: (m_Local(?:Position|Rotation)\.[xyzw])\n\s+value: (\S+)", block)}
        qx, qy = vals.get("m_LocalRotation.x", 0.0), vals.get("m_LocalRotation.y", 0.0)
        qz, qw = vals.get("m_LocalRotation.z", 0.0), vals.get("m_LocalRotation.w", 1.0)
        if abs(qx) > 1e-3 or abs(qz) > 1e-3:
            skipped += 1
            print(f"warn: non-Y rotation on {guid_idx.get(src.group(1), src.group(1))} "
                  f"q=({qx},{qy},{qz},{qw}) — yaw unreliable, row emitted as q(...)", file=sys.stderr)
            yaw = f"q({qx},{qy},{qz},{qw})"
        else:
            yaw = round(math.degrees(2 * math.atan2(qy, qw))) % 360
        rows.append((layer, guid_idx.get(src.group(1), src.group(1)),
                     vals.get("m_LocalPosition.x", 0.0), vals.get("m_LocalPosition.z", 0.0), yaw))
    return rows, skipped


def parse(path, parent_name):
    txt = open(path, encoding="utf-8").read()
    parsed = parse_blocks(txt)
    guid_idx = guid_index()

    parent_tid = find_transform_of_go(parsed, parent_name)
    if parent_tid is None:
        print(f"warn: no GameObject named '{parent_name}' — emitting ALL native tiles as 'Road'",
              file=sys.stderr)
        # Fallback: emit all PrefabInstances
        rows, skipped = extract_prefab_instances(txt, None, "Road", guid_idx)
        return rows, skipped

    child_groups = get_child_groups(parsed, parent_tid)
    known_groups = {name: tid for name, tid in child_groups.items() if name in GROUP_TO_LAYER}

    all_rows = []
    total_skipped = 0

    if known_groups:
        for group_name, child_tid in known_groups.items():
            layer = GROUP_TO_LAYER[group_name]
            # Try native GOs first (the common case after SaveAsPrefabAsset bake)
            rows, skipped = extract_native_tiles(parsed, child_tid, layer, guid_idx)
            if rows:
                all_rows.extend(rows)
                total_skipped += skipped
            else:
                # Fall back to PrefabInstances
                rows, skipped = extract_prefab_instances(txt, child_tid, layer, guid_idx)
                all_rows.extend(rows)
                total_skipped += skipped
    else:
        # No recognized sub-groups — try native tiles directly under parent
        rows, skipped = extract_native_tiles(parsed, parent_tid, "Road", guid_idx)
        if rows:
            all_rows.extend(rows)
            total_skipped += skipped
        else:
            rows, skipped = extract_prefab_instances(txt, parent_tid, "Road", guid_idx)
            all_rows.extend(rows)
            total_skipped += skipped

    return all_rows, total_skipped


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("prefab")
    ap.add_argument("--parent", default="RoadParent",
                    help="parent node name (default: RoadParent)")
    ap.add_argument("--out")
    a = ap.parse_args()

    rows, skipped = parse(a.prefab, a.parent)
    text = "".join(f"{layer},{n},{x:g},{z:g},{y}\n" for layer, n, x, z, y in rows)
    if a.out:
        open(a.out, "w").write(text)
        layer_counts = {}
        for layer, *_ in rows:
            layer_counts[layer] = layer_counts.get(layer, 0) + 1
        detail = ", ".join(f"{k}={v}" for k, v in sorted(layer_counts.items()))
        print(f"{len(rows)} tiles -> {a.out}  ({detail})"
              + (f"  ({skipped} with non-Y rotation)" if skipped else ""))
    else:
        sys.stdout.write(text)


if __name__ == "__main__":
    sys.exit(main())
