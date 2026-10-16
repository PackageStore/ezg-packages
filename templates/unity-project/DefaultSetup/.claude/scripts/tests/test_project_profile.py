"""Tests for project_profile.py — merge semantics of the per-project knobs.

Trọng tâm là các key merge đệ quy (DEEP_MERGE_KEYS: backend, bugHub, qa): project chỉ ghi
một lá thì mọi lá anh em vẫn ra default, list THAY THẾ chứ không nối, DEFAULTS không bị
mutate giữa các lần gọi. CLI `project_profile.py qa` chạy thật qua subprocess với một
profile tạm đặt cạnh bản copy của script (đúng thứ tự tìm của profile()).

Run from the repo root:
    python3 -m unittest discover -s .claude/scripts/tests -v
"""

import copy
import importlib.util
import json
import shutil
import subprocess
import sys
import tempfile
import unittest
from pathlib import Path

REPO = Path(__file__).resolve().parents[3]
SCRIPTS = REPO / ".claude" / "scripts"
MODULE_PATH = SCRIPTS / "project_profile.py"


def load_module():
    spec = importlib.util.spec_from_file_location("project_profile_under_test", MODULE_PATH)
    mod = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(mod)
    return mod


class DeepMergeTests(unittest.TestCase):
    def setUp(self):
        self.pp = load_module()

    def test_defaults_when_profile_empty(self):
        p = self.pp.Profile({})
        self.assertEqual(p.qa, self.pp.DEFAULTS["qa"])
        self.assertEqual(p.qa["schemaVersion"], 1)
        self.assertEqual(p.qa_status, "template")
        self.assertEqual(p.backend, self.pp.DEFAULTS["backend"])
        self.assertEqual(p.bug_hub, self.pp.DEFAULTS["bugHub"])

    def test_leaf_override_keeps_siblings(self):
        p = self.pp.Profile({"qa": {"status": "draft", "conventions": {"cheatTypeName": "GameCheat"}}})
        qa = p.qa
        self.assertEqual(qa["status"], "draft")
        self.assertEqual(qa["conventions"]["cheatTypeName"], "GameCheat")
        # Lá anh em vẫn là default.
        self.assertEqual(qa["conventions"]["cheatFlagMember"], "isCheat")
        self.assertEqual(qa["paths"]["chartersDir"], ".claude/docs/qa/charters")
        self.assertEqual(qa["liveness"]["deadFeaturePatterns"], ["_Retired$"])

    def test_lists_replace_not_append(self):
        p = self.pp.Profile({"qa": {"conventions": {"screenBaseTypes": ["MyScreenBase"]},
                                    "liveness": {"deadFeaturePatterns": []}}})
        self.assertEqual(p.qa_conventions["screenBaseTypes"], ["MyScreenBase"])
        self.assertEqual(p.qa["liveness"]["deadFeaturePatterns"], [])

    def test_nested_dict_accrues(self):
        # Tool ghi tri thức vào observed/verified — dict con merge, không mất default rỗng khác.
        p = self.pp.Profile({"qa": {"liveness": {"observed": {"Shop": "2026-10-16"}},
                                    "verified": {"cheats": {"Cheat_AddGold": "2026-10-16"}}}})
        self.assertEqual(p.qa["liveness"]["observed"], {"Shop": "2026-10-16"})
        self.assertEqual(p.qa["liveness"]["forceLive"], [])
        self.assertEqual(p.qa["verified"]["cheats"], {"Cheat_AddGold": "2026-10-16"})
        self.assertEqual(p.qa["verified"]["statePaths"], {})

    def test_unknown_keys_pass_through(self):
        p = self.pp.Profile({"qa": {"extra": {"k": 1}, "noise": [{"flag": "text-overflow", "match": "screen:Shop"}]}})
        self.assertEqual(p.qa["extra"], {"k": 1})
        self.assertEqual(p.qa["noise"][0]["flag"], "text-overflow")

    def test_defaults_not_mutated(self):
        before = copy.deepcopy(self.pp.DEFAULTS)
        p = self.pp.Profile({"qa": {"liveness": {"observed": {"X": "d"}}}, "bugHub": {"watch": {"branch": "B"}}})
        p.qa["liveness"]["observed"]["Y"] = "e"
        p.qa["conventions"]["screenBaseTypes"].append("Z")
        p.bug_hub["watch"]["branch"] = "C"
        _ = p.backend
        self.assertEqual(self.pp.DEFAULTS, before)
        # Mỗi lần get là bản mới: sửa bản đã lấy không rò sang lần sau.
        self.assertEqual(p.qa["liveness"]["observed"], {"X": "d"})
        self.assertEqual(p.bug_hub_watch["branch"], "B")

    def test_bughub_watch_merge_unchanged(self):
        p = self.pp.Profile({"bugHub": {"projectCode": "SM007", "watch": {"branch": "AutoFixBug"}}})
        self.assertEqual(p.bug_hub_project_code, "SM007")
        self.assertEqual(p.bug_hub_watch["branch"], "AutoFixBug")
        self.assertEqual(p.bug_hub_watch["baseBranch"], "")
        self.assertFalse(p.bug_hub_watch["mergeToBase"])

    def test_backend_merge_unchanged(self):
        p = self.pp.Profile({"backend": {"kind": "supabase"}})
        self.assertEqual(p.backend["kind"], "supabase")
        self.assertFalse(p.backend_direct_write_banned)
        self.assertEqual(p.backend_direct_write_pattern, "")

    def test_deep_merge_helper_scalar_and_none(self):
        dm = self.pp.deep_merge
        self.assertEqual(dm({"a": 1}, {"a": 2}), {"a": 2})
        self.assertEqual(dm({"a": [1, 2]}, {"a": [3]}), {"a": [3]})
        self.assertEqual(dm({"a": {"b": 1}}, {"a": {"c": 2}}), {"a": {"b": 1, "c": 2}})
        self.assertEqual(dm({"a": 1}, None), {"a": 1})
        self.assertEqual(dm(None, {"a": 1}), {"a": 1})


