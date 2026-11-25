using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace SodaFlow.Analyzers;

// The part of the evaluator for a put through a local that got the array or the list of a field.
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
            case { } field and (IFieldSymbol or IPropertySymbol) when replaces && index >= 0 && this.function == null:
                this.region.ReplacesThePutsThroughLocals(field: field, index: index);

                break;
            case ILocalSymbol local
                when this.Frame.ReadFrom.Count > 0
                     && this.TheFieldThatItNames(local) is { } named
                     && this.region.SymbolOf(named) is { } field and (IFieldSymbol or IPropertySymbol)
                     && this.IsStillTheLocationOf(name: local, location: named, use: element, aCallCounts: false):
                this.region.AddAPutThroughALocal(
                    field: field,
                    index: index,
                    held: LoopState.None.HoldingWhatCameWith(state).Join(state.AsContainer.WithNoObjects));

                break;
        }
    }

    /// <summary>
    ///     Records a put into a list through a local that got the list of a
    ///     field, as <see cref="PutsThroughALocalOfAField" /> does for an
    ///     element. The put is at an unknown index, thus no put through the field
    ///     replaces it.
    /// </summary>
    /// <param name="list">The receiver of the call.</param>
    /// <param name="call">The call of Add, Insert, or AddRange of the list.</param>
    /// <param name="method">The callee.</param>
    /// <param name="state">The state of what the call puts into the list.</param>
    public void AddsThroughALocalOfAField(
        ExpressionSyntax list,
        InvocationExpressionSyntax call,
        IMethodSymbol method,
        LoopState state)
    {
        // Each call on an owner comes here. Thus, the cheap tests come first.
        if (!this.quiet
            && this.Frame.ReadFrom.Count > 0
            && Unwrapped(list) is IdentifierNameSyntax name
            && this.region.SymbolOf(name) is ILocalSymbol local
            && this.TheFieldThatItNames(local) is { } named
            && SinkWalker.PutsIntoAList(method)
            && this.region.SymbolOf(named) is { } field and (IFieldSymbol or IPropertySymbol)
            && this.IsStillTheLocationOf(name: local, location: named, use: call, aCallCounts: false)
            && !this.ACallGivesANewObjectTo(location: field, local: local, read: named, use: call))
        {
            this.region.AddAPutThroughALocal(
                field: field,
                index: -1,
                held: LoopState.None.HoldingWhatCameWith(state).Join(state.AsContainer.WithNoObjects));
        }
    }
}
