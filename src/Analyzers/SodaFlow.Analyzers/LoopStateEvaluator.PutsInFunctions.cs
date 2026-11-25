using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace SodaFlow.Analyzers;

// The part of the evaluator for a put through a local of a field in a lambda or a local function. The code around the
// function gave the local the value of the field.
internal sealed partial class LoopStateEvaluator
{
    /// <summary>
    ///     The same as <see cref="StillHasTheValueOf" />, where the read and
    ///     the put are in two different functions. One of them is a lambda or a
    ///     local function.
    /// </summary>
    /// <remarks>
    ///     The put runs at a call of the function, which can come after code
    ///     that is after the function. Thus, only code that runs before each run
    ///     of the put can stop the record. That code is in the function before
    ///     the put. For a lambda, it is also code between the read and the
    ///     lambda, because a lambda runs only after the code makes it. A local
    ///     function can run before its position in the code, thus the code
    ///     around it does not count. Where a function that is not around the
    ///     put made the read, the record stays.
    /// </remarks>
    /// <param name="local">The local that the put goes through.</param>
    /// <param name="location">The read of the field that gave the local its value.</param>
    /// <param name="use">The put.</param>
    /// <param name="code">The function of the read.</param>
    /// <param name="key">The location of the read.</param>
    private bool StillHasTheValueInAFunction(
        ISymbol local,
        SyntaxNode location,
        SyntaxNode use,
        SyntaxNode code,
        (ISymbol Of, ISymbol? Owner, int Index) key)
    {
        if (FunctionOf(use) is not { } inner)
        {
            return false;
        }

        // A function that is not around the put gave the local its value. The walk read that function at a call
        // before the put, and the positions of the two then tell nothing. Thus, the record stays.
        if (!use.Ancestors().Contains(code))
        {
            return true;
        }

        // The functions from the put to the code of the read. The last one is in the code of the read.
        SyntaxNode[] functions = [.. use.Ancestors().TakeWhile(around => around != code).Where(IsAFunction)];
        SyntaxNode outer = functions[functions.Length - 1];

        SyntaxNode? localFunction =
            functions.LastOrDefault(static function => function is LocalFunctionStatementSyntax);

        if (this.GetsAValue(
                name: local,
                code: inner,
                after: inner.SpanStart,
                before: use.SpanStart,
                orAFunctionUsesIt: false)
            || (localFunction == null
                && this.GetsAValue(
                    name: local,
                    code: code,
                    after: location.Span.End,
                    before: outer.SpanStart,
                    orAFunctionUsesIt: false,
                    ran: outer.DescendantNodesAndSelf().Where(IsAFunction).ToImmutableArray(),
                    aFunctionCanRun: true)))
        {
            return false;
        }

        return this.GetsASecondNameBefore(code: code, read: location, key: key)
               || !this.GetsANewObjectBetween(code: localFunction ?? code, key: key, read: location, use: use);
    }
}
