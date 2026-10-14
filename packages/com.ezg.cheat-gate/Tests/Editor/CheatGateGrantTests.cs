using System.Linq;
using System.Security.Cryptography;
using NUnit.Framework;
using V = Ezg.Package.CheatGate.Tests.CheatGateTestVectors;

namespace Ezg.Package.CheatGate.Tests
{
    public class CheatGateGrantTests
    {
        private static readonly CheatGateKeys.Key[] Keys = { V.TestKey };

        [Test]
        public void ValidGrantFromTheWorkerVerifies()
        {
            Assert.IsTrue(CheatGateGrant.TryVerify(V.Valid, Keys, V.Project, V.DeviceId, out var claims));
            Assert.AreEqual(1, claims.Version);
            Assert.AreEqual("t1", claims.KeyId);
            Assert.AreEqual(V.Project, claims.Project);
            Assert.AreEqual(V.DeviceId, claims.DeviceId);
            Assert.AreEqual(V.IssuedAt, claims.IssuedAt);
            Assert.AreEqual(V.ExpiresAt, claims.ExpiresAt);
        }

        [Test]
        public void TheNonceIsRead()
        {
            Assert.IsTrue(CheatGateGrant.TryVerify(V.Fresh, Keys, V.Project, V.DeviceId, out var fresh));
            Assert.AreEqual(V.Nonce, fresh.Nonce);
            Assert.IsTrue(CheatGateGrant.TryVerify(V.Valid, Keys, V.Project, V.DeviceId, out var stored));
            Assert.IsNull(stored.Nonce);
        }

        [Test]
        public void EveryTrustedKeyIsTried()
        {
            Assert.IsTrue(CheatGateGrant.TryVerify(V.Valid, new[] { V.OtherKey, V.TestKey }, V.Project, V.DeviceId, out _));
            Assert.IsFalse(CheatGateGrant.TryVerify(V.Valid, new[] { V.OtherKey }, V.Project, V.DeviceId, out _));
        }

        [Test]
        public void SpacingKeyOrderAndEscapesDoNotMatter()
        {
            Assert.IsTrue(CheatGateGrant.TryVerify(V.Spaced, Keys, V.Project, V.DeviceId, out var claims));
            Assert.AreEqual("t1", claims.KeyId);
            Assert.AreEqual(V.ExpiresAt, claims.ExpiresAt);
        }

        [TestCase(nameof(V.OtherProject))]
        [TestCase(nameof(V.OtherDevice))]
        [TestCase(nameof(V.ByOtherKey))]
        [TestCase(nameof(V.Swapped))]
        [TestCase(nameof(V.BadSignature))]
        [TestCase(nameof(V.Version2))]
        [TestCase(nameof(V.ExpBeforeIat))]
        [TestCase(nameof(V.NotJson))]
        [TestCase(nameof(V.Nested))]
        [TestCase(nameof(V.MissingExp))]
        [TestCase(nameof(V.LowerCaseProject))]
        public void RefusedVectors(string name)
        {
            var grant = (string)typeof(CheatGateTestVectors).GetField(name).GetValue(null);
            Assert.IsFalse(CheatGateGrant.TryVerify(grant, Keys, V.Project, V.DeviceId, out var claims), name);
            Assert.IsNull(claims);
        }

        [TestCase(null)]
        [TestCase("")]
        [TestCase(".")]
        [TestCase("abc.")]
        [TestCase(".abc")]
        [TestCase("a.b.c")]
        [TestCase("ab cd.efgh")]
        [TestCase("ab+c.efgh")]
        [TestCase("abcd.ef=gh")]
        public void MalformedGrantsAreRefusedWithoutThrowing(string grant)
        {
            Assert.IsFalse(CheatGateGrant.TryVerify(grant, Keys, V.Project, V.DeviceId, out _));
        }

        [Test]
        public void OversizedGrantIsRefused()
        {
            Assert.IsFalse(CheatGateGrant.TryVerify(V.Valid + new string('A', 5000), Keys, V.Project, V.DeviceId, out _));
        }

        [Test]
        public void WhitespaceAroundAGrantIsNotAccepted()
        {
            Assert.IsFalse(CheatGateGrant.TryVerify(" " + V.Valid, Keys, V.Project, V.DeviceId, out _));
            Assert.IsFalse(CheatGateGrant.TryVerify(V.Valid + "\n", Keys, V.Project, V.DeviceId, out _));
        }

