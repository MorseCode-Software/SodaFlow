using System.Collections.Generic;
using System.Collections.Immutable;
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

    /// <summary>
    ///     Each object that the async function of this walk keeps in its task,
    ///     where the caller does not await the task at the call. The state of
    ///     the task has them, and an await of the task throws them after the
    ///     call.
    /// </summary>
    /// <param name="call">The call in the caller.</param>
    public ImmutableArray<(ITypeSymbol? Type, LoopState State)> KeptInTheTaskOf(SyntaxNode call) =>
        this.intoTheTask.Count == 0 || AwaitsTheTaskOf(call)
            ? ImmutableArray<(ITypeSymbol? Type, LoopState State)>.Empty
            : this.intoTheTask.ToImmutableArray();

    // An await of a task throws each object that the task keeps. The await of a task whose result is a task, as the
    // task of WhenAny is, gives that second task and throws nothing.
    private void ThrowsTheFaultsOf(ExpressionSyntax task, SyntaxNode at)
    {
        LoopState awaited = this.evaluator.Eval(task);

        if (awaited.Faults.IsEmpty || LoopStateEvaluator.GivesATask(this.region.TypeOf(task)))
        {
            return;
        }

        foreach ((ITypeSymbol? type, LoopState state) in awaited.Faults)
        {
            this.Sends(from: at, type: type, state: state);
        }
    }

    // A wait of a task throws the exception of each read that a continuation of the task made in the call.
    private void ReportsTheReadsThatAWaitThrows(SyntaxNode wait)
    {
        foreach (Finding finding in this.evaluator.ReadsThatAWaitThrows(wait))
        {
            this.sink(finding);
        }
    }

    // True for a finally block that always throws an object of its own: it has a throw statement at its first level.
    // The runtime then gives that object, and not the object of the try block or of a catch clause. A goto can skip
    // the throw statement. Thus, a finally block with a label gets false. A call in the finally block can also
    // throw, but the walk does not know that each such call throws.
    private static bool ThrowsAnObjectOfItsOwn(FinallyClauseSyntax? clause) =>
        clause != null
        && clause.Block.Statements.Any(static statement => statement is ThrowStatementSyntax)
        && !clause.Block.DescendantNodes(static node =>
                node is not (AnonymousFunctionExpressionSyntax or LocalFunctionStatementSyntax))
            .Any(static node => node is LabeledStatementSyntax);

    // The expression of this walker that enumerates the query that runs now, where the walk knows it.
    private SyntaxNode? enumeratedAt;

    // Each object that leaves the code of the query that this walker runs. Null where the walk does not know the
    // expression that enumerates that query.
    private List<(ITypeSymbol? Type, LoopState State)>? ofTheQuery;

    // True for a lambda of the query that runs now, where the walk knows the expression that enumerates the query.
    // While a query runs, the walk reads only the code of the query. Thus, each lambda around a throw is in that code.
    // An async lambda puts the object into its task.
    private bool IsOfTheQueryThatRuns(SyntaxNode lambda) =>
        this.ofTheQuery != null && !IsAsync(lambda);

    // A query runs where something enumerates it. Thus, an object that leaves the code of the query goes to the catch
    // clauses around the expression that enumerates it. It does not go to the clauses around the code of the query.
    private bool LeavesTheQueryThatRuns(SyntaxNode code, ITypeSymbol? type, LoopState state)
    {
        if (this.ofTheQuery == null || code != this.running?.Call)
        {
            return false;
        }

        this.ofTheQuery.Add((type, state));

        return true;
    }

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
    // ConfigureAwait, and an await using whose resource the call disposes. An await foreach awaits each MoveNextAsync
    // of the sequence that the call gives. That is also true through ConfigureAwait and WithCancellation. An async
    // iterator throws its objects there.
    private static bool AwaitsTheTaskOf(SyntaxNode call)
    {
        SyntaxNode awaited = call;
        SyntaxNode? task = call.Parent;

        while (task is MemberAccessExpressionSyntax
               {
                   Name.Identifier.ValueText: "ConfigureAwait" or "WithCancellation", Parent: { } configured
               } configure
               && configure.Expression == awaited)
        {
            awaited = configured;
            task = configured.Parent;
        }

        return task switch
        {
            AwaitExpressionSyntax => true,
            UsingStatementSyntax used => used.AwaitKeyword.IsKind(SyntaxKind.AwaitKeyword),
            CommonForEachStatementSyntax loop => loop.AwaitKeyword.IsKind(SyntaxKind.AwaitKeyword),
            EqualsValueClauseSyntax { Parent.Parent.Parent: var declared } =>
                declared is UsingStatementSyntax { AwaitKeyword.RawKind: (int)SyntaxKind.AwaitKeyword }
                    or LocalDeclarationStatementSyntax { AwaitKeyword.RawKind: (int)SyntaxKind.AwaitKeyword },
            _ => false
        };
    }
}
