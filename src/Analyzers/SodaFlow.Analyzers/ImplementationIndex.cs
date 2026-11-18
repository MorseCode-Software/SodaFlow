using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace SodaFlow.Analyzers;

/// <summary>
///     The properties in one compilation that one property of a type in it
///     implements together. A pair is two properties of interfaces, or a
///     property of an interface and a property of a base class. That base class
///     does not have the interface.
/// </summary>
/// <remarks>
///     The first call of <see cref="AreTogether" /> makes the index from
///     declarations only. Most compilations never make it. Only an object with
///     states for an interface property and a second property of that name
///     asks for it.
/// </remarks>
internal sealed class ImplementationIndex(in Compilation compilation)
{
    private readonly Lazy<ImmutableDictionary<ISymbol, ImmutableHashSet<ISymbol>>> together = IndexOf(compilation);

    /// <summary>
    ///     True when one property of a type in the compilation implements the two
    ///     properties. It is true also when one of the two is that property and
    ///     the other is of an interface. Each is a definition.
    /// </summary>
    public bool AreTogether(ISymbol first, ISymbol second) =>
        this.together.Value.TryGetValue(key: first, value: out ImmutableHashSet<ISymbol>? others)
        && others.Contains(second);

    private static Lazy<ImmutableDictionary<ISymbol, ImmutableHashSet<ISymbol>>> IndexOf(Compilation compilation) =>
        new(() => Build(compilation));

    private static ImmutableDictionary<ISymbol, ImmutableHashSet<ISymbol>> Build(Compilation compilation)
    {
        // Only a type with a base list can have an interface. The search reads the declarations of
        // types only, and not the bodies.
        ImmutableHashSet<string> names =
            compilation.SyntaxTrees
                .SelectMany(static tree =>
                    tree.GetRoot()
                        .DescendantNodes(static node =>
                            node is CompilationUnitSyntax or BaseNamespaceDeclarationSyntax or TypeDeclarationSyntax))
                .OfType<TypeDeclarationSyntax>()
                .Where(static type => type.BaseList != null && type is not InterfaceDeclarationSyntax)
                .Select(static type => type.Identifier.ValueText)
                .ToImmutableHashSet(StringComparer.Ordinal);

        Dictionary<ISymbol, HashSet<ISymbol>> found = new(SymbolEqualityComparer.Default);

        foreach (ImmutableArray<ISymbol> together in compilation
                     .GetSymbolsWithName(predicate: names.Contains, filter: SymbolFilter.Type)
                     .OfType<INamedTypeSymbol>()
                     .Where(static type => !type.AllInterfaces.IsEmpty)
                     .SelectMany(PairsOf))
        {
            Pair(found: found, contracts: together);
        }

        ImmutableDictionary<ISymbol, ImmutableHashSet<ISymbol>>.Builder index =
            ImmutableDictionary.CreateBuilder<ISymbol, ImmutableHashSet<ISymbol>>(SymbolEqualityComparer.Default);

        foreach (KeyValuePair<ISymbol, HashSet<ISymbol>> pair in found)
        {
            index[pair.Key] = pair.Value.ToImmutableHashSet(SymbolEqualityComparer.Default);
        }

        return index.ToImmutable();
    }

    // The groups of properties that the type implements together. An override is the same location as
    // the property that it overrides.
    private static IEnumerable<ImmutableArray<ISymbol>> PairsOf(INamedTypeSymbol type)
    {
        ImmutableArray<(ISymbol Contract, ISymbol Root)> implemented =
            type.AllInterfaces
                .SelectMany(static contract => contract.GetMembers().OfType<IPropertySymbol>())
                .SelectMany(contract => ImplementationOf(type: type, contract: contract))
                .ToImmutableArray();

        // A derived class can give a property of a base class to an interface that the base class does
        // not have. The aliases of Construction pair the two only when the base class has the interface.
        return implemented
            .GroupBy(
                keySelector: static pair => pair.Root,
                elementSelector: static pair => pair.Contract,
                comparer: SymbolEqualityComparer.Default)
            .Select(static one => one.ToImmutableArray())
            .Concat(
                implemented
                    .Where(static pair =>
                        !pair.Root.ContainingType.AllInterfaces.Any(one =>
                            SymbolEqualityComparer.Default.Equals(
                                x: one.OriginalDefinition,
                                y: pair.Contract.ContainingType)))
                    .Select(static pair => ImmutableArray.Create(item1: pair.Contract, item2: pair.Root)));
    }

    private static IEnumerable<(ISymbol Contract, ISymbol Root)> ImplementationOf(
        ITypeSymbol type,
        IPropertySymbol contract) =>
        type.FindImplementationForInterfaceMember(contract) is { } implementation
            ? [(contract.OriginalDefinition, Construction.RootOf(implementation))]
            : [];

    private static void Pair(IDictionary<ISymbol, HashSet<ISymbol>> found, ImmutableArray<ISymbol> contracts)
    {
        foreach (ISymbol contract in contracts)
        {
            foreach (ISymbol other in contracts.Where(other =>
                         !SymbolEqualityComparer.Default.Equals(x: other, y: contract)))
            {
                if (!found.TryGetValue(key: contract, value: out HashSet<ISymbol>? others))
                {
                    others = new HashSet<ISymbol>(SymbolEqualityComparer.Default);
                    found[contract] = others;
                }

                others.Add(other);
            }
        }
    }
}
