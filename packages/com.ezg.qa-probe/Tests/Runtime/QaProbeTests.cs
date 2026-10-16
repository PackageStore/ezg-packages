using System;
using System.Collections;
using System.IO;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
#if EZG_QA_CHEAT_GATE
using Ezg.Package.CheatGate;
#endif
using TemplateGame = Ezg.QaProbe.Tests.Template.GameSystems;
using Object = UnityEngine.Object;

namespace Ezg.QaProbe.Tests
{
    public class QaProbeTests
    {
        private const string TOKEN = "QaProbeTestToken1234";
        private const string TEMPLATE = "Ezg.QaProbe.Tests.Template.GameSystems";
        private ProbeConfig _savedConfig;
#if EZG_QA_CHEAT_GATE
        private bool _savedUnlockInEditor;
#endif

        [SetUp]
        public void SetUp()
        {
            _savedConfig = EzgQaProbe.Config;
            TemplateGame.isCheat = false;
            TemplateGame.TryCalls = 0;
            TemplateGame.Grant = true;
#if EZG_QA_CHEAT_GATE
            _savedUnlockInEditor = CheatGate.UnlockInEditor;
            CheatGate.UnlockInEditor = false; // máy chưa có quyền: chỉ cờ cheat của game mở được cổng
#endif
        }

        [TearDown]
        public void TearDown()
        {
            EzgQaProbe.Deactivate();
            ProbeGate.SimulateRelease = false;
            EzgQaProbe.TestConfig = null;
            EzgQaProbe.Config = _savedConfig;
#if EZG_QA_CHEAT_GATE
            CheatGate.UnlockInEditor = _savedUnlockInEditor;
#endif
        }

        private static ProbeConfig TemplateConfig() => new ProbeConfig
        {
            releaseGate = TEMPLATE + ".isCheat", cheatOn = TEMPLATE + ".TryEnableCheat", cheatOff = TEMPLATE + ".DisableCheat"
        };

        // ───────────────────────────── tự dò config

        [Test]
        public void Detect_TemplateCheatGate()
        {
            var c = ProbeAutoConfig.Detect(new[] { typeof(TemplateGame) });
            Assert.AreEqual(TEMPLATE + ".isCheat", c.releaseGate);
            Assert.AreEqual(TEMPLATE + ".TryEnableCheat", c.cheatOn);
            Assert.AreEqual(TEMPLATE + ".DisableCheat", c.cheatOff);
        }

        [Test]
        public void Detect_RemoteConfigTryEnableIsNotCheatOn()
        {
            var c = ProbeAutoConfig.Detect(new[] { typeof(RemoteConfig.GameSystems) });
            Assert.AreEqual("Ezg.QaProbe.Tests.RemoteConfig.GameSystems.isCheat", c.releaseGate);
            Assert.AreEqual("", c.cheatOn);
            Assert.AreEqual("Ezg.QaProbe.Tests.RemoteConfig.GameSystems.DisableCheat", c.cheatOff);
        }

        [Test]
        public void Detect_PrefersEzgNamespaceAndSkipsNonBoolFlag()
        {
            var c = ProbeAutoConfig.Detect(new[] { typeof(Other.Game.GameSystems), typeof(WrongFlag.GameSystems), typeof(TemplateGame) });
            Assert.AreEqual(TEMPLATE + ".isCheat", c.releaseGate);
            c = ProbeAutoConfig.Detect(new[] { typeof(WrongFlag.GameSystems), typeof(Other.Game.GameSystems) });
            Assert.AreEqual("Other.Game.GameSystems.isCheat", c.releaseGate);
            Assert.AreEqual("", ProbeAutoConfig.Detect(new[] { typeof(WrongFlag.GameSystems), typeof(string) }).releaseGate);
        }

        [Test]
        public void ResolveDefaults_KeepsOverridesAndNoneDisables()
        {
            var c = JsonUtility.FromJson<ProbeConfig>("{\"releaseGate\":\"none\",\"cheatOff\":\"My.Flags.Off\"}");
            c.ResolveDefaults(new[] { typeof(TemplateGame) });
            Assert.AreEqual("", c.releaseGate);
            Assert.AreEqual(TEMPLATE + ".TryEnableCheat", c.cheatOn);
            Assert.AreEqual("My.Flags.Off", c.cheatOff);
            Assert.IsTrue(c.autoCheat);
            Assert.AreEqual(EzgQaProbe.DEFAULT_PORT, c.port);
        }

