using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using TUnit.Assertions;
using TUnit.Assertions.Enums;
using TUnit.Assertions.Extensions;
using TUnit.Core;

namespace SodaFlow.Tests;

public class DenotationalSemanticsTests
{
    [Test]
    public async Task Test_Never_TestCase()
    {
        List<int> @out = RunSimulation<int>(Stream.Never<int>().ListenStrong);
        await Assert.That(@out).IsEquivalentTo(Array.Empty<int>(), CollectionOrdering.Matching);
    }

    [Test]
    public async Task Test_MapS_TestCase()
    {
        (Stream<int> s, Dictionary<int, Action> sf) =
            MkStream(new Dictionary<int, int> { { 0, 5 }, { 1, 10 }, { 2, 12 } });

        List<int> @out =
            RunSimulation<int>(
                listenStrong: s.Map(static x => x + 1).ListenStrong,
                firings: (IReadOnlyList<Dictionary<int, Action>>)[sf]);

        await Assert.That(@out).IsEquivalentTo(new[] { 6, 11, 13 }, CollectionOrdering.Matching);
    }

    [Test]
    public async Task Test_Snapshot_TestCase()
    {
        (Stream<char> s1, Dictionary<int, Action> s1F) =
            MkStream(new Dictionary<int, char> { { 0, 'a' }, { 3, 'b' }, { 5, 'c' } });

        (Stream<int> s2, Dictionary<int, Action> s2F) = MkStream(new Dictionary<int, int> { { 1, 4 }, { 5, 7 } });
        Cell<int> c = s2.Hold(3);

        List<int> @out =
            RunSimulation<int>(
                listenStrong: s1.Snapshot(c).ListenStrong,
                firings: (IReadOnlyList<Dictionary<int, Action>>)[s1F, s2F]);

        await Assert.That(@out).IsEquivalentTo(new[] { 3, 4, 4 }, CollectionOrdering.Matching);
    }

    [Test]
    public async Task Test_Merge_TestCase()
    {
        (Stream<int> s1, Dictionary<int, Action> s1F) = MkStream(new Dictionary<int, int> { { 0, 0 }, { 2, 2 } });

        (Stream<int> s2, Dictionary<int, Action> s2F) =
            MkStream(new Dictionary<int, int> { { 1, 10 }, { 2, 20 }, { 3, 30 } });

        List<int> @out =
            RunSimulation<int>(
                listenStrong: s1.Merge(s2: s2, f: static (x, y) => x + y).ListenStrong,
                firings: (IReadOnlyList<Dictionary<int, Action>>)[s1F, s2F]);

        await Assert.That(@out).IsEquivalentTo(new[] { 0, 10, 22, 30 }, CollectionOrdering.Matching);
    }

    [Test]
    public async Task Test_Filter_TestCase()
    {
        (Stream<int> s, Dictionary<int, Action> sf) =
            MkStream(new Dictionary<int, int> { { 0, 5 }, { 1, 6 }, { 2, 7 } });

        List<int> @out =
            RunSimulation<int>(
                listenStrong: s.Filter(static x => x % 2 != 0).ListenStrong,
                firings: (IReadOnlyList<Dictionary<int, Action>>)[sf]);

        await Assert.That(@out).IsEquivalentTo(new[] { 5, 7 }, CollectionOrdering.Matching);
    }

