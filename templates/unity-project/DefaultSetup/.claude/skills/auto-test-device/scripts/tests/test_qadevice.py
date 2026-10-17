# -*- coding: utf-8 -*-
"""Tests for the pure parts (no device needed) of qadevice.py.

Run: python3 -m unittest discover -s .claude/skills/auto-test-device/scripts/tests
"""
import contextlib
import io
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
        self.assertIn("error", kinds)            # [Ads] plain error — medium
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
        self.assertNotIn((3, "text-overlap"), found)       # different layer → not counted
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

    TEAM = {"people": [{"id": "1", "name": "Nguyễn Diệu Hoài Anh"}, {"id": "2", "name": "Nguyễn Tuấn Anh"},
                       {"id": "3", "name": "Nguyễn Đức Trung"}]}

    def test_pick_people_never_pings_everyone_with_a_shared_name(self):
        self.assertEqual(q.pick_people(self.TEAM, ["Nguyễn Tuấn Anh"]), ["2"])
        self.assertEqual(q.pick_people(self.TEAM, ["nguyen duc trung", "3"]), ["3"])
        self.assertEqual(q.pick_people(self.TEAM, ["Tuấn Anh"]), ["2"])
        for shared in ("Nguyễn", "Anh"):
            with self.assertRaises(q.QaError) as err:
                q.pick_people(self.TEAM, [shared])
            self.assertIn("matches", str(err.exception))
        with self.assertRaises(q.QaError):
            q.pick_people(self.TEAM, ["Nobody"])

    def test_tidy_title_and_feature(self):
        self.assertEqual(q.tidy_title("[0.0.114][Android]  Crash khi out game"), "Crash khi out game")
        with self.assertRaises(q.QaError):
            q.tidy_title("Shop: " + "rất dài " * 10)
        self.assertEqual(q.bug_feature("", "Shop: trừ gem 2 lần", "Full game Map 1-3 (mọi màn)", "s"), "Shop")
        self.assertEqual(q.bug_feature("", "Nút Back không đóng", "Full game Map 1-3 (mọi màn)", "s"), "Full game Map 1-3")
        self.assertEqual(q.bug_feature("Upgrade", "Shop: x", "y", "s"), "Upgrade")

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


GAMESYSTEMS_CHEATGATE = """using System;
namespace Ezg.Feature.Shared.Systems
{
    public static class GameSystems
    {
        public static bool isCheat;
        public static void TryEnableCheat(Action<bool> done = null) { }
        public static void DisableCheat(bool emitEvent = true) { isCheat = false; }
    }
}
"""

GAMESYSTEMS_REMOTECONFIG = """namespace Ezg.Feature.Shared.Systems
{
    public static class GameSystems
    {
        public static bool isCheat = false;
        public static bool TryEnableCheat() { return false; }
        public static void DisableCheat(bool emitEvent = true) { }
    }
}
"""


def make_project(gamesystems, cheat_gate_ver=None, code=None, registry=False):
    root = tempfile.mkdtemp()
    d = os.path.join(root, "Assets", "_Project", "Features", "_Shared", "Systems")
    os.makedirs(d)
    with open(os.path.join(d, "GameSystems.cs"), "w") as f:
        f.write(gamesystems)
    os.makedirs(os.path.join(root, "Packages"))
    deps = {"com.unity.ugui": "2.0.0"}
    if cheat_gate_ver:
        deps["com.ezg.cheat-gate"] = cheat_gate_ver
    man = {"dependencies": deps}
    if registry:
        man["scopedRegistries"] = [{"name": "Easygoing", "url": "https://registry.example", "scopes": ["com.ezg"]}]
    with open(os.path.join(root, "Packages", "manifest.json"), "w") as f:
        json.dump(man, f)
    if code is not None:
        c = os.path.join(root, "Assets", "_Project", "Features", "_Shared", "Config")
        os.makedirs(c)
        with open(os.path.join(c, "GameConstant.cs"), "w") as f:
            f.write('public static class GameConstant { public const string CheatGateProject = "%s"; }\n' % code)
    return root


