using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using TUnit.Assertions;
using TUnit.Assertions.Enums;
using TUnit.Assertions.Extensions;
using TUnit.Core;
using SodaFlow.Functional;

namespace SodaFlow.Tests;

public class EnumerableExtensionMethodsTests
{
    [Test]
    public async Task TestChoose()
    {
        string[] source = ["1", "x", "3", string.Empty, "5"];

        IEnumerable<int> result = source.Choose(static s => s.TryParseInt32());

        await Assert.That(result).IsEquivalentTo(new[] { 1, 3, 5 }, CollectionOrdering.Matching);
    }

    [Test]
    public async Task TestChooseNoneKept()
    {
        string[] source = ["x", "y"];

        IEnumerable<int> result = source.Choose(static s => s.TryParseInt32());

        await Assert.That(result).IsEquivalentTo(Array.Empty<int>(), CollectionOrdering.Matching);
    }

    [Test]
    public async Task TestChooseNullSource()
    {
        IEnumerable<string>? source = null;

        IEnumerable<int> result = source.Choose(static s => s.TryParseInt32());

        await Assert.That(result).IsEquivalentTo(Array.Empty<int>(), CollectionOrdering.Matching);
    }

    [Test]
    public async Task TestChooseIsLazy()
    {
        int calls = 0;

        IEnumerable<int> result =
            new[] { 1, 2, 3 }.Choose(v =>
            {
                calls++;
                return Maybe.Some(v);
            });

        await Assert.That(calls).IsEqualTo(0);

        // ReSharper disable once ReturnValueOfPureMethodIsNotUsed - Testing for side effect only.
        result.ToArray();

        await Assert.That(calls).IsEqualTo(3);
    }

    [Test]
    public async Task TestChooseWithIndex()
    {
        string[] source = ["a", "b", "c", "d"];

        IEnumerable<string> result = source.Choose(static (v, i) => Maybe.SomeIf(condition: i % 2 == 0, value: v + i));

        await Assert.That(result).IsEquivalentTo(new[] { "a0", "c2" }, CollectionOrdering.Matching);
    }

    [Test]
    public async Task TestFirstOrNone()
    {
        await Assert.That(new[] { 1, 2, 3 }.FirstOrNone()).IsEqualTo(Maybe.Some(1));
        await Assert.That(Array.Empty<int>().FirstOrNone()).IsEqualTo(Maybe<int>.None);
        await Assert.That(((IEnumerable<int>?)null).FirstOrNone()).IsEqualTo(Maybe<int>.None);
    }

    [Test]
    public async Task TestFirstOrNoneKeepsDefaultValue() =>
        await Assert.That(new[] { 0, 1 }.FirstOrNone()).IsEqualTo(Maybe.Some(0));

    [Test]
    public async Task TestFirstOrNoneReadsOneElement()
    {
        int read = 0;

        Maybe<int> result = Counted(source: [1, 2, 3], onRead: () => read++).FirstOrNone();

        await Assert.That(result).IsEqualTo(Maybe.Some(1));
        await Assert.That(read).IsEqualTo(1);
    }

    [Test]
    public async Task TestFirstOrNoneWithPredicate()
    {
        await Assert.That(new[] { 1, 2, 3, 4 }.FirstOrNone(static v => v % 2 == 0)).IsEqualTo(Maybe.Some(2));
        await Assert.That(new[] { 1, 3 }.FirstOrNone(static v => v % 2 == 0)).IsEqualTo(Maybe<int>.None);
        await Assert.That(((IEnumerable<int>?)null).FirstOrNone(static _ => true)).IsEqualTo(Maybe<int>.None);
    }

    [Test]
    public async Task TestLastOrNoneIndexable()
    {
        await Assert.That(new[] { 1, 2, 3 }.LastOrNone()).IsEqualTo(Maybe.Some(3));
        await Assert.That(Array.Empty<int>().LastOrNone()).IsEqualTo(Maybe<int>.None);
        await Assert.That(((IEnumerable<int>?)null).LastOrNone()).IsEqualTo(Maybe<int>.None);
    }

    [Test]
    public async Task TestLastOrNoneNotIndexable()
    {
        await Assert.That(Yield(1, 2, 3).LastOrNone()).IsEqualTo(Maybe.Some(3));
        await Assert.That(Yield<int>().LastOrNone()).IsEqualTo(Maybe<int>.None);
    }

