using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace SodaFlow.Analyzers;

// The part of the evaluator for the value of a task. A task that the code creates with new gets that value from its
// function.
internal sealed partial class LoopStateEvaluator
{
    /// <summary>
    ///     The state of a read of the value of a task, with the value that the
    ///     functions of the task return. Such a read is <c>Result</c>, or
    ///     <c>GetResult</c> of the awaiter of the task. Each other expression
    ///     keeps the given state.
    /// </summary>
    /// <remarks>
    ///     The read waits for the task. Thus, the function ran before the read
    ///     gives a value, on this thread or on a different thread. Where no code
    ///     runs the task, the read does not end, and no code after it runs. The
    ///     read does not run the function again. Thus, the writes of the function
    ///     stay where <c>RunSynchronously</c> runs it.
    /// </remarks>
    /// <param name="read">The expression.</param>
    /// <param name="state">The state of the expression without the function.</param>
    private LoopState WithTheResultOfATask(SyntaxNode read, LoopState state)
    {
        // A test of the name comes before each question to the compiler, because each name and each
        // call comes here.
        ExpressionSyntax? task =
            read switch
            {
                MemberAccessExpressionSyntax { Name.Identifier.ValueText: "Result" } access => access.Expression,
                MemberBindingExpressionSyntax { Name.Identifier.ValueText: "Result" } binding =>
                    ConditionalReceiver(binding),
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
                } => this.TheTaskOfAnAwaiter(awaited: awaited, awaitable: awaiter.Expression),
                _ => null
            };

        // GetResult is of the awaiter of a task. A Result of a type that is not a task is a different
        // property.
        ITypeSymbol? value =
            task == null
                ? null
                : read is InvocationExpressionSyntax
                    ? (this.region.SymbolOf(read) as IMethodSymbol)?.ReturnType
                    : this.region.SymbolOf(read) is IPropertySymbol property
                      && SodaFlowSymbols.IsTask(property.ContainingType)
                        ? property.Type
                        : null;

        return task == null || value == null
            ? state
            : state.Join(this.ResultOfTheFunctions(access: read, written: task, task: this.Eval(task), value: value));
    }

    /// <summary>
    ///     The task whose awaiter <c>GetAwaiter</c> gives, also through
    ///     <c>ConfigureAwait</c>. Null for an awaiter of a different value.
    /// </summary>
    /// <param name="awaited">The call of <c>GetAwaiter</c>.</param>
    /// <param name="awaitable">The receiver of <c>GetAwaiter</c>.</param>
    private ExpressionSyntax? TheTaskOfAnAwaiter(SyntaxNode awaited, ExpressionSyntax awaitable)
    {
        if (this.region.SymbolOf(awaited) is IMethodSymbol { ContainingType: var type } && SodaFlowSymbols.IsTask(type))
        {
            return awaitable;
        }

        return awaitable is InvocationExpressionSyntax
               {
                   Expression: MemberAccessExpressionSyntax { Name.Identifier.ValueText: "ConfigureAwait" } configured
               }
               && this.region.SymbolOf(awaitable) is IMethodSymbol { ContainingType: var configuredType }
               && SodaFlowSymbols.IsTask(configuredType)
            ? configured.Expression
            : null;
    }

    /// <summary>
    ///     The value that the functions of a task return, for a property pattern
    ///     that reads <c>Result</c> of the task. None for each other member.
    /// </summary>
    /// <param name="name">The name of the property in the pattern.</param>
    /// <param name="member">The member that the pattern reads.</param>
    /// <param name="task">The state of the value that the pattern is for.</param>
    public LoopState ResultOfATask(ExpressionSyntax name, ISymbol member, LoopState task) =>
        member is IPropertySymbol { Name: "Result" } property && SodaFlowSymbols.IsTask(property.ContainingType)
            ? this.ResultOfTheFunctions(access: name, written: name, task: task, value: property.Type)
            : LoopState.None;

    /// <summary>
    ///     The calls that an await makes with no call syntax: <c>GetAwaiter</c>
    ///     of the value, <c>IsCompleted</c> of the awaiter, and <c>GetResult</c>
    ///     of the awaiter. Only a method of this compilation has a body to read.
    /// </summary>
    /// <remarks>
    ///     The await calls <c>GetResult</c> in the call only when the task is
    ///     complete. Otherwise, the continuation calls it later.
    /// </remarks>
    /// <param name="awaited">The await.</param>
    public Followed? FollowAwaiter(AwaitExpressionSyntax awaited)
    {
        if (!this.region.Symbols.DeclaresAwaiters
            || this.region.ModelFor(awaited).GetAwaitExpressionInfo(awaited)
                is not { GetAwaiterMethod: { } start } info)
        {
            return null;
        }

        LoopState value = this.Self(awaited.Expression);

        // An extension method gets the value as its argument.
        Followed? awaiter =
            start.IsStatic
                ? this.FollowImplied(
                    call: awaited,
                    member: (start.ReducedFrom ?? start).OriginalDefinition,
                    owner: null,
                    arguments: [(awaited.Expression, value)])
                : this.FollowImplied(call: awaited, member: start, owner: value, arguments: []);

        LoopState state = awaiter?.State ?? LoopState.None;

        return Both(
            first: Both(
                first: awaiter,
                second: info.IsCompletedProperty?.GetMethod is { } test
                    ? this.FollowImplied(call: awaited, member: test, owner: state, arguments: [])
                    : null),
            second: info.GetResultMethod is { } result
                    && CompleteTasks.IsComplete(region: this.region, task: awaited.Expression)
                ? this.FollowImplied(call: awaited, member: result, owner: state, arguments: [])
                : null);
    }

    /// <summary>
    ///     True when each method that a call of the member can run on the object
    ///     gives only complete tasks. Only a method of this compilation can give
    ///     true. A member of an interface that the object does not resolve gives
    ///     false.
    /// </summary>
    /// <param name="owner">The state of the object.</param>
    /// <param name="member">The member that the code calls.</param>
    public bool GivesOnlyCompleteTasks(LoopState owner, IMethodSymbol member)
    {
        IMethodSymbol[] actual = [.. owner.Members?.Resolve(member.OriginalDefinition).OfType<IMethodSymbol>() ?? []];

        return (actual.Length == 0 ? [member] : actual)
            .All(method => CompleteTasks.GivesOnlyCompleteTasks(region: this.region, method: method));
    }

    // The task gives its state object to the function as its parameter, as RunSynchronously does.
    private LoopState ResultOfTheFunctions(SyntaxNode access, ExpressionSyntax written, LoopState task, ITypeSymbol value)
        =>
            this.FollowLazy(access: access, written: written, lazy: task.AsOneOfItsElements, value: value)?.State
            ?? LoopState.None;
}
