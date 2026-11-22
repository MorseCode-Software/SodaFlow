using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using SodaFlow.Functional;
using TUnit.Assertions;
using TUnit.Assertions.Enums;
using TUnit.Assertions.Extensions;
using TUnit.Core;

namespace SodaFlow.Collections.Tests;

/// <summary>
///     A change goes only to the per-key cells of the keys that it names. These
///     tests hold the per-key cells to the values and the firings that a filter on
///     the full change stream gave.
/// </summary>
public sealed class PerKeyRoutingTests
{
    private const int KeyCount = 16;

    private static ReactiveCollection<int, ItemIdentity, ItemState> Create(
        Stream<CollectionEdit<int, ItemIdentity, ItemState>> edits,
        params Item<ItemIdentity, ItemState>[] initial) =>
        ReactiveCollection<int, ItemIdentity, ItemState>.Create(
            keySelector: TestUtil.KeyOf,
            initialItems: initial,
            edits);

    private static string NameOf(Maybe<ItemState> state) =>
        state.Match(onSome: static s => s.Name + ":" + s.Score, onNone: static () => "none");

    /// <summary>
    ///     A cell that a transaction makes after the change of its key gets that
    ///     change. Here the router exists before the change and sends it. Thus, the
    ///     new cell gets the change again from the firings of the change stream.
    /// </summary>
    [Test]
    public async Task ACellMadeAfterTheRouterSentTheChangeGetsTheChange()
    {
        StreamSink<CollectionEdit<int, ItemIdentity, ItemState>> edits =
            Stream.CreateSink<CollectionEdit<int, ItemIdentity, ItemState>>();

        ReactiveCollection<int, ItemIdentity, ItemState> collection =
            Create(
                edits: edits,
                TestUtil.Item(number: 1, name: "one", score: 10),
                TestUtil.Item(number: 2, name: "two", score: 20));

        // The cell of key 1 makes the router. Its updates come after the router sent the change.
        Cell<Maybe<ItemState>> one = collection.StateCell(1);
        List<string> seen = [];
        Cell<Maybe<ItemState>>? two = null;
        IListener? inner = null;

        IListener outer =
            one.Updates()
                .Listen(_ =>
                {
                    if (two is not null)
                    {
                        return;
                    }

                    two = collection.StateCell(2);
                    inner = two.Updates().Listen(state => seen.Add(NameOf(state)));
                });

        edits.Send(TestUtil.Score(key: 1, score: 11).CombineWith(TestUtil.Rename(key: 2, name: "two again")));
        edits.Send(TestUtil.Rename(key: 2, name: "two later"));

        outer.Dispose();
        inner?.Dispose();

        await Assert.That(seen)
            .IsEquivalentTo(expected: ["two again:20", "two later:20"], ordering: CollectionOrdering.Matching);
    }

    /// <summary>
    ///     The same, when the new cell is the first per-key cell of the collection.
    ///     Thus, the transaction makes the router after the change.
    /// </summary>
    [Test]
    public async Task TheFirstCellMadeAfterTheChangeGetsTheChange()
    {
        StreamSink<CollectionEdit<int, ItemIdentity, ItemState>> edits =
            Stream.CreateSink<CollectionEdit<int, ItemIdentity, ItemState>>();

        ReactiveCollection<int, ItemIdentity, ItemState> collection =
            Create(edits: edits, TestUtil.Item(number: 2, name: "two", score: 20));

        List<string> seen = [];
        Cell<Maybe<ItemState>>? two = null;
        IListener? inner = null;

        IListener outer =
            collection.ItemChangesStream.Listen(_ =>
            {
                if (two is not null)
                {
                    return;
                }

                two = collection.StateCell(2);
                inner = two.Updates().Listen(state => seen.Add(NameOf(state)));
            });

        edits.Send(TestUtil.Rename(key: 2, name: "two again"));
        edits.Send(TestUtil.Rename(key: 2, name: "two later"));

        outer.Dispose();
        inner?.Dispose();

        await Assert.That(seen)
            .IsEquivalentTo(expected: ["two again:20", "two later:20"], ordering: CollectionOrdering.Matching);
    }

