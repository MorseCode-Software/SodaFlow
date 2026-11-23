using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace SodaFlow.Analyzers;

// The part of the evaluator for the names through which the walk reads a cell of a build that makes two or more
// cells.
internal sealed partial class LoopStateEvaluator
{
    /// <summary>
    ///     The name through which the code reads a value. Null for each read
    ///     through a different expression.
    /// </summary>
    /// <param name="read">The expression of the value at the read.</param>
    public ReadKey? ReadKeyOf(ExpressionSyntax? read)
    {
        ExpressionSyntax? name = read == null ? null : Unwrapped(read);

        if (name is IdentifierNameSyntax or MemberAccessExpressionSyntax { Expression: ThisExpressionSyntax })
        {
            return this.region.SymbolOf(name) is { } symbol
                   && symbol is ILocalSymbol or IParameterSymbol or IFieldSymbol { IsStatic: false }
                ? new ReadKey(name: symbol, index: null)
                : null;
        }

        return name is ElementAccessExpressionSyntax { ArgumentList.Arguments: { Count: 1 } arguments } element
               && Unwrapped(element.Expression) is IdentifierNameSyntax collection
               && this.region.SymbolOf(collection) is { } named
               && named is ILocalSymbol or IParameterSymbol
               && this.region.ModelFor(element).GetConstantValue(arguments[0].Expression)
                   is { HasValue: true, Value: { } index }
            ? new ReadKey(name: named, index: index)
            : null;
    }

    // True when a build can run two or more times for one run of the code that the walk reads. That is a build in a
    // loop, in a lambda, or in a local function. It is also a build in a callee that the walk reads one time for two
    // or more calls.
    private bool CanBuildTwoOrMoreCells(SyntaxNode build)
    {
        if (this.Frame.Repeats)
        {
            return true;
        }

        for (SyntaxNode child = build; child.Parent is { } above && child != this.region.Root; child = above)
        {
            if (above is AnonymousFunctionExpressionSyntax or LocalFunctionStatementSyntax
                || LoopBodyOf(above) == child)
            {
                return true;
            }
        }

        return false;
    }
}
