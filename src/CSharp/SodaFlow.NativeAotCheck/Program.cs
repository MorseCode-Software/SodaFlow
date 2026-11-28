using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SodaFlow.Async;
using SodaFlow.Bindable.ObjectModel;
using SodaFlow.Collections;
using SodaFlow.Functional;

namespace SodaFlow.NativeAotCheck;

/// <summary>
///     An app that the build publishes with native AOT and then runs. Its exit code
///     is the number of checks that failed.
/// </summary>
/// <remarks>
///     <para>
///         The native AOT compiler makes the code of each generic type before the
///         app runs. Where a generic type nests in itself with no limit that the
///         compiler can prove, the compiler stops at a depth and reports IL3054. The
///         code that it did not make throws when it runs. The analyzers in the build
///         of a library cannot see this. The publish of this app treats each warning
///         as an error, thus a report from code that an app reaches stops the build.
///     </para>
///     <para>
///         The run checks the results. A sort with many levels is here because each
///         level made a deeper generic type before. The stream and cell operations
///         are here because the compiler named them when it made each method of
///         the library.
///     </para>
///     <para>
///         SodaFlow.Async and SodaFlow.Bindable.ObjectModel also have the mark for
///         native AOT. Thus, a MapAsync pipeline runs here with each strategy of the
///         library, and so do the bindable values and a bindable action. Each
///         operation awaits before it ends, thus the pipeline gets its result on a
///         different thread.
///     </para>
/// </remarks>
internal static class Program
{
    private const int ItemCount = 24;

    private static readonly Func<Id, State, int> ByGroup = static (_, state) => state.Group;

    private static readonly Func<Id, State, string> ByName = static (_, state) => state.Name;

    private static readonly Func<Id, State, long> ByWeight = static (_, state) => state.Weight;

    // Each level after the first, with the same level for LINQ. LINQ is the reference for each
    // sequence, thus no check holds a sequence that a person wrote. The levels have different types,
    // most of them value types, and one level reads the identity only.
    private static readonly ImmutableArray<Level> NextLevels =
    [
        new(
            Then: static order =>
                order.ThenBy(selector: ByName, sortComparer: StringComparer.Ordinal, isDescending: false),
            ThenLinq: static items =>
                items.ThenBy(keySelector: static item => item.State.Name, comparer: StringComparer.Ordinal)),
        new(
            Then: static order => order.ThenByDescending(selector: ByWeight),
            ThenLinq: static items => items.ThenByDescending(static item => item.State.Weight)),
        new(
            Then: static order => order.ThenByIdentity(selector: static identity => identity.Stamp),
            ThenLinq: static items => items.ThenBy(static item => item.Identity.Stamp)),
        new(
            Then: static order => order.ThenBy(selector: static (_, state) => state.Ratio),
            ThenLinq: static items => items.ThenBy(static item => item.State.Ratio)),
        new(
            Then: static order => order.ThenByDescending(selector: static (_, state) => state.IsOpen),
            ThenLinq: static items => items.ThenByDescending(static item => item.State.IsOpen)),
        new(
            Then: static order => order.ThenBy(selector: static (_, state) => state.Tag),
            ThenLinq: static items => items.ThenBy(static item => item.State.Tag))
    ];

    private static int Main()
    {
        ImmutableArray<(string Name, Func<bool> Check)> checks =
        [
            (Name: "stream operations", Check: StreamOperations),
            (Name: "cell operations", Check: CellOperations),
            .. Enumerable.Range(start: 1, count: NextLevels.Length + 1).Select(selector: SortCheck),
            (Name: "a state edit and then the reverse of a sort with three levels", Check: EditAndReverse),
            (Name: "MapAsync with the Parallel strategy", Check: MapAsyncParallel),
            (Name: "MapAsync with the Queue strategy", Check: MapAsyncQueue),
            (Name: "MapAsync with the QueuePerGroup strategy", Check: MapAsyncQueuePerGroup),
            (Name: "MapAsync with the SwitchLatest strategy", Check: MapAsyncSwitchLatest),
            (Name: "bindable values and a bindable action", Check: Bindables)
        ];

        return checks.Count(static check => !Passes(name: check.Name, check: check.Check));
    }

    private static bool Passes(string name, Func<bool> check)
    {
        try
        {
            bool isCorrect = check();

            Console.WriteLine($"{(isCorrect ? "ok  " : "FAIL")} {name}");

            return isCorrect;
        }
        catch (Exception exception)
        {
            // The code of a stopped generic expansion throws, and the message names the type.
            Console.WriteLine($"FAIL {name}: {exception}");

            return false;
        }
    }

