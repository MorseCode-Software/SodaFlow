using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace SodaFlow.Analyzers;

// The part of the evaluator for a collection in a static field that gets this in a constructor, as in into[0] = this.
// The collection has no object of the store. Thus, a put into it keeps no object of the store.
internal sealed partial class LoopStateEvaluator
{
    /// <summary>
    ///     Gives each caller a record of a put of objects of the store into an
    ///     element of a static collection. The index is a constant.
    ///     This code names the collection through a parameter or the field.
    /// </summary>
    /// <remarks>
    ///     The put and each call up to the caller always run. The element keeps
    ///     the objects to the end of the code: see TheNameToTheEnd.
    ///     A read of the element in the caller then gets the objects. For each
    ///     other put of this, see CanPutThisIntoAStaticCollection.
    /// </remarks>
    /// <param name="element">The element that the assignment gives a value.</param>
    /// <param name="state">The state of the value.</param>
    /// <param name="replaces">True when the assignment always runs in its function.</param>
    public void GivesTheCallersAPutIntoAStaticElement(
        ElementAccessExpressionSyntax element,
        LoopState state,
        bool replaces)
    {
        if (this.quiet
            || this.call == null
            || state.Objects.IsEmpty
            || state.Objects.Any(made => this.region.Objects.StateOf(made) == null)
            || element.ArgumentList.Arguments.Count != 1
            || Unwrapped(element.Expression) is not (IdentifierNameSyntax or MemberAccessExpressionSyntax)
            || this.region.SymbolOf(Unwrapped(element.Expression))
                is not ((IParameterSymbol { RefKind: RefKind.None } or IFieldSymbol { IsStatic: true }) and var of))
        {
            return;
        }

        if (!replaces
            || KeyOf(this.region.ModelFor(element).GetConstantValue(element.ArgumentList.Arguments[0].Expression))
                is not { } key)
        {
            this.CanPutThisIntoAStaticCollection(collection: of, state: state);

            return;
        }

        (ISymbol Of, ISymbol? Owner, object? Index)? named = (of, null, key);
        SyntaxNode use = element;

        for (LoopStateEvaluator code = this; code.call is { } call; code = call.Caller)
        {
            // Later code of this run can put a different object there. Then no caller gets a record.
            if (named is not { } inThisCode || code.TheNameToTheEnd(name: inThisCode, after: use) is not { } kept)
            {
                return;
            }

            named = call.AlwaysRuns ? call.Caller.TheNameAtTheCall(name: kept, site: call.Site) : null;

            // Where a caller has no name of the element, a read there can get the new object only from the collection.
            if (named is not { } atTheCall || FunctionOf(call.Site) is not { } caller)
            {
                this.CanPutThisIntoAStaticCollection(collection: of, state: state);

                return;
            }

            use = call.Site;

            if (atTheCall is { Of: IFieldSymbol { IsStatic: true, Type: var type }, Owner: null }
                && IsAnArrayOrAList(type))
            {
                List<(NameOfAnObject Name, ImmutableArray<MadeObject> Objects)> puts =
                    call.Caller.Frame.PutsIntoStaticElements;

                puts.RemoveAll(put => put.Name.Is(of: atTheCall.Of, owner: null, index: atTheCall.Index));

                puts.Add(
                    (new NameOfAnObject(
                            of: atTheCall.Of,
                            owner: null,
                            index: atTheCall.Index,
                            code: caller,
                            from: use.Span.End),
                        state.Objects));
            }
        }
    }

    // An array, or a List<T>, whose indexer puts the value at the index and runs no code of the project.
    private static bool IsAnArrayOrAList(ITypeSymbol type) =>
        type is IArrayTypeSymbol { Rank: 1 }
        || type.OriginalDefinition.ToDisplayString() == "System.Collections.Generic.List<T>";

    // A constructor can put this into an element of a collection in a static field, also one that it gets. Where the
    // put does not always run, or the index is no constant, each element can be the new object.
    private void CanPutThisIntoAStaticCollection(ISymbol collection, LoopState state)
    {
        if (this.thisInTheStore is { } built
            && state.Objects.Contains(built)
            && collection switch
            {
                IFieldSymbol named => named,
                IParameterSymbol parameter => this.TheStaticFieldGivenTo(parameter),
                _ => null
            } is { } field)
        {
            this.region.AddAPutThroughALocal(field: field, index: -1, held: LoopState.None.HoldingWhatCameWith(state));
        }
    }

    /// <summary>
    ///     Records a put of this into a list in a static field, in the code of a
    ///     constructor. A read through the field then holds the new object.
    /// </summary>
    public void AddsThisToAStaticList(ExpressionSyntax list, IMethodSymbol method, LoopState state)
    {
        if (this.thisInTheStore is not { } built
            || this.quiet
            || !state.Objects.Contains(built)
            || !SinkWalker.PutsIntoAList(method)
            || Unwrapped(list) is not (IdentifierNameSyntax or MemberAccessExpressionSyntax)
            || this.region.SymbolOf(Unwrapped(list)) switch
                {
                    IFieldSymbol { IsStatic: true } named => named,
                    IParameterSymbol { RefKind: RefKind.None } parameter => this.TheStaticFieldGivenTo(parameter),
                    _ => null
                } is not { } field)
        {
            return;
        }

        this.region.AddAPutThroughALocal(field: field, index: -1, held: LoopState.None.HoldingWhatCameWith(state));
    }

    // The static field that the calls up to this code give a parameter, where no code gives the parameter a
    // different value. Null where an argument is no such field or parameter.
    private ISymbol? TheStaticFieldGivenTo(ISymbol parameter)
    {
        ISymbol named = parameter;

        for (LoopStateEvaluator code = this; code.call is { } call; code = call.Caller)
        {
            if (named is not IParameterSymbol given
                || (code.region.SymbolOf(call.Site) as IMethodSymbol)?.OriginalDefinition.DeclaringSyntaxReferences
                    .FirstOrDefault()?.GetSyntax() is not { } callee
                || code.UsesOf(name: given, code: callee).Any(static one => one.Gives)
                || call.Caller.ArgumentOf(parameter: given, site: call.Site) is not { } argument
                || call.Caller.region.SymbolOf(Unwrapped(argument)) is not { } next)
            {
                return null;
            }

            if (next is IFieldSymbol { IsStatic: true })
            {
                return next;
            }

            named = next;
        }

        return null;
    }

    // The objects that a callee put into an element of a collection in a static field, while the element holds them.
    // Null for each other element.
    private LoopState? PutIntoAStaticElement(ElementAccessExpressionSyntax element)
    {
        if (this.Frame.PutsIntoStaticElements.Count == 0
            || element.ArgumentList.Arguments.Count != 1
            || this.region.SymbolOf(element.Expression) is not IFieldSymbol { IsStatic: true } field
            || KeyOf(this.region.ModelFor(element).GetConstantValue(element.ArgumentList.Arguments[0].Expression))
                is not { } key)
        {
            return null;
        }

        foreach ((NameOfAnObject name, ImmutableArray<MadeObject> objects) in this.Frame.PutsIntoStaticElements)
        {
            if (name.Is(of: field, owner: null, index: key) && this.HoldsItsObject(name: name, use: element))
            {
                return LoopState.None.WithObjects(objects);
            }
        }

        return null;
    }
}
