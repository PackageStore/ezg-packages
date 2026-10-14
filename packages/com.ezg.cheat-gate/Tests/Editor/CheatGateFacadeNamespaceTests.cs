// Compile-time guard: game code lives in Ezg.* namespaces (Ezg.Feature.*, Ezg.Game.*) and imports this package with a
// file-level using. If the package's own namespace ever ends in a segment spelled like the class (it was Ezg.CheatGate in
// 0.1.0), `CheatGate.X` binds to that namespace from inside any Ezg.* namespace and this file stops compiling.
using System;
using Ezg.Package.CheatGate;
using NUnit.Framework;

namespace Ezg.Feature.Shared.Systems.CheatGateUsageCheck
{
    public class CheatGateFacadeNamespaceTests
    {
        [Test]
        public void TheFacadeResolvesFromAGameNamespace()
        {
            Action<bool> onChanged = _ => { };
            CheatGate.Changed += onChanged;
            CheatGate.Changed -= onChanged;
            var unlocked = CheatGate.IsUnlocked;
            var display = CheatGate.DeviceIdDisplay;
            Action<CheatGateResult> callback = _ => { };
            Assert.IsNotNull(callback);
            Assert.IsNotNull(display);
            Assert.IsTrue(unlocked || !unlocked);
        }
    }
}