    private static bool StreamOperations()
    {
        StreamSink<int> numbers = Stream.CreateSink<int>();
        StreamSink<int> others = Stream.CreateSink<int>();
        CellSink<bool> gate = Cell.CreateSink(initialValue: true);
        List<string> seen = [];

        IListener listener =
            Transaction.Run(
                () =>
                {
                    Cell<int> total = numbers.Accum(initialState: 0, f: static (number, sum) => sum + number);

                    Stream<string> gated =
                        numbers.Gate(c: gate).Calm().Snapshot(c: total, f: static (number, sum) => $"{number}/{sum}");

                    Stream<string> counted =
                        others.Collect(
                            initialState: 0,
                            f: static (number, count) => (ReturnValue: $"{number}#{count}", State: count + 1));

                    return gated.OrElse(s2: counted).Listen(handler: seen.Add);
                });

        numbers.Send(a: 1);
        numbers.Send(a: 1);
        gate.Send(a: false);
        numbers.Send(a: 2);
        gate.Send(a: true);
        numbers.Send(a: 3);
        others.Send(a: 7);
        others.Send(a: 7);
        listener.Dispose();

        // A snapshot reads the total from before its transaction. Calm drops the second 1, and the
        // gate drops the 2.
        return seen.SequenceEqual(second: ["1/0", "3/4", "7#0", "7#1"]);
    }

    private static bool CellOperations()
    {
        StreamSink<string> words = Stream.CreateSink<string>();
        StreamSink<string> moreWords = Stream.CreateSink<string>();
        CellSink<int> offset = Cell.CreateSink(initialValue: 10);
        List<int> seen = [];

        IListener listener =
            Transaction.Run(
                () =>
                {
                    Cell<string> last =
                        words.Merge(s2: moreWords, f: static (first, second) => first + second)
                            .Hold(initialValue: "none");

                    Cell<int> length = last.Lift(c2: offset, f: static (word, added) => word.Length + added).Calm();

                    return length.Updates().Listen(handler: seen.Add);
                });

        words.Send(a: "ab");
        moreWords.Send(a: "cd");
        offset.Send(a: 20);
        words.Send(a: "abc");
        listener.Dispose();

        // The word "cd" is as long as "ab", thus Calm drops that update.
        return seen.SequenceEqual(second: [12, 22, 23]);
    }

    private static (string Name, Func<bool> Check) SortCheck(int levelCount) =>
        (Name: $"a sort with {levelCount} levels", Check: () => SortWithLevels(levelCount: levelCount));

    private static bool SortWithLevels(int levelCount)
    {
        ImmutableArray<Item<Id, State>> items = Items();

        ReactiveCollection<int, Id, State> collection =
            ReactiveCollection<int, Id, State>.Create(
                keySelector: static identity => identity.Number,
                initialItems: items,
                Stream.CreateSink<CollectionEdit<int, Id, State>>());

        KeyOrder<int, Id, State> order =
            NextLevels.Take(count: levelCount - 1)
                .Aggregate(
                    seed: KeyOrder<int, Id, State>.By(selector: ByGroup),
                    func: static (levels, level) => level.Then(arg: levels));

        IEnumerable<int> reference =
            NextLevels.Take(count: levelCount - 1)
                .Aggregate(
                    seed: items.OrderBy(static item => item.State.Group),
                    func: static (sorted, level) => level.ThenLinq(arg: sorted))
                .ThenBy(static item => item.Identity.Number)
                .Select(static item => item.Identity.Number);

        return collection.SortBy(order: order).KeysCell.Sample().SequenceEqual(second: reference);
    }

