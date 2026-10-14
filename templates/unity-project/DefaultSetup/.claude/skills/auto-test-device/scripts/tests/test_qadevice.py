# -*- coding: utf-8 -*-
"""Test các phần thuần (không cần thiết bị) của qadevice.py.

Chạy: python3 -m unittest discover -s .claude/skills/auto-test-device/scripts/tests
"""
import json
import os
import sys
import tempfile
import threading
import unittest
from http.server import BaseHTTPRequestHandler, HTTPServer

sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
import qadevice as q  # noqa: E402

PKG = "com.example.game"

LOGCAT = """\
10-15 00:41:02.100  1234  1250 I Unity   : [EzgQaProbe] listening 127.0.0.1:7532 gate=open v1.0.0 source=launch:intent
10-15 00:41:03.200  1234  1250 E Unity   : NullReferenceException: Object reference not set to an instance of an object.
10-15 00:41:03.200  1234  1250 E Unity   :   at Shop.ShopController.OnBuy () [0x00012] in <abc>:0
10-15 00:41:03.200  1234  1250 E Unity   :   at UnityEngine.Events.InvokableCall.Invoke () [0x00010] in <def>:0
10-15 00:41:04.000  1234  1250 E Unity   : [Ads] load failed: no fill
10-15 00:41:04.500  9999  9999 E Unity   : other app error
10-15 00:41:05.000  1234  1250 W Unity   : The character with Unicode value \\u1EA1 was not found in the [Roboto SDF] font asset or any potential fallbacks.
10-15 00:41:06.000  1234  1234 E AndroidRuntime: FATAL EXCEPTION: main
10-15 00:41:06.000  1234  1234 E AndroidRuntime: Process: com.example.game, PID: 1234
10-15 00:41:06.000  1234  1234 E AndroidRuntime: java.lang.IllegalStateException: boom
10-15 00:41:06.001  1234  1234 E AndroidRuntime: 	at com.example.Foo.bar(Foo.java:10)
10-15 00:41:07.000   555   600 I ActivityManager: Process com.example.game (pid 1234) has died: fg  TOP
10-15 00:41:08.000   555   600 E ActivityManager: ANR in com.example.game (com.example.game/.MainActivity)
10-15 00:41:08.000   555   600 E ActivityManager: Reason: Input dispatching timed out
"""


class LogTests(unittest.TestCase):
    def setUp(self):
        self.records = q.group_logcat(LOGCAT.splitlines())

    def test_grouping_merges_multiline_unity_exception(self):
        exc = [r for r in self.records if r["tag"] == "Unity" and r["lines"][0].startswith("NullReference")]
        self.assertEqual(len(exc), 1)
        self.assertEqual(len(exc[0]["lines"]), 3)

    def test_classify_android(self):
        events, probe = q.classify_android(self.records, PKG, [1234])
        kinds = [e["kind"] for e in events]
        self.assertIn("exception", kinds)
        self.assertIn("error", kinds)            # [Ads] lỗi thường — medium
        self.assertIn("missing-glyph", kinds)
        self.assertIn("crash-java", kinds)
        self.assertIn("app-died", kinds)
        self.assertIn("anr", kinds)
        self.assertNotIn("other app error", [e["first"] for e in events])
        self.assertEqual(probe, [{"host": "127.0.0.1", "port": 7532, "gate": "open"}])
        crash = next(e for e in events if e["kind"] == "crash-java")
        self.assertIn("IllegalStateException", crash["first"])
        self.assertEqual(crash["severity"], "critical")

    def test_ignore_patterns(self):
        events, _ = q.classify_android(self.records, PKG, [1234], ignore=[r"\[Ads\]"])
        self.assertFalse(any("[Ads]" in e["first"] for e in events))

    def test_signature_stable_across_numbers(self):
        a = q.log_signature("exception", "Foo failed at 0x7f12 id 15", "at A.B () [0x00012]")
        b = q.log_signature("exception", "Foo failed at 0x9a00 id 99", "at A.B () [0x00034]")
        self.assertEqual(a, b)
        self.assertNotEqual(a, q.log_signature("error", "Foo failed at 0x7f12 id 15"))

    def test_plain_ios(self):
        lines = ["2026-10-15 00:00:01.000 Df Game[12:ab] NullReferenceException: boom",
                 "2026-10-15 00:00:02.000 Df Game[12:ab] *** Terminating app due to uncaught exception"]
        kinds = [e["kind"] for e in q.classify_plain(lines)]
        self.assertEqual(kinds, ["exception", "crash-native"])

    def test_probe_logs(self):
        entries = [{"type": "Log", "msg": "ok"}, {"type": "Exception", "msg": "InvalidOperationException: x", "stack": "A.B ()\nC.D ()"},
                   {"type": "Error", "msg": "Failed"}]
        ev = q.classify_probe_logs(entries)
        self.assertEqual([e["kind"] for e in ev], ["exception", "error"])


