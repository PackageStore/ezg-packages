"""End-to-end tests for the spec-first UI mockup toolchain."""

import json
import subprocess
import sys
import tempfile
import unittest
from pathlib import Path


REPO = Path(__file__).resolve().parents[3]
VALIDATOR = REPO / ".claude" / "scripts" / "ui-spec-validator.py"
RENDERER = REPO / ".claude" / "scripts" / "ui-spec-render.py"
EXTRACTOR = REPO / ".claude" / "scripts" / "ui-spec-extract.py"
REPORT = REPO / ".claude" / "scripts" / "ui-build-report.py"
VISUAL_DIFF = REPO / ".claude" / "scripts" / "ui-visual-diff.py"

# Every test here validates a spec against the UI kit, and the validator refuses
# a kit whose `_meta.sourceHash` no longer matches the prefabs it was extracted
# from. The kit is a generated artifact, so it is legitimately absent or stale
# in a tree that has not run ui-kit-sync yet: a fresh clone before bootstrap, or
# a project template that ships the toolchain without any prefabs. Failing there
# would report a broken pipeline when the only real news is "no kit built yet".
KIT_JSON = REPO / ".claude" / "ui-kit" / "ui-kit.json"


def _kit_state():
    """None when the kit is usable, else why the suite has to stand down."""
    if not KIT_JSON.exists():
        return f"UI kit not generated yet ({KIT_JSON} missing) — run ui-kit-sync.py"
    try:
        meta = json.loads(KIT_JSON.read_text(encoding="utf-8")).get("_meta", {})
    except (OSError, ValueError) as exc:
        return f"UI kit unreadable: {exc}"
    source = REPO / meta.get("source", "Assets/Resources/Prefabs/Templates")
    if not source.is_dir():
        return f"prefab source {source} absent — kit cannot be validated here"
    return None


KIT_SKIP_REASON = _kit_state()


def valid_spec():
    return {
        "specVersion": 1,
        "screen": "TestPopup",
        "feature": "TestFeature",
        "branch": "Popup",
        "designResolution": [1080, 1920],
        "contentRoot": "content",
        "containers": [
            {
                "id": "popup",
                "type": "col",
                "size": [900, 1000],
                "children": ["title", "content"],
            },
            {
                "id": "title",
                "type": "row",
                "parent": "popup",
                "children": ["TitleText"],
            },
            {
                "id": "content",
                "type": "col",
                "parent": "popup",
                "gap": 24,
                "padding": [32, 32, 32, 32],
                "children": ["BuyButton"],
            },
        ],
        "elements": [
            {
                "id": "TitleText",
                "template": "TextTemplate",
                "parent": "title",
                "size": [600, 90],
                "text": "#test_title",
                "localize": "#test_title",
                "baseChrome": True,
            },
            {
                "id": "BuyButton",
                "template": "ButtonActive",
                "parent": "content",
                "size": [350, 150],
                "text": "Mua",
                "localize": "#buy",
            },
        ],
        "assumptions": [],
        "questions": [],
    }


