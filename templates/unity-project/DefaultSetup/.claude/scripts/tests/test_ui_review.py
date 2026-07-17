import importlib.util
import hashlib
import json
import os
import sys
import tempfile
import threading
import unittest
from pathlib import Path
from urllib import error, request
from unittest import mock


SCRIPTS = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(SCRIPTS))
import ui_spec_common as common
from ui_spec_common import validate_spec

SPEC = importlib.util.spec_from_file_location("ui_review", SCRIPTS / "ui-review.py")
review = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(review)


def sample_spec():
    return {
        "specVersion": 1,
        "screen": "TestPopup",
        "feature": "TestFeature",
        "branch": "Popup",
        "designResolution": [1080, 2400],
        "contentRoot": "content",
        "containers": [{"id": "content", "type": "col", "children": []}],
        "elements": [],
        "assumptions": [],
        "questions": [],
    }


class UIReviewTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.root = Path(self.temp.name)
        self.catalog_dir = self.root / "ui-catalog"
        self.catalog_dir.mkdir()
        catalog_bytes = b'{"tokens":[]}\n'
        (self.catalog_dir / "ui-tokens.json").write_bytes(catalog_bytes)
        source_hash = hashlib.sha256(catalog_bytes + b"\0").hexdigest()
        (self.catalog_dir / "ui-kit.json").write_text(json.dumps({
            "_meta": {"designResolution": [1080, 2400], "sourceHash": source_hash},
            "templates": {},
        }))
        (self.catalog_dir / "ui-kit.css").write_text(
            ".stage{position:relative;width:1080px;height:2400px}.dim{position:absolute;inset:0}"
        )
        self.mockups = self.root / "TechSpec/Mockups"
        self.feature = self.mockups / "TestFeature"
        self.feature.mkdir(parents=True)
        self.spec = sample_spec()
        self.spec_path = self.feature / "TestPopup.ui-spec.json"
        self.html = self.feature / "TestPopup.html"
        self.spec_path.write_text(json.dumps(self.spec))
        self.html.write_text('<script id="spec">' + json.dumps(self.spec) + "</script>")
        self.patch = mock.patch.multiple(
            review,
            ROOT=self.root,
            MOCKUPS_ROOT=self.mockups,
            DASHBOARD_PATH=self.mockups / "_ui-review.html",
            REGEN_QUEUE=self.mockups / "_regen-queue.jsonl",
            CLAUDE_GLOBAL_CONFIG=self.root / ".claude.json",
        )
        self.patch.start()
        self.common_patch = mock.patch.multiple(
            common,
            ROOT=self.root,
            CATALOG_JSON=self.catalog_dir / "ui-tokens.json",
            KIT_JSON=self.catalog_dir / "ui-kit.json",
            KIT_CSS=self.catalog_dir / "ui-kit.css",
        )
        self.common_patch.start()
        self.env_patch = mock.patch.dict(os.environ, {"UI_PIPELINE_ROOT": str(self.root)})
        self.env_patch.start()

    def tearDown(self):
        self.env_patch.stop()
        self.common_patch.stop()
        self.patch.stop()
        self.temp.cleanup()

    def test_dashboard_has_direct_approval_refresh_and_ai_regenerate(self):
        source, screens = review.render_dashboard_html({"trusted": True, "model": "Default", "effort": "Default", "serve": True, "token": "x"})
        self.assertEqual(1, len(screens))
        self.assertIn("/api/approve", source)
        self.assertIn("/api/screens", source)
        self.assertIn("/api/regenerate", source)
        self.assertIn("/api/apply-decisions", source)
        self.assertIn("Apply choices", source)
        self.assertIn("Apply + AI", source)
        self.assertIn("s.strict&&", source)
        self.assertIn("Approve all", source)
        self.assertIn("↻ Refresh", source)
        self.assertIn("Local service · direct approval", source)

    def test_discovery_skips_transient_invalid_spec_from_parallel_planning(self):
        self.spec_path.write_text("{")
        self.assertEqual([], review.discover_screens())

    def test_loopback_api_requires_token_and_routes_bounded_batch(self):
        token = "test-token"
        server = review.create_review_server(0, token, {"trusted": True}, False)
        port = server.server_address[1]
        thread = threading.Thread(target=server.serve_forever, daemon=True)
        thread.start()
        try:
            with self.assertRaises(error.HTTPError) as denied:
                request.urlopen(f"http://127.0.0.1:{port}/api/screens")
            self.assertEqual(403, denied.exception.code)

            screens_request = request.Request(
                f"http://127.0.0.1:{port}/api/screens",
                headers={"X-Review-Token": token},
            )
            screens = json.loads(request.urlopen(screens_request).read())
            self.assertEqual(1, len(screens["screens"]))

            with self.assertRaises(error.HTTPError) as forbidden:
                request.urlopen(f"http://127.0.0.1:{port}/CLAUDE.md")
            self.assertEqual(403, forbidden.exception.code)

            result = {"ok": True, "approved": [{"html": "x"}], "errors": [], "screens": []}
            body = json.dumps({"items": [{"html": "x", "specHash": "0" * 64}]}).encode()
            approve_request = request.Request(
                f"http://127.0.0.1:{port}/api/approve",
                data=body,
                method="POST",
                headers={"Content-Type": "application/json", "X-Review-Token": token},
            )
            with mock.patch.object(review, "approve_items", return_value=result) as approve:
                payload = json.loads(request.urlopen(approve_request).read())
            self.assertTrue(payload["ok"])
            approve.assert_called_once()
        finally:
            server.shutdown()
            server.server_close()
            thread.join(timeout=2)

    def test_regenerate_agent_does_not_bypass_permissions(self):
        html_rel = "TechSpec/Mockups/TestFeature/TestPopup.html"
        with mock.patch.object(review, "find_claude", return_value="/fake/claude"), mock.patch.object(review.subprocess, "Popen") as popen:
            popen.return_value.pid = 4242  # real Popen always yields an int pid; the status file needs one
            result = review.spawn_regen(html_rel, review.current_hash(self.html), "move button")
        command = popen.call_args.args[0]
        self.assertTrue(result["auto"])
        self.assertIn("acceptEdits", command)
        self.assertNotIn("bypassPermissions", command)
        self.assertIn("--allowedTools", command)
        # user-chosen tuning: at least Sonnet + effort xhigh (never haiku), stream-json for live progress
        self.assertEqual(command[command.index("--model") + 1], review.REGEN_MODEL)
        self.assertEqual(command[command.index("--effort") + 1], "xhigh")
        self.assertNotIn("haiku", " ".join(command).lower())
        self.assertIn("stream-json", command)
        # PID + expected hash are tracked so the dashboard can show progress and detect a crash
        status = review.load_json_object(review.regen_status_path(html_rel))
        self.assertEqual(status.get("pid"), 4242)
        self.assertEqual(status.get("state"), "running")
        review.regen_status_path(html_rel).unlink(missing_ok=True)

    def test_regenerate_queue_rejects_stale_hash(self):
        with self.assertRaisesRegex(ValueError, "Hash changed"):
            review.queue_regen("TechSpec/Mockups/TestFeature/TestPopup.html", "0" * 64, "move button")

    def test_structured_option_is_browser_safe_and_marked_instant(self):
        self.spec["questions"] = [{
            "q": "Gap nào?",
            "options": [
                {"label": "Gap 12", "patch": [{"op": "add", "path": "/containers/@content/gap", "value": 12}]},
                "Tự cân layout",
            ],
        }]
        record = review.normalize_questions(self.spec["questions"])[0]
        self.assertEqual({"label": "Gap 12", "instant": True}, record["options"][0])
        self.assertEqual({"label": "Tự cân layout", "instant": False}, record["options"][1])
        self.assertNotIn("patch", record["options"][0])

    def test_screen_record_marks_legacy_for_compatible_approval(self):
        legacy = {**self.spec, "specVersion": 0, "questions": ["old unresolved question"]}
        record = review.screen_record(legacy, self.html, "TechSpec/Mockups/TestFeature/TestPopup.html")
        self.assertFalse(record["strict"])

    def test_apply_json_patch_restricts_identity_fields(self):
        with self.assertRaisesRegex(ValueError, "must stay under"):
            review.apply_json_patch(self.spec, [{"op": "replace", "path": "/screen", "value": "Other"}])

    def test_apply_decisions_patches_spec_removes_question_and_skips_ai(self):
        self.spec["mockupLane"] = "kit-composition"
        self.spec["questions"] = [{
            "q": "Gap nào?",
            "options": [{
                "label": "Gap 12",
                "patch": [{"op": "add", "path": "/containers/@content/gap", "value": 12}],
            }],
        }]
        self.spec_path.write_text(json.dumps(self.spec))
        self.html.write_text('<script id="spec">' + json.dumps(self.spec) + "</script>")
        expected = review.current_hash(self.html)

        result = review.apply_decisions(
            "TechSpec/Mockups/TestFeature/TestPopup.html",
            expected,
            [{"questionIndex": 0, "optionIndex": 0}],
        )
        updated = json.loads(self.spec_path.read_text())
        self.assertTrue(result["ok"])
        self.assertEqual(12, updated["containers"][0]["gap"])
        self.assertEqual([], updated["questions"])
        self.assertEqual(updated, review.load_spec(self.html, prefer_sidecar=False)[0])

    def test_validator_accepts_structured_patch_shape_in_draft_mode(self):
        self.spec["mockupLane"] = "kit-composition"
        self.spec["questions"] = [{
            "q": "Gap nào?",
            "options": [{
                "label": "Gap 12",
                "patch": [{"op": "add", "path": "/containers/@content/gap", "value": 12}],
            }],
        }]
        result = validate_spec(self.spec, mode="draft")
        question_errors = [entry for entry in result["errors"] if entry["code"] == "questions"]
        self.assertEqual([], question_errors)
        self.assertTrue(any(entry["code"] == "unresolved_questions" for entry in result["warnings"]))

    def test_new_draft_validation_requires_fast_lane_without_breaking_legacy_v1(self):
        compatible = validate_spec(self.spec, mode="draft")
        strict_new = validate_spec(self.spec, mode="draft", require_lane=True)
        self.assertFalse(any(entry["code"] == "mockup_lane" for entry in compatible["errors"]))
        self.assertTrue(any(entry["code"] == "mockup_lane" for entry in strict_new["errors"]))

    def test_validator_rejects_structured_option_with_missing_node(self):
        self.spec["mockupLane"] = "kit-composition"
        self.spec["questions"] = [{
            "q": "Gap nào?",
            "options": [{
                "label": "Broken",
                "patch": [{"op": "replace", "path": "/containers/@missing/gap", "value": 12}],
            }],
        }]
        result = validate_spec(self.spec, mode="draft", require_lane=True)
        self.assertTrue(any("cannot apply" in entry["message"] for entry in result["errors"]))

    def test_validator_rejects_structured_option_that_breaks_ui_invariants(self):
        self.spec["mockupLane"] = "kit-composition"
        self.spec["questions"] = [{
            "q": "Gap nào?",
            "options": [{
                "label": "Invalid negative gap",
                "patch": [{"op": "add", "path": "/containers/@content/gap", "value": -1}],
            }],
        }]
        result = validate_spec(self.spec, mode="draft", require_lane=True)
        self.assertTrue(any("produces invalid UI" in entry["message"] for entry in result["errors"]))

    def test_apply_decisions_rolls_back_when_render_fails(self):
        self.spec["questions"] = [{
            "q": "Gap nào?",
            "options": [{"label": "Gap 12", "patch": [{"op": "add", "path": "/containers/@content/gap", "value": 12}]}],
        }]
        self.spec_path.write_text(json.dumps(self.spec))
        self.html.write_text('<script id="spec">' + json.dumps(self.spec) + "</script>")
        old_spec, old_html = self.spec_path.read_bytes(), self.html.read_bytes()
        with mock.patch.object(review, "run_json", side_effect=RuntimeError("render failed")):
            with self.assertRaisesRegex(RuntimeError, "render failed"):
                review.apply_decisions(
                    "TechSpec/Mockups/TestFeature/TestPopup.html",
                    review.current_hash(self.html),
                    [{"questionIndex": 0, "optionIndex": 0}],
                )
        self.assertEqual(old_spec, self.spec_path.read_bytes())
        self.assertEqual(old_html, self.html.read_bytes())

    def test_apply_decisions_endpoint_is_token_guarded(self):
        token = "test-token"
        server = review.create_review_server(0, token, {"trusted": True}, False)
        port = server.server_address[1]
        thread = threading.Thread(target=server.serve_forever, daemon=True)
        thread.start()
        try:
            body = json.dumps({
                "html": "TechSpec/Mockups/TestFeature/TestPopup.html",
                "specHash": "0" * 64,
                "decisions": [{"questionIndex": 0, "optionIndex": 0}],
            }).encode()
            req = request.Request(
                f"http://127.0.0.1:{port}/api/apply-decisions",
                data=body,
                method="POST",
                headers={"Content-Type": "application/json", "X-Review-Token": token},
            )
            payload = {"ok": True, "applied": 1, "screens": []}
            with mock.patch.object(review, "apply_decisions", return_value=payload) as apply:
                result = json.loads(request.urlopen(req).read())
            self.assertTrue(result["ok"])
            apply.assert_called_once()
        finally:
            server.shutdown()
            server.server_close()
            thread.join(timeout=2)


if __name__ == "__main__":
    unittest.main()
