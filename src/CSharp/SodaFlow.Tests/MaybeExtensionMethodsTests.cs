using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using SodaFlow.Functional;

namespace SodaFlow.Tests;

[TestFixture]
public class MaybeExtensionMethodsTests
{
    [Test]
    public void TestFlatten1()
    {
        Maybe<Maybe<int>> m = Maybe.None;

        Maybe<int> result = m.Flatten();

        Assert.AreEqual(expected: Maybe<int>.None, actual: result);
    }

    [Test]
    public void TestFlatten2()
    {
        Maybe<Maybe<int>> m = Maybe.Some(Maybe<int>.None);

        Maybe<int> result = m.Flatten();

        Assert.AreEqual(expected: Maybe<int>.None, actual: result);
    }

    [Test]
    public void TestFlatten3()
    {
        Maybe<Maybe<int>> m = Maybe.Some(Maybe.Some(5));

        Maybe<int> result = m.Flatten();

        Assert.AreEqual(expected: Maybe.Some(5), actual: result);
    }

    [Test]
    public void TestWhereSomeNone()
    {
        Maybe<int>[] m = [Maybe<int>.None, Maybe<int>.None, Maybe<int>.None, Maybe<int>.None, Maybe<int>.None];

        IEnumerable<int> result = m.WhereSome();

        CollectionAssert.AreEqual(expected: Array.Empty<int>(), actual: result);
    }

    [Test]
    public void TestWhereSomeSome()
    {
        Maybe<int>[] m = [Maybe<int>.None, Maybe.Some(2), Maybe.Some(5), Maybe<int>.None, Maybe.Some(7)];

        IEnumerable<int> result = m.WhereSome();

        CollectionAssert.AreEqual(expected: new[] { 2, 5, 7 }, actual: result);
    }

    [Test]
    public void TestWhereSomeAll()
    {
        Maybe<int>[] m = [Maybe.Some(3), Maybe.Some(2), Maybe.Some(5), Maybe.Some(4), Maybe.Some(7)];

        IEnumerable<int> result = m.WhereSome();

        CollectionAssert.AreEqual(expected: new[] { 3, 2, 5, 4, 7 }, actual: result);
    }

    [Test]
    public void TestAllSomeOrNoneNone()
    {
        Maybe<int>[] m = [Maybe<int>.None, Maybe<int>.None, Maybe<int>.None, Maybe<int>.None, Maybe<int>.None];

        Maybe<IEnumerable<int>> result = m.AllSomeOrNone();

        Assert.AreEqual(expected: Maybe<IEnumerable<int>>.None, actual: result);
    }

    [Test]
    public void TestAllSomeOrNoneSome()
    {
        Maybe<int>[] m = [Maybe<int>.None, Maybe.Some(2), Maybe.Some(5), Maybe<int>.None, Maybe.Some(7)];

        Maybe<IEnumerable<int>> result = m.AllSomeOrNone();

        Assert.AreEqual(expected: Maybe<IEnumerable<int>>.None, actual: result);
    }

    [Test]
    public void TestAllSomeOrNoneAll()
    {
        Maybe<int>[] m = [Maybe.Some(3), Maybe.Some(2), Maybe.Some(5), Maybe.Some(4), Maybe.Some(7)];

        Maybe<IEnumerable<int>> result = m.AllSomeOrNone();

        IEnumerable<int>? r = result.Match<IEnumerable<int>?>(onSome: static v => v, onNone: static () => null);
        Assert.IsNotNull(r);
        CollectionAssert.AreEqual(expected: new[] { 3, 2, 5, 4, 7 }, actual: r);
    }

    [Test]
    public void TestWhereSomeEmptyAndNullSource()
    {
        CollectionAssert.AreEqual(expected: Array.Empty<int>(), actual: Array.Empty<Maybe<int>>().WhereSome());
        CollectionAssert.AreEqual(expected: Array.Empty<int>(), actual: ((IEnumerable<Maybe<int>>?)null).WhereSome());
    }

