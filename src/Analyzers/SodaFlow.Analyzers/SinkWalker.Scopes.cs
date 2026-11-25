using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;

namespace SodaFlow.Analyzers;

// The part of the walker that finds the code after a loop or a switch statement that runs only
// after an await. A loop can run no turn, and a switch can run no section. A break or a continue
// can also skip the await. Each of them runs the code after the loop or the switch now. The same
// rules find the code after an if or a switch that an iterator does not run after its first item.
internal sealed partial class SinkWalker
{
    // Each if and each switch statement that stops the iterator on each path.
    private HashSet<StatementSyntax>? stopsOnEachPath;

    // The iterator stops in an if when each branch stops it.
    private void StopsInEachBranchOf(IfStatementSyntax node)
    {
        if (this.takesOneItem
            && node.Else is { } otherwise
            && this.StopsIn(node.Statement)
            && this.StopsIn(otherwise.Statement))
        {
            this.StopsOnEachPathAt(node);
        }
    }

    // The iterator stops in a switch when each section stops it, and one section matches each value.
    private void StopsInEachSectionOf(SwitchStatementSyntax node)
    {
        if (this.takesOneItem
            && node.Sections.All(this.StopsIn)
            && node.Sections.SelectMany(static section => section.Labels).Any(this.MatchesEachValue))
        {
            this.StopsOnEachPathAt(node);
        }
    }

    private void StopsOnEachPathAt(StatementSyntax node)
    {
        (this.stopsOnEachPath ??= []).Add(node);
        this.StopsAfter(node);
    }

    // True when the code always stops the iterator in a branch, or in a section of a switch. The
    // branch is a yield statement or code that runs to a yield statement. It can also be an if or a
    // switch that stops on each path. A goto, a break, or a continue before that yield statement can
    // skip it.
    private bool StopsIn(SyntaxNode branch)
    {
        if (branch is YieldStatementSyntax
            || (branch is StatementSyntax statement && this.stopsOnEachPath?.Contains(statement) == true))
        {
            return true;
        }

        // The walk reads no code after the first stop in the branch. Thus, the branch has one stop.
        foreach ((SyntaxNode block, int from) in this.afterTheItem)
        {
            if (block == branch)
            {
                return !branch.DescendantNodes(static node =>
                        node is not (AnonymousFunctionExpressionSyntax or LocalFunctionStatementSyntax))
                    .Any(node =>
                        node.SpanStart < from
                        && node is GotoStatementSyntax or BreakStatementSyntax or ContinueStatementSyntax);
            }
        }

        return false;
    }

    /// <summary>
    ///     True when the code after a loop runs only after a position in its
    ///     body. The loop always runs its first turn. No break or continue of
    ///     the loop comes before the position.
    /// </summary>
    /// <param name="loop">The loop statement.</param>
    /// <param name="body">The body of the loop.</param>
    /// <param name="at">The position in the body.</param>
    private bool EndsOnlyAfter(SyntaxNode loop, SyntaxNode body, int at) =>
        this.HasAFirstTurn(loop) && !JumpsOutBefore(construct: loop, code: body, at: at);

    /// <summary>
    ///     Makes the code after a switch statement run later when each path to
    ///     it passes an await. The switch always selects a section, and each
    ///     break of the switch comes after an await that stops its section.
    /// </summary>
    /// <param name="node">The switch statement, after the walk read it.</param>
    private void AwaitsInEachSection(SwitchStatementSyntax node)
    {
        // Synchronous code has no await, and the test is one test of an empty list.
        if (this.later.Count == 0 || this.later.All(part => part.Block.Parent != node))
        {
            return;
        }

        foreach (SwitchSectionSyntax section in node.Sections)
        {
            int awaited = int.MaxValue;

            foreach ((SyntaxNode block, int from) in this.later)
            {
                awaited = block == section ? Math.Min(val1: awaited, val2: from) : awaited;
            }

            if (JumpsOutBefore(construct: node, code: section, at: awaited))
            {
                return;
            }
        }

        if (node.Sections.SelectMany(static section => section.Labels).Any(this.MatchesEachValue)
            && this.PartAround(node) is { } around)
        {
            this.later.Add((around, node.Span.End));
        }
    }

    // A default label, and a pattern with no when clause that is true for each value, also null.
    // The compiler knows more switches that select a section for each value, but it does not tell
    // which.
    private bool MatchesEachValue(SwitchLabelSyntax label) =>
        label is DefaultSwitchLabelSyntax
        || (label is CasePatternSwitchLabelSyntax { WhenClause: null } pattern
            && this.region.ModelFor(pattern).GetOperation(pattern)
                is IPatternCaseClauseOperation
                {
                    Pattern: IDiscardPatternOperation or IDeclarationPatternOperation { MatchesNull: true }
                });

    // True when a break or a continue of a loop or a switch comes before a position in its code. A
    // break is of the loop or the switch nearest to it, and a continue is of the loop nearest to it.
    // A lambda or a local function cannot jump out of its code. Code with errors can have a jump
    // with no loop or switch around it.
    private static bool JumpsOutBefore(SyntaxNode construct, SyntaxNode code, int at) =>
        code.DescendantNodes(node =>
                node.SpanStart < at && node is not (AnonymousFunctionExpressionSyntax or LocalFunctionStatementSyntax))
            .Any(node =>
                node.SpanStart < at
                && node is BreakStatementSyntax or ContinueStatementSyntax
                && node.Ancestors()
                    .FirstOrDefault(around =>
                        (around is SwitchStatementSyntax && node is BreakStatementSyntax)
                        || LoopStateEvaluator.LoopBodyOf(around) != null)
                == construct);
}
