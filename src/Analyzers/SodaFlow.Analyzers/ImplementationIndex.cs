using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace SodaFlow.Analyzers;

/// <summary>
///     The properties of interfaces in one compilation that one property of a
///     type in it implements together.
/// </summary>
/// <remarks>
///     The first call of <see cref="AreTogether" /> makes the index from
///     declarations only. Most compilations never make it, because an object
///     with states for two interface properties of one name is not usual.
/// </remarks>
internal sealed class ImplementationIndex(in Compilation compilation)
{
    private readonly Lazy<ImmutableDictionary<ISymbol, ImmutableHashSet<ISymbol>>> together = IndexOf(compilation);

    /// <summary>
    ///     True when one property of a type in the compilation implements the two
    ///     properties. Each is the definition of a property of an interface.
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

        if (!names.IsEmpty)
        {
            foreach (INamedTypeSymbol type in compilation
                         .GetSymbolsWithName(predicate: names.Contains, filter: SymbolFilter.Type)
                         .OfType<INamedTypeSymbol>()
                         .Where(static type => type.AllInterfaces.Length > 1))
            {
                // An override is the same location as the property that it overrides.
                foreach (IGrouping<ISymbol, ISymbol> one in type.AllInterfaces
                             .SelectMany(static contract => contract.GetMembers().OfType<IPropertySymbol>())
                             .Select(contract =>
                                 (Contract: contract,
                                     Implementation: type.FindImplementationForInterfaceMember(contract)))
                             .Where(static pair => pair.Implementation != null)
                             .GroupBy(
                                 keySelector: static pair => Construction.RootOf(pair.Implementation!),
                                 elementSelector: static pair => (ISymbol)pair.Contract.OriginalDefinition,
                                 comparer: SymbolEqualityComparer.Default))
                {
                    Pair(found: found, contracts: one);
                }
            }
        }

        ImmutableDictionary<ISymbol, ImmutableHashSet<ISymbol>>.Builder index =
            ImmutableDictionary.CreateBuilder<ISymbol, ImmutableHashSet<ISymbol>>(SymbolEqualityComparer.Default);

        foreach (KeyValuePair<ISymbol, HashSet<ISymbol>> pair in found)
        {
            index[pair.Key] = pair.Value.ToImmutableHashSet(SymbolEqualityComparer.Default);
        }

        return index.ToImmutable();
    }

    private static void Pair(Dictionary<ISymbol, HashSet<ISymbol>> found, IGrouping<ISymbol, ISymbol> contracts)
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
