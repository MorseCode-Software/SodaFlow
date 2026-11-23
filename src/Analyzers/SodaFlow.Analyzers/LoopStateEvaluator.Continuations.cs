using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace SodaFlow.Analyzers;

// The part of the evaluator for a continuation that ContinueWith runs in the call.
internal sealed partial class LoopStateEvaluator
{
    /// <summary>
    ///     The function of a call of <c>ContinueWith</c> that runs it in the
    ///     call. That is so where the call gets <c>ExecuteSynchronously</c> as a
    ///     constant, and its task is complete. Null for each other call.
    /// </summary>
    /// <remarks>
    ///     The writes of the function occur in the call. The task keeps the
    ///     exception of each read that throws, and each object that the function
    ///     throws. Thus, the walk reports such a read only where the code waits
    ///     for the task: see <see cref="ReadsThatAWaitThrows" />. An await puts
    ///     the faults into the task of its async function. The function
    ///     gets the task, and the state object where the call gives one.
    /// </remarks>
    /// <param name="invocation">The call.</param>
    /// <param name="method">The method of the call.</param>
    private Followed? FollowContinuation(InvocationExpressionSyntax invocation, IMethodSymbol method)
    {
        if (!SodaFlowSymbols.IsTask(method.ContainingType)
            || invocation.Expression is not MemberAccessExpressionSyntax { Expression: var task })
        {
            return null;
        }

        SeparatedSyntaxList<ArgumentSyntax> arguments = invocation.ArgumentList.Arguments;
        ExpressionSyntax? function = null;
        ExpressionSyntax? given = null;
        bool inTheCall = false;

        for (int i = 0; i < arguments.Count; i++)
        {
            switch (ParameterFor(method: method, argument: arguments[i], position: i))
            {
                case { Type.TypeKind: TypeKind.Delegate }:
                    function = arguments[i].Expression;

                    break;
                case { Name: "state" }:
                    given = arguments[i].Expression;

                    break;
                case { Type: INamedTypeSymbol { TypeKind: TypeKind.Enum } options }:
                    inTheCall = this.HasTheOption(options: options, written: arguments[i].Expression);

                    break;
            }
        }

        if (!inTheCall || function == null || !CompleteTasks.IsComplete(region: this.region, task: task))
        {
            return null;
        }

        LoopState functions = this.Eval(function);

        ImmutableArray<LoopState> parameters = ImmutableArray.Create(this.Eval(task), this.Eval(given));

        Followed? followed =
            functions.Functions.Aggregate(
                seed: (Followed?)null,
                func: (all, each) =>
                    Both(
                        first: all,
                        second: this.FollowFunction(
                            call: invocation,
                            name: NameOf(function),
                            function: each,
                            target: functions,
                            arguments: default,
                            each: (function, LoopState.None, ManyTimes: false),
                            byPosition: parameters)));

        if (followed == null)
        {
            return null;
        }

        ImmutableArray<(ITypeSymbol? Type, LoopState State)> faults =
            followed.Thrown.Select(static thrown => (thrown.Type, thrown.State)).ToImmutableArray();

        // The exception of a read is a fault of the task, of a type that the walk does not know. Its state
        // has a query that keeps the findings of the function. Thus, each method that gives the objects of a
        // task to a different task gives the findings also, as WhenAll does.
        if (!followed.Findings.IsEmpty)
        {
            QueryValue kept = this.QueryAt(invocation);
            kept.Hold(followed.Findings);

            if (!this.quiet)
            {
                this.Frame.Wait(kept);
                this.region.KeepsTheReadsOfAContinuation = true;
            }

            faults = faults.Add((null, LoopState.None.WithQuery(kept)));
        }

        return new Followed(
            state: followed.State.WithFaults(faults),
            findings: ImmutableArray<Finding>.Empty,
            outs: followed.Outs,
            sets: followed.Sets,
            through: followed.Through,
            alwaysGives: followed.AlwaysGives);
    }

    // True for a constant value of TaskContinuationOptions with ExecuteSynchronously. A different enumeration has no
    // such member.
    private bool HasTheOption(INamedTypeSymbol options, ExpressionSyntax written) =>
        options.GetMembers("ExecuteSynchronously").FirstOrDefault() is IFieldSymbol { ConstantValue: int flag }
        && this.region.ModelFor(written).GetConstantValue(written) is { HasValue: true, Value: int value }
        && (value & flag) != 0;

    /// <summary>
    ///     The findings that a wait of a task reports. They are the reads of
    ///     each continuation that ran in the call of <c>ContinueWith</c> for that
    ///     task. Such a wait is <c>Wait</c>, <c>Task.WaitAll</c>, <c>Result</c>,
    ///     and <c>GetResult</c> of the awaiter. Each wait takes the findings that
    ///     it reports. An evaluator that changes nothing takes none.
    /// </summary>
    /// <param name="wait">The wait.</param>
    public ImmutableArray<Finding> ReadsThatAWaitThrows(SyntaxNode wait)
    {
        if (this.quiet || !this.region.KeepsTheReadsOfAContinuation)
        {
            return ImmutableArray<Finding>.Empty;
        }

        ExpressionSyntax?[] tasks =
            wait switch
            {
                MemberAccessExpressionSyntax { Name.Identifier.ValueText: "Result" } read => [read.Expression],
                MemberBindingExpressionSyntax { Name.Identifier.ValueText: "Result" } binding =>
                    [ConditionalReceiver(binding)],
                InvocationExpressionSyntax
                {
                    Expression: MemberAccessExpressionSyntax { Name.Identifier.ValueText: "Wait" } call
                } => [call.Expression],
                InvocationExpressionSyntax
                {
                    Expression: MemberAccessExpressionSyntax { Name.Identifier.ValueText: "WaitAll" }
                } all when this.region.SymbolOf(all) is IMethodSymbol { ContainingType: var owner }
                           && SodaFlowSymbols.IsTask(owner) =>
                    [.. all.ArgumentList.Arguments.Select(static argument => argument.Expression)],
                InvocationExpressionSyntax
                {
                    Expression: MemberAccessExpressionSyntax
                    {
                        Name.Identifier.ValueText: "GetResult",
                        Expression: InvocationExpressionSyntax
                        {
                            Expression: MemberAccessExpressionSyntax
                            {
                                Name.Identifier.ValueText: "GetAwaiter"
                            } awaiter
                        } awaited
                    }
                } => [this.TheTaskOfAnAwaiter(awaited: awaited, awaitable: awaiter.Expression)],
                _ => []
            };

        return tasks
            .SelectMany(task => this.Eval(task).Faults)
            .SelectMany(static fault => fault.State.Queries)
            .Where(this.IsAContinuation)
            .SelectMany(static continuation => continuation.TakeHeld() ?? ImmutableArray<Finding>.Empty)
            .ToImmutableArray();
    }

    // A query of a task is the task of a continuation: see FollowContinuation. A different query is a sequence that an
    // exception object holds.
    private bool IsAContinuation(QueryValue query) =>
        query.Call is InvocationExpressionSyntax made
        && SodaFlowSymbols.IsTask(this.region.TypeOf(made) as INamedTypeSymbol);
}
