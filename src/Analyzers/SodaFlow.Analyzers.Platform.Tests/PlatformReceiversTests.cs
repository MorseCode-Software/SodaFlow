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
    // A row with no code is for a member that this target framework does not have.
    private static readonly Dictionary<(string Type, string Method), Action<Probe, Probe>?> Calls =
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
            [("MemoryExtensions", "Contains")] = static (stored, given) =>
                _ = ((ReadOnlySpan<Probe>)ArrayOf(stored)).Contains(given)
#else
            [("Dictionary", "TryAdd")] = null,
            [("PriorityQueue", "Enqueue")] = null,
            [("MemoryExtensions", "Contains")] = null
#endif
        };

    [Test]
    public async Task EachRowNamesTheObjectThatGetsTheCall() =>
        await Assert.That(
                PlatformReceivers.ByMethod.SelectMany(static row => WrongFor(key: row.Key, receiver: row.Value)))
            .IsEmpty();

    [Test]
    public async Task EachCallHasARow() =>
        await Assert.That(Calls.Keys.Where(static key => !PlatformReceivers.ByMethod.ContainsKey(key))).IsEmpty();

    [Test]
    public async Task NoRowSaysEither() =>
        await Assert.That(
                PlatformReceivers.ByMethod.Values.Where(static receiver => receiver == PlatformReceiver.Either))
            .IsEmpty();

    // One line for a row that is not correct, and no line for a row that is correct.
    private static IEnumerable<string> WrongFor((string Type, string Method) key, PlatformReceiver receiver)
    {
        if (!Calls.TryGetValue(key: key, value: out Action<Probe, Probe>? call))
        {
            return [$"{key.Type}.{key.Method}: no code calls it"];
        }

        if (call == null)
        {
            return [];
        }

        List<string> record = [];

        call(
            arg1: new Probe(name: Stored, rank: 1, record: record),
            arg2: new Probe(name: Given, rank: 2, record: record));

        string expected = receiver == PlatformReceiver.Given ? Given : Stored;

        return record.Count == 0 || record.Any(name => name != expected)
            ?
            [
                $"{key.Type}.{key.Method}: the row says {expected}, and the calls went to "
                + $"[{string.Join(separator: ", ", values: record)}]"
            ]
            : [];
    }

    private static Probe[] ArrayOf(Probe stored) => [stored];

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
}