        // ───────────────────────────── config 1.3: key mới, lớp màn hình, cheat type riêng

        [Test]
        public void Config_ParsesNewKeysAndEmptyMeansDefault()
        {
            var c = JsonUtility.FromJson<ProbeConfig>(
                "{\"screenBaseTypes\":[\"My.UI.ScreenBase\",\"PopupBase\"],\"cheatTypeName\":\"DebugFlags\",\"cheatFlagMember\":\"cheatsEnabled\"," +
                "\"cheatEnableMethod\":\"EnableCheats\",\"cheatDisableMethod\":\"DisableCheats\"}");
            Assert.IsTrue(c.HasScreenBaseTypes);
            CollectionAssert.AreEqual(new[] { "My.UI.ScreenBase", "PopupBase" }, c.EffectiveScreenBaseTypes);
            Assert.AreEqual("DebugFlags", c.cheatTypeName);
            Assert.AreEqual("cheatsEnabled", c.cheatFlagMember);
            Assert.AreEqual("EnableCheats", c.cheatEnableMethod);
            Assert.AreEqual("DisableCheats", c.cheatDisableMethod);

            // Config 1.2.x (không có key mới) → hành vi cũ: danh sách mặc định, tự dò GameSystems.
            var old = JsonUtility.FromJson<ProbeConfig>("{\"releaseGate\":\"none\"}");
            old.ResolveDefaults(new[] { typeof(TemplateGame) });
            Assert.IsFalse(old.HasScreenBaseTypes);
            CollectionAssert.AreEqual(ProbeConfig.DEFAULT_SCREEN_BASE_TYPES, old.EffectiveScreenBaseTypes);
            Assert.AreEqual(TEMPLATE + ".TryEnableCheat", old.cheatOn);
            Assert.AreEqual("", old.cheatTypeName);

            // Mảng toàn chuỗi trống = chưa khai.
            var blank = JsonUtility.FromJson<ProbeConfig>("{\"screenBaseTypes\":[\"\",\" \"]}");
            Assert.IsFalse(blank.HasScreenBaseTypes);
            CollectionAssert.AreEqual(ProbeConfig.DEFAULT_SCREEN_BASE_TYPES, blank.EffectiveScreenBaseTypes);
        }

        [Test]
        public void Detect_UsesConfiguredCheatTypeAndMembers()
        {
            const string custom = "Ezg.QaProbe.Tests.Custom.DebugFlags";
            var types = new[] { typeof(TemplateGame), typeof(Custom.DebugFlags) };
            var c = ProbeAutoConfig.Detect(types, "DebugFlags", "cheatsEnabled", "EnableCheats", "DisableCheats");
            Assert.AreEqual(custom + ".cheatsEnabled", c.releaseGate);
            Assert.AreEqual(custom + ".EnableCheats", c.cheatOn, "hàm dev chỉ đích danh được nhận cả khi không có Action<bool>");
            Assert.AreEqual(custom + ".DisableCheats", c.cheatOff);

            // Tên đầy đủ cũng được; type không có cờ vẫn dùng được hàm bật/tắt.
            c = ProbeAutoConfig.Detect(types, custom, "noSuchFlag", "EnableCheats", "DisableCheats");
            Assert.AreEqual("", c.releaseGate);
            Assert.AreEqual(custom + ".EnableCheats", c.cheatOn);

            // Qua ResolveDefaults rồi bật/tắt thật.
            var cfg = new ProbeConfig { cheatTypeName = "DebugFlags", cheatFlagMember = "cheatsEnabled", cheatEnableMethod = "EnableCheats", cheatDisableMethod = "DisableCheats" };
            cfg.ResolveDefaults(types);
            EzgQaProbe.Config = cfg;
            Custom.DebugFlags.cheatsEnabled = false;
            Assert.IsTrue(ProbeGameCheat.Set(true, out var via, out var error), error);
            Assert.AreEqual(custom + ".EnableCheats", via);
            Assert.IsTrue(Custom.DebugFlags.cheatsEnabled);
            Assert.IsTrue(ProbeGameCheat.Set(false, out _, out _));
            Assert.IsFalse(Custom.DebugFlags.cheatsEnabled);

            // Không khai gì → vẫn là quy ước template (và vẫn bỏ TryEnableCheat() đời cũ).
            c = ProbeAutoConfig.Detect(new[] { typeof(RemoteConfig.GameSystems), typeof(Custom.DebugFlags) }, "", "", "", "");
            Assert.AreEqual("Ezg.QaProbe.Tests.RemoteConfig.GameSystems.isCheat", c.releaseGate);
            Assert.AreEqual("", c.cheatOn);
        }

