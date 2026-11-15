using System.Collections.Generic;

namespace SodaFlow.Analyzers;

/// <summary>
///     Which of two objects gets the call, when a method of the platform calls
///     <c>Equals</c> or <c>CompareTo</c> of one of them with the other one.
/// </summary>
internal enum PlatformReceiver
{
    /// <summary>
    ///     The analyzer does not know. A sort is such a method. The object that
    ///     gets the call depends on the position, the number of elements, and the
    ///     data. The runtimes do not agree.
    /// </summary>
    Either,

    /// <summary>
    ///     An object that the collection has. The value that the code gives is
    ///     the argument. For a method that gets two values and no collection, it is
    ///     the first value.
    /// </summary>
    Stored,

    /// <summary>
    ///     The value that the code gives. An object that the collection has is
    ///     the argument.
    /// </summary>
    Given,

    /// <summary>
    ///     The first of the two values that a comparer of the platform gets.
    /// </summary>
    FirstValue,

    /// <summary>
    ///     An object that the collection has. The argument is the value that the
    ///     code gives, or a second object that the collection has.
    ///     <c>IntersectWith</c> of a <c>HashSet</c> removes an element, and that
    ///     compares its elements with each other.
    /// </summary>
    StoredAlsoAmong,

    /// <summary>
    ///     A value that the code gives. The argument is an object that the
    ///     collection has, or a second value that the code gives. <c>Union</c> of
    ///     an immutable set makes a set from the values first, and that compares
    ///     them with each other.
    /// </summary>
    GivenAlsoAmong
}

/// <summary>
///     The methods of the platform for which each runtime gives the call to the
///     same one of the two objects. A method that is not here has
///     <see cref="PlatformReceiver.Either" />.
/// </summary>
/// <remarks>
///     <para>
///         The documentation of the platform does not say which object gets the
///         call. Each row is here because a run showed it on .NET Framework and on
///         each .NET from 6 to 11. A runtime that does not have the method has no
///         run. A test runs each row on each target framework of the tests, and
///         fails when a runtime changes.
///     </para>
///     <para>
///         A row is for each overload of the method that gets no comparer. Thus, a
///         method whose overloads do not agree has no row here.
///         <c>IndexOfAny</c> of a span is such a method. It gives the call to the
///         value for two values, and to the element for a span of values.
///         <see cref="PlatformReceivers.WithValuesOneByOne" /> has the row for
///         the first of the two.
///     </para>
    ///     <para>