class CliTests(unittest.TestCase):
    def setUp(self):
        self.tmp = Path(tempfile.mkdtemp(prefix="pp-cli-"))
        scripts = self.tmp / ".claude" / "scripts"
        scripts.mkdir(parents=True)
        shutil.copy(MODULE_PATH, scripts / "project_profile.py")
        self.script = scripts / "project_profile.py"
        self.profile = self.tmp / ".claude" / "project-profile.json"

    def tearDown(self):
        shutil.rmtree(self.tmp, ignore_errors=True)

    def run_cli(self, *args):
        return subprocess.run([sys.executable, str(self.script), *args], cwd=self.tmp,
                              capture_output=True, encoding="utf-8")

    def test_qa_key_prints_merged_block(self):
        self.profile.write_text(json.dumps({"qa": {"status": "draft", "liveness": {"forceDead": ["Admin"]}}}),
                                encoding="utf-8")
        res = self.run_cli("qa")
        self.assertEqual(res.returncode, 0, res.stderr)
        out = json.loads(res.stdout)
        self.assertEqual(out["status"], "draft")
        self.assertEqual(out["liveness"]["forceDead"], ["Admin"])
        self.assertEqual(out["conventions"]["featuresEnum"], "GameEnums+Features")

    def test_qa_key_without_profile_is_defaults(self):
        res = self.run_cli("qa")
        self.assertEqual(res.returncode, 0, res.stderr)
        out = json.loads(res.stdout)
        self.assertEqual(out["status"], "template")
        self.assertEqual(out["paths"]["probeOverride"], "Assets/Resources/EzgQaProbeConfig.json")

    def test_dump_includes_qa(self):
        res = self.run_cli()
        self.assertEqual(res.returncode, 0, res.stderr)
        out = json.loads(res.stdout)
        self.assertIn("qa", out)
        self.assertIn("bugHub", out)


if __name__ == "__main__":
    unittest.main()