class CheatGateTests(unittest.TestCase):
    def test_detect_template_cheat_gate(self):
        root = make_project(GAMESYSTEMS_CHEATGATE, "0.1.1", "DEMO01")
        cfg = q.detect_cheat_config(root)
        self.assertEqual(cfg["releaseGate"], "Ezg.Feature.Shared.Systems.GameSystems.isCheat")
        self.assertEqual(cfg["cheatOn"], "Ezg.Feature.Shared.Systems.GameSystems.TryEnableCheat")
        self.assertEqual(cfg["cheatOff"], "Ezg.Feature.Shared.Systems.GameSystems.DisableCheat")
        self.assertEqual(q.cheat_gate_info(root), ("0.1.1", "DEMO01"))

    def test_remote_config_try_enable_is_not_cheat_on(self):
        # The old TryEnableCheat() asks Remote Config and can turn cheats OFF — must not be used as cheatOn.
        root = make_project(GAMESYSTEMS_REMOTECONFIG)
        cfg = q.detect_cheat_config(root)
        self.assertEqual(cfg["cheatOn"], "")
        self.assertEqual(cfg["cheatOff"], "Ezg.Feature.Shared.Systems.GameSystems.DisableCheat")
        self.assertEqual(q.cheat_gate_info(root), (None, None))

    def test_ensure_probe_adds_package_and_removes_legacy_copy(self):
        root = make_project(GAMESYSTEMS_REMOTECONFIG, registry=True)
        legacy = os.path.join(root, "Assets", "EzgQaProbe")
        os.makedirs(os.path.join(legacy, "Resources"))
        with open(os.path.join(legacy, "EzgQaProbe.cs"), "w") as f:
            f.write('// %s — đừng sửa tay.\npublic const string VERSION = "1.1.0";\n' % q.LEGACY_PROBE_MARK)
        q.write_json(os.path.join(legacy, "Resources", "EzgQaProbeConfig.json"),
                     {"releaseGate": "My.Flags.qa", "cheatOn": "", "cheatOff": "Ezg.Feature.Shared.Systems.GameSystems.DisableCheat",
                      "autoCheat": True, "port": 7531})
        open(legacy + ".meta", "w").close()
        info = q.find_probe_in_project(root)
        self.assertEqual((info["package"], info["legacy"], info["legacyVersion"], info["registry"]),
                         (None, os.path.join("Assets", "EzgQaProbe"), "1.1.0", True))
        notes = q.ensure_probe_in_project(root)
        self.assertEqual(len(notes), 3, notes)  # add package · keep custom settings · remove the old copy
        man = q.read_manifest(root)
        self.assertEqual(man["dependencies"][q.PROBE_PACKAGE], q.PROBE_MIN_VERSION)
        self.assertEqual(list(man["dependencies"]), sorted(man["dependencies"]))  # inserted in the right order
        self.assertEqual(man["scopedRegistries"][0]["scopes"], ["com.ezg"])
        self.assertFalse(os.path.exists(legacy) or os.path.exists(legacy + ".meta"))
        self.assertEqual(q.read_json(os.path.join(root, q.PROBE_OVERRIDE)), {"releaseGate": "My.Flags.qa"})
        cfg = q.effective_probe_config(root)
        self.assertEqual((cfg["releaseGate"], cfg["cheatOn"], cfg["override"]), ("My.Flags.qa", "", True))
        self.assertEqual(q.ensure_probe_in_project(root), [])  # re-run: nothing changes

    def test_ensure_probe_version_rules(self):
        root = make_project(GAMESYSTEMS_CHEATGATE, "0.1.1", "DEMO01")
        for current, expected in (("1.0.0", q.PROBE_MIN_VERSION), ("9.0.0", "9.0.0"), ("file:../qa-probe", "file:../qa-probe")):
            q.set_manifest_dependency(root, q.PROBE_PACKAGE, current)
            notes = q.ensure_probe_in_project(root)
            self.assertEqual(q.read_manifest(root)["dependencies"][q.PROBE_PACKAGE], expected)
            self.assertEqual(bool(notes), current == "1.0.0")
        # keep Unity's file ending (no trailing newline)
        mp = os.path.join(root, "Packages", "manifest.json")
        with open(mp, "w") as f:
            f.write(json.dumps(q.read_manifest(root), indent=2))
        q.set_manifest_dependency(root, "com.ezg.zz", "1.0.0")
        with open(mp) as f:
            self.assertTrue(f.read().endswith("}"))
        # no EZG registry → still added, but with a warning
        root = make_project(GAMESYSTEMS_CHEATGATE)
        notes = q.ensure_probe_in_project(root)
        self.assertTrue(any("scoped registry" in n for n in notes), notes)
        cfg = q.effective_probe_config(root)
        self.assertEqual(cfg["cheatOn"], "Ezg.Feature.Shared.Systems.GameSystems.TryEnableCheat")
        q.write_probe_override(root, {"cheatOn": "none"})
        self.assertEqual(q.effective_probe_config(root)["cheatOn"], "")

    def test_describe_probe_states(self):
        base = {"gate": "closed", "cheatGate": {"present": True, "deviceId": "7F3A-9C21-B04E", "project": "DEMO01"}}
        j = dict(base, cheatGate=dict(base["cheatGate"], state="refused"))
        status, hint = q.describe_probe(j, "DEMO01", "Pixel 7")
        self.assertIn("7F3A-9C21-B04E", status)
        self.assertIn('test_device_add(project="DEMO01", device_id="7F3A-9C21-B04E"', hint)
        self.assertIn("probe-connect --unlock", hint)
        j = {"gate": "open", "cheat": True, "cheatGate": dict(base["cheatGate"], state="unlocked", unlocked=True)}
        status, hint = q.describe_probe(j)
        self.assertIn("test list DEMO01", status)
        self.assertIn("game cheats ON", status)
        self.assertEqual(hint, "")
        status, hint = q.describe_probe({"gate": "closed", "gateReason": "X.isCheat=false", "cheatGate": {"present": False}})
        self.assertIn("X.isCheat=false", status)
        self.assertIn("probe-connect", hint)
        for st in ("offline", "not_configured", "off", "checking"):
            status, hint = q.describe_probe(dict(base, cheatGate=dict(base["cheatGate"], state=st)))
            self.assertTrue(status.startswith("GATE CLOSED") and hint, st)

    def test_wait_cheat_gate_polls_until_final(self):
        seq = [{"gate": "closed", "cheatGate": {"present": True, "state": "checking"}},
               {"gate": "open", "cheatGate": {"present": True, "state": "unlocked"}}]

        class Cli:
            def get(self, endpoint, timeout=None):
                return seq.pop(0)

        orig = q.time.sleep
        q.time.sleep = lambda s: None
        try:
            j = q.wait_cheat_gate(Cli(), {"gate": "closed", "cheatGate": {"present": True, "state": "waiting_init"}}, 30)
        finally:
            q.time.sleep = orig
        self.assertEqual(j["gate"], "open")
        self.assertEqual(seq, [])

    def test_parser_new_commands(self):
        p = q.build_parser()
        a = p.parse_args(["probe-connect", "--unlock", "--unlock-wait", "60"])
        self.assertTrue(a.unlock)
        self.assertEqual(a.unlock_wait, 60)
        self.assertEqual(p.parse_args(["cheat", "off"]).mode, "off")
        self.assertEqual(p.parse_args(["cheat"]).mode, "status")
        a = p.parse_args(["start", "--feature", "Shop"])
        self.assertEqual((a.device, a.no_probe_install, a.skip_bughub), ("auto", False, False))


