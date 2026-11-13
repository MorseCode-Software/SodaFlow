using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using SodaFlow.Collections;

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
            (Name: "a state edit and then the reverse of a sort with three levels", Check: EditAndReverse)
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
        listener.Unlisten();

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
        listener.Unlisten();

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
