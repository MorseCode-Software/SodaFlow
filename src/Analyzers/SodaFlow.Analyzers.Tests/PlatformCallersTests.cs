using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
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
        typeof(ValueTuple<,>),
        typeof(string),
        typeof(StringBuilder),
        typeof(TextWriter),
        typeof(Console),
        typeof(Convert)
    ];

    [Test]
    public async Task EachRowNamesAMemberOfThePlatform()
    {
        Dictionary<string, Type> byName =
            Types.ToDictionary(keySelector: static type => type.Name.Split('`')[0], comparer: StringComparer.Ordinal);

        List<string> unknown = [];

        foreach ((string type, _, ImmutableArray<string> methods) in PlatformCallers.Rows)
        {
            if (!byName.TryGetValue(key: type, value: out Type? found))
            {
                unknown.Add(type);

                continue;
            }

            unknown.AddRange(
                methods.Where(method => !Has(type: found, method: method)).Select(method => $"{type}.{method}"));
        }

        await Assert.That(unknown).IsEmpty();
    }

    // A constructor has the name .ctor in the table, and an accessor has the name that the
    // compiler gives it.
    private static bool Has(Type type, string method) =>
        method == ".ctor"
            ? type.GetConstructors().Length > 0
            : type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static)
                .Any(candidate => candidate.Name == method);
}