    [Test]
    public async Task TestLastOrNoneWithPredicate()
    {
        await Assert.That(new[] { 1, 2, 3, 4, 5 }.LastOrNone(static v => v % 2 == 0)).IsEqualTo(Maybe.Some(4));
        await Assert.That(new[] { 1, 3 }.LastOrNone(static v => v % 2 == 0)).IsEqualTo(Maybe<int>.None);
    }

    [Test]
    public async Task TestSingleOrNone()
    {
        await Assert.That(new[] { 1 }.SingleOrNone()).IsEqualTo(Maybe.Some(1));
        await Assert.That(Array.Empty<int>().SingleOrNone()).IsEqualTo(Maybe<int>.None);
        await Assert.That(((IEnumerable<int>?)null).SingleOrNone()).IsEqualTo(Maybe<int>.None);
    }

    [Test]
    public async Task TestSingleOrNoneThrowsOnMoreThanOne() =>
        // ReSharper disable once ReturnValueOfPureMethodIsNotUsed - Testing for side effect only.
        await Assert.That(static () => new[] { 1, 2 }.SingleOrNone()).ThrowsExactly<InvalidOperationException>();

    [Test]
    public async Task TestSingleOrNoneReadsTwoElements()
    {
        int read = 0;

        Maybe<int> result = Counted(source: [1], onRead: () => read++).SingleOrNone();

        await Assert.That(result).IsEqualTo(Maybe.Some(1));
        await Assert.That(read).IsEqualTo(1);
    }

    [Test]
    public async Task TestSingleOrNoneWithPredicate()
    {
        await Assert.That(new[] { 1, 2, 3 }.SingleOrNone(static v => v % 2 == 0)).IsEqualTo(Maybe.Some(2));
        await Assert.That(new[] { 1, 3 }.SingleOrNone(static v => v % 2 == 0)).IsEqualTo(Maybe<int>.None);
        // ReSharper disable once ReturnValueOfPureMethodIsNotUsed - Testing for side effect only.
        await Assert.That(static () => new[] { 2, 4 }.SingleOrNone(static v => v % 2 == 0)).ThrowsExactly<InvalidOperationException>();
    }

    [Test]
    public async Task TestElementAtOrNoneIndexable()
    {
        int[] source = [1, 2, 3];

        await Assert.That(source.ElementAtOrNone(0)).IsEqualTo(Maybe.Some(1));
        await Assert.That(source.ElementAtOrNone(2)).IsEqualTo(Maybe.Some(3));
        await Assert.That(source.ElementAtOrNone(3)).IsEqualTo(Maybe<int>.None);
        await Assert.That(source.ElementAtOrNone(-1)).IsEqualTo(Maybe<int>.None);
        await Assert.That(((IEnumerable<int>?)null).ElementAtOrNone(0)).IsEqualTo(Maybe<int>.None);
    }

    [Test]
    public async Task TestElementAtOrNoneNotIndexable()
    {
        await Assert.That(Yield(1, 2, 3).ElementAtOrNone(0)).IsEqualTo(Maybe.Some(1));
        await Assert.That(Yield(1, 2, 3).ElementAtOrNone(2)).IsEqualTo(Maybe.Some(3));
        await Assert.That(Yield(1, 2, 3).ElementAtOrNone(3)).IsEqualTo(Maybe<int>.None);
        await Assert.That(Yield(1, 2, 3).ElementAtOrNone(-1)).IsEqualTo(Maybe<int>.None);
    }

    [Test]
    public async Task TestElementAtOrNoneReadsNoFurtherThanNeeded()
    {
        int read = 0;

        Maybe<int> result = Counted(source: [1, 2, 3, 4, 5], onRead: () => read++).ElementAtOrNone(1);

        await Assert.That(result).IsEqualTo(Maybe.Some(2));
        await Assert.That(read).IsEqualTo(2);
    }

    [Test]
    public async Task TestAggregateOrNone()
    {
        await Assert.That(new[] { 1, 2, 3, 4 }.AggregateOrNone(static (a, b) => a + b)).IsEqualTo(Maybe.Some(10));
        await Assert.That(new[] { 7 }.AggregateOrNone(static (a, b) => a + b)).IsEqualTo(Maybe.Some(7));
        await Assert.That(Array.Empty<int>().AggregateOrNone(static (a, b) => a + b)).IsEqualTo(Maybe<int>.None);

        await Assert.That(((IEnumerable<int>?)null).AggregateOrNone(static (a, b) => a + b)).IsEqualTo(Maybe<int>.None);
    }

