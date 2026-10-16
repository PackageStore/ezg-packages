# -*- coding: utf-8 -*-
"""Test phần 1.5 của qadevice (hồ sơ QA theo project, auto-audit, env, charter/coverage, csv, autotest-import…)
trên repo giả trong thư mục tạm — không cần thiết bị, không chạm repo thật.

Chạy: python3 -m unittest discover -s .claude/skills/auto-test-device/scripts/tests
"""
import contextlib
import io
import json
import os
import shutil
import struct
import sys
import tempfile
import unittest
import zlib

HERE = os.path.dirname(os.path.abspath(__file__))
SCRIPTS = os.path.dirname(HERE)
sys.path.insert(0, SCRIPTS)
import qadevice as q  # noqa: E402
import qaprofile as qp  # noqa: E402

SKILL_DIR = os.path.dirname(SCRIPTS)
REAL_REPO = os.path.dirname(os.path.dirname(os.path.dirname(SKILL_DIR)))  # <repo>/.claude/skills/auto-test-device → <repo>
REAL_TEMPLATE = os.path.join(REAL_REPO, ".claude", "docs", "QaProfile.template.md")
REAL_PROJECT_PROFILE_PY = os.path.join(REAL_REPO, ".claude", "scripts", "project_profile.py")

PROFILE_JSON = """{
  "$comment": [
    "Per-project knobs.  Giữ nguyên format tay này."
  ],

  "projectName": "FakeGame",
  "sourceRoot": "Assets/_Project",
  "sensitiveGlobs": [
    "*Purchase*", "*IAP*",
    "*Auth*"
  ],
  "backend": {
    "kind": "none"
  },
  "nested": { "qa": { "decoy": true } },
  "bugHub": { "projectCode": "FAKE" }
}
"""

GAME_ENUMS = """// enum màn
namespace Fake.Shared.Config
{
    public static class GameEnums
    {
        /* chú thích khối */
        public enum Features
        {
            none = 0,
            [BundleName("features__settings")] Settings = 1,
            Shop = 2, // mở từ Home
            RewardPopup = 3,
            Tut1Intro_Retired = 40,
            Tut1Outro_Retired = 41,
            Admin = 50,
            DeadByBundleOnly = 51,
            OfflineEarning = 60,
        }
        public enum Other { A = 1, B = 2 }
    }
}
"""

GAME_SYSTEMS = """using System;
namespace Fake.Shared.Systems
{
    public static class GameSystems
    {
        public static bool isCheat;
        public static void TryEnableCheat(Action<bool> done = null) { }
        public static void DisableCheat(bool emitEvent = true) { isCheat = false; }
    }
}
"""

UI_FRAMEWORK = """namespace Fake.UI
{
    public class UIManager : Singleton<UIManager> { public void Show(GameEnums.Features f) { } }
    public abstract class FeatureBaseController : MonoBehaviour
    {
        public virtual void OnShow() { } public virtual void OnHide() { } public virtual void OnBack() { } public bool isShowing;
    }
    public class ScreenShopController : FeatureBaseController { void Open() { UIManager.Instance.Show(GameEnums.Features.Shop); } }
    public class ScreenSettingsController : FeatureBaseController { }
    public class ScreenRewardPopupController : FeatureBaseController { void X() { var f = GameEnums.Features.RewardPopup; } }
    public class ScreenOfflineEarningController : FeatureBaseController { void X() { var f = GameEnums.Features.OfflineEarning; } }
    public class PackagePurchaseBaseController : MonoBehaviour { public void Show() {} public void Hide() {} public void Close() {} }
    public class PackA : PackagePurchaseBaseController { }
    public class PackB : PackagePurchaseBaseController { }
    public class PackC : PackagePurchaseBaseController { }
    public class ToolA : EditorWindow { } public class ToolB : EditorWindow { } public class ToolC : EditorWindow { }
}
"""

SERVICES = """namespace Fake.Shared
{
    public static class PlayerDataManager { public static PlayerResource PlayerResource => DataPlayer.GetModule<PlayerResource>(); }
    public static class TimeManager { public static long Now => 0; }
    public static class TrackingEventName { public const string ShopOpen = "shop_open"; }
    public static class FarmService
    {
        public static void Cheat_AddGold(long v) { } public static void Cheat_SetLevel(int l) { }
    }
}
"""

ASSET_REFS = """public static class AssetRefs { public static string Of(GameEnums.Features f) { return f == GameEnums.Features.DeadByBundleOnly ? "x" : ""; } }
"""

COINPACK_CSV = "﻿id,pack_name,price\n1,gold_pack_1,100\n2,gold_pack_2,250\n#comment,,\n3,gold_pack_3,900\n"

CHARTER_SHOP = """# Charter — Shop

**Status:** draft

| # | Ca | Bước chính | Kỳ vọng | Oracle |
|---|---|---|---|---|
| P0-01 | Mua gói 1 | tap | -100 gem | CoinPack:1 |
| P0-02 | Mua gói 2 | tap | -250 gem | CoinPack:2 |
| P1-01 | Thiếu tiền | tap | báo thiếu | — |
| P2-01 | Back ở popup | key back | đóng 1 lớp | — |
"""


def png_bytes(w=8, h=8, color=(120, 120, 120)):
    raw = b"".join(b"\x00" + bytes(color) * w for _ in range(h))

    def chunk(t, d):
        c = struct.pack(">I", len(d)) + t + d
        return c + struct.pack(">I", zlib.crc32(t + d) & 0xffffffff)
    return b"\x89PNG\r\n\x1a\n" + chunk(b"IHDR", struct.pack(">IIBBBBB", w, h, 8, 2, 0, 0, 0)) + chunk(b"IDAT", zlib.compress(raw)) + chunk(b"IEND", b"")


def report_011(path, run_id="20260101-000000"):
    rep = {"runId": run_id, "startedAt": "2026-01-01T00:00:00+07:00", "summary": {"total": 3},
           "suites": [{"suiteId": "smoke", "status": 4, "cases": [
               {"caseId": "boot", "name": "Khởi động game", "status": 2, "issues": []},
               {"caseId": "screen-Shop", "name": "Mở màn Shop", "status": 2, "issues": [
                   {"id": "a", "severity": 1, "category": "UI.Overflow", "title": "Chữ tràn", "location": "x.prefab", "objectPath": "a/b"}]},
               {"caseId": "screen-Admin", "name": "Mở màn Admin", "status": 4, "issues": [
                   {"id": "b", "severity": 3, "category": "Log.Exception", "title": "NullReferenceException", "location": "UIManager.cs:1", "objectPath": ""}]}]},
                      {"suiteId": "static", "status": 3, "cases": [{"caseId": "missing-ref", "name": "Missing", "status": 3, "issues": [
                          {"id": "c", "severity": 2, "category": "Asset.MissingReference", "title": "Missing", "location": "y.prefab", "objectPath": "y"}]}]}]}
    os.makedirs(os.path.dirname(path), exist_ok=True)
    with open(path, "w", encoding="utf-8") as f:
        json.dump(rep, f)


def report_02(path, run_id="20260102-000000"):
    rep = {"runId": run_id, "version": "0.2.0", "generatedAt": "2026-01-02T00:00:00Z", "summary": {"total": 1, "bySeverity": {"Critical": 1}},
           "suites": [{"suiteId": "smoke", "status": "Failed", "cases": [
               {"caseId": "screen-Shop", "name": "Mở màn Shop", "status": "Passed", "screen": "Shop", "issues": []},
               {"caseId": "screen-Settings", "name": "Mở màn Settings", "status": "Failed", "screen": "Settings", "issues": [
                   {"severity": "Critical", "group": "Log.Exception", "message": "boom", "location": "a.cs", "objectPath": "", "screen": "Settings"}]}]}],
           "issues": [{"suite": "static", "group": "Feature.Dead", "severity": "Info", "message": "Tut1Intro_Retired", "location": "", "objectPath": "", "screen": ""}]}
    os.makedirs(os.path.dirname(path), exist_ok=True)
    with open(path, "w", encoding="utf-8") as f:
        json.dump(rep, f)


