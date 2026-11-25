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
        ITypeSymbol? type = this.TypeBeforeObject(value);

        if (!Construction.SaysNothing(type))
        {
            return type;
        }

        ExpressionSyntax unwrapped = Unwrapped(value);

        if (this.ValueOfALocalThatNoCodeWrites(unwrapped) is var initial && initial != unwrapped)
        {
            return this.TypeOfTheValue(initial);
        }

        return unwrapped is IdentifierNameSyntax name
               && this.region.SymbolOf(name) is IParameterSymbol parameter
               && !IsWrittenIn(parameter: parameter, region: this.region)
               && this.call is { } call
               && call.Caller.ArgumentOf(parameter: parameter, site: call.Site) is { } argument
            ? call.Caller.TypeOfTheValue(argument)
            : type;
    }
}
