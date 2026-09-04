#!/usr/bin/env python3
"""
Verify Figma frames against PSD manifest — the numeric gate.

Art nodes: layout box, 0.00 px tolerance.
Text nodes: ink box (absoluteRenderBounds, stroke+shadow adjusted), 2.00 px tolerance.

Exceptions: accepted_debt.json entries that pin known deviations at their
measured values, with a 0.5px drift guard.  An exception is a pin, not an
amnesty — if a node's delta drifts from the recorded value, the gate fails.

Flags (parsed from the argv pipeline_config.resolve leaves behind):
  --screen KEY   restrict the gate to KEY (repeatable); an unknown key exits 2
                 listing the known keys. Manifest layers, summary lines, the
                 Failures block and the exit code cover only the named screens.
  --json         also write verify_report.json beside verify_report.md; the
                 markdown is unchanged. Shape:
                   {
                     "screens": {
                       "<key>": {
                         "art_max": float, "text_max": float,
                         "unmapped": int,
                         "font_violations": int, "style_violations": int,
                         "rows": [
                           {"node": str, "role": "art"|"text",
                            "status": "PASS"|"FAIL"|"EXC_PASS"|"EXC_FAIL"|"UNMAPPED",
                            "dx": float, "dy": float, "dw": float, "dh": float,
                            "pin": str|null}
                         ]
                       }
                     },
                     "missing": ["<key>", ...],
                     "exit": int
                   }
                 art_max/text_max come from non-exception rows only; pin is the
                 exception reason for EXC_* rows, null otherwise.

With no flag the report is byte-identical to before these flags existed, except
text rows whose shared style declares a drop shadow the PSD layer disables (the
manifest recipe below no longer subtracts it). An extract file absent for a screen is now tolerated (the key goes to "missing",
its manifest rows are skipped, exit becomes 1) instead of raising.

Text recipe: a matched text layer's expected style and effects come from the
manifest's per-layer type.style (resolve_recipe), not the global layerMap.
layerMap survives as an override — keyed screen/node, then node name for Unknown
or absent styles (today's behaviour). A drop shadow the shared style declares
but neither the rendered node nor the PSD layer carries (type.effects) is not
subtracted. The recipe source per row (manifest/override/ignored/missing/legacy)
is summarised on stdout ("recipe source: ..."); RECIPE_OVERRIDE_IGNORED,
RECIPE_MISSING and PIN_UNUSED (a pin whose row now clears the bar) are printed to
stderr and, under --json, added to verify_report.json as top-level "recipe"
counts and "recipeIgnored"/"recipeMissing"/"pinUnused" arrays. verify_report.md
carries none of these — the summary line there would break D-3.

--selftest runs resolve_recipe's unit checks and exits without reading data.
"""

import argparse
import json
import sys

from pipeline_config import resolve

# Tolerances are the gate's contract and stay in code, never JSON: making them
# configurable would let a caller widen a tolerance, which this project forbids.
ART_TOL = 0.0
TEXT_TOL = 2.0
DRIFT_TOL = 0.5


def load(path):
    return json.loads(path.read_text())


def derive_extract_keys(cfg, manifest_screen_order):
    """Screen keys whose figma_extract_<key>.json this gate reads.

    Primary source: screens.json (tables.screens), key order preserved.
    Fallback (screens.json absent): the figma_extract_<key>.json files present
    in the data dir, ordered by the manifest's screen list. The extract-config
    sidecar figma_extract_config.json is not a screen and is excluded.
    """
    screens = cfg.load_optional("screens")
    if screens is not None:
        return list(screens.keys()) if isinstance(screens, dict) else list(screens)
    present = set()
    for p in cfg.data_dir.glob("figma_extract_*.json"):
        key = p.name[len("figma_extract_"):-len(".json")]
        if key != "config":
            present.add(key)
    ordered = [k for k in manifest_screen_order if k in present]
    ordered += [k for k in sorted(present) if k not in ordered]
    return ordered


def match_node(mx, my, mw, mh, role, nodes, used):
    best, best_d = None, 1e9
    for n in nodes:
        if n["id"] in used:
            continue
        d = abs(n["x"] - mx) + abs(n["y"] - my) + abs(n["w"] - mw) + abs(n["h"] - mh)
        if d < best_d:
            best_d = d
            best = n
        if role == "text" and "inkX" in n:
            d2 = abs(n["inkX"] - mx) + abs(n["inkY"] - my)
            if d2 < best_d:
                best_d = d2
                best = n
    return best if best and best_d < 200 else None