def fullscreen_tab_spec():
    """FullScreen screen whose tabs live in the Bot chrome, like Equipment/Shop."""
    return {
        "specVersion": 1,
        "screen": "TestScreen",
        "feature": "TestFeature",
        "branch": "FullScreen",
        "designResolution": [1080, 1920],
        "contentRoot": "Mid",
        "containers": [
            {"id": "screenRoot", "type": "absolute", "children": ["topChrome", "Mid", "botChrome"]},
            {"id": "topChrome", "type": "absolute", "parent": "screenRoot", "size": [1080, 136],
             "pos": [0, 0], "children": ["resourceBar"]},
            {"id": "Mid", "type": "col", "parent": "screenRoot", "anchor": "stretch",
             "offsets": [0, 0, 136, 199], "children": ["pageA"]},
            {"id": "pageA", "type": "col", "parent": "Mid", "size": [1000, 1000], "children": ["pageTitle"]},
            {"id": "botChrome", "type": "absolute", "parent": "screenRoot", "size": [1080, 199],
             "pos": [0, 0], "children": ["tabBar", "ButtonBack"]},
            {"id": "tabBar", "type": "row", "parent": "botChrome", "tabBar": True,
             "size": [1080, 148.73], "children": ["tabOne", "tabTwo"]},
        ],
        "elements": [
            {"id": "resourceBar", "template": "ResourceViewTemplate", "parent": "topChrome",
             "size": [750, 140], "baseChrome": True},
            {"id": "ButtonBack", "template": "ButtonIcon", "parent": "botChrome",
             "size": [64, 55], "baseChrome": True},
            {"id": "tabOne", "template": "TabToggleIconTemplate", "parent": "tabBar", "size": [200, 200]},
            {"id": "tabTwo", "template": "TabToggleIconTemplate", "parent": "tabBar", "size": [200, 200]},
            {"id": "pageTitle", "template": "TextTemplate", "parent": "pageA", "size": [900, 88],
             "text": "Trang", "localize": "#test_title"},
        ],
        "assumptions": [],
        "questions": [],
    }


