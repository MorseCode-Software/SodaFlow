using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace SodaFlow.Analyzers;

/// <summary>
///     Says if a task is complete before the code awaits it. An await of such a
///     task does not stop the call, and the code after it runs in the call.
/// </summary>
/// <remarks>
///     The answer is true for a task that the platform gives complete. It is
///     also true for a method of this compilation that gives only such tasks,
///     and for a local with one such value. A readonly field and a get-only
///     property with such an initializer count also, and so does a getter that
///     gives only such tasks. For each other task, the answer is false, and the
///     walk stops at the await.
/// </remarks>
internal static class CompleteTasks
{
    /// <summary>True when the task of an awaited expression is complete at the await.</summary>
    /// <param name="region">The region, for the symbols of the code.</param>
    /// <param name="task">The expression that the code awaits.</param>
    public static bool IsComplete(LoopRegion region, ExpressionSyntax task) =>
        IsComplete(region: region, task: task, visiting: new HashSet<ISymbol>(SymbolEqualityComparer.Default));

    // The set has each method that the question is in now. A method that awaits itself gets the
    // answer false there.
    private static bool IsComplete(LoopRegion region, ExpressionSyntax task, HashSet<ISymbol> visiting)
    {
        ExpressionSyntax written = Unwrapped(task);

        // The default value of a ValueTask is complete. The default value of a Task is null.
        if (written is DefaultExpressionSyntax)
        {
            return region.TypeOf(written) is INamedTypeSymbol { Name: "ValueTask" } type
                   && SodaFlowSymbols.IsTask(type);
        }

        ISymbol? symbol = region.SymbolOf(written);

        if (SodaFlowSymbols.MakesACompleteTask(symbol))
        {
            return true;
        }

        return symbol switch
        {
            ILocalSymbol local => HasOneCompleteValue(region: region, local: local, visiting: visiting),
            // A getter with a body gives the tasks of its return statements, as a method does.
            IFieldSymbol { IsReadOnly: true } or IPropertySymbol { IsReadOnly: true } =>
                HasACompleteInitializer(region: region, member: symbol.OriginalDefinition, visiting: visiting)
                || (symbol is IPropertySymbol { GetMethod: { } getter }
                    && GivesOnlyCompleteTasks(region: region, method: getter, visiting: visiting)),
            IMethodSymbol method when written is InvocationExpressionSyntax call =>
                SodaFlowSymbols.IsTask(method.ContainingType)
                    ? IsCompleteFromThePlatform(region: region, method: method, call: call, visiting: visiting)
                    : GivesOnlyCompleteTasks(region: region, method: method, visiting: visiting),
            _ => false
        };
    }

    // The task in parentheses, and the task that ConfigureAwait gets.
    private static ExpressionSyntax Unwrapped(ExpressionSyntax task)
    {
        while (true)
        {
            switch (task)
            {
                case ParenthesizedExpressionSyntax inParentheses:
                    task = inParentheses.Expression;

                    break;
                case InvocationExpressionSyntax
                {
                    Expression: MemberAccessExpressionSyntax { Name.Identifier.ValueText: "ConfigureAwait" } configured
                }:
                    task = configured.Expression;

                    break;
                default:
                    return task;
            }
        }
    }

    // WhenAll of tasks that are each complete is complete, and so is WhenAll of no task. A Delay
    // of the constant zero, or of TimeSpan.Zero, is complete also.
    private static bool IsCompleteFromThePlatform(
        LoopRegion region,
        ISymbol method,
        InvocationExpressionSyntax call,
        HashSet<ISymbol> visiting)
    {
        SeparatedSyntaxList<ArgumentSyntax> arguments = call.ArgumentList.Arguments;

        return method.Name switch
        {
            "WhenAll" => arguments.All(argument =>
                IsComplete(region: region, task: argument.Expression, visiting: visiting)),
            "Delay" => arguments.Count == 1 && IsZero(region: region, delay: arguments[0].Expression),
            _ => false
        };
    }

    /// <summary>True when each task that a method of this compilation gives is complete.</summary>
    /// <param name="region">The region, for the symbols of the code.</param>
    /// <param name="method">The method.</param>
    public static bool GivesOnlyCompleteTasks(LoopRegion region, IMethodSymbol method) =>
        GivesOnlyCompleteTasks(
            region: region,
            method: method,
            visiting: new HashSet<ISymbol>(SymbolEqualityComparer.Default));

