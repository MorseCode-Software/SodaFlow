using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace SodaFlow.Analyzers;

// The part of the evaluator for the type that the value of an expression has at run time.
internal sealed partial class LoopStateEvaluator
{
    /// <summary>
    ///     True when the value of the expression can never have the type. The
    ///     compiler then has no conversion from the type of the value to that
    ///     type. Thus, a pattern of the type is always false for the value.
    /// </summary>
    /// <remarks>
    ///     The type <c>object</c> tells nothing. Then, the walk reads the type
    ///     before a cast to <c>object</c>. It also reads the value of a local
    ///     that no code writes. For a parameter that no code writes, it reads the
    ///     argument of the call of this code.
    /// </remarks>
    /// <param name="value">The expression of the value.</param>
    /// <param name="type">The type that the code tests.</param>
    public bool NeverHas(ExpressionSyntax value, ITypeSymbol type) =>
        this.TypeOfTheValue(value) is { } known
        && !Construction.SaysNothing(known)
        && !this.region.CanBe(from: known, to: type);

    private ITypeSymbol? TypeOfTheValue(ExpressionSyntax value)
    {
        LoopStateEvaluator reader = this;

        while (true)
        {
            ITypeSymbol? type = reader.TypeBeforeObject(value);

            if (!Construction.SaysNothing(type))
            {
                return type;
            }

            ExpressionSyntax unwrapped = Unwrapped(value);

            if (reader.ValueOfALocalThatNoCodeWrites(unwrapped) is var initial && initial != unwrapped)
            {
                value = initial;

                continue;
            }

            if (unwrapped is not IdentifierNameSyntax name
                || reader.region.SymbolOf(name) is not IParameterSymbol parameter
                || IsWrittenIn(parameter: parameter, region: reader.region)
                || reader.call is not { } call
                || (call.Caller.ArgumentOf(parameter: parameter, site: call.Site)
                    ?? call.Caller.ArgumentOfADelegateCall(parameter: parameter, site: call.Site)) is not { } argument)
            {
                return type;
            }

            reader = call.Caller;
            value = argument;
        }
    }

    // The argument that a call of a delegate gives a parameter of the function that the call runs. Each function of the
    // delegate gets the arguments of the call in their order. A method of a delegate can be an extension method whose
    // first argument the delegate keeps. Thus, the function must be a lambda or a local function. A named argument
    // can be at a different position. Null for each other call.
    private ExpressionSyntax? ArgumentOfADelegateCall(IParameterSymbol parameter, SyntaxNode site) =>
        parameter.ContainingSymbol is IMethodSymbol
        {
            MethodKind: MethodKind.AnonymousFunction or MethodKind.LocalFunction
        }
        && site is InvocationExpressionSyntax { ArgumentList.Arguments: var arguments } invocation
        && this.region.SymbolOf(invocation) is IMethodSymbol { MethodKind: MethodKind.DelegateInvoke }
        && parameter.Ordinal < arguments.Count
        && arguments.All(static argument => argument.NameColon == null)
            ? arguments[parameter.Ordinal].Expression
            : null;
}
