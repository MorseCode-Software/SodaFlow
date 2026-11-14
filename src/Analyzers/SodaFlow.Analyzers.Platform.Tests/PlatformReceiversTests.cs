using System;
using System.Collections;
using System.Collections.Concurrent;
#if NET
using System.Collections.Frozen;
#endif
using System.Collections.Generic;
#if NET
using System.Collections.Immutable;
#endif
using System.Collections.ObjectModel;
using System.Linq;
#if NET
using System.Runtime.CompilerServices;
#endif
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
            [("Array", "LastIndexOf")] = static (stored, given) =>
                _ = Array.LastIndexOf(array: ArrayOf(stored), value: given),
            [("ArrayList", "BinarySearch")] = static (stored, given) => _ = ArrayListOf(stored).BinarySearch(given),
            [("ArrayList", "Contains")] = static (stored, given) => _ = ArrayListOf(stored).Contains(given),
            [("ArrayList", "IndexOf")] = static (stored, given) => _ = ArrayListOf(stored).IndexOf(given),
            [("ArrayList", "LastIndexOf")] = static (stored, given) => _ = ArrayListOf(stored).LastIndexOf(given),
            [("ArrayList", "Remove")] = static (stored, given) => ArrayListOf(stored).Remove(given),
            [("Collection", "Contains")] = static (stored, given) =>
                _ = new Collection<Probe>(ListOf(stored)).Contains(given),
            [("Collection", "IndexOf")] = static (stored, given) =>
                _ = new Collection<Probe>(ListOf(stored)).IndexOf(given),
            [("Collection", "Remove")] = static (stored, given) =>
                _ = new Collection<Probe>(ListOf(stored)).Remove(given),
            [("ConcurrentDictionary", "AddOrUpdate")] = static (stored, given) =>
                _ = ConcurrentOf(stored)
                    .AddOrUpdate(key: given, addValue: 2, updateValueFactory: static (_, value) => value),
            [("ConcurrentDictionary", "ContainsKey")] = static (stored, given) =>
                _ = ConcurrentOf(stored).ContainsKey(given),
            [("ConcurrentDictionary", "GetOrAdd")] = static (stored, given) =>
                _ = ConcurrentOf(stored).GetOrAdd(key: given, value: 2),
            [("ConcurrentDictionary", "TryAdd")] = static (stored, given) =>
                _ = ConcurrentOf(stored).TryAdd(key: given, value: 2),
            [("ConcurrentDictionary", "TryGetValue")] = static (stored, given) =>
                _ = ConcurrentOf(stored).TryGetValue(key: given, value: out _),
            [("ConcurrentDictionary", "TryRemove")] = static (stored, given) =>
                _ = ConcurrentOf(stored).TryRemove(key: given, value: out _),
            [("ConcurrentDictionary", "TryUpdate")] = static (stored, given) =>
                _ = ConcurrentOf(stored).TryUpdate(key: given, newValue: 2, comparisonValue: 1),
            [("ConcurrentDictionary", "get_Item")] = static (stored, given) =>
                Try(() => _ = ConcurrentOf(stored)[given]),
            [("ConcurrentDictionary", "set_Item")] = static (stored, given) => ConcurrentOf(stored)[given] = 2,
            [("Dictionary", "ContainsValue")] = static (stored, given) =>
                _ = new Dictionary<int, Probe> { { 1, stored } }.ContainsValue(given),
            [("Enumerable", "Intersect")] = static (stored, given) =>
                _ = ArrayOf(stored).Intersect(ArrayOf(given)).ToArray(),
            [("HashSet", "TryGetValue")] = static (stored, given) =>
                _ = SetOf(stored).TryGetValue(equalValue: given, actualValue: out _),
            [("Hashtable", "Add")] = static (stored, given) => HashtableOf(stored).Add(key: given, value: 2),
            [("Hashtable", "Contains")] = static (stored, given) => _ = HashtableOf(stored).Contains(given),
            [("Hashtable", "ContainsKey")] = static (stored, given) => _ = HashtableOf(stored).ContainsKey(given),
            [("Hashtable", "ContainsValue")] = static (stored, given) =>
                _ = new Hashtable { { 1, stored } }.ContainsValue(given),
            [("Hashtable", "Remove")] = static (stored, given) => HashtableOf(stored).Remove(given),
            [("Hashtable", "get_Item")] = static (stored, given) => _ = HashtableOf(stored)[given],
            [("Hashtable", "set_Item")] = static (stored, given) => HashtableOf(stored)[given] = 2,
            [("LinkedList", "Contains")] = static (stored, given) =>
                _ = new LinkedList<Probe>(ArrayOf(stored)).Contains(given),
            [("LinkedList", "Find")] = static (stored, given) => _ = new LinkedList<Probe>(ArrayOf(stored)).Find(given),
            [("LinkedList", "FindLast")] = static (stored, given) =>
                _ = new LinkedList<Probe>(ArrayOf(stored)).FindLast(given),
            [("LinkedList", "Remove")] = static (stored, given) =>
                _ = new LinkedList<Probe>(ArrayOf(stored)).Remove(given),
            [("Queue", "Contains")] = static (stored, given) => _ = new Queue<Probe>(ArrayOf(stored)).Contains(given),
            [("ReadOnlyCollection", "Contains")] = static (stored, given) =>
                _ = new ReadOnlyCollection<Probe>(ListOf(stored)).Contains(given),
            [("ReadOnlyCollection", "IndexOf")] = static (stored, given) =>
                _ = new ReadOnlyCollection<Probe>(ListOf(stored)).IndexOf(given),
            [("ReadOnlyDictionary", "ContainsKey")] = static (stored, given) =>
                _ = new ReadOnlyDictionary<Probe, int>(DictionaryOf(stored)).ContainsKey(given),
            [("ReadOnlyDictionary", "TryGetValue")] = static (stored, given) =>
                _ = new ReadOnlyDictionary<Probe, int>(DictionaryOf(stored)).TryGetValue(key: given, value: out _),
            [("ReadOnlyDictionary", "get_Item")] = static (stored, given) =>
                Try(() => _ = new ReadOnlyDictionary<Probe, int>(DictionaryOf(stored))[given]),
            [("SortedDictionary", "ContainsKey")] = static (stored, given) =>
                _ = SortedDictionaryOf(stored).ContainsKey(given),
            [("SortedDictionary", "ContainsValue")] = static (stored, given) =>
                _ = new SortedDictionary<int, Probe> { { 1, stored } }.ContainsValue(given),
            [("SortedDictionary", "Remove")] = static (stored, given) => _ = SortedDictionaryOf(stored).Remove(given),
            [("SortedDictionary", "TryGetValue")] = static (stored, given) =>
                _ = SortedDictionaryOf(stored).TryGetValue(key: given, value: out _),
            [("SortedDictionary", "get_Item")] = static (stored, given) =>
                Try(() => _ = SortedDictionaryOf(stored)[given]),
            [("SortedList", "ContainsKey")] = static (stored, given) => _ = SortedListOf(stored).ContainsKey(given),
            [("SortedList", "ContainsValue")] = static (stored, given) =>
                _ = new SortedList<int, Probe> { { 1, stored } }.ContainsValue(given),
            [("SortedList", "IndexOfKey")] = static (stored, given) => _ = SortedListOf(stored).IndexOfKey(given),
            [("SortedList", "IndexOfValue")] = static (stored, given) =>
                _ = new SortedList<int, Probe> { { 1, stored } }.IndexOfValue(given),
            [("SortedList", "Remove")] = static (stored, given) => _ = SortedListOf(stored).Remove(given),
            [("SortedList", "TryGetValue")] = static (stored, given) =>
                _ = SortedListOf(stored).TryGetValue(key: given, value: out _),
            [("SortedList", "get_Item")] = static (stored, given) => Try(() => _ = SortedListOf(stored)[given]),
            [("SortedList", "set_Item")] = static (stored, given) => SortedListOf(stored)[given] = 2,
            [("SortedSet", "GetViewBetween")] = static (stored, given) =>
                _ = SortedSetOf(stored).GetViewBetween(lowerValue: given, upperValue: given),
            [("SortedSet", "Remove")] = static (stored, given) => _ = SortedSetOf(stored).Remove(given),
            [("SortedSet", "TryGetValue")] = static (stored, given) =>
                _ = SortedSetOf(stored).TryGetValue(equalValue: given, actualValue: out _),
            [("Stack", "Contains")] = static (stored, given) => _ = new Stack<Probe>(ArrayOf(stored)).Contains(given),
            [("Tuple", "CompareTo")] = static (stored, given) =>
                _ = ((IComparable)Tuple.Create(item1: stored, item2: 1))
                    .CompareTo(Tuple.Create(item1: given, item2: 1)),
            [("Tuple", "Equals")] = static (stored, given) =>
                _ = Tuple.Create(item1: stored, item2: 1).Equals(Tuple.Create(item1: given, item2: 1)),
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
                _ = SpanOf(stored).ContainsAnyExceptInRange(lowInclusive: given, highInclusive: given),
            [("CollectionExtensions", "GetValueOrDefault")] = static (stored, given) =>
            {
                IReadOnlyDictionary<Probe, int> map = DictionaryOf(stored);
                _ = map.GetValueOrDefault(given);
            },
            [("CollectionExtensions", "Remove")] = static (stored, given) =>
            {
                IDictionary<Probe, int> map = DictionaryOf(stored);
                _ = map.Remove(key: given, value: out _);
            },
            [("CollectionExtensions", "TryAdd")] = static (stored, given) =>
            {
                IDictionary<Probe, int> map = DictionaryOf(stored);
                _ = map.TryAdd(key: given, value: 2);
            },
            [("FrozenDictionary", "ContainsKey")] = static (stored, given) =>
                _ = FrozenDictionaryOf(stored).ContainsKey(given),
            [("FrozenDictionary", "GetValueRefOrNullRef")] = static (stored, given) =>
            {
                ref readonly int found = ref FrozenDictionaryOf(stored).GetValueRefOrNullRef(given);
                _ = Unsafe.IsNullRef(in found);
            },
            [("FrozenDictionary", "TryGetValue")] = static (stored, given) =>
                _ = FrozenDictionaryOf(stored).TryGetValue(key: given, value: out _),
            [("FrozenDictionary", "get_Item")] = static (stored, given) =>
                Try(() => _ = FrozenDictionaryOf(stored)[given]),
            [("FrozenSet", "Contains")] = static (stored, given) => _ = FrozenSetOf(stored).Contains(given),
            [("FrozenSet", "TryGetValue")] = static (stored, given) =>
                _ = FrozenSetOf(stored).TryGetValue(equalValue: given, actualValue: out _),
            [("ImmutableArray", "Contains")] = static (stored, given) =>
                _ = ImmutableArray.Create(stored).Contains(given),
            [("ImmutableArray", "IndexOf")] = static (stored, given) =>
                _ = ImmutableArray.Create(stored).IndexOf(given),
            [("ImmutableArray", "LastIndexOf")] = static (stored, given) =>
                _ = ImmutableArray.Create(stored).LastIndexOf(given),
            [("ImmutableArray", "Remove")] = static (stored, given) => _ = ImmutableArray.Create(stored).Remove(given),
            [("ImmutableArray", "RemoveRange")] = static (stored, given) =>
                _ = ImmutableArray.Create(stored).RemoveRange(items: (IEnumerable<Probe>)ArrayOf(given)),
            [("ImmutableArray", "Replace")] = static (stored, given) =>
                Try(() => _ = ImmutableArray.Create(stored).Replace(oldValue: given, newValue: given)),
            [("ImmutableDictionary", "Add")] = static (stored, given) =>
                _ = ImmutableDictionaryOf(stored).Add(key: given, value: 2),
            [("ImmutableDictionary", "ContainsValue")] = static (stored, given) =>
                _ = ImmutableDictionary<int, Probe>.Empty.Add(key: 1, value: stored).ContainsValue(given),
            [("ImmutableDictionary", "SetItem")] = static (stored, given) =>
                _ = ImmutableDictionaryOf(stored).SetItem(key: given, value: 2),
            [("ImmutableHashSet", "Add")] = static (stored, given) => _ = ImmutableHashSetOf(stored).Add(given),
            [("ImmutableHashSet", "Contains")] = static (stored, given) =>
                _ = ImmutableHashSetOf(stored).Contains(given),
            [("ImmutableHashSet", "TryGetValue")] = static (stored, given) =>
                _ = ImmutableHashSetOf(stored).TryGetValue(equalValue: given, actualValue: out _),
            [("ImmutableList", "BinarySearch")] = static (stored, given) =>
                _ = ImmutableList.Create(stored).BinarySearch(given),
            [("ImmutableList", "Contains")] = static (stored, given) =>
                _ = ImmutableList.Create(stored).Contains(given),
            [("ImmutableList", "IndexOf")] = static (stored, given) => _ = ImmutableList.Create(stored).IndexOf(given),
            [("ImmutableList", "Remove")] = static (stored, given) => _ = ImmutableList.Create(stored).Remove(given),
            [("ImmutableList", "RemoveRange")] = static (stored, given) =>
                _ = ImmutableList.Create(stored).RemoveRange(ArrayOf(given)),
            [("ImmutableList", "Replace")] = static (stored, given) =>
                Try(() => _ = ImmutableList.Create(stored).Replace(oldValue: given, newValue: given)),
            [("ImmutableSortedDictionary", "Add")] = static (stored, given) =>
                _ = ImmutableSortedDictionaryOf(stored).Add(key: given, value: 2),
            [("ImmutableSortedDictionary", "AddRange")] = static (stored, given) =>
                _ = ImmutableSortedDictionaryOf(stored).AddRange([new KeyValuePair<Probe, int>(key: given, value: 2)]),
            [("ImmutableSortedDictionary", "Contains")] = static (stored, given) =>
                _ = ImmutableSortedDictionaryOf(stored).Contains(new KeyValuePair<Probe, int>(key: given, value: 1)),
            [("ImmutableSortedDictionary", "ContainsKey")] = static (stored, given) =>
                _ = ImmutableSortedDictionaryOf(stored).ContainsKey(given),
            [("ImmutableSortedDictionary", "ContainsValue")] = static (stored, given) =>
                _ = ImmutableSortedDictionary<int, Probe>.Empty.Add(key: 1, value: stored).ContainsValue(given),
            [("ImmutableSortedDictionary", "Remove")] = static (stored, given) =>
                _ = ImmutableSortedDictionaryOf(stored).Remove(given),
            [("ImmutableSortedDictionary", "RemoveRange")] = static (stored, given) =>
                _ = ImmutableSortedDictionaryOf(stored).RemoveRange(ArrayOf(given)),
            [("ImmutableSortedDictionary", "SetItem")] = static (stored, given) =>
                _ = ImmutableSortedDictionaryOf(stored).SetItem(key: given, value: 2),
            [("ImmutableSortedDictionary", "SetItems")] = static (stored, given) =>
                _ = ImmutableSortedDictionaryOf(stored).SetItems([new KeyValuePair<Probe, int>(key: given, value: 2)]),
            [("ImmutableSortedDictionary", "TryGetKey")] = static (stored, given) =>
                _ = ImmutableSortedDictionaryOf(stored).TryGetKey(equalKey: given, actualKey: out _),
            [("ImmutableSortedDictionary", "TryGetValue")] = static (stored, given) =>
                _ = ImmutableSortedDictionaryOf(stored).TryGetValue(key: given, value: out _),
            [("ImmutableSortedDictionary", "ValueRef")] = static (stored, given) =>
                Try(() => _ = ImmutableSortedDictionaryOf(stored).ValueRef(given)),
            [("ImmutableSortedDictionary", "get_Item")] = static (stored, given) =>
                Try(() => _ = ImmutableSortedDictionaryOf(stored)[given]),
            [("ImmutableSortedSet", "Add")] = static (stored, given) => _ = ImmutableSortedSetOf(stored).Add(given),
            [("ImmutableSortedSet", "Contains")] = static (stored, given) =>
                _ = ImmutableSortedSetOf(stored).Contains(given),
            [("ImmutableSortedSet", "IndexOf")] = static (stored, given) =>
                _ = ImmutableSortedSetOf(stored).IndexOf(given),
            [("ImmutableSortedSet", "Remove")] = static (stored, given) =>
                _ = ImmutableSortedSetOf(stored).Remove(given),
            [("ImmutableSortedSet", "TryGetValue")] = static (stored, given) =>
                _ = ImmutableSortedSetOf(stored).TryGetValue(equalValue: given, actualValue: out _),
            [("ImmutableHashSet.Builder", "Add")] = static (stored, given) =>
                _ = ImmutableHashSetOf(stored).ToBuilder().Add(given),
            [("ImmutableHashSet.Builder", "Contains")] = static (stored, given) =>
                _ = ImmutableHashSetOf(stored).ToBuilder().Contains(given),
            [("ImmutableHashSet.Builder", "TryGetValue")] = static (stored, given) =>
                _ = ImmutableHashSetOf(stored).ToBuilder().TryGetValue(equalValue: given, actualValue: out _),
            [("ImmutableSortedSet.Builder", "Add")] = static (stored, given) =>
                _ = ImmutableSortedSetOf(stored).ToBuilder().Add(given),
            [("ImmutableSortedSet.Builder", "Contains")] = static (stored, given) =>
                _ = ImmutableSortedSetOf(stored).ToBuilder().Contains(given),
            [("ImmutableSortedSet.Builder", "IndexOf")] = static (stored, given) =>
                _ = ImmutableSortedSetOf(stored).ToBuilder().IndexOf(given),
            [("ImmutableSortedSet.Builder", "Remove")] = static (stored, given) =>
                _ = ImmutableSortedSetOf(stored).ToBuilder().Remove(given),
            [("ImmutableSortedSet.Builder", "TryGetValue")] = static (stored, given) =>
                _ = ImmutableSortedSetOf(stored).ToBuilder().TryGetValue(equalValue: given, actualValue: out _),
            [("ImmutableDictionary.Builder", "Add")] = static (stored, given) =>
            {
                ImmutableDictionaryOf(stored).ToBuilder().Add(key: given, value: 2);
                ImmutableDictionaryOf(stored).ToBuilder().Add(new KeyValuePair<Probe, int>(key: given, value: 2));
            },
            [("ImmutableDictionary.Builder", "ContainsValue")] = static (stored, given) =>
                _ = ImmutableDictionary<int, Probe>.Empty.Add(key: 1, value: stored).ToBuilder().ContainsValue(given),
            [("ImmutableSortedDictionary.Builder", "Add")] = static (stored, given) =>
            {
                ImmutableSortedDictionaryOf(stored).ToBuilder().Add(key: given, value: 2);
                ImmutableSortedDictionaryOf(stored).ToBuilder().Add(new KeyValuePair<Probe, int>(key: given, value: 2));
            },
            [("ImmutableSortedDictionary.Builder", "AddRange")] = static (stored, given) =>
                ImmutableSortedDictionaryOf(stored).ToBuilder().AddRange([new KeyValuePair<Probe, int>(key: given, value: 2)]),
            [("ImmutableSortedDictionary.Builder", "Contains")] = static (stored, given) =>
                _ = ImmutableSortedDictionaryOf(stored).ToBuilder().Contains(new KeyValuePair<Probe, int>(key: given, value: 1)),
            [("ImmutableSortedDictionary.Builder", "ContainsKey")] = static (stored, given) =>
                _ = ImmutableSortedDictionaryOf(stored).ToBuilder().ContainsKey(given),
            [("ImmutableSortedDictionary.Builder", "ContainsValue")] = static (stored, given) =>
                _ = ImmutableSortedDictionary<int, Probe>.Empty.Add(key: 1, value: stored).ToBuilder().ContainsValue(given),
            [("ImmutableSortedDictionary.Builder", "GetValueOrDefault")] = static (stored, given) =>
            {
                _ = ImmutableSortedDictionaryOf(stored).ToBuilder().GetValueOrDefault(given);
                _ = ImmutableSortedDictionaryOf(stored).ToBuilder().GetValueOrDefault(key: given, defaultValue: 2);
            },
            [("ImmutableSortedDictionary.Builder", "Remove")] = static (stored, given) =>
            {
                _ = ImmutableSortedDictionaryOf(stored).ToBuilder().Remove(given);
                _ = ImmutableSortedDictionaryOf(stored).ToBuilder().Remove(new KeyValuePair<Probe, int>(key: given, value: 1));
            },
            [("ImmutableSortedDictionary.Builder", "RemoveRange")] = static (stored, given) =>
                ImmutableSortedDictionaryOf(stored).ToBuilder().RemoveRange(ArrayOf(given)),
            [("ImmutableSortedDictionary.Builder", "TryGetKey")] = static (stored, given) =>
                _ = ImmutableSortedDictionaryOf(stored).ToBuilder().TryGetKey(equalKey: given, actualKey: out _),
            [("ImmutableSortedDictionary.Builder", "TryGetValue")] = static (stored, given) =>
                _ = ImmutableSortedDictionaryOf(stored).ToBuilder().TryGetValue(key: given, value: out _),
            [("ImmutableSortedDictionary.Builder", "ValueRef")] = static (stored, given) =>
                Try(() => _ = ImmutableSortedDictionaryOf(stored).ToBuilder().ValueRef(given)),
            [("ImmutableSortedDictionary.Builder", "get_Item")] = static (stored, given) =>
                Try(() => _ = ImmutableSortedDictionaryOf(stored).ToBuilder()[given]),
            [("ImmutableSortedDictionary.Builder", "set_Item")] = static (stored, given) =>
                ImmutableSortedDictionaryOf(stored).ToBuilder()[given] = 2,
            [("ImmutableList.Builder", "BinarySearch")] = static (stored, given) =>
                _ = ImmutableList.Create(stored).ToBuilder().BinarySearch(given),
            [("ImmutableList.Builder", "Contains")] = static (stored, given) =>
                _ = ImmutableList.Create(stored).ToBuilder().Contains(given),
            [("ImmutableList.Builder", "IndexOf")] = static (stored, given) =>
                _ = ImmutableList.Create(stored).ToBuilder().IndexOf(given),
            [("ImmutableList.Builder", "LastIndexOf")] = static (stored, given) =>
                _ = ImmutableList.Create(stored).ToBuilder().LastIndexOf(given),
            [("ImmutableList.Builder", "Remove")] = static (stored, given) =>
                _ = ImmutableList.Create(stored).ToBuilder().Remove(given),
            [("ImmutableList.Builder", "RemoveRange")] = static (stored, given) =>
                ImmutableList.Create(stored).ToBuilder().RemoveRange(ArrayOf(given)),
            [("ImmutableList.Builder", "Replace")] = static (stored, given) =>
                Try(() => ImmutableList.Create(stored).ToBuilder().Replace(oldValue: given, newValue: given)),
            [("ImmutableArray.Builder", "Contains")] = static (stored, given) =>
                _ = ImmutableArray.Create(stored).ToBuilder().Contains(given),
            [("ImmutableArray.Builder", "IndexOf")] = static (stored, given) =>
                _ = ImmutableArray.Create(stored).ToBuilder().IndexOf(given),
            [("ImmutableArray.Builder", "LastIndexOf")] = static (stored, given) =>
                _ = ImmutableArray.Create(stored).ToBuilder().LastIndexOf(given),
            [("ImmutableArray.Builder", "Remove")] = static (stored, given) =>
                _ = ImmutableArray.Create(stored).ToBuilder().Remove(given),
            [("ImmutableArray.Builder", "RemoveRange")] = static (stored, given) =>
                ImmutableArray.Create(stored).ToBuilder().RemoveRange(items: (IEnumerable<Probe>)ArrayOf(given)),
            [("ImmutableArray.Builder", "Replace")] = static (stored, given) =>
                Try(() => ImmutableArray.Create(stored).ToBuilder().Replace(oldValue: given, newValue: given)),
            [("OrderedDictionary", "Add")] = static (stored, given) =>
                OrderedDictionaryOf(stored).Add(key: given, value: 2),
            [("OrderedDictionary", "ContainsKey")] = static (stored, given) =>
                _ = OrderedDictionaryOf(stored).ContainsKey(given),
            [("OrderedDictionary", "ContainsValue")] = static (stored, given) =>
                _ = new OrderedDictionary<int, Probe> { { 1, stored } }.ContainsValue(given),
            [("OrderedDictionary", "IndexOf")] = static (stored, given) =>
                _ = OrderedDictionaryOf(stored).IndexOf(given),
            [("OrderedDictionary", "Insert")] = static (stored, given) =>
                OrderedDictionaryOf(stored).Insert(index: 0, key: given, value: 2),
            [("OrderedDictionary", "Remove")] = static (stored, given) => _ = OrderedDictionaryOf(stored).Remove(given),
            [("OrderedDictionary", "TryAdd")] = static (stored, given) =>
                _ = OrderedDictionaryOf(stored).TryAdd(key: given, value: 2),
            [("OrderedDictionary", "TryGetValue")] = static (stored, given) =>
                _ = OrderedDictionaryOf(stored).TryGetValue(key: given, value: out _),
            [("OrderedDictionary", "get_Item")] = static (stored, given) =>
                Try(() => _ = OrderedDictionaryOf(stored)[given]),
            [("OrderedDictionary", "set_Item")] = static (stored, given) => OrderedDictionaryOf(stored)[given] = 2,
            [("PriorityQueue", "DequeueEnqueue")] = static (stored, given) =>
                _ = QueueOfTwo(stored).DequeueEnqueue(element: 3, priority: given),
            [("PriorityQueue", "EnqueueDequeue")] = static (stored, given) =>
                _ = QueueOf(stored).EnqueueDequeue(element: 2, priority: given),
            [("PriorityQueue", "EnqueueRange")] = static (stored, given) => QueueOf(stored).EnqueueRange([(2, given)])
