using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace SodaFlow.Analyzers;

// The part of the evaluator for the static constructor of a type, which runs at the first use of a static member.
internal sealed partial class LoopStateEvaluator
{
    /// <summary>
    ///     The result of the static constructor of a type, at the first use of a
    ///     static member of that type in a block. Null at each other member
    ///     access.
    /// </summary>
    /// <remarks>
    ///     C# runs an explicit static constructor before the first use of a
    ///     static member, and its static initializers first. A type with
    ///     initializers only has no such fixed time: the runtime can run them at
    ///     any time before the first read of a field. Thus, the walk reads only a
    ///     type with an explicit static constructor. A constant is no use. The
    ///     code of the type can be the first use, when it runs on an object.
    /// </remarks>
    /// <param name="access">The member access, as in <c>T.Value</c>.</param>
    public Followed? FollowTheStaticConstructor(MemberAccessExpressionSyntax access)
    {
        // The name index of the compiler comes first, so that a compilation with no static
        // constructor does not build the DeclaredCode at each member access.
        if (this.quiet
            || !this.region.Symbols.DeclaresStaticConstructors
            || !this.region.Symbols.DeclaredCode.HasAStaticConstructor(
                access.Expression switch
                {
                    SimpleNameSyntax name => name.Identifier.ValueText,
                    MemberAccessExpressionSyntax qualified => qualified.Name.Identifier.ValueText,
                    _ => string.Empty
                })
            || this.region.SymbolOf(access) is not { IsStatic: true, ContainingType: { } owner } member
            || member is IFieldSymbol { IsConst: true }
            || owner.OriginalDefinition.StaticConstructors.FirstOrDefault()?.DeclaringSyntaxReferences.FirstOrDefault()
                ?.GetSyntax() is not ConstructorDeclarationSyntax constructor
            || !this.region.StartsTheType(owner.OriginalDefinition))
        {
            return null;
        }

        // The static initializers run in the order of the code, and then the body.
        IEnumerable<SyntaxNode> parts =
            constructor.Parent is TypeDeclarationSyntax type
                ? type.Members
                    .Where(static part => part.Modifiers.Any(SyntaxKind.StaticKeyword))
                    .SelectMany(InitializersOf)
                    .OfType<SyntaxNode>()
                : [];

        if ((constructor.Body ?? (SyntaxNode?)constructor.ExpressionBody?.Expression) is { } body)
        {
            parts = parts.Append(body);
        }

        return parts.Aggregate(
            seed: (Followed?)null,
            func: (all, part) =>
                Both(
                    first: all,
                    second: this.Enter(
                        call: access,
                        name: owner.Name,
                        body: part,
                        bindings: [],
                        definer: null,
                        members: new Dictionary<ISymbol, LoopState>(SymbolEqualityComparer.Default),
                        receiver: null,
                        target: null)));

        static IEnumerable<ExpressionSyntax?> InitializersOf(MemberDeclarationSyntax part) =>
            part switch
            {
                FieldDeclarationSyntax field => field.Declaration.Variables
                    .Select(static variable => variable.Initializer?.Value),
                PropertyDeclarationSyntax property => [property.Initializer?.Value],
                _ => []
            };
    }
}
