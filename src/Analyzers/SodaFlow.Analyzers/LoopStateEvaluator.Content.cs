using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace SodaFlow.Analyzers;

// The part of the evaluator that gives the content of the result of a library operator, such as a hold, a
// snapshot, or a collect.
internal sealed partial class LoopStateEvaluator
{
    /// <summary>
    ///     The state of what a lambda given to an operator returns, for the
    ///     content of a cell of cells or a cell of streams.
    /// </summary>
    private LoopState LambdaContent(InvocationExpressionSyntax invocation, LoopState parameterState)
    {
        AnonymousFunctionExpressionSyntax? lambda =
            invocation.ArgumentList.Arguments
                .Select(static a => a.Expression)
                .OfType<AnonymousFunctionExpressionSyntax>()
                .FirstOrDefault();

        return lambda == null
            ? LoopState.None
            : this.EvalLambdaResult(lambda: lambda, boundParameters: ImmutableArray.Create(parameterState));
    }

    /// <summary>
    ///     True when the value that the result of an operator holds can hold a
    ///     state, as a cell of cells does. The walk reads the function of the
    ///     operator for its content only then.
    /// </summary>
    private bool HoldsContent(IMethodSymbol method) =>
        method.ReturnType is INamedTypeSymbol { TypeArguments.Length: 1 } result
        && this.region.Symbols.CanHoldState(type: result.TypeArguments[0], compilation: this.region.Assembly);

    /// <summary>
    ///     The state of what the function of a lift returns. Each parameter has
    ///     the content of its input: the receiver first, and then each argument.
    /// </summary>
    private LoopState LiftedContent(
        InvocationExpressionSyntax invocation,
        ExpressionSyntax? receiver,
        LoopState receiverState,
        IEnumerable<LoopState> argumentStates)
    {
        AnonymousFunctionExpressionSyntax? lambda =
            invocation.ArgumentList.Arguments
                .Select(static a => a.Expression)
                .OfType<AnonymousFunctionExpressionSyntax>()
                .FirstOrDefault();

        IEnumerable<LoopState> given = argumentStates.Select(static state => state.Content);

        ImmutableArray<LoopState> inputs =
            (receiver != null ? given.Prepend(receiverState.Content) : given).ToImmutableArray();

        return lambda == null
            ? this.ContentOfANamedFunction(invocation: invocation, given: inputs)
            : this.EvalLambdaResult(lambda: lambda, boundParameters: inputs);
    }

    /// <summary>
    ///     The state of what the functions in the cell of an apply return. Each
    ///     function gets the value of the receiver.
    /// </summary>
    /// <remarks>
    ///     The functions are in the content of the cell, and not at the call.
    ///     The operator runs them at a time that the walk does not know. Thus,
    ///     an evaluator that changes nothing reads them, as for a named function.
    /// </remarks>
    private LoopState AppliedContent(
        InvocationExpressionSyntax invocation,
        IMethodSymbol method,
        ExpressionSyntax? receiver,
        LoopState receiverState,
        LoopState functionCell)
    {
        if (functionCell.Content.Functions.IsEmpty
            || !this.HoldsContent(method)
            || invocation.ArgumentList.Arguments.FirstOrDefault(argument => argument.Expression != receiver)
                is not { } written)
        {
            return LoopState.None;
        }

        LoopStateEvaluator quiet = this.Quiet();
        ImmutableArray<LoopState> given = ImmutableArray.Create(receiverState.Content);

        return functionCell.Content.Functions.Aggregate(
            seed: LoopState.None,
            func: (all, function) =>
                all.Join(
                    quiet.FollowFunction(
                            call: invocation,
                            name: NameOf(written.Expression),
                            function: function,
                            target: functionCell.Content,
                            arguments: default,
                            each: (written.Expression, LoopState.None, ManyTimes: true),
                            byPosition: given)
                        ?.State
                    ?? LoopState.None));
    }

    /// <summary>
    ///     What the function of a merge returns. The first parameter gets the
    ///     content of the receiver. The second gets the content of the second
    ///     stream, or of the list for a merge of a list of streams.
    /// </summary>
    /// <remarks>
    ///     The function runs only when two streams fire in one transaction. The
    ///     walk does not know when that occurs. Thus, it reads the function as a
    ///     function that runs, as for a read that occurs only if a condition is
    ///     true.
    /// </remarks>
    private LoopState MergedContent(
        InvocationExpressionSyntax invocation,
        IMethodSymbol method,
        ITypeSymbol? receiverType,
        LoopState receiverState,
        IReadOnlyList<LoopState> argumentStates)
    {
        if (!method.Parameters.Any(static parameter => parameter.Type.TypeKind == TypeKind.Delegate)
            || !this.HoldsContent(method))
        {
            return LoopState.None;
        }

        ImmutableArray<LoopState> given =
            ImmutableArray.Create(
                item1: receiverState.Content,
                item2: this.region.Symbols.IsStreamType(receiverType) && argumentStates.Count > 0
                    ? argumentStates[0].Content
                    : receiverState.Content);

        return invocation.ArgumentList.Arguments
                .Select(static a => a.Expression)
                .OfType<AnonymousFunctionExpressionSyntax>()
                .FirstOrDefault() is { } lambda
            ? this.EvalLambdaResult(lambda: lambda, boundParameters: given)
            : this.ContentOfANamedFunction(invocation: invocation, given: given);
    }

