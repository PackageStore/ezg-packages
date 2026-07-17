#!/usr/bin/env python3
"""Render a deterministic review HTML from an authoritative UI spec sidecar."""

from __future__ import annotations

import argparse
import html
import json
import re
import sys
from pathlib import Path

from ui_spec_common import (
    KIT_CSS,
    UISpecError,
    canonical_json,
    css_class,
    kit_hash,
    load_spec,
    spec_hash,
    validate_spec,
)


STYLE_MAP = {
    "background": "background",
    "color": "color",
    "border": "border",
    "borderBottom": "border-bottom",
    "alignItems": "align-items",
    "justifyContent": "justify-content",
    "borderRadius": "border-radius",
    "boxShadow": "box-shadow",
    "fontWeight": "font-weight",
    "whiteSpace": "white-space",
    "lineHeight": "line-height",
    "letterSpacing": "letter-spacing",
    "paddingLeft": "padding-left",
    "marginTop": "margin-top",
    "transform": "transform",
    "overflow": "overflow",
    "opacity": "opacity",
    "flexWrap": "flex-wrap",
    "textAlign": "text-align",
}


def px(value) -> str:
    return f"{value:g}px" if isinstance(value, float) else f"{value}px"


def css_style(node: dict) -> str:
    rules = []
    size = node.get("size")
    if isinstance(size, list) and len(size) == 2:
        rules += [f"width:{px(size[0])}", f"height:{px(size[1])}"]
    if "gap" in node:
        rules.append(f"gap:{px(node['gap'])}")
    padding = node.get("padding")
    if isinstance(padding, list) and len(padding) == 4:
        left, right, top, bottom = padding
        rules.append(f"padding:{px(top)} {px(right)} {px(bottom)} {px(left)}")
    if "fontSize" in node:
        rules.append(f"font-size:{px(node['fontSize'])}")
    if "color" in node:
        color = node["color"]
        if isinstance(color, str):
            rules.append(f"color:{color}")
        elif isinstance(color, list) and len(color) in (3, 4):
            channels = list(color) + ([1] if len(color) == 3 else [])
            if all(isinstance(v, (int, float)) and 0 <= v <= 1 for v in channels):
                r, g, b, a = channels
                rules.append(f"color:rgba({round(r*255)},{round(g*255)},{round(b*255)},{a})")
    if node.get("position") == "abs" or "pos" in node or node.get("anchor") == "stretch":
        rules.append("position:absolute")
        pos = node.get("pos", [0, 0])
        anchor = node.get("anchor", "top-left")
        transforms = []
        if anchor == "stretch":
            left, right, top, bottom = node.get("offsets", [0, 0, 0, 0])
            rules += [
                f"left:{px(left)}", f"right:{px(right)}", f"top:{px(top)}", f"bottom:{px(bottom)}",
                "width:auto", "height:auto", "min-width:0", "min-height:0",
            ]
        else:
            if "right" in anchor:
                rules.append(f"right:{px(pos[0])}")
            elif "center" in anchor or anchor == "center":
                rules.append(f"left:calc(50% + {px(pos[0])})")
                transforms.append("translateX(-50%)")
            else:
                rules.append(f"left:{px(pos[0])}")

            if "bottom" in anchor:
                rules.append(f"bottom:{px(pos[1])}")
            elif "middle" in anchor or anchor == "center":
                rules.append(f"top:calc(50% + {px(pos[1])})")
                transforms.append("translateY(-50%)")
            else:
                rules.append(f"top:{px(pos[1])}")
        if transforms:
            rules.append(f"transform:{' '.join(transforms)}")
    for key, value in node.get("style", {}).items():
        css_key = STYLE_MAP.get(key)
        if css_key and isinstance(value, (str, int, float)):
            rules.append(f"{css_key}:{value}")
    return ";".join(rules)


def alignment_style(node: dict) -> str:
    value = node.get("childAlignment")
    if not value:
        return ""
    if value == "center":
        horizontal, vertical = "center", "center"
    else:
        vertical = "start" if value.startswith("top-") else "end" if value.startswith("bottom-") else "center"
        horizontal = "start" if value.endswith("-left") else "end" if value.endswith("-right") else "center"
    kind = node.get("type")
    if kind == "row":
        return f"justify-content:{horizontal};align-items:{vertical}"
    if kind == "col":
        return f"align-items:{horizontal};justify-content:{vertical}"
    if kind == "grid":
        return f"justify-content:{horizontal};align-content:{vertical};justify-items:start;align-items:start"
    return ""