    [Test]
    public async Task TestAggregateOrNoneIsLeftAssociative() =>
        await Assert.That(new[] { "a", "b", "c" }.AggregateOrNone(static (a, b) => "(" + a + " " + b + ")")).IsEqualTo(Maybe.Some("((a b) c)"));

    [Test]
    public async Task TestAggregateOrNoneDoesNotRunFunctionForOneElement()
    {
        int calls = 0;

        Maybe<int> result =
            new[] { 7 }.AggregateOrNone((a, b) =>
            {
                calls++;
                return a + b;
            });

        await Assert.That(result).IsEqualTo(Maybe.Some(7));
        await Assert.That(calls).IsEqualTo(0);
    }

    [Test]
    public async Task TestMinOrNoneAndMaxOrNone()
    {
        int[] source = [3, 1, 4, 1, 5];

        await Assert.That(source.MinOrNone()).IsEqualTo(Maybe.Some(1));
        await Assert.That(source.MaxOrNone()).IsEqualTo(Maybe.Some(5));
        await Assert.That(Array.Empty<int>().MinOrNone()).IsEqualTo(Maybe<int>.None);
        await Assert.That(Array.Empty<int>().MaxOrNone()).IsEqualTo(Maybe<int>.None);
        await Assert.That(((IEnumerable<int>?)null).MinOrNone()).IsEqualTo(Maybe<int>.None);
        await Assert.That(((IEnumerable<int>?)null).MaxOrNone()).IsEqualTo(Maybe<int>.None);
    }

    [Test]
    public async Task TestMinOrNoneKeepsZero() =>
        // Min throws on an empty sequence precisely so it need not conflate it with this.
        await Assert.That(new[] { 0, 1 }.MinOrNone()).IsEqualTo(Maybe.Some(0));

    [Test]
    public async Task TestMinOrNoneWithComparer()
    {
        string[] source = ["bbb", "a", "cc"];

        await Assert.That(source.MinOrNone(Comparer<string>.Create(static (x, y) => x.Length - y.Length))).IsEqualTo(Maybe.Some("a"));

        await Assert.That(source.MaxOrNone(Comparer<string>.Create(static (x, y) => x.Length - y.Length))).IsEqualTo(Maybe.Some("bbb"));
    }

    [Test]
    public async Task TestMinOrNoneWithSelector()
    {
        string[] source = ["bbb", "a", "cc"];

        await Assert.That(source.MinOrNone(static v => v.Length)).IsEqualTo(Maybe.Some(1));
        await Assert.That(source.MaxOrNone(static v => v.Length)).IsEqualTo(Maybe.Some(3));
        await Assert.That(Array.Empty<string>().MinOrNone(static v => v.Length)).IsEqualTo(Maybe<int>.None);
    }

    [Test]
    public async Task TestMinOrNoneSkipsNulls()
    {
        // Comparer<string>.Default sorts null before everything, so without skipping them a
        // single null would be the answer for every sequence of a reference type.
        string?[] source = ["b", null, "a"];

        await Assert.That(source.MinOrNone()).IsEqualTo(Maybe.Some("a"));
        await Assert.That(source.MaxOrNone()).IsEqualTo(Maybe.Some("b"));
    }

    [Test]
    public async Task TestMinOrNoneSkipsNullsInNullableValueTypes()
    {
        int?[] source = [3, null, 1];

        await Assert.That(source.MinOrNone()).IsEqualTo(Maybe.Some((int?)1));
        await Assert.That(source.MaxOrNone()).IsEqualTo(Maybe.Some((int?)3));
    }

    [Test]
    public async Task TestMinOrNoneOfNothingButNulls()
    {
        // LINQ answers null here, which cannot be told from a sequence whose minimum is null.
        // There is genuinely nothing to compare, so this says so.
        await Assert.That(new string?[] { null, null }.MinOrNone()).IsEqualTo(Maybe<string?>.None);
        await Assert.That(new int?[] { null, null }.MaxOrNone()).IsEqualTo(Maybe<int?>.None);
    }

    [Test]
    public async Task TestMinOrNoneOrdersByTheComparerIncludingNaN()
    {
        // Pinned rather than claimed: Comparer<double>.Default sorts NaN below everything.
        double[] source = [2.0, double.NaN, 1.0];

        await Assert.That(source.MinOrNone()).IsEqualTo(Maybe.Some(double.NaN));
        await Assert.That(source.MaxOrNone()).IsEqualTo(Maybe.Some(2.0));
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
