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
    ///     Such a method can run its function or keep it. Thus, each function
    ///     parameter gets the expression of its clause as a function. The walk
    ///     reads that expression where the code of the method calls it.
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
                                .. this.ArgumentsOf(clause: clause, written: written, method: method)
                            ])
                        : this.FollowImplied(
                            call: clause,
                            member: method,
                            owner: state,
                            arguments: this.ArgumentsOf(clause: clause, written: written, method: method));

            all = Both(first: all, second: one);
            state = one?.State ?? LoopState.None;
            source = written;
        }

        return all;
    }

    // The arguments of the parameters of a clause method. The compiler makes a lambda of each expression of the clause,
    // and the delegate parameters get them in order. A select clause that the compiler puts into the method of the
    // clause before it gives the last one. A delegate parameter with no expression gets no function, because the
    // lambda that the compiler makes for it reads only range variables. The other parameter of a join gets the
    // sequence of the join.
    private IEnumerable<(ExpressionSyntax Written, LoopState State)> ArgumentsOf(
        SyntaxNode clause,
        ExpressionSyntax written,
        IMethodSymbol method)
    {
        ExpressionSyntax?[] bodies =
            clause switch
            {
                FromClauseSyntax from => [from.Expression, SelectPutInto(from)],
                JoinClauseSyntax join => [join.LeftExpression, join.RightExpression, SelectPutInto(join)],
                GroupClauseSyntax group => [group.ByExpression, group.GroupExpression],
                _ => [written]
            };

        int next = 0;

        foreach (IParameterSymbol parameter in method.Parameters)
        {
            ExpressionSyntax? body =
                parameter.Type.TypeKind == TypeKind.Delegate && next < bodies.Length ? bodies[next++] : null;

            yield return body == null
                ? (written, parameter.Type.TypeKind == TypeKind.Delegate ? LoopState.None : this.Eval(written))
                : (body,
                    LoopState.OfFunction(
                        new FunctionValue(declaration: body, definer: this, receiver: null, ofThis: null, owner: null)));
        }
    }

    // The expression of the select clause that the compiler puts into the method of a from or a join clause. That is so
    // where the select clause comes next. Otherwise, the method gets a lambda that makes an object of the variables.
    private static ExpressionSyntax? SelectPutInto(QueryClauseSyntax clause) =>
        clause.Parent is QueryBodySyntax { SelectOrGroup: SelectClauseSyntax select } body
        && body.Clauses.Last() == clause
            ? select.Expression
            : null;

    /// <summary>
    ///     The range variable that the lambda of a clause expression gets as its
    ///     one parameter. The inner key of a join gets the variable of the join.
    ///     Null where the lambda gets two or more variables, which is so after a
    ///     clause that declares a variable.
    /// </summary>
    private static ISymbol? TheRangeVariableOf(ExpressionSyntax clause, SemanticModel model)
    {
        if (clause.Parent is JoinClauseSyntax { RightExpression: var right } join && right == clause)
        {
            return model.GetDeclaredSymbol(join);
        }

        SyntaxNode? of = clause.Parent is OrderingSyntax ? clause.Parent.Parent : clause.Parent;

        if (of?.Parent is not QueryBodySyntax body
            || body.Clauses
                .TakeWhile(each => each != of)
                .Any(static each => each is not (WhereClauseSyntax or OrderByClauseSyntax)))
        {
            return null;
        }

        return body.Parent switch
        {
            QueryExpressionSyntax query => model.GetDeclaredSymbol(query.FromClause),
            QueryContinuationSyntax continuation => model.GetDeclaredSymbol(continuation),
            _ => null
        };
    }

    /// <summary>
    ///     True for the expression of a query clause that the compiler makes
    ///     into a lambda. The range variables are the parameters of that lambda.
    /// </summary>
    private static bool IsTheBodyOfAClause(SyntaxNode body) =>
        body.Parent is QueryClauseSyntax or SelectOrGroupClauseSyntax or OrderingSyntax;

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