    private static bool EditAndReverse()
    {
        const int editedKey = 5;

        ImmutableArray<Item<Id, State>> items = Items();
        StreamSink<CollectionEdit<int, Id, State>> edits = Stream.CreateSink<CollectionEdit<int, Id, State>>();
        CellSink<KeyOrder<int, Id, State>> order = Cell.CreateSink(initialValue: ThreeLevels(isReversed: false));

        ReactiveCollection<int, Id, State> sorted =
            Transaction.Run(
                () =>
                    ReactiveCollection<int, Id, State>.Create(
                            keySelector: static identity => identity.Number,
                            initialItems: items,
                            edits)
                        .SortBy(orderCell: order));

        edits.Send(a: CollectionEdit<int, Id, State>.Update(key: editedKey, transform: Edit));

        ImmutableArray<Item<Id, State>> edited =
        [
            .. items.Select(
                static item =>
                    item.Identity.Number == editedKey
                        ? new Item<Id, State>(identity: item.Identity, state: Edit(state: item.State))
                        : item)
        ];

        bool isEditCorrect =
            sorted.KeysCell.Sample().SequenceEqual(second: SortedThreeLevels(items: edited, isReversed: false));

        // Each level in the opposite direction, from the same selectors and comparers, is the reverse of
        // the order. The stage then reads its set from the end and does not sort again.
        order.Send(a: ThreeLevels(isReversed: true));

        return isEditCorrect
               && sorted.KeysCell.Sample().SequenceEqual(second: SortedThreeLevels(items: edited, isReversed: true));

        static State Edit(State state) => state with { Group = state.Group + 1, Weight = -state.Weight };
    }

    // A strategy of the library that reads no input gets the unit value for each input.
    private static Unit ToUnit(string value) => Unit.Value;

    // Each operation yields before it ends. Thus, a thread of the pool ends each operation and
    // publishes its result, as in an application.
    private static async Task<MapAsyncResult<string>> Upper(
        string value,
        ResultFactory<string> factory,
        CancellationToken token)
    {
        await Task.Yield();

        return factory.FromValue(value.ToUpperInvariant());
    }

    // Parallel publishes in the order that the operations end, thus the check sorts the results.
    private static bool MapAsyncParallel() =>
        RunMapAsync(
                strategy: AsyncConcurrencyStrategy.Parallel(),
                inputConverter: ToUnit,
                operation: Upper,
                inputs: ["a", "b", "c"])
            .Order(comparer: StringComparer.Ordinal)
            .SequenceEqual(second: ["A", "B", "C"]);

    // Queue runs one item at a time, in the order of the sends.
    private static bool MapAsyncQueue() =>
        RunMapAsync(
                strategy: AsyncConcurrencyStrategy.Queue(),
                inputConverter: ToUnit,
                operation: Upper,
                inputs: ["a", "b", "c"])
            .SequenceEqual(second: ["A", "B", "C"]);

    // One queue for each first letter. The two groups do not wait for each other, thus only the
    // sequence in one group is certain.
    private static bool MapAsyncQueuePerGroup()
    {
        AsyncConcurrencyStrategyBase<string> strategy =
            AsyncConcurrencyStrategy.QueuePerGroup<string>()
                .Create(getGroup: static value => value[0], groupComparer: EqualityComparer<char>.Default);

        IReadOnlyList<string> results =
            RunMapAsync(
                strategy: strategy,
                inputConverter: static value => value,
                operation: Upper,
                inputs: ["a1", "b1", "a2", "b2"]);

        return results.Where(static result => result[0] == 'A').SequenceEqual(second: ["A1", "A2"])
               && results.Where(static result => result[0] == 'B').SequenceEqual(second: ["B1", "B2"]);
    }

    // The operation of the first value waits until its token cancels it. The second value replaces
    // it, thus the pipeline publishes only the second result.
    private static bool MapAsyncSwitchLatest() =>
        RunMapAsync(
                strategy: AsyncConcurrencyStrategy.SwitchLatest(),
                inputConverter: ToUnit,
                operation: static async (value, factory, token) =>
                {
                    if (value == "first")
                    {
                        await Task.Delay(delay: Timeout.InfiniteTimeSpan, cancellationToken: token)
                            .ConfigureAwait(false);
                    }

                    return await Upper(value: value, factory: factory, token: token).ConfigureAwait(false);
                },
                inputs: ["first", "second"])
            .SequenceEqual(second: ["SECOND"]);

    // Sends each input, and waits until the pipeline tracks no item. A result comes from a thread of
    // the pool, thus the results go to a queue that more than one thread can write.
    private static IReadOnlyList<string> RunMapAsync<TStrategyInput>(
        AsyncConcurrencyStrategyBase<TStrategyInput> strategy,
        Func<string, TStrategyInput> inputConverter,
        MapAsyncOperation<string, string> operation,
        IEnumerable<string> inputs)
    {
        StreamSink<string> source = Stream.CreateSink<string>();
        StreamSink<string> results = Stream.CreateSink<string>();
        StreamSink<Exception> errors = Stream.CreateSink<Exception>();
        ConcurrentQueue<string> received = new();
        ConcurrentQueue<Exception> failures = new();
        IListener resultListener = results.Listen(handler: received.Enqueue);
        IListener errorListener = errors.Listen(handler: failures.Enqueue);

        AsyncMapStatus<string> status =
            source.MapAsync(
                results: results,
                errors: errors,
                handleException: failures.Enqueue,
                operation: operation,
                strategy: strategy,
                inputConverter: inputConverter);

        Cell<IReadOnlyList<AsyncItem<string>>> items = status.Items;

        foreach (string input in inputs)
        {
            source.Send(a: input);
        }

        bool ended = SpinWait.SpinUntil(
            condition: () => !Transaction.Run(items.Sample).Any(),
            timeout: TimeSpan.FromSeconds(30));

        status.Dispose();
        resultListener.Dispose();
        errorListener.Dispose();

        return ended && failures.IsEmpty ? [.. received] : ["the pipeline did not end, or it failed"];
    }

