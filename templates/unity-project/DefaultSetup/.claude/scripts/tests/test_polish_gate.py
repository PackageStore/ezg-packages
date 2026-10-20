"""Gate polish đo được trên ui-spec (PolishRules §9) + chuẩn canvas designResolution.

Không phụ thuộc kit thật của project: mỗi test dựng một ui-kit giả trong thư mục tạm và vá
các global của ui_spec_common, nên chạy được cả ở monorepo (không có prefab) lẫn project game.
"""

import importlib
import json
import struct
import subprocess
import sys
import tempfile
import unittest
import zlib
from pathlib import Path
from unittest import mock

SCRIPTS = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(SCRIPTS))

import ui_spec_common as usc  # noqa: E402


KIT = {
    "ButtonBig": {"size": [360, 160], "texts": [{"fontSize": 40}]},
    "button_small": {"size": [96, 96], "texts": []},
    "TextLabel": {"size": [600, 80], "texts": [{"fontSize": 40}]},
    "text_button_CTA": {"size": [300, 80], "texts": [{"fontSize": 40}]},
    "ButtonTitleTemplate": {"size": [300, 60], "texts": [{"fontSize": 40}]},
    "Frame": {"size": [800, 400], "texts": []},
}


def spec(**overrides):
    base = {
        "specVersion": 1,
        "screen": "ShopPopup",
        "feature": "Shop",
        "branch": "Popup",
        "designResolution": [1080, 2400],
        "contentRoot": "content",
        "containers": [
            {"id": "content", "type": "col", "gap": 24, "padding": [48, 48, 48, 48],
             "children": ["title", "actions"]},
            {"id": "actions", "type": "row", "parent": "content", "gap": 32, "children": ["buy", "later"]},
        ],
        "elements": [
            {"id": "title", "template": "TextLabel", "parent": "content", "size": [800, 96],
             "fontSize": 48, "text": "#shop_title", "localize": "#shop_title"},
            {"id": "buy", "template": "ButtonBig", "parent": "actions", "size": [360, 160],
             "role": "primary", "text": "#buy", "localize": "#buy"},
            {"id": "later", "template": "ButtonBig", "parent": "actions", "size": [360, 160],
             "role": "secondary", "text": "#later", "localize": "#later"},
        ],
        "assumptions": [],
        "questions": [],
    }
    base.update(overrides)
    return base


def element(data, node_id):
    return next(row for row in data["elements"] if row["id"] == node_id)


def codes(entries):
    return {entry["code"] for entry in entries}


class KitSandbox(unittest.TestCase):
    """Kit giả + vá global để validate_spec chạy độc lập với project."""

    polish = None
    design = [1080, 2400]

    def setUp(self):
        self.tmp = tempfile.TemporaryDirectory()
        root = Path(self.tmp.name)
        kit_json, kit_css = root / "ui-kit.json", root / "ui-kit.css"
        kit_json.write_text(json.dumps({"_meta": {"sourceHash": "fake"}, "templates": KIT}), encoding="utf-8")
        kit_css.write_text("/* kit */\n", encoding="utf-8")
        settings = dict(usc.POLISH_DEFAULTS, **(self.polish or {}))
        patches = [
            mock.patch.object(usc, "KIT_JSON", kit_json),
            mock.patch.object(usc, "KIT_CSS", kit_css),
            mock.patch.object(usc, "kit_source_hash", lambda: "fake"),
            mock.patch.object(usc, "design_resolution", lambda: list(self.design)),
            mock.patch.object(usc, "_polish_settings", lambda: settings),
        ]
        for patcher in patches:
            patcher.start()
            self.addCleanup(patcher.stop)
        self.addCleanup(self.tmp.cleanup)

    def check(self, data, mode="draft"):
        return usc.validate_spec(data, mode=mode)


class CleanSpecTests(KitSandbox):
    def test_clean_spec_has_no_polish_findings(self):
        result = self.check(spec(), mode="approve")
        self.assertTrue(result["ok"], result["errors"])
        self.assertFalse([c for c in codes(result["warnings"]) if c.startswith("polish_")], result["warnings"])