def make_repo(with_template=True, with_charter=True, with_qa=False):
    root = tempfile.mkdtemp(prefix="qarepo")

    def w(rel, text):
        p = os.path.join(root, rel)
        os.makedirs(os.path.dirname(p), exist_ok=True)
        with open(p, "w", encoding="utf-8") as f:
            f.write(text)
    w(".claude/project-profile.json", PROFILE_JSON)
    w("Assets/_Project/Features/_Shared/Config/GameEnums.cs", GAME_ENUMS)
    w("Assets/_Project/Features/_Shared/Systems/GameSystems.cs", GAME_SYSTEMS)
    w("Assets/_Project/Features/_Shared/UI/Framework/UIFramework.cs", UI_FRAMEWORK)
    w("Assets/_Project/Features/_Shared/Services.cs", SERVICES)
    w("Assets/_Project/Features/_Shared/AssetBundles/AssetRefs.cs", ASSET_REFS)
    w("Assets/_Project/Features/Monetization/Shop/CsvConfig/CoinPack.csv", COINPACK_CSV)
    w("Assets/_Project/Features/System/Settings/CsvConfig/GeneralConfig.csv", "key,value\noffline_cap_hours,8\n")
    for n in ("screen_shop", "screen_settings", "screen_reward_popup"):
        w("Assets/_Project/Features/X/Resources/%s.prefab" % n, "")
    w("ProjectSettings/ProjectSettings.asset", "PlayerSettings:\n  productName: Fake Game\n  companyName: EZG\n  bundleVersion: 1.2.3\n  applicationIdentifier:\n    Android: com.fake.game\n")
    w("ProjectSettings/ProjectVersion.txt", "m_EditorVersion: 6000.2.6f2\n")
    w("ProjectSettings/EZGAutoTestSettings.json", json.dumps({"economy": {"skipCurrencies": ["Gold"]}}, indent=2))
    w("Packages/manifest.json", json.dumps({"dependencies": {"com.ezg.qa-probe": "1.2.1", "com.ezg.cheat-gate": "0.1.1", "com.ezg.autotest": "0.1.1"},
                                             "scopedRegistries": [{"name": "EZG", "url": "https://x", "scopes": ["com.ezg"]}]}))
    report_011(os.path.join(root, "AutoTestReports", "20260101-000000", "report.json"))
    if with_template:
        tpl = open(REAL_TEMPLATE, encoding="utf-8").read() if os.path.isfile(REAL_TEMPLATE) else qp.MINIMAL_QA_DOC
        w(".claude/docs/QaProfile.template.md", tpl)
    if with_charter:
        w(".claude/docs/qa/charters/shop.md", CHARTER_SHOP)
    if with_qa:
        qp.save_qa_patch(root, {"status": "reviewed", "conventions": {"featuresEnum": "GameEnums+Features"},
                                "noise": [{"flag": "*", "match": "screen:home", "reason": "backdrop", "addedBy": "dev", "date": "2026-01-01"}]})
    return root


class FakeProbe(object):
    """Probe giả: /ui trả theo self.ui (có thể là list để đổi theo từng lần gọi); ghi lại mọi call."""

    def __init__(self, ui=None, version="1.3.0"):
        self.calls = []
        self.ui = ui or {"scene": "Home", "screens": ["screen_home"], "screen": {"w": 1080, "h": 1920, "dpi": 420},
                         "elements": [{"id": 1, "name": "btn_shop", "path": "Canvas/btn_shop", "kind": "Button", "text": "Shop", "label": "Shop",
                                       "rect": [10, 10, 200, 100], "interactable": True, "flags": []}]}
        self.version = version
        self.ui_seq = None

    def _ui(self):
        if self.ui_seq:
            return self.ui_seq.pop(0) if len(self.ui_seq) > 1 else self.ui_seq[0]
        return self.ui

    def get(self, endpoint, timeout=None, **params):
        self.calls.append((endpoint, params))
        if endpoint == "/ui":
            return json.loads(json.dumps(self._ui()))
        if endpoint == "/ping":
            return {"version": self.version, "gate": "open", "cheat": True}
        if endpoint == "/get":
            return {"value": {"PlayerDataManager.PlayerResource.Gold": 100, "Fake.X": "y"}.get(params.get("path"), params.get("path"))}
        if endpoint == "/pseudoloc":
            return {"ok": True, "on": params.get("on") == 1, "mode": params.get("mode"), "count": 12}
        if endpoint == "/logs":
            return {"entries": [{"msg": "[Tracking] shop_open"}], "cursor": 1}
        if endpoint == "/capturehidden":
            return {"supported": False}
        return {}

    def post(self, endpoint, timeout=None, **data):
        self.calls.append((endpoint, data))
        if endpoint == "/call":
            return {"value": "ok"}
        return {"ok": True}

    def request(self, method, endpoint, params=None, data=None, timeout=None, raw=False, _retry=True):
        self.calls.append((endpoint, params or data or {}))
        if raw:
            return png_bytes()
        return {}


class FakeAndroid(q.Android):
    """Android giả: không gọi adb; ghi lệnh shell, trả output canned cho settings/wm/uimode."""

    def __init__(self, probe=None):
        q.Android.__init__(self, "emulator-5554", adb="adb")
        self.probe = probe
        self.shell = []
        self.settings = {}
        self.night = "no"
        self.size_override = None
        self.installed_paths = []

    def sh(self, cmd, timeout=30):
        self.shell.append(cmd)
        if cmd.startswith("settings get"):
            key = cmd.split()[-1]
            return 0, self.settings.get(key, "null"), ""
        if cmd.startswith("settings put"):
            parts = cmd.split()
            self.settings[parts[3]] = parts[4]
            return 0, "", ""
        if cmd.strip() == "cmd uimode night":
            return 0, "Night mode: %s" % self.night, ""
        if cmd.startswith("cmd uimode night"):
            self.night = cmd.split()[-1]
            return 0, "Night mode: %s" % self.night, ""
        if cmd.strip() == "wm size":
            return 0, "Physical size: 1080x1920" + ("\nOverride size: %s" % self.size_override if self.size_override else ""), ""
        if cmd.startswith("wm size"):
            spec = cmd.split()[-1]
            self.size_override = None if spec == "reset" else spec
            return 0, "", ""
        if cmd.startswith("am send-trim-memory"):
            return 0, "", ""
        if cmd.startswith("date +%s"):
            return 0, "1700000000", ""
        if "date -s" in cmd:
            return 1, "", "date: cannot set date: Operation not permitted"
        if cmd.startswith("su 0 date") or cmd.startswith("date "):
            return 1, "", "Permission denied"
        if cmd.startswith("dumpsys package"):
            return 0, "versionName=1.2.3 versionCode=45", ""
        return 0, "", ""

    def adb(self, *args, **kw):
        return 0, "", ""

    def screenshot_png(self):
        return png_bytes()

    def tap(self, x, y):
        self.shell.append("TAP %d %d" % (x, y))

    def key(self, name):
        self.shell.append("KEY " + name)

    def pidof(self, pkg):
        return [1234]

    def foreground(self):
        return "com.fake.game"

    def install(self, path):
        self.installed_paths.append(path)

    def stop(self, pkg):
        self.shell.append("STOP " + pkg)

    def launch(self, pkg, token=None, port=None):
        return 321

    def clear_data(self, pkg):
        self.shell.append("CLEAR " + pkg)

    def info(self):
        return {"model": "Fake", "os": "Android 14", "ramMb": 4096, "display": [1080, 1920], "density": 420, "deviceId": "abc", "emulator": True}