DROP_SHADOW_PSD = ("DropShadow", "OuterGlow")  # psd-tools effect classes -> Figma DROP_SHADOW


def _visible_drop_shadow(effects):
    return any(e.get("type") == "DROP_SHADOW" and e.get("visible", True) for e in effects)


def resolve_recipe(fn, layer, text_styles):
    """Expected text recipe for a matched layer: (style, effects, source).

    Style comes from the manifest's per-layer type.style; layerMap is an override
    keyed screen/node first, then a node-name fallback used verbatim for Unknown
    or absent styles (today's behaviour). effects is the style's effect list with
    a phantom drop shadow removed — one the shared style declares but neither the
    rendered node nor the PSD layer carries. The node effects are authoritative
    for render bounds: a shadow the manifest under-reports but the node renders is
    kept, so the manifest's type.effects only removes, never adds, a subtraction.
    source is manifest|override|ignored|missing|legacy.
    """
    styles = text_styles.get("styles", {})
    layer_map = text_styles.get("layerMap", {})
    node = layer.get("node")
    manifest_style = layer.get("type", {}).get("style")

    override = layer_map.get(f"{layer.get('screen')}/{node}")
    if override is not None:
        style, source = styles.get(override, {}), "override"
    elif manifest_style in (None, "", "Unknown"):
        style, source = styles.get(layer_map.get(node), {}), "legacy"
    elif manifest_style not in styles:
        style, source = styles.get(layer_map.get(node), {}), "missing"
    else:
        name_override = layer_map.get(node)
        source = "ignored" if (name_override is not None and name_override != manifest_style) else "manifest"
        style = styles[manifest_style]

    effects = style.get("effects", [])
    psd_effects = layer.get("type", {}).get("effects", [])
    if (effects
            and not any(e in DROP_SHADOW_PSD for e in psd_effects)
            and not _visible_drop_shadow(fn.get("effects") or [])):
        effects = [e for e in effects if e.get("type") != "DROP_SHADOW"]
    return style, effects, source


def effect_expansion(fn, layer, text_styles):
    """Total pixel expansion per edge from stroke + drop shadow; plus recipe source."""
    _, effects, source = resolve_recipe(fn, layer, text_styles)
    sw = 0
    if fn.get("hasVisibleStroke") and fn.get("strokeAlign") == "OUTSIDE":
        sw = fn.get("strokeWeight", 0)
    left = top = right = bottom = sw

    for eff in effects:
        if eff.get("type") == "DROP_SHADOW" and eff.get("visible", True):
            ox = eff.get("offset", {}).get("x", 0)
            oy = eff.get("offset", {}).get("y", 0)
            r = eff.get("radius", 0)
            left = max(left, max(0, r - ox))
            top = max(top, max(0, r - oy))
            right = max(right, max(0, r + ox))
            bottom = max(bottom, max(0, r + oy))
    return (left, top, right, bottom), source


def fill_only_ink(fn, layer, text_styles):
    """Subtract effect expansion from absoluteRenderBounds to get glyph-only bounds."""
    x, y, w, h = fn["inkX"], fn["inkY"], fn["inkW"], fn["inkH"]
    (el, et, er, eb), source = effect_expansion(fn, layer, text_styles)
    return (x + el, y + et, w - el - er, h - et - eb), source


def matches_exception(fn, layer, exc):
    exc_fid = exc.get("figma_node_id", "")
    if exc_fid:
        fid = fn["id"]
        if fid == exc_fid or fid.endswith(";" + exc_fid):
            return True
    if exc.get("node") != layer["node"]:
        return False
    # A name-only exception must not leak across screens: node names are not
    # unique in the manifest (Bg_Demo appears on both), so an exception that
    # names a screen is confined to it.
    exc_screen = exc.get("screen")
    return exc_screen is None or exc_screen == layer.get("screen")


def find_exception(fn, layer, exceptions):
    for exc in exceptions:
        if matches_exception(fn, layer, exc):
            return exc
    return None