    [Test]
    public void TestWhereSomeKeepsDefaultValues()
    {
        Maybe<int>[] m = [Maybe.Some(0), Maybe<int>.None, Maybe.Some(0)];

        CollectionAssert.AreEqual(expected: new[] { 0, 0 }, actual: m.WhereSome());
    }

    [Test]
    public void TestAllSomeOrNoneWithSelectorAll()
    {
        string[] source = ["1", "2", "3"];

        Maybe<IEnumerable<int>> result = source.AllSomeOrNone(static s => s.TryParseInt32());

        CollectionAssert.AreEqual(
            expected: new[] { 1, 2, 3 },
            actual: result.Match<IEnumerable<int>?>(onSome: static v => v, onNone: static () => null));
    }

    [Test]
    public void TestAllSomeOrNoneWithSelectorSome()
    {
        string[] source = ["1", "x", "3"];

        Maybe<IEnumerable<int>> result = source.AllSomeOrNone(static s => s.TryParseInt32());

        Assert.AreEqual(expected: Maybe<IEnumerable<int>>.None, actual: result);
    }

    [Test]
    public void TestAllSomeOrNoneWithSelectorEmpty()
    {
        Maybe<IEnumerable<int>> result = Array.Empty<string>().AllSomeOrNone(static s => s.TryParseInt32());

        CollectionAssert.AreEqual(
            expected: Array.Empty<int>(),
            actual: result.Match<IEnumerable<int>?>(onSome: static v => v, onNone: static () => null));
    }

    [Test]
    public void TestToEnumerable()
    {
        CollectionAssert.AreEqual(expected: new[] { 2 }, actual: Maybe.Some(2).ToEnumerable());
        CollectionAssert.AreEqual(expected: Array.Empty<int>(), actual: Maybe<int>.None.ToEnumerable());
    }

    [Test]
    public void TestToEnumerableFlattensInSelectMany()
    {
        string[] source = ["1", "x", "3"];

        IEnumerable<int> result = source.SelectMany(static s => s.TryParseInt32().ToEnumerable());

        CollectionAssert.AreEqual(expected: new[] { 1, 3 }, actual: result);
    }

    [Test]
    public void TestToNullable()
    {
        Assert.AreEqual(expected: (int?)2, actual: Maybe.Some(2).ToNullable());
        Assert.AreEqual(expected: null, actual: Maybe<int>.None.ToNullable());
    }

    [Test]
    public void TestToMaybeReference()
    {
        Assert.AreEqual(expected: Maybe.Some("a"), actual: "a".ToMaybe());
        Assert.AreEqual(expected: Maybe<string>.None, actual: ((string?)null).ToMaybe());
    }

    [Test]
    public void TestToMaybeNullable()
    {
        Assert.AreEqual(expected: Maybe.Some(2), actual: ((int?)2).ToMaybe());
        Assert.AreEqual(expected: Maybe<int>.None, actual: ((int?)null).ToMaybe());
    }

    [Test]
    public void TestToMaybeAndToNullableRoundTrip()
    {
        Assert.AreEqual(expected: (int?)2, actual: ((int?)2).ToMaybe().ToNullable());
        Assert.AreEqual(expected: null, actual: ((int?)null).ToMaybe().ToNullable());
    }

    [Test]
    public void TestValueOr()
    {
        Assert.AreEqual(expected: 2, actual: Maybe.Some(2).ValueOr(9));
        Assert.AreEqual(expected: 9, actual: Maybe<int>.None.ValueOr(9));
    }

    [Test]
    public void TestValueOrLazy()
    {
        int calls = 0;

        Assert.AreEqual(
            expected: 2,
            actual: Maybe.Some(2)
                .ValueOr(() =>
                {
                    calls++;
                    return 9;
                }));

        Assert.AreEqual(expected: 0, actual: calls);

        Assert.AreEqual(
            expected: 9,
            actual: Maybe<int>.None.ValueOr(() =>
            {
                calls++;
                return 9;
            }));

        Assert.AreEqual(expected: 1, actual: calls);
    }