class CaptureHiddenTests(unittest.TestCase):
    class FakeProbe(object):
        def __init__(self, supported=True, status=None):
            self.calls, self.supported, self.status = [], supported, status

        def get(self, endpoint, timeout=None, **params):
            self.calls.append((endpoint, params.get("sec")))
            if self.status:
                raise q.ProbeError("probe %s" % self.status, self.status)
            return {"supported": self.supported}

    class FakeDevice(object):
        def __init__(self, probe, shots):
            self.probe, self.shots = probe, shots

        def screenshot_png(self):
            self.shots.append(list(self.probe.calls) if self.probe else [])
            return b"png"

    def test_hides_bug_logger_around_native_screenshot(self):
        shots = []
        pr = self.FakeProbe()
        self.assertEqual(q.grab_png(None, self.FakeDevice(pr, shots)), b"png")
        self.assertEqual(shots, [[("/capturehidden", q.CAPTURE_HIDE_SECONDS)]])
        self.assertEqual(pr.calls[-1], ("/capturehidden", 0))

    def test_unsupported_game_or_old_probe_is_asked_once(self):
        for pr in (self.FakeProbe(supported=False), self.FakeProbe(status=404)):
            shots = []
            q.grab_png(None, self.FakeDevice(pr, shots))
            q.grab_png(None, self.FakeDevice(pr, shots))
            self.assertEqual(len(pr.calls), 1)
            self.assertEqual(len(shots), 2)

    def test_gate_closed_still_shoots_and_retries_later(self):
        pr = self.FakeProbe(status=423)
        shots = []
        q.grab_png(None, self.FakeDevice(pr, shots))
        q.grab_png(None, self.FakeDevice(pr, shots))
        self.assertEqual(len(pr.calls), 2)
        self.assertEqual(len(shots), 2)

    def test_no_probe(self):
        self.assertEqual(q.grab_png(None, self.FakeDevice(None, [])), b"png")


WINDOW_DUMP = """WINDOW MANAGER WINDOWS (dumpsys window windows)
  Window #12 Window{41f58bb u0 com.miui.securitycenter/.FloatingWindow}:
    mOwnerUid=1000 showForAllUsers=false package=com.miui.securitycenter appop=SYSTEM_ALERT_WINDOW
    mAttrs={(0,720)(wrapxwrap) gr=TOP START CENTER pkg = com.miui.securitycenter ty=SYSTEM_ALERT fmt=TRANSLUCENT
  Window #13 Window{8b9d7c8 u0 com.example.voicebubble}:
    mOwnerUid=10354 showForAllUsers=false package=com.example.voicebubble appop=SYSTEM_ALERT_WINDOW
    mAttrs={(1318,-627)(188x188) gr=TOP LEFT CENTER pkg = com.example.voicebubble adpkg = null ty=APPLICATION_OVERLAY fmt=TRANSLUCENT
  Window #14 Window{8e48eb4 u0 ShellDropTarget}:
    mOwnerUid=1000 showForAllUsers=true package=com.android.systemui appop=SYSTEM_ALERT_WINDOW
    mAttrs={(0,0)(fillxfill) pkg = com.android.systemui ty=APPLICATION_OVERLAY fmt=TRANSLUCENT
  Window #15 Window{77aa001 u0 com.example.game}:
    mOwnerUid=10390 package=com.example.game appop=SYSTEM_ALERT_WINDOW
    mAttrs={(0,0)(fillxfill) ty=APPLICATION_OVERLAY
  Window #16 Window{e4b7933 u0 com.example.game/com.unity3d.ads.adplayer.FullScreenWebViewDisplay}:
    mOwnerUid=10390 package=com.example.game
    mAttrs={(0,0)(fillxfill) ty=BASE_APPLICATION
"""

INPUT_DUMP = """Input Dispatcher State:
  Windows:
    3: name='41f58bb com.miui.securitycenter/.FloatingWindow', displayId=0, frame=[0,720][10,730], touchableRegion=[0,720][10,730], ownerUid=1000
    4: name='8b9d7c8 com.example.voicebubble', id=88, displayId=0, inputConfig=0x0, frame=[1092,152][1280,340], globalScale=1.0, touchableRegion=[1092,152][1280,340], ownerPid=4000, ownerUid=10354
    5: name='8e48eb4 ShellDropTarget', displayId=0, inputConfig=NOT_TOUCHABLE, frame=[0,0][1280,2772], touchableRegion=<empty>
    6: name='77aa001 com.example.game', displayId=0, frame=[0,0][1280,2772], touchableRegion=[0,0][1280,2772]
  RecentQueue:
    displayId=0, name='8b9d7c8 com.example.voicebubble' result='OK'
"""

