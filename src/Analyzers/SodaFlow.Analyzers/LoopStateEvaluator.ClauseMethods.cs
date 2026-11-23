using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace SodaFlow.Analyzers;

// The part of the evaluator for the methods of this compilation that a query expression calls.
internal sealed partial class LoopStateEvaluator
{
    /// <summary>
    ///     True for a method of this compilation with a body that the walk can
    ///     read.
    /// </summary>
    public static bool HasCodeToRead(IMethodSymbol method) =>
        BodyOf((method.ReducedFrom ?? method).OriginalDefinition) != null;

    /// <summary>
    ///     The methods of this compilation that a query expression calls, when
    ///     each clause method returns no sequence. Each method gets the result of
    ///     the method of the clause before it as its source.
    /// </summary>
    /// <remarks>
    ///     Such a method can run its function or keep it. The walk does not read
    ///     the expression of a clause, thus a function parameter gets no function.
    /// </remarks>
    public Followed? FollowClauseMethods(QueryExpressionSyntax query)
    {
        SemanticModel model = this.region.ModelFor(query);
        ExpressionSyntax source = query.FromClause.Expression;
        LoopState state = this.Eval(source);
        Followed? all = null;

        foreach ((SyntaxNode clause, ExpressionSyntax written, IMethodSymbol method) in ClauseMethodsOf(
                     query: query,
                     model: model))
        {
            Followed? one =
                this.region.Symbols.IsSodaFlow(method)
                    ? null
                    : method.ReducedFrom is { } extension
                        ? this.FollowImplied(
                            call: clause,
                            member: extension,
                            owner: null,
                            arguments:
                            [
                                (source, state),
                                .. FunctionsOf(clause: written, method: method)
                            ])
                        : this.FollowImplied(
                            call: clause,
                            member: method,
                            owner: state,
                            arguments: FunctionsOf(clause: written, method: method));

            all = Both(first: all, second: one);
            state = one?.State ?? LoopState.None;
            source = written;
        }

        return all;
    }

    // The arguments of the function parameters of a clause method. Each one gets the expression of
    // the clause and no state.
    private static IEnumerable<(ExpressionSyntax Written, LoopState State)> FunctionsOf(
        ExpressionSyntax clause,
        IMethodSymbol method) =>
        Enumerable.Repeat(element: (clause, LoopState.None), count: method.Parameters.Length);

    // Each clause of a query that calls a method, in order, with the expression of the clause.
    private static IEnumerable<(SyntaxNode Clause, ExpressionSyntax Written, IMethodSymbol Method)> ClauseMethodsOf(
        QueryExpressionSyntax query,
        SemanticModel model)
    {
        for (QueryBodySyntax? body = query.Body; body != null; body = body.Continuation?.Body)
        {
            foreach (QueryClauseSyntax clause in body.Clauses)
            {
                if (clause is OrderByClauseSyntax order)
                {
                    foreach (OrderingSyntax ordering in order.Orderings)
                    {
                        if (model.GetSymbolInfo(ordering).Symbol is IMethodSymbol sorts)
                        {
                            yield return (ordering, ordering.Expression, sorts);
                        }
                    }

                    continue;
                }

                if (model.GetQueryClauseInfo(clause).OperationInfo.Symbol is IMethodSymbol method
                    && ExpressionOf(clause) is { } written)
                {
                    yield return (clause, written, method);
                }
            }

            if (model.GetSymbolInfo(body.SelectOrGroup).Symbol is IMethodSymbol last)
            {
                yield return (
                    body.SelectOrGroup,
                    body.SelectOrGroup switch
                    {
                        SelectClauseSyntax select => select.Expression,
                        GroupClauseSyntax group => group.GroupExpression,
                        _ => query.FromClause.Expression
                    },
                    last);
            }
        }
    }

    private static ExpressionSyntax? ExpressionOf(QueryClauseSyntax clause) =>
        clause switch
        {
            WhereClauseSyntax where => where.Condition,
            LetClauseSyntax let => let.Expression,
            FromClauseSyntax from => from.Expression,
            JoinClauseSyntax join => join.InExpression,
            _ => null
        };
}