    [Test]
    public void TestValueOrDefault()
    {
        Assert.AreEqual(expected: 2, actual: Maybe.Some(2).ValueOrDefault());
        Assert.AreEqual(expected: 0, actual: Maybe<int>.None.ValueOrDefault());
        Assert.AreEqual(expected: null, actual: Maybe<string>.None.ValueOrDefault());
    }

    [Test]
    public void TestValueOrThrow()
    {
        Assert.AreEqual(
            expected: 2,
            actual: Maybe.Some(2).ValueOrThrow(static () => new InvalidOperationException("no value")));

        InvalidOperationException e =
            Assert.Throws<InvalidOperationException>(static () =>
                Maybe<int>.None.ValueOrThrow(static () => new InvalidOperationException("no value")));

        Assert.AreEqual(expected: "no value", actual: e.Message);
    }

    [Test]
    public void TestOrElse()
    {
        Assert.AreEqual(expected: Maybe.Some(2), actual: Maybe.Some(2).OrElse(Maybe.Some(9)));
        Assert.AreEqual(expected: Maybe.Some(9), actual: Maybe<int>.None.OrElse(Maybe.Some(9)));
        Assert.AreEqual(expected: Maybe<int>.None, actual: Maybe<int>.None.OrElse(Maybe<int>.None));
    }

    [Test]
    public void TestOrElseLazy()
    {
        int calls = 0;

        Assert.AreEqual(
            expected: Maybe.Some(2),
            actual: Maybe.Some(2)
                .OrElse(() =>
                {
                    calls++;
                    return Maybe.Some(9);
                }));

        Assert.AreEqual(expected: 0, actual: calls);

        Assert.AreEqual(
            expected: Maybe.Some(9),
            actual: Maybe<int>.None.OrElse(() =>
            {
                calls++;
                return Maybe.Some(9);
            }));

        Assert.AreEqual(expected: 1, actual: calls);
    }

    [Test]
    public void TestOrElseChains()
    {
        Maybe<int> result = Maybe<int>.None.OrElse(Maybe<int>.None).OrElse(Maybe.Some(3));

        Assert.AreEqual(expected: Maybe.Some(3), actual: result);
    }

    [Test]
    public void TestLift2()
    {
        Assert.AreEqual(
            expected: Maybe.Some(5),
            actual: Maybe.Some(2).Lift(b: Maybe.Some(3), f: static (a, b) => a + b));

        Assert.AreEqual(
            expected: Maybe<int>.None,
            actual: Maybe<int>.None.Lift(b: Maybe.Some(3), f: static (a, b) => a + b));

        Assert.AreEqual(
            expected: Maybe<int>.None,
            actual: Maybe.Some(2).Lift(b: Maybe<int>.None, f: static (a, b) => a + b));
    }

    [Test]
    public void TestLift2DoesNotRunFunctionWithoutBothValues()
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

        Assert.AreEqual(expected: Maybe<int>.None, actual: result);
        Assert.AreEqual(expected: 0, actual: calls);
    }

    [Test]
    public void TestLift3()
    {
        Assert.AreEqual(
            expected: Maybe.Some(9),
            actual: Maybe.Some(2).Lift(b: Maybe.Some(3), c: Maybe.Some(4), f: static (a, b, c) => a + b + c));

        Assert.AreEqual(
            expected: Maybe<int>.None,
            actual: Maybe.Some(2).Lift(b: Maybe<int>.None, c: Maybe.Some(4), f: static (a, b, c) => a + b + c));
    }

    [Test]
    public void TestLift4()
    {
        Assert.AreEqual(
            expected: Maybe.Some(14),
            actual: Maybe.Some(2)
                .Lift(b: Maybe.Some(3), c: Maybe.Some(4), d: Maybe.Some(5), f: static (a, b, c, d) => a + b + c + d));

        Assert.AreEqual(
            expected: Maybe<int>.None,
            actual: Maybe.Some(2)
                .Lift(b: Maybe.Some(3), c: Maybe.Some(4), d: Maybe<int>.None, f: static (a, b, c, d) => a + b + c + d));
    }
}
