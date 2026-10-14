using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Ezg.CheatGate.Tests
{
    /// <summary>The UnityEngine side: device id from SystemInfo, the Editor override, use before Initialize.</summary>
    public class CheatGateFacadeTests
    {
        private bool _unlockInEditor;

        [SetUp]
        public void SetUp()
        {
            _unlockInEditor = CheatGate.UnlockInEditor;
        }

        [TearDown]
        public void TearDown()
        {
            CheatGate.UnlockInEditor = _unlockInEditor;
        }

        [Test]
        public void DeviceIdComesFromSystemInfo()
        {
            Assert.AreEqual(CheatGateDeviceId.FromIdentifier(SystemInfo.deviceUniqueIdentifier), CheatGate.DeviceId);
            StringAssert.IsMatch("^([0-9A-F]{4}-[0-9A-F]{4}-[0-9A-F]{4}|N/A)$", CheatGate.DeviceIdDisplay);
        }

        [Test]
        public void TheEditorIsUnlockedByDefault()
        {
            CheatGate.UnlockInEditor = true;
            Assert.IsTrue(CheatGate.IsUnlocked);
            CheatGateResult? result = null;
            CheatGate.TryUnlock(r => result = r);
            Assert.AreEqual(CheatGateResult.Unlocked, result);
        }

        [Test]
        public void WithoutTheOverrideNothingIsUnlockedBeforeInitialize()
        {
            if (CheatGate.IsInitialized) Assert.Ignore("CheatGate was initialized by the project");
            CheatGate.UnlockInEditor = false;
            Assert.IsFalse(CheatGate.IsUnlocked);
            LogAssert.Expect(LogType.Error, new Regex("TryUnlock before CheatGate.Initialize"));
            CheatGateResult? result = null;
            CheatGate.TryUnlock(r => result = r);
            Assert.AreEqual(CheatGateResult.NotConfigured, result);
        }

        [Test]
        public void ASecondProjectIsRefused()
        {
            ResetStatics();
            try
            {
                CheatGate.Initialize("ZZTEST1");
                Assert.AreEqual("ZZTEST1", CheatGate.Project);
                LogAssert.Expect(LogType.Error, new Regex("already initialized with ZZTEST1"));
                CheatGate.Initialize("ZZTEST2");
                Assert.AreEqual("ZZTEST1", CheatGate.Project);
                CheatGate.Initialize("zztest1"); // same project: silently nothing
                Assert.AreEqual("ZZTEST1", CheatGate.Project);
            }
            finally
            {
                ResetStatics();
            }
        }

        [Test]
        public void AnInvalidCodeCanBeReplaced()
        {
            ResetStatics();
            try
            {
                LogAssert.Expect(LogType.Error, new Regex("invalid project code"));
                CheatGate.Initialize("not valid!");
                Assert.AreEqual("", CheatGate.Project);
                CheatGate.Initialize("ZZTEST3");
                Assert.AreEqual("ZZTEST3", CheatGate.Project);
            }
            finally
            {
                ResetStatics();
            }
        }

        private static void ResetStatics()
        {
            typeof(CheatGate).GetMethod("ResetStatics", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static).Invoke(null, null);
        }

        [Test]
        public void DefaultEndpointIsTheCheatGateWorker()
        {
            Assert.AreEqual("https://ezg-cheat-gate.developer-a1f.workers.dev", CheatGate.DefaultEndpoint);
        }
    }
}
