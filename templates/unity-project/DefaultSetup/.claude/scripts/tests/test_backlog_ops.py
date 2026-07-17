import importlib.util
import tempfile
import unittest
from pathlib import Path
from unittest.mock import patch


SCRIPTS = Path(__file__).resolve().parents[1]
SPEC = importlib.util.spec_from_file_location("backlog_ops", SCRIPTS / "backlog-ops.py")
ops = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(ops)


class BacklogMockupTests(unittest.TestCase):
    def task(self, clone=None, pending=None):
        return {
            "src": Path("draft.md"),
            "clone_mockup": clone,
            "pending_mockup": pending,
        }

    def test_catalog_prefab_is_a_valid_clone_fast_lane(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            catalog = root / "ui-catalog"
            catalog.mkdir()
            (catalog / "ui-tokens.json").write_text(
                '{"tokens":[{"token":"screen.example","prefab":"screen_example"}]}'
            )
            with patch.object(ops, "ROOT", root):
                self.assertEqual([], ops.mockup_warnings([self.task(clone="screen_example")]))

    def test_unknown_clone_target_blocks_promotion(self):
        with tempfile.TemporaryDirectory() as directory, patch.object(ops, "ROOT", Path(directory)):
            warnings = ops.mockup_warnings([self.task(clone="definitely_missing_prefab")])
        self.assertEqual(1, len(warnings))
        self.assertIn("no matching prefab", warnings[0])

    def test_prefab_path_is_valid_when_catalog_is_not_exported_yet(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            prefab_dir = root / "Assets" / "_Project" / "Features" / "Example" / "Resources"
            prefab_dir.mkdir(parents=True)
            (prefab_dir / "screen_example.prefab").write_text("%YAML 1.1")
            with patch.object(ops, "ROOT", root):
                self.assertEqual([], ops.mockup_warnings([self.task(clone="screen_example")]))

    def test_duplicate_prefab_stem_requires_an_explicit_asset_path(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            first = root / "Assets" / "FeatureA" / "screen_example.prefab"
            second = root / "Assets" / "FeatureB" / "screen_example.prefab"
            first.parent.mkdir(parents=True)
            second.parent.mkdir(parents=True)
            first.write_text("%YAML 1.1")
            second.write_text("%YAML 1.1")
            with patch.object(ops, "ROOT", root):
                warnings = ops.mockup_warnings([self.task(clone="screen_example")])
                explicit = ops.mockup_warnings([self.task(clone="Assets/FeatureA/screen_example.prefab")])
        self.assertEqual(1, len(warnings))
        self.assertEqual([], explicit)

    def test_pending_state_still_blocks_promotion(self):
        warnings = ops.mockup_warnings([self.task(pending="PENDING-MOCKUP")])
        self.assertEqual(1, len(warnings))
        self.assertIn("still PENDING-MOCKUP", warnings[0])

    def test_parse_planning_carries_clone_target_to_preflight(self):
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / "20260717T010203004-M-admin-clone.md"
            path.write_text(
                "# [MEDIUM] Example clone\n\n"
                "**Tier:** M\n"
                "**Workflow args:** `ExampleClone | groundTruth=clone:screen_example`\n"
            )
            parsed = ops.parse_planning(path)
        self.assertEqual("screen_example", parsed["clone_mockup"])


if __name__ == "__main__":
    unittest.main()
