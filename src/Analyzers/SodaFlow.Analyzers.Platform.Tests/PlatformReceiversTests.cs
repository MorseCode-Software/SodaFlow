using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using TUnit.Assertions;
using TUnit.Core;

namespace SodaFlow.Analyzers.Platform.Tests;

/// <summary>
///     Holds <see cref="PlatformReceivers" /> to the platform. Each row says
///     which of two objects a method of the platform gives the call to. The test
///     calls the method with two objects that record a call, and compares the
///     record with the row.
/// </summary>
/// <remarks>
///     The documentation of the platform does not say which object gets the
///     call, and a new runtime can change it. Then, this test fails on that
///     runtime, and the row must go.
/// </remarks>
internal sealed class PlatformReceiversTests
{
    private const string Stored = "stored";
    private const string Given = "given";

    // For each row: code that gives the method the two objects. The first object is the one that
    // the collection has, or the first value. The second object is the value that the code gives.
    // The code calls each overload of the method that gets the two objects and no comparer.
    private static readonly Dictionary<(string Type, string Method), Action<Probe, Probe>> Calls =
        new()
        {
            [("Array", "IndexOf")] = static (stored, given) => _ = Array.IndexOf(array: ArrayOf(stored), value: given),
            [("Array", "BinarySearch")] = static (stored, given) =>
                _ = Array.BinarySearch(array: ArrayOf(stored), value: given),
            [("List", "Contains")] = static (stored, given) => _ = ListOf(stored).Contains(given),
            [("List", "IndexOf")] = static (stored, given) => _ = ListOf(stored).IndexOf(given),
            [("List", "LastIndexOf")] = static (stored, given) => _ = ListOf(stored).LastIndexOf(given),
            [("List", "Remove")] = static (stored, given) => _ = ListOf(stored).Remove(given),
            [("List", "BinarySearch")] = static (stored, given) => _ = ListOf(stored).BinarySearch(given),
            [("Enumerable", "Contains")] = static (stored, given) =>
            {
                // A query, a list, and an array each have a path of their own in the platform. The
                // type of each is the interface, because the same call on a list or on an array
                // is a different method.
                IEnumerable<Probe>[] sources =
                    [ArrayOf(stored).Where(static _ => true), ListOf(stored), ArrayOf(stored)];

                foreach (IEnumerable<Probe> source in sources)
                {
                    _ = source.Contains(given);
                }
            },
            [("Enumerable", "SequenceEqual")] = static (stored, given) =>
            {
                IEnumerable<Probe> array = ArrayOf(stored);

                _ = ListOf(stored).SequenceEqual(ListOf(given));
                _ = array.SequenceEqual(ArrayOf(given));
            },
            [("HashSet", "Add")] = static (stored, given) => _ = SetOf(stored).Add(given),
            [("HashSet", "Contains")] = static (stored, given) => _ = SetOf(stored).Contains(given),
            [("HashSet", "Remove")] = static (stored, given) => _ = SetOf(stored).Remove(given),
            [("Dictionary", "Add")] = static (stored, given) => DictionaryOf(stored).Add(key: given, value: 2),
            [("Dictionary", "ContainsKey")] = static (stored, given) => _ = DictionaryOf(stored).ContainsKey(given),
            [("Dictionary", "TryGetValue")] = static (stored, given) =>
                _ = DictionaryOf(stored).TryGetValue(key: given, value: out _),
            [("Dictionary", "Remove")] = static (stored, given) => _ = DictionaryOf(stored).Remove(given),
            [("Dictionary", "get_Item")] = static (stored, given) => _ = ReadsByIndex(stored: stored, given: given),
            [("Dictionary", "set_Item")] = static (stored, given) => DictionaryOf(stored)[given] = 2,
            [("SortedList", "Add")] = static (stored, given) => SortedListOf(stored).Add(key: given, value: 2),
            [("Object", "Equals")] = static (stored, given) => _ = Equals(objA: stored, objB: given),
            [("ValueTuple", "Equals")] = static (stored, given) => _ = (stored, 1).Equals((given, 1)),
            [("ValueTuple", "CompareTo")] = static (stored, given) => _ = (stored, 1).CompareTo((given, 1)),
            [("SortedSet", "Add")] = static (stored, given) => _ = SortedSetOf(stored).Add(given),
            [("SortedSet", "Contains")] = static (stored, given) => _ = SortedSetOf(stored).Contains(given),
            [("SortedDictionary", "Add")] = static (stored, given) =>
                SortedDictionaryOf(stored).Add(key: given, value: 2),
            [("SortedDictionary", "set_Item")] = static (stored, given) => SortedDictionaryOf(stored)[given] = 2,
            [("Comparer", "Compare")] = static (stored, given) =>
                _ = Comparer<Probe>.Default.Compare(x: stored, y: given),
            [("EqualityComparer", "Equals")] = static (stored, given) =>
                _ = EqualityComparer<Probe>.Default.Equals(x: stored, y: given),
#if NET
            [("Dictionary", "TryAdd")] = static (stored, given) =>
                _ = DictionaryOf(stored).TryAdd(key: given, value: 2),
            [("PriorityQueue", "Enqueue")] = static (stored, given) =>
            {
                PriorityQueue<int, Probe> queue = new();
                queue.Enqueue(element: 1, priority: stored);
                stored.Forget();
                queue.Enqueue(element: 2, priority: given);
            },
            [("MemoryExtensions", "Contains")] = static (stored, given) => _ = SpanOf(stored).Contains(given),
            [("MemoryExtensions", "IndexOf")] = static (stored, given) =>
            {
                _ = SpanOf(stored).IndexOf(given);
                _ = SpanOf(stored).IndexOf(ArrayOf(given));
            },
            [("MemoryExtensions", "LastIndexOf")] = static (stored, given) =>
            {
                _ = SpanOf(stored).LastIndexOf(given);
                _ = SpanOf(stored).LastIndexOf(ArrayOf(given));
            },
            [("MemoryExtensions", "Count")] = static (stored, given) =>
            {
                _ = SpanOf(stored).Count(given);
                _ = SpanOf(stored).Count(ArrayOf(given));
            },
            [("MemoryExtensions", "Replace")] = static (stored, given) =>
            {
                new Span<Probe>(ArrayOf(stored)).Replace(oldValue: given, newValue: given);
                SpanOf(stored).Replace(destination: new Probe[1], oldValue: given, newValue: given);
            },
            [("MemoryExtensions", "IndexOfAnyExcept")] = static (stored, given) =>
            {
                _ = SpanOf(stored).IndexOfAnyExcept(given);
                _ = SpanOf(stored).IndexOfAnyExcept(value0: given, value1: given);
                _ = SpanOf(stored).IndexOfAnyExcept(value0: given, value1: given, value2: given);
                _ = SpanOf(stored).IndexOfAnyExcept(ArrayOf(given));
            },
            [("MemoryExtensions", "LastIndexOfAnyExcept")] = static (stored, given) =>
            {
                _ = SpanOf(stored).LastIndexOfAnyExcept(given);
                _ = SpanOf(stored).LastIndexOfAnyExcept(value0: given, value1: given);
                _ = SpanOf(stored).LastIndexOfAnyExcept(value0: given, value1: given, value2: given);
                _ = SpanOf(stored).LastIndexOfAnyExcept(ArrayOf(given));
            },
            [("MemoryExtensions", "ContainsAnyExcept")] = static (stored, given) =>
            {
                _ = SpanOf(stored).ContainsAnyExcept(given);
                _ = SpanOf(stored).ContainsAnyExcept(value0: given, value1: given);
                _ = SpanOf(stored).ContainsAnyExcept(value0: given, value1: given, value2: given);
                _ = SpanOf(stored).ContainsAnyExcept(ArrayOf(given));
            },
            [("MemoryExtensions", "CountAny")] = static (stored, given) =>
                _ = SpanOf(stored).CountAny(ArrayOf(given)),
            [("MemoryExtensions", "SequenceEqual")] = static (stored, given) =>
                _ = SpanOf(stored).SequenceEqual(ArrayOf(given)),
            [("MemoryExtensions", "StartsWith")] = static (stored, given) =>
            {
                _ = SpanOf(stored).StartsWith(given);
                _ = SpanOf(stored).StartsWith(ArrayOf(given));
            },
            [("MemoryExtensions", "EndsWith")] = static (stored, given) =>
            {
                _ = SpanOf(stored).EndsWith(given);
                _ = SpanOf(stored).EndsWith(ArrayOf(given));
            },
            [("MemoryExtensions", "CommonPrefixLength")] = static (stored, given) =>
                _ = SpanOf(stored).CommonPrefixLength(ArrayOf(given)),
            [("MemoryExtensions", "SequenceCompareTo")] = static (stored, given) =>
                _ = SpanOf(stored).SequenceCompareTo(ArrayOf(given)),
            [("MemoryExtensions", "Trim")] = static (stored, given) =>
            {
                _ = SpanOf(stored).Trim(given).Length;
                _ = SpanOf(stored).Trim(ArrayOf(given)).Length;
            },
            [("MemoryExtensions", "TrimStart")] = static (stored, given) =>
            {
                _ = SpanOf(stored).TrimStart(given).Length;
                _ = SpanOf(stored).TrimStart(ArrayOf(given)).Length;
            },
            [("MemoryExtensions", "TrimEnd")] = static (stored, given) =>
            {
                _ = SpanOf(stored).TrimEnd(given).Length;
                _ = SpanOf(stored).TrimEnd(ArrayOf(given)).Length;
            },
            [("MemoryExtensions", "BinarySearch")] = static (stored, given) =>
            {
                _ = SpanOf(stored).BinarySearch(given);
                _ = SpanOf(stored).BinarySearch<Probe>(given);
            },
            [("MemoryExtensions", "IndexOfAnyInRange")] = static (stored, given) =>
                _ = SpanOf(stored).IndexOfAnyInRange(lowInclusive: given, highInclusive: given),
            [("MemoryExtensions", "LastIndexOfAnyInRange")] = static (stored, given) =>
                _ = SpanOf(stored).LastIndexOfAnyInRange(lowInclusive: given, highInclusive: given),
            [("MemoryExtensions", "IndexOfAnyExceptInRange")] = static (stored, given) =>
                _ = SpanOf(stored).IndexOfAnyExceptInRange(lowInclusive: given, highInclusive: given),
            [("MemoryExtensions", "LastIndexOfAnyExceptInRange")] = static (stored, given) =>
                _ = SpanOf(stored).LastIndexOfAnyExceptInRange(lowInclusive: given, highInclusive: given),
            [("MemoryExtensions", "ContainsAnyInRange")] = static (stored, given) =>
                _ = SpanOf(stored).ContainsAnyInRange(lowInclusive: given, highInclusive: given),
            [("MemoryExtensions", "ContainsAnyExceptInRange")] = static (stored, given) =>
                _ = SpanOf(stored).ContainsAnyExceptInRange(lowInclusive: given, highInclusive: given)
#endif
        };

#if NET
    // The same for the rows of an overload that has a parameter for a comparer. The type of the
    // objects is not IEquatable, thus the compiler takes that overload, and the code gives no
    // comparer. Only a span method has such a row, and .NET Framework has no span method.
    private static readonly Dictionary<(string Type, string Method), Action<PlainProbe, PlainProbe>> PlainCalls =
        new()
        {
            [("MemoryExtensions", "Contains")] = static (stored, given) =>
                _ = SpanOf(stored).Contains(given),
            [("MemoryExtensions", "ContainsAny")] = static (stored, given) =>
            {
                _ = SpanOf(stored).ContainsAny(value0: given, value1: given);
                _ = SpanOf(stored).ContainsAny(value0: given, value1: given, value2: given);
                _ = SpanOf(stored).ContainsAny(ArrayOf(given));
            },
            [("MemoryExtensions", "IndexOfAny")] = static (stored, given) =>
            {
                _ = SpanOf(stored).IndexOfAny(value0: given, value1: given);
                _ = SpanOf(stored).IndexOfAny(value0: given, value1: given, value2: given);
                _ = SpanOf(stored).IndexOfAny(ArrayOf(given));
            },
            [("MemoryExtensions", "LastIndexOfAny")] = static (stored, given) =>
            {
                _ = SpanOf(stored).LastIndexOfAny(value0: given, value1: given);
                _ = SpanOf(stored).LastIndexOfAny(value0: given, value1: given, value2: given);
                _ = SpanOf(stored).LastIndexOfAny(ArrayOf(given));
            },
            [("MemoryExtensions", "IndexOf")] = static (stored, given) =>
            {
                _ = SpanOf(stored).IndexOf(given);
                _ = SpanOf(stored).IndexOf(ArrayOf(given));
            },
            [("MemoryExtensions", "LastIndexOf")] = static (stored, given) =>
            {
                _ = SpanOf(stored).LastIndexOf(given);
                _ = SpanOf(stored).LastIndexOf(ArrayOf(given));
            },
            [("MemoryExtensions", "Count")] = static (stored, given) =>
            {
                _ = SpanOf(stored).Count(given);
                _ = SpanOf(stored).Count(ArrayOf(given));
            },
            [("MemoryExtensions", "Replace")] = static (stored, given) =>
            {
                new Span<PlainProbe>(ArrayOf(stored)).Replace(oldValue: given, newValue: given);
                SpanOf(stored).Replace(destination: new PlainProbe[1], oldValue: given, newValue: given);
            },
            [("MemoryExtensions", "IndexOfAnyExcept")] = static (stored, given) =>
            {
                _ = SpanOf(stored).IndexOfAnyExcept(given);
                _ = SpanOf(stored).IndexOfAnyExcept(value0: given, value1: given);
                _ = SpanOf(stored).IndexOfAnyExcept(value0: given, value1: given, value2: given);
                _ = SpanOf(stored).IndexOfAnyExcept(ArrayOf(given));
            },
            [("MemoryExtensions", "LastIndexOfAnyExcept")] = static (stored, given) =>
            {
                _ = SpanOf(stored).LastIndexOfAnyExcept(given);
                _ = SpanOf(stored).LastIndexOfAnyExcept(value0: given, value1: given);
                _ = SpanOf(stored).LastIndexOfAnyExcept(value0: given, value1: given, value2: given);
                _ = SpanOf(stored).LastIndexOfAnyExcept(ArrayOf(given));
            },
            [("MemoryExtensions", "ContainsAnyExcept")] = static (stored, given) =>
            {
                _ = SpanOf(stored).ContainsAnyExcept(given);
                _ = SpanOf(stored).ContainsAnyExcept(value0: given, value1: given);
                _ = SpanOf(stored).ContainsAnyExcept(value0: given, value1: given, value2: given);
                _ = SpanOf(stored).ContainsAnyExcept(ArrayOf(given));
            },
            [("MemoryExtensions", "CountAny")] = static (stored, given) =>
                _ = SpanOf(stored).CountAny(ArrayOf(given)),
            [("MemoryExtensions", "SequenceEqual")] = static (stored, given) =>
                _ = SpanOf(stored).SequenceEqual(ArrayOf(given)),
            [("MemoryExtensions", "StartsWith")] = static (stored, given) =>
            {
                _ = SpanOf(stored).StartsWith(given);
                _ = SpanOf(stored).StartsWith(ArrayOf(given));
            },
            [("MemoryExtensions", "EndsWith")] = static (stored, given) =>
            {
                _ = SpanOf(stored).EndsWith(given);
                _ = SpanOf(stored).EndsWith(ArrayOf(given));
            },
            [("MemoryExtensions", "SequenceCompareTo")] = static (stored, given) =>
                _ = SpanOf(stored).SequenceCompareTo(ArrayOf(given))
        };
#endif