    [Test]
    public async Task Test_SwitchS_TestCase() =>
        RunPermutations<char>(
            createListAndListener: static createFiringsListAndListener =>
            {
                (Stream<char> s1, Dictionary<int, Action> s1F) =
                    MkStream(new Dictionary<int, char> { { 0, 'a' }, { 1, 'b' }, { 2, 'c' }, { 3, 'd' } });

                (Stream<char> s2, Dictionary<int, Action> s2F) =
                    MkStream(new Dictionary<int, char> { { 0, 'W' }, { 1, 'X' }, { 2, 'Y' }, { 3, 'Z' } });

                (Stream<Stream<char>> switcher, Dictionary<int, Action> switcherF) =
                    MkStream(new Dictionary<int, Stream<char>> { { 1, s2 } });

                Cell<Stream<char>> c = switcher.Hold(s1);

                IReadOnlyList<(string Name, Dictionary<int, Action> Firings)> firings =
                [
                    (Name: "s1", Firings: s1F), (Name: "s2", Firings: s2F), (Name: "switcher", Firings: switcherF)
                ];

                return createFiringsListAndListener(arg1: firings, arg2: c.SwitchS().ListenStrong);
            },
            assert: static @out => await Assert.That(@out).IsEquivalentTo(new[] { 'a', 'b', 'Y', 'Z' }, CollectionOrdering.Matching));

    [Test]
    public async Task Test_Updates_TestCase()
    {
        (Stream<char> s, Dictionary<int, Action> sf) =
            MkStream(new Dictionary<int, char> { { 1, 'b' }, { 3, 'c' } });

        Cell<char> c = s.Hold('a');

        List<char> @out =
            RunSimulation<char>(
                listenStrong: c.Updates().ListenStrong,
                firings: (IReadOnlyList<Dictionary<int, Action>>)[sf]);

        await Assert.That(@out).IsEquivalentTo(new[] { 'b', 'c' }, CollectionOrdering.Matching);
    }

    [Test]
    public async Task Test_Value_TestCase1()
    {
        (Stream<char> s, Dictionary<int, Action> sf) =
            MkStream(new Dictionary<int, char> { { 1, 'b' }, { 3, 'c' } });

        Cell<char> c = s.Hold('a');

        List<char> @out =
            RunSimulation<char>(
                listenStrong: h => Transaction.Run(() => c.Values().ListenStrong(h)),
                firings: (IReadOnlyList<Dictionary<int, Action>>)[sf]);

        await Assert.That(@out).IsEquivalentTo(new[] { 'a', 'b', 'c' }, CollectionOrdering.Matching);
    }

    [Test]
    public async Task Test_Value_TestCase2()
    {
        (Stream<char> s, Dictionary<int, Action> sf) =
            MkStream(new Dictionary<int, char> { { 0, 'b' }, { 1, 'c' }, { 3, 'd' } });

        Cell<char> c = s.Hold('a');

        List<char> @out =
            RunSimulation<char>(
                listenStrong: h => Transaction.Run(() => c.Values().ListenStrong(h)),
                firings: (IReadOnlyList<Dictionary<int, Action>>)[sf]);

        await Assert.That(@out).IsEquivalentTo(new[] { 'b', 'c', 'd' }, CollectionOrdering.Matching);
    }

    [Test]
    public async Task Test_ListenC_TestCase1()
    {
        (Stream<char> s, Dictionary<int, Action> sf) =
            MkStream(new Dictionary<int, char> { { 1, 'b' }, { 3, 'c' } });

        Cell<char> c = s.Hold('a');

        List<char> @out =
            RunSimulation<char>(listenStrong: c.ListenStrong, firings: (IReadOnlyList<Dictionary<int, Action>>)[sf]);

        await Assert.That(@out).IsEquivalentTo(new[] { 'a', 'b', 'c' }, CollectionOrdering.Matching);
    }

    [Test]
    public async Task Test_ListenC_TestCase2()
    {
        (Stream<char> s, Dictionary<int, Action> sf) =
            MkStream(new Dictionary<int, char> { { 0, 'b' }, { 1, 'c' }, { 3, 'd' } });

        Cell<char> c = s.Hold('a');

        List<char> @out =
            RunSimulation<char>(listenStrong: c.ListenStrong, firings: (IReadOnlyList<Dictionary<int, Action>>)[sf]);

        await Assert.That(@out).IsEquivalentTo(new[] { 'b', 'c', 'd' }, CollectionOrdering.Matching);
    }

