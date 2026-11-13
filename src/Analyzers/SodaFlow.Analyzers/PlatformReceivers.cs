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
    FirstValue
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
            [("EqualityComparer", "Equals")] = PlatformReceiver.FirstValue
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
    ///     for an <c>IEquatable</c> type can give it to the value.
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
            [("MemoryExtensions", "SequenceCompareTo")] = PlatformReceiver.Stored
        };
}