    [Test]
    public async Task EachRowNamesTheObjectThatGetsTheCall() =>
        await Assert.That(
                PlatformReceivers.ByMethod
                    .Where(static row => IsOnThisFramework(row.Key))
                    .SelectMany(static row =>
                        WrongFor(
                            calls: Calls,
                            make: static (name, rank, record) => new Probe(name: name, rank: rank, record: record),
                            key: row.Key,
                            receiver: row.Value)))
            .IsEmpty();

#if NET
    // The same for the rows of an overload that gets its values one by one.
    private static readonly Dictionary<(string Type, string Method), Action<Probe, Probe>> ValueCalls =
        new()
        {
            [("MemoryExtensions", "IndexOfAny")] = static (stored, given) =>
            {
                _ = SpanOf(stored).IndexOfAny(value0: given, value1: given);
                _ = SpanOf(stored).IndexOfAny(value0: given, value1: given, value2: given);
            },
            [("MemoryExtensions", "LastIndexOfAny")] = static (stored, given) =>
            {
                _ = SpanOf(stored).LastIndexOfAny(value0: given, value1: given);
                _ = SpanOf(stored).LastIndexOfAny(value0: given, value1: given, value2: given);
            },
            [("MemoryExtensions", "ContainsAny")] = static (stored, given) =>
            {
                _ = SpanOf(stored).ContainsAny(value0: given, value1: given);
                _ = SpanOf(stored).ContainsAny(value0: given, value1: given, value2: given);
            }
        };

