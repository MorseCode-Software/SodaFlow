using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace SodaFlow.Analyzers;

// The part of the evaluator that tells the walker where code runs on each path.
internal sealed partial class LoopStateEvaluator
{
    /// <summary>
    ///     True for a collection whose positions the store knows, with one
    ///     position or more. A loop over it always has a first turn.
    /// </summary>
    /// <param name="collection">The expression of the collection.</param>
    public bool HasAnElement(ExpressionSyntax collection) =>
        this.StorePositionsOf(collection) is (_, { Length: > 0 });

    // True when each run of the code runs the node one time. The node has only blocks and its own
    // statement around it, up to the code. A finally block runs what is in it also, and so does the
    // branch of an if that a constant selects. The block of a try with no catch clause runs the
    // node where no code runs after an exception before it. Thus, the finally block of that try
    // must not name a local of the given names.
    private bool RunsStraightIn(SyntaxNode node, SyntaxNode code, IReadOnlyCollection<string> names)
    {
        SyntaxNode child = node;

        foreach (SyntaxNode around in node.Ancestors().TakeWhile(around => around != code))
        {
            bool straight =
                around switch
                {
                    BlockSyntax
                        or ExpressionStatementSyntax
                        or LocalDeclarationStatementSyntax
                        or VariableDeclarationSyntax
                        or VariableDeclaratorSyntax
                        or EqualsValueClauseSyntax
                        or FinallyClauseSyntax
                        or ElseClauseSyntax => true,
                    TryStatementSyntax attempt =>
                        child is FinallyClauseSyntax
                        || (attempt.Catches.Count == 0
                            && attempt.Finally?.Block.DescendantTokens()
                                .Any(token =>
                                    token.IsKind(SyntaxKind.IdentifierToken) && names.Contains(token.ValueText))
                            != true),
                    IfStatementSyntax branch =>
                        SinkWalker.ConstantOf(region: this.region, condition: branch.Condition)
                        == (child == branch.Statement),
                    _ => false
                };

            if (!straight)
            {
                return false;
            }

            child = around;
        }

        return true;
    }

    // The name of the local whose delegate or event a remove changes.
    private static IEnumerable<string> NameOfTheOwner(ExpressionSyntax location) =>
        Unwrapped(location) switch
        {
            IdentifierNameSyntax name => [name.Identifier.ValueText],
            MemberAccessExpressionSyntax { Expression: IdentifierNameSyntax owner } => [owner.Identifier.ValueText],
            _ => []
        };
}
