using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading.Tasks;
using TUnit.Assertions;
using TUnit.Assertions.Extensions;
using TUnit.Core;

namespace SodaFlow.Collections.Tests;

/// <summary>
///     What a group can express. A fold needs an add, a subtract that removes what the add gave,
///     and a zero. The value can be a pair, a record, or a map, thus these tests hold more than a
///     sum and a count.
/// </summary>
public sealed class CollectionFoldShapeTests
{
    [Test]
    public async Task AnAverageThroughAPairAccumulator()
    {
        StreamSink<CollectionEdit<int, ItemIdentity, ItemState>> edits =
            Stream.CreateSink<CollectionEdit<int, ItemIdentity, ItemState>>();

        ReactiveCollection<int, ItemIdentity, ItemState> collection = Create(edits);

        Cell<(long Sum, int Count)> parts =
            Transaction.Run(() =>
                collection.Fold(
                    select: static state => (Sum: state.Score, Count: 1),
                    zero: (Sum: 0L, Count: 0),
                    add: static (a, b) => (Sum: a.Sum + b.Sum, Count: a.Count + b.Count),
                    subtract: static (a, b) => (Sum: a.Sum - b.Sum, Count: a.Count - b.Count)));

        Cell<double> average = Transaction.Run(() => parts.Map(static p => p.Count == 0 ? 0.0 : (double)p.Sum / p.Count));

        edits.Send(TestUtil.Add(TestUtil.Item(number: 1, name: "a", score: 10), TestUtil.Item(number: 2, name: "b", score: 20)));
        await Assert.That(Transaction.Run(average.Sample)).IsEqualTo(15.0);

        edits.Send(TestUtil.Score(key: 2, score: 50));
        await Assert.That(Transaction.Run(average.Sample)).IsEqualTo(30.0);

        edits.Send(TestUtil.Remove(1));
        await Assert.That(Transaction.Run(average.Sample)).IsEqualTo(50.0);

        await Assert.That(Transaction.Run(average.Sample))
            .IsEqualTo(Direct(collection: collection, f: static scores => scores.Average()));
    }

    [Test]
    public async Task AHistogramThroughADictionaryAccumulator()
    {
        StreamSink<CollectionEdit<int, ItemIdentity, ItemState>> edits =
            Stream.CreateSink<CollectionEdit<int, ItemIdentity, ItemState>>();

        ReactiveCollection<int, ItemIdentity, ItemState> collection = Create(edits);

        // One counter for each bucket. A counter of zero goes away. Thus, two collections with
        // the same items give the same map, at each sequence of the edits that made them.
        Cell<ImmutableSortedDictionary<string, int>> byBucket =
            Transaction.Run(() =>
                collection.Fold(
                    select: static state => ImmutableSortedDictionary<string, int>.Empty.Add(key: Bucket(state), value: 1),
                    zero: ImmutableSortedDictionary<string, int>.Empty,
                    add: static (a, b) => Combine(left: a, right: b, sign: 1),
                    subtract: static (a, b) => Combine(left: a, right: b, sign: -1)));

        edits.Send(
            TestUtil.Add(
                TestUtil.Item(number: 1, name: "a", score: 5),
                TestUtil.Item(number: 2, name: "b", score: 50),
                TestUtil.Item(number: 3, name: "c", score: 60)));

        await Assert.That(Describe(Transaction.Run(byBucket.Sample))).IsEqualTo("high=2,low=1");

        // An update that moves a key from one bucket to the other.
        edits.Send(TestUtil.Score(key: 3, score: 1));
        await Assert.That(Describe(Transaction.Run(byBucket.Sample))).IsEqualTo("high=1,low=2");

        edits.Send(TestUtil.Remove(1));
        await Assert.That(Describe(Transaction.Run(byBucket.Sample))).IsEqualTo("high=1,low=1");

        // Each item leaves, thus each counter must go to zero and go away.
        edits.Send(TestUtil.Remove(2, 3));
        await Assert.That(Describe(Transaction.Run(byBucket.Sample))).IsEqualTo(string.Empty);
    }

    [Test]
    public async Task AConditionalCountAndAFingerprint()
    {
        StreamSink<CollectionEdit<int, ItemIdentity, ItemState>> edits =
            Stream.CreateSink<CollectionEdit<int, ItemIdentity, ItemState>>();

        ReactiveCollection<int, ItemIdentity, ItemState> collection = Create(edits);

        Cell<int> high =
            Transaction.Run(() =>
                collection.Fold(
                    select: static state => state.Score >= 20 ? 1 : 0,
                    zero: 0,
                    add: static (a, b) => a + b,
                    subtract: static (a, b) => a - b));

        // Exclusive or is its own inverse, thus one function is the add and the subtract. This gives
        // a fingerprint of the states, for a test of equality and not for an order.
        Cell<int> fingerprint =
            Transaction.Run(() =>
                collection.Fold(
                    select: static state => state.Score * 31,
                    zero: 0,
                    add: static (a, b) => a ^ b,
                    subtract: static (a, b) => a ^ b));

        edits.Send(TestUtil.Add(TestUtil.Item(number: 1, name: "a", score: 5), TestUtil.Item(number: 2, name: "b", score: 50)));
        await Assert.That(Transaction.Run(high.Sample)).IsEqualTo(1);

        int before = Transaction.Run(fingerprint.Sample);

        edits.Send(TestUtil.Score(key: 1, score: 30));
        await Assert.That(Transaction.Run(high.Sample)).IsEqualTo(2).Because("the item crossed the line without a Filter stage");

        // The same set of states again, by the other order of the edits, gives the same fingerprint.
        edits.Send(TestUtil.Score(key: 1, score: 5));
        await Assert.That(Transaction.Run(fingerprint.Sample)).IsEqualTo(before).Because("a group of this shape does not read the order");
    }

    private static string Bucket(ItemState state) => state.Score >= 20 ? "high" : "low";

    private static ImmutableSortedDictionary<string, int> Combine(
        ImmutableSortedDictionary<string, int> left,
        ImmutableSortedDictionary<string, int> right,
        int sign)
    {
        ImmutableSortedDictionary<string, int> result = left;

        foreach (KeyValuePair<string, int> pair in right)
        {
            int count = (result.TryGetValue(key: pair.Key, value: out int current) ? current : 0) + sign * pair.Value;
            result = count == 0 ? result.Remove(pair.Key) : result.SetItem(key: pair.Key, value: count);
        }

        return result;
    }

    private static string Describe(ImmutableSortedDictionary<string, int> value) =>
        string.Join(separator: ",", values: value.Select(static pair => pair.Key + "=" + pair.Value));

    private static double Direct(
        ReactiveCollection<int, ItemIdentity, ItemState> collection,
        Func<IEnumerable<int>, double> f) =>
        f(Transaction.Run(collection.SnapshotCell.Sample).States.Pairs.Select(static pair => pair.Value.Score));

    private static ReactiveCollection<int, ItemIdentity, ItemState> Create(
        Stream<CollectionEdit<int, ItemIdentity, ItemState>> edits) =>
        ReactiveCollection<int, ItemIdentity, ItemState>.Create(
            keySelector: TestUtil.KeyOf,
            initialItems: [],
            edits);
}