@contextlib.contextmanager
def fake_env(root, probe=None):
    """repo_root → root; state → tmp; Session.device → FakeAndroid; check_logs/log không chạm adb."""
    home = tempfile.mkdtemp(prefix="qastate")
    dev = FakeAndroid(probe)
    saved = (q.repo_root, q.Session.device, q.check_logs, q.log_start, os.environ.get("QADEVICE_HOME"), q.AUTO_AUDIT)
    q.repo_root = lambda: root
    q.Session.device = lambda self: dev
    q.check_logs = lambda sess, dev_, defer=False: ([], 0)
    q.log_start = lambda sess, dev_: None
    os.environ["QADEVICE_HOME"] = home
    q._QA_CACHE.clear()
    try:
        yield dev
    finally:
        q.repo_root, q.Session.device, q.check_logs, q.log_start = saved[:4]
        if saved[4] is None:
            os.environ.pop("QADEVICE_HOME", None)
        else:
            os.environ["QADEVICE_HOME"] = saved[4]
        q.AUTO_AUDIT = saved[5]
        q._QA_CACHE.clear()
        shutil.rmtree(home, ignore_errors=True)


def new_session(dev, probe=True, feature="Shop"):
    sess = q.Session.create(dev, "com.fake.game", feature)
    if probe:
        sess.d["probe"] = {"ok": True, "base": "http://127.0.0.1:1", "token": "t", "port": 7531, "version": dev.probe.version if dev.probe else "1.2.1",
                           "screen_w": 1080, "screen_h": 1920}
    sess.d["device"].update({"model": "Fake", "os": "Android 14"})
    sess.d["app"] = {"version": "1.2.3", "build": "45"}
    sess.save()
    return sess


def run_cmd(argv):
    out = io.StringIO()
    with contextlib.redirect_stdout(out):
        rc = q.main(argv)
    return rc, out.getvalue()


# ───────────────────────────────────────────────────────────── schema · merge · ghi ngược

class SchemaTests(unittest.TestCase):
    def test_deep_merge_semantics(self):
        base = {"a": {"x": 1, "y": [1, 2]}, "b": 1}
        over = {"a": {"y": [9], "z": 2}, "c": 3}
        m = qp.deep_merge(base, over)
        self.assertEqual(m, {"a": {"x": 1, "y": [9], "z": 2}, "b": 1, "c": 3})
        self.assertEqual(base["a"]["y"], [1, 2])  # không sửa tham số

    def test_qa_defaults_match_template_project_profile(self):
        if not os.path.isfile(REAL_PROJECT_PROFILE_PY):
            self.skipTest("không có .claude/scripts/project_profile.py")
        mod = qp.load_project_profile_module(REAL_REPO)
        if mod is None or not isinstance(getattr(mod, "DEFAULTS", None), dict) or "qa" not in mod.DEFAULTS:
            self.skipTest("project_profile.py chưa có DEFAULTS['qa']")
        self.assertEqual(q.QA_DEFAULTS, mod.DEFAULTS["qa"], "QA_DEFAULTS của qadevice lệch DEFAULTS['qa'] của template")

    def test_save_patch_preserves_hand_format_and_comments(self):
        root = make_repo()
        before = open(os.path.join(root, ".claude", "project-profile.json"), encoding="utf-8").read()
        qp.save_qa_patch(root, {"status": "draft", "conventions": {"featuresEnum": "X+Features"}})
        after = open(os.path.join(root, ".claude", "project-profile.json"), encoding="utf-8").read()
        self.assertTrue(after.startswith(before.rstrip().rstrip("}").rstrip()))  # phần cũ giữ nguyên byte
        self.assertIn('"$comment_qa"', after)
        data = json.loads(after)
        self.assertEqual(data["qa"]["conventions"]["featuresEnum"], "X+Features")
        self.assertEqual(data["nested"]["qa"]["decoy"], True)  # "qa" lồng không bị đụng
        # patch lần 2: merge, giữ key cũ, không nhân đôi comment
        qp.save_qa_patch(root, {"liveness": {"observed": {"Shop": "2026-01-01"}}})
        text = open(os.path.join(root, ".claude", "project-profile.json"), encoding="utf-8").read()
        data = json.loads(text)
        self.assertEqual(data["qa"]["conventions"]["featuresEnum"], "X+Features")
        self.assertEqual(data["qa"]["liveness"]["observed"], {"Shop": "2026-01-01"})
        self.assertEqual(text.count('"$comment_qa"'), 1)
        self.assertEqual(text.count('"qa":'), 2)  # 1 thật + 1 decoy lồng
        qp.save_qa_patch(root, {"status": "reviewed"}, replace=True)
        data = json.loads(open(os.path.join(root, ".claude", "project-profile.json"), encoding="utf-8").read())
        self.assertEqual(set(data["qa"]), {"status", "schemaVersion"})

    def test_load_profile_merges_and_tracks_provenance(self):
        root = make_repo(with_qa=True)
        qa, prov = qp.load_qa_profile(root, with_provenance=True)
        self.assertEqual(qa["status"], "reviewed")
        self.assertEqual(qa["conventions"]["cheatTypeName"], "GameSystems")  # default
        self.assertEqual(prov["conventions.featuresEnum"], "dev")
        self.assertEqual(prov["conventions.cheatTypeName"], "default")
        self.assertNotIn("_block", prov)
        _, prov2 = qp.load_qa_profile(make_repo(), with_provenance=True)
        self.assertEqual(prov2["_block"], "missing")


# ───────────────────────────────────────────────────────────── tự dò quy ước · liveness