UIA_XML = ('<?xml version="1.0"?><hierarchy rotation="0">'
           '<node index="0" text="" resource-id="root" class="android.view.View" package="com.example.game" content-desc="" '
           'clickable="true" enabled="true" bounds="[0,0][1280,2772]">'
           '<node index="1" text="" resource-id="" class="android.widget.Button" package="com.example.game" content-desc="Close" '
           'clickable="true" enabled="true" bounds="[1134,146][1238,253]" />'
           '<node index="2" text="" resource-id="" class="android.widget.Button" package="com.example.game" content-desc="Privacy" '
           'clickable="true" enabled="true" bounds="[13,2668][104,2759]" />'
           '<node index="3" text="Install Now" resource-id="" class="android.widget.Button" package="com.example.game" '
           'content-desc="" clickable="true" enabled="true" bounds="[840,2540][1250,2680]" />'
           '<node index="4" text="Bỏ qua quảng cáo" resource-id="com.ads:id/skip" class="android.widget.TextView" '
           'package="com.example.game" content-desc="" clickable="false" enabled="true" bounds="[40,140][400,220]" />'
           '</node></hierarchy>')


class OverlayAndAdTests(unittest.TestCase):
    def test_parse_overlay_windows_keeps_third_party_only(self):
        ws = q.parse_overlay_windows(WINDOW_DUMP, PKG)
        self.assertEqual([w["package"] for w in ws], ["com.example.voicebubble"])
        self.assertEqual((ws[0]["token"], ws[0]["type"], ws[0]["uid"]), ("8b9d7c8", "APPLICATION_OVERLAY", 10354))

    def test_input_regions_and_overlay_rects(self):
        reg = q.parse_input_regions(INPUT_DUMP)
        self.assertEqual(reg["8b9d7c8"], {"rect": [1092, 152, 188, 188], "visible": True})
        self.assertFalse(reg["8e48eb4"]["visible"])
        ovs = q.overlay_rects(WINDOW_DUMP, INPUT_DUMP, PKG)
        self.assertEqual([(o["package"], o["rect"]) for o in ovs], [("com.example.voicebubble", [1092, 152, 188, 188])])

    def test_input_regions_multiline_entries(self):
        dump = ("  Windows:\n"
                "    4: name='Window{8b9d7c8 u0 com.example.voicebubble}', displayId=0, inputConfig=0x0\n"
                "        frame=[1092,152][1280,340], globalScale=1.0\n"
                "        touchableRegion=[1100,160][1270,330], ownerUid=10354\n"
                "    5: name='77aa001 com.example.game', displayId=0, inputConfig=NOT_VISIBLE\n"
                "        frame=[0,0][1280,2772]\n"
                "  RecentQueue:\n"
                "    displayId=0, name='8b9d7c8 com.example.voicebubble' frame=[0,0][1,1]\n")
        reg = q.parse_input_regions(dump)
        self.assertEqual(reg["8b9d7c8"], {"rect": [1100, 160, 170, 170], "visible": True})
        self.assertFalse(reg["77aa001"]["visible"])

    def test_input_regions_api35_unquoted(self):
        dump = ("      4: name=2ccd23c StatusBar, id=78, displayId=0, inputConfig=NOT_FOCUSABLE | TRUSTED_OVERLAY, alpha=1, "
                "frame=[0,0][1080,136], globalScale=1, touchableRegion=[0,0][1080,136], ownerPid=830, ownerUid=10179\n"
                "    transform (ROT_0) (IDENTITY)\n"
                "      5: name=8b9d7c8 com.example.voicebubble, id=88, displayId=0, inputConfig=NOT_FOCUSABLE, alpha=1, "
                "frame=[1092,152][1280,340], touchableRegion=[1092,152][1280,340], ownerUid=10354\n")
        reg = q.parse_input_regions(dump)
        self.assertEqual(reg["2ccd23c"]["rect"], [0, 0, 1080, 136])
        self.assertEqual(reg["8b9d7c8"]["rect"], [1092, 152, 188, 188])

    def test_point_and_uncovered(self):
        ovs = [{"package": "x", "rect": [1092, 152, 188, 188]}]
        self.assertIsNotNone(q.point_in_rects(1186, 200, ovs))
        self.assertIsNone(q.point_in_rects(1186, 148, ovs))
        # X button almost fully covered (only the top edge is free, and it lies outside the button's core area) → None
        self.assertIsNone(q.uncovered_point([1134, 146, 104, 107], ovs))
        pt = q.uncovered_point([1000, 146, 200, 100], ovs)
        self.assertIsNotNone(pt)
        self.assertIsNone(q.point_in_rects(pt[0], pt[1], ovs))

    def test_uia_close_candidates(self):
        nodes = q.parse_uia_nodes(UIA_XML)
        self.assertEqual(len(nodes), 5)
        cands = q.pick_close_candidates(nodes)
        self.assertEqual([c["label"] for c in cands], ["Close", "Bỏ qua quảng cáo"])
        self.assertEqual(cands[0]["rect"], [1134, 146, 104, 107])

    def test_close_label_marks_and_ids(self):
        base = {"text": "", "desc": "", "id": "", "cls": "", "pkg": "", "clickable": True, "enabled": True, "rect": [0, 0, 10, 10]}
        self.assertEqual(q._ad_close_label(dict(base, text="×")), "×")
        self.assertEqual(q._ad_close_label(dict(base, id="com.ad:id/close_button")), "close_button")
        self.assertEqual(q._ad_close_label(dict(base, text="Đóng")), "Đóng")
        self.assertEqual(q._ad_close_label(dict(base, desc="Ad info")), "")
        self.assertEqual(q._ad_close_label(dict(base, text="Install Now")), "")


class FakeKillDevice(object):
    def __init__(self, has_home=True):
        self.calls, self.has_home = [], has_home

    def stop_like_player(self, pkg, settle):
        if not self.has_home:
            raise q.NotSupported("no home")
        self.calls.append(("home-then-stop", pkg, settle))

    def stop(self, pkg):
        self.calls.append(("stop", pkg))


class FakeSession(object):
    def __init__(self, d=None):
        self.d = d or {}

    def save(self):
        pass


