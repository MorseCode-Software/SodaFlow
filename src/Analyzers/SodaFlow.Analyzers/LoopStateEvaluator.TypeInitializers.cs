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
        // The walk asks for the symbol of each call, and of each other member access. Thus, the
        // symbol here is no new question. A question about each type of the compilation is one,
        // because the driver can make the symbols of the analyzer again for each file.
        if (this.quiet
            || (access.Parent is InvocationExpressionSyntax call
                && call.Expression == access
                && this.region.SymbolOf(call) is IMethodSymbol { MethodKind: not MethodKind.DelegateInvoke } called
                    ? called
                    : this.region.SymbolOf(access))
                is not { IsStatic: true, ContainingType: { DeclaringSyntaxReferences.IsEmpty: false } owner } member
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