class DetectTests(unittest.TestCase):
    def test_parse_enum_and_nested_type_name(self):
        name, path, vals = qp.find_features_enum(make_repo())
        self.assertEqual(name, "GameEnums+Features")
        names = [v[0] for v in vals]
        self.assertEqual(names[:4], ["none", "Settings", "Shop", "RewardPopup"])
        self.assertIn("Tut1Intro_Retired", names)
        self.assertEqual(dict(vals)["Admin"], 50)
        self.assertNotIn("A", names)  # enum Other không lẫn

    def test_detect_conventions_template_layout(self):
        det = qp.detect_conventions(make_repo())
        v = {k: d["value"] for k, d in det.items()}
        self.assertEqual(v["featuresEnum"], "GameEnums+Features")
        self.assertEqual(v["screenBaseTypes"], ["FeatureBaseController"])  # PackagePurchaseBaseController (MonoBehaviour) + EditorWindow bị loại
        self.assertEqual((v["cheatTypeName"], v["cheatFlagMember"], v["cheatEnableMethod"], v["cheatDisableMethod"]),
                         ("GameSystems", "isCheat", "TryEnableCheat", "DisableCheat"))
        self.assertEqual(v["cheatMethodPrefix"], "Cheat_")
        self.assertEqual(v["playerDataFacade"], "PlayerDataManager")
        self.assertEqual(v["trackingEventNameType"], "TrackingEventName")
        self.assertEqual(v["csvConfigGlob"], "Assets/_Project/**/CsvConfig/*.csv")
        self.assertEqual(v["screenPrefabPrefix"], "screen_")
        self.assertEqual(det["screenBaseTypes"]["source"], "auto")
        self.assertEqual(det["trackingLogPattern"]["source"], "default")
        self.assertEqual(det["_deadFeaturePatterns"]["value"], ["_Retired$"])

    def test_detect_old_layout_without_csvconfig(self):
        root = make_repo()
        shutil.move(os.path.join(root, "Assets/_Project/Features/Monetization/Shop/CsvConfig"), os.path.join(root, "Assets/_Project/Csv/Collection"))
        shutil.rmtree(os.path.join(root, "Assets/_Project/Features/System"))
        for extra in ("A", "B"):
            with open(os.path.join(root, "Assets/_Project/Csv/Collection", extra + ".csv"), "w") as f:
                f.write("id\n1\n")
        g, d = qp.detect_csv_glob(root)
        self.assertEqual(g, "Assets/_Project/Csv/**/*.csv")

    def test_classify_features(self):
        root = make_repo()
        qa = qp.deep_merge(qp.QA_DEFAULTS, {"liveness": {"forceLive": ["Admin"], "forceDead": ["RewardPopup"], "observed": {"Settings": "2026-01-01"}}})
        cls = qp.classify_features(root, qa)
        self.assertEqual(cls["Tut1Intro_Retired"]["state"], "dead")
        self.assertEqual(cls["RewardPopup"]["state"], "dead")
        self.assertEqual(cls["Admin"]["state"], "live")
        self.assertEqual(cls["Settings"]["state"], "live")
        self.assertEqual(cls["Shop"]["state"], "referenced")
        self.assertEqual(cls["DeadByBundleOnly"]["state"], "unreferenced")  # chỉ AssetRefs.cs gọi → không tính
        self.assertNotIn("none", cls)

    def test_match_feature_from_screen_names(self):
        names = ["Settings", "Shop", "OfflineEarnings", "RewardPopup"]
        self.assertEqual(qp.match_feature("screen_shop(Clone)", names), "Shop")
        self.assertEqual(qp.match_feature("screen_offline_earning", names), "OfflineEarnings")
        self.assertEqual(qp.match_feature("ScreenRewardPopupController", names), "RewardPopup")
        self.assertIsNone(qp.match_feature("Canvas", names))


# ───────────────────────────────────────────────────────────── QaProfile.md · charter · coverage · csv · autotest

class DocCharterTests(unittest.TestCase):
    def test_fill_auto_sections_and_status(self):
        doc = "**Status:** template\n**Cập nhật:** <YYYY-MM-DD> · x\n<!-- auto:a:start -->\nold\n<!-- auto:a:end -->\nDEV TEXT\n<!-- auto:b:start -->\n<!-- auto:b:end -->\n"
        out = qp.fill_auto_sections(doc, {"a": "NEW"})
        self.assertIn("<!-- auto:a:start -->\nNEW\n<!-- auto:a:end -->", out)
        self.assertIn("DEV TEXT", out)
        self.assertIn("<!-- auto:b:start -->\n<!-- auto:b:end -->", out)
        out = qp.set_status(out, "draft", "2026-01-02")
        self.assertEqual(qp.doc_status(out), "draft")
        self.assertIn("**Cập nhật:** 2026-01-02 · x", out)

    def test_charter_parsing_and_case_ids(self):
        root = make_repo()
        qa = qp.QA_DEFAULTS
        chs = qp.list_charters(root, qa)
        self.assertEqual([c["id"] for c in chs], ["shop"])
        self.assertEqual(chs[0]["cases"], ["P0-01", "P0-02", "P1-01", "P2-01"])
        cases = qp.charter_cases(root, qa)
        self.assertEqual(cases["shop/P1-01"], {"charter": "shop", "case": "P1-01", "priority": "P1", "title": "Thiếu tiền"})
        self.assertEqual(qp.split_case("shop/p0-01"), ("shop", "P0-01"))
        self.assertEqual(qp.split_case("P0-01", "shop"), ("shop", "P0-01"))
        self.assertEqual(qp.split_case("P0-01"), ("", "P0-01"))
        self.assertIsNone(qp.split_case("abc"))
        self.assertEqual(qp.charter_slug("Offline Earning / Home"), "offline-earning-home")

    def test_charter_template_has_case_rows(self):
        tpl = open(os.path.join(SKILL_DIR, "references", "charter.template.md"), encoding="utf-8").read()
        ids = [m.group(1) for m in qp.CASE_ID_RE.finditer(tpl)]
        self.assertIn("P0-01", ids)
        self.assertIn("P2-01", ids)
        self.assertEqual(len(ids), len(set(ids)))
        txt = qp.new_charter_text(tpl, "Offline earning", qp.QA_DEFAULTS, "GDD.md")
        self.assertIn("# Charter — Offline earning", txt)
        self.assertIn("Nguồn: GDD.md", txt)

    def test_coverage_summary(self):
        root = make_repo()
        planned = qp.charter_cases(root, qp.QA_DEFAULTS)
        run = [{"case": "shop/P0-01", "result": "pass"}, {"case": "shop/P1-01", "result": "fail"}, {"case": "other/P0-01", "result": "pass"}]
        classes = {"Shop": {"state": "referenced"}, "Settings": {"state": "live"}, "Dead": {"state": "dead"}}
        cov = qp.coverage_summary(run, planned, classes, {"Shop": "2026-01-01"})
        b = cov["charters"]["shop"]
        self.assertEqual((b["planned"], b["run"], b["pass"], b["fail"]), (4, 2, 1, 1))
        self.assertEqual(b["not_run"], ["shop/P0-02", "shop/P2-01"])
        self.assertEqual(cov["adhoc"], ["other/P0-01"])
        self.assertEqual(cov["unvisited"], ["Settings"])
        md = qp.coverage_markdown(cov, "Shop")
        self.assertIn("| shop | 4 | 2 | 1 | 1 |", md)
        self.assertIn("chưa mở: 1", md)

    def test_csv_oracle(self):
        root = make_repo()
        path, _ = qp.find_csv(root, qp.QA_DEFAULTS, "coinpack")
        self.assertTrue(path.endswith("CoinPack.csv"))
        header, rows = qp.read_csv_rows(path)
        self.assertEqual(header[0], "id")  # BOM bỏ
        self.assertEqual([r["id"] for r in rows], ["1", "2", "3"])  # dòng #comment bỏ
        self.assertEqual([r["id"] for r in qp.filter_rows(rows, ["price>=250"])], ["2", "3"])
        self.assertEqual([r["id"] for r in qp.filter_rows(rows, ["pack_name~_2$"])], ["2"])
        self.assertEqual([r["id"] for r in qp.filter_rows(rows, ["id!=1", "price<900"])], ["2"])
        none, cands = qp.find_csv(root, qp.QA_DEFAULTS, "nope")
        self.assertIsNone(none)

    def test_parse_autotest_report_both_formats(self):
        root = make_repo()
        rep = qp.parse_autotest_report(os.path.join(root, "AutoTestReports", "20260101-000000", "report.json"))
        self.assertEqual(rep["version"], "0.1.1")
        self.assertEqual(rep["bySeverity"], {"Minor": 1, "Critical": 1, "Major": 1})
        self.assertEqual(rep["coveredScreens"], ["Shop"])
        self.assertEqual(rep["failedScreens"], ["Admin"])
        self.assertEqual([c["status"] for c in rep["cases"]], ["Passed", "Passed", "Failed", "Warning"])
        md = qp.autotest_markdown(rep)
        self.assertIn("Blocker/Critical (1)", md)
        self.assertIn("NullReferenceException", md)
        p2 = os.path.join(root, "AutoTestReports", "20260102-000000", "report.json")
        report_02(p2)
        rep2 = qp.parse_autotest_report(p2)
        self.assertEqual(rep2["version"], "0.2.0")
        self.assertEqual(rep2["bySeverity"], {"Critical": 1, "Info": 1})
        self.assertEqual(rep2["coveredScreens"], ["Shop"])
        self.assertEqual(rep2["failedScreens"], ["Settings"])
        self.assertEqual(qp.latest_report(root, qp.QA_DEFAULTS), p2)