        [Test]
        public void ScreenTypes_ComeFromConfig()
        {
            EzgQaProbe.Config = new ProbeConfig();
            ProbeHandlers.ResetCaches();
            Assert.IsTrue(ProbeHandlers.IsScreenType(typeof(Screens.FakeTemplateScreen)), "quy ước template nhận ra với config mặc định");
            Assert.IsFalse(ProbeHandlers.IsScreenType(typeof(Screens.FakeHomeScreen)));
            Assert.IsFalse(ProbeHandlers.IsScreenType(typeof(Screens.FakeShopScreen)));

            EzgQaProbe.Config = new ProbeConfig { screenBaseTypes = new[] { "FakeScreenBase", "FakeGenericScreen" } };
            ProbeHandlers.ResetCaches();
            Assert.IsTrue(ProbeHandlers.IsScreenType(typeof(Screens.FakeHomeScreen)));
            Assert.IsTrue(ProbeHandlers.IsScreenType(typeof(Screens.FakeShopScreen)), "lớp generic khai bằng tên không có `1");
            Assert.IsFalse(ProbeHandlers.IsScreenType(typeof(Screens.FakeTemplateScreen)), "config riêng thay hẳn danh sách mặc định");
            Assert.IsFalse(ProbeHandlers.IsScreenType(typeof(Screens.FakeScreenBase)), "chính lớp cơ sở không phải màn");

            EzgQaProbe.Config = new ProbeConfig { screenBaseTypes = new[] { "Ezg.QaProbe.Tests.Screens.FakeScreenBase" } };
            ProbeHandlers.ResetCaches();
            Assert.IsTrue(ProbeHandlers.IsScreenType(typeof(Screens.FakeHomeScreen)), "tên đầy đủ");
            ProbeHandlers.ResetCaches();
        }

        // ───────────────────────────── pseudo-localization

        [Test]
        public void PseudoLoc_TransformKeepsTagsAndModes()
        {
            Assert.AreEqual("[Ĥéļļö <b>Ŵöřļđ</b>~~~]", ProbePseudoLoc.Transform("Hello <b>World</b>", "both"));
            Assert.AreEqual("[Hello <color=#fff>World</color>~~~]", ProbePseudoLoc.Transform("Hello <color=#fff>World</color>", "expand"));
            Assert.AreEqual("[Ĥéļļö]", ProbePseudoLoc.Transform("Hello", "accent"));
            Assert.AreEqual("[123~]", ProbePseudoLoc.Transform("123", "both"), "số không có dấu nhưng vẫn đệm");
            Assert.AreEqual("  ", ProbePseudoLoc.Transform("  ", "both"), "chữ trống giữ nguyên");
            Assert.AreEqual("both", ProbePseudoLoc.NormalizeMode(null));
            Assert.AreEqual("accent", ProbePseudoLoc.NormalizeMode(" Accent "));
            Assert.IsNull(ProbePseudoLoc.NormalizeMode("xyz"));
        }

