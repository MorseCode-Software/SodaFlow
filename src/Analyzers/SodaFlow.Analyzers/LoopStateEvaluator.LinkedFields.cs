using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace SodaFlow.Analyzers;

// The part of the evaluator for two fields that an assignment gave one list or one array, as B = A does.
internal sealed partial class LoopStateEvaluator
{
    /// <summary>
    ///     Records that an assignment gives a field the list or the array of a
    ///     second field. A put through one of the two then goes into the record
    ///     of the other: see <see cref="LoopRegion.LinksTwoFields" />.
    /// </summary>
    /// <remarks>
    ///     A link stays until a sure assignment in the code of the block gives a
    ///     static field of the link a different value. A static field has one
    ///     location. A field of an object can be in each object of its type, and
    ///     a callee can run in a branch. Thus, only that assignment removes a
    ///     link. A local that got the list of the field before keeps the list of
    ///     each field of the link, thus the link stays then too. A link that
    ///     stays gives a report and not a miss.
    /// </remarks>
    /// <param name="target">The target of the assignment.</param>
    /// <param name="value">The expression of the value.</param>
    /// <param name="replaces">True when the target then has only this value.</param>
    public void NotesTwoFieldsOfOneList(SyntaxNode target, ExpressionSyntax value, bool replaces)
    {
        if (this.quiet
            || target is not (IdentifierNameSyntax or MemberAccessExpressionSyntax)
            || this.region.SymbolOf(target) is not ({ } field and (IFieldSymbol or IPropertySymbol)))
        {
            return;
        }

        if (replaces
            && this.call == null
            && this.region.HasLinkedFields
            && this.LocationKeyOf((ExpressionSyntax)target) is { Of.IsStatic: true }
            && !this.ALocalNames(field))
        {
            this.region.TakesAwayTheLinksOfAField(field);
        }

        if (this.region.TypeOf(value) is not { } type || !this.CanBeAnObject(type))
        {
            return;
        }

        foreach (ISymbol other in this.TheFieldsOfAList(value))
        {
            this.region.LinksTwoFields(one: field, other: other);
        }
    }

    // Records a put through a local of a field. The local can keep the list that the field had before, which a second
    // field of a link can also have. Thus, where the local is not sure to name the list of the field, only the second
    // fields get the put.
    private void PutsThroughALocalOf(ISymbol field, int index, LoopState held, bool namesTheList)
    {
        if (namesTheList)
        {
            this.region.AddAPutThroughALocal(field: field, index: index, held: held);
        }
        else
        {
            this.region.AddAPutThroughTheFieldsLinkedTo(field: field, index: index, held: held);
        }
    }

    // Records a put into a list through a field for each second field that got the same list. The put is a call of
    // Add, Insert, or AddRange.
    private void AddsThroughALinkedField(ExpressionSyntax list, IMethodSymbol method, LoopState state)
    {
        if (!this.quiet
            && this.region.HasLinkedFields
            && this.region.SymbolOf(Unwrapped(list)) is { } field and (IFieldSymbol or IPropertySymbol)
            && SinkWalker.PutsIntoAList(method))
        {
            this.region.AddAPutThroughTheFieldsLinkedTo(
                field: field,
                index: -1,
                held: LoopState.None.HoldingWhatCameWith(state).Join(state.AsContainer.WithNoObjects));
        }
    }

    // True when a local of this code is a name of a field, as NotesANameOfALocation records it.
    private bool ALocalNames(ISymbol field) =>
        this.Frame.ReadFrom.Values.Any(read =>
            SymbolEqualityComparer.Default.Equals(
                x: this.region.SymbolOf(Unwrapped(read))?.OriginalDefinition,
                y: field.OriginalDefinition));

    // The fields whose list or array a value can be. Each branch of a conditional value can be the value.
    private IEnumerable<ISymbol> TheFieldsOfAList(ExpressionSyntax value) =>
        Unwrapped(value) switch
        {
            ConditionalExpressionSyntax either =>
                this.TheFieldsOfAList(either.WhenTrue).Concat(this.TheFieldsOfAList(either.WhenFalse)),
            BinaryExpressionSyntax { RawKind: (int)SyntaxKind.CoalesceExpression } either =>
                this.TheFieldsOfAList(either.Left).Concat(this.TheFieldsOfAList(either.Right)),
            var one => this.TheFieldOfAList(one) is { } field ? [field] : []
        };

    // The field whose list or array a value is. The value reads the field, or a method returns it, or a local keeps
    // the value of the field. Null for each other value.
    private ISymbol? TheFieldOfAList(ExpressionSyntax value)
    {
        ExpressionSyntax read = Unwrapped(value);

        if (read is IdentifierNameSyntax name && this.region.SymbolOf(name) is ILocalSymbol local)
        {
            return this.TheFieldThatItNames(local) is { } named
                   && this.StillHasTheValueOf(local: local, location: named, use: name)
                ? this.region.SymbolOf(named)
                : null;
        }

        return read switch
        {
            IdentifierNameSyntax or MemberAccessExpressionSyntax
                when this.region.SymbolOf(read) is { } field and (IFieldSymbol or IPropertySymbol) => field,
            InvocationExpressionSyntax call
                when this.LocationReturnedBy(call)
                    is { Index: -1, Of: { } field and (IFieldSymbol or IPropertySymbol) } => field,
            _ => null
        };
    }
}