    /// <summary>
    ///     An add and an update of the same key in one edit name that key two times.
    ///     The cell of the key fires one time, with the value after the two edits.
    /// </summary>
    [Test]
    public async Task AKeyThatAChangeNamesTwoTimesFiresOneTime()
    {
        StreamSink<CollectionEdit<int, ItemIdentity, ItemState>> edits =
            Stream.CreateSink<CollectionEdit<int, ItemIdentity, ItemState>>();

        ReactiveCollection<int, ItemIdentity, ItemState> collection =
            Create(edits: edits, TestUtil.Item(number: 1, name: "one", score: 10));

        List<string> seen = [];
        IListener l = collection.StateCell(3).Updates().Listen(state => seen.Add(NameOf(state)));

        edits.Send(TestUtil.Add(TestUtil.Item(number: 3, name: "three", score: 30)).CombineWith(TestUtil.Score(key: 3, score: 33)));

        l.Dispose();

        await Assert.That(seen).IsEquivalentTo(expected: ["three:33"], ordering: CollectionOrdering.Matching);
    }

    /// <summary>
    ///     Random edits, each checked against a filter on the full change stream,
    ///     which is how a per-key cell was made before. The state cell of each key
    ///     must fire the same values in the same transactions as the filter.
    /// </summary>
    [Test]
    [Arguments(1)]
    [Arguments(2)]
    [Arguments(3)]
    public async Task StateCellsOfTheCollectionFireAsAFilterOnTheChangeStream(int seed)
    {
        Random random = new(seed);

        StreamSink<CollectionEdit<int, ItemIdentity, ItemState>> edits =
            Stream.CreateSink<CollectionEdit<int, ItemIdentity, ItemState>>();

        ReactiveCollection<int, ItemIdentity, ItemState> collection =
            Create(
                edits: edits,
                initial: [.. Enumerable.Range(start: 0, count: KeyCount / 2).Select(static n => TestUtil.Item(number: n * 2, name: "n" + n, score: n))]);

        List<string> routed = [];
        List<string> filtered = [];
        List<IListener> listeners = [];

        // A box, thus each listener reads the step of the transaction that runs it.
        StrongBox<int> step = new(0);

        for (int key = 0; key < KeyCount; key++)
        {
            int k = key;
            Cell<Maybe<ItemState>> cell = collection.StateCell(k);

            Cell<Maybe<ItemState>> filter =
                collection.ItemChangesStream
                    .Filter(change => change.WasChanged(k))
                    .Map(change => change.TryGetNewState(key: k, state: out ItemState? state) ? Maybe.Some(state) : Maybe<ItemState>.None)
                    .Hold(cell.Sample());

            listeners.Add(cell.Updates().Listen(state => routed.Add(step.Value + " " + k + " " + NameOf(state))));
            listeners.Add(filter.Updates().Listen(state => filtered.Add(step.Value + " " + k + " " + NameOf(state))));
        }

        HashSet<int> present = [.. Enumerable.Range(start: 0, count: KeyCount / 2).Select(static n => n * 2)];

        for (step.Value = 1; step.Value <= 200; step.Value++)
        {
            edits.Send(RandomEdit(random: random, present: present));
        }

        listeners.ForEach(static l => l.Dispose());

        // The cells of different keys fire at the same instant, and SodaFlow defines no sequence
        // between them. The filters fired in the sequence of their construction, and the router fires in the
        // sequence of the keys in the change. Thus, the two lists hold the same entries in a different
        // sequence. Each entry starts with its transaction, thus an entry in a different transaction is not in the other list.
        await Assert.That(routed.Count).IsGreaterThan(100);
        await Assert.That(routed).IsEquivalentTo(expected: filtered, ordering: CollectionOrdering.Any);
    }

