using System.Collections.Generic;
using NUnit.Framework;
using SodaFlow.Functional;

namespace SodaFlow.Tests
{
    [TestFixture]
    public class ReadOnlyDictionaryExtensionMethodsTests
    {
        [Test]
        public void TestTryGetValuePresent()
        {
            IReadOnlyDictionary<string, int> d = new Dictionary<string, int> { { "a", 1 } };

            Assert.AreEqual(Maybe.Some(1), d.TryGetValue("a"));
        }

        [Test]
        public void TestTryGetValueMissing()
        {
            IReadOnlyDictionary<string, int> d = new Dictionary<string, int> { { "a", 1 } };

            Assert.AreEqual(Maybe<int>.None, d.TryGetValue("b"));
        }

        [Test]
        public void TestTryGetValueDistinguishesStoredDefault()
        {
            IReadOnlyDictionary<string, int> d = new Dictionary<string, int> { { "a", 0 } };

            Assert.AreEqual(Maybe.Some(0), d.TryGetValue("a"));
            Assert.AreEqual(Maybe<int>.None, d.TryGetValue("b"));
        }

        [Test]
        public void TestTryGetValueStoredNull()
        {
            IReadOnlyDictionary<string, string> d = new Dictionary<string, string> { { "a", null } };

            Assert.AreEqual(Maybe.Some((string)null), d.TryGetValue("a"));
            Assert.AreEqual(Maybe<string>.None, d.TryGetValue("b"));
        }

        [Test]
        public void TestTryGetValueNullDictionary()
        {
            IReadOnlyDictionary<string, int> d = null;

            Assert.AreEqual(Maybe<int>.None, d.TryGetValue("a"));
        }
    }
}
