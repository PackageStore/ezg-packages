using NUnit.Framework;
using V = Ezg.Package.CheatGate.Tests.CheatGateTestVectors;

namespace Ezg.Package.CheatGate.Tests
{
    public class CheatGateDeviceIdTests
    {
        [Test]
        public void MatchesNodeCryptoVectors()
        {
            for (var i = 0; i < V.DeviceIds.GetLength(0); i++)
            {
                Assert.AreEqual(V.DeviceIds[i, 1], CheatGateDeviceId.FromIdentifier(V.DeviceIds[i, 0]), V.DeviceIds[i, 0]);
            }
        }

        [TestCase(null)]
        [TestCase("")]
        [TestCase("   ")]
        [TestCase("n/a")]
        public void NoIdentifierGivesNoId(string raw)
        {
            Assert.AreEqual("", CheatGateDeviceId.FromIdentifier(raw));
        }

        [Test]
        public void IdIsTwelveUpperHexDigits()
        {
            var id = CheatGateDeviceId.FromIdentifier("whatever");
            Assert.AreEqual(CheatGateDeviceId.Length, id.Length);
            StringAssert.IsMatch("^[0-9A-F]{12}$", id);
        }

        [TestCase("7f3a-9c21-b04e", "7F3A9C21B04E")]
        [TestCase(" 7F3A 9C21 B04E ", "7F3A9C21B04E")]
        [TestCase("7F3A9C21B04E", "7F3A9C21B04E")]
        [TestCase("7F3A9C21B04", "")]
        [TestCase("7F3A9C21B04E0", "")]
        [TestCase("7F3A9C21B04G", "")]
        [TestCase("7F3A_9C21_B04E", "")]
        [TestCase("", "")]
        [TestCase(null, "")]
        public void Normalize(string value, string expected)
        {
            Assert.AreEqual(expected, CheatGateDeviceId.Normalize(value));
        }

        [TestCase("7F3A9C21B04E", "7F3A-9C21-B04E")]
        [TestCase("7f3a9c21b04e", "7F3A-9C21-B04E")]
        [TestCase("", "N/A")]
        [TestCase(null, "N/A")]
        [TestCase("123", "N/A")]
        public void Display(string id, string expected)
        {
            Assert.AreEqual(expected, CheatGateDeviceId.Display(id));
        }
    }
}