# ───────────────────────────────────────────────────────────── audit: flags camelCase · noise · expect

class AuditFlagTests(unittest.TestCase):
    def ui(self, flags, detail=None, path="Canvas/btn"):
        e = {"id": 7, "path": path, "kind": "Button", "text": "Mua", "rect": [0, 0, 300, 120], "interactable": True, "flags": flags}
        if detail:
            e["flagDetail"] = detail
        return {"screen": {"w": 1080, "h": 1920}, "screens": ["screen_shop"], "elements": [e]}

    def test_probe13_flags_map_to_findings(self):
        f = q.audit_elements(self.ui(["deadButton", "noRaycastTarget", "overlapButton", "missingSprite", "solidBackground"],
                                     {"overlapButton": "bị 'btn_x' [3] đè 60%"}))
        kinds = {x["kind"]: x for x in f}
        self.assertEqual(kinds["dead-button"]["severity"], "medium")
        self.assertEqual(kinds["no-raycast-target"]["severity"], "high")
        self.assertEqual(kinds["overlap-button"]["severity"], "medium")
        self.assertIn("bị 'btn_x' [3] đè 60%", kinds["overlap-button"]["msg"])
        self.assertEqual(kinds["overlap-button"]["detail"], "bị 'btn_x' [3] đè 60%")
        self.assertEqual(kinds["sprite-missing-colored"]["severity"], "low")
        self.assertEqual(kinds["solid-background"]["severity"], "info")
        self.assertEqual(f[0]["severity"], "high")  # sắp theo mức

    def test_unknown_flag_becomes_kebab_medium_and_legacy_flags_untouched(self):
        f = q.audit_elements(self.ui(["weirdNewCheck", "noSprite"]))
        kinds = {x["kind"]: x for x in f}
        self.assertEqual(kinds["weird-new-check"]["severity"], "medium")
        self.assertIn("missing-sprite", kinds)  # nhánh cũ của noSprite vẫn chạy
        self.assertNotIn("no-sprite", kinds)

    def test_noise_rules_suppress_by_path_and_screen(self):
        noise = [{"flag": "dead-button", "match": r"Canvas/btn$", "reason": "nút trang trí"},
                 {"flag": "*", "match": "screen:shop", "reason": "màn shop toàn nhiễu"}]
        f = q.audit_elements(self.ui(["deadButton", "noRaycastTarget"]), noise=noise[:1])
        by = {x["kind"]: x for x in f}
        self.assertEqual(by["dead-button"]["suppressed"], "nút trang trí")
        self.assertNotIn("suppressed", by["no-raycast-target"])
        f = q.audit_elements(self.ui(["deadButton", "noRaycastTarget"]), noise=noise[1:])
        self.assertTrue(all(x.get("suppressed") for x in f))

    def test_audit_text_hash_key(self):
        self.assertEqual(q.audit_text("#shop_title")[0], "medium")
        self.assertIsNone(q.audit_text("#1 Farmer"))

    def test_parse_expect_and_expect_met(self):
        self.assertEqual(q.parse_expect("@12"), ("id", 12))
        self.assertEqual(q.parse_expect("screen:Shop"), ("screen", "Shop"))
        self.assertEqual(q.parse_expect("Nhận thưởng"), ("text", "Nhận thưởng"))
        j = {"screens": ["screen_shop"], "scene": "Home", "elements": [{"id": 12, "text": "Nhận thưởng", "name": "btn_claim", "path": "C/btn_claim"}]}
        self.assertTrue(q.expect_met(j, "id", 12))
        self.assertTrue(q.expect_met(j, "screen", "shop"))
        self.assertTrue(q.expect_met(j, "text", "nhận"))
        self.assertTrue(q.expect_met(j, "name", "btn_claim"))
        self.assertFalse(q.expect_met(j, "text", "Mua"))


# ───────────────────────────────────────────────────────────── parser

class ParserTests(unittest.TestCase):
    def test_new_commands_parse(self):
        p = q.build_parser()
        a = p.parse_args(["--no-auto-audit", "profile", "init", "--force", "--dry-run"])
        self.assertTrue(a.no_auto_audit and a.force and a.dry_run and a.op == "init")
        self.assertEqual(p.parse_args(["doctor", "--strict"]).strict, True)
        self.assertEqual(p.parse_args(["selfcheck", "--no-tap"]).no_tap, True)
        self.assertEqual(p.parse_args(["coverage", "--charter", "shop"]).charter, "shop")
        a = p.parse_args(["charter", "new", "Offline earning", "--id", "offline", "--sources", "GDD"])
        self.assertEqual((a.op, a.feature, a.id), ("new", "Offline earning", "offline"))
        a = p.parse_args(["aspect", "tall", "--density", "320", "--audit"])
        self.assertEqual((a.spec, a.density, a.audit), ("tall", 320, True))
        a = p.parse_args(["env", "font-scale", "1.3", "--audit"])
        self.assertEqual((a.name, a.value), ("font-scale", "1.3"))
        self.assertTrue(p.parse_args(["env", "matrix", "--quick"]).quick)
        a = p.parse_args(["csv", "CoinPack", "--where", "id=1", "--col", "price", "--json"])
        self.assertEqual((a.name, a.where, a.col, a.json), ("CoinPack", ["id=1"], ["price"], True))
        a = p.parse_args(["pseudoloc", "on", "--mode", "expand"])
        self.assertEqual((a.mode_onoff, a.mode), ("on", "expand"))
        a = p.parse_args(["track", "--since", "last", "--expect", "shop_open"])
        self.assertEqual((a.since, a.expect), ("last", "shop_open"))
        self.assertEqual(p.parse_args(["autotest-import", "x/report.json"]).path, "x/report.json")
        a = p.parse_args(["build", "update", "latest", "--wipe", "--state", "A.B"])
        self.assertEqual((a.op, a.path, a.wipe, a.state), ("update", "latest", True, ["A.B"]))
        a = p.parse_args(["tap", "--text", "Mua", "--expect", "screen:Shop", "--timeout", "3"])
        self.assertEqual((a.expect, a.timeout), ("screen:Shop", 3.0))
        a = p.parse_args(["note", "--case", "shop/P0-01", "--result", "fail", "--bug", "12", "ghi chú"])
        self.assertEqual((a.case, a.result, a.bug, a.text), ("shop/P0-01", "fail", 12, "ghi chú"))
        self.assertEqual(p.parse_args(["note", "chỉ ghi chú"]).text, "chỉ ghi chú")
        self.assertTrue(p.parse_args(["audit", "--include-suppressed"]).include_suppressed)
        self.assertTrue(p.parse_args(["start", "--no-profile-init"]).no_profile_init)
        a = p.parse_args(["doctor", "--json", "--fix", "--dry-run"])
        self.assertEqual((a.json, a.fix, a.dry_run, a.strict), (True, True, True, False))
        self.assertTrue(p.parse_args(["plan", "--only-screens"]).only_screens)
        self.assertEqual(q.VERSION, "1.6.0")
        self.assertEqual(q.AUTOTEST_MIN_VERSION, "0.2.0")
        self.assertEqual(q.PROBE_MIN_VERSION, "1.2.1")
        self.assertEqual(set(q.PROBE_FEATURES), {"pseudoloc", "flags", "screenBaseTypes"})


# ───────────────────────────────────────────────────────────── lệnh profile / doctor trên repo giả

