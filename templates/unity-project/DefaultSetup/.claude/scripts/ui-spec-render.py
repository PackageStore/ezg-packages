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
            css_class = "row" if kind == "row" else "col" if kind == "col" else ""
            if kind == "grid":
                css_class = "spec-grid"
            if kind == "absolute":
                css_class = "spec-absolute"
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
            scroll = node.get("scroll")
            # A scroll container maps to a ScrollViewTemplate/ScrollLoopTemplate instance in Unity
            # (new-ui-guide.md §3d), so mark it in both the tag and the rendered box.
            scroll_attr = f' data-scroll="{html.escape(str(scroll))}"' if scroll else ""
            scroll_bar = '<span class="spec-scrollbar"></span>' if scroll else ""
            # A tabBar container IS the TabBottomTemplate instance in Unity (new-ui-guide.md
            # §3d), so paint the bar itself — otherwise the frozen PNG shows floating toggles
            # over nothing and reads as "tabs anywhere is fine".
            tabbar = node.get("tabBar") is True
            tabbar_attr = ' data-tabbar="true"' if tabbar else ""
            # A section container IS a FrameTemplateInside instance carrying a ButtonTitleTemplate
            # pill on its top edge (new-ui-guide.md §3d), so paint both — a frozen PNG that shows
            # the blocks unframed is what makes the builder ship a flat wall of text.
            section = node.get("section")
            section_attr = ""
            section_title = ""
            if isinstance(section, dict) and isinstance(section.get("title"), str):
                section_attr = ' data-section="true"'
                section_title = (
                    '<span class="spec-section-title">'
                    f'{html.escape(section["title"])}</span>'
                )
            label = (
                f"{kind}·{node_id}"
                + (f" ⇅{scroll}" if scroll else "")
                + (" ⧉TabBottomTemplate" if tabbar else "")
                + (" ⧉FrameTemplateInside+ButtonTitleTemplate" if section_attr else "")
            )
            ct_tag = f'<span class="ui-tag ui-tag-ct">{html.escape(label)}</span>'
            return (
                f'<div id="{html.escape(node_id)}" class="spec-container {css_class}" '
                f'data-layout="{html.escape(kind)}" data-positioned="{positioned}"'
                f'{scroll_attr}{tabbar_attr}{section_attr} '
                f'style="{html.escape(style)}">'
                f"{ct_tag}{scroll_bar}{section_title}{children}</div>"
            )
        node = elements[node_id]
        template = node["template"]
        content = html.escape(str(node.get("text", "")))
        el_tag = f'<span class="ui-tag ui-tag-el">{html.escape(template)}</span>'
        return (
            f'<div id="{html.escape(node_id)}" class="tpl tpl-{html.escape(template)}" '
            f'data-tpl="{html.escape(template)}" style="{html.escape(css_style(node))}">'
            f"{el_tag}{content}</div>"
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
/* Scroll region — clips like the Unity Viewport and shows the track so the frozen PNG says "this scrolls". */
.spec-container[data-scroll]{{overflow:hidden}}
.spec-container[data-scroll] > .spec-scrollbar{{position:absolute;z-index:50;background:rgba(255,255,255,.30);border-radius:5px;pointer-events:none}}
.spec-container[data-scroll="horizontal"] > .spec-scrollbar{{left:8px;right:8px;bottom:5px;height:8px}}
.spec-container[data-scroll="vertical"] > .spec-scrollbar,
.spec-container[data-scroll="loop"] > .spec-scrollbar{{top:8px;bottom:8px;right:5px;width:8px}}
/* Tab bar — the container maps to the shipped FullScreen/Bot TabBottomTemplate, so it paints
   like the real bar instead of leaving the toggles floating on the background. */
.spec-container[data-tabbar]{{background:rgba(147,122,101,1.0)}}
/* Titled info block — the container maps to a FrameTemplateInside instance whose
   ButtonTitleTemplate pill straddles the top edge (ignoreLayout, pos (0,30), size (0,60)),
   so the frame's padding-top reserves 50px for it exactly like the Unity VerticalLayoutGroup. */
.spec-container[data-section]{{background:rgba(0,0,0,.533);border-radius:22px;padding-top:50px}}
.spec-container[data-section] > .spec-section-title{{position:absolute;z-index:40;top:-30px;left:50%;
transform:translateX(-50%);height:60px;padding:0 15px;display:flex;align-items:center;
background:rgba(0,0,0,.753);border-radius:12px;color:#fff;white-space:nowrap;
font:700 40px/1 'Source Sans Pro','Segoe UI',sans-serif}}
/* Template-identity overlay — which Unity template each node maps to. Toggle in the toolbar. */
.ui-tag{{position:absolute;z-index:60;font:600 12px/1.2 ui-monospace,Menlo,Consolas,monospace;padding:2px 6px;pointer-events:none;white-space:nowrap;letter-spacing:.2px;max-width:100%;overflow:hidden;text-overflow:ellipsis}}
.ui-tag-el{{top:0;left:0;background:#1e66f5;color:#fff;border-radius:0 0 6px 0;box-shadow:0 1px 3px rgba(0,0,0,.5)}}
.ui-tag-ct{{top:0;right:0;background:rgba(10,14,28,.82);color:#8fd6ff;border:1px solid rgba(143,214,255,.5);border-top:none;border-right:none;border-radius:0 0 0 6px}}
body:not(.ui-tags) .ui-tag{{display:none}}
.ui-toolbar{{position:fixed;top:10px;right:10px;z-index:400;background:rgba(10,14,28,.92);color:#fff;font:13px/1.4 -apple-system,'Segoe UI',Roboto,sans-serif;padding:7px 11px;border-radius:9px;border:1px solid rgba(255,255,255,.16);display:flex;gap:12px;align-items:center}}
.ui-toolbar label{{display:flex;gap:6px;align-items:center;cursor:pointer;user-select:none}}
.ui-toolbar .ui-hint{{opacity:.62;font-size:11px}}
.ui-toolbar b.el{{color:#5b9bff}}.ui-toolbar b.ct{{color:#8fd6ff}}
</style>
</head>
<body class="ui-tags">
<div class="ui-toolbar">
<label><input type="checkbox" id="tglTags" checked> Template labels</label>
<span class="ui-hint"><b class="el">■</b> element template · <b class="ct">■</b> container type·id</span>
</div>
<div class="stage" data-branch="{html.escape(spec['branch'])}">
<div class="dim"></div>
{body}
</div>
<script type="application/json" id="spec">
{spec_json}
</script>
<script>
(function(){{var b=document.body,t=document.getElementById('tglTags');if(t)t.addEventListener('change',function(){{b.classList.toggle('ui-tags',t.checked);}});}})();
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
