using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace SodaFlow.Analyzers;

// The part of the evaluator for a forward reference. The code can read a value back through the
// reference. That value gets its links and its initial value from a member of the object that the
// block returns. The result of an operator reads back the members of the inputs that give it its links.
// See LoopState.ReadBack.
internal sealed partial class LoopStateEvaluator
{
    /// <summary>
    ///     The result of an operator in the block of a forward reference, with the
    ///     members that it reads back. The inputs that the rule of the operator
    ///     uses give them.
    /// </summary>
    private LoopState WithReadBack(
        OperatorRule rule,
        LoopState state,
        InvocationExpressionSyntax invocation,
        IMethodSymbol method,
        LoopState receiverState,
        ITypeSymbol? receiverType,
        LoopState firstArgument,
        LoopState inputs)
    {
        if (state.Linked || state.Pending)
        {
            state = state.ReadingBack(
                rule switch
                {
                    // A snapshot, a gate, and a hold have links only to their stream.
                    OperatorRule.SampleLazy or OperatorRule.MapStream or OperatorRule.MapValue or OperatorRule.Hold
                        or OperatorRule.Fold or OperatorRule.MapTo or OperatorRule.StreamFromReceiver
                        or OperatorRule.Snapshot =>
                        receiverState.ReadBack,
                    OperatorRule.SwitchValue or OperatorRule.SwitchStream => receiverState.ReadBack.AddRange(
                        receiverState.Content.ReadBack),
                    OperatorRule.HoldLazy => receiverState.ReadBack.AddRange(firstArgument.ReadBack),
                    OperatorRule.DeferLazy => firstArgument.ReadBack,
                    _ => inputs.ReadBack
                });
        }

        // A map of a collection projects its items at the end of the transaction. It samples the
        // collection then, and that sample forces each member that the collection reads back.
        if (rule == OperatorRule.MapValue
            && !this.quiet
            && receiverState is { Pending: true, ReadBack.IsEmpty: false }
            && !this.region.Symbols.IsCellOrBehaviorType(receiverType))
        {
            this.region.Forces(receiverState.ReadBack);
        }

        // A function that computes the value reads a member at the first read of the value, which is
        // after the close. Each member that it reads back then gives the value a pending initial value.
        if (inputs.Content.ReadBack.IsEmpty
            || (rule is not (OperatorRule.MapLazy or OperatorRule.LiftLazy)
                && !OperatorTable.ComputesValueWithFunction(
                    symbols: this.region.Symbols,
                    method: method,
                    receiverType: receiverType)))
        {
            return state;
        }

        ImmutableArray<ISymbol> read =
            this.Quiet().FunctionReadBack(invocation: invocation, method: method, given: inputs.Content);

        return read.IsEmpty ? state : state.WithPending(true).ReadingBack(read);
    }

    /// <summary>
    ///     The members that the function of an operator reads back with a read
    ///     of a pending value. A quiet evaluator follows the function.
    /// </summary>
    private ImmutableArray<ISymbol> FunctionReadBack(
        InvocationExpressionSyntax invocation,
        IMethodSymbol method,
        LoopState given) =>
        invocation.ArgumentList.Arguments
            .Select((argument, position) =>
                (Written: argument.Expression,
                    Parameter: ParameterFor(method: method, argument: argument, position: position)))
            // A method by its name and a delegate in a local are functions also, as in FunctionReadsPending.
            .Where(argument =>
                (argument.Written is not AnonymousFunctionExpressionSyntax lambda || this.CanRead(lambda.Body))
                && OperatorTable.ComputesValue(method: method, parameter: argument.Parameter))
            .SelectMany(argument =>
            {
                LoopState functions = this.Eval(argument.Written);

                return Callables(state: functions, parameter: argument.Parameter)
                    .Select(function =>
                        this.FollowFunction(
                            call: invocation,
                            name: NameOf(argument.Written),
                            function: function,
                            target: functions,
                            arguments: default,
                            each: (argument.Written, given, ManyTimes: true)))
                    .SelectMany(static followed =>
                        followed?.Findings.Where(static finding => !finding.Escape) ?? []);
            })
            .SelectMany(static finding => finding.ReadBack)
            .Distinct(SymbolEqualityComparer.Default)
            .ToImmutableArray();
}
