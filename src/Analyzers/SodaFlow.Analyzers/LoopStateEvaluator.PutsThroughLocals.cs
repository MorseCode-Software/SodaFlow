using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace SodaFlow.Analyzers;

// The part of the evaluator for a put into an element through a local that got the array of a field.
internal sealed partial class LoopStateEvaluator
{
    /// <summary>
    ///     Records a put into an element through a local that got the array of a
    ///     field. A read through the field then gets the put also. A sure put
    ///     through the field at the same constant index replaces such puts.
    /// </summary>
    /// <remarks>
    ///     A read through the local gets the state of the field: see
    ///     WithItsField. This record is the other direction.
    /// </remarks>
    /// <param name="element">The element that gets the value.</param>
    /// <param name="state">The state of the value.</param>
    /// <param name="replaces">True when the put always runs in its function.</param>
    public void PutsThroughALocalOfAField(ElementAccessExpressionSyntax element, LoopState state, bool replaces)
    {
        if (this.quiet || element.ArgumentList.Arguments.Count != 1)
        {
            return;
        }

        int index = this.ConstantIndexOf(element.ArgumentList.Arguments[0]) ?? -1;

        switch (this.region.SymbolOf(Unwrapped(element.Expression)))
        {
            case IFieldSymbol field when replaces && index >= 0 && this.function == null:
                this.region.ReplacesThePutsThroughLocals(field: field, index: index);

                break;
            case ILocalSymbol local
                when this.Frame.ReadFrom.Count > 0
                     && this.TheFieldThatItNames(local) is { } named
                     && this.region.SymbolOf(named) is IFieldSymbol field
                     && this.IsStillTheLocationOf(name: local, location: named, use: element, aCallCounts: false):
                this.region.AddAPutThroughALocal(
                    field: field,
                    index: index,
                    held: LoopState.None.HoldingWhatCameWith(state).Join(state.AsContainer.WithNoObjects));

                break;
        }
    }
}