        [UnityTest]
        public IEnumerator PseudoLoc_EndpointAppliesAndRestores()
        {
            var canvas = NewCanvas("PseudoLocCanvas");
            var label = NewText(canvas.transform, "lbl", "Hello <b>World</b>");
            var blank = NewText(canvas.transform, "blank", " ");
            try
            {
                EzgQaProbe.TestConfig = TemplateConfig();
                Assert.IsTrue(EzgQaProbe.Activate(TOKEN, 7595, "test"));
                yield return WaitGate(true);

                yield return Http(EzgQaProbe.Port, "/pseudoloc?on=1&mode=both", TOKEN);
                Assert.AreEqual(200, _status, _body);
                StringAssert.Contains("\"on\":true", _body);
                StringAssert.Contains("\"count\":1", _body);
                Assert.AreEqual("[Ĥéļļö <b>Ŵöřļđ</b>~~~]", label.text);
                Assert.AreEqual(" ", blank.text, "chữ trống không đổi");
                Assert.IsTrue(ProbePseudoLoc.IsOn);

                yield return Http(EzgQaProbe.Port, "/pseudoloc?on=1", TOKEN);
                StringAssert.Contains("\"count\":0", _body, "gọi lại không đổi chồng");

                yield return Http(EzgQaProbe.Port, "/ui", TOKEN);
                Assert.AreEqual(200, _status, _body);
                StringAssert.Contains("\"probeVersion\":\"" + EzgQaProbe.VERSION + "\"", _body);
                StringAssert.Contains("\"pseudoloc\":true", _body);

                yield return Http(EzgQaProbe.Port, "/pseudoloc?on=0", TOKEN);
                Assert.AreEqual(200, _status, _body);
                StringAssert.Contains("\"on\":false", _body);
                Assert.AreEqual("Hello <b>World</b>", label.text);
                Assert.IsFalse(ProbePseudoLoc.IsOn);

                yield return Http(EzgQaProbe.Port, "/pseudoloc?on=1&mode=bogus", TOKEN);
                Assert.AreEqual(400, _status, _body);

                // Tool tắt probe khi chữ giả đang bật → trả lại chữ gốc.
                yield return Http(EzgQaProbe.Port, "/pseudoloc?on=1", TOKEN);
                Assert.AreNotEqual("Hello <b>World</b>", label.text);
                EzgQaProbe.Deactivate();
                Assert.AreEqual("Hello <b>World</b>", label.text);
            }
            finally
            {
                Object.DestroyImmediate(canvas.gameObject);
            }
        }

        // ───────────────────────────── /ui: flags port từ UiAuditSuite

        [UnityTest]
        public IEnumerator Ui_FlagsDeadButtonNoRaycastMissingSpriteOverlap()
        {
            var canvas = NewCanvas("FlagsCanvas");
            var group = new GameObject("group", typeof(RectTransform)).GetComponent<RectTransform>();
            group.SetParent(canvas.transform, false);
            group.sizeDelta = new Vector2(600, 600);
            // Nút không gắn hành động, có Image nhận raycast.
            var dead = NewButton(group, "btn_dead", new Vector2(-150, 200), new Color(0.2f, 0.4f, 0.9f, 1f), true);
            // Nút có listener nhưng không Graphic nào nhận raycast.
            var noRay = NewButton(group, "btn_noray", new Vector2(150, 200), new Color(0.2f, 0.4f, 0.9f, 1f), false);
            noRay.onClick.AddListener(() => { });
            // Hai nút cùng cha chồng nhau > 50%.
            var under = NewButton(group, "btn_under", new Vector2(0, -100), Color.gray, true);
            under.onClick.AddListener(() => { });
            var over = NewButton(group, "btn_over", new Vector2(40, -100), Color.gray, true);
            over.onClick.AddListener(() => { });
            // Image màu không sprite (không phải nút) + nền phẳng phủ cả màn.
            var colored = NewImage(group, "img_colored", new Vector2(0, 50), new Vector2(120, 60), new Color(0.9f, 0.3f, 0.3f, 1f));
            var backdrop = NewImage(canvas.transform, "img_backdrop", Vector2.zero, new Vector2(Screen.width * 2, Screen.height * 2), new Color(0.1f, 0.1f, 0.1f, 1f));
            backdrop.transform.SetAsFirstSibling();
            try
            {
                EzgQaProbe.TestConfig = TemplateConfig();
                Assert.IsTrue(EzgQaProbe.Activate(TOKEN, 7597, "test"));
                yield return WaitGate(true);
                yield return null; // layout một frame

                yield return Http(EzgQaProbe.Port, "/ui?all=1", TOKEN);
                Assert.AreEqual(200, _status, _body);
                StringAssert.Contains("\"name\":\"btn_dead\"", _body);
                Assert.IsTrue(ElementHasFlag("btn_dead", "deadButton"), _body);
                Assert.IsFalse(ElementHasFlag("btn_noray", "deadButton"), "có listener thì không phải nút chết");
                Assert.IsTrue(ElementHasFlag("btn_noray", "noRaycastTarget"), _body);
                Assert.IsFalse(ElementHasFlag("btn_dead", "noRaycastTarget"));
                Assert.IsTrue(ElementHasFlag("btn_under", "overlapButton"), _body);
                Assert.IsFalse(ElementHasFlag("btn_over", "overlapButton"), "chỉ nút bị đè mang flag");
                StringAssert.Contains("\"overlapButton\":\"bị 'btn_over'", _body);
                Assert.IsTrue(ElementHasFlag("img_colored", "missingSprite"), _body);
                Assert.IsTrue(ElementHasFlag("img_backdrop", "solidBackground"), _body);
                StringAssert.Contains("\"flagDetail\":{", _body);
            }
            finally
            {
                Object.DestroyImmediate(canvas.gameObject);
            }
        }

