using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace SodaFlow.Analyzers;

// The part of the evaluator for a call that gives a static location a new object. The call comes between the read of
// the location into a local and a put through that local.
internal sealed partial class LoopStateEvaluator
{
    /// <summary>
    ///     True when a call always runs between the read of a static location
    ///     and a put. The callee always gives the location a new object. Thus,
    ///     the local keeps the first object, and the put through the local does
    ///     not go into the location.
    /// </summary>
    /// <remarks>
    ///     The call is a statement of a block around the put, and no goto can go
    ///     over it. No loop in that block can run the put again after the code
    ///     that follows it. No code between the call and the put can give the
    ///     location a different value. Each other call can keep the first
    ///     object in the location, as the record of a put supposes.
    /// </remarks>
    /// <param name="location">The static field or the static property.</param>
    /// <param name="local">The local that got the object of the location.</param>
    /// <param name="read">The read of the location that gave the local its value.</param>
    /// <param name="use">The put through the local.</param>
    private bool ACallGivesANewObjectTo(ISymbol location, ISymbol local, ExpressionSyntax read, SyntaxNode use)
    {
        // A static member of a generic type has a location for each type argument.
        if (location is not (IFieldSymbol { IsStatic: true } or IPropertySymbol { IsStatic: true })
            || IsInAGenericType(location.ContainingType)
            || FunctionOf(use) is not { } code
            || code.DescendantNodes().Any(static node => node.IsKind(SyntaxKind.GotoStatement))
            || this.LocationKeyOf(read) is not { } key)
        {
            return false;
        }

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
                    && statement is ExpressionStatementSyntax { Expression: InvocationExpressionSyntax call }
                    && this.GivesANewObjectTo(call: call, location: location)
                    && !code.DescendantNodes(TextSpan.FromBounds(start: statement.Span.End, end: use.SpanStart))
                        .Any(node => node.SpanStart >= statement.Span.End
                                     && node.SpanStart < use.SpanStart
                                     && this.CanChangeTheLocation(
                                         node: node,
                                         key: key,
                                         name: local,
                                         aCallCounts: true))))
            {
                return true;
            }
        }

        return false;
    }

    // True when a callee always gives a location a new object. No override can replace the callee. Its code
    // runs at the call, thus it is not async and not an iterator. The last statement of its code gives the
    // location the new object, and no return can come before it. A goto cannot go over the last statement.
    private bool GivesANewObjectTo(SyntaxNode call, ISymbol location) =>
        this.region.SymbolOf(call)
            is IMethodSymbol { IsVirtual: false, IsOverride: false, IsAsync: false, ReturnsVoid: true } method
        && BodyOf(method.OriginalDefinition) switch
        {
            ExpressionSyntax body => this.IsANewObjectFor(expression: body, location: location),
            BlockSyntax { Statements: { Count: > 0 } statements }
                when statements.Last() is ExpressionStatementSyntax { Expression: var last } =>
                !statements.Take(statements.Count - 1).Any(static statement =>
                    statement.DescendantNodesAndSelf().Any(static node => node is ReturnStatementSyntax))
                && this.IsANewObjectFor(expression: last, location: location),
            _ => false
        };

    // True for an assignment of a new object to a location. A creation of the type of the location is a new
    // object. A collection expression for a list makes a new object. For an array, it makes a new object or
    // an empty array, which takes no put. A conversion of a different type can give an earlier object, and
    // so can the builder of a collection expression for a different type.
    private bool IsANewObjectFor(ExpressionSyntax expression, ISymbol location)
    {
        if (Unwrapped(expression)
                is not AssignmentExpressionSyntax { RawKind: (int)SyntaxKind.SimpleAssignmentExpression } assignment
            || !SymbolEqualityComparer.Default.Equals(
                x: this.region.SymbolOf(assignment.Left)?.OriginalDefinition,
                y: location.OriginalDefinition))
        {
            return false;
        }

        ITypeSymbol? type =
            location switch
            {
                IFieldSymbol field => field.Type,
                IPropertySymbol property => property.Type,
                _ => null
            };

        return Unwrapped(assignment.Right) switch
        {
            BaseObjectCreationExpressionSyntax creation =>
                SymbolEqualityComparer.Default.Equals(x: this.region.TypeOf(creation), y: type),
            CollectionExpressionSyntax =>
                type is IArrayTypeSymbol
                || (type is INamedTypeSymbol { Name: "List", Arity: 1 } list && SodaFlowSymbols.IsPlatform(list)),
            _ => false
        };
    }
}