        [Test]
        public void MissingInputsAreRefused()
        {
            Assert.IsFalse(CheatGateGrant.TryVerify(V.Valid, null, V.Project, V.DeviceId, out _));
            Assert.IsFalse(CheatGateGrant.TryVerify(V.Valid, new CheatGateKeys.Key[0], V.Project, V.DeviceId, out _));
            Assert.IsFalse(CheatGateGrant.TryVerify(V.Valid, Keys, "", V.DeviceId, out _));
            Assert.IsFalse(CheatGateGrant.TryVerify(V.Valid, Keys, V.Project, "", out _));
            Assert.IsFalse(CheatGateGrant.TryVerify(V.Valid, Keys, "sm007", V.DeviceId, out _), "project must already be normalized");
        }

        [Test]
        public void UnusableKeysAreSkipped()
        {
            var broken = new CheatGateKeys.Key("bad", "not base64!!", "AQAB");
            var empty = new CheatGateKeys.Key("empty", "", "");
            Assert.IsFalse(broken.IsUsable);
            Assert.IsFalse(empty.IsUsable);
            Assert.IsTrue(CheatGateGrant.TryVerify(V.Valid, new[] { broken, null, empty, V.TestKey }, V.Project, V.DeviceId, out _));
        }

        [Test]
        public void IsCurrentBoundaries()
        {
            CheatGateGrant.TryVerify(V.Valid, Keys, V.Project, V.DeviceId, out var claims);
            Assert.IsTrue(CheatGateGrant.IsCurrent(claims, V.IssuedAt));
            Assert.IsTrue(CheatGateGrant.IsCurrent(claims, V.ExpiresAt - 1));
            Assert.IsFalse(CheatGateGrant.IsCurrent(claims, V.ExpiresAt));
            Assert.IsTrue(CheatGateGrant.IsCurrent(claims, V.IssuedAt - CheatGateGrant.ClockSkewSeconds));
            Assert.IsFalse(CheatGateGrant.IsCurrent(claims, V.IssuedAt - CheatGateGrant.ClockSkewSeconds - 1));
            Assert.IsFalse(CheatGateGrant.IsCurrent(null, V.IssuedAt));
        }

        [Test]
        public void Base64Url()
        {
            CollectionAssert.AreEqual(new byte[] { 0xfb, 0xff }, CheatGateGrant.FromBase64Url("-_8"));
            CollectionAssert.AreEqual(new byte[] { 1, 2, 3 }, CheatGateGrant.FromBase64Url("AQID"));
            Assert.IsNull(CheatGateGrant.FromBase64Url("A"));
            Assert.IsNull(CheatGateGrant.FromBase64Url("AQ=="));
            Assert.IsNull(CheatGateGrant.FromBase64Url("A+/B"));
            Assert.IsNull(CheatGateGrant.FromBase64Url(""));
            Assert.IsNull(CheatGateGrant.FromBase64Url(null));
        }
    }

    public class CheatGateKeysTests
    {
        [Test]
        public void ShipsAtLeastOneUsableRsa2048Key()
        {
            Assert.IsNotEmpty(CheatGateKeys.Trusted);
            foreach (var key in CheatGateKeys.Trusted)
            {
                Assert.IsTrue(key.IsUsable, key.Id);
                Assert.AreEqual(256, key.Modulus.Length, key.Id);
                using (var rsa = RSA.Create())
                {
                    Assert.DoesNotThrow(() => rsa.ImportParameters(new RSAParameters { Modulus = key.Modulus, Exponent = key.Exponent }), key.Id);
                }
            }
        }

        [Test]
        public void KeyIdsAreUnique()
        {
            var ids = CheatGateKeys.Trusted.Select(k => k.Id).ToArray();
            Assert.AreEqual(ids.Length, ids.Distinct().Count());
            Assert.IsTrue(ids.All(id => !string.IsNullOrEmpty(id)));
        }

        [Test]
        public void TheTestKeyIsNotTrustedInProduction()
        {
            CollectionAssert.AreNotEqual(V.TestKey.Modulus, CheatGateKeys.Trusted[0].Modulus);
            Assert.IsFalse(CheatGateGrant.TryVerify(V.Valid, CheatGateKeys.Trusted, V.Project, V.DeviceId, out _));
        }
    }
}
