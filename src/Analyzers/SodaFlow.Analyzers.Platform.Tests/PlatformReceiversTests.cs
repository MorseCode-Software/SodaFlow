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
            [("HashSet", "ExceptWith")] = static (stored, given) => SetOf(stored).ExceptWith(ArrayOf(given)),
            [("HashSet", "IntersectWith")] = static (stored, given) => SetOf(stored).IntersectWith(ArrayOf(given)),
            [("HashSet", "IsProperSupersetOf")] = static (stored, given) =>
                _ = SetOf(stored).IsProperSupersetOf(ArrayOf(given)),
            [("HashSet", "IsSubsetOf")] = static (stored, given) => _ = SetOf(stored).IsSubsetOf(ArrayOf(given)),
            [("HashSet", "IsSupersetOf")] = static (stored, given) => _ = SetOf(stored).IsSupersetOf(ArrayOf(given)),
            [("HashSet", "Overlaps")] = static (stored, given) => _ = SetOf(stored).Overlaps(ArrayOf(given)),
            [("HashSet", "SetEquals")] = static (stored, given) => _ = SetOf(stored).SetEquals(ArrayOf(given)),
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
            [("SortedSet", "ExceptWith")] = static (stored, given) => SortedSetOf(stored).ExceptWith(ArrayOf(given)),
            [("SortedSet", "GetViewBetween")] = static (stored, given) =>
                _ = SortedSetOf(stored).GetViewBetween(lowerValue: given, upperValue: given),
            [("SortedSet", "IntersectWith")] = static (stored, given) =>
                SortedSetOf(stored).IntersectWith(ArrayOf(given)),
            [("SortedSet", "IsProperSubsetOf")] = static (stored, given) =>
                _ = SortedSetOf(stored).IsProperSubsetOf(ArrayOf(given)),
            [("SortedSet", "IsProperSupersetOf")] = static (stored, given) =>
                _ = SortedSetOf(stored).IsProperSupersetOf(ArrayOf(given)),
            [("SortedSet", "IsSubsetOf")] = static (stored, given) =>
                _ = SortedSetOf(stored).IsSubsetOf(ArrayOf(given)),
            [("SortedSet", "IsSupersetOf")] = static (stored, given) =>
                _ = SortedSetOf(stored).IsSupersetOf(ArrayOf(given)),
            [("SortedSet", "Overlaps")] = static (stored, given) => _ = SortedSetOf(stored).Overlaps(ArrayOf(given)),
            [("SortedSet", "Remove")] = static (stored, given) => _ = SortedSetOf(stored).Remove(given),
            [("SortedSet", "SetEquals")] = static (stored, given) => _ = SortedSetOf(stored).SetEquals(ArrayOf(given)),
            [("SortedSet", "SymmetricExceptWith")] = static (stored, given) =>
                SortedSetOf(stored).SymmetricExceptWith(ArrayOf(given)),
            [("SortedSet", "TryGetValue")] = static (stored, given) =>
                _ = SortedSetOf(stored).TryGetValue(equalValue: given, actualValue: out _),
            [("SortedSet", "UnionWith")] = static (stored, given) => SortedSetOf(stored).UnionWith(ArrayOf(given)),
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
            [("FrozenSet", "IsProperSubsetOf")] = static (stored, given) =>
                _ = FrozenSetOf(stored).IsProperSubsetOf(ArrayOf(given)),
            [("FrozenSet", "IsProperSupersetOf")] = static (stored, given) =>
                _ = FrozenSetOf(stored).IsProperSupersetOf(ArrayOf(given)),
            [("FrozenSet", "IsSubsetOf")] = static (stored, given) =>
                _ = FrozenSetOf(stored).IsSubsetOf(ArrayOf(given)),
            [("FrozenSet", "IsSupersetOf")] = static (stored, given) =>
                _ = FrozenSetOf(stored).IsSupersetOf(ArrayOf(given)),
            [("FrozenSet", "Overlaps")] = static (stored, given) => _ = FrozenSetOf(stored).Overlaps(ArrayOf(given)),
            [("FrozenSet", "SetEquals")] = static (stored, given) => _ = FrozenSetOf(stored).SetEquals(ArrayOf(given)),
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
            [("ImmutableHashSet", "Intersect")] = static (stored, given) =>
                _ = ImmutableHashSetOf(stored).Intersect(ArrayOf(given)),
            [("ImmutableHashSet", "IsProperSupersetOf")] = static (stored, given) =>
                _ = ImmutableHashSetOf(stored).IsProperSupersetOf(ArrayOf(given)),
            [("ImmutableHashSet", "IsSubsetOf")] = static (stored, given) =>
                _ = ImmutableHashSetOf(stored).IsSubsetOf(ArrayOf(given)),
            [("ImmutableHashSet", "IsSupersetOf")] = static (stored, given) =>
                _ = ImmutableHashSetOf(stored).IsSupersetOf(ArrayOf(given)),
            [("ImmutableHashSet", "Overlaps")] = static (stored, given) =>
                _ = ImmutableHashSetOf(stored).Overlaps(ArrayOf(given)),
            [("ImmutableHashSet", "SetEquals")] = static (stored, given) =>
                _ = ImmutableHashSetOf(stored).SetEquals(ArrayOf(given)),
            [("ImmutableHashSet", "TryGetValue")] = static (stored, given) =>
                _ = ImmutableHashSetOf(stored).TryGetValue(equalValue: given, actualValue: out _),
            [("ImmutableHashSet", "Union")] = static (stored, given) =>
                _ = ImmutableHashSetOf(stored).Union(ArrayOf(given)),
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
            [("ImmutableSortedSet", "Except")] = static (stored, given) =>
                _ = ImmutableSortedSetOf(stored).Except(ArrayOf(given)),
            [("ImmutableSortedSet", "IndexOf")] = static (stored, given) =>
                _ = ImmutableSortedSetOf(stored).IndexOf(given),
            [("ImmutableSortedSet", "Intersect")] = static (stored, given) =>
                _ = ImmutableSortedSetOf(stored).Intersect(ArrayOf(given)),
            [("ImmutableSortedSet", "IsProperSupersetOf")] = static (stored, given) =>
                _ = ImmutableSortedSetOf(stored).IsProperSupersetOf(ArrayOf(given)),
            [("ImmutableSortedSet", "IsSubsetOf")] = static (stored, given) =>
                _ = ImmutableSortedSetOf(stored).IsSubsetOf(ArrayOf(given)),
            [("ImmutableSortedSet", "IsSupersetOf")] = static (stored, given) =>
                _ = ImmutableSortedSetOf(stored).IsSupersetOf(ArrayOf(given)),
            [("ImmutableSortedSet", "Overlaps")] = static (stored, given) =>
                _ = ImmutableSortedSetOf(stored).Overlaps(ArrayOf(given)),
            [("ImmutableSortedSet", "Remove")] = static (stored, given) =>
                _ = ImmutableSortedSetOf(stored).Remove(given),
            [("ImmutableSortedSet", "TryGetValue")] = static (stored, given) =>
                _ = ImmutableSortedSetOf(stored).TryGetValue(equalValue: given, actualValue: out _),
            [("ImmutableSortedSet", "Union")] = static (stored, given) =>
                _ = ImmutableSortedSetOf(stored).Union(ArrayOf(given)),
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
        "FrozenSet", "FrozenDictionary"
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
