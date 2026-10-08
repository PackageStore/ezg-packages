#!/usr/bin/env python3
"""Layout lock + session for the /new-ui designer pass (Phase D — .claude/docs/ui-designer-pass.md).

Phase D may lay art, FX and motion OVER the layout Phase C shipped — never move it, never
re-skin anything else. This script makes that promise checkable and reversible:

  begin    Start (or resume) a Phase D session for one screen prefab: state, git baseline and
           file backups live in .claude/tmp/ui-designer/<screen>/ (gitignored, survives a new
           agent session). Also reports when Phase D cannot run here (pieces not installed).
  snippet  Print the C# body to run through `unity_execute_code`. It reads the SAVED prefab
           asset (AssetDatabase.LoadAssetAtPath — no LoadPrefabContents, no save, no mutation),
           measures every node in a throwaway preview-scene instance, and writes a JSON snapshot.
  diff     Compare the Phase C snapshot (before) with the Phase D snapshot (after) under the
           overlay-only policy below. Exit 0 = pass, 1 = violations, 2 = unusable input.
  scope    Check which files changed since `begin`: only the screen prefab, the feature's
           Scripts/Controller + Visuals, ArtStyle.md, its pending screenshots and the lock report
           may change; touching other art/material/font/prefab files is a violation (exit 1).
  revert   Put everything Phase D touched back to the `begin` state and prove the prefab is
           byte-identical to the Phase C one.
  finish   Mark the session done (a later run of the same task sees Phase D already applied).

Overlay-only policy (diff):
  * Every node that existed before keeps its parent, its order among the pre-existing
    siblings, name, active flag, layer, tag, every component, and every serialized value
    (floats/vectors compared with a small tolerance): layout groups/elements, fitters, text,
    font sizes, localize keys, template links, the controller's serialized references.
  * Every node that existed before keeps its EFFECTIVE rect: the snippet instantiates the
    prefab in a preview scene, forces the canvas to 1080x1920, 1080x2400 and 1536x2048, rebuilds
    layout and measures each node. A moved/resized/re-anchored node, or one pushed around by
    something else, fails here even if its own serialized values look the same. Layout-driven
    RectTransform fields (anchored position, size, anchors) that differ while every measured
    rect is unchanged are only a warning (Unity refreshed a stale value on save); pivot, scale
    and rotation never may change.
  * The one change allowed on an existing component is an ART FILL of a visible empty slot: an
    Image/RawImage whose sprite/texture was empty (null or a Unity built-in placeholder), whose
    colour alpha was above zero, on a node with no Selectable and no Mask, gets art plus the
    display fields that go with it. A transparent hit area, a button's own graphic or a mask
    is not a slot. A fill inside a nested template instance is a warning (reviewer confirms
    the slot is not bound at runtime).
  * New components on an existing node: CanvasGroup only at rest (alpha 1, interactable,
    blocks raycasts); custom behaviours (idle FX, tween drivers) pass as WARNINGS for the
    reviewer. Anything that draws, restyles a graphic (Shadow/Outline and other mesh effects),
    lays out, takes input, carries text or localizes is a violation — art goes on new nodes.
  * New nodes (the overlay) never hang directly under the prefab root, under one of the root's
    own children (the template's layout nodes: code addresses them by child index) or under a
    ScrollRect's viewport/content (its children are the list). Under a layout group they must
    carry LayoutElement.ignoreLayout and are flagged for the reviewer (code may fill, clear or
    index that container). Every Graphic in the overlay has raycastTarget off; no Selectable,
    ScrollRect, raycaster, text or localize component anywhere in it.
  * Session `scope`: only the screen prefab, the feature's Scripts/Controller + Visuals,
    ArtStyle.md (+ boards, pending shots) and the mockup evidence files may change. Existing
    art/material/font/prefab/template files, code outside Scripts/Controller, the prefab's .meta,
    the mockup contract and the approved/rejected references are violations.

A lock pass is necessary, not sufficient: the reviewer still judges what the lock cannot see.
"""

from __future__ import annotations

import argparse
import hashlib
import json
import math
import os
import posixpath
import shutil
import subprocess
import sys
from datetime import datetime, timezone
from pathlib import Path

LOCK_VERSION = 1
FLOAT_TOLERANCE = 0.01
RECT_TOLERANCE_PX = 0.5
# Unity's own placeholder assets (UISprite, Background, Knob... live here). An Image that still
# points at one of them is an empty slot, so giving it real art is a fill, not a re-skin.
BUILTIN_GUIDS = ("0000000000000000e000000000000000", "0000000000000000f000000000000000")
FILL_PROPS = {"m_Sprite", "m_Texture"}
FILL_COMPANION_PROPS = {
    "m_Color", "m_PreserveAspect", "m_Type", "m_FillCenter", "m_PixelsPerUnitMultiplier",
    "m_UseSpriteMesh", "m_UVRect",
}
# RectTransform fields a layout group writes. Only these may drift on an existing node, and only
# while its measured rect stays put; m_LocalPosition only in x/y (z is the "invisible node" trap).
LAYOUT_DRIVEN_RECT_PROPS = {"m_AnchoredPosition", "m_SizeDelta", "m_AnchorMin", "m_AnchorMax"}
DENY_ON_EXISTING_NODE = (
    "selectable", "graphic", "text", "layoutController", "layoutGroup", "layoutElement",
    "raycaster", "scroll", "mask", "canvas", "localize", "meshEffect",
)
DENY_IN_NEW_SUBTREE = ("selectable", "raycaster", "scroll", "text", "localize")
CANVAS_GROUP_AT_REST = {
    "m_Alpha": "f:1", "m_Interactable": "b:1", "m_BlocksRaycasts": "b:1",
    "m_IgnoreParentGroups": "b:0",
}

