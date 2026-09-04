using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using SodaFlow.Functional;

namespace SodaFlow.Tests;

[TestFixture]
public class EnumerableExtensionMethodsTests
{
    [Test]
    public void TestChoose()
    {
        string[] source = ["1", "x", "3", string.Empty, "5"];

        IEnumerable<int> result = source.Choose(static s => s.TryParseInt32());

        CollectionAssert.AreEqual(expected: new[] { 1, 3, 5 }, actual: result);
    }

    [Test]
    public void TestChooseNoneKept()
    {
        string[] source = ["x", "y"];

        IEnumerable<int> result = source.Choose(static s => s.TryParseInt32());

        CollectionAssert.AreEqual(expected: Array.Empty<int>(), actual: result);
    }

    [Test]
    public void TestChooseNullSource()
    {
        IEnumerable<string>? source = null;

        IEnumerable<int> result = source.Choose(static s => s.TryParseInt32());

        CollectionAssert.AreEqual(expected: Array.Empty<int>(), actual: result);
    }

    [Test]
    public void TestChooseIsLazy()
    {
        int calls = 0;

        IEnumerable<int> result =
            new[] { 1, 2, 3 }.Choose(v =>
            {
                calls++;
                return Maybe.Some(v);
            });

        Assert.AreEqual(expected: 0, actual: calls);

        // ReSharper disable once ReturnValueOfPureMethodIsNotUsed - Testing for side effect only.
        result.ToArray();

        Assert.AreEqual(expected: 3, actual: calls);
    }

    [Test]
    public void TestChooseWithIndex()
    {
        string[] source = ["a", "b", "c", "d"];

        IEnumerable<string> result = source.Choose(static (v, i) => Maybe.SomeIf(condition: i % 2 == 0, value: v + i));

        CollectionAssert.AreEqual(expected: new[] { "a0", "c2" }, actual: result);
    }

    [Test]
    public void TestFirstOrNone()
    {
        Assert.AreEqual(expected: Maybe.Some(1), actual: new[] { 1, 2, 3 }.FirstOrNone());
        Assert.AreEqual(expected: Maybe<int>.None, actual: Array.Empty<int>().FirstOrNone());
        Assert.AreEqual(expected: Maybe<int>.None, actual: ((IEnumerable<int>?)null).FirstOrNone());
    }

    [Test]
    public void TestFirstOrNoneKeepsDefaultValue() =>
        Assert.AreEqual(expected: Maybe.Some(0), actual: new[] { 0, 1 }.FirstOrNone());

    [Test]
    public void TestFirstOrNoneReadsOneElement()
    {
        int read = 0;

        Maybe<int> result = Counted(source: [1, 2, 3], onRead: () => read++).FirstOrNone();

        Assert.AreEqual(expected: Maybe.Some(1), actual: result);
        Assert.AreEqual(expected: 1, actual: read);
    }

    [Test]
    public void TestFirstOrNoneWithPredicate()
    {
        Assert.AreEqual(expected: Maybe.Some(2), actual: new[] { 1, 2, 3, 4 }.FirstOrNone(static v => v % 2 == 0));
        Assert.AreEqual(expected: Maybe<int>.None, actual: new[] { 1, 3 }.FirstOrNone(static v => v % 2 == 0));
        Assert.AreEqual(expected: Maybe<int>.None, actual: ((IEnumerable<int>?)null).FirstOrNone(static _ => true));
    }

    [Test]
    public void TestLastOrNoneIndexable()
    {
        Assert.AreEqual(expected: Maybe.Some(3), actual: new[] { 1, 2, 3 }.LastOrNone());
        Assert.AreEqual(expected: Maybe<int>.None, actual: Array.Empty<int>().LastOrNone());
        Assert.AreEqual(expected: Maybe<int>.None, actual: ((IEnumerable<int>?)null).LastOrNone());
    }

    [Test]
    public void TestLastOrNoneNotIndexable()
    {
        Assert.AreEqual(expected: Maybe.Some(3), actual: Yield(1, 2, 3).LastOrNone());
        Assert.AreEqual(expected: Maybe<int>.None, actual: Yield<int>().LastOrNone());
    }

    [Test]
    public void TestLastOrNoneWithPredicate()
    {
        Assert.AreEqual(expected: Maybe.Some(4), actual: new[] { 1, 2, 3, 4, 5 }.LastOrNone(static v => v % 2 == 0));
        Assert.AreEqual(expected: Maybe<int>.None, actual: new[] { 1, 3 }.LastOrNone(static v => v % 2 == 0));
    }

