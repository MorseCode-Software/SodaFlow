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

    private const PlatformCallbacks AllThree = HashAndEquals | PlatformCallbacks.Compare;

    /// <summary>
    ///     Each row: the name of a type of the platform, the members that its
    ///     methods call, and the names of those methods. A constructor has the
    ///     name <c>.ctor</c>, and an indexer has the names <c>get_Item</c> and
    ///     <c>set_Item</c>.
    /// </summary>
    public static readonly ImmutableArray<CallerRow> Rows =
        ImmutableArray.Create(
            Row(type: "Array", callbacks: PlatformCallbacks.Compare, "Sort", "BinarySearch"),
            Row(type: "Array", callbacks: PlatformCallbacks.Equals, "IndexOf", "LastIndexOf"),
            Row(type: "List", callbacks: PlatformCallbacks.Compare, "Sort", "BinarySearch"),
            Row(type: "List", callbacks: PlatformCallbacks.Equals, "Contains", "IndexOf", "LastIndexOf", "Remove"),
            Row(type: "Enumerable", callbacks: PlatformCallbacks.Compare,
                "OrderBy", "OrderByDescending", "ThenBy", "ThenByDescending", "Order", "OrderDescending", "Min",
                "Max", "MinBy", "MaxBy"),
            Row(type: "Enumerable", callbacks: PlatformCallbacks.Equals, "Contains", "SequenceEqual"),
            Row(type: "Enumerable", callbacks: HashAndEquals,
                "Distinct", "DistinctBy", "GroupBy", "ToDictionary", "ToHashSet", "ToLookup", "Union", "Intersect",
                "Except", "Join", "UnionBy", "IntersectBy", "ExceptBy", "GroupJoin", "LeftJoin", "RightJoin", "CountBy",
                "AggregateBy"),
            Row(type: "MemoryExtensions", callbacks: PlatformCallbacks.Equals,
                "Contains", "ContainsAny", "ContainsAnyExcept", "IndexOf", "LastIndexOf", "IndexOfAny",
                "LastIndexOfAny", "IndexOfAnyExcept", "LastIndexOfAnyExcept", "Count", "CountAny", "SequenceEqual",
                "StartsWith", "EndsWith", "CommonPrefixLength", "Replace", "Trim", "TrimStart", "TrimEnd"),
            Row(type: "MemoryExtensions", callbacks: PlatformCallbacks.Compare,
                "Sort", "BinarySearch", "SequenceCompareTo", "IndexOfAnyInRange", "LastIndexOfAnyInRange",
                "IndexOfAnyExceptInRange", "LastIndexOfAnyExceptInRange", "ContainsAnyInRange",
                "ContainsAnyExceptInRange"),
            Row(type: "Comparer", callbacks: PlatformCallbacks.Compare, "Compare"),
            Row(type: "EqualityComparer", callbacks: PlatformCallbacks.Equals, "Equals"),
            Row(type: "Object", callbacks: PlatformCallbacks.Equals, "Equals"),
            Row(type: "HashSet", callbacks: HashAndEquals,
                ".ctor", "Add", "Contains", "Remove", "TryGetValue", "ExceptWith", "IntersectWith", "IsProperSubsetOf",
                "IsProperSupersetOf", "IsSubsetOf", "IsSupersetOf", "Overlaps", "SetEquals", "SymmetricExceptWith",
                "UnionWith", "RemoveWhere"),
            Row(type: "Dictionary", callbacks: HashAndEquals,
                ".ctor", "Add", "TryAdd", "ContainsKey", "TryGetValue", "Remove", "get_Item", "set_Item"),
            Row(type: "Dictionary", callbacks: PlatformCallbacks.Equals, "ContainsValue"),
            Row(type: "SortedSet", callbacks: PlatformCallbacks.Compare,
                ".ctor", "Add", "Contains", "Remove", "TryGetValue", "GetViewBetween", "ExceptWith", "IntersectWith",
                "IsProperSubsetOf", "IsProperSupersetOf", "IsSubsetOf", "IsSupersetOf", "Overlaps", "SetEquals",
                "SymmetricExceptWith", "UnionWith", "RemoveWhere"),
            Row(type: "SortedDictionary", callbacks: PlatformCallbacks.Compare,
                ".ctor", "Add", "ContainsKey", "TryGetValue", "Remove", "get_Item", "set_Item"),
            Row(type: "SortedDictionary", callbacks: PlatformCallbacks.Equals, "ContainsValue"),
            Row(type: "SortedList", callbacks: PlatformCallbacks.Compare,
                ".ctor", "Add", "ContainsKey", "TryGetValue", "Remove", "IndexOfKey", "get_Item", "set_Item"),
            Row(type: "SortedList", callbacks: PlatformCallbacks.Equals, "ContainsValue", "IndexOfValue"),
            Row(type: "PriorityQueue", callbacks: PlatformCallbacks.Compare,
                ".ctor", "Enqueue", "EnqueueRange", "EnqueueDequeue", "DequeueEnqueue"),
            Row(type: "PriorityQueue", callbacks: PlatformCallbacks.Equals, "Remove"),
            Row(type: "LinkedList", callbacks: PlatformCallbacks.Equals, "Contains", "Find", "FindLast", "Remove"),
            Row(type: "Queue", callbacks: PlatformCallbacks.Equals, "Contains"),
            Row(type: "Stack", callbacks: PlatformCallbacks.Equals, "Contains"),
            Row(type: "OrderedDictionary", callbacks: HashAndEquals,
                ".ctor", "Add", "TryAdd", "Insert", "SetAt", "ContainsKey", "IndexOf", "TryGetValue", "Remove",
                "get_Item", "set_Item"),
            Row(type: "OrderedDictionary", callbacks: PlatformCallbacks.Equals, "ContainsValue"),
            Row(type: "ConcurrentDictionary", callbacks: HashAndEquals,
                ".ctor", "TryAdd", "GetOrAdd", "AddOrUpdate", "TryUpdate", "ContainsKey", "TryGetValue", "TryRemove",
                "get_Item", "set_Item"),
            Row(type: "Collection", callbacks: PlatformCallbacks.Equals, "Contains", "IndexOf", "Remove"),
            Row(type: "ReadOnlyCollection", callbacks: PlatformCallbacks.Equals, "Contains", "IndexOf"),
            Row(type: "ReadOnlyDictionary", callbacks: HashAndEquals, "ContainsKey", "TryGetValue", "get_Item"),
            Row(type: "CollectionExtensions", callbacks: HashAndEquals, "GetValueOrDefault", "TryAdd", "Remove"),
            Row(type: "ImmutableHashSet", callbacks: HashAndEquals,
                "Create", "CreateRange", "ToImmutableHashSet", "Add", "Contains", "Remove", "TryGetValue",
                "Except", "Intersect", "IsProperSubsetOf", "IsProperSupersetOf",
                "IsSubsetOf", "IsSupersetOf", "Overlaps", "SetEquals", "SymmetricExcept", "Union"),
            Row(type: "ImmutableSortedSet", callbacks: PlatformCallbacks.Compare,
                "Create", "CreateRange", "ToImmutableSortedSet", "Add", "Contains", "Remove", "TryGetValue", "IndexOf",
                "Except", "Intersect", "IsProperSubsetOf", "IsProperSupersetOf",
                "IsSubsetOf", "IsSupersetOf", "Overlaps", "SetEquals", "SymmetricExcept", "Union"),
            Row(type: "ImmutableDictionary", callbacks: HashAndEquals,
                "CreateRange", "ToImmutableDictionary", "Add", "AddRange", "SetItem", "SetItems", "Contains",
                "ContainsKey", "TryGetKey", "TryGetValue", "Remove", "RemoveRange", "get_Item"),
            Row(type: "ImmutableDictionary", callbacks: PlatformCallbacks.Equals, "ContainsValue"),
            Row(type: "ImmutableSortedDictionary", callbacks: PlatformCallbacks.Compare,
                "CreateRange", "ToImmutableSortedDictionary", "Add", "AddRange", "SetItem", "SetItems", "Contains",
                "ContainsKey", "TryGetKey", "TryGetValue", "ValueRef", "Remove", "RemoveRange", "get_Item"),
            Row(type: "ImmutableSortedDictionary", callbacks: PlatformCallbacks.Equals, "ContainsValue"),
            Row(type: "ImmutableList", callbacks: PlatformCallbacks.Equals,
                "Contains", "IndexOf", "Remove", "RemoveRange", "Replace"),
            Row(type: "ImmutableList", callbacks: PlatformCallbacks.Compare, "Sort", "BinarySearch"),
            Row(type: "ImmutableArray", callbacks: PlatformCallbacks.Equals,
                "Contains", "IndexOf", "LastIndexOf", "Remove", "RemoveRange", "Replace"),
            Row(type: "ImmutableArray", callbacks: PlatformCallbacks.Compare, "Sort"),
            Row(type: "FrozenSet", callbacks: HashAndEquals,
                "ToFrozenSet", "Contains", "TryGetValue", "IsProperSubsetOf", "IsProperSupersetOf", "IsSubsetOf",
                "IsSupersetOf", "Overlaps", "SetEquals"),
            Row(type: "FrozenDictionary", callbacks: HashAndEquals,
                "ToFrozenDictionary", "ContainsKey", "TryGetValue", "GetValueRefOrNullRef", "get_Item"),
            Row(type: "ArrayList", callbacks: PlatformCallbacks.Equals, "Contains", "IndexOf", "LastIndexOf", "Remove"),
            Row(type: "ArrayList", callbacks: PlatformCallbacks.Compare, "Sort", "BinarySearch"),
            Row(type: "Hashtable", callbacks: HashAndEquals,
                ".ctor", "Add", "Contains", "ContainsKey", "Remove", "get_Item", "set_Item"),
            Row(type: "Hashtable", callbacks: PlatformCallbacks.Equals, "ContainsValue"),
            // A member of an interface has a row when each collection of the platform that implements
            // it compares. A run of each such collection found that. The callbacks are those of all
            // of them, because the analyzer does not know which collection the object is.
            Row(type: "ICollection", callbacks: AllThree, "Contains", "Remove"),
            Row(type: "IList", callbacks: AllThree, "IndexOf", "Remove"),
            Row(type: "IList", callbacks: PlatformCallbacks.Compare | PlatformCallbacks.Equals, "Contains"),
            Row(type: "ISet", callbacks: AllThree,
                "Add", "ExceptWith", "IntersectWith", "IsProperSubsetOf", "IsProperSupersetOf", "IsSubsetOf",
                "IsSupersetOf", "Overlaps", "SetEquals", "SymmetricExceptWith", "UnionWith"),
            Row(type: "IReadOnlySet", callbacks: AllThree,
                "Contains", "IsProperSubsetOf", "IsProperSupersetOf", "IsSubsetOf", "IsSupersetOf", "Overlaps",
                "SetEquals"),
            Row(type: "IDictionary", callbacks: AllThree,
                "Add", "Contains", "ContainsKey", "Remove", "TryGetValue", "get_Item", "set_Item"),
            Row(type: "IReadOnlyDictionary", callbacks: AllThree, "ContainsKey", "TryGetValue", "get_Item"),
            Row(type: "ILookup", callbacks: HashAndEquals, "Contains", "get_Item"),
            Row(type: "IImmutableSet", callbacks: AllThree,
                "Add", "Contains", "Except", "Intersect", "IsProperSubsetOf", "IsProperSupersetOf", "IsSubsetOf",
                "IsSupersetOf", "Overlaps", "Remove", "SetEquals", "SymmetricExcept", "TryGetValue", "Union"),
            Row(type: "IImmutableDictionary", callbacks: AllThree,
                "Add", "AddRange", "Contains", "Remove", "RemoveRange", "SetItem", "SetItems", "TryGetKey"),
            Row(type: "EqualityComparer", callbacks: PlatformCallbacks.Hash, "GetHashCode"),
            Row(type: "HashCode", callbacks: PlatformCallbacks.Hash, "Combine", "Add"),
            Row(type: "Tuple", callbacks: PlatformCallbacks.Equals, "Equals"),
            Row(type: "Tuple", callbacks: PlatformCallbacks.Hash, "GetHashCode"),
            Row(type: "Tuple", callbacks: PlatformCallbacks.Compare, "CompareTo"),
            Row(type: "Tuple", callbacks: PlatformCallbacks.Text, "ToString"),
            Row(type: "ValueTuple", callbacks: PlatformCallbacks.Equals, "Equals"),
            Row(type: "ValueTuple", callbacks: PlatformCallbacks.Hash, "GetHashCode"),
            Row(type: "ValueTuple", callbacks: PlatformCallbacks.Compare, "CompareTo"),
            Row(type: "ValueTuple", callbacks: PlatformCallbacks.Text, "ToString"),
            Row(type: "String", callbacks: PlatformCallbacks.Text, "Join", "Concat", "Format"),
            Row(type: "StringBuilder", callbacks: PlatformCallbacks.Text,
                "Append", "AppendJoin", "AppendFormat", "Insert"),
            Row(type: "TextWriter", callbacks: PlatformCallbacks.Text, "Write", "WriteLine"),
            Row(type: "Console", callbacks: PlatformCallbacks.Text, "WriteLine"),
            Row(type: "Convert", callbacks: PlatformCallbacks.Text, "ToString"),
            Row(type: "XElement", callbacks: PlatformCallbacks.Text,
                ".ctor", "SetValue", "SetElementValue", "SetAttributeValue", "ReplaceAll"),
            Row(type: "XContainer", callbacks: PlatformCallbacks.Text, "Add", "AddFirst", "ReplaceNodes"),
            Row(type: "XAttribute", callbacks: PlatformCallbacks.Text, ".ctor", "SetValue"));

    /// <summary>
    ///     The methods of the table that compare the elements of one sequence
    ///     with each other, and with no other value. A sort and the constructor of
    ///     a set are such methods. A search is not: it compares a value that it
    ///     gets with each element. Such a method compares nothing when the sequence
    ///     has one element.
    /// </summary>
    public static readonly ImmutableHashSet<(string Type, string Method)> AmongTheElements =
        ImmutableHashSet.Create(
            ("Array", "Sort"),
            ("List", "Sort"),
            ("ArrayList", "Sort"),
            ("MemoryExtensions", "Sort"),
            ("Enumerable", "OrderBy"),
            ("Enumerable", "OrderByDescending"),
            ("Enumerable", "Order"),
            ("Enumerable", "OrderDescending"),
            ("Enumerable", "Min"),
            ("Enumerable", "Max"),
            ("Enumerable", "MinBy"),
            ("Enumerable", "MaxBy"),
            ("Enumerable", "Distinct"),
            ("Enumerable", "DistinctBy"),
            ("Enumerable", "GroupBy"),
            ("Enumerable", "ToDictionary"),
            ("Enumerable", "ToHashSet"),
            ("Enumerable", "ToLookup"),
            ("Enumerable", "CountBy"),
            ("Enumerable", "AggregateBy"),
            ("HashSet", ".ctor"),
            ("SortedSet", ".ctor"),
            ("PriorityQueue", ".ctor"),
            ("Dictionary", ".ctor"),
            ("OrderedDictionary", ".ctor"),
            ("ConcurrentDictionary", ".ctor"),
            ("ImmutableHashSet", "Create"),
            ("ImmutableHashSet", "CreateRange"),
            ("ImmutableHashSet", "ToImmutableHashSet"),
            ("ImmutableSortedSet", "Create"),
            ("ImmutableSortedSet", "CreateRange"),
            ("ImmutableSortedSet", "ToImmutableSortedSet"),
            ("ImmutableDictionary", "CreateRange"),
            ("ImmutableDictionary", "ToImmutableDictionary"),
            ("ImmutableSortedDictionary", "CreateRange"),
            ("ImmutableSortedDictionary", "ToImmutableSortedDictionary"),
            ("FrozenSet", "ToFrozenSet"),
            ("FrozenDictionary", "ToFrozenDictionary"));

    /// <summary>
    ///     The methods of the table that get a function, and do not compare what
    ///     it returns. They compare the values that they have or get. The function
    ///     of <c>RemoveWhere</c> only says which elements the method removes. The
    ///     function of <c>GetOrAdd</c> or of <c>AddOrUpdate</c> only makes the
    ///     value for the key. Each other method that gets a function compares what
    ///     the function returns, as a sort by a key does.
    /// </summary>
    public static readonly ImmutableHashSet<(string Type, string Method)> WithAFunctionThatChooses =
        ImmutableHashSet.Create(
            ("HashSet", "RemoveWhere"),
            ("SortedSet", "RemoveWhere"),
            ("ConcurrentDictionary", "GetOrAdd"),
            ("ConcurrentDictionary", "AddOrUpdate"));

    /// <summary>
    ///     The methods of the table that compare what their function returns with
    ///     the values of a sequence that they get. <c>ExceptBy</c> compares the key
    ///     of each element with each key of its second sequence.
    /// </summary>
    public static readonly ImmutableHashSet<(string Type, string Method)> WithAFunctionAndKeys =
        ImmutableHashSet.Create(("Enumerable", "ExceptBy"), ("Enumerable", "IntersectBy"));

    /// <summary>
    ///     The type parameters whose values a method compares, where that is not
    ///     the rule of <see cref="ComparedTypes" />. <c>Remove</c> of a queue
    ///     compares the elements, and not the priorities. <c>TryUpdate</c>
    ///     compares the key, and the value that it has with the value for the
    ///     comparison.
    /// </summary>
    private static readonly ImmutableDictionary<(string Type, string Method), ImmutableArray<string>>
        ComparedParameters =
            ImmutableDictionary.CreateRange(
            [
                new KeyValuePair<(string Type, string Method), ImmutableArray<string>>(
                    key: ("PriorityQueue", "Remove"),
                    value: ImmutableArray.Create("TElement")),
                new KeyValuePair<(string Type, string Method), ImmutableArray<string>>(
                    key: ("ConcurrentDictionary", "TryUpdate"),
                    value: ImmutableArray.Create(item1: "TKey", item2: "TValue"))
            ]);

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
            || !ByMethod.TryGetValue(key: (type.Name, NameOf(definition)), value: out PlatformCallbacks callbacks))
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
    ///     Which of two objects gets the call from a method of the platform.
    ///     <see cref="PlatformReceiver.Either" /> for a method with no row in
    ///     <see cref="PlatformReceivers" />.
    /// </summary>
    /// <remarks>
    ///     An overload with a parameter for a comparer has a row of its own. The
    ///     code gives no comparer here, because a method that gets one calls it,
    ///     and calls no member of an object.
    /// </remarks>
    public static PlatformReceiver ReceiverOf(IMethodSymbol method)
    {
        IMethodSymbol definition = (method.ReducedFrom ?? method).OriginalDefinition;

        if (definition.ContainingType is not { } type || !SodaFlowSymbols.IsPlatform(type))
        {
            return PlatformReceiver.Either;
        }

        (string Type, string Method) key = (type.Name, NameOf(definition));

        if (definition.Parameters.Any(static parameter => SodaFlowSymbols.IsComparer(parameter.Type)))
        {
            return PlatformReceivers.WithNoComparerGiven.TryGetValue(key: key, value: out PlatformReceiver plain)
                ? plain
                : PlatformReceiver.Either;
        }

        // The second parameter of an extension method is the first value. An overload that gets
        // its values one by one has a type parameter there, and not a span.
        if (definition.Parameters.Length > 1
            && definition.Parameters[1].Type is ITypeParameterSymbol
            && PlatformReceivers.WithValuesOneByOne.TryGetValue(key: key, value: out PlatformReceiver byValue))
        {
            return byValue;
        }

        return PlatformReceivers.ByMethod.TryGetValue(key: key, value: out PlatformReceiver receiver)
            ? receiver
            : PlatformReceiver.Either;
    }

    /// <summary>
    ///     True for a method that has a row in
    ///     <see cref="PlatformReceivers.WithASetArgument" />. The object that gets
    ///     the call then depends on the type of the argument.
    /// </summary>
    public static bool DependsOnItsArgument(IMethodSymbol method)
    {
        IMethodSymbol definition = (method.ReducedFrom ?? method).OriginalDefinition;

        return definition.ContainingType is { } type
               && SodaFlowSymbols.IsPlatform(type)
               && PlatformReceivers.WithASetArgument.Keys.Any(key =>
                   key.Type == type.Name && key.Method == NameOf(definition));
    }

    /// <summary>
    ///     The receiver of a method when its argument is a set of the given type
    ///     with the same comparer. Null when such an argument has the row of the
    ///     method.
    /// </summary>
    public static PlatformReceiver? ReceiverForASet(IMethodSymbol method, ITypeSymbol argument)
    {
        IMethodSymbol definition = (method.ReducedFrom ?? method).OriginalDefinition;

        return SodaFlowSymbols.IsPlatform(argument)
               && PlatformReceivers.WithASetArgument.TryGetValue(
                   key: (definition.ContainingType.Name, NameOf(definition), argument.Name),
                   value: out PlatformReceiver receiver)
            ? receiver
            : null;
    }

    /// <summary>
    ///     True for a parameter whose value a method keeps and does not compare.
    ///     <c>Replace</c> compares the old value with each element, and puts the
    ///     new value where that element was.
    /// </summary>
    public static bool IsOnlyKept(IParameterSymbol? parameter) =>
        parameter is
        {
            Name: "newValue",
            ContainingSymbol: IMethodSymbol { Name: "Replace" or "TryUpdate", ContainingType: { } type }
        }
        && SodaFlowSymbols.IsPlatform(type);

    /// <summary>True for a method of <see cref="WithAFunctionThatChooses" />.</summary>
    public static bool ComparesWhatAFunctionChooses(IMethodSymbol method)
    {
        IMethodSymbol definition = (method.ReducedFrom ?? method).OriginalDefinition;

        return definition.ContainingType is { } type
               && SodaFlowSymbols.IsPlatform(type)
               && WithAFunctionThatChooses.Contains((type.Name, NameOf(definition)));
    }

    /// <summary>True for a method of <see cref="WithAFunctionAndKeys" />.</summary>
    public static bool ComparesKeysBesideAFunction(IMethodSymbol method)
    {
        IMethodSymbol definition = (method.ReducedFrom ?? method).OriginalDefinition;

        return definition.ContainingType is { } type
               && SodaFlowSymbols.IsPlatform(type)
               && WithAFunctionAndKeys.Contains((type.Name, NameOf(definition)));
    }

    /// <summary>True for a method of <see cref="AmongTheElements" />.</summary>
    public static bool ComparesOnlyItsElements(IMethodSymbol method)
    {
        IMethodSymbol definition = (method.ReducedFrom ?? method).OriginalDefinition;

        return definition.ContainingType is { } type
               && SodaFlowSymbols.IsPlatform(type)
               && AmongTheElements.Contains((type.Name, NameOf(definition)));
    }

    /// <summary>
    ///     The members that a tuple calls on its elements, for a member with the
    ///     given name. The code can call that member through a type that is not
    ///     the type of the tuple, such as <c>object</c>. Then, the method in the
    ///     code is not the one of the tuple, and has no row.
    /// </summary>
    public static PlatformCallbacks OfTuple(string member) =>
        ByMethod.TryGetValue(key: ("ValueTuple", member), value: out PlatformCallbacks callbacks)
            ? callbacks
            : PlatformCallbacks.None;

    /// <summary>
    ///     The types of the values that a method compares. A method with a type
    ///     parameter for a key or for a priority compares those. A search for a
    ///     value of a dictionary is different: it compares the values. Each other
    ///     generic method compares values of its type arguments, and those of its
    ///     type. A tuple compares its elements. The result is empty for a method
    ///     with no type argument, which gets its values as <c>object</c>.
    /// </summary>
    public static ImmutableArray<ITypeSymbol> ComparedTypes(IMethodSymbol method) =>
        ComparedPairs(method).Pairs.SelectMany(static pair => Parts(pair.Argument)).ToImmutableArray();

    /// <summary>
    ///     The type parameters whose values a method compares, by the rule of
    ///     <see cref="ComparedTypes" />. Null when the method compares the values
    ///     of each type parameter. A value of a type parameter that is not here
    ///     gets no call, also when its type is a type that the method compares.
    ///     <c>Enqueue</c> of a queue whose elements and priorities have one type
    ///     compares only the priorities.
    /// </summary>
    public static ImmutableHashSet<ITypeParameterSymbol>? ComparedTypeParameters(IMethodSymbol method) =>
        ComparedPairs(method) is { All: false } chosen
            ? chosen.Pairs.Select(static pair => pair.Parameter)
                .ToImmutableHashSet<ITypeParameterSymbol>(SymbolEqualityComparer.Default)
            : null;

    // The pairs of a type parameter and its type argument whose values the method compares. All is
    // true when they are all the pairs.
    private static (IReadOnlyList<(ITypeParameterSymbol Parameter, ITypeSymbol Argument)> Pairs, bool All)
        ComparedPairs(IMethodSymbol method)
    {
        IMethodSymbol called = method.ReducedFrom ?? method;

        IEnumerable<(ITypeParameterSymbol Parameter, ITypeSymbol Argument)> all =
            Pairs(parameters: called.ContainingType.TypeParameters, arguments: called.ContainingType.TypeArguments)
                .Concat(Pairs(parameters: called.TypeParameters, arguments: method.TypeArguments));

        (ITypeParameterSymbol Parameter, ITypeSymbol Argument)[] pairs = [.. all];

        if (ComparedParameters.TryGetValue(
                key: (called.ContainingType.Name, NameOf(called)),
                value: out ImmutableArray<string> names))
        {
            return ([.. pairs.Where(pair => names.Contains(pair.Parameter.Name))], false);
        }

        // A search for a value of a dictionary compares the values, and not the keys.
        bool forValues = NameOf(called) is "ContainsValue" or "IndexOfValue";

        return pairs.Any(static pair => pair.Parameter.Name is "TKey" or "TPriority")
            ? ([
                    .. pairs.Where(pair => forValues
                        ? pair.Parameter.Name == "TValue"
                        : pair.Parameter.Name is "TKey" or "TPriority")
                ], false)
            : (pairs, true);
    }

    // The name of a method in the tables. An explicit implementation of a member of an interface,
    // as ICollection<T>.Add of a HashSet is, has the name of that member.
    private static string NameOf(IMethodSymbol method) =>
        method.ExplicitInterfaceImplementations.FirstOrDefault()?.Name ?? method.Name;

    private static CallerRow Row(string type, PlatformCallbacks callbacks, params string[] methods) =>
        (type, callbacks, methods.ToImmutableArray());

    private static IEnumerable<(ITypeParameterSymbol Parameter, ITypeSymbol Argument)> Pairs(
        ImmutableArray<ITypeParameterSymbol> parameters,
        ImmutableArray<ITypeSymbol> arguments) =>
        parameters.Length == arguments.Length
            ? parameters.Select((parameter, position) => (parameter, arguments[position]))
            : [];

    // A Tuple object compares its elements, as a tuple with parentheses does. A KeyValuePair is a
    // struct: its Equals calls Equals of each part, and a dictionary compares its key and its value.
    private static IEnumerable<ITypeSymbol> Parts(ITypeSymbol type) =>
        type switch
        {
            INamedTypeSymbol { IsTupleType: true } tuple =>
                tuple.TupleElements.SelectMany(static element => Parts(element.Type)),
            INamedTypeSymbol holder when SodaFlowSymbols.HoldsParts(holder) && !holder.TypeArguments.IsEmpty =>
                holder.TypeArguments.SelectMany(Parts),
            _ => [type]
        };
}