SNIPPET = r'''// ui-layout-lock snapshot v1 (.claude/scripts/ui-layout-lock.py) - READ-ONLY.
// Serialized values come from the saved prefab ASSET; effective rects come from a throwaway
// instance in a preview scene (canvas forced to world space at fixed sizes, layout rebuilt).
// Never LoadPrefabContents, never SaveAsPrefabAsset, never touches the asset or an open scene.
var lockPrefabPath = "__PREFAB__";
var lockOutPath = "__OUT__";
var lockRoot = UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.GameObject>(lockPrefabPath);
if (lockRoot == null) throw new System.Exception("ui-layout-lock: no prefab at " + lockPrefabPath);
var lockInv = System.Globalization.CultureInfo.InvariantCulture;
var lockGuid = UnityEditor.AssetDatabase.AssetPathToGUID(lockPrefabPath);
System.Func<string, string> lockJs = str => {
    if (str == null) return "null";
    var esc = new System.Text.StringBuilder("\"");
    foreach (var ch in str) {
        if (ch == '"' || ch == '\\') esc.Append('\\').Append(ch);
        else if (ch < (char)0x20 || ch > (char)0x7e) esc.Append("\\u").Append(((int)ch).ToString("x4"));
        else esc.Append(ch);
    }
    return esc.Append('"').ToString();
};
System.Func<double, string> lockNum = dv => (double.IsNaN(dv) || double.IsInfinity(dv)) ? "nan" : System.Math.Round(dv, 3).ToString("0.###", lockInv);
System.Func<double, string> lockRectNum = dv => (double.IsNaN(dv) || double.IsInfinity(dv)) ? "null" : lockNum(dv);
System.Func<UnityEngine.Object, string> lockId = uo => {
    string gid; long lid;
    if (uo != null && UnityEditor.AssetDatabase.TryGetGUIDAndLocalFileIdentifier(uo, out gid, out lid)) return lid.ToString(lockInv);
    return "0";
};
System.Func<UnityEngine.Object, string> lockRef = uo => {
    if (uo == null) return "null";
    string gid; long lid;
    if (UnityEditor.AssetDatabase.TryGetGUIDAndLocalFileIdentifier(uo, out gid, out lid))
        return gid == lockGuid ? "local:" + lid.ToString(lockInv) : "asset:" + gid + ":" + lid.ToString(lockInv);
    return "other:" + uo.GetType().Name + ":" + uo.name;
};
System.Func<UnityEngine.Transform, string> lockSeg = tx => {
    if (tx.parent == null) return tx.name;
    var dup = 0;
    for (var si = 0; si < tx.GetSiblingIndex(); si++) if (tx.parent.GetChild(si).name == tx.name) dup++;
    return dup > 0 ? tx.name + "#" + dup : tx.name;
};
System.Func<UnityEngine.Component, string> lockKinds = cmp => {
    var kl = new System.Collections.Generic.List<string>();
    if (cmp is UnityEngine.UI.Selectable) kl.Add("selectable");
    if (cmp is UnityEngine.UI.Graphic) kl.Add("graphic");
    var isText = cmp is UnityEngine.UI.Text;
    for (var ty = cmp.GetType(); ty != null && !isText; ty = ty.BaseType) if (ty.FullName == "TMPro.TMP_Text") isText = true;
    if (isText) kl.Add("text");
    if (cmp is UnityEngine.UI.ILayoutController) kl.Add("layoutController");
    if (cmp is UnityEngine.UI.LayoutGroup) kl.Add("layoutGroup");
    if (cmp is UnityEngine.UI.LayoutElement) kl.Add("layoutElement");
    if (cmp is UnityEngine.EventSystems.BaseRaycaster) kl.Add("raycaster");
    if (cmp is UnityEngine.UI.ScrollRect) kl.Add("scroll");
    if (cmp is UnityEngine.UI.Mask || cmp is UnityEngine.UI.RectMask2D) kl.Add("mask");
    if (cmp is UnityEngine.Canvas) kl.Add("canvas");
    if (cmp is UnityEngine.CanvasGroup) kl.Add("canvasGroup");
    if (cmp is UnityEngine.UI.BaseMeshEffect) kl.Add("meshEffect");
    if (cmp.GetType().Name.IndexOf("Locali", System.StringComparison.OrdinalIgnoreCase) >= 0) kl.Add("localize");
    return string.Join(",", kl.ToArray());
};
System.Func<UnityEngine.Component, string> lockProps = cmp => {
    var ps = new System.Text.StringBuilder("{");
    var it = new UnityEditor.SerializedObject(cmp).GetIterator();
    var enter = true; var firstProp = true;
    while (it.Next(enter)) {
        enter = false;
        var pn = it.name;
        if (pn == "m_ObjectHideFlags" || pn == "m_CorrespondingSourceObject" || pn == "m_PrefabInstance"
            || pn == "m_PrefabAsset" || pn == "m_GameObject" || pn == "m_Father" || pn == "m_Children"
            || pn == "m_EditorHideFlags" || pn == "m_EditorClassIdentifier" || pn == "m_Script") continue;
        string pv;
        switch (it.propertyType) {
            case UnityEditor.SerializedPropertyType.Generic: enter = true; continue;
            case UnityEditor.SerializedPropertyType.Integer: pv = "i:" + it.longValue.ToString(lockInv); break;
            case UnityEditor.SerializedPropertyType.Boolean: pv = it.boolValue ? "b:1" : "b:0"; break;
            case UnityEditor.SerializedPropertyType.Float: pv = "f:" + lockNum(it.doubleValue); break;
            case UnityEditor.SerializedPropertyType.String: pv = "s:" + it.stringValue; break;
            case UnityEditor.SerializedPropertyType.Color: { var cv4 = it.colorValue; pv = "v:" + lockNum(cv4.r) + "," + lockNum(cv4.g) + "," + lockNum(cv4.b) + "," + lockNum(cv4.a); break; }
            case UnityEditor.SerializedPropertyType.ObjectReference: pv = "r:" + lockRef(it.objectReferenceValue); break;
            case UnityEditor.SerializedPropertyType.LayerMask: pv = "i:" + it.intValue.ToString(lockInv); break;
            case UnityEditor.SerializedPropertyType.Enum: pv = "i:" + it.intValue.ToString(lockInv); break;
            case UnityEditor.SerializedPropertyType.Vector2: { var v2 = it.vector2Value; pv = "v:" + lockNum(v2.x) + "," + lockNum(v2.y); break; }
            case UnityEditor.SerializedPropertyType.Vector3: { var v3 = it.vector3Value; pv = "v:" + lockNum(v3.x) + "," + lockNum(v3.y) + "," + lockNum(v3.z); break; }
            case UnityEditor.SerializedPropertyType.Vector4: { var v4 = it.vector4Value; pv = "v:" + lockNum(v4.x) + "," + lockNum(v4.y) + "," + lockNum(v4.z) + "," + lockNum(v4.w); break; }
            case UnityEditor.SerializedPropertyType.Quaternion: { var q4 = it.quaternionValue; pv = "v:" + lockNum(q4.x) + "," + lockNum(q4.y) + "," + lockNum(q4.z) + "," + lockNum(q4.w); break; }
            case UnityEditor.SerializedPropertyType.Rect: { var r4 = it.rectValue; pv = "v:" + lockNum(r4.x) + "," + lockNum(r4.y) + "," + lockNum(r4.width) + "," + lockNum(r4.height); break; }
            case UnityEditor.SerializedPropertyType.ArraySize: pv = "i:" + it.intValue.ToString(lockInv); break;
            case UnityEditor.SerializedPropertyType.Character: pv = "i:" + it.intValue.ToString(lockInv); break;
            case UnityEditor.SerializedPropertyType.Vector2Int: { var i2 = it.vector2IntValue; pv = "v:" + i2.x + "," + i2.y; break; }
            case UnityEditor.SerializedPropertyType.Vector3Int: { var i3 = it.vector3IntValue; pv = "v:" + i3.x + "," + i3.y + "," + i3.z; break; }
            case UnityEditor.SerializedPropertyType.Bounds: { var b6 = it.boundsValue; pv = "v:" + lockNum(b6.center.x) + "," + lockNum(b6.center.y) + "," + lockNum(b6.center.z) + "," + lockNum(b6.extents.x) + "," + lockNum(b6.extents.y) + "," + lockNum(b6.extents.z); break; }
            case UnityEditor.SerializedPropertyType.BoundsInt: { var bi = it.boundsIntValue; pv = "v:" + bi.position.x + "," + bi.position.y + "," + bi.position.z + "," + bi.size.x + "," + bi.size.y + "," + bi.size.z; break; }
            case UnityEditor.SerializedPropertyType.RectInt: { var ri4 = it.rectIntValue; pv = "v:" + ri4.x + "," + ri4.y + "," + ri4.width + "," + ri4.height; break; }
            case UnityEditor.SerializedPropertyType.AnimationCurve: {
                var crv = it.animationCurveValue; var ksb = new System.Text.StringBuilder("c:");
                if (crv != null) foreach (var key in crv.keys) ksb.Append(lockNum(key.time)).Append('/').Append(lockNum(key.value)).Append('/').Append(lockNum(key.inTangent)).Append('/').Append(lockNum(key.outTangent)).Append(';');
                pv = ksb.ToString(); break; }
            case UnityEditor.SerializedPropertyType.ManagedReference: pv = "t:managed:" + it.managedReferenceFullTypename; enter = true; break;
            default: pv = "t:" + it.propertyType; break;
        }
        if (!firstProp) ps.Append(',');
        firstProp = false;
        ps.Append(lockJs(it.propertyPath)).Append(':').Append(lockJs(pv));
    }
    return ps.Append('}').ToString();
};
// Pass 1 - effective rects. Values in the asset can be stale for layout-driven nodes, so the
// lock also measures what is actually laid out, at three canvas sizes (anchor edits show up).
var lockSizes = new UnityEngine.Vector2[] { new UnityEngine.Vector2(1080, 1920), new UnityEngine.Vector2(1080, 2400), new UnityEngine.Vector2(1536, 2048) };
var lockRects = new System.Collections.Generic.Dictionary<string, System.Text.StringBuilder>();
var lockNested = new System.Collections.Generic.HashSet<string>();
var lockScene = UnityEditor.SceneManagement.EditorSceneManager.NewPreviewScene();
try {
    var inst = (UnityEngine.GameObject)UnityEditor.PrefabUtility.InstantiatePrefab(lockRoot, lockScene);
    var instRt = inst.GetComponent<UnityEngine.RectTransform>();
    var instCanvas = inst.GetComponent<UnityEngine.Canvas>();
    if (instCanvas != null) instCanvas.renderMode = UnityEngine.RenderMode.WorldSpace;
    var instRts = inst.GetComponentsInChildren<UnityEngine.RectTransform>(true);
    var corners = new UnityEngine.Vector3[4];
    foreach (var canvasSize in lockSizes) {
        if (instRt != null) { instRt.localScale = UnityEngine.Vector3.one; instRt.position = UnityEngine.Vector3.zero; instRt.sizeDelta = canvasSize; }
        UnityEngine.Canvas.ForceUpdateCanvases();
        for (var pass = 0; pass < 2; pass++)
            for (var ri = instRts.Length - 1; ri >= 0; ri--)
                if (instRts[ri].GetComponent<UnityEngine.UI.ILayoutController>() != null)
                    UnityEngine.UI.LayoutRebuilder.ForceRebuildLayoutImmediate(instRts[ri]);
        foreach (var rt in instRts) {
            var rid = lockId(UnityEditor.PrefabUtility.GetCorrespondingObjectFromSource(rt.gameObject));
            if (rid == "0" || instRt == null) continue;
            if (UnityEditor.PrefabUtility.GetNearestPrefabInstanceRoot(rt.gameObject) != inst) lockNested.Add(rid);
            rt.GetWorldCorners(corners);
            var lo = instRt.InverseTransformPoint(corners[0]);
            var hi = instRt.InverseTransformPoint(corners[2]);
            System.Text.StringBuilder rsb;
            if (!lockRects.TryGetValue(rid, out rsb)) { rsb = new System.Text.StringBuilder(); lockRects[rid] = rsb; }
            else rsb.Append(',');
            rsb.Append('"').Append(((int)canvasSize.x).ToString(lockInv)).Append('x').Append(((int)canvasSize.y).ToString(lockInv)).Append("\":[")
               .Append(lockRectNum(lo.x)).Append(',').Append(lockRectNum(lo.y)).Append(',').Append(lockRectNum(hi.x)).Append(',').Append(lockRectNum(hi.y)).Append(']');
        }
    }
} finally {
    UnityEditor.SceneManagement.EditorSceneManager.ClosePreviewScene(lockScene);
}
// Pass 2 - every node + every serialized property, keyed by local file id.
var lockAll = lockRoot.GetComponentsInChildren<UnityEngine.Transform>(true);
var lockOut = new System.Text.StringBuilder();
lockOut.Append("{\"lockVersion\":1,\"prefab\":").Append(lockJs(lockPrefabPath)).Append(",\"guid\":").Append(lockJs(lockGuid));
lockOut.Append(",\"unity\":").Append(lockJs(UnityEngine.Application.unityVersion)).Append(",\"nodes\":[");
for (var ni = 0; ni < lockAll.Length; ni++) {
    var tr = lockAll[ni]; var go = tr.gameObject;
    var nodePath = lockSeg(tr);
    for (var up = tr.parent; up != null; up = up.parent) nodePath = lockSeg(up) + "/" + nodePath;
    var nid = lockId(go);
    System.Text.StringBuilder nodeRects;
    if (ni > 0) lockOut.Append(',');
    lockOut.Append("{\"id\":").Append(lockJs(nid)).Append(",\"path\":").Append(lockJs(nodePath));
    lockOut.Append(",\"parent\":").Append(lockJs(tr.parent == null ? "" : lockId(tr.parent.gameObject)));
    lockOut.Append(",\"order\":").Append(tr.GetSiblingIndex().ToString(lockInv));
    lockOut.Append(",\"name\":").Append(lockJs(go.name)).Append(",\"active\":").Append(go.activeSelf ? "true" : "false");
    lockOut.Append(",\"layer\":").Append(go.layer.ToString(lockInv)).Append(",\"tag\":").Append(lockJs(go.tag));
    lockOut.Append(",\"nested\":").Append(lockNested.Contains(nid) ? "true" : "false");
    lockOut.Append(",\"rects\":{").Append(lockRects.TryGetValue(nid, out nodeRects) ? nodeRects.ToString() : "").Append('}');
    lockOut.Append(",\"components\":[");
    var comps = go.GetComponents<UnityEngine.Component>();
    for (var ci = 0; ci < comps.Length; ci++) {
        var comp = comps[ci];
        if (ci > 0) lockOut.Append(',');
        if (comp == null) { lockOut.Append("{\"id\":").Append(lockJs("missing#" + ci)).Append(",\"type\":\"<missing>\",\"kinds\":\"\",\"props\":{}}"); continue; }
        lockOut.Append("{\"id\":").Append(lockJs(lockId(comp))).Append(",\"type\":").Append(lockJs(comp.GetType().FullName));
        lockOut.Append(",\"kinds\":").Append(lockJs(lockKinds(comp))).Append(",\"props\":").Append(lockProps(comp)).Append('}');
    }
    lockOut.Append("]}");
}
lockOut.Append("]}");
var lockDir = System.IO.Path.GetDirectoryName(lockOutPath);
if (!string.IsNullOrEmpty(lockDir)) System.IO.Directory.CreateDirectory(lockDir);
System.IO.File.WriteAllText(lockOutPath, lockOut.ToString(), new System.Text.UTF8Encoding(false));
UnityEngine.Debug.Log("[ui-layout-lock] " + lockAll.Length + " nodes, " + lockRects.Count + " measured -> " + lockOutPath);
'''


