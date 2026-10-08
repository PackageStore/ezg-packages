"""Tests for ui-layout-lock.py — the overlay-only gate + session of the /new-ui designer pass (Phase D)."""

import copy
import json
import shutil
import subprocess
import sys
import tempfile
import unittest
from pathlib import Path


REPO = Path(__file__).resolve().parents[3]
LOCK = REPO / ".claude" / "scripts" / "ui-layout-lock.py"
SIZES = ("1080x1920", "1080x2400", "1536x2048")
BUILTIN_SPRITE = "r:asset:0000000000000000f000000000000000:10907"
KIT_SPRITE = "r:asset:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa:21300000"
OTHER_SPRITE = "r:asset:bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb:21300000"


def rect(x0, y0, x1, y1):
    return {s: [x0, y0, x1, y1] for s in SIZES}


def rect_transform(cid, pos="v:0,0", size="v:100,100"):
    return {"id": cid, "type": "UnityEngine.RectTransform", "kinds": "",
            "props": {"m_LocalRotation": "v:0,0,0,1", "m_LocalPosition": "v:0,0,0", "m_LocalScale": "v:1,1,1",
                      "m_AnchorMin": "v:0.5,0.5", "m_AnchorMax": "v:0.5,0.5", "m_Pivot": "v:0.5,0.5",
                      "m_AnchoredPosition": pos, "m_SizeDelta": size}}


def image(cid, sprite="r:null", color="v:1,1,1,1", raycast="b:1"):
    return {"id": cid, "type": "UnityEngine.UI.Image", "kinds": "graphic",
            "props": {"m_Enabled": "b:1", "m_Sprite": sprite, "m_Color": color,
                      "m_RaycastTarget": raycast, "m_PreserveAspect": "b:0", "m_Type": "i:0"}}


def node(nid, path, parent, order, comps, rects=None, active=True, nested=False):
    return {"id": nid, "path": path, "parent": parent, "order": order, "name": path.rsplit("/", 1)[-1],
            "active": active, "layer": 5, "tag": "Untagged", "nested": nested,
            "rects": rects if rects is not None else rect(0, 0, 100, 100), "components": comps}


def base_snapshot():
    """screen root
         ├─ bg_button                 (root child 0: background + tap-to-close)
         └─ popup                     (root child 1: template layout node)
              ├─ frame                (plain Image frame — a legal overlay parent)
              ├─ container            (vertical layout group)
              │    ├─ title           (text + localize)
              │    ├─ icon            (empty visible image slot)
              │    └─ button          (kit sprite + Button)
              └─ scroll               (ScrollRect)
                   └─ viewport (mask) └─ content
    """
    return {
        "lockVersion": 1, "prefab": "Assets/_Project/Features/Meta/Test/Resources/screen_test.prefab",
        "guid": "cccccccccccccccccccccccccccccccc", "unity": "6000.3.16f1",
        "nodes": [
            node("1", "screen_test", "", 0, [
                rect_transform("11", size="v:1080,1920"),
                {"id": "12", "type": "Game.ScreenTestController", "kinds": "",
                 "props": {"m_Enabled": "b:1", "_closeButtons.Array.size": "i:1",
                           "_closeButtons.Array.data[0]": "r:local:41"}},
            ], rects=rect(-540, -960, 540, 960)),
            node("7", "screen_test/bg_button", "1", 0, [rect_transform("71"), image("72", color="v:0,0,0,0.85")],
                 rects=rect(-540, -960, 540, 960)),
            node("6", "screen_test/popup", "1", 1, [rect_transform("61")], rects=rect(-540, -960, 540, 960)),
            node("8", "screen_test/popup/frame", "6", 0, [rect_transform("81"), image("82", sprite=KIT_SPRITE)],
                 rects=rect(-450, -700, 450, 700)),
            node("2", "screen_test/popup/container", "6", 1, [
                rect_transform("21", size="v:800,1200"),
                {"id": "22", "type": "UnityEngine.UI.VerticalLayoutGroup", "kinds": "layoutController,layoutGroup",
                 "props": {"m_Enabled": "b:1", "m_Spacing": "f:20", "m_ChildControlWidth": "b:1"}},
            ], rects=rect(-400, -600, 400, 600)),
            node("3", "screen_test/popup/container/title", "2", 0, [
                rect_transform("31"),
                {"id": "32", "type": "UnityEngine.UI.Text", "kinds": "graphic,text",
                 "props": {"m_Text": "s:Title", "m_FontData.m_FontSize": "i:64", "m_RaycastTarget": "b:0"}},
                {"id": "33", "type": "Game.LocalizeHelper", "kinds": "localize",
                 "props": {"m_Enabled": "b:1", "Key": "s:test_title"}},
            ], rects=rect(-400, 500, 400, 600)),
            node("4", "screen_test/popup/container/icon", "2", 1, [rect_transform("34"), image("35")],
                 rects=rect(-50, 300, 50, 400)),
            node("5", "screen_test/popup/container/button", "2", 2, [
                rect_transform("41"),
                image("42", sprite=KIT_SPRITE),
                {"id": "43", "type": "UnityEngine.UI.Button", "kinds": "selectable",
                 "props": {"m_Enabled": "b:1", "m_Interactable": "b:1"}},
            ], rects=rect(-150, -600, 150, -480)),
            node("9", "screen_test/popup/scroll", "6", 2, [
                rect_transform("91"),
                {"id": "92", "type": "UnityEngine.UI.ScrollRect", "kinds": "layoutController,scroll",
                 "props": {"m_Enabled": "b:1", "m_Content": "r:local:A1", "m_Viewport": "r:local:B1"}},
            ], rects=rect(-400, -900, 400, -700)),
            node("B", "screen_test/popup/scroll/viewport", "9", 0, [
                rect_transform("B1"), image("B2", color="v:1,1,1,1"),
                {"id": "B3", "type": "UnityEngine.UI.Mask", "kinds": "mask", "props": {"m_Enabled": "b:1"}},
            ], rects=rect(-400, -900, 400, -700)),
            node("A", "screen_test/popup/scroll/viewport/content", "B", 0, [rect_transform("A1")],
                 rects=rect(-400, -900, 400, -700)),
        ],
    }


def find(snap, nid):
    return next(n for n in snap["nodes"] if n["id"] == nid)


def fx_node(nid, parent, order, raycast="b:0", extra=None, name=None):
    comps = [rect_transform(nid + "1"), image(nid + "2", sprite=KIT_SPRITE, raycast=raycast)]
    comps.extend(extra or [])
    return node(nid, f"screen_test/fx/{name or 'fx_' + nid}", parent, order, comps, rects=rect(-300, 200, 300, 700))