class KillDedupValueTests(unittest.TestCase):
    def test_kill_app_modes(self):
        dev = FakeKillDevice()
        self.assertEqual(q.kill_app(dev, PKG), "player")
        self.assertEqual(dev.calls, [("home-then-stop", PKG, q.STOP_SETTLE_SECONDS)])
        dev = FakeKillDevice()
        self.assertEqual(q.kill_app(dev, PKG, hard=True), "hard")
        self.assertEqual(dev.calls, [("stop", PKG)])
        dev = FakeKillDevice(has_home=False)
        self.assertEqual(q.kill_app(dev, PKG), "hard-fallback")
        self.assertEqual(dev.calls, [("stop", PKG)])

    def test_find_duplicate(self):
        rows = [{"code": "GAME01", "number": 54, "title": "IAP: log lỗi OnProductsFetchFailed mỗi lần mở game",
                 "logSig": "1c4945d57f", "type": "logic", "screen": "screen_battle"},
                {"code": "GAME01", "number": 55, "title": "Upgrade: thời gian thập phân theo locale máy",
                 "type": "visual", "screen": "screen_upgrade"},
                {"code": "OTHER", "number": 9, "title": "Shop: mua gói trừ tiền 2 lần", "type": "logic"}]
        dup, why = q.find_duplicate(rows, "game01", "Khởi động: lỗi IAP khác hẳn tiêu đề", "1c4945d57f")
        self.assertEqual((dup["number"], "same log signature" in why), (54, True))
        dup, _ = q.find_duplicate(rows, "GAME01", "Upgrade: thời gian thập phân theo locale máy!")
        self.assertEqual(dup["number"], 55)
        dup, why = q.find_duplicate(rows, "GAME01", "Upgrade: số thập phân theo locale lệch với tiền", "", "screen_upgrade", "visual")
        self.assertEqual((dup["number"], "same screen" in why), (55, True))
        self.assertEqual(q.find_duplicate(rows, "GAME01", "Upgrade: số thập phân theo locale lệch với tiền", "", "screen_shop", "visual"), (None, ""))
        self.assertEqual(q.find_duplicate(rows, "GAME01", "Shop: mua gói trừ tiền 2 lần"), (None, ""))

    def test_format_value_truncates_loudly(self):
        self.assertEqual(q.format_value("f()", [1, 2]), "f() → [1, 2]")
        line = q.format_value("f()", ["x" * 50] * 100, limit=40)
        self.assertIn("[cut 40/", line)
        self.assertNotIn("[cut ", q.format_value("f()", ["x" * 50] * 100, limit=40, full=True))

    def test_guard_overlay_blocks_native_tap_only(self):
        dev = object.__new__(q.Android)
        dev.probe = None
        dev.overlays = lambda pkg="": [{"package": "com.example.voicebubble", "type": "APPLICATION_OVERLAY", "rect": [1092, 152, 188, 188]}]
        sess = FakeSession({"package": PKG, "view": {"w": 591, "h": 1280, "shot_w": 1280, "shot_h": 2772}})
        args = q.build_parser().parse_args(["tap", "546", "92"])
        with self.assertRaises(q.QaError) as cm:
            q.guard_overlay(args, sess, dev, [(1186, 200)])
        self.assertIn("com.example.voicebubble", str(cm.exception))
        q.guard_overlay(args, sess, dev, [(600, 1200)])  # outside the overlay — not blocked
        for flags in (["--inject"], ["--allow-overlay"]):
            q.guard_overlay(q.build_parser().parse_args(["tap", "546", "92"] + flags), sess, dev, [(1186, 200)])

    def test_parser_v13_options(self):
        p = q.build_parser()
        a = p.parse_args(["app", "restart", "--hard"])
        self.assertEqual((a.hard, a.settle), (True, q.STOP_SETTLE_SECONDS))
        self.assertFalse(p.parse_args(["app", "stop", "--settle", "4"]).hard)
        a = p.parse_args(["ad-close", "--timeout", "45", "--list"])
        self.assertEqual((a.timeout, a.list, a.allow_overlay), (45, True, False))
        self.assertEqual(p.parse_args(["overlays"]).fn, q.cmd_overlays)
        a = p.parse_args(["call", "ShopService.GetAllProductId", "--out", "/tmp/ids.json", "--full"])
        self.assertEqual((a.out, a.full), ("/tmp/ids.json", True))
        self.assertTrue(p.parse_args(["state", "A.B", "--full"]).full)
        self.assertTrue(p.parse_args(["drag", "1", "2", "3", "4", "--allow-overlay"]).allow_overlay)