class LockInputError(ValueError):
    pass


# ---------------------------------------------------------------------------------- snippet


def cs_string(value: str) -> str:
    """Escape for the inside of a C# regular string literal."""
    return value.replace("\\", "\\\\").replace('"', '\\"')


def check_prefab_path(prefab: str) -> str:
    prefab = prefab.replace("\\", "/")
    if not prefab.endswith(".prefab") or not (prefab.startswith("Assets/") or prefab.startswith("Packages/")):
        raise LockInputError(f"--prefab must be a project-relative .prefab asset path, got: {prefab}")
    return prefab


def build_snippet(prefab: str, out: Path) -> str:
    prefab = check_prefab_path(prefab)
    out_abs = str(out.resolve()).replace("\\", "/")
    return SNIPPET.replace("__PREFAB__", cs_string(prefab)).replace("__OUT__", cs_string(out_abs))


# ------------------------------------------------------------------------------------- diff


def load_snapshot(path: Path) -> dict:
    try:
        data = json.loads(path.read_text(encoding="utf-8"))
    except (OSError, json.JSONDecodeError) as exc:
        raise LockInputError(f"{path}: {exc}") from exc
    if data.get("lockVersion") != LOCK_VERSION:
        raise LockInputError(f"{path}: lockVersion {data.get('lockVersion')!r} (expected {LOCK_VERSION})")
    if not isinstance(data.get("nodes"), list) or not data["nodes"]:
        raise LockInputError(f"{path}: snapshot has no nodes")
    return data


def kinds(component: dict) -> set:
    return {k for k in component.get("kinds", "").split(",") if k}


def node_kinds(node: dict) -> set:
    out = set()
    for c in node["components"]:
        out |= kinds(c)
    return out


def numbers(value: str):
    try:
        return [float(x) for x in value[2:].split(",")]
    except ValueError:
        return None


def same_value(a: str, b: str) -> bool:
    if a == b:
        return True
    if a[:2] != b[:2] or a[:2] not in ("f:", "v:"):
        return False
    na, nb = numbers(a), numbers(b)
    if na is None or nb is None or len(na) != len(nb):
        return False
    return all(math.isclose(x, y, abs_tol=FLOAT_TOLERANCE) for x, y in zip(na, nb))


def same_coord(x, y) -> bool:
    if x is None or y is None:
        return x is None and y is None
    return math.isclose(x, y, abs_tol=RECT_TOLERANCE_PX)


def alpha_of(color: str):
    vals = numbers(color) if color and color.startswith("v:") else None
    return vals[3] if vals and len(vals) == 4 else None