    /// <summary>
    ///     The values that a hold or an accumulation holds. The initial value is
    ///     one. A hold gets the values of its stream after the close, and an
    ///     accumulation gets what its function returns then. A collect keeps no
    ///     value that it gives.
    /// </summary>
    private LoopState HeldContent(
        InvocationExpressionSyntax invocation,
        IMethodSymbol method,
        LoopState receiverState,
        LoopState initial) =>
        !this.HoldsContent(method)
            ? LoopState.None
            : OperatorName(method) switch
            {
                "HoldLazy" => initial.Join(receiverState.Content.AfterTheClose),
                "Accum" or "AccumLazy" => initial.Join(
                    this.AccumulatesValuesOfTheGraph(method)
                    || this.FunctionCanGiveAState(
                        invocation: invocation,
                        given: receiverState.Content.AfterTheClose)
                        ? this.LambdaContent(
                                invocation: invocation,
                                parameterState: receiverState.Content.AfterTheClose)
                            .AfterTheClose
                        : LoopState.None),
                _ => LoopState.None
            };

    /// <summary>
    ///     The values that a snapshot fires: what its function returns, or else
    ///     the value of its cell. The function gets the value of the stream and of
    ///     each cell at a firing, which is after the close. The walk reads the
    ///     function only for a result that can hold a state.
    /// </summary>
    private LoopState SnapshotContent(
        InvocationExpressionSyntax invocation,
        IMethodSymbol method,
        LoopState receiverState,
        IReadOnlyList<LoopState> argumentStates)
    {
        if (!this.HoldsContent(method))
        {
            return LoopState.None;
        }

        AnonymousFunctionExpressionSyntax? lambda =
            invocation.ArgumentList.Arguments
                .Select(static a => a.Expression)
                .OfType<AnonymousFunctionExpressionSyntax>()
                .FirstOrDefault();

        if (lambda != null)
        {
            return this.EvalLambdaResult(
                lambda: lambda,
                boundParameters: argumentStates.Select(static state => state.Content.AfterTheClose)
                    .Prepend(receiverState.Content.AfterTheClose)
                    .ToImmutableArray());
        }

        // A function that is not a lambda gives nothing that the walk knows.
        return method.Parameters.Any(static parameter => parameter.Type.TypeKind == TypeKind.Delegate)
               || argumentStates.Count == 0
            ? LoopState.None
            : argumentStates[0].Content.AfterTheClose;
    }

    /// <summary>
    ///     The values that a collect fires: the first element of the tuple that
    ///     its function returns. The function gets the value of the stream and the
    ///     state, at a firing after the close.
    /// </summary>
    private LoopState CollectedContent(
        InvocationExpressionSyntax invocation,
        IMethodSymbol method,
        LoopState receiverState,
        LoopState initial)
    {
        if (!this.HoldsContent(method)
            || invocation.ArgumentList.Arguments
                .Select(static a => a.Expression)
                .OfType<AnonymousFunctionExpressionSyntax>()
                .FirstOrDefault() is not { } lambda)
        {
            return LoopState.None;
        }

        LoopState returned =
            this.EvalLambdaResult(
                lambda: lambda,
                boundParameters: ImmutableArray.Create(
                    item1: receiverState.Content.AfterTheClose,
                    item2: initial.AfterTheClose));

        return returned.Members?.PartsAt(ofATuple: true).ToList() is { Count: > 0 } parts
            ? parts.Where(static part => part.Position == 0).Select(static part => part.State).FirstOrDefault()
              ?? LoopState.None
            : returned;
    }

    // The walk of the function of an accumulation costs much in a view model, which accumulates
    // records. Thus, the walk always reads the function only for an accumulation of cells or
    // streams. For each other accumulation, FunctionCanGiveAState decides.
    private bool AccumulatesValuesOfTheGraph(IMethodSymbol method) =>
        method.ReturnType is INamedTypeSymbol { TypeArguments.Length: 1 } result
        && (this.region.Symbols.IsCellOrBehaviorType(result.TypeArguments[0])
            || this.region.Symbols.IsStreamType(result.TypeArguments[0]));

    /// <summary>
    ///     True when the function of an accumulation of other values, such as
    ///     records, can return a value with a state. This is a check of the syntax
    ///     only, as <see cref="CanUseState" /> is. The walk reads the function only
    ///     then, and an accumulation that cannot give a state stays cheap.
    /// </summary>
    /// <remarks>
    ///     A function gets a value with a state through its parameters, when the
    ///     stream has content. A static lambda has no other source. Each other
    ///     lambda can also name a local, a parameter, or a member that has a state.
    ///     It can also call a local function or a member of this object.
    /// </remarks>
    private bool FunctionCanGiveAState(InvocationExpressionSyntax invocation, LoopState given)
    {
        if (!given.IsNone || this.region.HasFields)
        {
            return true;
        }

        AnonymousFunctionExpressionSyntax? lambda =
            invocation.ArgumentList.Arguments
                .Select(static a => a.Expression)
                .OfType<AnonymousFunctionExpressionSyntax>()
                .FirstOrDefault();

        if (lambda == null || lambda.Modifiers.Any(SyntaxKind.StaticKeyword))
        {
            return false;
        }

        HashSet<string> names = this.NamesWithState();

        return lambda.Body.DescendantNodesAndSelf()
            .Any(node =>
                node switch
                {
                    ThisExpressionSyntax => this.thisMembers.Count > 0,
                    InvocationExpressionSyntax { Expression: SimpleNameSyntax called } =>
                        this.thisMembers.Count > 0
                        || NamesALocalFunction(from: lambda, name: called.Identifier.ValueText),
                    IdentifierNameSyntax name => names.Contains(name.Identifier.ValueText),
                    _ => false
                });

        static bool NamesALocalFunction(SyntaxNode from, string name) =>
            from.Ancestors()
                .OfType<BlockSyntax>()
                .SelectMany(static block => block.Statements.OfType<LocalFunctionStatementSyntax>())
                .Any(function => function.Identifier.ValueText == name);
    }
}
