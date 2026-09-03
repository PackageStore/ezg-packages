#!/usr/bin/env python3
"""
Verify Figma frames against PSD manifest — the numeric gate.

Art nodes: layout box, 0.00 px tolerance.
Text nodes: ink box (absoluteRenderBounds, stroke+shadow adjusted), 2.00 px tolerance.

Exceptions: accepted_debt.json entries that pin known deviations at their
measured values, with a 0.5px drift guard.  An exception is a pin, not an
amnesty — if a node's delta drifts from the recorded value, the gate fails.
"""

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


def effect_expansion(fn, layer, text_styles):
    """Total pixel expansion per edge from stroke + drop shadow."""
    sw = 0
    if fn.get("hasVisibleStroke") and fn.get("strokeAlign") == "OUTSIDE":
        sw = fn.get("strokeWeight", 0)
    left = top = right = bottom = sw

    style_name = text_styles.get("layerMap", {}).get(layer["node"])
    if style_name:
        style = text_styles.get("styles", {}).get(style_name, {})
        for eff in style.get("effects", []):
            if eff.get("type") == "DROP_SHADOW" and eff.get("visible", True):
                ox = eff.get("offset", {}).get("x", 0)
                oy = eff.get("offset", {}).get("y", 0)
                r = eff.get("radius", 0)
                left = max(left, max(0, r - ox))
                top = max(top, max(0, r - oy))
                right = max(right, max(0, r + ox))
                bottom = max(bottom, max(0, r + oy))
    return left, top, right, bottom


def fill_only_ink(fn, layer, text_styles):
    """Subtract effect expansion from absoluteRenderBounds to get glyph-only bounds."""
    x, y, w, h = fn["inkX"], fn["inkY"], fn["inkW"], fn["inkH"]
    el, et, er, eb = effect_expansion(fn, layer, text_styles)
    return x + el, y + et, w - el - er, h - et - eb


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


def main():
    cfg, _argv = resolve()

    manifest = cfg.load("psd_manifest.json")
    debt = cfg.load("accepted_debt.json")
    text_styles = cfg.load("text_styles.json")
    report_path = cfg.path("verify_report.md")

    EXTRACT = {
        k: cfg.path(f"figma_extract_{k}.json")
        for k in derive_extract_keys(cfg, list(manifest["screens"].keys()))
    }

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

    for layer in manifest["layers"]:
        role, screen = layer["role"], layer["screen"]
        if role in ("skip", "group"):
            continue

        mx, my, mw, mh = layer["x"], layer["y"], layer["w"], layer["h"]
        name = layer["node"]
        fn = match_node(mx, my, mw, mh, role,
                        figma.get(screen, []), used.get(screen, set()))

        if fn is None:
            rows.append((screen, name, role, fmt_box(mx, my, mw, mh),
                         "UNMAPPED", 0, 0, 0, 0, "none", None))
            unmapped_manifest += 1
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
            ix, iy, iw, ih = fill_only_ink(fn, layer, text_styles)
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

            if vp == "ink_centre":
                if ink_vals:
                    iix, iiy, iiw, iih = ink_vals
                else:
                    iix, iiy = fn["x"], fn["y"]
                    iiw, iih = fn["w"], fn["h"]
                cdx = (iix + iiw / 2) - (mx + mw / 2)
                cdy = (iiy + iih / 2) - (my + mh / 2)

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
        else:
            # --- normal pass/fail ---
            peak = max(abs(dx), abs(dy), abs(dw), abs(dh))
            cmp = "ink" if is_ink else "layout"

            if role == "art":
                art_max = max(art_max, peak)
                if peak > ART_TOL:
                    failures.append(f"{name}: art {peak:.2f}px")
            else:
                text_max = max(text_max, peak)
                if peak > TEXT_TOL:
                    failures.append(f"{name}: text {peak:.2f}px")
            all_deltas.append((name, role, peak))

            rows.append((screen, name, role, fmt_box(mx, my, mw, mh),
                         actual_str, dx, dy, dw, dh, cmp, None))

    unmapped_figma = 0
    for screen, nodes in figma.items():
        for n in nodes:
            if n["id"] not in used[screen] and n["type"] in ("RECTANGLE", "TEXT", "FRAME", "INSTANCE"):
                unmapped_figma += 1
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
            if measured:
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

    for scr, nid, nname, detail in all_font_violations:
        failures.append(f"{nname}: wrong font — {detail}")
    for scr, nid, nname, detail in all_style_violations:
        failures.append(f"{nname}: unbound text style — {detail}")

    if failures:
        print("\nFailures:", file=sys.stderr)
        for f_ in failures:
            print(f"  {f_}", file=sys.stderr)

    sys.exit(0 if bar_met else 1)


if __name__ == "__main__":
    main()