def is_placeholder_ref(value: str) -> bool:
    if value == "r:null":
        return True
    return any(value.startswith(f"r:asset:{guid}:") for guid in BUILTIN_GUIDS)


def identity(before: dict, after: dict) -> str:
    """Local file ids are stable across saves; fall back to paths only if they are not usable."""
    for snap in (before, after):
        ids = [n.get("id") for n in snap["nodes"]]
        if any(i in (None, "", "0") for i in ids) or len(set(ids)) != len(ids):
            return "path"
    return "id"


def local_ref_targets(nodes: dict, comp_owner: dict, prop_names) -> set:
    """Node keys a ScrollRect points at through `r:local:<component id>` references."""
    out = set()
    for n in nodes.values():
        for c in n["components"]:
            if "scroll" not in kinds(c):
                continue
            for prop in prop_names:
                ref = c["props"].get(prop, "")
                if ref.startswith("r:local:"):
                    owner = comp_owner.get(ref[len("r:local:"):])
                    if owner is not None:
                        out.add(owner)
    return out


def diff(before: dict, after: dict) -> dict:
    if before.get("guid") != after.get("guid"):
        raise LockInputError(
            f"snapshots are of different prefabs: {before.get('prefab')} vs {after.get('prefab')}"
        )
    key = identity(before, after)
    b_nodes = {n[key]: n for n in before["nodes"]}
    a_nodes = {n[key]: n for n in after["nodes"]}
    # Parent links are recorded as ids; translate them when identity falls back to paths.
    for nodes in (b_nodes, a_nodes):
        by_id = {n["id"]: (n["path"] if key == "path" else n["id"]) for n in nodes.values()}
        for n in nodes.values():
            n["_parent"] = by_id.get(n["parent"], "") if n["parent"] else ""

    violations, warnings, art_fills, added_nodes, added_components = [], [], [], [], []

    def violate(node, rule, **extra):
        violations.append({"node": node["path"], "rule": rule, **extra})

    def warn(node, rule, **extra):
        warnings.append({"node": node["path"], "rule": rule, **extra})

    if key == "path":
        warnings.append({"node": before.get("prefab", ""), "rule": "identity-fallback",
                         "detail": "local file ids missing/duplicated - matched nodes by path"})

    # ---- existing nodes: structure ------------------------------------------------------
    for k, bn in b_nodes.items():
        an = a_nodes.get(k)
        if an is None:
            violate(bn, "node-removed")
            continue
        if an["_parent"] != bn["_parent"]:
            violate(bn, "node-reparented", before=bn["_parent"], after=an["_parent"])
        for field in ("name", "active", "layer", "tag"):
            if an.get(field) != bn.get(field):
                violate(bn, f"node-{field}-changed", before=bn.get(field), after=an.get(field))

    # Order among the siblings that existed before (overlay siblings may sit anywhere legal).
    def sibling_order(nodes, parent):
        kids = [(n["order"], k) for k, n in nodes.items() if k in b_nodes and n["_parent"] == parent]
        return [k for _, k in sorted(kids)]

    for parent in {n["_parent"] for n in b_nodes.values()}:
        if parent and parent not in a_nodes:
            continue
        b_order = sibling_order(b_nodes, parent)
        a_order = [k for k in sibling_order(a_nodes, parent) if k in b_order]
        if [k for k in b_order if k in a_order] != a_order:
            violate(b_nodes[b_order[0]], "siblings-reordered",
                    before=[b_nodes[k]["name"] for k in b_order],
                    after=[a_nodes[k]["name"] for k in a_order])

    # ---- existing nodes: effective rects ------------------------------------------------
    rect_changed = set()
    for k, bn in b_nodes.items():
        an = a_nodes.get(k)
        if an is None:
            continue
        b_rects, a_rects = bn.get("rects") or {}, an.get("rects") or {}
        sizes = sorted(b_rects.keys() & a_rects.keys())
        if not sizes:
            warn(bn, "rect-not-measured", detail="node has no measured rect in one of the snapshots")
            continue
        moved = {
            s: {"before": b_rects[s], "after": a_rects[s]}
            for s in sizes
            if len(b_rects[s]) != len(a_rects[s])
            or any(not same_coord(x, y) for x, y in zip(b_rects[s], a_rects[s]))
        }
        if moved:
            rect_changed.add(k)
            violate(bn, "node-rect-changed", rects=moved)

    # Objects custom code holds a serialized reference to (controllers, views, templates).
    referenced = {
        v[len("r:local:"):]
        for n in b_nodes.values() for c in n["components"]
        if not c["type"].startswith(("UnityEngine.", "TMPro."))
        for v in c["props"].values() if v.startswith("r:local:")
    }

    # ---- existing nodes: components -----------------------------------------------------
    for k, bn in b_nodes.items():
        an = a_nodes.get(k)
        if an is None:
            continue
        b_comps = {c["id"]: c for c in bn["components"]}
        a_comps = {c["id"]: c for c in an["components"]}
        measured = bool(bn.get("rects")) and bool(an.get("rects"))
        for cid, bc in b_comps.items():
            ac = a_comps.get(cid)
            if ac is None or ac["type"] != bc["type"]:
                violate(bn, "component-removed", component=bc["type"])
                continue
            filled = False
            for prop in FILL_PROPS:
                bv, av = bc["props"].get(prop), ac["props"].get(prop)
                if (bv is not None and av is not None and "graphic" in kinds(bc)
                        and is_placeholder_ref(bv) and not is_placeholder_ref(av)):
                    alpha = alpha_of(bc["props"].get("m_Color", ""))
                    blocked = node_kinds(bn) & {"selectable", "mask"}
                    if blocked or alpha is None or alpha <= 0.01:
                        violate(bn, "art-fill-not-a-slot", component=bc["type"], prop=prop, after=av,
                                detail="only a visible empty slot on a node without Button/Selectable/Mask may be filled")
                        continue
                    filled = True
                    art_fills.append({"node": bn["path"], "component": bc["type"], "prop": prop, "after": av})
                    if bn.get("nested"):
                        warn(bn, "art-fill-inside-template-instance", component=bc["type"],
                             detail="confirm the slot is static (not set at runtime by the template)")
                    if referenced & ({bn["id"]} | {c["id"] for c in bn["components"]}):
                        warn(bn, "art-fill-on-referenced-slot", component=bc["type"],
                             detail="code holds a reference to this node - confirm it never sets the sprite")
            for prop, bv in bc["props"].items():
                av = ac["props"].get(prop)
                if av is None:
                    violate(bn, "property-removed", component=bc["type"], prop=prop, before=bv)
                    continue
                if same_value(bv, av):
                    continue
                if filled and (prop in FILL_PROPS or prop in FILL_COMPANION_PROPS):
                    continue
                if prop in FILL_PROPS and "graphic" in kinds(bc) and not is_placeholder_ref(bv):
                    violate(bn, "existing-art-replaced", component=bc["type"], prop=prop, before=bv, after=av)
                elif (bc["type"] == "UnityEngine.RectTransform" and measured and k not in rect_changed
                        and (prop in LAYOUT_DRIVEN_RECT_PROPS
                             or (prop == "m_LocalPosition" and (numbers(bv) or [0, 0, 0])[2:] == (numbers(av) or [0, 0, 1])[2:]))):
                    warn(bn, "rect-serialized-drift", prop=prop, before=bv, after=av,
                         detail="layout-driven value differs but every measured rect is unchanged")
                else:
                    violate(bn, "property-changed", component=bc["type"], prop=prop, before=bv, after=av)
            for prop in ac["props"].keys() - bc["props"].keys():
                warn(bn, "serialized-field-added", component=ac["type"], prop=prop, after=ac["props"][prop])
        for cid, ac in a_comps.items():
            if cid in b_comps:
                continue
            added_components.append({"node": an["path"], "component": ac["type"]})
            ck = kinds(ac)
            denied = sorted(ck & set(DENY_ON_EXISTING_NODE))
            if denied:
                violate(an, "component-added-to-existing-node", component=ac["type"], kinds=denied)
            elif "canvasGroup" in ck:
                rest = {p: ac["props"].get(p) for p in CANVAS_GROUP_AT_REST}
                if any(not same_value(v or "", CANVAS_GROUP_AT_REST[p]) for p, v in rest.items()):
                    violate(an, "canvas-group-not-at-rest", component=ac["type"], props=rest)
            else:
                warn(an, "behaviour-added-to-existing-node", component=ac["type"])

    # ---- new nodes (the overlay) --------------------------------------------------------
    roots = {k for k, n in a_nodes.items() if not n["_parent"]}
    comp_owner = {c["id"]: k for k, n in a_nodes.items() for c in n["components"]}
    scroll_targets = local_ref_targets(a_nodes, comp_owner, ("m_Content", "m_Viewport"))

    def parent_ban(parent_key):
        if parent_key in roots:
            return "prefab root"
        if a_nodes[parent_key]["_parent"] in roots:
            return "a direct child of the prefab root (template layout node, addressed by child index)"
        if parent_key in scroll_targets:
            return "a ScrollRect viewport/content (its children are the list)"
        return None

    def in_layout_group(parent_key):
        return any("layoutGroup" in kinds(c) and c["props"].get("m_Enabled", "b:1") == "b:1"
                   for c in a_nodes[parent_key]["components"])

    for k, an in a_nodes.items():
        if k in b_nodes:
            continue
        added_nodes.append(an["path"])
        parent = an["_parent"]
        if parent in b_nodes and parent in a_nodes:
            reason = parent_ban(parent)
            if reason:
                violate(an, "overlay-parent-not-allowed",
                        detail=f"parent is {reason} - attach the overlay to a leaf visual node instead")
            elif in_layout_group(parent):
                ignores = any("layoutElement" in kinds(c) and c["props"].get("m_IgnoreLayout") == "b:1"
                              and c["props"].get("m_Enabled", "b:1") == "b:1" for c in an["components"])
                if not ignores:
                    violate(an, "overlay-joins-layout-flow",
                            detail="parent has a layout group - add LayoutElement with ignoreLayout")
                else:
                    warn(an, "overlay-in-layout-group",
                         detail="confirm no code fills, clears or indexes this container's children")
        for c in an["components"]:
            ck = kinds(c)
            denied = sorted(ck & set(DENY_IN_NEW_SUBTREE))
            if denied:
                violate(an, "overlay-component-denied", component=c["type"], kinds=denied)
            if "graphic" in ck and c["props"].get("m_RaycastTarget") != "b:0":
                violate(an, "overlay-eats-touches", component=c["type"],
                        detail="set raycastTarget = false on decorative graphics")
            if "canvas" in ck:
                warn(an, "overlay-nested-canvas", component=c["type"])
            if c["type"] == "<missing>":
                violate(an, "overlay-missing-script")

    return {
        "ok": not violations,
        "lockVersion": LOCK_VERSION,
        "prefab": after.get("prefab"),
        "identity": key,
        "counts": {
            "nodesBefore": len(b_nodes), "nodesAfter": len(a_nodes),
            "addedNodes": len(added_nodes), "addedComponents": len(added_components),
            "artFills": len(art_fills), "violations": len(violations), "warnings": len(warnings),
        },
        "violations": violations,
        "warnings": warnings,
        "artFills": art_fills,
        "addedNodes": added_nodes,
        "addedComponents": added_components,
    }