        private bool ElementHasFlag(string name, string flag)
        {
            // Body là JSON phẳng tự sinh: tìm object của phần tử theo "name" rồi xem mảng flags của nó.
            var marker = "\"name\":\"" + name + "\"";
            var at = _body.IndexOf(marker, StringComparison.Ordinal);
            if (at < 0) return false;
            var end = _body.IndexOf("\"name\":\"", at + marker.Length, StringComparison.Ordinal);
            var chunk = end < 0 ? _body.Substring(at) : _body.Substring(at, end - at);
            var flags = chunk.IndexOf("\"flags\":[", StringComparison.Ordinal);
            if (flags < 0) return false;
            var close = chunk.IndexOf(']', flags);
            return chunk.Substring(flags, close - flags).Contains("\"" + flag + "\"");
        }

        private static Canvas NewCanvas(string name)
        {
            var go = new GameObject(name, typeof(Canvas), typeof(UnityEngine.UI.GraphicRaycaster));
            var canvas = go.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            if (UnityEngine.EventSystems.EventSystem.current == null)
                new GameObject("EventSystem", typeof(UnityEngine.EventSystems.EventSystem)).transform.SetParent(go.transform);
            return canvas;
        }

        private static UnityEngine.UI.Text NewText(Transform parent, string name, string value)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(UnityEngine.UI.Text));
            go.transform.SetParent(parent, false);
            var rt = (RectTransform)go.transform;
            rt.sizeDelta = new Vector2(300, 60);
            var text = go.GetComponent<UnityEngine.UI.Text>();
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            text.text = value;
            return text;
        }

        private static UnityEngine.UI.Button NewButton(Transform parent, string name, Vector2 pos, Color color, bool raycast)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(UnityEngine.UI.Image), typeof(UnityEngine.UI.Button));
            go.transform.SetParent(parent, false);
            var rt = (RectTransform)go.transform;
            rt.anchoredPosition = pos;
            rt.sizeDelta = new Vector2(200, 80);
            var image = go.GetComponent<UnityEngine.UI.Image>();
            image.color = color;
            image.raycastTarget = raycast;
            image.sprite = Sprite.Create(Texture2D.whiteTexture, new Rect(0, 0, 4, 4), new Vector2(0.5f, 0.5f));
            var button = go.GetComponent<UnityEngine.UI.Button>();
            button.targetGraphic = image;
            return button;
        }

        private static UnityEngine.UI.Image NewImage(Transform parent, string name, Vector2 pos, Vector2 size, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(UnityEngine.UI.Image));
            go.transform.SetParent(parent, false);
            var rt = (RectTransform)go.transform;
            rt.anchoredPosition = pos;
            rt.sizeDelta = size;
            var image = go.GetComponent<UnityEngine.UI.Image>();
            image.color = color;
            image.raycastTarget = false;
            return image;
        }

        // ───────────────────────────── bật/tắt cheat của game

        [Test]
        public void GameCheat_UsesGameMethods()
        {
            EzgQaProbe.Config = TemplateConfig();
            Assert.IsTrue(ProbeGameCheat.Set(true, out var via, out var error), error);
            Assert.AreEqual(TEMPLATE + ".TryEnableCheat", via);
            Assert.IsTrue(TemplateGame.isCheat);
            Assert.IsTrue(ProbeGameCheat.Set(false, out via, out error), error);
            Assert.AreEqual(TEMPLATE + ".DisableCheat", via);
            Assert.IsFalse(TemplateGame.isCheat);

            bool? answer = null;
            TemplateGame.Grant = false;
            Assert.IsTrue(ProbeGameCheat.TryAsk(ok => answer = ok));
            Assert.AreEqual(false, answer);
        }

        [Test]
        public void GameCheat_FallsBackToFlagWithoutMethods()
        {
            EzgQaProbe.Config = new ProbeConfig { releaseGate = "Other.Game.GameSystems.isCheat" };
            Other.Game.GameSystems.isCheat = false;
            Assert.IsFalse(ProbeGameCheat.TryAsk(_ => { }));
            Assert.IsTrue(ProbeGameCheat.Set(true, out var via, out var error), error);
            Assert.AreEqual("Other.Game.GameSystems.isCheat=true", via);
            Assert.IsTrue(Other.Game.GameSystems.isCheat);
            Assert.IsTrue(ProbeGameCheat.Set(false, out _, out _));
            Assert.IsFalse(Other.Game.GameSystems.isCheat);
            EzgQaProbe.Config = new ProbeConfig();
            Assert.IsFalse(ProbeGameCheat.Set(true, out _, out error));
            StringAssert.Contains("releaseGate", error);
        }

        [Test]
        public void Reflect_EvalAndSetStatic()
        {
            Assert.IsTrue(ProbeReflect.TrySetStatic(TEMPLATE + ".isCheat", true, out var error), error);
            Assert.IsTrue(ProbeReflect.TryEval(TEMPLATE + ".isCheat", out var v, out error), error);
            Assert.AreEqual(true, v);
            Assert.IsFalse(ProbeReflect.TrySetStatic("No.Such.Type.flag", true, out error));
        }

        // ───────────────────────────── cổng release

        [Test]
        public void Gate_ReleaseFollowsCheatFlag()
        {
            EzgQaProbe.Config = TemplateConfig();
            ProbeGate.SimulateRelease = true;
            TemplateGame.isCheat = false;
            ProbeGate.Refresh();
            Assert.IsFalse(ProbeGate.IsOpen, ProbeGate.Reason);
            TemplateGame.isCheat = true;
            ProbeGate.Refresh();
            Assert.IsTrue(ProbeGate.IsOpen, ProbeGate.Reason);
            StringAssert.Contains("isCheat=true", ProbeGate.Reason);
            EzgQaProbe.Config = new ProbeConfig();
            ProbeGate.Refresh();
            Assert.IsFalse(ProbeGate.IsOpen, ProbeGate.Reason);
            ProbeGate.SimulateRelease = false;
            ProbeGate.Refresh();
            Assert.IsTrue(ProbeGate.IsOpen, "Editor luôn mở");
        }

        // ───────────────────────────── server: token + cổng (end-to-end qua socket)

        [UnityTest]
        public IEnumerator Server_RequiresTokenAndGate()
        {
            ProbeGate.SimulateRelease = true;
            EzgQaProbe.TestConfig = TemplateConfig();
            Assert.IsTrue(EzgQaProbe.Activate(TOKEN, 7591, "test"));
            yield return WaitGate(false);

            yield return Http(EzgQaProbe.Port, "/ping", null);
            Assert.AreEqual(401, _status, _body);
            yield return Http(EzgQaProbe.Port, "/ping", "wrong-token-wrong-token");
            Assert.AreEqual(401, _status, _body);

            yield return Http(EzgQaProbe.Port, "/ping", TOKEN);
            Assert.AreEqual(200, _status, _body);
            StringAssert.Contains("\"gate\":\"closed\"", _body);
            StringAssert.Contains("\"cheatGate\":", _body);
            StringAssert.Contains("\"releaseGate\":\"" + TEMPLATE + ".isCheat\"", _body);

            foreach (var path in new[] { "/ui", "/get?path=x", "/call?method=x", "/cheat?on=1", "/tap?x=1&y=1" })
            {
                yield return Http(EzgQaProbe.Port, path, TOKEN);
                Assert.AreEqual(423, _status, path + " " + _body);
            }

            Assert.IsFalse(TemplateGame.isCheat, "/cheat không được chạy khi cổng đóng");
            yield return Http(EzgQaProbe.Port, "/unlock", TOKEN);
            Assert.AreEqual(200, _status, _body);
            yield return Http(EzgQaProbe.Port, "/logs?since=0", TOKEN);
            Assert.AreEqual(200, _status, _body);

            TemplateGame.isCheat = true;
            yield return WaitGate(true);
            yield return Http(EzgQaProbe.Port, "/ui", TOKEN);
            Assert.AreEqual(200, _status, _body);
            yield return Http(EzgQaProbe.Port, "/cheat?on=0", TOKEN);
            Assert.AreEqual(200, _status, _body);
            Assert.IsFalse(TemplateGame.isCheat);
            yield return WaitGate(false);
        }

        [UnityTest]
        public IEnumerator Server_FocusSwitchesToNewTokenAndStopShutsDown()
        {
            const string next = "QaProbeNextToken5678";
            var trigger = Path.Combine(Application.persistentDataPath, "ezg_qa_probe.txt");
            var hadTrigger = File.Exists(trigger);
            var oldTrigger = hadTrigger ? File.ReadAllText(trigger) : null;
            EzgQaProbe.TestConfig = TemplateConfig();
            Assert.IsTrue(EzgQaProbe.Activate(TOKEN, 7591, "test"));
            try
            {
                // Tool `start --attach` ghi trigger mới rồi đưa app ra nền/vào lại → probe đổi sang token mới.
                File.WriteAllText(trigger, next + ":7591");
                typeof(EzgQaProbe).GetMethod("OnFocusChanged", BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, new object[] { true });
                Assert.IsTrue(EzgQaProbe.IsListening);
                yield return Http(EzgQaProbe.Port, "/ping", TOKEN);
                Assert.AreEqual(401, _status, "token phiên cũ phải hết hiệu lực");
                yield return Http(EzgQaProbe.Port, "/ping", next);
                Assert.AreEqual(200, _status, _body);

                // Focus lại với cùng token: không khởi động lại server.
                var port = EzgQaProbe.Port;
                typeof(EzgQaProbe).GetMethod("OnFocusChanged", BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, new object[] { true });
                Assert.AreEqual(port, EzgQaProbe.Port);

                // /stop: trả lời rồi tắt ở frame sau.
                yield return Http(EzgQaProbe.Port, "/stop", next);
                Assert.AreEqual(200, _status, _body);
                var t0 = Time.realtimeSinceStartup;
                while (EzgQaProbe.IsListening && Time.realtimeSinceStartup - t0 < 2f) yield return null;
                Assert.IsFalse(EzgQaProbe.IsListening);

                // Token của phiên đã dừng không bao giờ được dùng lại (intent / file cũ vẫn còn mang nó).
                typeof(EzgQaProbe).GetMethod("OnFocusChanged", BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, new object[] { true });
                Assert.IsFalse(EzgQaProbe.IsListening, "không kích hoạt lại bằng token đã retire");
            }
            finally
            {
                if (hadTrigger) File.WriteAllText(trigger, oldTrigger);
                else File.Delete(trigger);
            }
        }

