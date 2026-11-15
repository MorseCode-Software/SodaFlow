using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Linq;
using TUnit.Assertions;
using TUnit.Core;

namespace SodaFlow.Analyzers.Tests;

/// <summary>
///     Holds <see cref="PlatformCallers" /> to the platform. Each row has the
///     name of a type and the names of its methods. A name with no such member
///     is a row that does nothing, and nothing else shows that.
/// </summary>
internal sealed class PlatformCallersTests
{
    // The type of the platform for each type name in the table.
    private static readonly Type[] Types =
    [
        typeof(Array),
        typeof(List<>),
        typeof(Enumerable),
        typeof(MemoryExtensions),
        typeof(Comparer<>),
        typeof(EqualityComparer<>),
        typeof(object),
        typeof(HashSet<>),
        typeof(Dictionary<,>),
        typeof(SortedSet<>),
        typeof(SortedDictionary<,>),
        typeof(SortedList<,>),
        typeof(PriorityQueue<,>),
        typeof(ValueTuple<,>),
        typeof(string),
        typeof(StringBuilder),
        typeof(TextWriter),
        typeof(Console),
        typeof(Convert),
        typeof(XElement),
        typeof(XContainer),
        typeof(XAttribute),
        typeof(LinkedList<>),
        typeof(Queue<>),
        typeof(Stack<>),
        typeof(OrderedDictionary<,>),
        typeof(ConcurrentDictionary<,>),
        typeof(Collection<>),
        typeof(ReadOnlyCollection<>),
        typeof(ReadOnlyDictionary<,>),
        // A package of the test host has a type with this name also, thus the name alone is not one type.
        TypeIn(assembly: typeof(SortedSet<>).Assembly, name: "System.Collections.Generic.CollectionExtensions"),
        typeof(ImmutableHashSet),
        typeof(ImmutableHashSet<>),
        typeof(ImmutableSortedSet),
        typeof(ImmutableSortedSet<>),
        typeof(ImmutableDictionary),
        typeof(ImmutableDictionary<,>),
        typeof(ImmutableSortedDictionary),
        typeof(ImmutableSortedDictionary<,>),
        typeof(ImmutableList<>),
        typeof(ImmutableArray<>),
        typeof(ImmutableHashSet<>.Builder),
        typeof(ImmutableSortedSet<>.Builder),
        typeof(ImmutableDictionary<,>.Builder),
        typeof(ImmutableSortedDictionary<,>.Builder),
        typeof(ImmutableList<>.Builder),
        typeof(ImmutableArray<>.Builder),
        typeof(FrozenSet),
        typeof(FrozenSet<>),
        typeof(FrozenDictionary),
        typeof(FrozenDictionary<,>),
        typeof(ArrayList),
        typeof(Hashtable),
        typeof(HashCode),
        typeof(Tuple<>),
        typeof(Tuple<,>),
        typeof(ICollection<>),
        typeof(IList<>),
        typeof(IList),
        typeof(ISet<>),
        typeof(IReadOnlySet<>),
        typeof(IDictionary<,>),
        typeof(IDictionary),
        typeof(IReadOnlyDictionary<,>),
        typeof(ILookup<,>),
        typeof(IImmutableSet<>),
        typeof(IImmutableDictionary<,>)
    ];

    [Test]
    public async Task EachRowNamesAMemberOfThePlatform()
    {
        // A name can be for two types: the type of a collection, and the type with its factories.
        // A type in a type, as a builder is, has the name of that type before its own name.
        ILookup<string, Type> byName =
            Types.ToLookup(
                keySelector: static type =>
                    type.DeclaringType is { } outer
                        ? outer.Name.Split('`')[0] + "." + type.Name.Split('`')[0]
                        : type.Name.Split('`')[0],
                comparer: StringComparer.Ordinal);

        List<string> unknown = [];

        foreach ((string type, _, ImmutableArray<string> methods) in PlatformCallers.Rows)
        {
            if (!byName.Contains(type))
            {
                unknown.Add(type);

                continue;
            }

            unknown.AddRange(
                methods
                    .Where(method => !byName[type].Any(found => Has(type: found, method: method)))
                    .Select(method => $"{type}.{method}"));
        }

        await Assert.That(unknown).IsEmpty();
    }