///         A builder of an immutable collection has the name of its collection
///         before the name <c>Builder</c>. A member of a builder that is not here
///         has no row for a cause. A builder with hash codes gives the call in
///         its lookups and in <c>Remove</c> to one of the two objects. That is
///         its own object when that object is the first one with its hash code.
///         Otherwise, it is the object that the code gives. The set operations
///         of a builder have no run for each type of argument, and
///         <c>SetEquals</c> changed on .NET 11.
///     </para>
///     <para>
///         This file uses no type of the compiler, because that test compiles it
///         for .NET Framework also.
///     </para>
/// </remarks>
internal static class PlatformReceivers
{
    /// <summary>The receiver for each type name and method name.</summary>
    public static readonly IReadOnlyDictionary<(string Type, string Method), PlatformReceiver> ByMethod =
        new Dictionary<(string Type, string Method), PlatformReceiver>
        {
            [("Array", "IndexOf")] = PlatformReceiver.Stored,
            [("Array", "BinarySearch")] = PlatformReceiver.Stored,
            [("List", "Contains")] = PlatformReceiver.Stored,
            [("List", "IndexOf")] = PlatformReceiver.Stored,
            [("List", "LastIndexOf")] = PlatformReceiver.Stored,
            [("List", "Remove")] = PlatformReceiver.Stored,
            [("List", "BinarySearch")] = PlatformReceiver.Stored,
            [("Enumerable", "Contains")] = PlatformReceiver.Stored,
            [("Enumerable", "SequenceEqual")] = PlatformReceiver.Stored,
            [("HashSet", "Add")] = PlatformReceiver.Stored,
            [("HashSet", "Contains")] = PlatformReceiver.Stored,
            [("HashSet", "Remove")] = PlatformReceiver.Stored,
            [("Dictionary", "Add")] = PlatformReceiver.Stored,
            [("Dictionary", "TryAdd")] = PlatformReceiver.Stored,
            [("Dictionary", "ContainsKey")] = PlatformReceiver.Stored,
            [("Dictionary", "TryGetValue")] = PlatformReceiver.Stored,
            [("Dictionary", "Remove")] = PlatformReceiver.Stored,
            [("Dictionary", "get_Item")] = PlatformReceiver.Stored,
            [("Dictionary", "set_Item")] = PlatformReceiver.Stored,
            [("SortedList", "Add")] = PlatformReceiver.Stored,
            [("Object", "Equals")] = PlatformReceiver.Stored,
            [("ValueTuple", "Equals")] = PlatformReceiver.Stored,
            [("ValueTuple", "CompareTo")] = PlatformReceiver.Stored,
            [("SortedSet", "Add")] = PlatformReceiver.Given,
            [("SortedSet", "Contains")] = PlatformReceiver.Given,
            [("SortedDictionary", "Add")] = PlatformReceiver.Given,
            [("SortedDictionary", "set_Item")] = PlatformReceiver.Given,
            [("PriorityQueue", "Enqueue")] = PlatformReceiver.Given,
            [("MemoryExtensions", "Contains")] = PlatformReceiver.Given,
            [("MemoryExtensions", "IndexOf")] = PlatformReceiver.Given,
            [("MemoryExtensions", "LastIndexOf")] = PlatformReceiver.Given,
            [("MemoryExtensions", "Count")] = PlatformReceiver.Given,
            [("MemoryExtensions", "Replace")] = PlatformReceiver.Given,
            [("MemoryExtensions", "Trim")] = PlatformReceiver.Given,
            [("MemoryExtensions", "TrimStart")] = PlatformReceiver.Given,
            [("MemoryExtensions", "TrimEnd")] = PlatformReceiver.Given,
            [("MemoryExtensions", "BinarySearch")] = PlatformReceiver.Given,
            [("MemoryExtensions", "IndexOfAnyInRange")] = PlatformReceiver.Given,
            [("MemoryExtensions", "LastIndexOfAnyInRange")] = PlatformReceiver.Given,
            [("MemoryExtensions", "IndexOfAnyExceptInRange")] = PlatformReceiver.Given,
            [("MemoryExtensions", "LastIndexOfAnyExceptInRange")] = PlatformReceiver.Given,
            [("MemoryExtensions", "ContainsAnyInRange")] = PlatformReceiver.Given,
            [("MemoryExtensions", "ContainsAnyExceptInRange")] = PlatformReceiver.Given,
            [("MemoryExtensions", "IndexOfAnyExcept")] = PlatformReceiver.Stored,
            [("MemoryExtensions", "LastIndexOfAnyExcept")] = PlatformReceiver.Stored,
            [("MemoryExtensions", "ContainsAnyExcept")] = PlatformReceiver.Stored,
            [("MemoryExtensions", "CountAny")] = PlatformReceiver.Stored,
            [("MemoryExtensions", "SequenceEqual")] = PlatformReceiver.Stored,
            [("MemoryExtensions", "StartsWith")] = PlatformReceiver.Stored,
            [("MemoryExtensions", "EndsWith")] = PlatformReceiver.Stored,
            [("MemoryExtensions", "CommonPrefixLength")] = PlatformReceiver.Stored,
            [("MemoryExtensions", "SequenceCompareTo")] = PlatformReceiver.Stored,
            [("Comparer", "Compare")] = PlatformReceiver.FirstValue,
            [("EqualityComparer", "Equals")] = PlatformReceiver.FirstValue,
            [("Array", "LastIndexOf")] = PlatformReceiver.Stored,
            [("ArrayList", "BinarySearch")] = PlatformReceiver.Stored,
            [("ArrayList", "Contains")] = PlatformReceiver.Stored,
            [("ArrayList", "IndexOf")] = PlatformReceiver.Stored,
            [("ArrayList", "LastIndexOf")] = PlatformReceiver.Stored,
            [("ArrayList", "Remove")] = PlatformReceiver.Stored,
            [("Collection", "Contains")] = PlatformReceiver.Stored,
            [("Collection", "IndexOf")] = PlatformReceiver.Stored,
            [("Collection", "Remove")] = PlatformReceiver.Stored,
            [("CollectionExtensions", "GetValueOrDefault")] = PlatformReceiver.Stored,
            [("CollectionExtensions", "Remove")] = PlatformReceiver.Stored,
            [("CollectionExtensions", "TryAdd")] = PlatformReceiver.Stored,
            [("ConcurrentDictionary", "AddOrUpdate")] = PlatformReceiver.Stored,
            [("ConcurrentDictionary", "ContainsKey")] = PlatformReceiver.Stored,
            [("ConcurrentDictionary", "GetOrAdd")] = PlatformReceiver.Stored,
            [("ConcurrentDictionary", "TryAdd")] = PlatformReceiver.Stored,
            [("ConcurrentDictionary", "TryGetValue")] = PlatformReceiver.Stored,
            [("ConcurrentDictionary", "TryRemove")] = PlatformReceiver.Stored,
            [("ConcurrentDictionary", "TryUpdate")] = PlatformReceiver.Stored,
            [("ConcurrentDictionary", "get_Item")] = PlatformReceiver.Stored,
            [("ConcurrentDictionary", "set_Item")] = PlatformReceiver.Stored,
            [("Dictionary", "ContainsValue")] = PlatformReceiver.Stored,
            [("Enumerable", "Intersect")] = PlatformReceiver.Given,
            [("FrozenDictionary", "ContainsKey")] = PlatformReceiver.Stored,
            [("FrozenDictionary", "GetValueRefOrNullRef")] = PlatformReceiver.Stored,
            [("FrozenDictionary", "TryGetValue")] = PlatformReceiver.Stored,
            [("FrozenDictionary", "get_Item")] = PlatformReceiver.Stored,
            [("FrozenSet", "Contains")] = PlatformReceiver.Given,
            [("FrozenSet", "IsProperSubsetOf")] = PlatformReceiver.Given,
            [("FrozenSet", "IsProperSupersetOf")] = PlatformReceiver.Given,
            [("FrozenSet", "IsSubsetOf")] = PlatformReceiver.Given,
            [("FrozenSet", "IsSupersetOf")] = PlatformReceiver.Given,
            [("FrozenSet", "Overlaps")] = PlatformReceiver.Given,
            [("FrozenSet", "SetEquals")] = PlatformReceiver.Given,
            [("FrozenSet", "TryGetValue")] = PlatformReceiver.Given,
            [("HashSet", "ExceptWith")] = PlatformReceiver.Stored,
            [("HashSet", "IntersectWith")] = PlatformReceiver.StoredAlsoAmong,
            [("HashSet", "IsProperSubsetOf")] = PlatformReceiver.Stored,
            [("HashSet", "IsProperSupersetOf")] = PlatformReceiver.Stored,
            [("HashSet", "IsSubsetOf")] = PlatformReceiver.Stored,
            [("HashSet", "IsSupersetOf")] = PlatformReceiver.Stored,
            [("HashSet", "Overlaps")] = PlatformReceiver.Stored,
            [("HashSet", "SetEquals")] = PlatformReceiver.Stored,
            [("HashSet", "TryGetValue")] = PlatformReceiver.Stored,
            [("Hashtable", "Add")] = PlatformReceiver.Stored,
            [("Hashtable", "Contains")] = PlatformReceiver.Stored,
            [("Hashtable", "ContainsKey")] = PlatformReceiver.Stored,
            [("Hashtable", "ContainsValue")] = PlatformReceiver.Stored,
            [("Hashtable", "Remove")] = PlatformReceiver.Stored,
            [("Hashtable", "get_Item")] = PlatformReceiver.Stored,
            [("Hashtable", "set_Item")] = PlatformReceiver.Stored,
            [("ImmutableArray", "Contains")] = PlatformReceiver.Stored,
            [("ImmutableArray", "IndexOf")] = PlatformReceiver.Stored,
            [("ImmutableArray", "LastIndexOf")] = PlatformReceiver.Stored,
            [("ImmutableArray", "Remove")] = PlatformReceiver.Stored,
            [("ImmutableArray", "RemoveRange")] = PlatformReceiver.Stored,
            [("ImmutableArray", "Replace")] = PlatformReceiver.Stored,
            [("ImmutableDictionary", "Add")] = PlatformReceiver.Given,
            [("ImmutableDictionary", "ContainsValue")] = PlatformReceiver.Given,
            [("ImmutableDictionary", "SetItem")] = PlatformReceiver.Given,
            [("ImmutableHashSet", "Add")] = PlatformReceiver.Given,
            [("ImmutableHashSet", "Contains")] = PlatformReceiver.Given,
            [("ImmutableHashSet", "Intersect")] = PlatformReceiver.Given,
            [("ImmutableHashSet", "IsProperSubsetOf")] = PlatformReceiver.GivenAlsoAmong,
            [("ImmutableHashSet", "IsProperSupersetOf")] = PlatformReceiver.Given,
            [("ImmutableHashSet", "IsSubsetOf")] = PlatformReceiver.GivenAlsoAmong,
            [("ImmutableHashSet", "IsSupersetOf")] = PlatformReceiver.Given,
            [("ImmutableHashSet", "Overlaps")] = PlatformReceiver.Given,
            [("ImmutableHashSet", "SetEquals")] = PlatformReceiver.GivenAlsoAmong,
            [("ImmutableHashSet", "TryGetValue")] = PlatformReceiver.Given,
            [("ImmutableHashSet", "Union")] = PlatformReceiver.GivenAlsoAmong,
            [("ImmutableList", "BinarySearch")] = PlatformReceiver.Given,
            [("ImmutableList", "Contains")] = PlatformReceiver.Given,
            [("ImmutableList", "IndexOf")] = PlatformReceiver.Given,
            [("ImmutableList", "Remove")] = PlatformReceiver.Given,
            [("ImmutableList", "RemoveRange")] = PlatformReceiver.Given,
            [("ImmutableList", "Replace")] = PlatformReceiver.Given,
            [("ImmutableSortedDictionary", "Add")] = PlatformReceiver.Given,
            [("ImmutableSortedDictionary", "AddRange")] = PlatformReceiver.Given,
            [("ImmutableSortedDictionary", "Contains")] = PlatformReceiver.Given,
            [("ImmutableSortedDictionary", "ContainsKey")] = PlatformReceiver.Given,
            [("ImmutableSortedDictionary", "ContainsValue")] = PlatformReceiver.Given,
            [("ImmutableSortedDictionary", "Remove")] = PlatformReceiver.Given,
            [("ImmutableSortedDictionary", "RemoveRange")] = PlatformReceiver.Given,
            [("ImmutableSortedDictionary", "SetItem")] = PlatformReceiver.Given,
            [("ImmutableSortedDictionary", "SetItems")] = PlatformReceiver.Given,
            [("ImmutableSortedDictionary", "TryGetKey")] = PlatformReceiver.Given,
            [("ImmutableSortedDictionary", "TryGetValue")] = PlatformReceiver.Given,
            [("ImmutableSortedDictionary", "ValueRef")] = PlatformReceiver.Given,
            [("ImmutableSortedDictionary", "get_Item")] = PlatformReceiver.Given,
            [("ImmutableSortedSet", "Add")] = PlatformReceiver.Given,
            [("ImmutableSortedSet", "Contains")] = PlatformReceiver.Given,
            [("ImmutableSortedSet", "Except")] = PlatformReceiver.Given,
            [("ImmutableSortedSet", "IndexOf")] = PlatformReceiver.Given,
            [("ImmutableSortedSet", "Intersect")] = PlatformReceiver.Given,
            [("ImmutableSortedSet", "IsProperSubsetOf")] = PlatformReceiver.GivenAlsoAmong,
            [("ImmutableSortedSet", "IsProperSupersetOf")] = PlatformReceiver.Given,
            [("ImmutableSortedSet", "IsSubsetOf")] = PlatformReceiver.GivenAlsoAmong,
            [("ImmutableSortedSet", "IsSupersetOf")] = PlatformReceiver.Given,
            [("ImmutableSortedSet", "Overlaps")] = PlatformReceiver.Given,
            [("ImmutableSortedSet", "Remove")] = PlatformReceiver.Given,
            [("ImmutableSortedSet", "TryGetValue")] = PlatformReceiver.Given,
            [("ImmutableSortedSet", "Union")] = PlatformReceiver.GivenAlsoAmong,
            [("ImmutableHashSet.Builder", "Add")] = PlatformReceiver.Given,
            [("ImmutableHashSet.Builder", "Contains")] = PlatformReceiver.Given,
            [("ImmutableHashSet.Builder", "TryGetValue")] = PlatformReceiver.Given,
            [("ImmutableSortedSet.Builder", "Add")] = PlatformReceiver.Given,
            [("ImmutableSortedSet.Builder", "Contains")] = PlatformReceiver.Given,
            [("ImmutableSortedSet.Builder", "IndexOf")] = PlatformReceiver.Given,
            [("ImmutableSortedSet.Builder", "Remove")] = PlatformReceiver.Given,
            [("ImmutableSortedSet.Builder", "TryGetValue")] = PlatformReceiver.Given,
            [("ImmutableDictionary.Builder", "Add")] = PlatformReceiver.Given,
            [("ImmutableDictionary.Builder", "ContainsValue")] = PlatformReceiver.Given,
            [("ImmutableSortedDictionary.Builder", "Add")] = PlatformReceiver.Given,
            [("ImmutableSortedDictionary.Builder", "AddRange")] = PlatformReceiver.Given,
            [("ImmutableSortedDictionary.Builder", "Contains")] = PlatformReceiver.Given,
            [("ImmutableSortedDictionary.Builder", "ContainsKey")] = PlatformReceiver.Given,
            [("ImmutableSortedDictionary.Builder", "ContainsValue")] = PlatformReceiver.Given,
            [("ImmutableSortedDictionary.Builder", "GetValueOrDefault")] = PlatformReceiver.Given,
            [("ImmutableSortedDictionary.Builder", "Remove")] = PlatformReceiver.Given,
            [("ImmutableSortedDictionary.Builder", "RemoveRange")] = PlatformReceiver.Given,
            [("ImmutableSortedDictionary.Builder", "TryGetKey")] = PlatformReceiver.Given,
            [("ImmutableSortedDictionary.Builder", "TryGetValue")] = PlatformReceiver.Given,
            [("ImmutableSortedDictionary.Builder", "ValueRef")] = PlatformReceiver.Given,
            [("ImmutableSortedDictionary.Builder", "get_Item")] = PlatformReceiver.Given,
            [("ImmutableSortedDictionary.Builder", "set_Item")] = PlatformReceiver.Given,
            [("ImmutableList.Builder", "BinarySearch")] = PlatformReceiver.Given,
            [("ImmutableList.Builder", "Contains")] = PlatformReceiver.Given,
            [("ImmutableList.Builder", "IndexOf")] = PlatformReceiver.Given,
            [("ImmutableList.Builder", "LastIndexOf")] = PlatformReceiver.Given,
            [("ImmutableList.Builder", "Remove")] = PlatformReceiver.Given,
            [("ImmutableList.Builder", "RemoveRange")] = PlatformReceiver.Given,
            [("ImmutableList.Builder", "Replace")] = PlatformReceiver.Given,
            [("ImmutableArray.Builder", "Contains")] = PlatformReceiver.Stored,
            [("ImmutableArray.Builder", "IndexOf")] = PlatformReceiver.Stored,
            [("ImmutableArray.Builder", "LastIndexOf")] = PlatformReceiver.Stored,
            [("ImmutableArray.Builder", "Remove")] = PlatformReceiver.Stored,
            [("ImmutableArray.Builder", "RemoveRange")] = PlatformReceiver.Stored,
            [("ImmutableArray.Builder", "Replace")] = PlatformReceiver.Stored,
            [("LinkedList", "Contains")] = PlatformReceiver.Stored,
            [("LinkedList", "Find")] = PlatformReceiver.Stored,
            [("LinkedList", "FindLast")] = PlatformReceiver.Stored,
            [("LinkedList", "Remove")] = PlatformReceiver.Stored,
            [("OrderedDictionary", "Add")] = PlatformReceiver.Stored,
            [("OrderedDictionary", "ContainsKey")] = PlatformReceiver.Stored,
            [("OrderedDictionary", "ContainsValue")] = PlatformReceiver.Given,
            [("OrderedDictionary", "IndexOf")] = PlatformReceiver.Stored,
            [("OrderedDictionary", "Insert")] = PlatformReceiver.Stored,
            [("OrderedDictionary", "Remove")] = PlatformReceiver.Stored,
            [("OrderedDictionary", "TryAdd")] = PlatformReceiver.Stored,
            [("OrderedDictionary", "TryGetValue")] = PlatformReceiver.Stored,
            [("OrderedDictionary", "get_Item")] = PlatformReceiver.Stored,
            [("OrderedDictionary", "set_Item")] = PlatformReceiver.Stored,
            [("PriorityQueue", "DequeueEnqueue")] = PlatformReceiver.Given,
            [("PriorityQueue", "EnqueueDequeue")] = PlatformReceiver.Given,
            [("PriorityQueue", "EnqueueRange")] = PlatformReceiver.Given,
            [("Queue", "Contains")] = PlatformReceiver.Stored,
            [("ReadOnlyCollection", "Contains")] = PlatformReceiver.Stored,
            [("ReadOnlyCollection", "IndexOf")] = PlatformReceiver.Stored,
            [("ReadOnlyDictionary", "ContainsKey")] = PlatformReceiver.Stored,
            [("ReadOnlyDictionary", "TryGetValue")] = PlatformReceiver.Stored,
            [("ReadOnlyDictionary", "get_Item")] = PlatformReceiver.Stored,
            [("SortedDictionary", "ContainsKey")] = PlatformReceiver.Given,
            [("SortedDictionary", "ContainsValue")] = PlatformReceiver.Stored,
            [("SortedDictionary", "Remove")] = PlatformReceiver.Given,
            [("SortedDictionary", "TryGetValue")] = PlatformReceiver.Given,
            [("SortedDictionary", "get_Item")] = PlatformReceiver.Given,
            [("SortedList", "ContainsKey")] = PlatformReceiver.Stored,
            [("SortedList", "ContainsValue")] = PlatformReceiver.Stored,
            [("SortedList", "IndexOfKey")] = PlatformReceiver.Stored,
            [("SortedList", "IndexOfValue")] = PlatformReceiver.Stored,
            [("SortedList", "Remove")] = PlatformReceiver.Stored,
            [("SortedList", "TryGetValue")] = PlatformReceiver.Stored,
            [("SortedList", "get_Item")] = PlatformReceiver.Stored,
            [("SortedList", "set_Item")] = PlatformReceiver.Stored,
            [("SortedSet", "ExceptWith")] = PlatformReceiver.Given,
            [("SortedSet", "GetViewBetween")] = PlatformReceiver.Given,
            [("SortedSet", "IntersectWith")] = PlatformReceiver.Given,
            [("SortedSet", "IsProperSubsetOf")] = PlatformReceiver.Given,
            [("SortedSet", "IsProperSupersetOf")] = PlatformReceiver.Given,
            [("SortedSet", "IsSubsetOf")] = PlatformReceiver.Given,
            [("SortedSet", "IsSupersetOf")] = PlatformReceiver.Given,
            [("SortedSet", "Overlaps")] = PlatformReceiver.Given,
            [("SortedSet", "Remove")] = PlatformReceiver.Given,
            [("SortedSet", "SetEquals")] = PlatformReceiver.Given,
            [("SortedSet", "SymmetricExceptWith")] = PlatformReceiver.GivenAlsoAmong,
            [("SortedSet", "TryGetValue")] = PlatformReceiver.Given,
            [("SortedSet", "UnionWith")] = PlatformReceiver.GivenAlsoAmong,
            [("Stack", "Contains")] = PlatformReceiver.Stored,
            [("Tuple", "CompareTo")] = PlatformReceiver.Stored,
            [("Tuple", "Equals")] = PlatformReceiver.Stored
        };