class ApiRetryTests(unittest.TestCase):
    """Retry when a turn dies on an API error (start arms it via /auto-clear retry-arm, stop disarms it) + the `retry` command."""

    def setUp(self):
        self._saved = {k: getattr(q, k) for k in ("run", "auto_clear_script", "api_retry", "close_session", "repo_root")}
        self._load = q.Session.load
        self._call, self._sleep = q.subprocess.call, q.time.sleep

    def tearDown(self):
        for k, v in self._saved.items():
            setattr(q, k, v)
        q.Session.load = self._load
        q.subprocess.call, q.time.sleep = self._call, self._sleep

    def test_prompt_is_valid_for_auto_clear(self):
        p = q.API_RETRY_PROMPT
        self.assertLessEqual(len(p), 200)
        self.assertNotIn("\n", p)
        self.assertTrue(all(32 <= ord(c) < 127 for c in p))  # ASCII: the Windows version types it char by char into the console
        self.assertIn("/auto-test-device", p)

    def test_parse_retry_result(self):
        cases = [
            ("RETRY_ARMED pane=ABC pid=1 tty=/dev/ttys001 delay=30s max=∞", "armed"),
            ("RETRY_ARMED pid=4242 delay=30s max=unlimited", "armed"),          # Windows
            ("RETRY_DISARMED pane=ABC", "disarmed"),
            ("RETRY_NOT_ARMED pane=ABC", "not_armed"),
            ("NOT_INSTALLED: StopFailure hook chưa đăng ký — chạy: bash x install", "not_installed"),
            ("UNSUPPORTED: không có ITERM_SESSION_ID (không chạy trong iTerm2)", "unsupported"),
            ("INVALID_THEN: prompt rỗng", "error"),
        ]
        for out, want in cases:
            self.assertEqual(q.parse_retry_result(0, "noise\n" + out + "\n", "")[0], want, out)
        self.assertEqual(q.parse_retry_result(127, "", "bash: not found"), ("error", "bash: not found"))
        self.assertEqual(q.parse_retry_result(1, "", "")[0], "error")

    def test_api_retry_commands(self):
        calls = []
        q.auto_clear_script = lambda: "/x/auto-clear.sh"
        q.run = lambda cmd, **kw: calls.append(cmd) or (0, "RETRY_ARMED pane=P pid=1 tty=? delay=30s max=∞\n", "")
        self.assertEqual(q.api_retry("/repo", "arm")[0], "armed")
        self.assertEqual(calls[-1], ["bash", "/x/auto-clear.sh", "retry-arm", "--max", "0", "--prompt", q.API_RETRY_PROMPT])
        q.run = lambda cmd, **kw: calls.append(cmd) or (0, "RETRY_DISARMED pane=P\n", "")
        self.assertEqual(q.api_retry("/repo", "off")[0], "disarmed")
        self.assertEqual(calls[-1], ["bash", "/x/auto-clear.sh", "retry-off"])
        self.assertEqual(q.auto_clear_cmd("C:/x/auto-clear.ps1", "retry-off")[:6],
                         ["powershell", "-NoProfile", "-ExecutionPolicy", "Bypass", "-File", "C:/x/auto-clear.ps1"])
        q.auto_clear_script = lambda: None
        self.assertEqual(q.api_retry("/repo", "arm")[0], "missing")

    def test_api_retry_installs_hook_when_missing(self):
        calls, outs = [], ["NOT_INSTALLED: StopFailure hook chưa đăng ký", "INSTALLED Stop + StopFailure hook",
                           "RETRY_ARMED pane=P pid=1 tty=? delay=30s max=∞"]
        q.auto_clear_script = lambda: "/x/auto-clear.sh"
        q.run = lambda cmd, **kw: calls.append(cmd[2]) or (0, outs.pop(0) + "\n", "")
        st, line = q.api_retry("/repo", "arm")
        self.assertEqual((st, calls), ("armed", ["retry-arm", "install", "retry-arm"]))
        self.assertIn("hook auto-installed", q.describe_api_retry({"state": st, "line": line}))
        calls[:], outs[:] = [], ["UNSUPPORTED: không có ITERM_SESSION_ID"]
        self.assertEqual(q.api_retry("/repo", "arm")[0], "unsupported")
        self.assertEqual(calls, ["retry-arm"])  # not a missing-hook error → no install

    def test_describe_api_retry(self):
        self.assertTrue(q.describe_api_retry({"state": "armed"}).startswith("ON — "))
        self.assertIn("/auto-clear install", q.describe_api_retry({"state": "not_installed"}))
        self.assertIn("iTerm2", q.describe_api_retry({"state": "unsupported", "line": "UNSUPPORTED: x"}))
        self.assertIn("--no-api-retry", q.describe_api_retry(None))
        self.assertTrue(q.describe_api_retry({"state": "missing", "line": "project has no /auto-clear skill"}).startswith("OFF — "))

    def _fake_session(self, state):
        class S(object):
            id = "S1"
            d = {"api_retry": {"state": state, "line": ""}} if state else {}

            def path(self, *p):
                return "/tmp/" + "/".join(p)
        return S()

    def test_stop_disarms_only_when_armed(self):
        calls = []
        q.api_retry = lambda root, action: calls.append(action) or ("disarmed", "RETRY_DISARMED pane=P")
        q.close_session = lambda sess, quiet=False: None
        q.repo_root = lambda: "/repo"
        sess = self._fake_session("armed")
        q.Session.load = staticmethod(lambda sid=None: sess)
        with contextlib.redirect_stdout(io.StringIO()):
            q.cmd_stop(q.build_parser().parse_args(["stop"]))
        self.assertEqual(calls, ["off"])
        self.assertEqual(sess.d["api_retry"]["state"], "disarmed")
        for st in ("not_installed", "disarmed", None):
            sess = self._fake_session(st)
            q.Session.load = staticmethod(lambda sid=None: sess)
            with contextlib.redirect_stdout(io.StringIO()):
                q.cmd_stop(q.build_parser().parse_args(["stop"]))
        self.assertEqual(calls, ["off"])

    def test_retry_loops_until_pass(self):
        seq, cmds, sleeps = [1, 75, 0], [], []
        q.subprocess.call = lambda cmd: cmds.append(cmd) or seq.pop(0)
        q.time.sleep = lambda s: sleeps.append(s)
        buf = io.StringIO()
        with contextlib.redirect_stdout(buf):
            q.cmd_retry(q.build_parser().parse_args(["--session", "S9", "retry", "--every", "5", "--", "bug-resend", "r1"]))
        self.assertEqual(len(cmds), 3)
        self.assertEqual(cmds[0][-4:], ["--session", "S9", "bug-resend", "r1"])
        self.assertEqual(sleeps, [5.0, 5.0])
        self.assertIn("passed on attempt 3", buf.getvalue())

    def test_retry_fatal_and_deadline(self):
        q.time.sleep = lambda s: None
        q.subprocess.call = lambda cmd: 4
        with self.assertRaises(q.QaError):
            q.cmd_retry(q.build_parser().parse_args(["retry", "--", "start", "auto"]))
        q.subprocess.call = lambda cmd: 1
        buf = io.StringIO()
        with contextlib.redirect_stdout(buf), self.assertRaises(SystemExit) as cm:
            q.cmd_retry(q.build_parser().parse_args(["retry", "--max-wait", "0", "shot"]))
        self.assertEqual(cm.exception.code, q.RETRY_PENDING_RC)
        self.assertIn("do not stop the session", buf.getvalue())
        for bad in (["retry"], ["retry", "--", "retry", "shot"]):
            with self.assertRaises(q.QaError):
                q.cmd_retry(q.build_parser().parse_args(bad))

    def test_parser_v14_options(self):
        p = q.build_parser()
        self.assertTrue(p.parse_args(["start", "auto", "--no-api-retry"]).no_api_retry)
        self.assertFalse(p.parse_args(["start"]).no_api_retry)
        a = p.parse_args(["retry", "--", "shot", "--grid"])
        self.assertEqual((a.fn, a.every, a.max_wait), (q.cmd_retry, q.RETRY_EVERY_S, q.RETRY_MAX_WAIT_S))
        self.assertEqual(p.parse_args(["api-retry"]).action, "status")
        self.assertEqual(p.parse_args(["api-retry", "on"]).fn, q.cmd_api_retry)

    def test_api_retry_command_updates_session(self):
        sess = self._fake_session(None)
        sess.save = lambda: None
        q.Session.load = staticmethod(lambda sid=None: sess)
        q.repo_root = lambda: "/repo"
        q.api_retry = lambda root, action: ("armed", "RETRY_ARMED x") if action == "arm" else ("not_armed", "RETRY_NOT_ARMED x")
        with contextlib.redirect_stdout(io.StringIO()):
            q.cmd_api_retry(q.build_parser().parse_args(["api-retry", "on"]))
            self.assertEqual(sess.d["api_retry"]["state"], "armed")
            q.cmd_api_retry(q.build_parser().parse_args(["api-retry", "off"]))
        self.assertEqual(sess.d["api_retry"]["state"], "disarmed")