class FontTests(KitSandbox):
    def test_font_below_hard_floor_blocks_draft_and_approve(self):
        data = spec()
        element(data, "title")["fontSize"] = 24
        for mode in ("draft", "approve"):
            result = self.check(data, mode=mode)
            self.assertIn("polish_font_size", codes(result["errors"]), mode)

    def test_font_below_hard_floor_only_warns_at_build(self):
        data = spec()
        element(data, "title")["fontSize"] = 24
        result = self.check(data, mode="build")
        self.assertTrue(result["ok"], result["errors"])
        self.assertIn("polish_font_size", codes(result["warnings"]))

    def test_font_between_floor_and_minimum_warns(self):
        data = spec()
        element(data, "title")["fontSize"] = 31
        result = self.check(data)
        self.assertTrue(result["ok"], result["errors"])
        self.assertIn("polish_font_size_soft", codes(result["warnings"]))


class TouchTargetTests(KitSandbox):
    def test_small_button_blocks(self):
        data = spec()
        element(data, "later")["size"] = [96, 96]
        self.assertIn("polish_touch_target", codes(self.check(data)["errors"]))

    def test_native_kit_size_counts_when_spec_omits_size(self):
        data = spec()
        later = element(data, "later")
        later["template"] = "button_small"
        del later["size"]
        self.assertIn("polish_touch_target", codes(self.check(data)["errors"]))

    def test_hit_area_satisfies_touch_target(self):
        data = spec()
        later = element(data, "later")
        later["size"], later["hitArea"] = [96, 96], [128, 128]
        self.assertNotIn("polish_touch_target", codes(self.check(data)["errors"]))

    def test_base_chrome_and_text_labels_are_exempt(self):
        data = spec()
        data["elements"].append({"id": "close", "template": "button_small", "parent": "content",
                                 "baseChrome": True, "role": "close"})
        data["elements"].append({"id": "ctaLabel", "template": "text_button_CTA", "parent": "content",
                                 "size": [300, 60], "fontSize": 40, "text": "#cta", "localize": "#cta"})
        data["containers"][0]["children"] += ["close", "ctaLabel"]
        result = self.check(data)
        self.assertNotIn("polish_touch_target", codes(result["errors"]))
        self.assertNotIn("polish_button_role", codes(result["warnings"]))

    def test_section_title_pill_is_not_a_button(self):
        self.assertFalse(usc._is_interactive({"template": "ButtonTitleTemplate"}))
        self.assertTrue(usc._is_interactive({"template": "TextLabel", "interactive": True}))
        self.assertFalse(usc._is_interactive({"template": "ButtonBig", "interactive": False}))


class CtaTests(KitSandbox):
    def test_two_primary_ctas_block(self):
        data = spec()
        element(data, "later")["role"] = "primary"
        self.assertIn("polish_multiple_primary", codes(self.check(data)["errors"]))

    def test_primary_buttons_inside_a_repeated_list_do_not_count(self):
        data = spec()
        data["containers"].append({"id": "packs", "type": "col", "parent": "content", "scroll": "vertical",
                                   "size": [900, 1200], "children": ["packBuy1", "packBuy2"]})
        data["containers"][0]["children"].append("packs")
        for node_id in ("packBuy1", "packBuy2"):
            data["elements"].append({"id": node_id, "template": "ButtonBig", "parent": "packs",
                                     "size": [360, 160], "role": "primary"})
        result = self.check(data)
        self.assertNotIn("polish_multiple_primary", codes(result["errors"]))
        self.assertNotIn("polish_button_pair_stacked", codes(result["errors"]))

    def test_stacked_action_pair_blocks(self):
        data = spec()
        data["containers"][1]["type"] = "col"
        self.assertIn("polish_button_pair_stacked", codes(self.check(data)["errors"]))

    def test_cta_height_and_missing_role_warn(self):
        data = spec()
        element(data, "buy")["size"] = [360, 200]
        del element(data, "later")["role"]
        result = self.check(data)
        self.assertTrue(result["ok"], result["errors"])
        self.assertIn("polish_cta_height", codes(result["warnings"]))
        self.assertIn("polish_button_role", codes(result["warnings"]))

    def test_unknown_role_warns(self):
        data = spec()
        element(data, "later")["role"] = "fancy"
        self.assertIn("polish_role_unknown", codes(self.check(data)["warnings"]))