def render(spec: dict) -> str:
    kit_css = KIT_CSS.read_text(encoding="utf-8")
    containers = {row["id"]: row for row in spec["containers"]}
    elements = {row["id"]: row for row in spec["elements"]}
    referenced = {
        child
        for container in containers.values()
        for child in container.get("children", [])
    }
    roots = [node_id for node_id in containers if node_id not in referenced]
    emitted = set()

    def render_node(node_id: str) -> str:
        if node_id in emitted:
            return ""
        emitted.add(node_id)
        if node_id in containers:
            node = containers[node_id]
            kind = node.get("type", "col")
            layout_class = "row" if kind == "row" else "col" if kind == "col" else ""
            if kind == "grid":
                layout_class = "spec-grid"
            if kind == "absolute":
                layout_class = "spec-absolute"
            children = "".join(render_node(child) for child in node.get("children", []))
            if kind == "grid":
                columns = node.get("columns", 1)
                cell = node.get("cellSize", [0, 0])
                grid_rules = f"grid-template-columns:repeat({columns},{px(cell[0])});grid-auto-rows:{px(cell[1])}"
                if "spacing" in node:
                    grid_rules += f";column-gap:{px(node['spacing'][0])};row-gap:{px(node['spacing'][1])}"
            else:
                grid_rules = ""
            positioned = "true" if (
                node.get("position") == "abs" or "pos" in node or node.get("anchor") == "stretch"
            ) else "false"
            style = ";".join(part for part in (css_style(node), grid_rules, alignment_style(node)) if part)
            return (
                f'<div id="{html.escape(node_id)}" class="spec-container {layout_class}" '
                f'data-layout="{html.escape(kind)}" data-positioned="{positioned}" style="{html.escape(style)}">'
                f"{children}</div>"
            )
        node = elements[node_id]
        template = node["template"]
        content = html.escape(str(node.get("text", "")))
        return (
            f'<div id="{html.escape(node_id)}" class="tpl tpl-{html.escape(css_class(template))}" '
            f'data-tpl="{html.escape(template)}" style="{html.escape(css_style(node))}">'
            f"{content}</div>"
        )

    body = "".join(render_node(root) for root in roots)
    # Valid specs normally list every node through children. Appending orphans makes
    # an incomplete hierarchy visible in review instead of silently dropping it.
    orphans = [node_id for node_id in [*containers, *elements] if node_id not in emitted]
    if orphans:
        body += '<div class="spec-orphans">' + "".join(render_node(x) for x in orphans) + "</div>"

    title = f"{spec['screen']} — {spec['feature']}"
    spec_json = canonical_json(spec).rstrip()
    return f"""<!doctype html>
<html lang="vi">
<head>
<meta charset="utf-8">
<meta name="viewport" content="width=1080,initial-scale=1">
<meta name="ui-spec-version" content="{spec['specVersion']}">
<meta name="ui-spec-hash" content="{spec_hash(spec)}">
<meta name="ui-kit-hash" content="{kit_hash()}">
<title>{html.escape(title)}</title>
<style id="kit">
{kit_css}</style>
<style id="screen">
.spec-container{{position:relative;flex:none}}
.spec-grid{{position:relative;display:grid}}
.spec-absolute{{position:relative}}
.stage[data-branch="Popup"] > .spec-container[data-positioned="false"]{{position:absolute;left:50%;top:50%;transform:translate(-50%,-50%)}}
.stage[data-branch="FullScreen"] > .spec-container[data-positioned="false"]{{position:absolute;inset:0}}
.spec-orphans{{position:absolute;left:0;top:0;border:4px solid #f33}}
</style>
</head>
<body>
<div class="stage" data-branch="{html.escape(spec['branch'])}">
<div class="dim"></div>
{body}
</div>
<script type="application/json" id="spec">
{spec_json}
</script>
</body>
</html>
"""


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("spec", type=Path, help="authoritative .ui-spec.json")
    parser.add_argument("--output", type=Path, help="defaults to sibling .html")
    parser.add_argument("--check", action="store_true", help="fail if output is not up to date")
    args = parser.parse_args()
    try:
        spec, source, _ = load_spec(args.spec, prefer_sidecar=False)
        result = validate_spec(spec, mode="draft")
        if spec.get("specVersion") != 1:
            raise UISpecError("renderer accepts specVersion 1 only")
        if not result["ok"]:
            raise UISpecError(json.dumps(result, ensure_ascii=False))
        output = args.output or source.with_name(re.sub(r"\.ui-spec$", "", source.stem) + ".html")
        generated = render(spec)
        if args.check:
            if not output.exists() or output.read_text(encoding="utf-8") != generated:
                print(json.dumps({"ok": False, "error": "rendered HTML is stale", "output": str(output)}))
                return 1
        else:
            output.parent.mkdir(parents=True, exist_ok=True)
            output.write_text(generated, encoding="utf-8")
        print(json.dumps({
            "ok": True,
            "source": str(source),
            "output": str(output),
            "specHash": spec_hash(spec),
            "kitHash": kit_hash(),
        }, ensure_ascii=False, indent=2))
        return 0
    except (OSError, UISpecError) as exc:
        print(json.dumps({"ok": False, "error": str(exc)}, ensure_ascii=False, indent=2))
        return 1


if __name__ == "__main__":
    sys.exit(main())
