using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace SodaFlow.Analyzers;

// The part of the walker for a read whose exception a finally block replaces. Such a read throws, but its exception
// never leaves the try statement.
internal sealed partial class SinkWalker
{
    // True for a read in the block or in a catch clause of a try whose finally block always throws an object of its
    // own. The runtime then gives that object, and the exception of the read goes nowhere. A catch clause with a
    // variable can keep the exception of the read, and code can throw it again. Thus, such a clause between the read
    // and the finally block gives false. A read in a lambda or a local function can run after the try statement.
    private static bool IsReplacedByAFinally(SyntaxNode read)
    {
        SyntaxNode child = read;

        foreach (SyntaxNode around in read.Ancestors())
        {
            switch (around)
            {
                case AnonymousFunctionExpressionSyntax or LocalFunctionStatementSyntax:
                    return false;
                case TryStatementSyntax attempt when child == attempt.Block
                                                     && attempt.Catches.Any(static clause =>
                                                         clause.Declaration is { Identifier.ValueText.Length: > 0 }):
                    return false;
                case TryStatementSyntax attempt when child != attempt.Finally
                                                     && ThrowsAnObjectOfItsOwn(attempt.Finally):
                    return true;
            }

            child = around;
        }

        return false;
    }
}
