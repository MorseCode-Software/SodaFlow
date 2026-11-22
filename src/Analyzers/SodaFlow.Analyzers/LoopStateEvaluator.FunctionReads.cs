using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace SodaFlow.Analyzers;

// The part of the evaluator for a function that an operator gets, which reads a pending value at the first read of
// the result.
internal sealed partial class LoopStateEvaluator
{
    /// <summary>
    ///     The reads of a pending value that a function given to an operator
    ///     makes. Empty when it makes none. The evaluator reads the function as
    ///     a call of it, with each parameter bound to the given state. This
    ///     evaluator must be a quiet one, because the function does not run now.
    /// </summary>
    private IReadOnlyList<Finding> FunctionReadsPending(
        InvocationExpressionSyntax invocation,
        IMethodSymbol method,
        LoopState given)
    {
        return invocation.ArgumentList.Arguments
                   .Select((argument, position) =>
                       (Written: argument.Expression,
                           Parameter: ParameterFor(method: method, argument: argument, position: position)))
                   // A lambda with no call and no property that reads cannot read a value. A function that
                   // the operator keeps for a later time does not compute the value.
                   .Where(argument =>
                       (argument.Written is not AnonymousFunctionExpressionSyntax lambda || this.CanRead(lambda.Body))
                       && OperatorTable.ComputesValue(method: method, parameter: argument.Parameter))
                   .Select(Reads)
                   .FirstOrDefault(static found => found.Count > 0)
               ?? [];

        IReadOnlyList<Finding> Reads((ExpressionSyntax Written, IParameterSymbol? Parameter) argument)
        {
            LoopState functions = this.Eval(argument.Written);

            // A cell of functions, as Apply and a filter by a cell of predicates get, gives the
            // operator the function that it holds.
            if (functions.Functions.IsEmpty && this.region.Symbols.IsCellOrBehaviorType(argument.Parameter?.Type))
            {
                functions = functions.Content;
            }

            return Callables(state: functions, parameter: argument.Parameter)
                       .Select(function =>
                           this.FollowFunction(
                                   call: invocation,
                                   name: NameOf(argument.Written),
                                   function: function,
                                   target: functions,
                                   arguments: default,
                                   each: (argument.Written, given, ManyTimes: true))
                               ?.Findings
                               .Where(static finding => !finding.Escape)
                               .ToArray()
                           ?? [])
                       .FirstOrDefault(static found => found.Length > 0)
                   ?? [];
        }
    }

    // The parameter or the member of this code through which a read of a function got its pending value.
    // A value that the function makes pending came into this code through it also. Thus, a caller
    // reports a read of that value at its call, as it reports a read of the function at the first read.
    private ISymbol? OriginOfARead(IEnumerable<Finding> reads) =>
        reads.FirstOrDefault(read => ReferenceEquals(objA: read.Home, objB: this.Frame) && read.Origin != null)
            ?.Origin;
}