    /// <summary>
    ///     Three kinds of method call <c>Equals</c> or <c>CompareTo</c>. They are
    ///     a method that compares only its own elements, a method with a known
    ///     receiver, and a method that compares what a function chooses. Thus, each
    ///     of them has a row with one of those two in the first table. A name with
    ///     no such row does nothing.
    /// </summary>
    [Test]
    public async Task EachMethodOfTheTwoSmallTablesHasARowThatCompares()
    {
        HashSet<(string Type, string Method)> comparing =
        [
            .. PlatformCallers.Rows
                .Where(static row =>
                    (row.Callbacks & (PlatformCallbacks.Compare | PlatformCallbacks.Equals)) != PlatformCallbacks.None)
                .SelectMany(static row => row.Methods.Select(method => (row.Type, method)))
        ];

        await Assert.That(
                PlatformCallers.AmongTheElements
                    .Concat(PlatformReceivers.ByMethod.Keys)
                    .Concat(PlatformReceivers.WithNoComparerGiven.Keys)
                    .Concat(PlatformReceivers.WithValuesOneByOne.Keys)
                    .Concat(PlatformCallers.WithAFunctionThatChooses)
                    .Concat(PlatformCallers.WithAFunctionAndKeys)
                    .Where(entry => !comparing.Contains(entry)))
            .IsEmpty();
    }

    /// <summary>
    ///     A row for a member of an interface has the callbacks of each row of a
    ///     type of the table that implements the member. The analyzer reads that
    ///     row when it does not know the type of the object. Thus, a callback that
    ///     a collection makes and the row does not have is a read that the
    ///     analyzer misses.
    /// </summary>
    [Test]
    public async Task EachRowOfAnInterfaceHasTheCallbacksOfEachTypeThatImplementsIt()
    {
        Dictionary<(string Type, string Method), PlatformCallbacks> table = [];

        foreach ((string type, PlatformCallbacks callbacks, ImmutableArray<string> methods) in PlatformCallers.Rows)
        {
            foreach (string method in methods)
            {
                table[(type, method)] = table.GetValueOrDefault(key: (type, method)) | callbacks;
            }
        }

        List<string> missing = [];

        foreach (Type contract in Types.Where(static type => type.IsInterface))
        {
            string contractName = NameOf(contract);

            foreach (Type type in Types.Where(type => !type.IsInterface && Implements(type: type, contract: contract)))
            {
                InterfaceMapping map =
                    type.GetInterfaceMap(type.GetInterfaces().First(found => IsOf(found: found, contract: contract)));

                foreach (string method in map.InterfaceMethods.Select(static member => member.Name))
                {
                    if (!table.TryGetValue(key: (contractName, method), value: out PlatformCallbacks row)
                        || !table.TryGetValue(key: (NameOf(type), method), value: out PlatformCallbacks own))
                    {
                        continue;
                    }

                    // A method that makes text is not a comparison.
                    PlatformCallbacks more = own & ~row & ~PlatformCallbacks.Text;

                    if (more != PlatformCallbacks.None)
                    {
                        missing.Add($"{contractName}.{method}: {NameOf(type)} also calls {more}");
                    }
                }
            }
        }

        await Assert.That(missing).IsEmpty();
    }

    private static string NameOf(MemberInfo type) => type.Name.Split('`')[0];

    private static bool IsOf(Type found, Type contract) =>
        (found.IsGenericType ? found.GetGenericTypeDefinition() : found) == contract;

    private static bool Implements(Type type, Type contract) =>
        type.GetInterfaces().Any(found => IsOf(found: found, contract: contract));

    private static Type TypeIn(Assembly assembly, string name) =>
        assembly.GetType(name: name, throwOnError: true) ?? throw new InvalidOperationException(name);

    // A constructor has the name .ctor in the table, and an accessor has the name that the
    // compiler gives it. An explicit implementation of a member of an interface has the name of
    // that member, as CompareTo of a Tuple object has. Its own name has the interface before a dot.
    private static bool Has(Type type, string method) =>
        method == ".ctor"
            ? type.GetConstructors().Length > 0
            : type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static)
                  .Any(candidate => candidate.Name == method)
              || type.GetMethods(BindingFlags.NonPublic | BindingFlags.Instance)
                  .Any(candidate =>
                      candidate.IsPrivate
                      && candidate.Name.EndsWith(value: "." + method, comparisonType: StringComparison.Ordinal));
}