    [Test]
    public async Task Test_Split_TestCase()
    {
        (Stream<IReadOnlyList<char>> s, ILookup<int, Action> sf) =
            MkStream(
                firings: new (int Time, IReadOnlyList<char> Value)[]
                         {
                             (Time: 0, Value: ['a', 'b']), (Time: 1, Value: ['c']), (Time: 1, Value: ['d', 'e'])
                         },
                coalesce: static (x, y) => [.. x, .. y]);

        List<char> @out =
            RunSimulation<char>(
                listenStrong: Operational.Split<char, IReadOnlyList<char>>(s).ListenStrong,
                firings: [sf]);

        await Assert.That(@out).IsEquivalentTo(new[] { 'a', 'b', 'c', 'd', 'e' }, CollectionOrdering.Matching);
    }

    [Test]
    public async Task Test_Constant_TestCase()
    {
        Cell<char> c = Cell.Constant('a');
        List<char> @out = RunSimulation<char>(c.ListenStrong);
        await Assert.That(@out).IsEquivalentTo(new[] { 'a' }, CollectionOrdering.Matching);
    }

    [Test]
    public async Task Test_ConstantLazy_TestCase()
    {
        Cell<char> c = Cell.ConstantLazy(new Lazy<char>(static () => 'a'));
        List<char> @out = RunSimulation<char>(c.ListenStrong);
        await Assert.That(@out).IsEquivalentTo(new[] { 'a' }, CollectionOrdering.Matching);
    }

    [Test]
    public async Task Test_Hold_TestCase()
    {
        (Stream<char> s, Dictionary<int, Action> sf) =
            MkStream(new Dictionary<int, char> { { 1, 'b' }, { 3, 'c' } });

        Cell<char> c = s.Hold('a');

        List<char> @out =
            RunSimulation<char>(listenStrong: c.ListenStrong, firings: (IReadOnlyList<Dictionary<int, Action>>)[sf]);

        await Assert.That(@out).IsEquivalentTo(new[] { 'a', 'b', 'c' }, CollectionOrdering.Matching);
    }

    [Test]
    public async Task Test_MapC_TestCase()
    {
        (Stream<int> s, Dictionary<int, Action> sf) = MkStream(new Dictionary<int, int> { { 2, 3 }, { 3, 5 } });
        Cell<int> c = s.Hold(0);

        List<int> @out =
            RunSimulation<int>(
                listenStrong: c.Map(static x => x + 1).ListenStrong,
                firings: (IReadOnlyList<Dictionary<int, Action>>)[sf]);

        await Assert.That(@out).IsEquivalentTo(new[] { 1, 4, 6 }, CollectionOrdering.Matching);
    }

    [Test]
    public async Task Test_Apply_TestCase()
    {
        (Stream<int> s1, Dictionary<int, Action> s1F) =
            MkStream(new Dictionary<int, int> { { 1, 200 }, { 2, 300 }, { 4, 400 } });

        Cell<int> ca = s1.Hold(100);

        (Stream<Func<int, int>> s2, Dictionary<int, Action> s2F) =
            MkStream(new Dictionary<int, Func<int, int>> { { 1, static x => x + 5 }, { 3, static x => x + 6 } });

        Cell<Func<int, int>> cf = s2.Hold(static x => x + 0);

        List<int> @out =
            RunSimulation<int>(
                listenStrong: ca.Apply(cf).ListenStrong,
                firings: (IReadOnlyList<Dictionary<int, Action>>)[s1F, s2F]);

        await Assert.That(@out).IsEquivalentTo(new[] { 100, 205, 305, 306, 406 }, CollectionOrdering.Matching);
    }