# ---------------------------------------------------------------------------------- session
#
# Lives in <repo>/.claude/tmp/ui-designer/<screen>/ — gitignored, so `git add -A` never sweeps
# it, and outside the agent's scratchpad, so a recovery/resume session finds it again.
#
# Safety rules (each one closes a way a revert could destroy someone's work):
#   * A session belongs to the commit HEAD was on at `begin`. Once HEAD moves (the task shipped,
#     the dev committed), the session is stale: `begin` archives it and starts fresh, `scope` and
#     `revert` refuse. A later task can never revert an earlier task's state.
#   * "Changed since begin" is decided by CONTENT (git blob ids), never by git status: the backlog
#     stages the whole tree (`git add -A`) between Phase D and the later gates.
#   * Every file that was already dirty at begin and that a revert might have to put back is kept
#     — file copies for the screen's own files, `git hash-object -w` blobs for the rest.
#   * After `finish`, a revert touches only Phase D's own footprint, and refuses when a footprint
#     file was edited after finish (a later fix round) unless --discard-later-edits says so.
#   * Sessions are archived, never deleted; what a revert overwrites is copied to discarded/.

SESSION_ROOT = Path(".claude") / "tmp" / "ui-designer"
SESSION_PREFIX = ".claude/tmp/"
STATE_FILE = "state.json"
# Asset types whose modification re-skins other screens (or trips the ui-kit-stale preflight
# when it is a template prefab). Phase D creates new art; it never edits existing art.
RESKIN_SUFFIXES = (
    ".png", ".jpg", ".jpeg", ".psd", ".psb", ".tga", ".tif", ".tiff", ".gif", ".bmp", ".exr",
    ".hdr", ".svg", ".mat", ".shader", ".shadergraph", ".shadersubgraph", ".hlsl", ".cginc",
    ".ttf", ".otf", ".fontsettings", ".prefab", ".spriteatlas", ".spriteatlasv2", ".anim",
    ".controller", ".overridecontroller", ".mask", ".physicsmaterial2d",
)
CODE_SUFFIXES = (".cs", ".asmdef", ".asmref")
EVIDENCE_SUFFIXES = (".unity.png", ".ui-build-report.json", ".ui-visual-diff.json", ".ui-layout-lock.json")
REQUIRED_PIECES = {
    ".claude/docs/ui-designer-pass.md": None,
    ".claude/agents/ui-visual-reviewer.md": "Phase D (designer pass",
}
GIT_ENV = {**os.environ, "GIT_LITERAL_PATHSPECS": "1"}  # `btn_[ab].mat` is a file name, not a glob


def git(root: Path, *args, data: bytes | None = None, check: bool = True) -> subprocess.CompletedProcess:
    return subprocess.run(["git", *args], cwd=root, input=data, capture_output=True, check=check, env=GIT_ENV)


def repo_root() -> Path:
    try:
        out = subprocess.run(["git", "rev-parse", "--show-toplevel"], capture_output=True,
                             text=True, check=True).stdout.strip()
        if out:
            return Path(out)
    except (OSError, subprocess.CalledProcessError):
        pass
    raise LockInputError("not inside a git repository - run from the project root")


def sha256_of(path: Path):
    try:
        return hashlib.sha256(path.read_bytes()).hexdigest()
    except OSError:
        return None


def head_commit(root: Path):
    proc = git(root, "rev-parse", "--verify", "-q", "HEAD", check=False)
    return proc.stdout.decode().strip() or None


def dirty_paths(root: Path) -> dict:
    """path -> two-letter status for everything git reports (staged, unstaged, untracked)."""
    out = git(root, "status", "--porcelain=v1", "-z", "--untracked-files=all").stdout.decode("utf-8", "replace")
    entries, items, i = out.split("\0"), {}, 0
    while i < len(entries):
        entry = entries[i]
        i += 1
        if len(entry) < 4:
            continue
        status, path = entry[:2], entry[3:]
        if status[0] in "RC":  # rename/copy: the next field is the old path - track both
            if i < len(entries) and entries[i] and not entries[i].startswith(SESSION_PREFIX):
                items.setdefault(entries[i], "D ")
            i += 1
        if not path.startswith(SESSION_PREFIX):
            items[path] = status
    return items


def blob_ids(root: Path, paths, store: bool = False) -> dict:
    """path -> blob id of the working-tree file (None when missing); `store` writes the blobs."""
    paths = list(paths)
    present = [p for p in paths if (root / p).is_file()]
    ids = {p: None for p in paths}
    args = ["hash-object", "--stdin-paths"] + (["-w"] if store else [])
    for start in range(0, len(present), 500):
        chunk = present[start:start + 500]
        out = git(root, *args, data="\n".join(chunk).encode()).stdout.decode().split()
        ids.update(zip(chunk, out))
    return ids