    private static bool IsZero(LoopRegion region, ExpressionSyntax delay) =>
        region.ModelFor(delay).GetConstantValue(delay) is { HasValue: true, Value: 0 }
        || (delay is MemberAccessExpressionSyntax { Name.Identifier.ValueText: "Zero" }
            && region.SymbolOf(delay) is IFieldSymbol { ContainingType: { Name: "TimeSpan" } owner }
            && owner.ContainingNamespace?.ToDisplayString() == "System");

    // A method with async and no await of a task that can be not complete runs to its end in the
    // call. A method with no async gives the task of one of its return statements. A call of a
    // virtual method can run a different body, thus the answer for it is false.
    private static bool GivesOnlyCompleteTasks(LoopRegion region, IMethodSymbol method, HashSet<ISymbol> visiting)
    {
        IMethodSymbol definition = (method.ReducedFrom ?? method).OriginalDefinition;

        if (definition
            is not { IsVirtual: false, IsAbstract: false, IsOverride: false, DeclaringSyntaxReferences.Length: 1 })
        {
            return false;
        }

        SyntaxNode? body =
            definition.DeclaringSyntaxReferences[0].GetSyntax() switch
            {
                MethodDeclarationSyntax declared => (SyntaxNode?)declared.Body ?? declared.ExpressionBody?.Expression,
                LocalFunctionStatementSyntax declared =>
                    (SyntaxNode?)declared.Body ?? declared.ExpressionBody?.Expression,
                AccessorDeclarationSyntax declared => (SyntaxNode?)declared.Body ?? declared.ExpressionBody?.Expression,
                ArrowExpressionClauseSyntax declared => declared.Expression,
                _ => null
            };

        if (body == null || !visiting.Add(definition))
        {
            return false;
        }

        bool complete =
            definition.IsAsync
                ? NeverWaits(region: region, body: body, visiting: visiting)
                : ReturnsOnlyCompleteTasks(region: region, body: body, visiting: visiting);

        visiting.Remove(definition);

        return complete;
    }

    private static bool NeverWaits(LoopRegion region, SyntaxNode body, HashSet<ISymbol> visiting) =>
        OwnNodesOf(body).All(node => DoesNotWait(region: region, node: node, visiting: visiting));

    private static bool DoesNotWait(LoopRegion region, SyntaxNode node, HashSet<ISymbol> visiting) =>
        node switch
        {
            AwaitExpressionSyntax awaited => IsComplete(region: region, task: awaited.Expression, visiting: visiting),
            // An await in a foreach or in a using waits for a task that the walk does not see.
            CommonForEachStatementSyntax each => !each.AwaitKeyword.IsKind(SyntaxKind.AwaitKeyword),
            UsingStatementSyntax used => !used.AwaitKeyword.IsKind(SyntaxKind.AwaitKeyword),
            LocalDeclarationStatementSyntax local => !local.AwaitKeyword.IsKind(SyntaxKind.AwaitKeyword),
            _ => true
        };

    /// <summary>
    ///     Says which items of an await foreach come in the call, before the call
    ///     can stop.
    /// </summary>
    /// <remarks>
    ///     The sequence is a call of an async iterator of this compilation. Each
    ///     item comes in the call when the iterator awaits only complete tasks.
    ///     The first item comes in the call when a yield return at the first level
    ///     of the body has no other await before it. A goto can skip that yield
    ///     return. Thus, a body with a label gets the answer false.
    /// </remarks>
    /// <param name="region">The region, for the symbols of the code.</param>
    /// <param name="sequence">The sequence that the await foreach enumerates.</param>
    public static (bool First, bool Each) ItemsComeAtOnce(LoopRegion region, ExpressionSyntax sequence)
    {
        if (UnwrappedSequence(region: region, sequence: sequence) is not InvocationExpressionSyntax call
            || region.SymbolOf(call) is not IMethodSymbol method
            || (method.ReducedFrom ?? method).OriginalDefinition
            is not
            {
                IsAsync: true, IsVirtual: false, IsAbstract: false, IsOverride: false, DeclaringSyntaxReferences.Length: 1
            } definition)
        {
            return (false, false);
        }

        BlockSyntax? body =
            definition.DeclaringSyntaxReferences[0].GetSyntax() switch
            {
                MethodDeclarationSyntax declared => declared.Body,
                LocalFunctionStatementSyntax declared => declared.Body,
                _ => null
            };

        if (body == null)
        {
            return (false, false);
        }

        HashSet<ISymbol> visiting = new(SymbolEqualityComparer.Default) { definition };

        if (NeverWaits(region: region, body: body, visiting: visiting))
        {
            return (true, true);
        }

        StatementSyntax? yielded =
            body.Statements.FirstOrDefault(static statement => statement.IsKind(SyntaxKind.YieldReturnStatement));

        bool first =
            yielded != null
            && !OwnNodesOf(body).Any(static node => node is LabeledStatementSyntax)
            && OwnNodesOf(body)
                .Where(node => node.SpanStart < yielded.Span.End)
                .All(node => DoesNotWait(region: region, node: node, visiting: visiting));

        return (first, false);
    }