    /// <summary>
    ///     Random edits and moves of the criteria, the sort, and the page, each
    ///     checked against the keys of each view. After each transaction, each cell
    ///     of each key has the value of that key in the view. No cell fires two or
    ///     more times in a transaction.
    /// </summary>
    [Test]
    [Arguments(1)]
    [Arguments(2)]
    [Arguments(3)]
    public async Task PerKeyCellsOfEachViewAgreeWithTheViewAfterEachTransaction(int seed)
    {
        Random random = new(seed);

        StreamSink<CollectionEdit<int, ItemIdentity, ItemState>> edits =
            Stream.CreateSink<CollectionEdit<int, ItemIdentity, ItemState>>();

        CellSink<bool> evenOnly = Cell.CreateSink(false);
        CellSink<int> offset = Cell.CreateSink(0);

        ReactiveCollection<int, ItemIdentity, ItemState> collection =
            Create(
                edits: edits,
                initial: [.. Enumerable.Range(start: 0, count: KeyCount / 2).Select(static n => TestUtil.Item(number: n * 2, name: "n" + n, score: n))]);

        ReactiveCollection<int, ItemIdentity, ItemState> filtered =
            collection.Filter(
                criteriaCell: evenOnly,
                predicate: static (even, identity, _) => !even || identity.Number % 2 == 0);

        ReactiveCollection<int, ItemIdentity, ItemState>[] views =
        [
            collection,
            filtered,
            collection.SortBy(static (_, state) => state.Score),
            filtered.SortBy(static (_, state) => state.Score),
            collection.SortByKey().Slice(offsetCell: offset, limitCell: Cell.Constant(5))
        ];

        List<(ReactiveCollection<int, ItemIdentity, ItemState> View, int Key, Cell<Maybe<ItemState>> State, Cell<Maybe<ItemIdentity>> Identity, Cell<Maybe<Item<ItemIdentity, ItemState>>> Item)> cells = [];
        List<IListener> listeners = [];
        HashSet<string> firedInStep = [];
        List<string> twice = [];

        // A box, thus each listener reads the step of the transaction that runs it.
        StrongBox<int> step = new(0);

        foreach (ReactiveCollection<int, ItemIdentity, ItemState> view in views)
        {
            for (int key = 0; key < KeyCount; key++)
            {
                int k = key;
                string name = Array.IndexOf(array: views, value: view) + "/" + k;
                Cell<Maybe<ItemState>> state = view.StateCell(k);
                Cell<Maybe<ItemIdentity>> identity = view.IdentityCell(k);
                Cell<Maybe<Item<ItemIdentity, ItemState>>> item = view.ItemCell(k);

                cells.Add((view, k, state, identity, item));

                // Half of the keys have listeners, and the other half are only held.
                if (k % 2 == 0)
                {
                    listeners.Add(state.Updates().Listen(_ => Fired(name + " state")));
                    listeners.Add(identity.Updates().Listen(_ => Fired(name + " identity")));
                    listeners.Add(item.Updates().Listen(_ => Fired(name + " item")));
                }
            }
        }

        HashSet<int> present = [.. Enumerable.Range(start: 0, count: KeyCount / 2).Select(static n => n * 2)];
        List<string> mismatches = [];

        for (step.Value = 1; step.Value <= 150; step.Value++)
        {
            int choice = random.Next(6);
            bool edit = choice <= 3;
            bool flip = choice is 3 or 4;
            bool page = choice is 2 or 5;

            Transaction.RunVoid(() =>
            {
                if (edit)
                {
                    edits.Send(RandomEdit(random: random, present: present));
                }

                if (flip)
                {
                    evenOnly.Send(!evenOnly.Sample());
                }

                if (page)
                {
                    offset.Send(random.Next(3) * 5);
                }
            });

            CollectionSnapshot<int, ItemIdentity, ItemState> root = collection.SnapshotCell.Sample();

            foreach ((ReactiveCollection<int, ItemIdentity, ItemState> view, int key, Cell<Maybe<ItemState>> state, Cell<Maybe<ItemIdentity>> identity, Cell<Maybe<Item<ItemIdentity, ItemState>>> item) in cells)
            {
                Item<ItemIdentity, ItemState>? expected =
                    view.KeysCell.Sample().Contains(key) && root.TryGetItem(key: key, item: out Item<ItemIdentity, ItemState>? found)
                        ? found
                        : null;

                string expectedState = expected is null ? "none" : expected.State.Name + ":" + expected.State.Score;
                string expectedIdentity = expected is null ? "none" : expected.Identity.Code;

                string actualState = NameOf(state.Sample());
                string actualIdentity = identity.Sample().Match(onSome: static i => i.Code, onNone: static () => "none");
                string actualItem = item.Sample().Match(onSome: static i => i.State.Name + ":" + i.State.Score, onNone: static () => "none");

                if (actualState != expectedState || actualIdentity != expectedIdentity || actualItem != expectedState)
                {
                    mismatches.Add($"step {step.Value} view {Array.IndexOf(array: views, value: view)} key {key}: {actualState} {actualIdentity} {actualItem}, expected {expectedState} {expectedIdentity}");
                }
            }

            firedInStep.Clear();
        }

        listeners.ForEach(static l => l.Dispose());

        await Assert.That(mismatches).IsEmpty();
        await Assert.That(twice).IsEmpty();

        return;

        void Fired(string what)
        {
            if (!firedInStep.Add(what))
            {
                twice.Add("step " + step.Value + ": " + what);
            }
        }
    }

