using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace SodaFlow.Analyzers;

// The part of the walker for the objects that an async function throws. Such an object goes into the task of the
// function, and not to the caller.
internal sealed partial class SinkWalker
{
    // Each object that the async function of this walk throws, with the type that the throw names.
    private readonly List<(ITypeSymbol? Type, LoopState State)> intoTheTask = [];

    /// <summary>
    ///     Each object that the function sends to a call of it. An async
    ///     function puts each object that it throws into its task. Thus, the
    ///     call gets those objects only where the caller awaits that task.
    /// </summary>
    /// <param name="call">The call in the caller.</param>
    public IEnumerable<(ITypeSymbol? Type, LoopState State)> ThrownTo(SyntaxNode call) =>
        this.intoTheTask.Count == 0 || !AwaitsTheTaskOf(call) ? this.thrown : this.thrown.Concat(this.intoTheTask);

    // True for an async method, local function, or lambda.
    private static bool IsAsync(SyntaxNode? function) =>
        function switch
        {
            AnonymousFunctionExpressionSyntax lambda => lambda.AsyncKeyword.IsKind(SyntaxKind.AsyncKeyword),
            LocalFunctionStatementSyntax local => local.Modifiers.Any(SyntaxKind.AsyncKeyword),
            MethodDeclarationSyntax method => method.Modifiers.Any(SyntaxKind.AsyncKeyword),
            _ => false
        };

    // An object of an async function goes into its task. A lambda that the walk entered gives its task to a method of
    // the platform, which awaits no such task. Thus, no code gets the object of that lambda.
    private void IntoTheTask(SyntaxNode function, ITypeSymbol? type, LoopState state)
    {
        if (function is AnonymousFunctionExpressionSyntax lambda && this.entered.Contains(lambda))
        {
            return;
        }

        this.intoTheTask.Add((type, state));
    }

    // True when the caller awaits the task of a call at the call. That is an await of the call, also through
    // ConfigureAwait, and an await using whose resource the call disposes.
    private static bool AwaitsTheTaskOf(SyntaxNode call)
    {
        SyntaxNode? task = call.Parent;

        if (task is MemberAccessExpressionSyntax { Name.Identifier.ValueText: "ConfigureAwait" } configure
            && configure.Expression == call)
        {
            task = configure.Parent?.Parent;
        }

        return task switch
        {
            AwaitExpressionSyntax => true,
            UsingStatementSyntax used => used.AwaitKeyword.IsKind(SyntaxKind.AwaitKeyword),
            EqualsValueClauseSyntax { Parent.Parent.Parent: var declared } =>
                declared is UsingStatementSyntax { AwaitKeyword.RawKind: (int)SyntaxKind.AwaitKeyword }
                    or LocalDeclarationStatementSyntax { AwaitKeyword.RawKind: (int)SyntaxKind.AwaitKeyword },
            _ => false
        };
    }
}