    [Test]
    public async Task EachRowWithValuesOneByOneNamesTheObjectThatGetsTheCall() =>
        await Assert.That(
                PlatformReceivers.WithValuesOneByOne.SelectMany(static row =>
                    WrongFor(
                        calls: ValueCalls,
                        make: static (name, rank, record) => new Probe(name: name, rank: rank, record: record),
                        key: row.Key,
                        receiver: row.Value)))
            .IsEmpty();

    [Test]
    public async Task EachCallWithValuesOneByOneHasARow() =>
        await Assert.That(ValueCalls.Keys.Where(static key => !PlatformReceivers.WithValuesOneByOne.ContainsKey(key)))
            .IsEmpty();

    [Test]
    public async Task EachRowWithNoComparerGivenNamesTheObjectThatGetsTheCall() =>
        await Assert.That(
                PlatformReceivers.WithNoComparerGiven.SelectMany(static row =>
                    WrongFor(
                        calls: PlainCalls,
                        make: static (name, rank, record) => new PlainProbe(name: name, rank: rank, record: record),
                        key: row.Key,
                        receiver: row.Value)))
            .IsEmpty();

    [Test]
    public async Task EachCallWithNoComparerGivenHasARow() =>
        await Assert.That(PlainCalls.Keys.Where(static key => !PlatformReceivers.WithNoComparerGiven.ContainsKey(key)))
            .IsEmpty();
#endif

