"""Luật ui-spec trên kit của base template (tên template thật: tab_template, panel_template…).

Kit giả chỉ chứa tên của base kit — không có thanh tab đáy hay title pill của kit cũ — nên
các cảnh báo chỉ dành cho kit cũ (tabbar_in_content, tabbar_empty_chrome, hình học pill)
phải im, còn các luật cùng ngữ nghĩa (toggle phải nằm trong tab strip, scroll / frame không
là element, chrome cùng cỡ) phải chạy.
"""

import json
import sys
import tempfile
import unittest
from pathlib import Path
from unittest import mock

SCRIPTS = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(SCRIPTS))

import ui_spec_common as usc  # noqa: E402


BASE_KIT = {
    "text_title": {"size": [0, 0], "texts": [{"fontSize": 50}]},
    "text_body": {"size": [0, 0], "texts": [{"fontSize": 40}]},
    "button_template_common_txt": {"size": [360, 180], "texts": []},
    "tab_template": {"size": [960, 159.75], "texts": []},
    "toggle_tab_template": {"size": [300, 200], "texts": []},
    "ScrollViewTemplate": {"size": [1080, 2400], "texts": []},
    "ScrollViewLoopTemplate": {"size": [1080, 2400], "texts": []},
    "panel_template": {"size": [0, 0], "texts": []},
    "FrameTemplate": {"size": [600, 250], "texts": []},
    "cooldown_timer_template": {"size": [0, 0], "texts": []},
}


def spec():
    return {
        "specVersion": 1,
        "screen": "InventoryPopup",
        "feature": "Inventory",
        "branch": "Popup",
        "designResolution": [1080, 2400],
        "contentRoot": "container_content",
        "containers": [
            {"id": "container_content", "type": "col", "gap": 24, "padding": [32, 32, 32, 32],
             "children": ["tabs", "pageA", "pageB"]},
            {"id": "tabs", "type": "row", "parent": "container_content", "tabBar": True,
             "size": [960, 160], "children": ["tabA", "tabB"]},
            {"id": "pageA", "type": "col", "parent": "container_content", "gap": 16, "children": ["bodyA"]},
            {"id": "pageB", "type": "col", "parent": "container_content", "gap": 16, "children": ["bodyB"]},
        ],
        "elements": [
            {"id": "tabA", "template": "toggle_tab_template", "parent": "tabs", "size": [300, 160],
             "role": "tab", "text": "#inventory_tab_a", "localize": "#inventory_tab_a"},
            {"id": "tabB", "template": "toggle_tab_template", "parent": "tabs", "size": [300, 160],
             "role": "tab", "text": "#inventory_tab_b", "localize": "#inventory_tab_b"},
            {"id": "bodyA", "template": "text_body", "parent": "pageA", "size": [800, 120],
             "fontSize": 40, "text": "#inventory_body_a", "localize": "#inventory_body_a"},
            {"id": "bodyB", "template": "text_body", "parent": "pageB", "size": [800, 120],
             "fontSize": 40, "text": "#inventory_body_b", "localize": "#inventory_body_b"},
        ],
        "assumptions": [],
        "questions": [],
    }


def codes(entries):
    return {entry["code"] for entry in entries}


class BaseKitSandbox(unittest.TestCase):
    def setUp(self):
        self.tmp = tempfile.TemporaryDirectory()
        root = Path(self.tmp.name)
        kit_json, kit_css = root / "ui-kit.json", root / "ui-kit.css"
        kit_json.write_text(json.dumps({"_meta": {"sourceHash": "fake"}, "templates": BASE_KIT}), encoding="utf-8")
        kit_css.write_text("/* kit */\n", encoding="utf-8")
        settings = dict(usc.POLISH_DEFAULTS)
        patches = [
            mock.patch.object(usc, "KIT_JSON", kit_json),
            mock.patch.object(usc, "KIT_CSS", kit_css),
            mock.patch.object(usc, "kit_source_hash", lambda: "fake"),
            mock.patch.object(usc, "design_resolution", lambda: [1080, 2400]),
            mock.patch.object(usc, "_polish_settings", lambda: settings),
        ]
        for patcher in patches:
            patcher.start()
            self.addCleanup(patcher.stop)
        self.addCleanup(self.tmp.cleanup)

    def check(self, data, mode="approve"):
        return usc.validate_spec(data, mode=mode)