def tree_ids(root: Path, commit, paths) -> dict:
    """path -> blob id in `commit`, or None (no commit yet / path not in it)."""
    paths = list(paths)
    ids = {p: None for p in paths}
    if not commit:
        return ids
    for start in range(0, len(paths), 200):
        chunk = paths[start:start + 200]
        out = git(root, "ls-tree", "-r", "-z", commit, "--", *chunk).stdout.decode("utf-8", "replace")
        for rec in filter(None, out.split("\0")):
            meta, path = rec.split("\t", 1)
            ids[path] = meta.split()[2]
    return ids


def clean_rel(path: str, root: Path | None = None) -> str:
    if not path:
        return path
    path = path.replace("\\", "/")
    if root is not None and os.path.isabs(path):
        try:
            path = Path(path).resolve().relative_to(root.resolve()).as_posix()
        except ValueError:
            raise LockInputError(f"{path} is outside the repository")
    path = posixpath.normpath(path)
    while path.startswith("./"):
        path = path[2:]
    return path


def feature_dir_for(prefab: str) -> str:
    if "/Resources/" in prefab:
        return prefab.rsplit("/Resources/", 1)[0]
    return prefab.rsplit("/", 1)[0]


def session_dir(root: Path, prefab: str) -> Path:
    tag = hashlib.sha1(prefab.encode("utf-8")).hexdigest()[:8]  # same prefab name in two features
    return root / SESSION_ROOT / f"{Path(prefab).stem}-{tag}"


def read_state(sdir: Path):
    try:
        data = json.loads((sdir / STATE_FILE).read_text(encoding="utf-8"))
        return data if isinstance(data, dict) else None
    except (OSError, ValueError):  # missing, truncated, not UTF-8
        return None