class LockDiffTests(unittest.TestCase):
    def setUp(self):
        self.tmp = tempfile.TemporaryDirectory()
        self.dir = Path(self.tmp.name)

    def tearDown(self):
        self.tmp.cleanup()

    def run_diff(self, before, after):
        b, a = self.dir / "before.json", self.dir / "after.json"
        b.write_text(json.dumps(before), encoding="utf-8")
        a.write_text(json.dumps(after), encoding="utf-8")
        out = self.dir / "report.json"
        proc = subprocess.run([sys.executable, str(LOCK), "diff", str(b), str(a), "--output", str(out)],
                              capture_output=True, text=True)
        return proc.returncode, json.loads(proc.stdout)

    def rules(self, report):
        return {v["rule"] for v in report.get("violations", [])}

    def warn_rules(self, report):
        return {w["rule"] for w in report.get("warnings", [])}

    def assertPass(self, after, before=None):
        code, report = self.run_diff(before or base_snapshot(), after)
        self.assertEqual(code, 0, report)
        self.assertTrue(report["ok"])
        return report

    def assertViolation(self, after, rule, before=None):
        code, report = self.run_diff(before or base_snapshot(), after)
        self.assertEqual(code, 1, report)
        self.assertFalse(report["ok"])
        self.assertIn(rule, self.rules(report), report["violations"])
        return report

    # ---------------------------------------------------------------- passes

    def test_identical_snapshots_pass(self):
        report = self.assertPass(base_snapshot())
        self.assertEqual(report["counts"]["violations"], 0)
        self.assertTrue((self.dir / "report.json").exists())

    def test_overlay_under_a_plain_visual_node_passes(self):
        after = base_snapshot()
        after["nodes"].append(fx_node("90", "8", 0))                       # behind frame's children
        after["nodes"].append(fx_node("91", "4", 0))                       # inside the icon cell
        child = fx_node("92", "90", 0)                                     # subtree of a new node
        after["nodes"].append(child)
        report = self.assertPass(after)
        self.assertEqual(len(report["addedNodes"]), 3)

    def test_art_fill_on_empty_visible_slot_passes(self):
        after = base_snapshot()
        img = find(after, "4")["components"][1]
        img["props"].update({"m_Sprite": KIT_SPRITE, "m_Color": "v:1,0.9,0.8,1", "m_PreserveAspect": "b:1"})
        report = self.assertPass(after)
        self.assertEqual(len(report["artFills"]), 1)

    def test_art_fill_on_builtin_placeholder_passes(self):
        before = base_snapshot()
        find(before, "4")["components"][1]["props"]["m_Sprite"] = BUILTIN_SPRITE
        after = copy.deepcopy(before)
        find(after, "4")["components"][1]["props"]["m_Sprite"] = KIT_SPRITE
        self.assertPass(after, before=before)

    def test_art_fill_inside_template_instance_is_a_warning(self):
        before = base_snapshot()
        find(before, "4")["nested"] = True
        after = copy.deepcopy(before)
        find(after, "4")["components"][1]["props"]["m_Sprite"] = KIT_SPRITE
        report = self.assertPass(after, before=before)
        self.assertIn("art-fill-inside-template-instance", self.warn_rules(report))

    def test_art_fill_on_slot_code_references_is_a_warning(self):
        before = base_snapshot()
        find(before, "1")["components"][1]["props"]["_icon"] = "r:local:35"
        after = copy.deepcopy(before)
        find(after, "4")["components"][1]["props"]["m_Sprite"] = KIT_SPRITE
        report = self.assertPass(after, before=before)
        self.assertIn("art-fill-on-referenced-slot", self.warn_rules(report))

    def test_float_noise_within_tolerance_passes(self):
        after = base_snapshot()
        find(after, "3")["components"][0]["props"]["m_AnchoredPosition"] = "v:0.004,-0.003"
        find(after, "3")["rects"] = rect(-400.2, 500.1, 400.3, 600)
        self.assertPass(after)

    def test_stale_layout_driven_values_are_only_warnings(self):
        after = base_snapshot()
        props = find(after, "3")["components"][0]["props"]
        props["m_SizeDelta"] = "v:800,100"
        props["m_AnchorMin"] = "v:0,1"
        props["m_LocalPosition"] = "v:12,-40,0"
        report = self.assertPass(after)
        self.assertIn("rect-serialized-drift", self.warn_rules(report))

    def test_canvas_group_at_rest_on_existing_node_passes(self):
        after = base_snapshot()
        find(after, "2")["components"].append({
            "id": "29", "type": "UnityEngine.CanvasGroup", "kinds": "canvasGroup",
            "props": {"m_Alpha": "f:1", "m_Interactable": "b:1", "m_BlocksRaycasts": "b:1", "m_IgnoreParentGroups": "b:0"}})
        self.assertPass(after)

    def test_idle_fx_behaviour_on_existing_node_is_a_warning(self):
        after = base_snapshot()
        find(after, "5")["components"].append({"id": "49", "type": "Game.UIFxPulse", "kinds": "", "props": {"m_Enabled": "b:1"}})
        report = self.assertPass(after)
        self.assertIn("behaviour-added-to-existing-node", self.warn_rules(report))

    def test_new_controller_field_is_a_warning(self):
        after = base_snapshot()
        find(after, "1")["components"][1]["props"]["_bannerRays"] = "r:local:90"
        report = self.assertPass(after)
        self.assertIn("serialized-field-added", self.warn_rules(report))

    def test_mesh_effect_on_overlay_node_passes(self):
        after = base_snapshot()
        extra = {"id": "908", "type": "UnityEngine.UI.Shadow", "kinds": "meshEffect", "props": {"m_Enabled": "b:1"}}
        after["nodes"].append(fx_node("90", "8", 0, extra=[extra]))
        self.assertPass(after)

    # ------------------------------------------------- overlay placement

    def test_overlay_under_prefab_root_fails(self):
        # would shift root child indices: FeatureBaseController reads GetChild(0)/GetChild(1)
        after = base_snapshot()
        after["nodes"].append(fx_node("90", "1", 0))
        for n in after["nodes"]:
            if n["parent"] == "1" and n["id"] != "90":
                n["order"] += 1
        self.assertViolation(after, "overlay-parent-not-allowed")

    def test_overlay_under_template_layout_node_fails(self):
        after = base_snapshot()
        after["nodes"].append(fx_node("90", "6", 0))
        self.assertViolation(after, "overlay-parent-not-allowed")

    def test_overlay_in_layout_group_needs_ignore_layout_and_is_flagged(self):
        ignore = {"id": "903", "type": "UnityEngine.UI.LayoutElement", "kinds": "layoutElement",
                  "props": {"m_Enabled": "b:1", "m_IgnoreLayout": "b:1"}}
        after = base_snapshot()
        after["nodes"].append(fx_node("90", "2", 0, extra=[ignore]))
        for n in after["nodes"]:
            if n["parent"] == "2" and n["id"] != "90":
                n["order"] += 1
        report = self.assertPass(after)
        self.assertIn("overlay-in-layout-group", self.warn_rules(report))
        after = base_snapshot()
        after["nodes"].append(fx_node("90", "2", 3))
        self.assertViolation(after, "overlay-joins-layout-flow")

    def test_overlay_in_scroll_viewport_or_content_fails(self):
        for parent in ("A", "B"):
            after = base_snapshot()
            after["nodes"].append(fx_node("90", parent, 1))
            self.assertViolation(after, "overlay-parent-not-allowed")

    def test_overlay_that_eats_touches_fails(self):
        after = base_snapshot()
        after["nodes"].append(fx_node("90", "8", 0, raycast="b:1"))
        self.assertViolation(after, "overlay-eats-touches")

    def test_overlay_with_button_text_or_localize_fails(self):
        for kinds, ctype in (("selectable", "UnityEngine.UI.Button"),
                             ("graphic,text", "TMPro.TextMeshProUGUI"),
                             ("localize", "Game.LocalizeHelper")):
            after = base_snapshot()
            extra = {"id": "909", "type": ctype, "kinds": kinds, "props": {"m_RaycastTarget": "b:0"}}
            after["nodes"].append(fx_node("90", "8", 0, extra=[extra]))
            self.assertViolation(after, "overlay-component-denied")

    # ------------------------------------------------- existing nodes

    def test_moved_node_fails(self):
        after = base_snapshot()
        find(after, "5")["components"][0]["props"]["m_AnchoredPosition"] = "v:0,40"
        find(after, "5")["rects"] = rect(-150, -560, 150, -440)
        report = self.assertViolation(after, "node-rect-changed")
        self.assertIn("property-changed", self.rules(report))

    def test_rect_shift_without_own_change_fails(self):
        after = base_snapshot()
        find(after, "5")["rects"]["1080x2400"] = [-150, -640, 150, -520]
        self.assertViolation(after, "node-rect-changed")

    def test_pivot_change_fails_even_with_same_rect(self):
        after = base_snapshot()
        props = find(after, "8")["components"][0]["props"]
        props["m_Pivot"] = "v:0,0"
        props["m_AnchoredPosition"] = "v:-450,-700"
        self.assertViolation(after, "property-changed")

    def test_local_z_change_fails(self):
        after = base_snapshot()
        find(after, "8")["components"][0]["props"]["m_LocalPosition"] = "v:0,0,-5760"
        self.assertViolation(after, "property-changed")

    def test_serialized_scale_change_fails(self):
        after = base_snapshot()
        find(after, "5")["components"][0]["props"]["m_LocalScale"] = "v:0.9,0.9,1"
        find(after, "5")["rects"] = rect(-135, -594, 135, -486)
        self.assertViolation(after, "node-rect-changed")

    def test_layout_group_spacing_change_fails(self):
        after = base_snapshot()
        find(after, "2")["components"][1]["props"]["m_Spacing"] = "f:40"
        self.assertViolation(after, "property-changed")

    def test_text_and_font_size_changes_fail(self):
        after = base_snapshot()
        find(after, "3")["components"][1]["props"]["m_FontData.m_FontSize"] = "i:72"
        self.assertViolation(after, "property-changed")
        after = base_snapshot()
        find(after, "3")["components"][1]["props"]["m_Text"] = "s:New title"
        self.assertViolation(after, "property-changed")

    def test_localize_key_change_fails(self):
        after = base_snapshot()
        find(after, "3")["components"][2]["props"]["Key"] = "s:other_key"
        self.assertViolation(after, "property-changed")

    def test_tint_of_existing_art_fails(self):
        after = base_snapshot()
        find(after, "5")["components"][1]["props"]["m_Color"] = "v:1,0.5,0.5,1"
        self.assertViolation(after, "property-changed")

    def test_replacing_existing_art_fails(self):
        after = base_snapshot()
        find(after, "5")["components"][1]["props"]["m_Sprite"] = OTHER_SPRITE
        self.assertViolation(after, "existing-art-replaced")

    def test_art_fill_on_transparent_hit_area_fails(self):
        before = base_snapshot()
        find(before, "4")["components"][1]["props"]["m_Color"] = "v:1,1,1,0"
        after = copy.deepcopy(before)
        find(after, "4")["components"][1]["props"].update({"m_Sprite": KIT_SPRITE, "m_Color": "v:1,1,1,1"})
        self.assertViolation(after, "art-fill-not-a-slot", before=before)

    def test_art_fill_on_button_graphic_fails(self):
        before = base_snapshot()
        find(before, "5")["components"][1]["props"]["m_Sprite"] = "r:null"
        after = copy.deepcopy(before)
        find(after, "5")["components"][1]["props"]["m_Sprite"] = KIT_SPRITE
        self.assertViolation(after, "art-fill-not-a-slot", before=before)

    def test_art_fill_on_mask_graphic_fails(self):
        after = base_snapshot()
        find(after, "B")["components"][1]["props"]["m_Sprite"] = KIT_SPRITE
        self.assertViolation(after, "art-fill-not-a-slot")

    def test_removed_node_fails(self):
        after = base_snapshot()
        after["nodes"] = [n for n in after["nodes"] if n["id"] != "4"]
        self.assertViolation(after, "node-removed")

    def test_activation_changes_fail(self):
        for value in (False,):
            after = base_snapshot()
            find(after, "4")["active"] = value
            self.assertViolation(after, "node-active-changed")
        before = base_snapshot()
        find(before, "4")["active"] = False
        self.assertViolation(base_snapshot(), "node-active-changed", before=before)

    def test_reparented_node_fails(self):
        after = base_snapshot()
        find(after, "4")["parent"] = "8"
        self.assertViolation(after, "node-reparented")

    def test_reordered_siblings_fail(self):
        after = base_snapshot()
        find(after, "3")["order"], find(after, "4")["order"] = 1, 0
        self.assertViolation(after, "siblings-reordered")

    def test_removed_component_fails(self):
        after = base_snapshot()
        find(after, "3")["components"] = find(after, "3")["components"][:2]
        self.assertViolation(after, "component-removed")

    def test_controller_reference_rewired_fails(self):
        after = base_snapshot()
        find(after, "1")["components"][1]["props"]["_closeButtons.Array.data[0]"] = "r:local:99"
        self.assertViolation(after, "property-changed")

    def test_graphic_added_to_existing_node_fails(self):
        after = base_snapshot()
        find(after, "2")["components"].append(image("29", sprite=KIT_SPRITE, raycast="b:0"))
        self.assertViolation(after, "component-added-to-existing-node")

    def test_layout_element_added_to_existing_node_fails(self):
        after = base_snapshot()
        find(after, "4")["components"].append({"id": "39", "type": "UnityEngine.UI.LayoutElement",
                                               "kinds": "layoutElement", "props": {"m_IgnoreLayout": "b:1"}})
        self.assertViolation(after, "component-added-to-existing-node")

    def test_mesh_effect_added_to_existing_node_fails(self):
        after = base_snapshot()
        find(after, "5")["components"].append({"id": "48", "type": "UnityEngine.UI.Outline",
                                               "kinds": "meshEffect", "props": {"m_Enabled": "b:1"}})
        self.assertViolation(after, "component-added-to-existing-node")

    def test_canvas_group_not_at_rest_fails(self):
        after = base_snapshot()
        find(after, "2")["components"].append({
            "id": "29", "type": "UnityEngine.CanvasGroup", "kinds": "canvasGroup",
            "props": {"m_Alpha": "f:0", "m_Interactable": "b:1", "m_BlocksRaycasts": "b:1", "m_IgnoreParentGroups": "b:0"}})
        self.assertViolation(after, "canvas-group-not-at-rest")

    # ---------------------------------------------------------------- inputs

    def test_different_prefabs_are_unusable_input(self):
        after = base_snapshot()
        after["guid"] = "dddddddddddddddddddddddddddddddd"
        code, report = self.run_diff(base_snapshot(), after)
        self.assertEqual(code, 2)
        self.assertFalse(report["ok"])

    def test_wrong_lock_version_is_unusable_input(self):
        after = base_snapshot()
        after["lockVersion"] = 99
        code, _ = self.run_diff(base_snapshot(), after)
        self.assertEqual(code, 2)

    def test_path_identity_fallback_still_detects_moves(self):
        before, after = base_snapshot(), base_snapshot()
        for snap in (before, after):
            find(snap, "4")["id"] = "0"
        next(n for n in after["nodes"] if n["path"].endswith("/button"))["rects"] = rect(0, 0, 10, 10)
        code, report = self.run_diff(before, after)
        self.assertEqual(code, 1, report)
        self.assertEqual(report["identity"], "path")
        self.assertIn("identity-fallback", self.warn_rules(report))

    # --------------------------------------------------------------- snippet

    def snippet(self, prefab, out):
        return subprocess.run([sys.executable, str(LOCK), "snippet", "--prefab", prefab, "--out", str(out)],
                              capture_output=True, text=True)

    def test_snippet_substitutes_and_escapes_paths(self):
        out = self.dir / 'snap "a".json'
        proc = self.snippet('Assets/_Project/Features/X/Resources/screen_x.prefab', out)
        self.assertEqual(proc.returncode, 0, proc.stdout)
        body = proc.stdout
        self.assertNotIn("__PREFAB__", body)
        self.assertNotIn("__OUT__", body)
        self.assertIn('"Assets/_Project/Features/X/Resources/screen_x.prefab"', body)
        self.assertIn('snap \\"a\\".json', body)
        self.assertEqual(body.count("{"), body.count("}"))
        self.assertEqual(body.count("("), body.count(")"))
        # Read-only contract: the snippet must never write to the prefab asset or a scene.
        code = "\n".join(l for l in body.splitlines() if not l.lstrip().startswith("//"))
        for forbidden in ("SaveAsPrefabAsset", "LoadPrefabContents", "SaveScene", "SetDirty", "AssetDatabase.SaveAssets"):
            self.assertNotIn(forbidden, code)
        self.assertIn("ClosePreviewScene", body)

    def test_snippet_rejects_non_prefab_paths(self):
        for bad in ("Assets/x.unity", "/abs/path/x.prefab", "x.prefab"):
            proc = self.snippet(bad, self.dir / "o.json")
            self.assertEqual(proc.returncode, 2, bad)