    /// <summary>
    ///     Per-key cells that nothing holds, on many keys, then a collection, and then
    ///     more cells. The new cells make the router prune the dead routes. A cell
    ///     that something holds continues to get its changes.
    /// </summary>
    [Test]
    public async Task AHeldCellKeepsItsChangesAfterTheCellsAroundItAreCollected()
    {
        StreamSink<CollectionEdit<int, ItemIdentity, ItemState>> edits =
            Stream.CreateSink<CollectionEdit<int, ItemIdentity, ItemState>>();

        ReactiveCollection<int, ItemIdentity, ItemState> collection =
            Create(
                edits: edits,
                initial: [.. Enumerable.Range(start: 0, count: 300).Select(static n => TestUtil.Item(number: n, name: "n" + n, score: n))]);

        List<string> seen = [];
        IListener l = collection.StateCell(5).Updates().Listen(state => seen.Add(NameOf(state)));

        MakeCells(collection: collection, from: 100, count: 100);
        Collect();
        MakeCells(collection: collection, from: 200, count: 100);

        edits.Send(
            TestUtil.Rename(key: 5, name: "five")
                .CombineWith(TestUtil.Rename(key: 150, name: "gone"))
                .CombineWith(TestUtil.Rename(key: 250, name: "new")));

        edits.Send(TestUtil.Remove(5));

        l.Dispose();

        await Assert.That(seen).IsEquivalentTo(expected: ["five:5", "none"], ordering: CollectionOrdering.Matching);
        await Assert.That(NameOf(collection.StateCell(250).Sample())).IsEqualTo("new:250");
    }

    /// <summary>
    ///     A cell that nothing holds goes to the collector, while a cell of the same
    ///     collection keeps the router alive.
    /// </summary>
    [Test]
    public async Task APerKeyCellThatNothingHoldsIsCollectedWhileTheRouterLives()
    {
        StreamSink<CollectionEdit<int, ItemIdentity, ItemState>> edits =
            Stream.CreateSink<CollectionEdit<int, ItemIdentity, ItemState>>();

        ReactiveCollection<int, ItemIdentity, ItemState> collection =
            Create(
                edits: edits,
                TestUtil.Item(number: 1, name: "one", score: 10),
                TestUtil.Item(number: 2, name: "two", score: 20));

        Cell<Maybe<ItemState>> held = collection.StateCell(1);
        WeakReference dropped = MakeDroppedCell(collection: collection, key: 2);

        Collect();

        // A change of the key visits the dead route and removes it. The collector then removes the cell.
        edits.Send(TestUtil.Rename(key: 2, name: "two again"));

        Collect();

        await Assert.That(dropped.IsAlive).IsFalse();
        await Assert.That(NameOf(held.Sample())).IsEqualTo("one:10");
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference MakeDroppedCell(ReactiveCollection<int, ItemIdentity, ItemState> collection, int key) =>
        new(collection.StateCell(key));

    private static void Collect()
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
    }

    // Not inline, thus the JIT holds no reference to a cell after the return.
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void MakeCells(ReactiveCollection<int, ItemIdentity, ItemState> collection, int from, int count)
    {
        for (int key = from; key < from + count; key++)
        {
            _ = collection.StateCell(key).Sample();
        }
    }

    /// <summary>
    ///     One to three edits of keys in the range, combined in one edit, each to a
    ///     different key. Sometimes the edit adds a key and updates it again, which
    ///     names the key two times.
    /// </summary>
    // ReSharper disable once SuggestBaseTypeForParameter - The callers give a HashSet, and its calls cost less than calls through an interface.
    private static CollectionEdit<int, ItemIdentity, ItemState> RandomEdit(Random random, HashSet<int> present)
    {
        List<CollectionEdit<int, ItemIdentity, ItemState>> parts = [];
        HashSet<int> used = [];
        int count = 1 + random.Next(3);

        for (int i = 0; i < count; i++)
        {
            int key = random.Next(KeyCount);

            if (!used.Add(key))
            {
                continue;
            }

            CollectionEdit<int, ItemIdentity, ItemState> one;

            if (!present.Contains(key))
            {
                one = TestUtil.Add(TestUtil.Item(number: key, name: "k" + key, score: random.Next(100)));

                if (random.Next(3) == 0)
                {
                    one = one.CombineWith(TestUtil.Score(key: key, score: random.Next(100)));
                }

                present.Add(key);
            }
            else
            {
                switch (random.Next(3))
                {
                    case 0:
                        one = TestUtil.Remove(key);
                        present.Remove(key);

                        break;
                    case 1:
                        one = TestUtil.Score(key: key, score: random.Next(100));

                        break;
                    default:
                        one = TestUtil.Rename(key: key, name: "r" + random.Next(100));

                        break;
                }
            }

            parts.Add(one);
        }

        // The loop always makes the first edit, because the loop uses no key before it. Thus, the list is
        // not empty.
        return parts.Aggregate(static (first, second) => first.CombineWith(second));
    }
}