    /// <summary>
    ///     The receiver of a set operation for an argument that is a set of the
    ///     type with the given name. The argument must have the comparer of the
    ///     set of the call. The platform then uses a different path, and a row of
    ///     <see cref="ByMethod" /> does not hold. <see cref="PlatformReceiver.Either" />
    ///     is here for a path that differs between the runtimes, or that gives
    ///     the call to each side.
    /// </summary>
    /// <remarks>
    ///     A run called each set operation of each set type, with one to three
    ///     objects on each side. Its argument was an array, a list, a query, and
    ///     each set type in turn. An argument that is not here, and a set with a
    ///     different comparer, has the row of <see cref="ByMethod" />.
    /// </remarks>
    public static readonly IReadOnlyDictionary<(string Type, string Method, string Argument), PlatformReceiver>
        WithASetArgument =
            new Dictionary<(string Type, string Method, string Argument), PlatformReceiver>
            {
                [("HashSet", "IsProperSubsetOf", "HashSet")] = PlatformReceiver.Given,
                [("HashSet", "IsSubsetOf", "HashSet")] = PlatformReceiver.Given,
                [("HashSet", "SetEquals", "HashSet")] = PlatformReceiver.Either,
                [("HashSet", "IntersectWith", "HashSet")] = PlatformReceiver.Either,
                [("SortedSet", "IsProperSubsetOf", "SortedSet")] = PlatformReceiver.StoredAlsoAmong,
                [("SortedSet", "IsProperSupersetOf", "SortedSet")] = PlatformReceiver.GivenAlsoAmong,
                [("SortedSet", "IsSubsetOf", "SortedSet")] = PlatformReceiver.StoredAlsoAmong,
                [("SortedSet", "IsSupersetOf", "SortedSet")] = PlatformReceiver.GivenAlsoAmong,
                [("SortedSet", "Overlaps", "SortedSet")] = PlatformReceiver.Stored,
                [("SortedSet", "SetEquals", "SortedSet")] = PlatformReceiver.Stored,
                [("SortedSet", "UnionWith", "SortedSet")] = PlatformReceiver.Either,
                [("ImmutableHashSet", "SetEquals", "ImmutableHashSet")] = PlatformReceiver.Either,
                [("ImmutableSortedSet", "Union", "ImmutableSortedSet")] = PlatformReceiver.Either,
                [("FrozenSet", "IsProperSubsetOf", "ImmutableHashSet")] = PlatformReceiver.Either,
                [("FrozenSet", "IsProperSubsetOf", "FrozenSet")] = PlatformReceiver.Either,
                [("FrozenSet", "IsSubsetOf", "ImmutableHashSet")] = PlatformReceiver.Either,
                [("FrozenSet", "IsSubsetOf", "FrozenSet")] = PlatformReceiver.Either
            };

