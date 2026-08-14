import importlib.util
import json
import struct
import subprocess
import sys
import tempfile
import threading
import unittest
from pathlib import Path
from urllib import error, request
from unittest import mock

SCRIPTS = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(SCRIPTS))
SPEC = importlib.util.spec_from_file_location("ui_review", SCRIPTS / "ui-review.py")
review = importlib.util.module_from_spec(SPEC); SPEC.loader.exec_module(review)


def sample_spec():
    return {"specVersion": 1, "screen": "TestPopup", "feature": "TestFeature", "branch": "Popup", "designResolution": [1080, 1920], "contentRoot": "content", "containers": [{"id": "content", "type": "col", "children": []}], "elements": [], "assumptions": [], "questions": []}


class UIReviewTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory(); self.root = Path(self.temp.name); self.mockups = self.root / "TechSpec/Mockups"; self.feature = self.mockups / "TestFeature"; self.feature.mkdir(parents=True)
        self.spec = sample_spec(); self.spec_path = self.feature / "TestPopup.ui-spec.json"; self.html = self.feature / "TestPopup.html"
        self.spec_path.write_text(json.dumps(self.spec)); self.html.write_text('<script id="spec">'+json.dumps(self.spec)+'</script>')
        self.patch = mock.patch.multiple(
            review,
            ROOT=self.root,
            # The backlog lives in the git common dir, not the worktree.
            BACKLOG_ROOT=self.root / ".git" / "backlog",
            PLANNING_DIR=self.root / ".git" / "backlog" / "planning",
            MOCKUPS_ROOT=self.mockups,
            DASHBOARD_PATH=self.mockups / "_ui-review.html",
            CLAUDE_GLOBAL_CONFIG=self.root / ".claude.json",
            CLAUDE_LOCAL_SETTINGS=self.root / ".claude/settings.local.json",
        ); self.patch.start()

    def tearDown(self): self.patch.stop(); self.temp.cleanup()

    def test_dashboard_contains_direct_approval_api_and_ai_edit_fallback(self):
        review.CLAUDE_GLOBAL_CONFIG.write_text(json.dumps({"projects":{"/other":{"hasTrustDialogAccepted":False}}}))
        review.CLAUDE_LOCAL_SETTINGS.parent.mkdir(parents=True)
        review.CLAUDE_LOCAL_SETTINGS.write_text(json.dumps({"permissions":{"allow":["Read"]}}))
        screens = review.generate_dashboard(); source = review.DASHBOARD_PATH.read_text(encoding="utf-8")
        self.assertEqual(1, len(screens)); self.assertIn("claude-cli://open", source); self.assertIn("Duyệt màn đã chọn", source); self.assertIn("Duyệt tất cả", source); self.assertIn("Regenerate", source); self.assertIn("· UI Mockups", source); self.assertNotIn("__PROJECT_TITLE__", source); self.assertIn("APPROVE_SCHEME=", source); self.assertIn("-approve\"", source); self.assertNotIn("__APPROVE_SCHEME__", source); self.assertIn("/api/approve", source); self.assertIn("/api/apply-decisions", source); self.assertIn("Apply choices", source); self.assertIn("const PROJECT=", source); self.assertIn("SERVER_MODE=!!CONFIG.serve", source)
        self.assertNotIn('"token"', source)
        self.assertIn('id="previewModal"', source); self.assertIn("openPreview", source); self.assertIn("data-close-modal", source); self.assertIn("key==='Escape'", source); self.assertNotIn('target="_blank"', source)
        global_config=json.loads(review.CLAUDE_GLOBAL_CONFIG.read_text()); local=json.loads(review.CLAUDE_LOCAL_SETTINGS.read_text())
        self.assertTrue(global_config["projects"][str(self.root.resolve())]["hasTrustDialogAccepted"])
        self.assertEqual("opus", local["model"]); self.assertEqual("xhigh", local["effortLevel"])
        self.assertEqual(["Read"], local["permissions"]["allow"]); self.assertIn("/other", global_config["projects"])

    def test_loopback_server_requires_token_and_routes_approval(self):
        token = "test-token"
        server = review.create_review_server(0, token)
        port = server.server_address[1]
        thread = threading.Thread(target=server.serve_forever, daemon=True)
        thread.start()
        try:
            with self.assertRaises(error.HTTPError) as denied:
                request.urlopen(f"http://127.0.0.1:{port}/api/screens")
            self.assertEqual(403, denied.exception.code)

            screens_request = request.Request(f"http://127.0.0.1:{port}/api/screens", headers={"X-UI-Review-Token": token})
            screens = json.loads(request.urlopen(screens_request).read())
            self.assertEqual(1, len(screens["screens"]))

            dashboard = request.urlopen(f"http://127.0.0.1:{port}/").read().decode()
            self.assertIn(token, dashboard)
            self.assertFalse(review.DASHBOARD_PATH.exists())

            with self.assertRaises(error.HTTPError) as outside:
                request.urlopen(f"http://127.0.0.1:{port}/CLAUDE.md")
            self.assertEqual(404, outside.exception.code)

            result = {"ok": True, "approved": [{"html": "x"}], "errors": [], "screens": []}
            body = json.dumps({"items": [{"html": "x", "specHash": "0" * 64}]}).encode()
            approve_request = request.Request(f"http://127.0.0.1:{port}/api/approve", data=body, method="POST", headers={"Content-Type": "application/json", "X-UI-Review-Token": token})
            with mock.patch.object(review, "approve_items", return_value=result) as approve, mock.patch.object(review, "generate_dashboard"):
                payload = json.loads(request.urlopen(approve_request).read())
            self.assertTrue(payload["ok"]); approve.assert_called_once()

            decision_body = json.dumps({"html": "x", "specHash": "0" * 64, "decisions": [{"questionIndex": 0, "optionIndex": 0}]}).encode()
            decision_request = request.Request(f"http://127.0.0.1:{port}/api/apply-decisions", data=decision_body, method="POST", headers={"Content-Type": "application/json", "X-UI-Review-Token": token})
            with mock.patch.object(review, "apply_decisions", return_value={"ok": True, "applied": 1, "screens": []}) as apply:
                decision = json.loads(request.urlopen(decision_request).read())
            self.assertTrue(decision["ok"]); apply.assert_called_once()
        finally:
            server.shutdown(); server.server_close(); thread.join(timeout=2)

    def test_matching_approval_removes_card_and_html_change_restores_it(self):
        png = self.html.with_suffix(".png"); png.write_bytes(b"png")
        review.atomic_json(review.approval_path(self.html), {"specHash": review.current_hash(self.html), "htmlSha256": review.sha256_file(self.html), "pngSha256": review.sha256_file(png)})
        self.assertEqual([], review.discover_screens()); self.html.write_text(self.html.read_text()+"\n<style>x{}</style>"); self.assertEqual(1, len(review.discover_screens()))

    def test_matching_approval_survives_crlf_checkout_and_legacy_crlf_evidence(self):
        png = self.html.with_suffix(".png"); png.write_bytes(b"png")
        # Evidence frozen from LF bytes (normalized format), then git autocrlf rewrites the checkout to CRLF.
        review.atomic_json(review.approval_path(self.html), {"specHash": review.current_hash(self.html), "htmlSha256": review.sha256_html(self.html), "pngSha256": review.sha256_file(png)})
        self.html.write_bytes(self.html.read_bytes().replace(b"\n", b"\r\n"))
        self.assertEqual([], review.discover_screens())
        # Legacy evidence frozen from raw CRLF bytes (pre-normalization) must stay valid too.
        review.atomic_json(review.approval_path(self.html), {"specHash": review.current_hash(self.html), "htmlSha256": review.sha256_file(self.html), "pngSha256": review.sha256_file(png)})
        self.assertEqual([], review.discover_screens())
        # A real content change must still resurface the card.
        self.html.write_text(self.html.read_text() + "\n<style>x{}</style>")
        self.assertEqual(1, len(review.discover_screens()))

    def test_discovery_skips_transient_invalid_spec_from_parallel_drafter(self):
        self.spec_path.write_text("{")
        self.assertEqual([], review.discover_screens())

    def test_approve_is_hash_bound_and_updates_task(self):
        rel = "TechSpec/Mockups/TestFeature/TestPopup.html"; planning = review.PLANNING_DIR; planning.mkdir(parents=True); task = planning / "task.md"; task.write_text(f"groundTruth=PENDING-APPROVAL:{rel}\n"); digest = review.current_hash(self.html)
        def fake_run(command, **kwargs):
            shot = next((x for x in command if str(x).startswith("--screenshot=")), None)
            if shot: Path(shot.split("=",1)[1]).write_bytes(b"\x89PNG\r\n\x1a\n"+struct.pack(">I",13)+b"IHDR"+struct.pack(">II",1080,1920)+b"\x08\x06\x00\x00\x00")
            return subprocess.CompletedProcess(command,0,"","")
        with mock.patch.object(review,"run_json",return_value={"ok":True,"specHash":digest}), mock.patch.object(review,"find_chrome",return_value="/fake/chrome"), mock.patch.object(review.subprocess,"run",side_effect=fake_run):
            with self.assertRaisesRegex(RuntimeError,"Hash changed"):
                review.approve_screen(rel,"0"*64,existing_png=True)
            result=review.approve_screen(rel,digest)
        self.assertEqual(digest,result["specHash"]); self.assertIn("groundTruth=TechSpec/Mockups/TestFeature/TestPopup.png",task.read_text()); self.assertEqual([],review.discover_screens())

    def test_structured_choice_is_browser_safe_and_applies_without_ai(self):
        self.spec["questions"] = [{
            "q": "Gap nào?",
            "options": [
                {"label": "Gap 12", "patch": [{"op": "add", "path": "/containers/@content/gap", "value": 12}]},
                "Tự cân layout",
            ],
        }]
        self.spec_path.write_text(json.dumps(self.spec)); self.html.write_text('<script id="spec">'+json.dumps(self.spec)+'</script>')
        record = review.normalize_questions(self.spec["questions"])[0]
        self.assertEqual({"label": "Gap 12", "instant": True}, record["options"][0])
        self.assertNotIn("patch", record["options"][0])

        expected = review.current_hash(self.html)
        def fake_run(_command):
            updated = json.loads(self.spec_path.read_text())
            return {"ok": True, "specHash": review.spec_hash(updated)}
        with mock.patch.object(review, "run_json", side_effect=fake_run):
            result = review.apply_decisions(
                "TechSpec/Mockups/TestFeature/TestPopup.html", expected,
                [{"questionIndex": 0, "optionIndex": 0}],
            )
        updated = json.loads(self.spec_path.read_text())
        self.assertTrue(result["ok"]); self.assertEqual(12, updated["containers"][0]["gap"]); self.assertEqual([], updated["questions"])

    def test_structured_patch_cannot_change_screen_identity(self):
        with self.assertRaisesRegex(ValueError, "must stay under"):
            review.apply_json_patch(self.spec, [{"op": "replace", "path": "/screen", "value": "Other"}])


if __name__ == "__main__": unittest.main()