class AuditTests(unittest.TestCase):
    def test_audit_text(self):
        self.assertIsNone(q.audit_text("Mua ngay"))
        self.assertIsNone(q.audit_text("1.2K"))
        self.assertEqual(q.audit_text("shop_title_main")[0], "medium")
        self.assertEqual(q.audit_text("SHOP_TITLE")[0], "medium")
        self.assertEqual(q.audit_text("New Text")[0], "medium")
        self.assertEqual(q.audit_text("Reward {0}")[0], "high")
        self.assertEqual(q.audit_text("NaN")[0], "high")
        self.assertEqual(q.audit_text("System.Collections.Generic.List`1")[0], "high")

    def test_audit_elements(self):
        ui = {"screen": {"w": 1000, "h": 2000, "dpi": 400, "safe": [0, 80, 1000, 1880]}, "elements": [
            {"id": 1, "kind": "Text", "text": "Hello", "rect": [100, 100, 200, 50], "layer": "A", "path": "A/t1", "flags": ["overflow"]},
            {"id": 2, "kind": "Text", "text": "World", "rect": [110, 105, 200, 50], "layer": "A", "path": "A/t2"},
            {"id": 3, "kind": "Text", "text": "Other layer", "rect": [110, 105, 200, 50], "layer": "B", "path": "B/t3"},
            {"id": 4, "kind": "Button", "interactable": True, "rect": [950, 500, 200, 100], "path": "A/b"},
            {"id": 5, "kind": "Button", "interactable": True, "rect": [400, 10, 100, 60], "path": "A/top"},
            {"id": 6, "kind": "Button", "interactable": True, "rect": [400, 900, 30, 30], "path": "A/tiny"},
            {"id": 7, "kind": "Text", "text": "Go", "rect": [0, 0, 10, 10], "layer": "A", "path": "A/x", "flags": ["outsideContainer"], "container": "btn"},
        ]}
        found = {(f["id"], f["kind"]) for f in q.audit_elements(ui)}
        self.assertIn((1, "text-overflow"), found)
        self.assertIn((1, "text-overlap"), found)
        self.assertNotIn((3, "text-overlap"), found)       # khác lớp → không tính
        self.assertIn((4, "offscreen"), found)
        self.assertIn((5, "unsafe-area"), found)
        self.assertIn((6, "tiny-target"), found)
        self.assertIn((7, "text-outside"), found)


class CoordTests(unittest.TestCase):
    def sess(self):
        return q.Session(tempfile.mkdtemp(), {"view": {"w": 540, "h": 1170, "shot_w": 1080, "shot_h": 2340},
                                               "probe": {"screen_w": 540, "screen_h": 1170}})

    def test_view_to_device(self):
        s = self.sess()
        self.assertEqual(q.to_device_xy(s, 270, 585), (540, 1170))
        self.assertEqual(q.to_device_xy(s, "50%", "100%"), (540, 2339))
        self.assertEqual(q.to_device_xy(s, 100, 200, "device"), (100, 200))
        self.assertEqual(q.device_to_view(s, 540, 1170), (270, 585))

    def test_unity_scale(self):
        self.assertEqual(q.unity_scale(self.sess()), (0.5, 0.5))