class ReviewRegressionTests(KitSandbox):
    """Các ca reviewer độc lập bắt được — giữ để không tái phát."""

    def test_vertical_menu_of_secondary_buttons_is_not_a_stacked_pair(self):
        data = spec()
        data["containers"][1]["type"] = "col"
        element(data, "buy")["role"] = "secondary"
        result = self.check(data)
        self.assertNotIn("polish_button_pair_stacked", codes(result["errors"]))

    def test_pair_with_different_heights_warns(self):
        data = spec()
        element(data, "later")["size"] = [360, 128]
        self.assertIn("polish_button_pair_height", codes(self.check(data)["warnings"]))

    def test_template_chrome_and_cheat_nodes_are_not_graded(self):
        data = spec()
        data["containers"][0]["children"] += ["back", "CheatMenu"]
        data["containers"].append({"id": "CheatMenu", "type": "col", "parent": "content", "children": ["cheatBtn"]})
        data["elements"] += [
            {"id": "back", "template": "button_small", "parent": "content", "baseChrome": True,
             "fontSize": 24, "text": "#back", "localize": "#back"},
            {"id": "cheatBtn", "template": "button_small", "parent": "CheatMenu", "size": [96, 64]},
        ]
        result = self.check(data)
        found = codes(result["errors"]) | codes(result["warnings"])
        self.assertFalse({"polish_font_size", "polish_touch_target", "polish_button_role"} & found, found)

    def test_malformed_role_and_children_do_not_crash(self):
        data = spec()
        element(data, "later")["role"] = ["primary"]
        data["containers"].append({"id": "odd", "type": "col", "parent": "content", "children": 5})
        data["containers"][0]["children"].append("odd")
        result = self.check(data)
        self.assertIn("polish_role_unknown", codes(result["warnings"]))
        self.assertFalse(result["ok"])  # children=5 is a structural error, reported — not a crash

    def test_layout_driven_zero_axis_is_not_a_tiny_target(self):
        data = spec()
        element(data, "later")["size"] = [0, 160]
        self.assertNotIn("polish_touch_target", codes(self.check(data)["errors"]))


class CapabilityFallbackTests(unittest.TestCase):
    def test_old_pipeline_scripts_keep_legacy_resolution(self):
        with mock.patch.object(usc, "_pipeline_supports_design_resolution", lambda: False):
            self.assertEqual(usc.design_resolution(), [1080, 1920])
            self.assertEqual(usc.supported_resolutions(), [[1080, 1920]])

    def test_current_scripts_are_resolution_aware(self):
        self.assertTrue(usc._pipeline_supports_design_resolution())


class SoftRuleTests(KitSandbox):
    def test_off_grid_spacing_warns(self):
        # Lưới mềm 4 px (PolishRules §9.1): 18 lệch lưới, 12 / 20 nằm trên lưới.
        data = spec()
        data["containers"][0]["gap"] = 18
        result = self.check(data)
        self.assertTrue(result["ok"], result["errors"])
        self.assertIn("polish_grid", codes(result["warnings"]))

    def test_four_px_steps_are_on_grid(self):
        data = spec()
        data["containers"][0]["gap"] = 12
        data["containers"][0]["padding"] = [20, 20, 20, 20]
        result = self.check(data)
        self.assertNotIn("polish_grid", codes(result["warnings"]))

    def test_default_grid_step_is_four(self):
        self.assertEqual(usc.POLISH_DEFAULTS["gridStep"], 4)

    def test_long_button_label_warns(self):
        data = spec()
        element(data, "buy")["text"] = "Buy the legendary bundle right now"
        element(data, "buy")["localize"] = "dynamic"
        self.assertIn("polish_text_room", codes(self.check(data)["warnings"]))

    def test_dev_only_screen_is_skipped(self):
        data = spec(screen="CheatPanel")
        element(data, "title")["fontSize"] = 12
        result = self.check(data)
        self.assertFalse([c for c in codes(result["errors"]) | codes(result["warnings"]) if c.startswith("polish_")])


