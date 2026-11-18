using Microsoft.CodeAnalysis;
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

    // The task gives its state object to the function as its parameter, as RunSynchronously does.
    private LoopState ResultOfTheFunctions(SyntaxNode access, ExpressionSyntax written, LoopState task, ITypeSymbol value)
        =>
            this.FollowLazy(access: access, written: written, lazy: task.AsOneOfItsElements, value: value)?.State
            ?? LoopState.None;
}