class TabStripTests(BaseKitSandbox):
    def test_tab_strip_in_content_passes_without_bottom_bar_warning(self):
        result = self.check(spec())
        self.assertTrue(result["ok"], result["errors"])
        self.assertNotIn("tabbar_in_content", codes(result["warnings"]))

    def test_toggle_outside_a_tab_strip_is_blocking(self):
        data = spec()
        data["containers"][1]["tabBar"] = False
        result = self.check(data)
        self.assertIn("tabs_outside_bottom_bar", codes(result["errors"]))

    def test_strip_drawn_as_element_next_to_toggles_is_blocking(self):
        data = spec()
        data["containers"][0]["children"].append("strip")
        data["elements"].append({"id": "strip", "template": "tab_template", "parent": "container_content",
                                 "size": [960, 160]})
        result = self.check(data)
        self.assertIn("tabbar_as_element", codes(result["errors"]))

    def test_lone_strip_element_is_not_legacy_empty_chrome(self):
        data = spec()
        data["containers"] = [row for row in data["containers"] if row["id"] != "tabs"]
        data["containers"][0]["children"] = ["strip", "pageA", "pageB"]
        data["elements"] = [row for row in data["elements"] if row["parent"] != "tabs"]
        data["elements"].append({"id": "strip", "template": "tab_template", "parent": "container_content",
                                 "size": [960, 160]})
        result = self.check(data)
        self.assertNotIn("tabbar_empty_chrome", codes(result["warnings"]))


class ScrollAndSectionTests(BaseKitSandbox):
    def test_loop_scroll_template_as_element_is_blocking(self):
        data = spec()
        data["containers"][2]["children"].append("list")
        data["elements"].append({"id": "list", "template": "ScrollViewLoopTemplate", "parent": "pageA",
                                 "size": [900, 900]})
        result = self.check(data)
        self.assertIn("scroll_as_element", codes(result["errors"]))

    def test_panel_inside_a_section_is_a_frame_inside_a_frame(self):
        data = spec()
        data["containers"][2]["section"] = {"title": "Chỉ số", "localize": "#inventory_stats"}
        data["containers"][2]["children"].append("frame")
        data["elements"].append({"id": "frame", "template": "panel_template", "parent": "pageA",
                                 "anchor": "stretch", "offsets": [0, 0, 0, 0]})
        result = self.check(data)
        self.assertIn("section_frame_as_element", codes(result["errors"]))

    def test_section_without_pill_template_skips_pill_geometry(self):
        data = spec()
        data["containers"][2]["section"] = {"title": "Chỉ số", "localize": "#inventory_stats"}
        data["containers"][2]["padding"] = [16, 16, 16, 16]
        data["containers"][0]["gap"] = 8
        result = self.check(data)
        self.assertTrue(result["ok"], result["errors"])
        warned = codes(result["warnings"])
        self.assertNotIn("section_padding_top", warned)
        self.assertNotIn("section_parent_gap", warned)


class ChromeTests(BaseKitSandbox):
    def test_timers_at_two_sizes_warn(self):
        data = spec()
        data["containers"][2]["children"] += ["timerA", "timerB"]
        data["elements"] += [
            {"id": "timerA", "template": "cooldown_timer_template", "parent": "pageA", "size": [240, 60],
             "text": "01:00", "localize": "dynamic", "fontSize": 40},
            {"id": "timerB", "template": "cooldown_timer_template", "parent": "pageA", "size": [300, 60],
             "text": "02:00", "localize": "dynamic", "fontSize": 40},
        ]
        result = self.check(data)
        self.assertIn("inconsistent_chrome_size", codes(result["warnings"]))


if __name__ == "__main__":
    unittest.main()