class ProfileCommandTests(unittest.TestCase):
    def test_profile_init_sync_doctor_roundtrip(self):
        root = make_repo()
        with fake_env(root):
            rc, out = run_cmd(["profile", "init", "--dry-run"])
            self.assertEqual(rc, 0)
            self.assertIn("GameEnums+Features", out)
            self.assertFalse(os.path.isfile(os.path.join(root, ".claude", "docs", "QaProfile.md")))
            rc, out = run_cmd(["profile", "init"])
            self.assertEqual(rc, 0)
            prof = json.load(open(os.path.join(root, ".claude", "project-profile.json"), encoding="utf-8"))
            self.assertEqual(prof["qa"]["status"], "draft")
            self.assertEqual(prof["qa"]["conventions"]["screenBaseTypes"], ["FeatureBaseController"])
            self.assertEqual(prof["qa"]["liveness"]["deadFeaturePatterns"], ["_Retired$"])
            self.assertEqual(prof["nested"]["qa"]["decoy"], True)
            doc = open(os.path.join(root, ".claude", "docs", "QaProfile.md"), encoding="utf-8").read()
            self.assertEqual(qp.doc_status(doc), "draft")
            self.assertIn("# QA Profile — Fake Game", doc)
            self.assertIn("| `featuresEnum` | `GameEnums+Features` | auto |", doc)
            self.assertIn("Tut1Intro_Retired", doc)
            self.assertTrue(os.path.isfile(os.path.join(root, ".claude", "docs", "qa", "charters", "README.md")))
            # dev sửa tay 1 key + ghi chữ riêng vào doc → init lại giữ cả hai
            qp.save_qa_patch(root, {"conventions": {"cheatMethodPrefix": "Hack_"}})
            with open(os.path.join(root, ".claude", "docs", "QaProfile.md"), "a", encoding="utf-8") as f:
                f.write("\nDEV NOTE GIỮ LẠI\n")
            rc, out = run_cmd(["profile", "init"])
            self.assertIn("cheatMethodPrefix", out)
            self.assertIn("dev", out.split("cheatMethodPrefix")[1].splitlines()[0])
            prof = json.load(open(os.path.join(root, ".claude", "project-profile.json"), encoding="utf-8"))
            self.assertEqual(prof["qa"]["conventions"]["cheatMethodPrefix"], "Hack_")
            doc = open(os.path.join(root, ".claude", "docs", "QaProfile.md"), encoding="utf-8").read()
            self.assertIn("DEV NOTE GIỮ LẠI", doc)
            # sync → probe override (5 key) + autotest liveness, giữ key khác
            rc, out = run_cmd(["profile", "sync"])
            ov = json.load(open(os.path.join(root, "Assets", "Resources", "EzgQaProbeConfig.json")))
            self.assertEqual(set(ov), set(qp.PROBE_OVERRIDE_KEYS))
            self.assertEqual(ov["screenBaseTypes"], ["FeatureBaseController"])
            at = json.load(open(os.path.join(root, "ProjectSettings", "EZGAutoTestSettings.json")))
            self.assertEqual(at["economy"]["skipCurrencies"], ["Gold"])
            self.assertEqual(at["liveness"], {"enabled": True, "deadFeaturePatterns": ["_Retired$"], "forceLive": [], "forceDead": [], "observedLive": []})
            rc, out = run_cmd(["profile", "sync"])
            self.assertIn("không có gì đổi", out)
            # doctor --strict qua; show in provenance
            rc, out = run_cmd(["doctor", "--strict"])
            self.assertEqual(rc, 0, out)
            self.assertIn("probe override khớp", out)
            self.assertIn("com.ezg.autotest 0.1.1 (manifest) ⚠ < 0.2.0", out)
            rc, out = run_cmd(["profile", "show"])
            self.assertIn("conventions.cheatMethodPrefix", out)
            self.assertIn("status draft", out)

    def test_doctor_strict_fails_without_qa_block(self):
        root = make_repo()
        with fake_env(root):
            with self.assertRaises(SystemExit) as cm:
                run_cmd(["doctor", "--strict"])
            self.assertEqual(cm.exception.code, 1)
            rc, out = run_cmd(["doctor"])
            self.assertEqual(rc, 0)
            self.assertIn("chưa có block qa", out)

    def test_profile_init_without_template_uses_minimal_doc(self):
        root = make_repo(with_template=False)
        with fake_env(root):
            rc, out = run_cmd(["profile", "init"])
            self.assertEqual(rc, 0)
            doc = open(os.path.join(root, ".claude", "docs", "QaProfile.md"), encoding="utf-8").read()
            self.assertIn("Khung tối thiểu", doc)
            self.assertIn("| `featuresEnum` |", doc)
            self.assertEqual(qp.doc_status(doc), "draft")


# ───────────────────────────────────────────────────────────── lệnh trong phiên: note/coverage/charter/track/autotest-import

class SessionCommandTests(unittest.TestCase):
    def test_note_case_and_coverage_and_charter_diff(self):
        root = make_repo()
        with fake_env(root) as dev:
            sess = new_session(dev, probe=False)
            rc, out = run_cmd(["note", "--case", "P0-01", "--result", "pass"])
            self.assertEqual(rc, 1)  # chưa gắn charter → phải ghi đầy đủ
            rc, out = run_cmd(["note", "--case", "shop/p0-01", "--result", "pass", "mua gói 1 ok"])
            self.assertEqual(rc, 0)
            rc, out = run_cmd(["note", "--case", "P1-01", "--result", "fail", "--bug", "77"])  # charter đã gắn từ lệnh trước
            self.assertEqual(rc, 0)
            cases = q.read_jsonl(sess.path("cases.jsonl"))
            self.assertEqual([(c["case"], c["result"], c.get("bug")) for c in cases], [("shop/P0-01", "pass", None), ("shop/P1-01", "fail", 77)])
            rc, out = run_cmd(["note", "ghi chú thường"])
            self.assertEqual(rc, 0)
            rc, out = run_cmd(["coverage"])
            self.assertEqual(rc, 0)
            self.assertIn("| shop | 4 | 2 | 1 | 1 | 0 | 0 | shop/P0-02, shop/P2-01 |", out)
            self.assertTrue(os.path.isfile(sess.path("coverage.md")))
            rc, out = run_cmd(["charter", "diff"])
            self.assertIn("2 CHƯA chạy", out)
            self.assertIn("P0 chưa chạy: shop/P0-02", out)
            self.assertIn("ca FAIL: shop/P1-01", out)
            rc, out = run_cmd(["charter", "list"])
            self.assertIn("shop", out)
            rc, out = run_cmd(["charter", "new", "Offline earning", "--sources", "GDD.md"])
            self.assertEqual(rc, 0)
            self.assertTrue(os.path.isfile(os.path.join(root, ".claude", "docs", "qa", "charters", "offline-earning.md")))
            self.assertEqual(q.Session.load().d.get("charter"), "offline-earning")
            rc, out = run_cmd(["charter", "new", "Offline earning"])
            self.assertEqual(rc, 1)  # đã có → không ghi đè
            # report có mục ca + coverage
            md = q.build_session_report(q.Session.load())
            self.assertIn("## Ca theo charter (2)", md)
            self.assertIn("## Coverage", md)

    def test_autotest_import_marks_session(self):
        root = make_repo()
        with fake_env(root) as dev:
            sess = new_session(dev, probe=False)
            rc, out = run_cmd(["autotest-import"])
            self.assertEqual(rc, 0)
            self.assertIn("Màn autotest đã mở OK", out)
            self.assertIn("⚠ report của autotest < 0.2.0", out)
            d = q.Session.load().d
            self.assertEqual(d["autotest"]["coveredScreens"], ["Shop"])
            self.assertEqual(d["autotest"]["failedScreens"], ["Admin"])
            self.assertTrue(os.path.isfile(sess.path("autotest-summary.md")))
            rc, out = run_cmd(["coverage"])
            self.assertIn("autotest Editor đã mở OK", out) if "Shop" in out else None

    def test_track_filters_and_expect(self):
        root = make_repo()
        with fake_env(root) as dev:
            sess = new_session(dev, probe=False)
            sess.d["pids"] = [1234]
            sess.d["log"] = {"file": sess.path("device.log"), "offset": 0, "pid": 0}
            sess.save()
            q.write_text(sess.path("device.log"), "\n".join([
                "10-15 00:41:02.100  1234  1250 I Unity   : [Tracking] shop_open {source=home}",
                "10-15 00:41:02.200  1234  1250 I Unity   : [Firebase] logEvent iap_success",
                "10-15 00:41:02.300  9999  9999 I Unity   : [Tracking] other_app_event",
                "10-15 00:41:02.400  1234  1250 I Unity   : normal log"]) + "\n")
            rc, out = run_cmd(["track"])
            self.assertEqual(rc, 0)
            self.assertIn("2 dòng khớp", out)
            self.assertIn("shop_open", out)
            self.assertNotIn("other_app_event", out)
            rc, out = run_cmd(["track", "--expect", "iap_success"])
            self.assertEqual(rc, 0)
            with self.assertRaises(SystemExit) as cm:
                run_cmd(["track", "--expect", "level_up"])
            self.assertEqual(cm.exception.code, 1)
            steps = q.read_jsonl(sess.path("steps.jsonl"))
            self.assertEqual(steps[-1]["kind"], "tracking-missing")

    def test_csv_command(self):
        root = make_repo()
        with fake_env(root):
            rc, out = run_cmd(["csv", "--list"])
            self.assertIn("CoinPack", out)
            rc, out = run_cmd(["csv", "coinpack", "--where", "price>=250", "--col", "id", "--json"])
            self.assertEqual(json.loads(out), [{"id": "2"}, {"id": "3"}])
            rc, out = run_cmd(["csv", "nope"])
            self.assertEqual(rc, 1)


