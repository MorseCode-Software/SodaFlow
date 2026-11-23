using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace SodaFlow.Analyzers;

// The part of the evaluator for a partial method and a partial property. The symbol of a use is
// the part with no code, and the walk reads the code of the second part.
internal sealed partial class LoopStateEvaluator
{
    // The part with the body, for a partial method that has one. Each other method is itself.
    private static IMethodSymbol ImplementationPartOf(IMethodSymbol method) =>
        method.PartialImplementationPart ?? method;

    /// <summary>
    ///     The declarations of a member. A partial property has a second
    ///     declaration, with its code, in the type.
    /// </summary>
    /// <remarks>
    ///     The symbol of a partial property gives only the first declaration, and
    ///     Roslyn 4.8 has no member that gives the second part. Thus, the walk
    ///     finds the second declaration by its name. Only a declaration with the
    ///     partial keyword causes this search.
    /// </remarks>
    private static IEnumerable<SyntaxNode> DeclarationsOf(ISymbol member)
    {
        IEnumerable<SyntaxNode> declared =
            member.DeclaringSyntaxReferences.Select(static reference => reference.GetSyntax());

        return member.DeclaringSyntaxReferences.Any(static reference => IsPartial(reference.GetSyntax()))
            ? declared.Concat(SecondPartsOf(member))
            : declared;
    }

    // Each partial declaration of a property in its type. That includes the first declaration
    // again, which has no code and changes no result.
    private static IEnumerable<SyntaxNode> SecondPartsOf(ISymbol property) =>
        property.ContainingType.DeclaringSyntaxReferences
            .Select(static reference => reference.GetSyntax())
            .OfType<TypeDeclarationSyntax>()
            .SelectMany(static type => type.Members.OfType<PropertyDeclarationSyntax>())
            .Where(other => other.Identifier.ValueText == property.Name && IsPartial(other));

    private static bool IsPartial(SyntaxNode declaration) =>
        declaration is PropertyDeclarationSyntax { Modifiers: var modifiers }
        && modifiers.Any(SyntaxKind.PartialKeyword);
}