    [Test]
    public void TestSingleOrNone()
    {
        Assert.AreEqual(expected: Maybe.Some(1), actual: new[] { 1 }.SingleOrNone());
        Assert.AreEqual(expected: Maybe<int>.None, actual: Array.Empty<int>().SingleOrNone());
        Assert.AreEqual(expected: Maybe<int>.None, actual: ((IEnumerable<int>?)null).SingleOrNone());
    }

    [Test]
    public void TestSingleOrNoneThrowsOnMoreThanOne() =>
        // ReSharper disable once ReturnValueOfPureMethodIsNotUsed - Testing for side effect only.
        Assert.Throws<InvalidOperationException>(static () => new[] { 1, 2 }.SingleOrNone());

    [Test]
    public void TestSingleOrNoneReadsTwoElements()
    {
        int read = 0;

        Maybe<int> result = Counted(source: [1], onRead: () => read++).SingleOrNone();

        Assert.AreEqual(expected: Maybe.Some(1), actual: result);
        Assert.AreEqual(expected: 1, actual: read);
    }

    [Test]
    public void TestSingleOrNoneWithPredicate()
    {
        Assert.AreEqual(expected: Maybe.Some(2), actual: new[] { 1, 2, 3 }.SingleOrNone(static v => v % 2 == 0));
        Assert.AreEqual(expected: Maybe<int>.None, actual: new[] { 1, 3 }.SingleOrNone(static v => v % 2 == 0));
        // ReSharper disable once ReturnValueOfPureMethodIsNotUsed - Testing for side effect only.
        Assert.Throws<InvalidOperationException>(static () => new[] { 2, 4 }.SingleOrNone(static v => v % 2 == 0));
    }

    [Test]
    public void TestElementAtOrNoneIndexable()
    {
        int[] source = [1, 2, 3];

        Assert.AreEqual(expected: Maybe.Some(1), actual: source.ElementAtOrNone(0));
        Assert.AreEqual(expected: Maybe.Some(3), actual: source.ElementAtOrNone(2));
        Assert.AreEqual(expected: Maybe<int>.None, actual: source.ElementAtOrNone(3));
        Assert.AreEqual(expected: Maybe<int>.None, actual: source.ElementAtOrNone(-1));
        Assert.AreEqual(expected: Maybe<int>.None, actual: ((IEnumerable<int>?)null).ElementAtOrNone(0));
    }

    [Test]
    public void TestElementAtOrNoneNotIndexable()
    {
        Assert.AreEqual(expected: Maybe.Some(1), actual: Yield(1, 2, 3).ElementAtOrNone(0));
        Assert.AreEqual(expected: Maybe.Some(3), actual: Yield(1, 2, 3).ElementAtOrNone(2));
        Assert.AreEqual(expected: Maybe<int>.None, actual: Yield(1, 2, 3).ElementAtOrNone(3));
        Assert.AreEqual(expected: Maybe<int>.None, actual: Yield(1, 2, 3).ElementAtOrNone(-1));
    }

    [Test]
    public void TestElementAtOrNoneReadsNoFurtherThanNeeded()
    {
        int read = 0;

        Maybe<int> result = Counted(source: [1, 2, 3, 4, 5], onRead: () => read++).ElementAtOrNone(1);

        Assert.AreEqual(expected: Maybe.Some(2), actual: result);
        Assert.AreEqual(expected: 2, actual: read);
    }

    [Test]
    public void TestAggregateOrNone()
    {
        Assert.AreEqual(expected: Maybe.Some(10), actual: new[] { 1, 2, 3, 4 }.AggregateOrNone(static (a, b) => a + b));
        Assert.AreEqual(expected: Maybe.Some(7), actual: new[] { 7 }.AggregateOrNone(static (a, b) => a + b));
        Assert.AreEqual(expected: Maybe<int>.None, actual: Array.Empty<int>().AggregateOrNone(static (a, b) => a + b));

        Assert.AreEqual(
            expected: Maybe<int>.None,
            actual: ((IEnumerable<int>?)null).AggregateOrNone(static (a, b) => a + b));
    }

    [Test]
    public void TestAggregateOrNoneIsLeftAssociative() =>
        Assert.AreEqual(
            expected: Maybe.Some("((a b) c)"),
            actual: new[] { "a", "b", "c" }.AggregateOrNone(static (a, b) => "(" + a + " " + b + ")"));