    // The immediate scheduler applies each change on the thread that makes it, thus each read here
    // sees the change before it.
    private static bool Bindables()
    {
        CellSink<int> number = Cell.CreateSink(initialValue: 1);
        CellSink<bool> isEnabled = Cell.CreateSink(initialValue: false);
        StreamSink<int> firings = Stream.CreateSink<int>();
        List<int> fired = [];
        BindableFactory factory = new(bindingScheduler: BindingScheduler.Immediate);
        IListener listener = firings.Listen(handler: fired.Add);

        using IOneWayBindableValue<int> oneWay = factory.CreateOneWay(cell: number);
        using ITwoWayBindableValue<int> twoWay = factory.CreateTwoWay(sink: number);

        using IBindableAction<int> action =
            factory.CreateBindableAction(firingsStreamSink: firings, isEnabledCell: isEnabled);

        twoWay.Value = 3;
        bool wasEnabled = action.CanExecute(parameter: 4);
        isEnabled.Send(a: true);
        action.Execute(parameter: 5);
        listener.Dispose();

        return oneWay.Value == 3
               && number.Sample() == 3
               && !wasEnabled
               && action.CanExecute(parameter: 6)
               && fired.SequenceEqual(second: [5]);
    }

    private static KeyOrder<int, Id, State> ThreeLevels(bool isReversed) =>
        KeyOrder<int, Id, State>.By(
                selector: ByGroup,
                sortComparer: Comparer<int>.Default,
                keyComparer: Comparer<int>.Default,
                isDescending: isReversed)
            .ThenBy(selector: ByName, sortComparer: StringComparer.Ordinal, isDescending: isReversed)
            .ThenBy(selector: ByWeight, sortComparer: Comparer<long>.Default, isDescending: !isReversed);

    // The key is the last level, in the ascending direction for each direction of the levels.
    private static IEnumerable<int> SortedThreeLevels(ImmutableArray<Item<Id, State>> items, bool isReversed) =>
        (isReversed
            ? items.OrderByDescending(static item => item.State.Group)
                .ThenByDescending(keySelector: static item => item.State.Name, comparer: StringComparer.Ordinal)
                .ThenBy(static item => item.State.Weight)
            : items.OrderBy(static item => item.State.Group)
                .ThenBy(keySelector: static item => item.State.Name, comparer: StringComparer.Ordinal)
                .ThenByDescending(static item => item.State.Weight))
        .ThenBy(static item => item.Identity.Number)
        .Select(static item => item.Identity.Number);

    // Each value repeats with a different period, thus each level decides between some keys that the
    // levels before it rank equal.
    private static ImmutableArray<Item<Id, State>> Items() =>
    [
        .. Enumerable.Range(start: 0, count: ItemCount)
            .Select(
                static number =>
                    new Item<Id, State>(
                        identity: new Id(
                            Number: number,
                            Stamp: new DateTime(year: 2026, month: 1, day: 1 + number % 2)),
                        state: new State(
                            Group: number % 2,
                            Name: (number % 3) switch { 0 => "a", 1 => "b", _ => "c" },
                            Weight: number % 4,
                            Ratio: number % 5 / 2.0,
                            IsOpen: number % 7 == 0,
                            Tag: new Guid(a: number % 11, b: 0, c: 0, d: new byte[8]))))
    ];

    private sealed record Level(
        Func<KeyOrder<int, Id, State>, KeyOrder<int, Id, State>> Then,
        Func<IOrderedEnumerable<Item<Id, State>>, IOrderedEnumerable<Item<Id, State>>> ThenLinq);

    private sealed record Id(int Number, DateTime Stamp);

    private sealed record State(int Group, string Name, long Weight, double Ratio, bool IsOpen, Guid Tag);
}