    [Test]
    public async Task Test_SwitchC_TestCase1() =>
        RunPermutations<char>(
            createListAndListener: static createFiringsListAndListener =>
            {
                (Stream<char> s1, Dictionary<int, Action> s1F) =
                    MkStream(new Dictionary<int, char> { { 0, 'b' }, { 1, 'c' }, { 2, 'd' }, { 3, 'e' } });

                Cell<char> c1 = s1.Hold('a');

                (Stream<char> s2, Dictionary<int, Action> s2F) =
                    MkStream(new Dictionary<int, char> { { 0, 'W' }, { 1, 'X' }, { 2, 'Y' }, { 3, 'Z' } });

                Cell<char> c2 = s2.Hold('V');

                (Stream<Cell<char>> switcher, Dictionary<int, Action> switcherF) =
                    MkStream(new Dictionary<int, Cell<char>> { { 1, c2 } });

                Cell<Cell<char>> c = switcher.Hold(c1);

                IReadOnlyList<(string Name, Dictionary<int, Action> Firings)> firings =
                [
                    (Name: "s1", Firings: s1F), (Name: "s2", Firings: s2F), (Name: "switcher", Firings: switcherF)
                ];

                return createFiringsListAndListener(arg1: firings, arg2: c.SwitchC().ListenStrong);
            },
            assert: static @out => await Assert.That(@out).IsEquivalentTo(new[] { 'b', 'X', 'Y', 'Z' }, CollectionOrdering.Matching));

    [Test]
    public async Task Test_SwitchC_TestCase2() =>
        RunPermutations<char>(
            createListAndListener: static createFiringsListAndListener =>
            {
                (Stream<char> s1, Dictionary<int, Action> s1F) =
                    MkStream(new Dictionary<int, char> { { 0, 'b' }, { 1, 'c' }, { 2, 'd' }, { 3, 'e' } });

                Cell<char> c1 = s1.Hold('a');

                (Stream<char> s2, Dictionary<int, Action> s2F) =
                    MkStream(new Dictionary<int, char> { { 1, 'X' }, { 2, 'Y' }, { 3, 'Z' } });

                Cell<char> c2 = s2.Hold('W');

                (Stream<Cell<char>> switcher, Dictionary<int, Action> switcherF) =
                    MkStream(new Dictionary<int, Cell<char>> { { 1, c2 } });

                Cell<Cell<char>> c = switcher.Hold(c1);

                IReadOnlyList<(string Name, Dictionary<int, Action> Firings)> firings =
                [
                    (Name: "s1", Firings: s1F), (Name: "s2", Firings: s2F), (Name: "switcher", Firings: switcherF)
                ];

                return createFiringsListAndListener(arg1: firings, arg2: c.SwitchC().ListenStrong);
            },
            assert: static @out => await Assert.That(@out).IsEquivalentTo(new[] { 'b', 'X', 'Y', 'Z' }, CollectionOrdering.Matching));

    [Test]
    public async Task Test_SwitchC_TestCase3() =>
        RunPermutations<char>(
            createListAndListener: static createFiringsListAndListener =>
            {
                (Stream<char> s1, Dictionary<int, Action> s1F) =
                    MkStream(new Dictionary<int, char> { { 0, 'b' }, { 1, 'c' }, { 2, 'd' }, { 3, 'e' } });

                Cell<char> c1 = s1.Hold('a');

                (Stream<char> s2, Dictionary<int, Action> s2F) =
                    MkStream(new Dictionary<int, char> { { 2, 'Y' }, { 3, 'Z' } });

                Cell<char> c2 = s2.Hold('X');

                (Stream<Cell<char>> switcher, Dictionary<int, Action> switcherF) =
                    MkStream(new Dictionary<int, Cell<char>> { { 1, c2 } });

                Cell<Cell<char>> c = switcher.Hold(c1);

                IReadOnlyList<(string Name, Dictionary<int, Action> Firings)> firings =
                [
                    (Name: "s1", Firings: s1F), (Name: "s2", Firings: s2F), (Name: "switcher", Firings: switcherF)
                ];

                return createFiringsListAndListener(arg1: firings, arg2: c.SwitchC().ListenStrong);
            },
            assert: static @out => await Assert.That(@out).IsEquivalentTo(new[] { 'b', 'X', 'Y', 'Z' }, CollectionOrdering.Matching));