    [Test]
    public void TestAggregateOrNoneDoesNotRunFunctionForOneElement()
    {
        int calls = 0;

        Maybe<int> result =
            new[] { 7 }.AggregateOrNone((a, b) =>
            {
                calls++;
                return a + b;
            });

        Assert.AreEqual(expected: Maybe.Some(7), actual: result);
        Assert.AreEqual(expected: 0, actual: calls);
    }

    [Test]
    public void TestMinOrNoneAndMaxOrNone()
    {
        int[] source = [3, 1, 4, 1, 5];

        Assert.AreEqual(expected: Maybe.Some(1), actual: source.MinOrNone());
        Assert.AreEqual(expected: Maybe.Some(5), actual: source.MaxOrNone());
        Assert.AreEqual(expected: Maybe<int>.None, actual: Array.Empty<int>().MinOrNone());
        Assert.AreEqual(expected: Maybe<int>.None, actual: Array.Empty<int>().MaxOrNone());
        Assert.AreEqual(expected: Maybe<int>.None, actual: ((IEnumerable<int>?)null).MinOrNone());
        Assert.AreEqual(expected: Maybe<int>.None, actual: ((IEnumerable<int>?)null).MaxOrNone());
    }

    [Test]
    public void TestMinOrNoneKeepsZero() =>
        // Min throws on an empty sequence precisely so it need not conflate it with this.
        Assert.AreEqual(expected: Maybe.Some(0), actual: new[] { 0, 1 }.MinOrNone());

    [Test]
    public void TestMinOrNoneWithComparer()
    {
        string[] source = ["bbb", "a", "cc"];

        Assert.AreEqual(
            expected: Maybe.Some("a"),
            actual: source.MinOrNone(Comparer<string>.Create(static (x, y) => x.Length - y.Length)));

        Assert.AreEqual(
            expected: Maybe.Some("bbb"),
            actual: source.MaxOrNone(Comparer<string>.Create(static (x, y) => x.Length - y.Length)));
    }

    [Test]
    public void TestMinOrNoneWithSelector()
    {
        string[] source = ["bbb", "a", "cc"];

        Assert.AreEqual(expected: Maybe.Some(1), actual: source.MinOrNone(static v => v.Length));
        Assert.AreEqual(expected: Maybe.Some(3), actual: source.MaxOrNone(static v => v.Length));
        Assert.AreEqual(expected: Maybe<int>.None, actual: Array.Empty<string>().MinOrNone(static v => v.Length));
    }

    [Test]
    public void TestMinOrNoneSkipsNulls()
    {
        // Comparer<string>.Default sorts null before everything, so without skipping them a
        // single null would be the answer for every sequence of a reference type.
        string?[] source = ["b", null, "a"];

        Assert.AreEqual(expected: Maybe.Some("a"), actual: source.MinOrNone());
        Assert.AreEqual(expected: Maybe.Some("b"), actual: source.MaxOrNone());
    }

    [Test]
    public void TestMinOrNoneSkipsNullsInNullableValueTypes()
    {
        int?[] source = [3, null, 1];

        Assert.AreEqual(expected: Maybe.Some((int?)1), actual: source.MinOrNone());
        Assert.AreEqual(expected: Maybe.Some((int?)3), actual: source.MaxOrNone());
    }

    [Test]
    public void TestMinOrNoneOfNothingButNulls()
    {
        // LINQ answers null here, which cannot be told from a sequence whose minimum is null.
        // There is genuinely nothing to compare, so this says so.
        Assert.AreEqual(expected: Maybe<string?>.None, actual: new string?[] { null, null }.MinOrNone());
        Assert.AreEqual(expected: Maybe<int?>.None, actual: new int?[] { null, null }.MaxOrNone());
    }

    [Test]
    public void TestMinOrNoneOrdersByTheComparerIncludingNaN()
    {
        // Pinned rather than claimed: Comparer<double>.Default sorts NaN below everything.
        double[] source = [2.0, double.NaN, 1.0];

        Assert.AreEqual(expected: Maybe.Some(double.NaN), actual: source.MinOrNone());
        Assert.AreEqual(expected: Maybe.Some(2.0), actual: source.MaxOrNone());
    }

    private static IEnumerable<T> Yield<T>(params T[] items) => items;

    private static IEnumerable<T> Counted<T>(IEnumerable<T> source, Action onRead)
    {
        foreach (T item in source)
        {
            onRead();
            yield return item;
        }
    }
}