def check_drift(measured, current, keys):
    """Return list of drift violations (empty = OK)."""
    issues = []
    for key in keys:
        if key in measured:
            drift = abs(current[key] - measured[key])
            if drift > DRIFT_TOL:
                issues.append(
                    f"{key} drifted {drift:.3f}px "
                    f"(accepted {measured[key]:.4f}, now {current[key]:.4f})"
                )
    return issues


def fmt(v):
    return f"{v:g}" if v == int(v) else f"{v:.2f}"


def fmt_box(x, y, w, h):
    return f"{fmt(x)},{fmt(y)} {fmt(w)}x{fmt(h)}"


def load_expected_font(cfg):
    """Expected font {family, style}: settings figma.fonts.body, else paths.preflight."""
    font = cfg.settings.get("figma", {}).get("fonts", {}).get("body")
    if font:
        return font
    rel = cfg.settings.get("paths", {}).get("preflight")
    if rel:
        preflight = cfg.project_root / rel
        if preflight.exists():
            return json.loads(preflight.read_text())["fonts"]["body"]
    raise SystemExit(
        "no expected font: add figma.fonts.body to psd2figma.json "
        "(or paths.preflight pointing at a file that carries fonts.body)")


def load_valid_style_ids(cfg):
    """Merge the settings styleIdFiles, in order, into one set of valid ids."""
    ids = set()
    for name in cfg.settings.get("styleIdFiles", []):
        path = cfg.path(name)
        if not path.exists():
            continue
        data = json.loads(path.read_text())
        ts = data.get("textStyles", {})
        for sid in ts.values():
            if sid:
                ids.add(sid)
    return ids


def check_font_identity(nodes, expected_font, valid_style_ids):
    """Check every TEXT node for font family/style and text-style binding.

    Returns (font_violations, style_violations) — each a list of
    (node_id, node_name, detail_string).
    Font violations are a hard bug, never accepted debt.
    """
    font_violations = []
    style_violations = []
    for n in nodes:
        if n.get("type") != "TEXT":
            continue
        nid, nname = n["id"], n["name"]

        fn = n.get("fontName")
        if fn == "MIXED":
            font_violations.append((nid, nname, "fontName is MIXED"))
        elif isinstance(fn, dict):
            fam, sty = fn.get("family", ""), fn.get("style", "")
            if fam != expected_font["family"] or sty != expected_font["style"]:
                font_violations.append(
                    (nid, nname,
                     f"{fam}/{sty} (expected {expected_font['family']}/{expected_font['style']})"))
        elif fn is not None:
            font_violations.append((nid, nname, f"unexpected fontName value: {fn}"))

        sid = n.get("textStyleId")
        if sid == "MIXED":
            style_violations.append((nid, nname, "textStyleId is MIXED"))
        elif not sid:
            style_violations.append((nid, nname, "unbound (empty textStyleId)"))
        elif sid not in valid_style_ids:
            style_violations.append(
                (nid, nname, f"unknown style id {sid}"))

    return font_violations, style_violations


def _selftest():
    shadow = [{"type": "DROP_SHADOW", "radius": 3,
               "offset": {"x": 0, "y": 6}, "visible": True}]
    styles = {"s_shadow": {"effects": shadow},
              "s_plain": {"effects": []},
              "s_alt": {"effects": []}}
    ts = {"styles": styles,
          "layerMap": {"n_legacy": "s_plain", "scr/n_over": "s_alt"}}
    shadow_node = {"effects": shadow}
    plain_node = {"effects": []}

    def layer(style, effects, node="n_main", screen="scr"):
        return {"node": node, "screen": screen,
                "type": {"style": style, "effects": effects}}

    # manifest style drives selection; a shadow the node renders is kept
    _, eff, src = resolve_recipe(shadow_node, layer("s_shadow", ["DropShadow"]), ts)
    assert src == "manifest" and _visible_drop_shadow(eff), (src, eff)
    # phantom: shared style declares a shadow neither node nor PSD carries
    _, eff, src = resolve_recipe(plain_node, layer("s_shadow", ["Stroke"]), ts)
    assert src == "manifest" and not _visible_drop_shadow(eff), (src, eff)
    # a node-rendered shadow overrides an under-reporting manifest
    _, eff, src = resolve_recipe(shadow_node, layer("s_shadow", ["Stroke"]), ts)
    assert _visible_drop_shadow(eff), eff
    # Unknown style falls back to layerMap verbatim (legacy)
    _, eff, src = resolve_recipe(plain_node, layer("Unknown", [], "n_legacy"), ts)
    assert src == "legacy", src
    # manifest style absent from styles -> missing, layerMap fallback
    _, eff, src = resolve_recipe(plain_node, layer("s_gone", [], "n_legacy"), ts)
    assert src == "missing", src
    # layerMap disagrees with a valid manifest style -> ignored, manifest wins
    ts2 = {"styles": styles, "layerMap": {"n_main": "s_plain"}}
    _, eff, src = resolve_recipe(shadow_node, layer("s_shadow", ["DropShadow"]), ts2)
    assert src == "ignored", src
    # explicit screen/node override wins over everything
    _, eff, src = resolve_recipe(plain_node, layer("s_shadow", [], "n_over"), ts)
    assert src == "override", src
    print("resolve_recipe self-test OK")


