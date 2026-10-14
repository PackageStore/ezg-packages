using NUnit.Framework;

namespace Ezg.CheatGate.Tests
{
    public class CheatGateJsonTests
    {
        [Test]
        public void ReadsTheCheckAnswers()
        {
            var granted = CheatGateJson.ParseFlatObject("{\"granted\":true,\"grant\":\"a.b\",\"expiresAt\":1792551631}");
            Assert.IsTrue(CheatGateJson.TryGetBool(granted, "granted", out var flag) && flag);
            Assert.IsTrue(CheatGateJson.TryGetString(granted, "grant", out var grant) && grant == "a.b");
            Assert.IsTrue(CheatGateJson.TryGetLong(granted, "expiresAt", out var exp) && exp == 1792551631);

            var refused = CheatGateJson.ParseFlatObject("{\"granted\":false}");
            Assert.IsTrue(CheatGateJson.TryGetBool(refused, "granted", out var no) && !no);
        }

        [Test]
        public void ValueTypesAreChecked()
        {
            var obj = CheatGateJson.ParseFlatObject("{\"s\":\"1\",\"n\":1,\"b\":true,\"z\":null}");
            Assert.IsNotNull(obj);
            Assert.IsFalse(CheatGateJson.TryGetLong(obj, "s", out _));
            Assert.IsFalse(CheatGateJson.TryGetString(obj, "n", out _));
            Assert.IsFalse(CheatGateJson.TryGetBool(obj, "n", out _));
            Assert.IsFalse(CheatGateJson.TryGetString(obj, "z", out _));
            Assert.IsFalse(CheatGateJson.TryGetString(obj, "missing", out _));
            Assert.IsFalse(CheatGateJson.TryGetString(null, "s", out _));
        }

        [Test]
        public void Escapes()
        {
            var obj = CheatGateJson.ParseFlatObject("{\"a\":\"q\\\"b\\\\s\\/n\\nt\\tu\\u00e9\"}");
            Assert.IsTrue(CheatGateJson.TryGetString(obj, "a", out var value));
            Assert.AreEqual("q\"b\\s/n\nt\tu\u00e9", value);
        }

        [Test]
        public void WhitespaceAndEmptyObject()
        {
            Assert.IsNotNull(CheatGateJson.ParseFlatObject(" \n{ } \r\n"));
            var obj = CheatGateJson.ParseFlatObject("{ \"a\" : -12 ,\t\"b\" : 0 }");
            Assert.IsTrue(CheatGateJson.TryGetLong(obj, "a", out var a) && a == -12);
            Assert.IsTrue(CheatGateJson.TryGetLong(obj, "b", out var b) && b == 0);
        }

        [TestCase(null)]
        [TestCase("")]
        [TestCase("[]")]
        [TestCase("\"str\"")]
        [TestCase("{")]
        [TestCase("{\"a\":1")]
        [TestCase("{\"a\":1,}")]
        [TestCase("{\"a\" 1}")]
        [TestCase("{a:1}")]
        [TestCase("{\"a\":{\"b\":1}}")]
        [TestCase("{\"a\":[1]}")]
        [TestCase("{\"a\":1.5}")]
        [TestCase("{\"a\":1e3}")]
        [TestCase("{\"a\":01}")]
        [TestCase("{\"a\":-}")]
        [TestCase("{\"a\":1234567890123456789}")]
        [TestCase("{\"a\":tru}")]
        [TestCase("{\"a\":trueX}")]
        [TestCase("{\"a\":\"unterminated}")]
        [TestCase("{\"a\":\"bad \\x escape\"}")]
        [TestCase("{\"a\":\"\\u12\"}")]
        [TestCase("{\"a\":\"raw\nnewline\"}")]
        [TestCase("{\"a\":1} trailing")]
        [TestCase("{\"a\":1}{\"b\":2}")]
        public void RefusesAnythingElse(string json)
        {
            Assert.IsNull(CheatGateJson.ParseFlatObject(json));
        }

        [Test]
        public void RefusesHugeInput()
        {
            Assert.IsNull(CheatGateJson.ParseFlatObject("{\"a\":\"" + new string('x', 20000) + "\"}"));
        }
    }
}
