using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

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
            case { } field and (IFieldSymbol or IPropertySymbol):
                if (replaces && index >= 0 && this.function == null)
                {
                    this.region.ReplacesThePutsThroughLocals(field: field, index: index);
                }

                // A second field that got the same array gets the put also.
                if (this.region.HasLinkedFields)
                {
                    this.region.AddAPutThroughTheFieldsLinkedTo(
                        field: field,
                        index: index,
                        held: LoopState.None.HoldingWhatCameWith(state).Join(state.AsContainer.WithNoObjects));
                }

                break;
            case ILocalSymbol local
                when this.Frame.ReadFrom.Count > 0
                     && this.TheFieldThatItNames(local) is { } named
                     && this.region.SymbolOf(named) is { } field and (IFieldSymbol or IPropertySymbol)
                     && this.StillHasTheValueOf(local: local, location: named, use: element):
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
            && this.StillHasTheValueOf(local: local, location: named, use: call)
            && !this.ACallGivesANewObjectTo(location: field, local: local, read: named, use: call))
        {
            this.region.AddAPutThroughALocal(
                field: field,
                index: -1,
                held: LoopState.None.HoldingWhatCameWith(state).Join(state.AsContainer.WithNoObjects));
        }

        this.AddsThroughALinkedField(list: list, method: method, state: state);
    }

    // True when a put through a local can go into the value that a read through a location gets. The local is a name
    // of the location, or it keeps the value that it got there. A write to the location after the read changes only
    // the location. Thus, a different name of the first value can keep that value. The walk supposes no such name only
    // where the code gives the location a new object between the read and the put. Also, no code before the read
    // gives the location a value that a different name can have.
    private bool StillHasTheValueOf(ISymbol local, ExpressionSyntax location, SyntaxNode use)
    {
        if (this.IsStillTheLocationOf(name: local, location: location, use: use, aCallCounts: false))
        {
            return true;
        }

        if (FunctionOf(location) is not { } code || this.LocationKeyOf(location) is not { } key)
        {
            return false;
        }

        if (FunctionOf(use) != code)
        {
            return this.StillHasTheValueInAFunction(local: local, location: location, use: use, code: code, key: key);
        }

        if (location.Span.End > use.SpanStart
            || this.GetsAValue(
                name: local,
                code: code,
                after: location.Span.End,
                before: use.SpanStart,
                orAFunctionUsesIt: false))
        {
            return false;
        }

        return this.GetsASecondNameBefore(code: code, read: location, key: key)
               || !this.GetsANewObjectBetween(code: code, key: key, read: location, use: use);
    }

    // True when code before a read gives the location a value that is not a new object. A different name can have
    // that value.
    private bool GetsASecondNameBefore(SyntaxNode code, SyntaxNode read, (ISymbol Of, ISymbol? Owner, int Index) key) =>
        code.DescendantNodes(TextSpan.FromBounds(start: code.SpanStart, end: read.SpanStart))
            .OfType<AssignmentExpressionSyntax>()
            .Any(assignment => assignment.Span.End <= read.SpanStart
                               && this.IsAnAssignmentTo(assignment: assignment, key: key)
                               && !IsANewObject(assignment.Right));

    // True when a statement gives the location a new object between a read and a use, and always runs there. The
    // statement is in a block around the use, with no loop between the block and the use.
    private bool GetsANewObjectBetween(
        SyntaxNode code,
        (ISymbol Of, ISymbol? Owner, int Index) key,
        SyntaxNode read,
        SyntaxNode use)
    {
        foreach (BlockSyntax block in use.Ancestors().TakeWhile(around => around != code).OfType<BlockSyntax>())
        {
            if (use.Ancestors()
                .TakeWhile(around => around != block)
                .Any(static around => around is ForStatementSyntax
                                          or CommonForEachStatementSyntax
                                          or WhileStatementSyntax
                                          or DoStatementSyntax))
            {
                return false;
            }

            if (block.Statements.Any(statement =>
                    statement.SpanStart > read.Span.End
                    && statement.Span.End <= use.SpanStart
                    && statement is ExpressionStatementSyntax { Expression: AssignmentExpressionSyntax assignment }
                    && this.IsAnAssignmentTo(assignment: assignment, key: key)
                    && IsANewObject(assignment.Right)))
            {
                return true;
            }
        }

        return false;
    }

    // True for a simple assignment to the location of the key.
    private bool IsAnAssignmentTo(AssignmentExpressionSyntax assignment, (ISymbol Of, ISymbol? Owner, int Index) key) =>
        assignment.IsKind(SyntaxKind.SimpleAssignmentExpression)
        && IsTheSameLocation(one: this.LocationKeyOf(assignment.Left), other: key);

    // True for an expression that makes a new object, which no different name can have.
    private static bool IsANewObject(ExpressionSyntax value) =>
        Unwrapped(value) is BaseObjectCreationExpressionSyntax
            or CollectionExpressionSyntax
            or ArrayCreationExpressionSyntax
            or ImplicitArrayCreationExpressionSyntax;
}