    [Test]
    public async Task EachCallHasARow() =>
        await Assert.That(Calls.Keys.Where(static key => !PlatformReceivers.ByMethod.ContainsKey(key))).IsEmpty();

    [Test]
    public async Task NoRowSaysEither() =>
        await Assert.That(
                PlatformReceivers.ByMethod.Values.Concat(PlatformReceivers.WithNoComparerGiven.Values)
                    .Where(static receiver => receiver == PlatformReceiver.Either))
            .IsEmpty();

    // False for a row whose member this target framework does not have. .NET Framework has no
    // PriorityQueue and no TryAdd, and it has the span methods only from a package.
    private static bool IsOnThisFramework((string Type, string Method) key) =>
#if NET
        key.Type.Length > 0;
#else
        key.Type is not ("MemoryExtensions" or "PriorityQueue") && key != ("Dictionary", "TryAdd");
#endif

    // One line for a row that is not correct, and no line for a row that is correct.
    private static IEnumerable<string> WrongFor<T>(
        IReadOnlyDictionary<(string Type, string Method), Action<T, T>> calls,
        Func<string, int, List<string>, T> make,
        (string Type, string Method) key,
        PlatformReceiver receiver)
    {
        if (!calls.TryGetValue(key: key, value: out Action<T, T>? call))
        {
            return [$"{key.Type}.{key.Method}: no code calls it"];
        }

        List<string> record = [];

        call(arg1: make(arg1: Stored, arg2: 1, arg3: record), arg2: make(arg1: Given, arg2: 2, arg3: record));

        string expected = receiver == PlatformReceiver.Given ? Given : Stored;

        return record.Count == 0 || record.Any(name => name != expected)
            ?
            [
                $"{key.Type}.{key.Method}: the row says {expected}, and the calls went to "
                + $"[{string.Join(separator: ", ", values: record)}]"
            ]
            : [];
    }