def main():
    if "--selftest" in sys.argv[1:]:
        _selftest()
        return
    cfg, argv = resolve()

    parser = argparse.ArgumentParser(prog="verify_figma_vs_psd", add_help=False)
    parser.add_argument("--screen", action="append")
    parser.add_argument("--json", action="store_true", dest="json_out")
    args = parser.parse_args(argv)

    manifest = cfg.load("psd_manifest.json")
    debt = cfg.load("accepted_debt.json")
    text_styles = cfg.load("text_styles.json")
    report_path = cfg.path("verify_report.md")

    all_keys = derive_extract_keys(cfg, list(manifest["screens"].keys()))

    if args.screen:
        unknown = [k for k in args.screen if k not in all_keys]
        if unknown:
            print(f"unknown screen key(s): {', '.join(unknown)}", file=sys.stderr)
            print(f"known keys: {', '.join(all_keys)}", file=sys.stderr)
            sys.exit(2)
        requested = set(args.screen)
        active_keys = [k for k in all_keys if k in requested]
    else:
        active_keys = all_keys

    present_keys, missing = [], []
    for k in active_keys:
        if cfg.path(f"figma_extract_{k}.json").is_file():
            present_keys.append(k)
        else:
            missing.append(k)
    missing_set = set(missing)
    screen_filter = set(args.screen) if args.screen else None

    EXTRACT = {k: cfg.path(f"figma_extract_{k}.json") for k in present_keys}

    exceptions = debt.get("verification_exceptions", [])
    figma = {k: load(v)["nodes"] for k, v in EXTRACT.items()}

    expected_font = load_expected_font(cfg)
    valid_style_ids = load_valid_style_ids(cfg)

    figma_clipped = {}
    for k, v in EXTRACT.items():
        data = load(v)
        figma_clipped[k] = data.get("clippedText", [])

    all_font_violations = []
    all_style_violations = []
    for screen_key, nodes in figma.items():
        fv, sv = check_font_identity(nodes, expected_font, valid_style_ids)
        for nid, nname, detail in fv:
            all_font_violations.append((screen_key, nid, nname, detail))
        for nid, nname, detail in sv:
            all_style_violations.append((screen_key, nid, nname, detail))

    for screen_key, clipped in figma_clipped.items():
        fv, sv = check_font_identity(clipped, expected_font, valid_style_ids)
        for nid, nname, detail in fv:
            parent = next((c["parentLeaf"] for c in clipped if c["id"] == nid), "?")
            all_font_violations.append(
                (screen_key, nid, nname,
                 f"{detail} (inside clip frame {parent})"))
        for nid, nname, detail in sv:
            parent = next((c["parentLeaf"] for c in clipped if c["id"] == nid), "?")
            all_style_violations.append(
                (screen_key, nid, nname,
                 f"{detail} (inside clip frame {parent})"))

    rows = []
    used = {k: set() for k in figma}
    art_max = 0.0
    text_max = 0.0
    unmapped_manifest = 0
    exc_results = []
    failures = []
    all_deltas = []

    recipe_counts = {}
    recipe_ignored = []
    recipe_missing = []
    pin_unused = []

    json_rows = {k: [] for k in present_keys}
    per_art_max = {k: 0.0 for k in present_keys}
    per_text_max = {k: 0.0 for k in present_keys}
    per_unmapped_manifest = {k: 0 for k in present_keys}

    def json_row(screen, name, role, status, dx, dy, dw, dh, pin):
        if screen in json_rows:
            json_rows[screen].append({
                "node": name, "role": role, "status": status,
                "dx": dx, "dy": dy, "dw": dw, "dh": dh, "pin": pin,
            })

    for layer in manifest["layers"]:
        role, screen = layer["role"], layer["screen"]
        if role in ("skip", "group"):
            continue
        if screen in missing_set:
            continue
        if screen_filter is not None and screen not in screen_filter:
            continue

        mx, my, mw, mh = layer["x"], layer["y"], layer["w"], layer["h"]
        name = layer["node"]
        fn = match_node(mx, my, mw, mh, role,
                        figma.get(screen, []), used.get(screen, set()))

        if fn is None:
            rows.append((screen, name, role, fmt_box(mx, my, mw, mh),
                         "UNMAPPED", 0, 0, 0, 0, "none", None))
            unmapped_manifest += 1
            if screen in per_unmapped_manifest:
                per_unmapped_manifest[screen] += 1
            json_row(screen, name, role, "UNMAPPED", 0, 0, 0, 0, None)
            continue

        used[screen].add(fn["id"])

        # --- raw deltas and actual-box string ---
        is_ink = False
        ink_vals = None
        if role == "art":
            dx = fn["x"] - mx
            dy = fn["y"] - my
            dw = fn["w"] - mw
            dh = fn["h"] - mh
            actual_str = fmt_box(fn["x"], fn["y"], fn["w"], fn["h"])
        elif fn.get("type") == "TEXT" and "inkX" in fn:
            (ix, iy, iw, ih), recipe_source = fill_only_ink(fn, layer, text_styles)
            recipe_counts[recipe_source] = recipe_counts.get(recipe_source, 0) + 1
            layer_map = text_styles.get("layerMap", {})
            if recipe_source == "ignored":
                recipe_ignored.append(
                    (screen, name, layer_map.get(name), layer["type"]["style"]))
            elif recipe_source == "missing":
                recipe_missing.append(
                    (screen, name, layer["type"]["style"], layer_map.get(name)))
            ink_vals = (ix, iy, iw, ih)
            dx = ix - mx
            dy = iy - my
            dw = iw - mw
            dh = ih - mh
            actual_str = fmt_box(ix, iy, iw, ih)
            is_ink = True
        else:
            dx = fn["x"] - mx
            dy = fn["y"] - my
            dw = fn["w"] - mw
            dh = fn["h"] - mh
            actual_str = fmt_box(fn["x"], fn["y"], fn["w"], fn["h"])

        # --- exception check ---
        exc = find_exception(fn, layer, exceptions)

        if exc is not None:
            vp = exc.get("verify_property")
            measured = exc.get("measured", {})
            exc_ok = True
            detail = {"vp": vp, "role": role}

            # A pin whose row now clears the normal bar is unused, not a
            # regression: the recorded deviation no longer reproduces, so the
            # drift guard is moot. Report it for the next project to remove.
            pin_tol = ART_TOL if role == "art" else TEXT_TOL
            pin_unused_row = max(abs(dx), abs(dy), abs(dw), abs(dh)) <= pin_tol
            if pin_unused_row:
                detail["pin_unused"] = True
                pin_unused.append((screen, name, exc.get("reason", ""), dw, dh))

            if vp == "ink_centre":
                if ink_vals:
                    iix, iiy, iiw, iih = ink_vals
                else:
                    iix, iiy = fn["x"], fn["y"]
                    iiw, iih = fn["w"], fn["h"]
                cdx = (iix + iiw / 2) - (mx + mw / 2)
                cdy = (iiy + iih / 2) - (my + mh / 2)

                if not pin_unused_row:
                    if abs(cdx) > TEXT_TOL or abs(cdy) > TEXT_TOL:
                        exc_ok = False
                        detail["centre_fail"] = (
                            f"centre dx {cdx:.3f} dy {cdy:.3f} "
                            f"exceeds {TEXT_TOL}px bar"
                        )

                    current = {"centre_dx": cdx, "centre_dy": cdy,
                               "dw": dw, "dh": dh}
                    drift_issues = check_drift(measured, current,
                                               ["centre_dx", "centre_dy", "dw", "dh"])
                    if drift_issues:
                        exc_ok = False
                        detail["drift_fail"] = drift_issues

                detail.update({"cdx": cdx, "cdy": cdy, "dw": dw, "dh": dh})
                actual_str += " [centre]"
                cmp = "ink_centre"

            elif vp == "position_only":
                if not pin_unused_row:
                    tol = ART_TOL if role == "art" else TEXT_TOL
                    current = {"dx": dx, "dy": dy, "dw": dw, "dh": dh}

                    if "dx" in measured and "dy" in measured:
                        drift_issues = check_drift(
                            measured, current, ["dx", "dy", "dw", "dh"])
                    else:
                        if abs(dx) > tol or abs(dy) > tol:
                            exc_ok = False
                            detail["position_fail"] = (
                                f"dx {dx:.2f} dy {dy:.2f} exceeds {tol}px bar"
                            )
                        drift_issues = check_drift(
                            measured, current, ["dw", "dh"])

                    if drift_issues:
                        exc_ok = False
                        detail["drift_fail"] = drift_issues

                detail.update({"dx": dx, "dy": dy, "dw": dw, "dh": dh})
                cmp = "position_only"

            else:
                cmp = "ink" if is_ink else "layout"

            if exc_ok:
                exc_results.append((name, exc, "PASS", detail))
            else:
                exc_results.append((name, exc, "FAIL", detail))
                parts = []
                if "centre_fail" in detail:
                    parts.append(detail["centre_fail"])
                if "position_fail" in detail:
                    parts.append(detail["position_fail"])
                if "drift_fail" in detail:
                    parts.extend(detail["drift_fail"])
                failures.append(
                    f"{name}: exception {vp} FAILED — {'; '.join(parts)}"
                )

            rows.append((screen, name, role, fmt_box(mx, my, mw, mh),
                         actual_str, dx, dy, dw, dh, cmp,
                         exc.get("reason", "")))
            json_row(screen, name, role,
                     "EXC_PASS" if exc_ok else "EXC_FAIL",
                     dx, dy, dw, dh, exc.get("reason", ""))
        else:
            # --- normal pass/fail ---
            peak = max(abs(dx), abs(dy), abs(dw), abs(dh))
            cmp = "ink" if is_ink else "layout"

            if role == "art":
                art_max = max(art_max, peak)
                if screen in per_art_max:
                    per_art_max[screen] = max(per_art_max[screen], peak)
                if peak > ART_TOL:
                    failures.append(f"{name}: art {peak:.2f}px")
                status = "PASS" if peak <= ART_TOL else "FAIL"
            else:
                text_max = max(text_max, peak)
                if screen in per_text_max:
                    per_text_max[screen] = max(per_text_max[screen], peak)
                if peak > TEXT_TOL:
                    failures.append(f"{name}: text {peak:.2f}px")
                status = "PASS" if peak <= TEXT_TOL else "FAIL"
            all_deltas.append((name, role, peak))

            rows.append((screen, name, role, fmt_box(mx, my, mw, mh),
                         actual_str, dx, dy, dw, dh, cmp, None))
            json_row(screen, name, role, status, dx, dy, dw, dh, None)

    unmapped_figma = 0
    per_unmapped_figma = {k: 0 for k in present_keys}
    for screen, nodes in figma.items():
        for n in nodes:
            if n["id"] not in used[screen] and n["type"] in ("RECTANGLE", "TEXT", "FRAME", "INSTANCE"):
                unmapped_figma += 1
                if screen in per_unmapped_figma:
                    per_unmapped_figma[screen] += 1
    unmapped_total = unmapped_manifest + unmapped_figma

    all_exc_pass = all(s == "PASS" for _, _, s, _ in exc_results)
    font_ok = len(all_font_violations) == 0
    style_ok = len(all_style_violations) == 0
    bar_met = (art_max <= ART_TOL and text_max <= TEXT_TOL
               and unmapped_total == 0 and all_exc_pass
               and font_ok and style_ok)

    # ---- report ----
    lines = ["# Verify: Figma vs PSD Manifest\n"]

    for scr in EXTRACT:
        sr = [r for r in rows if r[0] == scr]
        if not sr:
            continue
        lines.append(f"\n## {scr}\n")
        hdr = (f"{'node':<25} {'expected':<22} {'actual':<30} "
               f"{'dx':>8} {'dy':>8} {'dw':>8} {'dh':>8}  compare")
        lines.append(hdr)
        lines.append("-" * len(hdr))
        for _, name, role, exp, act, dx, dy, dw, dh, cmp, exc_reason in sr:
            tag = f"  {cmp}"
            if exc_reason:
                tag += " [EXC]"
            if act == "UNMAPPED":
                lines.append(
                    f"{name:<25} {exp:<22} {'** UNMAPPED **':<30}"
                )
            else:
                lines.append(
                    f"{name:<25} {exp:<22} {act:<30} "
                    f"{dx:>8.2f} {dy:>8.2f} {dw:>8.2f} {dh:>8.2f}{tag}"
                )

    lines.append(f"\n## Unmapped\n")
    if unmapped_manifest:
        lines.append(f"Manifest without Figma match: {unmapped_manifest}")
    if unmapped_figma:
        lines.append(f"Figma without manifest match: {unmapped_figma}")
    if unmapped_total == 0:
        lines.append("None.")

    # ---- exceptions section (never folded into the pass list) ----
    lines.append(f"\n## Exceptions\n")
    if exc_results:
        for name, exc, status, detail in exc_results:
            vp = detail["vp"]
            lines.append(f"- **{name}** ({vp}): **{status}**")

            if vp == "ink_centre":
                lines.append(
                    f"  centre: dx {detail['cdx']:.3f} dy {detail['cdy']:.3f} "
                    f"(bar {TEXT_TOL:.1f}px)"
                )
                lines.append(
                    f"  size delta: dw {detail['dw']:+.2f} dh {detail['dh']:+.2f}"
                )
            elif vp == "position_only":
                tol = ART_TOL if detail.get("role") == "art" else TEXT_TOL
                lines.append(
                    f"  position: dx {detail['dx']:.2f} dy {detail['dy']:.2f} "
                    f"(bar {tol:.1f}px)"
                )
                lines.append(
                    f"  size delta: dw {detail['dw']:+.2f} dh {detail['dh']:+.2f}"
                )

            measured = exc.get("measured", {})
            if measured and not detail.get("pin_unused"):
                if vp == "ink_centre":
                    keys = ["centre_dx", "centre_dy", "dw", "dh"]
                    cur = {"centre_dx": detail["cdx"], "centre_dy": detail["cdy"],
                           "dw": detail["dw"], "dh": detail["dh"]}
                elif vp == "position_only":
                    if "dx" in measured and "dy" in measured:
                        keys = ["dx", "dy", "dw", "dh"]
                    else:
                        keys = ["dw", "dh"]
                    cur = {"dx": detail["dx"], "dy": detail["dy"],
                           "dw": detail["dw"], "dh": detail["dh"]}
                else:
                    keys, cur = [], {}

                parts = []
                for key in keys:
                    if key in measured:
                        drift = abs(cur.get(key, 0) - measured[key])
                        parts.append(f"{key} {drift:.3f}")
                if parts:
                    lines.append(
                        f"  drift: {', '.join(parts)} (max {DRIFT_TOL:.1f}px)"
                    )

            if "drift_fail" in detail:
                for issue in detail["drift_fail"]:
                    lines.append(f"  **DRIFT EXCEEDED**: {issue}")
            if "centre_fail" in detail:
                lines.append(f"  **CENTRE EXCEEDED**: {detail['centre_fail']}")
            if "position_fail" in detail:
                lines.append(f"  **POSITION EXCEEDED**: {detail['position_fail']}")

            lines.append(f"  reason: {exc.get('reason', '')}")
    else:
        lines.append("None.")

    # ---- font identity section ----
    lines.append(f"\n## Font Identity\n")
    lines.append(f"Expected: {expected_font['family']} {expected_font['style']}")
    if all_font_violations:
        lines.append(f"\n### Font violations ({len(all_font_violations)})\n")
        for scr, nid, nname, detail in all_font_violations:
            lines.append(f"- **{scr}** / {nname} ({nid}): {detail}")
    else:
        lines.append("All TEXT nodes: correct font.")

    # ---- text style binding section ----
    lines.append(f"\n## Text Style Binding\n")
    if all_style_violations:
        lines.append(f"### Unbound / unknown style ({len(all_style_violations)})\n")
        for scr, nid, nname, detail in all_style_violations:
            lines.append(f"- **{scr}** / {nname} ({nid}): {detail}")
    else:
        lines.append("All TEXT nodes: bound to a valid shared text style.")

    all_deltas.sort(key=lambda x: x[2], reverse=True)
    worst = [f"{n} {r}={d:.2f}" for n, r, d in all_deltas[:8]]

    n_pass = sum(1 for _, _, s, _ in exc_results if s == "PASS")
    n_fail = sum(1 for _, _, s, _ in exc_results if s == "FAIL")

    lines.append(f"\n## Summary\n")
    lines.append(f"art max: {art_max:.2f}px (tolerance {ART_TOL:.2f})")
    lines.append(f"text max: {text_max:.2f}px (tolerance {TEXT_TOL:.2f})")
    lines.append(f"unmapped: {unmapped_total}")
    lines.append(f"exceptions: {len(exc_results)} ({n_pass} pass, {n_fail} fail)")
    lines.append(f"font violations: {len(all_font_violations)}")
    lines.append(f"style violations: {len(all_style_violations)}")
    if worst:
        lines.append(f"worst (non-exception): {', '.join(worst[:5])}")
    lines.append(
        f"\nBar **{'MET (with exceptions)' if bar_met else 'NOT MET'}**\n"
    )

    report = "\n".join(lines)
    report_path.write_text(report)
    print(report)

    exit_code = 0 if (bar_met and not missing) else 1

    if missing:
        print("\nMissing extracts:")
        for k in missing:
            print(f"  {k}")

    print(
        f"\nrecipe source: manifest {recipe_counts.get('manifest', 0)} / "
        f"override {recipe_counts.get('override', 0)} / "
        f"ignored {recipe_counts.get('ignored', 0)}")

    for scr, node, lm_style, mf_style in recipe_ignored:
        print(f"RECIPE_OVERRIDE_IGNORED {scr}/{node}: "
              f"layerMap '{lm_style}' ignored, manifest '{mf_style}'",
              file=sys.stderr)
    for scr, node, mf_style, fallback in recipe_missing:
        print(f"RECIPE_MISSING {scr}/{node}: manifest style '{mf_style}' "
              f"not in text_styles.styles (using layerMap '{fallback}')",
              file=sys.stderr)
    for scr, node, reason, dw, dh in pin_unused:
        print(f"PIN_UNUSED {scr}/{node}: dw {dw:+.2f} dh {dh:+.2f} "
              f"now within bar — reason: {reason}", file=sys.stderr)

    if args.json_out:
        payload = {
            "screens": {
                k: {
                    "art_max": per_art_max[k],
                    "text_max": per_text_max[k],
                    "unmapped": per_unmapped_manifest[k] + per_unmapped_figma[k],
                    "font_violations": sum(
                        1 for s, *_ in all_font_violations if s == k),
                    "style_violations": sum(
                        1 for s, *_ in all_style_violations if s == k),
                    "rows": json_rows[k],
                }
                for k in present_keys
            },
            "missing": missing,
            "recipe": {
                tag: recipe_counts.get(tag, 0)
                for tag in ("manifest", "override", "ignored", "missing", "legacy")
            },
            "recipeIgnored": [
                {"screen": s, "node": n, "layerMap": lm, "manifest": mf}
                for s, n, lm, mf in recipe_ignored
            ],
            "recipeMissing": [
                {"screen": s, "node": n, "manifest": mf, "layerMap": fb}
                for s, n, mf, fb in recipe_missing
            ],
            "pinUnused": [
                {"screen": s, "node": n, "reason": r, "dw": dw, "dh": dh}
                for s, n, r, dw, dh in pin_unused
            ],
            "exit": exit_code,
        }
        cfg.path("verify_report.json").write_text(
            json.dumps(payload, indent=2, ensure_ascii=False))

    for scr, nid, nname, detail in all_font_violations:
        failures.append(f"{nname}: wrong font — {detail}")
    for scr, nid, nname, detail in all_style_violations:
        failures.append(f"{nname}: unbound text style — {detail}")

    if failures:
        print("\nFailures:", file=sys.stderr)
        for f_ in failures:
            print(f"  {f_}", file=sys.stderr)

    sys.exit(exit_code)


if __name__ == "__main__":
    main()
