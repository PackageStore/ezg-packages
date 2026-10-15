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