class WarnGateTests(KitSandbox):
    polish = {"specGate": "warn"}

    def test_warn_gate_never_blocks(self):
        data = spec()
        element(data, "title")["fontSize"] = 20
        element(data, "later")["role"] = "primary"
        result = self.check(data, mode="approve")
        self.assertTrue(result["ok"], result["errors"])
        self.assertTrue({"polish_font_size", "polish_multiple_primary"} <= codes(result["warnings"]))


class OffGateTests(KitSandbox):
    polish = {"specGate": "off"}

    def test_off_gate_reports_nothing(self):
        data = spec()
        element(data, "title")["fontSize"] = 20
        result = self.check(data, mode="approve")
        self.assertTrue(result["ok"], result["errors"])
        self.assertFalse([c for c in codes(result["warnings"]) if c.startswith("polish_")])


class ResolutionTests(KitSandbox):
    def test_project_resolution_is_clean(self):
        self.assertNotIn("resolution_legacy", codes(self.check(spec())["warnings"]))

    def test_legacy_1920_spec_stays_valid_with_a_warning(self):
        result = self.check(spec(designResolution=[1080, 1920]), mode="approve")
        self.assertTrue(result["ok"], result["errors"])
        self.assertIn("resolution_legacy", codes(result["warnings"]))

    def test_unsupported_resolution_blocks(self):
        result = self.check(spec(designResolution=[720, 1280]))
        self.assertIn("resolution", codes(result["errors"]))

    def test_spec_resolution_falls_back_to_legacy(self):
        self.assertEqual(usc.spec_resolution({"designResolution": [1080, 2400]}), [1080, 2400])
        self.assertEqual(usc.spec_resolution({}), [1080, 1920])
        self.assertEqual(usc.spec_resolution({"designResolution": [2400, 1080]}), [1080, 1920])


class ProjectResolutionTests(KitSandbox):
    design = [1080, 2340]

    def test_custom_project_resolution_is_accepted_and_preferred(self):
        self.assertEqual(usc.supported_resolutions()[0], [1080, 2340])
        self.assertTrue(self.check(spec(designResolution=[1080, 2340]))["ok"])
        self.assertIn("resolution_legacy", codes(self.check(spec())["warnings"]))


class DesignResolutionDetectionTests(unittest.TestCase):
    def setUp(self):
        self.tmp = tempfile.TemporaryDirectory()
        self.addCleanup(self.tmp.cleanup)
        self.templates = Path(self.tmp.name) / "Templates"
        (self.templates / "Popup_Template").mkdir(parents=True)

    def detect(self, configured=None, reference=None):
        if reference is not None:
            (self.templates / "Popup_Template" / "screen_template.prefab").write_text(
                "CanvasScaler:\n  m_ReferenceResolution: {x: %s, y: %s}\n" % reference, encoding="utf-8")
        fake_profile = mock.Mock()
        fake_profile.get.side_effect = lambda key, default=None: configured if key == "designResolution" else default
        with mock.patch.object(usc, "TEMPLATES_DIR", self.templates), \
                mock.patch.object(usc, "profile", lambda: fake_profile):
            return usc.design_resolution()

    def test_profile_value_wins(self):
        self.assertEqual(self.detect(configured=[1080, 2340], reference=(1080, 1920)), [1080, 2340])

    def test_screen_template_canvas_scaler_is_detected(self):
        self.assertEqual(self.detect(configured=[], reference=(1080, 1920)), [1080, 1920])

    def test_landscape_or_missing_falls_back_to_ezg_standard(self):
        self.assertEqual(self.detect(configured=[], reference=(1920, 1080)), [1080, 2400])
        self.assertEqual(self.detect(configured=[]), [1080, 2400])