@unittest.skipIf(KIT_SKIP_REASON is not None, KIT_SKIP_REASON or "")
class UISpecPipelineTests(unittest.TestCase):
    def setUp(self):
        self.tmp = tempfile.TemporaryDirectory(prefix="ui-spec-test-")
        self.addCleanup(self.tmp.cleanup)
        self.dir = Path(self.tmp.name)

    def run_json(self, script, *args, expected=0):
        proc = subprocess.run(
            [sys.executable, str(script), *map(str, args)],
            cwd=REPO,
            capture_output=True,
            text=True,
        )
        self.assertEqual(proc.returncode, expected, msg=proc.stdout + proc.stderr)
        return json.loads(proc.stdout)

    def write_spec(self, spec=None):
        path = self.dir / "TestPopup.ui-spec.json"
        path.write_text(json.dumps(spec or valid_spec(), ensure_ascii=False), encoding="utf-8")
        return path

    def test_valid_v1_passes_approval(self):
        result = self.run_json(VALIDATOR, self.write_spec(), "--mode", "approve")
        self.assertTrue(result["ok"], result)

    def test_missing_localize_is_blocking_for_v1(self):
        spec = valid_spec()
        del spec["elements"][1]["localize"]
        result = self.run_json(VALIDATOR, self.write_spec(spec), expected=1)
        self.assertTrue(any(e["code"] == "localize" for e in result["errors"]), result)

    def test_questions_warn_in_draft_and_block_approval(self):
        spec = valid_spec()
        spec["questions"] = ["Which price?"]
        path = self.write_spec(spec)
        draft = self.run_json(VALIDATOR, path)
        self.assertTrue(draft["ok"], draft)
        self.assertTrue(any(w["code"] == "unresolved_questions" for w in draft["warnings"]), draft)
        approve = self.run_json(VALIDATOR, path, "--mode", "approve", expected=1)
        self.assertFalse(approve["ok"])

    def test_structured_question_patch_is_validated_before_review(self):
        spec = valid_spec()
        spec["questions"] = [{
            "q": "Khoảng cách nào?",
            "options": [{
                "label": "24 px",
                "patch": [{"op": "replace", "path": "/containers/@content/gap", "value": 24}],
            }],
        }]
        result = self.run_json(VALIDATOR, self.write_spec(spec))
        self.assertFalse(any(e["code"] == "questions" for e in result["errors"]), result)
        self.assertTrue(any(w["code"] == "unresolved_questions" for w in result["warnings"]), result)

    def test_structured_question_patch_cannot_target_missing_node(self):
        spec = valid_spec()
        spec["questions"] = [{
            "q": "Khoảng cách nào?",
            "options": [{
                "label": "Broken",
                "patch": [{"op": "replace", "path": "/containers/@missing/gap", "value": 24}],
            }],
        }]
        result = self.run_json(VALIDATOR, self.write_spec(spec), expected=1)
        self.assertTrue(any(e["code"] == "questions" and "cannot apply" in e["message"] for e in result["errors"]), result)

    def test_unknown_template_and_containment_are_blocking(self):
        spec = valid_spec()
        spec["elements"][0]["template"] = "NotARealTemplate"
        spec["elements"][0]["baseChrome"] = False
        result = self.run_json(VALIDATOR, self.write_spec(spec), expected=1)
        codes = {e["code"] for e in result["errors"]}
        self.assertIn("unknown_template", codes)
        self.assertIn("containment", codes)

    def test_container_visible_background_is_blocking(self):
        spec = valid_spec()
        spec["containers"][0]["style"] = {"background": "rgba(103,84,69,1)", "borderRadius": "30px"}
        result = self.run_json(VALIDATOR, self.write_spec(spec), expected=1)
        self.assertTrue(any(e["code"] == "container_style" for e in result["errors"]), result)

    def test_container_border_and_shadow_are_blocking(self):
        spec = valid_spec()
        spec["containers"][2]["style"] = {"border": "2px solid #fff", "boxShadow": "0 2px 4px #000"}
        result = self.run_json(VALIDATOR, self.write_spec(spec), expected=1)
        offenders = [e for e in result["errors"] if e["code"] == "container_style"]
        self.assertTrue(offenders, result)
        self.assertIn("border", offenders[0]["message"])
        self.assertIn("boxShadow", offenders[0]["message"])

    def test_transparent_container_style_is_allowed(self):
        # Layout-only helpers (transparent bg, corner radius) must not trip the frame rule.
        spec = valid_spec()
        spec["containers"][2]["style"] = {"background": "transparent", "borderRadius": "20px"}
        result = self.run_json(VALIDATOR, self.write_spec(spec), "--mode", "approve")
        self.assertTrue(result["ok"], result)

    def test_inconsistent_chrome_size_warns_without_blocking(self):
        spec = valid_spec()
        spec["containers"][2]["children"] = ["BuyButton", "TorchChip", "GemChip"]
        spec["elements"].extend([
            {"id": "TorchChip", "template": "ResourceHomeTemplate", "parent": "content",
             "size": [320, 90], "text": "997", "localize": "dynamic"},
            {"id": "GemChip", "template": "ResourceHomeTemplate", "parent": "content",
             "size": [300, 90], "text": "8650", "localize": "dynamic"},
        ])
        result = self.run_json(VALIDATOR, self.write_spec(spec), "--mode", "approve")
        self.assertTrue(result["ok"], result)
        self.assertTrue(any(w["code"] == "inconsistent_chrome_size" for w in result["warnings"]), result)

    def test_scroll_container_passes_and_renders_viewport(self):
        spec = valid_spec()
        spec["containers"][2]["scroll"] = "vertical"
        spec["containers"][2]["size"] = [860, 900]
        spec_path = self.write_spec(spec)
        result = self.run_json(VALIDATOR, spec_path, "--mode", "approve")
        self.assertTrue(result["ok"], result)
        html_path = self.dir / "TestPopup.html"
        self.run_json(RENDERER, spec_path, "--output", html_path)
        markup = html_path.read_text(encoding="utf-8")
        self.assertIn('data-scroll="vertical"', markup)
        self.assertIn("spec-scrollbar", markup)

    def test_scroll_template_as_element_is_blocking(self):
        # Elements hold no children — a ScrollViewTemplate element would strand the
        # scrolled body outside its Viewport/Content (the StageOverview defect).
        spec = valid_spec()
        spec["containers"][2]["children"] = ["BuyButton", "ScrollBox"]
        spec["elements"].append({
            "id": "ScrollBox", "template": "ScrollViewTemplate", "parent": "content",
            "size": [860, 900],
        })
        result = self.run_json(VALIDATOR, self.write_spec(spec), expected=1)
        self.assertTrue(any(e["code"] == "scroll_as_element" for e in result["errors"]), result)

    def test_scroll_requires_a_sized_viewport_and_flow_container(self):
        spec = valid_spec()
        spec["containers"][2]["scroll"] = "vertical"  # no size, no stretch anchor
        result = self.run_json(VALIDATOR, self.write_spec(spec), expected=1)
        self.assertTrue(any(e["code"] == "scroll_size" for e in result["errors"]), result)

        spec = valid_spec()
        spec["containers"][2]["type"] = "absolute"
        spec["containers"][2]["scroll"] = "loop"
        spec["containers"][2]["size"] = [860, 900]
        result = self.run_json(VALIDATOR, self.write_spec(spec), expected=1)
        self.assertTrue(any(e["code"] == "scroll_container_type" for e in result["errors"]), result)

    def test_invalid_scroll_mode_is_blocking(self):
        spec = valid_spec()
        spec["containers"][2]["scroll"] = True
        spec["containers"][2]["size"] = [860, 900]
        result = self.run_json(VALIDATOR, self.write_spec(spec), expected=1)
        self.assertTrue(any(e["code"] == "scroll_value" for e in result["errors"]), result)

    def section_spec(self):
        """Popup whose body is a titled info block (StageOverview / DungeonGuide pattern)."""
        spec = valid_spec()
        spec["containers"][2]["gap"] = 40
        spec["containers"][2]["children"] = ["BuyButton", "loreSection"]
        spec["containers"].append({
            "id": "loreSection", "type": "col", "parent": "content",
            "size": [900, 300], "gap": 12, "padding": [20, 20, 50, 20],
            "section": {"title": "Cốt truyện", "localize": "#test_lore_label"},
            "children": ["loreText"],
        })
        spec["elements"].append({
            "id": "loreText", "template": "TextTemplate", "parent": "loreSection",
            "size": [860, 190], "text": "Lore", "localize": "dynamic",
        })
        return spec

    def test_section_container_passes_and_renders_frame_plus_pill(self):
        spec_path = self.write_spec(self.section_spec())
        result = self.run_json(VALIDATOR, spec_path, "--mode", "approve")
        self.assertTrue(result["ok"], result)
        html_path = self.dir / "TestPopup.html"
        self.run_json(RENDERER, spec_path, "--output", html_path)
        markup = html_path.read_text(encoding="utf-8")
        self.assertIn('data-section="true"', markup)
        self.assertIn("spec-section-title", markup)
        self.assertIn("Cốt truyện", markup)

    def test_section_title_drawn_as_element_is_blocking(self):
        # The flag already builds the pill; a hand-drawn one ships two titles.
        spec = self.section_spec()
        spec["containers"][-1]["children"] = ["sectionPill", "loreText"]
        spec["elements"].append({
            "id": "sectionPill", "template": "ButtonTitleTemplate", "parent": "loreSection",
            "size": [400, 60], "text": "Cốt truyện", "localize": "#test_lore_label",
        })
        result = self.run_json(VALIDATOR, self.write_spec(spec), expected=1)
        self.assertTrue(any(e["code"] == "section_title_as_element" for e in result["errors"]), result)

    def test_section_frame_drawn_as_element_is_blocking(self):
        spec = self.section_spec()
        spec["containers"][-1]["children"] = ["sectionFrame", "loreText"]
        spec["elements"].append({
            "id": "sectionFrame", "template": "FrameTemplateInside", "parent": "loreSection",
            "anchor": "stretch", "offsets": [0, 0, 0, 0],
        })
        result = self.run_json(VALIDATOR, self.write_spec(spec), expected=1)
        self.assertTrue(any(e["code"] == "section_frame_as_element" for e in result["errors"]), result)

    def test_section_requires_flow_container_and_static_title(self):
        spec = self.section_spec()
        spec["containers"][-1]["type"] = "absolute"
        spec["containers"][-1]["section"]["localize"] = "dynamic"
        result = self.run_json(VALIDATOR, self.write_spec(spec), expected=1)
        codes = {e["code"] for e in result["errors"]}
        self.assertIn("section_container_type", codes)
        self.assertIn("section_localize", codes)

    def test_tight_section_geometry_warns_without_blocking(self):
        # The DungeonGuide fix: the pill hangs 30px above its frame, so a tight parent gap
        # or a shallow padding.top puts the title on top of neighbouring content.
        spec = self.section_spec()
        spec["containers"][2]["gap"] = 18
        spec["containers"][-1]["padding"] = [20, 20, 20, 20]
        result = self.run_json(VALIDATOR, self.write_spec(spec), "--mode", "approve")
        self.assertTrue(result["ok"], result)
        codes = {w["code"] for w in result["warnings"]}
        self.assertIn("section_parent_gap", codes)
        self.assertIn("section_padding_top", codes)

    def test_loose_title_pill_warns(self):
        spec = valid_spec()
        spec["containers"][2]["children"] = ["BuyButton", "loosePill"]
        spec["elements"].append({
            "id": "loosePill", "template": "ButtonTitleTemplate", "parent": "content",
            "size": [400, 60], "text": "Cốt truyện", "localize": "#test_lore_label",
        })
        result = self.run_json(VALIDATOR, self.write_spec(spec), "--mode", "approve")
        self.assertTrue(result["ok"], result)
        self.assertTrue(
            any(w["code"] == "section_title_without_frame" for w in result["warnings"]), result
        )

    def write_named_spec(self, spec, name):
        path = self.dir / f"{name}.ui-spec.json"
        path.write_text(json.dumps(spec, ensure_ascii=False), encoding="utf-8")
        return path

    def test_tabbar_in_bot_chrome_passes_and_renders_the_bar(self):
        spec_path = self.write_named_spec(fullscreen_tab_spec(), "TestScreen")
        result = self.run_json(VALIDATOR, spec_path, "--mode", "approve")
        self.assertTrue(result["ok"], result)
        self.assertFalse(any(w["code"] == "tabbar_in_content" for w in result["warnings"]), result)
        html_path = self.dir / "TestScreen.html"
        self.run_json(RENDERER, spec_path, "--output", html_path)
        markup = html_path.read_text(encoding="utf-8")
        self.assertIn('data-tabbar="true"', markup)

    def test_tab_toggles_outside_a_tabbar_container_are_blocking(self):
        # The DungeonGuide defect: a hand-made tabRow inside Mid while the shipped
        # FullScreen/Bot TabBottomTemplate stays inactive.
        spec = fullscreen_tab_spec()
        spec["containers"] = [c for c in spec["containers"] if c["id"] != "tabBar"]
        spec["containers"].append({
            "id": "tabRow", "type": "row", "parent": "Mid", "size": [1000, 190],
            "children": ["tabOne", "tabTwo"],
        })
        for container in spec["containers"]:
            if container["id"] == "Mid":
                container["children"] = ["tabRow", "pageA"]
            if container["id"] == "botChrome":
                container["children"] = ["tabBottom", "ButtonBack"]
        spec["elements"].append({
            "id": "tabBottom", "template": "TabBottomTemplate", "parent": "botChrome",
            "size": [1080, 148.73], "baseChrome": True,
        })
        for element in spec["elements"]:
            if element["id"] in ("tabOne", "tabTwo"):
                element["parent"] = "tabRow"
        result = self.run_json(VALIDATOR, self.write_named_spec(spec, "TestScreen"), expected=1)
        self.assertTrue(any(e["code"] == "tabs_outside_bottom_bar" for e in result["errors"]), result)
        self.assertTrue(any(e["code"] == "tabbar_as_element" for e in result["errors"]), result)

    def test_bare_tabbottomtemplate_chrome_warns_without_toggles(self):
        # FeatureTemplate ships the bar inactive: drawing it with no tabs promises a brown
        # strip the build never renders (the BattleResultDungeon defect).
        spec = fullscreen_tab_spec()
        spec["containers"] = [c for c in spec["containers"] if c["id"] != "tabBar"]
        for container in spec["containers"]:
            if container["id"] == "botChrome":
                container["children"] = ["tabBottom", "ButtonBack"]
        spec["elements"] = [e for e in spec["elements"] if e["id"] not in ("tabOne", "tabTwo")]
        spec["elements"].append({
            "id": "tabBottom", "template": "TabBottomTemplate", "parent": "botChrome",
            "size": [1080, 148.73], "baseChrome": True,
        })
        result = self.run_json(VALIDATOR, self.write_named_spec(spec, "TestScreen"), "--mode", "approve")
        self.assertTrue(result["ok"], result)
        self.assertTrue(any(w["code"] == "tabbar_empty_chrome" for w in result["warnings"]), result)

    def test_bot_chrome_without_the_bar_is_clean(self):
        # No tabs -> Bot holds only ButtonBack, no bar at all.
        spec = fullscreen_tab_spec()
        spec["containers"] = [c for c in spec["containers"] if c["id"] != "tabBar"]
        for container in spec["containers"]:
            if container["id"] == "botChrome":
                container["children"] = ["ButtonBack"]
        spec["elements"] = [e for e in spec["elements"] if e["id"] not in ("tabOne", "tabTwo")]
        result = self.run_json(VALIDATOR, self.write_named_spec(spec, "TestScreen"), "--mode", "approve")
        self.assertTrue(result["ok"], result)
        self.assertFalse(any(w["code"] == "tabbar_empty_chrome" for w in result["warnings"]), result)
        self.assertFalse(any(w["code"] == "fullscreen_missing_chrome" for w in result["warnings"]), result)

    def test_tabbar_must_be_a_row_hosting_only_toggles(self):
        spec = fullscreen_tab_spec()
        for container in spec["containers"]:
            if container["id"] == "tabBar":
                container["type"] = "absolute"
                container["children"] = ["tabOne", "tabTwo", "strayLabel"]
        spec["elements"].append({
            "id": "strayLabel", "template": "TextTemplate", "parent": "tabBar",
            "size": [200, 40], "text": "x", "localize": "none",
        })
        result = self.run_json(VALIDATOR, self.write_named_spec(spec, "TestScreen"), expected=1)
        self.assertTrue(any(e["code"] == "tabbar_container_type" for e in result["errors"]), result)
        self.assertTrue(any(e["code"] == "tabbar_children" for e in result["errors"]), result)

    def test_content_level_tabbar_only_warns(self):
        # Equipment's EquipmentFilter case: a filter row inside the content is allowed.
        spec = fullscreen_tab_spec()
        for container in spec["containers"]:
            if container["id"] == "tabBar":
                container["parent"] = "pageA"
            if container["id"] == "botChrome":
                container["children"] = ["ButtonBack"]
            if container["id"] == "pageA":
                container["children"] = ["pageTitle", "tabBar"]
        result = self.run_json(VALIDATOR, self.write_named_spec(spec, "TestScreen"), "--mode", "approve")
        self.assertTrue(result["ok"], result)
        self.assertTrue(any(w["code"] == "tabbar_in_content" for w in result["warnings"]), result)

    def test_render_check_and_extract_roundtrip(self):
        spec_path = self.write_spec()
        html_path = self.dir / "TestPopup.html"
        rendered = self.run_json(RENDERER, spec_path, "--output", html_path)
        self.assertTrue(rendered["ok"])
        self.run_json(RENDERER, spec_path, "--output", html_path, "--check")
        # Sidecar next to the HTML is authoritative and identical, so validation passes.
        copied_sidecar = self.dir / "TestPopup.ui-spec.json"
        self.assertEqual(copied_sidecar, spec_path)
        self.run_json(VALIDATOR, html_path, "--mode", "approve")
        extracted = self.dir / "Extracted.ui-spec.json"
        self.run_json(EXTRACTOR, html_path, "--output", extracted)
        self.assertEqual(json.loads(extracted.read_text()), valid_spec())

    def test_popup_render_centers_root(self):
        spec_path = self.write_spec()
        html_path = self.dir / "TestPopup.html"
        self.run_json(RENDERER, spec_path, "--output", html_path)
        rendered = html_path.read_text(encoding="utf-8")
        self.assertIn('data-branch="Popup"', rendered)
        self.assertIn('data-positioned="false"', rendered)
        self.assertIn("translate(-50%,-50%)", rendered)

    def test_explicitly_anchored_popup_root_skips_default_centering(self):
        spec = valid_spec()
        spec["containers"][0].update({"position": "abs", "anchor": "top-left"})
        spec_path = self.write_spec(spec)
        html_path = self.dir / "AnchoredRoot.html"
        self.run_json(RENDERER, spec_path, "--output", html_path)
        rendered = html_path.read_text(encoding="utf-8")
        root_start = rendered.index('id="popup"')
        root_end = rendered.index(">", root_start)
        root_tag = rendered[root_start:root_end]
        self.assertIn('data-positioned="true"', root_tag)
        self.assertIn("left:0px;top:0px", root_tag)

    def test_grid_requires_and_renders_columns_and_cell_size(self):
        spec = valid_spec()
        content = spec["containers"][2]
        content.update({"type": "grid", "columns": 2, "cellSize": [350, 150], "spacing": [20, 12]})
        spec_path = self.write_spec(spec)
        html_path = self.dir / "Grid.html"
        self.run_json(RENDERER, spec_path, "--output", html_path)
        rendered = html_path.read_text(encoding="utf-8")
        self.assertIn("grid-template-columns:repeat(2,350px)", rendered)
        self.assertIn("grid-auto-rows:150px", rendered)
        self.assertIn("column-gap:20px;row-gap:12px", rendered)

    def test_absolute_center_anchor_and_child_alignment_render(self):
        spec = valid_spec()
        spec["containers"][2]["childAlignment"] = "center"
        button = spec["elements"][1]
        button.update({"position": "abs", "anchor": "center", "pos": [0, 0]})
        spec_path = self.write_spec(spec)
        html_path = self.dir / "Anchors.html"
        self.run_json(RENDERER, spec_path, "--output", html_path)
        rendered = html_path.read_text(encoding="utf-8")
        self.assertIn("left:calc(50% + 0px)", rendered)
        self.assertIn("top:calc(50% + 0px)", rendered)
        self.assertIn("translateX(-50%) translateY(-50%)", rendered)
        self.assertIn("align-items:center;justify-content:center", rendered)

    def test_stretch_anchor_requires_offsets(self):
        spec = valid_spec()
        spec["elements"][1]["anchor"] = "stretch"
        result = self.run_json(VALIDATOR, self.write_spec(spec), expected=1)
        self.assertTrue(any(e["code"] == "offsets" for e in result["errors"]), result)

    def test_stretch_overrides_native_template_size(self):
        spec = valid_spec()
        spec["elements"][1].update({
            "position": "abs",
            "anchor": "stretch",
            "offsets": [10, 20, 30, 40],
        })
        spec_path = self.write_spec(spec)
        html_path = self.dir / "Stretch.html"
        self.run_json(RENDERER, spec_path, "--output", html_path)
        rendered = html_path.read_text(encoding="utf-8")
        self.assertIn("left:10px;right:20px;top:30px;bottom:40px", rendered)
        self.assertIn("width:auto;height:auto;min-width:0;min-height:0", rendered)

    def test_grid_alignment_positions_grid_content_not_items(self):
        spec = valid_spec()
        content = spec["containers"][2]
        content.update({
            "type": "grid",
            "columns": 2,
            "cellSize": [100, 100],
            "size": [900, 500],
            "childAlignment": "bottom-right",
        })
        spec_path = self.write_spec(spec)
        html_path = self.dir / "GridAlignment.html"
        self.run_json(RENDERER, spec_path, "--output", html_path)
        rendered = html_path.read_text(encoding="utf-8")
        self.assertIn("justify-content:end;align-content:end", rendered)
        self.assertIn("justify-items:start;align-items:start", rendered)

    def test_html_sidecar_drift_is_blocking(self):
        spec_path = self.write_spec()
        html_path = self.dir / "TestPopup.html"
        self.run_json(RENDERER, spec_path, "--output", html_path)
        spec = valid_spec()
        spec["elements"][1]["text"] = "Changed"
        spec_path.write_text(json.dumps(spec), encoding="utf-8")
        result = self.run_json(VALIDATOR, html_path, expected=1)
        self.assertTrue(any(e["code"] == "sidecar_drift" for e in result["errors"]), result)

    def test_legacy_html_remains_compatible(self):
        legacy = valid_spec()
        legacy.pop("specVersion")
        legacy.pop("contentRoot")
        html_path = self.dir / "Legacy.html"
        html_path.write_text(
            '<script type="application/json" id="spec">'
            + json.dumps(legacy)
            + "</script>",
            encoding="utf-8",
        )
        result = self.run_json(VALIDATOR, html_path)
        self.assertTrue(result["ok"], result)
        self.assertTrue(any(w["code"] == "legacy_spec" for w in result["warnings"]), result)

    def test_legacy_unresolved_questions_do_not_block_approval(self):
        legacy = valid_spec()
        legacy.pop("specVersion")
        legacy.pop("contentRoot")
        legacy["questions"] = ["Old unresolved design question"]
        html_path = self.dir / "LegacyQuestions.html"
        html_path.write_text(
            '<script type="application/json" id="spec">'
            + json.dumps(legacy)
            + "</script>",
            encoding="utf-8",
        )
        result = self.run_json(VALIDATOR, html_path, "--mode", "approve")
        self.assertTrue(result["ok"], result)
        self.assertTrue(any(w["code"] == "unresolved_questions" for w in result["warnings"]), result)

    def test_extracted_v0_sidecar_does_not_override_legacy_html(self):
        legacy = valid_spec()
        legacy.pop("specVersion")
        legacy.pop("contentRoot")
        html_path = self.dir / "LegacyExtracted.html"
        html_path.write_text(
            '<script type="application/json" id="spec">'
            + json.dumps(legacy)
            + "</script>",
            encoding="utf-8",
        )
        sidecar = self.dir / "LegacyExtracted.ui-spec.json"
        self.run_json(EXTRACTOR, html_path, "--output", sidecar)
        legacy["elements"][1]["text"] = "HTML remains authoritative"
        html_path.write_text(
            '<script type="application/json" id="spec">'
            + json.dumps(legacy)
            + "</script>",
            encoding="utf-8",
        )
        result = self.run_json(VALIDATOR, html_path)
        self.assertTrue(result["ok"], result)
        self.assertEqual(result["source"], str(html_path.resolve()))

    def test_build_report_requires_final_evidence(self):
        spec_path = self.write_spec()
        prefab = self.dir / "TestPopup.prefab"
        prefab.write_text("prefab", encoding="utf-8")
        screenshot = self.dir / "TestPopup.unity.png"
        from PIL import Image
        Image.new("RGB", (1080, 1920), "black").save(screenshot)
        reference = self.dir / "TestPopup.png"
        Image.new("RGB", (1080, 1920), "black").save(reference)
        visual_diff = self.dir / "TestPopup.ui-visual-diff.json"
        self.run_json(VISUAL_DIFF, reference, screenshot, "--output", visual_diff)
        report = self.dir / "TestPopup.ui-build-report.json"
        self.run_json(
            REPORT, "create",
            "--spec", spec_path,
            "--prefab", prefab,
            "--screenshot", screenshot,
            "--visual-diff", visual_diff,
            "--output", report,
            "--structural", "pass",
            "--visual", "pass",
            "--localization", "pass",
            "--missing-references", "0",
        )
        self.run_json(REPORT, "validate", report)


if __name__ == "__main__":
    unittest.main()