#if EZG_QA_CHEAT_GATE
        [UnityTest]
        public IEnumerator CheatGate_UnlockedPhoneOpensGateAndTurnsCheatOn()
        {
            CheatGate.UnlockInEditor = true; // như máy có grant còn hạn
            ProbeGate.SimulateRelease = true;
            EzgQaProbe.TestConfig = TemplateConfig();
            Assert.IsTrue(EzgQaProbe.Activate(TOKEN, 7591, "test"));
            yield return WaitGate(true);
            StringAssert.Contains("cheat-gate", ProbeGate.Reason);
            var t0 = Time.realtimeSinceStartup;
            while (!TemplateGame.isCheat && Time.realtimeSinceStartup - t0 < 3f) yield return null;
            Assert.IsTrue(TemplateGame.isCheat, "probe phải tự bật cheat của game");
            Assert.AreEqual("unlocked", ProbeCheat.State);

            // Tắt cheat game: cổng theo CheatGate vẫn mở.
            Assert.IsTrue(ProbeGameCheat.Set(false, out _, out _));
            yield return new WaitForSecondsRealtime(0.7f);
            Assert.IsTrue(ProbeGate.IsOpen, ProbeGate.Reason);
            Assert.AreEqual(1, TemplateGame.TryCalls, "chỉ bật cheat một lần mỗi phiên");
        }
