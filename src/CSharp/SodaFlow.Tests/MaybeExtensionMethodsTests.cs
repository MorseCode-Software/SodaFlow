using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using SodaFlow.Functional;
using TUnit.Assertions;
using TUnit.Assertions.Enums;
using TUnit.Assertions.Extensions;
using TUnit.Core;

namespace SodaFlow.Tests;

public class MaybeExtensionMethodsTests
{
    [Test]
    public async Task TestFlatten1()
    {
        Maybe<Maybe<int>> m = Maybe.None;

        Maybe<int> result = m.Flatten();

        await Assert.That(result).IsEqualTo(Maybe<int>.None);
    }

    [Test]
    public async Task TestFlatten2()
    {
        Maybe<Maybe<int>> m = Maybe.Some(Maybe<int>.None);

        Maybe<int> result = m.Flatten();

        await Assert.That(result).IsEqualTo(Maybe<int>.None);
    }

    [Test]
    public async Task TestFlatten3()
    {
        Maybe<Maybe<int>> m = Maybe.Some(Maybe.Some(5));

        Maybe<int> result = m.Flatten();

        await Assert.That(result).IsEqualTo(Maybe.Some(5));
    }

    [Test]
    public async Task TestWhereSomeNone()
    {
        Maybe<int>[] m = [Maybe<int>.None, Maybe<int>.None, Maybe<int>.None, Maybe<int>.None, Maybe<int>.None];

        IEnumerable<int> result = m.WhereSome();

        await Assert.That(result).IsEquivalentTo(Array.Empty<int>(), CollectionOrdering.Matching);
    }

    [Test]
    public async Task TestWhereSomeSome()
    {
        Maybe<int>[] m = [Maybe<int>.None, Maybe.Some(2), Maybe.Some(5), Maybe<int>.None, Maybe.Some(7)];

        IEnumerable<int> result = m.WhereSome();

        await Assert.That(result).IsEquivalentTo(new[] { 2, 5, 7 }, CollectionOrdering.Matching);
    }

    [Test]
    public async Task TestWhereSomeAll()
    {
        Maybe<int>[] m = [Maybe.Some(3), Maybe.Some(2), Maybe.Some(5), Maybe.Some(4), Maybe.Some(7)];

        IEnumerable<int> result = m.WhereSome();

        await Assert.That(result).IsEquivalentTo(new[] { 3, 2, 5, 4, 7 }, CollectionOrdering.Matching);
    }

    [Test]
    public async Task TestAllSomeOrNoneNone()
    {
        Maybe<int>[] m = [Maybe<int>.None, Maybe<int>.None, Maybe<int>.None, Maybe<int>.None, Maybe<int>.None];

        Maybe<IEnumerable<int>> result = m.AllSomeOrNone();

        await Assert.That(result).IsEqualTo(Maybe<IEnumerable<int>>.None);
    }

    [Test]
    public async Task TestAllSomeOrNoneSome()
    {
        Maybe<int>[] m = [Maybe<int>.None, Maybe.Some(2), Maybe.Some(5), Maybe<int>.None, Maybe.Some(7)];

        Maybe<IEnumerable<int>> result = m.AllSomeOrNone();

        await Assert.That(result).IsEqualTo(Maybe<IEnumerable<int>>.None);
    }

    [Test]
    public async Task TestAllSomeOrNoneAll()
    {
        Maybe<int>[] m = [Maybe.Some(3), Maybe.Some(2), Maybe.Some(5), Maybe.Some(4), Maybe.Some(7)];

        Maybe<IEnumerable<int>> result = m.AllSomeOrNone();

        IEnumerable<int>? r = result.Match<IEnumerable<int>?>(onSome: static v => v, onNone: static () => null);
        await Assert.That(r).IsNotNull();
        await Assert.That(r).IsEquivalentTo(new[] { 3, 2, 5, 4, 7 }, CollectionOrdering.Matching);
    }

    [Test]
    public async Task TestWhereSomeEmptyAndNullSource()
    {
        await Assert.That(Array.Empty<Maybe<int>>().WhereSome()).IsEquivalentTo(Array.Empty<int>(), CollectionOrdering.Matching);
        await Assert.That(((IEnumerable<Maybe<int>>?)null).WhereSome()).IsEquivalentTo(Array.Empty<int>(), CollectionOrdering.Matching);
    }