    [Test]
    public async Task Test_SwitchC_TestCase4() =>
        RunPermutations<char>(
            createListAndListener: static createFiringsListAndListener =>
            {
                (Stream<char> s1, Dictionary<int, Action> s1F) =
                    MkStream(new Dictionary<int, char> { { 0, 'b' }, { 1, 'c' }, { 2, 'd' }, { 3, 'e' } });

                Cell<char> c1 = s1.Hold('a');

                (Stream<char> s2, Dictionary<int, Action> s2F) =
                    MkStream(new Dictionary<int, char> { { 0, 'W' }, { 1, 'X' }, { 2, 'Y' }, { 3, 'Z' } });

                Cell<char> c2 = s2.Hold('V');

                (Stream<char> s3, Dictionary<int, Action> s3F) =
                    MkStream(new Dictionary<int, char> { { 0, '2' }, { 1, '3' }, { 2, '4' }, { 3, '5' } });

                Cell<char> c3 = s3.Hold('1');

                (Stream<Cell<char>> switcher, Dictionary<int, Action> switcherF) =
                    MkStream(new Dictionary<int, Cell<char>> { { 1, c2 }, { 3, c3 } });

                Cell<Cell<char>> c = switcher.Hold(c1);

                IReadOnlyList<(string Name, Dictionary<int, Action> Firings)> firings =
                [
                    (Name: "s1", Firings: s1F),
                    (Name: "s2", Firings: s2F),
                    (Name: "s3", Firings: s3F),
                    (Name: "switcher", Firings: switcherF)
                ];

                return createFiringsListAndListener(arg1: firings, arg2: c.SwitchC().ListenStrong);
            },
            assert: static @out => await Assert.That(@out).IsEquivalentTo(new[] { 'b', 'X', 'Y', '5' }, CollectionOrdering.Matching));

    [Test]
    public async Task Test_Sample_TestCase()
    {
        StreamSink<char> s = Stream.CreateSink<char>();
        Cell<char> c = s.Hold('a');
        char sample1 = c.Sample();
        s.Send('b');
        char sample2 = c.Sample();
        await Assert.That(sample1).IsEqualTo('a');
        await Assert.That(sample2).IsEqualTo('b');
    }

    [Test]
    public async Task Test_SampleLazy_TestCase()
    {
        StreamSink<char> s = Stream.CreateSink<char>();
        Cell<char> c = s.Hold('a');
        Lazy<char> sample1 = c.SampleLazy();
        s.Send('b');
        Lazy<char> sample2 = c.SampleLazy();
        await Assert.That(sample1.Value).IsEqualTo('a');
        await Assert.That(sample2.Value).IsEqualTo('b');
    }

    private static (Stream<T> Stream, Dictionary<int, Action> Firings) MkStream<T>(Dictionary<int, T> firings)
    {
        StreamSink<T> s = Stream.CreateSink<T>();

        Dictionary<int, Action> f =
            firings.ToDictionary(
                keySelector: static firing => firing.Key,
                elementSelector: firing => (Action)(() => s.Send(firing.Value)));

        if (f.Keys.Any(static k => k < 0))
        {
            throw new InvalidOperationException("All firings must occur at T >= 0.");
        }

        Stream<T> returnStream = s;
        return (Stream: returnStream, Firings: f);
    }

    private static (Stream<T> Stream, ILookup<int, Action> Firings) MkStream<T>(
        IEnumerable<(int Time, T Value)> firings,
        Func<T, T, T> coalesce)
    {
        StreamSink<T> s = Stream.CreateSink(coalesce);

        ILookup<int, Action> f =
            firings.ToLookup(
                keySelector: static firing => firing.Time,
                elementSelector: firing => (Action)(() => s.Send(firing.Value)));

        if (f.Any(static g => g.Key < 0))
        {
            throw new InvalidOperationException("All firings must occur at T >= 0.");
        }

        Stream<T> returnStream = s;
        return (Stream: returnStream, Firings: f);
    }