PREFAB = "Assets/_Project/Features/Meta/Test/Resources/screen_test.prefab"
CONTROLLER = "Assets/_Project/Features/Meta/Test/Scripts/Controller/ScreenTestController.cs"
SHARED_MAT = "Assets/_Project/Visual/Shared/ui_glow.mat"
SCENE = "Assets/_Project/Scenes/HomeScene.unity"
TEMPLATE = "Assets/_Project/Visual/ArtAsset/Shared/Resources/Prefabs/Templates/Frame_Template/frame.prefab"


class LockSessionTests(unittest.TestCase):
    """begin / scope / revert / finish in a throwaway git repo."""

    def setUp(self):
        self.tmp = tempfile.TemporaryDirectory()
        self.root = Path(self.tmp.name)
        self.git("init", "-q")
        self.git("config", "user.email", "t@example.com")
        self.git("config", "user.name", "t")
        (self.root / ".gitignore").write_text(".claude/tmp/\n", encoding="utf-8")
        for rel, text in ((".claude/docs/ui-designer-pass.md", "phase D doc"),
                          (".claude/agents/ui-visual-reviewer.md", "**Phase D (designer pass — overlay only"),
                          (".claude/docs/ArtStyle.md", "art style v1"),
                          (SHARED_MAT, "mat v1"), (SCENE, "scene v1"), (TEMPLATE, "template v1")):
            self.write(rel, text)
        self.git("add", "-A")
        self.git("commit", "-qm", "base")
        # Phase C output: new, uncommitted files (the backlog has not committed them yet)
        self.write(PREFAB, "prefab phase C")
        self.write(PREFAB + ".meta", "assetBundleName: features__test")
        self.write(CONTROLLER, "class ScreenTestController {}")

    def tearDown(self):
        self.tmp.cleanup()

    def git(self, *args):
        subprocess.run(["git", *args], cwd=self.root, check=True, capture_output=True)

    def write(self, rel, text):
        p = self.root / rel
        p.parent.mkdir(parents=True, exist_ok=True)
        p.write_text(text, encoding="utf-8")

    def read(self, rel):
        return (self.root / rel).read_text(encoding="utf-8")

    def lock(self, *args):
        proc = subprocess.run([sys.executable, str(LOCK), *args, "--prefab", PREFAB],
                              cwd=self.root, capture_output=True, text=True)
        return proc.returncode, json.loads(proc.stdout)

    def test_begin_creates_session_with_backups(self):
        code, out = self.lock("begin")
        self.assertEqual(code, 0, out)
        self.assertEqual(out["action"], "start")
        self.assertEqual(out["feature"], "Assets/_Project/Features/Meta/Test")
        for rel in (PREFAB, CONTROLLER, ".claude/docs/ArtStyle.md"):
            self.assertIn(rel, out["backedUp"])
        sdir = self.root / out["dir"]
        self.assertTrue((sdir / "backup" / PREFAB).is_file())
        self.assertTrue((sdir / "baseline.json").is_file())
        self.assertEqual(out["lockBefore"], out["dir"] + "/lock-before.json")
        # the session is gitignored: `git add -A` never sweeps it
        status = subprocess.run(["git", "status", "--porcelain", "--untracked-files=all"], cwd=self.root,
                                capture_output=True, text=True).stdout
        self.assertNotIn(".claude/tmp", status)

    def test_second_begin_resumes_instead_of_overwriting(self):
        self.lock("begin")
        self.write(PREFAB, "prefab half polished")
        code, out = self.lock("begin")
        self.assertEqual(out["action"], "resume", out)
        sdir = self.root / out["dir"]
        self.assertEqual((sdir / "backup" / PREFAB).read_text(encoding="utf-8"), "prefab phase C")

    def test_skip_when_pieces_are_not_installed(self):
        self.write(".claude/agents/ui-visual-reviewer.md", "phases A/B/C only")
        code, out = self.lock("begin")
        self.assertEqual(code, 0, out)
        self.assertEqual(out["action"], "skip")
        self.assertIn("ui-visual-reviewer.md", out["reason"])
        self.assertFalse((self.root / ".claude/tmp").exists())

    def test_scope_classifies_changes(self):
        self.lock("begin")
        self.write(PREFAB, "prefab polished")
        self.write(CONTROLLER, "class ScreenTestController { void AnimOpenUI() {} }")
        self.write("Assets/_Project/Features/Meta/Test/Scripts/Controller/TestMotion.cs", "static class TestMotion {}")
        self.write("Assets/_Project/Features/Meta/Test/Visuals/test_fx_glow.png", "png")
        self.write(".claude/docs/ArtStyle.md", "art style v2")
        self.write(".claude/docs/ArtStyle/screens/pending/Test.png", "png")
        self.write("TechSpec/Mockups/Meta/Test.ui-layout-lock.json", "{}")
        self.write(SCENE, "scene auto-dirt")
        code, out = self.lock("scope")
        self.assertEqual(code, 0, out)
        allowed = {e["path"] for e in out["allowed"]}
        self.assertTrue({PREFAB, CONTROLLER, ".claude/docs/ArtStyle.md",
                         "Assets/_Project/Features/Meta/Test/Visuals/test_fx_glow.png"} <= allowed, allowed)
        self.assertEqual([e["path"] for e in out["warnings"]], [SCENE])
        # re-skinning shared art / templates / the prefab's .meta is a violation
        self.write(SHARED_MAT, "mat v2")
        self.write(TEMPLATE, "template v2")
        self.write(PREFAB + ".meta", "assetBundleName: none")
        code, out = self.lock("scope")
        self.assertEqual(code, 1, out)
        self.assertEqual({e["path"] for e in out["violations"]}, {SHARED_MAT, TEMPLATE, PREFAB + ".meta"})

    def test_revert_restores_phase_c_exactly(self):
        self.lock("begin")
        visuals = "Assets/_Project/Features/Meta/Test/Visuals"
        self.write(PREFAB, "prefab polished")
        self.write(CONTROLLER, "class ScreenTestController { void AnimOpenUI() {} }")
        self.write(visuals + "/test_fx_glow.png", "png")
        self.write(visuals + "/test_fx_glow.png.meta", "meta")
        self.write("Assets/_Project/Features/Meta/Test/Visuals.meta", "folder meta")
        self.write("Assets/_Project/Features/Meta/Test/Scripts/Controller/TestMotion.cs", "static class TestMotion {}")
        self.write(".claude/docs/ArtStyle.md", "art style v2")
        self.write(SHARED_MAT, "mat v2")
        self.write(SCENE, "scene auto-dirt")
        code, out = self.lock("revert")
        self.assertEqual(code, 0, out)
        self.assertTrue(out["prefabRestored"])
        self.assertEqual(self.read(PREFAB), "prefab phase C")
        self.assertEqual(self.read(CONTROLLER), "class ScreenTestController {}")
        self.assertEqual(self.read(".claude/docs/ArtStyle.md"), "art style v1")
        self.assertEqual(self.read(SHARED_MAT), "mat v1")
        self.assertFalse((self.root / visuals).exists())
        self.assertFalse((self.root / "Assets/_Project/Features/Meta/Test/Visuals.meta").exists())
        self.assertFalse((self.root / "Assets/_Project/Features/Meta/Test/Scripts/Controller/TestMotion.cs").exists())
        self.assertEqual(self.read(SCENE), "scene auto-dirt")   # not Phase D's to undo
        self.assertIn(SCENE, out["leftInPlace"])
        self.assertEqual(self.read(PREFAB + ".meta"), "assetBundleName: features__test")

    def test_finish_then_begin_reports_already_done_until_prefab_changes(self):
        self.lock("begin")
        self.write(PREFAB, "prefab polished")
        code, out = self.lock("finish")
        self.assertEqual(code, 0, out)
        code, out = self.lock("begin")
        self.assertEqual(out["action"], "already-done", out)
        self.write(PREFAB, "prefab rebuilt by Phase C again")
        code, out = self.lock("begin")
        self.assertEqual(out["action"], "done-modified", out)   # never wipes the Phase C backup on its own
        code, out = self.lock("begin", "--restart")
        self.assertEqual(out["action"], "start", out)
        sdir = self.root / out["dir"]
        self.assertEqual((sdir / "backup" / PREFAB).read_text(encoding="utf-8"), "prefab rebuilt by Phase C again")

    def test_revert_still_works_after_finish(self):
        # a later gate (compile / code review) can still undo a finished Phase D
        self.lock("begin")
        self.write(PREFAB, "prefab polished")
        self.lock("finish")
        code, out = self.lock("revert")
        self.assertEqual(code, 0, out)
        self.assertEqual(self.read(PREFAB), "prefab phase C")

    def test_revert_works_after_the_backlog_staged_everything(self):
        # /run-backlog STEP 5a runs `git add -A` before the gates that may ask for a revert
        self.lock("begin")
        motion = "Assets/_Project/Features/Meta/Test/Scripts/Controller/TestMotion.cs"
        png = "Assets/_Project/Features/Meta/Test/Visuals/test_fx_glow.png"
        self.write(PREFAB, "prefab polished")
        self.write(motion, "static class TestMotion {}")
        self.write(png, "png")
        self.write(SHARED_MAT, "mat v2")
        self.git("add", "-A")
        code, out = self.lock("scope")
        self.assertEqual({e["path"] for e in out["violations"]}, {SHARED_MAT}, out)
        code, out = self.lock("revert")
        self.assertEqual(code, 0, out)
        self.assertEqual(self.read(PREFAB), "prefab phase C")
        self.assertFalse((self.root / motion).exists())
        self.assertFalse((self.root / png).exists())
        self.assertEqual(self.read(SHARED_MAT), "mat v1")
        staged = subprocess.run(["git", "diff", "--cached", "--name-only"], cwd=self.root,
                                capture_output=True, text=True).stdout.split()
        for path in (motion, png, SHARED_MAT, PREFAB):
            self.assertNotIn(path, staged)
        # what the revert discarded is kept, never lost silently
        self.assertTrue((self.root / out["discardedCopies"] / SHARED_MAT).is_file())

    def test_scope_ignores_phase_c_files_that_were_only_staged(self):
        self.write("Assets/_Project/Features/Meta/Test/Visuals/phase_c_icon.png", "png")
        self.lock("begin")
        self.git("add", "-A")
        code, out = self.lock("scope")
        self.assertEqual(code, 0, out)
        self.assertEqual(out["violations"], [])
        self.assertEqual(out["allowed"], [])

    def test_scope_rules_for_code_artstyle_and_mockups(self):
        self.write(".claude/docs/ArtStyle/screens/approved/Home.png", "approved")
        self.write("TechSpec/Mockups/Meta/Test.png", "frozen mockup")
        self.git("add", "-A")
        self.git("commit", "-qm", "refs")
        self.lock("begin")
        self.write("Assets/_Project/Features/_Shared/UI/Fx/UIFxGlow.cs", "class UIFxGlow {}")
        self.write(".claude/docs/ArtStyle/kit-board.png", "board v2")
        self.write(".claude/docs/ArtStyle/screens/approved/Home.png", "changed")
        self.write("TechSpec/Mockups/Meta/Test.png", "changed")
        self.write("TechSpec/Mockups/Meta/Test.unity.png", "evidence")
        self.write("TechSpec/Mockups/Meta/Test.ui-build-report.json", "{}")
        code, out = self.lock("scope")
        self.assertEqual(code, 1, out)
        self.assertEqual({e["path"] for e in out["violations"]},
                         {"Assets/_Project/Features/_Shared/UI/Fx/UIFxGlow.cs",
                          ".claude/docs/ArtStyle/screens/approved/Home.png", "TechSpec/Mockups/Meta/Test.png"})
        allowed = {e["path"] for e in out["allowed"]}
        self.assertTrue({".claude/docs/ArtStyle/kit-board.png", "TechSpec/Mockups/Meta/Test.unity.png",
                         "TechSpec/Mockups/Meta/Test.ui-build-report.json"} <= allowed, allowed)

    def test_begin_after_revert_does_not_retry_unless_restarted(self):
        self.lock("begin")
        self.write(PREFAB, "prefab polished")
        self.lock("revert")
        code, out = self.lock("begin")
        self.assertEqual(out["action"], "already-reverted", out)
        code, out = self.lock("begin", "--restart")
        self.assertEqual(out["action"], "start", out)

    def test_revert_after_finish_refuses_to_drop_later_fix_rounds(self):
        self.lock("begin")
        self.write(PREFAB, "prefab polished")
        self.lock("finish")
        self.write(PREFAB, "prefab polished + review fix")
        code, out = self.lock("begin")
        self.assertEqual(out["action"], "done-modified", out)
        code, out = self.lock("revert")
        self.assertEqual(code, 1, out)
        self.assertTrue(out["refused"])
        self.assertEqual(out["editedAfterFinish"], [PREFAB])
        self.assertEqual(self.read(PREFAB), "prefab polished + review fix")   # untouched
        code, out = self.lock("revert", "--discard-later-edits")
        self.assertEqual(code, 0, out)
        self.assertEqual(self.read(PREFAB), "prefab phase C")

    def test_revert_after_finish_leaves_later_work_outside_the_footprint(self):
        shared = "Assets/_Project/Features/_Shared/Config/GameConstant.cs"
        self.write(shared, "const A = 1;")
        self.git("add", "-A")
        self.git("commit", "-qm", "shared")
        self.write(PREFAB, "prefab phase C")      # Phase C output, untracked again after the commit
        self.lock("begin")
        self.write(PREFAB, "prefab polished")
        self.lock("finish")
        self.write(shared, "const A = 2;  // a STEP 6 logic fix")
        code, out = self.lock("revert")
        self.assertEqual(code, 0, out)
        self.assertEqual(self.read(PREFAB), "prefab phase C")
        self.assertEqual(self.read(shared), "const A = 2;  // a STEP 6 logic fix")

    def test_session_is_stale_once_head_moves(self):
        self.lock("begin")
        self.write(PREFAB, "prefab polished")
        self.lock("finish")
        self.git("add", "-A")
        self.git("commit", "-qm", "task shipped")
        for cmd in ("scope", "revert"):
            code, out = self.lock(cmd)
            self.assertEqual(code, 2, out)
            self.assertIn("stale", out["error"])
        self.assertEqual(self.read(PREFAB), "prefab polished")
        code, out = self.lock("begin")               # the same task again: never Phase D twice
        self.assertEqual(out["action"], "already-done", out)
        code, out = self.lock("begin", "--task", "042")   # a later task on the same screen
        self.assertEqual(out["action"], "start", out)
        self.assertIn("_archive", out["archivedOldSession"])
        self.assertTrue((self.root / out["archivedOldSession"] / "backup" / PREFAB).is_file())

    def test_untracked_prefab_meta_and_dirty_files_are_restorable(self):
        spec = "TechSpec/Mockups/Meta/Test.ui-spec.json"
        self.write(spec, '{"v": 1}')                # untracked mockup contract at begin
        self.lock("begin")
        self.write(PREFAB + ".meta", "assetBundleName: none")
        self.write(spec, '{"v": 2}')
        code, out = self.lock("scope")
        self.assertEqual({e["path"] for e in out["violations"]}, {PREFAB + ".meta", spec})
        code, out = self.lock("revert")
        self.assertEqual(code, 0, out)
        self.assertEqual(self.read(PREFAB + ".meta"), "assetBundleName: features__test")
        self.assertEqual(self.read(spec), '{"v": 1}')
        self.assertEqual(out["unrestorable"], [])

    def test_existing_visuals_art_may_not_be_edited(self):
        icon = "Assets/_Project/Features/Meta/Test/Visuals/icon.png"
        self.write(icon, "original art")
        self.write(icon + ".meta", "spriteBorder: original")
        self.git("add", "-A")
        self.git("commit", "-qm", "art")
        self.write(PREFAB, "prefab phase C")
        self.lock("begin")
        self.write(icon, "recoloured")
        self.write(icon + ".meta", "spriteBorder: changed")
        self.write("Assets/_Project/Features/Meta/Test/Visuals/test_fx_new.png", "new art")
        code, out = self.lock("scope")
        self.assertEqual(code, 1, out)
        self.assertEqual({e["path"] for e in out["violations"]}, {icon, icon + ".meta"})
        self.assertIn("Assets/_Project/Features/Meta/Test/Visuals/test_fx_new.png", {e["path"] for e in out["allowed"]})

    def test_glob_characters_in_file_names_are_literal(self):
        a, pattern = "Assets/_Project/Visual/Shared/btn_a.mat", "Assets/_Project/Visual/Shared/btn_[ab].mat"
        self.write(a, "a v1")
        self.write(pattern, "pattern v1")
        self.git("add", "-A")
        self.git("commit", "-qm", "mats")
        self.write(PREFAB, "prefab phase C")
        self.write(a, "a - dev's uncommitted edit")
        self.lock("begin")
        self.write(pattern, "pattern v2")
        code, out = self.lock("revert")
        self.assertEqual(self.read(pattern), "pattern v1")
        self.assertEqual(self.read(a), "a - dev's uncommitted edit")

    def test_feature_dir_is_normalised(self):
        code, out = self.lock("begin", "--feature-dir", "./Assets/_Project/Features/Meta/Test/")
        self.assertEqual(out["feature"], "Assets/_Project/Features/Meta/Test", out)
        self.lock("revert")
        nested = "Assets/_Project/Features/Meta/Test/Resources/Sub/Resources/screen_deep.prefab"
        self.write(nested, "deep")
        proc = subprocess.run([sys.executable, str(LOCK), "begin", "--prefab", nested],
                              cwd=self.root, capture_output=True, text=True)
        self.assertEqual(json.loads(proc.stdout)["feature"], "Assets/_Project/Features/Meta/Test/Resources/Sub")

    def test_unreadable_state_is_archived_not_wiped(self):
        code, out = self.lock("begin")
        sdir = self.root / out["dir"]
        (sdir / "state.json").write_text("{trunc", encoding="utf-8")
        self.write(PREFAB, "prefab half polished")
        code, out = self.lock("begin")
        self.assertEqual(out["action"], "start", out)
        archived = self.root / out["archivedOldSession"]
        self.assertEqual((archived / "backup" / PREFAB).read_text(encoding="utf-8"), "prefab phase C")

    def test_restart_is_refused_while_in_progress(self):
        self.lock("begin")
        code, out = self.lock("begin", "--restart")
        self.assertEqual(code, 2, out)

    def test_feature_dir_for_prefab_in_a_resources_subfolder(self):
        nested = "Assets/_Project/Features/Meta/Test/Resources/Popups/screen_nested.prefab"
        self.write(nested, "nested prefab")
        proc = subprocess.run([sys.executable, str(LOCK), "begin", "--prefab", nested],
                              cwd=self.root, capture_output=True, text=True)
        out = json.loads(proc.stdout)
        self.assertEqual(out["feature"], "Assets/_Project/Features/Meta/Test", out)
        self.assertIn(CONTROLLER, out["backedUp"])

    def test_revert_keeps_pre_existing_empty_folders_and_other_tmp_files(self):
        empty = self.root / "Assets/_Project/Features/Meta/Test/Visuals/Empty"
        empty.mkdir(parents=True)
        (self.root / ".gitignore").write_text("", encoding="utf-8")   # an older project: tmp not ignored
        self.write(".claude/tmp/backlog/review-before.diff", "other tool")
        self.lock("begin")
        self.write("Assets/_Project/Features/Meta/Test/Visuals/Empty/glow.png", "png")
        code, out = self.lock("revert")
        self.assertEqual(code, 0, out)
        self.assertTrue(empty.is_dir())
        self.assertEqual(self.read(".claude/tmp/backlog/review-before.diff"), "other tool")
        self.assertTrue((self.root / out["discardedCopies"]).parent.joinpath("baseline.json").is_file())

    def test_unrelated_commit_mid_phase_d_keeps_the_session(self):
        self.lock("begin")
        self.write(PREFAB, "prefab polished")
        self.write("Assets/_Project/Other/note.txt", "unrelated")
        self.git("add", "--", "Assets/_Project/Other/note.txt")
        self.git("commit", "-qm", "unrelated work")
        code, out = self.lock("scope")
        self.assertEqual(code, 0, out)
        code, out = self.lock("revert")
        self.assertEqual(code, 0, out)
        self.assertEqual(self.read(PREFAB), "prefab phase C")
        self.assertEqual(self.read("Assets/_Project/Other/note.txt"), "unrelated")

    def test_commit_touching_the_screen_mid_phase_d_is_stale_never_a_fresh_start(self):
        self.lock("begin")
        self.write(PREFAB, "prefab half polished")
        self.git("add", "-A")
        self.git("commit", "-qm", "mid-task commit")
        self.write(PREFAB, "prefab half polished + more")
        code, out = self.lock("begin")
        self.assertEqual(code, 1, out)
        self.assertEqual(out["action"], "stale")
        for cmd in ("scope", "revert", "finish"):
            code, out = self.lock(cmd)
            self.assertEqual(code, 2, out)
            self.assertIn("stale", out["error"])
            self.assertNotIn("run `begin`", out["error"])
        self.assertEqual(self.read(PREFAB), "prefab half polished + more")

    def test_finish_is_idempotent(self):
        self.lock("begin")
        self.write(PREFAB, "prefab polished")
        self.lock("finish")
        self.write(PREFAB, "prefab polished + review fix")
        code, out = self.lock("finish")
        self.assertEqual(out["action"], "already-done", out)
        code, out = self.lock("revert")
        self.assertTrue(out.get("refused"), out)                 # the first footprint still guards the fix
        self.assertEqual(self.read(PREFAB), "prefab polished + review fix")

    def test_footprint_is_only_what_phase_d_changed(self):
        self.lock("begin")
        self.write(PREFAB, "prefab polished")
        self.lock("finish")
        self.write(CONTROLLER, "class ScreenTestController { /* STEP 6 logic fix */ }")
        self.write(SCENE, "scene auto-dirt")
        code, out = self.lock("revert")
        self.assertEqual(code, 0, out)
        self.assertEqual(self.read(PREFAB), "prefab phase C")
        self.assertEqual(self.read(CONTROLLER), "class ScreenTestController { /* STEP 6 logic fix */ }")
        self.assertEqual(self.read(SCENE), "scene auto-dirt")

    def test_second_revert_is_a_no_op(self):
        self.lock("begin")
        self.write(PREFAB, "prefab polished")
        self.lock("revert")
        self.write(CONTROLLER, "class ScreenTestController { /* later Phase C fix */ }")
        code, out = self.lock("revert")
        self.assertEqual(out["action"], "already-reverted", out)
        self.assertEqual(self.read(CONTROLLER), "class ScreenTestController { /* later Phase C fix */ }")

    def test_editing_the_agent_system_is_a_violation(self):
        self.lock("begin")
        self.write(".claude/agents/ui-visual-reviewer.md", "**Phase D (designer pass — loosened")
        code, out = self.lock("scope")
        self.assertEqual(code, 1, out)
        self.assertIn(".claude/agents/ui-visual-reviewer.md", {e["path"] for e in out["violations"]})

    def test_same_prefab_name_in_two_features_gets_two_sessions(self):
        other = "Assets/_Project/Features/Social/Test/Resources/screen_test.prefab"
        self.write(other, "other screen")
        code, a = self.lock("begin")
        proc = subprocess.run([sys.executable, str(LOCK), "begin", "--prefab", other],
                              cwd=self.root, capture_output=True, text=True)
        b = json.loads(proc.stdout)
        self.assertNotEqual(a["dir"], b["dir"])
        self.assertEqual(b["action"], "start", b)
        code, out = self.lock("scope")
        self.assertEqual(code, 0, out)

    def test_broken_session_files_answer_in_json(self):
        code, out = self.lock("begin")
        sdir = self.root / out["dir"]
        (sdir / "state.json").write_bytes(b"\xff\xfe not utf-8")
        code, out = self.lock("begin")
        self.assertEqual(out["action"], "start", out)
        (self.root / out["dir"] / "baseline.json").write_text("{}", encoding="utf-8")
        code, out = self.lock("scope")
        self.assertEqual(code, 2, out)
        self.assertFalse(out["ok"])

    def test_absolute_feature_dir_is_made_relative(self):
        code, out = self.lock("begin", "--feature-dir", str(self.root / "Assets/_Project/Features/Meta/Test"))
        self.assertEqual(out["feature"], "Assets/_Project/Features/Meta/Test", out)

    def test_task_ids_separate_tasks_and_never_rerun_one(self):
        code, out = self.lock("begin", "--task", "011")
        self.write(PREFAB, "prefab polished by 011")
        self.lock("revert")
        self.git("add", "-A")
        self.git("commit", "-qm", "011 shipped without Phase D")
        code, out = self.lock("begin", "--task", "011")             # recovery of 011: no retry
        self.assertEqual(out["action"], "already-reverted", out)
        self.write(PREFAB, "prefab rebuilt by task 012 (Phase C)")
        code, out = self.lock("begin", "--task", "012")             # a later task: fresh start
        self.assertEqual(out["action"], "start", out)
        sdir = self.root / out["dir"]
        self.assertEqual((sdir / "backup" / PREFAB).read_text(encoding="utf-8"), "prefab rebuilt by task 012 (Phase C)")

    def test_commit_of_a_dirty_unrelated_file_is_never_reverted(self):
        foo = "Assets/_Project/Other/Foo.cs"
        self.write(foo, "v1 dirty before begin")
        self.lock("begin")
        self.write(foo, "v2 the dev's work")
        self.git("add", "--", foo)
        self.git("commit", "-qm", "dev commits Foo")
        code, out = self.lock("scope")
        self.assertNotIn(foo, {e["path"] for e in out["violations"]}, out)
        code, out = self.lock("revert")
        self.assertEqual(self.read(foo), "v2 the dev's work")

    def test_asset_embedding_a_material_is_a_violation(self):
        font = "Assets/Fonts/Main SDF.asset"
        so = "Assets/_Project/Data/Config.asset"
        self.write(font, "--- !u!114 &1\nMonoBehaviour:\n--- !u!21 &2\nMaterial:\n  _OutlineWidth: 0\n")
        self.write(so, "--- !u!114 &1\nMonoBehaviour:\n  value: 1\n")
        self.git("add", "-A")
        self.git("commit", "-qm", "assets")
        self.write(PREFAB, "prefab phase C")
        self.lock("begin")
        self.write(font, "--- !u!114 &1\nMonoBehaviour:\n--- !u!21 &2\nMaterial:\n  _OutlineWidth: 0.3\n")
        self.write(so, "--- !u!114 &1\nMonoBehaviour:\n  value: 2\n")
        code, out = self.lock("scope")
        self.assertEqual({e["path"] for e in out["violations"]}, {font}, out)
        self.assertIn(so, {e["path"] for e in out["warnings"]})
        code, out = self.lock("revert")
        self.assertIn("_OutlineWidth: 0\n", self.read(font))

    def test_revert_after_finish_reports_what_it_leaves(self):
        self.lock("begin")
        self.write(PREFAB, "prefab polished")
        self.lock("finish")
        self.write(SHARED_MAT, "mat edited after finish")
        code, out = self.lock("revert")
        self.assertEqual(self.read(SHARED_MAT), "mat edited after finish")
        self.assertIn(SHARED_MAT, out["leftInPlace"])

    def test_another_screens_mockup_commit_does_not_stale_the_session(self):
        self.lock("begin")
        self.write(PREFAB, "prefab polished")
        self.write("TechSpec/Mockups/Other/Other.ui-build-report.json", "{}")
        self.write(".claude/docs/ArtStyle/screens/pending/Other.png", "png")
        self.git("add", "--", "TechSpec", ".claude/docs/ArtStyle")
        self.git("commit", "-qm", "another task shipped its screen")
        code, out = self.lock("scope")
        self.assertEqual(code, 0, out)

    def test_scope_without_session_is_unusable_input(self):
        code, out = self.lock("scope")
        self.assertEqual(code, 2)
        self.assertIn("begin", out["error"])


if __name__ == "__main__":
    unittest.main()