    private static T[] ArrayOf<T>(T stored) => [stored];

#if NET
    private static ReadOnlySpan<T> SpanOf<T>(T stored) => ArrayOf(stored);
#endif

    private static List<Probe> ListOf(Probe stored) => [stored];

    private static HashSet<Probe> SetOf(Probe stored)
    {
        HashSet<Probe> set = [stored];
        stored.Forget();

        return set;
    }

    private static Dictionary<Probe, int> DictionaryOf(Probe stored)
    {
        Dictionary<Probe, int> dictionary = new() { { stored, 1 } };
        stored.Forget();

        return dictionary;
    }

    private static SortedSet<Probe> SortedSetOf(Probe stored)
    {
        SortedSet<Probe> set = [stored];
        stored.Forget();

        return set;
    }

    private static SortedDictionary<Probe, int> SortedDictionaryOf(Probe stored)
    {
        SortedDictionary<Probe, int> dictionary = new() { { stored, 1 } };
        stored.Forget();

        return dictionary;
    }

    private static SortedList<Probe, int> SortedListOf(Probe stored)
    {
        SortedList<Probe, int> list = new() { { stored, 1 } };
        stored.Forget();

        return list;
    }

    // The getter of the indexer throws for a key that the dictionary does not have, after the
    // call that this test records.
    private static bool ReadsByIndex(Probe stored, Probe given)
    {
        try
        {
            return DictionaryOf(stored)[given] > 0;
        }
        catch (KeyNotFoundException)
        {
            return false;
        }
    }