if __name__ == "__main__":
    unittest.main()


class DoctorPlanTests(unittest.TestCase):
    """`doctor` as a report + actions (new machines onboard themselves) and `plan` (splits the whole game into charters)."""

    def setUp(self):
        import shutil
        self.tmp = tempfile.mkdtemp()
        self._rm = shutil.rmtree
        self._saved = {k: getattr(q, k) for k in ("auto_clear_script", "find_adb", "find_emulator", "find_idb")}
        q.auto_clear_script = lambda: None
        q.find_adb = lambda: None
        q.find_emulator = lambda adb: None
        q.find_idb = lambda: None

    def tearDown(self):
        for k, v in self._saved.items():
            setattr(q, k, v)
        self._rm(self.tmp, ignore_errors=True)

    def _mk(self, *rel):
        os.makedirs(os.path.join(self.tmp, *rel), exist_ok=True)

    def test_feature_buckets_new_and_old_layout(self):
        self._mk("Assets", "_Project", "Features", "Monetization", "Shop", "Scripts")
        self._mk("Assets", "_Project", "Features", "Monetization", "VipPass", "Resources")
        self._mk("Assets", "_Project", "Features", "System", "Settings", "Scripts")
        self._mk("Assets", "_Project", "Features", "_Shared", "UI")
        self._mk("Assets", "_Project", "Features", "_Gameplay", "Scripts")          # bucket with a single feature = itself
        self._mk("Assets", "_Project", "Features", "Gameplay", "Farm", "Scripts")   # bucket with its own Scripts + ≥2 child features
        self._mk("Assets", "_Project", "Features", "Gameplay", "Upgrade", "Scripts")
        self._mk("Assets", "_Project", "Features", "Gameplay", "Scripts")
        b = q.feature_buckets(self.tmp)
        self.assertEqual(b["Monetization"], ["Shop", "VipPass"])
        self.assertEqual(b["System"], ["Settings"])
        self.assertEqual(b["Gameplay"], ["Farm", "Upgrade", "_Gameplay"])   # `_Gameplay` (one feature) merged into the Gameplay bucket
        self.assertNotIn("_Shared", b)
        self.assertNotIn("_Gameplay", b)
        self.assertEqual(q.feature_buckets(tempfile.mkdtemp()), {})

    def test_screen_folder_index_from_code_refs(self):
        self._mk("Assets", "_Project", "Features", "Meta", "HomeScene", "Scripts")
        self._mk("Assets", "_Project", "Features", "Onboarding", "Tutorial", "Scripts")
        self._mk("Assets", "_Project", "Features", "_Shared", "UI")
        base = os.path.join(self.tmp, "Assets", "_Project", "Features")
        open(os.path.join(base, "Meta", "HomeScene", "Scripts", "Home.cs"), "w").write(
            "class H { void X() { UIManager.Instance.Show(GameEnums.Features.HomeScreen); var y = GameEnums.Features.CurrencyBar; } }")
        open(os.path.join(base, "Onboarding", "Tutorial", "Scripts", "Tut.cs"), "w").write(
            "class T { void X() { Show(GameEnums.Features.Tut1Step1); Show(GameEnums.Features.Tut1Step1); Show(GameEnums.Features.HomeScreen); } }")
        open(os.path.join(base, "_Shared", "UI", "UIManager.cs"), "w").write("class U { void X() { var a = GameEnums.Features.Tut1Step1; var b = GameEnums.Features.Mystery; } }")
        buckets = q.feature_buckets(self.tmp)
        idx = q.screen_folder_index(self.tmp, ["HomeScreen", "CurrencyBar", "Tut1Step1", "Mystery"], buckets)
        self.assertEqual(idx["HomeScreen"], ("Meta", "HomeScene"))        # 1 ref in HomeScene + 1 in Tutorial → tie, max() takes the first seen; both are valid
        self.assertEqual(idx["CurrencyBar"], ("Meta", "HomeScene"))
        self.assertEqual(idx["Tut1Step1"], ("Onboarding", "Tutorial"))     # _Shared not counted
        self.assertNotIn("Mystery", idx)                                     # referenced only from _Shared → not assigned
        folders = [f for fs in buckets.values() for f in fs]
        self.assertEqual(q.match_folder("Tut1Step1", folders, idx.get("Tut1Step1")), "Tutorial")
        self.assertEqual(q.match_folder("HomeScene", folders, None), "HomeScene")   # name match beats ref
        g = q.group_plan({"Tut1Step1": {"state": "referenced", "why": ""}, "Mystery": {"state": "referenced", "why": ""}}, buckets, [], idx)
        names = {x["feature"]: [s["name"] for s in x["screens"]] for x in g}
        self.assertEqual((names["Tutorial"], names["Khác"]), (["Tut1Step1"], ["Mystery"]))

    def test_group_plan_orders_by_risk_matches_exact_first_and_skips_dead(self):
        classes = {"Shop": {"state": "live", "why": ""}, "Settings": {"state": "referenced", "why": ""},
                   "Tut1_Retired": {"state": "dead", "why": ""}, "RewardPopup": {"state": "referenced", "why": ""},
                   "RewardAds": {"state": "unreferenced", "why": ""}, "Mystery": {"state": "referenced", "why": ""},
                   "OfflineEarnings": {"state": "live", "why": ""}}
        buckets = {"System": ["Settings", "RewardPopup", "RewardAds"], "Monetization": ["Shop"], "Meta": ["OfflineEarning"]}
        g = q.group_plan(classes, buckets, charters=[{"id": "shop"}])
        self.assertEqual((g[0]["bucket"], g[0]["feature"], g[0]["charter"], g[0]["risk"]), ("Monetization", "Shop", "shop", "cao"))
        names = {x["feature"]: [s["name"] for s in x["screens"]] for x in g}
        self.assertEqual(names["RewardPopup"], ["RewardPopup"])      # exact name match, not RewardAds by prefix
        self.assertEqual(names["RewardAds"], ["RewardAds"])
        self.assertEqual(names["OfflineEarning"], ["OfflineEarnings"])  # singular/plural
        self.assertEqual(names["Khác"], ["Mystery"])
        self.assertEqual(g[-1]["bucket"], "Khác")
        self.assertNotIn("Tut1_Retired", sum(names.values(), []))
        order = [x["bucket"] for x in g if x["bucket"] != "Khác"]
        self.assertEqual(order.index("Monetization"), 0)
        self.assertLess(order.index("Meta"), order.index("System"))

    def test_doctor_report_on_empty_repo_lists_actions_in_order(self):
        rep = q.doctor_report(self.tmp)
        self.assertFalse(rep["ok"])
        ids = [a["id"] for a in rep["actions"]]
        self.assertEqual(ids[0], "feature-hub-update")
        for want in ("probe-package", "autotest-package", "cheat-gate", "bughub-setup", "profile-init", "api-retry-hook", "mcp-check"):
            self.assertIn(want, ids)
        self.assertEqual(ids[-1], "mcp-check")
        who = {a["id"]: a["who"] for a in rep["actions"]}
        self.assertEqual((who["feature-hub-update"], who["bughub-setup"], who["probe-package"], who["cheat-gate"]), ("model", "model", "tool", "dev"))
        self.assertEqual(rep["featureHub"]["items"], q.QA_HUB_ITEMS)
        self.assertIn("Skills/auto-test-device", q.QA_HUB_ITEMS["ai"])
        self.assertTrue(rep["mcp"]["bughub"]["url"].endswith("/mcp"))
        self.assertEqual(rep["apiRetryHook"]["state"], "missing")
        self.assertEqual(os.listdir(self.tmp), [])   # doctor is read-only

    def test_doctor_fix_dry_run_writes_nothing(self):
        rep = q.doctor_report(self.tmp)
        lines = q.doctor_fix(self.tmp, rep, dry_run=True)
        self.assertTrue(any("profile init" in l for l in lines))
        self.assertTrue(any(q.PROBE_PACKAGE in l for l in lines))
        self.assertEqual(os.listdir(self.tmp), [])

    def test_doctor_json_cli(self):
        saved = q.repo_root
        q.repo_root = lambda: self.tmp
        try:
            buf = io.StringIO()
            with contextlib.redirect_stdout(buf):
                rc = q.main(["doctor", "--json"])
            self.assertEqual(rc, 0)
            d = json.loads(buf.getvalue())
            self.assertIn("actions", d)
            buf = io.StringIO()
            with contextlib.redirect_stdout(buf):
                self.assertRaises(SystemExit, q.main, ["doctor", "--strict"])
            self.assertIn("remaining tasks", buf.getvalue())
        finally:
            q.repo_root = saved
