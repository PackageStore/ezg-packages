#!/usr/bin/env python3
"""Migrate underscore node/style/component names to hyphen convention.

Dry run prints per-file counts; --apply rewrites the five data files and
writes name_map.json.  --selftest runs unit checks and exits.
"""

import argparse
import json
import re
import sys

from pipeline_config import resolve


def migrate_name(name):
    if re.match(r"slice_\d+_\d+$", name):
        return name
    result = re.sub(r"(?<=[A-Za-z0-9])_(?=[A-Za-z0-9])", "-", name)
    parts = result.split("-")
    return "-".join(
        p[0].upper() + p[1:] if p and p[0].isalpha() else p for p in parts
    )


def build_map(names):
    mapping = {}
    reverse = {}
    collisions = []
    for name in sorted(names):
        new = migrate_name(name)
        if name == new:
            continue
        if new in reverse and reverse[new] != name:
            collisions.append((name, reverse[new], new))
        mapping[name] = new
        reverse[new] = name
    return mapping, collisions


def _selftest():
    for inp, exp in [("Bg_Demo", "Bg-Demo"), ("Card_Bg_Normal", "Card-Bg-Normal"),
                     ("Text_CardTitle_2", "Text-CardTitle-2"), ("slice_0_0", "slice_0_0"),
                     ("slice_2_1", "slice_2_1"), ("F", "F"), ("500", "500"),
                     ("Bg_btn", "Bg-Btn"), ("Title_Popup", "Title-Popup"),
                     ("NetInfo_F", "NetInfo-F"), ("ResourceBar_Currency", "ResourceBar-Currency"),
                     ("", ""), ("NoBorder", "NoBorder")]:
        assert migrate_name(inp) == exp, f"{inp!r} => {migrate_name(inp)!r}, expected {exp!r}"
    m, c = build_map(["Bg_Demo", "Text_Title", "F", "slice_0_0"])
    assert m == {"Bg_Demo": "Bg-Demo", "Text_Title": "Text-Title"} and c == []
    print("names_migrate self-test OK")


def main():
    if "--selftest" in sys.argv[1:]:
        _selftest()
        return

    cfg, argv = resolve()
    parser = argparse.ArgumentParser(prog="names_migrate")
    parser.add_argument("--apply", action="store_true")
    args = parser.parse_args(argv)

    node_names = cfg.load("node_names.json")
    text_styles = cfg.load("text_styles.json")
    component_ids = cfg.load("component_ids.json")
    style_ids = cfg.load("style_ids.json")
    debt = cfg.load("accepted_debt.json")

    all_nodes = set(node_names.get("nodeNames", {}).values())
    for k in text_styles.get("layerMap", {}):
        all_nodes.add(k.split("/", 1)[1] if "/" in k else k)
    for exc in debt.get("verification_exceptions", []):
        if exc.get("node"):
            all_nodes.add(exc["node"])
    all_styles = (set(text_styles.get("styles", {})) | set(text_styles.get("layerMap", {}).values())
                  | set(style_ids.get("textStyles", {})) | set(node_names.get("textStyles", {}).values()))
    node_map, nc = build_map(all_nodes)
    style_map, sc = build_map(all_styles)
    comp_map, cc = build_map(set(component_ids))
    print(f"nodes: {len(node_map)}  styles: {len(style_map)}  components: {len(comp_map)}")
    for label, coll in [("nodes", nc), ("styles", sc), ("components", cc)]:
        for a, b, new in coll:
            print(f"COLLISION {label}: {a!r} and {b!r} both -> {new!r}", file=sys.stderr)

    if not args.apply:
        print("\ndry run; pass --apply to rewrite files")
        return

    nn = node_names.get("nodeNames", {})
    for k in nn:
        nn[k] = node_map.get(nn[k], nn[k])

    ts_map = node_names.get("textStyles", {})
    for k in ts_map:
        ts_map[k] = style_map.get(ts_map[k], ts_map[k])

    old_styles = text_styles.get("styles", {})
    text_styles["styles"] = {
        style_map.get(k, k): v for k, v in old_styles.items()
    }

    def _lm_key(k):
        if "/" in k:
            s, n = k.split("/", 1)
            return f"{s}/{node_map.get(n, n)}"
        return node_map.get(k, k)

    text_styles["layerMap"] = {
        _lm_key(k): style_map.get(v, v) for k, v in text_styles.get("layerMap", {}).items()
    }
    new_comp = {comp_map.get(k, k): v for k, v in component_ids.items()}
    style_ids["textStyles"] = {
        style_map.get(k, k): v for k, v in style_ids.get("textStyles", {}).items()
    }
    for exc in debt.get("verification_exceptions", []):
        exc["node"] = node_map.get(exc.get("node", ""), exc.get("node", ""))

    out = {"node_names.json": node_names, "text_styles.json": text_styles,
           "component_ids.json": new_comp, "style_ids.json": style_ids,
           "accepted_debt.json": debt}
    for name, data in out.items():
        cfg.path(name).write_text(json.dumps(data, indent=2, ensure_ascii=False) + "\n")
    nm = {"nodes": dict(sorted(node_map.items())), "styles": dict(sorted(style_map.items())),
          "components": dict(sorted(comp_map.items()))}
    cfg.path("name_map.json").write_text(json.dumps(nm, indent=2, ensure_ascii=False) + "\n")
    print(f"\nwrote {len(out)} files + name_map.json")


if __name__ == "__main__":
    main()