def write_json_atomic(path: Path, data):
    path.parent.mkdir(parents=True, exist_ok=True)
    tmp = path.with_name(path.name + ".tmp")
    tmp.write_text(json.dumps(data, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    os.replace(tmp, path)


def write_state(sdir: Path, state: dict):
    write_json_atomic(sdir / STATE_FILE, state)


def now_iso() -> str:
    return datetime.now(timezone.utc).isoformat()


def archive_session(root: Path, sdir: Path) -> str:
    """Move an old session aside (never delete: it may hold the only copy of something)."""
    dst = root / SESSION_ROOT / "_archive" / f"{sdir.name}-{datetime.now(timezone.utc):%Y%m%dT%H%M%S%f}"
    dst.parent.mkdir(parents=True, exist_ok=True)
    shutil.move(str(sdir), str(dst))
    return dst.relative_to(root).as_posix()


def backup_targets(root: Path, prefab: str, feature: str) -> list:
    """The screen's own files — copied byte-exact at begin so revert can put them back."""
    targets = [prefab, prefab + ".meta", ".claude/docs/ArtStyle.md"]
    for sub in (("Scripts", "Controller"), ("Visuals",)):
        folder = root.joinpath(feature, *sub)
        if folder.is_dir():
            targets += [p.relative_to(root).as_posix() for p in sorted(folder.rglob("*")) if p.is_file()]
    return [t for t in targets if (root / t).is_file()]


def watched_roots(feature: str) -> tuple:
    return (feature, ".claude/docs/ArtStyle", "TechSpec/Mockups")


def watched_dirs(root: Path, feature: str) -> list:
    """Directories that existed at begin, so revert only removes folders Phase D created."""
    out = []
    for base in watched_roots(feature):
        folder = root / base
        if folder.is_dir():
            out.append(base)
            out += [p.relative_to(root).as_posix() for p in folder.rglob("*") if p.is_dir()]
    return sorted(set(out))


def classify(path: str, state: dict, templates_root: str, new: bool = False) -> str:
    prefab, feature = state["prefab"], state["feature"]
    rel = path.replace("\\", "/")
    base = rel[:-5] if rel.endswith(".meta") else rel
    if rel == prefab + ".meta":
        return "violation"  # assetBundleName lives there - UIManager stops finding the screen
    if rel == prefab or rel == ".claude/docs/ArtStyle.md":
        return "allowed"
    if base in (f"{feature}/Visuals", f"{feature}/Scripts", f"{feature}/Scripts/Controller"):
        return "allowed"  # folder .meta Phase D had to create
    if rel.startswith(f"{feature}/Scripts/Controller/"):
        return "allowed"
    if rel.startswith(f"{feature}/Visuals/"):
        return "allowed" if new else "violation"  # new art only - existing sprites/.meta stay as they are
    if rel.startswith(".claude/") and not rel.startswith((".claude/docs/ArtStyle", SESSION_PREFIX)):
        return "violation"  # the agent system itself is not Phase D's to edit
    if rel.startswith(".claude/docs/ArtStyle/"):
        sub = rel[len(".claude/docs/ArtStyle/"):]
        if sub.startswith("screens/pending/") or "/" not in sub:  # pending shots + regenerated boards
            return "allowed"
        return "violation"  # approved / rejected references belong to the dev
    if rel.startswith("TechSpec/Mockups/"):
        return "allowed" if rel.endswith(EVIDENCE_SUFFIXES) else "violation"  # the mockup is the contract
    if base.lower().endswith(CODE_SUFFIXES):
        return "violation"  # Phase D code lives in the feature's Scripts/Controller/
    if templates_root and rel.startswith(templates_root.rstrip("/") + "/"):
        return "violation"
    if base.lower().endswith(RESKIN_SUFFIXES):
        return "violation"
    return "warning"


def embeds_render_data(root: Path, rel: str) -> bool:
    """A .asset holding a Material (!u!21) or Texture2D (!u!28) - e.g. a TMP font asset - re-skins
    every text that uses it; a plain ScriptableObject does not."""
    if not rel.endswith(".asset"):
        return False
    try:
        with open(root / rel, "rb") as fh:
            head = fh.read(4 * 1024 * 1024)
    except OSError:
        return False
    return b"--- !u!21 " in head or b"--- !u!28 " in head


def classify_at(root: Path, path: str, state: dict, templates_root: str, new: bool = False) -> str:
    cls = classify(path, state, templates_root, new)
    if cls == "warning" and embeds_render_data(root, path):
        return "violation"
    return cls


def templates_root_of() -> str:
    try:
        sys.path.insert(0, str(Path(__file__).resolve().parent))
        from project_profile import profile  # noqa: E402
        return profile().ui_templates_root or ""
    except Exception:  # profile missing or unreadable: the suffix rule still covers templates
        return ""


def reconcile_head(root: Path, sdir: Path, state: dict) -> str:
    """'same' | 'moved' (commits since begin left this screen alone - session follows HEAD) |
    'touched' (commits since begin changed this screen's files - the session can't be trusted)."""
    head = head_commit(root)
    old = state.get("head")
    if old == head:
        return "same"
    touched = True
    if old and head:
        proc = git(root, "diff", "--name-only", "-z", old, head, check=False)
        if proc.returncode == 0:
            names = [n for n in proc.stdout.decode("utf-8", "replace").split("\0") if n]
            mine = set(state.get("backedUp", [])) | set(state.get("footprint", {})) | {
                state["prefab"], state["prefab"] + ".meta", ".claude/docs/ArtStyle.md"}
            touched = any(n in mine or n.startswith(state["feature"].rstrip("/") + "/") for n in names)
    if touched:
        return "touched"
    # The commits left this screen alone: whatever they committed is the new starting point, so a
    # revert can never put back an older version of someone's committed file.
    baseline_file = sdir / "baseline.json"
    baseline = json.loads(baseline_file.read_text(encoding="utf-8"))
    for path, blob in tree_ids(root, head, names).items():
        baseline["paths"][path] = {"status": "  ", "blob": blob, "stored": blob is not None}
    baseline["head"] = head
    write_json_atomic(baseline_file, baseline)  # baseline first: a kill in between leaves state on the old HEAD
    state["head"] = head
    write_state(sdir, state)
    return "moved"


def committed_clean(root: Path, rel: str) -> bool:
    """True when `rel` is in HEAD and the working tree matches it."""
    head = head_commit(root)
    if not head or tree_ids(root, head, [rel])[rel] is None:
        return False
    return git(root, "diff", "--quiet", "HEAD", "--", rel, check=False).returncode == 0


STALE_DETAIL = ("commits since `begin` changed this screen's files, so this Phase D session can no longer be "
                "checked or reverted safely. Do not start Phase D again on this screen in this task: record "
                "`designer-pass: failed (stale session)` and leave the screen as it is for the dev.")


def cmd_begin(prefab: str, feature: str | None, restart: bool, task: str | None = None) -> dict:
    root = repo_root()
    prefab = clean_rel(check_prefab_path(prefab))
    missing = []
    for rel, marker in REQUIRED_PIECES.items():
        text = (root / rel).read_text(encoding="utf-8", errors="replace") if (root / rel).is_file() else None
        if text is None or (marker and marker not in text):
            missing.append(rel)
    if missing:
        return {"ok": True, "action": "skip", "reason": "not installed: " + ", ".join(missing)}
    if not (root / prefab).is_file():
        raise LockInputError(f"prefab not found: {prefab}")
    feature = clean_rel(feature, root) if feature else feature_dir_for(prefab)
    sdir = session_dir(root, prefab)
    rel_dir = sdir.relative_to(root).as_posix()
    paths = {"dir": rel_dir, "lockBefore": f"{rel_dir}/lock-before.json",
             "lockAfter": f"{rel_dir}/lock-after.json", "captures": f"{rel_dir}/captures"}
    head = head_commit(root)
    prefab_sha = sha256_of(root / prefab)
    archived = None
    if sdir.exists():
        state = read_state(sdir)
        other_task = bool(task and state and state.get("task") != task)  # incl. an old session with no id
        rec = None if not state or state.get("prefab") != prefab or other_task else reconcile_head(root, sdir, state)
        if rec is None:
            archived = archive_session(root, sdir)  # unreadable / foreign / another task's: keep it, start clean
        elif rec == "touched" and restart:
            archived = archive_session(root, sdir)
        elif rec == "touched" and state.get("phase") == "in-progress":
            return {"ok": False, "action": "stale", **paths, "detail": STALE_DETAIL}
        elif rec == "touched":
            # done / reverted, and the screen was committed since: the same task (shipped or not) -
            # never run Phase D twice on one screen. Another task passes its own --task and starts fresh.
            return {"ok": True, "action": "already-done" if state.get("phase") == "done" else "already-reverted",
                    **paths, "detail": "Phase D already ran on this screen (" + state.get("phase", "") +
                    ") and its result was committed since. A later task passes --task <id> to start over."}
        else:
            phase = state.get("phase")
            if phase == "in-progress":
                if restart:
                    raise LockInputError("Phase D is in progress for this screen - `revert` (or `finish`) before --restart")
                return {"ok": True, "action": "resume", **paths,
                        "lockBeforeExists": (root / paths["lockBefore"]).is_file(),
                        "prefabUnchangedSinceBegin": prefab_sha == state.get("prefabSha"),
                        "detail": "Phase D started in an earlier session of this task - do NOT retake lock-before; "
                                  "continue at D6.1 (diff + scope first) to see where the screen stands"}
            if not restart and phase == "done":
                same = state.get("finishedPrefabSha") == prefab_sha
                return {"ok": True, "action": "already-done" if same else "done-modified", **paths,
                        "detail": "Phase D finished earlier in this task (HEAD unchanged since)" + ("" if same else
                                  "; later fix rounds edited the prefab since - treat Phase D as done") +
                                  ". --restart only after Phase C was rebuilt."}
            if not restart and phase == "reverted":
                return {"ok": True, "action": "already-reverted", **paths,
                        "detail": "Phase D was reverted earlier in this task - do not retry on your own; "
                                  "record `designer-pass: reverted`. --restart tries again on purpose."}
            archived = archive_session(root, sdir)
    state = {"phase": "in-progress", "prefab": prefab, "feature": feature, "head": head, "task": task,
             "prefabSha": prefab_sha, "startedAt": now_iso()}
    templates_root = templates_root_of()
    files = backup_targets(root, prefab, feature)
    for rel in files:
        dst = sdir / "backup" / rel
        dst.parent.mkdir(parents=True, exist_ok=True)
        shutil.copy2(root / rel, dst)
    (sdir / "captures").mkdir(parents=True, exist_ok=True)
    dirty = dirty_paths(root)
    keep = [p for p in dirty if classify_at(root, p, state, templates_root) in ("allowed", "violation")]
    ids = {**blob_ids(root, [p for p in dirty if p not in keep]), **blob_ids(root, keep, store=True)}
    baseline = {"head": head,
                "paths": {p: {"status": s, "blob": ids.get(p), "stored": p in keep and ids.get(p) is not None}
                          for p, s in dirty.items()},
                "dirs": watched_dirs(root, feature)}
    write_json_atomic(sdir / "baseline.json", baseline)
    state["backedUp"] = files
    write_state(sdir, state)
    out = {"ok": True, "action": "start", **paths, "feature": feature, "backedUp": files}
    if archived:
        out["archivedOldSession"] = archived
    if not (root / feature).is_dir():
        out["warning"] = f"feature folder {feature} does not exist - pass --feature-dir"
    return out


def load_session(prefab: str):
    root = repo_root()
    prefab = clean_rel(check_prefab_path(prefab))
    sdir = session_dir(root, prefab)
    state = read_state(sdir)
    if not state or state.get("prefab") != prefab:
        raise LockInputError(f"no Phase D session for {prefab} - run `begin` first")
    if reconcile_head(root, sdir, state) == "touched":
        raise LockInputError("stale Phase D session: " + STALE_DETAIL)
    try:
        baseline = json.loads((sdir / "baseline.json").read_text(encoding="utf-8"))
        baseline["paths"], baseline["head"]
    except (OSError, ValueError, KeyError, TypeError) as exc:
        raise LockInputError(f"Phase D session baseline unreadable ({exc}) - it can't be checked or reverted")
    return root, sdir, state, baseline


def changed_since(root: Path, baseline: dict) -> dict:
    """path -> {begin, now, new, cleanAtBegin} for every path whose content differs from begin."""
    known = baseline["paths"]
    candidates = set(dirty_paths(root)) | set(known)
    now = blob_ids(root, candidates)
    head = tree_ids(root, baseline.get("head"), [p for p in candidates if p not in known])
    changed = {}
    for path in sorted(candidates):
        begin = known[path]["blob"] if path in known else head.get(path)
        if begin != now[path]:
            changed[path] = {"begin": begin, "now": now[path], "new": begin is None,
                             "cleanAtBegin": path not in known}
    return changed


def cmd_scope(prefab: str) -> dict:
    root, _, state, baseline = load_session(prefab)
    templates_root = templates_root_of()
    result = {"allowed": [], "violations": [], "warnings": []}
    for path, info in changed_since(root, baseline).items():
        cls = classify_at(root, path, state, templates_root, new=info["new"])
        entry = {"path": path, "change": "added" if info["new"] else ("deleted" if info["now"] is None else "modified")}
        result["allowed" if cls == "allowed" else cls + "s"].append(entry)
    return {"ok": not result["violations"], **result,
            "detail": "warnings are usually editor auto-dirt (scenes/SOs re-synced on refresh): leave them out of the commit"}


def keep_copy(root: Path, sdir: Path, rel: str):
    """Save what a revert is about to overwrite or delete, so nothing is ever lost silently."""
    src = root / rel
    if src.is_file():
        dst = sdir / "discarded" / rel
        dst.parent.mkdir(parents=True, exist_ok=True)
        shutil.copy2(src, dst)


def restore_index(root: Path, rel: str, begin_status, head_has_it: bool):
    """Put the index entry back to how it was at begin (best effort; never touches the work tree)."""
    if begin_status is None or begin_status[0] in " ?":
        if head_has_it:
            git(root, "reset", "-q", "--", rel, check=False)
        else:
            git(root, "rm", "--cached", "-f", "-q", "--ignore-unmatch", "--", rel, check=False)
    else:
        git(root, "add", "--", rel, check=False)


def cmd_revert(prefab: str, discard_later_edits: bool = False) -> dict:
    root, sdir, state, baseline = load_session(prefab)
    if state.get("phase") == "reverted":
        return {"ok": True, "action": "already-reverted",
                "detail": "this Phase D was already reverted - nothing to do (a second revert would undo later work)"}
    templates_root = templates_root_of()
    known = baseline["paths"]
    head = baseline.get("head")
    backed = state.get("backedUp", [])
    changes = changed_since(root, baseline)
    scope = None
    if state.get("phase") == "done":
        footprint = state.get("footprint", {})
        scope = set(footprint)
        now = blob_ids(root, footprint)
        later = sorted(p for p, blob in footprint.items() if now[p] != blob)
        if later and not discard_later_edits:
            return {"ok": False, "refused": True, "editedAfterFinish": later,
                    "detail": "these Phase D files were edited after `finish` (a later fix round) - a revert would "
                              "throw that work away. Fix inside the gate instead, or re-run with "
                              "--discard-later-edits if every later edit was itself a Phase D fix attempt"}
    restored, deleted, git_restored, left, unrestorable = [], [], [], [], []
    in_head = tree_ids(root, head, backed)
    for rel in backed:
        if scope is not None and rel not in scope:
            continue
        src = sdir / "backup" / rel
        if src.is_file() and sha256_of(src) != sha256_of(root / rel):
            keep_copy(root, sdir, rel)
            (root / rel).parent.mkdir(parents=True, exist_ok=True)
            shutil.copy2(src, root / rel)
            restore_index(root, rel, known.get(rel, {}).get("status"), in_head.get(rel) is not None)
            restored.append(rel)
    for path, info in changes.items():
        if scope is not None and path not in scope:
            left.append(path)  # changed after Phase D finished (or never Phase D's): not ours to undo
            continue
        if path in backed:
            continue
        cls = classify_at(root, path, state, templates_root, new=info["new"])
        if cls not in ("allowed", "violation"):
            left.append(path)
            continue
        if info["new"]:
            keep_copy(root, sdir, path)
            git(root, "rm", "--cached", "-f", "-q", "--ignore-unmatch", "--", path, check=False)
            if (root / path).is_file():
                (root / path).unlink()
            deleted.append(path)
        elif info["cleanAtBegin"] and head:
            keep_copy(root, sdir, path)
            proc = git(root, "checkout", head, "--", path, check=False)  # work tree + index = begin
            (git_restored if proc.returncode == 0 else unrestorable).append(path)
        elif known.get(path, {}).get("stored"):
            keep_copy(root, sdir, path)
            data = git(root, "cat-file", "blob", known[path]["blob"]).stdout
            (root / path).parent.mkdir(parents=True, exist_ok=True)
            (root / path).write_bytes(data)
            restore_index(root, path, known[path]["status"], tree_ids(root, head, [path])[path] is not None)
            git_restored.append(path)
        else:
            unrestorable.append(path)
    begin_dirs = set(baseline.get("dirs", []))
    roots = tuple(r.rstrip("/") + "/" for r in watched_roots(state["feature"]))
    for path in deleted:
        parent = (root / path).parent
        while parent != root and parent.is_dir() and not any(parent.iterdir()):
            rel = parent.relative_to(root).as_posix()
            if rel in begin_dirs or not (rel + "/").startswith(roots):
                break
            parent.rmdir()
            meta = parent.with_name(parent.name + ".meta")
            meta_rel = meta.relative_to(root).as_posix()
            if meta.is_file() and meta_rel not in known and tree_ids(root, head, [meta_rel])[meta_rel] is None:
                git(root, "rm", "--cached", "-f", "-q", "--ignore-unmatch", "--", meta_rel, check=False)
                meta.unlink()
            parent = parent.parent
    prefab_ok = sha256_of(root / state["prefab"]) == state["prefabSha"]
    state.update({"phase": "reverted", "revertedAt": now_iso()})
    write_state(sdir, state)
    return {"ok": prefab_ok and not unrestorable, "prefabRestored": prefab_ok, "restored": restored,
            "deleted": deleted, "gitRestored": git_restored, "unrestorable": unrestorable, "leftInPlace": left,
            "discardedCopies": (sdir / "discarded").relative_to(root).as_posix(),
            "detail": "refresh Unity (AssetDatabase.Refresh), compile once, and regenerate the new-ui-guide §5 "
                      "evidence on the Phase C screen; leftInPlace = not Phase D's to undo (auto-dirt); "
                      "unrestorable = put back by hand from git / discarded/ and say so in the report"}


def cmd_finish(prefab: str) -> dict:
    root, sdir, state, baseline = load_session(prefab)
    if state.get("phase") == "done":
        return {"ok": True, "action": "already-done", "phase": "done",
                "detail": "already finished - the footprint recorded the first time is kept"}
    if state.get("phase") == "reverted":
        raise LockInputError("this Phase D was reverted - there is nothing to finish")
    templates_root = templates_root_of()
    footprint_paths = sorted(p for p, info in changed_since(root, baseline).items()
                             if classify_at(root, p, state, templates_root, new=info["new"]) in ("allowed", "violation"))
    state.update({"phase": "done", "finishedPrefabSha": sha256_of(root / state["prefab"]),
                  "footprint": blob_ids(root, footprint_paths, store=True), "finishedAt": now_iso()})
    write_state(sdir, state)
    return {"ok": True, "phase": "done", "dir": sdir.relative_to(root).as_posix(),
            "footprint": len(footprint_paths),
            "detail": "a later gate can still `revert` this Phase D, as long as its files were not edited since"}


# -------------------------------------------------------------------------------------- cli


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    sub = parser.add_subparsers(dest="command", required=True)
    beg = sub.add_parser("begin", help="start or resume the Phase D session of a screen")
    beg.add_argument("--prefab", required=True)
    beg.add_argument("--feature-dir", help="defaults to the folder above the prefab's Resources/")
    beg.add_argument("--task", help="the backlog task id (NNN) - a different task's old session is archived, not reused")
    beg.add_argument("--restart", action="store_true",
                     help="start over even if this screen already has a done/reverted session")
    snip = sub.add_parser("snippet", help="print the C# snapshot body for unity_execute_code")
    snip.add_argument("--prefab", required=True, help="project-relative prefab asset path")
    snip.add_argument("--out", required=True, type=Path, help="where Unity writes the snapshot JSON")
    check = sub.add_parser("diff", help="check a Phase D snapshot against the Phase C one")
    check.add_argument("before", type=Path)
    check.add_argument("after", type=Path)
    check.add_argument("--output", type=Path, help="also write the report JSON here")
    for name, text in (("scope", "files changed since begin, classified"),
                       ("revert", "undo every Phase D change of this screen"),
                       ("finish", "mark the Phase D session done")):
        sp = sub.add_parser(name, help=text)
        sp.add_argument("--prefab", required=True)
        if name == "revert":
            sp.add_argument("--discard-later-edits", action="store_true",
                            help="after finish: revert even files edited since (they are copied to discarded/)")
    args = parser.parse_args()
    try:
        if args.command == "snippet":
            sys.stdout.write(build_snippet(args.prefab, args.out))
            return 0
        if args.command == "diff":
            report = diff(load_snapshot(args.before), load_snapshot(args.after))
        elif args.command == "begin":
            report = cmd_begin(args.prefab, args.feature_dir, args.restart, args.task)
        elif args.command == "scope":
            report = cmd_scope(args.prefab)
        elif args.command == "revert":
            report = cmd_revert(args.prefab, args.discard_later_edits)
        else:
            report = cmd_finish(args.prefab)
    except Exception as exc:  # always answer in JSON - an agent reads this, not a human
        print(json.dumps({"ok": False, "error": f"{type(exc).__name__}: {exc}"}, ensure_ascii=False, indent=2))
        return 2
    text = json.dumps(report, ensure_ascii=False, indent=2)
    if getattr(args, "output", None):
        args.output.parent.mkdir(parents=True, exist_ok=True)
        args.output.write_text(text + "\n", encoding="utf-8")
    print(text)
    return 0 if report.get("ok") else 1


if __name__ == "__main__":
    sys.exit(main())