    /// <summary>
    ///     The receiver for the values of a dictionary. An immutable dictionary
    ///     that gets a key that it has compares two values with <c>Equals</c>.
    ///     They are the value that it has, and the value that the call gives. A
    ///     member that is here makes that call beside the calls of its row for the
    ///     keys.
    /// </summary>
    /// <remarks>
    ///     A run called each member of the immutable dictionaries, of their
    ///     builders, and of their factories. It ran on .NET Framework 4.8 and on
    ///     each .NET from 6 to 11. A member of a builder has the name of its dictionary
    ///     before the name <c>Builder</c>. A row is for the overloads that get a
    ///     value or a pair: <c>Remove</c> of a key compares no value. A factory
    ///     has no dictionary before the call, and its receiver says which value
    ///     of its sequence gets the call: the one that comes first.
    /// </remarks>
    public static readonly IReadOnlyDictionary<(string Type, string Method), PlatformReceiver> ForValues =
        new Dictionary<(string Type, string Method), PlatformReceiver>
        {
            [("ImmutableDictionary", "Add")] = PlatformReceiver.Stored,
            [("ImmutableDictionary", "AddRange")] = PlatformReceiver.StoredAlsoAmong,
            [("ImmutableDictionary", "Contains")] = PlatformReceiver.Stored,
            [("ImmutableDictionary", "CreateRange")] = PlatformReceiver.StoredAlsoAmong,
            [("ImmutableDictionary", "SetItem")] = PlatformReceiver.Stored,
            [("ImmutableDictionary", "SetItems")] = PlatformReceiver.Stored,
            [("ImmutableDictionary", "ToImmutableDictionary")] = PlatformReceiver.StoredAlsoAmong,
            [("ImmutableDictionary.Builder", "Add")] = PlatformReceiver.Stored,
            [("ImmutableDictionary.Builder", "AddRange")] = PlatformReceiver.StoredAlsoAmong,
            [("ImmutableDictionary.Builder", "Contains")] = PlatformReceiver.Stored,
            [("ImmutableDictionary.Builder", "Remove")] = PlatformReceiver.Stored,
            [("ImmutableDictionary.Builder", "set_Item")] = PlatformReceiver.Stored,
            [("ImmutableSortedDictionary", "Add")] = PlatformReceiver.Stored,
            [("ImmutableSortedDictionary", "AddRange")] = PlatformReceiver.Stored,
            [("ImmutableSortedDictionary", "Contains")] = PlatformReceiver.Stored,
            [("ImmutableSortedDictionary", "CreateRange")] = PlatformReceiver.Stored,
            [("ImmutableSortedDictionary", "SetItem")] = PlatformReceiver.Stored,
            [("ImmutableSortedDictionary", "SetItems")] = PlatformReceiver.Stored,
            [("ImmutableSortedDictionary", "ToImmutableSortedDictionary")] = PlatformReceiver.Stored,
            [("ImmutableSortedDictionary.Builder", "Add")] = PlatformReceiver.Stored,
            [("ImmutableSortedDictionary.Builder", "AddRange")] = PlatformReceiver.Stored,
            [("ImmutableSortedDictionary.Builder", "Contains")] = PlatformReceiver.Stored,
            [("ImmutableSortedDictionary.Builder", "Remove")] = PlatformReceiver.Stored,
            [("ImmutableSortedDictionary.Builder", "set_Item")] = PlatformReceiver.Stored
        };

