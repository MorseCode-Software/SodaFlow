using System.Collections.Generic;
using NUnit.Framework;
using SodaFlow.Functional;

namespace SodaFlow.Tests;

[TestFixture]
public class ReadOnlyDictionaryExtensionMethodsTests
{
    [Test]
    public void TestTryGetValuePresent()
    {
        IReadOnlyDictionary<string, int> d = new Dictionary<string, int> { { "a", 1 } };

        Assert.AreEqual(expected: Maybe.Some(1), actual: d.TryGetValue("a"));
    }

    [Test]
    public void TestTryGetValueMissing()
    {
        IReadOnlyDictionary<string, int> d = new Dictionary<string, int> { { "a", 1 } };

        Assert.AreEqual(expected: Maybe<int>.None, actual: d.TryGetValue("b"));
    }

    [Test]
    public void TestTryGetValueDistinguishesStoredDefault()
    {
        IReadOnlyDictionary<string, int> d = new Dictionary<string, int> { { "a", 0 } };

        Assert.AreEqual(expected: Maybe.Some(0), actual: d.TryGetValue("a"));
        Assert.AreEqual(expected: Maybe<int>.None, actual: d.TryGetValue("b"));
    }

    [Test]
    public void TestTryGetValueStoredNull()
    {
        IReadOnlyDictionary<string, string?> d = new Dictionary<string, string?> { { "a", null } };

        Assert.AreEqual(expected: Maybe.Some<string?>(null), actual: d.TryGetValue("a"));
        Assert.AreEqual(expected: Maybe<string?>.None, actual: d.TryGetValue("b"));
    }

    [Test]
    public void TestTryGetValueNullDictionary()
    {
        IReadOnlyDictionary<string, int>? d = null;

        Assert.AreEqual(expected: Maybe<int>.None, actual: d.TryGetValue("a"));
    }
}