    // An object that records its name when the platform calls its Equals or its CompareTo. Two of
    // them are never equal, and each has the same hash code, so that a set compares them.
    private sealed class Probe(in string name, in int rank, in List<string> record)
        : IEquatable<Probe>, IComparable<Probe>
    {
        private readonly string name = name;
        private readonly int rank = rank;
        private readonly List<string> record = record;

        // The code that puts the first object into a collection can make a call. That call is not
        // the one that the row is about.
        public void Forget() => this.record.Clear();

        public bool Equals(Probe? other)
        {
            this.record.Add(this.name);

            return ReferenceEquals(objA: this, objB: other);
        }

        public override bool Equals(object? obj)
        {
            this.record.Add(this.name);

            return ReferenceEquals(objA: this, objB: obj);
        }

        public override int GetHashCode() => 1;

        public int CompareTo(Probe? other)
        {
            this.record.Add(this.name);

            return other == null ? 1 : this.rank.CompareTo(other.rank);
        }
    }

#if NET
    // The same object with only the members that each type can have. It is not IEquatable, and it
    // is IComparable only with no type argument.
    private sealed class PlainProbe(in string name, in int rank, in List<string> record) : IComparable
    {
        private readonly string name = name;
        private readonly int rank = rank;
        private readonly List<string> record = record;

        public override bool Equals(object? obj)
        {
            this.record.Add(this.name);

            return ReferenceEquals(objA: this, objB: obj);
        }

        public override int GetHashCode() => 1;

        public int CompareTo(object? obj)
        {
            this.record.Add(this.name);

            return obj is PlainProbe other ? this.rank.CompareTo(other.rank) : 1;
        }
    }
#endif
}