#endif

        // ───────────────────────────── tiện ích

        private int _status;
        private string _body;

        private static IEnumerator WaitGate(bool open)
        {
            var t0 = Time.realtimeSinceStartup;
            while (ProbeGate.IsOpen != open && Time.realtimeSinceStartup - t0 < 3f) yield return null;
            Assert.AreEqual(open, ProbeGate.IsOpen, ProbeGate.Reason);
        }

        // ───────────────────────────── chụp màn: ẩn UI Bug Logger

        [UnityTest]
        public IEnumerator Shot_HidesBugLoggerAroundCaptureAndCaptureHiddenRoute()
        {
            var savedType = ProbeHandlers.BugLoggerUiType;
            ProbeHandlers.BugLoggerUiType = typeof(BugLogger.FakeBugLoggerUI).FullName;
            BugLogger.FakeBugLoggerUI.Calls.Clear();
            try
            {
                EzgQaProbe.TestConfig = TemplateConfig();
                Assert.IsTrue(EzgQaProbe.Activate(TOKEN, 7593, "test"));
                yield return WaitGate(true);

                yield return Http(EzgQaProbe.Port, "/shot?fmt=jpg&max=64", TOKEN);
                Assert.AreEqual(200, _status, _body);
                CollectionAssert.AreEqual(new[] { 2f, 0f }, BugLogger.FakeBugLoggerUI.Calls, "ẩn trước khi chụp, hiện lại sau");

                BugLogger.FakeBugLoggerUI.Calls.Clear();
                yield return Http(EzgQaProbe.Port, "/capturehidden?sec=99", TOKEN);
                Assert.AreEqual(200, _status, _body);
                StringAssert.Contains("\"supported\":true", _body);
                yield return Http(EzgQaProbe.Port, "/capturehidden?sec=0", TOKEN);
                Assert.AreEqual(200, _status, _body);
                CollectionAssert.AreEqual(new[] { 10f, 0f }, BugLogger.FakeBugLoggerUI.Calls, "giới hạn 10 s, 0 = hiện lại");

                ProbeHandlers.BugLoggerUiType = "Ezg.QaProbe.Tests.Missing.BugLoggerUI";
                yield return Http(EzgQaProbe.Port, "/capturehidden?sec=2", TOKEN);
                Assert.AreEqual(200, _status, _body);
                StringAssert.Contains("\"supported\":false", _body);
                yield return Http(EzgQaProbe.Port, "/shot?fmt=jpg&max=64", TOKEN);
                Assert.AreEqual(200, _status, "game không có Bug Logger vẫn chụp được");
            }
            finally
            {
                ProbeHandlers.BugLoggerUiType = savedType;
            }
        }

        private IEnumerator Http(int port, string path, string token)
        {
            var task = Task.Run(() => Send(port, path, token));
            var t0 = Time.realtimeSinceStartup;
            while (!task.IsCompleted && Time.realtimeSinceStartup - t0 < 10f) yield return null;
            Assert.IsTrue(task.IsCompleted, "probe không trả lời " + path);
            var raw = task.Result;
            var firstSpace = raw.IndexOf(' ');
            _status = firstSpace > 0 ? int.Parse(raw.Substring(firstSpace + 1, 3)) : 0;
            var sep = raw.IndexOf("\r\n\r\n", StringComparison.Ordinal);
            _body = sep >= 0 ? raw.Substring(sep + 4) : raw;
        }

        private static string Send(int port, string path, string token)
        {
            using (var client = new TcpClient("127.0.0.1", port))
            using (var stream = client.GetStream())
            {
                var req = "GET " + path + " HTTP/1.1\r\nHost: 127.0.0.1\r\n" + (token != null ? "X-QA-Token: " + token + "\r\n" : "") +
                          "Connection: close\r\n\r\n";
                var bytes = Encoding.ASCII.GetBytes(req);
                stream.Write(bytes, 0, bytes.Length);
                using (var reader = new StreamReader(stream, Encoding.UTF8)) return reader.ReadToEnd();
            }
        }
    }
}