    [Test]
    public async Task TestWhereSomeKeepsDefaultValues()
    {
        Maybe<int>[] m = [Maybe.Some(0), Maybe<int>.None, Maybe.Some(0)];

        await Assert.That(m.WhereSome()).IsEquivalentTo(new[] { 0, 0 }, CollectionOrdering.Matching);
    }

    [Test]
    public async Task TestAllSomeOrNoneWithSelectorAll()
    {
        string[] source = ["1", "2", "3"];

        Maybe<IEnumerable<int>> result = source.AllSomeOrNone(static s => s.TryParseInt32());

        await Assert.That(result.Match<IEnumerable<int>?>(onSome: static v => v, onNone: static () => null)).IsEquivalentTo(new[] { 1, 2, 3 }, CollectionOrdering.Matching);
    }

    [Test]
    public async Task TestAllSomeOrNoneWithSelectorSome()
    {
        string[] source = ["1", "x", "3"];

        Maybe<IEnumerable<int>> result = source.AllSomeOrNone(static s => s.TryParseInt32());

        await Assert.That(result).IsEqualTo(Maybe<IEnumerable<int>>.None);
    }

    [Test]
    public async Task TestAllSomeOrNoneWithSelectorEmpty()
    {
        Maybe<IEnumerable<int>> result = Array.Empty<string>().AllSomeOrNone(static s => s.TryParseInt32());

        await Assert.That(result.Match<IEnumerable<int>?>(onSome: static v => v, onNone: static () => null)).IsEquivalentTo(Array.Empty<int>(), CollectionOrdering.Matching);
    }

    [Test]
    public async Task TestToEnumerable()
    {
        await Assert.That(Maybe.Some(2).ToEnumerable()).IsEquivalentTo(new[] { 2 }, CollectionOrdering.Matching);
        await Assert.That(Maybe<int>.None.ToEnumerable()).IsEquivalentTo(Array.Empty<int>(), CollectionOrdering.Matching);
    }

    [Test]
    public async Task TestToEnumerableFlattensInSelectMany()
    {
        string[] source = ["1", "x", "3"];

        IEnumerable<int> result = source.SelectMany(static s => s.TryParseInt32().ToEnumerable());

        await Assert.That(result).IsEquivalentTo(new[] { 1, 3 }, CollectionOrdering.Matching);
    }

    [Test]
    public async Task TestToNullable()
    {
        await Assert.That(Maybe.Some(2).ToNullable()).IsEqualTo((int?)2);
        await Assert.That(Maybe<int>.None.ToNullable()).IsNull();
    }

    [Test]
    public async Task TestToMaybeReference()
    {
        await Assert.That("a".ToMaybe()).IsEqualTo(Maybe.Some("a"));
        await Assert.That(((string?)null).ToMaybe()).IsEqualTo(Maybe<string>.None);
    }

    [Test]
    public async Task TestToMaybeNullable()
    {
        await Assert.That(((int?)2).ToMaybe()).IsEqualTo(Maybe.Some(2));
        await Assert.That(((int?)null).ToMaybe()).IsEqualTo(Maybe<int>.None);
    }

    [Test]
    public async Task TestToMaybeAndToNullableRoundTrip()
    {
        await Assert.That(((int?)2).ToMaybe().ToNullable()).IsEqualTo((int?)2);
        await Assert.That(((int?)null).ToMaybe().ToNullable()).IsNull();
    }

    [Test]
    public async Task TestValueOr()
    {
        await Assert.That(Maybe.Some(2).ValueOr(9)).IsEqualTo(2);
        await Assert.That(Maybe<int>.None.ValueOr(9)).IsEqualTo(9);
    }

    [Test]
    public async Task TestValueOrLazy()
    {
        int calls = 0;

        await Assert.That(Maybe.Some(2)
                .ValueOr(() =>
                {
                    calls++;
                    return 9;
                })).IsEqualTo(2);

        await Assert.That(calls).IsEqualTo(0);

        await Assert.That(Maybe<int>.None.ValueOr(() =>
            {
                calls++;
                return 9;
            })).IsEqualTo(9);

        await Assert.That(calls).IsEqualTo(1);
    }

