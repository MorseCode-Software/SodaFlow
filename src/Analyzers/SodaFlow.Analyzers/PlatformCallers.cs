using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using CallerRow =
    (string Type, SodaFlow.Analyzers.PlatformCallbacks Callbacks,
    System.Collections.Immutable.ImmutableArray<string> Methods);

namespace SodaFlow.Analyzers;

/// <summary>
///     The members of an object that code of the platform calls with no call
///     in the code of the project.
/// </summary>
[Flags]
internal enum PlatformCallbacks
{
    None = 0,

    /// <summary><c>CompareTo</c> of <c>IComparable</c>.</summary>
    Compare = 1,

    /// <summary><c>Equals</c> of <c>object</c> or of <c>IEquatable</c>.</summary>
    Equals = 2,

    /// <summary><c>GetHashCode</c>.</summary>
    Hash = 4,

    /// <summary><c>ToString</c>.</summary>
    Text = 8
}

/// <summary>
///     The methods of the platform that call a member of an object which they
///     get. A sort calls <c>CompareTo</c>, a search calls <c>Equals</c>, a set
///     calls <c>GetHashCode</c>, and a method that makes text calls
///     <c>ToString</c>.
/// </summary>
/// <remarks>
///     <para>
///         The analyzer reads code of the platform as code that cannot know the
///         library. That is correct for a cell. It is not correct for an object of
///         the project. The platform calls the members that such an object has from
///         <c>object</c>, and from the interfaces of the platform.
///     </para>
///     <para>
///         The table is not complete, and cannot be. Each row is here because a
///         run showed the call. A method that is not here calls no member, as far
///         as the analyzer knows.
///     </para>
///     <para>
///         A member of a set or of a dictionary calls <c>Equals</c> only for two
///         objects with one hash code. A sort calls <c>CompareTo</c> only when it
///         has two elements. The analyzer cannot know either, and reports the
///         member. A method that gets a comparer calls the comparer, and not
///         these members.
///     </para>
/// </remarks>
internal static class PlatformCallers
{
    private const PlatformCallbacks HashAndEquals = PlatformCallbacks.Hash | PlatformCallbacks.Equals;

    /// <summary>
    ///     Each row: the name of a type of the platform, the members that its
    ///     methods call, and the names of those methods. A constructor has the
    ///     name <c>.ctor</c>, and an indexer has the names <c>get_Item</c> and
    ///     <c>set_Item</c>.
    /// </summary>
    public static readonly ImmutableArray<CallerRow> Rows =
        ImmutableArray.Create(
            Row(type: "Array", callbacks: PlatformCallbacks.Compare, "Sort", "BinarySearch"),
            Row(type: "Array", callbacks: PlatformCallbacks.Equals, "IndexOf"),
            Row(type: "List", callbacks: PlatformCallbacks.Compare, "Sort", "BinarySearch"),
            Row(type: "List", callbacks: PlatformCallbacks.Equals, "Contains", "IndexOf", "LastIndexOf", "Remove"),
            Row(type: "Enumerable", callbacks: PlatformCallbacks.Compare,
                "OrderBy", "OrderByDescending", "ThenBy", "ThenByDescending", "Order", "OrderDescending", "Min",
                "Max", "MinBy", "MaxBy"),
            Row(type: "Enumerable", callbacks: PlatformCallbacks.Equals, "Contains", "SequenceEqual"),
            Row(type: "Enumerable", callbacks: HashAndEquals,
                "Distinct", "DistinctBy", "GroupBy", "ToDictionary", "ToHashSet", "ToLookup", "Union", "Intersect",
                "Except", "Join"),
            Row(type: "MemoryExtensions", callbacks: PlatformCallbacks.Equals, "Contains"),
            Row(type: "Comparer", callbacks: PlatformCallbacks.Compare, "Compare"),
            Row(type: "EqualityComparer", callbacks: PlatformCallbacks.Equals, "Equals"),
            Row(type: "Object", callbacks: PlatformCallbacks.Equals, "Equals"),
            Row(type: "HashSet", callbacks: HashAndEquals, ".ctor", "Add", "Contains", "Remove"),
            Row(type: "Dictionary", callbacks: HashAndEquals,
                "Add", "TryAdd", "ContainsKey", "TryGetValue", "Remove", "get_Item", "set_Item"),
            Row(type: "SortedSet", callbacks: PlatformCallbacks.Compare, ".ctor", "Add", "Contains"),
            Row(type: "SortedDictionary", callbacks: PlatformCallbacks.Compare, "Add", "set_Item"),
            Row(type: "SortedList", callbacks: PlatformCallbacks.Compare, "Add"),
            Row(type: "PriorityQueue", callbacks: PlatformCallbacks.Compare,
                ".ctor", "Enqueue", "EnqueueRange", "EnqueueDequeue"),
            Row(type: "ValueTuple", callbacks: PlatformCallbacks.Equals, "Equals"),
            Row(type: "ValueTuple", callbacks: PlatformCallbacks.Text, "ToString"),
            Row(type: "String", callbacks: PlatformCallbacks.Text, "Join", "Concat", "Format"),
            Row(type: "StringBuilder", callbacks: PlatformCallbacks.Text,
                "Append", "AppendJoin", "AppendFormat", "Insert"),
            Row(type: "TextWriter", callbacks: PlatformCallbacks.Text, "Write", "WriteLine"),
            Row(type: "Console", callbacks: PlatformCallbacks.Text, "WriteLine"),
            Row(type: "Convert", callbacks: PlatformCallbacks.Text, "ToString"));

