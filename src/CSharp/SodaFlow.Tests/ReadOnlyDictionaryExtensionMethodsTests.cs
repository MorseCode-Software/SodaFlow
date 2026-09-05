using System.Collections.Generic;
using System.Threading.Tasks;
using SodaFlow.Functional;
using TUnit.Assertions;
using TUnit.Assertions.Enums;
using TUnit.Assertions.Extensions;
using TUnit.Core;

namespace SodaFlow.Tests;

public class ReadOnlyDictionaryExtensionMethodsTests
{
    [Test]
    public async Task TestTryGetValuePresent()
    {
        IReadOnlyDictionary<string, int> d = new Dictionary<string, int> { { "a", 1 } };

        await Assert.That(d.TryGetValue("a")).IsEqualTo(Maybe.Some(1));
    }

    [Test]
    public async Task TestTryGetValueMissing()
    {
        IReadOnlyDictionary<string, int> d = new Dictionary<string, int> { { "a", 1 } };

        await Assert.That(d.TryGetValue("b")).IsEqualTo(Maybe<int>.None);
    }

    [Test]
    public async Task TestTryGetValueDistinguishesStoredDefault()
    {
        IReadOnlyDictionary<string, int> d = new Dictionary<string, int> { { "a", 0 } };

        await Assert.That(d.TryGetValue("a")).IsEqualTo(Maybe.Some(0));
        await Assert.That(d.TryGetValue("b")).IsEqualTo(Maybe<int>.None);
    }

    [Test]
    public async Task TestTryGetValueStoredNull()
    {
        IReadOnlyDictionary<string, string?> d = new Dictionary<string, string?> { { "a", null } };

        await Assert.That(d.TryGetValue("a")).IsEqualTo(Maybe.Some<string?>(null));
        await Assert.That(d.TryGetValue("b")).IsEqualTo(Maybe<string?>.None);
    }

    [Test]
    public async Task TestTryGetValueNullDictionary()
    {
        IReadOnlyDictionary<string, int>? d = null;

        await Assert.That(d.TryGetValue("a")).IsEqualTo(Maybe<int>.None);
    }
}