class PolishSettingsTests(unittest.TestCase):
    def settings(self, raw):
        fake_profile = mock.Mock()
        fake_profile.get.side_effect = lambda key, default=None: raw if key == "polish" else default
        with mock.patch.object(usc, "profile", lambda: fake_profile):
            return usc._polish_settings()

    def test_bad_values_fall_back_to_defaults(self):
        result = self.settings({"specGate": "maybe", "minTouchTarget": -5, "ctaHeight": [200, 100]})
        self.assertEqual(result["specGate"], "block")
        self.assertEqual(result["minTouchTarget"], 128)
        self.assertEqual(result["ctaHeight"], [152, 176])

    def test_string_shorthand_sets_the_gate(self):
        self.assertEqual(self.settings("off")["specGate"], "off")
        self.assertEqual(self.settings("nonsense")["specGate"], "block")

    def test_project_overrides_apply(self):
        result = self.settings({"specGate": "warn", "minFontSize": 36})
        self.assertEqual((result["specGate"], result["minFontSize"], result["hardMinFontSize"]), ("warn", 36, 30))


class RenderStageTests(KitSandbox):
    def test_stage_follows_spec_resolution(self):
        render = importlib.import_module("ui-spec-render")
        with mock.patch.object(render, "KIT_CSS", usc.KIT_CSS), \
                mock.patch.object(render, "kit_hash", lambda: "0" * 64):
            tall = render.render(spec())
            legacy = render.render(spec(designResolution=[1080, 1920]))
        self.assertIn('style="--design-w:1080px;--design-h:2400px;width:1080px;height:2400px"', tall)
        self.assertIn('style="--design-w:1080px;--design-h:1920px;width:1080px;height:1920px"', legacy)


def _png(path: Path, width: int, height: int):
    raw = b"".join(b"\x00" + b"\x00\x00\x00" * width for _ in range(height))
    chunk = lambda kind, data: (struct.pack(">I", len(data)) + kind + data
                                + struct.pack(">I", zlib.crc32(kind + data) & 0xFFFFFFFF))
    path.write_bytes(b"\x89PNG\r\n\x1a\n" + chunk(b"IHDR", struct.pack(">IIBBBBB", width, height, 8, 2, 0, 0, 0))
                     + chunk(b"IDAT", zlib.compress(raw)) + chunk(b"IEND", b""))


class VisualDiffTests(unittest.TestCase):
    def run_diff(self, ref_size, actual_size):
        try:
            import PIL  # noqa: F401
        except ImportError:
            self.skipTest("Pillow not installed")
        with tempfile.TemporaryDirectory() as tmp:
            tmp = Path(tmp)
            _png(tmp / "ref.png", *ref_size)
            _png(tmp / "act.png", *actual_size)
            proc = subprocess.run(
                [sys.executable, str(SCRIPTS / "ui-visual-diff.py"), str(tmp / "ref.png"), str(tmp / "act.png"),
                 "--output", str(tmp / "diff.json")], capture_output=True, text=True)
            return proc.returncode, json.loads(proc.stdout)

    def test_matching_design_resolution_pair_passes(self):
        code, payload = self.run_diff((1080, 2400), (1080, 2400))
        self.assertEqual(code, 0, payload)
        self.assertEqual(payload["resolution"], [1080, 2400])

    def test_legacy_pair_still_passes(self):
        self.assertEqual(self.run_diff((1080, 1920), (1080, 1920))[0], 0)

    def test_size_mismatch_fails(self):
        code, payload = self.run_diff((1080, 2400), (1080, 1920))
        self.assertEqual(code, 1)
        self.assertIn("differ in size", payload["error"])


if __name__ == "__main__":
    unittest.main()