    [Test]
    public async Task TestValueOrDefault()
    {
        await Assert.That(Maybe.Some(2).ValueOrDefault()).IsEqualTo(2);
        await Assert.That(Maybe<int>.None.ValueOrDefault()).IsEqualTo(0);
        await Assert.That(Maybe<string>.None.ValueOrDefault()).IsNull();
    }

    [Test]
    public async Task TestValueOrThrow()
    {
        await Assert.That(Maybe.Some(2).ValueOrThrow(static () => new InvalidOperationException("no value"))).IsEqualTo(2);

        InvalidOperationException? e =
            await Assert.That(static () =>
                Maybe<int>.None.ValueOrThrow(static () => new InvalidOperationException("no value"))).ThrowsExactly<InvalidOperationException>();

        await Assert.That(e?.Message).IsEqualTo("no value");
    }

    [Test]
    public async Task TestOrElse()
    {
        await Assert.That(Maybe.Some(2).OrElse(Maybe.Some(9))).IsEqualTo(Maybe.Some(2));
        await Assert.That(Maybe<int>.None.OrElse(Maybe.Some(9))).IsEqualTo(Maybe.Some(9));
        await Assert.That(Maybe<int>.None.OrElse(Maybe<int>.None)).IsEqualTo(Maybe<int>.None);
    }

    [Test]
    public async Task TestOrElseLazy()
    {
        int calls = 0;

        await Assert.That(Maybe.Some(2)
                .OrElse(() =>
                {
                    calls++;
                    return Maybe.Some(9);
                })).IsEqualTo(Maybe.Some(2));

        await Assert.That(calls).IsEqualTo(0);

        await Assert.That(Maybe<int>.None.OrElse(() =>
            {
                calls++;
                return Maybe.Some(9);
            })).IsEqualTo(Maybe.Some(9));

        await Assert.That(calls).IsEqualTo(1);
    }

    [Test]
    public async Task TestOrElseChains()
    {
        Maybe<int> result = Maybe<int>.None.OrElse(Maybe<int>.None).OrElse(Maybe.Some(3));

        await Assert.That(result).IsEqualTo(Maybe.Some(3));
    }

    [Test]
    public async Task TestLift2()
    {
        await Assert.That(Maybe.Some(2).Lift(b: Maybe.Some(3), f: static (a, b) => a + b)).IsEqualTo(Maybe.Some(5));

        await Assert.That(Maybe<int>.None.Lift(b: Maybe.Some(3), f: static (a, b) => a + b)).IsEqualTo(Maybe<int>.None);

        await Assert.That(Maybe.Some(2).Lift(b: Maybe<int>.None, f: static (a, b) => a + b)).IsEqualTo(Maybe<int>.None);
    }

    [Test]
    public async Task TestLift2DoesNotRunFunctionWithoutBothValues()
    {
        int calls = 0;

        Maybe<int> result =
            Maybe.Some(2)
                .Lift(
                    b: Maybe<int>.None,
                    f: (a, b) =>
                    {
                        calls++;
                        return a + b;
                    });

        await Assert.That(result).IsEqualTo(Maybe<int>.None);
        await Assert.That(calls).IsEqualTo(0);
    }

    [Test]
    public async Task TestLift3()
    {
        await Assert.That(Maybe.Some(2).Lift(b: Maybe.Some(3), c: Maybe.Some(4), f: static (a, b, c) => a + b + c)).IsEqualTo(Maybe.Some(9));

        await Assert.That(Maybe.Some(2).Lift(b: Maybe<int>.None, c: Maybe.Some(4), f: static (a, b, c) => a + b + c)).IsEqualTo(Maybe<int>.None);
    }

    [Test]
    public async Task TestLift4()
    {
        await Assert.That(Maybe.Some(2)
                .Lift(b: Maybe.Some(3), c: Maybe.Some(4), d: Maybe.Some(5), f: static (a, b, c, d) => a + b + c + d)).IsEqualTo(Maybe.Some(14));

        await Assert.That(Maybe.Some(2)
                .Lift(b: Maybe.Some(3), c: Maybe.Some(4), d: Maybe<int>.None, f: static (a, b, c, d) => a + b + c + d)).IsEqualTo(Maybe<int>.None);
    }
}
