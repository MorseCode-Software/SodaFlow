using System.Collections.Generic;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace SodaFlow.Analyzers;

// The part of the walker for a using declaration, which disposes its resource at the end of its
// block.
internal sealed partial class SinkWalker
{
    // The resource of each using declaration that the walk read, with its block, in the order of the
    // code. The block disposes them at its end.
    private readonly List<(SyntaxNode Block, ExpressionSyntax Resource)> disposals = [];

    // Records the resource of a using declaration for the end of its block. C# permits a using
    // declaration also at the top level of a file, which no loop block calls.
    private void DisposesAtTheEnd(VariableDeclarationSyntax declaration, ExpressionSyntax resource)
    {
        if (declaration.Parent?.Parent is BlockSyntax block)
        {
            this.disposals.Add((block, resource));
        }
    }

    // A block calls Dispose for each of its using declarations at its end, from the last to the
    // first.
    private void DisposesEachAtTheEndOf(BlockSyntax block)
    {
        for (int i = this.disposals.Count - 1; i >= 0 && this.disposals[i].Block == block; i--)
        {
            ExpressionSyntax resource = this.disposals[i].Resource;
            this.disposals.RemoveAt(i);
            this.CheckDisposed(resource);
        }
    }
}