    // The sequence that WithCancellation gets. The await foreach enumerates that sequence, and
    // gives the token to its GetAsyncEnumerator.
    private static ExpressionSyntax UnwrappedSequence(LoopRegion region, ExpressionSyntax sequence)
    {
        while (true)
        {
            ExpressionSyntax written = Unwrapped(sequence);

            if (written is not InvocationExpressionSyntax
                {
                    Expression: MemberAccessExpressionSyntax
                    {
                        Name.Identifier.ValueText: "WithCancellation"
                    } cancelable
                } call
                || region.SymbolOf(call) is not IMethodSymbol
                {
                    ContainingType.Name: "TaskAsyncEnumerableExtensions" or "ConfiguredCancelableAsyncEnumerable"
                })
            {
                return written;
            }

            sequence = cancelable.Expression;
        }
    }

    private static bool ReturnsOnlyCompleteTasks(LoopRegion region, SyntaxNode body, HashSet<ISymbol> visiting)
    {
        if (body is ExpressionSyntax result)
        {
            return IsComplete(region: region, task: result, visiting: visiting);
        }

        ExpressionSyntax[] results = [.. LoopStateEvaluator.ReturnExpressions(body)];

        return results.Length > 0
               && results.All(returned => IsComplete(region: region, task: returned, visiting: visiting));
    }

    // The nodes of a body, without the code of a lambda or of a local function in it.
    private static IEnumerable<SyntaxNode> OwnNodesOf(SyntaxNode body) =>
        body.DescendantNodesAndSelf(static node =>
            node is not (AnonymousFunctionExpressionSyntax or LocalFunctionStatementSyntax));

    // The region gives the initializer of a member only when no constructor can write the member.
    private static bool HasACompleteInitializer(LoopRegion region, ISymbol member, HashSet<ISymbol> visiting)
    {
        if (region.InitializerOf(member) is not { } value || !visiting.Add(member))
        {
            return false;
        }

        bool complete = IsComplete(region: region, task: value, visiting: visiting);
        visiting.Remove(member);

        return complete;
    }

    // A local with an initializer and no other write has the value of that initializer at each
    // read. The search for a write is by the name, in the function that declares the local.
    private static bool HasOneCompleteValue(LoopRegion region, ISymbol local, HashSet<ISymbol> visiting)
    {
        if (local.DeclaringSyntaxReferences.Length != 1
            || local.DeclaringSyntaxReferences[0].GetSyntax()
                is not VariableDeclaratorSyntax { Initializer.Value: { } value } declarator
            || declarator.Ancestors()
                    .FirstOrDefault(static node =>
                        node
                            is BaseMethodDeclarationSyntax
                            or AccessorDeclarationSyntax
                            or LocalFunctionStatementSyntax
                            or AnonymousFunctionExpressionSyntax)
                is not { } function)
        {
            return false;
        }

        string name = local.Name;

        bool written =
            function.DescendantNodes()
                .Any(node =>
                    node switch
                    {
                        AssignmentExpressionSyntax assignment => Names(assignment.Left),
                        ArgumentSyntax { RefOrOutKeyword.RawKind: not 0 } argument => Names(argument.Expression),
                        RefExpressionSyntax reference => Names(reference.Expression),
                        _ => false
                    });

        return !written && IsComplete(region: region, task: value, visiting: visiting);

        bool Names(SyntaxNode target) =>
            target.DescendantNodesAndSelf()
                .OfType<IdentifierNameSyntax>()
                .Any(identifier => identifier.Identifier.ValueText == name);
    }
}