#endif
        };

#if NET
    // The same for the rows of an overload that has a parameter for a comparer. The type of the
    // objects is not IEquatable, thus the compiler takes that overload, and the code gives no
    // comparer. A span method and Remove of a queue have such a row, and .NET Framework has
    // neither.
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
                _ = SpanOf(stored).SequenceCompareTo(ArrayOf(given)),
            [("PriorityQueue", "Remove")] = static (stored, given) =>
            {
                PriorityQueue<PlainProbe, int> queue = new();
                queue.Enqueue(element: stored, priority: 1);
                _ = queue.Remove(element: given, removedElement: out _, priority: out _);
            }
        };
#endif

    [Test]
    public async Task EachRowNamesTheObjectThatGetsTheCall() =>
        await Assert.That(
                PlatformReceivers.ByMethod
                    .Where(static row => IsOnThisFramework(row.Key) && !IsASetOperation(row.Key))
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

    // The methods of a set that get a second sequence. The object that gets the call depends on the
    // type of that sequence. Thus, the test with one object on each side does not hold them.
    private static readonly HashSet<string> SetOperations =
    [
        "IsProperSubsetOf", "IsProperSupersetOf", "IsSubsetOf", "IsSupersetOf", "Overlaps", "SetEquals", "ExceptWith",
        "IntersectWith", "SymmetricExceptWith", "UnionWith", "Except", "Intersect", "SymmetricExcept", "Union"
    ];

    private static readonly HashSet<string> SetTypes =
        ["HashSet", "SortedSet", "ImmutableHashSet", "ImmutableSortedSet", "FrozenSet"];

    private static bool IsASetOperation((string Type, string Method) key) =>
        SetTypes.Contains(key.Type) && SetOperations.Contains(key.Method);

    // The sets that this target framework has, each made from the objects that it gets.
    private static readonly Dictionary<string, Func<Side[], IEnumerable<Side>>> Sets =
        new()
        {
            ["HashSet"] = static sides => new HashSet<Side>(sides),
            ["SortedSet"] = static sides => new SortedSet<Side>(sides),
#if NET
            ["ImmutableHashSet"] = static sides => ImmutableHashSet.CreateRange(sides),
            ["ImmutableSortedSet"] = static sides => ImmutableSortedSet.CreateRange(sides),
            ["FrozenSet"] = static sides => sides.ToFrozenSet()
#endif
        };

    // The numbers of objects in the set and in the argument. A set operation can end at the two
    // numbers alone, and compare nothing.
    private static readonly (int Stored, int Given)[] Counts = [(1, 1), (1, 2), (2, 1), (2, 2), (2, 3), (3, 2)];

    /// <summary>
    ///     Each set operation with a row gives each call as the row says. The
    ///     argument is an array and each type of set in turn, with one to three
    ///     objects on each side. A set of a type that
    ///     <see cref="PlatformReceivers.WithASetArgument" /> names has the receiver
    ///     from there.
    /// </summary>
    /// <remarks>
    ///     The objects record the side of the object that gets a call and the
    ///     side of its argument. Thus, the test tells a call between two given
    ///     values from a call between a given value and an element of the set.
    /// </remarks>
    [Test]
    public async Task EachSetOperationGivesEachCallAsItsRowSaysForEachKindOfArgument() =>
        await Assert.That(
                PlatformReceivers.ByMethod
                    .Where(static row => IsASetOperation(row.Key) && Sets.ContainsKey(row.Key.Type))
                    .SelectMany(static row => WrongForEachArgument(key: row.Key, receiver: row.Value)))
            .IsEmpty();

    [Test]
    public async Task EachRowForASetArgumentIsForAMethodWithARow() =>
        await Assert.That(
                PlatformReceivers.WithASetArgument.Keys.Where(static key =>
                    !PlatformReceivers.ByMethod.ContainsKey((key.Type, key.Method))))
            .IsEmpty();

#if NET
    // Each member with a row for the values of a dictionary. The dictionary has the first value
    // for a key, and the call gives the second value for the same key.
    private static readonly Dictionary<(string Type, string Method), Action<Value, Value>> CallsForValues =
        new()
        {
            [("ImmutableDictionary", "Add")] = static (stored, given) =>
                _ = ImmutableDictionaryOf(stored).Add(key: 1, value: given),
            [("ImmutableDictionary", "AddRange")] = static (stored, given) =>
                _ = ImmutableDictionaryOf(stored).AddRange([PairOf(given)]),
            [("ImmutableDictionary", "Contains")] = static (stored, given) =>
                _ = ImmutableDictionaryOf(stored).Contains(PairOf(given)),
            [("ImmutableDictionary", "CreateRange")] = static (stored, given) =>
                _ = ImmutableDictionary.CreateRange([PairOf(stored), PairOf(given)]),
            [("ImmutableDictionary", "SetItem")] = static (stored, given) =>
                _ = ImmutableDictionaryOf(stored).SetItem(key: 1, value: given),
            [("ImmutableDictionary", "SetItems")] = static (stored, given) =>
                _ = ImmutableDictionaryOf(stored).SetItems([PairOf(given)]),
            [("ImmutableDictionary", "ToImmutableDictionary")] = static (stored, given) =>
                _ = new[] { PairOf(stored), PairOf(given) }.ToImmutableDictionary(),
            [("ImmutableDictionary.Builder", "Add")] = static (stored, given) =>
                ImmutableDictionaryBuilderOf(stored).Add(key: 1, value: given),
            [("ImmutableDictionary.Builder", "AddRange")] = static (stored, given) =>
                ImmutableDictionaryBuilderOf(stored).AddRange([PairOf(given)]),
            [("ImmutableDictionary.Builder", "Contains")] = static (stored, given) =>
                _ = ImmutableDictionaryBuilderOf(stored).Contains(PairOf(given)),
            [("ImmutableDictionary.Builder", "Remove")] = static (stored, given) =>
                _ = ImmutableDictionaryBuilderOf(stored).Remove(PairOf(given)),
            [("ImmutableDictionary.Builder", "set_Item")] = static (stored, given) =>
                ImmutableDictionaryBuilderOf(stored)[1] = given,
            [("ImmutableSortedDictionary", "Add")] = static (stored, given) =>
                _ = ImmutableSortedDictionaryOf(stored).Add(key: 1, value: given),
            [("ImmutableSortedDictionary", "AddRange")] = static (stored, given) =>
                _ = ImmutableSortedDictionaryOf(stored).AddRange([PairOf(given)]),
            [("ImmutableSortedDictionary", "Contains")] = static (stored, given) =>
                _ = ImmutableSortedDictionaryOf(stored).Contains(PairOf(given)),
            [("ImmutableSortedDictionary", "CreateRange")] = static (stored, given) =>
                _ = ImmutableSortedDictionary.CreateRange([PairOf(stored), PairOf(given)]),
            [("ImmutableSortedDictionary", "SetItem")] = static (stored, given) =>
                _ = ImmutableSortedDictionaryOf(stored).SetItem(key: 1, value: given),
            [("ImmutableSortedDictionary", "SetItems")] = static (stored, given) =>
                _ = ImmutableSortedDictionaryOf(stored).SetItems([PairOf(given)]),
            [("ImmutableSortedDictionary", "ToImmutableSortedDictionary")] = static (stored, given) =>
                _ = new[] { PairOf(stored), PairOf(given) }.ToImmutableSortedDictionary(),
            [("ImmutableSortedDictionary.Builder", "Add")] = static (stored, given) =>
                ImmutableSortedDictionaryBuilderOf(stored).Add(key: 1, value: given),
            [("ImmutableSortedDictionary.Builder", "AddRange")] = static (stored, given) =>
                ImmutableSortedDictionaryBuilderOf(stored).AddRange([PairOf(given)]),
            [("ImmutableSortedDictionary.Builder", "Contains")] = static (stored, given) =>
                _ = ImmutableSortedDictionaryBuilderOf(stored).Contains(PairOf(given)),
            [("ImmutableSortedDictionary.Builder", "Remove")] = static (stored, given) =>
                _ = ImmutableSortedDictionaryBuilderOf(stored).Remove(PairOf(given)),
            [("ImmutableSortedDictionary.Builder", "set_Item")] = static (stored, given) =>
                ImmutableSortedDictionaryBuilderOf(stored)[1] = given
        };

    /// <summary>
    ///     Each row of <see cref="PlatformReceivers.ForValues" /> gives each call
    ///     as it says. The value that the dictionary has gets the call of
    ///     <c>Equals</c>, and the value that the call gives is its argument. A
    ///     member that also compares the value that it has with a value of its own
    ///     side has the receiver for that.
    /// </summary>
    [Test]
    public async Task EachRowForTheValuesOfADictionaryGivesEachCallAsItSays() =>
        await Assert.That(
                PlatformReceivers.ForValues.SelectMany(static row =>
                    WrongForTheValues(key: row.Key, receiver: row.Value)))
            .IsEmpty();

    private static IEnumerable<string> WrongForTheValues((string Type, string Method) key, PlatformReceiver receiver)
    {
        if (!CallsForValues.TryGetValue(key: key, value: out Action<Value, Value>? call))
        {
            yield return $"{key.Type}.{key.Method}: the test has no call for the row";

            yield break;
        }

        List<string> calls = [];
        call(arg1: new Value(side: Stored, calls: calls), arg2: new Value(side: Given, calls: calls));

        string[] permitted =
            receiver switch
            {
                PlatformReceiver.Stored => [$"{Stored}<-{Given}"],
                PlatformReceiver.StoredAlsoAmong => [$"{Stored}<-{Given}", $"{Stored}<-{Stored}"],
                PlatformReceiver.Given => [$"{Given}<-{Stored}"],
                _ => []
            };

        if (calls.Count == 0 || calls.Any(made => !permitted.Contains(made)))
        {
            yield return $"{key.Type}.{key.Method}: the receiver is {receiver}, and the calls were "
                         + $"[{string.Join(separator: ", ", values: calls.Distinct())}]";
        }
    }

    private static KeyValuePair<int, Value> PairOf(Value value) => new(key: 1, value: value);

    private static ImmutableDictionary<int, Value> ImmutableDictionaryOf(Value stored) =>
        ImmutableDictionary<int, Value>.Empty.Add(key: 1, value: stored);

    private static ImmutableSortedDictionary<int, Value> ImmutableSortedDictionaryOf(Value stored) =>
        ImmutableSortedDictionary<int, Value>.Empty.Add(key: 1, value: stored);

    private static ImmutableDictionary<int, Value>.Builder ImmutableDictionaryBuilderOf(Value stored) =>
        ImmutableDictionaryOf(stored).ToBuilder();

    private static ImmutableSortedDictionary<int, Value>.Builder ImmutableSortedDictionaryBuilderOf(Value stored) =>
        ImmutableSortedDictionaryOf(stored).ToBuilder();

    // A value of a dictionary that records each call of its Equals: its own side, and the side of
    // its argument. Two of them are always equal. Thus, a member that compares two values for one
    // key makes each of its calls, and refuses no value.
    private sealed class Value(in string side, in List<string> calls) : IEquatable<Value>
    {
        private readonly string side = side;
        private readonly List<string> calls = calls;

        public bool Equals(Value? other)
        {
            this.calls.Add($"{this.side}<-{other?.side}");

            return true;
        }

        public override bool Equals(object? obj) => obj is Value other && this.Equals(other);

        public override int GetHashCode() => 1;
    }
#endif

    // One line for each type of argument where a call is not one that the receiver permits. One
    // more line for an array as the argument when no number of objects makes a call.
    private static IEnumerable<string> WrongForEachArgument((string Type, string Method) key, PlatformReceiver receiver)
    {
        Func<Side[], IEnumerable<Side>> makeSet = Sets[key.Type];

        if (makeSet([]).GetType().GetMethod(name: key.Method, types: [typeof(IEnumerable<Side>)]) is not { } operation)
        {
            yield return $"{key.Type}.{key.Method}: the type has no such method for a sequence";

            yield break;
        }

        foreach (KeyValuePair<string, Func<Side[], IEnumerable<Side>>> argument in
                 Sets.Append(
                     new KeyValuePair<string, Func<Side[], IEnumerable<Side>>>(
                         key: "array",
                         value: static sides => sides)))
        {
            PlatformReceiver expected =
                PlatformReceivers.WithASetArgument.TryGetValue(
                    key: (key.Type, key.Method, argument.Key),
                    value: out PlatformReceiver forASet)
                    ? forASet
                    : receiver;

            HashSet<string> found = [];

            foreach ((int stored, int given) in Counts)
            {
                List<string> calls = [];
                IEnumerable<Side> set = makeSet(SidesOf(side: Stored, count: stored, calls: calls));
                IEnumerable<Side> other = argument.Value(SidesOf(side: Given, count: given, calls: calls));

                // The code that makes the two collections can make a call. That call is not the
                // one that the row is about.
                calls.Clear();
                _ = operation.Invoke(obj: set, parameters: [other]);
                found.UnionWith(calls);
            }

            string[] permitted =
                expected switch
                {
                    PlatformReceiver.Stored => [$"{Stored}<-{Given}"],
                    PlatformReceiver.Given => [$"{Given}<-{Stored}"],
                    PlatformReceiver.StoredAlsoAmong => [$"{Stored}<-{Given}", $"{Stored}<-{Stored}"],
                    PlatformReceiver.GivenAlsoAmong => [$"{Given}<-{Stored}", $"{Given}<-{Given}"],
                    _ => [.. found]
                };

            if (found.Any(call => !permitted.Contains(call)) || (argument.Key == "array" && found.Count == 0))
            {
                yield return $"{key.Type}.{key.Method} with {argument.Key}: the receiver is {expected}, and the calls "
                             + $"were [{string.Join(separator: ", ", values: found.OrderBy(static call => call))}]";
            }
        }
    }

    private static Side[] SidesOf(string side, int count, List<string> calls) =>
        [.. Enumerable.Range(start: 1, count: count).Select(rank => new Side(side: side, rank: rank, calls: calls))];

    // False for a row whose member this target framework does not have. .NET Framework has no
    // PriorityQueue, no OrderedDictionary, no CollectionExtensions, and no TryAdd of a dictionary.
    // It has the span methods, the immutable collections, and the frozen collections only from a
    // package, which these tests do not reference. The run of each row on .NET Framework used the
    // packages.
    private static bool IsOnThisFramework((string Type, string Method) key) =>
#if NET
        key.Type.Length > 0;
#else
        !NotOnNetFramework.Contains(key.Type) && key != ("Dictionary", "TryAdd");

    private static readonly HashSet<string> NotOnNetFramework =
    [
        "MemoryExtensions", "PriorityQueue", "OrderedDictionary", "CollectionExtensions", "ImmutableArray",
        "ImmutableList", "ImmutableHashSet", "ImmutableSortedSet", "ImmutableDictionary", "ImmutableSortedDictionary",
        "FrozenSet", "FrozenDictionary", "ImmutableArray.Builder", "ImmutableList.Builder", "ImmutableHashSet.Builder",
        "ImmutableSortedSet.Builder", "ImmutableDictionary.Builder", "ImmutableSortedDictionary.Builder"
    ];
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

    private static ArrayList ArrayListOf(Probe stored) => [stored];

    private static ConcurrentDictionary<Probe, int> ConcurrentOf(Probe stored)
    {
        ConcurrentDictionary<Probe, int> dictionary = new() { [stored] = 1 };
        stored.Forget();

        return dictionary;
    }

    private static Hashtable HashtableOf(Probe stored)
    {
        Hashtable table = new() { { stored, 1 } };
        stored.Forget();

        return table;
    }

#if NET
    private static FrozenSet<Probe> FrozenSetOf(Probe stored)
    {
        FrozenSet<Probe> set = ArrayOf(stored).ToFrozenSet();
        stored.Forget();

        return set;
    }

    private static FrozenDictionary<Probe, int> FrozenDictionaryOf(Probe stored)
    {
        FrozenDictionary<Probe, int> dictionary = DictionaryOf(stored).ToFrozenDictionary();
        stored.Forget();

        return dictionary;
    }

    private static ImmutableHashSet<Probe> ImmutableHashSetOf(Probe stored)
    {
        ImmutableHashSet<Probe> set = ImmutableHashSet.Create(stored);
        stored.Forget();

        return set;
    }

    private static ImmutableSortedSet<Probe> ImmutableSortedSetOf(Probe stored)
    {
        ImmutableSortedSet<Probe> set = ImmutableSortedSet.Create(stored);
        stored.Forget();

        return set;
    }

    private static ImmutableDictionary<Probe, int> ImmutableDictionaryOf(Probe stored)
    {
        ImmutableDictionary<Probe, int> dictionary = ImmutableDictionary<Probe, int>.Empty.Add(key: stored, value: 1);
        stored.Forget();

        return dictionary;
    }

    private static ImmutableSortedDictionary<Probe, int> ImmutableSortedDictionaryOf(Probe stored)
    {
        ImmutableSortedDictionary<Probe, int> dictionary =
            ImmutableSortedDictionary<Probe, int>.Empty.Add(key: stored, value: 1);

        stored.Forget();

        return dictionary;
    }

    private static OrderedDictionary<Probe, int> OrderedDictionaryOf(Probe stored)
    {
        OrderedDictionary<Probe, int> dictionary = new() { { stored, 1 } };
        stored.Forget();

        return dictionary;
    }

    // A queue whose one element has the first object as its priority.
    private static PriorityQueue<int, Probe> QueueOf(Probe stored)
    {
        PriorityQueue<int, Probe> queue = new();
        queue.Enqueue(element: 1, priority: stored);
        stored.Forget();

        return queue;
    }

    // A queue with two elements that have the first object as their priority. A dequeue with one
    // element compares nothing.
    private static PriorityQueue<int, Probe> QueueOfTwo(Probe stored)
    {
        PriorityQueue<int, Probe> queue = QueueOf(stored);
        queue.Enqueue(element: 2, priority: stored);
        stored.Forget();

        return queue;
    }
#endif

    // A getter that throws for a key that the collection does not have. It throws after the call
    // that this test records.
    private static void Try(Action read)
    {
        try
        {
            read();
        }
        catch (Exception exception) when (exception is KeyNotFoundException or ArgumentException)
        {
            // The record of the call is what the test reads.
        }
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
        : IEquatable<Probe>, IComparable<Probe>, IComparable
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

        // A Tuple object and an ArrayList compare with this form.
        public int CompareTo(object? obj) => this.CompareTo(obj as Probe);
    }

    // An object that records each call of its Equals and of its CompareTo: its own side, and the
    // side of its argument. Two of them are never equal, and each has the same hash code. The
    // objects of the set have the lower ranks.
    private sealed class Side(in string side, in int rank, in List<string> calls) : IEquatable<Side>, IComparable<Side>
    {
        private readonly string side = side;
        private readonly int rank = side == Stored ? rank : rank + 10;
        private readonly List<string> calls = calls;

        public bool Equals(Side? other)
        {
            this.calls.Add($"{this.side}<-{other?.side}");

            return ReferenceEquals(objA: this, objB: other);
        }

        public override bool Equals(object? obj) => obj is Side other && this.Equals(other);

        public override int GetHashCode() => 1;

        public int CompareTo(Side? other)
        {
            this.calls.Add($"{this.side}<-{other?.side}");

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
