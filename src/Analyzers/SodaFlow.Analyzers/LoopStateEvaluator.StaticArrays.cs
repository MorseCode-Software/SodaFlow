using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace SodaFlow.Analyzers;

// The part of the evaluator for an element of an array in a static field at a constant index.
internal sealed partial class LoopStateEvaluator
{
    /// <summary>
    ///     Records a put into an element of the array of a static field at a
    ///     constant index, as in <c>Cells[1] = value</c>. A read of a different
    ///     element through the field does not get the put. False for each other
    ///     put, which the caller gives to all of the collection.
    /// </summary>
    /// <param name="element">The element that gets the value.</param>
    /// <param name="collection">The collection of the element.</param>
    /// <param name="state">The state of the value.</param>
    public bool PutsAtAConstantIndexOfAStaticArray(
        ElementAccessExpressionSyntax? element,
        ExpressionSyntax collection,
        LoopState state)
    {
        if (element is not { ArgumentList.Arguments: { Count: 1 } arguments }
            || Unwrapped(collection) is not ((IdentifierNameSyntax or MemberAccessExpressionSyntax) and var named)
            || this.region.SymbolOf(named)
                is not IFieldSymbol { IsStatic: true, Type: IArrayTypeSymbol { Rank: 1 } } field
            || this.ConstantIndexOf(arguments[0]) is not { } index)
        {
            return false;
        }

        if (!this.quiet)
        {
            this.region.AddAPutAtAnIndex(
                field: field,
                index: index,
                held: this.InAnOldLocation(state.AsContainer.WithNoObjects));
        }

        return true;
    }

    // Records a change to the object in an element of a static array whose objects no code changes, for that element
    // only: see StaticOwnerOf. Each other element has a different object. A local with ref of such an element names it at
    // each use, because UnchangedStatics permits no other use of that local. A local that gets only the element holds
    // its object. False for each other expression.
    private bool PutsIntoAnElementOfItsOwnObject(ExpressionSyntax target, LoopState state)
    {
        ExpressionSyntax element =
            target is IdentifierNameSyntax && this.region.SymbolOf(target) is ILocalSymbol local
                ? this.ElementThatALocalNames(local) ?? target
                : target;

        if (element is not ElementAccessExpressionSyntax { ArgumentList.Arguments: { Count: 1 } arguments } access
            || this.StaticOwnerOf(element) == null
            || this.TheArrayOfAStaticField(Unwrapped(access.Expression)) is not { } field
            || this.ConstantIndexOf(arguments[0]) is not { } index)
        {
            return false;
        }

        if (!this.quiet)
        {
            this.region.AddAPutAtAnIndex(field: field, index: index, held: this.InAnOldLocation(state));
        }

        return true;
    }

    // The element of a static array that a local names at each use. A local with ref names the location of its
    // declaration. A local with no ref that gets only an element of a static array holds the object of that element.
    // Null for each other local.
    private ExpressionSyntax? ElementThatALocalNames(ILocalSymbol local) =>
        local.RefKind != RefKind.None
            ? local.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax()
                is VariableDeclaratorSyntax { Initializer.Value: RefExpressionSyntax { Expression: var location } }
                ? Unwrapped(location)
                : null
            : this.Frame.ElementOf.TryGetValue(key: local, value: out ExpressionSyntax? collection)
              && this.TheArrayOfAStaticField(Unwrapped(collection)) != null
              && this.OnlyValueOf(local) is { } value
                ? Unwrapped(value)
                : null;

    // The puts at a constant index into the array of a static field that a read of the field gets. A read that is the
    // collection of an element at a constant index gets only the puts at that index.
    private LoopState PutsAtTheIndexOfARead(ISymbol field, SyntaxNode read) =>
        this.region.HasPutsAtIndexes && field is IFieldSymbol { IsStatic: true, Type: IArrayTypeSymbol }
            ? this.region.PutsAtAnIndexOf(
                field: field,
                index: read.Parent is ElementAccessExpressionSyntax
                       {
                           ArgumentList.Arguments: { Count: 1 } arguments
                       } element
                       && element.Expression == read
                    ? this.ConstantIndexOf(arguments[0])
                    : null)
            : LoopState.None;
}