# ───────────────────────────────────────────────────────────── probe/thiết bị giả: auto-audit · expect · env · aspect · selfcheck · flush

class DeviceFlowTests(unittest.TestCase):
    def ui(self, screens, n_el=1, flags=()):
        els = [{"id": i + 1, "name": "btn%d" % i, "path": "Canvas/btn%d" % i, "kind": "Button", "text": "Shop" if i == 0 else "x",
                "label": "Shop" if i == 0 else "", "rect": [10, 10 + 120 * i, 200, 100], "interactable": True, "flags": list(flags)} for i in range(n_el)]
        return {"scene": "Home", "screens": screens, "screen": {"w": 1080, "h": 1920, "dpi": 420}, "elements": els}

    def test_auto_audit_only_when_screens_change(self):
        root = make_repo(with_qa=True)
        probe = FakeProbe()
        with fake_env(root, probe) as dev:
            sess = new_session(dev)
            q.observe(sess, dev, "start", logs=False)
            probe.ui = self.ui(["screen_home"], flags=["deadButton"])
            r1 = q.maybe_auto_audit(sess, dev)
            self.assertIsNotNone(r1)
            self.assertTrue(os.path.isfile(sess.path("audit-auto-001.json")))
            self.assertIsNone(q.maybe_auto_audit(sess, dev))  # cùng màn → không audit lại
            probe.ui = self.ui(["screen_home", "screen_shop"], flags=["deadButton"])
            out = io.StringIO()
            with contextlib.redirect_stdout(out):
                r2 = q.maybe_auto_audit(sess, dev)
            self.assertIn("⚑ audit tự động #2", out.getvalue())
            self.assertTrue(os.path.isfile(sess.path("audit-auto-002.json")))
            steps = q.read_jsonl(sess.path("steps.jsonl"))
            self.assertEqual(steps[-1]["action"], "auto-audit")
            self.assertEqual(steps[-1]["screens"], ["screen_home", "screen_shop"])
            # màn home có luật nhiễu screen:home (with_qa) → finding ở audit #1 bị suppressed, #2 (shop) không
            a1 = q.read_json(sess.path("audit-auto-001.json"))
            self.assertTrue(all(f.get("suppressed") for f in a1["findings"]))
            self.assertEqual(sess.d["observed"].get("Shop"), qp.today())
            q.AUTO_AUDIT = False
            self.assertIsNone(q.maybe_auto_audit(sess, dev))

    def test_run_audit_hides_suppressed_unless_asked(self):
        root = make_repo(with_qa=True)
        probe = FakeProbe(self.ui(["screen_home"], flags=["deadButton"]))
        with fake_env(root, probe) as dev:
            sess = new_session(dev)
            out = io.StringIO()
            with contextlib.redirect_stdout(out):
                findings, _ = q.run_audit(sess, dev)
            self.assertIn("1 bị luật nhiễu của project ẩn", out.getvalue())
            self.assertNotIn("[medium] [1] dead-button", out.getvalue())
            out = io.StringIO()
            with contextlib.redirect_stdout(out):
                q.run_audit(sess, dev, include_suppressed=True)
            self.assertIn("⟂ nhiễu: backdrop", out.getvalue())

    def test_check_expect_pass_and_fail(self):
        root = make_repo()
        probe = FakeProbe(self.ui(["screen_shop"]))
        with fake_env(root, probe) as dev:
            sess = new_session(dev)

            class A(object):
                timeout = 0.6
            out = io.StringIO()
            with contextlib.redirect_stdout(out):
                self.assertTrue(q.check_expect(A(), sess, dev, "screen:shop"))
                self.assertTrue(q.check_expect(A(), sess, dev, "Shop"))
                self.assertFalse(q.check_expect(A(), sess, dev, "text:Không có"))
            self.assertIn("KỲ VỌNG KHÔNG ĐẠT", out.getvalue())
            self.assertEqual(len(sess.d["expect_failed"]), 1)
            steps = q.read_jsonl(sess.path("steps.jsonl"))
            self.assertEqual([s.get("result") for s in steps if s["action"] == "check"], ["pass", "pass", "fail"])
            md = q.build_session_report(q.Session.load())
            self.assertIn("## Kỳ vọng không đạt (1)", md)

    def test_env_apply_revert_and_aspect(self):
        root = make_repo()
        with fake_env(root) as dev:
            sess = new_session(dev, probe=False)
            self.assertIn("1.3", q.apply_env(sess, dev, "font-scale", "1.3"))
            self.assertEqual(dev.settings["font_scale"], "1.3")
            self.assertIn("BẬT", q.apply_env(sess, dev, "dark", "on"))
            self.assertEqual(dev.night, "yes")
            q.apply_env(sess, dev, "animator", "0")
            self.assertEqual(dev.settings["animator_duration_scale"], "0")
            q.apply_env(sess, dev, "kill-activities", "on")
            self.assertEqual(dev.settings["always_finish_activities"], "1")
            self.assertIn("trim-memory", q.apply_env(sess, dev, "trim-memory", None))
            with self.assertRaises(q.NotSupported):
                q.apply_env(sess, dev, "clock", "+1d")  # máy không root
            with self.assertRaises(q.QaError):
                q.apply_env(sess, dev, "clock", "tomorrow")
            with self.assertRaises(q.QaError):
                q.apply_env(sess, dev, "bogus", "1")
            ch = q.Session.load().d["changed"]["env"]
            self.assertEqual(set(ch), {"font-scale", "dark", "animator", "kill-activities", "clock"})
            rc, out = run_cmd(["aspect", "tall"])
            self.assertEqual(rc, 0)
            self.assertEqual(dev.size_override, "720x1600")
            self.assertTrue(q.Session.load().d["changed"]["wm_size"])
            notes = q.revert_env(q.Session.load(), dev)
            self.assertEqual(notes, [])
            self.assertEqual(dev.settings["font_scale"], "1")
            self.assertEqual(dev.night, "no")
            self.assertEqual(dev.settings["animator_duration_scale"], "1")
            self.assertEqual(dev.settings["always_finish_activities"], "0")
            self.assertEqual(dev.settings["auto_time"], "1")
            self.assertIsNone(dev.size_override)
            self.assertEqual(q.Session.load().d["changed"]["env"], {})

    def test_env_command_unsupported_is_not_an_error(self):
        root = make_repo()
        with fake_env(root) as dev:
            new_session(dev, probe=False)
            rc, out = run_cmd(["env", "clock", "+1d"])
            self.assertEqual(rc, 0)
            self.assertIn("unsupported", out)

    def test_env_matrix_writes_table(self):
        root = make_repo()
        probe = FakeProbe(self.ui(["screen_home"]))
        with fake_env(root, probe) as dev:
            new_session(dev)
            rc, out = run_cmd(["env", "matrix", "--quick", "--wait", "0"])
            self.assertEqual(rc, 0, out)
            sess = q.Session.load()
            md = q.read_text(sess.path("env-matrix.md"))
            for step in ("font-scale=1.3", "aspect=720x1280", "dark=on", "animator=0"):
                self.assertIn("| %s | ok |" % step, md)
            self.assertEqual(dev.settings["font_scale"], "1")  # từng bước đã hoàn nguyên
            self.assertIsNone(dev.size_override)
            self.assertEqual(dev.night, "no")
            self.assertIn("## Ma trận môi trường", q.build_session_report(sess))

    def test_pseudoloc_gating(self):
        root = make_repo()
        probe = FakeProbe(self.ui(["screen_home"]), version="1.2.1")
        with fake_env(root, probe) as dev:
            new_session(dev)
            rc, out = run_cmd(["pseudoloc", "on"])
            self.assertEqual(rc, 0)
            self.assertIn("unsupported (probe v1.2.1 < 1.3.0", out)
            self.assertFalse(any(c[0] == "/pseudoloc" for c in probe.calls))
            probe.version = "1.3.0"
            sess = q.Session.load()
            sess.d["probe"]["version"] = "1.3.0"
            sess.save()
            rc, out = run_cmd(["pseudoloc", "on", "--wait", "0"])
            self.assertEqual(rc, 0, out)
            self.assertIn("12 phần tử chữ đổi", out)
            self.assertTrue(any(c[0] == "/pseudoloc" and c[1].get("on") == 1 for c in probe.calls))
            self.assertTrue(q.Session.load().d["pseudoloc"])

    def test_selfcheck_fails_when_logcat_missing(self):
        root = make_repo()
        probe = FakeProbe(self.ui(["screen_home"]))
        with fake_env(root, probe) as dev:
            new_session(dev)
            with self.assertRaises(SystemExit) as cm:
                run_cmd(["selfcheck", "--no-tap"])
            self.assertEqual(cm.exception.code, 1)

    def test_selfcheck_without_safe_button_skips_tap(self):
        root = make_repo()
        probe = FakeProbe(self.ui(["screen_home"]))
        with fake_env(root, probe) as dev:
            sess = new_session(dev)
            sess.d["log"] = {"file": sess.path("device.log"), "offset": 0, "pid": 0}
            sess.save()
            q.write_text(sess.path("device.log"), "10-15 00:41:02.100  1234  1250 I Unity   : hello\n")
            rc, out = run_cmd(["selfcheck"])
            self.assertEqual(rc, 0, out)
            res = q.read_json(sess.path("selfcheck.json"))["results"]
            st = {r["check"]: r["status"] for r in res}
            self.assertEqual((st["probe"], st["shot"], st["ui"], st["tap"]), ("pass", "pass", "pass", "skip"))
            probe.ui["elements"][0]["text"] = probe.ui["elements"][0]["label"] = "Settings"
            probe.ui_seq = [probe.ui, self.ui(["screen_home", "screen_settings"])]
            rc, out = run_cmd(["selfcheck"])
            st = {r["check"]: r["status"] for r in q.read_json(sess.path("selfcheck.json"))["results"]}
            self.assertEqual(st["tap"], "pass")
            self.assertIn("KEY back", dev.shell)

    def test_state_call_record_verified_and_stop_flushes_profile(self):
        root = make_repo(with_qa=True)
        probe = FakeProbe(self.ui(["screen_shop"]))
        with fake_env(root, probe) as dev:
            sess = new_session(dev)
            rc, out = run_cmd(["state", "PlayerDataManager.PlayerResource.Gold"])
            self.assertEqual(rc, 0)
            rc, out = run_cmd(["call", "FarmService.Cheat_AddGold", "100", "--no-shot"])
            self.assertEqual(rc, 0)
            rc, out = run_cmd(["call", "FarmService.NotCheat", "--no-shot"])
            d = q.Session.load().d
            self.assertEqual(list(d["verified_cheats"]), ["FarmService.Cheat_AddGold"])
            self.assertIn("PlayerDataManager.PlayerResource.Gold", d["verified_state"])
            self.assertEqual(d["observed"].get("Shop"), qp.today())  # call → auto-audit → quan sát màn
            rc, out = run_cmd(["stop"])
            self.assertEqual(rc, 0, out)
            self.assertIn("hồ sơ QA: +1 màn quan sát · +1 cheat · +1 state path", out)
            prof = json.load(open(os.path.join(root, ".claude", "project-profile.json"), encoding="utf-8"))
            self.assertEqual(prof["qa"]["liveness"]["observed"], {"Shop": qp.today()})
            self.assertEqual(prof["qa"]["verified"]["cheats"], {"FarmService.Cheat_AddGold": qp.today()})
            self.assertEqual(prof["qa"]["verified"]["statePaths"]["PlayerResource.Gold"]["path"], "PlayerDataManager.PlayerResource.Gold")
            self.assertEqual(prof["qa"]["status"], "reviewed")  # không đụng status/noise của dev
            self.assertEqual(len(prof["qa"]["noise"]), 1)
            self.assertTrue(any(c[0] == "/pseudoloc" for c in probe.calls) is False)
            md = q.read_text(q.Session.load().path("report.md"))
            self.assertIn("## Hồ sơ QA học được", md)
            self.assertIn("Cheat đã gọi OK: FarmService.Cheat_AddGold", md)

    def test_build_update_compares_state(self):
        root = make_repo(with_qa=True)
        qp.save_qa_patch(root, {"verified": {"statePaths": {"PlayerResource.Gold": {"path": "PlayerDataManager.PlayerResource.Gold", "lastOk": "2026-01-01"}}}})
        probe = FakeProbe(self.ui(["screen_home"]))
        apk = os.path.join(root, "new.apk")
        open(apk, "wb").write(b"apk")
        with fake_env(root, probe) as dev:
            sess = new_session(dev)
            saved = q.connect_probe
            q.connect_probe = lambda sess_, dev_, wait=30, cheat_wait=40: "OK"
            try:
                rc, out = run_cmd(["build", "update", apk, "--settle", "0", "--state", "Fake.X"])
            finally:
                q.connect_probe = saved
            self.assertEqual(rc, 0, out)
            self.assertEqual(dev.installed_paths, [apk])
            md = q.read_text(sess.path("migration.md"))
            self.assertIn("State so sánh: 2 đường dẫn · 0 khác", md)
            steps = q.read_jsonl(sess.path("steps.jsonl"))
            self.assertEqual(steps[-1]["action"], "build-update")


if __name__ == "__main__":
    unittest.main()
