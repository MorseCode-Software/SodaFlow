using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace SodaFlow.Analyzers;

/// <summary>
///     The methods of one compilation that an override in it replaces, each
///     with the types that declare such an override.
/// </summary>
/// <remarks>
///     The first call of <see cref="CanRunFor" /> makes the index from
///     declarations only. Most compilations never make it, because a call of a
///     virtual method on an object that the walk did not see made is not
///     usual.
/// </remarks>
internal sealed class OverrideIndex(in Compilation compilation)
{
    private readonly Lazy<ImmutableDictionary<IMethodSymbol, ImmutableArray<INamedTypeSymbol>>> byMethod =
        IndexOf(compilation);

    /// <summary>
    ///     True when an override that the compilation declares can run for a
    ///     call of the method on a value of the given type. The override must be
    ///     in that type or in a type that derives from it. A type that is not a
    ///     class, or no type, can be the type of each such override.
    /// </summary>
    /// <remarks>
    ///     The compiler gives a call the override that the type of the receiver
    ///     inherits, when there is one. Thus, an override in a base type of the
    ///     receiver is the method of the call, and not a different body.
    /// </remarks>
    public bool CanRunFor(IMethodSymbol method, ITypeSymbol? receiver) =>
        this.byMethod.Value.TryGetValue(
            key: method.OriginalDefinition,
            value: out ImmutableArray<INamedTypeSymbol> types)
        && (receiver is not INamedTypeSymbol { TypeKind: TypeKind.Class } named
            || types.Any(type => IsOrDerivesFrom(type: type, other: named.OriginalDefinition)));

    private static Lazy<ImmutableDictionary<IMethodSymbol, ImmutableArray<INamedTypeSymbol>>> IndexOf(
        Compilation compilation) =>
        new(() => Build(compilation));

    private static ImmutableDictionary<IMethodSymbol, ImmutableArray<INamedTypeSymbol>> Build(Compilation compilation)
    {
        // The syntax gives the names of the methods with the override modifier. The search reads the
        // declarations of types only, and not the bodies.
        ImmutableHashSet<string> names =
            compilation.SyntaxTrees
                .SelectMany(static tree =>
                    tree.GetRoot()
                        .DescendantNodes(static node =>
                            node is CompilationUnitSyntax or BaseNamespaceDeclarationSyntax or TypeDeclarationSyntax))
                .OfType<MethodDeclarationSyntax>()
                .Where(static method => method.Modifiers.Any(SyntaxKind.OverrideKeyword))
                .Select(static method => method.Identifier.ValueText)
                .ToImmutableHashSet(StringComparer.Ordinal);

        Dictionary<IMethodSymbol, ImmutableArray<INamedTypeSymbol>.Builder> found =
            new(SymbolEqualityComparer.Default);

        if (!names.IsEmpty)
        {
            foreach (IMethodSymbol method in compilation
                         .GetSymbolsWithName(predicate: names.Contains, filter: SymbolFilter.Member)
                         .OfType<IMethodSymbol>())
            {
                // An override of an override replaces each method in the chain.
                for (IMethodSymbol? replaced = method.OverriddenMethod;
                     replaced != null;
                     replaced = replaced.OverriddenMethod)
                {
                    if (!found.TryGetValue(
                            key: replaced.OriginalDefinition,
                            value: out ImmutableArray<INamedTypeSymbol>.Builder? types))
                    {
                        types = ImmutableArray.CreateBuilder<INamedTypeSymbol>();
                        found[replaced.OriginalDefinition] = types;
                    }

                    types.Add(method.ContainingType.OriginalDefinition);
                }
            }
        }

        ImmutableDictionary<IMethodSymbol, ImmutableArray<INamedTypeSymbol>>.Builder index =
            ImmutableDictionary.CreateBuilder<IMethodSymbol, ImmutableArray<INamedTypeSymbol>>(
                SymbolEqualityComparer.Default);

        foreach (KeyValuePair<IMethodSymbol, ImmutableArray<INamedTypeSymbol>.Builder> pair in found)
        {
            index[pair.Key] = pair.Value.ToImmutable();
        }

        return index.ToImmutable();
    }

    private static bool IsOrDerivesFrom(INamedTypeSymbol type, ISymbol other)
    {
        for (INamedTypeSymbol? one = type; one != null; one = one.BaseType)
        {
            if (SymbolEqualityComparer.Default.Equals(x: one.OriginalDefinition, y: other))
            {
                return true;
            }
        }

        return false;
    }
}
