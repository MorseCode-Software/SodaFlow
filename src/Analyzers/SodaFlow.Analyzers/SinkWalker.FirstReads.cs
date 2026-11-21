using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace SodaFlow.Analyzers;

// The part of the walker that records the first read of a cell. That record has its own rule for
// where a read runs. The record of writes does not count a try block. A call in the block can throw
// before the write, and a catch clause then runs. With no catch clause, the code after the try runs
// only when the block runs to its end.
internal sealed partial class SinkWalker
{
    // Each if on the constant true that the walk read.
    private readonly HashSet<IfStatementSyntax> alwaysTaken = [];

    // The marks of the first reads in a try block, which the walk applies at the end of that try.
    private List<(TryStatementSyntax After, Action Marks)>? afterTheTry;

    // The marks of the first reads in each turn of a loop, with the body of that loop.
    private List<(SyntaxNode Turn, ComputedValue Cell)>? turnMarks;

    private bool AlwaysRunsAt(SyntaxNode place, out SyntaxNode? turn) =>
        this.AlwaysRunsAt(place: place, forAFirstRead: false, turn: out turn, after: out _);

    /// <summary>
    ///     What records the run of each cell that a first read runs. Null when
    ///     the walk cannot know that the read runs.
    /// </summary>
    /// <remarks>
    ///     A read in a try block with no catch clause runs before the code
    ///     after that try. A read in each turn of a loop runs before the next
    ///     turn. It also runs before the code after a loop that always has a
    ///     first turn.
    /// </remarks>
    private Action<ComputedValue>? MarksOfAFirstRead(SyntaxNode read)
    {
        if (WhereItAlwaysRuns(read) is not { } place)
        {
            return null;
        }

        bool always =
            this.AlwaysRunsAt(
                place: place,
                forAFirstRead: true,
                turn: out SyntaxNode? turn,
                after: out TryStatementSyntax? after);

        // A callee that runs a lambda for each element can run it no time. The walk does not record
        // the end of such a turn.
        Action<ComputedValue>? marks =
            always
                ? static cell => cell.Runs()
                : turn is { Parent: { } loop } && LoopStateEvaluator.LoopBodyOf(loop) == turn
                    ? cell => this.RunsInATurn(cell: cell, turn: turn)
                    : null;

        return marks == null || after == null
            ? marks
            : cell => (this.afterTheTry ??= []).Add((after, () => marks(cell)));
    }

    private void RunsInATurn(ComputedValue cell, SyntaxNode turn)
    {
        cell.RunsInATurn();
        (this.turnMarks ??= []).Add((turn, cell));
    }

    // Removes the marks of the turns of a loop at its end. The loop can have no turn.
    private void EndsTheTurnsOf(SyntaxNode body)
    {
        if (this.turnMarks is not { Count: > 0 } marks)
        {
            return;
        }

        foreach ((SyntaxNode _, ComputedValue cell) in marks.Where(mark => mark.Turn == body))
        {
            cell.LeavesATurn();
        }

        marks.RemoveAll(mark => mark.Turn == body);
    }

    public override void VisitTryStatement(TryStatementSyntax node)
    {
        base.VisitTryStatement(node);

        if (this.afterTheTry is not { Count: > 0 } waiting)
        {
            return;
        }

        foreach ((TryStatementSyntax _, Action marks) in waiting.Where(mark => mark.After == node).ToList())
        {
            marks();
        }

        waiting.RemoveAll(mark => mark.After == node);
    }

    /// <summary>
    ///     True when a loop always runs its first turn. A do loop, a while loop
    ///     on the constant true, and a for loop with no condition have one. A
    ///     for loop from a constant to a larger constant has one also, and so
    ///     has a foreach over an array literal with an element.
    /// </summary>
    private bool HasAFirstTurn(SyntaxNode loop) =>
        loop switch
        {
            DoStatementSyntax => true,
            WhileStatementSyntax repeat => this.ConstantOf(repeat.Condition) == true,
            ForStatementSyntax counted => counted.Condition == null || this.CountsUpFromBelow(counted),
            // The compiler needs an element to give an implicit array its type.
            ForEachStatementSyntax each =>
                LoopStateEvaluator.Unwrapped(each.Expression)
                    is ArrayCreationExpressionSyntax { Initializer.Expressions.Count: > 0 }
                    or ImplicitArrayCreationExpressionSyntax,
            _ => false
        };

    // True for a for loop whose condition is a variable of the loop less than a constant, and the
    // variable starts at a smaller constant.
    private bool CountsUpFromBelow(ForStatementSyntax loop) =>
        loop is
        {
            Declaration.Variables: var variables,
            Condition: BinaryExpressionSyntax
            {
                RawKind: (int)SyntaxKind.LessThanExpression,
                Left: IdentifierNameSyntax name,
                Right: var end
            }
        }
        && variables.FirstOrDefault(variable => variable.Identifier.ValueText == name.Identifier.ValueText)
            is { Initializer.Value: var start }
        && this.region.ModelFor(loop).GetConstantValue(start) is { Value: int from }
        && this.region.ModelFor(loop).GetConstantValue(end) is { Value: int to }
        && from < to;
}