    private static List<T> RunSimulation<T>(
        Func<Action<T>, IListener> listenStrong,
        IEnumerable<Dictionary<int, Action>> firings) =>
        RunSimulation(
            listenStrong: listenStrong,
            firings:
            [
                .. firings.Select(static f =>
                    f.ToLookup(keySelector: static p => p.Key, elementSelector: static p => p.Value))
            ]);

    private static List<T> RunSimulation<T>(
        Func<Action<T>, IListener> listenStrong,
        IReadOnlyList<ILookup<int, Action>>? firings = null)
    {
        int maxKey = firings?.SelectMany(static d => d.Select(static g => g.Key)).DefaultIfEmpty(-1).Max() ?? -1;
        List<T> @out = [];
        IListener? l = null;

        try
        {
            void Run(int t)
            {
                if (firings != null)
                {
                    foreach (Action a in firings.SelectMany(f => f[t]))
                    {
                        a();
                    }
                }
            }

            if (maxKey > -1)
            {
                l =
                    Transaction.Run(() =>
                    {
                        IListener lLocal = listenStrong(@out.Add);
                        Run(0);
                        return lLocal;
                    });

                for (int i = 1; i <= maxKey; i++)
                {
                    int t = i;

                    Transaction.RunVoid(() =>
                    {
                        Run(t);
                    });
                }
            }
            else
            {
                l = listenStrong(@out.Add);
            }
        }
        finally
        {
            l?.Unlisten();
        }

        return @out;
    }

    private static void RunPermutations<T>(
        Func<Func<IReadOnlyList<(string Name, Dictionary<int, Action> Firings)>, Func<Action<T>, IListener>, (
                IReadOnlyList<(string Name, Dictionary<int, Action> Firings)> FiringsList,
                Func<Action<T>, IListener>
                ListenStrong)>, (IReadOnlyList<(string Name, Dictionary<int, Action> Firings)> FiringsList,
            Func<Action<T>, IListener> ListenStrong)> createListAndListener,
        Action<IReadOnlyList<T>> assert)
    {
        IReadOnlyList<int> indexes =
        [
            .. Enumerable.Range(
                start: 0,
                count: createListAndListener(static (fl, l) => (FiringsList: fl, ListenStrong: l)).FiringsList.Count)
        ];

        foreach ((IReadOnlyList<(string Name, Dictionary<int, Action> Firings)> firingsList,
                     Func<Action<T>, IListener> listener) in
                 GetPermutations(indexes)
                     .Select(ii =>
                     {
                         (IReadOnlyList<(string Name, Dictionary<int, Action> Firings)> firingsList,
                                 Func<Action<T>, IListener> listenStrong) =
                             createListAndListener(static (fl, l) => (FiringsList: fl, ListenStrong: l));

                         return (FiringsList: ii.Select(i => firingsList[i]).ToArray(), ListenStrong: listenStrong);
                     }))
        {
            try
            {
                List<T> @out =
                    RunSimulation(
                        listenStrong: listener,
                        firings: (IReadOnlyList<Dictionary<int, Action>>)
                        [
                            .. firingsList.Select(static o => o.Firings)
                        ]);

                assert(@out);
            }
            catch
            {
                Console.WriteLine(
                    "Test failed for ordering { " + string.Join(
                        separator: ", ",
                        values: firingsList.Select(static o => o.Name)) + " }.");

                throw;
            }
        }
    }

    private static IReadOnlyList<IReadOnlyList<T>> GetPermutations<T>(IReadOnlyList<T> list) =>
        GetPermutations(list: list, length: list.Count);

    private static IReadOnlyList<IReadOnlyList<T>> GetPermutations<T>(IReadOnlyList<T> list, int length)
    {
        if (length == 1)
        {
            return [.. list.Select(static t => new[] { t })];
        }

        return
        [
            .. GetPermutations(list: list, length: length - 1)
                .SelectMany(
                    collectionSelector: t => list.Where(e => !t.Contains(e)),
                    resultSelector: static (t1, t2) => t1.Concat([t2]).ToArray())
        ];
    }
}