    /// <summary>
    ///     The receiver for an overload that gets its values one by one. The
    ///     second parameter of such an overload is one value. A row is here when
    ///     the overload with a span of values gives the call to a different
    ///     object. It is here also when that overload differs between the
    ///     runtimes.
    /// </summary>
    public static readonly IReadOnlyDictionary<(string Type, string Method), PlatformReceiver> WithValuesOneByOne =
        new Dictionary<(string Type, string Method), PlatformReceiver>
        {
            [("MemoryExtensions", "IndexOfAny")] = PlatformReceiver.Given,
            [("MemoryExtensions", "LastIndexOfAny")] = PlatformReceiver.Given,
            [("MemoryExtensions", "ContainsAny")] = PlatformReceiver.Given
        };

    /// <summary>
    ///     The receiver for an overload that has a parameter for a comparer, when
    ///     the code gives no comparer. A span method of .NET 10 has such an
    ///     overload for an element type that is not <c>IEquatable</c>. It gives
    ///     the call to the element that the first span has, where the overload
    ///     for an <c>IEquatable</c> type can give it to the value. The one
    ///     overload of <c>Remove</c> of a queue has an optional comparer.
    /// </summary>
    public static readonly IReadOnlyDictionary<(string Type, string Method), PlatformReceiver> WithNoComparerGiven =
        new Dictionary<(string Type, string Method), PlatformReceiver>
        {
            [("MemoryExtensions", "Contains")] = PlatformReceiver.Stored,
            [("MemoryExtensions", "ContainsAny")] = PlatformReceiver.Stored,
            [("MemoryExtensions", "ContainsAnyExcept")] = PlatformReceiver.Stored,
            [("MemoryExtensions", "IndexOf")] = PlatformReceiver.Stored,
            [("MemoryExtensions", "LastIndexOf")] = PlatformReceiver.Stored,
            [("MemoryExtensions", "IndexOfAny")] = PlatformReceiver.Stored,
            [("MemoryExtensions", "LastIndexOfAny")] = PlatformReceiver.Stored,
            [("MemoryExtensions", "IndexOfAnyExcept")] = PlatformReceiver.Stored,
            [("MemoryExtensions", "LastIndexOfAnyExcept")] = PlatformReceiver.Stored,
            [("MemoryExtensions", "Count")] = PlatformReceiver.Stored,
            [("MemoryExtensions", "CountAny")] = PlatformReceiver.Stored,
            [("MemoryExtensions", "SequenceEqual")] = PlatformReceiver.Stored,
            [("MemoryExtensions", "StartsWith")] = PlatformReceiver.Stored,
            [("MemoryExtensions", "EndsWith")] = PlatformReceiver.Stored,
            [("MemoryExtensions", "Replace")] = PlatformReceiver.Stored,
            [("MemoryExtensions", "SequenceCompareTo")] = PlatformReceiver.Stored,
            [("PriorityQueue", "Remove")] = PlatformReceiver.Given
        };
}