class BugTests(unittest.TestCase):
    FORM = {"categories": ["Visual", "Logic", "GD"], "limits": {"maxTags": 4},
            "tags": [{"id": "1", "name": "Logic"}, {"id": "2", "name": "Visual"}, {"id": "3", "name": "Performance"},
                     {"id": "4", "name": "High"}, {"id": "5", "name": "Medium"}, {"id": "6", "name": "Low"}, {"id": "7", "name": "Improvement"}],
            "people": [{"id": "p1", "name": "Nguyễn Văn An"}]}

    def test_pickers(self):
        self.assertEqual(q.pick_category(self.FORM, "visual"), "Visual")
        self.assertEqual(q.pick_category(self.FORM, "crash"), "Logic")
        self.assertEqual(q.pick_category(self.FORM, "gd"), "GD")
        self.assertEqual(q.pick_tags(self.FORM, "perf", "critical"), ["3", "4"])
        self.assertEqual(q.pick_tags(self.FORM, "ux", "low"), ["7", "6"])
        self.assertEqual(q.pick_people(self.FORM, ["van an"]), ["p1"])

    def test_report_schema_and_limits(self):
        r = q.build_report({"title": "x" * 150, "actual": "a", "steps": "1. b", "tagIds": ["1"] * 9},
                           {"version": "1.0", "platform": "Android"}, {"id": "d", "ramMb": 4000, "screen": "1080x2340"},
                           extra={"k%d" % i: "v" for i in range(40)})
        self.assertEqual(r["schema"], 1)
        self.assertEqual(len(r["title"]), 100)
        self.assertEqual(len(r["tagIds"]), 4)
        self.assertEqual(len(r["extra"]), 32)
        self.assertRegex(r["id"], r"^[0-9a-f]{8}-[0-9a-f]{4}-4[0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$")
        self.assertRegex(r["createdAtUtc"], r"^\d{4}-\d\d-\d\dT\d\d:\d\d:\d\d\.\d{3}Z$")
        for key in ("version", "build", "commitSha", "branch", "platform", "unityVersion"):
            self.assertIn(key, r["app"])
        self.assertEqual(set(r["device"]), {"id", "model", "os", "ramMb", "screen"})

    def test_steps_format(self):
        steps = [{"n": 3, "action": "tap", "desc": "Bấm Shop"}, {"n": 4, "action": "tap", "xy": [10, 20]},
                 {"n": 5, "action": "background", "secs": 5}, {"n": 6, "action": "key", "key": "back", "desc": ""}]
        text = q.format_steps(steps, "Có 500 kim cương")
        self.assertEqual(text.splitlines()[0], "Điều kiện: Có 500 kim cương")
        self.assertIn("1. Bấm Shop", text)
        self.assertIn("2. Chạm toạ độ (10,20)", text)
        self.assertIn("3. Đưa game xuống nền 5s rồi mở lại", text)
        self.assertIn("4. Bấm phím back", text)

    def test_similarity(self):
        self.assertGreaterEqual(q.similar("Shop: mua gói trừ tiền 2 lần", "shop - Mua gói trừ tiền 2 lần!"), 0.99)
        self.assertLess(q.similar("Shop: mua gói trừ tiền 2 lần", "Nhiệm vụ: không nhận được thưởng"), 0.3)

    def test_post_report_multipart(self):
        got = {}

        class H(BaseHTTPRequestHandler):
            def do_POST(self):
                body = self.rfile.read(int(self.headers["Content-Length"]))
                got["project"] = self.headers["X-BugHub-Project"]
                got["ctype"] = self.headers["Content-Type"]
                got["body"] = body
                out = json.dumps({"number": 42, "url": "u", "duplicate": False}).encode()
                self.send_response(201)
                self.send_header("Content-Type", "application/json")
                self.send_header("Content-Length", str(len(out)))
                self.end_headers()
                self.wfile.write(out)

            def log_message(self, *a):
                pass

        srv = HTTPServer(("127.0.0.1", 0), H)
        th = threading.Thread(target=srv.handle_request)
        th.start()
        rep = q.build_report({"title": "T", "actual": "A"}, {}, {})
        status, resp = q.post_report("http://127.0.0.1:%d" % srv.server_port, "DEMO01", rep, shot=b"\xff\xd8jpg", log=b"log")
        th.join(5)
        srv.server_close()
        self.assertEqual((status, resp["number"]), (201, 42))
        self.assertEqual(got["project"], "DEMO01")
        self.assertIn("multipart/form-data; boundary=", got["ctype"])
        for part in (b'name="report"; filename="report.json"', b'name="screenshot"; filename="screenshot.jpg"', b'name="log"; filename="log.txt"'):
            self.assertIn(part, got["body"])


class MiscTests(unittest.TestCase):
    def test_png_size(self):
        png = b"\x89PNG\r\n\x1a\n" + b"\x00\x00\x00\rIHDR" + (1080).to_bytes(4, "big") + (2340).to_bytes(4, "big") + b"\x08\x06\x00\x00\x00"
        self.assertEqual(q.png_size(png), (1080, 2340))
        self.assertIsNone(q.png_size(b"nope"))

    def test_project_settings_parse(self):
        root = tempfile.mkdtemp()
        os.makedirs(os.path.join(root, "ProjectSettings"))
        with open(os.path.join(root, "ProjectSettings", "ProjectSettings.asset"), "w") as f:
            f.write("PlayerSettings:\n  productName: Demo\n  applicationIdentifier:\n    Android: com.a.b\n    iPhone: com.a.c\n  buildNumber:\n    Standalone: 0\n")
        with open(os.path.join(root, "ProjectSettings", "ProjectVersion.txt"), "w") as f:
            f.write("m_EditorVersion: 6000.2.6f2\n")
        ps = q.project_settings(root)
        self.assertEqual(ps["ids"], {"Android": "com.a.b", "iPhone": "com.a.c"})
        self.assertEqual(ps["productName"], "Demo")
        self.assertEqual(ps["unityVersion"], "6000.2.6f2")

    def test_parser_builds(self):
        p = q.build_parser()
        a = p.parse_args(["tap", "10", "20", "--as", "x", "--times", "3"])
        self.assertEqual((a.target, a.desc, a.times), (["10", "20"], "x", 3))
        a = p.parse_args(["bug", "--title", "t", "--actual", "a", "--type", "visual", "--severity", "low", "--dry-run"])
        self.assertTrue(a.dry_run)


if __name__ == "__main__":
    unittest.main()