    private static readonly ImmutableDictionary<(string Type, string Method), PlatformCallbacks> ByMethod =
        Rows.SelectMany(static row => row.Methods.Select(method => (Key: (row.Type, method), row.Callbacks)))
            .ToImmutableDictionary(
                keySelector: static pair => pair.Key,
                elementSelector: static pair => pair.Callbacks);

    /// <summary>
    ///     The members that a method of the platform calls on the objects that it
    ///     gets. None for a method that is not in the table.
    /// </summary>
    public static PlatformCallbacks Of(IMethodSymbol method)
    {
        IMethodSymbol definition = (method.ReducedFrom ?? method).OriginalDefinition;

        if (definition.ContainingType is not { } type
            || !SodaFlowSymbols.IsPlatform(type)
            || !ByMethod.TryGetValue(key: (type.Name, definition.Name), value: out PlatformCallbacks callbacks))
        {
            return PlatformCallbacks.None;
        }

        // Equals of object with two parameters calls Equals of the first one. Equals with one
        // parameter is the member itself, and the code calls it.
        return type.SpecialType == SpecialType.System_Object && !definition.IsStatic
            ? PlatformCallbacks.None
            : callbacks;
    }

    /// <summary>
    ///     The types of the values that a method compares. A method with a type
    ///     parameter for a key or for a priority compares those. Each other
    ///     generic method compares values of its type arguments, and those of its
    ///     type. A tuple compares its elements. The result is empty for a method
    ///     with no type argument, which gets its values as <c>object</c>.
    /// </summary>
    public static ImmutableArray<ITypeSymbol> ComparedTypes(IMethodSymbol method)
    {
        IMethodSymbol called = method.ReducedFrom ?? method;

        IEnumerable<(ITypeParameterSymbol Parameter, ITypeSymbol Argument)> all =
            Pairs(parameters: called.ContainingType.TypeParameters, arguments: called.ContainingType.TypeArguments)
                .Concat(Pairs(parameters: called.TypeParameters, arguments: method.TypeArguments));

        (ITypeParameterSymbol Parameter, ITypeSymbol Argument)[] pairs = [.. all];

        return (pairs.Any(static pair => pair.Parameter.Name is "TKey" or "TPriority")
                ? pairs.Where(static pair => pair.Parameter.Name is "TKey" or "TPriority")
                : pairs)
            .SelectMany(static pair => Parts(pair.Argument))
            .ToImmutableArray();
    }

    private static CallerRow Row(string type, PlatformCallbacks callbacks, params string[] methods) =>
        (type, callbacks, methods.ToImmutableArray());

    private static IEnumerable<(ITypeParameterSymbol Parameter, ITypeSymbol Argument)> Pairs(
        ImmutableArray<ITypeParameterSymbol> parameters,
        ImmutableArray<ITypeSymbol> arguments) =>
        parameters.Length == arguments.Length
            ? parameters.Select((parameter, position) => (parameter, arguments[position]))
            : [];

    private static IEnumerable<ITypeSymbol> Parts(ITypeSymbol type) =>
        type is INamedTypeSymbol { IsTupleType: true } tuple
            ? tuple.TupleElements.SelectMany(static element => Parts(element.Type))
            : [type];
}
